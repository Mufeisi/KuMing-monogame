using Launcher.ThemeRuntime;
using LyoCrystal.DesignCore;

namespace LyoCrystal.LauncherEditor;

public enum LauncherCanvasAlignment { Left, HorizontalCenter, Right, Top, VerticalCenter, Bottom }
public enum LauncherCanvasDistribution { Horizontal, Vertical }
public sealed record LauncherCanvasGuide(bool Vertical, int Position);

public sealed record LauncherCanvasStyleChange(
    string? ForeColor = null,
    string? BackColor = null,
    string? FontName = null,
    float? FontSize = null,
    bool? Bold = null,
    int? OpacityPercent = null,
    string? BackgroundImage = null);

public sealed record LauncherCanvasLayoutChange(int? X = null, int? Y = null, int? Width = null, int? Height = null);

public sealed class LauncherCanvasDocument
{
    private readonly LauncherTheme _theme;
    private readonly IList<LauncherCanvasControlState> _editorStates;
    private readonly CanvasDocument<LauncherControlId, DocumentState> _core;

    public LauncherCanvasDocument(LauncherTheme theme, IReadOnlyDictionary<LauncherControlId, Rectangle> runtimeLayout, IList<LauncherCanvasControlState>? editorStates = null)
    {
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));
        _editorStates = editorStates ?? new List<LauncherCanvasControlState>();
        ArgumentNullException.ThrowIfNull(runtimeLayout);
        var existing = theme.Controls.ToDictionary(item => item.Id);
        List<LauncherControlOverride> materialized = theme.Controls.ToList();
        foreach (LauncherControlId id in Enum.GetValues<LauncherControlId>())
        {
            if (existing.ContainsKey(id)) continue;
            Rectangle bounds = runtimeLayout.TryGetValue(id, out Rectangle value) ? value : new Rectangle(20, 20, 120, 36);
            materialized.Add(new LauncherControlOverride { Id = id, X = bounds.X, Y = bounds.Y, Width = bounds.Width, Height = bounds.Height, Visible = true });
        }
        theme.Controls = materialized;
        foreach (LauncherControlId id in Enum.GetValues<LauncherControlId>())
            if (!_editorStates.Any(item => item.Id == id)) _editorStates.Add(new LauncherCanvasControlState { Id = id });
        _core = new CanvasDocument<LauncherControlId, DocumentState>(
            new LauncherCanvasAdapter(_theme, _editorStates),
            _theme.CanvasWidth,
            _theme.CanvasHeight);
        _core.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? Changed;
    public IReadOnlyCollection<LauncherControlId> Selection => _core.Selection;
    public IReadOnlyList<LauncherControlOverride> Controls => _theme.Controls.ToArray();
    public IReadOnlyList<LauncherCanvasGuide> SnapGuides => _core.SnapGuides.Select(guide => new LauncherCanvasGuide(guide.Vertical, guide.Position)).ToArray();
    public bool IsDirty => _core.IsDirty;
    public bool CanUndo => _core.CanUndo;
    public bool CanRedo => _core.CanRedo;

    public void MarkSaved() => _core.MarkSaved();
    public void MarkExternalChange() => _core.MarkExternalChange();
    public void Select(IEnumerable<LauncherControlId> ids, bool additive = false) => _core.Select(ids, additive);

    public Rectangle GetBounds(LauncherControlId id)
    {
        CanvasBounds value = _core.GetBounds(id);
        return new Rectangle(value.X, value.Y, value.Width, value.Height);
    }

    public bool IsLocked(LauncherControlId id) => _core.IsLocked(id);

    public void SetBounds(LauncherControlId id, Rectangle bounds)
    {
        _core.SetBounds(id, ToCore(bounds));
    }

    public void ChangeSelectionLayout(LauncherCanvasLayoutChange change)
        => _core.ChangeSelectionBounds(new CanvasBoundsChange(change.X, change.Y, change.Width, change.Height));

    public bool MoveSelection(int deltaX, int deltaY, bool snap)
        => _core.MoveSelection(deltaX, deltaY, snap);

    public bool ResizeSelection(int deltaWidth, int deltaHeight, bool snap)
        => _core.ResizeSelection(deltaWidth, deltaHeight, snap);

    public void AlignSelection(LauncherCanvasAlignment alignment) => _core.AlignSelection(alignment switch
    {
        LauncherCanvasAlignment.Left => CanvasAlignment.Left,
        LauncherCanvasAlignment.HorizontalCenter => CanvasAlignment.HorizontalCenter,
        LauncherCanvasAlignment.Right => CanvasAlignment.Right,
        LauncherCanvasAlignment.Top => CanvasAlignment.Top,
        LauncherCanvasAlignment.VerticalCenter => CanvasAlignment.VerticalCenter,
        _ => CanvasAlignment.Bottom,
    });

    public void DistributeSelection(LauncherCanvasDistribution direction)
        => _core.DistributeSelection(direction == LauncherCanvasDistribution.Horizontal ? CanvasDistribution.Horizontal : CanvasDistribution.Vertical);

    public void SetLocked(IEnumerable<LauncherControlId> ids, bool locked) => _core.SetLocked(ids, locked);
    public void SetVisible(IEnumerable<LauncherControlId> ids, bool visible) => _core.SetVisible(ids, visible);
    public bool DeleteSelection() => _core.DeleteSelection();
    public void AddOrShow(LauncherControlId id) => _core.AddOrShow(id);
    public void BringSelectionForward() => _core.BringSelectionForward();
    public void SendSelectionBackward() => _core.SendSelectionBackward();

    public void ChangeSelectionStyle(LauncherCanvasStyleChange change)
    {
        Execute(() =>
        {
            foreach (LauncherControlOverride value in _core.EditableSelection.Select(Find))
            {
                if (change.ForeColor is not null) value.ForeColor = change.ForeColor;
                if (change.BackColor is not null) value.BackColor = change.BackColor;
                if (change.FontName is not null) value.FontName = change.FontName;
                if (change.FontSize.HasValue) value.FontSize = change.FontSize.Value;
                if (change.Bold.HasValue) value.Bold = change.Bold.Value;
                if (change.OpacityPercent.HasValue) value.OpacityPercent = Math.Clamp(change.OpacityPercent.Value, 0, 100);
                if (change.BackgroundImage is not null) value.BackgroundImage = change.BackgroundImage;
            }
        });
    }

    public bool Undo() => _core.Undo();
    public bool Redo() => _core.Redo();
    private void Execute(Action change) => _core.ApplyChange(change);

    private LauncherControlOverride Find(LauncherControlId id) => _theme.Controls.Single(item => item.Id == id);
    private static void ApplyBounds(LauncherControlOverride value, Rectangle bounds) { value.X = bounds.X; value.Y = bounds.Y; value.Width = bounds.Width; value.Height = bounds.Height; }
    private static CanvasBounds ToCore(Rectangle value) => new(value.X, value.Y, value.Width, value.Height);
    private static Rectangle ToRectangle(CanvasBounds value) => new(value.X, value.Y, value.Width, value.Height);
    private static bool Equivalent(DocumentState a, DocumentState b) =>
        a.Controls.Count == b.Controls.Count && a.Controls.Zip(b.Controls).All(pair => Properties(pair.First).SequenceEqual(Properties(pair.Second))) &&
        a.EditorStates.Count == b.EditorStates.Count && a.EditorStates.Zip(b.EditorStates).All(pair => pair.First.Id == pair.Second.Id && pair.First.Locked == pair.Second.Locked);
    private static object[] Properties(LauncherControlOverride x) => [x.Id, x.X, x.Y, x.Width, x.Height, x.Visible, x.ForeColor, x.BackColor, x.FontName, x.FontSize, x.Bold, x.OpacityPercent, x.BackgroundImage];
    private static LauncherControlOverride Clone(LauncherControlOverride x) => new() { Id = x.Id, X = x.X, Y = x.Y, Width = x.Width, Height = x.Height, Visible = x.Visible, ForeColor = x.ForeColor, BackColor = x.BackColor, FontName = x.FontName, FontSize = x.FontSize, Bold = x.Bold, OpacityPercent = x.OpacityPercent, BackgroundImage = x.BackgroundImage };
    private sealed record DocumentState(List<LauncherControlOverride> Controls, List<LauncherCanvasControlState> EditorStates);

    private sealed class LauncherCanvasAdapter(LauncherTheme theme, IList<LauncherCanvasControlState> editorStates)
        : ICanvasDocumentAdapter<LauncherControlId, DocumentState>
    {
        public IReadOnlyList<LauncherControlId> ElementIds => theme.Controls.Select(item => item.Id).ToArray();
        public CanvasBounds GetBounds(LauncherControlId id)
        {
            LauncherControlOverride value = Find(id);
            return new CanvasBounds(value.X, value.Y, value.Width, value.Height);
        }
        public void SetBounds(LauncherControlId id, CanvasBounds bounds) => ApplyBounds(Find(id), ToRectangle(bounds));
        public bool IsVisible(LauncherControlId id) => Find(id).Visible;
        public bool IsLocked(LauncherControlId id) => editorStates.Single(item => item.Id == id).Locked;
        public void SetVisible(LauncherControlId id, bool visible) => Find(id).Visible = visible;
        public void SetLocked(LauncherControlId id, bool locked) => editorStates.Single(item => item.Id == id).Locked = locked;
        public void SetOrder(IReadOnlyList<LauncherControlId> ids)
            => theme.Controls = ids.Select(Find).ToList();
        public DocumentState Capture() => new(
            theme.Controls.Select(Clone).ToList(),
            editorStates.Select(item => new LauncherCanvasControlState { Id = item.Id, Locked = item.Locked }).ToList());
        public void Restore(DocumentState state)
        {
            theme.Controls = state.Controls.Select(Clone).ToList();
            editorStates.Clear();
            foreach (LauncherCanvasControlState item in state.EditorStates)
                editorStates.Add(new LauncherCanvasControlState { Id = item.Id, Locked = item.Locked });
        }
        public bool Equivalent(DocumentState left, DocumentState right) => LauncherCanvasDocument.Equivalent(left, right);
        private LauncherControlOverride Find(LauncherControlId id) => theme.Controls.Single(item => item.Id == id);
    }
}
