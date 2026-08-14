using System.ComponentModel;
using LyoCrystal.DesignCore;
using Shared.CustomGui;

namespace LyoCrystal.LauncherEditor;

internal sealed record CustomGuiWorkspaceSnapshot(int ObjectTreeWidth, int PropertiesWidth, Size CanvasSize, int ObjectCount, string SelectedId, bool Dirty);

internal sealed class CustomGuiCanvasEditorPanel : UserControl
{
    private const int ObjectTreeWidth = 190;
    private const int PropertiesWidth = 250;
    private readonly CustomGuiCanvasDocument _document;
    private readonly TreeView _tree = new() { Dock = DockStyle.Fill, HideSelection = false };
    private readonly PropertyGrid _properties = new() { Dock = DockStyle.Fill, HelpVisible = true, ToolbarVisible = false };
    private readonly Surface _surface;
    private readonly Label _status = new() { AutoSize = true, Padding = new Padding(8, 7, 0, 0) };

    public CustomGuiCanvasEditorPanel(CustomGuiCanvasDocument document)
    {
        _document = document;
        _surface = new Surface(document) { Dock = DockStyle.Fill, TabStop = true };
        Dock = DockStyle.Fill;

        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42, WrapContents = false, Padding = new Padding(6, 4, 0, 0) };
        AddButton(toolbar, "撤销", () => _document.Core.Undo());
        AddButton(toolbar, "重做", () => _document.Core.Redo());
        AddButton(toolbar, "显示/隐藏", ToggleVisible);
        AddButton(toolbar, "锁定/解锁", ToggleLocked);
        AddButton(toolbar, "左对齐", () => _document.Core.AlignSelection(CanvasAlignment.Left));
        AddButton(toolbar, "顶对齐", () => _document.Core.AlignSelection(CanvasAlignment.Top));
        toolbar.Controls.Add(_status);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty, Padding = Padding.Empty };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ObjectTreeWidth));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, PropertiesWidth));
        layout.Controls.Add(BuildGroup("对象树", _tree), 0, 0);
        layout.Controls.Add(BuildGroup("设计画布 · 1280 × 720", _surface), 1, 0);
        layout.Controls.Add(BuildGroup("属性", _properties), 2, 0);
        Controls.Add(layout);
        Controls.Add(toolbar);

        _tree.AfterSelect += (_, _) => SelectTreeNode();
        _surface.Selected += id => Select(id);
        _document.Core.Changed += OnDocumentChanged;
        RebuildTree();
        Select(_document.Core.ElementIds.First());
    }

    internal void Select(string id)
    {
        _document.Core.Select([id]);
        TreeNode? node = _tree.Nodes.Cast<TreeNode>().FirstOrDefault(item => Equals(item.Tag, id));
        if (node is not null) _tree.SelectedNode = node;
        RefreshState();
    }

    internal CustomGuiWorkspaceSnapshot CaptureForEvidence()
    {
        string selected = _document.Core.Selection.FirstOrDefault() ?? string.Empty;
        return new(ObjectTreeWidth, PropertiesWidth, new Size(_document.Runtime.Viewport.ReferenceWidth, _document.Runtime.Viewport.ReferenceHeight), _document.Core.ElementIds.Count, selected, _document.Core.IsDirty);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _document.Core.Changed -= OnDocumentChanged;
        base.Dispose(disposing);
    }

    private void OnDocumentChanged(object? sender, EventArgs e) => RefreshState();
    private void RefreshState()
    {
        string? id = _document.Core.Selection.FirstOrDefault();
        _properties.SelectedObject = id is null ? null : new ElementPropertyView(_document, id);
        _status.Text = id is null ? "未选择对象" : $"已选择 {id} · {(_document.Core.IsDirty ? "未保存" : "已保存")}";
        RebuildTree();
        _surface.Invalidate();
    }

    private void RebuildTree()
    {
        string? selected = _document.Core.Selection.FirstOrDefault();
        _tree.BeginUpdate();
        _tree.Nodes.Clear();
        foreach (string id in _document.Core.ElementIds)
        {
            CustomGuiElement element = _document.Element(id);
            string flags = $"{(element.Visible ? "" : " [隐藏]")}{(_document.Core.IsLocked(id) ? " [锁定]" : "")}";
            _tree.Nodes.Add(new TreeNode($"{TypeName(element)} · {id}{flags}") { Tag = id });
        }
        _tree.SelectedNode = _tree.Nodes.Cast<TreeNode>().FirstOrDefault(node => Equals(node.Tag, selected));
        _tree.EndUpdate();
    }

    private void SelectTreeNode()
    {
        if (_tree.SelectedNode?.Tag is string id && !_document.Core.Selection.Contains(id)) _document.Core.Select([id]);
    }

    private void ToggleVisible()
    {
        string[] ids = _document.Core.Selection.ToArray();
        foreach (string id in ids) _document.Core.SetVisible([id], !_document.Core.IsVisible(id));
    }

    private void ToggleLocked()
    {
        string[] ids = _document.Core.Selection.ToArray();
        foreach (string id in ids) _document.Core.SetLocked([id], !_document.Core.IsLocked(id));
    }

    private static GroupBox BuildGroup(string title, Control content)
    {
        var group = new GroupBox { Text = title, Dock = DockStyle.Fill, Padding = new Padding(6) };
        group.Controls.Add(content);
        return group;
    }

    private static void AddButton(Control parent, string text, Action action)
    {
        var button = new Button { Text = text, AutoSize = true, MinimumSize = new Size(72, 30) };
        button.Click += (_, _) => action();
        parent.Controls.Add(button);
    }

    private static string TypeName(CustomGuiElement element) => element switch
    {
        CustomGuiWindow => "窗口", CustomGuiPanel => "面板", CustomGuiImage => "图片", CustomGuiText => "文本",
        CustomGuiButton => "按钮", CustomGuiTextInput => "输入框", CustomGuiList => "列表", CustomGuiProgressBar => "进度条", _ => "物品格",
    };

    private sealed class Surface(CustomGuiCanvasDocument document) : Control
    {
        public event Action<string>? Selected;

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.Clear(Color.FromArgb(18, 23, 31));
            float scale = Math.Min(ClientSize.Width / (float)document.Runtime.Viewport.ReferenceWidth, ClientSize.Height / (float)document.Runtime.Viewport.ReferenceHeight);
            if (scale <= 0) return;
            float left = (ClientSize.Width - document.Runtime.Viewport.ReferenceWidth * scale) / 2;
            float top = (ClientSize.Height - document.Runtime.Viewport.ReferenceHeight * scale) / 2;
            e.Graphics.FillRectangle(Brushes.Black, left, top, document.Runtime.Viewport.ReferenceWidth * scale, document.Runtime.Viewport.ReferenceHeight * scale);
            foreach (string id in document.Core.ElementIds.Where(document.Core.IsVisible))
            {
                CanvasBounds bounds = document.Core.GetBounds(id);
                var rectangle = new RectangleF(left + bounds.X * scale, top + bounds.Y * scale, bounds.Width * scale, bounds.Height * scale);
                bool selected = document.Core.Selection.Contains(id);
                using var fill = new SolidBrush(selected ? Color.FromArgb(90, 25, 167, 206) : Color.FromArgb(55, 118, 134, 157));
                using var pen = new Pen(selected ? Color.DeepSkyBlue : Color.SlateGray, selected ? 2 : 1);
                e.Graphics.FillRectangle(fill, rectangle); e.Graphics.DrawRectangle(pen, rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height);
                e.Graphics.DrawString(id, Font, Brushes.White, rectangle.X + 4, rectangle.Y + 4);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            float scale = Math.Min(ClientSize.Width / (float)document.Runtime.Viewport.ReferenceWidth, ClientSize.Height / (float)document.Runtime.Viewport.ReferenceHeight);
            if (scale <= 0) return;
            float left = (ClientSize.Width - document.Runtime.Viewport.ReferenceWidth * scale) / 2;
            float top = (ClientSize.Height - document.Runtime.Viewport.ReferenceHeight * scale) / 2;
            int x = (int)((e.X - left) / scale), y = (int)((e.Y - top) / scale);
            string? id = document.Core.ElementIds.Reverse().FirstOrDefault(item => { CanvasBounds b = document.Core.GetBounds(item); return document.Core.IsVisible(item) && x >= b.X && y >= b.Y && x <= b.X + b.Width && y <= b.Y + b.Height; });
            if (id is not null) Selected?.Invoke(id);
            Focus();
        }

        protected override bool IsInputKey(Keys keyData) => keyData is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(keyData);
        protected override void OnKeyDown(KeyEventArgs e)
        {
            int step = e.Shift ? 10 : 1;
            (int x, int y) = e.KeyCode switch { Keys.Left => (-step, 0), Keys.Right => (step, 0), Keys.Up => (0, -step), Keys.Down => (0, step), _ => (0, 0) };
            if (x != 0 || y != 0) { document.Core.MoveSelection(x, y, snap: false); e.Handled = true; }
            base.OnKeyDown(e);
        }
    }

    private sealed class ElementPropertyView(CustomGuiCanvasDocument document, string id)
    {
        [Category("标识"), DisplayName("对象标识"), ReadOnly(true)] public string Id => id;
        [Category("布局"), DisplayName("X")] public int X { get => document.Core.GetBounds(id).X; set => document.Core.SetBounds(id, document.Core.GetBounds(id) with { X = value }); }
        [Category("布局"), DisplayName("Y")] public int Y { get => document.Core.GetBounds(id).Y; set => document.Core.SetBounds(id, document.Core.GetBounds(id) with { Y = value }); }
        [Category("布局"), DisplayName("宽度")] public int Width { get => document.Core.GetBounds(id).Width; set => document.Core.SetBounds(id, document.Core.GetBounds(id) with { Width = value }); }
        [Category("布局"), DisplayName("高度")] public int Height { get => document.Core.GetBounds(id).Height; set => document.Core.SetBounds(id, document.Core.GetBounds(id) with { Height = value }); }
        [Category("状态"), DisplayName("显示")] public bool Visible { get => document.Core.IsVisible(id); set => document.Core.SetVisible([id], value); }
        [Category("状态"), DisplayName("锁定")] public bool Locked { get => document.Core.IsLocked(id); set => document.Core.SetLocked([id], value); }
        [Category("内容"), DisplayName("显示文字")] public string Text { get => ReadText(document.Element(id)); set => document.ChangeElement(id, element => WriteText(element, value)); }

        private static string ReadText(CustomGuiElement element) => element switch { CustomGuiWindow x => x.Title, CustomGuiText x => x.Content, CustomGuiButton x => x.Text, CustomGuiTextInput x => x.Placeholder, CustomGuiProgressBar x => x.Text ?? string.Empty, CustomGuiItemSlot x => x.DisplayName ?? string.Empty, _ => string.Empty };
        private static void WriteText(CustomGuiElement element, string value)
        {
            switch (element) { case CustomGuiWindow x: x.Title = value; break; case CustomGuiText x: x.Content = value; break; case CustomGuiButton x: x.Text = value; break; case CustomGuiTextInput x: x.Placeholder = value; break; case CustomGuiProgressBar x: x.Text = value; break; case CustomGuiItemSlot x: x.DisplayName = value; break; }
        }
    }
}
