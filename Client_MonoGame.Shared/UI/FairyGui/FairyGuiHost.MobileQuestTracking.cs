using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using FairyGUI;
using Microsoft.Xna.Framework;
using MonoShare.MirScenes;

namespace MonoShare
{
    internal static partial class FairyGuiHost
    {
        private const string MobileQuestTrackingTextConfigKey = "MobileQuestTracking.Text";

        private static readonly string[] DefaultQuestTrackingKeywords = { "任务追踪", "追踪", "任务", "quest", "track", "target", "目标", "task" };

        private static bool _mobileQuestTrackingDirty;
        private static DateTime _nextMobileQuestTrackingBindAttemptUtc = DateTime.MinValue;
        private static bool _mobileQuestTrackingBindingsDumped;

        private static string _mobileQuestTrackingOverrideSpec;
        private static string[] _mobileQuestTrackingOverrideKeywords;
        private static string _mobileQuestTrackingResolveInfo;

        private static GTextField _mobileQuestTrackingTextField;
        private static string _mobileQuestTrackingLastText;

        private static GComponent _mobileQuestTrackingFallbackRoot;
        private static GRichTextField _mobileQuestTrackingFallbackText;

        public static void MarkMobileQuestTrackingDirty()
        {
            try
            {
                _mobileQuestTrackingDirty = true;
            }
            catch
            {
            }

            TryRefreshMobileQuestTrackingIfDue(force: false);
        }

        private static void ResetMobileQuestTrackingBindings()
        {
            _mobileQuestTrackingDirty = false;
            _nextMobileQuestTrackingBindAttemptUtc = DateTime.MinValue;
            _mobileQuestTrackingBindingsDumped = false;

            _mobileQuestTrackingOverrideSpec = null;
            _mobileQuestTrackingOverrideKeywords = null;
            _mobileQuestTrackingResolveInfo = null;

            _mobileQuestTrackingTextField = null;
            _mobileQuestTrackingLastText = null;

            try
            {
                _mobileQuestTrackingFallbackRoot?.Dispose();
            }
            catch
            {
            }

            _mobileQuestTrackingFallbackRoot = null;
            _mobileQuestTrackingFallbackText = null;
        }

        private static void TryRefreshMobileQuestTrackingIfDue(bool force)
        {
            if (_stage == null || !_initialized || !_packagesLoaded)
                return;

            if (_mobileMainHud == null || _mobileMainHud._disposed || _mobileMainHudSafeAreaRoot == null || _mobileMainHudSafeAreaRoot._disposed)
            {
                if (_mobileQuestTrackingTextField != null || _mobileQuestTrackingFallbackRoot != null)
                    ResetMobileQuestTrackingBindings();
                return;
            }

            TryBindMobileQuestTrackingIfDue();

            GTextField field = _mobileQuestTrackingTextField;
            if (field == null || field._disposed)
                return;

            if (!force && !_mobileQuestTrackingDirty)
                return;

            string nextText = BuildMobileQuestTrackingText() ?? string.Empty;

            if (!force && string.Equals(nextText, _mobileQuestTrackingLastText ?? string.Empty, StringComparison.Ordinal))
            {
                _mobileQuestTrackingDirty = false;
                return;
            }

            try
            {
                field.text = nextText;
                field.visible = nextText.Length > 0;
            }
            catch
            {
            }

            _mobileQuestTrackingLastText = nextText;
            _mobileQuestTrackingDirty = false;
        }

        private static void TryBindMobileQuestTrackingIfDue()
        {
            if (_mobileMainHud == null || _mobileMainHud._disposed)
                return;

            if (_mobileQuestTrackingTextField != null && !_mobileQuestTrackingTextField._disposed)
                return;

            if (DateTime.UtcNow < _nextMobileQuestTrackingBindAttemptUtc)
                return;

            _nextMobileQuestTrackingBindAttemptUtc = DateTime.UtcNow.AddSeconds(2);

            string spec = string.Empty;
            try
            {
                InIReader reader = TryCreateConfigReader();
                if (reader != null)
                {
                    spec = reader.ReadString(FairyGuiConfigSectionName, MobileQuestTrackingTextConfigKey, string.Empty, writeWhenNull: false);
                }
            }
            catch
            {
                spec = string.Empty;
            }

            spec = spec?.Trim() ?? string.Empty;
            _mobileQuestTrackingOverrideSpec = spec;

            string[] keywordsUsed = DefaultQuestTrackingKeywords;
            string resolveInfo = null;
            GTextField selected = null;
            List<(int Score, GObject Target)> candidates = null;

            if (!string.IsNullOrWhiteSpace(spec))
            {
                if (TryResolveMobileMainHudObjectBySpec(_mobileMainHud, spec, out GObject resolved, out string[] overrideKeywords))
                {
                    if (resolved is GTextField resolvedText && !resolvedText._disposed)
                    {
                        selected = resolvedText;
                        resolveInfo = "override " + DescribeObject(_mobileMainHud, resolvedText);
                    }
                    else if (overrideKeywords != null && overrideKeywords.Length > 0)
                    {
                        keywordsUsed = overrideKeywords;
                        resolveInfo = "override keywords=" + string.Join("|", keywordsUsed);
                    }
                }
                else
                {
                    string[] splitKeywords = SplitKeywords(spec);
                    if (splitKeywords.Length > 0)
                    {
                        keywordsUsed = splitKeywords;
                        resolveInfo = "override keywords=" + string.Join("|", keywordsUsed);
                    }
                }
            }

            if (selected == null)
            {
                candidates = CollectMobileChatCandidates(_mobileMainHud, obj => obj is GTextField, keywordsUsed, ScoreMobileQuestTrackingCandidate);
                selected = SelectMobileChatCandidate<GTextField>(candidates, minScore: 30);
                resolveInfo = selected != null ? ("auto " + DescribeObject(_mobileMainHud, selected)) : "auto (miss)";
            }

            if (selected == null)
            {
                EnsureMobileQuestTrackingFallbackIfNeeded();
                if (_mobileQuestTrackingFallbackText != null && !_mobileQuestTrackingFallbackText._disposed)
                {
                    selected = _mobileQuestTrackingFallbackText;
                    resolveInfo = "fallback " + DescribeObject(_mobileMainHudSafeAreaRoot, selected);
                }
            }

            if (selected == null || selected._disposed)
                return;

            try
            {
                selected.touchable = false;
            }
            catch
            {
            }

            _mobileQuestTrackingTextField = selected;
            _mobileQuestTrackingOverrideKeywords = keywordsUsed;
            _mobileQuestTrackingResolveInfo = resolveInfo;

            TryDumpMobileQuestTrackingBindingReportIfDue(keywordsUsed, selected, candidates, resolveInfo);
        }

        private static int ScoreMobileQuestTrackingCandidate(GObject obj, string[] keywords)
        {
            int score = ScoreAnyField(obj, keywords, equalsWeight: 160, startsWithWeight: 80, containsWeight: 30);
            score += ScoreRect(obj, preferLower: false, areaDivisor: 900, maxAreaScore: 260);
            if (obj is GRichTextField)
                score += 25;
            if (obj.packageItem?.exported == true)
                score += 10;
            return score;
        }

        private static void EnsureMobileQuestTrackingFallbackIfNeeded()
        {
            if (_mobileMainHudSafeAreaRoot == null || _mobileMainHudSafeAreaRoot._disposed)
                return;

            if (_mobileQuestTrackingFallbackRoot != null && !_mobileQuestTrackingFallbackRoot._disposed &&
                _mobileQuestTrackingFallbackText != null && !_mobileQuestTrackingFallbackText._disposed)
                return;

            try
            {
                _mobileQuestTrackingFallbackRoot?.Dispose();
            }
            catch
            {
            }

            _mobileQuestTrackingFallbackRoot = null;
            _mobileQuestTrackingFallbackText = null;

            GComponent root = new GComponent
            {
                name = "MobileQuestTrackingFallbackRoot",
                opaque = false,
                touchable = false,
            };

            _mobileMainHudSafeAreaRoot.AddChild(root);
            root.AddRelation(_mobileMainHudSafeAreaRoot, RelationType.Size);

            GRichTextField text = new GRichTextField
            {
                name = "MobileQuestTrackingText",
                touchable = false,
            };

            text.singleLine = false;
            text.autoSize = AutoSizeType.Height;
            text.color = Color.White;
            text.stroke = 1;
            text.strokeColor = Color.Black;

            float safeW = _mobileMainHudSafeAreaRoot.width;
            float safeH = _mobileMainHudSafeAreaRoot.height;

            text.width = MathF.Max(220, safeW * 0.45f);
            text.SetPosition(12, MathF.Max(100, safeH * 0.18f));

            root.AddChild(text);

            _mobileQuestTrackingFallbackRoot = root;
            _mobileQuestTrackingFallbackText = text;
        }

        private static string BuildMobileQuestTrackingText()
        {
            try
            {
                int[] tracked = Settings.TrackedQuests;
                if (tracked == null || tracked.Length == 0)
                    return string.Empty;

                var user = GameScene.User;
                var quests = user?.CurrentQuests;
                if (quests == null || quests.Count == 0)
                    return string.Empty;

                var seen = new HashSet<int>();
                var lines = new List<string>(32);

                const int maxLines = 18;

                for (int i = 0; i < tracked.Length; i++)
                {
                    int index = tracked[i];
                    if (index <= 0)
                        continue;

                    if (!seen.Add(index))
                        continue;

                    ClientQuestProgress quest = null;
                    for (int q = 0; q < quests.Count; q++)
                    {
                        ClientQuestProgress candidate = quests[q];
                        if (candidate == null)
                            continue;

                        if (candidate.Id == index)
                        {
                            quest = candidate;
                            break;
                        }

                        int questIndex = candidate.QuestInfo?.Index ?? 0;
                        if (questIndex == index)
                        {
                            quest = candidate;
                            break;
                        }
                    }

                    if (quest == null)
                        continue;

                    string name = quest.QuestInfo?.Name;
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        int idx = quest.QuestInfo?.Index ?? quest.Id;
                        name = idx > 0 ? ("任务 " + idx) : "任务";
                    }

                    name = name.Trim();

                    if (quest.Completed)
                        name += "（可交付）";
                    else if (!quest.Taken)
                        name += "（可接）";

                    lines.Add(name);
                    if (lines.Count >= maxLines)
                        break;

                    if (quest.TaskList != null)
                    {
                        for (int t = 0; t < quest.TaskList.Count; t++)
                        {
                            string task = quest.TaskList[t];
                            if (string.IsNullOrWhiteSpace(task))
                                continue;

                            task = task.Replace("\r", string.Empty).Trim();
                            if (task.Length == 0)
                                continue;

                            lines.Add("  " + task);
                            if (lines.Count >= maxLines)
                                break;
                        }
                    }

                    if (lines.Count >= maxLines)
                        break;

                    lines.Add(string.Empty);
                    if (lines.Count >= maxLines)
                        break;
                }

                return string.Join("\n", lines).TrimEnd();
            }
            catch
            {
                return string.Empty;
            }
        }

        private static void TryDumpMobileQuestTrackingBindingReportIfDue(
            string[] keywords,
            GTextField selected,
            List<(int Score, GObject Target)> candidates,
            string resolveInfo)
        {
            if (!Settings.DebugMode)
                return;

            if (_mobileQuestTrackingBindingsDumped)
                return;

            if (_mobileMainHud == null || _mobileMainHud._disposed)
                return;

            try
            {
                Directory.CreateDirectory(ClientResourceLayout.RuntimeRoot);
                string path = Path.Combine(ClientResourceLayout.RuntimeRoot, "FairyGui-MobileQuestTrackingBindings.txt");

                string[] keywordList = keywords != null && keywords.Length > 0 ? keywords : DefaultQuestTrackingKeywords;

                if (candidates == null)
                    candidates = CollectMobileChatCandidates(_mobileMainHud, obj => obj is GTextField, keywordList, ScoreMobileQuestTrackingCandidate);

                candidates.Sort((a, b) => b.Score.CompareTo(a.Score));

                var builder = new StringBuilder(8 * 1024);
                builder.AppendLine("FairyGUI 移动端任务追踪绑定报告");
                builder.AppendLine($"GeneratedAtUtc={DateTime.UtcNow:o}");
                builder.AppendLine();
                builder.AppendLine("配置覆盖（可选）：Mir2Config.ini -> [FairyGUI]");
                builder.AppendLine($"  {MobileQuestTrackingTextConfigKey}=<Spec>");
                builder.AppendLine("  Spec 支持：path:... / idx:... / name:... / item:... / url:... / title:... / 或者关键字列表(a|b|c)");
                builder.AppendLine();
                builder.AppendLine($"OverrideSpec={(string.IsNullOrWhiteSpace(_mobileQuestTrackingOverrideSpec) ? "-" : _mobileQuestTrackingOverrideSpec)}");
                builder.AppendLine($"Keywords={(keywordList == null ? "-" : string.Join("|", keywordList))}");
                builder.AppendLine($"Resolved={(string.IsNullOrWhiteSpace(resolveInfo) ? "-" : resolveInfo)}");
                builder.AppendLine($"Selected={DescribeObject(_mobileMainHud, selected)}");
                builder.AppendLine();
                builder.AppendLine("Candidates(top):");

                int top = Math.Min(12, candidates.Count);
                for (int i = 0; i < top; i++)
                {
                    (int score, GObject target) = candidates[i];
                    builder.Append("  score=").Append(score).Append(' ');
                    builder.AppendLine(DescribeObject(_mobileMainHud, target));
                }

                File.WriteAllText(path, builder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                _mobileQuestTrackingBindingsDumped = true;
            }
            catch (Exception ex)
            {
                CMain.SaveError("FairyGUI: 导出任务追踪绑定报告失败：" + ex.Message);
            }
        }
    }
}
