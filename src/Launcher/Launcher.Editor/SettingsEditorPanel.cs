using Launcher.ThemeRuntime;

namespace LyoCrystal.LauncherEditor;

internal sealed class SettingsEditorPanel : TableLayoutPanel
{
    private readonly LauncherPlayerSettings _value;

    public SettingsEditorPanel(LauncherPlayerSettings value)
    {
        _value = value;
        Dock = DockStyle.Fill;
        AutoScroll = true;
        ColumnCount = 2;
        ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        int row = 0;
        foreach (LauncherSettingDescriptor descriptor in LauncherSettingsRegistry.All)
        {
            RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            Controls.Add(new Label { Text = descriptor.Label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
            Control editor = CreateEditor(descriptor);
            editor.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            Controls.Add(editor, 1, row++);
        }
        RowCount = row;
    }

    private Control CreateEditor(LauncherSettingDescriptor descriptor)
    {
        object current = descriptor.Read(_value);
        if (descriptor.Editor == LauncherSettingEditorKind.Boolean)
        {
            var check = new CheckBox { Checked = current is true, AutoSize = true };
            check.CheckedChanged += (_, _) => descriptor.Write(_value, check.Checked);
            return check;
        }
        if (descriptor.Editor == LauncherSettingEditorKind.Integer)
        {
            var number = new NumericUpDown { Minimum = descriptor.Minimum, Maximum = descriptor.Maximum, Value = Math.Clamp(Convert.ToDecimal(current), descriptor.Minimum, descriptor.Maximum), Width = 160 };
            number.ValueChanged += (_, _) => descriptor.Write(_value, decimal.ToInt32(number.Value));
            return number;
        }
        var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
        object[] options = descriptor.Options().ToArray(); combo.Items.AddRange(options);
        int index = Array.FindIndex(options, value => Equals(value, current)); combo.SelectedIndex = index >= 0 ? index : (options.Length > 0 ? 0 : -1);
        combo.SelectedIndexChanged += (_, _) => { if (combo.SelectedItem is not null) descriptor.Write(_value, combo.SelectedItem); };
        return combo;
    }
}
