using System.Text.Json.Serialization;

namespace Shared.CustomGui;

public sealed class CustomGuiRuntimeDocument
{
    [JsonRequired]
    public int SchemaVersion { get; set; } = CustomGuiSchema.CurrentVersion;
    [JsonRequired]
    public string DocumentId { get; set; } = string.Empty;
    [JsonRequired]
    public long Revision { get; set; }
    [JsonRequired]
    public CustomGuiViewport Viewport { get; set; } = new(1280, 720, CustomGuiScaleMode.Fit, CustomGuiSafeAreaMode.Required);
    [JsonRequired]
    public List<CustomGuiElement> Elements { get; set; } = new();
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type", IgnoreUnrecognizedTypeDiscriminators = false)]
[JsonDerivedType(typeof(CustomGuiWindow), "window")]
[JsonDerivedType(typeof(CustomGuiPanel), "panel")]
[JsonDerivedType(typeof(CustomGuiImage), "image")]
[JsonDerivedType(typeof(CustomGuiText), "text")]
[JsonDerivedType(typeof(CustomGuiButton), "button")]
[JsonDerivedType(typeof(CustomGuiTextInput), "textInput")]
[JsonDerivedType(typeof(CustomGuiList), "list")]
[JsonDerivedType(typeof(CustomGuiProgressBar), "progressBar")]
[JsonDerivedType(typeof(CustomGuiItemSlot), "itemSlot")]
public abstract class CustomGuiElement
{
    [JsonRequired]
    public string Id { get; set; } = string.Empty;
    public string ParentId { get; set; }
    [JsonRequired]
    public CustomGuiLayout Layout { get; set; }
    public bool Visible { get; set; } = true;
    public int ZIndex { get; set; }
}

public sealed class CustomGuiWindow : CustomGuiElement
{
    public string Title { get; set; } = string.Empty;
    public bool Modal { get; set; }
}

public sealed class CustomGuiPanel : CustomGuiElement
{
    public CustomGuiFlow Flow { get; set; }
    public bool ClipChildren { get; set; }
    public string BackgroundColor { get; set; }
}

public sealed class CustomGuiImage : CustomGuiElement
{
    public string AssetId { get; set; } = string.Empty;
    public CustomGuiImageStretch Stretch { get; set; } = CustomGuiImageStretch.Uniform;
    public string AlternateText { get; set; }
}

public sealed class CustomGuiText : CustomGuiElement
{
    public string Content { get; set; } = string.Empty;
    public CustomGuiTextFormat Format { get; set; }
    public string FontId { get; set; }
    public float FontSize { get; set; } = 14;
    public string Color { get; set; }
}

public sealed class CustomGuiButton : CustomGuiElement
{
    public string Text { get; set; } = string.Empty;
    public string ActionId { get; set; }
    public string AssetId { get; set; }
    public bool Enabled { get; set; } = true;
}

public sealed class CustomGuiTextInput : CustomGuiElement
{
    public string Placeholder { get; set; } = string.Empty;
    public int MaxLength { get; set; }
    public bool Multiline { get; set; }
    public bool Password { get; set; }
    public string BindingKey { get; set; }
}

public sealed class CustomGuiList : CustomGuiElement
{
    public CustomGuiListOrientation Orientation { get; set; } = CustomGuiListOrientation.Vertical;
    public int Spacing { get; set; }
    public string SelectionBindingKey { get; set; }
    public List<CustomGuiListItem> Items { get; set; } = new();
}

public sealed class CustomGuiProgressBar : CustomGuiElement
{
    public decimal Minimum { get; set; }
    public decimal Maximum { get; set; } = 100;
    public decimal Value { get; set; }
    public string Text { get; set; }
    public string BindingKey { get; set; }
}

public sealed class CustomGuiItemSlot : CustomGuiElement
{
    public string AssetId { get; set; }
    public string DisplayName { get; set; }
    public int Quantity { get; set; }
    public string BindingKey { get; set; }
}
