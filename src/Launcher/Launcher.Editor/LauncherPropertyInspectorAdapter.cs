using Launcher.ThemeRuntime;
using LyoCrystal.DesignCore;

namespace LyoCrystal.LauncherEditor;

internal sealed record LauncherPropertyInspectorSnapshot(string Summary, int SelectedCount, int EditableCount, string Width, string Bold, string BackgroundImage);

internal sealed class LauncherPropertyInspectorAdapter : UserControl
{
    private readonly ICanvasDocument<LauncherControlId> _document;
    private readonly ILauncherCanvasAppearance _appearance;
    private readonly Func<string?> _importImage;
    private readonly Func<string, Image?> _loadImage;
    private readonly ErrorProvider _errors = new() { BlinkStyle = ErrorBlinkStyle.NeverBlink };
    private readonly Panel _empty = new() { Dock = DockStyle.Fill };
    private readonly Panel _content = new() { Dock = DockStyle.Fill, AutoScroll = true };
    private readonly Label _summary = new() { AutoSize = false, Height = 36, Dock = DockStyle.Top, ForeColor = DesktopAuthoringTheme.TextSecondary, Padding = new Padding(0, 4, 0, 4) };
    private readonly Dictionary<string, TextBox> _text = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ComboBox> _choices = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Button> _colors = new(StringComparer.Ordinal);
    private readonly List<Button> _imageCommands = new();
    private readonly List<Button> _appearanceCommands = new();
    private readonly PictureBox _imagePreview = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = DesktopAuthoringTheme.AppBackground, BorderStyle = BorderStyle.FixedSingle, AccessibleName = "背景图片缩略图" };
    private bool _refreshing;

    internal LauncherPropertyInspectorAdapter(ICanvasDocument<LauncherControlId> document, ILauncherCanvasAppearance appearance, Func<string?> importImage, Func<string, Image?> loadImage)
    {
        _document = document;
        _appearance = appearance;
        _importImage = importImage;
        _loadImage = loadImage;
        _errors.ContainerControl = this;
        Dock = DockStyle.Fill;
        BuildUi();
        DesktopAuthoringTheme.Apply(this);
        RefreshFromDocument();
    }

    internal void RefreshFromDocument()
    {
        _refreshing = true;
        try
        {
            SelectedControl[] selected = Selected();
            SelectedControl[] editable = selected.Where(item => !_document.IsLocked(item.Id) && item.Visible).ToArray();
            _empty.Visible = selected.Length == 0;
            _content.Visible = selected.Length > 0;
            if (selected.Length == 0) return;
            _summary.Text = selected.Length == 1
                ? $"{EditorChineseText.Control(selected[0].Id)} · {(_document.IsLocked(selected[0].Id) ? "已锁定" : selected[0].Visible ? "可编辑" : "已隐藏") }"
                : $"已选择 {selected.Length} 个对象 · {editable.Length} 个可编辑";
            SetText("name", selected.Length == 1 ? EditorChineseText.Control(selected[0].Id) : $"{selected.Length} 个对象");
            SetText("x", Common(selected, item => item.X));
            SetText("y", Common(selected, item => item.Y));
            SetText("width", Common(selected, item => item.Width));
            SetText("height", Common(selected, item => item.Height));
            SetChoice("visible", Common(selected, item => item.Visible));
            SetChoice("locked", Common(selected, item => _document.IsLocked(item.Id)));
            SetColor("fore", Common(selected, item => item.ForeColor));
            SetColor("back", Common(selected, item => item.BackColor));
            SetText("font", Common(selected, item => item.FontName));
            SetText("fontSize", Common(selected, item => item.FontSize));
            SetChoice("bold", Common(selected, item => item.Bold));
            SetText("opacity", Common(selected, item => item.OpacityPercent));
            SetText("image", Common(selected, item => item.BackgroundImage));
            RefreshImagePreview(selected);
            SetEditability(editable.Length > 0, selected.Any(item => !_document.IsLocked(item.Id)));
        }
        finally { _refreshing = false; }
    }

    internal LauncherPropertyInspectorSnapshot CaptureSnapshot()
    {
        SelectedControl[] selected = Selected();
        return new LauncherPropertyInspectorSnapshot(_summary.Text, selected.Length, selected.Count(item => !_document.IsLocked(item.Id) && item.Visible), _text["width"].Text, _choices["bold"].Text, _text["image"].Text);
    }

    internal void ApplyTextForEvidence(string key, string value) { _text[key].Text = value; CommitText(key); }
    internal void ApplyChoiceForEvidence(string key, string value) { _choices[key].SelectedItem = value; CommitChoice(key); }

    private void BuildUi()
    {
        var emptyTitle = new Label { Text = "未选择对象", AutoSize = true, Font = DesktopAuthoringTheme.CreateBodyFont(10F, FontStyle.Bold), Location = new Point(12, 16) };
        var emptyHint = new Label { Text = "在对象树或画布中选择一个对象，\r\n即可编辑布局、外观、状态与资源。", AutoSize = true, ForeColor = DesktopAuthoringTheme.TextSecondary, Location = new Point(12, 48) };
        _empty.Controls.Add(emptyHint); _empty.Controls.Add(emptyTitle);

        var fields = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(0, 0, 4, 12) };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 86));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddSection(fields, "标识");
        AddText(fields, "对象", "name", readOnly: true);
        AddSection(fields, "布局");
        AddText(fields, "横向位置", "x", suffix: "px");
        AddText(fields, "纵向位置", "y", suffix: "px");
        AddText(fields, "宽度", "width", suffix: "px");
        AddText(fields, "高度", "height", suffix: "px");
        AddSection(fields, "外观");
        AddColor(fields, "文字颜色", "fore");
        AddColor(fields, "背景颜色", "back");
        AddText(fields, "字体", "font");
        AddText(fields, "字号", "fontSize", suffix: "pt");
        AddChoice(fields, "粗体", "bold");
        AddText(fields, "不透明度", "opacity", suffix: "%");
        _appearanceCommands.Add(AddCommand(fields, "", "重置外观", "把所选可编辑对象的外观恢复为主题默认值", () => ChangeStyle(new(ForeColor: string.Empty, BackColor: string.Empty, FontName: string.Empty, FontSize: 0F, Bold: false, OpacityPercent: 100, BackgroundImage: string.Empty))));
        AddSection(fields, "状态");
        AddChoice(fields, "显示", "visible");
        AddChoice(fields, "锁定", "locked");
        AddSection(fields, "资源");
        AddImage(fields);
        _content.Controls.Add(fields);
        _content.Controls.Add(_summary);
        Controls.Add(_empty); Controls.Add(_content);
    }

    private static void AddSection(TableLayoutPanel fields, string title)
    {
        int row = fields.RowCount++;
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        var label = new Label { Text = title, Dock = DockStyle.Fill, TextAlign = ContentAlignment.BottomLeft, Font = DesktopAuthoringTheme.CreateBodyFont(9F, FontStyle.Bold), Padding = new Padding(0, 0, 0, 4) };
        fields.Controls.Add(label, 0, row); fields.SetColumnSpan(label, 2);
    }

    private void AddText(TableLayoutPanel fields, string label, string key, string? suffix = null, bool readOnly = false)
    {
        var input = new TextBox { Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle, ReadOnly = readOnly, AccessibleName = label };
        input.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { CommitText(key); e.SuppressKeyPress = true; } };
        input.Validated += (_, _) => CommitText(key);
        _text[key] = input;
        Control editor = input;
        if (suffix is not null)
        {
            var panel = new Panel { Dock = DockStyle.Fill };
            var unit = new Label { Text = suffix, Dock = DockStyle.Right, Width = 26, TextAlign = ContentAlignment.MiddleLeft, ForeColor = DesktopAuthoringTheme.TextSecondary };
            panel.Controls.Add(input); panel.Controls.Add(unit); editor = panel;
        }
        AddRow(fields, label, editor);
    }

    private void AddChoice(TableLayoutPanel fields, string label, string key)
    {
        var choice = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = label };
        choice.Items.AddRange(["多个值", "是", "否"]);
        choice.SelectedIndexChanged += (_, _) => CommitChoice(key);
        _choices[key] = choice;
        AddRow(fields, label, choice);
    }

    private void AddColor(TableLayoutPanel fields, string label, string key)
    {
        var button = new Button { Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat, TextAlign = ContentAlignment.MiddleRight, AccessibleName = label };
        button.Click += (_, _) => ChooseColor(key);
        _colors[key] = button;
        AddRow(fields, label, button);
    }

    private void AddImage(TableLayoutPanel fields)
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3 };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 54));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 64)); panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 26)); panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        var value = new TextBox { Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle, ReadOnly = true, AccessibleName = "背景图片" };
        _text["image"] = value;
        var replace = new Button { Text = "替换…", Dock = DockStyle.Fill, AccessibleName = "替换背景图片" };
        replace.Click += (_, _) => { string? path = _importImage(); if (path is not null) ChangeStyle(new(BackgroundImage: path)); };
        var clear = new Button { Text = "清除", Dock = DockStyle.Fill, AccessibleName = "清除背景图片" };
        clear.Click += (_, _) => ChangeStyle(new(BackgroundImage: string.Empty));
        _imageCommands.Add(replace); _imageCommands.Add(clear);
        panel.Controls.Add(_imagePreview, 0, 0); panel.SetColumnSpan(_imagePreview, 2);
        panel.Controls.Add(value, 0, 1); panel.SetColumnSpan(value, 2); panel.Controls.Add(replace, 0, 2); panel.Controls.Add(clear, 1, 2);
        AddRow(fields, "背景图片", panel, 122);
    }

    private static Button AddCommand(TableLayoutPanel fields, string label, string text, string accessibleDescription, Action action)
    {
        var button = new Button { Text = text, Dock = DockStyle.Fill, AccessibleName = text, AccessibleDescription = accessibleDescription };
        button.Click += (_, _) => action();
        AddRow(fields, label, button, 32);
        return button;
    }

    private static void AddRow(TableLayoutPanel fields, string label, Control editor, int height = 30)
    {
        int row = fields.RowCount++;
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        fields.Controls.Add(new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true }, 0, row);
        fields.Controls.Add(editor, 1, row);
    }

    private void CommitText(string key)
    {
        if (_refreshing || !_text.TryGetValue(key, out TextBox? input) || string.IsNullOrWhiteSpace(input.Text) || input.Text == "多个值") return;
        string value = input.Text.Trim();
        bool valid = key switch
        {
            "x" => ApplyInteger(value, number => _document.ChangeSelectionBounds(new(X: number))),
            "y" => ApplyInteger(value, number => _document.ChangeSelectionBounds(new(Y: number))),
            "width" => ApplyInteger(value, number => _document.ChangeSelectionBounds(new(Width: number)), 8),
            "height" => ApplyInteger(value, number => _document.ChangeSelectionBounds(new(Height: number)), 8),
            "fontSize" => ApplyFloat(value, number => ChangeStyle(new(FontSize: number)), 6F, 72F),
            "opacity" => ApplyInteger(value, number => ChangeStyle(new(OpacityPercent: number)), 0, 100),
            "font" => ApplyString(value, text => ChangeStyle(new(FontName: text))),
            _ => true,
        };
        input.BackColor = valid ? DesktopAuthoringTheme.InputBackground : Color.MistyRose;
        string error = valid ? string.Empty : key is "opacity" ? "请输入 0 到 100" : key is "fontSize" ? "请输入 6 到 72" : "请输入有效数值";
        _errors.SetError(input, error);
        input.AccessibleDescription = error;
    }

    private void CommitChoice(string key)
    {
        if (_refreshing || !_choices.TryGetValue(key, out ComboBox? choice) || choice.SelectedItem is not string text || text == "多个值") return;
        bool value = text == "是";
        switch (key)
        {
            case "visible": _document.SetVisible(_document.Selection, value); break;
            case "locked": _document.SetLocked(_document.Selection, value); break;
            case "bold": ChangeStyle(new(Bold: value)); break;
        }
    }

    private void ChooseColor(string key)
    {
        if (!_colors[key].Enabled) return;
        using var dialog = new ColorDialog { FullOpen = true };
        if (TryParseColor(_colors[key].Text, out Color current)) dialog.Color = current;
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        string value = $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
        ChangeStyle(key == "fore" ? new(ForeColor: value) : new(BackColor: value));
    }

    private void SetEditability(bool editable, bool stateEditable)
    {
        foreach ((string key, TextBox input) in _text) input.Enabled = key is "name" or "image" || editable;
        foreach ((string key, ComboBox choice) in _choices) choice.Enabled = key switch { "locked" => true, "visible" => stateEditable, _ => editable };
        foreach (Button button in _colors.Values) button.Enabled = editable;
        foreach (Button button in _imageCommands) button.Enabled = editable;
        foreach (Button button in _appearanceCommands) button.Enabled = editable;
    }

    private void SetText(string key, object? value)
    {
        string? text = value?.ToString();
        _text[key].Text = text is null ? "多个值" : key == "image" && text.Length == 0 ? "未设置" : text;
        _text[key].BackColor = DesktopAuthoringTheme.InputBackground;
        _errors.SetError(_text[key], string.Empty);
    }

    private void SetChoice(string key, object? value) => _choices[key].SelectedItem = value is bool boolean ? boolean ? "是" : "否" : "多个值";

    private void SetColor(string key, object? value)
    {
        string? raw = value?.ToString();
        string text = raw is null ? "多个值" : raw.Length == 0 ? "未设置" : raw;
        _colors[key].Text = text;
        _colors[key].BackColor = TryParseColor(text, out Color color) ? color : DesktopAuthoringTheme.InputBackground;
        _colors[key].ForeColor = _colors[key].BackColor.GetBrightness() < .5F ? Color.White : DesktopAuthoringTheme.TextPrimary;
    }

    private SelectedControl[] Selected() => _document.Selection.Select(id =>
    {
        CanvasBounds bounds = _document.GetBounds(id);
        LauncherControlAppearance appearance = _appearance.GetAppearance(id);
        return new SelectedControl(id, bounds.X, bounds.Y, bounds.Width, bounds.Height, _document.IsVisible(id), appearance.ForeColor, appearance.BackColor, appearance.FontName, appearance.FontSize, appearance.Bold, appearance.OpacityPercent, appearance.BackgroundImage);
    }).ToArray();
    private void ChangeStyle(LauncherCanvasStyleChange change)
        => _document.ChangeEditableSelection(id => _appearance.SetStyle(id, change));
    private void RefreshImagePreview(SelectedControl[] selected)
    {
        Image? next = null;
        string? common = Common(selected, item => item.BackgroundImage)?.ToString();
        if (!string.IsNullOrWhiteSpace(common)) next = _loadImage(common);
        Image? previous = _imagePreview.Image;
        _imagePreview.Image = next;
        _imagePreview.AccessibleDescription = next is null ? common is null ? "多个背景图片值" : "未设置背景图片" : "已加载背景图片缩略图";
        previous?.Dispose();
    }
    private static object? Common<T>(SelectedControl[] selected, Func<SelectedControl, T> select)
    {
        T first = select(selected[0]);
        return selected.Skip(1).All(item => EqualityComparer<T>.Default.Equals(first, select(item))) ? first : null;
    }

    private static bool ApplyInteger(string value, Action<int> apply, int min = 0, int max = 4096)
    {
        if (!int.TryParse(value, out int parsed) || parsed < min || parsed > max) return false;
        apply(parsed); return true;
    }
    private static bool ApplyFloat(string value, Action<float> apply, float min, float max)
    {
        if (!float.TryParse(value, out float parsed) || parsed < min || parsed > max) return false;
        apply(parsed); return true;
    }
    private static bool ApplyString(string value, Action<string> apply) { apply(value); return true; }
    private static bool TryParseColor(string value, out Color color)
    {
        color = Color.Empty;
        if (value.Length != 7 || value[0] != '#' || !int.TryParse(value[1..], System.Globalization.NumberStyles.HexNumber, null, out int rgb)) return false;
        color = Color.FromArgb((rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255); return true;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _imagePreview.Image?.Dispose();
            _errors.Dispose();
        }
        base.Dispose(disposing);
    }

    private sealed record SelectedControl(
        LauncherControlId Id,
        int X,
        int Y,
        int Width,
        int Height,
        bool Visible,
        string ForeColor,
        string BackColor,
        string FontName,
        float FontSize,
        bool Bold,
        int OpacityPercent,
        string BackgroundImage);
}
