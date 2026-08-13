using System.ComponentModel;
using Launcher.ThemeRuntime;

namespace LyoCrystal.LauncherEditor;

internal sealed class LauncherCanvasEditorPanel : UserControl
{
    private readonly LauncherCanvasDocument _document;
    private readonly Func<Bitmap> _render;
    private readonly Action<ThemeImageUsage> _importImage;
    private readonly ListBox _tree = new() { Dock = DockStyle.Fill, SelectionMode = SelectionMode.MultiExtended };
    private readonly PropertyGrid _properties = new() { Dock = DockStyle.Fill, HelpVisible = true, ToolbarVisible = false };
    private readonly CanvasSurface _surface;
    private bool _synchronizing;

    internal LauncherCanvasEditorPanel(LauncherCanvasDocument document, Func<Bitmap> render, Action<ThemeImageUsage> importImage)
    {
        _document = document;
        _render = render;
        _importImage = importImage;
        _surface = new CanvasSurface(document) { Dock = DockStyle.Fill };
        Dock = DockStyle.Fill;
        BuildUi();
        _document.Changed += OnDocumentChanged;
        _tree.SelectedIndexChanged += (_, _) => SelectFromTree();
        RefreshFromDocument();
    }

    internal LauncherCanvasDocument Document => _document;

    private void OnDocumentChanged(object? sender, EventArgs e) => RefreshFromDocument();

    private void BuildUi()
    {
        var tools = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Padding = new Padding(4) };
        Add(tools, "撤销", () => _document.Undo());
        Add(tools, "重做", () => _document.Redo());
        Add(tools, "左对齐", () => _document.AlignSelection(LauncherCanvasAlignment.Left));
        Add(tools, "水平居中", () => _document.AlignSelection(LauncherCanvasAlignment.HorizontalCenter));
        Add(tools, "右对齐", () => _document.AlignSelection(LauncherCanvasAlignment.Right));
        Add(tools, "顶对齐", () => _document.AlignSelection(LauncherCanvasAlignment.Top));
        Add(tools, "垂直居中", () => _document.AlignSelection(LauncherCanvasAlignment.VerticalCenter));
        Add(tools, "底对齐", () => _document.AlignSelection(LauncherCanvasAlignment.Bottom));
        Add(tools, "水平等距", () => _document.DistributeSelection(LauncherCanvasDistribution.Horizontal));
        Add(tools, "垂直等距", () => _document.DistributeSelection(LauncherCanvasDistribution.Vertical));
        Add(tools, "上移一层", _document.BringSelectionForward);
        Add(tools, "下移一层", _document.SendSelectionBackward);
        Add(tools, "锁定", () => _document.SetLocked(_document.Selection, true));
        Add(tools, "解锁", () => _document.SetLocked(_document.Selection, false));
        Add(tools, "显示", () => _document.SetVisible(_document.Selection, true));
        Add(tools, "隐藏", () => _document.SetVisible(_document.Selection, false));
        Add(tools, "背景素材…", () => _importImage(ThemeImageUsage.Background));
        Add(tools, "按钮基础图…", () => _importImage(ThemeImageUsage.ButtonBase));
        Add(tools, "悬停图…", () => _importImage(ThemeImageUsage.ButtonHover));
        Add(tools, "按下图…", () => _importImage(ThemeImageUsage.ButtonPressed));
        Add(tools, "禁用图…", () => _importImage(ThemeImageUsage.ButtonDisabled));

        var left = new Panel { Dock = DockStyle.Fill, Padding = new Padding(4) };
        left.Controls.Add(_tree);
        left.Controls.Add(new Label { Text = "控件树（Ctrl/Shift 多选）", Dock = DockStyle.Top, Height = 28, TextAlign = ContentAlignment.MiddleLeft });
        var canvasHost = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.FromArgb(38, 38, 42), Padding = new Padding(16) };
        canvasHost.Controls.Add(_surface);
        var right = new Panel { Dock = DockStyle.Fill, Padding = new Padding(4) };
        right.Controls.Add(_properties);
        right.Controls.Add(new Label { Text = "所选控件属性", Dock = DockStyle.Top, Height = 28, TextAlign = ContentAlignment.MiddleLeft });
        var main = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3 };
        main.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210));
        main.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        main.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 280));
        main.Controls.Add(left, 0, 0); main.Controls.Add(canvasHost, 1, 0); main.Controls.Add(right, 2, 0);
        Controls.Add(main); Controls.Add(tools);
    }

    private static void Add(Control parent, string text, Action action)
    {
        var button = new Button { Text = text, AutoSize = true, Margin = new Padding(2) };
        button.Click += (_, _) => action(); parent.Controls.Add(button);
    }

    private void SelectFromTree()
    {
        if (_synchronizing) return;
        _document.Select(_tree.SelectedItems.Cast<ControlItem>().Select(item => item.Id));
    }

    private void RefreshFromDocument()
    {
        _synchronizing = true;
        try
        {
            LauncherControlId[] selected = _document.Selection.ToArray();
            _tree.BeginUpdate(); _tree.Items.Clear();
            foreach (LauncherControlOverride control in _document.Controls)
            {
                int index = _tree.Items.Add(new ControlItem(control.Id, control.Visible, _document.IsLocked(control.Id)));
                if (selected.Contains(control.Id)) _tree.SetSelected(index, true);
            }
            _tree.EndUpdate();
            _properties.SelectedObject = selected.Length == 1 ? new CanvasControlPropertyView(_document, selected[0]) : null;
            Image? previous = _surface.BackgroundImage;
            try { _surface.BackgroundImage = _render(); }
            catch { _surface.BackgroundImage = previous; previous = null; }
            previous?.Dispose();
            _surface.SetCanvasSize(_surface.ThemeSize);
            _surface.Invalidate();
        }
        finally { _synchronizing = false; }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _document.Changed -= OnDocumentChanged;
            _surface.BackgroundImage?.Dispose();
        }
        base.Dispose(disposing);
    }

    private sealed record ControlItem(LauncherControlId Id, bool Visible, bool Locked)
    {
        public override string ToString() => $"{(Visible ? "●" : "○")} {(Locked ? "🔒" : "  ")} {EditorChineseText.Control(Id)}";
    }

    private sealed class CanvasSurface : Control
    {
        private readonly LauncherCanvasDocument _document;
        private Point _mouseDown;
        private bool _resizing;
        internal CanvasSurface(LauncherCanvasDocument document)
        {
            _document = document;
            DoubleBuffered = true; TabStop = true; BackColor = Color.FromArgb(18, 20, 28);
            BackgroundImageLayout = ImageLayout.Stretch;
            Size = ThemeSize;
        }
        internal Size ThemeSize => BackgroundImage?.Size ?? new Size(Math.Max(640, _document.Controls.Max(x => x.X + x.Width)), Math.Max(420, _document.Controls.Max(x => x.Y + x.Height)));
        internal void SetCanvasSize(Size value) { Size = value; MinimumSize = value; }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using var selectedPen = new Pen(Color.DeepSkyBlue, 2);
            using var normalPen = new Pen(Color.FromArgb(150, Color.White), 1) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
            using var guidePen = new Pen(Color.LimeGreen, 1) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
            foreach (LauncherCanvasGuide guide in _document.SnapGuides)
                if (guide.Vertical) e.Graphics.DrawLine(guidePen, guide.Position, 0, guide.Position, Height);
                else e.Graphics.DrawLine(guidePen, 0, guide.Position, Width, guide.Position);
            foreach (LauncherControlOverride item in _document.Controls)
            {
                Rectangle bounds = _document.GetBounds(item.Id);
                bool selected = _document.Selection.Contains(item.Id);
                e.Graphics.DrawRectangle(selected ? selectedPen : normalPen, bounds);
                using var brush = new SolidBrush(Color.FromArgb(190, selected ? Color.DeepSkyBlue : Color.Black));
                string label = EditorChineseText.Control(item.Id) + (_document.IsLocked(item.Id) ? " [锁]" : string.Empty) + (!item.Visible ? " [隐藏]" : string.Empty);
                e.Graphics.DrawString(label, Font, brush, bounds.X + 3, bounds.Y + 3);
                if (selected && !_document.IsLocked(item.Id)) e.Graphics.FillRectangle(Brushes.White, bounds.Right - 7, bounds.Bottom - 7, 7, 7);
            }
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e); Focus();
            LauncherControlOverride? hit = _document.Controls.Reverse().FirstOrDefault(item => item.Visible && _document.GetBounds(item.Id).Contains(e.Location));
            if (hit is null) { _document.Select([]); return; }
            bool additive = (ModifierKeys & Keys.Control) != 0;
            if (!_document.Selection.Contains(hit.Id) || !additive) _document.Select([hit.Id], additive);
            Rectangle bounds = _document.GetBounds(hit.Id);
            _resizing = e.X >= bounds.Right - 12 && e.Y >= bounds.Bottom - 12;
            _mouseDown = e.Location; Capture = true;
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!Capture) return;
            int dx = e.X - _mouseDown.X, dy = e.Y - _mouseDown.Y;
            if (_resizing) _document.ResizeSelection(dx, dy, snap: true); else _document.MoveSelection(dx, dy, snap: true);
            Capture = false;
        }
        protected override bool IsInputKey(Keys keyData) => keyData is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(keyData);
        protected override void OnKeyDown(KeyEventArgs e)
        {
            int step = e.Shift ? 10 : 1;
            if (e.Control && e.KeyCode == Keys.Z) { _document.Undo(); e.Handled = true; return; }
            if (e.Control && e.KeyCode == Keys.Y) { _document.Redo(); e.Handled = true; return; }
            if (e.KeyCode == Keys.Delete) { _document.DeleteSelection(); e.Handled = true; return; }
            Point delta = e.KeyCode switch { Keys.Left => new Point(-step, 0), Keys.Right => new Point(step, 0), Keys.Up => new Point(0, -step), Keys.Down => new Point(0, step), _ => Point.Empty };
            if (delta != Point.Empty) { _document.MoveSelection(delta.X, delta.Y, snap: false); e.Handled = true; }
        }
    }

    private sealed class CanvasControlPropertyView
    {
        private readonly LauncherCanvasDocument _document;
        private readonly LauncherControlId _id;
        internal CanvasControlPropertyView(LauncherCanvasDocument document, LauncherControlId id) { _document = document; _id = id; }
        private LauncherControlOverride Value => _document.Controls.Single(x => x.Id == _id);
        [Category("标识"), DisplayName("控件"), ReadOnly(true)] public string Name => EditorChineseText.Control(_id);
        [Category("布局"), DisplayName("横向位置")] public int X { get => Value.X; set { Rectangle b = _document.GetBounds(_id); b.X = value; _document.SetBounds(_id, b); } }
        [Category("布局"), DisplayName("纵向位置")] public int Y { get => Value.Y; set { Rectangle b = _document.GetBounds(_id); b.Y = value; _document.SetBounds(_id, b); } }
        [Category("布局"), DisplayName("宽度")] public int Width { get => Value.Width; set { Rectangle b = _document.GetBounds(_id); b.Width = value; _document.SetBounds(_id, b); } }
        [Category("布局"), DisplayName("高度")] public int Height { get => Value.Height; set { Rectangle b = _document.GetBounds(_id); b.Height = value; _document.SetBounds(_id, b); } }
        [Category("状态"), DisplayName("显示")] public bool Visible { get => Value.Visible; set => _document.SetVisible([_id], value); }
        [Category("状态"), DisplayName("锁定")] public bool Locked { get => _document.IsLocked(_id); set => _document.SetLocked([_id], value); }
        [Category("外观"), DisplayName("文字颜色")] public string ForeColor { get => Value.ForeColor; set => _document.ChangeSelectionStyle(new(ForeColor: value)); }
        [Category("外观"), DisplayName("背景颜色")] public string BackColor { get => Value.BackColor; set => _document.ChangeSelectionStyle(new(BackColor: value)); }
        [Category("外观"), DisplayName("字体")] public string FontName { get => Value.FontName; set => _document.ChangeSelectionStyle(new(FontName: value)); }
        [Category("外观"), DisplayName("字号")] public float FontSize { get => Value.FontSize; set => _document.ChangeSelectionStyle(new(FontSize: value)); }
        [Category("外观"), DisplayName("粗体")] public bool Bold { get => Value.Bold; set => _document.ChangeSelectionStyle(new(Bold: value)); }
        [Category("外观"), DisplayName("不透明度")] public int OpacityPercent { get => Value.OpacityPercent; set => _document.ChangeSelectionStyle(new(OpacityPercent: value)); }
        [Category("资源"), DisplayName("背景图片")] public string BackgroundImage { get => Value.BackgroundImage; set => _document.ChangeSelectionStyle(new(BackgroundImage: value)); }
    }
}
