using System;
using System.Collections.Generic;
using System.Linq;
using FairyGUI;
using Microsoft.Xna.Framework;
using MonoShare.MirObjects;
using MonoShare.MirScenes;

namespace MonoShare;

/// <summary>FairyGUI seam for the Android item seal and rental flow.</summary>
internal static partial class FairyGuiHost
{
    private const string MobileSealRentalWindowKey = "SealRental";
    private const string MobileSealRentalFallbackName = "__codex_mobile_seal_rental_fallback";
    private const string MobileSealRentalPanelName = "__codex_mobile_seal_rental_panel";

    private static readonly string[] MobileSealRentalWindowKeywords =
        { "封印", "上锁", "租赁", "出租", "租借", "Rental", "Rent", "Seal" };

    private sealed class MobileSealRentalBinding
    {
        public GComponent Window;
        public GTextField Status;
        public GTextField SealStatus;
        public GTextField RentalStatus;
        public GTextField RentedList;
        public GTextInput FeeInput;
        public GTextInput PeriodInput;
        public GButton SealTab;
        public GButton RentalTab;
        public GComponent SealSection;
        public GComponent RentalSection;
        public readonly List<GButton> MaterialSlots = new List<GButton>(MobileSealRentalLayout.MaterialPageSize);
        public readonly List<GButton> TargetSlots = new List<GButton>(MobileSealRentalLayout.TargetPageSize);
        public readonly List<GButton> RentalItemSlots = new List<GButton>(MobileSealRentalLayout.RentalPageSize);
        public int MaterialPage;
        public int TargetPage;
        public int RentalItemPage;
        public int SelectedRentalSlot = -1;
        public ulong SelectedRentalUniqueId;
        public bool RentalTabSelected;
        public readonly List<GButton> Buttons = new List<GButton>(64);
        public readonly List<EventCallback0> Callbacks = new List<EventCallback0>(64);
    }

    private static MobileSealRentalBinding _mobileSealRentalBinding;
    private static bool _mobileSealRentalDirty = true;
    private static DateTime _nextMobileSealRentalRefreshUtc = DateTime.MinValue;

    public static void MarkMobileSealRentalDirty()
    {
        _mobileSealRentalDirty = true;
        TryRefreshMobileSealRentalIfDue(force: false);
    }

    public static bool TryToggleMobileSealRentalWindow(out bool nowVisible)
    {
        nowVisible = false;
        if (_stage == null || !_initialized || !_packagesLoaded)
            return false;

        try
        {
            if (MobileWindows.TryGetValue(MobileSealRentalWindowKey, out GComponent existing) &&
                existing != null && !existing._disposed)
            {
                existing.visible = !existing.visible;
                nowVisible = existing.visible;
                if (nowVisible)
                {
                    BringToFront(existing);
                    TryBindMobileSealRentalWindow(existing);
                    TryRefreshMobileSealRentalIfDue(force: true);
                }
                else
                {
                    if (GameScene.MobileSealRentalState.RentalSessionActive)
                        GameScene.Scene?.TryCancelMobileRental();
                    ResetMobileSealRentalBindings();
                }
                return true;
            }

            if (!TryCreateMobileWindowComponent(MobileSealRentalWindowKey, MobileSealRentalWindowKeywords,
                    out GComponent component, out string resolveInfo))
            {
                if (!TryCreateMobileSealRentalFallbackWindow(out component, out resolveInfo))
                    return false;
            }

            GComponent layer = _mobileOverlaySafeAreaRoot != null && !_mobileOverlaySafeAreaRoot._disposed
                ? _mobileOverlaySafeAreaRoot
                : (_uiManager?.OverlayLayer ?? GRoot.inst);
            layer.AddChild(component);
            component.AddRelation(layer, RelationType.Size);
            MobileWindows[MobileSealRentalWindowKey] = component;
            component.visible = true;
            nowVisible = true;
            TryBindMobileWindowCloseButton(MobileSealRentalWindowKey, component);
            TryBindMobileSealRentalWindow(component);
            TryRefreshMobileSealRentalIfDue(force: true);
            if (Settings.DebugMode && !string.IsNullOrWhiteSpace(resolveInfo))
                CMain.SaveLog("FairyGUI: 封印/租赁窗口已创建 -> " + resolveInfo);
            return true;
        }
        catch (Exception ex)
        {
            CMain.SaveError("FairyGUI: 封印/租赁窗口切换异常：" + ex.Message);
            return false;
        }
    }

    public static bool TryShowMobileSealRentalWindow()
    {
        if (MobileWindows.TryGetValue(MobileSealRentalWindowKey, out GComponent existing) &&
            existing != null && !existing._disposed)
        {
            existing.visible = true;
            BringToFront(existing);
            TryBindMobileSealRentalWindow(existing);
            TryRefreshMobileSealRentalIfDue(force: true);
            return true;
        }

        return TryToggleMobileSealRentalWindow(out _);
    }

    internal static void ResetMobileSealRentalBindings()
    {
        MobileSealRentalBinding binding = _mobileSealRentalBinding;
        _mobileSealRentalBinding = null;
        _nextMobileSealRentalRefreshUtc = DateTime.MinValue;
        _mobileSealRentalDirty = true;

        if (binding == null)
            return;

        for (int i = 0; i < binding.Buttons.Count && i < binding.Callbacks.Count; i++)
        {
            try { binding.Buttons[i]?.onClick.Remove(binding.Callbacks[i]); } catch { }
        }

        try { binding.FeeInput?.onFocusOut.Remove(OnMobileSealRentalFeeFocusOut); } catch { }
        try { binding.PeriodInput?.onFocusOut.Remove(OnMobileSealRentalPeriodFocusOut); } catch { }

        // The item buttons are generated from the current inventory. Remove
        // that panel on hide so reopening rebuilds a fresh, non-duplicated
        // list after inventory changes.
        try
        {
            GObject generatedPanel = binding.Window?.GetChild(MobileSealRentalPanelName);
            if (generatedPanel != null && generatedPanel.parent != null)
                generatedPanel.parent.RemoveChild(generatedPanel, dispose: true);
        }
        catch { }
    }

    private static void TryBindMobileSealRentalWindow(GComponent window)
    {
        if (window == null || window._disposed)
            return;

        if (_mobileSealRentalBinding != null &&
            ReferenceEquals(_mobileSealRentalBinding.Window, window))
            return;

        ResetMobileSealRentalBindings();
        var binding = new MobileSealRentalBinding { Window = window };

        // The generated interaction panel is deliberately added on top of a
        // published component too.  It keeps the two-step item picker and all
        // server packet operations reachable when a package only contains a
        // decorative rental window or has no matching child names.
        BuildMobileSealRentalControls(binding);
        _mobileSealRentalBinding = binding;
        _mobileSealRentalDirty = true;
        _nextMobileSealRentalRefreshUtc = DateTime.MinValue;
    }

    private static bool TryCreateMobileSealRentalFallbackWindow(out GComponent component, out string resolveInfo)
    {
        component = null;
        resolveInfo = null;
        try
        {
            GComponent layer = _mobileOverlaySafeAreaRoot != null && !_mobileOverlaySafeAreaRoot._disposed
                ? _mobileOverlaySafeAreaRoot
                : (_uiManager?.OverlayLayer ?? GRoot.inst);
            float width = Math.Max(1F, layer?.width ?? GRoot.inst?.width ?? 720F);
            float height = Math.Max(1F, layer?.height ?? GRoot.inst?.height ?? 1280F);
            component = new GComponent
            {
                name = MobileSealRentalFallbackName,
                touchable = true,
                opaque = false,
            };
            component.SetSize(width, height);
            resolveInfo = "fallback";
            return true;
        }
        catch
        {
            try { component?.Dispose(); } catch { }
            component = null;
            return false;
        }
    }

    private static void BuildMobileSealRentalControls(MobileSealRentalBinding binding)
    {
        GComponent root = binding.Window;
        if (root == null || root._disposed)
            return;

        float width = Math.Max(1F, root.width > 1F ? root.width : (GRoot.inst?.width ?? 720F));
        float height = Math.Max(1F, root.height > 1F ? root.height : (GRoot.inst?.height ?? 1280F));
        MobileSealRentalLayout.Bounds layout = MobileSealRentalLayout.GetPanel(width, height);
        float panelWidth = layout.Width;
        float panelHeight = layout.Height;

        var panel = new GComponent
        {
            name = MobileSealRentalPanelName,
            touchable = true,
            opaque = true,
        };
        panel.SetSize(panelWidth, panelHeight);
        panel.SetPosition(layout.X, layout.Y);
        root.AddChild(panel);

        var background = new GGraph { name = "seal_rental_background", touchable = false };
        background.DrawRect(panelWidth, panelHeight, 2, new Color(120, 150, 195, 255), new Color(24, 30, 46, 250));
        panel.AddChild(background);

        float contentWidth = Math.Max(1F, panelWidth - 24F);
        float buttonWidth = Math.Max(1F, (contentWidth - 12F) / 3F);
        AddSealRentalText(panel, "seal_rental_title", "物品封印 / 租赁", 12F, 8F, panelWidth - 88F, 28F, 20, Color.White, true);
        AddSealRentalButton(panel, binding, "closeButton", "关闭", panelWidth - 72F, 8F, 60F, 30F, () =>
        {
            TryHideMobileWindow(MobileSealRentalWindowKey);
        });

        binding.Status = AddSealRentalText(panel, "seal_rental_status", string.Empty, 12F, 42F, contentWidth, 28F, 14, Color.LightGray, false);
        float tabY = 74F;
        float tabWidth = Math.Max(1F, (contentWidth - 8F) / 2F);
        binding.SealTab = AddSealRentalButton(panel, binding, "seal_tab", "封印", 12F, tabY, tabWidth, 30F,
            () => SetMobileSealRentalTab(binding, rental: false));
        binding.RentalTab = AddSealRentalButton(panel, binding, "rental_tab", "租赁", 12F + tabWidth + 8F, tabY, tabWidth, 30F,
            () => SetMobileSealRentalTab(binding, rental: true));

        float sectionY = tabY + 38F;
        float sectionHeight = Math.Max(1F, panelHeight - sectionY - 12F);
        binding.SealSection = new GComponent { name = "seal_section", touchable = true };
        binding.SealSection.SetPosition(12F, sectionY);
        binding.SealSection.SetSize(contentWidth, sectionHeight);
        panel.AddChild(binding.SealSection);
        BuildMobileSealSection(binding, buttonWidth);

        binding.RentalSection = new GComponent { name = "rental_section", touchable = true, visible = false };
        binding.RentalSection.SetPosition(12F, sectionY);
        binding.RentalSection.SetSize(contentWidth, sectionHeight);
        panel.AddChild(binding.RentalSection);
        BuildMobileRentalSection(binding, buttonWidth);
        SetMobileSealRentalTab(binding, rental: false);
    }

    private static void BuildMobileSealSection(MobileSealRentalBinding binding, float buttonWidth)
    {
        GComponent section = binding.SealSection;
        float gap = 6F;
        float colWidth = Math.Max(1F, (section.width - gap * 2F) / 3F);
        binding.SealStatus = AddSealRentalText(section, "seal_section_status", string.Empty, 0F, 0F, section.width, 28F, 14, new Color(240, 225, 190, 255), false);
        AddSealRentalText(section, "seal_material_label", "① 选择封印材料（宝玉神珠 Shape=8）", 0F, 30F, section.width, 24F, 14, Color.White, true);
        for (int slot = 0; slot < MobileSealRentalLayout.MaterialPageSize; slot++)
        {
            int localSlot = slot;
            float x = (slot % 3) * (colWidth + gap);
            float y = 56F + (slot / 3) * 34F;
            binding.MaterialSlots.Add(AddSealRentalButton(section, binding, "seal_material_slot_" + slot, "—", x, y, colWidth, 30F,
                () => SelectMobileSealMaterialSlot(binding, localSlot)));
        }
        AddSealRentalButton(section, binding, "seal_material_prev", "上一页", 0F, 128F, colWidth, 30F,
            () => ChangeSealRentalPage(binding, materialDelta: -1, targetDelta: 0, rentalDelta: 0));
        AddSealRentalButton(section, binding, "seal_material_next", "下一页", colWidth + gap, 128F, colWidth, 30F,
            () => ChangeSealRentalPage(binding, materialDelta: 1, targetDelta: 0, rentalDelta: 0));

        AddSealRentalText(section, "seal_target_label", "② 选择目标装备", 0F, 164F, section.width, 24F, 14, Color.White, true);
        for (int slot = 0; slot < MobileSealRentalLayout.TargetPageSize; slot++)
        {
            int localSlot = slot;
            float x = (slot % 3) * (colWidth + gap);
            float y = 190F + (slot / 3) * 34F;
            binding.TargetSlots.Add(AddSealRentalButton(section, binding, "seal_target_slot_" + slot, "—", x, y, colWidth, 30F,
                () => SelectMobileSealTargetSlot(binding, localSlot)));
        }
        AddSealRentalButton(section, binding, "seal_target_prev", "上一页", 0F, 298F, colWidth, 30F,
            () => ChangeSealRentalPage(binding, materialDelta: 0, targetDelta: -1, rentalDelta: 0));
        AddSealRentalButton(section, binding, "seal_target_next", "下一页", colWidth + gap, 298F, colWidth, 30F,
            () => ChangeSealRentalPage(binding, materialDelta: 0, targetDelta: 1, rentalDelta: 0));
        AddSealRentalButton(section, binding, "seal_confirm", "确认封印", 2F * (colWidth + gap), 298F, colWidth, 30F,
            () => GameScene.Scene?.TryConfirmMobileSeal());
    }

    private static void BuildMobileRentalSection(MobileSealRentalBinding binding, float buttonWidth)
    {
        GComponent section = binding.RentalSection;
        float gap = 6F;
        float colWidth = Math.Max(1F, (section.width - gap * 2F) / 3F);
        AddSealRentalText(section, "rental_label", "租赁流程（服务端面对面校验）", 0F, 0F, section.width, 24F, 14, Color.White, true);
        binding.RentalStatus = AddSealRentalText(section, "rental_status", string.Empty, 0F, 24F, section.width, 42F, 13, Color.LightGray, false);
        AddSealRentalText(section, "rental_item_label", "选择要押入的可租赁物品", 0F, 68F, section.width, 22F, 13, Color.White, true);
        for (int slot = 0; slot < MobileSealRentalLayout.RentalPageSize; slot++)
        {
            int localSlot = slot;
            float x = (slot % 3) * (colWidth + gap);
            float y = 94F + (slot / 3) * 34F;
            binding.RentalItemSlots.Add(AddSealRentalButton(section, binding, "rental_item_slot_" + slot, "—", x, y, colWidth, 30F,
                () => SelectMobileRentalItemSlot(binding, localSlot)));
        }
        AddSealRentalButton(section, binding, "rental_item_prev", "上一页", 0F, 164F, colWidth, 30F,
            () => ChangeSealRentalPage(binding, materialDelta: 0, targetDelta: 0, rentalDelta: -1));
        AddSealRentalButton(section, binding, "rental_item_next", "下一页", colWidth + gap, 164F, colWidth, 30F,
            () => ChangeSealRentalPage(binding, materialDelta: 0, targetDelta: 0, rentalDelta: 1));
        AddSealRentalButton(section, binding, "rental_request", "请求租赁", 2F * (colWidth + gap), 164F, colWidth, 30F,
            () => GameScene.Scene?.TryBeginMobileRentalRequest());
        AddSealRentalButton(section, binding, "rental_deposit", "押入所选物品", 0F, 200F, colWidth, 30F,
            () => GameScene.Scene?.TryDepositMobileRentalItem(binding.SelectedRentalSlot, binding.SelectedRentalUniqueId));
        AddSealRentalButton(section, binding, "rental_retrieve", "取回物品", colWidth + gap, 200F, colWidth, 30F,
            () => GameScene.Scene?.TryRetrieveMobileRentalItem(FindFirstFreeInventoryIndex()));

        AddSealRentalText(section, "rental_period_label", "租期（1-30 天）", 2F * (colWidth + gap), 200F, colWidth, 22F, 12, Color.White, false);
        var periodInput = new GTextInput { name = "rental_period_input", text = "7", touchable = true };
        periodInput.SetPosition(2F * (colWidth + gap), 224F);
        periodInput.SetSize(colWidth, 30F);
        try { periodInput.textFormat.size = 14; periodInput.textFormat.color = Color.White; } catch { }
        section.AddChild(periodInput);
        binding.PeriodInput = periodInput;
        periodInput.onFocusOut.Add(OnMobileSealRentalPeriodFocusOut);
        AddSealRentalButton(section, binding, "rental_period_submit", "提交租期", 2F * (colWidth + gap), 258F, colWidth, 30F,
            () => SubmitMobileSealRentalPeriod(binding));

        var feeInput = new GTextInput
        {
            name = "rental_fee_input",
            text = "100",
            touchable = true,
        };
        feeInput.SetPosition(0F, 224F);
        feeInput.SetSize(colWidth, 30F);
        try
        {
            feeInput.textFormat.size = 16;
            feeInput.textFormat.color = Color.White;
        }
        catch { }
        section.AddChild(feeInput);
        binding.FeeInput = feeInput;
        feeInput.onFocusOut.Add(OnMobileSealRentalFeeFocusOut);

        AddSealRentalText(section, "rental_fee_label", "租金", 0F, 200F, colWidth, 22F, 12, Color.White, false);
        AddSealRentalButton(section, binding, "rental_fee", "提交租金", colWidth + gap, 224F, colWidth, 30F,
            () => SubmitMobileSealRentalFee(binding));
        AddSealRentalButton(section, binding, "rental_period_1", "租期 1 天", 0F, 258F, colWidth, 30F,
            () => GameScene.Scene?.TrySetMobileRentalPeriod(1));
        AddSealRentalButton(section, binding, "rental_period_7", "租期 7 天", colWidth + gap, 258F, colWidth, 30F,
            () => GameScene.Scene?.TrySetMobileRentalPeriod(7));
        AddSealRentalButton(section, binding, "rental_period_30", "租期 30 天", 0F, 292F, colWidth, 30F,
            () => GameScene.Scene?.TrySetMobileRentalPeriod(30));
        AddSealRentalButton(section, binding, "rental_lock_fee", "锁定租金", colWidth + gap, 292F, colWidth, 30F,
            () => GameScene.Scene?.TryLockMobileRentalFee());
        AddSealRentalButton(section, binding, "rental_lock_item", "锁定物品", 2F * (colWidth + gap), 292F, colWidth, 30F,
            () => GameScene.Scene?.TryLockMobileRentalItem());
        AddSealRentalButton(section, binding, "rental_confirm", "确认租赁", 0F, 326F, colWidth, 30F,
            () => GameScene.Scene?.TryConfirmMobileRental());
        AddSealRentalButton(section, binding, "rental_cancel", "取消租赁", colWidth + gap, 326F, colWidth, 30F,
            () => GameScene.Scene?.TryCancelMobileRental());

        binding.RentedList = AddSealRentalText(section, "rental_list", string.Empty, 0F, 362F, section.width,
            Math.Max(60F, section.height - 362F), 12, Color.LightGray, false);
    }

    private static GTextField AddSealRentalText(GComponent parent, string name, string text,
        float x, float y, float width, float height, int size, Color color, bool bold)
    {
        var field = new GTextField
        {
            name = name,
            text = text ?? string.Empty,
            touchable = false,
            align = AlignType.Left,
            verticalAlign = VertAlignType.Middle,
            autoSize = AutoSizeType.None,
            singleLine = false,
        };
        field.SetPosition(x, y);
        field.SetSize(Math.Max(1F, width), Math.Max(1F, height));
        try
        {
            field.textFormat.size = size;
            field.textFormat.color = color;
            field.textFormat.bold = bold;
        }
        catch { }
        parent.AddChild(field);
        return field;
    }

    private static GButton AddSealRentalButton(GComponent parent, MobileSealRentalBinding binding,
        string name, string title, float x, float y, float width, float height, Action action)
    {
        var button = new GButton
        {
            name = name,
            title = title ?? string.Empty,
            touchable = true,
            enabled = true,
            grayed = false,
            opaque = true,
            changeStateOnClick = false,
        };
        button.SetPosition(x, y);
        button.SetSize(Math.Max(1F, width), Math.Max(1F, height));
        var background = new GGraph { name = name + "_background", touchable = false };
        background.DrawRect(width, height, 2, new Color(120, 150, 195, 255), new Color(50, 75, 110, 255));
        button.AddChild(background);
        var label = new GTextField
        {
            name = "title",
            text = title ?? string.Empty,
            touchable = false,
            align = AlignType.Center,
            verticalAlign = VertAlignType.Middle,
            autoSize = AutoSizeType.None,
        };
        label.SetSize(width, height);
        try
        {
            label.textFormat.size = 14;
            label.textFormat.color = Color.White;
            label.textFormat.bold = true;
        }
        catch { }
        button.AddChild(label);
        EventCallback0 callback = () =>
        {
            try { action?.Invoke(); } catch (Exception ex) { CMain.SaveError("FairyGUI: 封印/租赁按钮异常：" + ex.Message); }
            TryRefreshMobileSealRentalIfDue(force: true);
        };
        button.onClick.Add(callback);
        binding.Buttons.Add(button);
        binding.Callbacks.Add(callback);
        parent.AddChild(button);
        return button;
    }

    private static void OnMobileSealRentalFeeFocusOut(EventContext context)
    {
        TryRefreshMobileSealRentalIfDue(force: true);
    }

    private static void OnMobileSealRentalPeriodFocusOut(EventContext context)
    {
        TryRefreshMobileSealRentalIfDue(force: true);
    }

    private static void SetMobileSealRentalTab(MobileSealRentalBinding binding, bool rental)
    {
        if (binding == null)
            return;

        binding.RentalTabSelected = rental;
        try { if (binding.SealSection != null) binding.SealSection.visible = !rental; } catch { }
        try { if (binding.RentalSection != null) binding.RentalSection.visible = rental; } catch { }
        // Keep the current tab disabled and the other tab clickable.  The
        // previous polarity made the initial Seal tab trap users there.
        MobileSealRentalLayout.PanelTab selected = rental
            ? MobileSealRentalLayout.PanelTab.Rental
            : MobileSealRentalLayout.PanelTab.Seal;
        SetSealRentalButtonEnabled(binding, "seal_tab",
            MobileSealRentalLayout.IsTabEnabled(selected, MobileSealRentalLayout.PanelTab.Seal));
        SetSealRentalButtonEnabled(binding, "rental_tab",
            MobileSealRentalLayout.IsTabEnabled(selected, MobileSealRentalLayout.PanelTab.Rental));
        TryRefreshMobileSealRentalIfDue(force: true);
    }

    private static void ChangeSealRentalPage(MobileSealRentalBinding binding,
        int materialDelta, int targetDelta, int rentalDelta)
    {
        if (binding == null)
            return;

        binding.MaterialPage = Math.Max(0, binding.MaterialPage + materialDelta);
        binding.TargetPage = Math.Max(0, binding.TargetPage + targetDelta);
        binding.RentalItemPage = Math.Max(0, binding.RentalItemPage + rentalDelta);
        MarkMobileSealRentalDirty();
        TryRefreshMobileSealRentalIfDue(force: true);
    }

    private static void SelectMobileSealMaterialSlot(MobileSealRentalBinding binding, int slot)
    {
        List<UserItem> materials = GetMobileSealMaterials();
        int index = binding.MaterialPage * MobileSealRentalLayout.MaterialPageSize + slot;
        if (index >= 0 && index < materials.Count)
            GameScene.Scene?.TrySelectMobileSealMaterial(materials[index].UniqueID);
        else
            GameScene.Scene?.MobileReceiveChat("封印：当前页没有该材料。", ChatType.System);
    }

    private static void SelectMobileSealTargetSlot(MobileSealRentalBinding binding, int slot)
    {
        List<UserItem> targets = GetMobileSealTargets();
        int index = binding.TargetPage * MobileSealRentalLayout.TargetPageSize + slot;
        if (index >= 0 && index < targets.Count)
            GameScene.Scene?.TrySelectMobileSealTarget(targets[index].UniqueID);
        else
            GameScene.Scene?.MobileReceiveChat("封印：当前页没有该目标装备。", ChatType.System);
    }

    private static void SelectMobileRentalItemSlot(MobileSealRentalBinding binding, int slot)
    {
        List<KeyValuePair<int, UserItem>> items = GetMobileRentalItems();
        int index = binding.RentalItemPage * MobileSealRentalLayout.RentalPageSize + slot;
        if (index < 0 || index >= items.Count)
        {
            binding.SelectedRentalSlot = -1;
            binding.SelectedRentalUniqueId = 0;
            MarkMobileSealRentalDirty();
            return;
        }

        binding.SelectedRentalSlot = items[index].Key;
        binding.SelectedRentalUniqueId = items[index].Value.UniqueID;
        MarkMobileSealRentalDirty();
    }

    private static List<UserItem> GetMobileSealMaterials()
    {
        var result = new List<UserItem>();
        UserItem[] inventory = GameScene.User?.Inventory;
        if (inventory == null)
            return result;
        for (int i = 0; i < inventory.Length; i++)
        {
            if (MobileSealRentalState.IsSealMaterial(inventory[i]))
                result.Add(inventory[i]);
        }
        return result;
    }

    private static List<UserItem> GetMobileSealTargets()
    {
        var result = new List<UserItem>();
        UserItem[] inventory = GameScene.User?.Inventory;
        if (inventory == null)
            return result;
        for (int i = 0; i < inventory.Length; i++)
        {
            if (MobileSealRentalState.IsSealTarget(inventory[i]))
                result.Add(inventory[i]);
        }
        return result;
    }

    private static List<KeyValuePair<int, UserItem>> GetMobileRentalItems()
    {
        var result = new List<KeyValuePair<int, UserItem>>();
        UserItem[] inventory = GameScene.User?.Inventory;
        if (inventory == null)
            return result;
        for (int i = 0; i < inventory.Length; i++)
        {
            if (MobileSealRentalState.IsRentableItem(inventory[i]))
                result.Add(new KeyValuePair<int, UserItem>(i, inventory[i]));
        }
        return result;
    }

    private static void SubmitMobileSealRentalPeriod(MobileSealRentalBinding binding)
    {
        if (binding?.PeriodInput == null || !uint.TryParse(binding.PeriodInput.text, out uint days))
        {
            GameScene.Scene?.MobileReceiveChat("租赁：请输入1至30天的租期。", ChatType.System);
            return;
        }
        GameScene.Scene?.TrySetMobileRentalPeriod(days);
    }

    private static void SubmitMobileSealRentalFee(MobileSealRentalBinding binding)
    {
        if (binding?.FeeInput == null)
            return;
        if (!uint.TryParse(binding.FeeInput.text, out uint amount) || amount == 0)
        {
            GameScene.Scene?.MobileReceiveChat("租赁：请输入有效租金。", ChatType.System);
            return;
        }
        GameScene.Scene?.TrySetMobileRentalFee(amount);
    }

    private static void TryRefreshMobileSealRentalIfDue(bool force)
    {
        MobileSealRentalBinding binding = _mobileSealRentalBinding;
        if (binding == null || binding.Window == null || binding.Window._disposed || !binding.Window.visible)
            return;
        if (!force && !_mobileSealRentalDirty && DateTime.UtcNow < _nextMobileSealRentalRefreshUtc)
            return;

        _mobileSealRentalDirty = false;
        _nextMobileSealRentalRefreshUtc = DateTime.UtcNow.AddMilliseconds(350);
        MobileSealRentalState state = GameScene.MobileSealRentalState;

        SetSealRentalText(binding.Status, state.RentalSessionActive
            ? "租赁对象：" + state.RentalPartnerName + (state.IsRenting ? "（租入方）" : "（出租方）")
            : "未进入租赁会话。" + (state.RentedItems.Count > 0 ? " 已加载出租记录。" : string.Empty));
        SetSealRentalText(binding.SealStatus, BuildSealStatusText(state));
        SetSealRentalText(binding.RentalStatus, BuildRentalStatusText(state));
        SetSealRentalText(binding.RentedList, BuildRentedListText(state));

        bool pendingSeal = state.SealRequestPending;
        bool rentalBusy = state.RentalSessionActive || state.PendingRentalOperation != MobileSealRentalState.RentalOperation.None;
        bool canSeal = state.HasSealSelection && !pendingSeal && !rentalBusy;
        bool sealPickerAllowed = !pendingSeal && !rentalBusy;
        bool activeRental = state.RentalSessionActive;
        bool rentalIdle = activeRental && !pendingSeal && state.PendingRentalOperation == MobileSealRentalState.RentalOperation.None;
        RefreshMobileSealCandidates(binding, state, sealPickerAllowed);
        RefreshMobileRentalCandidates(binding, state, rentalIdle);
        SetSealRentalButtonEnabled(binding, "seal_confirm", canSeal);
        SetSealRentalButtonEnabled(binding, "rental_request", !activeRental && !pendingSeal && state.PendingRentalOperation == MobileSealRentalState.RentalOperation.None);
        SetSealRentalButtonEnabled(binding, "rental_deposit", rentalIdle && !state.IsRenting && state.RentalDepositedItem == null && binding.SelectedRentalSlot >= 0);
        SetSealRentalButtonEnabled(binding, "rental_retrieve", rentalIdle && !state.IsRenting &&
            !state.LocalItemLocked && state.RentalDepositedItem != null);
        SetSealRentalButtonEnabled(binding, "rental_fee", rentalIdle && state.IsRenting && !state.LocalFeeLocked);
        SetSealRentalButtonEnabled(binding, "rental_period_submit", rentalIdle && !state.IsRenting && !state.LocalItemLocked);
        SetSealRentalButtonEnabled(binding, "rental_period_1", rentalIdle && !state.IsRenting && !state.LocalItemLocked);
        SetSealRentalButtonEnabled(binding, "rental_period_7", rentalIdle && !state.IsRenting && !state.LocalItemLocked);
        SetSealRentalButtonEnabled(binding, "rental_period_30", rentalIdle && !state.IsRenting && !state.LocalItemLocked);
        SetSealRentalButtonEnabled(binding, "rental_lock_fee", rentalIdle && state.IsRenting && state.RentalFee > 0 && !state.LocalFeeLocked);
        SetSealRentalButtonEnabled(binding, "rental_lock_item", rentalIdle && !state.IsRenting && state.RentalDepositedItem != null && !state.LocalItemLocked);
        SetSealRentalButtonEnabled(binding, "rental_confirm", rentalIdle && !state.IsRenting && state.CanConfirmRental &&
            state.RentalDays >= MobileSealRentalState.MinRentalDays && state.RentalDays <= MobileSealRentalState.MaxRentalDays &&
            state.RentalFee > 0 && state.RentalDepositedItem != null && state.LocalItemLocked && state.PartnerFeeLocked);
        SetSealRentalButtonEnabled(binding, "rental_cancel", activeRental && !pendingSeal && state.PendingRentalOperation != MobileSealRentalState.RentalOperation.Cancel);
    }

    private static void RefreshMobileSealCandidates(MobileSealRentalBinding binding,
        MobileSealRentalState state, bool enabled)
    {
        List<UserItem> materials = GetMobileSealMaterials();
        List<UserItem> targets = GetMobileSealTargets();
        binding.MaterialPage = ClampPage(binding.MaterialPage, materials.Count, MobileSealRentalLayout.MaterialPageSize);
        binding.TargetPage = ClampPage(binding.TargetPage, targets.Count, MobileSealRentalLayout.TargetPageSize);

        for (int i = 0; i < binding.MaterialSlots.Count; i++)
        {
            int index = binding.MaterialPage * MobileSealRentalLayout.MaterialPageSize + i;
            bool present = index >= 0 && index < materials.Count;
            SetSealRentalButtonTitle(binding.MaterialSlots[i], present ? TruncateSealRentalText(materials[index].Info.FriendlyName, 14) : "—");
            SetSealRentalButtonEnabled(binding.MaterialSlots[i], enabled && present);
        }
        for (int i = 0; i < binding.TargetSlots.Count; i++)
        {
            int index = binding.TargetPage * MobileSealRentalLayout.TargetPageSize + i;
            bool present = index >= 0 && index < targets.Count;
            SetSealRentalButtonTitle(binding.TargetSlots[i], present ? TruncateSealRentalText(targets[index].Info.FriendlyName, 14) : "—");
            SetSealRentalButtonEnabled(binding.TargetSlots[i], enabled && present);
        }

        SetSealRentalButtonEnabled(binding, "seal_material_prev", enabled && binding.MaterialPage > 0);
        SetSealRentalButtonEnabled(binding, "seal_material_next", enabled && (binding.MaterialPage + 1) * MobileSealRentalLayout.MaterialPageSize < materials.Count);
        SetSealRentalButtonEnabled(binding, "seal_target_prev", enabled && binding.TargetPage > 0);
        SetSealRentalButtonEnabled(binding, "seal_target_next", enabled && (binding.TargetPage + 1) * MobileSealRentalLayout.TargetPageSize < targets.Count);
        MobileSealRentalLayout.PanelTab selected = binding.RentalTabSelected
            ? MobileSealRentalLayout.PanelTab.Rental
            : MobileSealRentalLayout.PanelTab.Seal;
        SetSealRentalButtonEnabled(binding, "seal_tab",
            MobileSealRentalLayout.IsTabEnabled(selected, MobileSealRentalLayout.PanelTab.Seal));
        SetSealRentalButtonEnabled(binding, "rental_tab",
            MobileSealRentalLayout.IsTabEnabled(selected, MobileSealRentalLayout.PanelTab.Rental));
    }

    private static void RefreshMobileRentalCandidates(MobileSealRentalBinding binding,
        MobileSealRentalState state, bool enabled)
    {
        List<KeyValuePair<int, UserItem>> items = GetMobileRentalItems();
        binding.RentalItemPage = ClampPage(binding.RentalItemPage, items.Count, MobileSealRentalLayout.RentalPageSize);
        int selectedIndex = -1;
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i].Key == binding.SelectedRentalSlot && items[i].Value.UniqueID == binding.SelectedRentalUniqueId)
            {
                selectedIndex = i;
                break;
            }
        }
        if (selectedIndex < 0)
        {
            binding.SelectedRentalSlot = -1;
            binding.SelectedRentalUniqueId = 0;
        }

        for (int i = 0; i < binding.RentalItemSlots.Count; i++)
        {
            int index = binding.RentalItemPage * MobileSealRentalLayout.RentalPageSize + i;
            bool present = index >= 0 && index < items.Count;
            bool selected = present && items[index].Key == binding.SelectedRentalSlot && items[index].Value.UniqueID == binding.SelectedRentalUniqueId;
            string title = present ? TruncateSealRentalText(items[index].Value.Info.FriendlyName, 14) : "—";
            SetSealRentalButtonTitle(binding.RentalItemSlots[i], selected ? "✓ " + title : title);
            SetSealRentalButtonEnabled(binding.RentalItemSlots[i], enabled && present);
        }
        SetSealRentalButtonEnabled(binding, "rental_item_prev", enabled && binding.RentalItemPage > 0);
        SetSealRentalButtonEnabled(binding, "rental_item_next", enabled && (binding.RentalItemPage + 1) * MobileSealRentalLayout.RentalPageSize < items.Count);
    }

    private static int ClampPage(int page, int itemCount, int pageSize)
    {
        int maxPage = itemCount <= 0 ? 0 : (itemCount - 1) / pageSize;
        return Math.Max(0, Math.Min(page, maxPage));
    }

    private static string BuildSealStatusText(MobileSealRentalState state)
    {
        if (!string.IsNullOrWhiteSpace(state.Error) &&
            MobileSealRentalState.ClassifyError(state.Error) != MobileSealRentalState.ErrorDomain.Rental)
            return MobileSealRentalState.FormatError(MobileSealRentalState.ErrorDomain.Seal, state.Error);
        if (state.SealRequestPending)
            return "封印：正在等待服务端确认……";
        if (state.LastSealSucceeded == true)
            return "封印：成功，目标物品已更新。";
        if (state.HasSealTarget)
            return "封印：已选择材料和目标，点击确认封印。";
        if (state.HasSealMaterial)
            return "封印：材料已选择，请继续选择目标装备。";
        return "封印：先选材料，再选目标装备。";
    }

    private static string BuildRentalStatusText(MobileSealRentalState state)
    {
        if (!string.IsNullOrWhiteSpace(state.Error) &&
            MobileSealRentalState.ClassifyError(state.Error) == MobileSealRentalState.ErrorDomain.Rental)
            return state.Error;
        if (!state.RentalSessionActive)
            return "租赁：请求面对面租赁后，按角色完成押入/报价/租期/锁定/确认。";

        string item = state.RentalDepositedItem?.FriendlyName ?? state.RentalLoanItem?.FriendlyName ?? "未押入物品";
        return "租赁：" + state.RentalPartnerName + "  物品：" + item +
               "  租金：" + state.RentalFee + "  租期：" + state.RentalDays + "天" +
               "  锁定：" + (state.LocalFeeLocked ? "租金 " : string.Empty) + (state.LocalItemLocked ? "物品" : string.Empty) +
               (state.CanConfirmRental ? "  双方已满足确认条件。" : string.Empty);
    }

    private static string BuildRentedListText(MobileSealRentalState state)
    {
        if (state.RentedItems.Count == 0)
            return "出租记录：暂无。";

        var lines = new List<string> { "出租记录：" };
        for (int i = 0; i < state.RentedItems.Count; i++)
        {
            MobileSealRentalState.RentedItemSnapshot item = state.RentedItems[i];
            lines.Add("· " + item.ItemName + " → " + item.RentingPlayerName + "，到期 " + item.ItemReturnDate.ToShortDateString());
        }
        return string.Join("\n", lines);
    }

    private static void SetSealRentalText(GTextField field, string value)
    {
        try { if (field != null && !field._disposed) field.text = value ?? string.Empty; } catch { }
    }

    private static void SetSealRentalButtonTitle(GButton button, string title)
    {
        if (button == null || button._disposed)
            return;
        try
        {
            button.title = title ?? string.Empty;
            if (button.GetChild("title") is GTextField label)
                label.text = title ?? string.Empty;
        }
        catch { }
    }

    private static void SetSealRentalButtonEnabled(GButton button, bool enabled)
    {
        if (button == null || button._disposed)
            return;
        try { button.enabled = enabled; button.grayed = !enabled; button.touchable = enabled; } catch { }
    }

    private static void SetSealRentalButtonEnabled(MobileSealRentalBinding binding, string name, bool enabled)
    {
        if (binding == null)
            return;
        for (int i = 0; i < binding.Buttons.Count; i++)
        {
            GButton button = binding.Buttons[i];
            if (button == null || button._disposed || !string.Equals(button.name, name, StringComparison.OrdinalIgnoreCase))
                continue;
            try { button.enabled = enabled; button.grayed = !enabled; button.touchable = enabled; } catch { }
        }
    }

    private static int FindFirstFreeInventoryIndex()
    {
        UserObject user = GameScene.User;
        if (user?.Inventory == null)
            return -1;
        for (int i = 0; i < user.Inventory.Length; i++)
            if (user.Inventory[i] == null)
                return i;
        return -1;
    }

    private static string TruncateSealRentalText(string value, int maxLength)
    {
        string text = value ?? string.Empty;
        if (text.Length <= maxLength)
            return text;
        return text.Substring(0, Math.Max(1, maxLength - 1)) + "…";
    }
}
