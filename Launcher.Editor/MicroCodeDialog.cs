namespace LyoCrystal.LauncherEditor;

internal sealed class MicroCodeDialog : Form
{
    private readonly TextBox _code = new() { UseSystemPasswordChar = true, Dock = DockStyle.Top, MaxLength = 512 };
    public string Code => _code.Text;
    public MicroCodeDialog()
    {
        Text = "本次生成使用的微端 Code"; ClientSize = new Size(500, 150); StartPosition = FormStartPosition.CenterParent; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false;
        var note = new Label { Text = "请输入与独立微端网关相同的 Code。编辑器不会把它写入项目 JSON 或命令行。", Dock = DockStyle.Top, Height = 45 };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 45, FlowDirection = FlowDirection.RightToLeft };
        buttons.Controls.Add(new Button { Text = "继续生成", DialogResult = DialogResult.OK, Width = 100 }); buttons.Controls.Add(new Button { Text = "取消", DialogResult = DialogResult.Cancel });
        Controls.Add(_code); Controls.Add(note); Controls.Add(buttons);
    }
}
