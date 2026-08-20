using System;
using System.Collections.Generic;
using System.Drawing;
using FairyGUI;
using Microsoft.Xna.Framework;

namespace MonoShare
{
    internal static partial class FairyGuiHost
    {
        private static bool _mobileMainHudDefaultVisibilityApplied;

        private static GObject _mobileDoubleJoystickRoot;
        private static DateTime _nextMobileDoubleJoystickRootSearchUtc = DateTime.MinValue;

        private static bool _mobileMainHudFunButtonsExpanded;
        private static DateTime _nextMobileMainHudFunButtonsBindAttemptUtc = DateTime.MinValue;
        private static GComponent _mobileMainHudFunButtonsRoot;
        private static GButton _mobileMainHudFunButtonsShowButton;
        private static EventCallback0 _mobileMainHudFunButtonsShowButtonCallback;
        private static DateTime _nextMobileMainHudPersistentHiddenEnforceUtc = DateTime.MinValue;

        private sealed class MobileJoystickVisual
        {
            public string Key;
            public GObject Root;
            public GButton Button;
            public GObject Knob;
            public GObject Thumb;
            public GObject Center;

            public bool Initialized;
            public Vector2 InitButtonPos;
            public Vector2 InitKnobPos;
            public Vector2 InitCenterPos;
            public bool InitKnobVisible;
            public bool InitCenterVisible;
        }

        private static MobileJoystickVisual _mobileWalkJoystickVisual;
        private static MobileJoystickVisual _mobileRunJoystickVisual;
        private static bool _mobileJoystickVisualSearched;
        private static bool _mobileJoystickVisualLogged;
        private static bool _mobileJoystickVisualActive;
        private static bool _mobileJoystickVisualForceRun;
        private static DateTime _nextMobileJoystickVisualResolveAttemptUtc = DateTime.MinValue;
        private static DateTime _nextMobileJoystickVisualResolveLogUtc = DateTime.MinValue;

        // CMain 的摇杆逻辑更新发生在 FairyGuiHost.Update() 之前，Stage.Update 可能会刷新显示对象状态。
        // 为了保证“中间圈/动效”在本帧可见，这里把视觉更新参数缓存起来，等待 Stage.Update 之后再应用一次。
        private static bool _mobileJoystickVisualPending;
        private static bool _mobileJoystickVisualPendingActive;
        private static bool _mobileJoystickVisualPendingForceRun;
        private static Vector2 _mobileJoystickVisualPendingOrigin;
        private static Vector2 _mobileJoystickVisualPendingDelta;
        private static float _mobileJoystickVisualPendingRadius;

        private static readonly HashSet<string> MobileMainHudDefaultVisibleChildren =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "BottomUI",
                "DoubleDJoystick",
                "DMinMapUI",
                "DStateWindow",
                "TaskAndRanksPanel",
                "DFunBtms",
                "DArrackModelUI",
            };

        private static readonly HashSet<string> MobileMainHudDefaultVisibleFunButtons =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                // 需求：DFunBtms 里这些按钮需要始终可见（移动端主屏常驻）
                "DBtnFollow",
                "BottomDealBankBtn",
                "DBtnState",
                "DBtnBag",
                "DBtnShow",
                "DBtnExit",
                "BottomAuPickupBtn",
                "RightUiPromoteBtn",
                "DA2ESimpleButton5",
                "DBtnAuto",
            };

        private static readonly HashSet<string> MobileMainHudDefaultHiddenFunParts =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                // 需求：DFunBtms 里这些零件始终隐藏
                "RigPromoteUI",
                "NoteBagWin",
                "RigPetUI",
            };

        private static readonly string[] MobileMainHudCoreVisualPaths =
        {
            "BottomUI/bg",
            "BottomUI/DHPBall",
            "BottomUI/DMPBall",
            "DoubleDJoystick/joystick_center_1",
            "DoubleDJoystick/joystick_center_2",
            "DoubleDJoystick/joystick_1",
            "DoubleDJoystick/joystick_2",
        };

        private static readonly string[] MobileMainHudPersistentHiddenPaths =
        {
            "BottomPCUI",
            "DBottom",
            "BottomPCUI/DBottom",
            "BottomPCUI/DBottom/DBottom",
            "BottomPCUI/DBottomLeftPanel",
            "BottomPCUI/DBottomLeftPanel/DBottomLeftPanel",
            "BottomPCUI/DBottomRightPanel",
            "BottomPCUI/DBottomRightPanel/DBottomRightPanel",
            "BottomPCUI/DBottomBelts",
            "BottomPCUI/DBottomBelts/DBottomBelts",
            "BottomPCUI/DBottomMemo",
            "BottomPCUI/DBottomMemo/bg",
            "BottomPCUI/DMidleComp",
        };

        private static readonly string[] MobileMainHudPersistentHiddenNames =
        {
            "DBottom",
        };

        private static void ResetMobileMainHudDefaults()
        {
            _mobileMainHudDefaultVisibilityApplied = false;
            _mobileDoubleJoystickRoot = null;
            _nextMobileDoubleJoystickRootSearchUtc = DateTime.MinValue;

            _mobileWalkJoystickVisual = null;
            _mobileRunJoystickVisual = null;
            _mobileJoystickVisualSearched = false;
            _mobileJoystickVisualLogged = false;
            _mobileJoystickVisualActive = false;
            _mobileJoystickVisualForceRun = false;
            _nextMobileJoystickVisualResolveAttemptUtc = DateTime.MinValue;
            _nextMobileJoystickVisualResolveLogUtc = DateTime.MinValue;
            _mobileJoystickVisualPending = false;
            _mobileJoystickVisualPendingActive = false;
            _mobileJoystickVisualPendingForceRun = false;
            _mobileJoystickVisualPendingOrigin = Vector2.Zero;
            _mobileJoystickVisualPendingDelta = Vector2.Zero;
            _mobileJoystickVisualPendingRadius = 0F;

            ResetMobileMainHudFunButtonsToggle();
        }

        private static void ResetMobileMainHudFunButtonsToggle()
        {
            _mobileMainHudFunButtonsExpanded = false;
            _nextMobileMainHudFunButtonsBindAttemptUtc = DateTime.MinValue;
            _mobileMainHudFunButtonsRoot = null;
            _nextMobileMainHudPersistentHiddenEnforceUtc = DateTime.MinValue;

            try
            {
                if (_mobileMainHudFunButtonsShowButton != null &&
                    !_mobileMainHudFunButtonsShowButton._disposed &&
                    _mobileMainHudFunButtonsShowButtonCallback != null)
                {
                    _mobileMainHudFunButtonsShowButton.onClick.Remove(_mobileMainHudFunButtonsShowButtonCallback);
                }
            }
            catch
            {
            }

            _mobileMainHudFunButtonsShowButton = null;
            _mobileMainHudFunButtonsShowButtonCallback = null;
        }

        private static void TryApplyMobileMainHudDefaultVisibilityIfDue()
        {
            if (_mobileMainHud == null || _mobileMainHud._disposed)
                return;

            if (_mobileMainHudDefaultVisibilityApplied)
                return;

            _mobileMainHudDefaultVisibilityApplied = true;

            try
            {
                ApplyMobileMainHudDefaultVisibility();
            }
            catch (Exception ex)
            {
                CMain.SaveError("FairyGUI: 应用主界面默认显隐失败：" + ex.Message);
            }

            try { TryBindMobileMainHudFunButtonsToggleIfDue(); } catch { }
        }

        private static void ApplyMobileMainHudDefaultVisibility()
        {
            if (_mobileMainHud == null || _mobileMainHud._disposed)
                return;

            try
            {
                int count = _mobileMainHud.numChildren;
                for (int i = 0; i < count; i++)
                {
                    GObject child = _mobileMainHud.GetChildAt(i);
                    if (child == null || child._disposed)
                        continue;

                    string name = child.name ?? child.packageItem?.name ?? string.Empty;
                    bool shouldShow = MobileMainHudDefaultVisibleChildren.Contains(name) ||
                                      (child is GComponent component && ContainsAnyNamed(component, MobileMainHudDefaultVisibleChildren));
                    child.visible = shouldShow;

                    if (!shouldShow)
                        child.touchable = false;
                }
            }
            catch
            {
            }

            try
            {
                EnsureMobileMainHudCoreContainersVisible();
                TryEnforceMobileMainHudPersistentHiddenPartsIfDue(force: true);
                TryLogMobileMainHudCoreState("ApplyDefaults");
            }
            catch
            {
            }

            // BottomUI：默认隐藏部分快捷入口（邮件/行会/好友/组队/NPC/自动寻路），避免遮挡主界面。
            try
            {
                if (TryFindChildByNameRecursive(_mobileMainHud, "BottomUI") is GComponent bottomRoot && bottomRoot != null && !bottomRoot._disposed)
                {
                    string[] hidden = { "BottMailWin", "BottGuildWin", "BottFriendWin", "BottGroupWin", "BtnNpc", "AutoWay" };
                    for (int i = 0; i < hidden.Length; i++)
                    {
                        string name = hidden[i];
                        if (string.IsNullOrWhiteSpace(name))
                            continue;

                        GObject obj = null;
                        try { obj = bottomRoot.GetChild(name) ?? TryFindChildByNameRecursive(bottomRoot, name); } catch { obj = null; }
                        if (obj == null || obj._disposed)
                            continue;

                        try { obj.visible = false; } catch { }
                        try { obj.touchable = false; } catch { }
                        try { SetTouchableRecursive(obj, touchable: false); } catch { }
                    }
                }
            }
            catch
            {
            }

            try
            {
                if (TryFindChildByNameRecursive(_mobileMainHud, "DFunBtms") is not GComponent funRoot || funRoot._disposed)
                    return;

                try
                {
                    // 如果发布包里 DFunBtms 默认 touchable=false，会导致其内部按钮全部无法点击。
                    funRoot.visible = true;
                    funRoot.touchable = true;
                    if (funRoot is GButton button)
                    {
                        button.enabled = true;
                        button.grayed = false;
                        button.changeStateOnClick = false;
                    }
                }
                catch
                {
                }

                int count = funRoot.numChildren;
                for (int i = 0; i < count; i++)
                {
                    GObject child = funRoot.GetChildAt(i);
                    if (child == null || child._disposed)
                        continue;

                    string name = child.name ?? child.packageItem?.name ?? string.Empty;

                    bool shouldHide = MobileMainHudDefaultHiddenFunParts.Contains(name) ||
                                      (child is GComponent hiddenComponent && ContainsAnyNamed(hiddenComponent, MobileMainHudDefaultHiddenFunParts));

                    bool isBtnsWindow = string.Equals(name, "DBtnsWindow", StringComparison.OrdinalIgnoreCase);

                    bool shouldShow = !shouldHide &&
                                      (MobileMainHudDefaultVisibleFunButtons.Contains(name) ||
                                       (child is GComponent component && ContainsAnyNamed(component, MobileMainHudDefaultVisibleFunButtons)));

                    // 需求：DBtnsWindow 默认收起，交由 DBtnShow 控制展开/收起
                    if (isBtnsWindow)
                        shouldShow = false;

                    child.visible = shouldShow;

                    // publish 里这些按钮可能默认 touchable=false / grayed=true，导致点击无效；这里强制启用。
                    if (shouldShow)
                    {
                        SetTouchableRecursive(child, touchable: true);
                    }
                    else
                    {
                        SetTouchableRecursive(child, touchable: false);
                    }
                }

                // 兜底：部分发布资源里按钮被包在容器内且自身 visible=false，这里按名称再强制点亮一次（尤其是拾取按钮）。
                try
                {
                    foreach (string forceName in MobileMainHudDefaultVisibleFunButtons)
                    {
                        if (TryFindChildByNameRecursive(funRoot, forceName) is not GObject obj || obj == null || obj._disposed)
                            continue;

                        obj.visible = true;
                        SetTouchableRecursive(obj, touchable: true);
                        if (obj is GButton b)
                        {
                            b.enabled = true;
                            b.grayed = false;
                            b.changeStateOnClick = false;
                        }
                    }
                }
                catch
                {
                }

                // 强制隐藏指定零件（避免被布局脚本/默认状态重新点亮）
                try
                {
                    foreach (string hideName in MobileMainHudDefaultHiddenFunParts)
                    {
                        if (TryFindChildByNameRecursive(funRoot, hideName) is not GObject obj || obj == null || obj._disposed)
                            continue;

                        obj.visible = false;
                        SetTouchableRecursive(obj, touchable: false);
                    }
                }
                catch
                {
                }

                // 默认收起 DBtnsWindow
                try
                {
                    if (TryFindChildByNameRecursive(funRoot, "DBtnsWindow") is GObject btnsWindow && btnsWindow != null && !btnsWindow._disposed)
                    {
                        btnsWindow.visible = false;
                        SetTouchableRecursive(btnsWindow, touchable: false);
                    }
                }
                catch
                {
                }

                // 兜底：把功能按钮整体约束在安全区域内，避免 DBtnAuto 等按钮被屏幕边界裁掉只显示一半
                try { TryClampMobileMainHudFunButtonsIntoSafeArea(funRoot); } catch { }
            }
            catch
            {
            }
        }

        private static void TryBindMobileMainHudFunButtonsToggleIfDue()
        {
            if (_mobileMainHud == null || _mobileMainHud._disposed)
                return;

            if (DateTime.UtcNow < _nextMobileMainHudFunButtonsBindAttemptUtc)
                return;

            if (_mobileMainHudFunButtonsRoot != null && !_mobileMainHudFunButtonsRoot._disposed &&
                _mobileMainHudFunButtonsShowButton != null && !_mobileMainHudFunButtonsShowButton._disposed &&
                _mobileMainHudFunButtonsShowButtonCallback != null)
            {
                try { TryClampMobileMainHudFunButtonsIntoSafeArea(_mobileMainHudFunButtonsRoot); } catch { }
                return;
            }

            _nextMobileMainHudFunButtonsBindAttemptUtc = DateTime.UtcNow.AddSeconds(2);

            GComponent funRoot = null;
            try { funRoot = TryFindChildByNameRecursive(_mobileMainHud, "DFunBtms") as GComponent; } catch { funRoot = null; }
            if (funRoot == null || funRoot._disposed)
                return;

            GButton showButton = null;
            try { showButton = funRoot.GetChild("DBtnShow") as GButton; } catch { showButton = null; }
            if (showButton == null || showButton._disposed)
            {
                try { showButton = TryFindChildByNameRecursive(funRoot, "DBtnShow") as GButton; } catch { showButton = null; }
            }

            if (showButton == null || showButton._disposed)
            {
                // 兜底：按名字/资源名包含 show 关键字匹配
                try
                {
                    foreach (GObject obj in Enumerate(funRoot))
                    {
                        if (obj is not GButton b || b == null || b._disposed)
                            continue;

                        string n = b.name ?? string.Empty;
                        string item = b.packageItem?.name ?? string.Empty;
                        if (n.IndexOf("show", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            item.IndexOf("show", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            showButton = b;
                            break;
                        }
                    }
                }
                catch
                {
                    showButton = null;
                }
            }

            if (showButton == null || showButton._disposed)
                return;

            // 如果按钮对象发生变化，先解绑旧回调
            if (_mobileMainHudFunButtonsShowButton != null &&
                !_mobileMainHudFunButtonsShowButton._disposed &&
                !ReferenceEquals(_mobileMainHudFunButtonsShowButton, showButton) &&
                _mobileMainHudFunButtonsShowButtonCallback != null)
            {
                try { _mobileMainHudFunButtonsShowButton.onClick.Remove(_mobileMainHudFunButtonsShowButtonCallback); } catch { }
            }

            _mobileMainHudFunButtonsRoot = funRoot;
            _mobileMainHudFunButtonsShowButton = showButton;

            try
            {
                showButton.touchable = true;
                showButton.enabled = true;
                showButton.grayed = false;
                showButton.changeStateOnClick = false;
                showButton.mode = ButtonMode.Check;
            }
            catch
            {
            }

            _mobileMainHudFunButtonsShowButtonCallback ??= OnMobileMainHudFunButtonsShowButtonClicked;
            try { showButton.onClick.Add(_mobileMainHudFunButtonsShowButtonCallback); } catch { }

            // 同步一次展开状态与图标
            ApplyMobileMainHudFunButtonsExpandedState(_mobileMainHudFunButtonsExpanded);
        }

        private static void OnMobileMainHudFunButtonsShowButtonClicked()
        {
            _mobileMainHudFunButtonsExpanded = !_mobileMainHudFunButtonsExpanded;
            ApplyMobileMainHudFunButtonsExpandedState(_mobileMainHudFunButtonsExpanded);
        }

        private static void ApplyMobileMainHudFunButtonsExpandedState(bool expanded)
        {
            if (_mobileMainHud == null || _mobileMainHud._disposed)
                return;

            GComponent funRoot = _mobileMainHudFunButtonsRoot;
            if (funRoot == null || funRoot._disposed)
            {
                try { funRoot = TryFindChildByNameRecursive(_mobileMainHud, "DFunBtms") as GComponent; } catch { funRoot = null; }
                if (funRoot == null || funRoot._disposed)
                    return;
                _mobileMainHudFunButtonsRoot = funRoot;
            }

            // 切换展开按钮图标（选中态）
            try
            {
                if (_mobileMainHudFunButtonsShowButton != null && !_mobileMainHudFunButtonsShowButton._disposed)
                    _mobileMainHudFunButtonsShowButton.selected = expanded;
            }
            catch
            {
            }

            // 需求：DFunBtms 保持指定零件隐藏/指定按钮常显，DBtnShow 仅控制 DBtnsWindow 与 DArrackModelUI 的显隐联动。
            try
            {
                // 先隐藏指定零件
                foreach (string hideName in MobileMainHudDefaultHiddenFunParts)
                {
                    if (TryFindChildByNameRecursive(funRoot, hideName) is not GObject obj || obj == null || obj._disposed)
                        continue;

                    obj.visible = false;
                    SetTouchableRecursive(obj, touchable: false);
                }
            }
            catch
            {
            }

            // 常显按钮
            try
            {
                foreach (string forceName in MobileMainHudDefaultVisibleFunButtons)
                {
                    if (TryFindChildByNameRecursive(funRoot, forceName) is not GObject obj || obj == null || obj._disposed)
                        continue;

                    obj.visible = true;
                    SetTouchableRecursive(obj, touchable: true);
                    EnsureMobileInteractiveChain(obj, funRoot);

                    if (obj is GButton button)
                    {
                        button.enabled = true;
                        button.grayed = false;
                        button.changeStateOnClick = false;
                    }
                }
            }
            catch
            {
            }

            // 展开/收起 DBtnsWindow（更多功能按钮容器）
            try
            {
                if (TryFindChildByNameRecursive(funRoot, "DBtnsWindow") is GObject btnsWindow && btnsWindow != null && !btnsWindow._disposed)
                {
                    btnsWindow.visible = expanded;
                    SetTouchableRecursive(btnsWindow, touchable: expanded);
                    if (expanded)
                        EnsureMobileInteractiveChain(btnsWindow, funRoot);

                    // 避免 DBtnsWindow 覆盖 DBtnAuto 等常显按钮（部分资源包里 DBtnsWindow 背景区域会遮挡同层按钮）
                    try
                    {
                        if (btnsWindow.parent is GComponent parent && parent != null && !parent._disposed)
                        {
                            parent.SetChildIndex(btnsWindow, 0);
                        }
                    }
                    catch
                    {
                    }

                }
            }
            catch
            {
            }

            // 常显按钮置顶，防止被其它容器遮挡（例如 DBtnAuto 被遮住）
            try
            {
                foreach (string forceName in MobileMainHudDefaultVisibleFunButtons)
                {
                    if (TryFindChildByNameRecursive(funRoot, forceName) is not GObject obj || obj == null || obj._disposed)
                        continue;

                    try
                    {
                        if (obj.parent is GComponent p && p != null && !p._disposed)
                            p.SetChildIndex(obj, p.numChildren - 1);
                    }
                    catch
                    {
                    }

                    try { TryElevateMobileMainHudObjectChain(obj, _mobileMainHudSafeAreaRoot ?? _mobileMainHud); } catch { }
                }
            }
            catch
            {
            }

            // 展开时隐藏右下角快捷施法栏（DArrackModelUI），收起时恢复
            try
            {
                if (TryFindChildByNameRecursive(_mobileMainHud, "DArrackModelUI") is GObject attackRoot && attackRoot != null && !attackRoot._disposed)
                {
                    attackRoot.visible = !expanded;
                    attackRoot.touchable = !expanded;
                }
            }
            catch
            {
            }

            // 提升 DFunBtms 本体层级，避免被 BottomUI 等兄弟节点遮挡（修复 DBtnAuto 仍被遮住的问题）
            try
            {
                if (funRoot.parent is GComponent funParent && funParent != null && !funParent._disposed)
                {
                    funParent.SetChildIndex(funRoot, funParent.numChildren - 1);
                }
            }
            catch
            {
            }

            // 切换展开/收起后再次做一次安全区域约束，避免按钮被裁切
            try { TryClampMobileMainHudFunButtonsIntoSafeArea(funRoot); } catch { }
        }

        private static void TryElevateMobileMainHudObjectChain(GObject obj, GObject stopParent, int baseSortingOrder = 10000, int maxDepth = 16)
        {
            if (obj == null || obj._disposed)
                return;

            GObject current = obj;
            int depth = 0;

            while (current != null && !current._disposed && depth++ < maxDepth)
            {
                try
                {
                    if (current.parent is GComponent parent && parent != null && !parent._disposed)
                        parent.SetChildIndex(current, parent.numChildren - 1);
                }
                catch
                {
                }

                try
                {
                    int desiredSortingOrder = Math.Max(1, baseSortingOrder - depth);
                    if (current.sortingOrder < desiredSortingOrder)
                        current.sortingOrder = desiredSortingOrder;
                }
                catch
                {
                }

                if (ReferenceEquals(current, stopParent))
                    break;

                current = current.parent;
            }
        }

        private static GObject ResolveMobileMainHudClampAnchor(GComponent funRoot)
        {
            if (funRoot == null || funRoot._disposed)
                return null;

            static bool IsNamed(GObject obj, string expected)
            {
                if (obj == null || obj._disposed || string.IsNullOrWhiteSpace(expected))
                    return false;

                string name = obj.name ?? string.Empty;
                string item = obj.packageItem?.name ?? string.Empty;
                return string.Equals(name, expected, StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(item, expected, StringComparison.OrdinalIgnoreCase);
            }

            static bool IsDescendantOf(GObject child, GObject ancestor)
            {
                if (child == null || ancestor == null)
                    return false;

                GObject current = child;
                int guard = 0;
                while (current != null && guard++ < 16)
                {
                    if (ReferenceEquals(current, ancestor))
                        return true;

                    current = current.parent;
                }

                return false;
            }

            static int GetPreferredRank(GObject obj)
            {
                if (IsNamed(obj, "DBtnAuto"))
                    return 0;
                if (IsNamed(obj, "BottomAuPickupBtn"))
                    return 1;
                if (IsNamed(obj, "DBtnShow"))
                    return 2;
                if (IsNamed(obj, "DBtnBag"))
                    return 3;
                if (IsNamed(obj, "DBtnExit"))
                    return 4;

                return int.MaxValue;
            }

            GObject best = null;
            int bestScore = int.MinValue;

            void Consider(GComponent searchRoot, int baseScore)
            {
                if (searchRoot == null || searchRoot._disposed)
                    return;

                foreach (GObject obj in Enumerate(searchRoot))
                {
                    if (obj == null || obj._disposed)
                        continue;

                    int preferredRank = GetPreferredRank(obj);
                    if (preferredRank == int.MaxValue)
                        continue;

                    int score = baseScore - preferredRank * 120;

                    try
                    {
                        if (obj.visible && obj.alpha > 0.01F && obj.width > 8F && obj.height > 8F)
                            score += 180;
                        else
                            score -= 300;
                    }
                    catch
                    {
                        score -= 300;
                    }

                    if (obj is GButton)
                        score += 40;

                    if (IsDescendantOf(obj, funRoot))
                        score += 90;

                    try
                    {
                        RectangleF rect = obj.LocalToGlobal(new RectangleF(0, 0, obj.width, obj.height));
                        score += (int)Math.Round(rect.Right * 0.8F + rect.Bottom * 0.3F);
                    }
                    catch
                    {
                    }

                    if (best == null || best._disposed || score > bestScore)
                    {
                        best = obj;
                        bestScore = score;
                    }
                }
            }

            try { Consider(funRoot, 2400); } catch { }
            try
            {
                if ((best == null || best._disposed) && _mobileMainHud != null && !_mobileMainHud._disposed)
                    Consider(_mobileMainHud, 1600);
            }
            catch
            {
            }

            if (best != null && !best._disposed)
                return best;

            try
            {
                GObject fallback = null;
                float bestMetric = float.MinValue;

                foreach (GObject obj in Enumerate(funRoot))
                {
                    if (obj == null || obj._disposed || obj is not GButton)
                        continue;

                    try
                    {
                        if (!obj.visible || obj.alpha <= 0.01F || obj.width <= 8F || obj.height <= 8F)
                            continue;

                        RectangleF rect = obj.LocalToGlobal(new RectangleF(0, 0, obj.width, obj.height));
                        float metric = rect.Right + rect.Bottom * 0.5F;
                        if (fallback == null || metric > bestMetric)
                        {
                            fallback = obj;
                            bestMetric = metric;
                        }
                    }
                    catch
                    {
                    }
                }

                return fallback;
            }
            catch
            {
                return null;
            }
        }

        private static GObject ResolveMobileMainHudClampMoveTarget(GComponent funRoot, GObject anchor)
        {
            if (funRoot == null || funRoot._disposed)
                return null;

            if (anchor == null || anchor._disposed)
                return funRoot;

            GObject current = anchor;
            int guard = 0;
            while (current != null && guard++ < 16)
            {
                if (ReferenceEquals(current, funRoot))
                    return funRoot;

                current = current.parent;
            }

            if (anchor.parent != null && !anchor.parent._disposed)
                return anchor.parent;

            return funRoot;
        }

        private static void TryClampMobileMainHudFunButtonsIntoSafeArea(GComponent funRoot)
        {
            if (funRoot == null || funRoot._disposed)
                return;

            GObject anchor = ResolveMobileMainHudClampAnchor(funRoot);
            if (anchor == null || anchor._disposed)
                return;

            GObject moveTarget = ResolveMobileMainHudClampMoveTarget(funRoot, anchor);
            if (moveTarget == null || moveTarget._disposed)
                return;

            try
            {
                RectangleF rect = anchor.LocalToGlobal(new RectangleF(0, 0, anchor.width, anchor.height));

                var safeArea = Settings.GetMobileSafeAreaBounds();
                float scale = UIContentScaler.scaleFactor;
                if (scale <= 0.01F || float.IsNaN(scale) || float.IsInfinity(scale))
                    scale = 1F;

                float safeX = safeArea.Left / scale;
                float safeY = safeArea.Top / scale;
                float safeR = safeArea.Right / scale;
                float safeB = safeArea.Bottom / scale;

                const float margin = 6f;
                float moveX = 0f;
                float moveY = 0f;

                if (rect.Right > safeR - margin)
                    moveX = (safeR - margin) - rect.Right;
                else if (rect.Left < safeX + margin)
                    moveX = (safeX + margin) - rect.Left;

                if (rect.Bottom > safeB - margin)
                    moveY = (safeB - margin) - rect.Bottom;
                else if (rect.Top < safeY + margin)
                    moveY = (safeY + margin) - rect.Top;

                if (Math.Abs(moveX) < 0.1f && Math.Abs(moveY) < 0.1f)
                    return;

                // 将“全局位移”换算到实际移动目标父容器的本地坐标后再设置位置，避免缩放导致位移不足。
                Vector2 moveTargetGlobal = moveTarget.LocalToGlobal(Vector2.Zero);
                Vector2 targetGlobal = new Vector2(moveTargetGlobal.X + moveX, moveTargetGlobal.Y + moveY);
                Vector2 targetLocal = moveTarget.parent != null
                    ? moveTarget.parent.GlobalToLocal(targetGlobal)
                    : new Vector2(moveTarget.x + moveX, moveTarget.y + moveY);
                moveTarget.SetPosition(targetLocal.X, targetLocal.Y);

                if (Settings.LogErrors)
                    CMain.SaveLog(
                        $"FairyGUI: Clamp(DFunBtms) anchor={DescribeObject(_mobileMainHud ?? funRoot, anchor)} " +
                        $"moveTarget={DescribeObject(_mobileMainHud ?? funRoot, moveTarget)} " +
                        $"move=({moveX:0.##},{moveY:0.##}) rect={rect.X:0.##},{rect.Y:0.##},{rect.Width:0.##},{rect.Height:0.##} " +
                        $"safe={safeX:0.##},{safeY:0.##},{(safeR - safeX):0.##},{(safeB - safeY):0.##}");
            }
            catch
            {
            }
        }

        private static void SetTouchableRecursive(GObject obj, bool touchable)
        {
            if (obj == null || obj._disposed)
                return;

            try
            {
                obj.touchable = touchable;

                if (obj is GButton button)
                {
                    if (touchable)
                    {
                        button.enabled = true;
                        button.grayed = false;
                        button.changeStateOnClick = false;
                    }
                }
            }
            catch
            {
            }

            if (obj is not GComponent component || component._disposed)
                return;

            try
            {
                int count = component.numChildren;
                for (int i = 0; i < count; i++)
                {
                    GObject child = component.GetChildAt(i);
                    if (child == null || child._disposed)
                        continue;

                    SetTouchableRecursive(child, touchable);
                }
            }
            catch
            {
            }
        }

        private static void EnsureMobileMainHudCoreContainersVisible()
        {
            if (_mobileMainHud == null || _mobileMainHud._disposed)
                return;

            string[] required =
            {
                "DMinMapUI",
                "DStateWindow",
                "TaskAndRanksPanel",
                "DFunBtms",
                "BottomUI",
                "DArrackModelUI",
                "DoubleDJoystick",
            };

            for (int i = 0; i < required.Length; i++)
                EnsureMobileMainHudContainerVisible(required[i]);

            EnsureMobileMainHudCoreVisualsVisible();
        }

        internal static void TryEnforceMobileMainHudPersistentHiddenPartsIfDue(bool force)
        {
            if (_mobileMainHud == null || _mobileMainHud._disposed)
                return;

            DateTime now = DateTime.UtcNow;
            if (!force && now < _nextMobileMainHudPersistentHiddenEnforceUtc)
                return;

            _nextMobileMainHudPersistentHiddenEnforceUtc = now.AddMilliseconds(250);

            for (int i = 0; i < MobileMainHudPersistentHiddenPaths.Length; i++)
            {
                string path = MobileMainHudPersistentHiddenPaths[i];
                if (string.IsNullOrWhiteSpace(path))
                    continue;

                GObject target = null;
                try { target = FindByPath(_mobileMainHud, path); } catch { target = null; }
                if (target == null || target._disposed)
                    continue;

                try { target.visible = false; } catch { }
                try { target.touchable = false; } catch { }
                try { SetTouchableRecursive(target, touchable: false); } catch { }
            }

            for (int i = 0; i < MobileMainHudPersistentHiddenNames.Length; i++)
            {
                string name = MobileMainHudPersistentHiddenNames[i];
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                GObject target = null;
                try { target = TryFindChildByNameRecursive(_mobileMainHud, name); } catch { target = null; }
                if (target == null || target._disposed)
                    continue;

                try { target.visible = false; } catch { }
                try { target.touchable = false; } catch { }
                try { SetTouchableRecursive(target, touchable: false); } catch { }
            }
        }

        private static void EnsureMobileMainHudContainerVisible(string targetName)
        {
            if (_mobileMainHud == null || _mobileMainHud._disposed || string.IsNullOrWhiteSpace(targetName))
                return;

            GObject target = null;
            try { target = TryFindChildByNameRecursive(_mobileMainHud, targetName); } catch { target = null; }
            if (target == null || target._disposed)
                return;

            EnsureMobileVisibleChain(target, _mobileMainHud);
        }

        private static void EnsureMobileMainHudCoreVisualsVisible()
        {
            if (_mobileMainHud == null || _mobileMainHud._disposed)
                return;

            for (int i = 0; i < MobileMainHudCoreVisualPaths.Length; i++)
                EnsureMobileMainHudVisualVisible(MobileMainHudCoreVisualPaths[i]);
        }

        private static void EnsureMobileMainHudVisualVisible(string path)
        {
            if (_mobileMainHud == null || _mobileMainHud._disposed || string.IsNullOrWhiteSpace(path))
                return;

            GObject target = null;
            try { target = FindByPath(_mobileMainHud, path); } catch { target = null; }
            if (target == null || target._disposed)
                return;

            EnsureMobileVisibleChain(target, _mobileMainHud);
        }

        private static void EnsureMobileVisibleChain(GObject obj, GObject stopParent = null, int maxDepth = 16)
        {
            if (obj == null || obj._disposed)
                return;

            try { SetTouchableRecursive(obj, touchable: true); } catch { }

            GObject current = obj;
            int guard = 0;
            while (current != null && !current._disposed && guard++ < maxDepth)
            {
                try { current.visible = true; } catch { }
                try { current.touchable = true; } catch { }

                try
                {
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

        private static void TryLogMobileMainHudCoreState(string stage)
        {
            if (!Settings.LogErrors)
                return;

            if (_mobileMainHud == null || _mobileMainHud._disposed)
                return;

            string[] names =
            {
                "DMinMapUI",
                "DStateWindow",
                "TaskAndRanksPanel",
                "DFunBtms",
                "BottomUI",
                "BottomPCUI",
                "DArrackModelUI",
                "DoubleDJoystick",
            };

            var lines = new List<string>(names.Length + MobileMainHudCoreVisualPaths.Length);
            for (int i = 0; i < names.Length; i++)
            {
                string name = names[i];
                GObject obj = null;
                try { obj = TryFindChildByNameRecursive(_mobileMainHud, name); } catch { obj = null; }

                if (obj == null || obj._disposed)
                {
                    lines.Add(name + "=missing");
                    continue;
                }

                lines.Add(name + "=" + DescribeObject(_mobileMainHud, obj) + " chain=" + DescribeMobileVisibilityChain(obj, _mobileMainHud));
            }
            for (int i = 0; i < MobileMainHudCoreVisualPaths.Length; i++)
            {
                string path = MobileMainHudCoreVisualPaths[i];
                GObject obj = null;
                try { obj = FindByPath(_mobileMainHud, path); } catch { obj = null; }

                if (obj == null || obj._disposed)
                {
                    lines.Add(path + "=missing");
                    continue;
                }

                lines.Add(path + "=" + DescribeObject(_mobileMainHud, obj) + " chain=" + DescribeMobileVisibilityChain(obj, _mobileMainHud));
            }

            CMain.SaveLog("FairyGUI: 主界面核心节点状态(" + stage + ")\n  " + string.Join("\n  ", lines));
        }

        private static string DescribeMobileVisibilityChain(GObject obj, GObject stopParent = null, int maxDepth = 16)
        {
            if (obj == null)
                return "(null)";

            var parts = new List<string>(8);
            GObject current = obj;
            int guard = 0;

            while (current != null && !current._disposed && guard++ < maxDepth)
            {
                string name = current.name ?? current.packageItem?.name ?? current.GetType().Name ?? "(null)";
                string visible = "?";
                string alpha = "?";

                try { visible = current.visible ? "1" : "0"; } catch { }
                try { alpha = current.alpha.ToString("0.##"); } catch { }

                parts.Add(name + "(v=" + visible + ",a=" + alpha + ")");

                if (ReferenceEquals(current, stopParent))
                    break;

                current = current.parent;
            }

            return string.Join(" <- ", parts);
        }

        private static bool ContainsAnyNamed(GComponent root, HashSet<string> allowList)
        {
            if (root == null || root._disposed || allowList == null || allowList.Count == 0)
                return false;

            int count = root.numChildren;
            for (int i = 0; i < count; i++)
            {
                GObject child = root.GetChildAt(i);
                if (child == null || child._disposed)
                    continue;

                string name = child.name ?? child.packageItem?.name ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(name) && allowList.Contains(name))
                    return true;

                if (child is GComponent component && ContainsAnyNamed(component, allowList))
                    return true;
            }

            return false;
        }

        public static bool TryResolveMobileDoubleJoystickActivation(Vector2 position, out bool forceRun, out Vector2 origin, out float radius)
        {
            forceRun = false;
            origin = Vector2.Zero;
            radius = 0F;

            GObject joystickRoot = TryGetMobileDoubleJoystickRoot();
            if (joystickRoot == null || joystickRoot._disposed || !joystickRoot.visible)
                return false;

            try
            {
                static bool TryResolveByVisual(MobileJoystickVisual view, Vector2 pos, out Vector2 resolvedOrigin, out float resolvedRadius)
                {
                    resolvedOrigin = Vector2.Zero;
                    resolvedRadius = 0F;

                    if (view == null || view.Root == null || view.Root._disposed || !view.Root.visible)
                        return false;

                    RectangleF touchRect = view.Root.LocalToGlobal(new RectangleF(0, 0, view.Root.width, view.Root.height));
                    if (touchRect.Width <= 1F || touchRect.Height <= 1F)
                        return false;

                    // 优先使用 center（外圈背景圆）的中心作为摇杆原点/半径来源，避免 touch 区域过大导致：
                    // - 小摇杆按下却命中右侧大摇杆（root touch 覆盖过大）
                    // - 中间圈可滑出外圈（半径取自 touchRect 而不是外圈）
                    RectangleF centerRect = default;
                    bool hasCenterRect = false;
                    try
                    {
                        if (view.Center != null && !view.Center._disposed)
                        {
                            centerRect = view.Center.LocalToGlobal(new RectangleF(0, 0, view.Center.width, view.Center.height));
                            if (centerRect.Width > 1F && centerRect.Height > 1F)
                            {
                                hasCenterRect = true;
                                resolvedOrigin = new Vector2(centerRect.X + centerRect.Width * 0.5F, centerRect.Y + centerRect.Height * 0.5F);
                            }
                        }
                    }
                    catch
                    {
                        hasCenterRect = false;
                    }

                    if (!hasCenterRect)
                        resolvedOrigin = new Vector2(touchRect.X + touchRect.Width * 0.5F, touchRect.Y + touchRect.Height * 0.5F);

                    RectangleF hitRect = hasCenterRect ? centerRect : touchRect;
                    float x = pos.X;
                    float y = pos.Y;
                    if (x < hitRect.X || x > hitRect.Right || y < hitRect.Y || y > hitRect.Bottom)
                        return false;

                    float diameter = hasCenterRect ? Math.Min(centerRect.Width, centerRect.Height) : Math.Min(touchRect.Width, touchRect.Height);
                    float outerRadius = diameter * 0.5F;

                    float knobDiameter = 0F;
                    try
                    {
                        GObject knobObj = view.Knob != null && !view.Knob._disposed ? view.Knob : (view.Button != null && !view.Button._disposed ? view.Button : null);
                        if (knobObj != null)
                            knobDiameter = Math.Max(0F, Math.Min(knobObj.width, knobObj.height));
                    }
                    catch
                    {
                        knobDiameter = 0F;
                    }

                    float knobRadius = knobDiameter * 0.5F;
                    float maxDelta = outerRadius - knobRadius;
                    // 留一点边距，避免贴边时看起来“出圈/抖动”
                    maxDelta *= 0.95F;

                    resolvedRadius = Math.Max(16F, maxDelta);
                    return true;
                }

                try
                {
                    if (TryEnsureMobileDoubleJoystickVisualsResolved())
                    {
                        bool hitRun = TryResolveByVisual(_mobileRunJoystickVisual, position, out Vector2 runOrigin, out float runRadius);
                        bool hitWalk = TryResolveByVisual(_mobileWalkJoystickVisual, position, out Vector2 walkOrigin, out float walkRadius);

                        if (hitRun || hitWalk)
                        {
                            if (hitRun && !hitWalk)
                            {
                                forceRun = true;
                                origin = runOrigin;
                                radius = runRadius;
                                return true;
                            }

                            if (hitWalk && !hitRun)
                            {
                                forceRun = false;
                                origin = walkOrigin;
                                radius = walkRadius;
                                return true;
                            }

                            // 两个 touch 区域有重叠时，按“更接近哪个摇杆原点”来决定，避免小摇杆误触发到大摇杆。
                            float distRun = Vector2.DistanceSquared(position, runOrigin);
                            float distWalk = Vector2.DistanceSquared(position, walkOrigin);
                            bool chooseRun = distRun < distWalk;

                            forceRun = chooseRun;
                            origin = chooseRun ? runOrigin : walkOrigin;
                            radius = chooseRun ? runRadius : walkRadius;
                            return true;
                        }
                    }
                }
                catch
                {
                }

                RectangleF rect = joystickRoot.LocalToGlobal(new RectangleF(0, 0, joystickRoot.width, joystickRoot.height));
                if (rect.Width <= 1F || rect.Height <= 1F)
                    return false;

                float x = position.X;
                float y = position.Y;

                if (x < rect.X || x > rect.Right || y < rect.Y || y > rect.Bottom)
                    return false;

                float halfW = rect.Width * 0.5F;
                forceRun = x >= rect.X + halfW;

                float centerX = rect.X + (forceRun ? halfW * 1.5F : halfW * 0.5F);
                float centerY = rect.Y + rect.Height * 0.5F;
                origin = new Vector2(centerX, centerY);

                float padMin = Math.Min(halfW, rect.Height);
                radius = Math.Max(16F, padMin * 0.35F);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static void TryUpdateMobileDoubleJoystickVisual(bool active, bool forceRun, Vector2 joystickOrigin, Vector2 joystickDelta, float joystickRadius)
        {
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                return;

            // 缓存到本帧末尾（Stage.Update 后）再应用，避免被 UI 更新覆盖导致“中间圈不跟手”。
            _mobileJoystickVisualPending = true;
            _mobileJoystickVisualPendingActive = active;
            _mobileJoystickVisualPendingForceRun = forceRun;
            _mobileJoystickVisualPendingOrigin = joystickOrigin;
            _mobileJoystickVisualPendingDelta = joystickDelta;
            _mobileJoystickVisualPendingRadius = joystickRadius;
        }

        private static void TryApplyPendingMobileDoubleJoystickVisualIfDue()
        {
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                return;

            if (!_mobileJoystickVisualPending)
                return;

            if (_stage == null || !_initialized || !_packagesLoaded)
                return;

            bool active = _mobileJoystickVisualPendingActive;
            bool forceRun = _mobileJoystickVisualPendingForceRun;
            Vector2 origin = _mobileJoystickVisualPendingOrigin;
            Vector2 delta = _mobileJoystickVisualPendingDelta;
            float radius = _mobileJoystickVisualPendingRadius;

            _mobileJoystickVisualPending = false;

            ApplyMobileDoubleJoystickVisual(active, forceRun, origin, delta, radius);
        }

        private static void ApplyMobileDoubleJoystickVisual(bool active, bool forceRun, Vector2 joystickOrigin, Vector2 joystickDelta, float joystickRadius)
        {
            if (!TryEnsureMobileDoubleJoystickVisualsResolved())
                return;

            float scale = 1F;
            try
            {
                scale = UIContentScaler.scaleFactor;
                if (scale <= 0.01F)
                    scale = 1F;
            }
            catch
            {
                scale = 1F;
            }

            // CMain 触点坐标一般是“屏幕像素”。FairyGUI 的 Global 坐标通常是“像素/scaleFactor”的 UI 坐标。
            Vector2 originUi = joystickOrigin / scale;
            Vector2 deltaUi = joystickDelta / scale;
            float radiusUi = joystickRadius / scale;

            // 避免误差导致 NaN
            if (float.IsNaN(originUi.X) || float.IsNaN(originUi.Y) || float.IsNaN(deltaUi.X) || float.IsNaN(deltaUi.Y))
                return;

            if (!active || radiusUi <= 1F)
            {
                if (_mobileJoystickVisualActive)
                {
                    ResetJoystickVisual(_mobileWalkJoystickVisual);
                    ResetJoystickVisual(_mobileRunJoystickVisual);
                    _mobileJoystickVisualActive = false;
                    _mobileJoystickVisualForceRun = false;
                }
                return;
            }

            MobileJoystickVisual selected = forceRun ? _mobileRunJoystickVisual : _mobileWalkJoystickVisual;
            MobileJoystickVisual other = forceRun ? _mobileWalkJoystickVisual : _mobileRunJoystickVisual;

            if (selected == null || selected.Root == null || selected.Root._disposed)
                return;

            if (!_mobileJoystickVisualActive || _mobileJoystickVisualForceRun != forceRun)
            {
                // 切换摇杆：先重置另一只，避免两只同时“按下”状态
                ResetJoystickVisual(other);
                _mobileJoystickVisualForceRun = forceRun;
                _mobileJoystickVisualActive = true;
            }

            Vector2 clampedDeltaUi = ClampJoystickDeltaToRing(selected, deltaUi, radiusUi);
            Vector2 knobUi = originUi + clampedDeltaUi;
            ApplyJoystickVisual(selected, originUi, knobUi, clampedDeltaUi);
        }

        private static Vector2 ClampJoystickDeltaToRing(MobileJoystickVisual view, Vector2 deltaUi, float radiusUi)
        {
            if (view == null || view.Root == null || view.Root._disposed)
                return deltaUi;

            float limit = radiusUi;

            try
            {
                // 以外圈背景圆为准（更符合视觉）；没有的话退回 Root。
                GObject ringObj = view.Center != null && !view.Center._disposed ? view.Center : view.Root;
                float diameter = Math.Min(ringObj.width, ringObj.height);
                if (diameter > 1F)
                {
                    float outer = diameter * 0.5F;

                    float knobDiameter = 0F;
                    try
                    {
                        GObject knobObj = view.Knob != null && !view.Knob._disposed ? view.Knob : (view.Button != null && !view.Button._disposed ? view.Button : null);
                        if (knobObj != null)
                            knobDiameter = Math.Max(0F, Math.Min(knobObj.width, knobObj.height));
                    }
                    catch
                    {
                        knobDiameter = 0F;
                    }

                    float visualLimit = outer - knobDiameter * 0.5F;
                    visualLimit *= 0.95F;
                    if (visualLimit > 1F)
                        limit = Math.Min(limit, visualLimit);
                }
            }
            catch
            {
            }

            float len = deltaUi.Length();
            if (len > limit && len > 0.001F)
                deltaUi *= limit / len;

            return deltaUi;
        }

        private static bool TryEnsureMobileDoubleJoystickVisualsResolved()
        {
            if (_mobileWalkJoystickVisual != null && _mobileWalkJoystickVisual.Root != null && !_mobileWalkJoystickVisual.Root._disposed &&
                _mobileRunJoystickVisual != null && _mobileRunJoystickVisual.Root != null && !_mobileRunJoystickVisual.Root._disposed)
            {
                return true;
            }

            if (_mobileJoystickVisualSearched)
                return _mobileWalkJoystickVisual?.Root != null && !_mobileWalkJoystickVisual.Root._disposed &&
                       _mobileRunJoystickVisual?.Root != null && !_mobileRunJoystickVisual.Root._disposed;

            // HUD/Stage 初始化时序：可能在主界面创建前就触发一次“摇杆松开”的视觉更新，
            // 如果这里直接标记 searched=true，会导致后续永远不再解析 UI 节点（表现为“中间圈不跟手/动效不触发”）。
            if (DateTime.UtcNow < _nextMobileJoystickVisualResolveAttemptUtc)
                return false;

            try
            {
                if (TryGetMobileDoubleJoystickRoot() is not GComponent doubleRoot || doubleRoot._disposed)
                {
                    // 还没找到 DoubleDJoystick：稍后重试（节流）
                    _nextMobileJoystickVisualResolveAttemptUtc = DateTime.UtcNow.AddMilliseconds(600);
                    if (Settings.LogErrors && DateTime.UtcNow >= _nextMobileJoystickVisualResolveLogUtc)
                    {
                        _nextMobileJoystickVisualResolveLogUtc = DateTime.UtcNow.AddSeconds(3);
                        try
                        {
                            CMain.SaveLog("FairyGUI: DoubleDJoystick 动效绑定等待主界面创建（稍后自动重试）。");
                        }
                        catch
                        {
                        }
                    }
                    return false;
                }

                _mobileJoystickVisualSearched = true;

                _mobileWalkJoystickVisual = TryBuildJoystickVisual(doubleRoot, "joystick_1");
                _mobileRunJoystickVisual = TryBuildJoystickVisual(doubleRoot, "joystick_2");

                if (!_mobileJoystickVisualLogged)
                {
                    _mobileJoystickVisualLogged = true;
                    if (_mobileWalkJoystickVisual?.Root == null || _mobileRunJoystickVisual?.Root == null)
                    {
                        CMain.SaveLog("FairyGUI: DoubleDJoystick 动效绑定未完全命中（joystick_1/joystick_2），将仅保留逻辑摇杆。可开启 DebugMode 导出主界面树以补充命名映射。");
                    }
                    else if (Settings.LogErrors && _mobileMainHud != null && !_mobileMainHud._disposed)
                    {
                        try
                        {
                            string walkRoot = DescribeObject(_mobileMainHud, _mobileWalkJoystickVisual.Root);
                            string walkKnob = DescribeObject(_mobileMainHud, _mobileWalkJoystickVisual.Knob);
                            string walkCenter = DescribeObject(_mobileMainHud, _mobileWalkJoystickVisual.Center);
                            string walkThumb = DescribeObject(_mobileMainHud, _mobileWalkJoystickVisual.Thumb);

                            string runRoot = DescribeObject(_mobileMainHud, _mobileRunJoystickVisual.Root);
                            string runKnob = DescribeObject(_mobileMainHud, _mobileRunJoystickVisual.Knob);
                            string runCenter = DescribeObject(_mobileMainHud, _mobileRunJoystickVisual.Center);
                            string runThumb = DescribeObject(_mobileMainHud, _mobileRunJoystickVisual.Thumb);

                            CMain.SaveLog("FairyGUI: DoubleDJoystick 动效绑定完成：\n" +
                                          "  Walk Root=" + walkRoot + "\n" +
                                          "  Walk Knob=" + walkKnob + "\n" +
                                          "  Walk Center=" + walkCenter + "\n" +
                                          "  Walk Thumb=" + walkThumb + "\n" +
                                          "  Run Root=" + runRoot + "\n" +
                                          "  Run Knob=" + runKnob + "\n" +
                                          "  Run Center=" + runCenter + "\n" +
                                          "  Run Thumb=" + runThumb);
                        }
                        catch
                        {
                        }
                    }
                }
            }
            catch
            {
                return false;
            }

            return _mobileWalkJoystickVisual?.Root != null && !_mobileWalkJoystickVisual.Root._disposed &&
                   _mobileRunJoystickVisual?.Root != null && !_mobileRunJoystickVisual.Root._disposed;
        }

        private static MobileJoystickVisual TryBuildJoystickVisual(GComponent doubleJoystickRoot, string key)
        {
            if (doubleJoystickRoot == null || doubleJoystickRoot._disposed || string.IsNullOrWhiteSpace(key))
                return null;

            string suffix = null;
            try
            {
                if (key.EndsWith("_1", StringComparison.OrdinalIgnoreCase))
                    suffix = "1";
                else if (key.EndsWith("_2", StringComparison.OrdinalIgnoreCase))
                    suffix = "2";
            }
            catch
            {
                suffix = null;
            }

            // UIRes 里的 DoubleDJoystick 结构：
            // - joystick_touch_1/2：Graph（触发区域）
            // - joystick_center_1/2：Image（背景圆）
            // - joystick_1/2：Button（中间圈，需要跟随手指）
            // - direct_1/2：Image（方向箭头）
            // 这里 Root 使用 touch（Graph），Knob 使用 joystick_1/2（Button）。
            GObject root = null;
            try
            {
                if (!string.IsNullOrWhiteSpace(suffix))
                    root = TryFindChildByNameRecursive(doubleJoystickRoot, "joystick_touch_" + suffix);
            }
            catch
            {
                root = null;
            }

            if (root == null || root._disposed)
                root = TryFindChildByNameRecursive(doubleJoystickRoot, key);

            if (root == null || root._disposed)
                return null;

            var view = new MobileJoystickVisual
            {
                Key = key,
                Root = root,
            };

            try
            {
                // 中间圈（需要跟随手指）
                GObject keyObj = null;
                try
                {
                    keyObj = TryFindChildByNameRecursive(doubleJoystickRoot, key);
                }
                catch
                {
                    keyObj = null;
                }

                view.Button = keyObj as GButton ?? keyObj?.asButton;
                view.Knob = keyObj;

                // 背景圆（原点）
                try
                {
                    if (!string.IsNullOrWhiteSpace(suffix))
                        view.Center = TryFindChildByNameRecursive(doubleJoystickRoot, "joystick_center_" + suffix);
                }
                catch
                {
                    view.Center = null;
                }

                // 方向/箭头：direct_1/2（优先）或 thumb1/2（兜底）
                try
                {
                    if (!string.IsNullOrWhiteSpace(suffix))
                    {
                        view.Thumb = TryFindChildByNameRecursive(doubleJoystickRoot, "direct_" + suffix) ??
                                    TryFindChildByNameRecursive(doubleJoystickRoot, "thumb" + suffix);
                    }
                }
                catch
                {
                    view.Thumb = null;
                }

                if (view.Thumb == null || view.Thumb._disposed)
                {
                    try
                    {
                        if (view.Button != null && !view.Button._disposed)
                            view.Thumb = view.Button.GetChild("thumb") ?? view.Button.GetChild("Thumb") ?? view.Button.GetChild("direct") ?? view.Button.GetChild("Direct");
                    }
                    catch
                    {
                        view.Thumb = null;
                    }
                }
            }
            catch
            {
                view.Button = null;
            }

            try
            {
                if (view.Button != null && !view.Button._disposed)
                {
                    view.Button.changeStateOnClick = false;
                    view.Button.enabled = true;
                    view.Button.grayed = false;
                    view.Button.touchable = true;

                    if (view.Knob == null || view.Knob._disposed)
                        view.Knob = view.Button;
                }
            }
            catch
            {
            }

            if (view.Knob == null || view.Knob._disposed)
                view.Knob = view.Button ?? root;

            if (view.Center == null || view.Center._disposed)
                view.Center = view.Knob;

            try
            {
                if (view.Button != null && !view.Button._disposed)
                    view.InitButtonPos = new Vector2(view.Button.x, view.Button.y);
            }
            catch
            {
                view.InitButtonPos = Vector2.Zero;
            }

            try
            {
                if (view.Knob != null && !view.Knob._disposed)
                    view.InitKnobPos = new Vector2(view.Knob.x, view.Knob.y);
            }
            catch
            {
                view.InitKnobPos = Vector2.Zero;
            }

            try
            {
                if (view.Center != null && !view.Center._disposed)
                    view.InitCenterPos = new Vector2(view.Center.x, view.Center.y);
            }
            catch
            {
                view.InitCenterPos = Vector2.Zero;
            }

            try
            {
                if (view.Knob != null && !view.Knob._disposed)
                    view.InitKnobVisible = view.Knob.visible;
            }
            catch
            {
                view.InitKnobVisible = true;
            }

            try
            {
                if (view.Center != null && !view.Center._disposed)
                    view.InitCenterVisible = view.Center.visible;
            }
            catch
            {
                view.InitCenterVisible = true;
            }

            view.Initialized = true;
            return view;
        }

        private static GObject ChooseSmallerArea(GObject a, GObject b)
        {
            if (a == null || a._disposed)
                return b != null && !b._disposed ? b : null;

            if (b == null || b._disposed)
                return a;

            try
            {
                float areaA = Math.Max(0F, a.width) * Math.Max(0F, a.height);
                float areaB = Math.Max(0F, b.width) * Math.Max(0F, b.height);
                if (areaA <= 0F && areaB <= 0F)
                    return a;

                if (areaA <= 0F)
                    return b;

                if (areaB <= 0F)
                    return a;

                return areaA <= areaB ? a : b;
            }
            catch
            {
                return a;
            }
        }

        private static void ResetJoystickVisual(MobileJoystickVisual view)
        {
            if (view == null)
                return;

            try
            {
                if (view.Button != null && !view.Button._disposed)
                {
                    SetJoystickButtonPressed(view.Button, pressed: false);
                }
            }
            catch
            {
            }

            try
            {
                if (view.Knob != null && !view.Knob._disposed)
                {
                    view.Knob.visible = view.InitKnobVisible;
                    view.Knob.SetPosition(view.InitKnobPos.X, view.InitKnobPos.Y);
                }
                else if (view.Button != null && !view.Button._disposed)
                    view.Button.SetPosition(view.InitButtonPos.X, view.InitButtonPos.Y);
            }
            catch
            {
            }

            try
            {
                if (view.Center != null && !view.Center._disposed)
                {
                    view.Center.visible = view.InitCenterVisible;
                    view.Center.SetPosition(view.InitCenterPos.X, view.InitCenterPos.Y);
                }
            }
            catch
            {
            }

            try
            {
                if (view.Thumb != null && !view.Thumb._disposed)
                    view.Thumb.rotation = 0F;
            }
            catch
            {
            }
        }

        private static void ApplyJoystickVisual(MobileJoystickVisual view, Vector2 originUi, Vector2 knobUi, Vector2 deltaUi)
        {
            if (view == null || view.Root == null || view.Root._disposed)
                return;

            try
            {
                if (view.Button != null && !view.Button._disposed)
                    SetJoystickButtonPressed(view.Button, pressed: true);
            }
            catch
            {
            }

            try
            {
                if (view.Center != null && !view.Center._disposed)
                {
                    view.Center.visible = true;
                    SetObjectCenterToGlobal(view.Center, originUi);
                }
            }
            catch
            {
            }

            try
            {
                if (view.Knob != null && !view.Knob._disposed)
                {
                    view.Knob.visible = true;
                    SetObjectCenterToGlobal(view.Knob, knobUi);
                }
                else if (view.Button != null && !view.Button._disposed)
                    SetObjectCenterToGlobal(view.Button, knobUi);
            }
            catch
            {
            }

            try
            {
                if (view.Thumb != null && !view.Thumb._disposed)
                {
                    float degrees = 0F;
                    if (deltaUi.LengthSquared() > 0.0001F)
                        degrees = (float)Math.Atan2(deltaUi.Y, deltaUi.X) * 180F / (float)Math.PI;
                    view.Thumb.rotation = degrees + 90F;
                }
            }
            catch
            {
            }
        }

        private static void SetJoystickButtonPressed(GButton button, bool pressed)
        {
            if (button == null || button._disposed)
                return;

            // GButton 在 Common mode 下 selected setter 直接 return；这里优先用 button controller 触发 down 状态。
            try
            {
                Controller controller = null;
                try
                {
                    controller = button.GetController("button");
                }
                catch
                {
                    controller = null;
                }

                if (controller == null)
                {
                    try
                    {
                        controller = button.GetControllerAt(0);
                    }
                    catch
                    {
                        controller = null;
                    }
                }

                if (controller != null && controller.HasPage(GButton.UP) && controller.HasPage(GButton.DOWN))
                    controller.selectedPage = pressed ? GButton.DOWN : GButton.UP;
            }
            catch
            {
            }

            try
            {
                button.selected = pressed;
            }
            catch
            {
            }
        }

        private static void SetObjectCenterToGlobal(GObject obj, Vector2 globalUi)
        {
            if (obj == null || obj._disposed)
                return;

            GObject parent = obj.parent;
            if (parent == null || parent._disposed)
                return;

            try
            {
                // FairyGUI 的 Global 坐标就是 Stage 坐标（UI 坐标）。直接用 parent.GlobalToLocal 转换即可。
                Vector2 local = parent.GlobalToLocal(globalUi);
                obj.SetPosition(local.X - obj.width / 2F, local.Y - obj.height / 2F);
            }
            catch
            {
            }
        }

        private static GObject TryGetMobileDoubleJoystickRoot()
        {
            if (_mobileMainHud == null || _mobileMainHud._disposed)
            {
                _mobileDoubleJoystickRoot = null;
                _nextMobileDoubleJoystickRootSearchUtc = DateTime.MinValue;
                return null;
            }

            if (_mobileDoubleJoystickRoot != null && !_mobileDoubleJoystickRoot._disposed)
                return _mobileDoubleJoystickRoot;

            if (DateTime.UtcNow < _nextMobileDoubleJoystickRootSearchUtc)
                return null;

            _mobileDoubleJoystickRoot = TryFindChildByNameRecursive(_mobileMainHud, "DoubleDJoystick");
            if (_mobileDoubleJoystickRoot == null || _mobileDoubleJoystickRoot._disposed)
            {
                _mobileDoubleJoystickRoot = null;
                // 可能主界面还没构造完：允许后续重试
                _nextMobileDoubleJoystickRootSearchUtc = DateTime.UtcNow.AddMilliseconds(800);
                return null;
            }

            return _mobileDoubleJoystickRoot;
        }

        private static GObject TryFindChildByNameRecursive(GComponent root, string targetName)
        {
            if (root == null || root._disposed || string.IsNullOrWhiteSpace(targetName))
                return null;

            int count = root.numChildren;
            for (int i = 0; i < count; i++)
            {
                GObject child = root.GetChildAt(i);
                if (child == null || child._disposed)
                    continue;

                if (string.Equals(child.name, targetName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(child.packageItem?.name, targetName, StringComparison.OrdinalIgnoreCase))
                {
                    return child;
                }

                if (child is GComponent component)
                {
                    GObject found = TryFindChildByNameRecursive(component, targetName);
                    if (found != null && !found._disposed)
                        return found;
                }
            }

            return null;
        }
    }
}
