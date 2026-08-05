using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using FontStashSharp;
using MonoShare.MirControls;
using MonoShare.MirGraphics;
using MonoShare.MirScenes;
using MonoShare.MirSounds;
using MonoShare.Share.Extensions;



namespace MonoShare.MirObjects
{
    public abstract class MapObject
    {
        //public static Font ChatFont = new Font(Settings.FontName, 10F);
        //public static List<MirLabel> LabelList = new List<MirLabel>();

        public static UserObject User;
        public static MapObject MouseObject, TargetObject, MagicObject;
        public abstract ObjectType Race { get; }
        public abstract bool Blocking { get; }

        public uint ObjectID;
        public string Name = string.Empty;
        public Point CurrentLocation, MapLocation;
        public MirDirection Direction;
        public bool Dead, Hidden, SitDown, Sneaking;
        public PoisonType Poison;
        public long DeadTime;
        public ushort AI;
        public bool InTrapRock;

        public bool Blend = true;



        public byte PercentHealth;
        public long HealthTime;

        public List<QueuedAction> ActionFeed = new List<QueuedAction>();
        public QueuedAction NextAction
        {
            get { return ActionFeed.Count > 0 ? ActionFeed[0] : null; }
        }

        public List<Effect> Effects = new List<Effect>();
        public List<BuffType> Buffs = new List<BuffType>();

        public MLibrary BodyLibrary;
        public Color DrawColour = Color.White, NameColour = Color.White, LightColour = Color.White;
        //public MirLabel NameLabel, ChatLabel, GuildLabel;
        public long ChatTime;
        public int DrawFrame, DrawWingFrame;
        public Point DrawLocation, Movement, FinalDrawLocation, OffSetMove;
        public Rectangle DisplayRectangle;
        public int Light, DrawY;
        public long NextMotion, NextMotion2;
        public MirAction CurrentAction;
        public bool SkipFrames;

        //Sound
        public int StruckWeapon;

        //public MirLabel TempLabel;

        //public static List<MirLabel> DamageLabelList = new List<MirLabel>();
        public List<Damage> Damages = new List<Damage>();

        protected Point GlobalDisplayLocationOffset
        {
            get { return new Point(0, 0); }
        }

        protected MapObject(uint objectID)
        {
            ObjectID = objectID;

            for (int i = MapControl.Objects.Count - 1; i >= 0; i--)
            {
                MapObject ob = MapControl.Objects[i];
                if (ob.ObjectID != ObjectID) continue;
                ob.Remove();
            }

            MapControl.Objects.Add(this);
            GameScene.Scene?.MapControl?.TrackObjectArrivalDuringPendingLoad(ObjectID);
        }
        public void Remove()
        {
            if (MouseObject == this) MouseObject = null;
            if (TargetObject == this) TargetObject = null;
            if (MagicObject == this) MagicObject = null;

            if (this == User.NextMagicObject)
                User.ClearMagic();

            bool flag = MapControl.Objects.Remove(this);
            GameScene.Scene.MapControl.RemoveObject(this);

            if (ObjectID != GameScene.NPCID) return;

            GameScene.NPCID = 0;
            //GameScene.Scene.NPCDialog.Hide();
        }

        public abstract void Process();
        public abstract void Draw();
        public abstract bool MouseOver(Point p);

        public void AddBuffEffect(BuffType type)
        {
            for (int i = 0; i < Effects.Count; i++)
            {
                if (!(Effects[i] is BuffEffect)) continue;
                if (((BuffEffect)(Effects[i])).BuffType == type) return;
            }

            PlayerObject ob = null;

            if (Race == ObjectType.Player)
            {
                ob = (PlayerObject)this;
            }

            switch (type)
            {
                case BuffType.Fury:
                    Effects.Add(new BuffEffect(Libraries.Magic3, 190, 7, 1400, this, true, type) { Repeat = true });
                    break;
                case BuffType.CounterAttack:
                    Effects.Add(new BuffEffect(Libraries.Magic3, 130, 8, 900, this, true, type) { Repeat = false });
                    SoundManager.PlaySound(20000 + (ushort)Spell.CounterAttack * 10 + 0);
                    Effects.Add(new BuffEffect(Libraries.Magic3, 140, 2, 800, this, true, type) { Repeat = true });
                    break;
                case BuffType.ImmortalSkin:
                    Effects.Add(new BuffEffect(Libraries.Magic3, 570, 5, 1400, this, true, type) { Repeat = true });
                    break;
                case BuffType.天上秘术:
                    Effects.Add(new BuffEffect(Libraries.Magic3, 210, 7, 1400, this, true, type) { Repeat = true });
                    break;
                case BuffType.SwiftFeet:
                    if (ob != null) ob.Sprint = true;
                    break;
                case BuffType.MoonLight:
                case BuffType.DarkBody:
                    if (ob != null) ob.Sneaking = true;
                    break;
                case BuffType.VampireShot:
                    Effects.Add(new BuffEffect(Libraries.Magic3, 2110, 6, 1400, this, true, type) { Repeat = false });
                    break;
                case BuffType.PoisonShot:
                    Effects.Add(new BuffEffect(Libraries.Magic3, 2310, 7, 1400, this, true, type) { Repeat = false });
                    break;
                case BuffType.EnergyShield:
                    BuffEffect effect;

                    Effects.Add(effect = new BuffEffect(Libraries.Magic2, 1880, 9, 900, this, true, type) { Repeat = false });
                    SoundManager.PlaySound(20000 + (ushort)Spell.EnergyShield * 10 + 0);

                    effect.Complete += (o, e) =>
                    {
                        Effects.Add(new BuffEffect(Libraries.Magic2, 1900, 2, 800, this, true, type) { Repeat = true });
                    };
                    break;
                case BuffType.MagicBooster:
                    Effects.Add(new BuffEffect(Libraries.Magic3, 90, 6, 1200, this, true, type) { Repeat = true });
                    break;
                case BuffType.PetEnhancer:
                    Effects.Add(new BuffEffect(Libraries.Magic3, 230, 6, 1200, this, true, type) { Repeat = true });
                    break;
                case BuffType.GameMaster:
                    Effects.Add(new BuffEffect(Libraries.CHumEffect[5], 0, 1, 1200, this, true, type) { Repeat = true });
                    break;
                case BuffType.华丽雨光:
                    Effects.Add(new BuffEffect(Libraries.Magic3, 3470, 8, 1400, this, true, type) { Repeat = true });
                    break;
                case BuffType.龙之特效:
                    Effects.Add(new BuffEffect(Libraries.Magic3, 7040, 32, 1600, this, true, type) { Blend = true, Repeat = true, Delay = 60000 });
                    Effects.Add(new BuffEffect(Libraries.Magic3, 7080, 25, 1200, this, true, type) { Repeat = true, DrawBehind = true, Delay = 10000 });
                    break;
                case BuffType.龙的特效:
                    Effects.Add(new BuffEffect(Libraries.Magic3, 7040, 32, 1600, this, true, type) { Blend = true, Repeat = true, Delay = 60000 });
                    Effects.Add(new BuffEffect(Libraries.Magic3, 7080, 25, 1200, this, true, type) { Repeat = true, DrawBehind = true, Delay = 10000 });
                    break;
                case BuffType.GeneralMeowMeowShield:
                    Effects.Add(new BuffEffect(Libraries.Monsters[(ushort)Monster.GeneralMeowMeow], 569, 7, 700, this, true, type) { Repeat = true, Light = 1 });
                    SoundManager.PlaySound(8322);
                    break;
                case BuffType.御体之力:
                    Effects.Add(new BuffEffect(Libraries.Monsters[(ushort)Monster.PowerUpBead], 62, 6, 600, this, true, type) { Blend = true, Repeat = true });
                    break;
            }
        }
        public void RemoveBuffEffect(BuffType type)
        {
            PlayerObject ob = null;

            if (Race == ObjectType.Player)
            {
                ob = (PlayerObject)this;
            }

            for (int i = 0; i < Effects.Count; i++)
            {
                if (!(Effects[i] is BuffEffect)) continue;
                if (((BuffEffect)(Effects[i])).BuffType != type) continue;
                Effects[i].Repeat = false;
            }

            switch (type)
            {
                case BuffType.SwiftFeet:
                    if (ob != null) ob.Sprint = false;
                    break;
                case BuffType.MoonLight:
                case BuffType.DarkBody:
                    if (ob != null) ob.Sneaking = false;
                    break;
            }
        }

        public virtual Missile CreateProjectile(int baseIndex, MLibrary library, bool blend, int count, int interval, int skip, int lightDistance = 6, bool direction16 = true, Color? lightColour = null, uint targetID = 0)
        {
            return null;
        }

        public void Chat(string text)
        {
            //if (ChatLabel != null && !ChatLabel.IsDisposed)
            //{
            //    ChatLabel.Dispose();
            //    ChatLabel = null;
            //}

            //const int chatWidth = 200;
            //List<string> chat = new List<string>();

            //int index = 0;
            //for (int i = 1; i < text.Length; i++)
            //    if (TextRenderer.MeasureText(CMain.Graphics, text.Substring(index, i - index), ChatFont).Width > chatWidth)
            //    {
            //        chat.Add(text.Substring(index, i - index - 1));
            //        index = i - 1;
            //    }
            //chat.Add(text.Substring(index, text.Length - index));

            //text = chat[0];
            //for (int i = 1; i < chat.Count; i++)
            //    text += string.Format("\n{0}", chat[i]);

            //ChatLabel = new MirLabel
            //{
            //    AutoSize = true,
            //    BackColour = Color.Transparent,
            //    ForeColour = Color.White,
            //    OutLine = true,
            //    OutLineColour = Color.Black,
            //    DrawFormat = TextFormatFlags.HorizontalCenter,
            //    Text = text,
            //};
            ChatTime = CMain.Time + 5000;
        }
        public virtual void DrawChat()
        {
            //if (ChatLabel == null || ChatLabel.IsDisposed) return;

            //if (CMain.Time > ChatTime)
            //{
            //    ChatLabel.Dispose();
            //    ChatLabel = null;
            //    return;
            //}

            //ChatLabel.ForeColour = Dead ? Color.Gray : Color.White;
            //ChatLabel.Location = new Point(DisplayRectangle.X + (48 - ChatLabel.Size.Width) / 2, DisplayRectangle.Y - (60 + ChatLabel.Size.Height) - (Dead ? 35 : 0));
            //ChatLabel.Draw();
        }

        public virtual void CreateLabel()
        {
            //NameLabel = null;

            //for (int i = 0; i < LabelList.Count; i++)
            //{
            //    if (LabelList[i].Text != Name || LabelList[i].ForeColour != NameColour) continue;
            //    NameLabel = LabelList[i];
            //    break;
            //}


            //if (NameLabel != null && !NameLabel.IsDisposed) return;

            //NameLabel = new MirLabel
            //{
            //    AutoSize = true,
            //    BackColour = Color.Transparent,
            //    ForeColour = NameColour,
            //    OutLine = true,
            //    OutLineColour = Color.Black,
            //    Text = Name,
            //};
            //NameLabel.Disposing += (o, e) => LabelList.Remove(NameLabel);
            //LabelList.Add(NameLabel);



        }
        public virtual void DrawName()
        {
            if (string.IsNullOrWhiteSpace(Name))
                return;

            DrawNameBlock(new[] { Name }, _ => NameColour);
        }

        protected void DrawNameBlock(
            IReadOnlyList<string> lines,
            Func<int, Color> lineColorSelector,
            int yOffset = 0,
            float yAbove = 10f,
            int fontSize = 14,
            float lineSpacing = 2f)
        {
            if (lines == null || lines.Count == 0)
                return;

            if (!TryGetFont(fontSize, out DynamicSpriteFont font))
                return;

            float lineHeight = Math.Max(1f, font.MeasureString("Hg").Y);
            float totalHeight = lines.Count * lineHeight + Math.Max(0, lines.Count - 1) * lineSpacing;
            float startY = DisplayRectangle.Y - totalHeight - yAbove + yOffset;

            for (int i = 0; i < lines.Count; i++)
            {
                string line = lines[i] ?? string.Empty;
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                Microsoft.Xna.Framework.Vector2 size = font.MeasureString(line);

                // 注意：DisplayRectangle.Width 对应的是资源实际帧宽度（可能远大于一个地图格子），
                // 用它居中会导致部分宽体型 NPC/怪物名字向右“漂移”。
                // 这里按老客户端的逻辑，基于格子宽度（玩家略宽）来做水平居中。
                int anchorWidth = Race == ObjectType.Player ? 50 : MapControl.CellWidth;
                float x = DisplayRectangle.X + (anchorWidth - size.X) / 2f;
                float y = startY + i * (lineHeight + lineSpacing);

                Color color = lineColorSelector != null ? lineColorSelector(i) : Color.White;
                DrawTextShadow(font, line, new Microsoft.Xna.Framework.Vector2(x, y), color);
            }
        }

        protected static bool TryGetFont(int fontSize, out DynamicSpriteFont font)
        {
            font = null;

            if (fontSize <= 0)
                return false;

            if (CMain.fontSystem == null || CMain.spriteBatch == null)
                return false;

            try
            {
                font = CMain.fontSystem.GetFont(fontSize);
            }
            catch (Exception)
            {
                font = null;
                return false;
            }

            return font != null;
        }

        protected static void DrawTextShadow(DynamicSpriteFont font, string text, Microsoft.Xna.Framework.Vector2 position, Color color)
        {
            if (font == null || CMain.spriteBatch == null || string.IsNullOrWhiteSpace(text))
                return;

            bool ownsSpriteBatch = CMain.SpriteBatchScope == null || CMain.SpriteBatchScope.Depth == 0;
            if (ownsSpriteBatch)
                CMain.SpriteBatchScope?.Begin();

            try
            {
                var shadow = new Microsoft.Xna.Framework.Color(0, 0, 0, 180);
                var main = color.ToXnaColor();

                CMain.spriteBatch.DrawString(font, text, position + new Microsoft.Xna.Framework.Vector2(1, 1), shadow);
                CMain.spriteBatch.DrawString(font, text, position, main);
            }
            finally
            {
                if (ownsSpriteBatch)
                    CMain.SpriteBatchScope?.End();
            }
        }
        public virtual void DrawBlend()
        {
            //DXManager.SetBlend(true, 0.3F); //0.8
            Draw();
            //DXManager.SetBlend(false);
        }
        public void DrawDamages()
        {
            for (int i = Damages.Count - 1; i >= 0; i--)
            {
                Damage info = Damages[i];
                if (CMain.Time > info.ExpireTime)
                {
                    //info.DamageLabel.Dispose();
                    Damages.RemoveAt(i);
                }
                else
                {
                    info.Draw(DisplayRectangle.Location);
                }
            }
        }
        public void DrawHealth()
        {
            string name = Name;

            if (Name.Contains("(")) name = Name.Substring(Name.IndexOf("(") + 1, Name.Length - Name.IndexOf("(") - 2);

            if (Dead) return;
            if (Race == ObjectType.Item) return;
            //if (Race != ObjectType.Player && Race != ObjectType.Monster && Race != ObjectType.Merchant) return;

            // 移动端：双方非互为战斗对象时不显示血条（玩家/怪物），避免城镇等非战斗场景血条过多影响观感。
            // 判定口径：若“我没有选中它”且“它也没有以我为目标”，则认为非战斗关系。
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
            {
                try
                {
                    if (Race == ObjectType.Player || Race == ObjectType.Monster)
                    {
                        bool isUser = ReferenceEquals(this, User);
                        bool isSelected = ReferenceEquals(this, TargetObject);

                        bool targetsUser = false;
                        try
                        {
                            uint userId = User?.ObjectID ?? 0;
                            if (userId != 0)
                            {
                                if (this is PlayerObject po)
                                    targetsUser = po.TargetID == userId;
                                else if (this is MonsterObject mo)
                                    targetsUser = mo.TargetID == userId;
                            }
                        }
                        catch
                        {
                            targetsUser = false;
                        }

                        if (!isUser && !isSelected && !targetsUser)
                            return;
                    }
                }
                catch
                {
                }
            }

            if (CMain.Time >= HealthTime)
            {
                //if (Race == ObjectType.Monster && !Name.EndsWith(string.Format("({0})", User.Name)) && !GroupDialog.GroupList.Contains(name))
                //    return;
                if (Race == ObjectType.Monster)
                {
                    var pet = (MonsterObject)this;
                    switch (pet.BaseImage)
                    {
                        case Monster.BabyPig:
                        case Monster.Chick:
                        case Monster.Kitten:
                        case Monster.BabySkeleton:
                        case Monster.Baekdon:
                        case Monster.Wimaen:
                        case Monster.BlackKitten:
                        case Monster.BabyDragon:
                        case Monster.OlympicFlame:
                        case Monster.BabySnowMan:
                        case Monster.Frog:
                        case Monster.BabyMonkey:
                        case Monster.AngryBird:
                        case Monster.Foxey:
                        case Monster.MedicalRat:
                            return;
                            break;
                    }
                }
                //if (Race == ObjectType.Player && this != User && !GroupDialog.GroupList.Contains(Name))
                //    return;
                //if (this == User && GroupDialog.GroupList.Count == 0) return;
            }

            int offsetY = 63;
            if (Race == ObjectType.Player)
            {
                if (((PlayerObject)this).RidingMount)
                    offsetY = 78;
            }
            if (Race == ObjectType.Merchant)
                offsetY = 58;

            Libraries.Prguse2.Draw(0, DisplayRectangle.X + 8, DisplayRectangle.Y - offsetY);
            int index = 1;

            switch (Race)
            {
                case ObjectType.Player:
                    //if (GroupDialog.GroupList.Contains(name))
                    //    index = 10;
                    //else if (this != User)
                    //{
                    //    index = 1;
                    //    PercentHealth = this.PercentHealth;
                    //}
                    break;
                case ObjectType.Monster:
                    //if (GroupDialog.GroupList.Contains(name) || name == User.Name) index = 11;
                    var monster = (MonsterObject)this;
                    if (!monster.Dead)
                        PercentHealth = monster.PercentHealth > 0 ? monster.PercentHealth : (byte)100;
                    break;
                case ObjectType.Merchant:
                    PercentHealth = 100;
                    index = 12;
                    break;
            }

            Libraries.Prguse2.Draw(index, new Rectangle(0, 0, (int)(32 * PercentHealth / 100F), 4), new Point(DisplayRectangle.X + 8, DisplayRectangle.Y - offsetY), Color.White, false);
        }

        public void DrawPoison()
        {
            byte poisoncount = 0;
            if (Poison != PoisonType.None)
            {
                //if (Poison.HasFlag(PoisonType.Green))
                //{
                //    DXManager.Sprite.Draw(DXManager.PoisonDotBackground, new Rectangle(0, 0, 2, 2), Vector3.Zero, new Vector3((float)(DisplayRectangle.X + 7 + (poisoncount * 3)), (float)(DisplayRectangle.Y - 21), 0.0F), Color.Black);
                //    DXManager.Sprite.Draw(DXManager.RadarTexture, new Rectangle(0, 0, 2, 2), Vector3.Zero, new Vector3((float)(DisplayRectangle.X + 8 + (poisoncount * 3)), (float)(DisplayRectangle.Y - 20), 0.0F), Color.Green);

                //    poisoncount++;
                //}
                //if (Poison.HasFlag(PoisonType.Red))
                //{
                //    DXManager.Sprite.Draw(DXManager.PoisonDotBackground, new Rectangle(0, 0, 2, 2), Vector3.Zero, new Vector3((float)(DisplayRectangle.X + 7 + (poisoncount * 3)), (float)(DisplayRectangle.Y - 21), 0.0F), Color.Black);
                //    DXManager.Sprite.Draw(DXManager.RadarTexture, new Rectangle(0, 0, 2, 2), Vector3.Zero, new Vector3((float)(DisplayRectangle.X + 8 + (poisoncount * 3)), (float)(DisplayRectangle.Y - 20), 0.0F), Color.Red);
                //    poisoncount++;
                //}
                //if (Poison.HasFlag(PoisonType.Bleeding))
                //{
                //    DXManager.Sprite.Draw(DXManager.PoisonDotBackground, new Rectangle(0, 0, 2, 2), Vector3.Zero, new Vector3((float)(DisplayRectangle.X + 7 + (poisoncount * 3)), (float)(DisplayRectangle.Y - 21), 0.0F), Color.Black);
                //    DXManager.Sprite.Draw(DXManager.RadarTexture, new Rectangle(0, 0, 2, 2), Vector3.Zero, new Vector3((float)(DisplayRectangle.X + 8 + (poisoncount * 3)), (float)(DisplayRectangle.Y - 20), 0.0F), Color.DarkRed);
                //    poisoncount++;
                //}
                //if (Poison.HasFlag(PoisonType.Slow))
                //{
                //    DXManager.Sprite.Draw(DXManager.PoisonDotBackground, new Rectangle(0, 0, 2, 2), Vector3.Zero, new Vector3((float)(DisplayRectangle.X + 7 + (poisoncount * 3)), (float)(DisplayRectangle.Y - 21), 0.0F), Color.Black);
                //    DXManager.Sprite.Draw(DXManager.RadarTexture, new Rectangle(0, 0, 2, 2), Vector3.Zero, new Vector3((float)(DisplayRectangle.X + 8 + (poisoncount * 3)), (float)(DisplayRectangle.Y - 20), 0.0F), Color.Purple);
                //    poisoncount++;
                //}
                //if (Poison.HasFlag(PoisonType.Stun))
                //{
                //    DXManager.Sprite.Draw(DXManager.PoisonDotBackground, new Rectangle(0, 0, 2, 2), Vector3.Zero, new Vector3((float)(DisplayRectangle.X + 7 + (poisoncount * 3)), (float)(DisplayRectangle.Y - 21), 0.0F), Color.Black);
                //    DXManager.Sprite.Draw(DXManager.RadarTexture, new Rectangle(0, 0, 2, 2), Vector3.Zero, new Vector3((float)(DisplayRectangle.X + 8 + (poisoncount * 3)), (float)(DisplayRectangle.Y - 20), 0.0F), Color.Yellow);
                //    poisoncount++;
                //}
                //if (Poison.HasFlag(PoisonType.Frozen))
                //{
                //    DXManager.Sprite.Draw(DXManager.PoisonDotBackground, new Rectangle(0, 0, 2, 2), Vector3.Zero, new Vector3((float)(DisplayRectangle.X + 7 + (poisoncount * 3)), (float)(DisplayRectangle.Y - 21), 0.0F), Color.Black);
                //    DXManager.Sprite.Draw(DXManager.RadarTexture, new Rectangle(0, 0, 2, 2), Vector3.Zero, new Vector3((float)(DisplayRectangle.X + 8 + (poisoncount * 3)), (float)(DisplayRectangle.Y - 20), 0.0F), Color.Blue);
                //    poisoncount++;
                //}
                //if (Poison.HasFlag(PoisonType.Paralysis) || Poison.HasFlag(PoisonType.LRParalysis))
                //{
                //    DXManager.Sprite.Draw(DXManager.PoisonDotBackground, new Rectangle(0, 0, 2, 2), Vector3.Zero, new Vector3((float)(DisplayRectangle.X + 7 + (poisoncount * 3)), (float)(DisplayRectangle.Y - 21), 0.0F), Color.Black);
                //    DXManager.Sprite.Draw(DXManager.RadarTexture, new Rectangle(0, 0, 2, 2), Vector3.Zero, new Vector3((float)(DisplayRectangle.X + 8 + (poisoncount * 3)), (float)(DisplayRectangle.Y - 20), 0.0F), Color.Gray);
                //    poisoncount++;
                //}
                //if (Poison.HasFlag(PoisonType.DelayedExplosion))
                //{
                //    DXManager.Sprite.Draw(DXManager.PoisonDotBackground, new Rectangle(0, 0, 2, 2), Vector3.Zero, new Vector3((float)(DisplayRectangle.X + 7 + (poisoncount * 3)), (float)(DisplayRectangle.Y - 21), 0.0F), Color.Black);
                //    DXManager.Sprite.Draw(DXManager.RadarTexture, new Rectangle(0, 0, 2, 2), Vector3.Zero, new Vector3((float)(DisplayRectangle.X + 8 + (poisoncount * 3)), (float)(DisplayRectangle.Y - 20), 0.0F), Color.Orange);
                //    poisoncount++;
                //}
            }
        }

        public abstract void DrawBehindEffects(bool effectsEnabled);

        public abstract void DrawEffects(bool effectsEnabled);

    }

}
