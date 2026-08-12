using System.Text.RegularExpressions;
using Server.MirEnvir;
using Server.Scripting;

namespace Server.MirDatabase
{
    public class MonsterInfo
    {
        protected static Envir Envir
        {
            get { return Envir.Main; }
        }

        protected static Envir EditEnvir
        {
            get { return Envir.Edit; }
        }

        protected static MessageQueue MessageQueue
        {
            get { return MessageQueue.Instance; }
        }

        public int Index;
        public string Name = string.Empty;

        public Monster Image;
        public byte Effect, ViewRange = 7, CoolEye;
        public ushort AI, Level;

        public byte Light;

        public ushort AttackSpeed = 2500, MoveSpeed = 1800;
        public uint Experience;

        public string DropPath = "";
        
        public List<DropInfo> Drops = new List<DropInfo>();

        public bool CanTame = true, CanPush = true, AutoRev = true, Undead = false;

        public bool HasSpawnScript;
        public bool HasDieScript;

        public Stats Stats;

        public MonsterInfo()
        {
            Stats = new Stats();
        }

        public MonsterInfo(BinaryReader reader)
        {
            Index = reader.ReadInt32();
            Name = reader.ReadString();

            Image = (Monster) reader.ReadUInt16();
            AI = reader.ReadUInt16();
            Effect = reader.ReadByte();

            if (Envir.LoadVersion < 62)
            {
                Level = (ushort)reader.ReadByte();
            }
            else
            {
                Level = reader.ReadUInt16();
            }

            ViewRange = reader.ReadByte();
            CoolEye = reader.ReadByte();

            if (Envir.LoadVersion > 84)
            {
                Stats = new Stats(reader);
            }

            if (Envir.LoadVersion <= 84)
            {
                Stats = new Stats();
                Stats[Stat.HP] = (int)reader.ReadUInt32(); //Monster form prevented greater than ushort, so this should never overflow.
            }

            if (Envir.LoadVersion < 62)
            {
                Stats[Stat.MinAC] = reader.ReadByte();
                Stats[Stat.MaxAC] = reader.ReadByte();
                Stats[Stat.MinMAC] = reader.ReadByte();
                Stats[Stat.MaxMAC] = reader.ReadByte();
                Stats[Stat.MinDC] = reader.ReadByte();
                Stats[Stat.MaxDC] = reader.ReadByte();
                Stats[Stat.MinMC] = reader.ReadByte();
                Stats[Stat.MaxMC] = reader.ReadByte();
                Stats[Stat.MinSC] = reader.ReadByte();
                Stats[Stat.MaxSC] = reader.ReadByte();
            }
            else
            {
                if (Envir.LoadVersion <= 84)
                {
                    Stats[Stat.MinAC] = reader.ReadUInt16();
                    Stats[Stat.MaxAC] = reader.ReadUInt16();
                    Stats[Stat.MinMAC] = reader.ReadUInt16();
                    Stats[Stat.MaxMAC] = reader.ReadUInt16();
                    Stats[Stat.MinDC] = reader.ReadUInt16();
                    Stats[Stat.MaxDC] = reader.ReadUInt16();
                    Stats[Stat.MinMC] = reader.ReadUInt16();
                    Stats[Stat.MaxMC] = reader.ReadUInt16();
                    Stats[Stat.MinSC] = reader.ReadUInt16();
                    Stats[Stat.MaxSC] = reader.ReadUInt16();
                }
            }

            if (Envir.LoadVersion <= 84)
            {
                Stats[Stat.准确] = reader.ReadByte();
                Stats[Stat.敏捷] = reader.ReadByte();
            }

            Light = reader.ReadByte();

            AttackSpeed = reader.ReadUInt16();
            MoveSpeed = reader.ReadUInt16();

            Experience = reader.ReadUInt32();

            CanPush = reader.ReadBoolean();
            CanTame = reader.ReadBoolean();

            if (Envir.LoadVersion < 18) return;
            AutoRev = reader.ReadBoolean();
            Undead = reader.ReadBoolean();

            if (Envir.LoadVersion < 89) return;

            DropPath = reader.ReadString();
        }

        public string GameName
        {
            get { return Regex.Replace(Name, @"[\d-]", string.Empty); }
        }

        public void Save(BinaryWriter writer)
        {
            writer.Write(Index);
            writer.Write(Name);

            writer.Write((ushort) Image);
            writer.Write((ushort) AI);
            writer.Write(Effect);
            writer.Write(Level);
            writer.Write(ViewRange);
            writer.Write(CoolEye);

            Stats.Save(writer);


            writer.Write(Light);

            writer.Write(AttackSpeed);
            writer.Write(MoveSpeed);

            writer.Write(Experience);

            writer.Write(CanPush);
            writer.Write(CanTame);
            writer.Write(AutoRev);
            writer.Write(Undead);

            writer.Write(DropPath);
        }

        public static void FromText(string text)
        {
            string[] data = text.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

            if (data.Length < 28) return; //28

            MonsterInfo info = new MonsterInfo {Name = data[0]};
            ushort image;
            if (!ushort.TryParse(data[1], out image)) return;
            info.Image = (Monster) image;

            if (!ushort.TryParse(data[2], out info.AI)) return;
            if (!byte.TryParse(data[3], out info.Effect)) return;
            if (!ushort.TryParse(data[4], out info.Level)) return;
            if (!byte.TryParse(data[5], out info.ViewRange)) return;

            //if (!int.TryParse(data[6], out info.HP)) return;

            //if (!ushort.TryParse(data[7], out info.MinAC)) return;
            //if (!ushort.TryParse(data[8], out info.MaxAC)) return;
            //if (!ushort.TryParse(data[9], out info.MinMAC)) return;
            //if (!ushort.TryParse(data[10], out info.MaxMAC)) return;
            //if (!ushort.TryParse(data[11], out info.MinDC)) return;
            //if (!ushort.TryParse(data[12], out info.MaxDC)) return;
            //if (!ushort.TryParse(data[13], out info.MinMC)) return;
            //if (!ushort.TryParse(data[14], out info.MaxMC)) return;
            //if (!ushort.TryParse(data[15], out info.MinSC)) return;
            //if (!ushort.TryParse(data[16], out info.MaxSC)) return;
            //if (!byte.TryParse(data[17], out info.Accuracy)) return;
            //if (!byte.TryParse(data[18], out info.Agility)) return;
            if (!byte.TryParse(data[19], out info.Light)) return;

            if (!ushort.TryParse(data[20], out info.AttackSpeed)) return;
            if (!ushort.TryParse(data[21], out info.MoveSpeed)) return;

            if (!uint.TryParse(data[22], out info.Experience)) return;
            
            if (!bool.TryParse(data[23], out info.CanTame)) return;
            if (!bool.TryParse(data[24], out info.CanPush)) return;

            if (!bool.TryParse(data[25], out info.AutoRev)) return;
            if (!bool.TryParse(data[26], out info.Undead)) return;
            if (!byte.TryParse(data[27], out info.CoolEye)) return;

            //int count;

            //if (!int.TryParse(data[27], out count)) return;

            //if (28 + count * 3 > data.Length) return;

            info.Index = ++EditEnvir.MonsterIndex;
            EditEnvir.MonsterInfoList.Add(info);
        }
        public string ToText()
        {
            return "";// string.Format("{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14},{15},{16},{17},{18},{19},{20},{21},{22},{23},{24},{25},{26},{27}", Name, (ushort)Image, AI, Effect, Level, ViewRange,
              //  HP, 
                //MinAC, MaxAC, MinMAC, MaxMAC, MinDC, MaxDC, MinMC, MaxMC, MinSC, MaxSC, 
               // Accuracy, Agility, Light, AttackSpeed, MoveSpeed, Experience, CanTame, CanPush, AutoRev, Undead, CoolEye);
        }

        public override string ToString()
        {
            return string.Format("{0}: {1}", Index, Name);
            //return string.Format("{0}", Name);
        }
    }

    public class DropRewardInfo
    {
        public List<ItemInfo> Items;
        public uint Gold;
    }

    public class GroupDropInfo : List<DropInfo>
    {
        public bool Random;
        public bool First;
    }

    public class DropInfo
    {
        protected static Envir Envir
        {
            get { return Envir.Main; }
        }

        protected static MessageQueue MessageQueue
        {
            get { return MessageQueue.Instance; }
        }

        public int Chance;
        public ItemInfo Item;

        /// <summary>
        /// 物品掉落数量（仅在 <see cref="Item"/> 非空时生效）。默认 1。
        /// </summary>
        public ushort Count = 1;

        /// <summary>
        /// 权重（默认 1）。当前仅用于 GROUP(Random) 的“随机选择”阶段：当组内存在多个成功候选时，按 Weight 加权随机选 1 个。
        /// </summary>
        public int Weight = 1;

        /// <summary>
        /// 可选：掉落条件。返回 false 时该掉落项视为“不参与计算”。
        /// 注意：如果设置了 Condition，但调用方未传入 <see cref="DropAttemptContext"/>，则该掉落项会被视为不满足条件。
        /// </summary>
        public Func<DropAttemptContext, bool> Condition;
        public uint Gold;
        public GroupDropInfo GroupedDrop;

        public byte Type;
        public bool QuestRequired;

        public static void Load(List<DropInfo> list, string name, string path, byte type = 0)
        {
            Envir.LoadDropTable(list, name, path, type);
        }

        internal static void SortDrops(List<DropInfo> list)
        {
            if (list == null) return;

            list.Sort((drop1, drop2) =>
            {
                if (drop1.Gold > 0 && drop2.Gold == 0)
                    return 1;
                if (drop1.Gold == 0 && drop2.Gold > 0)
                    return -1;

                if (drop1.Item == null || drop2.Item == null) return 0;

                return drop1.Item.Type.CompareTo(drop2.Item.Type);
            });
        }

        internal static void AddFromCSharpTable(List<DropInfo> list, IReadOnlyList<DropInfo> table, byte type)
        {
            if (list == null) return;
            if (table == null || table.Count == 0) return;

            for (var i = 0; i < table.Count; i++)
            {
                var src = table[i];
                if (src == null) continue;

                list.Add(CloneDrop(src, type));
            }
        }

        internal static DropInfo CloneDrop(DropInfo src, byte type)
        {
            var dst = new DropInfo
            {
                Chance = src.Chance,
                Item = src.Item,
                Count = src.Count,
                Weight = src.Weight,
                Gold = src.Gold,
                GroupedDrop = null,
                Type = type,
                QuestRequired = src.QuestRequired,
                Condition = src.Condition,
            };

            if (src.GroupedDrop != null)
            {
                var group = new GroupDropInfo
                {
                    Random = src.GroupedDrop.Random,
                    First = src.GroupedDrop.First
                };

                for (var i = 0; i < src.GroupedDrop.Count; i++)
                {
                    var child = src.GroupedDrop[i];
                    if (child == null) continue;
                    group.Add(CloneDrop(child, type));
                }

                dst.GroupedDrop = group;
            }

            return dst;
        }

        internal static bool TryResolveDropTableKey(string path, out string key)
        {
            key = string.Empty;

            if (string.IsNullOrWhiteSpace(path))
                return false;

            try
            {
                var dropRoot = Path.GetFullPath(Settings.DropPath);
                var fullPath = Path.GetFullPath(path);

                var relative = Path.GetRelativePath(dropRoot, fullPath);

                if (relative.StartsWith("..", StringComparison.Ordinal) ||
                    relative.StartsWith("../", StringComparison.Ordinal) ||
                    relative.StartsWith("..\\", StringComparison.Ordinal))
                {
                    return false;
                }

                relative = relative.Replace('\\', '/');

                if (relative.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                {
                    relative = relative.Substring(0, relative.Length - 4);
                }

                if (string.IsNullOrWhiteSpace(relative) || relative == ".")
                    return false;

                return LogicKey.TryNormalize($"Drops/{relative}", out key);
            }
            catch
            {
                return false;
            }
        }

        public DropRewardInfo AttemptDrop(int itemDropRatePercentOffset = 0, int goldDropRatePercentOffset = 0, float dropRate = 0, DropAttemptContext context = null)
        {
            if (Condition != null)
            {
                if (context == null) return null;

                try
                {
                    if (!Condition(context))
                        return null;
                }
                catch (Exception ex)
                {
                    if (!_conditionErrorLogged && Settings.CSharpScriptsLogDiagnostics)
                    {
                        _conditionErrorLogged = true;

                        var playerName = context.Player?.Name ?? string.Empty;
                        var monsterName = context.Monster?.Name ?? string.Empty;
                        var key = context.DropTableKey ?? string.Empty;
                        var source = context.Source ?? string.Empty;

                        MessageQueue.Enqueue($"[Scripts] Drop 条件执行异常：source={source} key={key} player={playerName} monster={monsterName} err={ex.Message}");
                    }

                    return null;
                }
            }

            var effectiveDropRate = dropRate > 0 ? dropRate : Settings.DropRate;
            if (effectiveDropRate <= 0) return null;

            int rate = (int)(Chance / effectiveDropRate);

            if (itemDropRatePercentOffset > 0)
            {
                rate -= (rate * itemDropRatePercentOffset) / 100;
            }

            if (rate < 1) rate = 1;

            if (Envir.Random.Next(rate) != 0)
            {
                return null;
            }

            uint gold = 0;
            var items = new List<ItemInfo>();

            if (Gold > 0)
            {
                int lowerGoldRange = (int)(Gold / 2);
                int upperGoldRange = (int)(Gold + Gold / 2);

                if (goldDropRatePercentOffset > 0)
                {
                    lowerGoldRange += (lowerGoldRange * goldDropRatePercentOffset) / 100;
                }

                if (lowerGoldRange > upperGoldRange) lowerGoldRange = upperGoldRange;

                gold = (uint)Envir.Random.Next(lowerGoldRange, upperGoldRange);
            }
            else if (Item != null)
            {
                var itemCount = (int)Count;
                if (itemCount < 1) itemCount = 1;

                for (var i = 0; i < itemCount; i++)
                {
                    items.Add(Item);
                }
            }
            else if (GroupedDrop != null)
            {
                var candidates = new List<(ItemInfo Item, int Weight)>();

                foreach (var item in GroupedDrop)
                {
                    var reward = item.AttemptDrop(itemDropRatePercentOffset, goldDropRatePercentOffset, effectiveDropRate, context);

                    if (reward != null)
                    {
                        gold += reward.Gold;

                        if (reward.Items != null && reward.Items.Count > 0)
                        {
                            var w = item?.Weight ?? 1;
                            if (w < 1) w = 1;

                            for (var i = 0; i < reward.Items.Count; i++)
                            {
                                var rewardItem = reward.Items[i];
                                if (rewardItem == null) continue;
                                candidates.Add((rewardItem, w));
                            }
                        }

                        if (GroupedDrop.First)
                        {
                            break;
                        }
                    }
                }

                if (GroupedDrop.Random)
                {
                    if (candidates.Count > 0)
                    {
                        var totalWeight = 0;
                        for (var i = 0; i < candidates.Count; i++)
                        {
                            totalWeight += candidates[i].Weight;
                        }

                        if (totalWeight <= 0) totalWeight = candidates.Count;

                        var roll = Envir.Random.Next(totalWeight);

                        for (var i = 0; i < candidates.Count; i++)
                        {
                            roll -= candidates[i].Weight;
                            if (roll >= 0) continue;

                            items.Add(candidates[i].Item);
                            break;
                        }
                    }
                }
                else
                {
                    for (var i = 0; i < candidates.Count; i++)
                    {
                        items.Add(candidates[i].Item);
                    }
                }
            }

            return new DropRewardInfo
            {
                Gold = gold,
                Items = items
            };
        }

        private bool _conditionErrorLogged;
    }
}
