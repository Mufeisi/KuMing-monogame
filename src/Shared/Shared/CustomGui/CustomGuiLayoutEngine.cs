namespace Shared.CustomGui;

public readonly record struct CustomGuiResolvedBounds(int X, int Y, int Width, int Height);

public sealed class CustomGuiLayoutException : Exception
{
    public CustomGuiLayoutException(string message, Exception innerException = null)
        : base($"GUI03-LAYOUT-001: {message}", innerException) => Code = "GUI03-LAYOUT-001";

    public string Code { get; }
}

public static class CustomGuiLayoutEngine
{
    public static IReadOnlyDictionary<string, CustomGuiResolvedBounds> Resolve(CustomGuiRuntimeDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        try
        {
            if (document.Viewport == null || document.Viewport.ReferenceWidth <= 0 || document.Viewport.ReferenceHeight <= 0)
                throw new InvalidDataException("参考视口无效");
            var elements = document.Elements.ToDictionary(element => element.Id, StringComparer.Ordinal);
            if (elements.Count != document.Elements.Count) throw new InvalidDataException("对象标识重复");
            var resolver = new Resolver(document, elements);
            foreach (CustomGuiElement element in document.Elements) resolver.Resolve(element.Id);
            return resolver.Results;
        }
        catch (CustomGuiLayoutException) { throw; }
        catch (Exception error) when (error is InvalidDataException or InvalidOperationException or ArgumentException)
        {
            throw new CustomGuiLayoutException("运行描述的父级或布局关系无效", error);
        }
    }

    private sealed class Resolver
    {
        private readonly CustomGuiRuntimeDocument _document;
        private readonly IReadOnlyDictionary<string, CustomGuiElement> _elements;
        private readonly HashSet<string> _visiting = new(StringComparer.Ordinal);
        private readonly Dictionary<string, CustomGuiResolvedBounds> _results = new(StringComparer.Ordinal);

        public Resolver(CustomGuiRuntimeDocument document, IReadOnlyDictionary<string, CustomGuiElement> elements)
        {
            _document = document;
            _elements = elements;
        }

        public IReadOnlyDictionary<string, CustomGuiResolvedBounds> Results => _results;

        public CustomGuiResolvedBounds Resolve(string id)
        {
            if (_results.TryGetValue(id, out CustomGuiResolvedBounds existing)) return existing;
            if (!_elements.TryGetValue(id, out CustomGuiElement element)) throw new InvalidDataException("父对象不存在");
            if (!_visiting.Add(id)) throw new InvalidDataException("父级关系存在循环");
            try
            {
                CustomGuiResolvedBounds parent = string.IsNullOrWhiteSpace(element.ParentId)
                    ? new(0, 0, _document.Viewport.ReferenceWidth, _document.Viewport.ReferenceHeight)
                    : Resolve(element.ParentId);
                CustomGuiResolvedBounds resolved = ResolveInParent(element, parent);
                _results.Add(id, resolved);
                return resolved;
            }
            finally { _visiting.Remove(id); }
        }

        private CustomGuiResolvedBounds ResolveInParent(CustomGuiElement element, CustomGuiResolvedBounds parent)
        {
            CustomGuiFlow flow = element.ParentId is not null && _elements.TryGetValue(element.ParentId, out CustomGuiElement parentElement) && parentElement is CustomGuiPanel panel
                ? panel.Flow
                : default;
            CustomGuiResolvedBounds content = flow.Direction == CustomGuiFlowDirection.None
                ? parent
                : Inset(parent, flow.Padding);
            CustomGuiResolvedBounds result = Anchor(element.Layout, content);
            if (flow.Direction == CustomGuiFlowDirection.None) return result;

            int main = flow.Direction == CustomGuiFlowDirection.Horizontal ? content.X : content.Y;
            foreach (CustomGuiElement sibling in _document.Elements)
            {
                if (ReferenceEquals(sibling, element)) break;
                if (!string.Equals(sibling.ParentId, element.ParentId, StringComparison.Ordinal)) continue;
                CustomGuiResolvedBounds measured = Anchor(sibling.Layout, content);
                main += flow.Direction == CustomGuiFlowDirection.Horizontal
                    ? sibling.Layout.Margin.Left + measured.Width + sibling.Layout.Margin.Right + flow.Spacing
                    : sibling.Layout.Margin.Top + measured.Height + sibling.Layout.Margin.Bottom + flow.Spacing;
            }
            return flow.Direction == CustomGuiFlowDirection.Horizontal
                ? result with { X = main + element.Layout.Margin.Left }
                : result with { Y = main + element.Layout.Margin.Top };
        }

        private static CustomGuiResolvedBounds Anchor(CustomGuiLayout layout, CustomGuiResolvedBounds parent)
        {
            int width = layout.HorizontalAnchor == CustomGuiHorizontalAnchor.Stretch
                ? parent.Width - layout.Margin.Left - layout.Margin.Right - layout.X - layout.Width
                : layout.Width;
            int height = layout.VerticalAnchor == CustomGuiVerticalAnchor.Stretch
                ? parent.Height - layout.Margin.Top - layout.Margin.Bottom - layout.Y - layout.Height
                : layout.Height;
            int x = layout.HorizontalAnchor switch
            {
                CustomGuiHorizontalAnchor.Center => parent.X + (parent.Width - width) / 2 + layout.X,
                CustomGuiHorizontalAnchor.Right => parent.X + parent.Width - layout.Margin.Right - width - layout.X,
                _ => parent.X + layout.Margin.Left + layout.X,
            };
            int y = layout.VerticalAnchor switch
            {
                CustomGuiVerticalAnchor.Center => parent.Y + (parent.Height - height) / 2 + layout.Y,
                CustomGuiVerticalAnchor.Bottom => parent.Y + parent.Height - layout.Margin.Bottom - height - layout.Y,
                _ => parent.Y + layout.Margin.Top + layout.Y,
            };
            return new CustomGuiResolvedBounds(x, y, width, height);
        }

        private static CustomGuiResolvedBounds Inset(CustomGuiResolvedBounds bounds, CustomGuiThickness padding) => new(
            bounds.X + padding.Left,
            bounds.Y + padding.Top,
            bounds.Width - padding.Left - padding.Right,
            bounds.Height - padding.Top - padding.Bottom);
    }
}
