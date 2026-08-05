using System;
using System.Collections.Generic;
using Server.MirDatabase;
using Server.MirObjects;

namespace Server.Scripting
{
    public sealed class MonsterDropRequest
    {
        public MonsterDropRequest(
            MonsterObject monster,
            MapObject expOwner,
            IReadOnlyList<DropInfo> drops,
            string dropTableKey,
            int itemDropRatePercentOffset,
            int goldDropRatePercentOffset,
            MapObject dropOwner,
            long ownerDuration)
        {
            Monster = monster ?? throw new ArgumentNullException(nameof(monster));
            ExpOwner = expOwner;
            DropTableKey = dropTableKey ?? string.Empty;

            Drops = CloneDrops(drops);

            ItemDropRatePercentOffset = itemDropRatePercentOffset;
            GoldDropRatePercentOffset = goldDropRatePercentOffset;

            DropOwner = dropOwner;
            OwnerDuration = ownerDuration;
        }

        public MonsterObject Monster { get; }

        /// <summary>
        /// 掉落的经验归属（通常为击杀者玩家）。
        /// </summary>
        public MapObject ExpOwner { get; }

        /// <summary>
        /// 当前掉落表逻辑 Key（与 Drops/*.txt 对齐；仅用于信息/日志）。
        /// </summary>
        public string DropTableKey { get; set; } = string.Empty;

        /// <summary>
        /// 掉落项（默认深拷贝自 MonsterInfo.Drops；可增删改以修正掉落表）。
        /// </summary>
        public List<DropInfo> Drops { get; set; } = new List<DropInfo>();

        /// <summary>
        /// 物品掉落数率（百分比偏移，传入 DropInfo.AttemptDrop；可设为 0 以忽略玩家加成）。
        /// </summary>
        public int ItemDropRatePercentOffset { get; set; }

        /// <summary>
        /// 金币收益数率（百分比偏移，传入 DropInfo.AttemptDrop；可设为 0 以忽略玩家加成）。
        /// </summary>
        public int GoldDropRatePercentOffset { get; set; }

        /// <summary>
        /// 倍率：重复执行掉落计算次数（1=原逻辑；2=双倍掉落，依此类推）。
        /// </summary>
        public int DropTimes { get; set; } = 1;

        /// <summary>
        /// 掉落归属（ItemObject.Owner/OwnerTime 逻辑；为 null 表示自由拾取）。
        /// </summary>
        public MapObject DropOwner { get; set; }

        /// <summary>
        /// 掉落归属时长（毫秒，默认 Settings.Minute）。
        /// </summary>
        public long OwnerDuration { get; set; }

        public ScriptHookDecision Decision { get; set; } = ScriptHookDecision.Continue;

        public static List<DropInfo> CloneDrops(IReadOnlyList<DropInfo> src)
        {
            if (src == null || src.Count == 0)
                return new List<DropInfo>(0);

            var list = new List<DropInfo>(src.Count);
            for (var i = 0; i < src.Count; i++)
            {
                var item = src[i];
                if (item == null) continue;
                list.Add(CloneDrop(item));
            }

            return list;
        }

        private static DropInfo CloneDrop(DropInfo src)
        {
            var dst = new DropInfo
            {
                Chance = src.Chance,
                Item = src.Item,
                Count = src.Count,
                Weight = src.Weight,
                Gold = src.Gold,
                GroupedDrop = null,
                Type = src.Type,
                QuestRequired = src.QuestRequired,
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
                    group.Add(CloneDrop(child));
                }

                dst.GroupedDrop = group;
            }

            return dst;
        }
    }

    public sealed class MonsterDropResult
    {
        public MonsterDropResult(
            MonsterObject monster,
            MapObject expOwner,
            MapObject dropOwner,
            long ownerTime,
            string dropTableKey,
            int itemDropRatePercentOffset,
            int goldDropRatePercentOffset,
            int dropTimes,
            uint droppedGold,
            IReadOnlyList<UserItem> droppedItems,
            bool executedLegacy,
            ScriptHookDecision decision)
        {
            Monster = monster ?? throw new ArgumentNullException(nameof(monster));
            ExpOwner = expOwner;
            DropOwner = dropOwner;
            OwnerTime = ownerTime;
            DropTableKey = dropTableKey ?? string.Empty;
            ItemDropRatePercentOffset = itemDropRatePercentOffset;
            GoldDropRatePercentOffset = goldDropRatePercentOffset;
            DropTimes = dropTimes;
            DroppedGold = droppedGold;
            DroppedItems = droppedItems ?? Array.Empty<UserItem>();
            ExecutedLegacy = executedLegacy;
            Decision = decision;
        }

        public MonsterObject Monster { get; }

        public MapObject ExpOwner { get; }

        public MapObject DropOwner { get; }

        public long OwnerTime { get; }

        public string DropTableKey { get; }

        public int ItemDropRatePercentOffset { get; }

        public int GoldDropRatePercentOffset { get; }

        public int DropTimes { get; }

        public uint DroppedGold { get; }

        public IReadOnlyList<UserItem> DroppedItems { get; }

        public bool ExecutedLegacy { get; }

        public ScriptHookDecision Decision { get; }
    }
}
