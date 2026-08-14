using Shared.CustomGui;

namespace MonoShare.CustomGui;

public enum MobileCustomGuiNodeKind
{
    Root,
    Window,
    Panel,
    Image,
    Text,
    Button,
    TextInput,
    List,
    ProgressBar,
    ItemSlot,
}

public readonly record struct MobileCustomGuiBounds(float X, float Y, float Width, float Height);

public sealed record MobileCustomGuiNodeSpec(
    string Id,
    MobileCustomGuiNodeKind Kind,
    MobileCustomGuiBounds Bounds,
    bool Visible,
    int ZIndex,
    CustomGuiElement? Element,
    float Scale);

public interface IMobileCustomGuiNode : IDisposable
{
    void AddChild(IMobileCustomGuiNode child);
    void ApplyState(CustomGuiStateEntry state);
}

public interface IMobileCustomGuiFactory
{
    IMobileCustomGuiNode Create(MobileCustomGuiNodeSpec spec);
}

public static class MobileCustomGuiAdapter
{
    public static MobileCustomGuiHost Create(CustomGuiRuntimeDocument document, int viewportWidth, int viewportHeight, IMobileCustomGuiFactory factory)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(factory);
        if (viewportWidth <= 0) throw new ArgumentOutOfRangeException(nameof(viewportWidth));
        if (viewportHeight <= 0) throw new ArgumentOutOfRangeException(nameof(viewportHeight));

        IReadOnlyDictionary<string, CustomGuiResolvedBounds> layout = CustomGuiLayoutEngine.Resolve(document);
        float scale = Math.Min(viewportWidth / (float)document.Viewport.ReferenceWidth, viewportHeight / (float)document.Viewport.ReferenceHeight);
        float offsetX = (viewportWidth - document.Viewport.ReferenceWidth * scale) / 2F;
        float offsetY = (viewportHeight - document.Viewport.ReferenceHeight * scale) / 2F;
        IMobileCustomGuiNode root = factory.Create(new MobileCustomGuiNodeSpec(
            "__custom_gui_root", MobileCustomGuiNodeKind.Root, new(0, 0, viewportWidth, viewportHeight), true, int.MinValue, null, scale))
            ?? throw new InvalidOperationException("移动控件工厂未创建根节点");
        var nodes = new Dictionary<string, IMobileCustomGuiNode>(StringComparer.Ordinal);
        try
        {
            var pending = new List<CustomGuiElement>(document.Elements);
            while (pending.Count > 0)
            {
                int before = pending.Count;
                foreach (CustomGuiElement element in pending.ToArray())
                {
                    if (!string.IsNullOrWhiteSpace(element.ParentId) && !nodes.ContainsKey(element.ParentId)) continue;
                    IMobileCustomGuiNode parent = string.IsNullOrWhiteSpace(element.ParentId) ? root : nodes[element.ParentId];
                    CustomGuiResolvedBounds absolute = layout[element.Id];
                    CustomGuiResolvedBounds parentAbsolute = string.IsNullOrWhiteSpace(element.ParentId)
                        ? new(0, 0, document.Viewport.ReferenceWidth, document.Viewport.ReferenceHeight)
                        : layout[element.ParentId];
                    float localOffsetX = ReferenceEquals(parent, root) ? offsetX : 0F;
                    float localOffsetY = ReferenceEquals(parent, root) ? offsetY : 0F;
                    var bounds = new MobileCustomGuiBounds(
                        (absolute.X - parentAbsolute.X) * scale + localOffsetX,
                        (absolute.Y - parentAbsolute.Y) * scale + localOffsetY,
                        Math.Max(1F, absolute.Width * scale),
                        Math.Max(1F, absolute.Height * scale));
                    IMobileCustomGuiNode node = factory.Create(new MobileCustomGuiNodeSpec(
                        element.Id, GetKind(element), bounds, element.Visible, element.ZIndex, element, scale))
                        ?? throw new InvalidOperationException("移动控件工厂未创建节点");
                    bool attached = false;
                    try
                    {
                        parent.AddChild(node);
                        attached = true;
                        nodes.Add(element.Id, node);
                    }
                    catch
                    {
                        if (!attached) node.Dispose();
                        throw;
                    }
                    pending.Remove(element);
                }
                if (pending.Count == before) throw new CustomGuiLayoutException("移动 Adapter 无法物化父级顺序");
            }
            return new MobileCustomGuiHost(root, nodes, document, scale, offsetX, offsetY);
        }
        catch (Exception error)
        {
            try { root.Dispose(); }
            catch (Exception cleanupError) { throw new AggregateException("移动控件树创建和清理均失败", error, cleanupError); }
            throw;
        }
    }

    private static MobileCustomGuiNodeKind GetKind(CustomGuiElement element) => element switch
    {
        CustomGuiWindow => MobileCustomGuiNodeKind.Window,
        CustomGuiPanel => MobileCustomGuiNodeKind.Panel,
        CustomGuiImage => MobileCustomGuiNodeKind.Image,
        CustomGuiText => MobileCustomGuiNodeKind.Text,
        CustomGuiButton => MobileCustomGuiNodeKind.Button,
        CustomGuiTextInput => MobileCustomGuiNodeKind.TextInput,
        CustomGuiList => MobileCustomGuiNodeKind.List,
        CustomGuiProgressBar => MobileCustomGuiNodeKind.ProgressBar,
        CustomGuiItemSlot => MobileCustomGuiNodeKind.ItemSlot,
        _ => throw new CustomGuiLayoutException("移动 Adapter 不支持该控件类型"),
    };
}

public sealed class MobileCustomGuiHost : IDisposable, ICustomGuiStateProjectionTarget
{
    private bool _disposed;
    private readonly IReadOnlyDictionary<string, string> _bindingTargets;
    private IReadOnlyDictionary<string, CustomGuiStateEntry> _state = new Dictionary<string, CustomGuiStateEntry>();
    internal MobileCustomGuiHost(
        IMobileCustomGuiNode root,
        IReadOnlyDictionary<string, IMobileCustomGuiNode> nodes,
        CustomGuiRuntimeDocument document,
        float scale,
        float viewportOffsetX,
        float viewportOffsetY)
    {
        Root = root;
        Nodes = nodes;
        _bindingTargets = CustomGuiStateBindingCatalog.Create(document);
        Scale = scale;
        ViewportOffsetX = viewportOffsetX;
        ViewportOffsetY = viewportOffsetY;
    }

    public IMobileCustomGuiNode Root { get; }
    public IReadOnlyDictionary<string, IMobileCustomGuiNode> Nodes { get; }
    public float Scale { get; }
    public float ViewportOffsetX { get; }
    public float ViewportOffsetY { get; }
    public bool IsDisposed => _disposed;
    public IReadOnlyDictionary<string, CustomGuiStateEntry> ProjectedState => _state;
    public void Apply(IReadOnlyDictionary<string, CustomGuiStateEntry> state)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(state);
        foreach (string key in state.Keys)
            if (!_bindingTargets.TryGetValue(key, out string? elementId) || !Nodes.ContainsKey(elementId))
                throw new CustomGuiStateProjectionException("GUI10-STATE-BINDING", $"移动端不存在绑定目标：{key}");
        foreach ((string key, CustomGuiStateEntry value) in state)
            Nodes[_bindingTargets[key]].ApplyState(value);
        _state = state;
    }
    public void Dispose()
    {
        if (_disposed) return;
        Root.Dispose();
        _disposed = true;
    }
}
