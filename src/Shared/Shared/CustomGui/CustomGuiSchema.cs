using System.Text.Json.Serialization;

namespace Shared.CustomGui;

public static class CustomGuiSchema
{
    public const int CurrentVersion = 1;
}

public enum CustomGuiScaleMode
{
    [JsonStringEnumMemberName("fit")]
    Fit,
}

public enum CustomGuiSafeAreaMode
{
    [JsonStringEnumMemberName("required")]
    Required,
}

public enum CustomGuiHorizontalAnchor
{
    [JsonStringEnumMemberName("left")]
    Left,
    [JsonStringEnumMemberName("center")]
    Center,
    [JsonStringEnumMemberName("right")]
    Right,
    [JsonStringEnumMemberName("stretch")]
    Stretch,
}

public enum CustomGuiVerticalAnchor
{
    [JsonStringEnumMemberName("top")]
    Top,
    [JsonStringEnumMemberName("center")]
    Center,
    [JsonStringEnumMemberName("bottom")]
    Bottom,
    [JsonStringEnumMemberName("stretch")]
    Stretch,
}

public enum CustomGuiFlowDirection
{
    [JsonStringEnumMemberName("none")]
    None,
    [JsonStringEnumMemberName("horizontal")]
    Horizontal,
    [JsonStringEnumMemberName("vertical")]
    Vertical,
}

public enum CustomGuiImageStretch
{
    [JsonStringEnumMemberName("none")]
    None,
    [JsonStringEnumMemberName("fill")]
    Fill,
    [JsonStringEnumMemberName("uniform")]
    Uniform,
    [JsonStringEnumMemberName("uniformToFill")]
    UniformToFill,
}

public enum CustomGuiTextFormat
{
    [JsonStringEnumMemberName("plain")]
    Plain,
    [JsonStringEnumMemberName("rich")]
    Rich,
}

public enum CustomGuiListOrientation
{
    [JsonStringEnumMemberName("horizontal")]
    Horizontal,
    [JsonStringEnumMemberName("vertical")]
    Vertical,
}

public readonly record struct CustomGuiThickness(int Left, int Top, int Right, int Bottom)
{
    public static CustomGuiThickness Zero => default;
}

public readonly record struct CustomGuiLayout(
    int X,
    int Y,
    int Width,
    int Height,
    CustomGuiHorizontalAnchor HorizontalAnchor = CustomGuiHorizontalAnchor.Left,
    CustomGuiVerticalAnchor VerticalAnchor = CustomGuiVerticalAnchor.Top,
    CustomGuiThickness Margin = default);

public readonly record struct CustomGuiFlow(
    CustomGuiFlowDirection Direction,
    int Spacing,
    CustomGuiThickness Padding);

public sealed record CustomGuiViewport(
    int ReferenceWidth,
    int ReferenceHeight,
    CustomGuiScaleMode ScaleMode,
    CustomGuiSafeAreaMode SafeArea);

public sealed record CustomGuiListItem(
    string Id,
    string PrimaryText,
    string SecondaryText,
    string AssetId);
