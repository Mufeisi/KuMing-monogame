using System;
using System.Diagnostics;
using System.Windows.Forms;
using Launcher.ThemeRuntime;

namespace Launcher.Remote
{
    public sealed class GameInstanceManager
    {
        private readonly GameInstanceLimit _limit;
        private readonly bool _useTestConfig;
        private readonly int _maximumInstances;
        private readonly string _executableDirectory;
        private readonly string _resourceDirectory;
        private readonly string _projectId;
        private readonly IReadOnlyCollection<LauncherCoreResource> _trustedResources;

        public event EventHandler ActiveCountChanged;
        public int ActiveCount => _limit.ActiveCount;

        public GameInstanceManager(int maximumInstances, bool useTestConfig = false, string executableDirectory = null, string resourceDirectory = null, string projectId = null, IReadOnlyCollection<LauncherCoreResource> trustedResources = null)
        {
            _limit = new GameInstanceLimit(maximumInstances);
            _maximumInstances = maximumInstances;
            _useTestConfig = useTestConfig;
            _executableDirectory = string.IsNullOrWhiteSpace(executableDirectory) ? Application.StartupPath : Path.GetFullPath(executableDirectory);
            _resourceDirectory = string.IsNullOrWhiteSpace(resourceDirectory) ? Application.StartupPath : Path.GetFullPath(resourceDirectory);
            _projectId = projectId ?? string.Empty;
            _trustedResources = trustedResources;
        }

        public bool TryStart(ServerEntry server, out string error)
        {
            error = string.Empty;
            if (!_limit.TryAcquire())
            {
                error = $"已达到多开上限（{_limit.ActiveCount}/{_maximumInstances}）。";
                return false;
            }

            int slotReleased = 0;
            void ReleaseSlot()
            {
                if (Interlocked.Exchange(ref slotReleased, 1) != 0) return;
                _limit.Release();
                ActiveCountChanged?.Invoke(this, EventArgs.Empty);
            }

            Process process = null;
            bool started = false;

            try
            {
                if (!string.IsNullOrWhiteSpace(_projectId))
                {
                    if (_trustedResources == null ||
                        !ClientSelection.IsCompatible(_executableDirectory) ||
                        !ClientSelection.IsTrustedResourceDirectory(_resourceDirectory, _trustedResources))
                        throw new InvalidOperationException("客户端入口或本地资源已发生变化，请重新选择客户端");
                    ClientSettingsWriter.ValidateWritableDirectory(_resourceDirectory);
                }

                var startInfo = new ProcessStartInfo
                {
                    FileName = Path.Combine(_executableDirectory, "Client.exe"),
                    WorkingDirectory = _resourceDirectory,
                    UseShellExecute = false,
                };
                foreach (string argument in GameLaunchArguments.Create(server))
                    startInfo.ArgumentList.Add(argument);
                if (_useTestConfig) startInfo.ArgumentList.Add("-tc");
                if (!string.IsNullOrWhiteSpace(_projectId)) startInfo.Environment["LYOCRYSTAL_CLASSIC_PROJECT_ID"] = _projectId;

                process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
                process.Exited += (_, _) =>
                {
                    process.Dispose();
                    ReleaseSlot();
                };

                if (!process.Start())
                    throw new InvalidOperationException("操作系统未能启动游戏进程");
                started = true;

                if (!string.IsNullOrWhiteSpace(_projectId))
                {
                    string sourcePlayer = Environment.GetEnvironmentVariable("LYOCRYSTAL_PLAYER_SOURCE_EXECUTABLE") ?? string.Empty;
                    try
                    {
                        if (!string.IsNullOrWhiteSpace(sourcePlayer)) Launcher.PlayerShell.PlayerGameSessionMarker.Record(sourcePlayer, process);
                    }
                    catch
                    {
                        try { process.Kill(entireProcessTree: true); } catch { }
                        throw;
                    }
                }

                ActiveCountChanged?.Invoke(this, EventArgs.Empty);
                return true;
            }
            catch (Exception ex)
            {
                if (!started) process?.Dispose();
                ReleaseSlot();
                error = "启动游戏失败：" + ex.Message;
                return false;
            }
        }
    }
}
