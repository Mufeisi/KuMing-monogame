using LyoCrystal.DesignCore;
using Xunit;

namespace Launcher.DesignCore.Tests;

public sealed class CanvasDocumentTests
{
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
        private List<Element> _elements = elements.ToList();
        public bool FailNextRestoreAfterPartialWrite { get; set; }
        public IReadOnlyList<string> ElementIds => _elements.Select(item => item.Id).ToArray();
        public CanvasBounds GetBounds(string id) => Find(id).Bounds;
        public void SetBounds(string id, CanvasBounds bounds) => Replace(Find(id) with { Bounds = bounds });
        public bool IsVisible(string id) => Find(id).Visible;
        public bool IsLocked(string id) => Find(id).Locked;
        public void SetVisible(string id, bool visible) => Replace(Find(id) with { Visible = visible });
        public Element[] Capture() => _elements.ToArray();
        public void Restore(Element[] state)
        {
            _elements = state.ToList();
            if (!FailNextRestoreAfterPartialWrite) return;
            FailNextRestoreAfterPartialWrite = false;
            throw new InvalidOperationException("模拟恢复失败");
        }
        public bool Equivalent(Element[] left, Element[] right) => left.SequenceEqual(right);
        private Element Find(string id) => _elements.Single(item => item.Id == id);
        private void Replace(Element value) => _elements[_elements.FindIndex(item => item.Id == value.Id)] = value;
    }
}
