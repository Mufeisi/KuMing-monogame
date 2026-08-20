namespace Server.Scripting
{
    /// <summary>
    /// C# 版掉落表定义（Key = Drops/&lt;FileName&gt;，用于替代 Envir/Drops/*.txt）。
    /// 当前目标：先做到 1:1 对齐现有 <see cref="Server.MirDatabase.DropInfo"/> 的表达能力。
    /// </summary>
    public sealed class DropTableDefinition
    {
        public string Key { get; }

        /// <summary>
        /// 掉落项列表（1/N 概率；支持 Item/Gold/Group；支持 Q(QuestRequired)）。
        /// </summary>
        public List<DropEntryDefinition> Drops { get; } = new List<DropEntryDefinition>();

        public DropTableDefinition(string key)
        {
            Key = LogicKey.NormalizeOrThrow(key);
        }
    }

    public sealed class DropEntryDefinition
    {
        /// <summary>
        /// 概率分母：1/Chance；必须 &gt; 0。
        /// </summary>
        public int Chance { get; set; }

        /// <summary>
        /// 权重（默认 1；必须 &gt; 0）。
        /// 说明：当前仅用于 GROUP(Random) 的“随机选择”阶段：当组内存在多个成功候选时，按 Weight 加权随机选 1 个。
        /// </summary>
        public int Weight { get; set; } = 1;

        /// <summary>
        /// 可选：掉落条件。返回 false 时该掉落项视为“不参与计算”。
        /// 注意：该条件会在掉落计算前执行；如需依赖玩家/怪物/来源等信息，可从 <see cref="DropAttemptContext"/> 获取。
        /// </summary>
        public Func<DropAttemptContext, bool> Condition { get; set; }

        /// <summary>
        /// 物品名（与 ItemInfo.Name 对齐）。与 <see cref="Gold"/>/<see cref="Group"/> 三选一。
        /// </summary>
        public string ItemName { get; set; } = string.Empty;

        /// <summary>
        /// 物品掉落数量（仅对 <see cref="ItemName"/> 生效）。必须 &gt; 0；默认 1。
        /// 说明：目前掉落引擎会按数量重复产出多件（不会自动合并为堆叠）。
        /// </summary>
        public ushort Count { get; set; } = 1;

        /// <summary>
        /// 金币基数（0 表示不是金币掉落）。与 <see cref="ItemName"/>/<see cref="Group"/> 三选一。
        /// </summary>
        public uint Gold { get; set; }

        /// <summary>
        /// 是否为任务掉落（对齐 txt 的第三列 Q）。
        /// </summary>
        public bool QuestRequired { get; set; }

        /// <summary>
        /// 分组掉落（对齐 txt 的 GROUP/GROUP*/GROUP^；可嵌套）。与 <see cref="ItemName"/>/<see cref="Gold"/> 三选一。
        /// </summary>
        public DropGroupDefinition Group { get; set; }

        public static DropEntryDefinition Item(int chance, string itemName, bool questRequired = false)
        {
            return new DropEntryDefinition
            {
                Chance = chance,
                ItemName = itemName ?? string.Empty,
                QuestRequired = questRequired,
            };
        }

        public static DropEntryDefinition Item(int chance, string itemName, ushort count, int weight, bool questRequired = false)
        {
            return new DropEntryDefinition
            {
                Chance = chance,
                Weight = weight,
                ItemName = itemName ?? string.Empty,
                Count = count,
                QuestRequired = questRequired,
            };
        }

        public static DropEntryDefinition Item(int chance, string itemName, ushort count, bool questRequired = false)
        {
            return new DropEntryDefinition
            {
                Chance = chance,
                ItemName = itemName ?? string.Empty,
                Count = count,
                QuestRequired = questRequired,
            };
        }

        public static DropEntryDefinition GoldDrop(int chance, uint gold)
        {
            return new DropEntryDefinition
            {
                Chance = chance,
                Gold = gold,
            };
        }

        public static DropEntryDefinition GoldDrop(int chance, uint gold, int weight)
        {
            return new DropEntryDefinition
            {
                Chance = chance,
                Weight = weight,
                Gold = gold,
            };
        }

        public static DropEntryDefinition GroupDrop(int chance, DropGroupDefinition group)
        {
            return new DropEntryDefinition
            {
                Chance = chance,
                Group = group,
            };
        }

        public static DropEntryDefinition GroupDrop(int chance, DropGroupDefinition group, int weight)
        {
            return new DropEntryDefinition
            {
                Chance = chance,
                Weight = weight,
                Group = group,
            };
        }
    }

    public sealed class DropGroupDefinition
    {
        /// <summary>
        /// Random=true：从组内成功掉落的候选里随机选 1 个；否则全部产出。
        /// </summary>
        public bool Random { get; set; }

        /// <summary>
        /// First=true：组内按顺序命中第一个成功的即停止；否则全部遍历。
        /// </summary>
        public bool First { get; set; }

        public List<DropEntryDefinition> Drops { get; } = new List<DropEntryDefinition>();
    }
}
