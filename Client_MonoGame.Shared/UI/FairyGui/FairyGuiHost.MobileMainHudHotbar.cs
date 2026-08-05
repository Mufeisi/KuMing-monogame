using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using C = ClientPackets;
using FairyGUI;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoShare.MirGraphics;
using MonoShare.MirNetwork;
using MonoShare.MirObjects;
using MonoShare.MirScenes;

namespace MonoShare
{
    internal static partial class FairyGuiHost
    {
        private const string MobileMainHudBeltGridConfigKey = "MobileMainHud.BeltGrid";
        private const string MobileMainHudSkillGridConfigKey = "MobileMainHud.SkillGrid";
        private const string MobileMainHudAttackButtonIconName = "__codex_mobile_main_hud_attack_button_icon";

        private static readonly string[] DefaultMainHudBeltGridKeywords = { "belt", "bar", "item", "物品", "道具" };
        private static readonly string[] DefaultMainHudSkillGridKeywords = { "skill", "magic", "技能", "快捷", "SkillCell" };

        private static MobileItemGridBinding _mobileMainHudBeltBinding;
        private static DateTime _nextMobileMainHudBeltBindAttemptUtc = DateTime.MinValue;
        private static DateTime _nextMobileMainHudBeltMissingSlotLogUtc = DateTime.MinValue;
        private static DateTime _nextMobileMainHudBeltAutoMoveUtc = DateTime.MinValue;
        private static bool _mobileMainHudBeltBindingsDumped;

        private sealed class MobileMainHudSkillHotbarBinding
        {
            public GComponent GridRoot;
            public string GridResolveInfo;
            public string OverrideSpec;
            public string[] OverrideKeywords;

            public readonly List<MobileMagicSlotBinding> Slots = new List<MobileMagicSlotBinding>(16);
        }

        private static MobileMainHudSkillHotbarBinding _mobileMainHudSkillHotbarBinding;
        private static DateTime _nextMobileMainHudSkillHotbarBindAttemptUtc = DateTime.MinValue;
        private static bool _mobileMainHudSkillHotbarBindingsDumped;

        private static void ResetMobileMainHudHotbars()
        {
            ResetMobileMainHudBeltBindings();
            ResetMobileMainHudSkillHotbarBindings();
        }

        private static void ResetMobileMainHudBeltBindings()
        {
            try
            {
                DetachMobileItemGridSlotCallbacks(_mobileMainHudBeltBinding);
            }
            catch
            {
            }

            _mobileMainHudBeltBinding = null;
            _nextMobileMainHudBeltBindAttemptUtc = DateTime.MinValue;
            _mobileMainHudBeltBindingsDumped = false;
        }

        private static void ResetMobileMainHudSkillHotbarBindings()
        {
            try
            {
                MobileMainHudSkillHotbarBinding binding = _mobileMainHudSkillHotbarBinding;
                if (binding != null)
                {
                    for (int i = 0; i < binding.Slots.Count; i++)
                    {
                        MobileMagicSlotBinding slot = binding.Slots[i];
                        if (slot == null)
                            continue;

                        try
                        {
                            UnbindMobileLongPressMagicDrag(slot.LongPressDragBinding);
                        }
                        catch
                        {
                        }

                        slot.LongPressDragBinding = null;

                        try
                        {
                            if (slot.Root != null && !slot.Root._disposed && slot.ClickCallback != null)
                                slot.Root.onClick.Remove(slot.ClickCallback);
                        }
                        catch
                        {
                        }

                        slot.ClickCallback = null;

                        try
                        {
                            if (slot.Root != null && !slot.Root._disposed && slot.DropCallback != null)
                                slot.Root.RemoveEventListener("onDrop", slot.DropCallback);
                        }
                        catch
                        {
                        }

                        slot.DropCallback = null;
                    }
                }
            }
            catch
            {
            }

            _mobileMainHudSkillHotbarBinding = null;
            _nextMobileMainHudSkillHotbarBindAttemptUtc = DateTime.MinValue;
            _mobileMainHudSkillHotbarBindingsDumped = false;
        }

        private static void EnsureMobileInteractiveChain(GObject obj, GObject stopParent = null, int maxDepth = 12)
        {
            if (obj == null || obj._disposed)
                return;

            try { SetTouchableRecursive(obj, touchable: true); } catch { }

            try
            {
                GObject current = obj;
                int guard = 0;
                while (current != null && !current._disposed && guard++ < maxDepth)
                {
                    try
                    {
                        current.touchable = true;
                        if (current is GButton button)
                        {
                            button.enabled = true;
                            button.grayed = false;
                            button.changeStateOnClick = false;
                        }
                    }
                    catch
                    {
                    }

                    if (ReferenceEquals(current, stopParent))
                        break;

                    current = current.parent;
                }
            }
            catch
            {
            }
        }

        private static void DisableMobileDescendantTouch(GObject obj)
        {
            if (obj == null || obj._disposed || obj is not GComponent component || component._disposed)
                return;

            try
            {
                foreach (GObject child in Enumerate(component))
                {
                    if (child == null || child._disposed || ReferenceEquals(child, obj))
                        continue;

                    try { child.touchable = false; } catch { }
                }
            }
            catch
            {
            }
        }

        private static bool TryCollectNamedMainHudAttackButtons(out GComponent gridRoot, out string resolveInfo, out List<GComponent> slots)
        {
            gridRoot = null;
            resolveInfo = null;
            slots = null;

            if (_mobileMainHud == null || _mobileMainHud._disposed)
                return false;

            GComponent attackRoot = null;
            try { attackRoot = TryFindChildByNameRecursive(_mobileMainHud, "DArrackModelUI") as GComponent; } catch { attackRoot = null; }
            if (attackRoot == null || attackRoot._disposed)
                return false;

            var resolvedSlots = new List<GComponent>(8);
            for (int i = 1; i <= 8; i++)
            {
                string buttonName = "DBtnAttack" + i;
                GComponent slot = null;
                try { slot = attackRoot.GetChild(buttonName) as GComponent; } catch { slot = null; }
                if (slot == null || slot._disposed)
                {
                    try { slot = TryFindChildByNameRecursive(attackRoot, buttonName) as GComponent; } catch { slot = null; }
                }

                if (slot == null || slot._disposed)
                    return false;

                resolvedSlots.Add(slot);
            }

            gridRoot = attackRoot;
            resolveInfo = DescribeObject(_mobileMainHud, attackRoot) + " (name:DArrackModelUI)";
            slots = resolvedSlots;
            return true;
        }

        private static GLoader EnsureMobileMainHudAttackButtonIcon(GComponent slotRoot)
        {
            if (slotRoot == null || slotRoot._disposed)
                return null;

            GLoader icon = null;
            try { icon = slotRoot.GetChild(MobileMainHudAttackButtonIconName) as GLoader; } catch { icon = null; }
            if (icon == null || icon._disposed)
            {
                try
                {
                    icon = new GLoader
                    {
                        name = MobileMainHudAttackButtonIconName,
                        touchable = false,
                        visible = false,
                        url = string.Empty,
                        showErrorSign = false,
                    };
                    slotRoot.AddChild(icon);
                }
                catch
                {
                    icon = null;
                }
            }

            if (icon == null || icon._disposed)
                return null;

            try
            {
                float size = Math.Min(slotRoot.width, slotRoot.height) * 0.58f;
                size = Math.Clamp(size, 18f, 72f);
                icon.SetSize(size, size);
                icon.SetPosition((slotRoot.width - size) * 0.5f, (slotRoot.height - size) * 0.5f);
                slotRoot.SetChildIndex(icon, slotRoot.numChildren - 1);
            }
            catch
            {
            }

            return icon;
        }

        private static bool TryAssignMobileMagicToHotKey(int hotKey, MobileMagicDragPayload payload)
        {
            if (hotKey <= 0 || payload == null)
                return false;

            hotKey = Math.Clamp(hotKey, 1, 16);

            UserObject user = GameScene.User;
            List<ClientMagic> magics = user?.Magics;
            if (magics == null)
                return false;

            ClientMagic magic = null;
            for (int i = 0; i < magics.Count; i++)
            {
                ClientMagic candidate = magics[i];
                if (candidate == null)
                    continue;

                if (payload.Spell != Spell.None)
                {
                    if (candidate.Spell == payload.Spell)
                    {
                        magic = candidate;
                        break;
                    }
                }
                else if (payload.HotKey > 0 && candidate.Key == payload.HotKey)
                {
                    magic = candidate;
                    break;
                }
            }

            if (magic == null)
                return false;

            byte newKey = (byte)hotKey;
            byte oldKey = magic.Key;
            if (oldKey == newKey)
                return true;

            for (int i = 0; i < magics.Count; i++)
            {
                ClientMagic other = magics[i];
                if (other == null || ReferenceEquals(other, magic))
                    continue;

                if (other.Key == newKey)
                    other.Key = 0;
            }

            try
            {
                MonoShare.MirNetwork.Network.Enqueue(new C.MagicKey
                {
                    Spell = magic.Spell,
                    Key = newKey,
                    OldKey = oldKey,
                });
            }
            catch (Exception ex)
            {
                CMain.SaveError("FairyGUI: 主界面技能快捷栏绑定失败：" + ex.Message);
                return false;
            }

            magic.Key = newKey;
            return true;
        }

        private static void OnMobileMainHudSkillHotbarDropped(int hotKey, EventContext context)
        {
            try { context?.StopPropagation(); } catch { }
            try { context?.PreventDefault(); } catch { }

            if (context?.data is not MobileMagicDragPayload payload)
                return;

            if (!TryAssignMobileMagicToHotKey(hotKey, payload))
                return;

            try { TryRefreshMobileMainHudSkillHotbar(force: true); } catch { }
            try { TryRefreshMobileMainHudAttackCircleIfDue(force: true); } catch { }
        }

        private static void TryRefreshMobileMainHudHotbarsIfDue(bool force)
        {
            if (_stage == null || !_initialized || !_packagesLoaded)
                return;

            if (_mobileMainHud == null || _mobileMainHud._disposed)
            {
                if (_mobileMainHudBeltBinding != null || _mobileMainHudSkillHotbarBinding != null)
                    ResetMobileMainHudHotbars();
                return;
            }

            TryBindMobileMainHudBeltIfDue();
            TryBindMobileMainHudSkillHotbarIfDue();

            TryRefreshMobileMainHudBelt(force);
            TryRefreshMobileMainHudSkillHotbar(force);
        }

        private static void TryBindMobileMainHudBeltIfDue()
        {
            if (_mobileMainHud == null || _mobileMainHud._disposed)
                return;

            MobileItemGridBinding binding = _mobileMainHudBeltBinding;
            if (binding != null && (binding.Window == null || binding.Window._disposed))
            {
                ResetMobileMainHudBeltBindings();
                binding = null;
            }

            if (binding == null || !ReferenceEquals(binding.Window, _mobileMainHud))
            {
                ResetMobileMainHudBeltBindings();

                binding = new MobileItemGridBinding
                {
                    WindowKey = "MainHud.Belt",
                    Window = _mobileMainHud,
                    ResolveInfo = "主界面",
                };

                _mobileMainHudBeltBinding = binding;
            }

            if (binding.Slots.Count > 0)
                return;

            if (DateTime.UtcNow < _nextMobileMainHudBeltBindAttemptUtc)
                return;

            _nextMobileMainHudBeltBindAttemptUtc = DateTime.UtcNow.AddSeconds(2);

            int desiredSlots = 6;
            try
            {
                desiredSlots = GameScene.User?.BeltIdx ?? desiredSlots;
            }
            catch
            {
                desiredSlots = 6;
            }

            desiredSlots = Math.Clamp(desiredSlots, 0, 46);

            string overrideSpec = string.Empty;
            try
            {
                InIReader reader = TryCreateConfigReader();
                if (reader != null)
                {
                    overrideSpec = reader.ReadString(
                        FairyGuiConfigSectionName,
                        MobileMainHudBeltGridConfigKey,
                        string.Empty,
                        writeWhenNull: false);
                }
            }
            catch
            {
                overrideSpec = string.Empty;
            }

            overrideSpec = overrideSpec?.Trim() ?? string.Empty;
            binding.OverrideSpec = overrideSpec;
            binding.OverrideKeywords = null;

            GComponent gridRoot = _mobileMainHud;
            string gridResolveInfo = DescribeObject(_mobileMainHud, _mobileMainHud);

            if (!string.IsNullOrWhiteSpace(overrideSpec))
            {
                if (TryResolveMobileMainHudObjectBySpec(_mobileMainHud, overrideSpec, out GObject resolved, out string[] keywords))
                {
                    if (resolved is GComponent resolvedComponent && !resolvedComponent._disposed)
                    {
                        gridRoot = resolvedComponent;
                        gridResolveInfo = DescribeObject(_mobileMainHud, resolvedComponent) + " (override)";
                    }
                    else if (keywords != null && keywords.Length > 0)
                    {
                        binding.OverrideKeywords = keywords;
                    }
                }
                else
                {
                    binding.OverrideKeywords = SplitKeywords(overrideSpec);
                }
            }

            if (binding.OverrideKeywords != null && binding.OverrideKeywords.Length > 0)
            {
                List<(int Score, GObject Target)> candidates =
                    CollectMobileChatCandidates(_mobileMainHud, obj => obj is GComponent, binding.OverrideKeywords, ScoreMobileMainHudBeltGridCandidate);

                GComponent selected = SelectMobileChatCandidate<GComponent>(candidates, minScore: 40);
                if (selected != null && !selected._disposed)
                {
                    gridRoot = selected;
                    gridResolveInfo = DescribeObject(_mobileMainHud, selected) + " (keywords)";
                }
            }
            else
            {
                gridRoot = AutoSelectMainHudGridRootFromItemSlots(_mobileMainHud, desiredSlots, out gridResolveInfo);

                if (ReferenceEquals(gridRoot, _mobileMainHud))
                {
                    List<(int Score, GObject Target)> candidates =
                        CollectMobileChatCandidates(_mobileMainHud, obj => obj is GComponent, DefaultMainHudBeltGridKeywords, ScoreMobileMainHudBeltGridCandidate);

                    GComponent selected = SelectMobileChatCandidate<GComponent>(candidates, minScore: 55);
                    if (selected != null && !selected._disposed)
                    {
                        gridRoot = selected;
                        gridResolveInfo = DescribeObject(_mobileMainHud, selected) + " (auto)";
                    }
                }
            }

            binding.GridRoot = gridRoot;
            binding.GridResolveInfo = gridResolveInfo;

            List<GComponent> slotCandidates = CollectInventorySlotCandidates(gridRoot);
            if (slotCandidates.Count == 0)
            {
                if (DateTime.UtcNow >= _nextMobileMainHudBeltMissingSlotLogUtc)
                {
                    _nextMobileMainHudBeltMissingSlotLogUtc = DateTime.UtcNow.AddSeconds(30);
                    CMain.SaveError("FairyGUI: 主界面腰带栏未找到物品格子（Belt）。可在 Mir2Config.ini 设置 [" + FairyGuiConfigSectionName + "] " +
                                    MobileMainHudBeltGridConfigKey + "=idx:... 指定格子根节点（或 path:/name:/item:/url:/title: / 关键字列表 a|b|c）。");
                }
                return;
            }

            List<GComponent> selectedSlots = SelectBottomMostSlots(slotCandidates, desiredSlots);

            binding.Slots.Clear();
            for (int i = 0; i < selectedSlots.Count; i++)
            {
                int slotIndex = i;
                GComponent slotRoot = selectedSlots[i];

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

                EventCallback0 callback = () => TryUseMobileBeltItem(slotIndex);
                try
                {
                    DisableMobileDescendantTouch(slotRoot);
                    EnsureMobileInteractiveChain(slotRoot, _mobileMainHud);
                    slotRoot.onClick.Add(callback);
                    slot.ClickCallback = callback;
                }
                catch
                {
                    slot.ClickCallback = null;
                }

                try
                {
                    DisableMobileDescendantTouch(slotRoot);
                    EnsureMobileInteractiveChain(slotRoot, _mobileMainHud);
                    slot.LongPressDragBinding = BindMobileLongPressItemDrag(
                        slotRoot,
                        resolveItem: () =>
                        {
                            try
                            {
                                UserItem[] inventory = GameScene.User?.Inventory;
                                int idx = slot.SlotIndex;
                                if (inventory != null && idx >= 0 && idx < inventory.Length)
                                    return inventory[idx];
                            }
                            catch
                            {
                            }

                            return null;
                        },
                        resolvePayload: () =>
                        {
                            UserItem t = null;
                            try
                            {
                                UserItem[] inventory = GameScene.User?.Inventory;
                                int idx = slot.SlotIndex;
                                if (inventory != null && idx >= 0 && idx < inventory.Length)
                                    t = inventory[idx];
                            }
                            catch
                            {
                                t = null;
                            }

                            if (t == null || t.Info == null)
                                return null;

                            return new MobileItemDragPayload
                            {
                                Grid = MirGridType.Inventory,
                                SlotIndex = slot.SlotIndex,
                                UniqueId = t.UniqueID,
                            };
                        });
                }
                catch
                {
                    slot.LongPressDragBinding = null;
                }

                try
                {
                    slot.DropCallback = context => OnMobileItemDroppedOnInventorySlot(slot.SlotIndex, context);
                    slotRoot.AddEventListener("onDrop", slot.DropCallback);
                }
                catch
                {
                    slot.DropCallback = null;
                }

                binding.Slots.Add(slot);
            }

            TryDumpMobileMainHudBeltBindingsReportIfDue(binding, desiredSlots, slotCandidates);

            CMain.SaveLog($"FairyGUI: 主界面腰带栏绑定完成：Slots={binding.Slots.Count} GridRoot={binding.GridResolveInfo}");
        }

        private static int ScoreMobileMainHudBeltGridCandidate(GObject obj, string[] keywords)
        {
            int score = ScoreAnyField(obj, keywords, equalsWeight: 220, startsWithWeight: 130, containsWeight: 60);
            score += ScoreRect(obj, preferLower: true, areaDivisor: 900, maxAreaScore: 120);
            if (obj.packageItem?.exported == true)
                score += 10;
            return score;
        }

        private static GComponent AutoSelectMainHudGridRootFromItemSlots(GComponent root, int desiredSlots, out string resolveInfo)
        {
            resolveInfo = DescribeObject(root, root) + " (auto:root)";

            if (root == null || root._disposed)
                return root;

            List<GComponent> slotCandidates = CollectInventorySlotCandidates(root);
            if (slotCandidates.Count == 0 || desiredSlots <= 0)
                return root;

            if (slotCandidates.Count <= desiredSlots + 2)
                return root;

            var descendantCount = new Dictionary<GComponent, int>();
            for (int i = 0; i < slotCandidates.Count; i++)
            {
                GObject current = slotCandidates[i];
                while (current != null)
                {
                    if (current is GComponent component && !component._disposed)
                    {
                        descendantCount.TryGetValue(component, out int count);
                        descendantCount[component] = count + 1;

                        if (ReferenceEquals(component, root))
                            break;
                    }

                    current = current.parent;
                }
            }

            GComponent best = root;
            int bestScore = int.MinValue;

            foreach (KeyValuePair<GComponent, int> pair in descendantCount)
            {
                GComponent component = pair.Key;
                int count = pair.Value;
                if (component == null || component._disposed)
                    continue;

                if (count < desiredSlots)
                    continue;

                if (count > desiredSlots * 2 + 4)
                    continue;

                int depth = 0;
                GObject parent = component.parent;
                while (parent != null && !ReferenceEquals(parent, root))
                {
                    depth++;
                    parent = parent.parent;
                }

                int score = 10000 - Math.Abs(count - desiredSlots) * 1500;
                score += ScoreRect(component, preferLower: true, areaDivisor: 900, maxAreaScore: 120);
                score += depth * 25;
                if (component.touchable)
                    score += 30;
                if (component.visible)
                    score += 20;
                if (component.packageItem?.exported == true)
                    score += 60;

                if (score > bestScore)
                {
                    bestScore = score;
                    best = component;
                }
            }

            if (best != null && !best._disposed && !ReferenceEquals(best, root))
            {
                resolveInfo = DescribeObject(root, best) + " (auto:desc)";
                return best;
            }

            return root;
        }

        private static List<GComponent> SelectBottomMostSlots(List<GComponent> candidates, int desiredSlots)
        {
            var list = new List<(GComponent Slot, Vector2 Pos)>(candidates?.Count ?? 0);
            if (candidates != null)
            {
                for (int i = 0; i < candidates.Count; i++)
                {
                    GComponent slot = candidates[i];
                    if (slot == null || slot._disposed)
                        continue;

                    Vector2 pos;
                    try
                    {
                        pos = slot.LocalToGlobal(Vector2.Zero);
                    }
                    catch
                    {
                        pos = Vector2.Zero;
                    }

                    list.Add((slot, pos));
                }
            }

            list.Sort((a, b) =>
            {
                int y = b.Pos.Y.CompareTo(a.Pos.Y);
                if (y != 0)
                    return y;
                return a.Pos.X.CompareTo(b.Pos.X);
            });

            int take = desiredSlots <= 0 ? list.Count : Math.Min(desiredSlots, list.Count);
            var selected = new List<GComponent>(take);
            for (int i = 0; i < take; i++)
                selected.Add(list[i].Slot);

            selected.Sort((a, b) =>
            {
                Vector2 pa;
                Vector2 pb;
                try
                {
                    pa = a.LocalToGlobal(Vector2.Zero);
                }
                catch
                {
                    pa = Vector2.Zero;
                }

                try
                {
                    pb = b.LocalToGlobal(Vector2.Zero);
                }
                catch
                {
                    pb = Vector2.Zero;
                }

                int y = pa.Y.CompareTo(pb.Y);
                if (y != 0)
                    return y;

                return pa.X.CompareTo(pb.X);
            });

            return selected;
        }

        private static void TryUseMobileBeltItem(int slotIndex)
        {
            if (slotIndex < 0)
                return;

            UserObject user = GameScene.User;
            if (user == null || user.Inventory == null)
                return;

            if (slotIndex >= user.Inventory.Length)
                return;

            UserItem item = user.Inventory[slotIndex];
            if (item == null || item.Info == null)
            {
                HideMobileItemTips();
                return;
            }

            if (IsMobileItemLocked(item.UniqueID))
            {
                try { GameScene.Scene?.OutputMessage("物品已锁定，无法操作。"); } catch { }
                return;
            }

            // 移动端触摸交互：单击看属性，双击才使用
            if (!IsMobileBeltSlotDoubleTap(slotIndex))
            {
                ShowMobileItemTips(item);
                return;
            }

            // 腰带栏限制：只允许消耗品进入，且任务物品不允许进入
            if (!IsMobileBeltAllowedItem(item))
            {
                ShowMobileItemTips(item);
                return;
            }

            if (TryUseMobileInventoryItem(item))
            {
                HideMobileItemTips();
                return;
            }

            ShowMobileItemTips(item);
            return;
        }

        private static void TryAutoMoveNonConsumablesOutOfBeltIfDue()
        {
            // 兜底：如果有非消耗品被放进了快捷栏（Inventory[0..BeltIdx)），尽量自动移到包裹区域，避免“快捷栏只能放消耗品”导致物品不可见/不可用。
            if (_nextMobileMainHudBeltAutoMoveUtc != DateTime.MinValue && DateTime.UtcNow < _nextMobileMainHudBeltAutoMoveUtc)
                return;

            UserObject user = GameScene.User;
            if (user == null || user.Inventory == null)
                return;

            int beltIdx;
            try { beltIdx = user.BeltIdx; }
            catch { beltIdx = 0; }

            if (beltIdx <= 0)
                return;

            UserItem[] inventory = user.Inventory;
            if (inventory == null || inventory.Length == 0)
                return;

            if (beltIdx > inventory.Length)
                beltIdx = inventory.Length;

            // 找一个包裹区域的空位（优先最前）
            int empty = -1;
            for (int i = beltIdx; i < inventory.Length; i++)
            {
                UserItem t = inventory[i];
                if (t == null || t.Info == null)
                {
                    empty = i;
                    break;
                }
            }

            if (empty < 0)
                return;

            // 找第一个非消耗品并移动到空位（每次最多移动一个，避免刷包/频繁发包）
            for (int i = 0; i < beltIdx; i++)
            {
                UserItem t = inventory[i];
                if (t == null || t.Info == null)
                    continue;

                if (IsMobileBeltAllowedItem(t))
                    continue;

                if (IsMobileItemLocked(t.UniqueID))
                    continue;

                try
                {
                    MonoShare.MirNetwork.Network.Enqueue(new C.MoveItem
                    {
                        Grid = MirGridType.Inventory,
                        From = i,
                        To = empty,
                    });

                    _nextMobileMainHudBeltAutoMoveUtc = DateTime.UtcNow.AddSeconds(1);
                }
                catch
                {
                }

                break;
            }
        }

        private static void TryRefreshMobileMainHudBelt(bool force)
        {
            MobileItemGridBinding binding = _mobileMainHudBeltBinding;
            if (binding == null || binding.Slots.Count == 0)
                return;

            if (binding.Window == null || binding.Window._disposed)
            {
                ResetMobileMainHudBeltBindings();
                return;
            }

            UserObject user = GameScene.User;
            if (user == null || user.Inventory == null)
                return;

            UserItem[] items = user.Inventory;

            TryAutoMoveNonConsumablesOutOfBeltIfDue();

            for (int i = 0; i < binding.Slots.Count; i++)
            {
                MobileItemSlotBinding slot = binding.Slots[i];
                if (slot == null || slot.Root == null || slot.Root._disposed)
                {
                    binding.Slots.Clear();
                    _nextMobileMainHudBeltBindAttemptUtc = DateTime.MinValue;
                    return;
                }

                UserItem item = slot.SlotIndex < items.Length ? items[slot.SlotIndex] : null;
                if (item == null || item.Info == null || !IsMobileBeltAllowedItem(item))
                {
                    if (slot.HasItem)
                        ClearInventorySlot(slot);
                    continue;
                }

                ushort iconIndex = item.Image;
                ushort countToShow = item.Count > 1 ? item.Count : (ushort)0;

                Libraries.Items.Touch(iconIndex);

                bool needsIconRefresh = force || !slot.HasItem || slot.LastIcon != iconIndex;
                if (!needsIconRefresh)
                {
                    bool textureOk = false;

                    if (slot.Icon != null && !slot.Icon._disposed)
                    {
                        try
                        {
                            NTexture current = slot.Icon.texture;
                            Texture2D native = current?.nativeTexture;
                            textureOk = current != null && native != null && !native.IsDisposed;
                        }
                        catch
                        {
                            textureOk = false;
                        }
                    }
                    else if (slot.IconImage != null && !slot.IconImage._disposed)
                    {
                        try
                        {
                            NTexture current = slot.IconImage.texture;
                            Texture2D native = current?.nativeTexture;
                            textureOk = current != null && native != null && !native.IsDisposed;
                        }
                        catch
                        {
                            textureOk = false;
                        }
                    }

                    if (!textureOk)
                        needsIconRefresh = true;
                }

                if (needsIconRefresh)
                {
                    NTexture texture = GetOrCreateItemIconTexture(iconIndex);

                    if (slot.Icon != null && !slot.Icon._disposed)
                    {
                        slot.Icon.showErrorSign = false;
                        slot.Icon.visible = true;
                        slot.Icon.url = string.Empty;
                        slot.Icon.texture = texture;
                    }
                    else if (slot.IconImage != null && !slot.IconImage._disposed)
                    {
                        slot.IconImage.texture = texture;
                    }

                    slot.LastIcon = iconIndex;
                }

                if (force || !slot.HasItem || slot.LastCountDisplayed != countToShow)
                {
                    if (slot.Count != null && !slot.Count._disposed)
                        slot.Count.text = countToShow > 0 ? countToShow.ToString() : string.Empty;

                    slot.LastCountDisplayed = countToShow;
                }

                slot.HasItem = true;
            }
        }

        private static void TryDumpMobileMainHudBeltBindingsReportIfDue(MobileItemGridBinding binding, int desiredSlots, List<GComponent> slotCandidates)
        {
            if (!Settings.DebugMode || _mobileMainHudBeltBindingsDumped)
                return;

            if (binding == null || binding.Window == null || binding.Window._disposed)
                return;

            try
            {
                Directory.CreateDirectory(ClientResourceLayout.RuntimeRoot);

                string path = Path.Combine(ClientResourceLayout.RuntimeRoot, "FairyGui-MobileMainHudBeltBindings.txt");

                var builder = new StringBuilder(12 * 1024);
                builder.AppendLine("FairyGUI 主界面腰带栏绑定报告（MainHud.Belt）");
                builder.AppendLine($"GeneratedAtUtc={DateTime.UtcNow:o}");
                builder.AppendLine($"Root={DescribeObject(_mobileMainHud, _mobileMainHud)}");
                builder.AppendLine($"GridRoot={DescribeObject(_mobileMainHud, binding.GridRoot)}");
                builder.AppendLine($"GridResolveInfo={binding.GridResolveInfo ?? "-"}");
                builder.AppendLine($"OverrideSpec={binding.OverrideSpec ?? "-"}");
                builder.AppendLine($"OverrideKeywords={(binding.OverrideKeywords == null ? "-" : string.Join("|", binding.OverrideKeywords))}");
                builder.AppendLine($"DesiredSlots={desiredSlots}");
                builder.AppendLine($"SlotCandidates={slotCandidates?.Count ?? 0}");
                builder.AppendLine($"SlotsBound={binding.Slots.Count}");
                builder.AppendLine();
                builder.AppendLine("配置覆盖（可选）：Mir2Config.ini -> [FairyGUI]");
                builder.AppendLine($"  {MobileMainHudBeltGridConfigKey}=<Spec>");
                builder.AppendLine("  Spec 支持：path:... / idx:... / name:... / item:... / url:... / title:... / 或者关键字列表(a|b|c)");
                builder.AppendLine();

                for (int i = 0; i < binding.Slots.Count; i++)
                {
                    MobileItemSlotBinding slot = binding.Slots[i];
                    if (slot == null)
                        continue;

                    builder.AppendLine($"[Slot {i}] InventoryIndex={slot.SlotIndex}");
                    builder.AppendLine($"  Root={DescribeObject(_mobileMainHud, slot.Root)}");
                    builder.AppendLine($"  Icon={DescribeObject(_mobileMainHud, slot.Icon)}");
                    builder.AppendLine($"  Count={DescribeObject(_mobileMainHud, slot.Count)}");
                    builder.AppendLine();
                }

                File.WriteAllText(path, builder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                _mobileMainHudBeltBindingsDumped = true;
                CMain.SaveLog($"FairyGUI: 已导出主界面腰带栏绑定报告到 {path}");
            }
            catch (Exception ex)
            {
                CMain.SaveError("FairyGUI: 导出主界面腰带栏绑定报告失败：" + ex.Message);
            }
        }

        private static void TryBindMobileMainHudSkillHotbarIfDue()
        {
            if (_mobileMainHud == null || _mobileMainHud._disposed)
                return;

            MobileMainHudSkillHotbarBinding binding = _mobileMainHudSkillHotbarBinding;
            if (binding != null && (binding.GridRoot == null || binding.GridRoot._disposed))
            {
                ResetMobileMainHudSkillHotbarBindings();
                binding = null;
            }

            if (binding == null)
            {
                ResetMobileMainHudSkillHotbarBindings();
                binding = new MobileMainHudSkillHotbarBinding();
                _mobileMainHudSkillHotbarBinding = binding;
            }

            if (binding.Slots.Count > 0)
                return;

            if (DateTime.UtcNow < _nextMobileMainHudSkillHotbarBindAttemptUtc)
                return;

            _nextMobileMainHudSkillHotbarBindAttemptUtc = DateTime.UtcNow.AddSeconds(2);

            int desiredSlots = 8;

            string overrideSpec = string.Empty;
            try
            {
                InIReader reader = TryCreateConfigReader();
                if (reader != null)
                {
                    overrideSpec = reader.ReadString(
                        FairyGuiConfigSectionName,
                        MobileMainHudSkillGridConfigKey,
                        string.Empty,
                        writeWhenNull: false);
                }
            }
            catch
            {
                overrideSpec = string.Empty;
            }

            overrideSpec = overrideSpec?.Trim() ?? string.Empty;
            binding.OverrideSpec = overrideSpec;
            binding.OverrideKeywords = null;

            GComponent gridRoot = _mobileMainHud;
            string gridResolveInfo = DescribeObject(_mobileMainHud, _mobileMainHud);

            if (!string.IsNullOrWhiteSpace(overrideSpec))
            {
                if (TryResolveMobileMainHudObjectBySpec(_mobileMainHud, overrideSpec, out GObject resolved, out string[] keywords))
                {
                    if (resolved is GComponent resolvedComponent && !resolvedComponent._disposed)
                    {
                        gridRoot = resolvedComponent;
                        gridResolveInfo = DescribeObject(_mobileMainHud, resolvedComponent) + " (override)";
                    }
                    else if (keywords != null && keywords.Length > 0)
                    {
                        binding.OverrideKeywords = keywords;
                    }
                }
                else
                {
                    binding.OverrideKeywords = SplitKeywords(overrideSpec);
                }
            }

            if (binding.OverrideKeywords != null && binding.OverrideKeywords.Length > 0)
            {
                List<(int Score, GObject Target)> candidates =
                    CollectMobileChatCandidates(_mobileMainHud, obj => obj is GComponent, binding.OverrideKeywords, ScoreMobileMainHudSkillGridCandidate);

                GComponent selected = SelectMobileChatCandidate<GComponent>(candidates, minScore: 40);
                if (selected != null && !selected._disposed)
                {
                    gridRoot = selected;
                    gridResolveInfo = DescribeObject(_mobileMainHud, selected) + " (keywords)";
                }
            }
            else
            {
                List<(int Score, GObject Target)> candidates =
                    CollectMobileChatCandidates(_mobileMainHud, obj => obj is GComponent, DefaultMainHudSkillGridKeywords, ScoreMobileMainHudSkillGridCandidate);

                GComponent selected = SelectMobileChatCandidate<GComponent>(candidates, minScore: 55);
                if (selected != null && !selected._disposed)
                {
                    gridRoot = selected;
                    gridResolveInfo = DescribeObject(_mobileMainHud, selected) + " (auto)";
                }
            }

            binding.GridRoot = gridRoot;
            binding.GridResolveInfo = gridResolveInfo;

            List<GComponent> slotCandidates;
            List<GComponent> selectedSlots;
            bool usingNamedAttackButtons = false;
            if (TryCollectNamedMainHudAttackButtons(out GComponent namedGridRoot, out string namedResolveInfo, out List<GComponent> namedSlots))
            {
                gridRoot = namedGridRoot;
                gridResolveInfo = namedResolveInfo + " (named)";
                binding.GridRoot = gridRoot;
                binding.GridResolveInfo = gridResolveInfo;
                slotCandidates = new List<GComponent>(namedSlots);
                selectedSlots = namedSlots;
                usingNamedAttackButtons = true;
            }
            else
            {
                slotCandidates = CollectMagicSlotCandidates(gridRoot);
                selectedSlots = SelectBottomMostSlots(slotCandidates, desiredSlots);
            }
            if (slotCandidates.Count == 0)
            {
                CMain.SaveError("FairyGUI: 主界面技能快捷栏未找到技能格子（SkillHotbar）。可在 Mir2Config.ini 设置 [" + FairyGuiConfigSectionName + "] " +
                                MobileMainHudSkillGridConfigKey + "=idx:... 指定格子根节点（或 path:/name:/item:/url:/title: / 关键字列表 a|b|c）。");
                return;
            }

            binding.Slots.Clear();
            for (int i = 0; i < selectedSlots.Count; i++)
            {
                int hotKey = i + 1;
                GComponent slotRoot = selectedSlots[i];
                GLoader overlayIcon = usingNamedAttackButtons ? EnsureMobileMainHudAttackButtonIcon(slotRoot) : null;

                var slot = new MobileMagicSlotBinding
                {
                    SlotIndex = i,
                    Root = slotRoot,
                    Icon = overlayIcon ?? FindBestInventorySlotIcon(slotRoot),
                    IconImage = overlayIcon == null ? FindBestInventorySlotIconImage(slotRoot) : null,
                    Name = null,
                    Level = null,
                    HasMagic = false,
                    LastIcon = 0,
                    LastLevel = 0,
                    LastName = null,
                };

                EventCallback0 callback = () =>
                {
                    try
                    {
                        GameScene.Scene?.UseSpell(hotKey, fromUI: true);
                    }
                    catch (Exception ex)
                    {
                        CMain.SaveError("FairyGUI: 主界面快捷技能触发异常：" + ex.Message);
                    }
                };

                try
                {
                    DisableMobileDescendantTouch(slotRoot);
                    EnsureMobileInteractiveChain(slotRoot, _mobileMainHud);
                    slotRoot.onClick.Add(callback);
                    slot.ClickCallback = callback;
                }
                catch
                {
                    slot.ClickCallback = null;
                }

                try
                {
                    DisableMobileDescendantTouch(slotRoot);
                    EnsureMobileInteractiveChain(slotRoot, _mobileMainHud);
                    slot.LongPressDragBinding = BindMobileLongPressMagicDrag(
                        slotRoot,
                        resolvePayload: () =>
                        {
                            try
                            {
                                UserObject user = GameScene.User;
                                List<ClientMagic> magics = user?.Magics;
                                if (magics == null)
                                    return null;

                                for (int j = 0; j < magics.Count; j++)
                                {
                                    ClientMagic candidate = magics[j];
                                    if (candidate != null && candidate.Key == hotKey)
                                    {
                                        return new MobileMagicDragPayload
                                        {
                                            HotKey = hotKey,
                                            Icon = candidate.Icon,
                                            Spell = candidate.Spell,
                                        };
                                    }
                                }
                            }
                            catch
                            {
                            }

                            return null;
                        });
                }
                catch
                {
                    slot.LongPressDragBinding = null;
                }

                try
                {
                    slot.DropCallback = context => OnMobileMainHudSkillHotbarDropped(hotKey, context);
                    slotRoot.AddEventListener("onDrop", slot.DropCallback);
                }
                catch
                {
                    slot.DropCallback = null;
                }

                binding.Slots.Add(slot);
            }

            TryDumpMobileMainHudSkillHotbarBindingsReportIfDue(binding, desiredSlots, slotCandidates);

            CMain.SaveLog($"FairyGUI: 主界面技能快捷栏绑定完成：Slots={binding.Slots.Count} GridRoot={binding.GridResolveInfo}");
        }

        private static int ScoreMobileMainHudSkillGridCandidate(GObject obj, string[] keywords)
        {
            int score = ScoreAnyField(obj, keywords, equalsWeight: 220, startsWithWeight: 130, containsWeight: 60);
            score += ScoreRect(obj, preferLower: true, areaDivisor: 900, maxAreaScore: 120);
            if (obj.packageItem?.exported == true)
                score += 10;
            return score;
        }

        private static void TryRefreshMobileMainHudSkillHotbar(bool force)
        {
            MobileMainHudSkillHotbarBinding binding = _mobileMainHudSkillHotbarBinding;
            if (binding == null || binding.Slots.Count == 0)
                return;

            if (_mobileMainHud == null || _mobileMainHud._disposed || binding.GridRoot == null || binding.GridRoot._disposed)
            {
                ResetMobileMainHudSkillHotbarBindings();
                return;
            }

            UserObject user = GameScene.User;
            if (user == null || user.Magics == null)
                return;

            List<ClientMagic> magics = user.Magics;

            for (int i = 0; i < binding.Slots.Count; i++)
            {
                MobileMagicSlotBinding slot = binding.Slots[i];
                if (slot == null || slot.Root == null || slot.Root._disposed)
                {
                    binding.Slots.Clear();
                    _nextMobileMainHudSkillHotbarBindAttemptUtc = DateTime.MinValue;
                    return;
                }

                int key = i + 1;

                ClientMagic magic = null;
                for (int j = 0; j < magics.Count; j++)
                {
                    ClientMagic candidate = magics[j];
                    if (candidate != null && candidate.Key == key)
                    {
                        magic = candidate;
                        break;
                    }
                }

                if (magic == null)
                {
                    if (slot.HasMagic)
                        ClearMagicSlot(slot);
                    continue;
                }

                byte iconByte = magic.Icon;

                try
                {
                    if (slot.Icon != null && !slot.Icon._disposed)
                        slot.Icon.visible = true;
                    else if (slot.IconImage != null && !slot.IconImage._disposed)
                        slot.IconImage.visible = true;
                }
                catch
                {
                }

                try
                {
                    if (slot.Icon != null && !slot.Icon._disposed &&
                        string.Equals(slot.Icon.name, MobileMainHudAttackButtonIconName, StringComparison.OrdinalIgnoreCase))
                    {
                        float size = Math.Min(slot.Root.width, slot.Root.height) * 0.58f;
                        size = Math.Clamp(size, 18f, 72f);
                        slot.Icon.SetSize(size, size);
                        slot.Icon.SetPosition((slot.Root.width - size) * 0.5f, (slot.Root.height - size) * 0.5f);
                    }
                }
                catch
                {
                }

                int primaryIndex = iconByte * 2;
                Libraries.MagIcon2.Touch(primaryIndex);
                Libraries.MagIcon2.Touch(iconByte);

                bool needsIconRefresh = force || !slot.HasMagic || slot.LastIcon != iconByte;
                if (!needsIconRefresh)
                {
                    bool textureOk = false;

                    if (slot.Icon != null && !slot.Icon._disposed)
                    {
                        try
                        {
                            NTexture current = slot.Icon.texture;
                            Texture2D native = current?.nativeTexture;
                            textureOk = current != null && native != null && !native.IsDisposed;
                        }
                        catch
                        {
                            textureOk = false;
                        }
                    }
                    else if (slot.IconImage != null && !slot.IconImage._disposed)
                    {
                        try
                        {
                            NTexture current = slot.IconImage.texture;
                            Texture2D native = current?.nativeTexture;
                            textureOk = current != null && native != null && !native.IsDisposed;
                        }
                        catch
                        {
                            textureOk = false;
                        }
                    }

                    if (!textureOk)
                        needsIconRefresh = true;
                }

                if (needsIconRefresh)
                {
                    NTexture texture = GetOrCreateMagicIconTexture(iconByte);

                    if (slot.Icon != null && !slot.Icon._disposed)
                    {
                        slot.Icon.showErrorSign = false;
                        slot.Icon.url = string.Empty;
                        slot.Icon.texture = texture;
                    }
                    else if (slot.IconImage != null && !slot.IconImage._disposed)
                    {
                        slot.IconImage.texture = texture;
                    }

                    slot.LastIcon = iconByte;
                }

                slot.HasMagic = true;
            }
        }

        private static void TryDumpMobileMainHudSkillHotbarBindingsReportIfDue(MobileMainHudSkillHotbarBinding binding, int desiredSlots, List<GComponent> slotCandidates)
        {
            if (!Settings.DebugMode || _mobileMainHudSkillHotbarBindingsDumped)
                return;

            if (_mobileMainHud == null || _mobileMainHud._disposed)
                return;

            try
            {
                Directory.CreateDirectory(ClientResourceLayout.RuntimeRoot);

                string path = Path.Combine(ClientResourceLayout.RuntimeRoot, "FairyGui-MobileMainHudSkillHotbarBindings.txt");

                var builder = new StringBuilder(12 * 1024);
                builder.AppendLine("FairyGUI 主界面技能快捷栏绑定报告（MainHud.SkillHotbar）");
                builder.AppendLine($"GeneratedAtUtc={DateTime.UtcNow:o}");
                builder.AppendLine($"Root={DescribeObject(_mobileMainHud, _mobileMainHud)}");
                builder.AppendLine($"GridRoot={DescribeObject(_mobileMainHud, binding.GridRoot)}");
                builder.AppendLine($"GridResolveInfo={binding.GridResolveInfo ?? "-"}");
                builder.AppendLine($"OverrideSpec={binding.OverrideSpec ?? "-"}");
                builder.AppendLine($"OverrideKeywords={(binding.OverrideKeywords == null ? "-" : string.Join("|", binding.OverrideKeywords))}");
                builder.AppendLine($"DesiredSlots={desiredSlots}");
                builder.AppendLine($"SlotCandidates={slotCandidates?.Count ?? 0}");
                builder.AppendLine($"SlotsBound={binding.Slots.Count}");
                builder.AppendLine();
                builder.AppendLine("配置覆盖（可选）：Mir2Config.ini -> [FairyGUI]");
                builder.AppendLine($"  {MobileMainHudSkillGridConfigKey}=<Spec>");
                builder.AppendLine("  Spec 支持：path:... / idx:... / name:... / item:... / url:... / title:... / 或者关键字列表(a|b|c)");
                builder.AppendLine();

                for (int i = 0; i < binding.Slots.Count; i++)
                {
                    MobileMagicSlotBinding slot = binding.Slots[i];
                    if (slot == null)
                        continue;

                    builder.AppendLine($"[Slot {i}] HotKey=F{i + 1} MagicKey={i + 1}");
                    builder.AppendLine($"  Root={DescribeObject(_mobileMainHud, slot.Root)}");
                    builder.AppendLine($"  Icon={DescribeObject(_mobileMainHud, slot.Icon)}");
                    builder.AppendLine();
                }

                File.WriteAllText(path, builder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                _mobileMainHudSkillHotbarBindingsDumped = true;
                CMain.SaveLog($"FairyGUI: 已导出主界面技能快捷栏绑定报告到 {path}");
            }
            catch (Exception ex)
            {
                CMain.SaveError("FairyGUI: 导出主界面技能快捷栏绑定报告失败：" + ex.Message);
            }
        }
    }
}
