#nullable enable

using Client.MirControls;
using Client.MirGraphics;
using Shared.CustomGui;

namespace Client.CustomGui;

internal readonly record struct PcCustomGuiAsset(MLibrary Library, int Index);

internal interface IPcCustomGuiAssetResolver
{
    bool TryResolve(string assetId, out PcCustomGuiAsset asset);
}

internal static class PcCustomGuiAdapter
{
    internal static PcCustomGuiHost Create(CustomGuiRuntimeDocument document, Size viewport, IPcCustomGuiAssetResolver? assetResolver = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (viewport.Width <= 0 || viewport.Height <= 0) throw new ArgumentOutOfRangeException(nameof(viewport));
        IReadOnlyDictionary<string, CustomGuiResolvedBounds> layout = CustomGuiLayoutEngine.Resolve(document);
        float scale = Math.Min(viewport.Width / (float)document.Viewport.ReferenceWidth, viewport.Height / (float)document.Viewport.ReferenceHeight);
        var offset = new Point(
            (int)Math.Round((viewport.Width - document.Viewport.ReferenceWidth * scale) / 2f),
            (int)Math.Round((viewport.Height - document.Viewport.ReferenceHeight * scale) / 2f));
        var root = new MirControl { Size = viewport };
        var controls = new Dictionary<string, MirControl>(StringComparer.Ordinal);
        try
        {
            var pending = new List<CustomGuiElement>(document.Elements);
            while (pending.Count > 0)
            {
                int before = pending.Count;
                foreach (CustomGuiElement element in pending.ToArray())
                {
                    if (!string.IsNullOrWhiteSpace(element.ParentId) && !controls.ContainsKey(element.ParentId)) continue;
                    MirControl parent = string.IsNullOrWhiteSpace(element.ParentId) ? root : controls[element.ParentId];
                    MirControl control = CreateControl(element, scale, assetResolver);
                    CustomGuiResolvedBounds absolute = layout[element.Id];
                    CustomGuiResolvedBounds parentAbsolute = string.IsNullOrWhiteSpace(element.ParentId)
                        ? new(0, 0, document.Viewport.ReferenceWidth, document.Viewport.ReferenceHeight)
                        : layout[element.ParentId];
                    control.Location = new Point(
                        Scale(absolute.X - parentAbsolute.X, scale) + (ReferenceEquals(parent, root) ? offset.X : 0),
                        Scale(absolute.Y - parentAbsolute.Y, scale) + (ReferenceEquals(parent, root) ? offset.Y : 0));
                    control.Size = new Size(Math.Max(1, Scale(absolute.Width, scale)), Math.Max(1, Scale(absolute.Height, scale)));
                    control.Visible = element.Visible;
                    control.Parent = parent;
                    controls.Add(element.Id, control);
                    pending.Remove(element);
                }
                if (pending.Count == before) throw new CustomGuiLayoutException("运行描述的父级顺序无法物化");
            }
            return new PcCustomGuiHost(root, controls, document, scale, offset);
        }
        catch
        {
            root.Dispose();
            throw;
        }
    }

    private static MirControl CreateControl(CustomGuiElement element, float scale, IPcCustomGuiAssetResolver? resolver) => element switch
    {
        CustomGuiWindow value => new PcCustomGuiWindowControl(value.Title, value.Modal, scale),
        CustomGuiPanel value => new PcCustomGuiPanelControl(value.BackgroundColor, value.ClipChildren),
        CustomGuiImage value => new PcCustomGuiImageControl(value.AssetId, value.AlternateText, value.Stretch, Resolve(value.AssetId, resolver), scale),
        CustomGuiText value => CreateLabel(value, scale),
        CustomGuiButton value => new PcCustomGuiButtonControl(value.Text, value.ActionId, value.Enabled, scale),
        CustomGuiTextInput value => new PcCustomGuiTextInputControl(value.Placeholder, value.MaxLength, value.Multiline, value.Password, value.BindingKey, scale),
        CustomGuiList value => new PcCustomGuiListControl(value.Orientation, value.Spacing, value.Items, scale),
        CustomGuiProgressBar value => new PcCustomGuiProgressBarControl(value.Minimum, value.Maximum, value.Value, value.Text, value.BindingKey, scale),
        CustomGuiItemSlot value => new PcCustomGuiItemSlotControl(value.AssetId, value.DisplayName, value.Quantity, value.BindingKey, Resolve(value.AssetId, resolver), scale),
        _ => throw new CustomGuiLayoutException("PC Adapter 不支持该控件类型"),
    };

    private static MirLabel CreateLabel(CustomGuiText value, float scale)
    {
        var label = new MirLabel
        {
            AutoSize = false,
            Text = value.Content,
            ForeColour = ParseColor(value.Color, Color.White),
        };
        if (value.FontSize > 0) label.Font = new Font(Settings.FontName, Math.Max(1, value.FontSize * scale));
        return label;
    }

    private static PcCustomGuiAsset? Resolve(string assetId, IPcCustomGuiAssetResolver? resolver) =>
        resolver is not null && !string.IsNullOrWhiteSpace(assetId) && resolver.TryResolve(assetId, out PcCustomGuiAsset asset) ? asset : null;

    internal static Color ParseColor(string value, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        try { return ColorTranslator.FromHtml(value); }
        catch (Exception error) when (error is ArgumentException or FormatException) { return fallback; }
    }

    internal static MirLabel ScaleLabel(MirLabel label, float scale)
    {
        label.Font = new Font(Settings.FontName, Math.Max(1, 8F * scale));
        return label;
    }

    internal static int ScaleMetric(int value, float scale) => Scale(value, scale);

    private static int Scale(int value, float scale) => (int)Math.Round(value * scale);
}

internal sealed class PcCustomGuiHost : IDisposable, ICustomGuiStateProjectionTarget
{
    private readonly IReadOnlyDictionary<string, string> _bindingTargets;
    private IReadOnlyDictionary<string, CustomGuiStateEntry> _state = new Dictionary<string, CustomGuiStateEntry>();
    internal PcCustomGuiHost(MirControl root, IReadOnlyDictionary<string, MirControl> controls, CustomGuiRuntimeDocument document, float scale, Point viewportOffset)
    {
        Root = root;
        Controls = controls;
        Scale = scale;
        ViewportOffset = viewportOffset;
        _bindingTargets = CustomGuiStateBindingCatalog.Create(document);
    }

    internal MirControl Root { get; }
    internal IReadOnlyDictionary<string, MirControl> Controls { get; }
    internal float Scale { get; }
    internal Point ViewportOffset { get; }
    internal IReadOnlyDictionary<string, CustomGuiStateEntry> ProjectedState => _state;
    public void Apply(IReadOnlyDictionary<string, CustomGuiStateEntry> state)
    {
        ArgumentNullException.ThrowIfNull(state);
        foreach (string key in state.Keys)
            if (!_bindingTargets.TryGetValue(key, out string? id) || !Controls.ContainsKey(id))
                throw new CustomGuiStateProjectionException("GUI10-STATE-BINDING", $"PC 端不存在绑定目标：{key}");
        foreach ((string key, CustomGuiStateEntry value) in state) ApplyToControl(Controls[_bindingTargets[key]], value);
        _state = state;
    }

    private static void ApplyToControl(MirControl control, CustomGuiStateEntry state)
    {
        switch (state.Kind)
        {
            case CustomGuiStateKind.Text when control is MirLabel text: text.Text = state.TextValue; break;
            case CustomGuiStateKind.Text when control is PcCustomGuiTextInputControl input: input.ApplyText(state.TextValue); break;
            case CustomGuiStateKind.Integer when control is MirLabel integer: integer.Text = state.IntegerValue.ToString(System.Globalization.CultureInfo.InvariantCulture); break;
            case CustomGuiStateKind.Boolean: control.Visible = state.BooleanValue; break;
            case CustomGuiStateKind.Progress when control is PcCustomGuiProgressBarControl progress: progress.Apply(state.CurrentValue, state.MaximumValue); break;
            case CustomGuiStateKind.List when control is PcCustomGuiListControl list: list.Apply(state.ListItems); break;
            case CustomGuiStateKind.ItemSlots when control is PcCustomGuiItemSlotControl slot: slot.Apply(state.ItemSlots); break;
            case CustomGuiStateKind.ButtonVisible: control.Visible = state.BooleanValue; break;
            case CustomGuiStateKind.ButtonEnabled: control.Enabled = state.BooleanValue; break;
        }
    }
    internal void AttachTo(MirControl parent)
    {
        ArgumentNullException.ThrowIfNull(parent);
        Root.Parent = parent;
    }
    public void Dispose() => Root.Dispose();
}

internal class PcCustomGuiPanelControl : MirControl
{
    internal PcCustomGuiPanelControl(string backgroundColor, bool clipChildren)
    {
        ClipChildren = clipChildren;
        BackColour = PcCustomGuiAdapter.ParseColor(backgroundColor, Color.FromArgb(34, 40, 49));
        Border = true;
        BorderColour = Color.FromArgb(92, 112, 136);
        DrawControlTexture = true;
    }

    internal bool ClipChildren { get; }
}

internal sealed class PcCustomGuiWindowControl : PcCustomGuiPanelControl
{
    private readonly MirLabel? _title;
    private readonly int _horizontalPadding;
    private readonly int _titleHeight;

    internal PcCustomGuiWindowControl(string title, bool modal, float scale) : base("#20252E", false)
    {
        Title = title ?? string.Empty;
        Modal = modal;
        _horizontalPadding = PcCustomGuiAdapter.ScaleMetric(24, scale);
        _titleHeight = PcCustomGuiAdapter.ScaleMetric(28, scale);
        _title = null;
        if (Title.Length > 0) _title = PcCustomGuiAdapter.ScaleLabel(new MirLabel { Text = Title, AutoSize = false, Location = new Point(PcCustomGuiAdapter.ScaleMetric(12, scale), PcCustomGuiAdapter.ScaleMetric(8, scale)), Parent = this }, scale);
    }

    internal string Title { get; }

    protected override void OnSizeChanged()
    {
        base.OnSizeChanged();
        if (_title is not null) _title.Size = new Size(Math.Max(1, Size.Width - _horizontalPadding), Math.Min(_titleHeight, Size.Height));
    }
}

internal sealed class PcCustomGuiImageControl : MirControl
{
    private readonly PcCustomGuiAsset? _asset;
    private readonly MirLabel? _fallback;

    internal PcCustomGuiImageControl(string assetId, string alternateText, CustomGuiImageStretch stretch, PcCustomGuiAsset? asset, float scale)
    {
        AssetId = assetId ?? string.Empty;
        Stretch = stretch;
        _asset = asset;
        if (!_asset.HasValue)
        {
            BackColour = Color.FromArgb(44, 57, 72);
            Border = true;
            BorderColour = Color.FromArgb(92, 112, 136);
            DrawControlTexture = true;
            _fallback = PcCustomGuiAdapter.ScaleLabel(new MirLabel
            {
                Text = string.IsNullOrWhiteSpace(alternateText) ? "图片资源" : alternateText,
                AutoSize = false,
                ForeColour = Color.FromArgb(196, 208, 220),
                DrawFormat = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter,
                Parent = this,
            }, scale);
        }
    }

    internal string AssetId { get; }
    internal CustomGuiImageStretch Stretch { get; }
    internal bool AssetResolved => _asset.HasValue;

    protected override void OnSizeChanged()
    {
        base.OnSizeChanged();
        if (_fallback is not null) _fallback.Size = Size;
    }

    protected internal override void DrawControl()
    {
        base.DrawControl();
        if (_asset is PcCustomGuiAsset asset)
            asset.Library.Draw(asset.Index, DisplayLocation, Size, Color.White);
    }
}

internal sealed class PcCustomGuiButtonControl : PcCustomGuiPanelControl
{
    private readonly MirLabel _label;

    internal PcCustomGuiButtonControl(string text, string actionId, bool enabled, float scale) : base("#245D82", false)
    {
        Text = text ?? string.Empty;
        ActionId = actionId ?? string.Empty;
        Enabled = enabled;
        _label = PcCustomGuiAdapter.ScaleLabel(new MirLabel { Text = Text, AutoSize = false, DrawFormat = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter, Parent = this }, scale);
    }

    internal string Text { get; }
    internal string ActionId { get; }

    protected override void OnSizeChanged()
    {
        base.OnSizeChanged();
        if (_label is not null) _label.Size = Size;
    }
}

internal sealed class PcCustomGuiTextInputControl : PcCustomGuiPanelControl
{
    private readonly MirLabel _placeholder;
    private readonly int _horizontalPadding;
    private readonly int _verticalPadding;

    internal PcCustomGuiTextInputControl(string placeholder, int maxLength, bool multiline, bool password, string bindingKey, float scale) : base("#11151C", false)
    {
        Placeholder = placeholder ?? string.Empty;
        _horizontalPadding = PcCustomGuiAdapter.ScaleMetric(16, scale);
        _verticalPadding = PcCustomGuiAdapter.ScaleMetric(8, scale);
        MaxLength = maxLength;
        Multiline = multiline;
        Password = password;
        BindingKey = bindingKey ?? string.Empty;
        _placeholder = PcCustomGuiAdapter.ScaleLabel(new MirLabel { Text = Placeholder, AutoSize = false, ForeColour = Color.Gray, Location = new Point(PcCustomGuiAdapter.ScaleMetric(8, scale), PcCustomGuiAdapter.ScaleMetric(4, scale)), Parent = this }, scale);
    }

    internal string Placeholder { get; }
    internal int MaxLength { get; }
    internal bool Multiline { get; }
    internal bool Password { get; }
    internal string BindingKey { get; }
    internal void ApplyText(string value) { _placeholder.Text = value ?? string.Empty; _placeholder.ForeColour = Color.White; }

    protected override void OnSizeChanged()
    {
        base.OnSizeChanged();
        if (_placeholder is not null) _placeholder.Size = new Size(Math.Max(1, Size.Width - _horizontalPadding), Math.Max(1, Size.Height - _verticalPadding));
    }
}

internal sealed class PcCustomGuiListControl : PcCustomGuiPanelControl
{
    internal PcCustomGuiListControl(CustomGuiListOrientation orientation, int spacing, IReadOnlyList<CustomGuiListItem> items, float scale) : base("#171D26", true)
    {
        Orientation = orientation;
        Spacing = spacing;
        StaticItems = items?.ToArray() ?? [];
        int offset = PcCustomGuiAdapter.ScaleMetric(6, scale);
        foreach (CustomGuiListItem item in StaticItems)
        {
            MirLabel label = PcCustomGuiAdapter.ScaleLabel(new MirLabel { Text = string.IsNullOrWhiteSpace(item.SecondaryText) ? item.PrimaryText : $"{item.PrimaryText}  {item.SecondaryText}", AutoSize = false, Parent = this }, scale);
            int itemHeight = PcCustomGuiAdapter.ScaleMetric(24, scale), scaledSpacing = PcCustomGuiAdapter.ScaleMetric(spacing, scale);
            if (orientation == CustomGuiListOrientation.Vertical) { label.Location = new Point(PcCustomGuiAdapter.ScaleMetric(8, scale), offset); label.Size = new Size(PcCustomGuiAdapter.ScaleMetric(240, scale), itemHeight); offset += itemHeight + scaledSpacing; }
            else { int itemWidth = PcCustomGuiAdapter.ScaleMetric(120, scale); label.Location = new Point(offset, PcCustomGuiAdapter.ScaleMetric(8, scale)); label.Size = new Size(itemWidth, itemHeight); offset += itemWidth + scaledSpacing; }
        }
    }

    internal CustomGuiListOrientation Orientation { get; }
    internal int Spacing { get; }
    internal IReadOnlyList<CustomGuiListItem> StaticItems { get; }
    internal void Apply(IReadOnlyList<CustomGuiStateListItem> items)
    {
        foreach (MirControl child in Controls.ToArray()) child.Dispose();
        int y = 6;
        foreach (CustomGuiStateListItem item in items ?? [])
        {
            new MirLabel { Text = string.IsNullOrWhiteSpace(item.SecondaryText) ? item.PrimaryText : $"{item.PrimaryText}  {item.SecondaryText}", AutoSize = false, Location = new Point(8, y), Size = new Size(Math.Max(1, Size.Width - 16), 24), Parent = this };
            y += 24 + Spacing;
        }
    }
}

internal sealed class PcCustomGuiProgressBarControl : PcCustomGuiPanelControl
{
    internal PcCustomGuiProgressBarControl(decimal minimum, decimal maximum, decimal value, string text, string bindingKey, float scale) : base("#121820", false)
    {
        Minimum = minimum; Maximum = maximum; Value = value; Text = text ?? string.Empty; BindingKey = bindingKey ?? string.Empty;
        Ratio = maximum <= minimum ? 0 : Math.Clamp((value - minimum) / (maximum - minimum), 0, 1);
        new MirControl { BackColour = Color.FromArgb(42, 139, 189), DrawControlTexture = true, Parent = this };
        PcCustomGuiAdapter.ScaleLabel(new MirLabel { Text = Text, AutoSize = false, DrawFormat = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter, Parent = this }, scale);
    }

    internal decimal Minimum { get; }
    internal decimal Maximum { get; }
    internal decimal Value { get; }
    internal decimal Ratio { get; }
    internal string Text { get; }
    internal string BindingKey { get; }
    internal void Apply(long current, long maximum)
    {
        decimal ratio = maximum <= 0 ? 0 : Math.Clamp((decimal)current / maximum, 0, 1);
        if (Controls.Count > 0) Controls[0].Size = new Size((int)Math.Round(Size.Width * ratio), Size.Height);
        if (Controls.Count > 1 && Controls[1] is MirLabel label) label.Text = $"{current}/{maximum}";
    }

    protected override void OnSizeChanged()
    {
        base.OnSizeChanged();
        if (Controls.Count > 0) Controls[0].Size = new Size((int)Math.Round(Size.Width * Ratio), Size.Height);
        if (Controls.Count > 1) Controls[1].Size = Size;
    }
}

internal sealed class PcCustomGuiItemSlotControl : PcCustomGuiPanelControl
{
    private readonly int _padding;
    private readonly int _labelHeight;
    private readonly int _labelReserve;

    internal PcCustomGuiItemSlotControl(string assetId, string displayName, int quantity, string bindingKey, PcCustomGuiAsset? asset, float scale) : base("#181E27", false)
    {
        AssetId = assetId ?? string.Empty; DisplayName = displayName ?? string.Empty; Quantity = quantity; BindingKey = bindingKey ?? string.Empty;
        _padding = PcCustomGuiAdapter.ScaleMetric(4, scale);
        _labelHeight = PcCustomGuiAdapter.ScaleMetric(20, scale);
        _labelReserve = PcCustomGuiAdapter.ScaleMetric(28, scale);
        new PcCustomGuiImageControl(AssetId, DisplayName, CustomGuiImageStretch.Uniform, asset, scale) { Location = new Point(PcCustomGuiAdapter.ScaleMetric(4, scale), PcCustomGuiAdapter.ScaleMetric(4, scale)), Parent = this };
        PcCustomGuiAdapter.ScaleLabel(new MirLabel { Text = Quantity > 1 ? $"{DisplayName} × {Quantity}" : DisplayName, AutoSize = false, Parent = this }, scale);
    }

    internal string AssetId { get; }
    internal string DisplayName { get; }
    internal int Quantity { get; }
    internal string BindingKey { get; }
    internal void Apply(IReadOnlyList<CustomGuiStateItemSlot> items)
    {
        CustomGuiStateItemSlot? first = items?.FirstOrDefault();
        Enabled = first?.Enabled ?? false;
        if (Controls.Count > 1 && Controls[1] is MirLabel label)
            label.Text = first is null ? string.Empty : first.Quantity > 1 ? $"{first.DisplayName} × {first.Quantity}" : first.DisplayName;
    }

    protected override void OnSizeChanged()
    {
        base.OnSizeChanged();
        if (Controls.Count > 0) Controls[0].Size = new Size(Math.Max(1, Size.Width - _padding * 2), Math.Max(1, Size.Height - _labelReserve));
        if (Controls.Count > 1) { Controls[1].Location = new Point(_padding, Math.Max(0, Size.Height - _labelHeight - _padding)); Controls[1].Size = new Size(Math.Max(1, Size.Width - _padding * 2), _labelHeight); }
    }
}
