namespace MonoShare.MirScenes;

/// <summary>
/// Fixed mobile seal/rental geometry.  Candidate lists are paged instead of
/// growing the panel, so every action remains inside the safe-area bounds on
/// both portrait phones and the 1334x750 landscape simulator.
/// </summary>
public static class MobileSealRentalLayout
{
    public enum PanelTab
    {
        Seal,
        Rental,
    }

    public const PanelTab DefaultTab = PanelTab.Seal;
    public const int MaterialPageSize = 6;
    public const int TargetPageSize = 9;
    public const int RentalPageSize = 6;
    public const float Margin = 12F;
    public const float MaxPanelWidth = 760F;
    public const float MaxPanelHeight = 704F;

    public static PanelTab SelectTab(PanelTab requested)
    {
        return requested == PanelTab.Rental ? PanelTab.Rental : PanelTab.Seal;
    }

    public static bool IsTabEnabled(PanelTab selected, PanelTab candidate)
    {
        return selected != candidate;
    }

    public readonly struct Bounds
    {
        public Bounds(float x, float y, float width, float height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        public float X { get; }
        public float Y { get; }
        public float Width { get; }
        public float Height { get; }

        public bool Contains(float x, float y, float width, float height)
        {
            return x >= X && y >= Y && width >= 0F && height >= 0F &&
                   x + width <= X + Width && y + height <= Y + Height;
        }
    }

    public static Bounds GetPanel(float rootWidth, float rootHeight)
    {
        float width = rootWidth < 1F ? 1F : rootWidth;
        float height = rootHeight < 1F ? 1F : rootHeight;
        float panelWidth = Clamp(width - Margin * 2F, 1F, MaxPanelWidth);
        float panelHeight = Clamp(height - Margin * 2F, 1F, MaxPanelHeight);
        return new Bounds((width - panelWidth) / 2F, (height - panelHeight) / 2F,
            panelWidth, panelHeight);
    }

    public static bool IsReachable(float rootWidth, float rootHeight,
        float localX, float localY, float localWidth, float localHeight)
    {
        Bounds panel = GetPanel(rootWidth, rootHeight);
        return panel.Contains(panel.X + localX, panel.Y + localY, localWidth, localHeight);
    }

    private static float Clamp(float value, float min, float max)
    {
        return value < min ? min : (value > max ? max : value);
    }
}
