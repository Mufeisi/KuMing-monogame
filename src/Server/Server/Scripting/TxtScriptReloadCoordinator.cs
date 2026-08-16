using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace Server.Scripting
{
    public sealed record TxtScriptSnapshot(
        long Version,
        string Digest,
        IReadOnlyList<string> Keys,
        IReadOnlyList<string> ChangedKeys,
        long LoadMilliseconds,
        DateTimeOffset PublishedAt,
        ITextFileProvider Provider);

    public sealed record TxtScriptReloadResult(
        bool Published,
        TxtScriptSnapshot Snapshot,
        IReadOnlyList<string> Errors);

    public sealed class TxtScriptReloadCoordinator : IDisposable
    {
        private readonly PhysicalTextFileProviderOptions _options;
        private readonly Func<ITextFileProvider, IReadOnlyList<string>> _validator;
        private readonly Func<ITextFileProvider, bool> _publisher;
        private readonly FileSystemWatcher _watcher;
        private readonly Timer _debounceTimer;
        private readonly object _gate = new object();
        private readonly int _debounceMs;
        private long _version;
        private bool _pending;
        private bool _disposed;
        private int _reloadInProgress;

        public TxtScriptReloadCoordinator(
            PhysicalTextFileProviderOptions options,
            int debounceMs,
            Func<ITextFileProvider, bool> publisher,
            Func<ITextFileProvider, IReadOnlyList<string>> validator = null)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
            _validator = validator ?? (_ => Array.Empty<string>());
            _debounceMs = Math.Max(0, debounceMs);

            string root = Path.GetFullPath(options.RootPath);
            if (!Directory.Exists(root))
                throw new DirectoryNotFoundException($"TXT 脚本根目录不存在：{root}");
            _watcher = new FileSystemWatcher(root, "*.txt")
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite |
                               NotifyFilters.CreationTime | NotifyFilters.Size,
                EnableRaisingEvents = false
            };
            _watcher.Created += OnChanged;
            _watcher.Changed += OnChanged;
            _watcher.Deleted += OnChanged;
            _watcher.Renamed += OnRenamed;
            _watcher.Error += OnError;
            _debounceTimer = new Timer(OnDebounce, null, Timeout.Infinite, Timeout.Infinite);
        }

        public TxtScriptSnapshot Current { get; private set; }

        public TxtScriptReloadResult LastResult { get; private set; }

        public DateTimeOffset? LastSuccessfulReload => Current?.PublishedAt;

        public event Action<TxtScriptReloadResult> ReloadCompleted;

        public void Start() => _watcher.EnableRaisingEvents = true;

        public void Stop()
        {
            _watcher.EnableRaisingEvents = false;
            lock (_gate)
            {
                _pending = false;
                _debounceTimer.Change(Timeout.Infinite, Timeout.Infinite);
            }
        }

        public TxtScriptReloadResult ReloadNow()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(TxtScriptReloadCoordinator));
            if (Interlocked.Exchange(ref _reloadInProgress, 1) != 0)
                return Complete(false, Current, new[] { "已有 TXT 重载正在进行，本次请求未发布。" });

            try
            {
                var stopwatch = Stopwatch.StartNew();
                var provider = new PhysicalTextFileProvider(_options);
                IReadOnlyList<string> errors = _validator(provider) ?? Array.Empty<string>();
                if (errors.Count > 0) return Complete(false, Current, errors);

                string[] keys = IndexContent(provider).Keys
                    .OrderBy(key => key, StringComparer.Ordinal).ToArray();
                string digest = ComputeDigest(provider);
                string[] changed = FindChangedKeys(Current?.Provider, provider);
                var candidate = new TxtScriptSnapshot(
                    Interlocked.Read(ref _version) + 1,
                    digest,
                    keys,
                    changed,
                    stopwatch.ElapsedMilliseconds,
                    DateTimeOffset.UtcNow,
                    provider);
                if (!_publisher(provider))
                    return Complete(false, Current, new[] { "TXT 候选快照未能在服务端主线程发布。" });

                Interlocked.Exchange(ref _version, candidate.Version);
                Current = candidate;
                return Complete(true, candidate, Array.Empty<string>());
            }
            catch (Exception ex)
            {
                return Complete(false, Current, new[] { ex.Message });
            }
            finally
            {
                Volatile.Write(ref _reloadInProgress, 0);
            }
        }

        internal void NotifyChangeForTest() => Schedule();

        private void OnChanged(object sender, FileSystemEventArgs e) => Schedule();

        private void OnRenamed(object sender, RenamedEventArgs e) => Schedule();

        private void OnError(object sender, ErrorEventArgs e) =>
            Complete(false, Current, new[] { $"TXT watcher 错误：{e.GetException().Message}" });

        private void Schedule()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _pending = true;
                _debounceTimer.Change(_debounceMs, Timeout.Infinite);
            }
        }

        private void OnDebounce(object state)
        {
            lock (_gate)
            {
                if (!_pending || _disposed) return;
                _pending = false;
            }
            ReloadNow();
        }

        private TxtScriptReloadResult Complete(
            bool published,
            TxtScriptSnapshot snapshot,
            IReadOnlyList<string> errors)
        {
            var result = new TxtScriptReloadResult(published, snapshot, errors);
            LastResult = result;
            ReloadCompleted?.Invoke(result);
            return result;
        }

        private static string ComputeDigest(ITextFileProvider provider)
        {
            using var sha = SHA256.Create();
            foreach ((string key, string content) in IndexContent(provider)
                         .OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                byte[] bytes = Encoding.UTF8.GetBytes(key + "\n" + content + "\n");
                sha.TransformBlock(bytes, 0, bytes.Length, null, 0);
            }
            sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            return Convert.ToHexString(sha.Hash);
        }

        private static string[] FindChangedKeys(ITextFileProvider previous, ITextFileProvider current)
        {
            var previousDigests = IndexContent(previous);
            var currentDigests = IndexContent(current);
            return previousDigests.Keys.Concat(currentDigests.Keys).Distinct(StringComparer.Ordinal)
                .Where(key => !previousDigests.TryGetValue(key, out string before) ||
                              !currentDigests.TryGetValue(key, out string after) || before != after)
                .OrderBy(key => key, StringComparer.Ordinal).ToArray();
        }

        private static Dictionary<string, string> IndexContent(ITextFileProvider provider)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            if (provider == null) return result;
            foreach (TextFileDefinition definition in provider.GetAll())
                result[definition.Key] = definition.SourceEncoding + "\n" +
                                         definition.SourceNewLine + "\n" + string.Join("\n", definition.Lines);
            if (provider is PhysicalTextFileProvider physical)
            {
                foreach (TextFileDefinition definition in physical.CommerceSourceDefinitions)
                    result[definition.Key] = definition.SourceEncoding + "\n" +
                                             definition.SourceNewLine + "\n" + string.Join("\n", definition.Lines);
            }
            return result;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
            _watcher.Dispose();
            _debounceTimer.Dispose();
        }
    }
}
