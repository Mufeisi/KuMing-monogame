using System;
using FairyGUI;
using Microsoft.Xna.Framework;
using MonoShare.MirScenes;

namespace MonoShare
{
    internal static partial class FairyGuiHost
    {
        private static GComponent _mobileMainHudStateWindow;
        private static GTextField _mobileMainHudGoldText;
        private static GTextField _mobileMainHudCreditText;

        private static DateTime _nextMobileMainHudCurrencyBindAttemptUtc = DateTime.MinValue;
        private static uint _mobileMainHudLastGold;
        private static uint _mobileMainHudLastCredit;

        private static void ResetMobileMainHudCurrencyBindings()
        {
            _mobileMainHudStateWindow = null;
            _mobileMainHudGoldText = null;
            _mobileMainHudCreditText = null;
            _nextMobileMainHudCurrencyBindAttemptUtc = DateTime.MinValue;
            _mobileMainHudLastGold = uint.MaxValue;
            _mobileMainHudLastCredit = uint.MaxValue;
        }

        private static GTextField FindStateWindowTextFieldByName(GComponent stateWindow, string name)
        {
            if (stateWindow == null || stateWindow._disposed || string.IsNullOrWhiteSpace(name))
                return null;

            GObject obj = null;
            try { obj = stateWindow.GetChild(name) ?? TryFindChildByNameRecursive(stateWindow, name); } catch { obj = null; }

            if (obj is GTextField tf && tf is not GTextInput && !tf._disposed)
                return tf;

            if (obj is GComponent component && component != null && !component._disposed)
            {
                try
                {
                    foreach (GObject child in Enumerate(component))
                    {
                        if (child is GTextField childText && childText is not GTextInput && !childText._disposed)
                            return childText;
                    }
                }
                catch
                {
                }

                // 有些 publish 把文字包在组件里（例如 Label/title），这里递归找一个最像文字的字段。
                try
                {
                    var candidates = CollectMobileChatCandidates(
                        component,
                        o => o is GTextField && o is not GTextInput,
                        new[] { name, "gold", "money", "jinbi", "yuanbao", "credit", "金币", "元宝", "点券" },
                        (o, kw) => ScoreAnyField(o, kw, equalsWeight: 200, startsWithWeight: 120, containsWeight: 50));

                    GTextField selected = SelectMobileChatCandidate<GTextField>(candidates, minScore: 10);
                    if (selected != null && !selected._disposed)
                        return selected;
                }
                catch
                {
                }
            }

            return null;
        }

        private static GTextField FindStateWindowTextField(GComponent stateWindow, params string[] names)
        {
            if (names == null || names.Length == 0)
                return null;

            for (int i = 0; i < names.Length; i++)
            {
                GTextField field = FindStateWindowTextFieldByName(stateWindow, names[i]);
                if (field != null && !field._disposed)
                    return field;
            }

            return null;
        }

        private static void TryBindMobileMainHudCurrencyIfDue()
        {
            if (_mobileMainHud == null || _mobileMainHud._disposed)
                return;

            if (_mobileMainHudStateWindow != null && !_mobileMainHudStateWindow._disposed &&
                _mobileMainHudGoldText != null && !_mobileMainHudGoldText._disposed &&
                _mobileMainHudCreditText != null && !_mobileMainHudCreditText._disposed)
                return;

            if (DateTime.UtcNow < _nextMobileMainHudCurrencyBindAttemptUtc)
                return;

            _nextMobileMainHudCurrencyBindAttemptUtc = DateTime.UtcNow.AddSeconds(2);

            GComponent stateWindow = null;
            try { stateWindow = TryFindChildByNameRecursive(_mobileMainHud, "DStateWindow") as GComponent; } catch { stateWindow = null; }

            GTextField goldField = null;
            GTextField creditField = null;

            if (stateWindow != null && !stateWindow._disposed)
            {
                goldField = FindStateWindowTextField(stateWindow, "JinBiWin", "JinPiaoWin", "GoldWin", "GoldText");
                creditField = FindStateWindowTextField(stateWindow, "YuanBaoWin", "CreditWin", "YBWin");
            }

            if (stateWindow == null || stateWindow._disposed || goldField == null || creditField == null)
            {
                GComponent bestStateWindow = stateWindow;
                GTextField bestGoldField = goldField;
                GTextField bestCreditField = creditField;
                int bestScore = int.MinValue;

                try
                {
                    foreach (GObject obj in Enumerate(_mobileMainHud))
                    {
                        if (obj is not GComponent candidate || candidate._disposed)
                            continue;

                        string candidateName = candidate.name ?? string.Empty;
                        string candidateItem = candidate.packageItem?.name ?? string.Empty;
                        if (!candidateName.Contains("DStateWindow", StringComparison.OrdinalIgnoreCase) &&
                            !candidateItem.Contains("DStateWindow", StringComparison.OrdinalIgnoreCase))
                            continue;

                        GTextField candidateGoldField = FindStateWindowTextField(candidate, "JinBiWin", "JinPiaoWin", "GoldWin", "GoldText");
                        GTextField candidateCreditField = FindStateWindowTextField(candidate, "YuanBaoWin", "CreditWin", "YBWin");

                        int score = 0;
                        if (candidateGoldField != null && !candidateGoldField._disposed)
                            score += 240;
                        if (candidateCreditField != null && !candidateCreditField._disposed)
                            score += 240;

                        try
                        {
                            Vector2 pos = candidate.LocalToGlobal(Vector2.Zero);
                            score -= (int)Math.Clamp(pos.X + pos.Y, 0F, 500F);
                        }
                        catch
                        {
                        }

                        if (score <= bestScore)
                            continue;

                        bestScore = score;
                        bestStateWindow = candidate;
                        bestGoldField = candidateGoldField;
                        bestCreditField = candidateCreditField;
                    }
                }
                catch
                {
                }

                stateWindow = bestStateWindow;
                goldField = bestGoldField;
                creditField = bestCreditField;
            }

            if (stateWindow == null || stateWindow._disposed)
                return;

            _mobileMainHudStateWindow = stateWindow;
            _mobileMainHudGoldText = goldField;
            _mobileMainHudCreditText = creditField;

            try
            {
                if (_mobileMainHudGoldText != null && !_mobileMainHudGoldText._disposed)
                {
                    _mobileMainHudGoldText.touchable = false;
                    TextFormat tf = _mobileMainHudGoldText.textFormat;
                    tf.size = Math.Max(tf.size, 18);
                    _mobileMainHudGoldText.textFormat = tf;
                }
            }
            catch
            {
            }

            try
            {
                if (_mobileMainHudCreditText != null && !_mobileMainHudCreditText._disposed)
                {
                    _mobileMainHudCreditText.touchable = false;
                    TextFormat tf = _mobileMainHudCreditText.textFormat;
                    tf.size = Math.Max(tf.size, 18);
                    _mobileMainHudCreditText.textFormat = tf;
                }
            }
            catch
            {
            }
        }

        private static void TryRefreshMobileMainHudCurrencyIfDue(bool force)
        {
            if (_stage == null || !_initialized || !_packagesLoaded)
                return;

            if (_mobileMainHud == null || _mobileMainHud._disposed)
            {
                if (_mobileMainHudStateWindow != null || _mobileMainHudGoldText != null || _mobileMainHudCreditText != null)
                    ResetMobileMainHudCurrencyBindings();
                return;
            }

            TryBindMobileMainHudCurrencyIfDue();

            bool hasGoldField = _mobileMainHudGoldText != null && !_mobileMainHudGoldText._disposed;
            bool hasCreditField = _mobileMainHudCreditText != null && !_mobileMainHudCreditText._disposed;
            if (!hasGoldField && !hasCreditField)
                return;

            uint gold = GameScene.Gold;
            uint credit = GameScene.Credit;

            if (!force && gold == _mobileMainHudLastGold && credit == _mobileMainHudLastCredit)
                return;

            _mobileMainHudLastGold = gold;
            _mobileMainHudLastCredit = credit;

            try
            {
                if (_mobileMainHudGoldText != null && !_mobileMainHudGoldText._disposed)
                {
                    _mobileMainHudGoldText.text = gold.ToString("#,##0");

                    TextFormat tf = _mobileMainHudGoldText.textFormat ?? new TextFormat();
                    tf.size = Math.Max(tf.size, 18);
                    _mobileMainHudGoldText.textFormat = tf;
                }
            }
            catch
            {
            }

            try
            {
                if (_mobileMainHudCreditText != null && !_mobileMainHudCreditText._disposed)
                {
                    _mobileMainHudCreditText.text = credit.ToString("#,##0");

                    TextFormat tf = _mobileMainHudCreditText.textFormat ?? new TextFormat();
                    tf.size = Math.Max(tf.size, 18);
                    _mobileMainHudCreditText.textFormat = tf;
                }
            }
            catch
            {
            }
        }
    }
}
