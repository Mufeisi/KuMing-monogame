using System.Collections.Immutable;

namespace LyoCrystal.MicroGateway;

public sealed record MicroResourceIndexSnapshot(long Version, int FileCount, long TotalBytes, DateTime BuiltUtc, string? LastError);

public sealed class MicroResourceIndex : IAsyncDisposable
{
    private readonly TimeSpan _stabilityDelay;
    private readonly TimeSpan _reconcileInterval;
    private readonly TimeSpan _minimumStableAge;
    private readonly TimeSpan _minimumObservationAge;
    private readonly SemaphoreSlim _reconcileLock = new(1, 1);
    private ImmutableDictionary<string, IndexedFile> _files = EmptyFiles();
    private CancellationTokenSource? _lifetime;
    private Task? _background;
    private FileSystemWatcher? _watcher;
    private int _dirty;
    private string? _root;
    private long _version;
    private string? _lastError;
    private readonly Dictionary<string, FileObservation> _observations = new(StringComparer.OrdinalIgnoreCase);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _invalidated = new(StringComparer.OrdinalIgnoreCase);
    private int _resetObservations;
    private long _nextObservationDueUtcTicks;

    private sealed record IndexedFile(string FullPath, long Length, DateTime LastWriteUtc);
    private sealed record FileObservation(long Length, DateTime LastWriteUtc, DateTime FirstSeenUtc);

    public MicroResourceIndex(TimeSpan? stabilityDelay = null, TimeSpan? reconcileInterval = null, TimeSpan? minimumStableAge = null, TimeSpan? minimumObservationAge = null)
    {
        _stabilityDelay = stabilityDelay ?? TimeSpan.FromSeconds(2);
        _reconcileInterval = reconcileInterval ?? TimeSpan.FromMinutes(5);
        _minimumStableAge = minimumStableAge ?? TimeSpan.FromMinutes(1);
        _minimumObservationAge = minimumObservationAge ?? TimeSpan.FromSeconds(10);
    }

    public async Task StartAsync(string resourceRoot, CancellationToken cancellationToken = default)
    {
        string root = NormalizeRoot(resourceRoot);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException($"微端资源目录不存在：{root}");
        await StopAsync().ConfigureAwait(false);
        _root = root;
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        await ReconcileAsync(_lifetime.Token).ConfigureAwait(false);
        if (_minimumObservationAge > TimeSpan.Zero && _observations.Count > 0)
        {
            await Task.Delay(_minimumObservationAge, _lifetime.Token).ConfigureAwait(false);
            await ReconcileAsync(_lifetime.Token).ConfigureAwait(false);
        }
        _watcher = new FileSystemWatcher(root)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size | NotifyFilters.LastWrite,
            EnableRaisingEvents = false,
        };
        _watcher.Changed += MarkDirty;
        _watcher.Created += MarkDirty;
        _watcher.Deleted += MarkDirty;
        _watcher.Renamed += MarkDirty;
        _watcher.Error += MarkDirty;
        _watcher.EnableRaisingEvents = true;
        _background = RunReconcileLoopAsync(_lifetime.Token);
    }

    public async Task StopAsync()
    {
        CancellationTokenSource? lifetime = Interlocked.Exchange(ref _lifetime, null);
        Task? background = Interlocked.Exchange(ref _background, null);
        if (lifetime is null) return;
        lifetime.Cancel();
        FileSystemWatcher? watcher = Interlocked.Exchange(ref _watcher, null);
        if (watcher is not null)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }
        if (background is not null)
        {
            try { await background.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
        await _reconcileLock.WaitAsync().ConfigureAwait(false);
        _reconcileLock.Release();
        lifetime.Dispose();
    }

    public async Task<bool> ReconcileAsync(CancellationToken cancellationToken = default)
    {
        string root = _root ?? throw new InvalidOperationException("资源索引尚未启动。");
        await _reconcileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Interlocked.Exchange(ref _dirty, 0);
            Dictionary<string, IndexedFile> first = Scan(root, _minimumStableAge);
            await Task.Delay(_stabilityDelay, cancellationToken).ConfigureAwait(false);
            Dictionary<string, IndexedFile> second = Scan(root, _minimumStableAge);
            if (Volatile.Read(ref _dirty) != 0) return false;
            var builder = ImmutableDictionary.CreateBuilder<string, IndexedFile>(StringComparer.OrdinalIgnoreCase);
            DateTime observedUtc = DateTime.UtcNow;
            if (Interlocked.Exchange(ref _resetObservations, 0) != 0) _observations.Clear();
            foreach (string invalidated in _invalidated.Keys.ToArray())
                if (_invalidated.TryRemove(invalidated, out _)) _observations.Remove(invalidated);
            foreach ((string relative, IndexedFile candidate) in second)
            {
                if (first.TryGetValue(relative, out IndexedFile? observed) &&
                    observed.Length == candidate.Length && observed.LastWriteUtc == candidate.LastWriteUtc)
                {
                    if (!_observations.TryGetValue(relative, out FileObservation? prior) || prior.Length != candidate.Length || prior.LastWriteUtc != candidate.LastWriteUtc)
                    {
                        _observations[relative] = new FileObservation(candidate.Length, candidate.LastWriteUtc, observedUtc);
                        prior = _observations[relative];
                    }
                    if (observedUtc - prior.FirstSeenUtc >= _minimumObservationAge)
                        builder[relative] = candidate;
                }
            }
            foreach (string missing in _observations.Keys.Except(second.Keys, StringComparer.OrdinalIgnoreCase).ToArray()) _observations.Remove(missing);
            long[] pendingDue = _observations.Values.Select(item => (item.FirstSeenUtc + _minimumObservationAge).Ticks).Where(ticks => ticks > observedUtc.Ticks).ToArray();
            long nextDue = pendingDue.Length == 0 ? 0 : pendingDue.Min();
            Interlocked.Exchange(ref _nextObservationDueUtcTicks, nextDue);
            ImmutableDictionary<string, IndexedFile> next = builder.ToImmutable();
            Interlocked.Exchange(ref _files, next);
            Interlocked.Increment(ref _version);
            Volatile.Write(ref _lastError, null);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            Volatile.Write(ref _lastError, error.Message);
            return false;
        }
        finally { _reconcileLock.Release(); }
    }

    public bool TryGetFile(string fullPath, out string indexedPath)
    {
        indexedPath = string.Empty;
        string? root = _root;
        if (root is null) return false;
        try
        {
            string normalized = Path.GetFullPath(fullPath);
            string relative = Path.GetRelativePath(root, normalized);
            if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative)) return false;
            if (!Volatile.Read(ref _files).TryGetValue(NormalizeKey(relative), out IndexedFile? file)) return false;
            var current = new FileInfo(file.FullPath);
            if (!current.Exists || current.Length != file.Length || current.LastWriteTimeUtc != file.LastWriteUtc) return false;
            indexedPath = file.FullPath;
            return true;
        }
        catch { return false; }
    }

    public MicroResourceIndexSnapshot GetSnapshot()
    {
        ImmutableDictionary<string, IndexedFile> files = Volatile.Read(ref _files);
        return new MicroResourceIndexSnapshot(
            Interlocked.Read(ref _version), files.Count, files.Values.Sum(file => file.Length),
            files.Count == 0 ? DateTime.MinValue : DateTime.UtcNow, Volatile.Read(ref _lastError));
    }

    private async Task RunReconcileLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        DateTime nextFullScan = DateTime.UtcNow + _reconcileInterval;
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            bool changed = Interlocked.Exchange(ref _dirty, 0) != 0;
            long maturityDue = Interlocked.Read(ref _nextObservationDueUtcTicks);
            if (!changed && DateTime.UtcNow < nextFullScan && (maturityDue == 0 || DateTime.UtcNow.Ticks < maturityDue)) continue;
            await ReconcileAsync(cancellationToken).ConfigureAwait(false);
            nextFullScan = DateTime.UtcNow + _reconcileInterval;
        }
    }

    private void MarkDirty(object? sender, FileSystemEventArgs args)
    {
        string? root = _root;
        if (root is not null)
        {
            try
            {
                string relative = NormalizeKey(Path.GetRelativePath(root, args.FullPath));
                if (!relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relative)) _invalidated[relative] = 0;
            }
            catch { }
        }
        Interlocked.Exchange(ref _dirty, 1);
    }
    private void MarkDirty(object? sender, ErrorEventArgs args) { Interlocked.Exchange(ref _resetObservations, 1); Interlocked.Exchange(ref _dirty, 1); }

    private static Dictionary<string, IndexedFile> Scan(string root, TimeSpan minimumStableAge)
    {
        var result = new Dictionary<string, IndexedFile>(StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            string directory = pending.Pop();
            if (IsReparse(directory)) continue;
            foreach (string child in Directory.EnumerateDirectories(directory))
                if (!IsReparse(child)) pending.Push(child);
            foreach (string path in Directory.EnumerateFiles(directory))
            {
                if (IsReparse(path)) continue;
                if (path.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) || path.Contains(".tmp-", StringComparison.OrdinalIgnoreCase) ||
                    path.EndsWith(".part", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".uploading", StringComparison.OrdinalIgnoreCase)) continue;
                try { using var lease = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read); }
                catch (IOException) { continue; }
                var info = new FileInfo(path);
                if (DateTime.UtcNow - info.LastWriteTimeUtc < minimumStableAge) continue;
                result[NormalizeKey(Path.GetRelativePath(root, path))] = new IndexedFile(path, info.Length, info.LastWriteTimeUtc);
            }
        }
        return result;
    }

    private static bool IsReparse(string path) => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
    private static string NormalizeRoot(string path) => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    private static string NormalizeKey(string path) => path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
    private static ImmutableDictionary<string, IndexedFile> EmptyFiles() => ImmutableDictionary.Create<string, IndexedFile>(StringComparer.OrdinalIgnoreCase);

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _reconcileLock.Dispose();
    }
}
