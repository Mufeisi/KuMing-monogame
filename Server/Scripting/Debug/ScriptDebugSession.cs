namespace Server.Scripting.Debug
{
    public sealed class ScriptDebugSession : IDisposable
    {
        private readonly object _gate = new object();

        private readonly HashSet<ScriptDebugBreakpoint> _breakpoints = new HashSet<ScriptDebugBreakpoint>();

        private readonly ManualResetEventSlim _resumeGate = new ManualResetEventSlim(initialState: true);

        private bool _disposed;
        private bool _pauseRequested;
        private bool _cancelRequested;
        private ScriptDebugRunMode _runMode = ScriptDebugRunMode.Run;

        private bool _isPaused;
        private ScriptDebugPauseReason _pauseReason;
        private ScriptDebugLocation _currentLocation;
        private long _pauseSequence;

        public bool Enabled { get; set; } = true;

        public ScriptDebugRunMode RunMode
        {
            get
            {
                lock (_gate) return _runMode;
            }
        }

        public bool IsPaused
        {
            get
            {
                lock (_gate) return _isPaused;
            }
        }

        public ScriptDebugPauseReason PauseReason
        {
            get
            {
                lock (_gate) return _pauseReason;
            }
        }

        public ScriptDebugLocation CurrentLocation
        {
            get
            {
                lock (_gate) return _currentLocation;
            }
        }

        public long PauseSequence
        {
            get
            {
                lock (_gate) return _pauseSequence;
            }
        }

        public ScriptDebugBreakpoint[] GetBreakpointsSnapshot()
        {
            lock (_gate)
            {
                return _breakpoints
                    .OrderBy(b => b.FilePath, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(b => b.Line)
                    .ToArray();
            }
        }

        public void ClearBreakpoints()
        {
            lock (_gate)
            {
                _breakpoints.Clear();
            }
        }

        public bool AddBreakpoint(string filePath, int line)
        {
            if (string.IsNullOrWhiteSpace(filePath) || line <= 0) return false;

            var bp = new ScriptDebugBreakpoint(NormalizePath(filePath), line);

            lock (_gate)
            {
                return _breakpoints.Add(bp);
            }
        }

        public bool RemoveBreakpoint(string filePath, int line)
        {
            if (string.IsNullOrWhiteSpace(filePath) || line <= 0) return false;

            var bp = new ScriptDebugBreakpoint(NormalizePath(filePath), line);

            lock (_gate)
            {
                return _breakpoints.Remove(bp);
            }
        }

        public void RequestPause()
        {
            lock (_gate)
            {
                _pauseRequested = true;
            }
        }

        public void Continue()
        {
            lock (_gate)
            {
                _runMode = ScriptDebugRunMode.Run;
                _pauseRequested = false;
                _isPaused = false;
                _pauseReason = ScriptDebugPauseReason.None;
            }

            _resumeGate.Set();
        }

        public void StepOnce()
        {
            lock (_gate)
            {
                _runMode = ScriptDebugRunMode.Step;
                _pauseRequested = false;
                _isPaused = false;
                _pauseReason = ScriptDebugPauseReason.None;
            }

            _resumeGate.Set();
        }

        public void Cancel()
        {
            lock (_gate)
            {
                _cancelRequested = true;
                _pauseRequested = false;
                _isPaused = false;
                _pauseReason = ScriptDebugPauseReason.None;
            }

            _resumeGate.Set();
        }

        public void ResetForNewRun()
        {
            lock (_gate)
            {
                _cancelRequested = false;
                _pauseRequested = false;
                _runMode = ScriptDebugRunMode.Run;
                _isPaused = false;
                _pauseReason = ScriptDebugPauseReason.None;
                _currentLocation = default;
            }

            _resumeGate.Set();
        }

        internal void Step(string filePath, int line, int column)
        {
            if (!Enabled) return;

            ScriptDebugPauseReason reasonToPause = ScriptDebugPauseReason.None;

            lock (_gate)
            {
                if (_disposed) return;
                if (_cancelRequested) throw new OperationCanceledException("脚本调试会话已取消。");

                filePath = NormalizePath(filePath);
                _currentLocation = new ScriptDebugLocation(filePath, line, column);

                var bp = new ScriptDebugBreakpoint(filePath, line);

                if (_pauseRequested)
                {
                    reasonToPause = ScriptDebugPauseReason.PauseRequested;
                    _pauseRequested = false;
                }
                else if (_runMode == ScriptDebugRunMode.Step)
                {
                    reasonToPause = ScriptDebugPauseReason.Step;
                }
                else if (bp.IsValid && _breakpoints.Contains(bp))
                {
                    reasonToPause = ScriptDebugPauseReason.Breakpoint;
                }

                if (reasonToPause != ScriptDebugPauseReason.None)
                {
                    _isPaused = true;
                    _pauseReason = reasonToPause;
                    _pauseSequence++;
                    _resumeGate.Reset();
                }
            }

            if (reasonToPause == ScriptDebugPauseReason.None)
                return;

            _resumeGate.Wait();

            lock (_gate)
            {
                if (_disposed) return;
                if (_cancelRequested) throw new OperationCanceledException("脚本调试会话已取消。");
            }
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;

            try
            {
                return Path.GetFullPath(path);
            }
            catch
            {
                return path.Trim();
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
            }

            _resumeGate.Set();
            _resumeGate.Dispose();
        }
    }
}
