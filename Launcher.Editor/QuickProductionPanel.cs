using Launcher.ThemeRuntime;

namespace LyoCrystal.LauncherEditor;

internal sealed class QuickProductionPanel : UserControl
{
    private readonly EditorProject _project;
    private readonly Label _resource = new() { AutoEllipsis = true, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Label _result = new() { AutoEllipsis = true, Dock = DockStyle.Fill, ForeColor = Color.FromArgb(20, 105, 45), TextAlign = ContentAlignment.MiddleLeft };

    public QuickProductionPanel(EditorProject project, Action chooseResource, Action chooseBackground, Action chooseButton, Action generateAll, Action showAdvanced)
    {
        _project = project;
        Dock = DockStyle.Fill;
        BackColor = Color.White;
        AutoScroll = true;

        var title = new Label { Text = "两步制作启动器", Font = new Font(Font.FontFamily, 20, FontStyle.Bold), AutoSize = true, ForeColor = Color.FromArgb(34, 70, 125) };
        var subtitle = new Label { Text = "第一步选择完整客户端资源，第二步一键生成。通常只需确认服务器地址和端口。", AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(0, 6, 0, 18) };
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(28) };
        flow.Controls.Add(title); flow.Controls.Add(subtitle);
        flow.Controls.Add(CreateBasicSettings());
        flow.Controls.Add(CreateResourceStep(chooseResource, chooseBackground, chooseButton));
        flow.Controls.Add(CreateGenerateStep(generateAll, showAdvanced));
        Controls.Add(flow);
        Resize += (_, _) => ResizeCards(flow);
        ResizeCards(flow);
    }

    public void SetResult(string outputDirectory)
    {
        _result.Text = string.IsNullOrWhiteSpace(outputDirectory) ? "尚未生成" : "已生成到：" + outputDirectory;
    }

    private Control CreateBasicSettings()
    {
        LauncherServer server = _project.Snapshot.Servers[0];
        var name = new TextBox { Text = _project.Snapshot.ProjectName };
        name.TextChanged += (_, _) =>
        {
            string value = string.IsNullOrWhiteSpace(name.Text) ? "未命名启动器" : name.Text.Trim();
            _project.Snapshot.ProjectName = value; _project.Brand.ProductName = value; _project.Brand.WindowTitle = value; _project.Brand.TaskbarName = value;
        };
        var gameAddress = new TextBox { Text = server.Address }; gameAddress.TextChanged += (_, _) => server.Address = gameAddress.Text.Trim();
        var gamePort = Number(server.Port); gamePort.ValueChanged += (_, _) => server.Port = (int)gamePort.Value;
        var microAddress = new TextBox { Text = _project.Snapshot.DefaultMicro.Address }; microAddress.TextChanged += (_, _) => _project.Snapshot.DefaultMicro.Address = microAddress.Text.Trim();
        var microPort = Number(_project.Snapshot.DefaultMicro.Port); microPort.ValueChanged += (_, _) => { _project.Snapshot.DefaultMicro.Port = (int)microPort.Value; _project.Gateway.Port = (int)microPort.Value; };
        var template = Choice(EditorChineseText.Choices(Enum.GetValues<LauncherTemplateKind>(), EditorChineseText.Template), _project.Snapshot.Theme.Template);
        template.SelectedValueChanged += (_, _) => { if (template.SelectedItem is ChineseChoice<LauncherTemplateKind> item) _project.Snapshot.Theme.Template = item.Value; };
        var serverList = Choice(EditorChineseText.Choices(Enum.GetValues<ServerListMode>(), EditorChineseText.ServerList), _project.Snapshot.Theme.ServerListMode);
        serverList.SelectedValueChanged += (_, _) => { if (serverList.SelectedItem is ChineseChoice<ServerListMode> item) _project.Snapshot.Theme.ServerListMode = item.Value; };
        return Card("基础设置（一般只改这里）",
            ("启动器名称", name), ("游戏服务器地址", gameAddress), ("游戏服务器端口", gamePort),
            ("微端服务器地址", microAddress), ("微端服务器端口", microPort), ("界面样式", template), ("区服选择方式", serverList));
    }

    private Control CreateResourceStep(Action chooseResource, Action chooseBackground, Action chooseButton)
    {
        _resource.Text = string.IsNullOrWhiteSpace(_project.ImportedClientDirectory) ? "尚未选择完整客户端目录" : _project.ImportedClientDirectory;
        var choose = BigButton("① 选择完整客户端资源", chooseResource, Color.FromArgb(42, 111, 185));
        var background = BigButton("选择启动器背景图", chooseBackground, Color.FromArgb(90, 100, 115));
        var button = BigButton("选择进入游戏按钮图", chooseButton, Color.FromArgb(90, 100, 115));
        var actions = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = true };
        actions.Controls.AddRange(new Control[] { choose, background, button });
        var panel = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Dock = DockStyle.Top, Padding = new Padding(16) };
        panel.Controls.Add(actions); panel.Controls.Add(_resource);
        return Group("第一步：提供资源文件", panel);
    }

    private Control CreateGenerateStep(Action generateAll, Action showAdvanced)
    {
        var generate = BigButton("② 一键生成全部成品", generateAll, Color.FromArgb(30, 145, 70));
        generate.Width = 260; generate.Height = 54; generate.Font = new Font(generate.Font, FontStyle.Bold);
        var advanced = BigButton("显示高级设置", showAdvanced, Color.FromArgb(110, 110, 110));
        var actions = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
        actions.Controls.AddRange(new Control[] { generate, advanced });
        var panel = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Dock = DockStyle.Top, Padding = new Padding(16) };
        panel.Controls.Add(actions); panel.Controls.Add(new Label { Text = "自动保存项目、检查配置，并生成玩家单文件启动器和独立微端部署包。", AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(3, 8, 3, 3) }); panel.Controls.Add(_result);
        return Group("第二步：生成成品", panel);
    }

    private static GroupBox Card(string title, params (string Label, Control Editor)[] rows)
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 4, Padding = new Padding(14) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        for (int i = 0; i < rows.Length; i++)
        {
            int row = i / 2, column = (i % 2) * 2;
            rows[i].Editor.Dock = DockStyle.Fill; rows[i].Editor.Margin = new Padding(4, 7, 14, 7);
            layout.Controls.Add(new Label { Text = rows[i].Label, AutoSize = true, Anchor = AnchorStyles.Left }, column, row);
            layout.Controls.Add(rows[i].Editor, column + 1, row);
        }
        return Group(title, layout);
    }

    private static GroupBox Group(string title, Control content)
    {
        var group = new GroupBox { Text = title, Height = content.PreferredSize.Height + 42, Margin = new Padding(0, 0, 0, 14) };
        group.Controls.Add(content); return group;
    }

    private static NumericUpDown Number(int value) => new() { Minimum = 1, Maximum = 65535, Value = Math.Clamp(value, 1, 65535) };
    private static ComboBox Choice<T>(ChineseChoice<T>[] choices, T selected) where T : struct, Enum
    {
        var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        combo.Items.AddRange(choices); combo.SelectedItem = choices.First(item => EqualityComparer<T>.Default.Equals(item.Value, selected)); return combo;
    }
    private static Button BigButton(string text, Action action, Color color)
    {
        var button = new Button { Text = text, AutoSize = true, MinimumSize = new Size(180, 42), BackColor = color, ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Margin = new Padding(4, 4, 10, 4) };
        button.FlatAppearance.BorderSize = 0; button.Click += (_, _) => action(); return button;
    }
    private static void ResizeCards(FlowLayoutPanel flow)
    {
        int width = Math.Max(700, flow.ClientSize.Width - flow.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth - 8);
        foreach (Control control in flow.Controls) if (control is GroupBox) control.Width = width;
    }
}
