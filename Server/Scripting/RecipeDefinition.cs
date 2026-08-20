namespace Server.Scripting
{
    /// <summary>
    /// C# 版配方定义（Key = Recipe/&lt;FileName&gt;，用于替代 Envir/Recipe/*.txt）。
    /// 当前目标：先做到 1:1 对齐现有 <see cref="Server.MirDatabase.RecipeInfo"/> 的表达能力。
    /// </summary>
    public sealed class RecipeDefinition
    {
        public string Key { get; }

        /// <summary>
        /// 合成产出数量（对齐 txt 的 [Recipe] Amount）。
        /// </summary>
        public ushort Amount { get; set; } = 1;

        /// <summary>
        /// 成功率（0~100，对齐 txt 的 [Recipe] Chance）。
        /// </summary>
        public byte Chance { get; set; } = 100;

        /// <summary>
        /// 金币消耗（对齐 txt 的 [Recipe] Gold）。
        /// </summary>
        public uint Gold { get; set; }

        /// <summary>
        /// 工具列表（对齐 txt 的 [Tools]）。
        /// </summary>
        public List<string> Tools { get; } = new List<string>();

        /// <summary>
        /// 材料列表（对齐 txt 的 [Ingredients]）。
        /// </summary>
        public List<RecipeIngredientDefinition> Ingredients { get; } = new List<RecipeIngredientDefinition>();

        /// <summary>
        /// 条件列表（对齐 txt 的 [Criteria]）。
        /// </summary>
        public ushort? RequiredLevel { get; set; }
        public MirGender? RequiredGender { get; set; }
        public List<MirClass> RequiredClass { get; } = new List<MirClass>();
        public List<int> RequiredFlag { get; } = new List<int>();
        public List<int> RequiredQuest { get; } = new List<int>();

        public RecipeDefinition(string key)
        {
            Key = LogicKey.NormalizeOrThrow(key);
        }
    }

    public readonly struct RecipeIngredientDefinition
    {
        public string ItemName { get; }
        public ushort Count { get; }

        /// <summary>
        /// 对齐旧逻辑：当 RequiredDura &lt; 物品 MaxDura 且 &gt; 玩家物品 CurrentDura 时视为不满足。
        /// 0 表示不限制。
        /// </summary>
        public ushort RequiredDura { get; }

        public RecipeIngredientDefinition(string itemName, ushort count = 1, ushort requiredDura = 0)
        {
            ItemName = itemName ?? string.Empty;
            Count = count;
            RequiredDura = requiredDura;
        }
    }
}

