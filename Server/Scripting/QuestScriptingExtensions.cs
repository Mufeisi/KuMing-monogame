using System.Collections.Generic;
using Server.MirDatabase;
using Server.MirObjects;

namespace Server.Scripting
{
    public readonly struct QuestConditionResult
    {
        public bool Allowed { get; }
        public string FailMessage { get; }

        public QuestConditionResult(bool allowed, string failMessage)
        {
            Allowed = allowed;
            FailMessage = failMessage ?? string.Empty;
        }

        public static QuestConditionResult Allow() => new QuestConditionResult(true, string.Empty);

        public static QuestConditionResult Deny(string message) => new QuestConditionResult(false, message);
    }

    public sealed class QuestAcceptContext
    {
        public PlayerObject Player { get; }
        public QuestInfo Info { get; }

        public QuestAcceptContext(PlayerObject player, QuestInfo info)
        {
            Player = player;
            Info = info;
        }
    }

    public sealed class QuestAcceptedContext
    {
        public PlayerObject Player { get; }
        public QuestProgressInfo Quest { get; }

        public QuestAcceptedContext(PlayerObject player, QuestProgressInfo quest)
        {
            Player = player;
            Quest = quest;
        }
    }

    public sealed class QuestFinishContext
    {
        public PlayerObject Player { get; }
        public QuestProgressInfo Quest { get; }
        public int SelectedItemIndex { get; }

        public QuestFinishContext(PlayerObject player, QuestProgressInfo quest, int selectedItemIndex)
        {
            Player = player;
            Quest = quest;
            SelectedItemIndex = selectedItemIndex;
        }
    }

    public sealed class QuestRewardContext
    {
        public PlayerObject Player { get; }
        public QuestProgressInfo Quest { get; }
        public int SelectedItemIndex { get; }

        public QuestRewardContext(PlayerObject player, QuestProgressInfo quest, int selectedItemIndex)
        {
            Player = player;
            Quest = quest;
            SelectedItemIndex = selectedItemIndex;
        }
    }

    public sealed class QuestRewardOverride
    {
        public uint GoldReward { get; set; }
        public uint ExpReward { get; set; }
        public uint CreditReward { get; set; }

        public List<QuestItemRewardDefinition> FixedRewards { get; } = new List<QuestItemRewardDefinition>();
        public List<QuestItemRewardDefinition> SelectRewards { get; } = new List<QuestItemRewardDefinition>();
    }
}

