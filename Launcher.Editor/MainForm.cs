using System.ComponentModel;
using Launcher.PlayerShell;
using Launcher.ThemeRuntime;
using Shared.Security;
using System.Security.Cryptography;

namespace LyoCrystal.LauncherEditor;

internal sealed class MainForm : Form
{
    private readonly EditorProjectStore _store;
    private readonly ListBox _projects = new() { Dock = DockStyle.Fill };
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
    private readonly PictureBox _preview = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(28, 28, 32) };
    private readonly ToolStripStatusLabel _status = new() { Text = "就绪" };
    private EditorProject? _project;
    private BindingList<LauncherServer>? _servers;
    private BindingList<LauncherAnnouncement>? _announcements;
    private BindingList<LauncherControlOverride>? _controlOverrides;
    private float _previewScale = 1f;

    public MainForm(EditorProjectStore store)
    {
        _store = store;
        Text = "LyoCrystal 启动器编辑器";
        WindowState = FormWindowState.Maximized;
        MinimumSize = new Size(1100, 700);
        BuildUi();
        ReloadProjects();
    }

    private void BuildUi()
    {
        var tools = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden };
        AddTool(tools, "新建项目", NewProject);
        AddTool(tools, "导入客户端", ImportClient);
        AddTool(tools, "保存", SaveProject);
        AddTool(tools, "导入主题图片", ImportThemeImage);
        AddTool(tools, "刷新预览", RefreshPreview);
        AddTool(tools, "发布前检查", ValidateBeforeGeneration);
        AddTool(tools, "生成玩家 EXE", GeneratePlayerExecutable);
        AddTool(tools, "生成微端部署包", GenerateGatewayPackage);
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

    private void ReloadProjects(string? select = null)
    {
        _projects.Items.Clear();
        foreach (string id in _store.ListProjectIds()) _projects.Items.Add(id);
        if (select is not null) _projects.SelectedItem = select;
        else if (_projects.Items.Count > 0) _projects.SelectedIndex = 0;
    }

    private void NewProject()
    {
        using var wizard = new NewProjectWizard();
        if (wizard.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            EditorProject project = _store.Create(wizard.ProjectId, wizard.ProjectName, wizard.Template);
            ReloadProjects(project.Snapshot.ProjectId);
            SetStatus("项目已创建，可在断网状态继续编辑和生成预览");
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void LoadSelectedProject()
    {
        if (_projects.SelectedItem is not string id) return;
        try { _project = _store.Load(id); RebuildTabs(); RefreshPreview(); SetStatus("已加载项目：" + id); }
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

        AddPropertyTab("项目与品牌", new ProjectBrandPropertyView(_project));
        AddPropertyTab("主题", new ThemePropertyView(_project.Snapshot.Theme));
        _tabs.TabPages.Add(CreateControlLayoutTab());
        _tabs.TabPages.Add(CreateServerTab());
        _tabs.TabPages.Add(CreateAnnouncementTab());
        _tabs.TabPages.Add(new TabPage("玩家设置") { Controls = { new SettingsEditorPanel(_project.Snapshot.Defaults) } });
        AddPropertyTab("项目默认微端", new DefaultMicroPropertyView(_project.Snapshot.DefaultMicro));
        AddPropertyTab("微端部署", new GatewayPropertyView(_project.Gateway));
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
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "标识", DataPropertyName = nameof(LauncherServer.Id) });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "分组", DataPropertyName = nameof(LauncherServer.Group) });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "区服名称", DataPropertyName = nameof(LauncherServer.Name) });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "排序", DataPropertyName = nameof(LauncherServer.SortOrder), FillWeight = 55 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "游戏地址", DataPropertyName = nameof(LauncherServer.Address) });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "游戏端口", DataPropertyName = nameof(LauncherServer.Port) });
        grid.Columns.Add(new DataGridViewComboBoxColumn { HeaderText = "运营状态", DataPropertyName = nameof(LauncherServer.Status), DataSource = Enum.GetValues<ServerOperatingStatus>() });
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

    private TabPage CreateControlLayoutTab()
    {
        var grid = new DataGridView { Dock = DockStyle.Fill, AutoGenerateColumns = true, DataSource = _controlOverrides, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, AllowUserToAddRows = false };
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
        bar.Controls.AddRange(new Control[] { new Label { Text = "预览 DPI", AutoSize = true, Padding = new Padding(0, 8, 0, 0) }, scale, new Label { Text = "预览与玩家入口共用同一 WinForms 渲染模块", AutoSize = true, Padding = new Padding(10, 8, 0, 0) } });
        var page = new TabPage("实时预览"); page.Controls.Add(_preview); page.Controls.Add(bar); return page;
    }

    private void SyncLists()
    {
        if (_project is null) return;
        if (_servers is not null) _project.Snapshot.Servers = _servers.ToList();
        if (_announcements is not null) _project.Snapshot.Announcements = _announcements.ToList();
        if (_controlOverrides is not null) _project.Snapshot.Theme.Controls = _controlOverrides.ToList();
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
            string unknown = result.UnknownFields.Count == 0 ? "无" : string.Join("、", result.UnknownFields.Take(20));
            MessageBox.Show(this, $"已映射 {result.MappedFields.Count} 项；未知字段：{unknown}\r\n敏感值已忽略：{(result.SensitiveValuesOmitted ? "是" : "未发现")}\r\n原客户端未被修改。", "导入预览", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) { ShowError(ex); }
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
            MessageBox.Show(this, issues.Count == 0 ? "发布前检查通过：四档 DPI、控件边界、点击区域、素材和链接均有效。" : string.Join("\r\n", issues), "发布前检查", MessageBoxButtons.OK, issues.Count == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
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
            string relative = ThemeAssetImporter.Import(_store.GetProjectDirectory(_project.Snapshot.ProjectId), dialog.FileName);
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

    private void GenerateGatewayPackage()
    {
        if (_project is null) return;
        using var dialog = new SaveFileDialog { Filter = "微端部署包 (*.zip)|*.zip", FileName = _project.Snapshot.ProjectId + "-微端网关.zip" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try { SyncLists(); EditorPreflightValidator.ThrowIfInvalid(_project, _store.GetProjectDirectory(_project.Snapshot.ProjectId)); DeploymentPackageBuilder.CreateGatewayPackage(_project, dialog.FileName, GetOrCreateMicroCode()); SetStatus("微端部署包已生成，访问 Code 已加密同步：" + dialog.FileName); }
        catch (Exception ex) { ShowError(ex); }
    }

    private void GeneratePlayerExecutable()
    {
        if (_project is null) return;
        string? microCode = PlayerArtifactBuilder.RequiresMicroCredential(_project) ? GetOrCreateMicroCode() : null;
        using var dialog = new SaveFileDialog { Filter = "玩家入口 (*.exe)|*.exe", FileName = _project.Brand.OutputFileName };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            SyncLists(); EditorPreflightValidator.ThrowIfInvalid(_project, _store.GetProjectDirectory(_project.Snapshot.ProjectId)); _store.Save(_project);
            PlayerPayloadInfo info = PlayerArtifactBuilder.Create(_project, _store.GetProjectDirectory(_project.Snapshot.ProjectId), dialog.FileName, microCode);
            SetStatus($"玩家 EXE 已生成并验证：{info.FileCount} 个载荷文件，{new FileInfo(dialog.FileName).Length / 1024d / 1024d:F2} MiB");
        }
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

    private void SetStatus(string value) => _status.Text = value;
    private void ShowError(Exception error) => MessageBox.Show(this, error.Message, "操作失败", MessageBoxButtons.OK, MessageBoxIcon.Error);

    protected override void Dispose(bool disposing)
    {
        if (disposing) _preview.Image?.Dispose();
        base.Dispose(disposing);
    }
}
