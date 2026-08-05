using System;
using System.Collections.Generic;
using Server.MirObjects;

namespace Server.Scripting
{
    public enum MailCostOperation : byte
    {
        Preview = 1,
        Send = 2,
    }

    public sealed class MailCostRequest
    {
        public MailCostRequest(
            PlayerObject player,
            MailCostOperation operation,
            ulong[] itemUniqueIds,
            IReadOnlyList<UserItem> items,
            uint gold,
            bool requestedStamped,
            bool effectiveStamped,
            uint baseCost,
            uint cost)
        {
            Player = player ?? throw new ArgumentNullException(nameof(player));
            Operation = operation;
            ItemUniqueIds = itemUniqueIds ?? Array.Empty<ulong>();
            Items = items ?? Array.Empty<UserItem>();
            Gold = gold;
            RequestedStamped = requestedStamped;
            EffectiveStamped = effectiveStamped;
            BaseCost = baseCost;
            Cost = cost;
        }

        public PlayerObject Player { get; }

        public MailCostOperation Operation { get; }

        public ulong[] ItemUniqueIds { get; }

        public IReadOnlyList<UserItem> Items { get; }

        public uint Gold { get; }

        public bool RequestedStamped { get; }

        public bool EffectiveStamped { get; }

        public uint BaseCost { get; }

        public uint Cost { get; set; }
    }

    public sealed class MailCostResult
    {
        public MailCostResult(
            PlayerObject player,
            MailCostOperation operation,
            ulong[] itemUniqueIds,
            IReadOnlyList<UserItem> items,
            uint gold,
            bool requestedStamped,
            bool effectiveStamped,
            uint baseCost,
            uint cost)
        {
            Player = player ?? throw new ArgumentNullException(nameof(player));
            Operation = operation;
            ItemUniqueIds = itemUniqueIds ?? Array.Empty<ulong>();
            Items = items ?? Array.Empty<UserItem>();
            Gold = gold;
            RequestedStamped = requestedStamped;
            EffectiveStamped = effectiveStamped;
            BaseCost = baseCost;
            Cost = cost;
        }

        public PlayerObject Player { get; }

        public MailCostOperation Operation { get; }

        public ulong[] ItemUniqueIds { get; }

        public IReadOnlyList<UserItem> Items { get; }

        public uint Gold { get; }

        public bool RequestedStamped { get; }

        public bool EffectiveStamped { get; }

        public uint BaseCost { get; }

        public uint Cost { get; }
    }
}
