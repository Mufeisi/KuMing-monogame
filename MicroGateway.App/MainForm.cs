namespace LyoCrystal.MicroGateway.App;

internal sealed class MainForm : Form
{
    private readonly TextBox _resourceRoot = new() { Dock = DockStyle.Fill };
    private readonly TextBox _launcherRoot = new() { Dock = DockStyle.Fill };
    private readonly TextBox _address = new() { Dock = DockStyle.Fill, Text = "127.0.0.1" };
    private readonly NumericUpDown _port = new() { Dock = DockStyle.Fill, Minimum = 1, Maximum = 65535, Value = 7000 };
    private readonly TextBox _user = new() { Dock = DockStyle.Fill, Text = "MicroUser" };
    private readonly TextBox _code = new() { Dock = DockStyle.Fill, UseSystemPasswordChar = true };
    private readonly TextBox _cacheRoot = new() { Dock = DockStyle.Fill };
    private readonly NumericUpDown _memoryCache = new() { Dock = DockStyle.Fill, Minimum = 16, Maximum = 1024, Value = 128 };
    private readonly NumericUpDown _diskCache = new() { Dock = DockStyle.Fill, Minimum = 128, Maximum = 32768, Value = 2048 };
    private readonly Button _start = new() { Text = "启动", AutoSize = true };
    private readonly Button _stop = new() { Text = "停止", AutoSize = true, Enabled = false };
    private readonly Button _network = new() { Text = "配置网络", AutoSize = true };
    private readonly Button _rollbackNetwork = new() { Text = "撤销网络", AutoSize = true };
    private readonly Button _installService = new() { Text = "安装并启动服务", AutoSize = true };
    private readonly Button _uninstallService = new() { Text = "卸载服务", AutoSize = true };
    private readonly Button _diagnose = new() { Text = "本机连通检测", AutoSize = true };
    private readonly Button _rescan = new() { Text = "立即重扫资源", AutoSize = true };
    private readonly Label _status = new() { AutoSize = true, Text = "未启动" };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };
    private readonly MicroHttpListenerHost _host = new();
    private readonly GatewayProjectConfiguration? _project;
    private GatewayRuntime? _runtime;
    private readonly NotifyIcon _tray = new() { Icon = SystemIcons.Application, Text = "LyoCrystal 独立微端网关" };
    private readonly List<Button> _pathButtons = new();
    private bool _serviceManaged;

    public MainForm(GatewayProjectConfiguration? project = null)
    {
        _project = project;
        Text = "LyoCrystal 独立微端网关";
        MinimumSize = new Size(680, 360);
        StartPosition = FormStartPosition.CenterScreen;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 3, RowCount = 12 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        AddPathRow(layout, 0, "完整客户端目录", _resourceRoot);
        AddPathRow(layout, 1, "启动器发布目录", _launcherRoot);
        AddRow(layout, 2, "监听 IP", _address);
        AddRow(layout, 3, "端口", _port);
        AddRow(layout, 4, "User", _user);
        AddRow(layout, 5, "Code", _code);
        AddPathRow(layout, 6, "缓存目录", _cacheRoot);
        AddRow(layout, 7, "内存缓存 MiB", _memoryCache);
        AddRow(layout, 8, "磁盘缓存 MiB", _diskCache);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        buttons.Controls.AddRange([_start, _stop]);
        layout.Controls.Add(buttons, 1, 9);
        var operations = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        operations.Controls.AddRange([_diagnose, _rescan, _network, _rollbackNetwork, _installService, _uninstallService]);
        layout.Controls.Add(operations, 1, 10);
        layout.SetColumnSpan(operations, 2);
        layout.Controls.Add(_status, 1, 11);
        Controls.Add(layout);
        if (project is not null)
        {
            _address.Text = project.ListenAddress;
            _port.Value = project.Port;
            _user.Text = project.User;
            _code.Text = Shared.Security.ProtectedClientSecretStore.ReadMicroCode(project.ProjectId);
            _resourceRoot.Text = project.ResolveOptionalDirectory(AppContext.BaseDirectory, project.ResourceDirectory);
            _launcherRoot.Text = project.ResolveOptionalDirectory(AppContext.BaseDirectory, project.LauncherDirectory);
            _cacheRoot.Text = project.ResolveOptionalDirectory(AppContext.BaseDirectory, project.CacheDirectory);
            _memoryCache.Value = Math.Clamp(project.MemoryCacheMb, (int)_memoryCache.Minimum, (int)_memoryCache.Maximum);
            _diskCache.Value = Math.Clamp(project.DiskCacheMb, (int)_diskCache.Minimum, (int)_diskCache.Maximum);
        }
        _start.Click += async (_, _) => await StartGatewayAsync();
        _stop.Click += async (_, _) => await StopGatewayAsync();
        _timer.Tick += (_, _) => RefreshStatus();
        _network.Click += (_, _) => RunElevated("--configure-network");
        _rollbackNetwork.Click += (_, _) => RunElevated("--rollback-network");
        _installService.Click += (_, _) => RunElevated("--install-service");
        _uninstallService.Click += (_, _) => RunElevated("--uninstall-service");
        _diagnose.Click += async (_, _) => await DiagnoseAsync();
        _rescan.Click += async (_, _) => await RescanAsync();
        _network.Enabled = _rollbackNetwork.Enabled = _installService.Enabled = _uninstallService.Enabled = project is not null;
        FormClosing += OnFormClosing;
        Resize += (_, _) =>
        {
            if (WindowState != FormWindowState.Minimized) return;
            Hide();
            _tray.Visible = true;
        };
        _tray.DoubleClick += (_, _) => RestoreFromTray();
        var trayMenu = new ContextMenuStrip();
        trayMenu.Items.Add("显示", null, (_, _) => RestoreFromTray());
        trayMenu.Items.Add("退出", null, (_, _) => Close());
        _tray.ContextMenuStrip = trayMenu;
        _timer.Start();
        RefreshStatus();
    }

    private static void AddRow(TableLayoutPanel layout, int row, string label, Control control)
    {
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
        layout.Controls.Add(control, 1, row);
        layout.SetColumnSpan(control, 2);
    }

    private void AddPathRow(TableLayoutPanel layout, int row, string label, TextBox box)
    {
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
        layout.Controls.Add(box, 1, row);
        var choose = new Button { Text = "选择…", AutoSize = true };
        _pathButtons.Add(choose);
        choose.Click += (_, _) => { using var dialog = new FolderBrowserDialog { SelectedPath = box.Text }; if (dialog.ShowDialog(this) == DialogResult.OK) box.Text = dialog.SelectedPath; };
        layout.Controls.Add(choose, 2, row);
    }

    private async Task StartGatewayAsync()
    {
        try
        {
            if (_project is not null && WindowsGatewayOperations.IsServiceInstalled(_project.ProjectId))
                throw new InvalidOperationException("Windows Service 已安装；请先卸载服务，再修改或启动 GUI 网关。");
            if (!Directory.Exists(_resourceRoot.Text.Trim()))
                throw new DirectoryNotFoundException("请选择已上传完整客户端的资源目录。");
            if (string.IsNullOrWhiteSpace(_user.Text))
                throw new InvalidOperationException("User 不能为空。");
            if (_project is not null)
            {
                _project.ListenAddress = _address.Text.Trim();
                _project.Port = (int)_port.Value;
                _project.User = _user.Text.Trim();
                _project.ResourceDirectory = _resourceRoot.Text.Trim();
                _project.LauncherDirectory = _launcherRoot.Text.Trim();
                _project.CacheDirectory = _cacheRoot.Text.Trim();
                _project.MemoryCacheMb = (int)_memoryCache.Value;
                _project.DiskCacheMb = (int)_diskCache.Value;
                _project.Save(AppContext.BaseDirectory);
                Shared.Security.ProtectedClientSecretStore.WriteMicroCode(_project.ProjectId, _code.Text);
                _runtime = new GatewayRuntime(AppContext.BaseDirectory, _project, serviceMode: false);
                await _runtime.StartAsync();
                SetEditing(false); _timer.Start(); RefreshStatus();
                return;
            }
            string? launcher = string.IsNullOrWhiteSpace(_launcherRoot.Text) ? null : _launcherRoot.Text.Trim();
            await _host.StartAsync($"http://{_address.Text.Trim()}:{_port.Value}/", new MicroGatewayOptions(
                _resourceRoot.Text.Trim(), _user.Text.Trim(), _code.Text, launcher));
            SetEditing(false); _timer.Start(); RefreshStatus();
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message + Environment.NewLine + "若提示拒绝访问，请以管理员身份运行，或为该地址配置 URLACL。", "启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task StopGatewayAsync()
    {
        if (_runtime is not null) { await _runtime.StopAsync(); await _runtime.DisposeAsync(); _runtime = null; }
        else await _host.StopAsync();
        SetEditing(true); RefreshStatus();
    }

    private void SetEditing(bool enabled)
    {
        _resourceRoot.Enabled = enabled;
        _launcherRoot.Enabled = enabled;
        _address.Enabled = enabled;
        _port.Enabled = enabled;
        _user.Enabled = enabled;
        _code.Enabled = enabled;
        _cacheRoot.Enabled = enabled;
        _memoryCache.Enabled = enabled;
        _diskCache.Enabled = enabled;
        foreach (Button button in _pathButtons) button.Enabled = enabled;
        _start.Enabled = enabled; _stop.Enabled = !enabled; _status.Enabled = true;
        if (_project is not null) { _installService.Enabled = enabled; _uninstallService.Enabled = enabled; }
    }

    private void RefreshStatus()
    {
        MicroGatewaySnapshot snapshot = _runtime?.GetSnapshot() ?? _host.GetSnapshot();
        if (!snapshot.IsRunning && _project is not null)
        {
            string? service = GatewayRuntime.TryReadServiceStatus(AppContext.BaseDirectory, _project.ProjectId);
            if (service is not null) { SetServiceManaged(true); _status.Text = service; return; }
            if (WindowsGatewayOperations.IsServiceInstalled(_project.ProjectId))
            {
                SetServiceManaged(true);
                _status.Text = "Windows Service 已安装但当前未运行；可点击“安装并启动服务”恢复运行，或先卸载再编辑。";
                return;
            }
            if (_serviceManaged) SetServiceManaged(false);
        }
        _status.Text = snapshot.IsRunning
            ? $"运行中｜请求 {snapshot.RequestCount}｜处理中 {snapshot.ActiveRequestCount}｜索引 {snapshot.IndexedFileCount} 个文件 / {snapshot.IndexedBytes / 1024 / 1024} MiB｜缓存命中 {snapshot.CacheHits}/{snapshot.CacheHits + snapshot.CacheMisses}"
            : "未启动";
        if (!string.IsNullOrWhiteSpace(snapshot.LastError)) _status.Text += $"｜最近错误：{snapshot.LastError}";
    }

    private bool _closeReady;

    private async void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_closeReady) return;
        e.Cancel = true;
        Enabled = false;
        _timer.Stop();
        if (_runtime is not null) { await _runtime.StopAsync(); await _runtime.DisposeAsync(); _runtime = null; }
        else await _host.StopAsync();
        _closeReady = true;
        _tray.Visible = false;
        _tray.Dispose();
        Close();
    }

    private void RunElevated(string operation)
    {
        try
        {
            int exitCode = WindowsGatewayOperations.RelaunchElevated(operation);
            MessageBox.Show(this, exitCode == 0 ? "操作完成。" : $"操作失败，退出码 {exitCode}。", "微端运维", MessageBoxButtons.OK,
                exitCode == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Error);
        }
        catch (Exception error) { MessageBox.Show(this, error.Message, "微端运维", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private async Task DiagnoseAsync()
    {
        _diagnose.Enabled = false;
        try
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            using var client = new HttpClient(new HttpClientHandler { UseProxy = false });
            string address = _address.Text.Trim() is "0.0.0.0" or "*" or "+" ? "127.0.0.1" : _address.Text.Trim();
            using HttpResponseMessage response = await client.GetAsync($"http://{address}:{_port.Value}/api/health", cancellation.Token);
            MessageBox.Show(this, response.IsSuccessStatusCode ? "网关本机连通正常。" : $"网关返回 HTTP {(int)response.StatusCode}。", "连通检测", MessageBoxButtons.OK,
                response.IsSuccessStatusCode ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException)
        {
            MessageBox.Show(this, "三秒内无法连接网关：" + error.Message, "连通检测", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally { _diagnose.Enabled = true; }
    }

    private async Task RescanAsync()
    {
        bool updated = _runtime is not null ? await _runtime.ReconcileResourcesAsync() : await _host.ReconcileResourcesAsync();
        if (!updated && _project is not null && GatewayRuntime.TryReadServiceStatus(AppContext.BaseDirectory, _project.ProjectId) is not null)
        {
            GatewayRuntime.RequestServiceRescan(AppContext.BaseDirectory, _project.ProjectId);
            MessageBox.Show(this, "已向 Windows Service 提交重扫请求。", "资源重扫", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        MessageBox.Show(this, updated ? "资源索引已原子更新。" : "网关当前未运行。", "资源重扫", MessageBoxButtons.OK,
            updated ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
    }

    private void RestoreFromTray()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
        _tray.Visible = false;
    }

    private void SetServiceManaged(bool managed)
    {
        _serviceManaged = managed;
        _resourceRoot.Enabled = _launcherRoot.Enabled = _address.Enabled = _port.Enabled = _user.Enabled = _code.Enabled =
            _cacheRoot.Enabled = _memoryCache.Enabled = _diskCache.Enabled = !managed;
        foreach (Button button in _pathButtons) button.Enabled = !managed;
        _start.Enabled = !managed;
        _stop.Enabled = false;
        _installService.Enabled = _uninstallService.Enabled = _project is not null;
    }
}
