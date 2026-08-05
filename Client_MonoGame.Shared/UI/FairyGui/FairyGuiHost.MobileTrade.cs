using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using C = ClientPackets;
using FairyGUI;
using MonoShare.MirScenes;

namespace MonoShare
{
    internal static partial class FairyGuiHost
    {
        private const string MobileTradeInventoryGridConfigKey = "MobileTrade.InventoryGrid";
        private const string MobileTradeSelfTradeGridConfigKey = "MobileTrade.SelfTradeGrid";
        private const string MobileTradeGuestTradeGridConfigKey = "MobileTrade.GuestTradeGrid";
        private const string MobileTradeGuestNameConfigKey = "MobileTrade.GuestName";
        private const string MobileTradeSelfGoldConfigKey = "MobileTrade.SelfGold";
        private const string MobileTradeGuestGoldConfigKey = "MobileTrade.GuestGold";
        private const string MobileTradeGoldInputConfigKey = "MobileTrade.GoldInput";
        private const string MobileTradeGoldAddButtonConfigKey = "MobileTrade.GoldAddButton";
        private const string MobileTradeLockButtonConfigKey = "MobileTrade.LockButton";
        private const string MobileTradeCancelButtonConfigKey = "MobileTrade.CancelButton";

        private static readonly string[] DefaultTradeInventoryGridKeywords = { "bag", "inventory", "背包", "物品", "格子" };
        private static readonly string[] DefaultTradeSelfTradeGridKeywords = { "trade", "my", "self", "offer", "交易", "我方", "自己" };
        private static readonly string[] DefaultTradeGuestTradeGridKeywords = { "guest", "other", "target", "对方", "他方", "对面" };
        private static readonly string[] DefaultTradeGuestNameKeywords = { "guest", "name", "对方", "玩家", "名字", "名称" };
        private static readonly string[] DefaultTradeSelfGoldKeywords = { "gold", "money", "金额", "金币", "我方", "自己" };
        private static readonly string[] DefaultTradeGuestGoldKeywords = { "gold", "money", "金额", "金币", "对方", "他方" };
        private static readonly string[] DefaultTradeGoldInputKeywords = { "gold", "money", "金额", "金币", "输入", "input", "edit" };
        private static readonly string[] DefaultTradeGoldAddButtonKeywords = { "add", "ok", "confirm", "set", "金币", "金额", "添加", "确定", "放入" };
        private static readonly string[] DefaultTradeLockButtonKeywords = { "lock", "confirm", "ok", "trade", "锁", "锁定", "确认" };
        private static readonly string[] DefaultTradeCancelButtonKeywords = { "cancel", "close", "exit", "取消", "关闭", "退出" };

        private sealed class MobileTradeWindowBinding
        {
            public string WindowKey;
            public GComponent Window;
            public string ResolveInfo;

            public MobileItemGridBinding InventoryGrid;
            public MobileItemGridBinding SelfTradeGrid;
            public MobileItemGridBinding GuestTradeGrid;

            public GTextField GuestName;
            public string GuestNameResolveInfo;
            public string GuestNameOverrideSpec;
            public string[] GuestNameOverrideKeywords;

            public GTextField SelfGold;
            public string SelfGoldResolveInfo;
            public string SelfGoldOverrideSpec;
            public string[] SelfGoldOverrideKeywords;

            public GTextField GuestGold;
            public string GuestGoldResolveInfo;
            public string GuestGoldOverrideSpec;
            public string[] GuestGoldOverrideKeywords;

            public GTextInput GoldInput;
            public string GoldInputResolveInfo;
            public string GoldInputOverrideSpec;
            public string[] GoldInputOverrideKeywords;
            public EventCallback0 GoldSubmitCallback;

            public GButton GoldAddButton;
            public string GoldAddButtonResolveInfo;
            public string GoldAddButtonOverrideSpec;
            public string[] GoldAddButtonOverrideKeywords;
            public EventCallback0 GoldAddClickCallback;

            public GButton LockButton;
            public string LockButtonResolveInfo;
            public string LockButtonOverrideSpec;
            public string[] LockButtonOverrideKeywords;
            public EventCallback0 LockClickCallback;

            public GButton CancelButton;
            public string CancelButtonResolveInfo;
            public string CancelButtonOverrideSpec;
            public string[] CancelButtonOverrideKeywords;
            public EventCallback0 CancelClickCallback;

            public int SelectedIndex = -1;
            public MirGridType SelectedGrid = MirGridType.Inventory;
        }

        private static MobileTradeWindowBinding _mobileTradeBinding;
        private static DateTime _nextMobileTradeBindAttemptUtc = DateTime.MinValue;
        private static bool _mobileTradeBindingsDumped;

        private static string _mobileTradeGuestName = string.Empty;
        private static uint _mobileTradeGuestGold;
        private static readonly UserItem[] MobileTradeGuestItems = new UserItem[10];

        public static void BeginMobileTrade(string guestName)
        {
            _mobileTradeGuestName = guestName ?? string.Empty;
            _mobileTradeGuestGold = 0;
            Array.Clear(MobileTradeGuestItems, 0, MobileTradeGuestItems.Length);

            MobileTradeWindowBinding binding = _mobileTradeBinding;
            if (binding != null)
            {
                binding.SelectedIndex = -1;
                binding.SelectedGrid = MirGridType.Inventory;
            }

            MarkMobileTradeDirty();
        }

        public static void EndMobileTrade()
        {
            _mobileTradeGuestName = string.Empty;
            _mobileTradeGuestGold = 0;
            Array.Clear(MobileTradeGuestItems, 0, MobileTradeGuestItems.Length);

            MobileTradeWindowBinding binding = _mobileTradeBinding;
            if (binding != null)
            {
                binding.SelectedIndex = -1;
                binding.SelectedGrid = MirGridType.Inventory;
            }

            MarkMobileTradeDirty();
        }

        public static void UpdateMobileTradeGuestGold(uint amount)
        {
            _mobileTradeGuestGold = amount;
            MarkMobileTradeDirty();
        }

        public static void UpdateMobileTradeGuestItems(UserItem[] items)
        {
            try
            {
                Array.Clear(MobileTradeGuestItems, 0, MobileTradeGuestItems.Length);

                if (items != null)
                {
                    int count = Math.Min(MobileTradeGuestItems.Length, items.Length);
                    for (int i = 0; i < count; i++)
                    {
                        UserItem item = items[i];
                        if (item != null)
                        {
                            GameScene.Bind(item);
                            MobileTradeGuestItems[i] = item;
                        }
                    }
                }
            }
            catch
            {
            }

            MarkMobileTradeDirty();
        }

        public static void MarkMobileTradeDirty()
        {
            TryRefreshMobileTradeIfDue(force: false);
        }

        private static void ResetMobileTradeBindings()
        {
            try
            {
                MobileTradeWindowBinding binding = _mobileTradeBinding;
                if (binding != null)
                {
                    DetachMobileItemGridSlotCallbacks(binding.InventoryGrid);
                    DetachMobileItemGridSlotCallbacks(binding.SelfTradeGrid);

                    try
                    {
                        if (binding.GoldAddButton != null && !binding.GoldAddButton._disposed && binding.GoldAddClickCallback != null)
                            binding.GoldAddButton.onClick.Remove(binding.GoldAddClickCallback);
                    }
                    catch
                    {
                    }

                    try
                    {
                        if (binding.LockButton != null && !binding.LockButton._disposed && binding.LockClickCallback != null)
                            binding.LockButton.onClick.Remove(binding.LockClickCallback);
                    }
                    catch
                    {
                    }

                    try
                    {
                        if (binding.CancelButton != null && !binding.CancelButton._disposed && binding.CancelClickCallback != null)
                            binding.CancelButton.onClick.Remove(binding.CancelClickCallback);
                    }
                    catch
                    {
                    }

                    try
                    {
                        if (binding.GoldInput != null && !binding.GoldInput._disposed && binding.GoldSubmitCallback != null)
                            binding.GoldInput.onSubmit.Remove(binding.GoldSubmitCallback);
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }

            _mobileTradeBinding = null;
            _nextMobileTradeBindAttemptUtc = DateTime.MinValue;
            _mobileTradeBindingsDumped = false;
        }

        private static MobileTradeWindowBinding EnsureMobileTradeBinding(string windowKey, GComponent window, string resolveInfo)
        {
            if (window == null || window._disposed)
                return null;

            MobileTradeWindowBinding binding = _mobileTradeBinding;
            if (binding != null && (binding.Window == null || binding.Window._disposed))
            {
                ResetMobileTradeBindings();
                binding = null;
            }

            if (binding == null || !ReferenceEquals(binding.Window, window))
            {
                ResetMobileTradeBindings();
                binding = new MobileTradeWindowBinding
                {
                    WindowKey = windowKey,
                    Window = window,
                    ResolveInfo = resolveInfo,
                    SelectedIndex = -1,
                    SelectedGrid = MirGridType.Inventory,
                };

                _mobileTradeBinding = binding;
                _mobileTradeBindingsDumped = false;
                _nextMobileTradeBindAttemptUtc = DateTime.MinValue;
            }

            return binding;
        }

        private static bool IsMobileTradeBindingComplete(MobileTradeWindowBinding binding)
        {
            if (binding == null || binding.Window == null || binding.Window._disposed)
                return false;

            bool gridsOk =
                binding.InventoryGrid != null && binding.InventoryGrid.Slots.Count > 0 &&
                binding.SelfTradeGrid != null && binding.SelfTradeGrid.Slots.Count > 0 &&
                binding.GuestTradeGrid != null && binding.GuestTradeGrid.Slots.Count > 0;

            if (!gridsOk)
                return false;

            bool basicTextsOk =
                binding.GuestName != null && !binding.GuestName._disposed &&
                binding.SelfGold != null && !binding.SelfGold._disposed &&
                binding.GuestGold != null && !binding.GuestGold._disposed;

            bool basicControlsOk =
                binding.GoldInput != null && !binding.GoldInput._disposed &&
                binding.GoldAddButton != null && !binding.GoldAddButton._disposed &&
                binding.LockButton != null && !binding.LockButton._disposed &&
                binding.CancelButton != null && !binding.CancelButton._disposed;

            return basicTextsOk && basicControlsOk;
        }

        private static MobileItemGridBinding CreateMobileTradeItemGridBinding(
            string windowKey,
            GComponent window,
            string windowResolveInfo,
            GComponent gridRoot,
            string gridResolveInfo,
            string overrideSpec,
            string[] overrideKeywords,
            int desiredSlots,
            MirGridType clickGridType,
            bool attachClick)
        {
            if (gridRoot == null || gridRoot._disposed)
                return null;

            var gridBinding = new MobileItemGridBinding
            {
                WindowKey = windowKey,
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

                if (attachClick)
                {
                    try
                    {
                        EventCallback0 callback = () => OnMobileTradeSlotClicked(clickGridType, slotIndex);
                        slot.ClickCallback = callback;
                        slotRoot.onClick.Add(callback);
                    }
                    catch
                    {
                    }
                }

                gridBinding.Slots.Add(slot);
            }

            return gridBinding;
        }

        private static void TryBindMobileTradeGridsIfDue(MobileTradeWindowBinding binding)
        {
            if (binding == null || binding.Window == null || binding.Window._disposed)
                return;

            GComponent window = binding.Window;

            int desiredInventorySlots = GameScene.User?.Inventory?.Length ?? 46;
            int desiredTradeSlots = GameScene.User?.Trade?.Length ?? 10;
            int desiredGuestSlots = MobileTradeGuestItems.Length;

            bool inventoryBound = binding.InventoryGrid != null && binding.InventoryGrid.Slots.Count > 0 && binding.InventoryGrid.GridRoot != null && !binding.InventoryGrid.GridRoot._disposed;
            bool selfTradeBound = binding.SelfTradeGrid != null && binding.SelfTradeGrid.Slots.Count > 0 && binding.SelfTradeGrid.GridRoot != null && !binding.SelfTradeGrid.GridRoot._disposed;
            bool guestTradeBound = binding.GuestTradeGrid != null && binding.GuestTradeGrid.Slots.Count > 0 && binding.GuestTradeGrid.GridRoot != null && !binding.GuestTradeGrid.GridRoot._disposed;

            if (inventoryBound && selfTradeBound && guestTradeBound)
                return;

            string inventoryGridSpec = string.Empty;
            string selfTradeGridSpec = string.Empty;
            string guestTradeGridSpec = string.Empty;

            try
            {
                InIReader reader = TryCreateConfigReader();
                if (reader != null)
                {
                    inventoryGridSpec = reader.ReadString(FairyGuiConfigSectionName, MobileTradeInventoryGridConfigKey, string.Empty, writeWhenNull: false);
                    selfTradeGridSpec = reader.ReadString(FairyGuiConfigSectionName, MobileTradeSelfTradeGridConfigKey, string.Empty, writeWhenNull: false);
                    guestTradeGridSpec = reader.ReadString(FairyGuiConfigSectionName, MobileTradeGuestTradeGridConfigKey, string.Empty, writeWhenNull: false);
                }
            }
            catch
            {
                inventoryGridSpec = string.Empty;
                selfTradeGridSpec = string.Empty;
                guestTradeGridSpec = string.Empty;
            }

            inventoryGridSpec = inventoryGridSpec?.Trim() ?? string.Empty;
            selfTradeGridSpec = selfTradeGridSpec?.Trim() ?? string.Empty;
            guestTradeGridSpec = guestTradeGridSpec?.Trim() ?? string.Empty;

            var used = new HashSet<GObject>();
            try { if (binding.InventoryGrid?.GridRoot != null && !binding.InventoryGrid.GridRoot._disposed) used.Add(binding.InventoryGrid.GridRoot); } catch { }
            try { if (binding.SelfTradeGrid?.GridRoot != null && !binding.SelfTradeGrid.GridRoot._disposed) used.Add(binding.SelfTradeGrid.GridRoot); } catch { }
            try { if (binding.GuestTradeGrid?.GridRoot != null && !binding.GuestTradeGrid.GridRoot._disposed) used.Add(binding.GuestTradeGrid.GridRoot); } catch { }

            if (!inventoryBound)
            {
                GComponent root = ResolveTradeGridRoot(
                    window,
                    inventoryGridSpec,
                    DefaultTradeInventoryGridKeywords,
                    used,
                    minScore: 60,
                    minSlots: Math.Max(10, desiredInventorySlots / 2),
                    out string gridResolveInfo,
                    out string[] overrideKeywords,
                    out _);

                if (root == null || root._disposed)
                {
                    CMain.SaveError("FairyGUI: 交易窗口未找到背包格子（Trade）。可在 Mir2Config.ini 设置 [" + FairyGuiConfigSectionName + "] " +
                                    MobileTradeInventoryGridConfigKey + "=idx:... 指定背包格子根节点。");
                    return;
                }

                used.Add(root);

                if (binding.InventoryGrid != null)
                    DetachMobileItemGridSlotCallbacks(binding.InventoryGrid);

                binding.InventoryGrid = CreateMobileTradeItemGridBinding(
                    binding.WindowKey,
                    window,
                    binding.ResolveInfo,
                    root,
                    gridResolveInfo,
                    inventoryGridSpec,
                    overrideKeywords,
                    desiredInventorySlots,
                    MirGridType.Inventory,
                    attachClick: true);

                if (binding.InventoryGrid == null || binding.InventoryGrid.Slots.Count == 0)
                {
                    CMain.SaveError("FairyGUI: 交易窗口背包格子绑定失败（Slots=0）。请检查 publish UI 或通过配置覆盖格子根节点。");
                    return;
                }
            }

            if (!selfTradeBound)
            {
                GComponent root = ResolveTradeGridRoot(
                    window,
                    selfTradeGridSpec,
                    DefaultTradeSelfTradeGridKeywords,
                    used,
                    minScore: 60,
                    minSlots: Math.Max(6, desiredTradeSlots),
                    out string gridResolveInfo,
                    out string[] overrideKeywords,
                    out _);

                if (root == null || root._disposed)
                {
                    CMain.SaveError("FairyGUI: 交易窗口未找到我方交易格子（Trade）。可在 Mir2Config.ini 设置 [" + FairyGuiConfigSectionName + "] " +
                                    MobileTradeSelfTradeGridConfigKey + "=idx:... 指定我方交易格子根节点。");
                    return;
                }

                used.Add(root);

                if (binding.SelfTradeGrid != null)
                    DetachMobileItemGridSlotCallbacks(binding.SelfTradeGrid);

                binding.SelfTradeGrid = CreateMobileTradeItemGridBinding(
                    binding.WindowKey,
                    window,
                    binding.ResolveInfo,
                    root,
                    gridResolveInfo,
                    selfTradeGridSpec,
                    overrideKeywords,
                    desiredTradeSlots,
                    MirGridType.Trade,
                    attachClick: true);

                if (binding.SelfTradeGrid == null || binding.SelfTradeGrid.Slots.Count == 0)
                {
                    CMain.SaveError("FairyGUI: 交易窗口我方交易格子绑定失败（Slots=0）。请检查 publish UI 或通过配置覆盖格子根节点。");
                    return;
                }
            }

            if (!guestTradeBound)
            {
                GComponent root = ResolveTradeGridRoot(
                    window,
                    guestTradeGridSpec,
                    DefaultTradeGuestTradeGridKeywords,
                    used,
                    minScore: 60,
                    minSlots: Math.Max(6, desiredGuestSlots),
                    out string gridResolveInfo,
                    out string[] overrideKeywords,
                    out _);

                if (root == null || root._disposed)
                {
                    CMain.SaveError("FairyGUI: 交易窗口未找到对方交易格子（Trade）。可在 Mir2Config.ini 设置 [" + FairyGuiConfigSectionName + "] " +
                                    MobileTradeGuestTradeGridConfigKey + "=idx:... 指定对方交易格子根节点。");
                    return;
                }

                used.Add(root);

                if (binding.GuestTradeGrid != null)
                    DetachMobileItemGridSlotCallbacks(binding.GuestTradeGrid);

                binding.GuestTradeGrid = CreateMobileTradeItemGridBinding(
                    binding.WindowKey,
                    window,
                    binding.ResolveInfo,
                    root,
                    gridResolveInfo,
                    guestTradeGridSpec,
                    overrideKeywords,
                    desiredGuestSlots,
                    MirGridType.GuestTrade,
                    attachClick: false);

                if (binding.GuestTradeGrid == null || binding.GuestTradeGrid.Slots.Count == 0)
                {
                    CMain.SaveError("FairyGUI: 交易窗口对方交易格子绑定失败（Slots=0）。请检查 publish UI 或通过配置覆盖格子根节点。");
                    return;
                }
            }
        }

        private static void TryBindMobileTradeControlsIfDue(MobileTradeWindowBinding binding)
        {
            if (binding == null || binding.Window == null || binding.Window._disposed)
                return;

            GComponent window = binding.Window;

            bool basicTextsOk =
                binding.GuestName != null && !binding.GuestName._disposed &&
                binding.SelfGold != null && !binding.SelfGold._disposed &&
                binding.GuestGold != null && !binding.GuestGold._disposed;

            bool basicControlsOk =
                binding.GoldInput != null && !binding.GoldInput._disposed &&
                binding.GoldAddButton != null && !binding.GoldAddButton._disposed &&
                binding.LockButton != null && !binding.LockButton._disposed &&
                binding.CancelButton != null && !binding.CancelButton._disposed;

            if (basicTextsOk && basicControlsOk)
                return;

            string guestNameSpec = string.Empty;
            string selfGoldSpec = string.Empty;
            string guestGoldSpec = string.Empty;
            string goldInputSpec = string.Empty;
            string goldAddButtonSpec = string.Empty;
            string lockButtonSpec = string.Empty;
            string cancelButtonSpec = string.Empty;

            try
            {
                InIReader reader = TryCreateConfigReader();
                if (reader != null)
                {
                    guestNameSpec = reader.ReadString(FairyGuiConfigSectionName, MobileTradeGuestNameConfigKey, string.Empty, writeWhenNull: false);
                    selfGoldSpec = reader.ReadString(FairyGuiConfigSectionName, MobileTradeSelfGoldConfigKey, string.Empty, writeWhenNull: false);
                    guestGoldSpec = reader.ReadString(FairyGuiConfigSectionName, MobileTradeGuestGoldConfigKey, string.Empty, writeWhenNull: false);
                    goldInputSpec = reader.ReadString(FairyGuiConfigSectionName, MobileTradeGoldInputConfigKey, string.Empty, writeWhenNull: false);
                    goldAddButtonSpec = reader.ReadString(FairyGuiConfigSectionName, MobileTradeGoldAddButtonConfigKey, string.Empty, writeWhenNull: false);
                    lockButtonSpec = reader.ReadString(FairyGuiConfigSectionName, MobileTradeLockButtonConfigKey, string.Empty, writeWhenNull: false);
                    cancelButtonSpec = reader.ReadString(FairyGuiConfigSectionName, MobileTradeCancelButtonConfigKey, string.Empty, writeWhenNull: false);
                }
            }
            catch
            {
                guestNameSpec = string.Empty;
                selfGoldSpec = string.Empty;
                guestGoldSpec = string.Empty;
                goldInputSpec = string.Empty;
                goldAddButtonSpec = string.Empty;
                lockButtonSpec = string.Empty;
                cancelButtonSpec = string.Empty;
            }

            guestNameSpec = guestNameSpec?.Trim() ?? string.Empty;
            selfGoldSpec = selfGoldSpec?.Trim() ?? string.Empty;
            guestGoldSpec = guestGoldSpec?.Trim() ?? string.Empty;
            goldInputSpec = goldInputSpec?.Trim() ?? string.Empty;
            goldAddButtonSpec = goldAddButtonSpec?.Trim() ?? string.Empty;
            lockButtonSpec = lockButtonSpec?.Trim() ?? string.Empty;
            cancelButtonSpec = cancelButtonSpec?.Trim() ?? string.Empty;

            var used = new HashSet<GObject>();

            GTextField ResolveTextField(string spec, string[] defaultKeywords, out string resolveInfoOut, out string[] overrideKeywords)
            {
                resolveInfoOut = null;
                overrideKeywords = null;

                if (!string.IsNullOrWhiteSpace(spec))
                {
                    if (TryResolveMobileMainHudObjectBySpec(window, spec, out GObject resolved, out string[] keywords))
                    {
                        if (resolved is GTextField resolvedField && !resolvedField._disposed && resolved is not GTextInput)
                        {
                            resolveInfoOut = DescribeObject(window, resolvedField) + " (override)";
                            return resolvedField;
                        }

                        if (keywords != null && keywords.Length > 0)
                            overrideKeywords = keywords;
                    }
                    else
                    {
                        overrideKeywords = SplitKeywords(spec);
                    }
                }

                string[] keywordsUsed = overrideKeywords != null && overrideKeywords.Length > 0 ? overrideKeywords : defaultKeywords;
                List<(int Score, GObject Target)> candidates = CollectMobileChatCandidates(window, obj => obj is GTextField && obj is not GTextInput, keywordsUsed, ScoreMobileShopTextCandidate);
                GTextField selected = SelectBestUnusedCandidate<GTextField>(candidates, minScore: 40, used: used);
                if (selected != null && !selected._disposed)
                    resolveInfoOut = DescribeObject(window, selected) + (overrideKeywords != null && overrideKeywords.Length > 0 ? " (keywords)" : " (auto)");
                return selected;
            }

            GTextInput ResolveTextInput(string spec, string[] defaultKeywords, out string resolveInfoOut, out string[] overrideKeywords)
            {
                resolveInfoOut = null;
                overrideKeywords = null;

                if (!string.IsNullOrWhiteSpace(spec))
                {
                    if (TryResolveMobileMainHudObjectBySpec(window, spec, out GObject resolved, out string[] keywords))
                    {
                        if (resolved is GTextInput resolvedInput && !resolvedInput._disposed)
                        {
                            resolveInfoOut = DescribeObject(window, resolvedInput) + " (override)";
                            return resolvedInput;
                        }

                        if (keywords != null && keywords.Length > 0)
                            overrideKeywords = keywords;
                    }
                    else
                    {
                        overrideKeywords = SplitKeywords(spec);
                    }
                }

                string[] keywordsUsed = overrideKeywords != null && overrideKeywords.Length > 0 ? overrideKeywords : defaultKeywords;
                List<(int Score, GObject Target)> candidates = CollectMobileChatCandidates(window, obj => obj is GTextInput && obj.touchable, keywordsUsed, ScoreMobileChatInputCandidate);
                GTextInput selected = SelectBestUnusedCandidate<GTextInput>(candidates, minScore: 40, used: used);
                if (selected != null && !selected._disposed)
                    resolveInfoOut = DescribeObject(window, selected) + (overrideKeywords != null && overrideKeywords.Length > 0 ? " (keywords)" : " (auto)");
                return selected;
            }

            GButton ResolveButton(string spec, string[] defaultKeywords, out string resolveInfoOut, out string[] overrideKeywords)
            {
                resolveInfoOut = null;
                overrideKeywords = null;

                if (!string.IsNullOrWhiteSpace(spec))
                {
                    if (TryResolveMobileMainHudObjectBySpec(window, spec, out GObject resolved, out string[] keywords))
                    {
                        if (resolved is GButton resolvedButton && !resolvedButton._disposed)
                        {
                            resolveInfoOut = DescribeObject(window, resolvedButton) + " (override)";
                            return resolvedButton;
                        }

                        if (keywords != null && keywords.Length > 0)
                            overrideKeywords = keywords;
                    }
                    else
                    {
                        overrideKeywords = SplitKeywords(spec);
                    }
                }

                string[] keywordsUsed = overrideKeywords != null && overrideKeywords.Length > 0 ? overrideKeywords : defaultKeywords;
                List<(int Score, GObject Target)> candidates = CollectMobileChatCandidates(window, obj => obj is GButton && obj.touchable, keywordsUsed, ScoreMobileShopButtonCandidate);
                GButton selected = SelectBestUnusedCandidate<GButton>(candidates, minScore: 40, used: used);
                if (selected != null && !selected._disposed)
                    resolveInfoOut = DescribeObject(window, selected) + (overrideKeywords != null && overrideKeywords.Length > 0 ? " (keywords)" : " (auto)");
                return selected;
            }

            if (binding.GuestName == null || binding.GuestName._disposed)
            {
                binding.GuestNameOverrideSpec = guestNameSpec;
                binding.GuestName = ResolveTextField(guestNameSpec, DefaultTradeGuestNameKeywords, out string resolved, out string[] overrideKeywords);
                binding.GuestNameResolveInfo = resolved;
                binding.GuestNameOverrideKeywords = overrideKeywords;
                if (binding.GuestName != null && !binding.GuestName._disposed)
                    used.Add(binding.GuestName);
            }

            if (binding.SelfGold == null || binding.SelfGold._disposed)
            {
                binding.SelfGoldOverrideSpec = selfGoldSpec;
                binding.SelfGold = ResolveTextField(selfGoldSpec, DefaultTradeSelfGoldKeywords, out string resolved, out string[] overrideKeywords);
                binding.SelfGoldResolveInfo = resolved;
                binding.SelfGoldOverrideKeywords = overrideKeywords;
                if (binding.SelfGold != null && !binding.SelfGold._disposed)
                    used.Add(binding.SelfGold);
            }

            if (binding.GuestGold == null || binding.GuestGold._disposed)
            {
                binding.GuestGoldOverrideSpec = guestGoldSpec;
                binding.GuestGold = ResolveTextField(guestGoldSpec, DefaultTradeGuestGoldKeywords, out string resolved, out string[] overrideKeywords);
                binding.GuestGoldResolveInfo = resolved;
                binding.GuestGoldOverrideKeywords = overrideKeywords;
                if (binding.GuestGold != null && !binding.GuestGold._disposed)
                    used.Add(binding.GuestGold);
            }

            if (binding.GoldInput == null || binding.GoldInput._disposed)
            {
                binding.GoldInputOverrideSpec = goldInputSpec;
                binding.GoldInput = ResolveTextInput(goldInputSpec, DefaultTradeGoldInputKeywords, out string resolved, out string[] overrideKeywords);
                binding.GoldInputResolveInfo = resolved;
                binding.GoldInputOverrideKeywords = overrideKeywords;
                if (binding.GoldInput != null && !binding.GoldInput._disposed)
                    used.Add(binding.GoldInput);
            }

            if (binding.GoldAddButton == null || binding.GoldAddButton._disposed)
            {
                binding.GoldAddButtonOverrideSpec = goldAddButtonSpec;
                binding.GoldAddButton = ResolveButton(goldAddButtonSpec, DefaultTradeGoldAddButtonKeywords, out string resolved, out string[] overrideKeywords);
                binding.GoldAddButtonResolveInfo = resolved;
                binding.GoldAddButtonOverrideKeywords = overrideKeywords;
                if (binding.GoldAddButton != null && !binding.GoldAddButton._disposed)
                    used.Add(binding.GoldAddButton);
            }

            if (binding.LockButton == null || binding.LockButton._disposed)
            {
                binding.LockButtonOverrideSpec = lockButtonSpec;
                binding.LockButton = ResolveButton(lockButtonSpec, DefaultTradeLockButtonKeywords, out string resolved, out string[] overrideKeywords);
                binding.LockButtonResolveInfo = resolved;
                binding.LockButtonOverrideKeywords = overrideKeywords;
                if (binding.LockButton != null && !binding.LockButton._disposed)
                    used.Add(binding.LockButton);
            }

            if (binding.CancelButton == null || binding.CancelButton._disposed)
            {
                binding.CancelButtonOverrideSpec = cancelButtonSpec;
                binding.CancelButton = ResolveButton(cancelButtonSpec, DefaultTradeCancelButtonKeywords, out string resolved, out string[] overrideKeywords);
                binding.CancelButtonResolveInfo = resolved;
                binding.CancelButtonOverrideKeywords = overrideKeywords;
                if (binding.CancelButton != null && !binding.CancelButton._disposed)
                    used.Add(binding.CancelButton);
            }

            try
            {
                if (binding.GoldInput != null && !binding.GoldInput._disposed)
                {
                    if (binding.GoldSubmitCallback == null)
                        binding.GoldSubmitCallback = () => TrySubmitMobileTradeGold();

                    binding.GoldInput.onSubmit.Remove(binding.GoldSubmitCallback);
                    binding.GoldInput.onSubmit.Add(binding.GoldSubmitCallback);
                }
            }
            catch
            {
            }

            try
            {
                if (binding.GoldAddButton != null && !binding.GoldAddButton._disposed)
                {
                    if (binding.GoldAddClickCallback == null)
                        binding.GoldAddClickCallback = () => TrySubmitMobileTradeGold();

                    binding.GoldAddButton.onClick.Remove(binding.GoldAddClickCallback);
                    binding.GoldAddButton.onClick.Add(binding.GoldAddClickCallback);
                }
            }
            catch
            {
            }

            try
            {
                if (binding.LockButton != null && !binding.LockButton._disposed)
                {
                    if (binding.LockClickCallback == null)
                        binding.LockClickCallback = () => TryToggleMobileTradeLock();

                    binding.LockButton.onClick.Remove(binding.LockClickCallback);
                    binding.LockButton.onClick.Add(binding.LockClickCallback);
                }
            }
            catch
            {
            }

            try
            {
                if (binding.CancelButton != null && !binding.CancelButton._disposed)
                {
                    if (binding.CancelClickCallback == null)
                        binding.CancelClickCallback = () => TryCancelMobileTrade();

                    binding.CancelButton.onClick.Remove(binding.CancelClickCallback);
                    binding.CancelButton.onClick.Add(binding.CancelClickCallback);
                }
            }
            catch
            {
            }
        }

        private static void TryDumpMobileTradeBindingsReportIfDue(MobileTradeWindowBinding binding)
        {
            if (!Settings.DebugMode || _mobileTradeBindingsDumped)
                return;

            if (binding == null || binding.Window == null || binding.Window._disposed)
                return;

            try
            {
                Directory.CreateDirectory(ClientResourceLayout.RuntimeRoot);
                string path = Path.Combine(ClientResourceLayout.RuntimeRoot, "FairyGui-MobileTradeBindings.txt");

                var builder = new StringBuilder(12 * 1024);
                builder.AppendLine("FairyGUI 交易窗口绑定报告（用于排障/补充映射）");
                builder.AppendLine($"GeneratedAtUtc={DateTime.UtcNow:o}");
                builder.AppendLine($"WindowKey={binding.WindowKey}");
                if (!string.IsNullOrWhiteSpace(binding.ResolveInfo))
                    builder.AppendLine($"Resolved={binding.ResolveInfo}");
                builder.AppendLine();
                builder.AppendLine("配置覆盖（可选）：Mir2Config.ini -> [FairyGUI]");
                builder.AppendLine($"  {MobileTradeInventoryGridConfigKey}=idx:...（背包格子根节点）");
                builder.AppendLine($"  {MobileTradeSelfTradeGridConfigKey}=idx:...（我方交易格子根节点）");
                builder.AppendLine($"  {MobileTradeGuestTradeGridConfigKey}=idx:...（对方交易格子根节点）");
                builder.AppendLine($"  {MobileTradeGuestNameConfigKey}=idx:...（对方名称文本）");
                builder.AppendLine($"  {MobileTradeSelfGoldConfigKey}=idx:...（我方金币文本）");
                builder.AppendLine($"  {MobileTradeGuestGoldConfigKey}=idx:...（对方金币文本）");
                builder.AppendLine($"  {MobileTradeGoldInputConfigKey}=idx:...（金币输入框）");
                builder.AppendLine($"  {MobileTradeGoldAddButtonConfigKey}=idx:...（添加金币按钮）");
                builder.AppendLine($"  {MobileTradeLockButtonConfigKey}=idx:...（锁定/解锁按钮）");
                builder.AppendLine($"  {MobileTradeCancelButtonConfigKey}=idx:...（取消按钮）");
                builder.AppendLine("说明：idx/path 均相对交易窗口 Root。建议先查看窗口树（FairyGui-MobileWindow-Trade-Tree.txt），再填入精确覆盖。");
                builder.AppendLine();
                builder.AppendLine("绑定结果：");
                builder.AppendLine($"InventoryGrid={DescribeObject(binding.Window, binding.InventoryGrid?.GridRoot)} OverrideSpec={binding.InventoryGrid?.OverrideSpec ?? "-"}");
                builder.AppendLine($"SelfTradeGrid={DescribeObject(binding.Window, binding.SelfTradeGrid?.GridRoot)} OverrideSpec={binding.SelfTradeGrid?.OverrideSpec ?? "-"}");
                builder.AppendLine($"GuestTradeGrid={DescribeObject(binding.Window, binding.GuestTradeGrid?.GridRoot)} OverrideSpec={binding.GuestTradeGrid?.OverrideSpec ?? "-"}");
                builder.AppendLine($"GuestName={DescribeObject(binding.Window, binding.GuestName)} OverrideSpec={binding.GuestNameOverrideSpec ?? "-"}");
                builder.AppendLine($"SelfGold={DescribeObject(binding.Window, binding.SelfGold)} OverrideSpec={binding.SelfGoldOverrideSpec ?? "-"}");
                builder.AppendLine($"GuestGold={DescribeObject(binding.Window, binding.GuestGold)} OverrideSpec={binding.GuestGoldOverrideSpec ?? "-"}");
                builder.AppendLine($"GoldInput={DescribeObject(binding.Window, binding.GoldInput)} OverrideSpec={binding.GoldInputOverrideSpec ?? "-"}");
                builder.AppendLine($"GoldAddButton={DescribeObject(binding.Window, binding.GoldAddButton)} OverrideSpec={binding.GoldAddButtonOverrideSpec ?? "-"}");
                builder.AppendLine($"LockButton={DescribeObject(binding.Window, binding.LockButton)} OverrideSpec={binding.LockButtonOverrideSpec ?? "-"}");
                builder.AppendLine($"CancelButton={DescribeObject(binding.Window, binding.CancelButton)} OverrideSpec={binding.CancelButtonOverrideSpec ?? "-"}");

                File.WriteAllText(path, builder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                _mobileTradeBindingsDumped = true;
                CMain.SaveLog($"FairyGUI: 已导出交易窗口绑定报告到 {path}");
            }
            catch (Exception ex)
            {
                CMain.SaveError("FairyGUI: 导出交易窗口绑定报告失败：" + ex.Message);
            }
        }

        private static void TryBindMobileTradeWindowIfDue(string windowKey, GComponent window, string resolveInfo)
        {
            if (window == null || window._disposed)
                return;

            MobileTradeWindowBinding binding = EnsureMobileTradeBinding(windowKey, window, resolveInfo);
            if (binding == null)
                return;

            if (IsMobileTradeBindingComplete(binding))
                return;

            if (DateTime.UtcNow < _nextMobileTradeBindAttemptUtc)
                return;

            _nextMobileTradeBindAttemptUtc = DateTime.UtcNow.AddSeconds(2);

            TryBindMobileTradeGridsIfDue(binding);
            TryBindMobileTradeControlsIfDue(binding);
            TryDumpMobileTradeBindingsReportIfDue(binding);
        }

        private static void TryRefreshMobileTradeIfDue(bool force)
        {
            if (_stage == null || !_initialized || !_packagesLoaded)
                return;

            if (!MobileWindows.TryGetValue("Trade", out GComponent window) || window == null || window._disposed)
            {
                if (_mobileTradeBinding != null)
                    ResetMobileTradeBindings();
                return;
            }

            if (!window.visible)
                return;

            TryBindMobileTradeWindowIfDue("Trade", window, resolveInfo: null);

            MobileTradeWindowBinding binding = _mobileTradeBinding;
            if (binding == null || binding.Window == null || binding.Window._disposed)
            {
                ResetMobileTradeBindings();
                return;
            }

            var user = GameScene.User;
            if (user == null || user.Inventory == null || user.Trade == null)
                return;

            if (force || user.TradeLocked)
                binding.SelectedIndex = -1;

            if (binding.InventoryGrid != null && binding.InventoryGrid.Slots.Count > 0)
            {
                RefreshMobileItemGridSlots(binding.InventoryGrid, user.Inventory, out bool invalidated);
                if (invalidated)
                {
                    _nextMobileTradeBindAttemptUtc = DateTime.MinValue;
                    return;
                }
            }

            if (binding.SelfTradeGrid != null && binding.SelfTradeGrid.Slots.Count > 0)
            {
                RefreshMobileItemGridSlots(binding.SelfTradeGrid, user.Trade, out bool invalidated);
                if (invalidated)
                {
                    _nextMobileTradeBindAttemptUtc = DateTime.MinValue;
                    return;
                }
            }

            if (binding.GuestTradeGrid != null && binding.GuestTradeGrid.Slots.Count > 0)
            {
                RefreshMobileItemGridSlots(binding.GuestTradeGrid, MobileTradeGuestItems, out bool invalidated);
                if (invalidated)
                {
                    _nextMobileTradeBindAttemptUtc = DateTime.MinValue;
                    return;
                }
            }

            ApplyMobileTradeSelectionVisuals(binding);

            try
            {
                if (binding.GuestName != null && !binding.GuestName._disposed)
                    binding.GuestName.text = _mobileTradeGuestName ?? string.Empty;
            }
            catch
            {
            }

            try
            {
                if (binding.SelfGold != null && !binding.SelfGold._disposed)
                    binding.SelfGold.text = user.TradeGoldAmount.ToString("###,###,##0");
            }
            catch
            {
            }

            try
            {
                if (binding.GuestGold != null && !binding.GuestGold._disposed)
                    binding.GuestGold.text = _mobileTradeGuestGold.ToString("###,###,##0");
            }
            catch
            {
            }

            try
            {
                if (binding.LockButton != null && !binding.LockButton._disposed)
                    binding.LockButton.title = user.TradeLocked ? "解锁" : "锁定";
            }
            catch
            {
            }

            bool locked = user.TradeLocked;

            try { if (binding.GoldInput != null && !binding.GoldInput._disposed) binding.GoldInput.grayed = locked; } catch { }
            try { if (binding.GoldAddButton != null && !binding.GoldAddButton._disposed) binding.GoldAddButton.grayed = locked; } catch { }
            try { if (binding.InventoryGrid?.GridRoot != null && !binding.InventoryGrid.GridRoot._disposed) binding.InventoryGrid.GridRoot.grayed = locked; } catch { }
            try { if (binding.SelfTradeGrid?.GridRoot != null && !binding.SelfTradeGrid.GridRoot._disposed) binding.SelfTradeGrid.GridRoot.grayed = locked; } catch { }
        }

        private static GComponent ResolveTradeGridRoot(
            GComponent window,
            string spec,
            string[] defaultKeywords,
            ISet<GObject> used,
            int minScore,
            int minSlots,
            out string resolveInfo,
            out string[] overrideKeywords,
            out List<(int Score, GObject Target)> candidates)
        {
            resolveInfo = null;
            overrideKeywords = null;
            candidates = null;

            if (window == null || window._disposed)
                return null;

            if (!string.IsNullOrWhiteSpace(spec))
            {
                if (TryResolveMobileMainHudObjectBySpec(window, spec, out GObject resolved, out string[] keywords))
                {
                    if (resolved is GComponent resolvedComponent && !resolvedComponent._disposed)
                    {
                        resolveInfo = DescribeObject(window, resolvedComponent) + " (override)";
                        return resolvedComponent;
                    }

                    if (keywords != null && keywords.Length > 0)
                        overrideKeywords = keywords;
                }
                else
                {
                    overrideKeywords = SplitKeywords(spec);
                }
            }

            string[] keywordsUsed = overrideKeywords != null && overrideKeywords.Length > 0 ? overrideKeywords : defaultKeywords;
            candidates = CollectMobileChatCandidates(window, obj => obj is GComponent, keywordsUsed, ScoreMobileInventoryGridCandidate);

            for (int i = 0; i < candidates.Count; i++)
            {
                (int score, GObject target) = candidates[i];
                if (score < minScore)
                    break;

                if (target is not GComponent component || component._disposed)
                    continue;

                if (used != null && used.Contains(component))
                    continue;

                int slotCount;
                try
                {
                    slotCount = CollectInventorySlotCandidates(component).Count;
                }
                catch
                {
                    slotCount = 0;
                }

                if (slotCount < minSlots)
                    continue;

                resolveInfo = DescribeObject(window, component) + (overrideKeywords != null && overrideKeywords.Length > 0 ? " (keywords)" : " (auto)");
                return component;
            }

            // Fallback：按格子数量找一个“像格子根节点”的组件
            GComponent best = null;
            int bestSlots = 0;

            foreach (GObject obj in Enumerate(window))
            {
                if (obj == null || obj._disposed)
                    continue;

                if (obj is not GComponent component || component._disposed)
                    continue;

                if (used != null && used.Contains(component))
                    continue;

                int slotCount;
                try
                {
                    slotCount = CollectInventorySlotCandidates(component).Count;
                }
                catch
                {
                    slotCount = 0;
                }

                if (slotCount < minSlots)
                    continue;

                if (slotCount <= bestSlots)
                    continue;

                bestSlots = slotCount;
                best = component;
            }

            if (best != null && !best._disposed)
            {
                resolveInfo = DescribeObject(window, best) + " (fallback by slots=" + bestSlots + ")";
                return best;
            }

            return null;
        }

        private static void ApplyMobileTradeSelectionVisuals(MobileTradeWindowBinding binding)
        {
            if (binding == null)
                return;

            bool hasSelection = binding.SelectedIndex >= 0;
            int selectedIndex = binding.SelectedIndex;
            MirGridType selectedGrid = binding.SelectedGrid;

            ApplyMobileTradeSelectionVisualsToGrid(binding.InventoryGrid, hasSelection, selectedGrid == MirGridType.Inventory ? selectedIndex : -1);
            ApplyMobileTradeSelectionVisualsToGrid(binding.SelfTradeGrid, hasSelection, selectedGrid == MirGridType.Trade ? selectedIndex : -1);
        }

        private static void ApplyMobileTradeSelectionVisualsToGrid(MobileItemGridBinding grid, bool hasSelection, int selectedIndex)
        {
            if (grid == null)
                return;

            for (int i = 0; i < grid.Slots.Count; i++)
            {
                MobileItemSlotBinding slot = grid.Slots[i];
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

        private static void OnMobileTradeSlotClicked(MirGridType gridType, int slotIndex)
        {
            MobileTradeWindowBinding binding = _mobileTradeBinding;
            if (binding == null)
                return;

            if (slotIndex < 0)
                return;

            var user = GameScene.User;
            if (user == null || user.Inventory == null || user.Trade == null)
                return;

            if (user.TradeLocked)
            {
                GameScene.Scene?.MobileReceiveChat("[交易] 已锁定，无法修改交易内容。", ChatType.Hint);
                return;
            }

            bool clickedHasItem = false;
            if (gridType == MirGridType.Inventory)
            {
                if (slotIndex < user.Inventory.Length)
                    clickedHasItem = user.Inventory[slotIndex] != null && user.Inventory[slotIndex].Info != null;
            }
            else if (gridType == MirGridType.Trade)
            {
                if (slotIndex < user.Trade.Length)
                    clickedHasItem = user.Trade[slotIndex] != null && user.Trade[slotIndex].Info != null;
            }
            else
            {
                return;
            }

            int selectedIndex = binding.SelectedIndex;
            MirGridType selectedGrid = binding.SelectedGrid;

            if (selectedIndex < 0)
            {
                if (clickedHasItem)
                {
                    binding.SelectedGrid = gridType;
                    binding.SelectedIndex = slotIndex;
                    MarkMobileTradeDirty();
                }
                return;
            }

            if (selectedGrid == gridType)
            {
                if (selectedIndex == slotIndex)
                {
                    binding.SelectedIndex = -1;
                    MarkMobileTradeDirty();
                    return;
                }

                if (clickedHasItem)
                {
                    binding.SelectedIndex = slotIndex;
                    MarkMobileTradeDirty();
                    return;
                }

                binding.SelectedIndex = -1;
                MarkMobileTradeDirty();
                return;
            }

            if (selectedGrid == MirGridType.Inventory && gridType == MirGridType.Trade)
            {
                if (selectedIndex >= 0 && selectedIndex < user.Inventory.Length && user.Inventory[selectedIndex] != null && user.Inventory[selectedIndex].Info != null)
                {
                    if (slotIndex < user.Trade.Length)
                    {
                        TrySendDepositTradeItem(selectedIndex, slotIndex);
                        binding.SelectedIndex = -1;
                        MarkMobileTradeDirty();
                    }
                }

                return;
            }

            if (selectedGrid == MirGridType.Trade && gridType == MirGridType.Inventory)
            {
                if (selectedIndex >= 0 && selectedIndex < user.Trade.Length && user.Trade[selectedIndex] != null && user.Trade[selectedIndex].Info != null)
                {
                    if (slotIndex < user.Inventory.Length)
                    {
                        TrySendRetrieveTradeItem(selectedIndex, slotIndex);
                        binding.SelectedIndex = -1;
                        MarkMobileTradeDirty();
                    }
                }

                return;
            }
        }

        private static void TrySendDepositTradeItem(int fromInventoryIndex, int toTradeIndex)
        {
            try
            {
                MonoShare.MirNetwork.Network.Enqueue(new C.DepositTradeItem { From = fromInventoryIndex, To = toTradeIndex });
            }
            catch (Exception ex)
            {
                CMain.SaveError("FairyGUI: 发送放入交易物品失败：" + ex.Message);
            }
        }

        private static void TrySendRetrieveTradeItem(int fromTradeIndex, int toInventoryIndex)
        {
            try
            {
                MonoShare.MirNetwork.Network.Enqueue(new C.RetrieveTradeItem { From = fromTradeIndex, To = toInventoryIndex });
            }
            catch (Exception ex)
            {
                CMain.SaveError("FairyGUI: 发送取回交易物品失败：" + ex.Message);
            }
        }

        private static void TrySubmitMobileTradeGold()
        {
            MobileTradeWindowBinding binding = _mobileTradeBinding;
            if (binding == null)
                return;

            var user = GameScene.User;
            if (user == null)
                return;

            if (user.TradeLocked)
            {
                GameScene.Scene?.MobileReceiveChat("[交易] 已锁定，无法添加金币。", ChatType.Hint);
                return;
            }

            string raw = string.Empty;
            try
            {
                raw = binding.GoldInput?.text ?? string.Empty;
            }
            catch
            {
                raw = string.Empty;
            }

            raw = raw?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(raw))
                return;

            if (!uint.TryParse(raw, out uint amount) || amount == 0)
            {
                GameScene.Scene?.MobileReceiveChat("[交易] 请输入有效的金币数量。", ChatType.Hint);
                return;
            }

            uint available = GameScene.Gold > user.TradeGoldAmount ? (GameScene.Gold - user.TradeGoldAmount) : 0;
            if (available == 0)
            {
                GameScene.Scene?.MobileReceiveChat("[交易] 金币不足。", ChatType.Hint);
                return;
            }

            if (amount > available)
                amount = available;

            try
            {
                user.TradeGoldAmount += amount;
                MonoShare.MirNetwork.Network.Enqueue(new C.TradeGold { Amount = amount });

                if (binding.GoldInput != null && !binding.GoldInput._disposed)
                    binding.GoldInput.text = string.Empty;
            }
            catch (Exception ex)
            {
                CMain.SaveError("FairyGUI: 发送交易金币失败：" + ex.Message);
                return;
            }

            MarkMobileTradeDirty();
        }

        private static void TryToggleMobileTradeLock()
        {
            var user = GameScene.User;
            if (user == null)
                return;

            try
            {
                user.TradeLocked = !user.TradeLocked;
                MonoShare.MirNetwork.Network.Enqueue(new C.TradeConfirm { Locked = user.TradeLocked });
            }
            catch (Exception ex)
            {
                CMain.SaveError("FairyGUI: 发送交易锁定失败：" + ex.Message);
                return;
            }

            MobileTradeWindowBinding binding = _mobileTradeBinding;
            if (binding != null)
                binding.SelectedIndex = -1;

            MarkMobileTradeDirty();
        }

        private static void TryCancelMobileTrade()
        {
            try
            {
                MonoShare.MirNetwork.Network.Enqueue(new C.TradeCancel());
            }
            catch (Exception ex)
            {
                CMain.SaveError("FairyGUI: 发送交易取消失败：" + ex.Message);
            }

            TryHideMobileWindow("Trade");
            EndMobileTrade();
        }
    }
}
