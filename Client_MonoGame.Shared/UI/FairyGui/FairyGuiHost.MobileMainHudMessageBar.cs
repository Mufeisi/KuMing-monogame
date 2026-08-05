using System;
using System.Collections.Generic;
using FairyGUI;
using Microsoft.Xna.Framework;

namespace MonoShare
{
    internal static partial class FairyGuiHost
    {
        private static readonly string[] DefaultMobileMainHudMessageBarKeywords = { "消息", "msg", "message", "chat", "聊天", "提示", "hint", "系统", "公告" };

        private static GTextField _mobileMainHudMessageBar;
        private static GTextField _mobileMainHudMessageBarFallback;
        private static GGraph _mobileMainHudMessageBarFallbackBg;
        private static GTextField _mobileMainHudChatWindowFallback;
        private static string _mobileMainHudMessageBarResolveInfo;
        private static DateTime _nextMobileMainHudMessageBarBindAttemptUtc = DateTime.MinValue;
        private static DateTime _nextMobileMainHudChatTextStyleEnforceUtc = DateTime.MinValue;
        private static bool _mobileMainHudMessageBarDirty;
        private static int _mobileMainHudMessageBarLastHash;
        private static bool _mobileMainHudMessageBarIsChatWindow;
        private static bool _mobileMainHudChatTextStyleApplied;

        private static void ResetMobileMainHudMessageBarBindings()
        {
            _mobileMainHudMessageBar = null;
            _mobileMainHudMessageBarFallback = null;
            _mobileMainHudMessageBarFallbackBg = null;
            _mobileMainHudChatWindowFallback = null;
            _mobileMainHudMessageBarResolveInfo = null;
            _nextMobileMainHudMessageBarBindAttemptUtc = DateTime.MinValue;
            _nextMobileMainHudChatTextStyleEnforceUtc = DateTime.MinValue;
            _mobileMainHudMessageBarDirty = false;
            _mobileMainHudMessageBarLastHash = 0;
            _mobileMainHudMessageBarIsChatWindow = false;
            _mobileMainHudChatTextStyleApplied = false;
        }

        private static void ApplyMobileChatTextStyle(GTextField field, bool isChatWindow)
        {
            if (field == null || field._disposed)
                return;

            try
            {
                // 需求：锁定字号与描边，避免 publish/控制器在运行时重置导致“字号跳变”。
                int desiredSize = isChatWindow ? 20 : 22;

                try
                {
                    TextFormat textFormat = field.textFormat ?? new TextFormat();
                    if (textFormat.size != desiredSize)
                        textFormat.size = desiredSize;

                    textFormat.color = Color.White;
                    field.textFormat = textFormat;
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
            catch
            {
            }
        }

        private static void TryEnforceMobileMainHudChatTextStyleIfDue()
        {
            if (_mobileMainHudMessageBar == null || _mobileMainHudMessageBar._disposed)
                return;

            if (DateTime.UtcNow < _nextMobileMainHudChatTextStyleEnforceUtc)
                return;

            _nextMobileMainHudChatTextStyleEnforceUtc = DateTime.UtcNow.AddMilliseconds(500);

            try
            {
                ApplyMobileChatTextStyle(_mobileMainHudMessageBar, _mobileMainHudMessageBarIsChatWindow);
            }
            catch
            {
            }
        }

        private static GTextField TryResolveMobileMainHudChatWindowText(out string resolveInfo)
        {
            resolveInfo = null;

            if (_mobileMainHud == null || _mobileMainHud._disposed)
                return null;

            try
            {
                // 目标：BottomUI/DChatWindow（系统消息输出入口）
                if (TryFindChildByNameRecursive(_mobileMainHud, "BottomUI") is not GComponent bottom || bottom._disposed)
                    return null;

                GObject chatRoot =
                    TryFindChildByNameRecursive(bottom, "DChatWindowCom") ??
                    TryFindChildByNameRecursive(bottom, "DChatWindow");

                if (chatRoot == null || chatRoot._disposed)
                    return null;

                if (chatRoot is GTextField direct && direct is not GTextInput)
                {
                    resolveInfo = DescribeObject(_mobileMainHud, direct) + " (BottomUI/DChatWindow)";
                    return direct;
                }

                if (chatRoot is not GComponent chatComponent || chatComponent._disposed)
                    return null;

                try
                {
                    if (TryFindChildByNameRecursive(chatComponent, "ChatWindowText") is GTextField exact && exact is not GTextInput && !exact._disposed)
                    {
                        resolveInfo = DescribeObject(_mobileMainHud, exact) + " (BottomUI/DChatWindow/ChatWindowText)";
                        return exact;
                    }
                }
                catch
                {
                }

                string[] keywords = { "ChatWindowText", "chatwindow", "chat", "message", "msg", "聊天", "消息", "系统", "公告", "提示" };
                List<(int Score, GObject Target)> candidates = CollectMobileChatCandidates(
                    chatComponent,
                    obj => obj is GTextField && obj is not GTextInput,
                    keywords,
                    ScoreMobileMainHudMessageBarCandidate);

                GTextField selected = SelectMobileChatCandidate<GTextField>(candidates, minScore: 15);
                if (selected == null || selected._disposed)
                {
                    // 兜底：取聊天窗内“面积最大”的 TextField（publish 未必给了可命中的关键字）
                    try
                    {
                        float bestArea = 0F;
                        foreach (GObject obj in Enumerate(chatComponent))
                        {
                            if (obj == null || obj._disposed || obj is not GTextField field || field is GTextInput)
                                continue;

                            float area = Math.Max(0F, field.width) * Math.Max(0F, field.height);
                            if (area <= bestArea)
                                continue;

                            bestArea = area;
                            selected = field;
                        }
                    }
                    catch
                    {
                    }
                }

                if (selected == null || selected._disposed)
                {
                    selected = TryEnsureMobileMainHudChatWindowFallbackText(chatComponent);
                    if (selected == null || selected._disposed)
                        return null;

                    resolveInfo = DescribeObject(_mobileMainHud, selected) + " (BottomUI/DChatWindow-fallback)";
                    return selected;
                }

                resolveInfo = DescribeObject(_mobileMainHud, selected) + " (BottomUI/DChatWindow)";
                return selected;
            }
            catch
            {
                return null;
            }
        }

        private static GTextField TryEnsureMobileMainHudChatWindowFallbackText(GComponent chatComponent)
        {
            if (chatComponent == null || chatComponent._disposed)
                return null;

            if (_mobileMainHudChatWindowFallback != null && !_mobileMainHudChatWindowFallback._disposed && _mobileMainHudChatWindowFallback.parent == chatComponent)
                return _mobileMainHudChatWindowFallback;

            try
            {
                _mobileMainHudChatWindowFallback?.Dispose();
            }
            catch
            {
            }

            try
            {
                var text = new GRichTextField
                {
                    name = "__codex_mobile_chattxt",
                    touchable = false,
                    text = string.Empty,
                };

                try
                {
                    text.textFormat.size = 20;
                    text.textFormat.color = Color.White;
                    text.autoSize = AutoSizeType.None;
                }
                catch
                {
                }

                try
                {
                    text.stroke = 1;
                    text.strokeColor = new Color(0, 0, 0, 200);
                }
                catch
                {
                }

                text.SetPosition(0F, 0F);
                text.SetSize(Math.Max(1F, chatComponent.width), Math.Max(1F, chatComponent.height));

                chatComponent.AddChild(text);
                try
                {
                    chatComponent.SetChildIndex(text, chatComponent.numChildren - 1);
                }
                catch
                {
                }

                _mobileMainHudChatWindowFallback = text;
                return text;
            }
            catch
            {
                _mobileMainHudChatWindowFallback = null;
                return null;
            }
        }

        private static GTextField TryEnsureMobileMainHudMessageBarFallback()
        {
            if (_mobileMainHud == null || _mobileMainHud._disposed)
                return null;

            if (_mobileMainHudMessageBarFallback != null && !_mobileMainHudMessageBarFallback._disposed)
                return _mobileMainHudMessageBarFallback;

            try
            {
                float hudW = Math.Max(1F, _mobileMainHud.width);
                float safeTop = 0F;
                try
                {
                    // 如果已计算 SafeArea，则尽量避开刘海/状态栏区域（单位：UI 坐标）。
                    safeTop = Math.Max(0F, _mobileMainHudSafeAreaBounds.Y);
                }
                catch
                {
                    safeTop = 0F;
                }

                float marginTop = safeTop + 10F;
                float w = Math.Max(260F, hudW * 0.72F);
                float x = (hudW - w) / 2F;
                float y = marginTop;

                _mobileMainHudMessageBarFallbackBg = new GGraph { name = "__codex_mobile_msgbar_bg", touchable = false };
                _mobileMainHudMessageBarFallbackBg.DrawRoundRect(w, 54F, new Color(0, 0, 0, 140), new[] { 10F, 10F, 10F, 10F });
                _mobileMainHudMessageBarFallbackBg.SetPosition(x, y);

                _mobileMainHudMessageBarFallback = new GTextField
                {
                    name = "__codex_mobile_msgbar_text",
                    touchable = false,
                    text = string.Empty,
                };

                try
                {
                    _mobileMainHudMessageBarFallback.textFormat.size = 22;
                    _mobileMainHudMessageBarFallback.textFormat.color = Color.White;
                    _mobileMainHudMessageBarFallback.textFormat.bold = true;
                    _mobileMainHudMessageBarFallback.autoSize = AutoSizeType.Height;
                }
                catch
                {
                }

                try
                {
                    _mobileMainHudMessageBarFallback.stroke = 1;
                    _mobileMainHudMessageBarFallback.strokeColor = new Color(0, 0, 0, 200);
                }
                catch
                {
                }

                _mobileMainHudMessageBarFallback.SetSize(w - 18F, 54F);
                _mobileMainHudMessageBarFallback.SetPosition(x + 9F, y + 7F);

                _mobileMainHud.AddChild(_mobileMainHudMessageBarFallbackBg);
                _mobileMainHud.AddChild(_mobileMainHudMessageBarFallback);

                // 保证在最上层
                try
                {
                    _mobileMainHud.SetChildIndex(_mobileMainHudMessageBarFallbackBg, _mobileMainHud.numChildren - 1);
                    _mobileMainHud.SetChildIndex(_mobileMainHudMessageBarFallback, _mobileMainHud.numChildren - 1);
                }
                catch
                {
                }

                return _mobileMainHudMessageBarFallback;
            }
            catch
            {
                _mobileMainHudMessageBarFallback = null;
                _mobileMainHudMessageBarFallbackBg = null;
                return null;
            }
        }

        public static void MarkMobileMainHudMessageBarDirty()
        {
            _mobileMainHudMessageBarDirty = true;
            TryRefreshMobileMainHudMessageBarIfDue(force: false);
        }

        private static int ScoreMobileMainHudMessageBarCandidate(GObject obj, string[] keywords)
        {
            int score = ScoreAnyField(obj, keywords, equalsWeight: 180, startsWithWeight: 90, containsWeight: 40);
            score += ScoreRect(obj, preferLower: true, areaDivisor: 1100, maxAreaScore: 200);

            if (obj is GRichTextField)
                score += 10;
            if (obj.packageItem?.exported == true)
                score += 5;

            // 消息栏通常较宽
            try
            {
                float screenW = Math.Max(1, Settings.ScreenWidth);
                if (obj.width >= screenW * 0.35f)
                    score += 15;
            }
            catch
            {
            }

            return score;
        }

        private static void TryBindMobileMainHudMessageBarIfDue()
        {
            if (_mobileMainHud == null || _mobileMainHud._disposed)
                return;

            if (_mobileMainHudMessageBar != null && !_mobileMainHudMessageBar._disposed)
                return;

            if (DateTime.UtcNow < _nextMobileMainHudMessageBarBindAttemptUtc)
                return;

            _nextMobileMainHudMessageBarBindAttemptUtc = DateTime.UtcNow.AddSeconds(2);

            try
            {
                // 优先：把系统消息输出到 BottomUI/DChatWindow
                GTextField chatWindowText = TryResolveMobileMainHudChatWindowText(out string chatResolveInfo);
                if (chatWindowText != null && !chatWindowText._disposed)
                {
                    _mobileMainHudMessageBar = chatWindowText;
                    _mobileMainHudMessageBarResolveInfo = chatResolveInfo ?? DescribeObject(_mobileMainHud, chatWindowText);
                    _mobileMainHudMessageBarDirty = true;
                    _mobileMainHudMessageBarIsChatWindow = true;

                    if (!_mobileMainHudChatTextStyleApplied)
                    {
                        _mobileMainHudChatTextStyleApplied = true;
                        ApplyMobileChatTextStyle(_mobileMainHudMessageBar, isChatWindow: true);
                    }

                    if (Settings.LogErrors)
                        CMain.SaveLog("FairyGUI: 系统消息输出绑定到 BottomUI/DChatWindow：" + (_mobileMainHudMessageBarResolveInfo ?? "(null)"));
                    return;
                }

                List<(int Score, GObject Target)> candidates = CollectMobileChatCandidates(
                    _mobileMainHud,
                    obj => obj is GTextField && obj is not GTextInput,
                    DefaultMobileMainHudMessageBarKeywords,
                    ScoreMobileMainHudMessageBarCandidate);

                GTextField selected = SelectMobileChatCandidate<GTextField>(candidates, minScore: 40);
                if (selected == null || selected._disposed)
                {
                    // 兜底：如果 publish 中没有提供消息栏节点，则动态创建一个文本消息条，保证能看到服务端提示。
                    selected = TryEnsureMobileMainHudMessageBarFallback();
                    if (selected == null || selected._disposed)
                        return;
                    _mobileMainHudMessageBarResolveInfo = DescribeObject(_mobileMainHud, selected) + " (fallback)";
                }

                _mobileMainHudMessageBar = selected;
                if (string.IsNullOrWhiteSpace(_mobileMainHudMessageBarResolveInfo))
                    _mobileMainHudMessageBarResolveInfo = DescribeObject(_mobileMainHud, selected);
                _mobileMainHudMessageBarDirty = true;
                _mobileMainHudMessageBarIsChatWindow = false;

                if (!_mobileMainHudChatTextStyleApplied)
                {
                    _mobileMainHudChatTextStyleApplied = true;
                    ApplyMobileChatTextStyle(_mobileMainHudMessageBar, isChatWindow: false);
                }

                if (Settings.LogErrors)
                    CMain.SaveLog("FairyGUI: 主界面消息栏绑定完成：" + (_mobileMainHudMessageBarResolveInfo ?? "(null)"));
            }
            catch
            {
            }
        }

        private static void TryRefreshMobileMainHudMessageBarIfDue(bool force)
        {
            if (_stage == null || !_initialized || !_packagesLoaded)
                return;

            if (_mobileMainHud == null || _mobileMainHud._disposed)
            {
                if (_mobileMainHudMessageBar != null)
                    ResetMobileMainHudMessageBarBindings();
                return;
            }

            TryBindMobileMainHudMessageBarIfDue();

            if (_mobileMainHudMessageBar == null || _mobileMainHudMessageBar._disposed)
                return;

            if (!force && !_mobileMainHudMessageBarDirty && !_mobileChatDirty)
            {
                // 即使聊天内容不变，也定期强制回写字号，避免“下侧聊天框字体大小变来变去”。
                TryEnforceMobileMainHudChatTextStyleIfDue();
                return;
            }

            _mobileMainHudMessageBarDirty = false;

            try
            {
                int count = MobileChatEntries.Count;
                string text = _mobileMainHudMessageBarIsChatWindow
                    ? BuildMobileChatLogText(maxLines: 12)
                    : BuildMobileChatLogText(maxLines: 3);
                int hash = (count * 397) ^ text.GetHashCode();
                if (!force && hash == _mobileMainHudMessageBarLastHash)
                    return;

                _mobileMainHudMessageBarLastHash = hash;
                _mobileMainHudMessageBar.text = text;
                ApplyMobileChatTextStyle(_mobileMainHudMessageBar, _mobileMainHudMessageBarIsChatWindow);
            }
            catch
            {
            }
        }
    }
}
