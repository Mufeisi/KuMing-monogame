namespace Server.Scripting
{
    public sealed class ScriptWatcher : IDisposable
    {
        private readonly FileSystemWatcher _watcher;
        private readonly Timer _debounceTimer;
        private readonly object _gate = new object();
        private readonly int _debounceMs;
        private bool _pending;

        public event Action ScriptsChanged;

        public ScriptWatcher(string scriptsRootPath, int debounceMs)
        {
            if (string.IsNullOrWhiteSpace(scriptsRootPath))
                throw new ArgumentException("脚本根目录不能为空。", nameof(scriptsRootPath));

            _debounceMs = Math.Max(0, debounceMs);

            Directory.CreateDirectory(scriptsRootPath);

            _watcher = new FileSystemWatcher(scriptsRootPath)
            {
                IncludeSubdirectories = true,
                Filter = "*.cs",
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.Size,
                EnableRaisingEvents = false,
            };

            _watcher.Created += OnChanged;
            _watcher.Changed += OnChanged;
            _watcher.Deleted += OnChanged;
            _watcher.Renamed += OnRenamed;
            _watcher.Error += OnError;

            _debounceTimer = new Timer(OnDebounceTimer, null, Timeout.Infinite, Timeout.Infinite);
        }

        public void Start()
        {
            _watcher.EnableRaisingEvents = true;
        }

        public void Stop()
        {
            _watcher.EnableRaisingEvents = false;
        }

        private void OnChanged(object sender, FileSystemEventArgs e)
        {
            Schedule();
        }

        private void OnRenamed(object sender, RenamedEventArgs e)
        {
            Schedule();
        }

        private void OnError(object sender, ErrorEventArgs e)
        {
            MessageQueue.Instance.Enqueue(e.GetException());
        }

        private void Schedule()
        {
            lock (_gate)
            {
                _pending = true;
                _debounceTimer.Change(_debounceMs, Timeout.Infinite);
            }
        }

        private void OnDebounceTimer(object state)
        {
            Action handler = null;

            lock (_gate)
            {
                if (!_pending) return;
                _pending = false;
                handler = ScriptsChanged;
            }

            try
            {
                handler?.Invoke();
            }
            catch (Exception ex)
            {
                MessageQueue.Instance.Enqueue(ex);
            }
        }

        public void Dispose()
        {
            Stop();
            _watcher.Dispose();
            _debounceTimer.Dispose();
        }
    }
}

