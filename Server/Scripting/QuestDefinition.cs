namespace Server.Scripting
{
    /// <summary>
    /// C# 版任务定义（Key = Quests/&lt;FileName&gt;，用于替代 Envir/Quests/*.txt）。
    /// 说明：
    /// - 此定义主要对应 QuestInfo.ParseFile 的各个 section（Description/Tasks/Rewards...）
    /// - 任务的基础元数据（Index/Name/Group/RequiredLevel...）仍来自数据库（QuestInfoList）
    /// </summary>
    public sealed class QuestDefinition
    {
        public string Key { get; }

        public List<string> Description { get; } = new List<string>();
        public List<string> TaskDescription { get; } = new List<string>();
        public List<string> ReturnDescription { get; } = new List<string>();
        public List<string> CompletionDescription { get; } = new List<string>();

        public List<QuestItemTaskDefinition> CarryItems { get; } = new List<QuestItemTaskDefinition>();
        public List<QuestKillTaskDefinition> KillTasks { get; } = new List<QuestKillTaskDefinition>();
        public List<QuestItemTaskDefinition> ItemTasks { get; } = new List<QuestItemTaskDefinition>();
        public List<QuestFlagTaskDefinition> FlagTasks { get; } = new List<QuestFlagTaskDefinition>();

        public uint GoldReward { get; set; }
        public uint ExpReward { get; set; }
        public uint CreditReward { get; set; }

        public List<QuestItemRewardDefinition> FixedRewards { get; } = new List<QuestItemRewardDefinition>();
        public List<QuestItemRewardDefinition> SelectRewards { get; } = new List<QuestItemRewardDefinition>();

        /// <summary>
        /// 可选：覆盖 QuestInfo.TimeLimitInSeconds（若为 null 则不覆盖）。
        /// </summary>
        public int? TimeLimitInSeconds { get; set; }

        /// <summary>
        /// 可选：接受任务条件（返回 Deny 可阻止接受并提示 FailMessage）。
        /// </summary>
        public Func<QuestAcceptContext, QuestConditionResult> AcceptCondition { get; set; }

        /// <summary>
        /// 可选：提交任务条件（返回 Deny 可阻止提交并提示 FailMessage）。
        /// </summary>
        public Func<QuestFinishContext, QuestConditionResult> FinishCondition { get; set; }

        /// <summary>
        /// 可选：动态奖励解析器（返回 null 表示不覆盖；非 null 表示覆盖 Gold/Exp/Credit 与 Fixed/Select 奖励列表）。
        /// </summary>
        public Func<QuestRewardContext, QuestRewardOverride> RewardResolver { get; set; }

        /// <summary>
        /// 可选：成功接受任务后的回调（仅在任务已加入 CurrentQuests 后触发）。
        /// </summary>
        public Action<QuestAcceptedContext> OnAccepted { get; set; }

        /// <summary>
        /// 可选：成功提交任务后的回调（仅在奖励结算完成后触发）。
        /// </summary>
        public Action<QuestFinishContext> OnFinished { get; set; }

        public QuestDefinition(string key)
        {
            Key = LogicKey.NormalizeOrThrow(key);
        }
    }

    public readonly struct QuestKillTaskDefinition
    {
        public string MonsterName { get; }
        public int Count { get; }
        public string Message { get; }

        public QuestKillTaskDefinition(string monsterName, int count = 1, string message = "")
        {
            MonsterName = monsterName ?? string.Empty;
            Count = count;
            Message = message ?? string.Empty;
        }
    }

    public readonly struct QuestItemTaskDefinition
    {
        public string ItemName { get; }
        public ushort Count { get; }
        public string Message { get; }

        public QuestItemTaskDefinition(string itemName, ushort count = 1, string message = "")
        {
            ItemName = itemName ?? string.Empty;
            Count = count;
            Message = message ?? string.Empty;
        }
    }

    public readonly struct QuestFlagTaskDefinition
    {
        public int Number { get; }
        public string Message { get; }

        public QuestFlagTaskDefinition(int number, string message = "")
        {
            Number = number;
            Message = message ?? string.Empty;
        }
    }

    public readonly struct QuestItemRewardDefinition
    {
        public string ItemName { get; }
        public ushort Count { get; }

        public QuestItemRewardDefinition(string itemName, ushort count = 1)
        {
            ItemName = itemName ?? string.Empty;
            Count = count;
        }
    }
}
