using System.ComponentModel;
using System.Drawing.Drawing2D;
using Launcher.ThemeRuntime;

namespace LyoCrystal.LauncherEditor;

internal sealed record LauncherWorkspaceStatus(string Selection, string Viewport, bool Dirty);

internal sealed class LauncherCanvasEditorPanel : UserControl
{
    private readonly LauncherCanvasDocument _document;
    private readonly Func<Bitmap> _render;
    private readonly Action<ThemeImageUsage> _importImage;
    private readonly ListBox _tree = new() { Dock = DockStyle.Fill, SelectionMode = SelectionMode.MultiExtended, BorderStyle = BorderStyle.None, IntegralHeight = false };
    private readonly TextBox _search = new() { Dock = DockStyle.Top, PlaceholderText = "搜索对象", BorderStyle = BorderStyle.FixedSingle };
    private readonly PropertyGrid _properties = new() { Dock = DockStyle.Fill, HelpVisible = true, ToolbarVisible = false, PropertySort = PropertySort.Categorized };
    private readonly CanvasSurface _surface;
    private readonly Panel _canvasHost = new() { Dock = DockStyle.Fill, AutoScroll = true, BackColor = DesktopAuthoringTheme.CanvasViewport };
    private readonly ToolStripLabel _zoomLabel = new("100%") { Alignment = ToolStripItemAlignment.Right };
    private bool _synchronizing;
    private bool _snapEnabled = true;
    private bool _autoFit = true;

    internal LauncherCanvasEditorPanel(LauncherCanvasDocument document, Func<Bitmap> render, Action<ThemeImageUsage> importImage)
    {
        _document = document;
        _render = render;
        _importImage = importImage;
        _surface = new CanvasSurface(document) { SnapEnabled = _snapEnabled };
        Dock = DockStyle.Fill;
        BuildUi();
        DesktopAuthoringTheme.Apply(this);
        _document.Changed += OnDocumentChanged;
        _tree.SelectedIndexChanged += (_, _) => SelectFromTree();
        _search.TextChanged += (_, _) => RefreshObjectTree();
        _canvasHost.Resize += (_, _) => { if (_autoFit) FitCanvas(); else CenterCanvas(); };
        _surface.ZoomRequested += (_, delta) => SetZoom(_surface.Zoom + delta, center: true);
        RefreshFromDocument();
    }

    internal event EventHandler<LauncherWorkspaceStatus>? WorkspaceStatusChanged;
    internal LauncherCanvasDocument Document => _document;

    internal (int ObjectTreeWidth, int PropertiesWidth, Size CanvasSize) CaptureLayoutForEvidence()
    {
        var main = Controls.OfType<TableLayoutPanel>().Single();
        return ((int)main.ColumnStyles[0].Width, (int)main.ColumnStyles[2].Width, _surface.ThemeSize);
    }

    internal (float Zoom, bool Snap, bool Grid) CaptureViewportForEvidence() => (_surface.Zoom, _surface.SnapEnabled, _surface.GridVisible);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        BeginInvoke(FitCanvas);
    }

    private void OnDocumentChanged(object? sender, EventArgs e) => RefreshFromDocument();

    private void BuildUi()
    {
        var tools = new ToolStrip { Dock = DockStyle.Top, GripStyle = ToolStripGripStyle.Hidden, CanOverflow = true, AutoSize = false, Height = DesktopAuthoringTheme.ContextBarHeight, Padding = new Padding(4, 2, 4, 2) };
        Add(tools, "撤销", () => _document.Undo(), "撤销上一步画布修改");
        Add(tools, "重做", () => _document.Redo(), "恢复刚撤销的画布修改");
        tools.Items.Add(new ToolStripSeparator());
        AddMenu(tools, "对齐", ("左对齐", () => _document.AlignSelection(LauncherCanvasAlignment.Left)), ("水平居中", () => _document.AlignSelection(LauncherCanvasAlignment.HorizontalCenter)), ("右对齐", () => _document.AlignSelection(LauncherCanvasAlignment.Right)), ("顶对齐", () => _document.AlignSelection(LauncherCanvasAlignment.Top)), ("垂直居中", () => _document.AlignSelection(LauncherCanvasAlignment.VerticalCenter)), ("底对齐", () => _document.AlignSelection(LauncherCanvasAlignment.Bottom)));
        AddMenu(tools, "分布", ("水平等距", () => _document.DistributeSelection(LauncherCanvasDistribution.Horizontal)), ("垂直等距", () => _document.DistributeSelection(LauncherCanvasDistribution.Vertical)));
        AddMenu(tools, "层级", ("上移一层", _document.BringSelectionForward), ("下移一层", _document.SendSelectionBackward));
        AddMenu(tools, "对象状态", ("锁定", () => _document.SetLocked(_document.Selection, true)), ("解锁", () => _document.SetLocked(_document.Selection, false)), ("显示", () => _document.SetVisible(_document.Selection, true)), ("隐藏", () => _document.SetVisible(_document.Selection, false)));
        AddMenu(tools, "素材", ("背景素材…", () => _importImage(ThemeImageUsage.Background)), ("按钮基础图…", () => _importImage(ThemeImageUsage.ButtonBase)), ("悬停图…", () => _importImage(ThemeImageUsage.ButtonHover)), ("按下图…", () => _importImage(ThemeImageUsage.ButtonPressed)), ("禁用图…", () => _importImage(ThemeImageUsage.ButtonDisabled)));
        tools.Items.Add(new ToolStripSeparator());
        var grid = new ToolStripButton("网格") { CheckOnClick = true, Checked = false, ToolTipText = "显示或隐藏 10 像素网格" };
        grid.CheckedChanged += (_, _) => { _surface.GridVisible = grid.Checked; _surface.Invalidate(); PublishStatus(); };
        var snap = new ToolStripButton("吸附") { CheckOnClick = true, Checked = true, ToolTipText = "拖动时吸附画布和其他对象边缘" };
        snap.CheckedChanged += (_, _) => { _snapEnabled = snap.Checked; _surface.SnapEnabled = snap.Checked; PublishStatus(); };
        tools.Items.Add(grid); tools.Items.Add(snap);
        tools.Items.Add(new ToolStripSeparator());
        Add(tools, "适合窗口", FitCanvas, "缩放画布以完整显示");
        Add(tools, "100%", () => SetZoom(1F, center: true), "按实际像素显示画布");
        Add(tools, "−", () => SetZoom(_surface.Zoom - .1F, center: true), "缩小画布");
        Add(tools, "+", () => SetZoom(_surface.Zoom + .1F, center: true), "放大画布");
        tools.Items.Add(_zoomLabel);

        var left = CreateToolWindow("对象", "搜索并选择画布对象；Ctrl/Shift 可多选。", _tree, _search);
        _canvasHost.Controls.Add(_surface);
        var right = CreateToolWindow("属性", "选择对象后可编辑布局、状态与外观。", _properties);
        var main = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, BackColor = DesktopAuthoringTheme.Border, CellBorderStyle = TableLayoutPanelCellBorderStyle.Single };
        main.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, DesktopAuthoringTheme.ObjectTreeWidth));
        main.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        main.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, DesktopAuthoringTheme.PropertiesWidth));
        main.Controls.Add(left, 0, 0); main.Controls.Add(_canvasHost, 1, 0); main.Controls.Add(right, 2, 0);
        var propertiesToggle = new ToolStripButton("隐藏属性") { Alignment = ToolStripItemAlignment.Right, ToolTipText = "收起或恢复属性检查器" };
        propertiesToggle.Click += (_, _) =>
        {
            bool show = main.ColumnStyles[2].Width == 0;
            main.ColumnStyles[2].Width = show ? DesktopAuthoringTheme.PropertiesWidth : 0;
            right.Visible = show;
            propertiesToggle.Text = show ? "隐藏属性" : "显示属性";
            BeginInvoke(CenterCanvas);
        };
        tools.Items.Add(propertiesToggle);
        Controls.Add(main); Controls.Add(tools);
    }

    private static Panel CreateToolWindow(string title, string description, Control content, Control? headerControl = null)
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = DesktopAuthoringTheme.PanelBackground, Padding = new Padding(8, 0, 8, 8) };
        var titleLabel = new Label { Text = title, Dock = DockStyle.Top, Height = 32, Font = DesktopAuthoringTheme.CreateBodyFont(9F, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft };
        var hint = new Label { Text = description, Dock = DockStyle.Top, Height = 34, ForeColor = DesktopAuthoringTheme.TextSecondary, AutoEllipsis = true };
        panel.Controls.Add(content);
        panel.Controls.Add(hint);
        if (headerControl is not null) { headerControl.Height = 28; panel.Controls.Add(headerControl); }
        panel.Controls.Add(titleLabel);
        return panel;
    }

    private static void Add(ToolStrip parent, string text, Action action, string toolTip)
    {
        var button = new ToolStripButton(text) { ToolTipText = toolTip };
        button.Click += (_, _) => action(); parent.Items.Add(button);
    }

    private static void AddMenu(ToolStrip parent, string text, params (string Text, Action Action)[] commands)
    {
        var menu = new ToolStripDropDownButton(text);
        foreach ((string commandText, Action action) in commands)
        {
            var item = new ToolStripMenuItem(commandText);
            item.Click += (_, _) => action();
            menu.DropDownItems.Add(item);
        }
        parent.Items.Add(menu);
    }

    private void SelectFromTree()
    {
        if (_synchronizing) return;
        _document.Select(_tree.SelectedItems.Cast<ControlItem>().Select(item => item.Id));
    }

    private void RefreshObjectTree()
    {
        LauncherControlId[] selected = _document.Selection.ToArray();
        string query = _search.Text.Trim();
        _tree.BeginUpdate(); _tree.Items.Clear();
        foreach (LauncherControlOverride control in _document.Controls)
        {
            var item = new ControlItem(control.Id, control.Visible, _document.IsLocked(control.Id));
            if (query.Length > 0 && !item.ToString().Contains(query, StringComparison.CurrentCultureIgnoreCase)) continue;
            int index = _tree.Items.Add(item);
            if (selected.Contains(control.Id)) _tree.SetSelected(index, true);
        }
        _tree.EndUpdate();
    }

    private void RefreshFromDocument()
    {
        _synchronizing = true;
        try
        {
            LauncherControlId[] selected = _document.Selection.ToArray();
            RefreshObjectTree();
            _properties.SelectedObjects = selected.Select(id => (object)new CanvasControlPropertyView(_document, id)).ToArray();
            Bitmap? previous = _surface.CanvasImage;
            try { _surface.SetCanvasImage(_render()); }
            catch { if (previous is not null) _surface.SetCanvasImage(previous); previous = null; }
            previous?.Dispose();
            CenterCanvas();
            _surface.Invalidate();
            PublishStatus();
        }
        finally { _synchronizing = false; }
    }

    private void FitCanvas()
    {
        Size canvas = _surface.ThemeSize;
        float horizontal = Math.Max(1, _canvasHost.ClientSize.Width - 40) / (float)canvas.Width;
        float vertical = Math.Max(1, _canvasHost.ClientSize.Height - 40) / (float)canvas.Height;
        SetZoom(Math.Min(1F, Math.Min(horizontal, vertical)), center: true, automatic: true);
    }

    private void SetZoom(float zoom, bool center, bool automatic = false)
    {
        _autoFit = automatic;
        _canvasHost.AutoScroll = !automatic;
        if (automatic) _canvasHost.AutoScrollPosition = Point.Empty;
        _surface.Zoom = Math.Clamp((float)Math.Round(zoom, 2), .25F, 4F);
        _zoomLabel.Text = $"{_surface.Zoom:P0}";
        if (center) CenterCanvas();
        PublishStatus();
    }

    private void CenterCanvas()
    {
        int x = Math.Max(20, (_canvasHost.ClientSize.Width - _surface.Width) / 2);
        int y = Math.Max(20, (_canvasHost.ClientSize.Height - _surface.Height) / 2);
        _surface.Location = new Point(x, y);
    }

    private void PublishStatus()
    {
        string selection = _document.Selection.Count == 0 ? "未选择对象" : $"已选择 {_document.Selection.Count} 个对象";
        string viewport = $"画布 {_surface.Zoom:P0} · {(_surface.SnapEnabled ? "吸附开" : "吸附关")} · {(_surface.GridVisible ? "网格开" : "网格关")}";
        WorkspaceStatusChanged?.Invoke(this, new LauncherWorkspaceStatus(selection, viewport, _document.IsDirty));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _document.Changed -= OnDocumentChanged;
            _surface.CanvasImage?.Dispose();
        }
        base.Dispose(disposing);
    }

    private sealed record ControlItem(LauncherControlId Id, bool Visible, bool Locked)
    {
        public override string ToString() => $"{(Visible ? "●" : "○")}  {(Locked ? "已锁定" : "可编辑")}  {EditorChineseText.Control(Id)}";
    }

    private sealed class CanvasSurface : Control
    {
        private readonly LauncherCanvasDocument _document;
        private Point _mouseDown;
        private bool _resizing;
        private Bitmap? _canvasImage;
        private float _zoom = 1F;

        internal CanvasSurface(LauncherCanvasDocument document)
        {
            _document = document;
            DoubleBuffered = true; TabStop = true; BackColor = Color.FromArgb(18, 20, 28);
            Size = ThemeSize;
        }

        internal event EventHandler<float>? ZoomRequested;
        internal Bitmap? CanvasImage => _canvasImage;
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        internal bool GridVisible { get; set; }
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        internal bool SnapEnabled { get; set; } = true;
        internal Size ThemeSize => _canvasImage?.Size ?? new Size(Math.Max(640, _document.Controls.Max(x => x.X + x.Width)), Math.Max(420, _document.Controls.Max(x => x.Y + x.Height)));
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        internal float Zoom
        {
            get => _zoom;
            set { _zoom = value; Size = new Size((int)Math.Ceiling(ThemeSize.Width * value), (int)Math.Ceiling(ThemeSize.Height * value)); Invalidate(); }
        }

        internal void SetCanvasImage(Bitmap value)
        {
            _canvasImage = value;
            Size = new Size((int)Math.Ceiling(ThemeSize.Width * Zoom), (int)Math.Ceiling(ThemeSize.Height * Zoom));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.InterpolationMode = Zoom >= 1F ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
            e.Graphics.ScaleTransform(Zoom, Zoom);
            if (_canvasImage is not null) e.Graphics.DrawImage(_canvasImage, Point.Empty);
            if (GridVisible)
            {
                using var gridPen = new Pen(Color.FromArgb(42, Color.White), 1F / Zoom);
                for (int x = 10; x < ThemeSize.Width; x += 10) e.Graphics.DrawLine(gridPen, x, 0, x, ThemeSize.Height);
                for (int y = 10; y < ThemeSize.Height; y += 10) e.Graphics.DrawLine(gridPen, 0, y, ThemeSize.Width, y);
            }
            using var selectedPen = new Pen(DesktopAuthoringTheme.Accent, 2F / Zoom);
            using var guidePen = new Pen(DesktopAuthoringTheme.Guide, 1F / Zoom) { DashStyle = DashStyle.Dash };
            foreach (LauncherCanvasGuide guide in _document.SnapGuides)
                if (guide.Vertical) e.Graphics.DrawLine(guidePen, guide.Position, 0, guide.Position, ThemeSize.Height);
                else e.Graphics.DrawLine(guidePen, 0, guide.Position, ThemeSize.Width, guide.Position);
            foreach (LauncherControlOverride item in _document.Controls.Where(item => _document.Selection.Contains(item.Id)))
            {
                Rectangle bounds = _document.GetBounds(item.Id);
                e.Graphics.DrawRectangle(selectedPen, bounds);
                if (!_document.IsLocked(item.Id)) e.Graphics.FillRectangle(Brushes.White, bounds.Right - 7, bounds.Bottom - 7, 7, 7);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e); Focus();
            Point logical = ToLogical(e.Location);
            LauncherControlOverride? hit = _document.Controls.Reverse().FirstOrDefault(item => item.Visible && _document.GetBounds(item.Id).Contains(logical));
            if (hit is null) { _document.Select([]); return; }
            bool additive = (ModifierKeys & Keys.Control) != 0;
            if (!_document.Selection.Contains(hit.Id) || !additive) _document.Select([hit.Id], additive);
            Rectangle bounds = _document.GetBounds(hit.Id);
            _resizing = logical.X >= bounds.Right - 12 && logical.Y >= bounds.Bottom - 12;
            _mouseDown = logical; Capture = true;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!Capture) return;
            Point logical = ToLogical(e.Location);
            int dx = logical.X - _mouseDown.X, dy = logical.Y - _mouseDown.Y;
            if (_resizing) _document.ResizeSelection(dx, dy, SnapEnabled); else _document.MoveSelection(dx, dy, SnapEnabled);
            Capture = false;
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if ((ModifierKeys & Keys.Control) == 0) { base.OnMouseWheel(e); return; }
            ZoomRequested?.Invoke(this, e.Delta > 0 ? .1F : -.1F);
        }

        private Point ToLogical(Point point) => new((int)Math.Round(point.X / Zoom), (int)Math.Round(point.Y / Zoom));
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
        [Category("标识"), DisplayName("对象"), ReadOnly(true)] public string Name => EditorChineseText.Control(_id);
        [Category("布局（像素）"), DisplayName("横向位置")] public int X { get => Value.X; set { Rectangle b = _document.GetBounds(_id); b.X = value; _document.SetBounds(_id, b); } }
        [Category("布局（像素）"), DisplayName("纵向位置")] public int Y { get => Value.Y; set { Rectangle b = _document.GetBounds(_id); b.Y = value; _document.SetBounds(_id, b); } }
        [Category("布局（像素）"), DisplayName("宽度")] public int Width { get => Value.Width; set { Rectangle b = _document.GetBounds(_id); b.Width = value; _document.SetBounds(_id, b); } }
        [Category("布局（像素）"), DisplayName("高度")] public int Height { get => Value.Height; set { Rectangle b = _document.GetBounds(_id); b.Height = value; _document.SetBounds(_id, b); } }
        [Category("状态"), DisplayName("显示"), TypeConverter(typeof(ChineseBooleanConverter))] public bool Visible { get => Value.Visible; set => _document.SetVisible([_id], value); }
        [Category("状态"), DisplayName("锁定"), TypeConverter(typeof(ChineseBooleanConverter))] public bool Locked { get => _document.IsLocked(_id); set => _document.SetLocked([_id], value); }
        [Category("外观"), DisplayName("文字颜色")] public string ForeColor { get => Value.ForeColor; set => _document.ChangeSelectionStyle(new(ForeColor: value)); }
        [Category("外观"), DisplayName("背景颜色")] public string BackColor { get => Value.BackColor; set => _document.ChangeSelectionStyle(new(BackColor: value)); }
        [Category("外观"), DisplayName("字体")] public string FontName { get => Value.FontName; set => _document.ChangeSelectionStyle(new(FontName: value)); }
        [Category("外观"), DisplayName("字号")] public float FontSize { get => Value.FontSize; set => _document.ChangeSelectionStyle(new(FontSize: value)); }
        [Category("外观"), DisplayName("粗体"), TypeConverter(typeof(ChineseBooleanConverter))] public bool Bold { get => Value.Bold; set => _document.ChangeSelectionStyle(new(Bold: value)); }
        [Category("外观"), DisplayName("不透明度（%）")] public int OpacityPercent { get => Value.OpacityPercent; set => _document.ChangeSelectionStyle(new(OpacityPercent: value)); }
        [Category("资源"), DisplayName("背景图片")] public string BackgroundImage { get => Value.BackgroundImage; set => _document.ChangeSelectionStyle(new(BackgroundImage: value)); }
    }
}
