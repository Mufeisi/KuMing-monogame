using System;
using System.Collections.Generic;
using C = ClientPackets;
using FairyGUI;
using MonoShare.MirGraphics;
using MonoShare.MirObjects;
using MonoShare.MirScenes;

namespace MonoShare
{
    internal static partial class FairyGuiHost
    {
        private const string MobileAttackCircleMagicIconName = "__codex_mobile_attack_circle_magic_icon";

        private static GComponent _mobileAttackCircleRoot;
        private static GLoader _mobileAttackCircleMagicIcon;
        private static EventCallback1 _mobileAttackCircleDropCallback;
        private static EventCallback1 _mobileAttackCircleClickCaptureCallback;
        private static DateTime _nextMobileAttackCircleBindAttemptUtc = DateTime.MinValue;

        private static int _mobileAttackCircleHotKey;
        private static byte _mobileAttackCircleLastIcon;
        private static bool _mobileAttackCircleDirty;

        private static void ResetMobileAttackCircleBindings()
        {
            _mobileAttackCircleRoot = null;
            _mobileAttackCircleMagicIcon = null;
            _nextMobileAttackCircleBindAttemptUtc = DateTime.MinValue;
            _mobileAttackCircleHotKey = 0;
            _mobileAttackCircleLastIcon = 0;
            _mobileAttackCircleDirty = false;
        }

        private static void EnsureAttackCircleIconLayout()
        {
            if (_mobileAttackCircleRoot == null || _mobileAttackCircleRoot._disposed)
                return;

            if (_mobileAttackCircleMagicIcon == null || _mobileAttackCircleMagicIcon._disposed)
                return;

            float w = 0f;
            float h = 0f;
            try
            {
                w = _mobileAttackCircleRoot.width;
                h = _mobileAttackCircleRoot.height;
            }
            catch
            {
                return;
            }

            float size = Math.Min(w, h) * 0.62f;
            size = Math.Clamp(size, 18f, 120f);

            try
            {
                _mobileAttackCircleMagicIcon.SetSize(size, size);
                _mobileAttackCircleMagicIcon.SetPosition((w - size) / 2f, (h - size) / 2f);
            }
            catch
            {
            }
        }

        private static void OnMobileAttackCircleDropped(EventContext context)
        {
            try { context?.StopPropagation(); } catch { }
            try { context?.PreventDefault(); } catch { }

            if (context?.data is not MobileMagicDragPayload payload)
                return;

            int hotKey = payload.HotKey;
            if (hotKey <= 0 && payload.Spell != Spell.None)
            {
                try
                {
                    UserObject user = GameScene.User;
                    ClientMagic magic = user?.GetMagic(payload.Spell);
                    if (magic != null)
                    {
                        if (magic.Key > 0)
                        {
                            hotKey = magic.Key;
                        }
                        else
                        {
                            byte oldKey = magic.Key;
                            byte keyToUse = 0;

                            if (_mobileAttackCircleHotKey > 0 && _mobileAttackCircleHotKey <= byte.MaxValue)
                                keyToUse = (byte)_mobileAttackCircleHotKey;

                            List<ClientMagic> magics = user?.Magics;
                            if (keyToUse == 0 && magics != null)
                            {
                                for (int candidateKey = 9; candidateKey <= 16; candidateKey++)
                                {
                                    bool used = false;
                                    for (int i = 0; i < magics.Count; i++)
                                    {
                                        ClientMagic m = magics[i];
                                        if (m == null || m == magic)
                                            continue;
                                        if (m.Key == candidateKey)
                                        {
                                            used = true;
                                            break;
                                        }
                                    }

                                    if (!used)
                                    {
                                        keyToUse = (byte)candidateKey;
                                        break;
                                    }
                                }

                                for (int candidateKey = 1; keyToUse == 0 && candidateKey <= 8; candidateKey++)
                                {
                                    bool used = false;
                                    for (int i = 0; i < magics.Count; i++)
                                    {
                                        ClientMagic m = magics[i];
                                        if (m == null || m == magic)
                                            continue;
                                        if (m.Key == candidateKey)
                                        {
                                            used = true;
                                            break;
                                        }
                                    }

                                    if (!used)
                                    {
                                        keyToUse = (byte)candidateKey;
                                        break;
                                    }
                                }
                            }

                            if (keyToUse == 0)
                                keyToUse = 16;

                            if (magics != null)
                            {
                                for (int i = 0; i < magics.Count; i++)
                                {
                                    ClientMagic m = magics[i];
                                    if (m == null || m == magic)
                                        continue;
                                    if (m.Key == keyToUse)
                                        m.Key = 0;
                                }
                            }

                            MonoShare.MirNetwork.Network.Enqueue(new C.MagicKey
                            {
                                Spell = magic.Spell,
                                Key = keyToUse,
                                OldKey = oldKey,
                            });

                            magic.Key = keyToUse;
                            hotKey = keyToUse;
                        }
                    }
                }
                catch
                {
                    return;
                }
            }

            if (hotKey <= 0)
                return;

            _mobileAttackCircleHotKey = hotKey;
            _mobileAttackCircleDirty = true;

            try
            {
                // 立即刷新图标（避免等下一帧）
                TryRefreshMobileMainHudAttackCircleIfDue(force: true);
            }
            catch
            {
            }
        }

        private static void OnMobileAttackCircleClickedCapture(EventContext context)
        {
            // 用 capture 吞掉 publish/控制器里可能绑定的“切换攻击模式”点击事件
            try { context?.StopPropagation(); } catch { }
            try { context?.PreventDefault(); } catch { }

            int hotKey = _mobileAttackCircleHotKey;
            if (hotKey > 0)
            {
                try
                {
                    GameScene.Scene?.UseSpell(hotKey, fromUI: true);
                    return;
                }
                catch
                {
                }
            }

            try
            {
                GameScene.Scene?.ChangeAttackMode();
            }
            catch
            {
            }
        }

        private static void TryBindMobileMainHudAttackCircleIfDue()
        {
            if (_mobileMainHud == null || _mobileMainHud._disposed)
                return;

            if (_mobileAttackCircleRoot != null && !_mobileAttackCircleRoot._disposed)
            {
                // root 变更或 icon 丢失时允许重绑
                if (_mobileAttackCircleMagicIcon != null && !_mobileAttackCircleMagicIcon._disposed)
                    return;
            }

            if (DateTime.UtcNow < _nextMobileAttackCircleBindAttemptUtc)
                return;

            _nextMobileAttackCircleBindAttemptUtc = DateTime.UtcNow.AddSeconds(2);

            GObject found = null;
            try { found = TryFindChildByNameRecursive(_mobileMainHud, "DBtnAttack0"); } catch { found = null; }
            if (found == null || found._disposed)
            {
                try { found = TryFindChildByNameRecursive(_mobileMainHud, "DArrackModelUI"); } catch { found = null; }
            }

            if (found == null || found._disposed)
                return;

            if (found is not GComponent root || root._disposed)
                return;

            _mobileAttackCircleRoot = root;

            try
            {
                DisableMobileDescendantTouch(root);
                root.touchable = true;
                if (root is GButton button)
                {
                    button.enabled = true;
                    button.grayed = false;
                    button.changeStateOnClick = false;
                }
            }
            catch
            {
            }

            // drop
            _mobileAttackCircleDropCallback ??= OnMobileAttackCircleDropped;
            try { root.RemoveEventListener("onDrop", _mobileAttackCircleDropCallback); } catch { }
            try { root.AddEventListener("onDrop", _mobileAttackCircleDropCallback); } catch { }

            // click capture（避免触发其它 click 绑定）
            _mobileAttackCircleClickCaptureCallback ??= OnMobileAttackCircleClickedCapture;
            try { root.onClick.RemoveCapture(_mobileAttackCircleClickCaptureCallback); } catch { }
            try { root.onClick.AddCapture(_mobileAttackCircleClickCaptureCallback); } catch { }

            // icon overlay
            GLoader icon = null;
            try { icon = root.GetChild(MobileAttackCircleMagicIconName) as GLoader; } catch { icon = null; }
            if (icon == null || icon._disposed)
            {
                try
                {
                    icon = new GLoader
                    {
                        name = MobileAttackCircleMagicIconName,
                        touchable = false,
                        visible = false,
                        url = string.Empty,
                        showErrorSign = false,
                    };
                    root.AddChild(icon);
                }
                catch
                {
                    icon = null;
                }
            }

            _mobileAttackCircleMagicIcon = icon;

            try
            {
                if (_mobileAttackCircleMagicIcon != null && !_mobileAttackCircleMagicIcon._disposed)
                    root.SetChildIndex(_mobileAttackCircleMagicIcon, root.numChildren - 1);
            }
            catch
            {
            }

            EnsureAttackCircleIconLayout();
            _mobileAttackCircleDirty = true;
        }

        private static void TryRefreshMobileMainHudAttackCircleIfDue(bool force)
        {
            if (_stage == null || !_initialized || !_packagesLoaded)
                return;

            if (_mobileMainHud == null || _mobileMainHud._disposed)
            {
                if (_mobileAttackCircleRoot != null || _mobileAttackCircleMagicIcon != null)
                    ResetMobileAttackCircleBindings();
                return;
            }

            TryBindMobileMainHudAttackCircleIfDue();

            if (_mobileAttackCircleRoot == null || _mobileAttackCircleRoot._disposed ||
                _mobileAttackCircleMagicIcon == null || _mobileAttackCircleMagicIcon._disposed)
                return;

            EnsureAttackCircleIconLayout();

            int hotKey = _mobileAttackCircleHotKey;
            if (hotKey <= 0)
            {
                if (force || _mobileAttackCircleDirty || _mobileAttackCircleLastIcon != 0)
                {
                    _mobileAttackCircleDirty = false;
                    _mobileAttackCircleLastIcon = 0;
                    try
                    {
                        _mobileAttackCircleMagicIcon.texture = null;
                        _mobileAttackCircleMagicIcon.url = string.Empty;
                        _mobileAttackCircleMagicIcon.visible = false;
                    }
                    catch
                    {
                    }
                }

                return;
            }

            byte iconByte = 0;
            try
            {
                UserObject user = GameScene.User;
                if (user?.Magics != null)
                {
                    for (int i = 0; i < user.Magics.Count; i++)
                    {
                        ClientMagic magic = user.Magics[i];
                        if (magic != null && magic.Key == hotKey)
                        {
                            iconByte = magic.Icon;
                            break;
                        }
                    }
                }
            }
            catch
            {
                iconByte = 0;
            }

            if (!force && !_mobileAttackCircleDirty && iconByte == _mobileAttackCircleLastIcon)
                return;

            _mobileAttackCircleDirty = false;
            _mobileAttackCircleLastIcon = iconByte;

            if (iconByte == 0)
            {
                try
                {
                    _mobileAttackCircleMagicIcon.texture = null;
                    _mobileAttackCircleMagicIcon.url = string.Empty;
                    _mobileAttackCircleMagicIcon.visible = false;
                }
                catch
                {
                }

                return;
            }

            try { Libraries.MagIcon2.Touch(iconByte * 2); } catch { }
            try { Libraries.MagIcon2.Touch(iconByte); } catch { }

            try
            {
                NTexture texture = GetOrCreateMagicIconTexture(iconByte);
                _mobileAttackCircleMagicIcon.showErrorSign = false;
                _mobileAttackCircleMagicIcon.url = string.Empty;
                _mobileAttackCircleMagicIcon.texture = texture;
                _mobileAttackCircleMagicIcon.visible = true;
            }
            catch
            {
            }
        }
    }
}
