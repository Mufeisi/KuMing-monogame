using MonoShare.MirControls;
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace MonoShare.MirGraphics
{
    class DXManager
    {
        public static List<MImage> TextureList = new List<MImage>();
        public static List<MirControl> ControlList = new List<MirControl>();

        public static void Clean()
        {
            for (int i = TextureList.Count - 1; i >= 0; i--)
            {
                MImage m = TextureList[i];

                if (m == null)
                {
                    TextureList.RemoveAt(i);
                    continue;
                }

                if (CMain.Time <= m.CleanTime) continue;

                m.DisposeTexture();
            }

            for (int i = ControlList.Count - 1; i >= 0; i--)
            {
                MirControl c = ControlList[i];

                if (c == null)
                {
                    ControlList.RemoveAt(i);
                    continue;
                }

                if (CMain.Time <= c.CleanTime) continue;

                c.DisposeTexture();
            }
        }



        public static void Draw(MImage mi, int index, int x, int y)
        {
            CMain.spriteBatch.Draw(mi.Image,
                new Vector2(x, y),
                new Rectangle(new Point(0, 0), new Point(mi.Width, mi.Height)),
                Color.White);
            //DXManager.Sprite.Draw(mi.Image, new Rectangle(0, 0, mi.Width, mi.Height), Vector3.Zero, new Vector3((float)x, (float)y, 0.0F), Color.White);
            mi.CleanTime = CMain.Time + Settings.CleanDelay;

        }

        public static void Draw(MImage mi, int index, Point point, Color colour)
        {
            CMain.spriteBatch.Draw(mi.Image,
                new Vector2(point.X, point.Y),
                new Rectangle(new Point(0, 0), new Point(mi.Width, mi.Height)),
                colour);

            //DXManager.Sprite.Draw(mi.Image, new Rectangle(0, 0, mi.Width, mi.Height), Vector3.Zero, new Vector3((float)point.X, (float)point.Y, 0.0F), colour);
        }

    }
}