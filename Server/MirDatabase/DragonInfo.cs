using System.Drawing;
﻿using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Server.MirEnvir;
using Server.Scripting;

namespace Server.MirDatabase
{
    public class DragonInfo
    {
        protected static Envir Envir
        {
            get { return Envir.Main; }
        }

        protected static MessageQueue MessageQueue
        {
            get { return MessageQueue.Instance; }
        }

        public bool Enabled;
        public string MapFileName, MonsterName, BodyName;
        public Point Location, DropAreaTop, DropAreaBottom;
        public List<DropInfo>[] Drops = new List<DropInfo>[Globals.MaxDragonLevel];
        public long[] Exps = new long[Globals.MaxDragonLevel - 1];

        public byte Level;
        public long Experience;

        public DragonInfo()
        {
            //Default values
            Enabled = false;
            MapFileName = "D2083";
            MonsterName = "破天魔龙";
            BodyName = "00";
            Location = new Point(82, 44);
            DropAreaTop = new Point(75, 45);
            DropAreaBottom = new Point(86, 57);

            Level = 1;

            for (int i = 0; i < Exps.Length; i++)
            {
                Exps[i] = (i + 1) * 10000;
            }
            for (int i = 0; i < Drops.Length; i++)
            {
                Drops[i] = new List<DropInfo>();
            }
        }
        public DragonInfo(BinaryReader reader)
        {
            Enabled = reader.ReadBoolean();
            MapFileName = reader.ReadString();
            MonsterName = reader.ReadString();
            BodyName = reader.ReadString();
            Location = new Point(reader.ReadInt32(), reader.ReadInt32());
            DropAreaTop = new Point(reader.ReadInt32(), reader.ReadInt32());
            DropAreaBottom = new Point(reader.ReadInt32(), reader.ReadInt32());

            Level = 1;

            for (int i = 0; i < Exps.Length; i++)
            {
                Exps[i] = reader.ReadInt64();
            }
            for (int i = 0; i < Drops.Length; i++)
            {
                Drops[i] = new List<DropInfo>();
            }
        }
        public void Save(BinaryWriter writer)
        {
            writer.Write(Enabled);
            writer.Write(MapFileName);
            writer.Write(MonsterName);
            writer.Write(BodyName);
            writer.Write(Location.X);
            writer.Write(Location.Y);
            writer.Write(DropAreaTop.X);
            writer.Write(DropAreaTop.Y);
            writer.Write(DropAreaBottom.X);
            writer.Write(DropAreaBottom.Y);
            for (int i = 0; i < Exps.Length; i++)
            {
                writer.Write(Exps[i]);
            }
        }

        public void LoadDrops()
        {
            Envir?.ReloadDragonDrops(this);
        }

        internal void ClearDrops()
        {
            for (int i = 0; i < Globals.MaxDragonLevel; i++) Drops[i].Clear();
        }

        internal void SortDrops()
        {
            for (int i = 0; i < Globals.MaxDragonLevel; i++)
            {
                Drops[i].Sort((drop1, drop2) =>
                {
                    var aIsGold = drop1?.Gold > 0;
                    var bIsGold = drop2?.Gold > 0;

                    if (aIsGold && !bIsGold) return 1;
                    if (!aIsGold && bIsGold) return -1;

                    if (aIsGold && bIsGold)
                        return drop1.Gold.CompareTo(drop2.Gold);

                    return (drop1?.Item?.Type ?? ItemType.杂物).CompareTo(drop2?.Item?.Type ?? ItemType.杂物);
                });
            }
        }

        internal sealed class DragonDropJsonEntry
        {
            public int Chance { get; set; }
            public string ItemName { get; set; } = string.Empty;
            public uint Gold { get; set; }
            public byte Level { get; set; }
        }

        internal static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        internal static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        internal bool TryLoadDropsFromJson(string jsonPath, out string error)
        {
            error = string.Empty;

            try
            {
                if (string.IsNullOrWhiteSpace(jsonPath) || !File.Exists(jsonPath))
                    return false;

                var json = File.ReadAllText(jsonPath, Utf8NoBom);
                var entries = JsonSerializer.Deserialize<List<DragonDropJsonEntry>>(json, JsonOptions) ?? new List<DragonDropJsonEntry>(0);

                for (var i = 0; i < entries.Count; i++)
                {
                    var e = entries[i];
                    if (e == null) continue;

                    if (e.Level <= 0 || e.Level > Globals.MaxDragonLevel) continue;
                    if (e.Chance <= 0) continue;

                    if (e.Gold > 0)
                    {
                        Drops[e.Level - 1].Add(new DropInfo
                        {
                            Chance = e.Chance,
                            Gold = e.Gold,
                            level = e.Level,
                        });

                        continue;
                    }

                    e.ItemName ??= string.Empty;
                    if (string.IsNullOrWhiteSpace(e.ItemName)) continue;

                    var item = Envir.GetItemInfo(e.ItemName);
                    if (item == null)
                    {
                        MessageQueue.Enqueue($"加载掉落物品失败: 破天魔龙, 未知物品 {e.ItemName}");
                        continue;
                    }

                    Drops[e.Level - 1].Add(new DropInfo
                    {
                        Chance = e.Chance,
                        Item = item,
                        level = e.Level,
                    });
                }

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public class DropInfo
        {
            public int Chance;
            public ItemInfo Item;
            public uint Gold;
            public byte level;
        }
    }
}
