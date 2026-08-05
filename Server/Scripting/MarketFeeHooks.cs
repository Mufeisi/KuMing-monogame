using System;
using Server.MirDatabase;
using Server.MirObjects;

namespace Server.Scripting
{
    public enum MarketFeeOperation : byte
    {
        Listing = 1,
        Commission = 2,
    }

    public sealed class MarketFeeRequest
    {
        public MarketFeeRequest(
            PlayerObject player,
            NPCObject npc,
            MarketFeeOperation operation,
            MarketPanelType panelType,
            MarketItemType itemType,
            AuctionInfo auction,
            UserItem item,
            uint price,
            uint baseListingFee,
            uint listingFee,
            float baseCommissionRate,
            float commissionRate)
        {
            Player = player ?? throw new ArgumentNullException(nameof(player));
            NPC = npc ?? throw new ArgumentNullException(nameof(npc));
            Operation = operation;
            PanelType = panelType;
            ItemType = itemType;
            Auction = auction;
            Item = item;
            Price = price;
            BaseListingFee = baseListingFee;
            ListingFee = listingFee;
            BaseCommissionRate = baseCommissionRate;
            CommissionRate = commissionRate;
        }

        public PlayerObject Player { get; }

        public NPCObject NPC { get; }

        public MarketFeeOperation Operation { get; }

        public MarketPanelType PanelType { get; }

        public MarketItemType ItemType { get; }

        public AuctionInfo Auction { get; }

        public UserItem Item { get; }

        public uint Price { get; }

        public uint BaseListingFee { get; }

        public uint ListingFee { get; set; }

        public float BaseCommissionRate { get; }

        public float CommissionRate { get; set; }

        public string FailMessage { get; set; } = string.Empty;

        public ScriptHookDecision Decision { get; set; } = ScriptHookDecision.Continue;

        public uint BaseSellerReceives => CalculateSellerReceives(Price, BaseCommissionRate);

        public uint SellerReceives => CalculateSellerReceives(Price, CommissionRate);

        public uint BaseCommissionAmount => Price > BaseSellerReceives ? Price - BaseSellerReceives : 0U;

        public uint CommissionAmount => Price > SellerReceives ? Price - SellerReceives : 0U;

        public static uint CalculateSellerReceives(uint price, float commissionRate)
        {
            if (commissionRate < 0)
                commissionRate = 0;

            return (uint)Math.Max(0, price - price * commissionRate);
        }
    }

    public sealed class MarketFeeResult
    {
        public MarketFeeResult(
            PlayerObject player,
            NPCObject npc,
            MarketFeeOperation operation,
            MarketPanelType panelType,
            MarketItemType itemType,
            AuctionInfo auction,
            UserItem item,
            uint price,
            uint baseListingFee,
            uint listingFee,
            float baseCommissionRate,
            float commissionRate,
            bool executedLegacy,
            ScriptHookDecision decision)
        {
            Player = player ?? throw new ArgumentNullException(nameof(player));
            NPC = npc ?? throw new ArgumentNullException(nameof(npc));
            Operation = operation;
            PanelType = panelType;
            ItemType = itemType;
            Auction = auction;
            Item = item;
            Price = price;
            BaseListingFee = baseListingFee;
            ListingFee = listingFee;
            BaseCommissionRate = baseCommissionRate;
            CommissionRate = commissionRate;
            ExecutedLegacy = executedLegacy;
            Decision = decision;
        }

        public PlayerObject Player { get; }

        public NPCObject NPC { get; }

        public MarketFeeOperation Operation { get; }

        public MarketPanelType PanelType { get; }

        public MarketItemType ItemType { get; }

        public AuctionInfo Auction { get; }

        public UserItem Item { get; }

        public uint Price { get; }

        public uint BaseListingFee { get; }

        public uint ListingFee { get; }

        public float BaseCommissionRate { get; }

        public float CommissionRate { get; }

        public bool ExecutedLegacy { get; }

        public ScriptHookDecision Decision { get; }

        public uint BaseSellerReceives => MarketFeeRequest.CalculateSellerReceives(Price, BaseCommissionRate);

        public uint SellerReceives => MarketFeeRequest.CalculateSellerReceives(Price, CommissionRate);

        public uint BaseCommissionAmount => Price > BaseSellerReceives ? Price - BaseSellerReceives : 0U;

        public uint CommissionAmount => Price > SellerReceives ? Price - SellerReceives : 0U;
    }
}

