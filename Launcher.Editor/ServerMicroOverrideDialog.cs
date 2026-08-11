using Launcher.ThemeRuntime;

namespace LyoCrystal.LauncherEditor;

internal sealed class ServerMicroOverrideDialog : Form
{
    private readonly CheckBox _enabled = new() { Text = "启用该区服专属微端入口", AutoSize = true };
    private readonly TextBox _address = new() { Width = 240 };
    private readonly NumericUpDown _port = new() { Minimum = 1, Maximum = 65535, Width = 120 };
    private readonly TextBox _backupAddress = new() { Width = 240 };
    private readonly NumericUpDown _backupPort = new() { Minimum = 0, Maximum = 65535, Width = 120 };
    private readonly string _defaultUser;
    public MicroEndpoint? Value => !_enabled.Checked ? null : new MicroEndpoint { Enabled = true, Address = _address.Text.Trim(), Port = decimal.ToInt32(_port.Value), BackupAddress = _backupAddress.Text.Trim(), BackupPort = decimal.ToInt32(_backupPort.Value), User = _defaultUser };

    public ServerMicroOverrideDialog(MicroEndpoint? value, string defaultUser)
    {
        Text = "区服微端覆盖"; ClientSize = new Size(520, 300); StartPosition = FormStartPosition.CenterParent; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false;
        _enabled.Checked = value is not null; _address.Text = value?.Address ?? "127.0.0.1"; _port.Value = value?.Port is >= 1 and <= 65535 ? value.Port : 8080;
        _defaultUser = defaultUser;
        _backupAddress.Text = value?.BackupAddress ?? string.Empty; _backupPort.Value = value?.BackupPort is >= 0 and <= 65535 ? value.BackupPort : 0;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 2, RowCount = 7 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.Controls.Add(_enabled, 0, 0); layout.SetColumnSpan(_enabled, 2);
        Add(layout, 1, "主入口地址", _address); Add(layout, 2, "主入口端口", _port); Add(layout, 3, "备用地址", _backupAddress); Add(layout, 4, "备用端口", _backupPort);
        var inheritedUser = new Label { Text = "访问用户继承项目默认值：" + defaultUser, AutoSize = true };
        layout.Controls.Add(inheritedUser, 0, 5); layout.SetColumnSpan(inheritedUser, 2);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        buttons.Controls.Add(new Button { Text = "确定", DialogResult = DialogResult.OK }); buttons.Controls.Add(new Button { Text = "取消", DialogResult = DialogResult.Cancel });
        layout.Controls.Add(buttons, 0, 6); layout.SetColumnSpan(buttons, 2); Controls.Add(layout);
    }
    private static void Add(TableLayoutPanel layout, int row, string label, Control control) { layout.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row); layout.Controls.Add(control, 1, row); }
}
