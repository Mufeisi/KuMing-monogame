namespace LyoCrystal.LauncherEditor;

internal static class DesktopAuthoringTheme
{
    internal static readonly Color AppBackground = Color.FromArgb(243, 243, 243);
    internal static readonly Color PanelBackground = Color.FromArgb(250, 250, 250);
    internal static readonly Color InputBackground = Color.White;
    internal static readonly Color CanvasViewport = Color.FromArgb(36, 38, 43);
    internal static readonly Color Border = Color.FromArgb(218, 220, 224);
    internal static readonly Color TextPrimary = Color.FromArgb(31, 31, 31);
    internal static readonly Color TextSecondary = Color.FromArgb(96, 96, 96);
    internal static readonly Color Accent = Color.FromArgb(0, 103, 192);
    internal static readonly Color AccentSoft = Color.FromArgb(225, 240, 252);
    internal static readonly Color Guide = Color.FromArgb(16, 185, 129);

    internal const int AppBarHeight = 36;
    internal const int ContextBarHeight = 32;
    internal const int StatusBarHeight = 24;
    internal const int ObjectTreeWidth = 190;
    internal const int PropertiesWidth = 250;

    internal static Font CreateBodyFont(float size = 9F, FontStyle style = FontStyle.Regular)
    {
        string family = FontFamily.Families.Any(item => item.Name.Equals("Segoe UI Variable", StringComparison.OrdinalIgnoreCase))
            ? "Segoe UI Variable"
            : "Segoe UI";
        return new Font(family, size, style, GraphicsUnit.Point);
    }

    internal static void Apply(Control root)
    {
        root.Font = CreateBodyFont();
        root.ForeColor = TextPrimary;
        if (root is Form or TabPage or UserControl) root.BackColor = AppBackground;
        ApplyChildren(root);
    }

    private static void ApplyChildren(Control parent)
    {
        foreach (Control control in parent.Controls)
        {
            control.Font = parent.Font;
            switch (control)
            {
                case TextBox or ComboBox or ListBox or TreeView or PropertyGrid or DataGridView:
                    control.BackColor = InputBackground;
                    control.ForeColor = TextPrimary;
                    break;
                case Panel or TableLayoutPanel or FlowLayoutPanel:
                    if (control.BackColor == SystemColors.Control) control.BackColor = PanelBackground;
                    control.ForeColor = TextPrimary;
                    break;
                case TabControl tabs:
                    tabs.BackColor = AppBackground;
                    break;
                case ToolStrip strip:
                    strip.BackColor = PanelBackground;
                    strip.ForeColor = TextPrimary;
                    strip.RenderMode = ToolStripRenderMode.System;
                    break;
            }
            ApplyChildren(control);
        }
    }
}
