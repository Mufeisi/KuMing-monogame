using LyoCrystal.DesignCore;
using Xunit;

namespace Launcher.DesignCore.Tests;

public sealed class CanvasDocumentTests
{
    [Fact]
    public void LayoutAlignmentDistributionStateAndLayerCommandsAreUndoable()
    {
        var adapter = new MemoryCanvasAdapter(
            new Element("first", new CanvasBounds(10, 10, 20, 20)),
            new Element("middle", new CanvasBounds(40, 40, 20, 20)),
            new Element("last", new CanvasBounds(100, 80, 20, 20)));
        var document = new CanvasDocument<string, Element[]>(adapter, 200, 120);
        document.Select(["first", "middle", "last"]);

        document.AlignSelection(CanvasAlignment.Top);
        document.DistributeSelection(CanvasDistribution.Horizontal);
        document.SetLocked(["last"], true);
        document.SetVisible(["first", "last"], false);
        document.Select(["middle"]);
        document.BringSelectionForward();

        Assert.Equal(10, document.GetBounds("middle").Y);
        Assert.Equal(55, document.GetBounds("middle").X);
        Assert.False(adapter.IsVisible("first"));
        Assert.True(adapter.IsVisible("last"));
        Assert.Equal(["first", "last", "middle"], adapter.ElementIds);
        Assert.True(document.Undo());
        Assert.Equal(["first", "middle", "last"], adapter.ElementIds);
        Assert.True(document.Undo());
        Assert.True(adapter.IsVisible("first"));
        Assert.True(document.Undo());
        Assert.False(adapter.IsLocked("last"));
    }

    [Fact]
    public void LockedSelectionCannotBypassLockThroughLayerCommands()
    {
        var adapter = new MemoryCanvasAdapter(
            new Element("first", new CanvasBounds(10, 10, 20, 20)),
            new Element("locked", new CanvasBounds(40, 10, 20, 20), Locked: true),
            new Element("last", new CanvasBounds(70, 10, 20, 20)));
        var document = new CanvasDocument<string, Element[]>(adapter, 120, 80);
        document.Select(["locked"]);

        document.BringSelectionForward();
        document.SendSelectionBackward();

        Assert.Equal(["first", "locked", "last"], adapter.ElementIds);
        Assert.False(document.CanUndo);
    }

    [Fact]
    public void HistoryCapacityPreservesSavedCheckpointDirtySemanticsAfterEviction()
    {
        var adapter = new MemoryCanvasAdapter(new Element("button", new CanvasBounds(10, 10, 8, 8)));
        var document = new CanvasDocument<string, Element[]>(adapter, 100, 100, historyCapacity: 3);
        document.Select(["button"]);
        document.MoveSelection(1, 0, snap: false);
        document.MoveSelection(1, 0, snap: false);
        document.MarkSaved();
        Assert.False(document.IsDirty);

        document.MoveSelection(1, 0, snap: false);
        document.MoveSelection(1, 0, snap: false);
        Assert.True(document.IsDirty);
        Assert.True(document.Undo());
        Assert.True(document.IsDirty);
        Assert.True(document.Undo());
        Assert.False(document.IsDirty);
        Assert.True(document.Undo());
        Assert.True(document.IsDirty);
        Assert.False(document.Undo());

        var initialCheckpointAdapter = new MemoryCanvasAdapter(new Element("button", new CanvasBounds(10, 10, 8, 8)));
        var initialCheckpointDocument = new CanvasDocument<string, Element[]>(initialCheckpointAdapter, 100, 100, historyCapacity: 2);
        initialCheckpointDocument.Select(["button"]);
        for (int index = 0; index < 3; index++) initialCheckpointDocument.MoveSelection(1, 0, snap: false);
        Assert.True(initialCheckpointDocument.Undo());
        Assert.True(initialCheckpointDocument.Undo());
        Assert.False(initialCheckpointDocument.Undo());
        Assert.True(initialCheckpointDocument.IsDirty);
    }

    [Fact]
    public void FiveHundredElementsMeetContinuousSelectionAndDragLatencyGate()
    {
        Element[] elements = Enumerable.Range(0, 500)
            .Select(index => new Element(index.ToString(), new CanvasBounds(index % 20 * 10, index / 20 * 10, 8, 8)))
            .ToArray();
        var adapter = new MemoryCanvasAdapter(elements);
        var document = new CanvasDocument<string, Element[]>(adapter, 400, 400);
        document.Select(adapter.ElementIds);
        for (int index = 0; index < 5; index++) document.MoveSelection(index % 2 == 0 ? 1 : -1, 0, snap: false);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        document.Select(adapter.ElementIds);
        stopwatch.Stop();
        TimeSpan selectionLatency = stopwatch.Elapsed;

        var dragLatencies = new List<TimeSpan>(60);
        for (int index = 0; index < 60; index++)
        {
            stopwatch.Restart();
            Assert.True(document.MoveSelection(index % 2 == 0 ? 1 : -1, 0, snap: false));
            stopwatch.Stop();
            dragLatencies.Add(stopwatch.Elapsed);
        }

        TimeSpan[] ordered = dragLatencies.OrderBy(value => value).ToArray();
        TimeSpan percentile95 = ordered[(int)Math.Ceiling(ordered.Length * .95) - 1];
        TimeSpan maximumDragLatency = ordered[^1];
        Assert.True(selectionLatency < TimeSpan.FromMilliseconds(50), $"500 个对象选择耗时 {selectionLatency.TotalMilliseconds:F1}ms");
        Assert.True(percentile95 < TimeSpan.FromMilliseconds(50), $"500 个对象拖动 P95 耗时 {percentile95.TotalMilliseconds:F1}ms");
        Assert.True(maximumDragLatency < TimeSpan.FromMilliseconds(100), $"500 个对象最大单次拖动耗时 {maximumDragLatency.TotalMilliseconds:F1}ms");
    }

    [Fact]
    public void GeometryDiagnosticsAreStableAndObservableThroughTheCoreInterface()
    {
        var adapter = new MemoryCanvasAdapter(
            new Element("outside", new CanvasBounds(-1, 0, 20, 20)),
            new Element("empty", new CanvasBounds(10, 10, 0, 20)));
        var document = new CanvasDocument<string, Element[]>(adapter, 100, 100);

        IReadOnlyList<CanvasDiagnostic<string>> diagnostics = document.Validate();

        Assert.Contains(diagnostics, item => item.Code == "DESIGN-GEOMETRY-001" && item.ElementId == "outside");
        Assert.Contains(diagnostics, item => item.Code == "DESIGN-GEOMETRY-002" && item.ElementId == "empty");
    }

    [Fact]
    public void SelectionMoveSnapUndoRedoAndDirtyStateUseOnePublicInterface()
    {
        var adapter = new MemoryCanvasAdapter(
            new Element("peer", new CanvasBounds(0, 20, 100, 40)),
            new Element("button", new CanvasBounds(108, 20, 100, 40)));
        var document = new CanvasDocument<string, Element[]>(adapter, 640, 480);

        document.Select(["button"]);
        Assert.True(document.MoveSelection(-3, 0, snap: true));

        Assert.Equal(new CanvasBounds(100, 20, 100, 40), document.GetBounds("button"));
        Assert.Contains(document.SnapGuides, guide => guide.Vertical && guide.Position == 100);
        Assert.True(document.IsDirty);
        Assert.True(document.Undo());
        Assert.Equal(new CanvasBounds(108, 20, 100, 40), document.GetBounds("button"));
        Assert.False(document.IsDirty);
        Assert.True(document.Redo());
        Assert.Equal(new CanvasBounds(100, 20, 100, 40), document.GetBounds("button"));
    }

    [Fact]
    public void LockedOrHiddenSelectionCannotMoveAndCanvasBoundsAreClamped()
    {
        var adapter = new MemoryCanvasAdapter(
            new Element("locked", new CanvasBounds(10, 10, 40, 40), Locked: true),
            new Element("hidden", new CanvasBounds(20, 20, 40, 40), Visible: false),
            new Element("active", new CanvasBounds(30, 30, 40, 40)));
        var document = new CanvasDocument<string, Element[]>(adapter, 200, 100);

        document.Select(["locked", "hidden"]);
        Assert.False(document.MoveSelection(10, 10, snap: false));
        document.Select(["active"]);
        Assert.True(document.MoveSelection(999, 999, snap: false));

        Assert.Equal(new CanvasBounds(160, 60, 40, 40), document.GetBounds("active"));
    }

    [Fact]
    public void AdapterChangesShareTheSameUndoHistoryAsGeometryCommands()
    {
        var adapter = new MemoryCanvasAdapter(new Element("button", new CanvasBounds(10, 10, 40, 40)));
        var document = new CanvasDocument<string, Element[]>(adapter, 200, 100);
        document.Select(["button"]);

        document.MoveSelection(10, 0, snap: false);
        document.ApplyChange(() => adapter.SetVisible("button", false));

        Assert.True(document.Undo());
        Assert.True(adapter.IsVisible("button"));
        Assert.True(document.Undo());
        Assert.Equal(new CanvasBounds(10, 10, 40, 40), document.GetBounds("button"));
    }

    [Fact]
    public void ReplacingSavedHistoryKeepsDocumentDirtyAndFailedChangeRollsBack()
    {
        var adapter = new MemoryCanvasAdapter(new Element("button", new CanvasBounds(10, 10, 40, 40)));
        var document = new CanvasDocument<string, Element[]>(adapter, 200, 100);
        document.Select(["button"]);
        document.MoveSelection(10, 0, snap: false);
        document.MoveSelection(10, 0, snap: false);
        document.MarkSaved();
        Assert.True(document.Undo());

        document.MoveSelection(5, 0, snap: false);
        Assert.True(document.IsDirty);
        CanvasBounds beforeFailure = document.GetBounds("button");

        Assert.Throws<InvalidOperationException>(() => document.ApplyChange(() =>
        {
            adapter.SetVisible("button", false);
            throw new InvalidOperationException("模拟 Adapter 失败");
        }));
        Assert.True(adapter.IsVisible("button"));
        Assert.Equal(beforeFailure, document.GetBounds("button"));
    }

    [Fact]
    public void FailedUndoRestoresCurrentFactsAndDoesNotAdvanceHistoryCursor()
    {
        var adapter = new MemoryCanvasAdapter(new Element("button", new CanvasBounds(10, 10, 40, 40)));
        var document = new CanvasDocument<string, Element[]>(adapter, 200, 100);
        document.Select(["button"]);
        document.MoveSelection(10, 0, snap: false);
        CanvasBounds changed = document.GetBounds("button");
        adapter.FailNextRestoreAfterPartialWrite = true;

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => document.Undo());

        Assert.Contains("已恢复到操作前状态", error.Message, StringComparison.Ordinal);
        Assert.Equal(changed, document.GetBounds("button"));
        Assert.True(document.CanUndo);
        Assert.False(document.CanRedo);
        Assert.True(document.IsDirty);
        Assert.True(document.Undo());
        Assert.Equal(new CanvasBounds(10, 10, 40, 40), document.GetBounds("button"));
    }

    [Fact]
    public void CoreAssemblyDoesNotReferenceGuiOrRenderingFrameworks()
    {
        string[] forbidden = ["System.Windows.Forms", "FairyGUI", "MonoGame", "Vortice"];
        string[] references = typeof(CanvasDocument<,>).Assembly.GetReferencedAssemblies().Select(item => item.Name ?? string.Empty).ToArray();

        Assert.DoesNotContain(references, name => forbidden.Any(item => name.Contains(item, StringComparison.OrdinalIgnoreCase)));
    }

    private sealed record Element(string Id, CanvasBounds Bounds, bool Visible = true, bool Locked = false);

    private sealed class MemoryCanvasAdapter(params Element[] elements) : ICanvasDocumentAdapter<string, Element[]>
    {
        private List<string> _order = elements.Select(item => item.Id).ToList();
        private Dictionary<string, Element> _elements = elements.ToDictionary(item => item.Id);
        public bool FailNextRestoreAfterPartialWrite { get; set; }
        public IReadOnlyList<string> ElementIds => _order.ToArray();
        public CanvasBounds GetBounds(string id) => Find(id).Bounds;
        public void SetBounds(string id, CanvasBounds bounds) => Replace(Find(id) with { Bounds = bounds });
        public bool IsVisible(string id) => Find(id).Visible;
        public bool IsLocked(string id) => Find(id).Locked;
        public void SetVisible(string id, bool visible) => Replace(Find(id) with { Visible = visible });
        public void SetLocked(string id, bool locked) => Replace(Find(id) with { Locked = locked });
        public void SetOrder(IReadOnlyList<string> ids) => _order = ids.ToList();
        public Element[] Capture() => _order.Select(Find).ToArray();
        public void Restore(Element[] state)
        {
            _order = state.Select(item => item.Id).ToList();
            _elements = state.ToDictionary(item => item.Id);
            if (!FailNextRestoreAfterPartialWrite) return;
            FailNextRestoreAfterPartialWrite = false;
            throw new InvalidOperationException("模拟恢复失败");
        }
        public bool Equivalent(Element[] left, Element[] right) => left.SequenceEqual(right);
        private Element Find(string id) => _elements[id];
        private void Replace(Element value) => _elements[value.Id] = value;
    }
}
