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

public interface IMobileCustomGuiInteractiveNode
{
    event Action? Activated;
    event Action<string>? SelectionChanged;
    void Activate();
    void Select(string itemId);
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
    private readonly Dictionary<string, CustomGuiButton> _buttons;
    private readonly Dictionary<string, HashSet<string>> _availableSelections;
    private readonly Dictionary<string, string> _selections = new(StringComparer.Ordinal);
    private CustomGuiClientStateSession? _actionSession;
    private Action<CustomGuiClientAction>? _sendAction;
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
        _buttons = (document.Elements ?? []).OfType<CustomGuiButton>()
            .ToDictionary(button => button.Id, StringComparer.Ordinal);
        _availableSelections = (document.Elements ?? []).OfType<CustomGuiList>()
            .ToDictionary(list => list.Id,
                list => new HashSet<string>((list.Items ?? []).Select(item => item.Id), StringComparer.Ordinal),
                StringComparer.Ordinal);
        foreach ((string id, IMobileCustomGuiNode node) in nodes)
        {
            if (node is not IMobileCustomGuiInteractiveNode interactive) continue;
            if (_availableSelections.ContainsKey(id))
                interactive.SelectionChanged += itemId => RecordSelection(id, itemId);
            if (_buttons.ContainsKey(id))
                interactive.Activated += () =>
                {
                    if (_actionSession is not null && _sendAction is not null) Submit(id, _actionSession, _sendAction);
                };
        }
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
    public void BindActions(CustomGuiClientStateSession session, Action<CustomGuiClientAction> send)
    {
        _actionSession = session ?? throw new ArgumentNullException(nameof(session));
        _sendAction = send ?? throw new ArgumentNullException(nameof(send));
    }
    public void Select(string listId, string itemId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!Nodes.TryGetValue(listId, out IMobileCustomGuiNode? node) || node is not IMobileCustomGuiInteractiveNode interactive)
            throw new CustomGuiStateProjectionException("GUI12-CLIENT-SELECTION", $"移动端列表不可交互：{listId}");
        interactive.Select(itemId);
    }
    public CustomGuiClientAction Submit(
        string buttonId,
        CustomGuiClientStateSession session,
        Action<CustomGuiClientAction> send)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_buttons.TryGetValue(buttonId, out CustomGuiButton? button))
            throw new CustomGuiStateProjectionException("GUI12-CLIENT-ACTION", $"移动端按钮不存在：{buttonId}");
        if (_state.TryGetValue($"{buttonId}.enabled", out CustomGuiStateEntry? enabled) && !enabled.BooleanValue)
            throw new CustomGuiStateProjectionException("GUI12-CLIENT-ACTION", "移动端按钮当前不可用");
        List<string> selections = _selections.Values.Distinct(StringComparer.Ordinal).ToList();
        return session.SendAction(send, button.Action, button.ActionId, selectionIds: selections);
    }
    public void Apply(IReadOnlyDictionary<string, CustomGuiStateEntry> state)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(state);
        foreach (string key in state.Keys)
            if (!_bindingTargets.TryGetValue(key, out string? elementId) || !Nodes.ContainsKey(elementId))
                throw new CustomGuiStateProjectionException("GUI10-STATE-BINDING", $"移动端不存在绑定目标：{key}");
        foreach ((string key, CustomGuiStateEntry value) in state)
        {
            Nodes[_bindingTargets[key]].ApplyState(value);
            if (value.Kind == CustomGuiStateKind.List)
            {
                string listId = _bindingTargets[key];
                _availableSelections[listId] = new HashSet<string>((value.ListItems ?? []).Select(item => item.Id), StringComparer.Ordinal);
                if (_selections.TryGetValue(listId, out string? selected) && !_availableSelections[listId].Contains(selected))
                    _selections.Remove(listId);
            }
        }
        _state = state;
    }
    private void RecordSelection(string listId, string itemId)
    {
        if (!_availableSelections.TryGetValue(listId, out HashSet<string>? available) ||
            string.IsNullOrWhiteSpace(itemId) || !available.Contains(itemId))
            throw new CustomGuiStateProjectionException("GUI12-CLIENT-SELECTION", $"移动端选择项不存在：{itemId}");
        _selections[listId] = itemId;
    }
    public void Dispose()
    {
        if (_disposed) return;
        Root.Dispose();
        _actionSession = null;
        _sendAction = null;
        _selections.Clear();
        _disposed = true;
    }
}
