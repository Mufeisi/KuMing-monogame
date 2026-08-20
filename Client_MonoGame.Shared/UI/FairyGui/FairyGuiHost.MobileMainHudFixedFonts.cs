using System;
using FairyGUI;
using Microsoft.Xna.Framework;

namespace MonoShare
{
    internal static partial class FairyGuiHost
    {
        private static DateTime _nextMobileMainHudFixedFontEnforceUtc = DateTime.MinValue;

        private static void TryEnforceMobileMainHudFixedFontSizesIfDue()
        {
            if (_mobileMainHud == null || _mobileMainHud._disposed)
            {
                _nextMobileMainHudFixedFontEnforceUtc = DateTime.MinValue;
                return;
            }

            if (DateTime.UtcNow < _nextMobileMainHudFixedFontEnforceUtc)
                return;

            _nextMobileMainHudFixedFontEnforceUtc = DateTime.UtcNow.AddMilliseconds(500);

            const int desiredSize = 20;

            // 聊天输入窗口（若存在）
            try { ApplyMobileChatFontSizes(); } catch { }

            // 左侧怪物信息框/目标信息框（名字、血量等）
            try
            {
                ApplyFixedFontToHudBlock("DMonsterBlood", desiredSize);
                ApplyFixedFontToHudBlock("TargetCom", desiredSize);
                ApplyFixedFontToHudBlock("TargetHP", desiredSize);
                ApplyFixedFontToHudBlock("BloodTxtCom", desiredSize);
            }
            catch
            {
            }
        }

        private static void ApplyFixedFontToHudBlock(string rootName, int desiredSize)
        {
            if (string.IsNullOrWhiteSpace(rootName))
                return;

            GObject root = null;
            try { root = TryFindChildByNameRecursive(_mobileMainHud, rootName); } catch { root = null; }
            if (root == null || root._disposed)
                return;

            if (root is GTextField direct && direct is not GTextInput)
            {
                ApplyFixedFontToTextField(direct, desiredSize);
                return;
            }

            if (root is not GComponent component || component._disposed)
                return;

            foreach (GObject obj in Enumerate(component))
            {
                if (obj == null || obj._disposed)
                    continue;

                if (obj is not GTextField field || field is GTextInput || field._disposed)
                    continue;

                ApplyFixedFontToTextField(field, desiredSize);
            }
        }

        private static void ApplyFixedFontToTextField(GTextField field, int desiredSize)
        {
            if (field == null || field._disposed)
                return;

            try
            {
                TextFormat format = field.textFormat ?? new TextFormat();
                if (format.size != desiredSize)
                {
                    format.size = desiredSize;
                    field.textFormat = format;
                }
            }
            catch
            {
            }

            try
            {
                field.stroke = 1;
                field.strokeColor = new Color(0, 0, 0, 200);
            }
            catch
            {
            }
        }
    }
}

