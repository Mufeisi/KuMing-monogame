using Launcher.ThemeRuntime;

namespace LyoCrystal.LauncherEditor;

internal sealed class NewProjectWizard : Form
{
    private readonly TextBox _id = new() { Text = "new-project" };
    private readonly TextBox _name = new() { Text = "新传奇启动器" };
    private readonly TextBox _company = new();
    private readonly TextBox _client = new();
    private readonly TextBox _release = new();
    private readonly TextBox _gameAddress = new() { Text = "127.0.0.1" };
    private readonly NumericUpDown _gamePort = new() { Minimum = 1, Maximum = 65535, Value = 7000 };
    private readonly TextBox _microAddress = new() { Text = "127.0.0.1" };
    private readonly NumericUpDown _microPort = new() { Minimum = 1, Maximum = 65535, Value = 8080 };
    private readonly TextBox _backupMicroAddress = new();
    private readonly NumericUpDown _backupMicroPort = new() { Minimum = 0, Maximum = 65535 };
    private readonly ComboBox _resolution = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CheckBox _fullScreen = new() { Text = "默认全屏" };
    private readonly TextBox _announcementTitle = new() { Text = "欢迎公告" };
    private readonly TextBox _announcementSummary = new() { Text = "欢迎进入游戏。" };
    private readonly ComboBox _updateMode = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _cacheDirectory = new() { Text = "Cache" };
    private readonly NumericUpDown _memoryCache = new() { Minimum = 16, Maximum = 1024, Value = 128 };
    private readonly NumericUpDown _diskCache = new() { Minimum = 128, Maximum = 32768, Value = 2048 };
    private readonly ComboBox _template = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _delivery = new() { DropDownStyle = ComboBoxStyle.DropDownList };

    public EditorProjectCreationOptions Options => new()
    {
        ProjectId = _id.Text.Trim(), ProjectName = _name.Text.Trim(), CompanyName = _company.Text.Trim(), ImportedClientDirectory = _client.Text.Trim(),
        RemoteReleaseBaseUrl = _release.Text.Trim(), ServerAddress = _gameAddress.Text.Trim(), ServerPort = (int)_gamePort.Value,
        MicroAddress = _microAddress.Text.Trim(), MicroPort = (int)_microPort.Value,
        BackupMicroAddress = _backupMicroAddress.Text.Trim(), BackupMicroPort = (int)_backupMicroPort.Value,
        Resolution = _resolution.SelectedItem is int resolution ? resolution : 1024, FullScreen = _fullScreen.Checked,
        AnnouncementTitle = _announcementTitle.Text.Trim(), AnnouncementSummary = _announcementSummary.Text.Trim(),
        PlayerUpdateMode = _updateMode.SelectedItem is PlayerUpdateMode updateMode ? updateMode : PlayerUpdateMode.None,
        GatewayCacheDirectory = _cacheDirectory.Text.Trim(), GatewayMemoryCacheMb = (int)_memoryCache.Value, GatewayDiskCacheMb = (int)_diskCache.Value,
        Template = _template.SelectedItem is LauncherTemplateKind template ? template : LauncherTemplateKind.Classic,
        DeliveryMode = _delivery.SelectedItem is ClientDeliveryMode delivery ? delivery : ClientDeliveryMode.MicroOnDemand,
    };

    public NewProjectWizard()
    {
        Text = "新建启动器项目（可随时跳过）"; ClientSize = new Size(680, 560); StartPosition = FormStartPosition.CenterParent; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false;
        _template.Items.AddRange(Enum.GetValues<LauncherTemplateKind>().Cast<object>().ToArray()); _template.SelectedIndex = 0;
        _delivery.Items.AddRange(Enum.GetValues<ClientDeliveryMode>().Cast<object>().ToArray()); _delivery.SelectedIndex = 0;
        _resolution.Items.AddRange(new object[] { 1024, 1280, 1366, 1920 }); _resolution.SelectedIndex = 0;
        _updateMode.Items.AddRange(Enum.GetValues<PlayerUpdateMode>().Cast<object>().ToArray()); _updateMode.SelectedIndex = 0;
        var pages = new TabControl { Dock = DockStyle.Fill };
        pages.TabPages.Add(Page("项目与品牌", ("项目标识", _id), ("项目名称", _name), ("公司名称", _company), ("主题模板", _template)));
        var browse = new Button { Text = "选择客户端目录…", AutoSize = true }; browse.Click += (_, _) => { using var folder = new FolderBrowserDialog(); if (folder.ShowDialog(this) == DialogResult.OK) _client.Text = folder.SelectedPath; };
        pages.TabPages.Add(Page("客户端与交付", ("客户端目录（可空）", _client), ("", browse), ("交付模式", _delivery)));
        pages.TabPages.Add(Page("游戏与微端", ("游戏地址", _gameAddress), ("游戏端口", _gamePort), ("微端地址", _microAddress), ("微端端口", _microPort), ("备用微端地址", _backupMicroAddress), ("备用微端端口", _backupMicroPort)));
        pages.TabPages.Add(Page("玩家与公告", ("默认分辨率", _resolution), ("窗口模式", _fullScreen), ("首篇公告标题", _announcementTitle), ("首篇公告摘要", _announcementSummary)));
        pages.TabPages.Add(Page("发布与缓存", ("远程发布地址（可空）", _release), ("入口更新策略", _updateMode), ("网关缓存目录", _cacheDirectory), ("内存缓存 MiB", _memoryCache), ("磁盘缓存 MiB", _diskCache), ("说明", new Label { Text = "项目密钥和恢复包将在项目创建后由编辑器生成；高级公告、动作和主题可在项目标签页继续维护。", AutoSize = true })));
        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Bottom, Height = 52, Padding = new Padding(8) };
        var finish = new Button { Text = "完成", DialogResult = DialogResult.OK, Width = 90 };
        var skip = new Button { Text = "跳过向导", Width = 100 }; skip.Click += (_, _) => { _id.Text = "project-" + DateTime.Now.ToString("yyyyMMddHHmmss"); _name.Text = "未命名启动器"; DialogResult = DialogResult.OK; };
        buttons.Controls.AddRange(new Control[] { finish, skip, new Button { Text = "取消", DialogResult = DialogResult.Cancel, Width = 80 } });
        Controls.Add(pages); Controls.Add(buttons); AcceptButton = finish;
    }

    private static TabPage Page(string title, params (string Label, Control Control)[] rows)
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 2, AutoScroll = true };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < rows.Length; i++)
        {
            Control control = rows[i].Control; control.Anchor = AnchorStyles.Left | AnchorStyles.Right; control.Margin = new Padding(4, 10, 4, 10);
            layout.Controls.Add(new Label { Text = rows[i].Label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, i); layout.Controls.Add(control, 1, i);
        }
        return new TabPage(title) { Controls = { layout } };
    }
}
