namespace Server.Scripting
{
    /// <summary>
    /// C# 侧 NPC 商店定义（替代 legacy 脚本中的 [Types]/[UsedTypes]/[Trade]/[Quests] 段落）。
    /// Key 口径：npcFileName 为 Envir/NPCs 下的 FileName（不含 .txt）。
    /// </summary>
    public sealed class NpcShopDefinition
    {
        public NpcShopDefinition(
            string npcFileName,
            IReadOnlyList<ItemType> types,
            IReadOnlyList<NpcShopGoodDefinition> goods,
            IReadOnlyList<ItemType> usedTypes = null,
            IReadOnlyList<int> questIndices = null,
            IReadOnlyList<string> craftRecipeOutputItemNames = null)
        {
            if (string.IsNullOrWhiteSpace(npcFileName))
                throw new ArgumentException("npcFileName 不能为空。", nameof(npcFileName));

            NpcFileName = npcFileName.Trim();
            Key = LogicKey.NormalizeOrThrow($"NPCs/{NpcFileName}");

            Types = types ?? Array.Empty<ItemType>();
            UsedTypes = usedTypes ?? Array.Empty<ItemType>();
            Goods = goods ?? Array.Empty<NpcShopGoodDefinition>();
            QuestIndices = questIndices ?? Array.Empty<int>();
            CraftRecipeOutputItemNames = craftRecipeOutputItemNames ?? Array.Empty<string>();
        }

        /// <summary>
        /// 归一化后的逻辑 Key：npcs/&lt;fileName&gt;
        /// </summary>
        public string Key { get; }

        public string NpcFileName { get; }

        public IReadOnlyList<ItemType> Types { get; }

        public IReadOnlyList<ItemType> UsedTypes { get; }

        public IReadOnlyList<NpcShopGoodDefinition> Goods { get; }

        /// <summary>
        /// 对齐 legacy [Quests]：正数表示接任务 NPC，负数表示交任务 NPC。
        /// </summary>
        public IReadOnlyList<int> QuestIndices { get; }

        /// <summary>
        /// 对齐 legacy [RECIPE]：合成输出物品名列表（用于 NPC 的 Craft 面板）。
        /// </summary>
        public IReadOnlyList<string> CraftRecipeOutputItemNames { get; }
    }
}
