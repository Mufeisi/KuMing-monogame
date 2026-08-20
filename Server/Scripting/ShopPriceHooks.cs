using System;
using Server.MirObjects;

namespace Server.Scripting
{
    public enum ShopPriceOperation : byte
    {
        Buy = 1,
        Sell = 2,
    }

    public enum ShopPriceCurrency : byte
    {
        Gold = 1,
        Pearl = 2,
    }

    public sealed class ShopPriceRequest
    {
        public ShopPriceRequest(
            PlayerObject player,
            NPCObject npc,
            ShopPriceOperation operation,
            ShopPriceCurrency currency,
            UserItem item,
            ushort count,
            bool previewOnly,
            bool isUsedGoods,
            bool isBuyBack,
            float baseRate,
            float rate,
            int conquestTaxRatePercent,
            bool depositTaxToConquest)
        {
            Player = player ?? throw new ArgumentNullException(nameof(player));
            NPC = npc ?? throw new ArgumentNullException(nameof(npc));
            Operation = operation;
            Currency = currency;
            Item = item;
            Count = count;
            PreviewOnly = previewOnly;
            IsUsedGoods = isUsedGoods;
            IsBuyBack = isBuyBack;
            BaseRate = baseRate;
            Rate = rate;
            ConquestTaxRatePercent = conquestTaxRatePercent;
            DepositTaxToConquest = depositTaxToConquest;
        }

        public PlayerObject Player { get; }

        public NPCObject NPC { get; }

        public ShopPriceOperation Operation { get; }

        public ShopPriceCurrency Currency { get; }

        public UserItem Item { get; }

        public ushort Count { get; }

        public bool PreviewOnly { get; }

        public bool IsUsedGoods { get; }

        public bool IsBuyBack { get; }

        public float BaseRate { get; }

        public float Rate { get; set; }

        public int ConquestTaxRatePercent { get; set; }

        public bool DepositTaxToConquest { get; set; }

        public string FailMessage { get; set; } = string.Empty;

        public ScriptHookDecision Decision { get; set; } = ScriptHookDecision.Continue;

        public uint BaseItemPrice => Item?.Price() ?? 0U;

        public uint BaseTotalPrice => BaseItemPrice == 0 ? 0U : (uint)Math.Max(0, BaseItemPrice * BaseRate);

        public uint TotalPrice => BaseItemPrice == 0 ? 0U : (uint)Math.Max(0, BaseItemPrice * Rate);

        public uint TaxAmount
        {
            get
            {
                switch (Operation)
                {
                    case ShopPriceOperation.Buy:
                        return TotalPrice > BaseTotalPrice ? TotalPrice - BaseTotalPrice : 0U;
                    case ShopPriceOperation.Sell:
                        return BaseTotalPrice > TotalPrice ? BaseTotalPrice - TotalPrice : 0U;
                    default:
                        return 0U;
                }
            }
        }
    }

    public sealed class ShopPriceResult
    {
        public ShopPriceResult(
            PlayerObject player,
            NPCObject npc,
            ShopPriceOperation operation,
            ShopPriceCurrency currency,
            UserItem item,
            ushort count,
            bool previewOnly,
            bool isUsedGoods,
            bool isBuyBack,
            float baseRate,
            float rate,
            int conquestTaxRatePercent,
            bool depositTaxToConquest,
            bool executedLegacy,
            ScriptHookDecision decision)
        {
            Player = player ?? throw new ArgumentNullException(nameof(player));
            NPC = npc ?? throw new ArgumentNullException(nameof(npc));
            Operation = operation;
            Currency = currency;
            Item = item;
            Count = count;
            PreviewOnly = previewOnly;
            IsUsedGoods = isUsedGoods;
            IsBuyBack = isBuyBack;
            BaseRate = baseRate;
            Rate = rate;
            ConquestTaxRatePercent = conquestTaxRatePercent;
            DepositTaxToConquest = depositTaxToConquest;
            ExecutedLegacy = executedLegacy;
            Decision = decision;
        }

        public PlayerObject Player { get; }

        public NPCObject NPC { get; }

        public ShopPriceOperation Operation { get; }

        public ShopPriceCurrency Currency { get; }

        public UserItem Item { get; }

        public ushort Count { get; }

        public bool PreviewOnly { get; }

        public bool IsUsedGoods { get; }

        public bool IsBuyBack { get; }

        public float BaseRate { get; }

        public float Rate { get; }

        public int ConquestTaxRatePercent { get; }

        public bool DepositTaxToConquest { get; }

        public bool ExecutedLegacy { get; }

        public ScriptHookDecision Decision { get; }

        public uint BaseItemPrice => Item?.Price() ?? 0U;

        public uint BaseTotalPrice => BaseItemPrice == 0 ? 0U : (uint)Math.Max(0, BaseItemPrice * BaseRate);

        public uint TotalPrice => BaseItemPrice == 0 ? 0U : (uint)Math.Max(0, BaseItemPrice * Rate);

        public uint TaxAmount
        {
            get
            {
                switch (Operation)
                {
                    case ShopPriceOperation.Buy:
                        return TotalPrice > BaseTotalPrice ? TotalPrice - BaseTotalPrice : 0U;
                    case ShopPriceOperation.Sell:
                        return BaseTotalPrice > TotalPrice ? BaseTotalPrice - TotalPrice : 0U;
                    default:
                        return 0U;
                }
            }
        }
    }
}

