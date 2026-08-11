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
    private readonly ComboBox _template = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _delivery = new() { DropDownStyle = ComboBoxStyle.DropDownList };

    public EditorProjectCreationOptions Options => new()
    {
        ProjectId = _id.Text.Trim(), ProjectName = _name.Text.Trim(), CompanyName = _company.Text.Trim(), ImportedClientDirectory = _client.Text.Trim(),
        RemoteReleaseBaseUrl = _release.Text.Trim(), ServerAddress = _gameAddress.Text.Trim(), ServerPort = (int)_gamePort.Value,
        MicroAddress = _microAddress.Text.Trim(), MicroPort = (int)_microPort.Value,
        Template = _template.SelectedItem is LauncherTemplateKind template ? template : LauncherTemplateKind.Classic,
        DeliveryMode = _delivery.SelectedItem is ClientDeliveryMode delivery ? delivery : ClientDeliveryMode.MicroOnDemand,
    };

    public NewProjectWizard()
    {
        Text = "新建启动器项目（可随时跳过）"; ClientSize = new Size(680, 560); StartPosition = FormStartPosition.CenterParent; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false;
        _template.Items.AddRange(Enum.GetValues<LauncherTemplateKind>().Cast<object>().ToArray()); _template.SelectedIndex = 0;
        _delivery.Items.AddRange(Enum.GetValues<ClientDeliveryMode>().Cast<object>().ToArray()); _delivery.SelectedIndex = 0;
        var pages = new TabControl { Dock = DockStyle.Fill };
        pages.TabPages.Add(Page("项目与品牌", ("项目标识", _id), ("项目名称", _name), ("公司名称", _company), ("主题模板", _template)));
        var browse = new Button { Text = "选择客户端目录…", AutoSize = true }; browse.Click += (_, _) => { using var folder = new FolderBrowserDialog(); if (folder.ShowDialog(this) == DialogResult.OK) _client.Text = folder.SelectedPath; };
        pages.TabPages.Add(Page("客户端与交付", ("客户端目录（可空）", _client), ("", browse), ("交付模式", _delivery)));
        pages.TabPages.Add(Page("游戏与微端", ("游戏地址", _gameAddress), ("游戏端口", _gamePort), ("微端地址", _microAddress), ("微端端口", _microPort)));
        pages.TabPages.Add(Page("发布", ("远程发布地址（可空）", _release), ("说明", new Label { Text = "项目密钥和恢复包将在项目创建后由编辑器生成；所有字段均可稍后修改。", AutoSize = true })));
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
