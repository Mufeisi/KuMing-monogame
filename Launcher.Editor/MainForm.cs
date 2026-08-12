using System.ComponentModel;
using Launcher.PlayerShell;
using Launcher.ThemeRuntime;
using Shared.Security;
using System.Security.Cryptography;
using System.Diagnostics;

namespace LyoCrystal.LauncherEditor;

internal sealed class MainForm : Form
{
    private readonly EditorProjectStore _store;
    private readonly ListBox _projects = new() { Dock = DockStyle.Fill };
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
    private readonly PictureBox _preview = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(28, 28, 32) };
    private readonly ToolStripStatusLabel _status = new() { Text = "就绪" };
    private readonly ToolStrip _tools = new() { GripStyle = ToolStripGripStyle.Hidden };
    private EditorProject? _project;
    private BindingList<LauncherServer>? _servers;
    private BindingList<LauncherAnnouncement>? _announcements;
    private BindingList<LauncherControlOverride>? _controlOverrides;
    private BindingList<LauncherActionLink>? _actionLinks;
    private float _previewScale = 1f;
    private bool _advancedVisible;
    private QuickProductionPanel? _quickPanel;
    private string _lastQuickOutput = string.Empty;
    private CancellationTokenSource? _quickGenerationCancellation;
    private bool _closeAfterQuickGeneration;
    private bool _quickGenerationRunning;

    public MainForm(EditorProjectStore store)
    {
        _store = store;
        Text = "传奇启动器配置器";
        WindowState = FormWindowState.Maximized;
        MinimumSize = new Size(1100, 700);
        BuildUi();
        EnsureFirstProject();
        ReloadProjects();
    }

    private void BuildUi()
    {
        ToolStrip tools = _tools;
        AddTool(tools, "新建启动器", NewProject);
        AddTool(tools, "选择客户端资源", SelectQuickResource);
        AddTool(tools, "一键生成全部成品", GenerateAllQuick);
        AddTool(tools, "快速制作首页", () => { if (_tabs.TabPages.Count > 0) _tabs.SelectedIndex = 0; });
        var advanced = new ToolStripDropDownButton("高级工具");
        AddAdvanced(advanced, "显示高级设置", ShowAdvanced);
        AddAdvanced(advanced, "保存项目", SaveProject);
        AddAdvanced(advanced, "导入旧客户端配置", ImportClient);
        AddAdvanced(advanced, "导入主题图片", ImportThemeImage);
        AddAdvanced(advanced, "导入主题包", ImportThemePackage);
        AddAdvanced(advanced, "导出主题包", ExportThemePackage);
        AddAdvanced(advanced, "刷新界面预览", RefreshPreview);
        AddAdvanced(advanced, "发布前检查", ValidateBeforeGeneration);
        AddAdvanced(advanced, "单独生成玩家启动器", GeneratePlayerExecutable);
        AddAdvanced(advanced, "生成完整客户端包", GenerateFullClientPackage);
        AddAdvanced(advanced, "单独生成微端部署包", GenerateGatewayPackage);
        AddAdvanced(advanced, "发布新版本", PublishRelease);
        AddAdvanced(advanced, "回滚历史版本", RollbackRelease);
        AddAdvanced(advanced, "导出离线发布包", ExportOfflineRelease);
        AddAdvanced(advanced, "导入离线发布包", ImportOfflineRelease);
        AddAdvanced(advanced, "导出密钥恢复包", ExportRecoveryPackage);
        AddAdvanced(advanced, "导入密钥恢复包", ImportRecoveryPackage);
        AddAdvanced(advanced, "轮换签名密钥", RotateReleaseKey);
        tools.Items.Add(advanced);
        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 220, FixedPanel = FixedPanel.Panel1 };
        split.Panel1.Controls.Add(_projects); split.Panel2.Controls.Add(_tabs);
        _projects.SelectedIndexChanged += (_, _) => LoadSelectedProject();
        Controls.Add(split); Controls.Add(tools); tools.Dock = DockStyle.Top;
        Controls.Add(new StatusStrip { Items = { _status } });
    }

    private static void AddTool(ToolStrip strip, string text, Action action)
    {
        var button = new ToolStripButton(text); button.Click += (_, _) => action(); strip.Items.Add(button);
    }

    private static void AddAdvanced(ToolStripDropDownButton parent, string text, Action action)
    {
        var item = new ToolStripMenuItem(text); item.Click += (_, _) => action(); parent.DropDownItems.Add(item);
    }

    private void ReloadProjects(string? select = null)
    {
        _projects.Items.Clear();
        foreach (string id in _store.ListProjectIds())
        {
            try
            {
                EditorProject project = _store.Load(id);
                _projects.Items.Add(new ProjectListItem(id, project.Snapshot.ProjectName));
            }
            catch { _projects.Items.Add(new ProjectListItem(id, "无法读取的旧项目")); }
        }
        if (select is not null) _projects.SelectedItem = _projects.Items.Cast<ProjectListItem>().FirstOrDefault(item => item.Id == select);
        else if (_projects.Items.Count > 0) _projects.SelectedIndex = 0;
    }

    private void NewProject()
    {
        using var wizard = new NewProjectWizard();
        if (wizard.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            EditorProject project = _store.Create(wizard.Options);
            if (!string.IsNullOrWhiteSpace(wizard.Options.ImportedClientDirectory))
            {
                _store.ImportClientReadOnly(project, wizard.Options.ImportedClientDirectory);
                project.Gateway.ResourceDirectory = wizard.Options.ImportedClientDirectory;
                project.Snapshot.Servers[0].Address = wizard.Options.ServerAddress;
                project.Snapshot.Servers[0].Port = wizard.Options.ServerPort;
                project.Snapshot.DefaultMicro.Address = wizard.Options.MicroAddress;
                project.Snapshot.DefaultMicro.Port = wizard.Options.MicroPort;
                project.Gateway.Port = wizard.Options.MicroPort;
                _store.Save(project);
            }
            ReloadProjects(project.Snapshot.ProjectId);
            SetStatus("启动器已创建；确认服务器地址后即可一键生成");
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void LoadSelectedProject()
    {
        if (_projects.SelectedItem is not ProjectListItem selected) return;
        string id = selected.Id;
        try { _project = _store.Load(id); RebuildTabs(); RefreshPreview(); SetStatus("已加载启动器：" + _project.Snapshot.ProjectName); }
        catch (Exception ex) { ShowError(ex); }
    }

    private void RebuildTabs()
    {
        _preview.Parent?.Controls.Remove(_preview);
        TabPage[] previousPages = _tabs.TabPages.Cast<TabPage>().ToArray();
        _tabs.TabPages.Clear();
        foreach (TabPage page in previousPages) page.Dispose();
        if (_project is null) return;
        _servers = new BindingList<LauncherServer>(_project.Snapshot.Servers);
        _announcements = new BindingList<LauncherAnnouncement>(_project.Snapshot.Announcements);
        _controlOverrides = new BindingList<LauncherControlOverride>(_project.Snapshot.Theme.Controls);
        _actionLinks = new BindingList<LauncherActionLink>(_project.Snapshot.ActionLinks);

        _quickPanel = new QuickProductionPanel(_project, SelectQuickResource, () => ImportQuickImage(ThemeImageUsage.Background), () => ImportQuickImage(ThemeImageUsage.ButtonBase), GenerateAllQuick, ShowAdvanced);
        _quickPanel.SetResult(_lastQuickOutput);
        _tabs.TabPages.Add(new TabPage("快速制作") { Controls = { _quickPanel } });
        if (!_advancedVisible) return;
        AddPropertyTab("项目与品牌", new ProjectBrandPropertyView(_project));
        AddPropertyTab("主题", new ThemePropertyView(_project));
        _tabs.TabPages.Add(CreateControlLayoutTab());
        _tabs.TabPages.Add(CreateServerTab());
        _tabs.TabPages.Add(CreateAnnouncementTab());
        _tabs.TabPages.Add(CreateActionLinksTab());
        _tabs.TabPages.Add(new TabPage("玩家设置") { Controls = { new SettingsEditorPanel(_project.Snapshot.Defaults) } });
        AddPropertyTab("项目默认微端", new DefaultMicroPropertyView(_project.Snapshot.DefaultMicro));
        AddPropertyTab("微端部署", new GatewayPropertyView(_project.Gateway));
        AddPropertyTab("签名与发布", new ReleasePropertyView(_project.Release));
        _tabs.TabPages.Add(CreatePreviewTab());
    }

    private void AddPropertyTab(string name, object value)
    {
        var grid = new PropertyGrid { Dock = DockStyle.Fill, SelectedObject = value, HelpVisible = true, ToolbarVisible = true };
        grid.PropertyValueChanged += (_, _) => { SyncLists(); RefreshPreview(); };
        _tabs.TabPages.Add(new TabPage(name) { Controls = { grid } });
    }

    private TabPage CreateServerTab()
    {
        var grid = new DataGridView { Dock = DockStyle.Fill, AutoGenerateColumns = false, DataSource = _servers, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, AllowUserToAddRows = false };
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "分组", DataPropertyName = nameof(LauncherServer.Group) });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "区服名称", DataPropertyName = nameof(LauncherServer.Name) });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "排序", DataPropertyName = nameof(LauncherServer.SortOrder), FillWeight = 55 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "游戏地址", DataPropertyName = nameof(LauncherServer.Address) });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "游戏端口", DataPropertyName = nameof(LauncherServer.Port) });
        grid.Columns.Add(new DataGridViewComboBoxColumn { HeaderText = "运营状态", DataPropertyName = nameof(LauncherServer.Status), DataSource = EditorChineseText.Choices(Enum.GetValues<ServerOperatingStatus>(), EditorChineseText.ServerStatus), DisplayMember = nameof(ChineseChoice<ServerOperatingStatus>.Text), ValueMember = nameof(ChineseChoice<ServerOperatingStatus>.Value) });
        grid.CellValueChanged += (_, _) => RefreshPreview();
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42 };
        var add = new Button { Text = "新增区服", AutoSize = true }; add.Click += (_, _) => { _servers!.Add(new LauncherServer { Id = "server-" + (_servers.Count + 1), Name = "新区服" }); RefreshPreview(); };
        var remove = new Button { Text = "删除区服", AutoSize = true }; remove.Click += (_, _) => { if (grid.CurrentRow?.DataBoundItem is LauncherServer server && _servers!.Count > 1) _servers.Remove(server); RefreshPreview(); };
        var micro = new Button { Text = "区服微端覆盖…", AutoSize = true }; micro.Click += (_, _) => { if (grid.CurrentRow?.DataBoundItem is LauncherServer server) { using var dialog = new ServerMicroOverrideDialog(server.MicroOverride, _project!.Snapshot.DefaultMicro.User); if (dialog.ShowDialog(this) == DialogResult.OK) server.MicroOverride = dialog.Value; } };
        bar.Controls.AddRange(new Control[] { add, remove, micro });
        var page = new TabPage("区服与分组"); page.Controls.Add(grid); page.Controls.Add(bar); return page;
    }

    private TabPage CreateAnnouncementTab()
    {
        var grid = new DataGridView { Dock = DockStyle.Fill, AutoGenerateColumns = false, DataSource = _announcements, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, AllowUserToAddRows = false };
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "标题", DataPropertyName = nameof(LauncherAnnouncement.Title) });
        grid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "置顶", DataPropertyName = nameof(LauncherAnnouncement.Pinned), FillWeight = 45 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "摘要", DataPropertyName = nameof(LauncherAnnouncement.Summary), FillWeight = 180 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "发布日期", DataPropertyName = nameof(LauncherAnnouncement.Date) });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "图片", DataPropertyName = nameof(LauncherAnnouncement.Image) });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "外部链接", DataPropertyName = nameof(LauncherAnnouncement.ExternalUrl) });
        grid.CellValueChanged += (_, _) => RefreshPreview();
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42 };
        var add = new Button { Text = "新增公告", AutoSize = true }; add.Click += (_, _) => { _announcements!.Add(new LauncherAnnouncement { Title = "新公告", Date = DateTime.Today.ToString("yyyy-MM-dd") }); RefreshPreview(); };
        var remove = new Button { Text = "删除公告", AutoSize = true }; remove.Click += (_, _) => { if (grid.CurrentRow?.DataBoundItem is LauncherAnnouncement item) _announcements!.Remove(item); RefreshPreview(); };
        bar.Controls.AddRange(new Control[] { add, remove });
        var page = new TabPage("公告"); page.Controls.Add(grid); page.Controls.Add(bar); return page;
    }

    private TabPage CreateActionLinksTab()
    {
        var grid = new DataGridView { Dock = DockStyle.Fill, AutoGenerateColumns = false, DataSource = _actionLinks, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, AllowUserToAddRows = false };
        grid.Columns.Add(new DataGridViewComboBoxColumn { HeaderText = "安全动作", DataPropertyName = nameof(LauncherActionLink.Action), DataSource = EditorChineseText.Choices(Enum.GetValues<LauncherAction>().Where(LauncherActionDispatcher.IsWebAction), EditorChineseText.Action), DisplayMember = nameof(ChineseChoice<LauncherAction>.Text), ValueMember = nameof(ChineseChoice<LauncherAction>.Value) });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "显示文字", DataPropertyName = nameof(LauncherActionLink.Text) });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "网页地址", DataPropertyName = nameof(LauncherActionLink.Url), FillWeight = 180 });
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42 };
        var add = new Button { Text = "新增安全链接", AutoSize = true };
        add.Click += (_, _) => _actionLinks!.Add(new LauncherActionLink { Action = LauncherAction.OfficialWebsite, Text = "官方网站", Url = "https://example.com/" });
        var remove = new Button { Text = "删除", AutoSize = true };
        remove.Click += (_, _) => { if (grid.CurrentRow?.DataBoundItem is LauncherActionLink item) _actionLinks!.Remove(item); };
        bar.Controls.AddRange(new Control[] { add, remove, new Label { Text = "仅允许白名单网页动作，不允许脚本、程序或命令行。", AutoSize = true, Padding = new Padding(12, 8, 0, 0) } });
        var page = new TabPage("安全动作"); page.Controls.Add(grid); page.Controls.Add(bar); return page;
    }

    private TabPage CreateControlLayoutTab()
    {
        var grid = new DataGridView { Dock = DockStyle.Fill, AutoGenerateColumns = false, DataSource = _controlOverrides, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, AllowUserToAddRows = false };
        grid.Columns.Add(new DataGridViewComboBoxColumn { HeaderText = "控件", DataPropertyName = nameof(LauncherControlOverride.Id), DataSource = EditorChineseText.Choices(Enum.GetValues<LauncherControlId>(), EditorChineseText.Control), DisplayMember = nameof(ChineseChoice<LauncherControlId>.Text), ValueMember = nameof(ChineseChoice<LauncherControlId>.Value) });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "横向位置", DataPropertyName = nameof(LauncherControlOverride.X) });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "纵向位置", DataPropertyName = nameof(LauncherControlOverride.Y) });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "宽度", DataPropertyName = nameof(LauncherControlOverride.Width) });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "高度", DataPropertyName = nameof(LauncherControlOverride.Height) });
        grid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "显示", DataPropertyName = nameof(LauncherControlOverride.Visible) });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "文字颜色", DataPropertyName = nameof(LauncherControlOverride.ForeColor) });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "背景颜色", DataPropertyName = nameof(LauncherControlOverride.BackColor) });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "字体", DataPropertyName = nameof(LauncherControlOverride.FontName) });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "字号", DataPropertyName = nameof(LauncherControlOverride.FontSize) });
        grid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "粗体", DataPropertyName = nameof(LauncherControlOverride.Bold) });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "不透明度（百分比）", DataPropertyName = nameof(LauncherControlOverride.OpacityPercent) });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "背景图片", DataPropertyName = nameof(LauncherControlOverride.BackgroundImage) });
        grid.CellValueChanged += (_, _) => RefreshPreview();
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42 };
        var add = new Button { Text = "添加控件覆盖", AutoSize = true };
        add.Click += (_, _) =>
        {
            LauncherControlId? id = Enum.GetValues<LauncherControlId>().Cast<LauncherControlId?>().FirstOrDefault(candidate => candidate.HasValue && !_controlOverrides!.Any(item => item.Id == candidate.Value));
            if (!id.HasValue) return;
            _controlOverrides!.Add(new LauncherControlOverride { Id = id.Value, X = 20, Y = 20, Width = 180, Height = 40 }); RefreshPreview();
        };
        var remove = new Button { Text = "恢复模板默认", AutoSize = true }; remove.Click += (_, _) => { if (grid.CurrentRow?.DataBoundItem is LauncherControlOverride item) _controlOverrides!.Remove(item); RefreshPreview(); };
        bar.Controls.AddRange(new Control[] { add, remove, new Label { Text = "只允许固定控件；留空表示使用模板位置与样式。", AutoSize = true, Padding = new Padding(12, 8, 0, 0) } });
        var page = new TabPage("控件布局"); page.Controls.Add(grid); page.Controls.Add(bar); return page;
    }

    private TabPage CreatePreviewTab()
    {
        var scale = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
        scale.Items.AddRange(new object[] { "100%", "125%", "150%", "200%" }); scale.SelectedIndex = _previewScale switch { 1.25f => 1, 1.5f => 2, 2f => 3, _ => 0 };
        scale.SelectedIndexChanged += (_, _) => { _previewScale = scale.SelectedIndex switch { 1 => 1.25f, 2 => 1.5f, 3 => 2f, _ => 1f }; RefreshPreview(); };
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42 };
        bar.Controls.AddRange(new Control[] { new Label { Text = "界面缩放预览", AutoSize = true, Padding = new Padding(0, 8, 0, 0) }, scale, new Label { Text = "这里看到的效果与玩家启动器一致", AutoSize = true, Padding = new Padding(10, 8, 0, 0) } });
        var page = new TabPage("实时预览"); page.Controls.Add(_preview); page.Controls.Add(bar); return page;
    }

    private void SyncLists()
    {
        if (_project is null) return;
        QuickProductionPanel.ApplyLauncherName(_project, _project.Snapshot.ProjectName);
        if (_servers is not null) _project.Snapshot.Servers = _servers.ToList();
        if (_announcements is not null) _project.Snapshot.Announcements = _announcements.ToList();
        if (_controlOverrides is not null) _project.Snapshot.Theme.Controls = _controlOverrides.ToList();
        if (_actionLinks is not null) _project.Snapshot.ActionLinks = _actionLinks.ToList();
    }

    private void SaveProject()
    {
        if (_project is null) return;
        try { SyncLists(); _store.Save(_project); SetStatus("项目已原子保存"); }
        catch (Exception ex) { ShowError(ex); }
    }

    private void ImportClient()
    {
        if (_project is null) { MessageBox.Show(this, "请先新建或选择项目。", Text); return; }
        using var dialog = new FolderBrowserDialog { Description = "只读导入现有客户端目录", ShowNewFolderButton = false, UseDescriptionForTitle = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            ImportPreview result = _store.ImportClientReadOnly(_project, dialog.SelectedPath);
            RebuildTabs(); RefreshPreview();
            MessageBox.Show(this, $"已读取 {result.MappedFields.Count} 项可用配置；未识别 {result.UnknownFields.Count} 项。\r\n敏感值已忽略：{(result.SensitiveValuesOmitted ? "是" : "未发现")}\r\n原客户端未被修改。", "导入完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void EnsureFirstProject()
    {
        if (_store.ListProjectIds().Count > 0) return;
        string id = "project-" + DateTime.Now.ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid().ToString("N")[..4];
        _store.Create(id, "新传奇启动器", LauncherTemplateKind.Classic);
    }

    private void ShowAdvanced()
    {
        if (_project is null) { MessageBox.Show(this, "请先新建或选择启动器项目。", Text); return; }
        if (!_advancedVisible) { _advancedVisible = true; RebuildTabs(); }
        if (_tabs.TabPages.Count > 1) _tabs.SelectedIndex = 1;
    }

    private void SelectQuickResource()
    {
        if (_project is null) { MessageBox.Show(this, "请先新建启动器。", Text); return; }
        using var dialog = new FolderBrowserDialog { Description = "选择包含 Client.exe 的完整客户端目录", ShowNewFolderButton = false, UseDescriptionForTitle = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            ImportPreview result = _store.ImportClientReadOnly(_project, dialog.SelectedPath);
            _project.Gateway.ResourceDirectory = dialog.SelectedPath;
            _store.Save(_project);
            RebuildTabs(); RefreshPreview();
            SetStatus($"客户端资源已选择，自动读取 {result.MappedFields.Count} 项配置，原目录未被修改");
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void ImportQuickImage(ThemeImageUsage usage)
    {
        if (_project is null) return;
        using var dialog = new OpenFileDialog { Title = usage == ThemeImageUsage.Background ? "选择启动器背景图" : "选择进入游戏按钮图", Filter = "图片文件 (*.png;*.bmp;*.jpg;*.jpeg)|*.png;*.bmp;*.jpg;*.jpeg" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            string relative = ThemeAssetImporter.Import(_store.GetProjectDirectory(_project.Snapshot.ProjectId), dialog.FileName, _project.OptimizeImportedImages);
            if (usage == ThemeImageUsage.Background) _project.Snapshot.Theme.BackgroundImage = relative;
            else _project.Snapshot.Theme.LaunchButtonImage = relative;
            _store.Save(_project); RebuildTabs(); RefreshPreview(); SetStatus("图片已导入并自动应用");
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private async void GenerateAllQuick()
    {
        if (_quickGenerationRunning) { SetStatus("成品正在生成，请勿重复操作"); return; }
        if (_project is null) { MessageBox.Show(this, "请先新建启动器。", Text); return; }
        if (string.IsNullOrWhiteSpace(_project.ImportedClientDirectory) || !Directory.Exists(_project.ImportedClientDirectory))
        {
            MessageBox.Show(this, "请先完成第一步：选择完整客户端资源目录。", "还差一步", MessageBoxButtons.OK, MessageBoxIcon.Information); return;
        }
        string? staging = null;
        string? stagingRoot = null;
        CancellationTokenSource? generation = null;
        try
        {
            SyncLists();
            LauncherServer server = _project.Snapshot.Servers[0];
            _project.Gateway.Port = _project.Snapshot.DefaultMicro.Port;
            _project.Gateway.ResourceDirectory = _project.ImportedClientDirectory;
            if (string.IsNullOrWhiteSpace(server.Address) || string.IsNullOrWhiteSpace(_project.Snapshot.DefaultMicro.Address)) throw new InvalidDataException("请填写游戏服务器地址和微端服务器地址");
            string projectRoot = _store.GetProjectDirectory(_project.Snapshot.ProjectId);
            EditorPreflightValidator.ThrowIfInvalid(_project, projectRoot);
            _store.Save(_project);
            string safeName = SafeFileName(_project.Snapshot.ProjectName);
            string outputRoot = stagingRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "传奇启动器成品");
            string output = Path.Combine(outputRoot, safeName + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            staging = Path.Combine(outputRoot, "." + safeName + "-生成中-" + Guid.NewGuid().ToString("N"));
            string projectId = _project.Snapshot.ProjectId;
            string? code = PlayerArtifactBuilder.RequiresMicroCredential(_project) ? GetOrCreateMicroCode() : null;
            string gatewayCode = GetOrCreateMicroCode();
            EditorProject buildProject = _store.Load(projectId);
            generation = new CancellationTokenSource();
            _quickGenerationCancellation = generation; _quickGenerationRunning = true;
            CancellationToken cancellation = generation.Token;
            _quickPanel?.SetBusy(true); _projects.Enabled = false; _tools.Enabled = false; UseWaitCursor = true; SetStatus("正在后台生成全部成品，请稍候……");
            await Task.Run(() =>
            {
                cancellation.ThrowIfCancellationRequested();
                Directory.CreateDirectory(staging);
                string player = Path.Combine(staging, buildProject.Brand.OutputFileName);
                PlayerArtifactBuilder.Create(buildProject, projectRoot, player, code, cancellation);
                cancellation.ThrowIfCancellationRequested();
                DeploymentPackageBuilder.CreateGatewayPackage(buildProject, Path.Combine(staging, "独立微端部署包.zip"), gatewayCode, cancellation);
                cancellation.ThrowIfCancellationRequested();
                if (buildProject.DeliveryMode == ClientDeliveryMode.FullClient) FullClientDistributionBuilder.Create(buildProject, player, Path.Combine(staging, "完整客户端包.zip"), cancellation);
                cancellation.ThrowIfCancellationRequested();
                Directory.CreateDirectory(outputRoot);
                Directory.Move(staging, output);
            }, cancellation);
            staging = null;
            _lastQuickOutput = output; _quickPanel?.SetResult(output);
            SetStatus("全部成品已生成：" + output);
            try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{output}\"") { UseShellExecute = true }); }
            catch { /* 成品已经生成，无法自动打开目录不应把成功结果误报为失败。 */ }
        }
        catch (OperationCanceledException) { SetStatus("已取消生成，正在检查临时文件"); }
        catch (Exception ex) { ShowError(ex); }
        finally
        {
            bool cleaned = string.IsNullOrWhiteSpace(staging) || !string.IsNullOrWhiteSpace(stagingRoot) && CleanupStaging(stagingRoot, staging);
            if (generation is not null) generation.Dispose();
            if (ReferenceEquals(_quickGenerationCancellation, generation)) _quickGenerationCancellation = null;
            _quickGenerationRunning = false;
            _quickPanel?.SetBusy(false); _projects.Enabled = true; _tools.Enabled = true; UseWaitCursor = false;
            if (!cleaned) SetStatus("生成已停止，但临时目录未能自动清理：" + staging);
            if (_closeAfterQuickGeneration && !IsDisposed) { _closeAfterQuickGeneration = false; BeginInvoke(Close); }
        }
    }

    private void RefreshPreview()
    {
        if (_project is null) return;
        try
        {
            SyncLists();
            string projectRoot = _store.GetProjectDirectory(_project.Snapshot.ProjectId);
            Bitmap next = LauncherRuntimeHost.RenderTemplateForEvidence(_project.Snapshot, projectRoot, _previewScale);
            Image? previous = _preview.Image; _preview.Image = next; previous?.Dispose();
            SetStatus("预览已使用玩家入口同一渲染模块刷新");
        }
        catch (Exception ex) { SetStatus("预览失败：" + ex.Message); }
    }

    private void ValidateBeforeGeneration()
    {
        if (_project is null) return;
        try
        {
            SyncLists();
            string root = _store.GetProjectDirectory(_project.Snapshot.ProjectId);
            IReadOnlyList<string> issues = EditorPreflightValidator.Validate(_project, root);
            MessageBox.Show(this, issues.Count == 0 ? "发布前检查通过：四档界面缩放、控件边界、点击区域、素材和链接均有效。" : string.Join("\r\n", issues), "发布前检查", MessageBoxButtons.OK, issues.Count == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void ImportThemeImage()
    {
        if (_project is null) return;
        using var dialog = new OpenFileDialog { Filter = "主题图片 (*.png;*.bmp;*.jpg;*.jpeg)|*.png;*.bmp;*.jpg;*.jpeg", Multiselect = false };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        using var usageDialog = new ThemeImageUsageDialog();
        if (usageDialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            string relative = ThemeAssetImporter.Import(_store.GetProjectDirectory(_project.Snapshot.ProjectId), dialog.FileName, _project.OptimizeImportedImages);
            switch (usageDialog.Usage)
            {
                case ThemeImageUsage.Background: _project.Snapshot.Theme.BackgroundImage = relative; break;
                case ThemeImageUsage.ButtonHover: _project.Snapshot.Theme.LaunchButtonHoverImage = relative; break;
                case ThemeImageUsage.ButtonPressed: _project.Snapshot.Theme.LaunchButtonPressedImage = relative; break;
                case ThemeImageUsage.ButtonDisabled: _project.Snapshot.Theme.LaunchButtonDisabledImage = relative; break;
                default: _project.Snapshot.Theme.LaunchButtonImage = relative; break;
            }
            RebuildTabs(); RefreshPreview(); SetStatus("主题图片已复制到项目素材目录");
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void ImportThemePackage()
    {
        if (_project is null) return;
        using var dialog = new OpenFileDialog { Filter = "传奇启动器主题模板 (*.lyotheme)|*.lyotheme" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try { ThemeTemplatePackage.Import(_project, _store.GetProjectDirectory(_project.Snapshot.ProjectId), dialog.FileName); RebuildTabs(); RefreshPreview(); SetStatus("主题模板已导入"); }
        catch (Exception ex) { ShowError(ex); }
    }

    private void ExportThemePackage()
    {
        if (_project is null) return;
        using var dialog = new SaveFileDialog { Filter = "传奇启动器主题模板 (*.lyotheme)|*.lyotheme", FileName = SafeFileName(_project.Snapshot.ProjectName) + "-主题模板.lyotheme" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try { SyncLists(); ThemeTemplatePackage.Export(_project, _store.GetProjectDirectory(_project.Snapshot.ProjectId), dialog.FileName); SetStatus("不含项目秘密的主题模板已导出"); }
        catch (Exception ex) { ShowError(ex); }
    }

    private void GenerateGatewayPackage()
    {
        if (_project is null) return;
        using var dialog = new SaveFileDialog { Filter = "微端部署包 (*.zip)|*.zip", FileName = SafeFileName(_project.Snapshot.ProjectName) + "-独立微端.zip" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try { SyncLists(); EditorPreflightValidator.ThrowIfInvalid(_project, _store.GetProjectDirectory(_project.Snapshot.ProjectId)); DeploymentPackageBuilder.CreateGatewayPackage(_project, dialog.FileName, GetOrCreateMicroCode()); SetStatus("微端部署包已生成，访问密码已加密同步：" + dialog.FileName); }
        catch (Exception ex) { ShowError(ex); }
    }

    private void GeneratePlayerExecutable()
    {
        if (_project is null) return;
        string? microCode = PlayerArtifactBuilder.RequiresMicroCredential(_project) ? GetOrCreateMicroCode() : null;
        using var dialog = new SaveFileDialog { Filter = "玩家启动器 (*.exe)|*.exe", FileName = _project.Brand.OutputFileName };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            SyncLists(); EditorPreflightValidator.ThrowIfInvalid(_project, _store.GetProjectDirectory(_project.Snapshot.ProjectId)); _store.Save(_project);
            PlayerPayloadInfo info = PlayerArtifactBuilder.Create(_project, _store.GetProjectDirectory(_project.Snapshot.ProjectId), dialog.FileName, microCode);
            SetStatus($"玩家启动器已生成并验证：{info.FileCount} 个载荷文件，{new FileInfo(dialog.FileName).Length / 1024d / 1024d:F2} 兆字节");
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void GenerateFullClientPackage()
    {
        if (_project is null) return;
        if (_project.DeliveryMode != ClientDeliveryMode.FullClient) { MessageBox.Show(this, "请先把玩家下载方式改为“完整客户端下载”。", "完整客户端交付"); return; }
        using var entry = new OpenFileDialog { Filter = "已生成的玩家启动器 (*.exe)|*.exe", Title = "选择与本项目匹配的玩家启动器" };
        if (entry.ShowDialog(this) != DialogResult.OK) return;
        using var output = new SaveFileDialog { Filter = "完整客户端包 (*.zip)|*.zip", FileName = _project.Snapshot.ProjectId + "-完整客户端.zip" };
        if (output.ShowDialog(this) != DialogResult.OK) return;
        try { SyncLists(); EditorPreflightValidator.ThrowIfInvalid(_project, _store.GetProjectDirectory(_project.Snapshot.ProjectId)); _store.Save(_project); FullClientDistributionBuilder.Create(_project, entry.FileName, output.FileName); SetStatus("完整客户端交付包已生成；微端项目仍采用单文件按需下载模式"); }
        catch (Exception ex) { ShowError(ex); }
    }

    private string GetOrCreateMicroCode()
    {
        if (_project is null) throw new InvalidOperationException("未选择项目");
        string value = ProtectedClientSecretStore.ReadMicroCode(_project.Snapshot.ProjectId);
        if (!string.IsNullOrWhiteSpace(value)) return value;
        value = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        ProtectedClientSecretStore.WriteMicroCode(_project.Snapshot.ProjectId, value);
        return value;
    }

    private void PublishRelease()
    {
        if (_project is null) return;
        string projectRoot = _store.GetProjectDirectory(_project.Snapshot.ProjectId);
        using var folder = new FolderBrowserDialog { Description = "选择不可变发布源目录", SelectedPath = string.IsNullOrWhiteSpace(_project.Release.LastPublishRoot) ? Path.Combine(projectRoot, "Publish") : _project.Release.LastPublishRoot };
        if (folder.ShowDialog(this) != DialogResult.OK) return;
        using var note = new TextValueDialog("发布备注", "输入本次发布备注（可留空）：");
        if (note.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            SyncLists(); EditorPreflightValidator.ThrowIfInvalid(_project, projectRoot); _store.Save(_project);
            ProjectReleaseResult result = ProjectReleasePublisher.Publish(_project, projectRoot, folder.SelectedPath, note.Value);
            _project.Release.LastPublishRoot = Path.GetFullPath(folder.SelectedPath); _store.Save(_project);
            SetStatus($"已发布不可变版本：序列 {result.Sequence}，{result.VersionName}"); RebuildTabs();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void RollbackRelease()
    {
        if (_project is null || _project.Release.History.Count == 0) { MessageBox.Show(this, "当前项目没有可回滚历史。", "回滚版本"); return; }
        using var dialog = new RollbackReleaseDialog(_project, _project.Release.LastPublishRoot);
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Selected is null) return;
        try
        {
            string projectRoot = _store.GetProjectDirectory(_project.Snapshot.ProjectId);
            ProjectReleaseResult result = ProjectReleasePublisher.Rollback(_project, projectRoot, _project.Release.LastPublishRoot, dialog.Selected.VersionName, "回滚到序列 " + dialog.Selected.Sequence);
            _store.Save(_project); SetStatus($"回滚已生成更高序列 {result.Sequence}：{result.VersionName}"); RebuildTabs();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void ExportOfflineRelease()
    {
        if (_project is null) return;
        using var dialog = new SaveFileDialog { Filter = "离线发布包 (*.zip)|*.zip", FileName = SafeFileName(_project.Snapshot.ProjectName) + "-离线发布.zip" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try { ProjectReleasePublisher.CreateOfflineDeploymentPackage(_project.Release.LastPublishRoot, dialog.FileName); SetStatus("离线发布包已生成：" + dialog.FileName); }
        catch (Exception ex) { ShowError(ex); }
    }

    private void ExportRecoveryPackage()
    {
        if (_project is null) return;
        using var password = new TextValueDialog("项目恢复密码", "输入至少 12 个字符的独立恢复密码：", secret: true);
        if (password.ShowDialog(this) != DialogResult.OK) return;
        using var dialog = new SaveFileDialog { Filter = "项目恢复包 (*.launcher-recovery.json)|*.launcher-recovery.json", FileName = SafeFileName(_project.Snapshot.ProjectName) + "-项目恢复.launcher-recovery.json" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try { ProjectReleaseKeyStore.ExportRecovery(_project, _store.GetProjectDirectory(_project.Snapshot.ProjectId), password.Value, dialog.FileName); SetStatus("项目恢复包已导出，请将密码与文件分开保存。"); }
        catch (Exception ex) { ShowError(ex); }
    }

    private void ImportOfflineRelease()
    {
        if (_project is null) return;
        using var file = new OpenFileDialog { Filter = "离线发布包 (*.zip)|*.zip" };
        if (file.ShowDialog(this) != DialogResult.OK) return;
        using var folder = new FolderBrowserDialog { Description = "选择离线版本安装目录", SelectedPath = string.IsNullOrWhiteSpace(_project.Release.LastPublishRoot) ? _store.GetProjectDirectory(_project.Snapshot.ProjectId) : _project.Release.LastPublishRoot };
        if (folder.ShowDialog(this) != DialogResult.OK) return;
        try { ProjectReleaseResult result = ProjectReleasePublisher.ImportOfflineDeploymentPackage(_project, file.FileName, folder.SelectedPath); _store.Save(_project); SetStatus($"已导入签名离线版本：序列 {result.Sequence}"); RebuildTabs(); }
        catch (Exception ex) { ShowError(ex); }
    }

    private void RotateReleaseKey()
    {
        if (_project is null) return;
        if (MessageBox.Show(this, "下一把密钥将提升为当前密钥，并生成新的下一把密钥。继续？", "轮换签名密钥", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        try { ProjectReleaseKeyStore.Rotate(_project, _store.GetProjectDirectory(_project.Snapshot.ProjectId)); _store.Save(_project); SetStatus("签名密钥已轮换；请立即生成新玩家入口并发布新版本。"); RebuildTabs(); }
        catch (Exception ex) { ShowError(ex); }
    }

    private void ImportRecoveryPackage()
    {
        if (_project is null) return;
        using var file = new OpenFileDialog { Filter = "项目恢复包 (*.launcher-recovery.json)|*.launcher-recovery.json" };
        if (file.ShowDialog(this) != DialogResult.OK) return;
        using var password = new TextValueDialog("项目恢复密码", "输入该恢复包的独立密码：", secret: true);
        if (password.ShowDialog(this) != DialogResult.OK) return;
        try { ProjectReleaseKeyStore.ImportRecovery(_project, _store.GetProjectDirectory(_project.Snapshot.ProjectId), password.Value, file.FileName); SetStatus("项目签名私钥已恢复到当前系统用户。 "); }
        catch (Exception ex) { ShowError(ex); }
    }

    private void SetStatus(string value) => _status.Text = value;
    private void ShowError(Exception error) => MessageBox.Show(this, error.Message, "操作失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
    private static string SafeFileName(string value)
    {
        string safe = string.Concat(value.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character)).Trim().TrimEnd('.', ' ');
        return string.IsNullOrWhiteSpace(safe) ? "传奇启动器" : safe;
    }

    private static bool CleanupStaging(string root, string staging)
    {
        try
        {
            string parent = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
            string full = Path.GetFullPath(staging).TrimEnd(Path.DirectorySeparatorChar);
            if (!string.Equals(Path.GetDirectoryName(full), parent, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(full).StartsWith(".", StringComparison.Ordinal) || !Directory.Exists(full)) return !Directory.Exists(full);
            RejectReparseChain(parent);
            if ((File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0) { Directory.Delete(full, false); return !Directory.Exists(full); }
            var pending = new Stack<string>(); pending.Push(full);
            while (pending.Count > 0)
            {
                string directory = pending.Pop();
                foreach (string path in Directory.EnumerateFileSystemEntries(directory))
                {
                    FileAttributes attributes = File.GetAttributes(path);
                    if ((attributes & FileAttributes.ReparsePoint) != 0) return false;
                    if ((attributes & FileAttributes.Directory) != 0) pending.Push(path);
                }
            }
            Directory.Delete(full, true);
            return !Directory.Exists(full);
        }
        catch { return false; }
    }

    private static void RejectReparseChain(string path)
    {
        string full = Path.GetFullPath(path), current = Path.GetPathRoot(full) ?? string.Empty;
        foreach (string part in full[current.Length..].Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (part.Length == 0) continue;
            current = Path.Combine(current, part);
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("成品目录不得经过重解析点");
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _preview.Image?.Dispose();
        base.Dispose(disposing);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_quickGenerationCancellation is not null)
        {
            _closeAfterQuickGeneration = true;
            _quickGenerationCancellation.Cancel();
            SetStatus("正在安全停止生成，完成清理后自动关闭……");
            e.Cancel = true;
        }
        base.OnFormClosing(e);
    }

    private sealed record ProjectListItem(string Id, string Name)
    {
        public override string ToString() => Name;
    }
}
