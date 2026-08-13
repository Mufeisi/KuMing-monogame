using Launcher.ThemeRuntime;

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
    private const int SnapDistance = 6;
    private readonly LauncherTheme _theme;
    private readonly IList<LauncherCanvasControlState> _editorStates;
    private readonly List<HistoryEntry> _history = new();
    private readonly HashSet<LauncherControlId> _selection = new();
    private int _historyIndex;
    private int _savedIndex;

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
    }

    public event EventHandler? Changed;
    public IReadOnlyCollection<LauncherControlId> Selection => _selection;
    public IReadOnlyList<LauncherControlOverride> Controls => _theme.Controls.ToArray();
    public IReadOnlyList<LauncherCanvasGuide> SnapGuides { get; private set; } = Array.Empty<LauncherCanvasGuide>();
    public bool IsDirty => _historyIndex != _savedIndex;
    public bool CanUndo => _historyIndex > 0;
    public bool CanRedo => _historyIndex < _history.Count;

    public void MarkSaved() => _savedIndex = _historyIndex;
    public void MarkExternalChange()
    {
        if (_historyIndex == _savedIndex) _savedIndex = -1;
        Changed?.Invoke(this, EventArgs.Empty);
    }
    public void Select(IEnumerable<LauncherControlId> ids, bool additive = false)
    {
        SnapGuides = Array.Empty<LauncherCanvasGuide>();
        if (!additive) _selection.Clear();
        foreach (LauncherControlId id in ids) if (Enum.IsDefined(id)) _selection.Add(id);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public Rectangle GetBounds(LauncherControlId id)
    {
        LauncherControlOverride value = Find(id);
        return new Rectangle(value.X, value.Y, value.Width, value.Height);
    }

    public bool IsLocked(LauncherControlId id) => State(id).Locked;

    public void SetBounds(LauncherControlId id, Rectangle bounds)
    {
        if (IsLocked(id)) return;
        SnapGuides = Array.Empty<LauncherCanvasGuide>();
        Execute(() => ApplyBounds(Find(id), Clamp(bounds)));
    }

    public void ChangeSelectionLayout(LauncherCanvasLayoutChange change)
    {
        SnapGuides = Array.Empty<LauncherCanvasGuide>();
        Execute(() =>
        {
            foreach (LauncherControlOverride control in EditableSelection())
            {
                Rectangle bounds = GetBounds(control.Id);
                bounds.X = change.X ?? bounds.X;
                bounds.Y = change.Y ?? bounds.Y;
                bounds.Width = change.Width ?? bounds.Width;
                bounds.Height = change.Height ?? bounds.Height;
                ApplyBounds(control, Clamp(bounds));
            }
        });
    }

    public bool MoveSelection(int deltaX, int deltaY, bool snap)
    {
        LauncherControlOverride[] selected = EditableSelection();
        if (selected.Length == 0 || deltaX == 0 && deltaY == 0) return false;
        if (!snap) SnapGuides = Array.Empty<LauncherCanvasGuide>();
        Execute(() =>
        {
            foreach (LauncherControlOverride control in selected)
            {
                Rectangle moved = new(control.X + deltaX, control.Y + deltaY, control.Width, control.Height);
                ApplyBounds(control, Clamp(snap ? Snap(moved, control.Id) : moved));
            }
        });
        return true;
    }

    public bool ResizeSelection(int deltaWidth, int deltaHeight, bool snap)
    {
        LauncherControlOverride[] selected = EditableSelection();
        if (selected.Length == 0 || deltaWidth == 0 && deltaHeight == 0) return false;
        if (!snap) SnapGuides = Array.Empty<LauncherCanvasGuide>();
        Execute(() =>
        {
            foreach (LauncherControlOverride control in selected)
            {
                Rectangle resized = new(control.X, control.Y, Math.Max(8, control.Width + deltaWidth), Math.Max(8, control.Height + deltaHeight));
                ApplyBounds(control, Clamp(snap ? Snap(resized, control.Id) : resized));
            }
        });
        return true;
    }

    public void AlignSelection(LauncherCanvasAlignment alignment)
    {
        LauncherControlOverride[] values = EditableSelection();
        if (values.Length < 2) return;
        Execute(() =>
        {
            int target = alignment switch
            {
                LauncherCanvasAlignment.Left => values.Min(x => x.X),
                LauncherCanvasAlignment.HorizontalCenter => (int)Math.Round(values.Average(x => x.X + x.Width / 2d)),
                LauncherCanvasAlignment.Right => values.Max(x => x.X + x.Width),
                LauncherCanvasAlignment.Top => values.Min(x => x.Y),
                LauncherCanvasAlignment.VerticalCenter => (int)Math.Round(values.Average(x => x.Y + x.Height / 2d)),
                _ => values.Max(x => x.Y + x.Height),
            };
            foreach (LauncherControlOverride value in values)
            {
                Rectangle bounds = GetBounds(value.Id);
                bounds.X = alignment switch { LauncherCanvasAlignment.Left => target, LauncherCanvasAlignment.HorizontalCenter => target - bounds.Width / 2, LauncherCanvasAlignment.Right => target - bounds.Width, _ => bounds.X };
                bounds.Y = alignment switch { LauncherCanvasAlignment.Top => target, LauncherCanvasAlignment.VerticalCenter => target - bounds.Height / 2, LauncherCanvasAlignment.Bottom => target - bounds.Height, _ => bounds.Y };
                ApplyBounds(value, Clamp(bounds));
            }
        });
    }

    public void DistributeSelection(LauncherCanvasDistribution direction)
    {
        LauncherControlOverride[] values = EditableSelection();
        if (values.Length < 3) return;
        Execute(() =>
        {
            LauncherControlOverride[] ordered = direction == LauncherCanvasDistribution.Horizontal ? values.OrderBy(x => x.X).ToArray() : values.OrderBy(x => x.Y).ToArray();
            double first = direction == LauncherCanvasDistribution.Horizontal ? ordered[0].X : ordered[0].Y;
            double last = direction == LauncherCanvasDistribution.Horizontal ? ordered[^1].X : ordered[^1].Y;
            for (int index = 1; index < ordered.Length - 1; index++)
            {
                Rectangle bounds = GetBounds(ordered[index].Id);
                int position = (int)Math.Round(first + (last - first) * index / (ordered.Length - 1));
                if (direction == LauncherCanvasDistribution.Horizontal) bounds.X = position; else bounds.Y = position;
                ApplyBounds(ordered[index], Clamp(bounds));
            }
        });
    }

    public void SetLocked(IEnumerable<LauncherControlId> ids, bool locked) => Execute(() => { foreach (LauncherControlId id in ids) State(id).Locked = locked; });
    public void SetVisible(IEnumerable<LauncherControlId> ids, bool visible) => Execute(() =>
    {
        foreach (LauncherControlId id in ids)
            if (!IsLocked(id)) Find(id).Visible = visible;
    });
    public bool DeleteSelection()
    {
        LauncherControlId[] editable = _selection.Where(id => !IsLocked(id)).ToArray();
        if (editable.Length == 0) return false;
        SetVisible(editable, false);
        return true;
    }
    public void AddOrShow(LauncherControlId id) { Select([id]); SetVisible([id], true); }
    public void BringSelectionForward() => Execute(() =>
    {
        for (int index = _theme.Controls.Count - 2; index >= 0; index--)
            if (_selection.Contains(_theme.Controls[index].Id) && !_selection.Contains(_theme.Controls[index + 1].Id))
                (_theme.Controls[index], _theme.Controls[index + 1]) = (_theme.Controls[index + 1], _theme.Controls[index]);
    });
    public void SendSelectionBackward() => Execute(() =>
    {
        for (int index = 1; index < _theme.Controls.Count; index++)
            if (_selection.Contains(_theme.Controls[index].Id) && !_selection.Contains(_theme.Controls[index - 1].Id))
                (_theme.Controls[index], _theme.Controls[index - 1]) = (_theme.Controls[index - 1], _theme.Controls[index]);
    });

    public void ChangeSelectionStyle(LauncherCanvasStyleChange change)
    {
        Execute(() =>
        {
            foreach (LauncherControlOverride value in EditableSelection())
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

    public bool Undo()
    {
        if (!CanUndo) return false;
        Restore(_history[--_historyIndex].Before); Changed?.Invoke(this, EventArgs.Empty); return true;
    }

    public bool Redo()
    {
        if (!CanRedo) return false;
        Restore(_history[_historyIndex++].After); Changed?.Invoke(this, EventArgs.Empty); return true;
    }

    private void Execute(Action change)
    {
        DocumentState before = Capture();
        change();
        DocumentState after = Capture();
        if (Equivalent(before, after)) return;
        if (_historyIndex < _history.Count) _history.RemoveRange(_historyIndex, _history.Count - _historyIndex);
        _history.Add(new HistoryEntry(before, after)); _historyIndex++;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private Rectangle Snap(Rectangle bounds, LauncherControlId current)
    {
        int[] xTargets = [0, _theme.CanvasWidth - bounds.Width, .. _theme.Controls.Where(x => x.Id != current && x.Visible).SelectMany(x => new[] { x.X, x.X + x.Width, x.X - bounds.Width, x.X + x.Width - bounds.Width })];
        int[] yTargets = [0, _theme.CanvasHeight - bounds.Height, .. _theme.Controls.Where(x => x.Id != current && x.Visible).SelectMany(x => new[] { x.Y, x.Y + x.Height, x.Y - bounds.Height, x.Y + x.Height - bounds.Height })];
        int x = Nearest(bounds.X, xTargets, out bool snappedX), y = Nearest(bounds.Y, yTargets, out bool snappedY);
        var guides = new List<LauncherCanvasGuide>(2);
        if (snappedX) guides.Add(new LauncherCanvasGuide(true, x));
        if (snappedY) guides.Add(new LauncherCanvasGuide(false, y));
        SnapGuides = guides;
        return new Rectangle(x, y, bounds.Width, bounds.Height);
    }

    private static int Nearest(int value, IEnumerable<int> targets, out bool snapped)
    {
        int target = targets.OrderBy(item => Math.Abs(item - value)).First();
        snapped = Math.Abs(target - value) <= SnapDistance;
        return snapped ? target : value;
    }

    private Rectangle Clamp(Rectangle value)
    {
        int width = Math.Clamp(value.Width, 8, _theme.CanvasWidth);
        int height = Math.Clamp(value.Height, 8, _theme.CanvasHeight);
        return new Rectangle(Math.Clamp(value.X, 0, _theme.CanvasWidth - width), Math.Clamp(value.Y, 0, _theme.CanvasHeight - height), width, height);
    }

    private LauncherControlOverride[] EditableSelection() => Selected().Where(x => !IsLocked(x.Id) && x.Visible).ToArray();
    private LauncherControlOverride[] Selected() => _selection.Select(Find).ToArray();
    private LauncherControlOverride Find(LauncherControlId id) => _theme.Controls.Single(item => item.Id == id);
    private LauncherCanvasControlState State(LauncherControlId id) => _editorStates.Single(item => item.Id == id);
    private static void ApplyBounds(LauncherControlOverride value, Rectangle bounds) { value.X = bounds.X; value.Y = bounds.Y; value.Width = bounds.Width; value.Height = bounds.Height; }
    private List<LauncherControlOverride> CloneControls() => _theme.Controls.Select(Clone).ToList();
    private DocumentState Capture() => new(CloneControls(), _editorStates.Select(item => new LauncherCanvasControlState { Id = item.Id, Locked = item.Locked }).ToList());
    private void Restore(DocumentState state)
    {
        _theme.Controls = state.Controls.Select(Clone).ToList();
        _editorStates.Clear();
        foreach (LauncherCanvasControlState item in state.EditorStates) _editorStates.Add(new LauncherCanvasControlState { Id = item.Id, Locked = item.Locked });
    }
    private static bool Equivalent(DocumentState a, DocumentState b) =>
        a.Controls.Count == b.Controls.Count && a.Controls.Zip(b.Controls).All(pair => Properties(pair.First).SequenceEqual(Properties(pair.Second))) &&
        a.EditorStates.Count == b.EditorStates.Count && a.EditorStates.Zip(b.EditorStates).All(pair => pair.First.Id == pair.Second.Id && pair.First.Locked == pair.Second.Locked);
    private static object[] Properties(LauncherControlOverride x) => [x.Id, x.X, x.Y, x.Width, x.Height, x.Visible, x.ForeColor, x.BackColor, x.FontName, x.FontSize, x.Bold, x.OpacityPercent, x.BackgroundImage];
    private static LauncherControlOverride Clone(LauncherControlOverride x) => new() { Id = x.Id, X = x.X, Y = x.Y, Width = x.Width, Height = x.Height, Visible = x.Visible, ForeColor = x.ForeColor, BackColor = x.BackColor, FontName = x.FontName, FontSize = x.FontSize, Bold = x.Bold, OpacityPercent = x.OpacityPercent, BackgroundImage = x.BackgroundImage };
    private sealed record DocumentState(List<LauncherControlOverride> Controls, List<LauncherCanvasControlState> EditorStates);
    private sealed record HistoryEntry(DocumentState Before, DocumentState After);
}
