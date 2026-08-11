using Launcher.ThemeRuntime;

namespace LyoCrystal.LauncherEditor;

internal sealed class NewProjectWizard : Form
{
    private readonly TextBox _id = new() { Text = "new-project", Width = 260 };
    private readonly TextBox _name = new() { Text = "新传奇启动器", Width = 260 };
    private readonly ComboBox _template = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
    public string ProjectId => _id.Text.Trim();
    public string ProjectName => _name.Text.Trim();
    public LauncherTemplateKind Template => _template.SelectedItem is LauncherTemplateKind kind ? kind : LauncherTemplateKind.Classic;

    public NewProjectWizard()
    {
        Text = "新建启动器项目"; ClientSize = new Size(520, 250); StartPosition = FormStartPosition.CenterParent; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false;
        _template.Items.AddRange(Enum.GetValues<LauncherTemplateKind>().Cast<object>().ToArray()); _template.SelectedIndex = 0;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 2, RowCount = 4 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.Controls.Add(new Label { Text = "项目标识", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0); layout.Controls.Add(_id, 1, 0);
        layout.Controls.Add(new Label { Text = "项目名称", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1); layout.Controls.Add(_name, 1, 1);
        layout.Controls.Add(new Label { Text = "主题模板", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 2); layout.Controls.Add(_template, 1, 2);
        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill };
        buttons.Controls.Add(new Button { Text = "完成", DialogResult = DialogResult.OK, Width = 90 });
        var skip = new Button { Text = "跳过向导", Width = 100 }; skip.Click += (_, _) => { _id.Text = "project-" + DateTime.Now.ToString("yyyyMMddHHmmss"); _name.Text = "未命名启动器"; DialogResult = DialogResult.OK; };
        buttons.Controls.Add(skip); buttons.Controls.Add(new Button { Text = "取消", DialogResult = DialogResult.Cancel, Width = 80 }); layout.Controls.Add(buttons, 0, 3); layout.SetColumnSpan(buttons, 2);
        Controls.Add(layout); AcceptButton = buttons.Controls[0] as Button;
    }
}
