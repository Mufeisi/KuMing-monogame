namespace LyoCrystal.LauncherEditor;

internal sealed class TextValueDialog : Form
{
    private readonly TextBox _value = new() { Dock = DockStyle.Top, Width = 420 };
    public string Value => _value.Text;
    public TextValueDialog(string title, string prompt, bool secret = false)
    {
        Text = title; ClientSize = new Size(470, 145); StartPosition = FormStartPosition.CenterParent; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false;
        _value.UseSystemPasswordChar = secret;
        var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), FlowDirection = FlowDirection.TopDown, WrapContents = false };
        layout.Controls.Add(new Label { Text = prompt, AutoSize = true }); layout.Controls.Add(_value);
        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Width = 420 };
        buttons.Controls.Add(new Button { Text = "确定", DialogResult = DialogResult.OK }); buttons.Controls.Add(new Button { Text = "取消", DialogResult = DialogResult.Cancel });
        layout.Controls.Add(buttons); Controls.Add(layout); AcceptButton = buttons.Controls.OfType<Button>().First();
    }
}

internal sealed class RollbackReleaseDialog : Form
{
    private readonly ComboBox _versions = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 430 };
    public ProjectReleaseHistoryItem? Selected => _versions.SelectedItem as ProjectReleaseHistoryItem;
    public RollbackReleaseDialog(EditorProject project, string publishRoot)
    {
        Text = "选择回滚版本"; ClientSize = new Size(480, 150); StartPosition = FormStartPosition.CenterParent; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false;
        foreach (ProjectReleaseHistoryItem item in project.Release.History.OrderByDescending(item => item.Sequence)) _versions.Items.Add(item);
        _versions.Format += (_, e) => { if (e.ListItem is ProjectReleaseHistoryItem item) e.Value = $"序列 {item.Sequence}｜{item.VersionName}｜{item.Note}"; };
        if (_versions.Items.Count > 0) _versions.SelectedIndex = 0;
        var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), FlowDirection = FlowDirection.TopDown, WrapContents = false };
        var diff = new TextBox { ReadOnly = true, Multiline = true, Width = 430, Height = 55, ScrollBars = ScrollBars.Vertical };
        void RefreshDiff()
        {
            if (_versions.SelectedItem is not ProjectReleaseHistoryItem selected || project.Release.History.Count == 0 || string.IsNullOrWhiteSpace(publishRoot)) { diff.Text = "无可比较内容"; return; }
            try
            {
                ProjectReleaseHistoryItem current = project.Release.History.MaxBy(item => item.Sequence)!;
                ProjectReleaseDiff value = ProjectReleasePublisher.CompareVersions(project, publishRoot, selected.VersionName, current.VersionName);
                diff.Text = value.Summary + Environment.NewLine + string.Join("，", value.Added.Concat(value.Removed).Concat(value.Changed).Take(12));
            }
            catch (Exception ex) { diff.Text = "差异读取失败：" + ex.Message; }
        }
        _versions.SelectedIndexChanged += (_, _) => RefreshDiff();
        layout.Controls.Add(new Label { Text = "回滚会复制旧内容并生成更高序列的新版本，不降低防回放序列。", AutoSize = true }); layout.Controls.Add(_versions); layout.Controls.Add(diff);
        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Width = 430 };
        buttons.Controls.Add(new Button { Text = "生成回滚版本", DialogResult = DialogResult.OK }); buttons.Controls.Add(new Button { Text = "取消", DialogResult = DialogResult.Cancel }); layout.Controls.Add(buttons); Controls.Add(layout); ClientSize = new Size(480, 220); RefreshDiff();
    }
}
