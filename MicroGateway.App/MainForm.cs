namespace LyoCrystal.MicroGateway.App;

internal sealed class MainForm : Form
{
    private readonly TextBox _resourceRoot = new() { Dock = DockStyle.Fill };
    private readonly TextBox _launcherRoot = new() { Dock = DockStyle.Fill };
    private readonly TextBox _address = new() { Dock = DockStyle.Fill, Text = "127.0.0.1" };
    private readonly NumericUpDown _port = new() { Dock = DockStyle.Fill, Minimum = 1, Maximum = 65535, Value = 7000 };
    private readonly TextBox _user = new() { Dock = DockStyle.Fill, Text = "MicroUser" };
    private readonly TextBox _code = new() { Dock = DockStyle.Fill, UseSystemPasswordChar = true };
    private readonly Button _start = new() { Text = "启动", AutoSize = true };
    private readonly Button _stop = new() { Text = "停止", AutoSize = true, Enabled = false };
    private readonly Label _status = new() { AutoSize = true, Text = "未启动" };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };
    private readonly MicroHttpListenerHost _host = new();
    private readonly GatewayProjectConfiguration? _project;

    public MainForm(GatewayProjectConfiguration? project = null)
    {
        _project = project;
        Text = "LyoCrystal 独立微端网关";
        MinimumSize = new Size(680, 360);
        StartPosition = FormStartPosition.CenterScreen;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 3, RowCount = 8 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        AddPathRow(layout, 0, "完整客户端目录", _resourceRoot);
        AddPathRow(layout, 1, "启动器发布目录", _launcherRoot);
        AddRow(layout, 2, "监听 IP", _address);
        AddRow(layout, 3, "端口", _port);
        AddRow(layout, 4, "User", _user);
        AddRow(layout, 5, "Code", _code);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        buttons.Controls.AddRange([_start, _stop]);
        layout.Controls.Add(buttons, 1, 6);
        layout.Controls.Add(_status, 1, 7);
        Controls.Add(layout);
        if (project is not null)
        {
            _address.Text = project.ListenAddress;
            _port.Value = project.Port;
            _user.Text = project.User;
            _code.Text = Shared.Security.ProtectedClientSecretStore.ReadMicroCode(project.ProjectId);
            _resourceRoot.Text = project.ResolveOptionalDirectory(AppContext.BaseDirectory, project.ResourceDirectory);
            _launcherRoot.Text = project.ResolveOptionalDirectory(AppContext.BaseDirectory, project.LauncherDirectory);
        }
        _start.Click += async (_, _) => await StartGatewayAsync();
        _stop.Click += async (_, _) => await StopGatewayAsync();
        _timer.Tick += (_, _) => RefreshStatus();
        FormClosing += OnFormClosing;
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
        choose.Click += (_, _) => { using var dialog = new FolderBrowserDialog { SelectedPath = box.Text }; if (dialog.ShowDialog(this) == DialogResult.OK) box.Text = dialog.SelectedPath; };
        layout.Controls.Add(choose, 2, row);
    }

    private async Task StartGatewayAsync()
    {
        try
        {
            if (!Directory.Exists(_resourceRoot.Text.Trim()))
                throw new DirectoryNotFoundException("请选择已上传完整客户端的资源目录。");
            if (string.IsNullOrWhiteSpace(_user.Text))
                throw new InvalidOperationException("User 不能为空。");
            if (_project is not null) Shared.Security.ProtectedClientSecretStore.WriteMicroCode(_project.ProjectId, _code.Text);
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

    private async Task StopGatewayAsync() { await _host.StopAsync(); _timer.Stop(); SetEditing(true); RefreshStatus(); }

    private void SetEditing(bool enabled)
    {
        _resourceRoot.Enabled = enabled;
        _launcherRoot.Enabled = enabled;
        _address.Enabled = enabled;
        _port.Enabled = enabled;
        _user.Enabled = enabled;
        _code.Enabled = enabled;
        _start.Enabled = enabled; _stop.Enabled = !enabled; _status.Enabled = true;
    }

    private void RefreshStatus()
    {
        MicroGatewaySnapshot snapshot = _host.GetSnapshot();
        _status.Text = snapshot.IsRunning
            ? $"运行中｜请求 {snapshot.RequestCount}｜处理中 {snapshot.ActiveRequestCount}｜资源 {snapshot.ResourceRoot}"
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
        await _host.StopAsync();
        _closeReady = true;
        Close();
    }
}
