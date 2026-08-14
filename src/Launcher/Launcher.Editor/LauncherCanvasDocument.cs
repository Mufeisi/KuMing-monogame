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

internal sealed record LauncherControlAppearance(
    LauncherControlId Id,
    string ForeColor,
    string BackColor,
    string FontName,
    float FontSize,
    bool Bold,
    int OpacityPercent,
    string BackgroundImage);

internal interface ILauncherCanvasAppearance
{
    LauncherControlAppearance GetAppearance(LauncherControlId id);
    void SetStyle(LauncherControlId id, LauncherCanvasStyleChange change);
}

public sealed class LauncherCanvasDocument
{
    private readonly LauncherTheme _theme;
    private readonly IList<LauncherCanvasControlState> _editorStates;
    private readonly LauncherCanvasAdapter _adapter;
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
        _adapter = new LauncherCanvasAdapter(_theme, _editorStates);
        Appearance = new LauncherCanvasAppearance(this);
        _core = new CanvasDocument<LauncherControlId, DocumentState>(
            _adapter,
            _theme.CanvasWidth,
            _theme.CanvasHeight);
        _core.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? Changed;
    internal ICanvasDocument<LauncherControlId> Core => _core;
    internal ILauncherCanvasAppearance Appearance { get; }
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
        => _core.ChangeEditableSelection(id => SetStyle(id, change));

    private void SetStyle(LauncherControlId id, LauncherCanvasStyleChange change)
    {
        LauncherControlOverride value = Find(id);
        if (change.ForeColor is not null) value.ForeColor = change.ForeColor;
        if (change.BackColor is not null) value.BackColor = change.BackColor;
        if (change.FontName is not null) value.FontName = change.FontName;
        if (change.FontSize.HasValue) value.FontSize = change.FontSize.Value;
        if (change.Bold.HasValue) value.Bold = change.Bold.Value;
        if (change.OpacityPercent.HasValue) value.OpacityPercent = Math.Clamp(change.OpacityPercent.Value, 0, 100);
        if (change.BackgroundImage is not null) value.BackgroundImage = change.BackgroundImage;
    }

    private LauncherControlAppearance GetAppearance(LauncherControlId id)
    {
        LauncherControlOverride value = _adapter.Control(id);
        return new LauncherControlAppearance(value.Id, value.ForeColor, value.BackColor, value.FontName, value.FontSize, value.Bold, value.OpacityPercent, value.BackgroundImage);
    }

    public bool Undo() => _core.Undo();
    public bool Redo() => _core.Redo();
    private LauncherControlOverride Find(LauncherControlId id) => _adapter.Control(id);
    private static void ApplyBounds(LauncherControlOverride value, Rectangle bounds) { value.X = bounds.X; value.Y = bounds.Y; value.Width = bounds.Width; value.Height = bounds.Height; }
    private static CanvasBounds ToCore(Rectangle value) => new(value.X, value.Y, value.Width, value.Height);
    private static Rectangle ToRectangle(CanvasBounds value) => new(value.X, value.Y, value.Width, value.Height);
    private static bool Equivalent(DocumentState a, DocumentState b) =>
        a.Controls.Count == b.Controls.Count && a.Controls.Zip(b.Controls).All(pair => Properties(pair.First).SequenceEqual(Properties(pair.Second))) &&
        a.EditorStates.Count == b.EditorStates.Count && a.EditorStates.Zip(b.EditorStates).All(pair => pair.First.Id == pair.Second.Id && pair.First.Locked == pair.Second.Locked);
    private static object[] Properties(LauncherControlOverride x) => [x.Id, x.X, x.Y, x.Width, x.Height, x.Visible, x.ForeColor, x.BackColor, x.FontName, x.FontSize, x.Bold, x.OpacityPercent, x.BackgroundImage];
    private static LauncherControlOverride Clone(LauncherControlOverride x) => new() { Id = x.Id, X = x.X, Y = x.Y, Width = x.Width, Height = x.Height, Visible = x.Visible, ForeColor = x.ForeColor, BackColor = x.BackColor, FontName = x.FontName, FontSize = x.FontSize, Bold = x.Bold, OpacityPercent = x.OpacityPercent, BackgroundImage = x.BackgroundImage };
    private sealed record DocumentState(List<LauncherControlOverride> Controls, List<LauncherCanvasControlState> EditorStates);

    private sealed class LauncherCanvasAppearance(LauncherCanvasDocument owner) : ILauncherCanvasAppearance
    {
        public LauncherControlAppearance GetAppearance(LauncherControlId id) => owner.GetAppearance(id);
        public void SetStyle(LauncherControlId id, LauncherCanvasStyleChange change) => owner.SetStyle(id, change);
    }

    private sealed class LauncherCanvasAdapter : ICanvasDocumentAdapter<LauncherControlId, DocumentState>
    {
        private readonly LauncherTheme _theme;
        private readonly IList<LauncherCanvasControlState> _editorStates;
        private Dictionary<LauncherControlId, LauncherControlOverride> _controls;
        private Dictionary<LauncherControlId, LauncherCanvasControlState> _states;

        public LauncherCanvasAdapter(LauncherTheme theme, IList<LauncherCanvasControlState> editorStates)
        {
            _theme = theme;
            _editorStates = editorStates;
            _controls = theme.Controls.ToDictionary(item => item.Id);
            _states = editorStates.ToDictionary(item => item.Id);
        }

        public IReadOnlyList<LauncherControlId> ElementIds => _theme.Controls.Select(item => item.Id).ToArray();
        public CanvasBounds GetBounds(LauncherControlId id)
        {
            LauncherControlOverride value = Control(id);
            return new CanvasBounds(value.X, value.Y, value.Width, value.Height);
        }
        public void SetBounds(LauncherControlId id, CanvasBounds bounds) => ApplyBounds(Control(id), ToRectangle(bounds));
        public bool IsVisible(LauncherControlId id) => Control(id).Visible;
        public bool IsLocked(LauncherControlId id) => State(id).Locked;
        public void SetVisible(LauncherControlId id, bool visible) => Control(id).Visible = visible;
        public void SetLocked(LauncherControlId id, bool locked) => State(id).Locked = locked;
        public void SetOrder(IReadOnlyList<LauncherControlId> ids)
            => _theme.Controls = ids.Select(Control).ToList();
        public DocumentState Capture() => new(
            _theme.Controls.Select(Clone).ToList(),
            _editorStates.Select(item => new LauncherCanvasControlState { Id = item.Id, Locked = item.Locked }).ToList());
        public void Restore(DocumentState state)
        {
            List<LauncherControlOverride> controls = state.Controls.Select(Clone).ToList();
            List<LauncherCanvasControlState> states = state.EditorStates.Select(item => new LauncherCanvasControlState { Id = item.Id, Locked = item.Locked }).ToList();
            Dictionary<LauncherControlId, LauncherControlOverride> controlIndex = controls.ToDictionary(item => item.Id);
            Dictionary<LauncherControlId, LauncherCanvasControlState> stateIndex = states.ToDictionary(item => item.Id);
            _theme.Controls = controls;
            _editorStates.Clear();
            foreach (LauncherCanvasControlState item in states) _editorStates.Add(item);
            _controls = controlIndex;
            _states = stateIndex;
        }
        public bool Equivalent(DocumentState left, DocumentState right) => LauncherCanvasDocument.Equivalent(left, right);
        internal LauncherControlOverride Control(LauncherControlId id) => _controls[id];
        private LauncherCanvasControlState State(LauncherControlId id) => _states[id];
    }
}
