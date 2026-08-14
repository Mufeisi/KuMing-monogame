using FairyGUI;
using Microsoft.Xna.Framework;
using MonoShare.CustomGui;
using Shared.CustomGui;

namespace MonoShare;

internal interface IFairyGuiCustomAssetResolver
{
    bool TryResolve(string assetId, out string packageUrl);
}

internal static class FairyGuiCustomGuiAdapter
{
    internal static MobileCustomGuiHost Create(
        CustomGuiRuntimeDocument document,
        int viewportWidth,
        int viewportHeight,
        IFairyGuiCustomAssetResolver? assetResolver = null) =>
        MobileCustomGuiAdapter.Create(document, viewportWidth, viewportHeight, new FairyGuiCustomGuiFactory(assetResolver));

    internal static void AttachTo(MobileCustomGuiHost host, GComponent parent)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(parent);
        ObjectDisposedException.ThrowIf(host.IsDisposed, host);
        parent.AddChild(((FairyGuiCustomGuiNode)host.Root).Object);
    }
}

internal sealed class FairyGuiCustomGuiFactory : IMobileCustomGuiFactory
{
    private readonly IFairyGuiCustomAssetResolver? _assetResolver;

    internal FairyGuiCustomGuiFactory(IFairyGuiCustomAssetResolver? assetResolver) => _assetResolver = assetResolver;

    public IMobileCustomGuiNode Create(MobileCustomGuiNodeSpec spec)
    {
        GObject value = spec.Kind switch
        {
            MobileCustomGuiNodeKind.Root => new GComponent { name = spec.Id, opaque = false },
            MobileCustomGuiNodeKind.Window => CreateWindow(Require<CustomGuiWindow>(spec), spec),
            MobileCustomGuiNodeKind.Panel => CreatePanel(Require<CustomGuiPanel>(spec), spec),
            MobileCustomGuiNodeKind.Image => CreateImage(Require<CustomGuiImage>(spec)),
            MobileCustomGuiNodeKind.Text => CreateText(Require<CustomGuiText>(spec), spec.Scale),
            MobileCustomGuiNodeKind.Button => CreateButton(Require<CustomGuiButton>(spec), spec),
            MobileCustomGuiNodeKind.TextInput => CreateTextInput(Require<CustomGuiTextInput>(spec), spec.Scale),
            MobileCustomGuiNodeKind.List => CreateList(Require<CustomGuiList>(spec), spec),
            MobileCustomGuiNodeKind.ProgressBar => CreateProgress(Require<CustomGuiProgressBar>(spec), spec),
            MobileCustomGuiNodeKind.ItemSlot => CreateItemSlot(Require<CustomGuiItemSlot>(spec), spec),
            _ => throw new CustomGuiLayoutException("FairyGUI Adapter 不支持该控件类型"),
        };
        value.name = spec.Id;
        value.data = spec;
        value.SetPosition(spec.Bounds.X, spec.Bounds.Y);
        value.SetSize(spec.Bounds.Width, spec.Bounds.Height);
        value.visible = spec.Visible;
        value.sortingOrder = spec.ZIndex;
        return new FairyGuiCustomGuiNode(value, spec);
    }

    private static T Require<T>(MobileCustomGuiNodeSpec spec) where T : CustomGuiElement =>
        spec.Element as T ?? throw new CustomGuiLayoutException("FairyGUI 节点类型与运行描述不匹配");

    private static GComponent CreateWindow(CustomGuiWindow source, MobileCustomGuiNodeSpec spec)
    {
        var window = new CustomGuiClippedComponent(false) { opaque = source.Modal };
        AddBackground(window, spec.Bounds, new Color(32, 37, 46, 245));
        if (!string.IsNullOrWhiteSpace(source.Title))
        {
            GTextField title = CreateLabel(source.Title, 18F * spec.Scale, Color.White);
            title.SetPosition(12F * spec.Scale, 8F * spec.Scale);
            title.SetSize(Math.Max(1F, spec.Bounds.Width - 24F * spec.Scale), Math.Min(spec.Bounds.Height, 30F * spec.Scale));
            window.AddChild(title);
        }
        return window;
    }

    private static GComponent CreatePanel(CustomGuiPanel source, MobileCustomGuiNodeSpec spec)
    {
        var panel = new CustomGuiClippedComponent(source.ClipChildren) { opaque = !string.IsNullOrWhiteSpace(source.BackgroundColor) };
        AddBackground(panel, spec.Bounds, ParseColor(source.BackgroundColor, new Color(23, 29, 38, 235)));
        return panel;
    }

    private GObject CreateImage(CustomGuiImage source)
    {
        var loader = new GLoader
        {
            align = AlignType.Center,
            verticalAlign = VertAlignType.Middle,
            fill = source.Stretch switch
            {
                CustomGuiImageStretch.None => FillType.None,
                CustomGuiImageStretch.Fill => FillType.ScaleFree,
                CustomGuiImageStretch.UniformToFill => FillType.ScaleNoBorder,
                _ => FillType.Scale,
            },
        };
        if (_assetResolver is not null && _assetResolver.TryResolve(source.AssetId, out string packageUrl))
        {
            loader.url = packageUrl;
            return loader;
        }
        loader.Dispose();
        return new CustomGuiImagePlaceholder(source.AlternateText);
    }

    private static GObject CreateText(CustomGuiText source, float scale)
    {
        GTextField field = source.Format == CustomGuiTextFormat.Rich ? new GRichTextField() : new GTextField();
        field.text = source.Content ?? string.Empty;
        field.color = ParseColor(source.Color, Color.White);
        field.textFormat.size = Math.Max(1, (int)Math.Round(source.FontSize * scale));
        if (!string.IsNullOrWhiteSpace(source.FontId)) field.textFormat.font = source.FontId;
        return field;
    }

    private GButton CreateButton(CustomGuiButton source, MobileCustomGuiNodeSpec spec)
    {
        var button = new GButton { enabled = source.Enabled };
        AddBackground(button, spec.Bounds, new Color(36, 93, 130, 255));
        if (!string.IsNullOrWhiteSpace(source.AssetId))
        {
            GObject image = CreateImage(new CustomGuiImage { AssetId = source.AssetId, AlternateText = source.Text, Stretch = CustomGuiImageStretch.Fill });
            image.SetSize(spec.Bounds.Width, spec.Bounds.Height);
            image.touchable = false;
            button.AddChild(image);
        }
        GTextField label = CreateLabel(source.Text, 16F * spec.Scale, Color.White);
        label.align = AlignType.Center;
        label.verticalAlign = VertAlignType.Middle;
        label.SetSize(spec.Bounds.Width, spec.Bounds.Height);
        button.AddChild(label);
        return button;
    }

    private static GTextInput CreateTextInput(CustomGuiTextInput source, float scale) => new()
    {
        promptText = source.Placeholder ?? string.Empty,
        maxLength = Math.Max(0, source.MaxLength),
        displayAsPassword = source.Password,
        singleLine = !source.Multiline,
        color = Color.White,
        textFormat = { size = Math.Max(1, (int)Math.Round(16F * scale)) },
    };

    private GList CreateList(CustomGuiList source, MobileCustomGuiNodeSpec spec)
    {
        var list = new GList
        {
            layout = source.Orientation == CustomGuiListOrientation.Vertical ? ListLayoutType.SingleColumn : ListLayoutType.SingleRow,
        };
        float cursor = 6F * spec.Scale;
        foreach (CustomGuiListItem item in source.Items ?? [])
        {
            float rowWidth = source.Orientation == CustomGuiListOrientation.Vertical
                ? Math.Max(1F, spec.Bounds.Width - 16F * spec.Scale)
                : 120F * spec.Scale;
            float rowHeight = source.Orientation == CustomGuiListOrientation.Vertical
                ? 26F * spec.Scale
                : Math.Max(1F, spec.Bounds.Height - 16F * spec.Scale);
            var row = new GComponent { name = item.Id ?? string.Empty, touchable = true };
            row.SetSize(rowWidth, rowHeight);
            float labelX = 0F;
            if (!string.IsNullOrWhiteSpace(item.AssetId))
            {
                float iconSize = Math.Min(rowHeight, 22F * spec.Scale);
                GObject icon = CreateImage(new CustomGuiImage { AssetId = item.AssetId, AlternateText = item.PrimaryText, Stretch = CustomGuiImageStretch.Uniform });
                icon.SetSize(iconSize, iconSize);
                icon.touchable = false;
                row.AddChild(icon);
                labelX = iconSize + 6F * spec.Scale;
            }
            GTextField label = CreateLabel(
                string.IsNullOrWhiteSpace(item.SecondaryText) ? item.PrimaryText : $"{item.PrimaryText}  {item.SecondaryText}",
                15F * spec.Scale,
                Color.White);
            label.SetPosition(labelX, 0F);
            label.SetSize(Math.Max(1F, rowWidth - labelX), rowHeight);
            row.AddChild(label);
            if (source.Orientation == CustomGuiListOrientation.Vertical)
            {
                row.SetPosition(8F * spec.Scale, cursor);
                cursor += (26F + source.Spacing) * spec.Scale;
            }
            else
            {
                row.SetPosition(cursor, 8F * spec.Scale);
                cursor += (120F + source.Spacing) * spec.Scale;
            }
            list.AddChild(row);
        }
        return list;
    }

    private static GProgressBar CreateProgress(CustomGuiProgressBar source, MobileCustomGuiNodeSpec spec)
    {
        decimal ratio = source.Maximum <= source.Minimum ? 0 : Math.Clamp((source.Value - source.Minimum) / (source.Maximum - source.Minimum), 0, 1);
        var progress = new GProgressBar { max = 100, value = (double)(ratio * 100) };
        AddBackground(progress, spec.Bounds, new Color(18, 24, 32, 255));
        var fill = new GGraph { touchable = false };
        fill.DrawRect(spec.Bounds.Width * (float)ratio, spec.Bounds.Height, 0, Color.Transparent, new Color(42, 139, 189, 255));
        progress.AddChild(fill);
        GTextField label = CreateLabel(source.Text ?? string.Empty, 14F * spec.Scale, Color.White);
        label.align = AlignType.Center;
        label.verticalAlign = VertAlignType.Middle;
        label.SetSize(spec.Bounds.Width, spec.Bounds.Height);
        progress.AddChild(label);
        return progress;
    }

    private GComponent CreateItemSlot(CustomGuiItemSlot source, MobileCustomGuiNodeSpec spec)
    {
        var slot = new CustomGuiClippedComponent(false) { opaque = true };
        AddBackground(slot, spec.Bounds, new Color(24, 30, 39, 255));
        GObject image = CreateImage(new CustomGuiImage { AssetId = source.AssetId, AlternateText = source.DisplayName, Stretch = CustomGuiImageStretch.Uniform });
        float labelHeight = Math.Min(spec.Bounds.Height, 22F * spec.Scale);
        image.SetPosition(4F * spec.Scale, 4F * spec.Scale);
        image.SetSize(Math.Max(1F, spec.Bounds.Width - 8F * spec.Scale), Math.Max(1F, spec.Bounds.Height - labelHeight - 8F * spec.Scale));
        slot.AddChild(image);
        GTextField label = CreateLabel(source.Quantity > 1 ? $"{source.DisplayName} × {source.Quantity}" : source.DisplayName, 13F * spec.Scale, Color.White);
        label.SetPosition(4F * spec.Scale, spec.Bounds.Height - labelHeight);
        label.SetSize(Math.Max(1F, spec.Bounds.Width - 8F * spec.Scale), labelHeight);
        slot.AddChild(label);
        return slot;
    }

    private static GTextField CreateLabel(string text, float fontSize, Color color) => new()
    {
        text = text ?? string.Empty,
        color = color,
        textFormat = { size = Math.Max(1, (int)Math.Round(fontSize)) },
    };

    private static void AddBackground(GComponent parent, MobileCustomGuiBounds bounds, Color color)
    {
        var background = new GGraph { name = "__background", touchable = false };
        background.DrawRect(bounds.Width, bounds.Height, 1, new Color(92, 112, 136, 255), color);
        parent.AddChild(background);
    }

    private static Color ParseColor(string value, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        string hex = value.Trim().TrimStart('#');
        if (hex.Length != 6 && hex.Length != 8) return fallback;
        if (!uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out uint parsed)) return fallback;
        return hex.Length == 8
            ? new Color((byte)(parsed >> 24), (byte)(parsed >> 16), (byte)(parsed >> 8), (byte)parsed)
            : new Color((byte)(parsed >> 16), (byte)(parsed >> 8), (byte)parsed, (byte)255);
    }
}

internal sealed class FairyGuiCustomGuiNode : IMobileCustomGuiNode, IMobileCustomGuiInteractiveNode
{
    private bool _disposed;
    private readonly MobileCustomGuiNodeKind _kind;
    internal FairyGuiCustomGuiNode(GObject value, MobileCustomGuiNodeSpec spec)
    {
        Object = value;
        _kind = spec.Kind;
        if (_kind == MobileCustomGuiNodeKind.Button) Object.onClick.Add(Activate);
        if (_kind == MobileCustomGuiNodeKind.List) WireListRows();
    }
    internal GObject Object { get; }
    public event Action? Activated;
    public event Action<string>? SelectionChanged;

    public void Activate()
    {
        if (!_disposed && Object.enabled) Activated?.Invoke();
    }

    public void Select(string itemId)
    {
        if (_disposed || _kind != MobileCustomGuiNodeKind.List || Object is not GList list ||
            string.IsNullOrWhiteSpace(itemId) || list.GetChild(itemId) is null)
            throw new CustomGuiStateProjectionException("GUI12-CLIENT-SELECTION", $"FairyGUI 选择项不存在：{itemId}");
        SelectionChanged?.Invoke(itemId);
    }

    public void AddChild(IMobileCustomGuiNode child)
    {
        if (Object is not GComponent parent) throw new CustomGuiLayoutException("仅容器控件可以拥有子对象");
        if (child is not FairyGuiCustomGuiNode typed) throw new CustomGuiLayoutException("移动控件工厂不匹配");
        parent.AddChild(typed.Object);
    }

    public void ApplyState(CustomGuiStateEntry state)
    {
        ArgumentNullException.ThrowIfNull(state);
        switch (state.Kind)
        {
            case CustomGuiStateKind.Text:
                if (Object is GTextField text) text.text = state.TextValue;
                else if (Object is GTextInput input) input.text = state.TextValue;
                break;
            case CustomGuiStateKind.Integer:
                if (Object is GTextField integer) integer.text = state.IntegerValue.ToString(System.Globalization.CultureInfo.InvariantCulture);
                break;
            case CustomGuiStateKind.Boolean:
                Object.visible = state.BooleanValue;
                break;
            case CustomGuiStateKind.Progress:
                if (Object is GProgressBar progress)
                {
                    progress.max = Math.Max(1, state.MaximumValue);
                    progress.value = Math.Clamp(state.CurrentValue, 0, progress.max);
                }
                break;
            case CustomGuiStateKind.List:
                ApplyList(Object as GList, state.ListItems);
                WireListRows();
                break;
            case CustomGuiStateKind.ItemSlots:
                ApplyItemSlots(Object as GComponent, state.ItemSlots);
                break;
            case CustomGuiStateKind.ButtonVisible:
                Object.visible = state.BooleanValue;
                break;
            case CustomGuiStateKind.ButtonEnabled:
                Object.enabled = state.BooleanValue;
                break;
        }
    }

    private static void ApplyList(GList? list, IReadOnlyList<CustomGuiStateListItem> items)
    {
        if (list is null) return;
        list.RemoveChildren(0, -1, dispose: true);
        foreach (CustomGuiStateListItem item in items ?? [])
        {
            var row = new GTextField { name = item.Id, text = string.IsNullOrWhiteSpace(item.SecondaryText) ? item.PrimaryText : $"{item.PrimaryText}  {item.SecondaryText}" };
            row.SetSize(Math.Max(1, list.width), 26);
            list.AddChild(row);
        }
    }

    private void WireListRows()
    {
        if (Object is not GList list) return;
        for (int index = 0; index < list.numChildren; index++)
        {
            GObject row = list.GetChildAt(index);
            string itemId = row.name ?? string.Empty;
            row.onClick.Add(() => Select(itemId));
        }
    }

    private static void ApplyItemSlots(GComponent? slot, IReadOnlyList<CustomGuiStateItemSlot> items)
    {
        if (slot is null) return;
        CustomGuiStateItemSlot? first = items?.FirstOrDefault();
        slot.enabled = first?.Enabled ?? false;
        GTextField? label = slot.GetChildAt(slot.numChildren - 1) as GTextField;
        if (label is not null) label.text = first is null ? string.Empty : first.Quantity > 1 ? $"{first.DisplayName} × {first.Quantity}" : first.DisplayName;
    }

    public void Dispose()
    {
        if (_disposed || Object._disposed)
        {
            _disposed = true;
            return;
        }
        Object.Dispose();
        _disposed = true;
    }
}

internal sealed class CustomGuiClippedComponent : GComponent
{
    internal CustomGuiClippedComponent(bool clipChildren)
    {
        if (clipChildren) SetupOverflow(OverflowType.Hidden);
    }
}

internal sealed class CustomGuiImagePlaceholder : GComponent
{
    private readonly GGraph _background;
    private readonly GTextField _label;

    internal CustomGuiImagePlaceholder(string? alternateText)
    {
        opaque = true;
        _background = new GGraph { touchable = false };
        _label = new GTextField
        {
            text = string.IsNullOrWhiteSpace(alternateText) ? "图片资源" : alternateText,
            color = new Color(196, 208, 220, 255),
            align = AlignType.Center,
            verticalAlign = VertAlignType.Middle,
            touchable = false,
        };
        _background.DrawRect(1, 1, 1, new Color(92, 112, 136, 255), new Color(44, 57, 72, 255));
        AddChild(_background);
        AddChild(_label);
    }

    protected override void HandleSizeChanged()
    {
        base.HandleSizeChanged();
        _background.SetSize(Math.Max(1F, width), Math.Max(1F, height));
        _label.SetSize(Math.Max(1F, width), Math.Max(1F, height));
    }
}
