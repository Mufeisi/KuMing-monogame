using System.Drawing;

namespace Server.Scripting
{
    internal static class LingFengLegacyPalette
    {
        public static bool TryGetColor(int index, out Color color)
        {
            if (!LingFengLegacyColorTable.TryGetRgb(index, out byte red, out byte green,
                    out byte blue))
            {
                color = default;
                return false;
            }
            color = Color.FromArgb(255, red, green, blue);
            return true;
        }
    }
}
