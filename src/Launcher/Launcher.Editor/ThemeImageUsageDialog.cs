namespace LyoCrystal.LauncherEditor;

internal enum ThemeImageUsage { Background, ButtonBase, ButtonHover, ButtonPressed, ButtonDisabled }

internal sealed class ThemeImageUsageDialog : Form
{
    private readonly ComboBox _usage = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top };
    public ThemeImageUsage Usage => (ThemeImageUsage)_usage.SelectedIndex;
    public ThemeImageUsageDialog()
    {
        Text = "选择图片用途"; ClientSize = new Size(420, 135); StartPosition = FormStartPosition.CenterParent; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false;
        _usage.Items.AddRange(new object[] { "启动器背景", "开始按钮基础图（自动派生四态）", "开始按钮悬停覆盖图", "开始按钮按下覆盖图", "开始按钮禁用覆盖图" }); _usage.SelectedIndex = 0;
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 45, FlowDirection = FlowDirection.RightToLeft };
        buttons.Controls.Add(new Button { Text = "确定", DialogResult = DialogResult.OK }); buttons.Controls.Add(new Button { Text = "取消", DialogResult = DialogResult.Cancel });
        Controls.Add(_usage); Controls.Add(new Label { Text = "未提供覆盖图时，玩家入口会从基础图自动生成细微状态差异。", Dock = DockStyle.Fill, Padding = new Padding(0, 10, 0, 0) }); Controls.Add(buttons);
    }
}
