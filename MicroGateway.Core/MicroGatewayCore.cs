using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;

namespace LyoCrystal.MicroGateway;

public sealed class MicroGatewayCore
{
    private const int CopyBufferBytes = 256 * 1024;
    private const int SoundChunkBytes = 1024 * 1024;
    private readonly ConcurrentDictionary<string, SoundListCache> _soundLists = new(StringComparer.OrdinalIgnoreCase);
    private MicroGatewayOptions? _options;
    private long _requestCount;
    private long _activeRequests;
    private string? _lastError;
    private readonly object _lifecycleLock = new();
    private TaskCompletionSource _idle = CompletedIdle();
    private bool _stopping;
    private readonly SemaphoreSlim _startLock = new(1, 1);

    private sealed record SoundListCache(DateTime LastWriteTimeUtc, long Length, Dictionary<int, string> Entries);
    private sealed record SoundPayload(byte[] Bytes, int Max, int Current);

    public async Task StartAsync(MicroGatewayOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        string root = Path.GetFullPath(options.ResourceRoot);
        string? launcherRoot = string.IsNullOrWhiteSpace(options.LauncherRoot) ? null : Path.GetFullPath(options.LauncherRoot);
        await _startLock.WaitAsync().ConfigureAwait(false);
        MicroResourceIndex? created = null;
        MicroResourceIndex? retired = null;
        try
        {
            lock (_lifecycleLock)
            {
                if (_stopping) throw new InvalidOperationException("微端网关正在停止，不能重新启动。");
                if (_options?.ResourceIndex is not null && string.Equals(_options.ResourceRoot, root, StringComparison.OrdinalIgnoreCase))
                {
                    _options = options with
                    {
                        ResourceRoot = root,
                        LauncherRoot = launcherRoot,
                        ResourceIndex = _options.ResourceIndex,
                        CacheRoot = _options.CacheRoot,
                        PayloadCache = _options.PayloadCache,
                    };
                    _lastError = null;
                    return;
                }
            }
            created = new MicroResourceIndex(minimumObservationAge: TimeSpan.FromSeconds(Math.Clamp(options.NewFileQuarantineSeconds, 0, 3600)));
            await created.StartAsync(root).ConfigureAwait(false);
            string cacheRoot = string.IsNullOrWhiteSpace(options.CacheRoot)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LyoCrystal", "MicroGateway", "Cache")
                : Path.GetFullPath(options.CacheRoot);
            var payloadCache = new MicroPayloadCache(root, cacheRoot, options.MemoryCacheMb, options.DiskCacheMb);
            lock (_lifecycleLock)
            {
                if (_stopping) throw new InvalidOperationException("微端网关正在停止，不能重新启动。");
                retired = _options?.ResourceIndex;
                _options = options with { ResourceRoot = root, LauncherRoot = launcherRoot, ResourceIndex = created, CacheRoot = cacheRoot, PayloadCache = payloadCache };
                _lastError = null;
                created = null;
            }
        }
        finally
        {
            _startLock.Release();
            if (created is not null) await created.DisposeAsync().ConfigureAwait(false);
            if (retired is not null) await retired.DisposeAsync().ConfigureAwait(false);
        }
    }

    public async Task StopAsync()
    {
        await _startLock.WaitAsync().ConfigureAwait(false);
        Task idle;
        MicroResourceIndex? index;
        try
        {
            lock (_lifecycleLock)
            {
                if (_options is null && !_stopping) return;
                index = _options?.ResourceIndex;
                if (!_stopping)
                {
                    _stopping = true;
                    _options = null;
                }
                idle = _idle.Task;
            }
            await idle.ConfigureAwait(false);
            if (index is not null) await index.DisposeAsync().ConfigureAwait(false);
            lock (_lifecycleLock) _stopping = false;
        }
        finally { _startLock.Release(); }
    }

    public MicroGatewaySnapshot GetSnapshot()
    {
        lock (_lifecycleLock)
        {
            MicroResourceIndexSnapshot? index = _options?.ResourceIndex?.GetSnapshot();
            MicroPayloadCacheSnapshot? cache = _options?.PayloadCache?.GetSnapshot();
            return new MicroGatewaySnapshot(
                _options is not null,
                _options?.ResourceRoot ?? string.Empty,
                Interlocked.Read(ref _requestCount),
                _activeRequests,
                _lastError ?? index?.LastError,
                index?.Version ?? 0,
                index?.FileCount ?? 0,
                index?.TotalBytes ?? 0,
                cache?.Hits ?? 0,
                cache?.Misses ?? 0,
                cache?.MemoryBytes ?? 0,
                cache?.DiskBytes ?? 0);
        }
    }

    public Task<bool> ReconcileResourcesAsync(CancellationToken cancellationToken = default)
    {
        lock (_lifecycleLock)
        {
            if (_stopping || _options?.ResourceIndex is null) return Task.FromResult(false);
            return _options.ResourceIndex.ReconcileAsync(cancellationToken);
        }
    }

    public Task<MicroGatewayResponse> HandleAsync(MicroGatewayRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Interlocked.Increment(ref _requestCount);
        if (!TryBeginRequest(out MicroGatewayOptions options))
            return Task.FromResult(MicroGatewayResponse.Text(503, "micro stopped"));
        MicroGatewayResponse response = Route(request, options);
        if (response.WriteBodyAsync is null)
        {
            EndRequest();
            return Task.FromResult(response);
        }

        Func<Stream, CancellationToken, Task> writer = response.WriteBodyAsync;
        MicroGatewayResponse? tracked = null;
        tracked = new MicroGatewayResponse
        {
            StatusCode = response.StatusCode,
            ContentType = response.ContentType,
            ContentLength = response.ContentLength,
            Headers = response.Headers,
            WriteBodyAsync = async (output, token) =>
            {
                try { await writer(output, token).ConfigureAwait(false); }
                finally { tracked!.Dispose(); }
            },
            Completion = EndRequest,
        };
        return Task.FromResult(tracked);
    }

    private MicroGatewayResponse Route(MicroGatewayRequest request, MicroGatewayOptions options)
    {
        try
        {
            if (!string.Equals(request.Method, "GET", StringComparison.OrdinalIgnoreCase))
                return MicroGatewayResponse.Text(405, "method not allowed");
            if (request.AbsolutePath.Equals("/api/health", StringComparison.OrdinalIgnoreCase))
                return MicroGatewayResponse.Text(200, "ok");
            if (request.AbsolutePath.StartsWith("/launcher/", StringComparison.OrdinalIgnoreCase))
                return HandleLauncherFile(options, request.AbsolutePath);
            if (!request.AbsolutePath.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
                return MicroGatewayResponse.Text(404, "not found");
            if (string.IsNullOrWhiteSpace(options.User))
                return MicroGatewayResponse.Text(503, "MicroAuthor not configured");
            if (!Authorized(options, request.Headers)) return MicroGatewayResponse.Text(401, "unauthorized");
            if (options.ResourceUpdateEnabled is not null && !options.ResourceUpdateEnabled())
                return MicroGatewayResponse.Text(503, "resource update disabled");
            string[] segments = request.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length < 2) return MicroGatewayResponse.Text(404, "not found");
            return segments[1].ToLowerInvariant() switch
            {
                "file" => HandleFile(options, request, segments),
                "libheader" => HandleLibraryHeader(options, segments),
                "libimage" => HandleLibraryImage(options, segments),
                "sound" => HandleSound(options, segments),
                _ => MicroGatewayResponse.Text(404, "not found"),
            };
        }
        catch (Exception error)
        {
            lock (_lifecycleLock) _lastError = error.Message;
            return MicroGatewayResponse.Text(500, "request error");
        }
    }

    private bool TryBeginRequest(out MicroGatewayOptions options)
    {
        lock (_lifecycleLock)
        {
            MicroGatewayOptions? current = _options;
            if (_stopping || current is null) { options = null!; return false; }
            options = current;
            if (_activeRequests == 0)
                _idle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _activeRequests++;
            return true;
        }
    }

    private void EndRequest()
    {
        lock (_lifecycleLock)
        {
            _activeRequests--;
            if (_activeRequests == 0) _idle.TrySetResult();
        }
    }

    private static TaskCompletionSource CompletedIdle()
    {
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        completed.SetResult();
        return completed;
    }

    private static bool Authorized(MicroGatewayOptions options, IReadOnlyDictionary<string, string?> headers) =>
        string.Equals(GetHeader(headers, "User"), options.User, StringComparison.Ordinal) &&
        (string.IsNullOrEmpty(options.Code) || string.Equals(GetHeader(headers, "Code"), options.Code, StringComparison.Ordinal));

    private static string? GetHeader(IReadOnlyDictionary<string, string?> headers, string name)
    {
        if (headers.TryGetValue(name, out string? value)) return value;
        foreach ((string key, string? candidate) in headers)
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase)) return candidate;
        return null;
    }

    private static MicroGatewayResponse HandleLauncherFile(MicroGatewayOptions options, string path)
    {
        if (options.LauncherRoot is null) return MicroGatewayResponse.Text(404, "not found");
        string relative = WebUtility.UrlDecode(path["/launcher/".Length..]);
        if (!TryResolve(options.LauncherRoot, string.Empty, relative, underscoreAsSeparator: false, out string? fullPath) || !File.Exists(fullPath))
            return MicroGatewayResponse.Text(404, "not found");
        return StreamFile(fullPath!, null);
    }

    private static MicroGatewayResponse HandleFile(MicroGatewayOptions options, MicroGatewayRequest request, string[] segments)
    {
        if (segments.Length != 4 || !TryResolveIndexed(options, segments[2], segments[3], true, out string? path))
            return MicroGatewayResponse.Text(404, "not found");
        string? range = GetHeader(request.Headers, "Range");
        return StreamFile(path!, range);
    }

    private static MicroGatewayResponse StreamFile(string path, string? range)
    {
        var indexed = new FileInfo(path);
        long total = indexed.Length;
        DateTime indexedWriteUtc = indexed.LastWriteTimeUtc;
        long start = 0, end = total - 1;
        int status = 200;
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Accept-Ranges"] = "bytes" };
        if (!string.IsNullOrWhiteSpace(range))
        {
            if (!TryParseRange(range, total, out start, out end))
                return new MicroGatewayResponse { StatusCode = 416, Headers = new Dictionary<string, string> { ["Content-Range"] = $"bytes */{total}" } };
            status = 206;
            headers["Content-Range"] = $"bytes {start}-{end}/{total}";
        }
        long length = end - start + 1;
        return new MicroGatewayResponse
        {
            StatusCode = status,
            ContentType = "application/octet-stream",
            ContentLength = length,
            Headers = headers,
            WriteBodyAsync = async (output, token) =>
            {
                await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, CopyBufferBytes, FileOptions.Asynchronous | FileOptions.SequentialScan);
                if (input.Length != total || File.GetLastWriteTimeUtc(path) != indexedWriteUtc)
                    throw new IOException("资源文件在建立响应后发生变化，已拒绝本次传输。");
                input.Position = start;
                byte[] buffer = new byte[CopyBufferBytes];
                long remaining = length;
                while (remaining > 0)
                {
                    int read = await input.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), token);
                    if (read == 0) break;
                    await output.WriteAsync(buffer.AsMemory(0, read), token);
                    remaining -= read;
                }
            },
        };
    }

    private static MicroGatewayResponse HandleLibraryHeader(MicroGatewayOptions options, string[] segments)
    {
        if (segments.Length != 4 || !TryResolveIndexed(options, segments[2], segments[3], true, out string? path))
            return MicroGatewayResponse.Text(404, "not found");
        byte[]? payload = options.PayloadCache?.GetOrCreate(CacheKey(path!, "header"), limit => MicroLibraryReader.TryCreateHeaderPayload(path!, limit))
            ?? MicroLibraryReader.TryCreateHeaderPayload(path!, 16 * 1024 * 1024);
        return payload is null ? MicroGatewayResponse.Text(404, "not found") : MicroGatewayResponse.Bytes(200, payload, "application/octet-stream");
    }

    private static MicroGatewayResponse HandleLibraryImage(MicroGatewayOptions options, string[] segments)
    {
        if (segments.Length != 5 || !int.TryParse(segments[4], out int index) ||
            !TryResolveIndexed(options, segments[2], segments[3], true, out string? path))
            return MicroGatewayResponse.Text(404, "not found");
        byte[]? payload = options.PayloadCache?.GetOrCreate(CacheKey(path!, "image:" + index), limit => MicroLibraryReader.TryCreateImagePayload(path!, index, limit))
            ?? MicroLibraryReader.TryCreateImagePayload(path!, index, 16 * 1024 * 1024);
        return payload is null ? MicroGatewayResponse.Text(404, "not found") : MicroGatewayResponse.Bytes(200, payload, "application/octet-stream");
    }

    private MicroGatewayResponse HandleSound(MicroGatewayOptions options, string[] segments)
    {
        if (segments.Length is not (3 or 4)) return MicroGatewayResponse.Text(404, "not found");
        string name = Path.GetFileNameWithoutExtension(WebUtility.UrlDecode(segments[2]));
        int chunk = 1;
        if (segments.Length == 4 && (!int.TryParse(segments[3], out chunk) || chunk < 1)) return MicroGatewayResponse.Text(404, "not found");
        if (!TryResolveSound(options, name, out string? path)) return MicroGatewayResponse.Text(404, "not found");
        try
        {
            long total = new FileInfo(path!).Length;
            if (total <= 0) return MicroGatewayResponse.Text(404, "not found");
            int max = checked((int)((total + SoundChunkBytes - 1) / SoundChunkBytes));
            if (chunk > max) return MicroGatewayResponse.Text(404, "not found");
            int length = (int)Math.Min(SoundChunkBytes, total - (long)(chunk - 1) * SoundChunkBytes);
            byte[]? CreateSoundPayload(int maximumBytes)
            {
                byte[] bytes = new byte[length];
                using var input = new FileStream(path!, FileMode.Open, FileAccess.Read, FileShare.Read);
                input.Position = (long)(chunk - 1) * SoundChunkBytes;
                input.ReadExactly(bytes);
                byte[] payload = JsonSerializer.SerializeToUtf8Bytes(new SoundPayload(bytes, max, chunk));
                return payload.Length <= maximumBytes ? payload : null;
            }
            byte[]? json = options.PayloadCache?.GetOrCreate(CacheKey(path!, "sound:" + chunk), CreateSoundPayload)
                ?? CreateSoundPayload(16 * 1024 * 1024);
            if (json is null) return MicroGatewayResponse.Text(404, "not found");
            return MicroGatewayResponse.Bytes(200, json, "application/json; charset=UTF-8");
        }
        catch { return MicroGatewayResponse.Text(404, "not found"); }
    }

    private bool TryResolveSound(MicroGatewayOptions options, string requested, out string? path)
    {
        path = null;
        string safe = (Path.GetFileNameWithoutExtension(requested) ?? string.Empty).Trim();
        if (safe.Length == 0) return false;
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { safe + ".wav" };
        if (int.TryParse(safe, out int index)) AddSoundAlias(options, candidates, index);
        string[] parts = safe.Split('-', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2 && int.TryParse(parts[0], out int prefix) && prefix >= 0 && int.TryParse(parts[1], out int suffix) && suffix is >= 0 and <= 9)
        {
            int dashed = checked(prefix * 10 + suffix);
            AddSoundAlias(options, candidates, dashed);
            if (dashed > 0) candidates.Add(dashed + ".wav");
        }
        foreach (string candidate in candidates)
            if (TryResolveIndexed(options, "Sound", candidate, true, out string? resolved)) { path = resolved; return true; }
        return false;
    }

    private void AddSoundAlias(MicroGatewayOptions options, HashSet<string> candidates, int index)
    {
        if (index <= 0 || !TryResolveIndexed(options, "Sound", "SoundList.lst", true, out string? listPath)) return;
        try
        {
            var info = new FileInfo(listPath!);
            if (!info.Exists) return;
            if (!_soundLists.TryGetValue(listPath!, out SoundListCache? cache) || cache.Length != info.Length || cache.LastWriteTimeUtc != info.LastWriteTimeUtc)
            {
                var entries = new Dictionary<int, string>();
                foreach (string line in File.ReadAllLines(listPath!))
                {
                    string[] split = line.Replace(" ", string.Empty).Split(':', '\t');
                    if (split.Length > 1 && int.TryParse(split[0], out int key)) entries.TryAdd(key, Path.GetFileName(split[^1]).Trim());
                }
                cache = new SoundListCache(info.LastWriteTimeUtc, info.Length, entries);
                _soundLists[listPath!] = cache;
            }
            if (cache.Entries.TryGetValue(index, out string? alias) && !string.IsNullOrWhiteSpace(alias))
                candidates.Add(alias.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ? alias : alias + ".wav");
        }
        catch { }
    }

    private static bool TryResolve(string root, string encodedPath, string encodedName, bool underscoreAsSeparator, out string? fullPath)
    {
        fullPath = null;
        try
        {
            string normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string relativePath = WebUtility.UrlDecode(encodedPath ?? string.Empty);
            if (underscoreAsSeparator) relativePath = relativePath.Replace('_', Path.DirectorySeparatorChar);
            relativePath = relativePath.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string name = WebUtility.UrlDecode(encodedName ?? string.Empty).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (name.Length == 0 || Path.IsPathRooted(name)) return false;
            string combined = Path.GetFullPath(Path.Combine(normalizedRoot, relativePath, name));
            if (!combined.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase)) return false;
            if (ContainsReparsePoint(normalizedRoot, combined)) return false;
            fullPath = combined;
            return true;
        }
        catch { return false; }
    }

    private static bool TryResolveIndexed(MicroGatewayOptions options, string encodedPath, string encodedName, bool underscoreAsSeparator, out string? fullPath)
    {
        fullPath = null;
        return TryResolve(options.ResourceRoot, encodedPath, encodedName, underscoreAsSeparator, out string? candidate) &&
               options.ResourceIndex is not null && options.ResourceIndex.TryGetFile(candidate!, out fullPath);
    }

    private static string CacheKey(string path, string operation)
    {
        var info = new FileInfo(path);
        return $"{path}|{info.Length}|{info.LastWriteTimeUtc.Ticks}|{operation}";
    }

    private static bool ContainsReparsePoint(string normalizedRoot, string fullPath)
    {
        string relative = Path.GetRelativePath(normalizedRoot, fullPath);
        string current = normalizedRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        foreach (string segment in relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (!File.Exists(current) && !Directory.Exists(current)) continue;
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return true;
        }
        return false;
    }

    private static bool TryParseRange(string value, long total, out long start, out long end)
    {
        start = end = 0;
        if (total <= 0 || !value.Trim().StartsWith("bytes=", StringComparison.OrdinalIgnoreCase)) return false;
        string spec = value.Trim()[6..].Trim();
        if (spec.Length == 0 || spec.Contains(',')) return false;
        int dash = spec.IndexOf('-');
        if (dash < 0) return false;
        string left = spec[..dash].Trim(), right = spec[(dash + 1)..].Trim();
        if (left.Length == 0)
        {
            if (!long.TryParse(right, out long suffix) || suffix <= 0) return false;
            suffix = Math.Min(suffix, total); start = total - suffix; end = total - 1; return true;
        }
        if (!long.TryParse(left, out start) || start < 0 || start >= total) return false;
        if (right.Length == 0) { end = total - 1; return true; }
        if (!long.TryParse(right, out end) || end < start) return false;
        end = Math.Min(end, total - 1); return true;
    }
}
