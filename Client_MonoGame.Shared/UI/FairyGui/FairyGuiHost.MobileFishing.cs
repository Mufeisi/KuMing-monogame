using System;
using System.Collections.Generic;
using System.Linq;
using C = ClientPackets;
using FairyGUI;
using Microsoft.Xna.Framework;
using MonoShare.MirObjects;
using MonoShare.MirScenes;

namespace MonoShare;

/// <summary>FairyGUI seam for the Android fishing flow.</summary>
internal static partial class FairyGuiHost
{
    private const string MobileFishingWindowKey = "Fishing";
    private const string MobileFishingFallbackName = "__codex_mobile_fishing_fallback";
    private const string MobileFishingPickerName = "fishing_inventory_picker";

    private static readonly string[] MobileFishingWindowKeywords =
        { "钓鱼", "鱼竿", "fishing", "fish", "rod" };
    private static readonly string[] MobileFishingStatusKeywords =
        { "status", "state", "fishingstatus", "钓鱼", "状态" };
    private static readonly string[] MobileFishingProgressKeywords =
        { "progress", "进度", "百分比" };
    private static readonly string[] MobileFishingChanceKeywords =
        { "chance", "success", "成功率", "几率" };
    private static readonly string[] MobileFishingErrorKeywords =
        { "error", "fail", "message", "错误", "失败", "提示" };
    private static readonly string[] MobileFishingCastKeywords =
        { "cast", "throw", "抛竿", "开始", "钓鱼" };
    private static readonly string[] MobileFishingReelKeywords =
        { "reel", "收线", "收杆", "结束" };
    private static readonly string[] MobileFishingAutocastKeywords =
        { "autocast", "auto", "自动抛竿", "自动钓鱼", "自动" };
    private static readonly string[][] MobileFishingSlotKeywords =
    {
        new[] { "hook", "鱼钩" },
        new[] { "float", "bobber", "鱼漂" },
        new[] { "bait", "鱼饵" },
        new[] { "finder", "探鱼器" },
        new[] { "reel", "摇轮" },
    };

    private sealed class MobileFishingWindowBinding
    {
        public GComponent Window;
        public GTextField Status;
        public GTextField Progress;
        public GTextField Chance;
        public GTextField Found;
        public GTextField Error;
        public GButton Cast;
        public GButton Reel;
        public GButton AutoCast;
        public readonly GObject[] Slots = new GObject[5];
        public readonly EventCallback0[] SlotClickCallbacks = new EventCallback0[5];
        public readonly EventCallback1[] SlotDropCallbacks = new EventCallback1[5];
        public EventCallback0 CastCallback;
        public EventCallback0 ReelCallback;
        public EventCallback0 AutoCastCallback;
        public GComponent Picker;
        public GTextField PickerTitle;
        public GTextField PickerPage;
        public GTextField PickerEmpty;
        public GButton PickerPrevious;
        public GButton PickerNext;
        public GButton PickerClose;
        public readonly GButton[] PickerItems = new GButton[MobileFishingInventoryPolicy.DefaultPageSize];
        public readonly EventCallback0[] PickerItemCallbacks = new EventCallback0[MobileFishingInventoryPolicy.DefaultPageSize];
        public EventCallback0 PickerPreviousCallback;
        public EventCallback0 PickerNextCallback;
        public EventCallback0 PickerCloseCallback;
        public int PickerSlot = -1;
        public int PickerPageIndex;
        public readonly int[] PickerCandidateInventoryIndices = new int[MobileFishingInventoryPolicy.DefaultPageSize];
        public readonly ulong[] PickerCandidateUniqueIds = new ulong[MobileFishingInventoryPolicy.DefaultPageSize];
        public bool PickerHasPrevious;
        public bool PickerHasNext;
    }

    private static MobileFishingWindowBinding _mobileFishingBinding;
    private static DateTime _nextMobileFishingBindAttemptUtc = DateTime.MinValue;
    private static bool _mobileFishingDirty = true;

    internal static bool TryToggleMobileFishingWindow(out bool nowVisible)
    {
        return TryToggleMobileWindowByKeywords(
            MobileFishingWindowKey, MobileFishingWindowKeywords, out nowVisible);
    }

    internal static void ResetMobileFishingBindingsForHide() => ResetMobileFishingBindings();

    public static void MarkMobileFishingDirty()
    {
        _mobileFishingDirty = true;
        TryRefreshMobileFishingIfDue(force: false);
    }

    private static void ResetMobileFishingBindings()
    {
        MobileFishingWindowBinding binding = _mobileFishingBinding;
        _mobileFishingBinding = null;
        _nextMobileFishingBindAttemptUtc = DateTime.MinValue;
        _mobileFishingDirty = true;

        if (binding == null)
            return;

        try { binding.Cast?.onClick.Remove(binding.CastCallback); } catch { }
        try { binding.Reel?.onClick.Remove(binding.ReelCallback); } catch { }
        try { binding.AutoCast?.onClick.Remove(binding.AutoCastCallback); } catch { }
        try { binding.PickerPrevious?.onClick.Remove(binding.PickerPreviousCallback); } catch { }
        try { binding.PickerNext?.onClick.Remove(binding.PickerNextCallback); } catch { }
        try { binding.PickerClose?.onClick.Remove(binding.PickerCloseCallback); } catch { }
        try { if (binding.Picker != null && !binding.Picker._disposed) binding.Picker.visible = false; } catch { }
        for (int i = 0; i < binding.PickerItems.Length; i++)
        {
            try { binding.PickerItems[i]?.onClick.Remove(binding.PickerItemCallbacks[i]); } catch { }
            binding.PickerItemCallbacks[i] = null;
            binding.PickerItems[i] = null;
            binding.PickerCandidateInventoryIndices[i] = -1;
            binding.PickerCandidateUniqueIds[i] = 0;
        }
        for (int i = 0; i < binding.Slots.Length; i++)
        {
            try { binding.Slots[i]?.onClick.Remove(binding.SlotClickCallbacks[i]); } catch { }
            try
            {
                if (binding.Slots[i] != null && binding.SlotDropCallbacks[i] != null)
                    binding.Slots[i].RemoveEventListener("onDrop", binding.SlotDropCallbacks[i]);
            }
            catch { }

            binding.SlotClickCallbacks[i] = null;
            binding.SlotDropCallbacks[i] = null;
            binding.Slots[i] = null;
        }
    }

    private static bool TryCreateMobileFishingFallbackWindow(out GComponent component, out string resolveInfo)
    {
        component = null;
        resolveInfo = null;

        try
        {
            float rootWidth = Math.Max(480F, GRoot.inst?.width ?? 720F);
            float rootHeight = Math.Max(640F, GRoot.inst?.height ?? 1280F);
            component = new GComponent
            {
                name = MobileFishingFallbackName,
                touchable = true,
                opaque = false,
            };
            component.SetSize(rootWidth, rootHeight);

            float panelWidth = Math.Min(rootWidth - 32F, 760F);
            float panelHeight = Math.Min(rootHeight - 80F, 570F);
            panelWidth = Math.Max(420F, panelWidth);
            panelHeight = Math.Max(480F, panelHeight);
            var panel = new GComponent { name = "fishing_fallback_panel", touchable = true, opaque = true };
            panel.SetSize(panelWidth, panelHeight);
            panel.SetPosition((rootWidth - panelWidth) / 2F, (rootHeight - panelHeight) / 2F);
            component.AddChild(panel);

            var background = new GGraph { name = "fishing_fallback_background", touchable = false };
            background.DrawRect(panelWidth, panelHeight, 2, new Color(90, 140, 155, 255), new Color(20, 42, 55, 245));
            panel.AddChild(background);

            AddFishingFallbackText(panel, "fishing_title", "钓鱼", 24F, 18F, panelWidth - 80F, 40F, 25, Color.White, true);
            AddFishingFallbackText(panel, "fishing_status", string.Empty, 24F, 68F, panelWidth - 48F, 52F, 19, Color.White, false);
            AddFishingFallbackText(panel, "fishing_progress", string.Empty, 24F, 122F, panelWidth - 48F, 30F, 18, Color.LightGray, false);
            AddFishingFallbackText(panel, "fishing_chance", string.Empty, 24F, 154F, panelWidth - 48F, 30F, 18, Color.LightGray, false);
            AddFishingFallbackText(panel, "fishing_found", string.Empty, 24F, 186F, panelWidth - 48F, 30F, 18, Color.LightGreen, false);
            AddFishingFallbackText(panel, "fishing_error", string.Empty, 24F, 218F, panelWidth - 48F, 42F, 17, new Color(255, 180, 120, 255), false);
            AddFishingFallbackText(panel, "fishing_hint", "点击空槽选择背包配件，也可拖入对应槽位；点击已有配件可卸下。", 24F, 262F, panelWidth - 48F, 52F, 16, Color.LightGray, false);

            float slotWidth = Math.Max(64F, (panelWidth - 48F - 4 * 10F) / 5F);
            for (int i = 0; i < 5; i++)
                AddFishingFallbackButton(panel, "fishing_slot_" + i, FishingSlotLabel(i), 24F + (slotWidth + 10F) * i, 330F, slotWidth, 66F);

            float buttonY = panelHeight - 70F;
            float buttonWidth = Math.Max(92F, (panelWidth - 96F) / 3F);
            AddFishingFallbackButton(panel, "fishing_cast", "抛竿", 24F, buttonY, buttonWidth, 44F);
            AddFishingFallbackButton(panel, "fishing_reel", "收线", 36F + buttonWidth, buttonY, buttonWidth, 44F);
            AddFishingFallbackButton(panel, "fishing_autocast", "自动抛竿", 48F + buttonWidth * 2F, buttonY, buttonWidth, 44F);
            GButton close = AddFishingFallbackButton(panel, "closeButton", "×", panelWidth - 60F, 16F, 36F, 34F);
            close.title = "关闭";
            CreateMobileFishingPicker(panel, panelWidth, panelHeight);
            resolveInfo = "fallback";
            return true;
        }
        catch
        {
            try { component?.Dispose(); } catch { }
            component = null;
            resolveInfo = null;
            return false;
        }
    }

    private static GTextField AddFishingFallbackText(GComponent parent, string name, string text,
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
        field.SetSize(width, height);
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

    private static GButton AddFishingFallbackButton(GComponent parent, string name, string title,
        float x, float y, float width, float height)
    {
        var button = new GButton
        {
            name = name,
            title = title,
            touchable = true,
            enabled = true,
            grayed = false,
            opaque = true,
            changeStateOnClick = false,
        };
        button.SetPosition(x, y);
        button.SetSize(width, height);
        var background = new GGraph { name = name + "_background", touchable = false };
        background.DrawRect(width, height, 2, new Color(100, 155, 170, 255), new Color(38, 78, 92, 255));
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
            label.textFormat.size = 16;
            label.textFormat.color = Color.White;
            label.textFormat.bold = true;
        }
        catch { }
        button.AddChild(label);
        parent.AddChild(button);
        return button;
    }

    private static GComponent CreateMobileFishingPicker(GComponent parent, float rootWidth, float rootHeight)
    {
        if (parent == null)
            return null;

        try
        {
            var bounds = MobileFishingInventoryPolicy.GetPickerBounds(rootWidth, rootHeight);
            var picker = new GComponent
            {
                name = MobileFishingPickerName,
                visible = false,
                touchable = true,
                opaque = true,
            };
            picker.SetSize(bounds.Width, bounds.Height);
            picker.SetPosition(bounds.X, bounds.Y);

            var background = new GGraph { name = "fishing_picker_background", touchable = false };
            background.DrawRect(bounds.Width, bounds.Height, 2,
                new Color(112, 165, 180, 255), new Color(24, 50, 66, 250));
            picker.AddChild(background);
            AddFishingFallbackText(picker, "fishing_picker_title", "选择钓鱼配件", 20F, 14F,
                bounds.Width - 100F, 38F, 20, Color.White, true);
            AddFishingFallbackText(picker, "fishing_picker_page", string.Empty, 20F, bounds.Height - 42F,
                bounds.Width - 150F, 28F, 15, Color.LightGray, false);
            AddFishingFallbackText(picker, "fishing_picker_empty", string.Empty, 20F, 60F,
                bounds.Width - 40F, 28F, 16, Color.LightGray, false);

            for (int i = 0; i < MobileFishingInventoryPolicy.DefaultPageSize; i++)
            {
                var itemBounds = MobileFishingInventoryPolicy.GetPickerItemBounds(bounds, i);
                GButton itemButton = AddFishingFallbackButton(picker, "fishing_picker_item_" + i,
                    string.Empty, itemBounds.X - bounds.X, itemBounds.Y - bounds.Y,
                    itemBounds.Width, itemBounds.Height);
                itemButton.visible = false;
            }

            float navY = bounds.Height - 48F;
            float navWidth = Math.Max(72F, (bounds.Width - 100F) / 3F);
            AddFishingFallbackButton(picker, "fishing_picker_previous", "上一页", 20F, navY,
                navWidth, 34F);
            AddFishingFallbackButton(picker, "fishing_picker_next", "下一页", 30F + navWidth, navY,
                navWidth, 34F);
            AddFishingFallbackButton(picker, "fishing_picker_close", "取消", 40F + navWidth * 2F, navY,
                navWidth, 34F);
            parent.AddChild(picker);
            return picker;
        }
        catch
        {
            return null;
        }
    }

    private static void EnsureMobileFishingPicker(MobileFishingWindowBinding binding)
    {
        if (binding?.Window == null || binding.Window._disposed)
            return;

        try
        {
            if (binding.Picker == null || binding.Picker._disposed)
            {
                binding.Picker = FindByExact(binding.Window, MobileFishingPickerName, true, false, false, false) as GComponent;
                if (binding.Picker == null)
                {
                    float width = Math.Max(320F, binding.Window.width);
                    float height = Math.Max(430F, binding.Window.height);
                    binding.Picker = CreateMobileFishingPicker(binding.Window, width, height);
                }
            }

            if (binding.Picker == null || binding.Picker._disposed)
                return;

            binding.PickerTitle ??= FindByExact(binding.Picker, "fishing_picker_title", true, false, false, false) as GTextField;
            binding.PickerPage ??= FindByExact(binding.Picker, "fishing_picker_page", true, false, false, false) as GTextField;
            binding.PickerEmpty ??= FindByExact(binding.Picker, "fishing_picker_empty", true, false, false, false) as GTextField;
            binding.PickerPrevious ??= FindByExact(binding.Picker, "fishing_picker_previous", true, false, false, false) as GButton;
            binding.PickerNext ??= FindByExact(binding.Picker, "fishing_picker_next", true, false, false, false) as GButton;
            binding.PickerClose ??= FindByExact(binding.Picker, "fishing_picker_close", true, false, false, false) as GButton;
            for (int i = 0; i < binding.PickerItems.Length; i++)
            {
                binding.PickerItems[i] ??= FindByExact(binding.Picker,
                    "fishing_picker_item_" + i, true, false, false, false) as GButton;
            }

            if (binding.PickerPrevious != null && binding.PickerPreviousCallback == null)
            {
                binding.PickerPreviousCallback = () => MoveMobileFishingPickerPage(-1);
                binding.PickerPrevious.onClick.Add(binding.PickerPreviousCallback);
            }
            if (binding.PickerNext != null && binding.PickerNextCallback == null)
            {
                binding.PickerNextCallback = () => MoveMobileFishingPickerPage(1);
                binding.PickerNext.onClick.Add(binding.PickerNextCallback);
            }
            if (binding.PickerClose != null && binding.PickerCloseCallback == null)
            {
                binding.PickerCloseCallback = HideMobileFishingPicker;
                binding.PickerClose.onClick.Add(binding.PickerCloseCallback);
            }
            for (int i = 0; i < binding.PickerItems.Length; i++)
            {
                if (binding.PickerItems[i] == null || binding.PickerItemCallbacks[i] != null)
                    continue;

                int captured = i;
                binding.PickerItemCallbacks[captured] = () => OnMobileFishingPickerCandidateClicked(captured);
                binding.PickerItems[captured].onClick.Add(binding.PickerItemCallbacks[captured]);
            }
        }
        catch
        {
            // FUI package trees are user-supplied; leave the fallback/drag path alive.
        }
    }

    private static void TryBindMobileFishingWindowIfDue(string windowKey, GComponent window, string resolveInfo)
    {
        if (window == null || window._disposed)
            return;

        MobileFishingWindowBinding binding = _mobileFishingBinding;
        if (binding != null && (binding.Window == null || binding.Window._disposed || !ReferenceEquals(binding.Window, window)))
        {
            ResetMobileFishingBindings();
            binding = null;
        }

        if (binding == null)
        {
            binding = new MobileFishingWindowBinding { Window = window };
            _mobileFishingBinding = binding;
            _nextMobileFishingBindAttemptUtc = DateTime.MinValue;
        }

        if (DateTime.UtcNow < _nextMobileFishingBindAttemptUtc)
            return;
        _nextMobileFishingBindAttemptUtc = DateTime.UtcNow.AddSeconds(2);

        var used = new HashSet<GObject>();
        binding.Status ??= ResolveFishingText(window, MobileFishingStatusKeywords, used, 15);
        binding.Progress ??= ResolveFishingText(window, MobileFishingProgressKeywords, used, 12);
        binding.Chance ??= ResolveFishingText(window, MobileFishingChanceKeywords, used, 12);
        binding.Found ??= ResolveFishingText(window, new[] { "found", "fish", "发现", "鱼" }, used, 10);
        binding.Error ??= ResolveFishingText(window, MobileFishingErrorKeywords, used, 15);
        binding.Cast ??= ResolveFishingButton(window, MobileFishingCastKeywords, used, 15);
        binding.Reel ??= ResolveFishingButton(window, MobileFishingReelKeywords, used, 15);
        binding.AutoCast ??= ResolveFishingButton(window, MobileFishingAutocastKeywords, used, 15);
        EnsureMobileFishingPicker(binding);

        if (binding.Cast != null && binding.CastCallback == null)
        {
            binding.CastCallback = () => GameScene.Scene?.TryCastMobileFishing(true);
            binding.Cast.onClick.Add(binding.CastCallback);
        }
        if (binding.Reel != null && binding.ReelCallback == null)
        {
            binding.ReelCallback = () => GameScene.Scene?.TryCastMobileFishing(false);
            binding.Reel.onClick.Add(binding.ReelCallback);
        }
        if (binding.AutoCast != null && binding.AutoCastCallback == null)
        {
            binding.AutoCastCallback = () => GameScene.Scene?.TryToggleMobileFishingAutocast(!GameScene.MobileFishingState.AutoCastIntent);
            binding.AutoCast.onClick.Add(binding.AutoCastCallback);
        }

        for (int i = 0; i < binding.Slots.Length; i++)
        {
            if (binding.Slots[i] == null || binding.Slots[i]._disposed)
                binding.Slots[i] = ResolveFishingObject(window, MobileFishingSlotKeywords[i], used, i);
            if (binding.Slots[i] == null || binding.Slots[i]._disposed)
                continue;

            int captured = i;
            try { binding.Slots[captured].onClick.Remove(binding.SlotClickCallbacks[captured]); } catch { }
            binding.SlotClickCallbacks[captured] = () => OnMobileFishingSlotClicked(captured);
            try { binding.Slots[captured].onClick.Add(binding.SlotClickCallbacks[captured]); } catch { }
            try { binding.Slots[captured].touchable = true; } catch { }

            if (binding.SlotDropCallbacks[captured] != null)
            {
                try { binding.Slots[captured].RemoveEventListener("onDrop", binding.SlotDropCallbacks[captured]); } catch { }
            }
            binding.SlotDropCallbacks[captured] = context => OnMobileItemDroppedOnFishingSlot(captured, context);
            try { binding.Slots[captured].AddEventListener("onDrop", binding.SlotDropCallbacks[captured]); } catch { }
        }
    }

    private static GTextField ResolveFishingText(GComponent window, string[] keywords, ISet<GObject> used, int minScore)
    {
        try
        {
            List<(int Score, GObject Target)> candidates = CollectMobileChatCandidates(
                window,
                obj => obj is GTextField && obj is not GTextInput && (used == null || !used.Contains(obj)),
                keywords,
                ScoreMobileShopTextCandidate);
            GTextField result = SelectMobileChatCandidate<GTextField>(candidates, minScore);
            used?.Add(result);
            return result;
        }
        catch { return null; }
    }

    private static GButton ResolveFishingButton(GComponent window, string[] keywords, ISet<GObject> used, int minScore)
    {
        try
        {
            List<(int Score, GObject Target)> candidates = CollectMobileChatCandidates(
                window,
                obj => obj is GButton && (used == null || !used.Contains(obj)),
                keywords,
                ScoreMobileShopButtonCandidate);
            GButton result = SelectMobileChatCandidate<GButton>(candidates, minScore);
            used?.Add(result);
            return result;
        }
        catch { return null; }
    }

    private static GObject ResolveFishingObject(GComponent window, string[] keywords, ISet<GObject> used, int slot)
    {
        try
        {
            GObject exact = FindByExact(window, "fishing_slot_" + slot, true, false, false, false);
            if (exact != null && (used == null || !used.Contains(exact)))
            {
                used?.Add(exact);
                return exact;
            }

            List<(int Score, GObject Target)> candidates = CollectMobileChatCandidates(
                window,
                obj => (obj is GButton || obj is GComponent) && (used == null || !used.Contains(obj)),
                keywords,
                ScoreMobileShopButtonCandidate);
            GObject result = candidates != null && candidates.Count > 0 ? candidates[0].Target : null;
            used?.Add(result);
            return result;
        }
        catch { return null; }
    }

    private static void TryRefreshMobileFishingIfDue(bool force)
    {
        if (_stage == null || !_initialized || !_packagesLoaded)
            return;

        if (!MobileWindows.TryGetValue(MobileFishingWindowKey, out GComponent window) || window == null || window._disposed)
        {
            if (_mobileFishingBinding != null)
                ResetMobileFishingBindings();
            return;
        }

        if (!window.visible)
            return;

        TryBindMobileFishingWindowIfDue(MobileFishingWindowKey, window, resolveInfo: null);
        MobileFishingWindowBinding binding = _mobileFishingBinding;
        if (binding == null || binding.Window == null || binding.Window._disposed)
        {
            ResetMobileFishingBindings();
            return;
        }

        if (!force && !_mobileFishingDirty)
            return;
        _mobileFishingDirty = false;

        MobileFishingState state = GameScene.MobileFishingState;
        string status = !state.HasFishingRod ? "当前没有鱼竿。" :
            state.Fishing ? (state.FoundFish ? "发现鱼，请收线。" : "鱼线已抛出，等待鱼儿上钩。") : "当前未抛竿。";
        if (state.CastRequestPending || state.AutoCastRequestPending)
            status += "\n请求已发送，等待服务器确认。";
        SetFishingText(binding.Status, status);
        SetFishingText(binding.Progress, $"进度：{state.ProgressPercent}%");
        SetFishingText(binding.Chance, $"成功率：{state.ChancePercent}%");
        SetFishingText(binding.Found, state.FoundFish ? "发现鱼！" : string.Empty);
        SetFishingText(binding.Error, state.Error ?? string.Empty);

        SetFishingButtonAvailability(binding.Cast, state.HasFishingRod && !state.Fishing && !state.CastRequestPending);
        SetFishingButtonAvailability(binding.Reel, state.HasFishingRod && state.Fishing && !state.CastRequestPending);
        SetFishingButtonAvailability(binding.AutoCast, state.HasReel && !state.AutoCastRequestPending);
        if (binding.AutoCast != null && !binding.AutoCast._disposed)
            binding.AutoCast.title = state.AutoCastIntent ? "自动抛竿：已请求" : "自动抛竿";

        UserObject user = GameScene.User;
        UserItem rod = user?.Equipment != null && user.Equipment.Length > (int)EquipmentSlot.Weapon
            ? user.Equipment[(int)EquipmentSlot.Weapon]
            : null;
        if (rod?.Info?.IsFishingRod != true)
            rod = null;

        for (int i = 0; i < binding.Slots.Length; i++)
        {
            UserItem item = rod?.Slots != null && i < rod.Slots.Length ? rod.Slots[i] : null;
            SetFishingSlotVisual(binding.Slots[i], item, i);
            SetFishingSlotAvailability(binding.Slots[i],
                rod?.Slots != null && i < rod.Slots.Length && !state.SlotRequestPending);
        }

        RefreshMobileFishingPicker(binding);
    }

    private static void ShowMobileFishingPicker(int slot)
    {
        MobileFishingWindowBinding binding = _mobileFishingBinding;
        if (binding == null || slot < 0 || slot >= MobileFishingInventoryPolicy.SlotCount)
            return;

        EnsureMobileFishingPicker(binding);
        if (binding.Picker == null || binding.Picker._disposed)
            return;

        binding.PickerSlot = slot;
        binding.PickerPageIndex = 0;
        binding.Picker.visible = true;
        RefreshMobileFishingPicker(binding);
    }

    private static void HideMobileFishingPicker()
    {
        MobileFishingWindowBinding binding = _mobileFishingBinding;
        if (binding == null)
            return;

        binding.PickerSlot = -1;
        binding.PickerPageIndex = 0;
        try { if (binding.Picker != null && !binding.Picker._disposed) binding.Picker.visible = false; } catch { }
        RefreshMobileFishingPicker(binding);
    }

    private static void MoveMobileFishingPickerPage(int delta)
    {
        MobileFishingWindowBinding binding = _mobileFishingBinding;
        if (binding == null || binding.PickerSlot < 0)
            return;

        int next = Math.Max(0, binding.PickerPageIndex + delta);
        if (delta > 0 && !binding.PickerHasNext)
            return;
        if (delta < 0 && !binding.PickerHasPrevious)
            return;

        binding.PickerPageIndex = next;
        RefreshMobileFishingPicker(binding);
    }

    private static void RefreshMobileFishingPicker(MobileFishingWindowBinding binding)
    {
        if (binding == null || binding.Picker == null || binding.Picker._disposed)
            return;

        bool visible = binding.PickerSlot >= 0;
        try { binding.Picker.visible = visible; } catch { }
        if (!visible)
            return;

        UserObject user = GameScene.User;
        IReadOnlyList<MobileFishingInventoryPolicy.Candidate> candidates =
            MobileFishingInventoryPolicy.GetCandidates(user?.Inventory, binding.PickerSlot,
                binding.PickerPageIndex, MobileFishingInventoryPolicy.DefaultPageSize,
                out bool hasPrevious, out bool hasNext);
        binding.PickerHasPrevious = hasPrevious;
        binding.PickerHasNext = hasNext;

        SetFishingText(binding.PickerTitle, "选择" + FishingSlotLabel(binding.PickerSlot));
        SetFishingText(binding.PickerPage,
            $"第 {binding.PickerPageIndex + 1} 页" + (hasNext ? "  ·  还有更多" : string.Empty));
        SetFishingText(binding.PickerEmpty, candidates.Count == 0 ? "背包中没有可用配件。" : string.Empty);

        for (int i = 0; i < binding.PickerItems.Length; i++)
        {
            if (i < candidates.Count)
            {
                MobileFishingInventoryPolicy.Candidate candidate = candidates[i];
                binding.PickerCandidateInventoryIndices[i] = candidate.InventoryIndex;
                binding.PickerCandidateUniqueIds[i] = candidate.UniqueId;
                string title = string.IsNullOrWhiteSpace(candidate.Name)
                    ? "配件 " + candidate.UniqueId
                    : candidate.Name;
                if (candidate.Count > 1)
                    title += " ×" + candidate.Count;
                SetFishingButtonTitle(binding.PickerItems[i], title);
                SetFishingButtonAvailability(binding.PickerItems[i], !GameScene.MobileFishingState.SlotRequestPending);
                try { binding.PickerItems[i].visible = true; } catch { }
            }
            else
            {
                binding.PickerCandidateInventoryIndices[i] = -1;
                binding.PickerCandidateUniqueIds[i] = 0;
                SetFishingButtonTitle(binding.PickerItems[i], string.Empty);
                SetFishingButtonAvailability(binding.PickerItems[i], false);
                try { binding.PickerItems[i].visible = false; } catch { }
            }
        }

        SetFishingButtonAvailability(binding.PickerPrevious, hasPrevious && !GameScene.MobileFishingState.SlotRequestPending);
        SetFishingButtonAvailability(binding.PickerNext, hasNext && !GameScene.MobileFishingState.SlotRequestPending);
        SetFishingButtonAvailability(binding.PickerClose, true);
    }

    private static void OnMobileFishingPickerCandidateClicked(int candidateIndex)
    {
        MobileFishingWindowBinding binding = _mobileFishingBinding;
        if (binding == null || binding.PickerSlot < 0 || candidateIndex < 0 ||
            candidateIndex >= binding.PickerCandidateInventoryIndices.Length)
            return;

        if (GameScene.MobileFishingState.SlotRequestPending)
            return;

        UserObject user = GameScene.User;
        int inventoryIndex = binding.PickerCandidateInventoryIndices[candidateIndex];
        ulong expectedUniqueId = binding.PickerCandidateUniqueIds[candidateIndex];
        if (!MobileFishingInventoryPolicy.TryGetCurrentCandidate(user?.Inventory, inventoryIndex,
                binding.PickerSlot, expectedUniqueId, out UserItem item))
        {
            GameScene.Scene?.OutputMessage("背包物品已变化，请重新选择。 ");
            MarkMobileFishingDirty();
            RefreshMobileFishingPicker(binding);
            return;
        }

        UserItem rod = user?.Equipment != null && user.Equipment.Length > (int)EquipmentSlot.Weapon
            ? user.Equipment[(int)EquipmentSlot.Weapon]
            : null;
        if (rod?.Info?.IsFishingRod != true)
        {
            GameScene.Scene?.OutputMessage("当前没有可用的鱼竿。 ");
            HideMobileFishingPicker();
            return;
        }

        UserItem target = rod.Slots != null && binding.PickerSlot < rod.Slots.Length
            ? rod.Slots[binding.PickerSlot]
            : null;
        bool accepted;
        if (target != null)
        {
            if (binding.PickerSlot != (int)FishingSlot.Bait)
            {
                GameScene.Scene?.OutputMessage("该槽位已有配件，请先卸下后再装入。 ");
                return;
            }

            if (!MobileFishingInventoryPolicy.CanMergeBait(item, target))
            {
                GameScene.Scene?.OutputMessage("只能合并相同鱼饵，且目标堆不能已满。 ");
                return;
            }

            accepted = GameScene.Scene?.TryMergeMobileFishingBait(item.UniqueID, target.UniqueID) == true;
        }
        else
        {
            accepted = GameScene.Scene?.TryEquipMobileFishingAccessory(item.UniqueID, binding.PickerSlot) == true;
        }

        if (accepted)
            HideMobileFishingPicker();
        else
            RefreshMobileFishingPicker(binding);
    }

    private static void OnMobileFishingSlotClicked(int slot)
    {
        UserObject user = GameScene.User;
        UserItem rod = user?.Equipment != null && user.Equipment.Length > (int)EquipmentSlot.Weapon
            ? user.Equipment[(int)EquipmentSlot.Weapon]
            : null;
        UserItem item = rod?.Slots != null && slot >= 0 && slot < rod.Slots.Length ? rod.Slots[slot] : null;
        if (rod?.Info?.IsFishingRod != true || !MobileFishingInventoryPolicy.IsValidSlot(slot))
        {
            GameScene.Scene?.OutputMessage("当前没有可用的鱼竿。 ");
            return;
        }
        if (item == null)
        {
            ShowMobileFishingPicker(slot);
            return;
        }

        if (GameScene.MobileFishingState.SlotRequestPending)
            return;

        int destination = MobileMountInventoryPolicy.FindFirstEmptyPackageSlot(user?.Inventory, user?.BeltIdx ?? 0);
        if (destination < 0)
        {
            GameScene.Scene?.OutputMessage("背包没有空位，无法卸下钓鱼配件。");
            return;
        }

        GameScene.Scene?.TryRemoveMobileFishingAccessory(item.UniqueID, destination);
    }

    private static void OnMobileItemDroppedOnFishingSlot(int slot, EventContext context)
    {
        MobileItemDragPayload payload = context?.data as MobileItemDragPayload;
        if (payload == null)
            return;

        payload.Handled = true;
        _mobileItemDragDropHandled = true;
        if (payload.Grid != MirGridType.Inventory)
            return;

        UserObject user = GameScene.User;
        if (user?.Inventory == null || payload.SlotIndex < 0 || payload.SlotIndex >= user.Inventory.Length)
            return;

        UserItem item = user.Inventory[payload.SlotIndex];
        if (item?.Info == null || !MobileFishingInventoryPolicy.MatchesInventoryIdentity(
                user.Inventory, payload.SlotIndex, payload.UniqueId))
        {
            GameScene.Scene?.OutputMessage("背包物品已变化，请重新拖入。 ");
            MarkMobileFishingDirty();
            return;
        }

        ItemType expected = FishingAccessoryType(slot);
        if (item.Info.Type != expected)
        {
            GameScene.Scene?.OutputMessage("该物品不是此钓鱼槽位需要的配件。");
            return;
        }

        UserItem rod = user.Equipment != null && user.Equipment.Length > (int)EquipmentSlot.Weapon
            ? user.Equipment[(int)EquipmentSlot.Weapon]
            : null;
        UserItem target = rod?.Slots != null && slot < rod.Slots.Length ? rod.Slots[slot] : null;
        if (GameScene.MobileFishingState.SlotRequestPending)
            return;

        if (slot == (int)FishingSlot.Bait && target != null)
        {
            if (MobileFishingInventoryPolicy.CanMergeBait(item, target))
                GameScene.Scene?.TryMergeMobileFishingBait(item.UniqueID, target.UniqueID);
            else
                GameScene.Scene?.OutputMessage("只能合并相同鱼饵，且目标堆不能已满。 ");
            return;
        }

        if (target != null)
        {
            GameScene.Scene?.OutputMessage("该槽位已有配件，请先卸下后再装入。 ");
            return;
        }

        GameScene.Scene?.TryEquipMobileFishingAccessory(item.UniqueID, slot);
    }

    private static void SetFishingText(GTextField field, string text)
    {
        try { if (field != null && !field._disposed) field.text = text ?? string.Empty; } catch { }
    }

    private static void SetFishingButtonTitle(GButton button, string title)
    {
        try
        {
            if (button == null || button._disposed)
                return;

            title ??= string.Empty;
            button.title = title;
            for (int i = 0; i < button.numChildren; i++)
            {
                if (button.GetChildAt(i) is GTextField field &&
                    string.Equals(field.name, "title", StringComparison.OrdinalIgnoreCase))
                    field.text = title;
            }
        }
        catch { }
    }

    private static void SetFishingButtonAvailability(GButton button, bool enabled)
    {
        try
        {
            if (button == null || button._disposed)
                return;
            button.enabled = enabled;
            button.grayed = !enabled;
            button.touchable = enabled;
        }
        catch { }
    }

    private static void SetFishingSlotAvailability(GObject target, bool enabled)
    {
        try
        {
            if (target == null || target._disposed)
                return;
            target.visible = enabled;
            target.touchable = enabled;
            if (target is GButton button)
            {
                button.enabled = enabled;
                button.grayed = !enabled;
            }
        }
        catch { }
    }

    private static void SetFishingSlotVisual(GObject target, UserItem item, int slot)
    {
        if (target == null || target._disposed)
            return;

        string title = item?.FriendlyName;
        if (string.IsNullOrWhiteSpace(title))
            title = item == null ? FishingSlotLabel(slot) + "（空）" : FishingSlotLabel(slot);

        try
        {
            if (target is GButton button)
            {
                button.title = title;
                for (int i = 0; i < button.numChildren; i++)
                {
                    if (button.GetChildAt(i) is GTextField field && string.Equals(field.name, "title", StringComparison.OrdinalIgnoreCase))
                        field.text = title;
                }
            }
            else if (target is GComponent component && component.GetChild("title") is GTextField field)
            {
                field.text = title;
            }
        }
        catch { }
    }

    private static ItemType FishingAccessoryType(int slot)
    {
        return slot switch
        {
            (int)FishingSlot.Hook => ItemType.鱼钩,
            (int)FishingSlot.Float => ItemType.鱼漂,
            (int)FishingSlot.Bait => ItemType.鱼饵,
            (int)FishingSlot.Finder => ItemType.探鱼器,
            (int)FishingSlot.Reel => ItemType.摇轮,
            _ => ItemType.Nothing,
        };
    }

    private static string FishingSlotLabel(int slot)
    {
        return slot switch
        {
            (int)FishingSlot.Hook => "鱼钩",
            (int)FishingSlot.Float => "鱼漂",
            (int)FishingSlot.Bait => "鱼饵",
            (int)FishingSlot.Finder => "探鱼器",
            (int)FishingSlot.Reel => "摇轮",
            _ => "配件",
        };
    }
}
