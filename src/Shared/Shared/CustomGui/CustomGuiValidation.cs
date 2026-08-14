#nullable enable

using System.Text.RegularExpressions;

namespace Shared.CustomGui;

public static class CustomGuiValidationLimits
{
    public const int MaximumDocumentBytes = 512 * 1024;
    public const long MaximumPackageBytes = 32L * 1024 * 1024;
    public const int MaximumElements = 256;
    public const int MaximumDepth = 12;
    public const int MaximumListItems = 128;
    public const int MaximumTotalListItems = 512;
    public const int MaximumTextLength = 4096;
    public const int MaximumTotalTextLength = 64 * 1024;
    public const int MaximumInputLength = 256;
    public const int MaximumResourceBindings = 256;
    public const int MaximumResourceBindingsBytes = 128 * 1024;
    public const int MaximumArchiveEntries = 512;
    public const long MaximumUncompressedPackageBytes = 64L * 1024 * 1024;
    public const int MaximumCoordinateMagnitude = 16384;
}
public sealed record CustomGuiValidationDiagnostic(string Code, string Source, string Message);

public sealed class CustomGuiValidationReport
{
    internal CustomGuiValidationReport(IReadOnlyList<CustomGuiValidationDiagnostic> diagnostics) => Diagnostics = diagnostics;
    public IReadOnlyList<CustomGuiValidationDiagnostic> Diagnostics { get; }
    public bool IsValid => Diagnostics.Count == 0;
}

public sealed class CustomGuiValidationException : Exception
{
    public CustomGuiValidationException(string code, string message, Exception? innerException = null)
        : base($"{code}: {message}", innerException) => Code = code;

    public string Code { get; }
}

public static class CustomGuiValidationPolicy
{
    private static readonly Regex StableId = new("^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$", RegexOptions.CultureInvariant);
    private static readonly Regex ResourceId = new("^[A-Za-z0-9][A-Za-z0-9._/-]{0,127}$", RegexOptions.CultureInvariant);
    private static readonly Regex Color = new("^#[0-9A-Fa-f]{6}([0-9A-Fa-f]{2})?$", RegexOptions.CultureInvariant);

    public static CustomGuiValidationReport Validate(CustomGuiRuntimeDocument? document, CustomGuiResourceCatalog? resources)
    {
        var diagnostics = new List<CustomGuiValidationDiagnostic>();
        if (document is null)
        {
            Add(diagnostics, "GUI05-DOC-001", "document", "运行描述为空");
            return new CustomGuiValidationReport(diagnostics);
        }
        resources ??= CustomGuiResourceCatalog.Empty;
        if (document.SchemaVersion != CustomGuiSchema.CurrentVersion)
            Add(diagnostics, "GUI05-DOC-001", "schemaVersion", "Schema 版本不受支持");
        if (!StableId.IsMatch(document.DocumentId ?? string.Empty))
            Add(diagnostics, "GUI05-DOC-001", "documentId", "文档标识无效");
        if (document.Revision < 0) Add(diagnostics, "GUI05-DOC-001", "revision", "文档修订号不得为负数");
        if (document.Viewport is null)
        {
            Add(diagnostics, "GUI05-LAYOUT-001", "viewport", "参考视口为空");
            return new CustomGuiValidationReport(diagnostics);
        }
        ValidateViewport(document.Viewport, diagnostics);
        if (document.Elements is null)
        {
            Add(diagnostics, "GUI05-GRAPH-001", "elements", "对象列表为空");
            return new CustomGuiValidationReport(diagnostics);
        }
        if (document.Elements.Count == 0 || document.Elements.Count > CustomGuiValidationLimits.MaximumElements)
        {
            Add(diagnostics, "GUI05-LIMIT-001", "elements", $"对象数量必须在 1..{CustomGuiValidationLimits.MaximumElements} 之间");
            return new CustomGuiValidationReport(diagnostics);
        }

        var byId = new Dictionary<string, CustomGuiElement>(StringComparer.Ordinal);
        foreach (CustomGuiElement? element in document.Elements)
        {
            if (element is null || !StableId.IsMatch(element.Id ?? string.Empty))
            {
                Add(diagnostics, "GUI05-GRAPH-001", element?.Id ?? "elements", "对象标识无效");
                continue;
            }
            string elementId = element.Id!;
            if (!byId.TryAdd(elementId, element)) Add(diagnostics, "GUI05-GRAPH-001", elementId, "对象标识重复");
            ValidateElementFields(element, resources, diagnostics);
        }
        if (diagnostics.Any(item => item.Code == "GUI05-GRAPH-001" && item.Message.Contains("标识", StringComparison.Ordinal)))
            return new CustomGuiValidationReport(diagnostics);

        ValidateGraph(document, byId, diagnostics);
        ValidateResolvedBounds(document, byId, diagnostics);
        ValidateTotalBudgets(document, diagnostics);
        return new CustomGuiValidationReport(diagnostics);
    }

    public static void EnsureValid(CustomGuiRuntimeDocument document, CustomGuiResourceCatalog resources)
    {
        CustomGuiValidationReport report = Validate(document, resources);
        if (!report.IsValid)
        {
            CustomGuiValidationDiagnostic first = report.Diagnostics[0];
            throw new CustomGuiValidationException(first.Code, $"{first.Source}: {first.Message}");
        }
    }

    internal static bool IsLogicalResourceId(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        ResourceId.IsMatch(value) &&
        !value.Contains("..", StringComparison.Ordinal) &&
        !value.Contains(":", StringComparison.Ordinal) &&
        !value.StartsWith('/') &&
        !value.Contains('\\');

    private static void ValidateViewport(CustomGuiViewport viewport, List<CustomGuiValidationDiagnostic> diagnostics)
    {
        if (viewport.ReferenceWidth < 320 || viewport.ReferenceWidth > 4096 || viewport.ReferenceHeight < 240 || viewport.ReferenceHeight > 4096)
            Add(diagnostics, "GUI05-LAYOUT-001", "viewport", "参考视口尺寸超出 320×240..4096×4096 范围");
        if (viewport.ScaleMode != CustomGuiScaleMode.Fit || viewport.SafeArea != CustomGuiSafeAreaMode.Required)
            Add(diagnostics, "GUI05-LAYOUT-001", "viewport", "v1 仅允许 fit 缩放并要求安全区");
    }

    private static void ValidateElementFields(
        CustomGuiElement element,
        CustomGuiResourceCatalog resources,
        List<CustomGuiValidationDiagnostic> diagnostics)
    {
        ValidateLayoutFields(element, diagnostics);
        switch (element)
        {
            case CustomGuiWindow window:
                Text(window.Id, window.Title, 256, diagnostics);
                break;
            case CustomGuiPanel panel:
                if (panel.Flow.Spacing < 0 || !ThicknessValid(panel.Flow.Padding))
                    Add(diagnostics, "GUI05-LAYOUT-001", panel.Id, "流布局间距或内边距无效");
                if (!string.IsNullOrWhiteSpace(panel.BackgroundColor) && !Color.IsMatch(panel.BackgroundColor))
                    Add(diagnostics, "GUI05-TEXT-001", panel.Id, "背景颜色必须为 #RRGGBB 或 #RRGGBBAA");
                break;
            case CustomGuiImage image:
                Asset(element.Id, image.AssetId, true, resources, diagnostics);
                Text(element.Id, image.AlternateText, 256, diagnostics);
                break;
            case CustomGuiText text:
                Text(element.Id, text.Content, CustomGuiValidationLimits.MaximumTextLength, diagnostics, rich: text.Format == CustomGuiTextFormat.Rich);
                if (text.FontSize < 8 || text.FontSize > 72) Add(diagnostics, "GUI05-LIMIT-001", element.Id, "字号必须在 8..72 之间");
                if (!string.IsNullOrWhiteSpace(text.Color) && !Color.IsMatch(text.Color)) Add(diagnostics, "GUI05-TEXT-001", element.Id, "文字颜色无效");
                if (!string.IsNullOrWhiteSpace(text.FontId) && !resources.ContainsFont(text.FontId)) Add(diagnostics, "GUI05-RESOURCE-001", element.Id, $"字体未登记：{text.FontId}");
                break;
            case CustomGuiButton button:
                Text(element.Id, button.Text, 256, diagnostics);
                StableValue(element.Id, button.ActionId, "动作", diagnostics, required: true);
                if (!Enum.IsDefined(button.Action)) Add(diagnostics, "GUI05-DOC-001", element.Id, "动作类型无效");
                Asset(element.Id, button.AssetId, false, resources, diagnostics);
                break;
            case CustomGuiTextInput input:
                Text(element.Id, input.Placeholder, 256, diagnostics);
                if (input.MaxLength < 1 || input.MaxLength > CustomGuiValidationLimits.MaximumInputLength)
                    Add(diagnostics, "GUI05-LIMIT-001", element.Id, $"输入长度必须在 1..{CustomGuiValidationLimits.MaximumInputLength} 之间");
                StableValue(element.Id, input.BindingKey, "绑定键", diagnostics, required: true);
                break;
            case CustomGuiList list:
                StableValue(element.Id, list.SelectionBindingKey, "选择绑定键", diagnostics, required: false);
                if (list.Spacing < 0 || list.Spacing > 256) Add(diagnostics, "GUI05-LAYOUT-001", element.Id, "列表间距无效");
                if (list.Items is null || list.Items.Count > CustomGuiValidationLimits.MaximumListItems)
                    Add(diagnostics, "GUI05-LIMIT-001", element.Id, $"列表项数量超过 {CustomGuiValidationLimits.MaximumListItems}");
                else
                {
                    var itemIds = new HashSet<string>(StringComparer.Ordinal);
                    foreach (CustomGuiListItem item in list.Items)
                    {
                        if (item is null || !StableId.IsMatch(item.Id ?? string.Empty)) Add(diagnostics, "GUI05-DOC-001", element.Id, "列表项标识无效");
                        else
                        {
                            string itemId = item.Id!;
                            if (!itemIds.Add(itemId)) Add(diagnostics, "GUI05-DOC-001", element.Id, $"列表项标识重复：{itemId}");
                            Text(element.Id, item.PrimaryText, 256, diagnostics);
                            Text(element.Id, item.SecondaryText, 256, diagnostics);
                            Asset(element.Id, item.AssetId, false, resources, diagnostics);
                        }
                    }
                }
                break;
            case CustomGuiProgressBar progress:
                if (progress.Maximum <= progress.Minimum || progress.Value < progress.Minimum || progress.Value > progress.Maximum)
                    Add(diagnostics, "GUI05-DOC-001", element.Id, "进度范围或当前值无效");
                Text(element.Id, progress.Text, 256, diagnostics);
                StableValue(element.Id, progress.BindingKey, "绑定键", diagnostics, required: false);
                break;
            case CustomGuiItemSlot slot:
                Asset(element.Id, slot.AssetId, true, resources, diagnostics);
                Text(element.Id, slot.DisplayName, 256, diagnostics);
                if (slot.Quantity < 0 || slot.Quantity > 1_000_000) Add(diagnostics, "GUI05-LIMIT-001", element.Id, "物品数量超出显示上限");
                StableValue(element.Id, slot.BindingKey, "绑定键", diagnostics, required: false);
                break;
            default:
                Add(diagnostics, "GUI05-DOC-001", element.Id, "控件类型不受支持");
                break;
        }
    }

    private static void ValidateLayoutFields(CustomGuiElement element, List<CustomGuiValidationDiagnostic> diagnostics)
    {
        CustomGuiLayout layout = element.Layout;
        int[] values = [layout.X, layout.Y, layout.Width, layout.Height, layout.Margin.Left, layout.Margin.Top, layout.Margin.Right, layout.Margin.Bottom, element.ZIndex];
        if (values.Any(value => Math.Abs((long)value) > CustomGuiValidationLimits.MaximumCoordinateMagnitude) ||
            layout.Width < 0 || layout.Height < 0 || !ThicknessValid(layout.Margin))
            Add(diagnostics, "GUI05-LAYOUT-001", element.Id, "布局坐标、尺寸、边距或层级超出允许范围");
    }

    private static bool ThicknessValid(CustomGuiThickness value) =>
        value.Left >= 0 && value.Top >= 0 && value.Right >= 0 && value.Bottom >= 0 &&
        value.Left <= CustomGuiValidationLimits.MaximumCoordinateMagnitude && value.Top <= CustomGuiValidationLimits.MaximumCoordinateMagnitude &&
        value.Right <= CustomGuiValidationLimits.MaximumCoordinateMagnitude && value.Bottom <= CustomGuiValidationLimits.MaximumCoordinateMagnitude;

    private static void ValidateGraph(
        CustomGuiRuntimeDocument document,
        IReadOnlyDictionary<string, CustomGuiElement> byId,
        List<CustomGuiValidationDiagnostic> diagnostics)
    {
        CustomGuiElement[] roots = document.Elements.Where(element => string.IsNullOrWhiteSpace(element.ParentId)).ToArray();
        if (roots.Length != 1 || roots[0] is not CustomGuiWindow)
            Add(diagnostics, "GUI05-GRAPH-001", "elements", "必须且只能有一个 Window 根对象");
        foreach (CustomGuiElement element in document.Elements)
        {
            if (string.IsNullOrWhiteSpace(element.ParentId)) continue;
            if (!byId.TryGetValue(element.ParentId, out CustomGuiElement? parent))
            {
                Add(diagnostics, "GUI05-GRAPH-001", element.Id, $"父对象不存在：{element.ParentId}");
                continue;
            }
            if (parent is not CustomGuiWindow and not CustomGuiPanel and not CustomGuiList)
                Add(diagnostics, "GUI05-GRAPH-001", element.Id, "父对象不是允许的容器类型");
            if (element is CustomGuiWindow) Add(diagnostics, "GUI05-GRAPH-001", element.Id, "Window 只能作为根对象");

            var visited = new HashSet<string>(StringComparer.Ordinal) { element.Id };
            CustomGuiElement cursor = element;
            int depth = 0;
            while (!string.IsNullOrWhiteSpace(cursor.ParentId) && byId.TryGetValue(cursor.ParentId, out CustomGuiElement? ancestor))
            {
                cursor = ancestor;
                depth++;
                if (!visited.Add(cursor.Id))
                {
                    Add(diagnostics, "GUI05-GRAPH-001", element.Id, "父级关系存在循环");
                    break;
                }
                if (depth > CustomGuiValidationLimits.MaximumDepth)
                {
                    Add(diagnostics, "GUI05-LIMIT-001", element.Id, $"嵌套深度超过 {CustomGuiValidationLimits.MaximumDepth}");
                    break;
                }
            }
        }
    }

    private static void ValidateResolvedBounds(
        CustomGuiRuntimeDocument document,
        IReadOnlyDictionary<string, CustomGuiElement> byId,
        List<CustomGuiValidationDiagnostic> diagnostics)
    {
        if (diagnostics.Any(item => item.Code == "GUI05-GRAPH-001" || item.Code == "GUI05-LAYOUT-001")) return;
        IReadOnlyDictionary<string, CustomGuiResolvedBounds> bounds;
        try { bounds = CustomGuiLayoutEngine.Resolve(document); }
        catch (CustomGuiLayoutException error)
        {
            Add(diagnostics, "GUI05-LAYOUT-001", "elements", error.Message);
            return;
        }
        foreach (CustomGuiElement element in document.Elements)
        {
            CustomGuiResolvedBounds child = bounds[element.Id];
            CustomGuiResolvedBounds parent = string.IsNullOrWhiteSpace(element.ParentId)
                ? new(0, 0, document.Viewport.ReferenceWidth, document.Viewport.ReferenceHeight)
                : bounds[element.ParentId];
            if (child.Width <= 0 || child.Height <= 0 ||
                child.X < parent.X || child.Y < parent.Y ||
                (long)child.X + child.Width > (long)parent.X + parent.Width ||
                (long)child.Y + child.Height > (long)parent.Y + parent.Height)
                Add(diagnostics, "GUI05-LAYOUT-001", element.Id, "最终边界为空或越过父级/安全区");
        }
    }

    private static void ValidateTotalBudgets(CustomGuiRuntimeDocument document, List<CustomGuiValidationDiagnostic> diagnostics)
    {
        int totalItems = document.Elements.OfType<CustomGuiList>().Sum(list => list.Items?.Count ?? 0);
        if (totalItems > CustomGuiValidationLimits.MaximumTotalListItems)
            Add(diagnostics, "GUI05-LIMIT-001", "elements", $"列表项总数超过 {CustomGuiValidationLimits.MaximumTotalListItems}");
        long totalText = 0;
        foreach (CustomGuiElement element in document.Elements)
        {
            totalText += element switch
            {
                CustomGuiWindow value => Length(value.Title),
                CustomGuiImage value => Length(value.AlternateText),
                CustomGuiText value => Length(value.Content),
                CustomGuiButton value => Length(value.Text),
                CustomGuiTextInput value => Length(value.Placeholder),
                CustomGuiList value => (value.Items ?? []).Sum(item => Length(item?.PrimaryText) + Length(item?.SecondaryText)),
                CustomGuiProgressBar value => Length(value.Text),
                CustomGuiItemSlot value => Length(value.DisplayName),
                _ => 0,
            };
        }
        if (totalText > CustomGuiValidationLimits.MaximumTotalTextLength)
            Add(diagnostics, "GUI05-LIMIT-001", "elements", $"文本总量超过 {CustomGuiValidationLimits.MaximumTotalTextLength} 字符");
    }

    private static void Text(
        string source,
        string? value,
        int maximum,
        List<CustomGuiValidationDiagnostic> diagnostics,
        bool rich = false)
    {
        if (Length(value) > maximum || (value ?? string.Empty).Any(character => char.IsControl(character) && character is not '\r' and not '\n' and not '\t'))
            Add(diagnostics, "GUI05-TEXT-001", source, $"文本包含非法控制符或超过 {maximum} 字符");
        if (rich && ContainsExternalMarkup(value))
            Add(diagnostics, "GUI05-TEXT-001", source, "富文本不得包含 URL、图片、文件或脚本标签");
    }

    private static bool ContainsExternalMarkup(string? value)
    {
        string text = value ?? string.Empty;
        return text.Contains("://", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("http:", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("https:", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("[url", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("[img", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("file:", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("javascript:", StringComparison.OrdinalIgnoreCase);
    }

    private static void Asset(
        string source,
        string? id,
        bool required,
        CustomGuiResourceCatalog resources,
        List<CustomGuiValidationDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            if (required) Add(diagnostics, "GUI05-RESOURCE-001", source, "资源标识不能为空");
            return;
        }
        if (!IsLogicalResourceId(id) || !resources.ContainsAsset(id))
            Add(diagnostics, "GUI05-RESOURCE-001", source, $"资源标识无效或未登记：{id}");
    }

    private static void StableValue(
        string source,
        string? value,
        string label,
        List<CustomGuiValidationDiagnostic> diagnostics,
        bool required)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            if (required) Add(diagnostics, "GUI05-DOC-001", source, $"{label}不能为空");
            return;
        }
        if (!StableId.IsMatch(value)) Add(diagnostics, "GUI05-DOC-001", source, $"{label}格式无效");
    }

    private static int Length(string? value) => value?.Length ?? 0;
    private static void Add(List<CustomGuiValidationDiagnostic> values, string code, string source, string message) =>
        values.Add(new CustomGuiValidationDiagnostic(code, source, message));
}
