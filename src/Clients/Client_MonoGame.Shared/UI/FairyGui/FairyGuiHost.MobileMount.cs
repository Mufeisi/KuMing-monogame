using System;
using System.Collections.Generic;
using C = ClientPackets;
using FairyGUI;
using Microsoft.Xna.Framework;
using MonoShare.MirObjects;
using MonoShare.MirScenes;

namespace MonoShare;

/// <summary>FairyGUI seam for the Android mount/ride flow.</summary>
internal static partial class FairyGuiHost
{
    private const string MobileMountWindowKey = "Mount";
    private const string MobileMountFallbackName = "__codex_mobile_mount_fallback";

    private static readonly string[] MobileMountWindowKeywords =
        { "坐骑", "骑乘", "mountwindow", "mountdialog", "horsewindow", "saddlewindow" };
    private static readonly string[] MobileMountStatusKeywords =
        { "status", "state", "mountstatus", "坐骑", "骑乘", "状态" };
    private static readonly string[] MobileMountNameKeywords =
        { "mountname", "name", "mount", "坐骑", "名称" };
    private static readonly string[] MobileMountLoyaltyKeywords =
        { "loyalty", "dura", "durability", "忠诚", "耐久" };
    private static readonly string[] MobileMountErrorKeywords =
        { "error", "fail", "message", "错误", "失败", "提示" };
    private static readonly string[] MobileMountRideKeywords =
        { "ride", "mount", "toggle", "乘骑", "骑乘", "上马", "下马" };
    private static readonly string[][] MobileMountSlotKeywords =
    {
        new[] { "reins", "bridle", "缰绳" },
        new[] { "bells", "bell", "铃铛" },
        new[] { "saddle", "马鞍" },
        new[] { "ribbon", "蝴蝶结" },
        new[] { "mask", "面甲" },
    };

    private sealed class MobileMountWindowBinding
    {
        public GComponent Window;
        public GTextField Status;
        public GTextField Name;
        public GTextField Loyalty;
        public GTextField Error;
        public GButton Ride;
        public readonly GObject[] Slots = new GObject[5];
        public readonly EventCallback0[] SlotClickCallbacks = new EventCallback0[5];
        public readonly EventCallback1[] SlotDropCallbacks = new EventCallback1[5];
        public EventCallback0 RideCallback;
    }

    private static MobileMountWindowBinding _mobileMountBinding;
    private static DateTime _nextMobileMountBindAttemptUtc = DateTime.MinValue;
    private static bool _mobileMountDirty = true;

    internal static bool TryToggleMobileMountWindow(out bool nowVisible)
    {
        return TryToggleMobileWindowByKeywords(MobileMountWindowKey, MobileMountWindowKeywords, out nowVisible);
    }

    internal static void ResetMobileMountBindingsForHide()
    {
        ResetMobileMountBindings();
    }

    public static void MarkMobileMountDirty()
    {
        _mobileMountDirty = true;
        TryRefreshMobileMountIfDue(force: false);
    }

    private static void ResetMobileMountBindings()
    {
        MobileMountWindowBinding binding = _mobileMountBinding;
        _mobileMountBinding = null;
        _nextMobileMountBindAttemptUtc = DateTime.MinValue;
        _mobileMountDirty = true;

        if (binding == null)
            return;

        try { binding.Ride?.onClick.Remove(binding.RideCallback); } catch { }
        for (int i = 0; i < binding.Slots.Length; i++)
        {
            GObject slot = binding.Slots[i];
            try { slot?.onClick.Remove(binding.SlotClickCallbacks[i]); } catch { }
            try
            {
                if (slot != null && binding.SlotDropCallbacks[i] != null)
                    slot.RemoveEventListener("onDrop", binding.SlotDropCallbacks[i]);
            }
            catch { }

            binding.SlotClickCallbacks[i] = null;
            binding.SlotDropCallbacks[i] = null;
            binding.Slots[i] = null;
        }
    }

    private static bool TryCreateMobileMountFallbackWindow(out GComponent component, out string resolveInfo)
    {
        component = null;
        resolveInfo = null;

        try
        {
            float rootWidth = Math.Max(480F, GRoot.inst?.width ?? 720F);
            float rootHeight = Math.Max(640F, GRoot.inst?.height ?? 1280F);
            component = new GComponent
            {
                name = MobileMountFallbackName,
                touchable = true,
                opaque = false,
            };
            component.SetSize(rootWidth, rootHeight);

            float panelWidth = Math.Min(rootWidth - 32F, 760F);
            float panelHeight = Math.Min(rootHeight - 80F, 560F);
            panelWidth = Math.Max(420F, panelWidth);
            panelHeight = Math.Max(460F, panelHeight);
            var panel = new GComponent { name = "mount_fallback_panel", touchable = true, opaque = true };
            panel.SetSize(panelWidth, panelHeight);
            panel.SetPosition((rootWidth - panelWidth) / 2F, (rootHeight - panelHeight) / 2F);
            component.AddChild(panel);

            var background = new GGraph { name = "mount_fallback_background", touchable = false };
            background.DrawRect(panelWidth, panelHeight, 2, new Color(90, 110, 150, 255), new Color(25, 30, 45, 245));
            panel.AddChild(background);

            AddMountFallbackText(panel, "mount_title", "坐骑", 24F, 18F, panelWidth - 80F, 40F, 25, Color.White, true);
            AddMountFallbackText(panel, "mount_status", string.Empty, 24F, 70F, panelWidth - 48F, 56F, 19, Color.White, false);
            AddMountFallbackText(panel, "mount_name", string.Empty, 24F, 132F, panelWidth - 48F, 32F, 19, Color.LightGray, false);
            AddMountFallbackText(panel, "mount_loyalty", string.Empty, 24F, 168F, panelWidth - 48F, 32F, 18, Color.LightGray, false);
            AddMountFallbackText(panel, "mount_error", string.Empty, 24F, 204F, panelWidth - 48F, 44F, 17, new Color(255, 180, 120, 255), false);
            AddMountFallbackText(panel, "mount_hint", "配件槽：将背包中的缰绳、铃铛、马鞍、蝴蝶结或面甲拖到对应槽位。点击已有配件可卸下。", 24F, 250F, panelWidth - 48F, 52F, 16, Color.LightGray, false);

            float slotWidth = Math.Max(64F, (panelWidth - 48F - 4 * 10F) / 5F);
            float slotY = 320F;
            for (int i = 0; i < 5; i++)
            {
                string label = MountSlotLabel(i);
                AddMountFallbackButton(panel, "mount_slot_" + i, label, 24F + (slotWidth + 10F) * i, slotY, slotWidth, 66F);
            }

            float buttonY = panelHeight - 66F;
            float buttonWidth = Math.Max(130F, (panelWidth - 72F) / 2F);
            AddMountFallbackButton(panel, "mount_ride", "上马/下马", 24F, buttonY, buttonWidth, 44F);
            GButton close = AddMountFallbackButton(panel, "closeButton", "×", panelWidth - 60F, 16F, 36F, 34F);
            close.title = "关闭";
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

    private static GTextField AddMountFallbackText(GComponent parent, string name, string text,
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

    private static GButton AddMountFallbackButton(GComponent parent, string name, string title,
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
            label.textFormat.size = 16;
            label.textFormat.color = Color.White;
            label.textFormat.bold = true;
        }
        catch { }
        button.AddChild(label);
        parent.AddChild(button);
        return button;
    }

    private static void TryBindMobileMountWindowIfDue(string windowKey, GComponent window, string resolveInfo)
    {
        if (window == null || window._disposed)
            return;

        MobileMountWindowBinding binding = _mobileMountBinding;
        if (binding != null && (binding.Window == null || binding.Window._disposed || !ReferenceEquals(binding.Window, window)))
        {
            ResetMobileMountBindings();
            binding = null;
        }

        if (binding == null)
        {
            binding = new MobileMountWindowBinding { Window = window };
            _mobileMountBinding = binding;
            _nextMobileMountBindAttemptUtc = DateTime.MinValue;
        }

        if (DateTime.UtcNow < _nextMobileMountBindAttemptUtc)
            return;
        _nextMobileMountBindAttemptUtc = DateTime.UtcNow.AddSeconds(2);

        var used = new HashSet<GObject>();
        if (binding.Status == null || binding.Status._disposed)
            binding.Status = ResolveMobileMountText(window, MobileMountStatusKeywords, used, 15);
        if (binding.Name == null || binding.Name._disposed)
            binding.Name = ResolveMobileMountText(window, MobileMountNameKeywords, used, 15);
        if (binding.Loyalty == null || binding.Loyalty._disposed)
            binding.Loyalty = ResolveMobileMountText(window, MobileMountLoyaltyKeywords, used, 15);
        if (binding.Error == null || binding.Error._disposed)
            binding.Error = ResolveMobileMountText(window, MobileMountErrorKeywords, used, 15);
        if (binding.Ride == null || binding.Ride._disposed)
            binding.Ride = ResolveMobileMountButton(window, MobileMountRideKeywords, used, 20);

        if (binding.Ride != null && binding.RideCallback == null)
        {
            binding.RideCallback = () => GameScene.Scene?.TryToggleMobileMount();
            binding.Ride.onClick.Add(binding.RideCallback);
        }

        for (int i = 0; i < binding.Slots.Length; i++)
        {
            if (binding.Slots[i] == null || binding.Slots[i]._disposed)
                binding.Slots[i] = ResolveMobileMountObject(window, MobileMountSlotKeywords[i], used);
            if (binding.Slots[i] == null || binding.Slots[i]._disposed)
                continue;

            int captured = i;
            try { binding.Slots[captured].onClick.Remove(binding.SlotClickCallbacks[captured]); } catch { }
            binding.SlotClickCallbacks[captured] = () => OnMobileMountSlotClicked(captured);
            try { binding.Slots[captured].onClick.Add(binding.SlotClickCallbacks[captured]); } catch { }
            try { binding.Slots[captured].touchable = true; } catch { }

            if (binding.SlotDropCallbacks[captured] != null)
            {
                try { binding.Slots[captured].RemoveEventListener("onDrop", binding.SlotDropCallbacks[captured]); } catch { }
            }
            binding.SlotDropCallbacks[captured] = context => OnMobileItemDroppedOnMountSlot(captured, context);
            try { binding.Slots[captured].AddEventListener("onDrop", binding.SlotDropCallbacks[captured]); } catch { }
        }
    }

    private static GTextField ResolveMobileMountText(GComponent window, string[] keywords, ISet<GObject> used, int minScore)
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

    private static GButton ResolveMobileMountButton(GComponent window, string[] keywords, ISet<GObject> used, int minScore)
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

    private static GObject ResolveMobileMountObject(GComponent window, string[] keywords, ISet<GObject> used)
    {
        try
        {
            List<(int Score, GObject Target)> candidates = CollectMobileChatCandidates(
                window,
                obj => (obj is GButton || obj is GComponent) && (used == null || !used.Contains(obj)),
                keywords,
                ScoreMobileShopButtonCandidate);
            GObject result = candidates != null && candidates.Count > 0 ? candidates[0].Target : null;
            if (result != null)
                used?.Add(result);
            return result;
        }
        catch { return null; }
    }

    private static void TryRefreshMobileMountIfDue(bool force)
    {
        if (_stage == null || !_initialized || !_packagesLoaded)
            return;

        if (!MobileWindows.TryGetValue(MobileMountWindowKey, out GComponent window) || window == null || window._disposed)
        {
            if (_mobileMountBinding != null)
                ResetMobileMountBindings();
            return;
        }

        if (!window.visible)
            return;

        TryBindMobileMountWindowIfDue(MobileMountWindowKey, window, resolveInfo: null);
        MobileMountWindowBinding binding = _mobileMountBinding;
        if (binding == null || binding.Window == null || binding.Window._disposed)
        {
            ResetMobileMountBindings();
            return;
        }

        // Ride eligibility is transient (cooldown/action/pending request) and
        // must not wait for a content-dirty event.  Both published FUI and the
        // fallback window bind through the same Ride target, so this remains a
        // small per-frame property update with no content rebuild.
        RefreshMobileMountRideAvailability(binding);

        if (!force && !_mobileMountDirty)
            return;
        _mobileMountDirty = false;

        MobileMountState state = GameScene.MobileMountState;
        string status = state.HasMount ? (state.RidingMount ? "当前已骑乘。" : "当前未骑乘。") : "当前没有装备坐骑。";
        if (state.HasPendingToggleRequest)
            status += "\n乘骑请求已发送，等待服务器确认。";
        SetMountText(binding.Status, status);

        UserObject user = GameScene.User;
        UserItem mount = user?.Equipment != null && user.Equipment.Length > (int)EquipmentSlot.Mount
            ? user.Equipment[(int)EquipmentSlot.Mount]
            : null;
        SetMountText(binding.Name, mount?.FriendlyName ?? string.Empty);
        SetMountText(binding.Loyalty, mount == null ? string.Empty : $"忠诚度：{mount.CurrentDura} / {mount.MaxDura}");
        SetMountText(binding.Error, state.Error ?? string.Empty);

        for (int i = 0; i < binding.Slots.Length; i++)
        {
            UserItem slotItem = mount?.Slots != null && i < mount.Slots.Length ? mount.Slots[i] : null;
            bool slotAvailable = mount?.Slots != null && i < mount.Slots.Length;
            SetMountSlotVisual(binding.Slots[i], slotItem, i);
            SetMountSlotAvailability(binding.Slots[i], slotAvailable);
        }
    }

    private static void RefreshMobileMountRideAvailability(MobileMountWindowBinding binding)
    {
        if (binding == null || binding.Ride == null || binding.Ride._disposed)
            return;

        SetMountButtonAvailability(binding.Ride, GameScene.Scene?.CanToggleMobileMount == true);
    }

    private static void OnMobileMountSlotClicked(int slot)
    {
        UserObject user = GameScene.User;
        UserItem mount = user?.Equipment != null && user.Equipment.Length > (int)EquipmentSlot.Mount
            ? user.Equipment[(int)EquipmentSlot.Mount]
            : null;
        UserItem item = mount?.Slots != null && slot >= 0 && slot < mount.Slots.Length ? mount.Slots[slot] : null;
        if (item == null)
        {
            GameScene.Scene?.OutputMessage("请从背包拖入对应坐骑配件。 ");
            return;
        }

        int destination = MobileMountInventoryPolicy.FindFirstEmptyPackageSlot(
            user?.Inventory, user?.BeltIdx ?? 0);
        if (destination < 0)
        {
            GameScene.Scene?.OutputMessage("背包没有空位，无法卸下坐骑配件。");
            return;
        }

        GameScene.Scene?.TryRemoveMobileMountAccessory(item.UniqueID, destination);
    }

    private static void OnMobileItemDroppedOnMountSlot(int slot, EventContext context)
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
        if (item == null || item.Info == null)
            return;

        ItemType expected = slot switch
        {
            (int)MountSlot.Reins => ItemType.Reins,
            (int)MountSlot.Bells => ItemType.Bells,
            (int)MountSlot.Saddle => ItemType.Saddle,
            (int)MountSlot.Ribbon => ItemType.Ribbon,
            (int)MountSlot.Mask => ItemType.Mask,
            _ => ItemType.Nothing,
        };
        if (expected == ItemType.Nothing || item.Info.Type != expected)
        {
            GameScene.Scene?.OutputMessage("该物品不是此坐骑槽位需要的配件。");
            return;
        }

        GameScene.Scene?.TryEquipMobileMountAccessory(item.UniqueID, slot);
    }

    private static void SetMountText(GTextField field, string text)
    {
        try { if (field != null && !field._disposed) field.text = text ?? string.Empty; } catch { }
    }

    private static void SetMountButtonAvailability(GButton button, bool enabled)
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

    private static void SetMountSlotAvailability(GObject target, bool enabled)
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

    private static void SetMountSlotVisual(GObject target, UserItem item, int slot)
    {
        if (target == null || target._disposed)
            return;

        string title = item?.FriendlyName;
        if (string.IsNullOrWhiteSpace(title))
            title = item == null ? MountSlotLabel(slot) + "（空）" : MountSlotLabel(slot);

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

    private static string MountSlotLabel(int slot)
    {
        return slot switch
        {
            (int)MountSlot.Reins => "缰绳",
            (int)MountSlot.Bells => "铃铛",
            (int)MountSlot.Saddle => "马鞍",
            (int)MountSlot.Ribbon => "蝴蝶结",
            (int)MountSlot.Mask => "面甲",
            _ => "配件",
        };
    }
}
