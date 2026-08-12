using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Client.Bootstrap;

namespace Client
{
    internal sealed class PcPreLoginUpdateForm : Form
    {
        private readonly Label _statusLabel;
        private readonly ProgressBar _progressBar;
        private readonly Button _actionButton;

        private CancellationTokenSource _cts;

        public PcBootstrapApplyResultView Result { get; private set; }

        public PcPreLoginUpdateForm()
        {
            Text = "资源更新";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = true;
            ClientSize = new Size(520, 150);

            _statusLabel = new Label
            {
                Dock = DockStyle.Top,
                Height = 80,
                Padding = new Padding(12, 12, 12, 6),
                TextAlign = ContentAlignment.MiddleLeft,
                Text = "正在检查资源更新…",
            };

            _progressBar = new ProgressBar
            {
                Dock = DockStyle.Top,
                Height = 16,
                Style = ProgressBarStyle.Marquee,
                MarqueeAnimationSpeed = 25,
            };

            _actionButton = new Button
            {
                Dock = DockStyle.Bottom,
                Height = 34,
                Text = "取消/跳过",
            };
            _actionButton.Click += (_, _) =>
            {
                try
                {
                    _cts?.Cancel();
                }
                catch (Exception)
                {
                }
            };

            Controls.Add(_actionButton);
            Controls.Add(_progressBar);
            Controls.Add(_statusLabel);
        }

        protected override async void OnShown(EventArgs e)
        {
            base.OnShown(e);

            _cts = new CancellationTokenSource();

            var progress = new Progress<PcBootstrapProgress>(UpdateProgressUi);

            Result = await PcBootstrapPreLoginUpdateService.TryRunPreLoginUpdateAsync(progress, _cts.Token);

            if (Result == null)
            {
                _statusLabel.Text = "未获取到资源更新结果，已跳过。";
                Close();
                return;
            }

            _progressBar.Style = ProgressBarStyle.Continuous;
            _progressBar.Value = 100;

            if (Result.Failed)
            {
                _statusLabel.Text = $"资源更新失败：{Result.Message}\n\n点击按钮继续启动（也可稍后重启再试）。";
                _actionButton.Text = "继续启动";
                _actionButton.Enabled = true;
                _actionButton.Click += (_, _) => Close();
                return;
            }

            _statusLabel.Text = Result.Message ?? "资源更新完成。";

            // 非失败：稍作停留，让玩家看到结果，再自动关闭。
            await Task.Delay(350);
            Close();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            try
            {
                _cts?.Cancel();
                _cts?.Dispose();
            }
            catch (Exception)
            {
            }

            _cts = null;
            base.OnFormClosing(e);
        }

        private void UpdateProgressUi(PcBootstrapProgress progress)
        {
            if (progress == null)
                return;

            string packageName = (progress.PackageName ?? string.Empty).Trim();
            string message = (progress.Message ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(message))
            {
                message = string.IsNullOrWhiteSpace(packageName)
                    ? "正在更新资源…"
                    : $"正在更新：{packageName}";
            }

            if (string.IsNullOrWhiteSpace(packageName))
                _statusLabel.Text = message;
            else
                _statusLabel.Text = $"{message}\n包：{packageName}";

            if (progress.TotalBytes > 0)
            {
                _progressBar.Style = ProgressBarStyle.Continuous;
                double percent = (double)Math.Clamp(progress.ReceivedBytes, 0, progress.TotalBytes) / progress.TotalBytes;
                int value = (int)Math.Round(percent * 100);
                _progressBar.Value = Math.Clamp(value, 0, 100);
            }
            else
            {
                _progressBar.Style = ProgressBarStyle.Marquee;
            }
        }
    }
}

