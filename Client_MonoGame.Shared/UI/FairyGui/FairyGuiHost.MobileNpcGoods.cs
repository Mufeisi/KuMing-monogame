using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using C = ClientPackets;
using FairyGUI;
using Microsoft.Xna.Framework.Graphics;
using MonoShare.MirGraphics;
using MonoShare.MirScenes;

namespace MonoShare
{
    internal static partial class FairyGuiHost
    {
        private const string MobileNpcGoodsListConfigKey = "MobileNpcGoods.List";
        private const string MobileNpcGoodsBuyButtonConfigKey = "MobileNpcGoods.BuyButton";
        private const string MobileNpcGoodsSellButtonConfigKey = "MobileNpcGoods.SellButton";
        private const string MobileNpcGoodsInventoryGridConfigKey = "MobileNpcGoods.InventoryGrid";

        private static readonly string[] DefaultNpcGoodsListKeywords = { "goods", "shop", "list", "grid", "商品", "购买", "NpcGoods", "Goods", "Buy" };
        private static readonly string[] DefaultNpcGoodsBuyButtonKeywords = { "buy", "购买", "确定", "buybtn", "btnbuy" };
        private static readonly string[] DefaultNpcGoodsSellButtonKeywords = { "sell", "出售", "卖出", "sellbtn", "btnsell" };
        private static readonly string[] DefaultNpcGoodsInventoryGridKeywords = { "bag", "inventory", "sell", "背包", "物品", "格子", "出售" };

        private sealed class MobileNpcGoodsItemView
        {
            public int Index;
            public GComponent Root;
            public GLoader Icon;
            public GTextField Name;
            public GTextField Price;
            public GButton BuyButton;
            public EventCallback0 BuyCallback;

            public bool HasItem;
            public ushort LastIcon;
            public string LastName;
            public string LastPrice;
        }

        private sealed class MobileNpcGoodsWindowBinding
        {
            public string WindowKey;
            public GComponent Window;
            public string ResolveInfo;

            public bool UsePearls;
            public PanelType Mode;

            public GList GoodsList;
            public string GoodsListResolveInfo;
            public string GoodsListOverrideSpec;
            public string[] GoodsListOverrideKeywords;
            public ListItemRenderer GoodsItemRenderer;

            public GButton BuyButton;
            public string BuyButtonResolveInfo;
            public string BuyButtonOverrideSpec;
            public string[] BuyButtonOverrideKeywords;
            public EventCallback0 BuyClickCallback;

            public GButton SellButton;
            public string SellButtonResolveInfo;
            public string SellButtonOverrideSpec;
            public string[] SellButtonOverrideKeywords;
            public EventCallback0 SellClickCallback;

            public MobileItemGridBinding InventoryGrid;
            public int SelectedInventoryIndex = -1;
        }

        private static MobileNpcGoodsWindowBinding _mobileNpcGoodsBinding;
        private static DateTime _nextMobileNpcGoodsBindAttemptUtc = DateTime.MinValue;
        private static bool _mobileNpcGoodsBindingsDumped;
        private static bool _mobileNpcGoodsDirty;

        private static readonly List<UserItem> MobileNpcGoodsItems = new List<UserItem>(256);
        private static PanelType _mobileNpcGoodsMode = PanelType.Buy;
        private static bool _mobileNpcGoodsUsePearls;

        public static void UpdateMobileNpcGoods(IList<UserItem> goods, float rate, PanelType type, bool usePearls)
        {
            _mobileNpcGoodsMode = type;
            if (_mobileNpcGoodsMode != PanelType.Buy &&
                _mobileNpcGoodsMode != PanelType.BuySub &&
                _mobileNpcGoodsMode != PanelType.Craft)
            {
                _mobileNpcGoodsMode = PanelType.Buy;
            }
            _mobileNpcGoodsUsePearls = usePearls;

            try
            {
                MobileNpcGoodsItems.Clear();
                if (goods != null && goods.Count > 0)
                    MobileNpcGoodsItems.AddRange(goods);
            }
            catch
            {
                MobileNpcGoodsItems.Clear();
            }

            MarkMobileNpcGoodsDirty();
        }

        public static void BeginMobileNpcSell(float rate)
        {
            _mobileNpcGoodsMode = PanelType.Sell;
            _mobileNpcGoodsUsePearls = false;
            MarkMobileNpcGoodsDirty();
        }

        public static void MarkMobileNpcGoodsDirty()
        {
            try
            {
                _mobileNpcGoodsDirty = true;
            }
            catch
            {
            }

            TryRefreshMobileNpcGoodsIfDue(force: false);
        }

        private static void ResetMobileNpcGoodsBindings()
        {
            try
            {
                MobileNpcGoodsWindowBinding binding = _mobileNpcGoodsBinding;
                if (binding != null)
                {
                    try
                    {
                        if (binding.GoodsList != null && !binding.GoodsList._disposed && binding.GoodsItemRenderer != null)
                            binding.GoodsList.itemRenderer = null;
                    }
                    catch
                    {
                    }

                    try
                    {
                        if (binding.BuyButton != null && !binding.BuyButton._disposed && binding.BuyClickCallback != null)
                            binding.BuyButton.onClick.Remove(binding.BuyClickCallback);
                    }
                    catch
                    {
                    }

                    try
                    {
                        if (binding.SellButton != null && !binding.SellButton._disposed && binding.SellClickCallback != null)
                            binding.SellButton.onClick.Remove(binding.SellClickCallback);
                    }
                    catch
                    {
                    }

                    try
                    {
                        DetachMobileItemGridSlotCallbacks(binding.InventoryGrid);
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }

            _mobileNpcGoodsBinding = null;
            _nextMobileNpcGoodsBindAttemptUtc = DateTime.MinValue;
            _mobileNpcGoodsBindingsDumped = false;
            _mobileNpcGoodsDirty = false;
        }

        private static void TryBindMobileNpcGoodsWindowIfDue(string windowKey, GComponent window, string resolveInfo)
        {
            if (window == null || window._disposed)
                return;

            MobileNpcGoodsWindowBinding binding = _mobileNpcGoodsBinding;
            if (binding != null && (binding.Window == null || binding.Window._disposed))
            {
                ResetMobileNpcGoodsBindings();
                binding = null;
            }

            if (binding == null || !ReferenceEquals(binding.Window, window))
            {
                ResetMobileNpcGoodsBindings();

                binding = new MobileNpcGoodsWindowBinding
                {
                    WindowKey = windowKey,
                    Window = window,
                    ResolveInfo = resolveInfo,
                };

                _mobileNpcGoodsBinding = binding;
                _mobileNpcGoodsBindingsDumped = false;
                _nextMobileNpcGoodsBindAttemptUtc = DateTime.MinValue;
            }

            if (DateTime.UtcNow < _nextMobileNpcGoodsBindAttemptUtc)
                return;

            bool listBound = binding.GoodsList != null && !binding.GoodsList._disposed && binding.GoodsItemRenderer != null;
            bool inventoryGridBound = binding.InventoryGrid != null && binding.InventoryGrid.GridRoot != null && !binding.InventoryGrid.GridRoot._disposed && binding.InventoryGrid.Slots.Count > 0;
            bool buyBound = binding.BuyButton != null && !binding.BuyButton._disposed && binding.BuyClickCallback != null;
            bool sellBound = binding.SellButton != null && !binding.SellButton._disposed && binding.SellClickCallback != null;

            if (listBound && inventoryGridBound && buyBound && sellBound)
                return;

            _nextMobileNpcGoodsBindAttemptUtc = DateTime.UtcNow.AddSeconds(2);

            string listSpec = string.Empty;
            string buyButtonSpec = string.Empty;
            string sellButtonSpec = string.Empty;
            string inventoryGridSpec = string.Empty;

            try
            {
                InIReader reader = TryCreateConfigReader();
                if (reader != null)
                {
                    listSpec = reader.ReadString(FairyGuiConfigSectionName, MobileNpcGoodsListConfigKey, string.Empty, writeWhenNull: false);
                    buyButtonSpec = reader.ReadString(FairyGuiConfigSectionName, MobileNpcGoodsBuyButtonConfigKey, string.Empty, writeWhenNull: false);
                    sellButtonSpec = reader.ReadString(FairyGuiConfigSectionName, MobileNpcGoodsSellButtonConfigKey, string.Empty, writeWhenNull: false);
                    inventoryGridSpec = reader.ReadString(FairyGuiConfigSectionName, MobileNpcGoodsInventoryGridConfigKey, string.Empty, writeWhenNull: false);
                }
            }
            catch
            {
                listSpec = string.Empty;
                buyButtonSpec = string.Empty;
                sellButtonSpec = string.Empty;
                inventoryGridSpec = string.Empty;
            }

            listSpec = listSpec?.Trim() ?? string.Empty;
            buyButtonSpec = buyButtonSpec?.Trim() ?? string.Empty;
            sellButtonSpec = sellButtonSpec?.Trim() ?? string.Empty;
            inventoryGridSpec = inventoryGridSpec?.Trim() ?? string.Empty;

            binding.Mode = _mobileNpcGoodsMode;
            binding.UsePearls = _mobileNpcGoodsUsePearls;

            List<(int Score, GObject Target)> listCandidates = null;
            List<(int Score, GObject Target)> inventoryGridCandidates = null;

            if (!listBound)
            {
                binding.GoodsListOverrideSpec = listSpec;
                binding.GoodsListOverrideKeywords = null;

                GList list = null;
                string listResolveInfo = null;

                if (!string.IsNullOrWhiteSpace(listSpec) && TryResolveMobileMainHudObjectBySpec(window, listSpec, out GObject resolved, out string[] keywords))
                {
                    if (resolved is GList resolvedList && !resolvedList._disposed)
                    {
                        list = resolvedList;
                        listResolveInfo = DescribeObject(window, resolvedList) + " (override)";
                    }
                    else if (keywords != null && keywords.Length > 0)
                    {
                        binding.GoodsListOverrideKeywords = keywords;
                    }
                }
                else if (!string.IsNullOrWhiteSpace(listSpec))
                {
                    binding.GoodsListOverrideKeywords = SplitKeywords(listSpec);
                }

                string[] keywordsUsed = binding.GoodsListOverrideKeywords != null && binding.GoodsListOverrideKeywords.Length > 0
                    ? binding.GoodsListOverrideKeywords
                    : DefaultNpcGoodsListKeywords;

                if (list == null)
                {
                    int minScore = binding.GoodsListOverrideKeywords != null && binding.GoodsListOverrideKeywords.Length > 0 ? 40 : 60;
                    listCandidates = CollectMobileChatCandidates(window, obj => obj is GList && obj.touchable, keywordsUsed, ScoreMobileShopListCandidate);
                    list = SelectMobileChatCandidate<GList>(listCandidates, minScore);
                    if (list != null && !list._disposed)
                        listResolveInfo = DescribeObject(window, list) + (binding.GoodsListOverrideKeywords != null && binding.GoodsListOverrideKeywords.Length > 0 ? " (keywords)" : " (auto)");
                }

                binding.GoodsList = list;
                binding.GoodsListResolveInfo = listResolveInfo;

                if (binding.GoodsItemRenderer == null)
                    binding.GoodsItemRenderer = RenderMobileNpcGoodsListItem;

                try
                {
                    if (binding.GoodsList != null && !binding.GoodsList._disposed)
                        binding.GoodsList.itemRenderer = binding.GoodsItemRenderer;
                }
                catch
                {
                }
            }

            if (!inventoryGridBound)
            {
                int desiredSlots = GameScene.User?.Inventory?.Length ?? 46;

                string gridResolveInfo;
                string[] overrideKeywords;

                var used = new HashSet<GObject>();

                GComponent gridRoot = ResolveTradeGridRoot(
                    window,
                    inventoryGridSpec,
                    DefaultNpcGoodsInventoryGridKeywords,
                    used,
                    minScore: 60,
                    minSlots: Math.Max(10, desiredSlots / 2),
                    out gridResolveInfo,
                    out overrideKeywords,
                    out inventoryGridCandidates);

                if (gridRoot != null && !gridRoot._disposed)
                {
                    if (binding.InventoryGrid != null)
                        DetachMobileItemGridSlotCallbacks(binding.InventoryGrid);

                    binding.InventoryGrid = CreateMobileNpcGoodsInventoryGridBinding(
                        windowKey,
                        window,
                        resolveInfo,
                        gridRoot,
                        gridResolveInfo,
                        inventoryGridSpec,
                        overrideKeywords,
                        desiredSlots);
                }
            }

            if (!buyBound)
            {
                binding.BuyButtonOverrideSpec = buyButtonSpec;
                binding.BuyButtonOverrideKeywords = null;

                GButton button = null;
                string buttonResolveInfo = null;

                if (!string.IsNullOrWhiteSpace(buyButtonSpec) && TryResolveMobileMainHudObjectBySpec(window, buyButtonSpec, out GObject resolved, out string[] keywords))
                {
                    if (resolved is GButton resolvedButton && !resolvedButton._disposed)
                    {
                        button = resolvedButton;
                        buttonResolveInfo = DescribeObject(window, resolvedButton) + " (override)";
                    }
                    else if (keywords != null && keywords.Length > 0)
                    {
                        binding.BuyButtonOverrideKeywords = keywords;
                    }
                }
                else if (!string.IsNullOrWhiteSpace(buyButtonSpec))
                {
                    binding.BuyButtonOverrideKeywords = SplitKeywords(buyButtonSpec);
                }

                string[] keywordsUsed = binding.BuyButtonOverrideKeywords != null && binding.BuyButtonOverrideKeywords.Length > 0
                    ? binding.BuyButtonOverrideKeywords
                    : DefaultNpcGoodsBuyButtonKeywords;

                if (button == null)
                {
                    int minScore = binding.BuyButtonOverrideKeywords != null && binding.BuyButtonOverrideKeywords.Length > 0 ? 30 : 45;
                    List<(int Score, GObject Target)> candidates = CollectMobileChatCandidates(window, obj => obj is GButton && obj.touchable, keywordsUsed, ScoreMobileShopButtonCandidate);
                    button = SelectMobileChatCandidate<GButton>(candidates, minScore);
                    if (button != null && !button._disposed)
                        buttonResolveInfo = DescribeObject(window, button) + (binding.BuyButtonOverrideKeywords != null && binding.BuyButtonOverrideKeywords.Length > 0 ? " (keywords)" : " (auto)");
                }

                binding.BuyButton = button;
                binding.BuyButtonResolveInfo = buttonResolveInfo;

                try
                {
                    if (binding.BuyButton != null && !binding.BuyButton._disposed)
                    {
                        if (binding.BuyClickCallback == null)
                            binding.BuyClickCallback = OnMobileNpcGoodsBuyClicked;

                        binding.BuyButton.onClick.Remove(binding.BuyClickCallback);
                        binding.BuyButton.onClick.Add(binding.BuyClickCallback);
                    }
                }
                catch
                {
                }
            }

            if (!sellBound)
            {
                binding.SellButtonOverrideSpec = sellButtonSpec;
                binding.SellButtonOverrideKeywords = null;

                GButton button = null;
                string buttonResolveInfo = null;

                if (!string.IsNullOrWhiteSpace(sellButtonSpec) && TryResolveMobileMainHudObjectBySpec(window, sellButtonSpec, out GObject resolved, out string[] keywords))
                {
                    if (resolved is GButton resolvedButton && !resolvedButton._disposed)
                    {
                        button = resolvedButton;
                        buttonResolveInfo = DescribeObject(window, resolvedButton) + " (override)";
                    }
                    else if (keywords != null && keywords.Length > 0)
                    {
                        binding.SellButtonOverrideKeywords = keywords;
                    }
                }
                else if (!string.IsNullOrWhiteSpace(sellButtonSpec))
                {
                    binding.SellButtonOverrideKeywords = SplitKeywords(sellButtonSpec);
                }

                string[] keywordsUsed = binding.SellButtonOverrideKeywords != null && binding.SellButtonOverrideKeywords.Length > 0
                    ? binding.SellButtonOverrideKeywords
                    : DefaultNpcGoodsSellButtonKeywords;

                if (button == null)
                {
                    int minScore = binding.SellButtonOverrideKeywords != null && binding.SellButtonOverrideKeywords.Length > 0 ? 30 : 45;
                    List<(int Score, GObject Target)> candidates = CollectMobileChatCandidates(window, obj => obj is GButton && obj.touchable, keywordsUsed, ScoreMobileShopButtonCandidate);
                    button = SelectMobileChatCandidate<GButton>(candidates, minScore);
                    if (button != null && !button._disposed)
                        buttonResolveInfo = DescribeObject(window, button) + (binding.SellButtonOverrideKeywords != null && binding.SellButtonOverrideKeywords.Length > 0 ? " (keywords)" : " (auto)");
                }

                binding.SellButton = button;
                binding.SellButtonResolveInfo = buttonResolveInfo;

                try
                {
                    if (binding.SellButton != null && !binding.SellButton._disposed)
                    {
                        if (binding.SellClickCallback == null)
                            binding.SellClickCallback = OnMobileNpcGoodsSellClicked;

                        binding.SellButton.onClick.Remove(binding.SellClickCallback);
                        binding.SellButton.onClick.Add(binding.SellClickCallback);
                    }
                }
                catch
                {
                }
            }

            TryDumpMobileNpcGoodsBindingsReportIfDue(binding, listCandidates, inventoryGridCandidates);
            _mobileNpcGoodsDirty = true;
        }

        private static void TryRefreshMobileNpcGoodsIfDue(bool force)
        {
            if (_stage == null || !_initialized || !_packagesLoaded)
                return;

            if (!MobileWindows.TryGetValue("NpcGoods", out GComponent window) || window == null || window._disposed)
            {
                if (_mobileNpcGoodsBinding != null)
                    ResetMobileNpcGoodsBindings();
                return;
            }

            if (!window.visible)
                return;

            TryBindMobileNpcGoodsWindowIfDue("NpcGoods", window, resolveInfo: null);

            MobileNpcGoodsWindowBinding binding = _mobileNpcGoodsBinding;
            if (binding == null || binding.Window == null || binding.Window._disposed)
            {
                ResetMobileNpcGoodsBindings();
                return;
            }

            binding.Mode = _mobileNpcGoodsMode;
            binding.UsePearls = _mobileNpcGoodsUsePearls;

            if (!force && !_mobileNpcGoodsDirty)
                return;

            _mobileNpcGoodsDirty = false;

            try
            {
                if (binding.GoodsList != null && !binding.GoodsList._disposed)
                {
                    bool showGoods = binding.Mode != PanelType.Sell;
                    binding.GoodsList.visible = showGoods;
                    binding.GoodsList.touchable = showGoods;
                }
            }
            catch
            {
            }

            try
            {
                if (binding.InventoryGrid?.GridRoot != null && !binding.InventoryGrid.GridRoot._disposed)
                {
                    bool showBag = binding.Mode == PanelType.Sell;
                    binding.InventoryGrid.GridRoot.visible = showBag;
                    binding.InventoryGrid.GridRoot.touchable = showBag;
                }
            }
            catch
            {
            }

            if (binding.Mode == PanelType.Sell)
            {
                var user = GameScene.User;
                if (user == null || user.Inventory == null)
                    return;

                if (binding.InventoryGrid == null || binding.InventoryGrid.Slots.Count == 0)
                    return;

                if (binding.SelectedInventoryIndex >= 0)
                {
                    if (binding.SelectedInventoryIndex >= user.Inventory.Length || user.Inventory[binding.SelectedInventoryIndex] == null)
                        binding.SelectedInventoryIndex = -1;
                }

                RefreshMobileItemGridSlots(binding.InventoryGrid, user.Inventory, out bool invalidated);
                if (invalidated)
                {
                    _nextMobileNpcGoodsBindAttemptUtc = DateTime.MinValue;
                    _mobileNpcGoodsDirty = true;
                    return;
                }

                ApplyMobileNpcGoodsInventorySelectionVisuals(binding);
            }
            else
            {
                if (binding.GoodsList == null || binding.GoodsList._disposed)
                    return;

                int count = 0;
                try
                {
                    count = MobileNpcGoodsItems.Count;
                }
                catch
                {
                    count = 0;
                }

                try
                {
                    if (binding.GoodsItemRenderer == null)
                        binding.GoodsItemRenderer = RenderMobileNpcGoodsListItem;

                    binding.GoodsList.itemRenderer = binding.GoodsItemRenderer;
                    binding.GoodsList.numItems = count;
                }
                catch (Exception ex)
                {
                    CMain.SaveError("FairyGUI: 刷新 NPC 商品窗口失败：" + ex.Message);
                    _nextMobileNpcGoodsBindAttemptUtc = DateTime.MinValue;
                    _mobileNpcGoodsDirty = true;
                }
            }

            UpdateMobileNpcGoodsButtons(binding);
        }

        private static void UpdateMobileNpcGoodsButtons(MobileNpcGoodsWindowBinding binding)
        {
            if (binding == null)
                return;

            try
            {
                if (binding.BuyButton != null && !binding.BuyButton._disposed)
                {
                    bool canBuy = binding.Mode != PanelType.Sell && binding.GoodsList != null && !binding.GoodsList._disposed && binding.GoodsList.selectedIndex >= 0;
                    binding.BuyButton.grayed = !canBuy;
                    binding.BuyButton.touchable = canBuy;
                    binding.BuyButton.visible = binding.Mode != PanelType.Sell;
                }
            }
            catch
            {
            }

            try
            {
                if (binding.SellButton != null && !binding.SellButton._disposed)
                {
                    bool canSell = binding.Mode == PanelType.Sell && binding.SelectedInventoryIndex >= 0;
                    binding.SellButton.grayed = !canSell;
                    binding.SellButton.touchable = canSell;
                    binding.SellButton.visible = binding.Mode == PanelType.Sell;
                }
            }
            catch
            {
            }
        }

        private static void ApplyMobileNpcGoodsInventorySelectionVisuals(MobileNpcGoodsWindowBinding binding)
        {
            if (binding == null || binding.InventoryGrid == null)
                return;

            bool hasSelection = binding.SelectedInventoryIndex >= 0;
            int selectedIndex = binding.SelectedInventoryIndex;

            for (int i = 0; i < binding.InventoryGrid.Slots.Count; i++)
            {
                MobileItemSlotBinding slot = binding.InventoryGrid.Slots[i];
                if (slot == null || slot.Root == null || slot.Root._disposed)
                    continue;

                try
                {
                    if (!slot.OriginalAlphaCaptured)
                    {
                        slot.OriginalAlpha = slot.Root.alpha;
                        slot.OriginalAlphaCaptured = true;
                    }

                    if (!hasSelection)
                    {
                        slot.Root.alpha = slot.OriginalAlpha;
                        continue;
                    }

                    bool isSelected = slot.SlotIndex == selectedIndex;
                    slot.Root.alpha = isSelected ? slot.OriginalAlpha : Math.Max(0.15f, slot.OriginalAlpha * 0.6f);
                }
                catch
                {
                }
            }
        }

        private static MobileItemGridBinding CreateMobileNpcGoodsInventoryGridBinding(
            string windowKey,
            GComponent window,
            string windowResolveInfo,
            GComponent gridRoot,
            string gridResolveInfo,
            string overrideSpec,
            string[] overrideKeywords,
            int desiredSlots)
        {
            if (gridRoot == null || gridRoot._disposed)
                return null;

            var gridBinding = new MobileItemGridBinding
            {
                WindowKey = windowKey + ".Inventory",
                Window = window,
                GridRoot = gridRoot,
                ResolveInfo = windowResolveInfo,
                GridResolveInfo = gridResolveInfo,
                OverrideSpec = overrideSpec,
                OverrideKeywords = overrideKeywords,
            };

            List<GComponent> slotCandidates = CollectInventorySlotCandidates(gridRoot);
            if (slotCandidates.Count == 0)
                return gridBinding;

            SortGComponentsByGlobalPosition(slotCandidates);

            int slotCount = Math.Min(desiredSlots, slotCandidates.Count);
            gridBinding.Slots.Clear();

            for (int i = 0; i < slotCount; i++)
            {
                int slotIndex = i;
                GComponent slotRoot = slotCandidates[i];
                var slot = new MobileItemSlotBinding
                {
                    SlotIndex = slotIndex,
                    Root = slotRoot,
                    Icon = FindBestInventorySlotIcon(slotRoot),
                    IconImage = FindBestInventorySlotIconImage(slotRoot),
                    Count = FindBestInventorySlotCount(slotRoot),
                    HasItem = false,
                    LastIcon = 0,
                    LastCountDisplayed = 0,
                };

                try
                {
                    EventCallback0 callback = () => OnMobileNpcGoodsInventorySlotClicked(slotIndex);
                    slot.ClickCallback = callback;
                    slotRoot.onClick.Add(callback);
                }
                catch
                {
                }

                gridBinding.Slots.Add(slot);
            }

            return gridBinding;
        }

        private static void OnMobileNpcGoodsInventorySlotClicked(int slotIndex)
        {
            MobileNpcGoodsWindowBinding binding = _mobileNpcGoodsBinding;
            if (binding == null)
                return;

            if (slotIndex < 0)
                return;

            var user = GameScene.User;
            if (user == null || user.Inventory == null)
                return;

            if (slotIndex >= user.Inventory.Length || user.Inventory[slotIndex] == null)
            {
                binding.SelectedInventoryIndex = -1;
            }
            else
            {
                binding.SelectedInventoryIndex = slotIndex;
            }

            _mobileNpcGoodsDirty = true;
        }

        private static void OnMobileNpcGoodsBuyClicked()
        {
            MobileNpcGoodsWindowBinding binding = _mobileNpcGoodsBinding;
            if (binding == null)
                return;

            if (binding.Mode == PanelType.Sell)
                return;

            int selectedIndex = -1;
            try
            {
                if (binding.GoodsList != null && !binding.GoodsList._disposed)
                    selectedIndex = binding.GoodsList.selectedIndex;
            }
            catch
            {
                selectedIndex = -1;
            }

            if (!TryGetNpcGoodsItemByIndex(selectedIndex, out UserItem item))
            {
                GameScene.Scene?.MobileReceiveChat("[NPC] 请先选择要购买的商品。", ChatType.Hint);
                return;
            }

            TryBuyNpcGoodsItem(item);
        }

        private static void OnMobileNpcGoodsSellClicked()
        {
            MobileNpcGoodsWindowBinding binding = _mobileNpcGoodsBinding;
            if (binding == null)
                return;

            if (binding.Mode != PanelType.Sell)
                return;

            var user = GameScene.User;
            if (user == null || user.Inventory == null)
                return;

            int slotIndex = binding.SelectedInventoryIndex;
            if (slotIndex < 0 || slotIndex >= user.Inventory.Length)
            {
                GameScene.Scene?.MobileReceiveChat("[NPC] 请先选择要出售的物品。", ChatType.Hint);
                return;
            }

            UserItem item = user.Inventory[slotIndex];
            if (item == null || item.Info == null)
            {
                GameScene.Scene?.MobileReceiveChat("[NPC] 请先选择要出售的物品。", ChatType.Hint);
                binding.SelectedInventoryIndex = -1;
                _mobileNpcGoodsDirty = true;
                return;
            }

            if (item.Info.Bind.HasFlag(BindMode.DontSell))
            {
                GameScene.Scene?.MobileReceiveChat("[NPC] 不能卖出物品。", ChatType.System);
                return;
            }

            if (item.Info.StackSize > 1 && item.Count > 1)
            {
                ushort maxQuantity = item.Count;

                GameScene.Scene?.PromptMobileText(
                    title: "出售数量",
                    message: $"请输入出售数量（1-{maxQuantity}）",
                    initialText: maxQuantity.ToString(),
                    maxLength: 5,
                    numericOnly: true,
                    onOk: text =>
                    {
                        if (!ushort.TryParse((text ?? string.Empty).Trim(), out ushort quantity) || quantity == 0)
                            return;

                        if (quantity > maxQuantity)
                            quantity = maxQuantity;

                        uint baseTotal = item.Price();
                        uint unit = maxQuantity > 0 ? baseTotal / maxQuantity : baseTotal;
                        ulong unitTotal = (ulong)unit * quantity;
                        uint sellTotal = (uint)Math.Min(uint.MaxValue, unitTotal * (double)GameScene.NPCSellRate);

                        if ((ulong)GameScene.Gold + sellTotal > uint.MaxValue)
                        {
                            GameScene.Scene?.MobileReceiveChat("[NPC] 金币已达携带上限。", ChatType.System);
                            return;
                        }

                        MonoShare.MirNetwork.Network.Enqueue(new C.SellItem
                        {
                            UniqueID = item.UniqueID,
                            Count = quantity,
                        });

                        binding.SelectedInventoryIndex = -1;
                        _mobileNpcGoodsDirty = true;
                    },
                    onCancel: null);

                return;
            }

            uint price = (uint)(item.Price() * GameScene.NPCSellRate);
            if ((ulong)GameScene.Gold + price > uint.MaxValue)
            {
                GameScene.Scene?.MobileReceiveChat("[NPC] 金币已达携带上限。", ChatType.System);
                return;
            }

            MonoShare.MirNetwork.Network.Enqueue(new C.SellItem
            {
                UniqueID = item.UniqueID,
                Count = item.Count,
            });

            binding.SelectedInventoryIndex = -1;
            _mobileNpcGoodsDirty = true;
        }

        private static void RenderMobileNpcGoodsListItem(int index, GObject obj)
        {
            if (obj is not GComponent itemRoot || itemRoot._disposed)
                return;

            if (!TryGetNpcGoodsItemByIndex(index, out UserItem item))
            {
                try
                {
                    MobileNpcGoodsItemView view = itemRoot.data as MobileNpcGoodsItemView;
                    if (view != null)
                        ClearMobileNpcGoodsItemView(view);
                }
                catch
                {
                }

                return;
            }

            MobileNpcGoodsItemView v = GetOrCreateMobileNpcGoodsItemView(itemRoot);
            if (v == null)
                return;

            v.Index = index;

            if (item == null || item.Info == null)
            {
                ClearMobileNpcGoodsItemView(v);
                return;
            }

            string nameText = item.FriendlyName ?? item.Info.FriendlyName ?? string.Empty;
            uint price = (uint)(item.Price() * GameScene.NPCRate);
            string priceText = price.ToString();

            try
            {
                if (v.Name != null && !v.Name._disposed && !string.Equals(v.LastName, nameText, StringComparison.Ordinal))
                {
                    v.Name.text = nameText;
                    v.LastName = nameText;
                }
            }
            catch
            {
            }

            try
            {
                if (v.Price != null && !v.Price._disposed && !string.Equals(v.LastPrice, priceText, StringComparison.Ordinal))
                {
                    v.Price.text = priceText;
                    v.LastPrice = priceText;
                }
            }
            catch
            {
            }

            ushort iconIndex = item.Image;

            bool needsIconRefresh = !v.HasItem || v.LastIcon != iconIndex;
            if (!needsIconRefresh && v.Icon != null && !v.Icon._disposed)
            {
                try
                {
                    NTexture current = v.Icon.texture;
                    Texture2D native = current?.nativeTexture;
                    if (current == null || native == null || native.IsDisposed)
                        needsIconRefresh = true;
                }
                catch
                {
                    needsIconRefresh = true;
                }
            }

            if (needsIconRefresh && v.Icon != null && !v.Icon._disposed)
            {
                try
                {
                    Libraries.Items.Touch(iconIndex);
                    v.Icon.showErrorSign = false;
                    v.Icon.url = string.Empty;
                    v.Icon.texture = GetOrCreateItemIconTexture(iconIndex);
                    v.LastIcon = iconIndex;
                }
                catch
                {
                }
            }

            v.HasItem = true;

            bool canBuy = _mobileNpcGoodsMode != PanelType.Sell;
            if (v.BuyButton != null && !v.BuyButton._disposed)
            {
                try
                {
                    v.BuyButton.visible = canBuy;
                    v.BuyButton.touchable = canBuy;

                    if (v.BuyCallback != null)
                        v.BuyButton.onClick.Remove(v.BuyCallback);

                    if (canBuy)
                    {
                        int itemIndex = index;
                        v.BuyCallback = () =>
                        {
                            if (TryGetNpcGoodsItemByIndex(itemIndex, out UserItem currentItem))
                                TryBuyNpcGoodsItem(currentItem);
                        };
                        v.BuyButton.onClick.Add(v.BuyCallback);
                    }
                    else
                    {
                        v.BuyCallback = null;
                    }
                }
                catch
                {
                }
            }
        }

        private static MobileNpcGoodsItemView GetOrCreateMobileNpcGoodsItemView(GComponent itemRoot)
        {
            if (itemRoot == null || itemRoot._disposed)
                return null;

            try
            {
                MobileNpcGoodsItemView cached = itemRoot.data as MobileNpcGoodsItemView;
                if (cached != null && cached.Root != null && !cached.Root._disposed && ReferenceEquals(cached.Root, itemRoot))
                    return cached;
            }
            catch
            {
            }

            var view = new MobileNpcGoodsItemView
            {
                Root = itemRoot,
                Index = -1,
                HasItem = false,
                LastIcon = 0,
                LastName = null,
                LastPrice = null,
            };

            try
            {
                // Icon
                {
                    List<(int Score, GObject Target)> candidates = CollectMobileChatCandidates(itemRoot, obj => obj is GLoader, DefaultShopItemIconKeywords, ScoreMobileShopTextCandidate);
                    view.Icon = SelectMobileChatCandidate<GLoader>(candidates, minScore: 10);
                }

                // Name
                {
                    List<(int Score, GObject Target)> candidates = CollectMobileChatCandidates(itemRoot, obj => obj is GTextField && obj is not GTextInput, DefaultShopItemNameKeywords, ScoreMobileShopTextCandidate);
                    view.Name = SelectMobileChatCandidate<GTextField>(candidates, minScore: 20);
                }

                // Price（优先 Gold/Credit，其次兜底 Price）
                {
                    GTextField price = null;

                    List<(int Score, GObject Target)> goldCandidates = CollectMobileChatCandidates(itemRoot, obj => obj is GTextField && obj is not GTextInput, DefaultShopItemPriceGoldKeywords, ScoreMobileShopTextCandidate);
                    GTextField gold = SelectMobileChatCandidate<GTextField>(goldCandidates, minScore: 20);

                    List<(int Score, GObject Target)> creditCandidates = CollectMobileChatCandidates(itemRoot, obj => obj is GTextField && obj is not GTextInput, DefaultShopItemPriceCreditKeywords, ScoreMobileShopTextCandidate);
                    GTextField credit = SelectMobileChatCandidate<GTextField>(creditCandidates, minScore: 20);

                    if (_mobileNpcGoodsUsePearls)
                        price = credit ?? gold;
                    else
                        price = gold ?? credit;

                    if (price == null)
                    {
                        string[] priceKeywords = { "price", "cost", "gold", "credit", "pearl", "金额", "价格", "金币", "元宝" };
                        List<(int Score, GObject Target)> candidates = CollectMobileChatCandidates(itemRoot, obj => obj is GTextField && obj is not GTextInput, priceKeywords, ScoreMobileShopTextCandidate);
                        price = SelectMobileChatCandidate<GTextField>(candidates, minScore: 10);
                    }

                    view.Price = price;
                }

                // Buy button（可选）
                {
                    List<(int Score, GObject Target)> candidates = CollectMobileChatCandidates(itemRoot, obj => obj is GButton && obj.touchable, DefaultShopBuyKeywords, ScoreMobileShopButtonCandidate);
                    view.BuyButton = SelectMobileChatCandidate<GButton>(candidates, minScore: 25);
                }
            }
            catch
            {
            }

            try
            {
                itemRoot.data = view;
            }
            catch
            {
            }

            return view;
        }

        private static void ClearMobileNpcGoodsItemView(MobileNpcGoodsItemView view)
        {
            if (view == null)
                return;

            try
            {
                if (view.Name != null && !view.Name._disposed)
                    view.Name.text = string.Empty;
            }
            catch
            {
            }

            try
            {
                if (view.Price != null && !view.Price._disposed)
                    view.Price.text = string.Empty;
            }
            catch
            {
            }

            try
            {
                if (view.Icon != null && !view.Icon._disposed)
                {
                    view.Icon.showErrorSign = false;
                    view.Icon.url = string.Empty;
                    view.Icon.texture = null;
                }
            }
            catch
            {
            }

            try
            {
                if (view.BuyButton != null && !view.BuyButton._disposed)
                {
                    if (view.BuyCallback != null)
                        view.BuyButton.onClick.Remove(view.BuyCallback);

                    view.BuyCallback = null;
                    view.BuyButton.visible = false;
                    view.BuyButton.touchable = false;
                }
            }
            catch
            {
            }

            view.HasItem = false;
            view.LastIcon = 0;
            view.LastName = null;
            view.LastPrice = null;
        }

        private static bool TryGetNpcGoodsItemByIndex(int index, out UserItem item)
        {
            item = null;

            if (index < 0)
                return false;

            try
            {
                if (index >= MobileNpcGoodsItems.Count)
                    return false;

                item = MobileNpcGoodsItems[index];
                return item != null && item.Info != null;
            }
            catch
            {
                item = null;
                return false;
            }
        }

        private static void TryBuyNpcGoodsItem(UserItem item)
        {
            if (item == null || item.Info == null)
                return;

            var user = GameScene.User;
            if (user == null)
                return;

            bool usePearls = _mobileNpcGoodsUsePearls;

            uint singlePrice = (uint)(item.Price() * GameScene.NPCRate);
            uint available = usePearls ? (uint)Math.Max(0, user.PearlCount) : GameScene.Gold;
            if (singlePrice > available)
            {
                GameScene.Scene?.MobileReceiveChat(usePearls ? "[NPC] 没有足够的珍珠。" : "[NPC] 金币不足。", ChatType.System);
                return;
            }

            if (item.Info.StackSize > 1)
            {
                uint unitPrice;
                try
                {
                    uint total = item.Price();
                    ushort baseCount = Math.Max((ushort)1, item.Count);
                    unitPrice = Math.Max(1U, (uint)Math.Ceiling((total / (double)baseCount) * GameScene.NPCRate));
                }
                catch
                {
                    unitPrice = Math.Max(1U, singlePrice);
                }

                ushort maxQuantity = item.Info.StackSize;
                if (unitPrice > 0)
                {
                    uint byFunds = available / unitPrice;
                    if (byFunds < maxQuantity)
                        maxQuantity = (ushort)Math.Max(0, Math.Min(ushort.MaxValue, byFunds));
                }

                if (maxQuantity <= 0)
                {
                    GameScene.Scene?.MobileReceiveChat(usePearls ? "[NPC] 没有足够的珍珠。" : "[NPC] 金币不足。", ChatType.System);
                    return;
                }

                GameScene.Scene?.PromptMobileText(
                    title: "购买数量",
                    message: $"请输入购买数量（1-{maxQuantity}）",
                    initialText: "1",
                    maxLength: 5,
                    numericOnly: true,
                    onOk: text =>
                    {
                        if (!ushort.TryParse((text ?? string.Empty).Trim(), out ushort quantity) || quantity == 0)
                            return;

                        if (quantity > maxQuantity)
                            quantity = maxQuantity;

                        MonoShare.MirNetwork.Network.Enqueue(new C.BuyItem
                        {
                            ItemIndex = item.UniqueID,
                            Count = quantity,
                            Type = PanelType.Buy,
                        });
                    },
                    onCancel: null);

                return;
            }

            // 非叠加：直接买 1 个
            MonoShare.MirNetwork.Network.Enqueue(new C.BuyItem
            {
                ItemIndex = item.UniqueID,
                Count = 1,
                Type = PanelType.Buy,
            });
        }

        private static void TryDumpMobileNpcGoodsBindingsReportIfDue(
            MobileNpcGoodsWindowBinding binding,
            List<(int Score, GObject Target)> listCandidates,
            List<(int Score, GObject Target)> inventoryGridCandidates)
        {
            if (!Settings.DebugMode || _mobileNpcGoodsBindingsDumped)
                return;

            if (binding == null || binding.Window == null || binding.Window._disposed)
                return;

            try
            {
                Directory.CreateDirectory(ClientResourceLayout.RuntimeRoot);

                string path = Path.Combine(ClientResourceLayout.RuntimeRoot, "FairyGui-MobileNpcGoodsBindings.txt");

                var builder = new StringBuilder(14 * 1024);
                builder.AppendLine("FairyGUI NPC 商品/出售窗口绑定报告（NpcGoods）");
                builder.AppendLine($"GeneratedAtUtc={DateTime.UtcNow:o}");
                builder.AppendLine($"WindowKey={binding.WindowKey ?? "NpcGoods"}");
                builder.AppendLine($"WindowResolveInfo={binding.ResolveInfo ?? "-"}");
                builder.AppendLine();

                builder.AppendLine($"GoodsList={DescribeObject(binding.Window, binding.GoodsList)}");
                builder.AppendLine($"GoodsListResolveInfo={binding.GoodsListResolveInfo ?? "-"}");
                builder.AppendLine($"OverrideSpec={binding.GoodsListOverrideSpec ?? "-"}");
                builder.AppendLine($"OverrideKeywords={(binding.GoodsListOverrideKeywords == null ? "-" : string.Join("|", binding.GoodsListOverrideKeywords))}");
                builder.AppendLine($"Items={MobileNpcGoodsItems.Count}");
                builder.AppendLine();

                builder.AppendLine($"InventoryGrid={DescribeObject(binding.Window, binding.InventoryGrid?.GridRoot)}");
                builder.AppendLine($"InventoryGridResolveInfo={binding.InventoryGrid?.GridResolveInfo ?? "-"}");
                builder.AppendLine($"InventoryOverrideSpec={binding.InventoryGrid?.OverrideSpec ?? "-"}");
                builder.AppendLine($"InventoryOverrideKeywords={(binding.InventoryGrid?.OverrideKeywords == null ? "-" : string.Join("|", binding.InventoryGrid.OverrideKeywords))}");
                builder.AppendLine();

                builder.AppendLine($"BuyButton={DescribeObject(binding.Window, binding.BuyButton)} ResolveInfo={binding.BuyButtonResolveInfo ?? "-"} OverrideSpec={binding.BuyButtonOverrideSpec ?? "-"}");
                builder.AppendLine($"SellButton={DescribeObject(binding.Window, binding.SellButton)} ResolveInfo={binding.SellButtonResolveInfo ?? "-"} OverrideSpec={binding.SellButtonOverrideSpec ?? "-"}");
                builder.AppendLine();

                builder.AppendLine("GoodsList Candidates(top 12):");
                int top = Math.Min(12, listCandidates?.Count ?? 0);
                for (int i = 0; i < top; i++)
                {
                    (int score, GObject target) = listCandidates[i];
                    builder.AppendLine($"  - score={score} obj={DescribeObject(binding.Window, target)}");
                }

                if ((listCandidates?.Count ?? 0) > top)
                    builder.AppendLine($"  ... ({listCandidates.Count - top} more)");

                builder.AppendLine();
                builder.AppendLine("InventoryGrid Candidates(top 12):");
                top = Math.Min(12, inventoryGridCandidates?.Count ?? 0);
                for (int i = 0; i < top; i++)
                {
                    (int score, GObject target) = inventoryGridCandidates[i];
                    builder.AppendLine($"  - score={score} obj={DescribeObject(binding.Window, target)}");
                }

                if ((inventoryGridCandidates?.Count ?? 0) > top)
                    builder.AppendLine($"  ... ({inventoryGridCandidates.Count - top} more)");

                builder.AppendLine();
                builder.AppendLine("配置覆盖（可选）：Mir2Config.ini -> [FairyGUI]");
                builder.AppendLine($"  {MobileNpcGoodsListConfigKey}=idx:...（推荐）或 path:... 或 name:/item:/url:/title:... 或关键字 a|b|c");
                builder.AppendLine($"  {MobileNpcGoodsInventoryGridConfigKey}=idx:...（推荐）或 path:... 或 name:/item:/url:/title:... 或关键字 a|b|c");
                builder.AppendLine($"  {MobileNpcGoodsBuyButtonConfigKey}=idx:...（推荐）或 path:... 或 name:/item:/url:/title:... 或关键字 a|b|c");
                builder.AppendLine($"  {MobileNpcGoodsSellButtonConfigKey}=idx:...（推荐）或 path:... 或 name:/item:/url:/title:... 或关键字 a|b|c");
                builder.AppendLine("说明：idx/path 均相对 NpcGoods 窗口 Root。建议先查看窗口树（FairyGui-MobileWindow-NpcGoods-Tree.txt），再填入精确覆盖。");

                File.WriteAllText(path, builder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                _mobileNpcGoodsBindingsDumped = true;
                CMain.SaveLog($"FairyGUI: 已导出 NPC 商品/出售窗口绑定报告到 {path}");
            }
            catch (Exception ex)
            {
                CMain.SaveError("FairyGUI: 导出 NPC 商品/出售窗口绑定报告失败：" + ex.Message);
            }
        }
    }
}
