using System;
using System.Collections.Generic;
using FairyGUI;

namespace MonoShare
{
    internal static partial class FairyGuiHost
    {
        private static readonly string[] DefaultMobileNoticeWindowKeywords = { "公告_DNoticeOKBoxUI", "DNoticeOKBox", "NoticeOKBox", "公告", "Notice" };
        private static readonly string[] DefaultMobileNoticeTitleKeywords = { "title", "标题", "公告标题", "notice", "公告" };
        private static readonly string[] DefaultMobileNoticeMessageKeywords = { "message", "msg", "content", "内容", "正文", "notice", "公告" };
        private static readonly string[] DefaultMobileNoticeOkButtonKeywords = { "ok", "确定", "确认", "知道了", "关闭", "返回", "sure" };

        private static global::Notice _mobileNoticeCurrent;
        private static bool _mobileNoticeDirty;

        private static GComponent _mobileNoticeWindow;
        private static string _mobileNoticeWindowResolveInfo;
        private static DateTime _nextMobileNoticeBindAttemptUtc = DateTime.MinValue;
        private static DateTime _nextMobileNoticeShowAttemptUtc = DateTime.MinValue;

        private static GTextField _mobileNoticeTitleField;
        private static GTextField _mobileNoticeMessageField;
        private static GButton _mobileNoticeOkButton;
        private static EventCallback0 _mobileNoticeOkCallback;

        public static void ShowMobileNotice(global::Notice notice)
        {
            if (notice == null)
                return;

            _mobileNoticeCurrent = notice;
            _mobileNoticeDirty = true;
            _nextMobileNoticeShowAttemptUtc = DateTime.MinValue;

            if (_stage == null || !_initialized || !_packagesLoaded)
                return;

            if (!TryShowMobileWindowByKeywords("Notice", DefaultMobileNoticeWindowKeywords))
                return;

            TryRefreshMobileNoticeIfDue(force: true);
        }

        private static void ResetMobileNoticeBindings()
        {
            try
            {
                if (_mobileNoticeOkButton != null && !_mobileNoticeOkButton._disposed && _mobileNoticeOkCallback != null)
                    _mobileNoticeOkButton.onClick.Remove(_mobileNoticeOkCallback);
            }
            catch
            {
            }

            _mobileNoticeWindow = null;
            _mobileNoticeWindowResolveInfo = null;
            _nextMobileNoticeBindAttemptUtc = DateTime.MinValue;
            _nextMobileNoticeShowAttemptUtc = DateTime.MinValue;

            _mobileNoticeTitleField = null;
            _mobileNoticeMessageField = null;
            _mobileNoticeOkButton = null;
            _mobileNoticeOkCallback = null;
        }

        private static void TryBindMobileNoticeWindowIfDue(string windowKey, GComponent window, string resolveInfo)
        {
            if (window == null || window._disposed)
                return;

            if (_mobileNoticeWindow != null && _mobileNoticeWindow._disposed)
                ResetMobileNoticeBindings();

            if (_mobileNoticeWindow == null || !ReferenceEquals(_mobileNoticeWindow, window))
            {
                ResetMobileNoticeBindings();
                _mobileNoticeWindow = window;
                _mobileNoticeWindowResolveInfo = resolveInfo;
            }

            if (DateTime.UtcNow < _nextMobileNoticeBindAttemptUtc)
                return;

            if (_mobileNoticeTitleField != null && !_mobileNoticeTitleField._disposed &&
                _mobileNoticeMessageField != null && !_mobileNoticeMessageField._disposed &&
                _mobileNoticeOkButton != null && !_mobileNoticeOkButton._disposed)
            {
                return;
            }

            _nextMobileNoticeBindAttemptUtc = DateTime.UtcNow.AddSeconds(2);

            if (_mobileNoticeTitleField == null || _mobileNoticeTitleField._disposed)
            {
                List<(int Score, GObject Target)> candidates =
                    CollectMobileChatCandidates(window, obj => obj is GTextField && obj is not GTextInput, DefaultMobileNoticeTitleKeywords, ScoreMobileNoticeTitleCandidate);
                _mobileNoticeTitleField = SelectMobileChatCandidate<GTextField>(candidates, minScore: 25);
            }

            if (_mobileNoticeMessageField == null || _mobileNoticeMessageField._disposed)
            {
                List<(int Score, GObject Target)> candidates =
                    CollectMobileChatCandidates(window, obj => obj is GTextField && obj is not GTextInput, DefaultMobileNoticeMessageKeywords, ScoreMobileNoticeMessageCandidate);
                _mobileNoticeMessageField = SelectMobileChatCandidate<GTextField>(candidates, minScore: 25);

                // 兜底：取最大文本框作为正文
                if (_mobileNoticeMessageField == null)
                {
                    var allText = new List<(int Score, GObject Target)>();
                    foreach (GObject obj in Enumerate(window))
                    {
                        if (obj == null || obj._disposed)
                            continue;
                        if (ReferenceEquals(obj, window))
                            continue;
                        if (obj is not GTextField tf || tf is GTextInput)
                            continue;

                        int score = 0;
                        try
                        {
                            float area = Math.Max(0F, obj.width) * Math.Max(0F, obj.height);
                            score = (int)Math.Min(int.MaxValue, area / 10F);
                        }
                        catch
                        {
                            score = 0;
                        }

                        allText.Add((score, obj));
                    }

                    allText.Sort((a, b) => b.Score.CompareTo(a.Score));
                    _mobileNoticeMessageField = SelectMobileChatCandidate<GTextField>(allText, minScore: 1);
                }
            }

            if (_mobileNoticeOkButton == null || _mobileNoticeOkButton._disposed)
            {
                List<(int Score, GObject Target)> candidates =
                    CollectMobileChatCandidates(window, obj => obj is GButton, DefaultMobileNoticeOkButtonKeywords, ScoreMobileNoticeOkButtonCandidate);
                _mobileNoticeOkButton = SelectMobileChatCandidate<GButton>(candidates, minScore: 15);

                if (_mobileNoticeOkButton != null && !_mobileNoticeOkButton._disposed)
                {
                    try
                    {
                        _mobileNoticeOkButton.changeStateOnClick = false;
                        _mobileNoticeOkButton.enabled = true;
                        _mobileNoticeOkButton.grayed = false;
                        _mobileNoticeOkButton.touchable = true;
                    }
                    catch
                    {
                    }

                    _mobileNoticeOkCallback = () =>
                    {
                        try
                        {
                            TryHideMobileWindow(windowKey);
                        }
                        catch
                        {
                        }
                    };

                    try
                    {
                        _mobileNoticeOkButton.onClick.Add(_mobileNoticeOkCallback);
                    }
                    catch
                    {
                    }
                }
            }

            if (Settings.LogErrors)
            {
                try
                {
                    string titleDesc = _mobileNoticeTitleField != null && !_mobileNoticeTitleField._disposed ? DescribeObject(window, _mobileNoticeTitleField) : "(null)";
                    string msgDesc = _mobileNoticeMessageField != null && !_mobileNoticeMessageField._disposed ? DescribeObject(window, _mobileNoticeMessageField) : "(null)";
                    string okDesc = _mobileNoticeOkButton != null && !_mobileNoticeOkButton._disposed ? DescribeObject(window, _mobileNoticeOkButton) : "(null)";
                    CMain.SaveLog($"FairyGUI: 公告窗体绑定：Title={titleDesc} Message={msgDesc} Ok={okDesc} Resolve={_mobileNoticeWindowResolveInfo ?? resolveInfo ?? "(null)"}");
                }
                catch
                {
                }
            }
        }

        private static void TryRefreshMobileNoticeIfDue(bool force)
        {
            if (!force && !_mobileNoticeDirty)
                return;

            if (_mobileNoticeCurrent == null)
                return;

            if (_stage == null || !_initialized || !_packagesLoaded)
                return;

            GComponent window = null;
            try
            {
                MobileWindows.TryGetValue("Notice", out window);
            }
            catch
            {
                window = null;
            }

            // 公告包可能在进入地图早期就到达，此时 FairyGUI 还没加载完成；这里做一次延迟重试。
            if (window == null || window._disposed)
            {
                if (DateTime.UtcNow < _nextMobileNoticeShowAttemptUtc)
                    return;

                _nextMobileNoticeShowAttemptUtc = DateTime.UtcNow.AddSeconds(1.5);

                if (!TryShowMobileWindowByKeywords("Notice", DefaultMobileNoticeWindowKeywords))
                    return;
            }

            try
            {
                MobileWindows.TryGetValue("Notice", out window);
            }
            catch
            {
                window = null;
            }

            if (window == null || window._disposed)
                return;

            TryBindMobileNoticeWindowIfDue("Notice", window, resolveInfo: null);

            if ((_mobileNoticeTitleField == null || _mobileNoticeTitleField._disposed) &&
                (_mobileNoticeMessageField == null || _mobileNoticeMessageField._disposed))
            {
                return;
            }

            _mobileNoticeDirty = false;

            string title = _mobileNoticeCurrent.Title ?? string.Empty;
            string message = _mobileNoticeCurrent.Message ?? string.Empty;

            try
            {
                if (_mobileNoticeTitleField != null && !_mobileNoticeTitleField._disposed)
                    _mobileNoticeTitleField.text = title;
            }
            catch
            {
            }

            try
            {
                if (_mobileNoticeMessageField != null && !_mobileNoticeMessageField._disposed)
                {
                    if (string.IsNullOrWhiteSpace(title) || ReferenceEquals(_mobileNoticeMessageField, _mobileNoticeTitleField))
                        _mobileNoticeMessageField.text = message;
                    else if (string.IsNullOrWhiteSpace(message))
                        _mobileNoticeMessageField.text = title;
                    else
                        _mobileNoticeMessageField.text = title + "\n" + message;
                }
            }
            catch
            {
            }
        }

        private static int ScoreMobileNoticeTitleCandidate(GObject obj, string[] keywords)
        {
            int score = ScoreAnyField(obj, keywords, equalsWeight: 160, startsWithWeight: 80, containsWeight: 35);
            score += ScoreRect(obj, preferLower: false, areaDivisor: 1600, maxAreaScore: 120);

            // 标题通常偏上且面积更小
            try
            {
                float area = Math.Max(0F, obj.width) * Math.Max(0F, obj.height);
                if (area > 240000F)
                    score -= 30;
            }
            catch
            {
            }

            if (obj.packageItem?.exported == true)
                score += 5;

            return score;
        }

        private static int ScoreMobileNoticeMessageCandidate(GObject obj, string[] keywords)
        {
            int score = ScoreAnyField(obj, keywords, equalsWeight: 120, startsWithWeight: 60, containsWeight: 25);
            score += ScoreRect(obj, preferLower: false, areaDivisor: 700, maxAreaScore: 200);

            if (obj.packageItem?.exported == true)
                score += 5;

            return score;
        }

        private static int ScoreMobileNoticeOkButtonCandidate(GObject obj, string[] keywords)
        {
            int score = ScoreAnyField(obj, keywords, equalsWeight: 180, startsWithWeight: 90, containsWeight: 35);
            score += ScoreRect(obj, preferLower: true, areaDivisor: 1500, maxAreaScore: 140);

            if (obj.packageItem?.exported == true)
                score += 10;

            return score;
        }
    }
}
