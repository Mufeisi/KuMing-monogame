using System.Drawing.Drawing2D;
using System.ComponentModel;
using Launcher.ThemeRuntime;

namespace LyoCrystal.LauncherEditor;

internal sealed record LauncherObjectTreeSnapshot(int GroupCount, int ObjectCount, int SelectedCount, string[] VisibleObjects);
internal sealed record LauncherObjectNode(LauncherControlId Id, string DisplayName);

internal sealed class LauncherObjectTreeAdapter : UserControl
{
    private const int StateTargetWidth = 24;
    private readonly LauncherCanvasDocument _document;
    private readonly TreeView _tree = new()
    {
        Dock = DockStyle.Fill,
        BorderStyle = BorderStyle.None,
        DrawMode = TreeViewDrawMode.OwnerDrawAll,
        FullRowSelect = true,
        HideSelection = false,
        ItemHeight = 26,
        ShowLines = false,
        ShowPlusMinus = true,
        ShowRootLines = false,
        Scrollable = false,
        AccessibleName = "启动器对象树",
    };
    private readonly ToolTip _toolTip = new();
    private readonly HashSet<LauncherControlId> _selected = new();
    private LauncherControlId? _anchor;
    private string _filter = string.Empty;
    private bool _synchronizing;

    internal LauncherObjectTreeAdapter(LauncherCanvasDocument document)
    {
        _document = document;
        Dock = DockStyle.Fill;
        BackColor = DesktopAuthoringTheme.InputBackground;
        Controls.Add(_tree);
        BuildContextMenu();
        _tree.DrawNode += DrawNode;
        _tree.NodeMouseClick += OnNodeMouseClick;
        _tree.KeyDown += OnTreeKeyDown;
        _tree.MouseMove += OnTreeMouseMove;
        _tree.MouseLeave += (_, _) => _toolTip.SetToolTip(_tree, null);
        RefreshFromDocument();
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal string Filter
    {
        get => _filter;
        set
        {
            string next = value?.Trim() ?? string.Empty;
            if (string.Equals(_filter, next, StringComparison.CurrentCultureIgnoreCase)) return;
            _filter = next;
            RefreshFromDocument();
        }
    }

    internal void RefreshFromDocument()
    {
        _synchronizing = true;
        try
        {
            _selected.Clear();
            _selected.UnionWith(_document.Selection);
            LauncherControlId? focused = NodeId(_tree.SelectedNode);
            _tree.BeginUpdate();
            _tree.Nodes.Clear();
            var root = new TreeNode("启动器界面") { Name = "root", NodeFont = DesktopAuthoringTheme.CreateBodyFont(9F, FontStyle.Bold) };
            AddGroup(root, "内容区域", [LauncherControlId.ServerList, LauncherControlId.Announcements]);
            AddGroup(root, "玩家操作", [LauncherControlId.ChooseClientButton, LauncherControlId.DiagnoseButton, LauncherControlId.SettingsButton, LauncherControlId.LaunchButton]);
            AddGroup(root, "更新进度", [LauncherControlId.OverallProgress, LauncherControlId.CurrentProgress, LauncherControlId.ProgressText]);
            _tree.Nodes.Add(root);
            root.Expand();
            foreach (TreeNode group in root.Nodes) group.Expand();
            _tree.SelectedNode = FindNode(focused) ?? VisibleObjectNodes().FirstOrDefault(node => _selected.Contains(NodeId(node)!.Value));
            _tree.EndUpdate();
            _tree.Invalidate();
        }
        finally { _synchronizing = false; }
    }

    internal LauncherObjectTreeSnapshot CaptureSnapshot()
    {
        TreeNode? root = _tree.Nodes.Count == 0 ? null : _tree.Nodes[0];
        string[] objects = VisibleObjectNodes().Select(node => EditorChineseText.Control(NodeId(node)!.Value)).ToArray();
        return new LauncherObjectTreeSnapshot(root?.Nodes.Count ?? 0, objects.Length, _selected.Count, objects);
    }

    internal void ToggleVisibilityForEvidence(LauncherControlId id) => ToggleVisibility(id);
    internal void SelectForEvidence(LauncherControlId id, Keys modifiers) => ApplySelection(id, modifiers);

    private void AddGroup(TreeNode root, string name, LauncherControlId[] ids)
    {
        var group = new TreeNode(name) { Name = name };
        foreach (LauncherControlId id in ids)
        {
            string text = EditorChineseText.Control(id);
            if (_filter.Length > 0 && !text.Contains(_filter, StringComparison.CurrentCultureIgnoreCase)) continue;
            LauncherControlOverride control = _document.Controls.Single(item => item.Id == id);
            string accessibleState = $"{text} {(control.Visible ? "显示" : "隐藏")} {(_document.IsLocked(id) ? "锁定" : "未锁")}";
            group.Nodes.Add(new TreeNode(accessibleState) { Tag = new LauncherObjectNode(id, text), Name = id.ToString(), ToolTipText = accessibleState });
        }
        if (group.Nodes.Count > 0) root.Nodes.Add(group);
    }

    private void BuildContextMenu()
    {
        var menu = new ContextMenuStrip();
        AddContext(menu, "显示", () => _document.SetVisible(CurrentTargets(), true));
        AddContext(menu, "隐藏", () => _document.SetVisible(CurrentTargets(), false));
        menu.Items.Add(new ToolStripSeparator());
        AddContext(menu, "锁定", () => _document.SetLocked(CurrentTargets(), true));
        AddContext(menu, "解锁", () => _document.SetLocked(CurrentTargets(), false));
        _tree.ContextMenuStrip = menu;
    }

    private static void AddContext(ContextMenuStrip menu, string text, Action action)
    {
        var item = new ToolStripMenuItem(text);
        item.Click += (_, _) => action();
        menu.Items.Add(item);
    }

    private LauncherControlId[] CurrentTargets()
    {
        LauncherControlId? focused = NodeId(_tree.SelectedNode);
        return _selected.Count > 0 ? _selected.ToArray() : focused.HasValue ? [focused.Value] : [];
    }

    private void OnNodeMouseClick(object? sender, TreeNodeMouseClickEventArgs e)
    {
        _tree.SelectedNode = e.Node;
        LauncherControlId? id = NodeId(e.Node);
        if (!id.HasValue) return;
        if (e.Button == MouseButtons.Right)
        {
            if (!_selected.Contains(id.Value)) ApplySelection(id.Value, Keys.None);
            return;
        }
        if (e.Button != MouseButtons.Left) return;
        if (e.X >= _tree.ClientSize.Width - StateTargetWidth) ToggleLocked(id.Value);
        else if (e.X >= _tree.ClientSize.Width - StateTargetWidth * 2) ToggleVisibility(id.Value);
        else ApplySelection(id.Value, ModifierKeys);
    }

    private void ApplySelection(LauncherControlId id, Keys modifiers)
    {
        if ((modifiers & Keys.Shift) != 0 && _anchor.HasValue)
        {
            LauncherControlId[] visible = VisibleObjectNodes().Select(node => NodeId(node)!.Value).ToArray();
            int start = Array.IndexOf(visible, _anchor.Value), end = Array.IndexOf(visible, id);
            if (start >= 0 && end >= 0)
            {
                if ((modifiers & Keys.Control) == 0) _selected.Clear();
                foreach (LauncherControlId value in visible.Skip(Math.Min(start, end)).Take(Math.Abs(end - start) + 1)) _selected.Add(value);
            }
        }
        else if ((modifiers & Keys.Control) != 0)
        {
            if (!_selected.Add(id)) _selected.Remove(id);
            _anchor = id;
        }
        else
        {
            _selected.Clear();
            _selected.Add(id);
            _anchor = id;
        }
        PushSelection();
    }

    private void PushSelection()
    {
        if (_synchronizing) return;
        _document.Select(_selected);
        _tree.Invalidate();
    }

    private void ToggleVisibility(LauncherControlId id)
    {
        LauncherControlOverride control = _document.Controls.Single(item => item.Id == id);
        _document.SetVisible([id], !control.Visible);
    }

    private void ToggleLocked(LauncherControlId id) => _document.SetLocked([id], !_document.IsLocked(id));

    private void OnTreeKeyDown(object? sender, KeyEventArgs e)
    {
        LauncherControlId? focused = NodeId(_tree.SelectedNode);
        if (e.KeyCode == Keys.Space && focused.HasValue)
        {
            ApplySelection(focused.Value, e.Control ? Keys.Control : Keys.None);
            e.Handled = true;
            return;
        }
        if (e.KeyCode is not (Keys.Up or Keys.Down)) return;
        TreeNode[] nodes = VisibleObjectNodes().ToArray();
        int index = focused.HasValue ? Array.FindIndex(nodes, node => NodeId(node) == focused) : -1;
        int next = e.KeyCode == Keys.Up ? Math.Max(0, index - 1) : Math.Min(nodes.Length - 1, index + 1);
        if (next < 0 || next >= nodes.Length) return;
        _tree.SelectedNode = nodes[next];
        ApplySelection(NodeId(nodes[next])!.Value, e.Shift ? Keys.Shift : e.Control ? Keys.Control : Keys.None);
        e.Handled = true;
    }

    private void OnTreeMouseMove(object? sender, MouseEventArgs e)
    {
        TreeNode? node = _tree.GetNodeAt(e.Location);
        if (!NodeId(node).HasValue) { _toolTip.SetToolTip(_tree, null); return; }
        string? tip = e.X >= _tree.ClientSize.Width - StateTargetWidth
            ? "切换锁定状态"
            : e.X >= _tree.ClientSize.Width - StateTargetWidth * 2 ? "切换显示状态" : null;
        _toolTip.SetToolTip(_tree, tip);
        _tree.Cursor = tip is null ? Cursors.Default : Cursors.Hand;
    }

    private void DrawNode(object? sender, DrawTreeNodeEventArgs e)
    {
        if (e.Node is null) return;
        Rectangle row = new(0, e.Bounds.Y, _tree.ClientSize.Width, e.Bounds.Height);
        LauncherControlId? id = NodeId(e.Node);
        bool selected = id.HasValue && _selected.Contains(id.Value);
        Color background = selected ? DesktopAuthoringTheme.AccentSoft : DesktopAuthoringTheme.InputBackground;
        using var backgroundBrush = new SolidBrush(background);
        e.Graphics.FillRectangle(backgroundBrush, row);
        if (selected)
        {
            using var accentBrush = new SolidBrush(DesktopAuthoringTheme.Accent);
            e.Graphics.FillRectangle(accentBrush, 0, row.Top, 3, row.Height);
        }
        string displayText = e.Node.Tag is LauncherObjectNode item ? item.DisplayName : e.Node.Text;
        Rectangle textBounds = e.Bounds;
        textBounds.Width = Math.Max(0, _tree.ClientSize.Width - StateTargetWidth * 2 - textBounds.X - 2);
        TextRenderer.DrawText(e.Graphics, displayText, e.Node.NodeFont ?? _tree.Font, textBounds, DesktopAuthoringTheme.TextPrimary, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
        if (id.HasValue)
        {
            DrawVisibility(e.Graphics, new Rectangle(_tree.ClientSize.Width - StateTargetWidth * 2, row.Top, StateTargetWidth, row.Height), _document.Controls.Single(item => item.Id == id.Value).Visible);
            DrawLock(e.Graphics, new Rectangle(_tree.ClientSize.Width - StateTargetWidth, row.Top, StateTargetWidth, row.Height), _document.IsLocked(id.Value));
        }
        if ((e.State & TreeNodeStates.Focused) != 0) ControlPaint.DrawFocusRectangle(e.Graphics, row, DesktopAuthoringTheme.TextPrimary, background);
    }

    private static void DrawVisibility(Graphics graphics, Rectangle bounds, bool visible)
    {
        Color color = visible ? DesktopAuthoringTheme.TextPrimary : DesktopAuthoringTheme.TextSecondary;
        using var pen = new Pen(color, 1.4F);
        Rectangle eye = new(bounds.Left + 5, bounds.Top + 8, 14, 9);
        graphics.DrawEllipse(pen, eye);
        if (visible)
        {
            using var brush = new SolidBrush(color);
            graphics.FillEllipse(brush, bounds.Left + 10, bounds.Top + 11, 4, 4);
        }
        else graphics.DrawLine(pen, bounds.Left + 5, bounds.Top + 7, bounds.Right - 5, bounds.Bottom - 7);
    }

    private static void DrawLock(Graphics graphics, Rectangle bounds, bool locked)
    {
        Color color = locked ? DesktopAuthoringTheme.TextPrimary : DesktopAuthoringTheme.TextSecondary;
        using var pen = new Pen(color, 1.4F);
        Rectangle body = new(bounds.Left + 7, bounds.Top + 11, 11, 9);
        graphics.DrawRectangle(pen, body);
        Rectangle shackle = new(bounds.Left + 9, bounds.Top + 6, 7, 9);
        if (locked) graphics.DrawArc(pen, shackle, 180, -180);
        else graphics.DrawArc(pen, new Rectangle(bounds.Left + 12, bounds.Top + 6, 7, 9), 180, -150);
    }

    private IEnumerable<TreeNode> VisibleObjectNodes()
    {
        if (_tree.Nodes.Count == 0) yield break;
        foreach (TreeNode group in _tree.Nodes[0].Nodes)
            foreach (TreeNode node in group.Nodes) yield return node;
    }

    private TreeNode? FindNode(LauncherControlId? id) => id.HasValue ? VisibleObjectNodes().FirstOrDefault(node => NodeId(node) == id) : null;
    private static LauncherControlId? NodeId(TreeNode? node) => node?.Tag is LauncherObjectNode item ? item.Id : null;

    protected override void Dispose(bool disposing)
    {
        if (disposing) _toolTip.Dispose();
        base.Dispose(disposing);
    }
}
