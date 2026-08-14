namespace LyoCrystal.DesignCore;

public readonly record struct CanvasBounds(int X, int Y, int Width, int Height);
public sealed record CanvasGuide(bool Vertical, int Position);

public interface ICanvasDocumentAdapter<TId, TSnapshot> where TId : notnull
{
    IReadOnlyList<TId> ElementIds { get; }
    CanvasBounds GetBounds(TId id);
    void SetBounds(TId id, CanvasBounds bounds);
    bool IsVisible(TId id);
    bool IsLocked(TId id);
    TSnapshot Capture();
    void Restore(TSnapshot state);
    bool Equivalent(TSnapshot left, TSnapshot right);
}

public sealed class CanvasDocument<TId, TSnapshot> where TId : notnull
{
    private const int SnapDistance = 6;
    private const int MinimumSize = 8;
    private readonly ICanvasDocumentAdapter<TId, TSnapshot> _adapter;
    private readonly int _canvasWidth;
    private readonly int _canvasHeight;
    private readonly List<HistoryEntry> _history = new();
    private readonly HashSet<TId> _selection = new();
    private int _historyIndex;
    private int _savedIndex;

    public CanvasDocument(ICanvasDocumentAdapter<TId, TSnapshot> adapter, int canvasWidth, int canvasHeight)
    {
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        if (canvasWidth < MinimumSize) throw new ArgumentOutOfRangeException(nameof(canvasWidth));
        if (canvasHeight < MinimumSize) throw new ArgumentOutOfRangeException(nameof(canvasHeight));
        _canvasWidth = canvasWidth;
        _canvasHeight = canvasHeight;
    }

    public event EventHandler? Changed;
    public IReadOnlyCollection<TId> Selection => _selection;
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

    public void SetBounds(TId id, CanvasBounds bounds)
    {
        if (_adapter.IsLocked(id)) return;
        SnapGuides = Array.Empty<CanvasGuide>();
        ApplyChange(() => _adapter.SetBounds(id, Clamp(bounds)));
    }

    public bool MoveSelection(int deltaX, int deltaY, bool snap)
    {
        TId[] selected = EditableSelection();
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
        TId[] selected = EditableSelection();
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

    private TId[] EditableSelection() => _selection
        .Where(id => _adapter.ElementIds.Contains(id) && !_adapter.IsLocked(id) && _adapter.IsVisible(id))
        .ToArray();

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
        SnapGuides = guides;
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
