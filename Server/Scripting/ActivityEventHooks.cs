using System;
using System.Collections.Generic;
using System.Drawing;
using Server.MirEnvir;
using Server.MirObjects;

namespace Server.Scripting
{
    public enum ActivitySourceType : byte
    {
        Unknown = 0,
        Conquest = 1,
        Dragon = 2,
    }

    public enum ActivityProgressKind : byte
    {
        State = 1,
        Wave = 2,
    }

    public enum ActivityProgressReason : byte
    {
        Unknown = 0,
        Start = 1,
        Advance = 2,
        Regress = 3,
        End = 4,
    }

    public enum ActivityResultReason : byte
    {
        Unknown = 0,
        Capture = 1,
        Complete = 2,
    }

    public enum ActivityRewardReason : byte
    {
        Unknown = 0,
        WaveAdvance = 1,
        Complete = 2,
    }

    public sealed class ActivityDescriptor
    {
        public ActivityDescriptor(ActivitySourceType sourceType, string activityKey, string displayName, Map map, int sourceIndex = 0)
        {
            SourceType = sourceType;
            ActivityKey = LogicKey.NormalizeOrThrow(activityKey);
            DisplayName = displayName ?? string.Empty;
            Map = map;
            SourceIndex = sourceIndex;
        }

        public ActivitySourceType SourceType { get; }

        public string ActivityKey { get; }

        public string DisplayName { get; }

        public Map Map { get; }

        public string MapFileName => Map?.Info?.FileName ?? string.Empty;

        public int SourceIndex { get; }
    }

    public sealed class ActivityProgressRequest
    {
        public ActivityProgressRequest(
            ActivityDescriptor descriptor,
            ActivityProgressKind kind,
            ActivityProgressReason reason,
            int previousValue,
            int value,
            int maxValue,
            ConquestObject conquest = null,
            Dragon dragon = null)
        {
            Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
            Kind = kind;
            Reason = reason;
            PreviousValue = previousValue;
            Value = value;
            MaxValue = maxValue;
            Conquest = conquest;
            Dragon = dragon;
        }

        public ActivityDescriptor Descriptor { get; }

        public ActivityProgressKind Kind { get; }

        public ActivityProgressReason Reason { get; }

        public int PreviousValue { get; }

        public int Value { get; set; }

        public int MaxValue { get; }

        public ConquestObject Conquest { get; }

        public Dragon Dragon { get; }

        public ScriptHookDecision Decision { get; set; } = ScriptHookDecision.Continue;
    }

    public sealed class ActivityProgressResult
    {
        public ActivityProgressResult(
            ActivityDescriptor descriptor,
            ActivityProgressKind kind,
            ActivityProgressReason reason,
            int previousValue,
            int value,
            int maxValue,
            bool executedLegacy,
            ScriptHookDecision decision,
            ConquestObject conquest = null,
            Dragon dragon = null)
        {
            Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
            Kind = kind;
            Reason = reason;
            PreviousValue = previousValue;
            Value = value;
            MaxValue = maxValue;
            ExecutedLegacy = executedLegacy;
            Decision = decision;
            Conquest = conquest;
            Dragon = dragon;
        }

        public ActivityDescriptor Descriptor { get; }

        public ActivityProgressKind Kind { get; }

        public ActivityProgressReason Reason { get; }

        public int PreviousValue { get; }

        public int Value { get; }

        public int MaxValue { get; }

        public bool ExecutedLegacy { get; }

        public ScriptHookDecision Decision { get; }

        public ConquestObject Conquest { get; }

        public Dragon Dragon { get; }
    }

    public sealed class ActivityResultRequest
    {
        public ActivityResultRequest(
            ActivityDescriptor descriptor,
            ActivityResultReason reason,
            GuildObject previousWinner,
            GuildObject winner,
            PlayerObject actor,
            bool endWar,
            ConquestObject conquest = null)
        {
            Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
            Reason = reason;
            PreviousWinner = previousWinner;
            Winner = winner;
            Actor = actor;
            EndWar = endWar;
            Conquest = conquest;
        }

        public ActivityDescriptor Descriptor { get; }

        public ActivityResultReason Reason { get; }

        public GuildObject PreviousWinner { get; }

        public GuildObject Winner { get; set; }

        public PlayerObject Actor { get; }

        public bool EndWar { get; set; }

        public ConquestObject Conquest { get; }

        public ScriptHookDecision Decision { get; set; } = ScriptHookDecision.Continue;
    }

    public sealed class ActivityResult
    {
        public ActivityResult(
            ActivityDescriptor descriptor,
            ActivityResultReason reason,
            GuildObject previousWinner,
            GuildObject winner,
            PlayerObject actor,
            bool endedWar,
            bool executedLegacy,
            ScriptHookDecision decision,
            ConquestObject conquest = null)
        {
            Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
            Reason = reason;
            PreviousWinner = previousWinner;
            Winner = winner;
            Actor = actor;
            EndedWar = endedWar;
            ExecutedLegacy = executedLegacy;
            Decision = decision;
            Conquest = conquest;
        }

        public ActivityDescriptor Descriptor { get; }

        public ActivityResultReason Reason { get; }

        public GuildObject PreviousWinner { get; }

        public GuildObject Winner { get; }

        public PlayerObject Actor { get; }

        public bool EndedWar { get; }

        public bool ExecutedLegacy { get; }

        public ScriptHookDecision Decision { get; }

        public ConquestObject Conquest { get; }
    }

    public sealed class ActivityRewardRequest
    {
        public ActivityRewardRequest(
            ActivityDescriptor descriptor,
            ActivityRewardReason reason,
            int rewardLevel,
            uint gold,
            IReadOnlyList<UserItem> items,
            MapObject owner,
            long ownerDuration,
            Rectangle dropArea,
            Dragon dragon = null)
        {
            Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
            Reason = reason;
            RewardLevel = rewardLevel;
            Gold = gold;
            Items = CloneItems(items);
            Owner = owner;
            OwnerDuration = ownerDuration;
            DropArea = dropArea;
            Dragon = dragon;
        }

        public ActivityDescriptor Descriptor { get; }

        public ActivityRewardReason Reason { get; }

        public int RewardLevel { get; set; }

        public uint Gold { get; set; }

        public List<UserItem> Items { get; set; }

        public MapObject Owner { get; set; }

        public long OwnerDuration { get; set; }

        public Rectangle DropArea { get; set; }

        public Dragon Dragon { get; }

        public ScriptHookDecision Decision { get; set; } = ScriptHookDecision.Continue;

        public static List<UserItem> CloneItems(IReadOnlyList<UserItem> src)
        {
            if (src == null || src.Count == 0)
                return new List<UserItem>(0);

            var items = new List<UserItem>(src.Count);
            for (var i = 0; i < src.Count; i++)
            {
                var item = src[i];
                if (item == null) continue;
                items.Add(item.Clone());
            }

            return items;
        }
    }

    public sealed class ActivityRewardResult
    {
        public ActivityRewardResult(
            ActivityDescriptor descriptor,
            ActivityRewardReason reason,
            int rewardLevel,
            uint requestedGold,
            uint distributedGold,
            IReadOnlyList<UserItem> items,
            int distributedItemCount,
            MapObject owner,
            long ownerDuration,
            Rectangle dropArea,
            bool success,
            bool executedLegacy,
            ScriptHookDecision decision,
            Dragon dragon = null)
        {
            Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
            Reason = reason;
            RewardLevel = rewardLevel;
            RequestedGold = requestedGold;
            DistributedGold = distributedGold;
            Items = ActivityRewardRequest.CloneItems(items);
            DistributedItemCount = distributedItemCount;
            Owner = owner;
            OwnerDuration = ownerDuration;
            DropArea = dropArea;
            Success = success;
            ExecutedLegacy = executedLegacy;
            Decision = decision;
            Dragon = dragon;
        }

        public ActivityDescriptor Descriptor { get; }

        public ActivityRewardReason Reason { get; }

        public int RewardLevel { get; }

        public uint RequestedGold { get; }

        public uint DistributedGold { get; }

        public List<UserItem> Items { get; }

        public int DistributedItemCount { get; }

        public MapObject Owner { get; }

        public long OwnerDuration { get; }

        public Rectangle DropArea { get; }

        public bool Success { get; }

        public bool ExecutedLegacy { get; }

        public ScriptHookDecision Decision { get; }

        public Dragon Dragon { get; }
    }
}

