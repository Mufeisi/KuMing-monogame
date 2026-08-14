namespace LyoCrystal.DesignCore;

public readonly record struct CanvasBounds(int X, int Y, int Width, int Height);
public sealed record CanvasGuide(bool Vertical, int Position);
public sealed record CanvasBoundsChange(int? X = null, int? Y = null, int? Width = null, int? Height = null);
public sealed record CanvasDiagnostic<TId>(string Code, string Message, TId ElementId) where TId : notnull;
public enum CanvasAlignment { Left, HorizontalCenter, Right, Top, VerticalCenter, Bottom }
public enum CanvasDistribution { Horizontal, Vertical }

public interface ICanvasDocumentAdapter<TId, TSnapshot> where TId : notnull
{
    IReadOnlyList<TId> ElementIds { get; }
    CanvasBounds GetBounds(TId id);
    void SetBounds(TId id, CanvasBounds bounds);
    bool IsVisible(TId id);
    bool IsLocked(TId id);
    void SetVisible(TId id, bool visible);
    void SetLocked(TId id, bool locked);
    void SetOrder(IReadOnlyList<TId> ids);
    TSnapshot Capture();
    void Restore(TSnapshot state);
    bool Equivalent(TSnapshot left, TSnapshot right);
}

public interface ICanvasDocument<TId> where TId : notnull
{
    event EventHandler? Changed;
    IReadOnlyList<TId> ElementIds { get; }
    IReadOnlyCollection<TId> Selection { get; }
    IReadOnlyCollection<TId> EditableSelection { get; }
    IReadOnlyList<CanvasGuide> SnapGuides { get; }
    bool IsDirty { get; }
    bool CanUndo { get; }
    bool CanRedo { get; }
    void MarkSaved();
    void MarkExternalChange();
    void Select(IEnumerable<TId> ids, bool additive = false);
    CanvasBounds GetBounds(TId id);
    bool IsVisible(TId id);
    bool IsLocked(TId id);
    void SetBounds(TId id, CanvasBounds bounds);
    void ChangeSelectionBounds(CanvasBoundsChange change);
    bool MoveSelection(int deltaX, int deltaY, bool snap);
    bool ResizeSelection(int deltaWidth, int deltaHeight, bool snap);
    void AlignSelection(CanvasAlignment alignment);
    void DistributeSelection(CanvasDistribution direction);
    void SetLocked(IEnumerable<TId> ids, bool locked);
    void SetVisible(IEnumerable<TId> ids, bool visible);
    void ChangeEditableSelection(Action<TId> change);
    bool DeleteSelection();
    void AddOrShow(TId id);
    void BringSelectionForward();
    void SendSelectionBackward();
    IReadOnlyList<CanvasDiagnostic<TId>> Validate();
    bool Undo();
    bool Redo();
}

public sealed class CanvasDocument<TId, TSnapshot> : ICanvasDocument<TId> where TId : notnull
{
    private const int SnapDistance = 6;
    private const int MinimumSize = 8;
    private readonly ICanvasDocumentAdapter<TId, TSnapshot> _adapter;
    private readonly int _canvasWidth;
    private readonly int _canvasHeight;
    private readonly int _historyCapacity;
    private readonly List<HistoryEntry> _history = new();
    private readonly HashSet<TId> _selection = new();
    private int _historyIndex;
    private int _savedIndex;

    public CanvasDocument(ICanvasDocumentAdapter<TId, TSnapshot> adapter, int canvasWidth, int canvasHeight, int historyCapacity = 100)
    {
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        if (canvasWidth < MinimumSize) throw new ArgumentOutOfRangeException(nameof(canvasWidth));
        if (canvasHeight < MinimumSize) throw new ArgumentOutOfRangeException(nameof(canvasHeight));
        if (historyCapacity < 1) throw new ArgumentOutOfRangeException(nameof(historyCapacity));
        _canvasWidth = canvasWidth;
        _canvasHeight = canvasHeight;
        _historyCapacity = historyCapacity;
    }

    public event EventHandler? Changed;
    public IReadOnlyList<TId> ElementIds => _adapter.ElementIds;
    public IReadOnlyCollection<TId> Selection => _selection.ToArray();
    public IReadOnlyCollection<TId> EditableSelection => EditableSelectionArray();
    public IReadOnlyList<CanvasGuide> SnapGuides { get; private set; } = Array.Empty<CanvasGuide>();
    public bool IsDirty => _historyIndex != _savedIndex;
    public bool CanUndo => _historyIndex > 0;
    public bool CanRedo => _historyIndex < _history.Count;

    public void MarkSaved() => _savedIndex = _historyIndex;

    public void MarkExternalChange()
    {
        if (_historyIndex == _savedIndex) _savedIndex = -1;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Select(IEnumerable<TId> ids, bool additive = false)
    {
        ArgumentNullException.ThrowIfNull(ids);
        SnapGuides = Array.Empty<CanvasGuide>();
        if (!additive) _selection.Clear();
        HashSet<TId> known = _adapter.ElementIds.ToHashSet();
        foreach (TId id in ids)
            if (known.Contains(id)) _selection.Add(id);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public CanvasBounds GetBounds(TId id) => _adapter.GetBounds(id);
    public bool IsVisible(TId id) => _adapter.IsVisible(id);
    public bool IsLocked(TId id) => _adapter.IsLocked(id);

    public void SetBounds(TId id, CanvasBounds bounds)
    {
        if (_adapter.IsLocked(id)) return;
        SnapGuides = Array.Empty<CanvasGuide>();
        ApplyChange(() => _adapter.SetBounds(id, Clamp(bounds)));
    }

    public bool MoveSelection(int deltaX, int deltaY, bool snap)
    {
        TId[] selected = EditableSelectionArray();
        if (selected.Length == 0 || deltaX == 0 && deltaY == 0) return false;
        if (!snap) SnapGuides = Array.Empty<CanvasGuide>();
        ApplyChange(() =>
        {
            foreach (TId id in selected)
            {
                CanvasBounds current = _adapter.GetBounds(id);
                CanvasBounds moved = current with { X = current.X + deltaX, Y = current.Y + deltaY };
                _adapter.SetBounds(id, Clamp(snap ? Snap(moved, id) : moved));
            }
        });
        return true;
    }

    public bool ResizeSelection(int deltaWidth, int deltaHeight, bool snap)
    {
        TId[] selected = EditableSelectionArray();
        if (selected.Length == 0 || deltaWidth == 0 && deltaHeight == 0) return false;
        if (!snap) SnapGuides = Array.Empty<CanvasGuide>();
        ApplyChange(() =>
        {
            foreach (TId id in selected)
            {
                CanvasBounds current = _adapter.GetBounds(id);
                CanvasBounds resized = current with
                {
                    Width = Math.Max(MinimumSize, current.Width + deltaWidth),
                    Height = Math.Max(MinimumSize, current.Height + deltaHeight)
                };
                _adapter.SetBounds(id, Clamp(snap ? Snap(resized, id) : resized));
            }
        });
        return true;
    }

    public void ChangeSelectionBounds(CanvasBoundsChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        SnapGuides = Array.Empty<CanvasGuide>();
        ApplyChange(() =>
        {
            foreach (TId id in EditableSelectionArray())
            {
                CanvasBounds bounds = _adapter.GetBounds(id);
                _adapter.SetBounds(id, Clamp(new CanvasBounds(
                    change.X ?? bounds.X,
                    change.Y ?? bounds.Y,
                    change.Width ?? bounds.Width,
                    change.Height ?? bounds.Height)));
            }
        });
    }

    public void AlignSelection(CanvasAlignment alignment)
    {
        TId[] ids = EditableSelectionArray();
        if (ids.Length < 2) return;
        ApplyChange(() =>
        {
            CanvasBounds[] values = ids.Select(_adapter.GetBounds).ToArray();
            int target = alignment switch
            {
                CanvasAlignment.Left => values.Min(value => value.X),
                CanvasAlignment.HorizontalCenter => (int)Math.Round(values.Average(value => value.X + value.Width / 2d)),
                CanvasAlignment.Right => values.Max(value => value.X + value.Width),
                CanvasAlignment.Top => values.Min(value => value.Y),
                CanvasAlignment.VerticalCenter => (int)Math.Round(values.Average(value => value.Y + value.Height / 2d)),
                _ => values.Max(value => value.Y + value.Height),
            };
            foreach (TId id in ids)
            {
                CanvasBounds bounds = _adapter.GetBounds(id);
                int x = alignment switch { CanvasAlignment.Left => target, CanvasAlignment.HorizontalCenter => target - bounds.Width / 2, CanvasAlignment.Right => target - bounds.Width, _ => bounds.X };
                int y = alignment switch { CanvasAlignment.Top => target, CanvasAlignment.VerticalCenter => target - bounds.Height / 2, CanvasAlignment.Bottom => target - bounds.Height, _ => bounds.Y };
                _adapter.SetBounds(id, Clamp(bounds with { X = x, Y = y }));
            }
        });
    }

    public void DistributeSelection(CanvasDistribution direction)
    {
        TId[] ids = EditableSelectionArray();
        if (ids.Length < 3) return;
        ApplyChange(() =>
        {
            TId[] ordered = direction == CanvasDistribution.Horizontal
                ? ids.OrderBy(id => _adapter.GetBounds(id).X).ToArray()
                : ids.OrderBy(id => _adapter.GetBounds(id).Y).ToArray();
            double first = direction == CanvasDistribution.Horizontal ? _adapter.GetBounds(ordered[0]).X : _adapter.GetBounds(ordered[0]).Y;
            double last = direction == CanvasDistribution.Horizontal ? _adapter.GetBounds(ordered[^1]).X : _adapter.GetBounds(ordered[^1]).Y;
            for (int index = 1; index < ordered.Length - 1; index++)
            {
                TId id = ordered[index];
                CanvasBounds bounds = _adapter.GetBounds(id);
                int position = (int)Math.Round(first + (last - first) * index / (ordered.Length - 1));
                _adapter.SetBounds(id, Clamp(direction == CanvasDistribution.Horizontal ? bounds with { X = position } : bounds with { Y = position }));
            }
        });
    }

    public void SetLocked(IEnumerable<TId> ids, bool locked) => ApplyChange(() =>
    {
        foreach (TId id in Existing(ids)) _adapter.SetLocked(id, locked);
    });

    public void SetVisible(IEnumerable<TId> ids, bool visible) => ApplyChange(() =>
    {
        foreach (TId id in Existing(ids))
            if (!_adapter.IsLocked(id)) _adapter.SetVisible(id, visible);
    });

    public void ChangeEditableSelection(Action<TId> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        TId[] ids = EditableSelectionArray();
        if (ids.Length == 0) return;
        ApplyChange(() =>
        {
            foreach (TId id in ids) change(id);
        });
    }

    public bool DeleteSelection()
    {
        TId[] editable = _selection.Where(id => !_adapter.IsLocked(id)).ToArray();
        if (editable.Length == 0) return false;
        SetVisible(editable, false);
        return true;
    }

    public void AddOrShow(TId id)
    {
        Select([id]);
        SetVisible([id], true);
    }

    public void BringSelectionForward() => ReorderSelection(forward: true);
    public void SendSelectionBackward() => ReorderSelection(forward: false);

    public IReadOnlyList<CanvasDiagnostic<TId>> Validate()
    {
        var diagnostics = new List<CanvasDiagnostic<TId>>();
        foreach (TId id in _adapter.ElementIds)
        {
            CanvasBounds bounds = _adapter.GetBounds(id);
            if (bounds.Width <= 0 || bounds.Height <= 0)
                diagnostics.Add(new CanvasDiagnostic<TId>("DESIGN-GEOMETRY-002", "设计对象的宽度和高度必须大于零。", id));
            if (bounds.X < 0 || bounds.Y < 0 || (long)bounds.X + bounds.Width > _canvasWidth || (long)bounds.Y + bounds.Height > _canvasHeight)
                diagnostics.Add(new CanvasDiagnostic<TId>("DESIGN-GEOMETRY-001", "设计对象必须位于画布范围内。", id));
        }
        return diagnostics.ToArray();
    }

    public void ApplyChange(Action change)
    {
        ArgumentNullException.ThrowIfNull(change);
        TSnapshot before = _adapter.Capture();
        TSnapshot after;
        try
        {
            change();
            after = _adapter.Capture();
        }
        catch (Exception changeError)
        {
            try
            {
                _adapter.Restore(before);
            }
            catch (Exception rollbackError)
            {
                throw new AggregateException("设计文档变更失败，且无法恢复操作前状态。", changeError, rollbackError);
            }
            throw;
        }
        if (_adapter.Equivalent(before, after)) return;
        if (_historyIndex < _history.Count)
        {
            if (_savedIndex > _historyIndex) _savedIndex = -1;
            _history.RemoveRange(_historyIndex, _history.Count - _historyIndex);
        }
        _history.Add(new HistoryEntry(before, after));
        _historyIndex++;
        if (_history.Count > _historyCapacity)
        {
            _history.RemoveAt(0);
            _historyIndex--;
            _savedIndex = _savedIndex > 0 ? _savedIndex - 1 : -1;
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public bool Undo()
    {
        if (!CanUndo) return false;
        RestoreHistory(_history[_historyIndex - 1].Before, "撤销");
        _historyIndex--;
        SnapGuides = Array.Empty<CanvasGuide>();
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public bool Redo()
    {
        if (!CanRedo) return false;
        RestoreHistory(_history[_historyIndex].After, "重做");
        _historyIndex++;
        SnapGuides = Array.Empty<CanvasGuide>();
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private void RestoreHistory(TSnapshot target, string operation)
    {
        TSnapshot current = _adapter.Capture();
        try
        {
            _adapter.Restore(target);
        }
        catch (Exception restoreError)
        {
            try
            {
                _adapter.Restore(current);
            }
            catch (Exception rollbackError)
            {
                throw new AggregateException($"{operation}失败，且无法恢复操作前状态。", restoreError, rollbackError);
            }
            throw new InvalidOperationException($"{operation}失败，设计文档已恢复到操作前状态。", restoreError);
        }
    }

    private TId[] EditableSelectionArray()
    {
        HashSet<TId> known = _adapter.ElementIds.ToHashSet();
        return _selection.Where(id => known.Contains(id) && !_adapter.IsLocked(id) && _adapter.IsVisible(id)).ToArray();
    }

    private IEnumerable<TId> Existing(IEnumerable<TId> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        HashSet<TId> known = _adapter.ElementIds.ToHashSet();
        return ids.Where(known.Contains).ToArray();
    }

    private void ReorderSelection(bool forward)
    {
        HashSet<TId> editable = EditableSelectionArray().ToHashSet();
        if (editable.Count == 0) return;
        ApplyChange(() =>
        {
            List<TId> order = _adapter.ElementIds.ToList();
            if (forward)
            {
                for (int index = order.Count - 2; index >= 0; index--)
                    if (editable.Contains(order[index]) && !editable.Contains(order[index + 1]))
                        (order[index], order[index + 1]) = (order[index + 1], order[index]);
            }
            else
            {
                for (int index = 1; index < order.Count; index++)
                    if (editable.Contains(order[index]) && !editable.Contains(order[index - 1]))
                        (order[index], order[index - 1]) = (order[index - 1], order[index]);
            }
            _adapter.SetOrder(order);
        });
    }

    private CanvasBounds Snap(CanvasBounds bounds, TId current)
    {
        var xTargets = new List<int> { 0, _canvasWidth - bounds.Width };
        var yTargets = new List<int> { 0, _canvasHeight - bounds.Height };
        foreach (TId id in _adapter.ElementIds.Where(id => !EqualityComparer<TId>.Default.Equals(id, current) && _adapter.IsVisible(id)))
        {
            CanvasBounds peer = _adapter.GetBounds(id);
            xTargets.AddRange([peer.X, peer.X + peer.Width, peer.X - bounds.Width, peer.X + peer.Width - bounds.Width]);
            yTargets.AddRange([peer.Y, peer.Y + peer.Height, peer.Y - bounds.Height, peer.Y + peer.Height - bounds.Height]);
        }

        int x = Nearest(bounds.X, xTargets, out bool snappedX);
        int y = Nearest(bounds.Y, yTargets, out bool snappedY);
        var guides = new List<CanvasGuide>(2);
        if (snappedX) guides.Add(new CanvasGuide(true, x));
        if (snappedY) guides.Add(new CanvasGuide(false, y));
        SnapGuides = guides.ToArray();
        return bounds with { X = x, Y = y };
    }

    private static int Nearest(int value, IEnumerable<int> targets, out bool snapped)
    {
        int target = targets.OrderBy(item => Math.Abs(item - value)).First();
        snapped = Math.Abs(target - value) <= SnapDistance;
        return snapped ? target : value;
    }

    private CanvasBounds Clamp(CanvasBounds value)
    {
        int width = Math.Clamp(value.Width, MinimumSize, _canvasWidth);
        int height = Math.Clamp(value.Height, MinimumSize, _canvasHeight);
        return new CanvasBounds(
            Math.Clamp(value.X, 0, _canvasWidth - width),
            Math.Clamp(value.Y, 0, _canvasHeight - height),
            width,
            height);
    }

    private sealed record HistoryEntry(TSnapshot Before, TSnapshot After);
}
