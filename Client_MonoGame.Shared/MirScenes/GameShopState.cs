using System.Collections.Generic;

namespace MonoShare.MirScenes
{
    public enum MobileShopPaymentOptions
    {
        None,
        CreditOnly,
        GoldOnly,
        CreditAndGold,
    }

    /// <summary>Pure selection rules shared by the mobile shop UI and regression tests.</summary>
    public static class MobileShopPurchasePolicy
    {
        public static MobileShopPaymentOptions GetPaymentOptions(GameShopItem item)
        {
            bool credit = item != null && item.CanBuyCredit && item.CreditPrice > 0;
            bool gold = item != null && item.CanBuyGold && item.GoldPrice > 0;

            if (credit && gold) return MobileShopPaymentOptions.CreditAndGold;
            if (credit) return MobileShopPaymentOptions.CreditOnly;
            if (gold) return MobileShopPaymentOptions.GoldOnly;
            return MobileShopPaymentOptions.None;
        }

        public static bool ShouldOpenPurchasePrompt(bool promptExists, bool promptDisposed)
        {
            return !promptExists || promptDisposed;
        }

        public static int GetLastPage(int itemCount, int pageSize)
        {
            if (itemCount <= 0 || pageSize <= 0)
                return 0;

            return (itemCount - 1) / pageSize;
        }

        public static int GetItemIndex(int page, int slot, int pageSize, int itemCount)
        {
            if (page < 0 || slot < 0 || pageSize <= 0 || slot >= pageSize || itemCount <= 0)
                return -1;

            long index = (long)page * pageSize + slot;
            return index < itemCount ? (int)index : -1;
        }
    }

    /// <summary>
    /// Main-thread state for the mobile GameShop view.
    ///
    /// The server sends one GameShopInfo packet per product and later sends
    /// GameShopStock deltas.  Keeping the packet merge/removal rules here
    /// prevents duplicate rows and gives the UI a small, testable seam.
    /// </summary>
    public sealed class GameShopState
    {
        private readonly List<GameShopItem> _items = new List<GameShopItem>();

        public IReadOnlyList<GameShopItem> Items => _items;

        public int Count => _items.Count;

        public GameShopItem this[int index] => _items[index];

        public bool ApplyInfo(GameShopItem item, int stockLevel)
        {
            if (item == null || item.Info == null || stockLevel < 0)
                return false;

            item.Stock = stockLevel;

            for (int i = 0; i < _items.Count; i++)
            {
                if (_items[i] == null || _items[i].GIndex != item.GIndex)
                    continue;

                _items[i] = item;
                return true;
            }

            _items.Add(item);
            return true;
        }

        public bool ApplyStock(int infoIndex, int stockLevel)
        {
            if (stockLevel < 0)
                return false;

            bool changed = false;

            for (int i = _items.Count - 1; i >= 0; i--)
            {
                GameShopItem item = _items[i];
                if (!MatchesInfoIndex(item, infoIndex))
                    continue;

                changed = true;
                if (stockLevel == 0)
                    _items.RemoveAt(i);
                else
                    item.Stock = stockLevel;
            }

            return changed;
        }

        public void Clear()
        {
            _items.Clear();
        }

        public void ResetForSession()
        {
            Clear();
        }

        private static bool MatchesInfoIndex(GameShopItem item, int infoIndex)
        {
            return item?.Info != null && item.Info.Index == infoIndex;
        }
    }
}
