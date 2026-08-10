using System;
using System.Diagnostics;
using System.Windows.Forms;

namespace Launcher.Remote
{
    public sealed class GameInstanceManager
    {
        private readonly GameInstanceLimit _limit;
        private readonly bool _useTestConfig;
        private readonly int _maximumInstances;

        public event EventHandler ActiveCountChanged;
        public int ActiveCount => _limit.ActiveCount;

        public GameInstanceManager(int maximumInstances, bool useTestConfig = false)
        {
            _limit = new GameInstanceLimit(maximumInstances);
            _maximumInstances = maximumInstances;
            _useTestConfig = useTestConfig;
        }

        public bool TryStart(ServerEntry server, out string error)
        {
            error = string.Empty;
            if (!_limit.TryAcquire())
            {
                error = $"已达到多开上限（{_limit.ActiveCount}/{_maximumInstances}）。";
                return false;
            }

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = Application.ExecutablePath,
                    WorkingDirectory = Application.StartupPath,
                    UseShellExecute = false,
                };
                foreach (string argument in GameLaunchArguments.Create(server))
                    startInfo.ArgumentList.Add(argument);
                if (_useTestConfig) startInfo.ArgumentList.Add("-tc");

                Process process = new() { StartInfo = startInfo, EnableRaisingEvents = true };
                process.Exited += (_, _) =>
                {
                    process.Dispose();
                    _limit.Release();
                    ActiveCountChanged?.Invoke(this, EventArgs.Empty);
                };

                if (!process.Start())
                    throw new InvalidOperationException("操作系统未能启动游戏进程");

                ActiveCountChanged?.Invoke(this, EventArgs.Empty);
                return true;
            }
            catch (Exception ex)
            {
                _limit.Release();
                error = "启动游戏失败：" + ex.Message;
                ActiveCountChanged?.Invoke(this, EventArgs.Empty);
                return false;
            }
        }
    }
}
