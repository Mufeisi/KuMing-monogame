namespace Launcher.ThemeRuntime;

internal sealed class PlayerSettingsForm : Form
{
    private readonly Dictionary<string, Control> _editors = new(StringComparer.Ordinal);
    public LauncherPlayerSettings Value { get; private set; }
    public PlayerSettingsForm(LauncherPlayerSettings value, string clientDirectory)
    {
        Value = value; Text = "游戏设置"; ClientSize = new Size(440, 620); StartPosition = FormStartPosition.CenterParent; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false;
        int y = 20;
        foreach (LauncherSettingDescriptor descriptor in LauncherSettingsRegistry.All)
        {
            Controls.Add(LabelAt(descriptor.Label, y));
            Control editor = CreateEditor(descriptor, value);
            Controls.Add(At(editor, 150, y, 260));
            _editors.Add(descriptor.Key, editor);
            y += 38;
        }
        var clearCache = new Button { Text = "清理微端缓存", Location = new Point(24, y + 8), Width = 120 };
        clearCache.Click += (_, _) => ClientMaintenance.ClearMicroCache(this, clientDirectory);
        var repair = new Button { Text = "签名检查与修复", Location = new Point(154, y + 8), Width = 130 };
        repair.Click += (_, _) => ClientMaintenance.StartRepair(this, clientDirectory);
        var logs = new Button { Text = "打开日志目录", Location = new Point(294, y + 8), Width = 120 };
        logs.Click += (_, _) => ClientMaintenance.OpenLogs(clientDirectory);
        int buttonY = y + 58;
        var ok = new Button { Text = "保存", DialogResult = DialogResult.OK, Location = new Point(250, buttonY), Width = 80 };
        var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Location = new Point(340, buttonY), Width = 80 };
        ok.Click += (_, _) =>
        {
            var next = new LauncherPlayerSettings();
            foreach (LauncherSettingDescriptor descriptor in LauncherSettingsRegistry.All) descriptor.Write(next, ReadEditor(descriptor, _editors[descriptor.Key]));
            Value = next;
        };
        Controls.AddRange(new Control[] { clearCache, repair, logs, ok, cancel }); AcceptButton = ok; CancelButton = cancel;
    }
    private static Control CreateEditor(LauncherSettingDescriptor descriptor, LauncherPlayerSettings value)
    {
        object current = descriptor.Read(value);
        if (descriptor.Editor == LauncherSettingEditorKind.Boolean) return new CheckBox { Checked = (bool)current };
        if (descriptor.Editor == LauncherSettingEditorKind.Integer) return new NumericUpDown { Minimum = descriptor.Minimum, Maximum = descriptor.Maximum, Value = Math.Clamp(Convert.ToDecimal(current), descriptor.Minimum, descriptor.Maximum) };
        var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        object[] options = descriptor.Options().ToArray();
        combo.Items.AddRange(options);
        combo.SelectedIndex = Math.Max(0, Array.FindIndex(options, item => Equals(item, current)));
        return combo;
    }
    private static object ReadEditor(LauncherSettingDescriptor descriptor, Control editor) => descriptor.Editor switch { LauncherSettingEditorKind.Boolean => ((CheckBox)editor).Checked, LauncherSettingEditorKind.Integer => (int)((NumericUpDown)editor).Value, _ => ((ComboBox)editor).SelectedItem ?? descriptor.Options().First() };
    private static Label LabelAt(string text, int y) => new() { Text = text, AutoSize = true, Location = new Point(24, y + 4) };
    private static T At<T>(T control, int x, int y, int width) where T : Control { control.Location = new Point(x, y); control.Width = width; return control; }
}
