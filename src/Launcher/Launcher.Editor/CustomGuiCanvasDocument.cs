using LyoCrystal.DesignCore;
using Shared.CustomGui;

namespace LyoCrystal.LauncherEditor;

internal sealed class CustomGuiCanvasDocument
{
    private readonly Adapter _adapter;
    private readonly CanvasDocument<string, Snapshot> _core;

    public CustomGuiCanvasDocument(CustomGuiRuntimeDocument document, List<CustomGuiCanvasControlState> states)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(states);
        _adapter = new Adapter(document, states);
        _core = new CanvasDocument<string, Snapshot>(_adapter, document.Viewport.ReferenceWidth, document.Viewport.ReferenceHeight);
    }

    public ICanvasDocument<string> Core => _core;
    public CustomGuiRuntimeDocument Runtime => _adapter.Document;

    public CustomGuiElement Element(string id) => _adapter.Element(id);

    public void ChangeElement(string id, Action<CustomGuiElement> change) =>
        _core.ApplyChange(() => change(_adapter.Element(id)));

    private sealed record Snapshot(CustomGuiRuntimeDocument Document, List<CustomGuiCanvasControlState> States);

    private sealed class Adapter : ICanvasDocumentAdapter<string, Snapshot>
    {
        private readonly List<CustomGuiCanvasControlState> _states;

        public Adapter(CustomGuiRuntimeDocument document, List<CustomGuiCanvasControlState> states)
        {
            Document = document;
            _states = states;
            foreach (CustomGuiElement element in document.Elements)
                if (!states.Any(state => state.DocumentId == document.DocumentId && state.ElementId == element.Id))
                    states.Add(new CustomGuiCanvasControlState { DocumentId = document.DocumentId, ElementId = element.Id });
        }

        public CustomGuiRuntimeDocument Document { get; }
        public IReadOnlyList<string> ElementIds => Document.Elements.OrderBy(element => element.ZIndex).Select(element => element.Id).ToArray();
        public CanvasBounds GetBounds(string id) => Resolve(id, []);
        public void SetBounds(string id, CanvasBounds value)
        {
            CustomGuiElement element = Element(id);
            CanvasBounds parent = ParentBounds(element, []);
            CustomGuiLayout layout = element.Layout;
            int x = layout.HorizontalAnchor switch
            {
                CustomGuiHorizontalAnchor.Center => value.X - parent.X - (parent.Width - value.Width) / 2,
                CustomGuiHorizontalAnchor.Right => parent.X + parent.Width - layout.Margin.Right - value.Width - value.X,
                _ => value.X - parent.X - layout.Margin.Left,
            };
            int y = layout.VerticalAnchor switch
            {
                CustomGuiVerticalAnchor.Center => value.Y - parent.Y - (parent.Height - value.Height) / 2,
                CustomGuiVerticalAnchor.Bottom => parent.Y + parent.Height - layout.Margin.Bottom - value.Height - value.Y,
                _ => value.Y - parent.Y - layout.Margin.Top,
            };
            int width = layout.HorizontalAnchor == CustomGuiHorizontalAnchor.Stretch
                ? parent.Width - layout.Margin.Left - layout.Margin.Right - x - value.Width
                : value.Width;
            int height = layout.VerticalAnchor == CustomGuiVerticalAnchor.Stretch
                ? parent.Height - layout.Margin.Top - layout.Margin.Bottom - y - value.Height
                : value.Height;
            element.Layout = layout with { X = x, Y = y, Width = width, Height = height };
        }
        public bool IsVisible(string id) => Element(id).Visible;
        public bool IsLocked(string id) => State(id).Locked;
        public void SetVisible(string id, bool visible) => Element(id).Visible = visible;
        public void SetLocked(string id, bool locked) => State(id).Locked = locked;
        public void SetOrder(IReadOnlyList<string> ids) { for (int index = 0; index < ids.Count; index++) Element(ids[index]).ZIndex = index; }
        public Snapshot Capture() => new(Clone(Document), _states.Where(state => state.DocumentId == Document.DocumentId).Select(Clone).ToList());
        public void Restore(Snapshot state)
        {
            CustomGuiRuntimeDocument copy = Clone(state.Document);
            Document.SchemaVersion = copy.SchemaVersion; Document.DocumentId = copy.DocumentId; Document.Revision = copy.Revision; Document.Viewport = copy.Viewport; Document.Elements = copy.Elements;
            _states.RemoveAll(item => item.DocumentId == Document.DocumentId);
            _states.AddRange(state.States.Select(Clone));
        }
        public bool Equivalent(Snapshot left, Snapshot right) =>
            CustomGuiDocumentCodec.Serialize(left.Document).AsSpan().SequenceEqual(CustomGuiDocumentCodec.Serialize(right.Document)) &&
            left.States.Count == right.States.Count && left.States.Zip(right.States).All(pair =>
                pair.First.DocumentId == pair.Second.DocumentId && pair.First.ElementId == pair.Second.ElementId && pair.First.Locked == pair.Second.Locked);

        public CustomGuiElement Element(string id) => Document.Elements.Single(element => element.Id == id);
        private CanvasBounds Resolve(string id, HashSet<string> path)
        {
            if (!path.Add(id)) throw new InvalidDataException("游戏 GUI 父级关系存在循环");
            CustomGuiElement element = Element(id);
            CanvasBounds parent = ParentBounds(element, path);
            CustomGuiLayout layout = element.Layout;
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
            path.Remove(id);
            return new CanvasBounds(x, y, width, height);
        }

        private CanvasBounds ParentBounds(CustomGuiElement element, HashSet<string> path) => string.IsNullOrWhiteSpace(element.ParentId)
            ? new CanvasBounds(0, 0, Document.Viewport.ReferenceWidth, Document.Viewport.ReferenceHeight)
            : Resolve(element.ParentId, path);
        private CustomGuiCanvasControlState State(string id) => _states.Single(state => state.DocumentId == Document.DocumentId && state.ElementId == id);
        private static CustomGuiRuntimeDocument Clone(CustomGuiRuntimeDocument value) => CustomGuiDocumentCodec.Deserialize(CustomGuiDocumentCodec.Serialize(value));
        private static CustomGuiCanvasControlState Clone(CustomGuiCanvasControlState value) => new() { DocumentId = value.DocumentId, ElementId = value.ElementId, Locked = value.Locked };
    }
}
