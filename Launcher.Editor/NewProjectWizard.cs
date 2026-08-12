using Launcher.ThemeRuntime;

namespace LyoCrystal.LauncherEditor;

internal sealed class NewProjectWizard : Form
{
    private readonly List<Font> _ownedFonts = new();
    private readonly string _projectId = "project-" + DateTime.Now.ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid().ToString("N")[..4];
    private readonly TextBox _name = new() { Text = "新传奇启动器" };
    private readonly TextBox _client = new() { ReadOnly = true };
    private readonly TextBox _gameAddress = new() { Text = "127.0.0.1" };
    private readonly NumericUpDown _gamePort = new() { Minimum = 1, Maximum = 65535, Value = 7000 };
    private readonly TextBox _microAddress = new() { Text = "127.0.0.1" };
    private readonly NumericUpDown _microPort = new() { Minimum = 1, Maximum = 65535, Value = 8080 };
    private readonly ComboBox _template = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _serverList = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _delivery = new() { DropDownStyle = ComboBoxStyle.DropDownList };

    public EditorProjectCreationOptions Options => new()
    {
        ProjectId = _projectId,
        ProjectName = _name.Text.Trim(),
        ImportedClientDirectory = _client.Text.Trim(),
        ServerAddress = _gameAddress.Text.Trim(),
        ServerPort = (int)_gamePort.Value,
        MicroAddress = _microAddress.Text.Trim(),
        MicroPort = (int)_microPort.Value,
        Template = (_template.SelectedItem as ChineseChoice<LauncherTemplateKind>)?.Value ?? LauncherTemplateKind.Classic,
        ServerListMode = (_serverList.SelectedItem as ChineseChoice<ServerListMode>)?.Value ?? ServerListMode.Dropdown,
        DeliveryMode = (_delivery.SelectedItem as ChineseChoice<ClientDeliveryMode>)?.Value ?? ClientDeliveryMode.MicroOnDemand,
    };

    public NewProjectWizard()
    {
        Text = "快速新建启动器";
        ClientSize = new Size(720, 570);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        AddChoices(_template, EditorChineseText.Choices(Enum.GetValues<LauncherTemplateKind>(), EditorChineseText.Template));
        AddChoices(_serverList, EditorChineseText.Choices(Enum.GetValues<ServerListMode>(), EditorChineseText.ServerList));
        AddChoices(_delivery, EditorChineseText.Choices(Enum.GetValues<ClientDeliveryMode>(), EditorChineseText.Delivery));

        var title = new Label { Text = "只填服务器地址，也能直接生成", Font = OwnFont(new Font(Font.FontFamily, 17, FontStyle.Bold)), AutoSize = true, ForeColor = Color.FromArgb(34, 70, 125), Margin = new Padding(0, 0, 0, 8) };
        var explanation = new Label { Text = "选择完整客户端后，配置器会自动读取已有设置。其余高级选项以后再改也可以。", AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(0, 0, 0, 18) };
        var choose = new Button { Text = "选择完整客户端目录", AutoSize = true };
        choose.Click += (_, _) => { using var folder = new FolderBrowserDialog { Description = "选择包含 Client.exe 的完整客户端目录", ShowNewFolderButton = false, UseDescriptionForTitle = true }; if (folder.ShowDialog(this) == DialogResult.OK) _client.Text = folder.SelectedPath; };

        var fields = new TableLayoutPanel { AutoSize = true, ColumnCount = 3, Width = 650 };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 155)); fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        AddRow(fields, 0, "启动器名称", _name);
        AddRow(fields, 1, "完整客户端资源", _client, choose);
        AddRow(fields, 2, "游戏服务器地址", _gameAddress);
        AddRow(fields, 3, "游戏服务器端口", _gamePort);
        AddRow(fields, 4, "微端服务器地址", _microAddress);
        AddRow(fields, 5, "微端服务器端口", _microPort);
        AddRow(fields, 6, "界面样式", _template);
        AddRow(fields, 7, "区服选择方式", _serverList);
        AddRow(fields, 8, "玩家下载方式", _delivery);

        var finish = new Button { Text = "创建启动器", DialogResult = DialogResult.OK, Width = 150, Height = 42, BackColor = Color.FromArgb(30, 145, 70), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
        finish.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(_name.Text)) { MessageBox.Show(this, "请填写启动器名称。", Text); DialogResult = DialogResult.None; return; }
            if (!string.IsNullOrWhiteSpace(_client.Text) && (!Directory.Exists(_client.Text) || !File.Exists(Path.Combine(_client.Text, "Client.exe"))))
            { MessageBox.Show(this, "所选目录不是完整客户端目录，请重新选择。", Text); DialogResult = DialogResult.None; }
        };
        var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Width = 90, Height = 42 };
        var skip = new Button { Text = "跳过向导，使用默认设置", DialogResult = DialogResult.OK, Width = 190, Height = 42 };
        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Width = 650, Margin = new Padding(0, 18, 0, 0) };
        buttons.Controls.AddRange(new Control[] { finish, cancel, skip });
        var content = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(28) };
        content.Controls.AddRange(new Control[] { title, explanation, fields, buttons });
        Controls.Add(content); AcceptButton = finish; CancelButton = cancel;
    }

    private static void AddChoices<T>(ComboBox combo, ChineseChoice<T>[] choices)
    {
        combo.Items.AddRange(choices); combo.SelectedIndex = 0;
    }

    private static void AddRow(TableLayoutPanel layout, int row, string label, Control editor, Control? extra = null)
    {
        editor.Dock = DockStyle.Fill; editor.Margin = new Padding(4, 7, 8, 7);
        layout.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
        layout.Controls.Add(editor, 1, row);
        if (extra is not null) { extra.Anchor = AnchorStyles.Left; layout.Controls.Add(extra, 2, row); }
    }
    private Font OwnFont(Font font) { _ownedFonts.Add(font); return font; }
    protected override void Dispose(bool disposing) { if (disposing) foreach (Font font in _ownedFonts) font.Dispose(); base.Dispose(disposing); }
}
