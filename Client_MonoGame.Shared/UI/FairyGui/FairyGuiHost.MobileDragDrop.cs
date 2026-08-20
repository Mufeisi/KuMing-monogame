using System;
using C = ClientPackets;
using FairyGUI;
using Microsoft.Xna.Framework;
using MonoShare.MirGraphics;
using MonoShare.MirObjects;
using MonoShare.MirScenes;

namespace MonoShare
{
    internal static partial class FairyGuiHost
    {
        private const string MobileItemDropGroundName = "__codex_mobile_item_drop_ground";

        private sealed class MobileItemDragPayload
        {
            public MirGridType Grid;
            public int SlotIndex;
            public ulong UniqueId;
            public bool Handled;
        }

        private sealed class MobileMagicDragPayload
        {
            public int HotKey;
            public byte Icon;
            public Spell Spell;
        }

        private static GGraph _mobileItemDropGround;
        private static EventCallback1 _mobileItemDropGroundDropCallback;
        private static bool _mobileDragDropHooksInstalled;
        private static EventCallback1 _mobileDragDropAgentDragEndCallback;
        private static bool _mobileItemDragActive;
        private static bool _mobileItemDragDropHandled;
        private static MobileItemDragPayload _mobileItemDragPayload;

        private static void EnsureMobileDragDropHooksInstalled()
        {
            if (_mobileDragDropHooksInstalled)
                return;

            _mobileDragDropHooksInstalled = true;
            _mobileDragDropAgentDragEndCallback = OnMobileDragDropAgentDragEnd;

            try
            {
                DragDropManager.inst.dragAgent.onDragEnd.Add(_mobileDragDropAgentDragEndCallback);
            }
            catch
            {
                _mobileDragDropHooksInstalled = false;
            }
        }

        private static void OnMobileDragDropAgentDragEnd(EventContext context)
        {
            SetMobileItemDropGroundActive(active: false);

            // DragDropManager 只会把 onDrop 分发到 touchTarget 的祖先；
            // 若 touchTarget 是全屏容器/遮罩且未注册 onDrop，就会导致“丢地上”无回调。
            // 这里做兜底：拖拽物品结束且没有任何 drop 目标处理时，按丢到地上处理。
            MobileItemDragPayload payload = _mobileItemDragPayload;
            bool shouldScheduleFallback = _mobileItemDragActive && payload != null;

            _mobileItemDragActive = false;
            _mobileItemDragPayload = null;

            if (shouldScheduleFallback)
            {
                try
                {
                    // onDrop 与 onDragEnd 在不同资源/平台下触发顺序可能不同：
                    // - 若先 onDragEnd 后 onDrop，会导致兜底与 onDrop 各执行一次（出现“1 变 2”）。
                    // 因此这里延后一帧再判断是否已被其它 drop 目标处理。
                    Timers.inst.CallLater(_ =>
                    {
                        if (_mobileItemDragDropHandled || payload.Handled)
                            return;

                        payload.Handled = true;
                        _mobileItemDragDropHandled = true;
                        TryHandleMobileItemDropToGround(payload);
                    });
                }
                catch
                {
                }
            }
        }

        private static void EnsureMobileItemDropGroundTargetCreated()
        {
            if (_stage == null || !_initialized)
                return;

            if (_mobileItemDropGround != null && !_mobileItemDropGround._disposed)
                return;

            _mobileItemDropGround = null;

            try
            {
                var graph = new GGraph
                {
                    name = MobileItemDropGroundName,
                    visible = false,
                    touchable = false,
                };

                try
                {
                    graph.DrawRect(GRoot.inst.width, GRoot.inst.height, 0, new Color(0, 0, 0, 0), new Color(0, 0, 0, 0));
                }
                catch
                {
                }

                try
                {
                    graph.AddRelation(GRoot.inst, RelationType.Size);
                    graph.SetSize(GRoot.inst.width, GRoot.inst.height);
                }
                catch
                {
                }

                try
                {
                    // 放在最底层，仅在拖拽期间开启 touchable，避免影响游戏点击/移动判定。
                    GRoot.inst.AddChildAt(graph, 0);
                }
                catch
                {
                    try { GRoot.inst.AddChild(graph); } catch { }
                }

                _mobileItemDropGroundDropCallback = OnMobileItemDroppedToGround;
                try { graph.AddEventListener("onDrop", _mobileItemDropGroundDropCallback); } catch { }

                _mobileItemDropGround = graph;
            }
            catch
            {
                _mobileItemDropGround = null;
            }
        }

        private static void SetMobileItemDropGroundActive(bool active)
        {
            EnsureMobileItemDropGroundTargetCreated();

            GGraph graph = _mobileItemDropGround;
            if (graph == null || graph._disposed)
                return;

            try
            {
                graph.visible = active;
                graph.touchable = active;
                graph.alpha = 0f;
            }
            catch
            {
            }
        }

        private static bool TryFindMobileInventoryItem(ulong uniqueId, out UserItem item, out int index)
        {
            item = null;
            index = -1;

            if (uniqueId == 0)
                return false;

            UserObject user = GameScene.User;
            if (user == null || user.Inventory == null)
                return false;

            UserItem[] inv = user.Inventory;
            for (int i = 0; i < inv.Length; i++)
            {
                UserItem t = inv[i];
                if (t == null || t.Info == null)
                    continue;

                if (t.UniqueID == uniqueId)
                {
                    item = t;
                    index = i;
                    return true;
                }
            }

            return false;
        }

        private static void TrySendDropItem(ulong uniqueId, ushort count)
        {
            if (uniqueId == 0 || count == 0)
                return;

            try
            {
                MonoShare.MirNetwork.Network.Enqueue(new C.DropItem
                {
                    UniqueID = uniqueId,
                    Count = count,
                });
            }
            catch (Exception ex)
            {
                CMain.SaveError("FairyGUI: 丢弃物品失败：" + ex.Message);
            }
        }

        private static void OnMobileItemDroppedOnInventorySlot(int targetInventoryIndex, EventContext context)
        {
            if (targetInventoryIndex < 0)
                return;

            MobileItemDragPayload payload = context?.data as MobileItemDragPayload;
            if (payload == null)
                return;

            payload.Handled = true;
            _mobileItemDragDropHandled = true;

            var user = GameScene.User;
            if (user == null || user.Inventory == null)
                return;

            UserItem[] inventory = user.Inventory;

            int beltIdx = 0;
            try { beltIdx = user.BeltIdx; } catch { beltIdx = 0; }
            beltIdx = Math.Clamp(beltIdx, 0, inventory.Length);

            if (payload.Grid == MirGridType.Inventory)
            {
                int from = payload.SlotIndex;
                int to = targetInventoryIndex;

                if (from < 0 || from >= inventory.Length)
                    return;
                if (to >= inventory.Length)
                    return;
                if (from == to)
                    return;

                UserItem moving = inventory[from];
                if (moving == null || moving.Info == null)
                    return;

                if (IsMobileItemLocked(moving.UniqueID))
                {
                    try { GameScene.Scene?.OutputMessage("物品已锁定，无法移动。"); } catch { }
                    return;
                }

                // 目标在腰带栏：仅允许消耗品，且任务物品不允许进入
                if (to < beltIdx && !IsMobileBeltAllowedItem(moving))
                {
                    try { GameScene.Scene?.OutputMessage("快捷栏仅允许消耗品，任务物品不可进入。"); } catch { }
                    return;
                }

                try
                {
                    MonoShare.MirNetwork.Network.Enqueue(new C.MoveItem
                    {
                        Grid = MirGridType.Inventory,
                        From = from,
                        To = to,
                    });
                }
                catch (Exception ex)
                {
                    CMain.SaveError("FairyGUI: 拖拽移动物品失败：" + ex.Message);
                }

                return;
            }

            if (payload.Grid == MirGridType.Storage)
            {
                UserItem[] storage = GameScene.Storage;
                if (storage == null)
                    return;

                int from = payload.SlotIndex;
                int to = targetInventoryIndex;

                if (from < 0 || from >= storage.Length)
                    return;
                if (to >= inventory.Length)
                    return;

                UserItem moving = storage[from];
                if (moving == null || moving.Info == null)
                    return;

                if (IsMobileItemLocked(moving.UniqueID))
                {
                    try { GameScene.Scene?.OutputMessage("物品已锁定，无法取回。"); } catch { }
                    return;
                }

                if (to < beltIdx && !IsMobileBeltAllowedItem(moving))
                {
                    try { GameScene.Scene?.OutputMessage("快捷栏仅允许消耗品，任务物品不可进入。"); } catch { }
                    return;
                }

                TryMoveStorageItemToInventory(storage, from, inventory, to, beltIdx);
                return;
            }
        }

        private static void OnMobileItemDroppedOnStorageSlot(int targetStorageIndex, EventContext context)
        {
            if (targetStorageIndex < 0)
                return;

            MobileItemDragPayload payload = context?.data as MobileItemDragPayload;
            if (payload == null)
                return;

            payload.Handled = true;
            _mobileItemDragDropHandled = true;

            if (payload.Grid != MirGridType.Inventory)
                return;

            UserObject user = GameScene.User;
            if (user == null || user.Inventory == null)
                return;

            UserItem[] inventory = user.Inventory;
            UserItem[] storage = GameScene.Storage;
            if (storage == null)
                return;

            int from = payload.SlotIndex;
            int to = targetStorageIndex;

            if (from < 0 || from >= inventory.Length)
                return;
            if (to >= storage.Length)
                return;

            UserItem moving = inventory[from];
            if (moving == null || moving.Info == null)
                return;

            if (IsMobileItemLocked(moving.UniqueID))
            {
                try { GameScene.Scene?.OutputMessage("物品已锁定，无法存入仓库。"); } catch { }
                return;
            }

            TryMoveInventoryItemToStorage(inventory, from, storage, to);
        }

        private static void OnMobileItemDroppedOnEquipmentSlot(int toSlot, EventContext context)
        {
            MobileItemDragPayload payload = context?.data as MobileItemDragPayload;
            if (payload == null)
                return;

            payload.Handled = true;
            _mobileItemDragDropHandled = true;

            if (payload.Grid != MirGridType.Inventory)
                return;

            if (!TryFindMobileInventoryItem(payload.UniqueId, out UserItem item, out _))
                return;

            if (IsMobileItemLocked(item.UniqueID))
            {
                try { GameScene.Scene?.OutputMessage("物品已锁定，无法穿戴。"); } catch { }
                return;
            }

            // 只处理可穿戴物品（服务端也会校验，这里做一次前置过滤减少无效发包）
            if (!TryResolveMobileEquipmentSlot(item, out _))
                return;

            try
            {
                if (CMain.Time < GameScene.UseItemTime)
                    return;
                GameScene.UseItemTime = CMain.Time + 250;
            }
            catch
            {
            }

            try
            {
                MonoShare.MirNetwork.Network.Enqueue(new C.EquipItem
                {
                    Grid = MirGridType.Inventory,
                    UniqueID = item.UniqueID,
                    To = toSlot,
                });
            }
            catch (Exception ex)
            {
                CMain.SaveError("FairyGUI: 拖拽穿戴物品失败：" + ex.Message);
            }
        }

        private static void OnMobileItemDroppedToGround(EventContext context)
        {
            MobileItemDragPayload payload = context?.data as MobileItemDragPayload;
            if (payload == null)
                return;

            payload.Handled = true;
            _mobileItemDragDropHandled = true;
            TryHandleMobileItemDropToGround(payload);
        }

        private static void TryHandleMobileItemDropToGround(MobileItemDragPayload payload)
        {
            if (payload == null)
                return;

            if (payload.Grid != MirGridType.Inventory)
                return;

            if (!TryFindMobileInventoryItem(payload.UniqueId, out UserItem item, out _))
                return;

            if (IsMobileItemLocked(item.UniqueID))
            {
                try { GameScene.Scene?.OutputMessage("物品已锁定，无法丢弃。"); } catch { }
                return;
            }

            bool dontDrop = false;
            try { dontDrop = item.Info != null && item.Info.Bind.HasFlag(BindMode.DontDrop); } catch { dontDrop = false; }
            if (dontDrop)
            {
                try { GameScene.Scene?.OutputMessage("该物品无法丢弃。"); } catch { }
                return;
            }

            ushort maxCount = 1;
            try { maxCount = item.Count; } catch { maxCount = 1; }
            if (maxCount <= 1)
            {
                TrySendDropItem(item.UniqueID, 1);
                return;
            }

            try
            {
                GameScene.Scene?.PromptMobileText(
                    title: "丢弃数量",
                    message: $"请输入丢弃数量（1-{maxCount}）",
                    initialText: maxCount.ToString(),
                    maxLength: 5,
                    numericOnly: true,
                    onOk: raw =>
                    {
                        if (!ushort.TryParse(raw, out ushort count))
                            return;

                        if (count < 1)
                            count = 1;
                        if (count > maxCount)
                            count = maxCount;

                        TrySendDropItem(item.UniqueID, count);
                    });
            }
            catch
            {
            }
        }

        private static void TryStartMobileItemDrag(GObject source, UserItem item, MobileItemDragPayload payload, int touchId)
        {
            if (source == null || source._disposed)
                return;

            if (item == null || item.Info == null || payload == null)
                return;

            if (IsMobileItemLocked(item.UniqueID))
            {
                try { GameScene.Scene?.OutputMessage("物品已锁定，无法拖拽。"); } catch { }
                return;
            }

            try { Libraries.Items.Touch(item.Image); } catch { }

            HideMobileItemTips();

            EnsureMobileDragDropHooksInstalled();
            SetMobileItemDropGroundActive(active: true);

            NTexture texture = null;
            try { texture = GetOrCreateItemIconTexture(item.Image); } catch { texture = null; }

            try
            {
                DragDropManager.inst.StartDrag(source, icon: string.Empty, sourceData: payload, touchPointID: touchId);
            }
            catch
            {
                SetMobileItemDropGroundActive(active: false);
                return;
            }

            _mobileItemDragActive = true;
            _mobileItemDragDropHandled = false;
            payload.Handled = false;
            _mobileItemDragPayload = payload;

            try
            {
                // 用纹理图标覆盖 Loader 的 url（无需依赖包内资源）。
                DragDropManager.inst.dragAgent.url = string.Empty;
                DragDropManager.inst.dragAgent.texture = texture;
            }
            catch
            {
            }

            try { DragDropManager.inst.dragAgent.SetSize(92, 92); } catch { }
        }

        private static void TryStartMobileMagicDrag(GObject source, MobileMagicDragPayload payload, int touchId)
        {
            if (source == null || source._disposed)
                return;

            if (payload == null)
                return;

            // HotKey<=0 表示该技能当前未绑定快捷键；仍允许拖拽（靠 Spell 在 Drop 目标处完成绑定/施放）。
            if (payload.HotKey <= 0 && payload.Spell == Spell.None)
                return;

            HideMobileItemTips();

            byte iconByte = payload.Icon;
            if (iconByte == 0)
            {
                try
                {
                    var user = GameScene.User;
                    if (user?.Magics != null)
                    {
                        for (int i = 0; i < user.Magics.Count; i++)
                        {
                            ClientMagic m = user.Magics[i];
                            if (m == null)
                                continue;

                            if (payload.HotKey > 0)
                            {
                                if (m.Key == payload.HotKey)
                                {
                                    iconByte = m.Icon;
                                    break;
                                }
                            }
                            else if (payload.Spell != Spell.None)
                            {
                                if (m.Spell == payload.Spell)
                                {
                                    iconByte = m.Icon;
                                    break;
                                }
                            }
                        }
                    }
                }
                catch
                {
                }
            }

            NTexture texture = null;
            if (iconByte != 0)
            {
                try { texture = GetOrCreateMagicIconTexture(iconByte); } catch { texture = null; }
            }

            try
            {
                DragDropManager.inst.StartDrag(source, icon: string.Empty, sourceData: payload, touchPointID: touchId);
            }
            catch
            {
                return;
            }

            try
            {
                DragDropManager.inst.dragAgent.url = string.Empty;
                DragDropManager.inst.dragAgent.texture = texture;
            }
            catch
            {
            }

            try { DragDropManager.inst.dragAgent.SetSize(92, 92); } catch { }
        }

        private sealed class MobileLongPressMagicDragBinding
        {
            public GObject Target;
            public Func<MobileMagicDragPayload> ResolvePayload;

            public float StartX;
            public float StartY;
            public int TouchId;
            public bool Armed;
            public bool LongPressTriggered;
            public bool SuppressClick;

            public readonly TimerCallback TimerCallback;
            public readonly EventCallback1 TouchBegin;
            public readonly EventCallback1 TouchMove;
            public readonly EventCallback1 TouchEnd;
            public readonly EventCallback1 ClickCapture;

            public MobileLongPressMagicDragBinding(GObject target, Func<MobileMagicDragPayload> resolvePayload)
            {
                Target = target;
                ResolvePayload = resolvePayload;

                TimerCallback = OnTimer;
                TouchBegin = OnTouchBegin;
                TouchMove = OnTouchMove;
                TouchEnd = OnTouchEnd;
                ClickCapture = OnClickCapture;
            }

            public void Attach()
            {
                if (Target == null || Target._disposed)
                    return;

                try { Target.onTouchBegin.Remove(TouchBegin); } catch { }
                try { Target.onTouchMove.Remove(TouchMove); } catch { }
                try { Target.onTouchEnd.Remove(TouchEnd); } catch { }
                try { Target.onClick.RemoveCapture(ClickCapture); } catch { }

                try { Target.onTouchBegin.Add(TouchBegin); } catch { }
                try { Target.onTouchMove.Add(TouchMove); } catch { }
                try { Target.onTouchEnd.Add(TouchEnd); } catch { }
                try { Target.onClick.AddCapture(ClickCapture); } catch { }
            }

            public void Detach()
            {
                try { Timers.inst.Remove(TimerCallback); } catch { }

                if (Target == null || Target._disposed)
                    return;

                try { Target.onTouchBegin.Remove(TouchBegin); } catch { }
                try { Target.onTouchMove.Remove(TouchMove); } catch { }
                try { Target.onTouchEnd.Remove(TouchEnd); } catch { }
                try { Target.onClick.RemoveCapture(ClickCapture); } catch { }
            }

            private void Arm(EventContext context)
            {
                if (Target == null || Target._disposed)
                    return;

                Armed = true;
                LongPressTriggered = false;
                SuppressClick = false;

                try
                {
                    InputEvent evt = context?.inputEvent;
                    StartX = evt != null ? evt.x : 0F;
                    StartY = evt != null ? evt.y : 0F;
                    TouchId = evt != null ? evt.touchId : -1;
                }
                catch
                {
                    StartX = 0F;
                    StartY = 0F;
                    TouchId = -1;
                }

                float thresholdSec = 0.45f;
                try
                {
                    thresholdSec = Math.Clamp(Settings.MobileTouchLongPressThresholdMs / 1000f, 0.25f, 1.2f);
                }
                catch
                {
                    thresholdSec = 0.45f;
                }

                try { Timers.inst.Remove(TimerCallback); } catch { }
                try { Timers.inst.Add(thresholdSec, 1, TimerCallback); } catch { }
            }

            private void CancelArmed()
            {
                Armed = false;
                try { Timers.inst.Remove(TimerCallback); } catch { }
            }

            private void OnTouchBegin(EventContext context)
            {
                Arm(context);
            }

            private void OnTouchMove(EventContext context)
            {
                if (!Armed)
                    return;

                float x = 0F;
                float y = 0F;
                try
                {
                    InputEvent evt = context?.inputEvent;
                    x = evt != null ? evt.x : 0F;
                    y = evt != null ? evt.y : 0F;
                }
                catch
                {
                    x = 0F;
                    y = 0F;
                }

                float dx = x - StartX;
                float dy = y - StartY;
                float distSq = dx * dx + dy * dy;

                float tolerance = 32F;
                try { tolerance = Math.Max(8F, Settings.MobileTouchTapMoveTolerancePixels); } catch { tolerance = 32F; }
                if (distSq > tolerance * tolerance)
                    CancelArmed();
            }

            private void OnTouchEnd(EventContext context)
            {
                if (Armed)
                {
                    CancelArmed();
                    return;
                }

                if (!SuppressClick)
                    return;

                float x = 0F;
                float y = 0F;
                try
                {
                    InputEvent evt = context?.inputEvent;
                    x = evt != null ? evt.x : 0F;
                    y = evt != null ? evt.y : 0F;
                }
                catch
                {
                    x = 0F;
                    y = 0F;
                }

                bool inside = true;
                try
                {
                    var rect = Target.LocalToGlobal(new System.Drawing.RectangleF(0, 0, Target.width, Target.height));
                    inside = x >= rect.Left && x <= rect.Right && y >= rect.Top && y <= rect.Bottom;
                }
                catch
                {
                    inside = true;
                }

                if (!inside)
                {
                    SuppressClick = false;
                    LongPressTriggered = false;
                }
            }

            private void OnTimer(object param)
            {
                if (!Armed)
                    return;

                Armed = false;
                LongPressTriggered = true;
                SuppressClick = true;

                if (Target == null || Target._disposed)
                    return;

                MobileMagicDragPayload payload = null;
                try { payload = ResolvePayload != null ? ResolvePayload() : null; } catch { payload = null; }
                if (payload == null)
                    return;

                if (payload.HotKey <= 0 && payload.Spell == Spell.None)
                    return;

                TryStartMobileMagicDrag(Target, payload, TouchId);
            }

            private void OnClickCapture(EventContext context)
            {
                if (!SuppressClick)
                    return;

                try { context?.StopPropagation(); } catch { }
                try { context?.PreventDefault(); } catch { }

                SuppressClick = false;
                LongPressTriggered = false;
            }
        }

        private sealed class MobileLongPressDragBinding
        {
            public GObject Target;
            public Func<UserItem> ResolveItem;
            public Func<MobileItemDragPayload> ResolvePayload;

            public float StartX;
            public float StartY;
            public int TouchId;
            public bool Armed;
            public bool LongPressTriggered;
            public bool SuppressClick;

            public readonly TimerCallback TimerCallback;
            public readonly EventCallback1 TouchBegin;
            public readonly EventCallback1 TouchMove;
            public readonly EventCallback1 TouchEnd;
            public readonly EventCallback1 ClickCapture;

            public MobileLongPressDragBinding(GObject target, Func<UserItem> resolveItem, Func<MobileItemDragPayload> resolvePayload)
            {
                Target = target;
                ResolveItem = resolveItem;
                ResolvePayload = resolvePayload;

                TimerCallback = OnTimer;
                TouchBegin = OnTouchBegin;
                TouchMove = OnTouchMove;
                TouchEnd = OnTouchEnd;
                ClickCapture = OnClickCapture;
            }

            public void Attach()
            {
                if (Target == null || Target._disposed)
                    return;

                try { Target.onTouchBegin.Remove(TouchBegin); } catch { }
                try { Target.onTouchMove.Remove(TouchMove); } catch { }
                try { Target.onTouchEnd.Remove(TouchEnd); } catch { }
                try { Target.onClick.RemoveCapture(ClickCapture); } catch { }

                try { Target.onTouchBegin.Add(TouchBegin); } catch { }
                try { Target.onTouchMove.Add(TouchMove); } catch { }
                try { Target.onTouchEnd.Add(TouchEnd); } catch { }
                try { Target.onClick.AddCapture(ClickCapture); } catch { }
            }

            public void Detach()
            {
                try { Timers.inst.Remove(TimerCallback); } catch { }

                if (Target == null || Target._disposed)
                    return;

                try { Target.onTouchBegin.Remove(TouchBegin); } catch { }
                try { Target.onTouchMove.Remove(TouchMove); } catch { }
                try { Target.onTouchEnd.Remove(TouchEnd); } catch { }
                try { Target.onClick.RemoveCapture(ClickCapture); } catch { }
            }

            private void Arm(EventContext context)
            {
                if (Target == null || Target._disposed)
                    return;

                Armed = true;
                LongPressTriggered = false;
                SuppressClick = false;

                try
                {
                    InputEvent evt = context?.inputEvent;
                    StartX = evt != null ? evt.x : 0F;
                    StartY = evt != null ? evt.y : 0F;
                    TouchId = evt != null ? evt.touchId : -1;
                }
                catch
                {
                    StartX = 0F;
                    StartY = 0F;
                    TouchId = -1;
                }

                float thresholdSec = 0.45f;
                try
                {
                    thresholdSec = Math.Clamp(Settings.MobileTouchLongPressThresholdMs / 1000f, 0.25f, 1.2f);
                }
                catch
                {
                    thresholdSec = 0.45f;
                }

                try { Timers.inst.Remove(TimerCallback); } catch { }
                try { Timers.inst.Add(thresholdSec, 1, TimerCallback); } catch { }
            }

            private void CancelArmed()
            {
                Armed = false;
                try { Timers.inst.Remove(TimerCallback); } catch { }
            }

            private void OnTouchBegin(EventContext context)
            {
                Arm(context);
            }

            private void OnTouchMove(EventContext context)
            {
                if (!Armed)
                    return;

                float x = 0F;
                float y = 0F;
                try
                {
                    InputEvent evt = context?.inputEvent;
                    x = evt != null ? evt.x : 0F;
                    y = evt != null ? evt.y : 0F;
                }
                catch
                {
                    x = 0F;
                    y = 0F;
                }

                float dx = x - StartX;
                float dy = y - StartY;
                float distSq = dx * dx + dy * dy;

                float tolerance = 32F;
                try { tolerance = Math.Max(8F, Settings.MobileTouchTapMoveTolerancePixels); } catch { tolerance = 32F; }
                if (distSq > tolerance * tolerance)
                    CancelArmed();
            }

            private void OnTouchEnd(EventContext context)
            {
                if (Armed)
                {
                    CancelArmed();
                    return;
                }

                if (!SuppressClick)
                    return;

                float x = 0F;
                float y = 0F;
                try
                {
                    InputEvent evt = context?.inputEvent;
                    x = evt != null ? evt.x : 0F;
                    y = evt != null ? evt.y : 0F;
                }
                catch
                {
                    x = 0F;
                    y = 0F;
                }

                bool inside = true;
                try
                {
                    var rect = Target.LocalToGlobal(new System.Drawing.RectangleF(0, 0, Target.width, Target.height));
                    inside = x >= rect.Left && x <= rect.Right && y >= rect.Top && y <= rect.Bottom;
                }
                catch
                {
                    inside = true;
                }

                // 抬起点不在目标上：不触发 click，避免 SuppressClick 残留影响下一次点击
                if (!inside)
                {
                    SuppressClick = false;
                    LongPressTriggered = false;
                }
            }

            private void OnTimer(object param)
            {
                if (!Armed)
                    return;

                Armed = false;
                LongPressTriggered = true;
                SuppressClick = true;

                if (Target == null || Target._disposed)
                    return;

                UserItem item = null;
                try { item = ResolveItem != null ? ResolveItem() : null; } catch { item = null; }
                if (item == null || item.Info == null)
                    return;

                MobileItemDragPayload payload = null;
                try { payload = ResolvePayload != null ? ResolvePayload() : null; } catch { payload = null; }
                if (payload == null)
                    return;

                TryStartMobileItemDrag(Target, item, payload, TouchId);
            }

            private void OnClickCapture(EventContext context)
            {
                if (!SuppressClick)
                    return;

                try { context?.StopPropagation(); } catch { }
                try { context?.PreventDefault(); } catch { }

                SuppressClick = false;
                LongPressTriggered = false;
            }
        }

        private static MobileLongPressMagicDragBinding BindMobileLongPressMagicDrag(GObject target, Func<MobileMagicDragPayload> resolvePayload)
        {
            if (target == null || target._disposed || resolvePayload == null)
                return null;

            var binding = new MobileLongPressMagicDragBinding(target, resolvePayload);
            binding.Attach();
            return binding;
        }

        private static void UnbindMobileLongPressMagicDrag(MobileLongPressMagicDragBinding binding)
        {
            if (binding == null)
                return;

            try { binding.Detach(); } catch { }
        }

        private static MobileLongPressDragBinding BindMobileLongPressItemDrag(GObject target, Func<UserItem> resolveItem, Func<MobileItemDragPayload> resolvePayload)
        {
            if (target == null || target._disposed || resolveItem == null || resolvePayload == null)
                return null;

            var binding = new MobileLongPressDragBinding(target, resolveItem, resolvePayload);
            binding.Attach();
            return binding;
        }

        private static void UnbindMobileLongPressItemDrag(MobileLongPressDragBinding binding)
        {
            if (binding == null)
                return;

            try { binding.Detach(); } catch { }
        }
    }
}
