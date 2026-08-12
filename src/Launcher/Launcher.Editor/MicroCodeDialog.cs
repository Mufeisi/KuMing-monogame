namespace LyoCrystal.LauncherEditor;

internal sealed class MicroCodeDialog : Form
{
    private readonly TextBox _code = new() { UseSystemPasswordChar = true, Dock = DockStyle.Top, MaxLength = 512 };
    public string Code => _code.Text;
    public MicroCodeDialog()
    {
        Text = "设置微端访问密码"; ClientSize = new Size(500, 150); StartPosition = FormStartPosition.CenterParent; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false;
        var note = new Label { Text = "请输入与独立微端相同的访问密码。配置器会安全保存，不会写入普通配置文件或启动参数。", Dock = DockStyle.Top, Height = 45 };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 45, FlowDirection = FlowDirection.RightToLeft };
        buttons.Controls.Add(new Button { Text = "继续生成", DialogResult = DialogResult.OK, Width = 100 }); buttons.Controls.Add(new Button { Text = "取消", DialogResult = DialogResult.Cancel });
        Controls.Add(_code); Controls.Add(note); Controls.Add(buttons);
    }
}
