using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MonoShare;
using MonoShare.MirControls;

namespace MonoShare.MirScenes
{
    public sealed class PreLoginUpdateScene : MirScene
    {
        private CancellationTokenSource _cts;
        private Task<BootstrapPreLoginUpdatePlanView> _planTask;
        private BootstrapPreLoginUpdatePlanView _plan;
        private bool _userContinue;

        public PreLoginUpdateScene()
        {
            _cts = new CancellationTokenSource();
        }

        public override void Process()
        {
            if (_planTask == null)
                _planTask = BootstrapPackageUpdateService.TryEnsurePreLoginUpdateQueueAsync(_cts.Token);

            if (_plan == null && _planTask != null && _planTask.IsCompleted)
            {
                try
                {
                    _plan = _planTask.GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    _plan = BootstrapPreLoginUpdatePlanView.Fail(ex.Message);
                }
            }

            if (ShouldStartMobileHudPrewarm())
            {
                MobileMainHudPrewarm.EnsureStarted();
                MobileMainHudPrewarm.Tick();
            }

            UpdateStatusText();

            if (_userContinue)
            {
                GoToLoginScene();
                return;
            }

            if (_plan == null)
                return;

            if (_plan.Failed)
                return;

            if (!_plan.Skipped && (_plan.PackagesToUpdate?.Count ?? 0) > 0)
            {
                if (BootstrapPackageUpdateRuntime.GetUpdatePackageNames().Count > 0)
                    return;
            }

            GoToLoginScene();
        }

        public override void OnMouseClick(EventArgs e)
        {
            base.OnMouseClick(e);

            if (_plan == null)
                return;

            if (_plan.Failed)
            {
                _userContinue = true;
                return;
            }

            if (_plan.Skipped)
            {
                _userContinue = true;
                return;
            }

            if ((_plan.PackagesToUpdate?.Count ?? 0) <= 0)
                return;

            BootstrapPackageDownloader.BootstrapDownloadStateSnapshot snapshot = BootstrapPackageDownloader.GetStateSnapshot();
            if (string.IsNullOrWhiteSpace(snapshot?.LastError))
                return;

            try
            {
                BootstrapPackageUpdateRuntime.ReplaceUpdateQueue(
                    _plan.ResourceVersion ?? string.Empty,
                    Array.Empty<BootstrapPackageUpdateEntryView>());
            }
            catch
            {
            }

            _userContinue = true;
        }

        private void UpdateStatusText()
        {
            string text;
            float progress01 = -1F;

            if (_plan == null)
            {
                text = "正在检查资源更新…";
                progress01 = 0F;
            }
            else if (_plan.Failed)
            {
                text = $"资源更新失败：{_plan.Message}\n\n点击屏幕继续登录。";
            }
            else
            {
                string version = string.IsNullOrWhiteSpace(_plan.ResourceVersion)
                    ? string.Empty
                    : $"（资源版本：{_plan.ResourceVersion}）";

                if ((_plan.PackagesToUpdate?.Count ?? 0) > 0)
                {
                    BootstrapPackageDownloader.BootstrapDownloadStateSnapshot snapshot = BootstrapPackageDownloader.GetStateSnapshot();
                    string lastError = (snapshot?.LastError ?? string.Empty).Trim();

                    if (!string.IsNullOrWhiteSpace(lastError))
                    {
                        text = $"资源更新进行中{version}\n最近错误：{lastError}\n\n点击屏幕跳过更新并登录。";
                    }
                    else
                    {
                        string sample = string.Join(", ", _plan.PackagesToUpdate.Take(4));
                        text = $"正在更新资源{version}\n待更新：{_plan.PackagesToUpdate.Count}（{sample}）";
                    }

                    if (snapshot != null && snapshot.ActiveTotalBytes > 0)
                    {
                        progress01 = Math.Max(0F, Math.Min(1F, snapshot.ActiveBytesReceived / (float)snapshot.ActiveTotalBytes));
                    }
                }
                else
                {
                    text = _plan.Skipped
                        ? $"已跳过资源更新：{_plan.Message}"
                        : $"资源已是最新{version}";
                    progress01 = 0F;
                }
            }

            if (ShouldStartMobileHudPrewarm() && !MobileMainHudPrewarm.IsCompleted && !MobileMainHudPrewarm.HasFailed)
            {
                int total = MobileMainHudPrewarm.TotalAtlases;
                int loaded = MobileMainHudPrewarm.LoadedAtlases;
                string prewarmText = total <= 0
                    ? "正在后台预取界面资源…"
                    : $"正在后台预取界面资源… {Math.Min(total, Math.Max(0, loaded))}/{total}";

                text = string.IsNullOrWhiteSpace(text)
                    ? prewarmText
                    : text + "\n\n" + prewarmText;

                if (progress01 < 0F && total > 0)
                    progress01 = Math.Max(0F, Math.Min(1F, loaded / (float)total));
            }

            FairyGuiHost.SetMobilePreLoginUpdateStatus(text);
            FairyGuiHost.SetMobilePreLoginUpdateProgress(progress01);
        }

        private static bool ShouldStartMobileHudPrewarm()
        {
            return Environment.OSVersion.Platform != PlatformID.Win32NT;
        }

        private void GoToLoginScene()
        {
            if (IsDisposed)
                return;

            if (ActiveScene is LoginScene)
                return;

            try
            {
                _cts?.Cancel();
            }
            catch
            {
            }

            FairyGuiHost.HideMobilePreLoginUpdateOverlay();
            Dispose();
            ActiveScene = new LoginScene();
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            if (!disposing)
                return;

            FairyGuiHost.HideMobilePreLoginUpdateOverlay();

            try
            {
                _cts?.Cancel();
            }
            catch
            {
            }

            _cts?.Dispose();
            _cts = null;
            _planTask = null;
            _plan = null;
        }
    }
}
