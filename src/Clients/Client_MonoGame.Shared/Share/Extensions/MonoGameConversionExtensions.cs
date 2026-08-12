#if MONOSHARE_XNA
using System.Drawing;

namespace MonoShare.Share.Extensions
{
    public static class MonoGameConversionExtensions
    {
        public static Point ToDrawPoint(this Microsoft.Xna.Framework.Point point)
        {
            return new Point(point.X, point.Y);
        }

        public static Microsoft.Xna.Framework.Point ToXnaPoint(this Point point)
        {
            return new Microsoft.Xna.Framework.Point(point.X, point.Y);
        }

        public static Microsoft.Xna.Framework.Color ToXnaColor(this Color color)
        {
            return new Microsoft.Xna.Framework.Color(color.R, color.G, color.B, color.A);
        }
    }
}
#endif
