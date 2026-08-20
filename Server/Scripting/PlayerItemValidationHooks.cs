using System;
using Server.MirObjects;

namespace Server.Scripting
{
    public enum PlayerItemPickupSource
    {
        Player = 0,
        IntelligentCreature = 1,
    }

    public sealed class PlayerItemPickupCheckRequest
    {
        public PlayerItemPickupCheckRequest(
            PlayerItemPickupSource source,
            MapObject picker,
            ItemObject itemObject,
            bool legacyHasPickupPermission,
            bool legacyCanGain)
        {
            Source = source;
            Picker = picker ?? throw new ArgumentNullException(nameof(picker));
            ItemObject = itemObject ?? throw new ArgumentNullException(nameof(itemObject));
            LegacyHasPickupPermission = legacyHasPickupPermission;
            LegacyCanGain = legacyCanGain;
        }

        public PlayerItemPickupSource Source { get; }

        /// <summary>
        /// 实际执行拾取动作的对象（玩家自身或灵物等）。
        /// </summary>
        public MapObject Picker { get; }

        public ItemObject ItemObject { get; }

        public MapObject DropOwner => ItemObject.Owner;

        public bool IsGold => ItemObject.Item == null;

        /// <summary>
        /// 当 <see cref="IsGold"/> 为 true 时可能为 null。
        /// </summary>
        public UserItem Item => ItemObject.Item;

        public uint Gold => ItemObject.Gold;

        public bool LegacyHasPickupPermission { get; }

        public bool LegacyCanGain { get; }

        /// <summary>
        /// 若为 true，则跳过默认 Owner/队伍拾取权限校验。
        /// </summary>
        public bool IgnoreOwnership { get; set; }

        /// <summary>
        /// 当 Decision=Cancel 时可选，用于提示玩家失败原因（空字符串表示不提示）。
        /// </summary>
        public string FailMessage { get; set; } = string.Empty;

        public ScriptHookDecision Decision { get; set; } = ScriptHookDecision.Continue;
    }

    public sealed class PlayerItemUseCheckRequest
    {
        public PlayerItemUseCheckRequest(MirGridType grid, UserItem item, int index, bool dead)
        {
            Grid = grid;
            Item = item ?? throw new ArgumentNullException(nameof(item));
            Index = index;
            Dead = dead;
        }

        public MirGridType Grid { get; }

        public UserItem Item { get; }

        public int Index { get; }

        public bool Dead { get; }

        /// <summary>
        /// 当 Decision=Cancel 时可选，用于提示玩家失败原因（空字符串表示不提示）。
        /// </summary>
        public string FailMessage { get; set; } = string.Empty;

        public ScriptHookDecision Decision { get; set; } = ScriptHookDecision.Continue;
    }
}

