namespace Launcher.ThemeRuntime;

internal sealed class LauncherForm : Form
{
    private readonly LoadedLauncherSnapshot _loaded;
    private readonly string _clientDirectory;
    private readonly Action<string, LauncherServer, MicroEndpoint, LauncherPlayerSettings> _launch;
    private readonly ComboBox _serverDropdown = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TreeView _serverSidebar = new() { BorderStyle = BorderStyle.None, HideSelection = false };
    private readonly Panel _announcements = new() { AutoScroll = true };
    private readonly ProgressBar _overall = new();
    private readonly ProgressBar _current = new();
    private readonly Label _progressText = new() { AutoEllipsis = true };
    private readonly Label _sourceText = new() { AutoSize = true };
    private readonly ImageStateButton _launchButton = new() { Text = "进入游戏" };
    private LauncherPlayerSettings _settings;
    private string _selectedClientDirectory;
    private bool _settingsDirty;
    private bool _launching;
    private readonly List<Image> _ownedImages = new();
    private readonly List<Control> _clickTargets = new();
    private readonly System.Windows.Forms.Timer _progressTimer = new() { Interval = 300 };
    private bool _autoStartTriggered;

    public LauncherForm(LoadedLauncherSnapshot loaded, string clientDirectory, Action<string, LauncherServer, MicroEndpoint, LauncherPlayerSettings> launch)
    {
        _loaded = loaded;
        _clientDirectory = clientDirectory;
        _launch = launch;
        _selectedClientDirectory = ClientSelection.GetPreferred(loaded.Snapshot.ProjectId, clientDirectory);
        _settings = ClientSettingsWriter.Read(_selectedClientDirectory, CloneSettings(loaded.Snapshot.Defaults));
        Text = loaded.Snapshot.ProjectName;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(loaded.Snapshot.Theme.CanvasWidth, loaded.Snapshot.Theme.CanvasHeight);
        MinimumSize = Size;
        BackColor = Color.FromArgb(18, 20, 28);
        ForeColor = Color.WhiteSmoke;
        DoubleBuffered = true;
        BuildUi();
        string background = LauncherSnapshotValidator.ResolveAsset(_loaded.Root, _loaded.Snapshot.Theme.BackgroundImage);
        if (!string.IsNullOrEmpty(background)) { BackgroundImage = Own(SafeLoadImage(background)); BackgroundImageLayout = ImageLayout.Stretch; }
        ApplyTemplate();
        DpiChanged += (_, _) => BeginInvoke(() => ApplyTemplate(initial: false));
        UpdateProgress(new LauncherProgressState("启动核心已就绪，可进入游戏", string.Empty, 0, 0, 0, 0, 0));
        _progressTimer.Tick += (_, _) => PollProgress();
        _progressTimer.Start();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (_settings.AutoStart && !_autoStartTriggered)
        {
            _autoStartTriggered = true;
            BeginInvoke(async () => await LaunchSelectedAsync());
        }
    }

    private void BuildUi()
    {
        Controls.AddRange(new Control[] { _announcements, _serverDropdown, _serverSidebar, _launchButton, _overall, _current, _progressText, _sourceText });
        foreach (LauncherServer server in _loaded.Snapshot.Servers) _serverDropdown.Items.Add(server);
        _serverDropdown.DisplayMember = nameof(LauncherServer.Name);
        if (_serverDropdown.Items.Count > 0) _serverDropdown.SelectedIndex = 0;
        foreach (IGrouping<string, LauncherServer> group in _loaded.Snapshot.Servers.GroupBy(x => x.Group))
        {
            var node = new TreeNode(group.Key);
            foreach (LauncherServer server in group) node.Nodes.Add(new TreeNode($"{server.Name}  [{StatusText(server.Status)}]") { Tag = server });
            _serverSidebar.Nodes.Add(node);
            node.Expand();
        }
        if (_serverSidebar.Nodes.Count > 0 && _serverSidebar.Nodes[0].Nodes.Count > 0) _serverSidebar.SelectedNode = _serverSidebar.Nodes[0].Nodes[0];
        foreach (LauncherAnnouncement item in _loaded.Snapshot.Announcements.Take(12))
        {
            var card = new AnnouncementCard(item, _loaded.Root) { Dock = DockStyle.Top, Height = 78 };
            _announcements.Controls.Add(card);
            card.BringToFront();
        }
        var settings = CreateTopButton("游戏设置", 145);
        settings.Click += (_, _) => { using var dialog = new PlayerSettingsForm(_settings, _selectedClientDirectory); if (dialog.ShowDialog(this) == DialogResult.OK) { _settings = dialog.Value; _settingsDirty = true; } };
        var diagnose = CreateTopButton("连通诊断", 265);
        diagnose.Click += async (_, _) => await DiagnoseAsync();
        var chooseClient = CreateTopButton("更换客户端", 385);
        chooseClient.Click += (_, _) =>
        {
            string? selected = ClientSelection.SelectManually(this, _loaded.Snapshot.ProjectId);
            if (selected is null) return;
            _selectedClientDirectory = selected;
            _settings = ClientSettingsWriter.Read(selected, CloneSettings(_loaded.Snapshot.Defaults));
            _settingsDirty = false;
        };
        Controls.AddRange(new Control[] { settings, diagnose, chooseClient });
        _clickTargets.AddRange(new Control[] { _launchButton, settings, diagnose, chooseClient, _serverDropdown, _serverSidebar });
        _launchButton.Click += async (_, _) => await LaunchSelectedAsync();
        _sourceText.Text = "配置来源：" + (_loaded.Source switch { SnapshotSource.Remote => "有效远程版本", SnapshotSource.Cache => "上次有效快照", _ => "内置快照" });
    }

    private Button CreateTopButton(string text, int rightOffset) => new() { Text = text, FlatStyle = FlatStyle.Flat, Size = new Size(110, 34), Location = new Point(Width - rightOffset, 20), Anchor = AnchorStyles.Top | AnchorStyles.Right };

    private void ApplyTemplate(bool initial = true)
    {
        int S(int value) => (int)Math.Round(value * DeviceDpi / 96d);
        bool sidebar = _loaded.Snapshot.Theme.ServerListMode == ServerListMode.Sidebar;
        _serverSidebar.Visible = sidebar;
        _serverDropdown.Visible = !sidebar;
        switch (_loaded.Snapshot.Theme.Template)
        {
            case LauncherTemplateKind.Widescreen:
                if (initial) ClientSize = new Size(Math.Max(1100, _loaded.Snapshot.Theme.CanvasWidth), Math.Max(650, _loaded.Snapshot.Theme.CanvasHeight));
                _serverSidebar.SetBounds(S(18), S(86), S(250), Math.Max(S(180), ClientSize.Height - S(180)));
                _serverDropdown.SetBounds(S(18), S(86), S(250), S(34));
                _announcements.SetBounds(S(290), S(86), Math.Max(S(200), ClientSize.Width - S(310)), Math.Max(S(120), ClientSize.Height - S(240)));
                break;
            case LauncherTemplateKind.Compact:
                if (initial) ClientSize = new Size(760, 520);
                _serverDropdown.SetBounds(S(24), Math.Min(S(300), ClientSize.Height - S(180)), Math.Min(S(330), ClientSize.Width - S(48)), S(34));
                _serverSidebar.SetBounds(S(24), S(80), S(230), Math.Min(S(210), ClientSize.Height - S(220)));
                _announcements.SetBounds(S(24), S(70), Math.Max(S(200), ClientSize.Width - S(48)), Math.Min(S(205), ClientSize.Height - S(250)));
                break;
            default:
                _serverDropdown.SetBounds(S(30), Math.Min(S(350), ClientSize.Height - S(180)), Math.Min(S(360), ClientSize.Width - S(60)), S(34));
                _serverSidebar.SetBounds(S(30), S(80), S(245), Math.Min(S(255), ClientSize.Height - S(220)));
                _announcements.SetBounds(S(sidebar ? 295 : 30), S(80), Math.Max(S(200), sidebar ? ClientSize.Width - S(325) : ClientSize.Width - S(60)), Math.Min(S(245), ClientSize.Height - S(250)));
                break;
        }
        _launchButton.SetBounds(ClientSize.Width - S(220), ClientSize.Height - S(125), S(180), S(54));
        _overall.SetBounds(S(30), ClientSize.Height - S(68), ClientSize.Width - S(60), S(12));
        _current.SetBounds(S(30), ClientSize.Height - S(48), ClientSize.Width - S(60), S(8));
        _progressText.SetBounds(S(30), ClientSize.Height - S(92), Math.Max(S(120), ClientSize.Width - S(280)), S(22));
        _sourceText.Location = new Point(S(30), ClientSize.Height - S(28));
        Button[] topButtons = Controls.OfType<Button>().Where(button => button != _launchButton).ToArray();
        for (int i = 0; i < topButtons.Length; i++) topButtons[i].SetBounds(ClientSize.Width - S(145 + i * 120), S(20), S(110), S(34));
        string image = LauncherSnapshotValidator.ResolveAsset(_loaded.Root, _loaded.Snapshot.Theme.LaunchButtonImage);
        if (!string.IsNullOrEmpty(image)) _launchButton.BaseImage = SafeLoadImage(image);
    }

    private async Task LaunchSelectedAsync()
    {
        if (_launching) return;
        _launching = true;
        _launchButton.Enabled = false;
        try
        {
        LauncherServer? server = _loaded.Snapshot.Theme.ServerListMode == ServerListMode.Sidebar ? _serverSidebar.SelectedNode?.Tag as LauncherServer : _serverDropdown.SelectedItem as LauncherServer;
        if (server is null) { MessageBox.Show(this, "请先选择区服。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        if (server.Status == ServerOperatingStatus.Maintenance) { MessageBox.Show(this, "该区服由 GM 标记为维护中。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        MicroEndpoint micro = server.MicroOverride ?? _loaded.Snapshot.DefaultMicro;
        string? selectedClient = ClientSelection.Resolve(this, _loaded.Snapshot.ProjectId, _clientDirectory);
        if (selectedClient is null) return;
        if (!string.Equals(selectedClient, _selectedClientDirectory, StringComparison.OrdinalIgnoreCase))
        {
            _selectedClientDirectory = selectedClient;
            if (!_settingsDirty) _settings = ClientSettingsWriter.Read(selectedClient, CloneSettings(_loaded.Snapshot.Defaults));
        }
        LauncherProgressChannel.Clear(_loaded.Snapshot.ProjectId);
        ClientSettingsWriter.Write(selectedClient, _settings);
        ClientSettingsWriter.WriteMicroIdentity(selectedClient, _loaded.Snapshot.ProjectId, micro.User);
        _launch(selectedClient, server, micro, _settings);
        UpdateProgress(new LauncherProgressState("游戏已启动；普通资源继续按需下载", string.Empty, 0, 0, 0, 0, 0));
        await Task.Delay(1500);
        }
        finally
        {
            _launching = false;
            if (!IsDisposed) _launchButton.Enabled = true;
        }
    }

    private async Task DiagnoseAsync()
    {
        LauncherServer? server = _serverDropdown.SelectedItem as LauncherServer ?? _serverSidebar.SelectedNode?.Tag as LauncherServer;
        if (server is null) return;
        _progressText.Text = "正在执行三秒连通性诊断……";
        TimeSpan? elapsed = await ServerConnectivityDiagnostic.ProbeAsync(server.Address, server.Port, CancellationToken.None);
        _progressText.Text = elapsed is null ? "诊断结果：无法在三秒内建立 TCP 连接（不改变 GM 区服状态）" : $"诊断结果：连接成功，用时 {elapsed.Value.TotalMilliseconds:F0} ms（不代表在线人数）";
    }

    public void UpdateProgress(LauncherProgressState state)
    {
        _overall.Value = (int)Math.Round(state.OverallFraction * 100);
        _current.Value = (int)Math.Round(state.CurrentFraction * 100);
        _progressText.Text = state.Stage + (string.IsNullOrEmpty(state.CurrentFile) ? string.Empty : $" · {state.CurrentFile}") + (state.BytesPerSecond <= 0 ? string.Empty : $" · {FormatBytes((long)state.BytesPerSecond)}/s · 剩余 {FormatBytes(state.RemainingBytes)}");
    }

    private void PollProgress()
    {
        if (LauncherProgressChannel.TryRead(_loaded.Snapshot.ProjectId, out LauncherProgressSnapshot? snapshot) && snapshot is not null && DateTimeOffset.UtcNow - snapshot.UpdatedUtc < TimeSpan.FromMinutes(2)) UpdateProgress(snapshot.State);
    }

    internal LauncherDpiLayoutResult ValidateDpiMessage(int dpi)
    {
        const int WmDpiChanged = 0x02E0;
        Rectangle bounds = Bounds;
        int width = (int)Math.Round(bounds.Width * dpi / (double)Math.Max(1, DeviceDpi));
        int height = (int)Math.Round(bounds.Height * dpi / (double)Math.Max(1, DeviceDpi));
        var suggested = new NativeRect(bounds.Left, bounds.Top, bounds.Left + width, bounds.Top + height);
        nint memory = System.Runtime.InteropServices.Marshal.AllocHGlobal(System.Runtime.InteropServices.Marshal.SizeOf<NativeRect>());
        try
        {
            System.Runtime.InteropServices.Marshal.StructureToPtr(suggested, memory, false);
            nint packedDpi = (nint)(dpi | (dpi << 16));
            SendMessage(Handle, WmDpiChanged, packedDpi, memory);
            Application.DoEvents();
            PerformLayout();
            Application.DoEvents();
        }
        finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(memory); }

        Rectangle canvas = new(Point.Empty, ClientSize);
        bool sidebarMode = _loaded.Snapshot.Theme.ServerListMode == ServerListMode.Sidebar;
        Control hidden = sidebarMode ? _serverDropdown : _serverSidebar;
        Control[] active = Controls.Cast<Control>().Where(item => item.Visible && item != hidden).ToArray();
        Control[] outside = active.Where(control => !canvas.Contains(control.Bounds)).ToArray();
        bool inside = outside.Length == 0;
        var missed = new List<string>();
        bool hits = _clickTargets.Where(item => item.Visible && item != hidden).All(control =>
        {
            Point center = new(control.Left + control.Width / 2, control.Top + control.Height / 2);
            Control? hit = GetChildAtPoint(center, GetChildAtPointSkip.Invisible | GetChildAtPointSkip.Disabled | GetChildAtPointSkip.Transparent);
            if (hit != control) missed.Add($"{control.Text}/{control.GetType().Name}->{hit?.Text}/{hit?.GetType().Name}");
            return hit == control;
        });
        string details = string.Join("; ", outside.Select(item => $"越界:{item.Text}/{item.GetType().Name}={item.Bounds},画布={canvas}").Concat(missed));
        return new LauncherDpiLayoutResult(inside && DeviceDpi == dpi, hits, active.Length, DeviceDpi, details);
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private readonly record struct NativeRect(int Left, int Top, int Right, int Bottom);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern nint SendMessage(nint window, int message, nint wParam, nint lParam);

    private static Image SafeLoadImage(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using Image source = Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: true);
        if (source.Width > 4096 || source.Height > 4096) throw new InvalidDataException("主题图片像素尺寸超过 4096");
        return new Bitmap(source);
    }
    private T Own<T>(T image) where T : Image { _ownedImages.Add(image); return image; }
    protected override void Dispose(bool disposing) { if (disposing) { _progressTimer.Dispose(); foreach (Image image in _ownedImages) image.Dispose(); _ownedImages.Clear(); } base.Dispose(disposing); }
    private static LauncherPlayerSettings CloneSettings(LauncherPlayerSettings value) => new() { Resolution = value.Resolution, FullScreen = value.FullScreen, Borderless = value.Borderless, FpsCap = value.FpsCap, MaxFps = value.MaxFps, Volume = value.Volume, MusicVolume = value.MusicVolume, TopMost = value.TopMost, AutoStart = value.AutoStart, AdvancedLogs = value.AdvancedLogs, MicroCacheLimitMb = value.MicroCacheLimitMb };
    private static string StatusText(ServerOperatingStatus value) => value switch { ServerOperatingStatus.Busy => "火爆", ServerOperatingStatus.Maintenance => "维护", _ => "正常" };
    private static string FormatBytes(long value) => value >= 1024 * 1024 ? $"{value / 1024d / 1024d:F1} MiB" : value >= 1024 ? $"{value / 1024d:F1} KiB" : $"{value} B";
}
