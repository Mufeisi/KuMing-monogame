using System;
using FairyGUI;
using MonoShare.MirScenes;

namespace MonoShare
{
    internal static partial class FairyGuiHost
    {
        private sealed class MobileButtonCooldownState
        {
            public GButton Button;
            public bool OriginalTouchable;
            public float OriginalAlpha;
        }

        private static void MobileHint(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            try { GameScene.Scene?.OutputMessage(message.Trim()); } catch { }
        }

        private static void ApplyMobileButtonCooldown(GButton button, float seconds = 0.6f)
        {
            if (button == null || button._disposed)
                return;

            if (seconds <= 0.01f)
                return;

            var state = new MobileButtonCooldownState
            {
                Button = button,
                OriginalTouchable = button.touchable,
                OriginalAlpha = button.alpha,
            };

            try { button.touchable = false; } catch { }
            try { button.alpha = Math.Max(0.25f, state.OriginalAlpha * 0.7f); } catch { }

            try { Timers.inst.Add(seconds, 1, RestoreMobileButtonCooldown, state); } catch { }
        }

        private static void RestoreMobileButtonCooldown(object param)
        {
            if (param is not MobileButtonCooldownState state)
                return;

            GButton button = state.Button;
            if (button == null || button._disposed)
                return;

            try { button.touchable = state.OriginalTouchable; } catch { }
            try { button.alpha = state.OriginalAlpha; } catch { }
        }

        private static bool TryValidateCharacterName(string raw, string purpose, out string cleaned)
        {
            cleaned = (raw ?? string.Empty).Trim();

            if (cleaned.Length == 0)
            {
                MobileHint($"{purpose}：请输入角色名。");
                return false;
            }

            if (cleaned.Length < Globals.MinCharacterNameLength || cleaned.Length > Globals.MaxCharacterNameLength)
            {
                MobileHint($"{purpose}：角色名长度需在 {Globals.MinCharacterNameLength}-{Globals.MaxCharacterNameLength} 之间。");
                return false;
            }

            if (cleaned.Contains(' ') || cleaned.Contains('\t') || cleaned.Contains('\r') || cleaned.Contains('\n'))
            {
                MobileHint($"{purpose}：角色名不能包含空格。");
                return false;
            }

            return true;
        }

        private static bool TryNormalizeFriendMemo(string raw, out string cleaned)
        {
            cleaned = (raw ?? string.Empty).Trim();

            if (cleaned.Length == 0)
            {
                MobileHint("请输入备注内容。");
                return false;
            }

            if (cleaned.Length > 200)
            {
                cleaned = cleaned.Substring(0, 200);
                MobileHint("备注过长，已截断为 200 字。");
            }

            return true;
        }
    }
}

