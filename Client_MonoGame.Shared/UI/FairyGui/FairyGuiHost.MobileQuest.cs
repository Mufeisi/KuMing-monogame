using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using C = ClientPackets;
using FairyGUI;
using Microsoft.Xna.Framework;
using MonoShare.MirNetwork;
using MonoShare.MirObjects;
using MonoShare.MirScenes;

namespace MonoShare
{
    internal static partial class FairyGuiHost
    {
        private const string MobileQuestListConfigKey = "MobileQuest.List";
        private const string MobileQuestTitleConfigKey = "MobileQuest.Title";
        private const string MobileQuestContentConfigKey = "MobileQuest.Content";
        private const string MobileQuestAcceptConfigKey = "MobileQuest.Accept";
        private const string MobileQuestFinishConfigKey = "MobileQuest.Finish";
        private const string MobileQuestAbandonConfigKey = "MobileQuest.Abandon";
        private const string MobileQuestShareConfigKey = "MobileQuest.Share";
        private const string MobileQuestTrackConfigKey = "MobileQuest.Track";
        private const string MobileActivityFallbackName = "__codex_mobile_activity_fallback";

        private static readonly string[] DefaultQuestListKeywords = { "DA2EGrid3", "任务_DA2EWindow1UI", "任务", "quest", "diary", "log", "list" };
        private static readonly string[] DefaultQuestTitleKeywords = { "任务", "quest", "title", "name", "标题", "名称" };
        private static readonly string[] DefaultQuestContentKeywords = { "DMissionDesc", "任务", "quest", "content", "desc", "detail", "text", "内容", "说明", "目标", "进度" };
        private static readonly string[] DefaultQuestAcceptKeywords = { "accept", "take", "接取", "接受", "领取" };
        private static readonly string[] DefaultQuestFinishKeywords = { "finish", "complete", "交付", "提交", "完成", "领奖" };
        private static readonly string[] DefaultQuestAbandonKeywords = { "abandon", "giveup", "drop", "放弃", "取消" };
        private static readonly string[] DefaultQuestShareKeywords = { "share", "共享", "分享" };
        private static readonly string[] DefaultQuestTrackKeywords = { "track", "pin", "追踪", "标记" };

        private sealed class MobileQuestItemView
        {
            public GComponent Root;
            public GTextField Label;
            public EventCallback0 Click;
            public float OriginalAlpha;
            public bool OriginalAlphaCaptured;
        }

        private sealed class MobileQuestWindowBinding
        {
            public string WindowKey;
            public GComponent Window;
            public string ResolveInfo;

            public GList List;
            public string ListResolveInfo;
            public string ListOverrideSpec;
            public string[] ListOverrideKeywords;
            public ListItemRenderer Renderer;
            public readonly List<GButton> FallbackListRows = new List<GButton>();

            public GTextField Title;
            public string TitleResolveInfo;
            public string TitleOverrideSpec;
            public string[] TitleOverrideKeywords;

            public GTextField Content;
            public string ContentResolveInfo;
            public string ContentOverrideSpec;
            public string[] ContentOverrideKeywords;

            public GButton Accept;
            public string AcceptResolveInfo;
            public string AcceptOverrideSpec;
            public string[] AcceptOverrideKeywords;
            public EventCallback0 AcceptClick;

            public GButton Finish;
            public string FinishResolveInfo;
            public string FinishOverrideSpec;
            public string[] FinishOverrideKeywords;
            public EventCallback0 FinishClick;

            public GButton Abandon;
            public string AbandonResolveInfo;
            public string AbandonOverrideSpec;
            public string[] AbandonOverrideKeywords;
            public EventCallback0 AbandonClick;

            public GButton Share;
            public string ShareResolveInfo;
            public string ShareOverrideSpec;
            public string[] ShareOverrideKeywords;
            public EventCallback0 ShareClick;

            public GButton Track;
            public string TrackResolveInfo;
            public string TrackOverrideSpec;
            public string[] TrackOverrideKeywords;
            public EventCallback0 TrackClick;

            public GComponent FallbackActionBar;
            public readonly List<GButton> FallbackActionButtons = new List<GButton>();
            public GComponent RewardSelectionBar;
            public readonly List<GButton> RewardSelectionButtons = new List<GButton>();
            public readonly List<EventCallback0> RewardSelectionClicks = new List<EventCallback0>();
        }

        private static MobileQuestWindowBinding _mobileQuestBinding;
        private static DateTime _nextMobileQuestBindAttemptUtc = DateTime.MinValue;
        private static bool _mobileQuestBindingsDumped;
        private static bool _mobileQuestDirty;
        private static bool _mobileQuestWindowWasVisible;

        private static readonly MobileQuestContextState _mobileQuestContext = new MobileQuestContextState();

        public static void UpdateMobileQuestContext(uint npcObjectId, string npcName)
        {
            _mobileQuestContext.EnterNpc(npcObjectId, npcName);
            MarkMobileQuestDirty();
        }

        public static void UpdateMobileActivityContext()
        {
            _mobileQuestContext.EnterActivity();
            MarkMobileActivityDirty();
        }

        public static void MarkMobileActivityDirty()
        {
            MarkMobileQuestDirty();
        }

        public static void BeginMobileQuestDetail(ClientQuestProgress quest)
        {
            int questIndex = 0;
            try { questIndex = quest?.QuestInfo?.Index ?? quest?.Id ?? 0; } catch { questIndex = 0; }

            _mobileQuestContext.Select(questIndex);

            MarkMobileQuestDirty();
        }

        public static void MarkMobileQuestDirty()
        {
            try { _mobileQuestDirty = true; } catch { }
            TryRefreshMobileQuestIfDue(force: false);
        }

        private static void ResetMobileQuestBindings()
        {
            ResetMobileQuestBindings(clearContext: true);
        }

        private static void ResetMobileQuestBindings(bool clearContext)
        {
            try
            {
                MobileQuestWindowBinding binding = _mobileQuestBinding;
                if (binding != null)
                {
                    try { if (binding.Accept != null && binding.AcceptClick != null) binding.Accept.onClick.Remove(binding.AcceptClick); } catch { }
                    try { if (binding.Finish != null && binding.FinishClick != null) binding.Finish.onClick.Remove(binding.FinishClick); } catch { }
                    try { if (binding.Abandon != null && binding.AbandonClick != null) binding.Abandon.onClick.Remove(binding.AbandonClick); } catch { }
                    try { if (binding.Share != null && binding.ShareClick != null) binding.Share.onClick.Remove(binding.ShareClick); } catch { }
                    try { if (binding.Track != null && binding.TrackClick != null) binding.Track.onClick.Remove(binding.TrackClick); } catch { }
                    try { if (binding.List != null && !binding.List._disposed) binding.List.itemRenderer = null; } catch { }
                    try
                    {
                        for (int i = 0; i < binding.FallbackListRows.Count; i++)
                        {
                            GButton row = binding.FallbackListRows[i];
                            if (row?.data is MobileQuestItemView view && view.Click != null)
                                row.onClick.Remove(view.Click);
                        }
                    }
                    catch { }
                    try
                    {
                        for (int i = 0; i < binding.RewardSelectionButtons.Count; i++)
                        {
                            GButton button = binding.RewardSelectionButtons[i];
                            EventCallback0 click = i < binding.RewardSelectionClicks.Count ? binding.RewardSelectionClicks[i] : null;
                            if (button != null && click != null)
                                button.onClick.Remove(click);
                        }
                    }
                    catch { }

                    try
                    {
                        if (binding.RewardSelectionBar != null && binding.RewardSelectionBar.parent != null)
                            binding.RewardSelectionBar.parent.RemoveChild(binding.RewardSelectionBar, dispose: true);
                        else
                            binding.RewardSelectionBar?.Dispose();
                    }
                    catch { }
                    try
                    {
                        if (binding.FallbackActionBar != null && binding.FallbackActionBar.parent != null)
                            binding.FallbackActionBar.parent.RemoveChild(binding.FallbackActionBar, dispose: true);
                        else
                            binding.FallbackActionBar?.Dispose();
                    }
                    catch { }
                    binding.RewardSelectionButtons.Clear();
                    binding.RewardSelectionClicks.Clear();
                    binding.FallbackActionButtons.Clear();
                }
            }
            catch
            {
            }

            _mobileQuestBinding = null;
            _nextMobileQuestBindAttemptUtc = DateTime.MinValue;
            _mobileQuestBindingsDumped = false;
            _mobileQuestDirty = true;
            _mobileQuestWindowWasVisible = false;
            if (clearContext)
            {
                _mobileQuestContext.ResetForClose();
                GameScene.MobileActivityRewardSelection.Clear();
            }
            else
                _mobileQuestContext.ResetForRebind();
        }

        private static void TryBindMobileQuestWindowIfDue(string windowKey, GComponent window, string resolveInfo)
        {
            if (window == null || window._disposed)
                return;

            if (_mobileQuestBinding != null && _mobileQuestBinding.Window != null && _mobileQuestBinding.Window._disposed)
                ResetMobileQuestBindings(clearContext: false);

            if (_mobileQuestBinding == null || _mobileQuestBinding.Window == null || _mobileQuestBinding.Window._disposed || !ReferenceEquals(_mobileQuestBinding.Window, window))
            {
                ResetMobileQuestBindings(clearContext: false);
                _mobileQuestBinding = new MobileQuestWindowBinding
                {
                    WindowKey = windowKey,
                    Window = window,
                    ResolveInfo = resolveInfo,
                };

                if (string.Equals(window.name, MobileActivityFallbackName, StringComparison.Ordinal))
                {
                    try { _mobileQuestBinding.Title = window.GetChild("activity_title") as GTextField; } catch { }
                    try { _mobileQuestBinding.Content = window.GetChild("activity_content") as GTextField; } catch { }
                    for (int i = 0; i < 6; i++)
                    {
                        try
                        {
                            if (window.GetChild("activity_row_" + i) is GButton row && !row._disposed)
                                _mobileQuestBinding.FallbackListRows.Add(row);
                        }
                        catch { }
                    }
                }
            }

            if (DateTime.UtcNow < _nextMobileQuestBindAttemptUtc)
                return;

            MobileQuestWindowBinding binding = _mobileQuestBinding;
            if (binding == null)
                return;

            bool listBound = (binding.List != null && !binding.List._disposed) || binding.FallbackListRows.Count > 0;
            if (listBound && binding.Accept != null && !binding.Accept._disposed && binding.Finish != null && !binding.Finish._disposed)
                return;

            _nextMobileQuestBindAttemptUtc = DateTime.UtcNow.AddSeconds(2);

            string listSpec = string.Empty;
            string titleSpec = string.Empty;
            string contentSpec = string.Empty;
            string acceptSpec = string.Empty;
            string finishSpec = string.Empty;
            string abandonSpec = string.Empty;
            string shareSpec = string.Empty;
            string trackSpec = string.Empty;

            try
            {
                InIReader reader = TryCreateConfigReader();
                if (reader != null)
                {
                    listSpec = reader.ReadString(FairyGuiConfigSectionName, MobileQuestListConfigKey, string.Empty, writeWhenNull: false);
                    titleSpec = reader.ReadString(FairyGuiConfigSectionName, MobileQuestTitleConfigKey, string.Empty, writeWhenNull: false);
                    contentSpec = reader.ReadString(FairyGuiConfigSectionName, MobileQuestContentConfigKey, string.Empty, writeWhenNull: false);
                    acceptSpec = reader.ReadString(FairyGuiConfigSectionName, MobileQuestAcceptConfigKey, string.Empty, writeWhenNull: false);
                    finishSpec = reader.ReadString(FairyGuiConfigSectionName, MobileQuestFinishConfigKey, string.Empty, writeWhenNull: false);
                    abandonSpec = reader.ReadString(FairyGuiConfigSectionName, MobileQuestAbandonConfigKey, string.Empty, writeWhenNull: false);
                    shareSpec = reader.ReadString(FairyGuiConfigSectionName, MobileQuestShareConfigKey, string.Empty, writeWhenNull: false);
                    trackSpec = reader.ReadString(FairyGuiConfigSectionName, MobileQuestTrackConfigKey, string.Empty, writeWhenNull: false);
                }
            }
            catch
            {
                listSpec = string.Empty;
                titleSpec = string.Empty;
                contentSpec = string.Empty;
                acceptSpec = string.Empty;
                finishSpec = string.Empty;
                abandonSpec = string.Empty;
                shareSpec = string.Empty;
                trackSpec = string.Empty;
            }

            listSpec = listSpec?.Trim() ?? string.Empty;
            titleSpec = titleSpec?.Trim() ?? string.Empty;
            contentSpec = contentSpec?.Trim() ?? string.Empty;
            acceptSpec = acceptSpec?.Trim() ?? string.Empty;
            finishSpec = finishSpec?.Trim() ?? string.Empty;
            abandonSpec = abandonSpec?.Trim() ?? string.Empty;
            shareSpec = shareSpec?.Trim() ?? string.Empty;
            trackSpec = trackSpec?.Trim() ?? string.Empty;

            binding.ListOverrideSpec = listSpec;
            binding.TitleOverrideSpec = titleSpec;
            binding.ContentOverrideSpec = contentSpec;
            binding.AcceptOverrideSpec = acceptSpec;
            binding.FinishOverrideSpec = finishSpec;
            binding.AbandonOverrideSpec = abandonSpec;
            binding.ShareOverrideSpec = shareSpec;
            binding.TrackOverrideSpec = trackSpec;

            var usedButtonTargets = new HashSet<GObject>();

            // List
            if ((binding.List == null || binding.List._disposed) && binding.FallbackListRows.Count == 0)
            {
                string[] keywordsUsed = DefaultQuestListKeywords;
                GList list = null;

                if (!string.IsNullOrWhiteSpace(listSpec))
                {
                    if (TryResolveMobileMainHudObjectBySpec(window, listSpec, out GObject resolved, out string[] overrideKeywords))
                    {
                        if (resolved is GList resolvedList && !resolvedList._disposed)
                        {
                            list = resolvedList;
                            binding.ListResolveInfo = "override " + DescribeObject(window, resolved);
                        }
                        else if (overrideKeywords != null && overrideKeywords.Length > 0)
                        {
                            keywordsUsed = overrideKeywords;
                            binding.ListResolveInfo = "override keywords=" + string.Join("|", keywordsUsed);
                        }
                    }
                }

                if (list == null)
                {
                    List<(int Score, GObject Target)> candidates = CollectMobileChatCandidates(window, obj => obj is GList, keywordsUsed, ScoreMobileShopListCandidate);
                    list = SelectMobileChatCandidate<GList>(candidates, minScore: 10);
                    binding.ListResolveInfo = list != null ? "auto " + DescribeObject(window, list) : "auto (miss)";
                }

                binding.List = list;
                binding.ListOverrideKeywords = keywordsUsed;
            }

            // Text
            BindMailText(window, ref binding.Title, ref binding.TitleResolveInfo, titleSpec, DefaultQuestTitleKeywords, out binding.TitleOverrideKeywords);
            BindMailText(window, ref binding.Content, ref binding.ContentResolveInfo, contentSpec, DefaultQuestContentKeywords, out binding.ContentOverrideKeywords);

            // Buttons
            BindQuestButton(window, ref binding.Accept, ref binding.AcceptResolveInfo, acceptSpec, DefaultQuestAcceptKeywords, out binding.AcceptOverrideKeywords, usedButtonTargets);
            BindQuestButton(window, ref binding.Finish, ref binding.FinishResolveInfo, finishSpec, DefaultQuestFinishKeywords, out binding.FinishOverrideKeywords, usedButtonTargets);
            BindQuestButton(window, ref binding.Abandon, ref binding.AbandonResolveInfo, abandonSpec, DefaultQuestAbandonKeywords, out binding.AbandonOverrideKeywords, usedButtonTargets);
            BindQuestButton(window, ref binding.Share, ref binding.ShareResolveInfo, shareSpec, DefaultQuestShareKeywords, out binding.ShareOverrideKeywords, usedButtonTargets);
            BindQuestButton(window, ref binding.Track, ref binding.TrackResolveInfo, trackSpec, DefaultQuestTrackKeywords, out binding.TrackOverrideKeywords, usedButtonTargets);
            EnsureMobileQuestActionFallback(binding);

            // Callbacks
            AttachMobileQuestActionCallbacks(binding);

            TryDumpMobileQuestBindingsIfDue(binding);
        }

        private static void BindQuestButton(
            GComponent window,
            ref GButton target,
            ref string resolveInfo,
            string overrideSpec,
            string[] defaultKeywords,
            out string[] usedKeywords,
            HashSet<GObject> usedTargets)
        {
            usedKeywords = defaultKeywords;

            if (target != null && !target._disposed && (usedTargets == null || usedTargets.Add(target)))
                return;

            target = null;
            GButton button = null;

            if (!string.IsNullOrWhiteSpace(overrideSpec) &&
                TryResolveMobileMainHudObjectBySpec(window, overrideSpec, out GObject resolved, out string[] overrideKeywords))
            {
                if (resolved is GButton resolvedButton && !resolvedButton._disposed &&
                    (usedTargets == null || !usedTargets.Contains(resolvedButton)))
                {
                    button = resolvedButton;
                    resolveInfo = "override " + DescribeObject(window, resolved);
                }
                else if (overrideKeywords != null && overrideKeywords.Length > 0)
                {
                    usedKeywords = overrideKeywords;
                    resolveInfo = "override keywords=" + string.Join("|", usedKeywords);
                }
            }

            if (button == null)
            {
                string[] candidateKeywords = usedKeywords;
                List<(int Score, GObject Target)> candidates = CollectMobileChatCandidates(
                    window,
                    obj => obj is GButton buttonCandidate && buttonCandidate.touchable && HasQuestButtonKeyword(buttonCandidate, candidateKeywords),
                    candidateKeywords,
                    ScoreMobileQuestButtonCandidate);

                for (int i = 0; i < candidates.Count; i++)
                {
                    if (candidates[i].Score < 25)
                        break;

                    if (candidates[i].Target is GButton candidate &&
                        (usedTargets == null || !usedTargets.Contains(candidate)))
                    {
                        button = candidate;
                        break;
                    }
                }

                resolveInfo = button != null ? "auto " + DescribeObject(window, button) : "auto (miss: no reliable keyword target)";
            }

            if (button != null && (usedTargets == null || usedTargets.Add(button)))
                target = button;
        }

        private static bool HasQuestButtonKeyword(GButton button, string[] keywords)
        {
            if (button == null)
                return false;

            return MobileQuestBindingPolicy.HasKeyword(button.name, button.title, keywords);
        }

        private static int ScoreMobileQuestButtonCandidate(GObject obj, string[] keywords)
        {
            if (obj is not GButton button || !HasQuestButtonKeyword(button, keywords))
                return 0;

            return ScoreMobileShopButtonCandidate(obj, keywords);
        }

        /// <summary>
        /// 任务包的真实窗口只有分类/关闭/列表控件时，不能把这些控件误当成操作按钮。
        /// 缺少可靠目标时在任务窗口内部创建明确的操作栏，并沿用同一组回调。
        /// </summary>
        private static void EnsureMobileQuestActionFallback(MobileQuestWindowBinding binding)
        {
            if (binding == null || binding.Window == null || binding.Window._disposed)
                return;

            // 普通 NPC 任务保留原有真实 FUI 语义；ANDROID-07 动态栏只服务活动/赏金上下文。
            if (!_mobileQuestContext.IsActivityMode)
                return;

            int reliableOperationCount = 0;
            if (binding.Accept != null && !binding.Accept._disposed) reliableOperationCount++;
            if (binding.Finish != null && !binding.Finish._disposed) reliableOperationCount++;
            if (binding.Abandon != null && !binding.Abandon._disposed) reliableOperationCount++;
            if (binding.Share != null && !binding.Share._disposed) reliableOperationCount++;

            if (!MobileQuestBindingPolicy.ShouldCreateFallback(_mobileQuestContext.IsActivityMode, reliableOperationCount))
                return;

            if (binding.FallbackActionBar != null && !binding.FallbackActionBar._disposed)
                return;

            binding.FallbackActionButtons.Clear();
            var bar = new GComponent
            {
                name = "__codex_mobile_quest_action_bar",
                // 父容器必须参与命中测试才能继续命中子按钮；透明父层不拦截空白区域。
                touchable = MobileQuestDynamicBarPolicy.ParentTouchable,
                opaque = MobileQuestDynamicBarPolicy.ParentOpaque,
            };
            binding.Window.AddChild(bar);
            binding.FallbackActionBar = bar;

            if (binding.Accept == null || binding.Accept._disposed)
            {
                binding.Accept = CreateMobileQuestActionButton(bar, "__codex_mobile_quest_action_accept", "接取");
                if (binding.Accept != null) binding.FallbackActionButtons.Add(binding.Accept);
            }
            if (binding.Finish == null || binding.Finish._disposed)
            {
                binding.Finish = CreateMobileQuestActionButton(bar, "__codex_mobile_quest_action_finish", "交付");
                if (binding.Finish != null) binding.FallbackActionButtons.Add(binding.Finish);
            }
            if (binding.Abandon == null || binding.Abandon._disposed)
            {
                binding.Abandon = CreateMobileQuestActionButton(bar, "__codex_mobile_quest_action_abandon", "放弃");
                if (binding.Abandon != null) binding.FallbackActionButtons.Add(binding.Abandon);
            }
            if (binding.Share == null || binding.Share._disposed)
            {
                binding.Share = CreateMobileQuestActionButton(bar, "__codex_mobile_quest_action_share", "分享");
                if (binding.Share != null) binding.FallbackActionButtons.Add(binding.Share);
            }
            if (binding.Track == null || binding.Track._disposed)
            {
                binding.Track = CreateMobileQuestActionButton(bar, "__codex_mobile_quest_action_track", "追踪");
                if (binding.Track != null) binding.FallbackActionButtons.Add(binding.Track);
            }
        }

        private static GButton CreateMobileQuestActionButton(GComponent bar, string name, string title)
        {
            if (bar == null || bar._disposed)
                return null;

            try
            {
                var button = new GButton
                {
                    name = name,
                    title = title,
                    touchable = true,
                    enabled = true,
                    grayed = false,
                    opaque = true,
                    changeStateOnClick = false,
                };

                var background = new GGraph
                {
                    name = "background",
                    touchable = false,
                };
                background.DrawRect(1F, 1F, 2, new Color(120, 150, 195, 255), new Color(40, 65, 100, 245));
                button.AddChild(background);

                var label = new GTextField
                {
                    name = "title",
                    text = title,
                    touchable = false,
                    align = AlignType.Center,
                    verticalAlign = VertAlignType.Middle,
                    autoSize = AutoSizeType.None,
                };
                try
                {
                    label.textFormat.size = 16;
                    label.textFormat.color = Color.White;
                    label.textFormat.bold = true;
                }
                catch
                {
                }
                button.AddChild(label);
                bar.AddChild(button);
                return button;
            }
            catch
            {
                return null;
            }
        }

        private static void AttachMobileQuestActionCallbacks(MobileQuestWindowBinding binding)
        {
            if (binding == null)
                return;

            try
            {
                if (binding.Accept != null && !binding.Accept._disposed && binding.AcceptClick == null)
                {
                    binding.AcceptClick = OnMobileQuestAcceptClicked;
                    binding.Accept.onClick.Add(binding.AcceptClick);
                }

                if (binding.Finish != null && !binding.Finish._disposed && binding.FinishClick == null)
                {
                    binding.FinishClick = OnMobileQuestFinishClicked;
                    binding.Finish.onClick.Add(binding.FinishClick);
                }

                if (binding.Abandon != null && !binding.Abandon._disposed && binding.AbandonClick == null)
                {
                    binding.AbandonClick = OnMobileQuestAbandonClicked;
                    binding.Abandon.onClick.Add(binding.AbandonClick);
                }

                if (binding.Share != null && !binding.Share._disposed && binding.ShareClick == null)
                {
                    binding.ShareClick = OnMobileQuestShareClicked;
                    binding.Share.onClick.Add(binding.ShareClick);
                }

                if (binding.Track != null && !binding.Track._disposed && binding.TrackClick == null)
                {
                    binding.TrackClick = OnMobileQuestTrackClicked;
                    binding.Track.onClick.Add(binding.TrackClick);
                }
            }
            catch
            {
            }
        }

        private static void RemoveMobileQuestActionFallback(MobileQuestWindowBinding binding)
        {
            if (binding == null || binding.FallbackActionBar == null)
                return;

            try
            {
                if (binding.Accept != null && binding.FallbackActionButtons.Contains(binding.Accept))
                {
                    if (binding.AcceptClick != null) binding.Accept.onClick.Remove(binding.AcceptClick);
                    binding.Accept = null;
                    binding.AcceptClick = null;
                }
                if (binding.Finish != null && binding.FallbackActionButtons.Contains(binding.Finish))
                {
                    if (binding.FinishClick != null) binding.Finish.onClick.Remove(binding.FinishClick);
                    binding.Finish = null;
                    binding.FinishClick = null;
                }
                if (binding.Abandon != null && binding.FallbackActionButtons.Contains(binding.Abandon))
                {
                    if (binding.AbandonClick != null) binding.Abandon.onClick.Remove(binding.AbandonClick);
                    binding.Abandon = null;
                    binding.AbandonClick = null;
                }
                if (binding.Share != null && binding.FallbackActionButtons.Contains(binding.Share))
                {
                    if (binding.ShareClick != null) binding.Share.onClick.Remove(binding.ShareClick);
                    binding.Share = null;
                    binding.ShareClick = null;
                }
                if (binding.Track != null && binding.FallbackActionButtons.Contains(binding.Track))
                {
                    if (binding.TrackClick != null) binding.Track.onClick.Remove(binding.TrackClick);
                    binding.Track = null;
                    binding.TrackClick = null;
                }
            }
            catch
            {
            }

            try
            {
                if (binding.FallbackActionBar.parent != null)
                    binding.FallbackActionBar.parent.RemoveChild(binding.FallbackActionBar, dispose: true);
                else
                    binding.FallbackActionBar.Dispose();
            }
            catch
            {
            }

            binding.FallbackActionBar = null;
            binding.FallbackActionButtons.Clear();
        }

        private static void TryRefreshMobileQuestIfDue(bool force)
        {
            MobileQuestWindowBinding binding = _mobileQuestBinding;
            if (binding == null)
                return;

            if (binding.Window == null || binding.Window._disposed)
            {
                ResetMobileQuestBindings(clearContext: false);
                return;
            }

            bool visible;
            try { visible = binding.Window.visible; } catch { visible = false; }

            if (!visible)
            {
                if (_mobileQuestWindowWasVisible)
                    _mobileQuestWindowWasVisible = false;
                return;
            }

            _mobileQuestWindowWasVisible = true;

            if (_mobileQuestContext.IsActivityMode)
            {
                EnsureMobileQuestActionFallback(binding);
                AttachMobileQuestActionCallbacks(binding);
            }
            else
            {
                RemoveMobileQuestActionFallback(binding);
            }

            // 控件尺寸可能被真实窗口重新布局；每次刷新都重新放置动态栏，避免盖住关闭/描述区域。
            TryLayoutMobileQuestControls(binding);

            if (!force && !_mobileQuestDirty)
                return;

            _mobileQuestDirty = false;

            List<ClientQuestProgress> quests = BuildMobileQuestList(_mobileQuestContext.NpcObjectId);

            ClientQuestProgress selected = null;
            int selectedIndex = _mobileQuestContext.SelectedQuestIndex;

            if (quests != null && quests.Count > 0)
            {
                if (selectedIndex < 1)
                    selectedIndex = GetQuestIndex(quests[0]);

                for (int i = 0; i < quests.Count; i++)
                {
                    ClientQuestProgress q = quests[i];
                    if (q == null)
                        continue;

                    if (GetQuestIndex(q) == selectedIndex)
                    {
                        selected = q;
                        break;
                    }
                }

                if (selected == null)
                {
                    selected = quests[0];
                    selectedIndex = GetQuestIndex(selected);
                }

                _mobileQuestContext.Select(selectedIndex);
            }
            else
            {
                _mobileQuestContext.ResetForRebind();
            }

            TryRefreshQuestList(binding, quests, _mobileQuestContext.SelectedQuestIndex);
            TryRefreshQuestDetails(binding, selected);
            TryRefreshMobileQuestRewardControls(binding, selected);
            TryRefreshQuestButtons(binding, selected);
            TryLayoutMobileQuestControls(binding);
        }

        private static List<ClientQuestProgress> BuildMobileQuestList(uint npcObjectId)
        {
            var user = GameScene.User;
            if (user == null)
                return new List<ClientQuestProgress>();

            if (_mobileQuestContext.IsActivityMode)
            {
                IReadOnlyList<ClientQuestProgress> activities = GameScene.MobileActivityState.Activities;
                return activities == null ? new List<ClientQuestProgress>() : activities.ToList();
            }

            if (npcObjectId == 0)
            {
                var list = new List<ClientQuestProgress>();
                try
                {
                    if (user.CurrentQuests != null)
                    {
                        for (int i = 0; i < user.CurrentQuests.Count; i++)
                        {
                            ClientQuestProgress q = user.CurrentQuests[i];
                            if (q != null)
                                list.Add(q);
                        }
                    }
                }
                catch
                {
                }

                return list;
            }

            try
            {
                NPCObject npc = MapControl.GetObject(npcObjectId) as NPCObject;
                if (npc != null)
                {
                    List<ClientQuestProgress> available = npc.GetAvailableQuests(returnFirst: false);
                    if (available != null)
                        return available;
                }
            }
            catch
            {
            }

            var result = new List<ClientQuestProgress>(64);

            try
            {
                if (user.CurrentQuests != null)
                {
                    for (int i = 0; i < user.CurrentQuests.Count; i++)
                    {
                        ClientQuestProgress q = user.CurrentQuests[i];
                        if (q?.QuestInfo == null)
                            continue;

                        if (q.QuestInfo.NPCIndex == npcObjectId || q.QuestInfo.FinishNPCIndex == npcObjectId)
                            result.Add(q);
                    }
                }
            }
            catch
            {
            }

            try
            {
                IList<int> completed = user.CompletedQuests;
                for (int i = 0; i < GameScene.QuestInfoList.Count; i++)
                {
                    ClientQuestInfo info = GameScene.QuestInfoList[i];
                    if (info == null || info.NPCIndex != npcObjectId)
                        continue;

                    if (completed != null && completed.Contains(info.Index))
                        continue;

                    bool already = false;
                    for (int j = 0; j < result.Count; j++)
                    {
                        if (GetQuestIndex(result[j]) == info.Index)
                        {
                            already = true;
                            break;
                        }
                    }
                    if (already)
                        continue;

                    result.Add(new ClientQuestProgress { Id = info.Index, QuestInfo = info });
                }
            }
            catch
            {
            }

            return result;
        }

        private static int GetQuestIndex(ClientQuestProgress quest)
        {
            if (quest == null)
                return 0;

            try { if (quest.QuestInfo != null) return quest.QuestInfo.Index; } catch { }
            try { return quest.Id; } catch { return 0; }
        }

        private static string GetQuestName(ClientQuestProgress quest)
        {
            try
            {
                string name = quest?.QuestInfo?.Name;
                if (!string.IsNullOrWhiteSpace(name))
                    return name.Trim();
            }
            catch
            {
            }

            int idx = GetQuestIndex(quest);
            return idx > 0 ? $"任务 {idx}" : "任务";
        }

        private static ClientQuestProgress TryGetSelectedQuest()
        {
            int questIndex = _mobileQuestContext.SelectedQuestIndex;
            if (questIndex < 1)
                return null;

            List<ClientQuestProgress> quests = BuildMobileQuestList(_mobileQuestContext.NpcObjectId);
            if (quests == null || quests.Count == 0)
                return null;

            for (int i = 0; i < quests.Count; i++)
            {
                ClientQuestProgress q = quests[i];
                if (q == null)
                    continue;

                if (GetQuestIndex(q) == questIndex)
                    return q;
            }

            return null;
        }

        private static void TryRefreshQuestList(MobileQuestWindowBinding binding, List<ClientQuestProgress> quests, int selectedQuestIndex)
        {
            if (binding == null)
                return;

            if (binding.FallbackListRows.Count > 0)
            {
                for (int i = 0; i < binding.FallbackListRows.Count; i++)
                    RenderQuestListItem(i, binding.FallbackListRows[i], quests, selectedQuestIndex);
                return;
            }

            if (binding.List == null || binding.List._disposed)
                return;

            try
            {
                binding.Renderer = (index, obj) => RenderQuestListItem(index, obj, quests, selectedQuestIndex);
                binding.List.itemRenderer = binding.Renderer;
                binding.List.numItems = quests?.Count ?? 0;
            }
            catch
            {
            }
        }

        private static bool TryCreateMobileActivityFallbackWindow(out GComponent component, out string resolveInfo)
        {
            component = null;
            resolveInfo = null;

            try
            {
                float width = Math.Max(720F, GRoot.inst?.width ?? 720F);
                float height = Math.Max(640F, GRoot.inst?.height ?? 1280F);
                component = new GComponent
                {
                    name = MobileActivityFallbackName,
                    touchable = true,
                    opaque = true,
                };
                component.SetSize(width, height);

                var background = new GGraph { name = "activity_background", touchable = false };
                background.DrawRect(width, height, 2, new Color(90, 110, 150, 255), new Color(22, 28, 42, 248));
                component.AddChild(background);

                AddMobileActivityFallbackText(component, "activity_heading", "活动 / 赏金", 24F, 14F, width - 96F, 48F, 26, Color.White, true);
                AddMobileActivityFallbackText(component, "activity_title", string.Empty, width * 0.38F, 70F, width * 0.58F, 40F, 21, Color.White, true);
                AddMobileActivityFallbackText(component, "activity_content", string.Empty, width * 0.38F, 116F, width * 0.58F, Math.Max(220F, height - 210F), 17, Color.LightGray, false);

                float rowWidth = width * 0.32F;
                for (int i = 0; i < 6; i++)
                {
                    GButton row = CreateMobileQuestActionButton(component, "activity_row_" + i, string.Empty);
                    row.SetPosition(24F, 76F + i * 58F);
                    row.SetSize(rowWidth, 50F);
                    if (row.GetChild("background") is GGraph rowBackground)
                        rowBackground.SetSize(rowWidth, 50F);
                    if (row.GetChild("title") is GTextField rowTitle)
                        rowTitle.SetSize(rowWidth, 50F);
                }

                GButton close = CreateMobileQuestActionButton(component, "closeButton", "关闭");
                close.SetPosition(width - 84F, 16F);
                close.SetSize(60F, 38F);
                if (close.GetChild("background") is GGraph closeBackground)
                    closeBackground.SetSize(60F, 38F);
                if (close.GetChild("title") is GTextField closeTitle)
                    closeTitle.SetSize(60F, 38F);

                resolveInfo = "fallback(activity)";
                return true;
            }
            catch
            {
                try { component?.Dispose(); } catch { }
                component = null;
                resolveInfo = null;
                return false;
            }
        }

        private static GTextField AddMobileActivityFallbackText(
            GComponent parent,
            string name,
            string text,
            float x,
            float y,
            float width,
            float height,
            int size,
            Color color,
            bool bold)
        {
            var field = new GTextField
            {
                name = name,
                text = text ?? string.Empty,
                touchable = false,
                align = AlignType.Left,
                verticalAlign = VertAlignType.Top,
                autoSize = AutoSizeType.None,
                singleLine = false,
            };
            field.SetPosition(x, y);
            field.SetSize(width, height);
            try
            {
                field.textFormat.size = size;
                field.textFormat.color = color;
                field.textFormat.bold = bold;
            }
            catch { }
            parent.AddChild(field);
            return field;
        }

        private static void RenderQuestListItem(int index, GObject obj, List<ClientQuestProgress> quests, int selectedQuestIndex)
        {
            if (obj is not GComponent itemRoot || itemRoot._disposed)
                return;

            ClientQuestProgress quest = null;
            try
            {
                if (quests != null && index >= 0 && index < quests.Count)
                    quest = quests[index];
            }
            catch
            {
                quest = null;
            }

            MobileQuestItemView view = GetOrCreateQuestItemView(itemRoot);
            if (view == null)
                return;

            try
            {
                if (view.Click != null)
                    itemRoot.onClick.Remove(view.Click);
            }
            catch
            {
            }

            if (quest == null)
            {
                try { itemRoot.visible = false; } catch { }
                return;
            }

            int questIndex = GetQuestIndex(quest);
            try
            {
                int stableIndex = questIndex;
                view.Click = () => OnMobileQuestSelected(stableIndex);
                itemRoot.onClick.Add(view.Click);
            }
            catch
            {
            }

            try { itemRoot.visible = true; } catch { }

            bool isSelected = questIndex > 0 && questIndex == selectedQuestIndex;
            try
            {
                if (!view.OriginalAlphaCaptured)
                {
                    view.OriginalAlpha = itemRoot.alpha;
                    view.OriginalAlphaCaptured = true;
                }

                itemRoot.alpha = isSelected ? view.OriginalAlpha : Math.Max(0.2f, view.OriginalAlpha * 0.85f);
            }
            catch
            {
            }

            string name = GetQuestName(quest);
            string prefix = quest.Completed ? "【可交】" : (quest.Taken ? "【进行中】" : "【可接】");
            if (_mobileQuestContext.IsActivityMode)
                prefix = "[" + MobileActivityState.TypeLabel(quest.QuestInfo) + "]" + prefix;
            try
            {
                if (view.Label != null && !view.Label._disposed)
                    view.Label.text = prefix + name;
            }
            catch
            {
            }
        }

        private static MobileQuestItemView GetOrCreateQuestItemView(GComponent itemRoot)
        {
            if (itemRoot == null || itemRoot._disposed)
                return null;

            if (itemRoot.data is MobileQuestItemView existing && existing.Root != null && !existing.Root._disposed)
                return existing;

            var view = new MobileQuestItemView
            {
                Root = itemRoot,
            };

            try
            {
                List<(int Score, GObject Target)> candidates = CollectMobileChatCandidates(itemRoot, obj => obj is GTextField && obj is not GTextInput, DefaultQuestTitleKeywords, ScoreMobileShopTextCandidate);
                view.Label = SelectMobileChatCandidate<GTextField>(candidates, minScore: 10);
            }
            catch
            {
                view.Label = null;
            }

            try { itemRoot.data = view; } catch { }

            return view;
        }

        private static void OnMobileQuestSelected(int questIndex)
        {
            if (questIndex < 1)
                return;

            _mobileQuestContext.Select(questIndex);
            MarkMobileQuestDirty();
        }

        private static void TryRefreshQuestDetails(MobileQuestWindowBinding binding, ClientQuestProgress selected)
        {
            if (binding == null)
                return;

            string title = selected != null ? GetQuestName(selected) : string.Empty;
            string content = selected != null ? BuildQuestDetailsText(selected) : string.Empty;

            try { if (binding.Title != null && !binding.Title._disposed) binding.Title.text = title; } catch { }
            try { if (binding.Content != null && !binding.Content._disposed) binding.Content.text = content; } catch { }
        }

        private static string BuildQuestDetailsText(ClientQuestProgress quest)
        {
            if (quest == null)
                return string.Empty;

            ClientQuestInfo info = null;
            try { info = quest.QuestInfo; } catch { info = null; }

            var builder = new StringBuilder(1024);

            try
            {
                if (!string.IsNullOrWhiteSpace(_mobileQuestContext.NpcName))
                    builder.Append("NPC：").AppendLine(_mobileQuestContext.NpcName.Trim());
            }
            catch
            {
            }

            builder.Append("状态：").AppendLine(quest.Completed ? "可交付" : (quest.Taken ? "进行中" : "可接取"));
            if (_mobileQuestContext.IsActivityMode && !string.IsNullOrWhiteSpace(GameScene.MobileActivityState.Error))
                builder.Append("提示：").AppendLine(GameScene.MobileActivityState.Error);
            if (_mobileQuestContext.IsActivityMode && info != null)
            {
                builder.Append("类型：").AppendLine(MobileActivityState.TypeLabel(info));
                if (info.TimeLimitInSeconds > 0)
                    builder.Append("限时：").AppendLine(FormatActivityDuration(info.TimeLimitInSeconds));
                if (info.NPCIndex > 0)
                    builder.Append("接取 NPC：").AppendLine(info.NPCIndex.ToString());
            }
            builder.AppendLine();

            if (info != null)
                AppendLines(builder, "描述：", info.Description);

            if (quest.TaskList != null && quest.TaskList.Count > 0)
                AppendLines(builder, "目标：", quest.TaskList);
            else if (info != null)
                AppendLines(builder, "目标：", info.TaskDescription);

            if (info != null && info.ReturnDescription != null && info.ReturnDescription.Count > 0 && quest.Completed)
                AppendLines(builder, "交付：", info.ReturnDescription);

            if (info != null)
            {
                bool hasRewards = info.RewardGold > 0 || info.RewardExp > 0 || info.RewardCredit > 0 ||
                                  (info.RewardsFixedItem != null && info.RewardsFixedItem.Count > 0) ||
                                  (info.RewardsSelectItem != null && info.RewardsSelectItem.Count > 0);
                if (hasRewards)
                {
                    builder.AppendLine();
                    builder.AppendLine("奖励：");
                    if (info.RewardGold > 0) builder.AppendLine($"  - 金币：{info.RewardGold}");
                    if (info.RewardExp > 0) builder.AppendLine($"  - 经验：{info.RewardExp}");
                    if (info.RewardCredit > 0) builder.AppendLine($"  - 点券：{info.RewardCredit}");

                    if (info.RewardsFixedItem != null)
                    {
                        for (int i = 0; i < info.RewardsFixedItem.Count; i++)
                        {
                            QuestItemReward reward = info.RewardsFixedItem[i];
                            string name = reward?.Item?.Name ?? "物品";
                            ushort count = reward?.Count ?? 1;
                            builder.AppendLine($"  - {name} x{count}");
                        }
                    }

                    if (info.RewardsSelectItem != null && info.RewardsSelectItem.Count > 0)
                    {
                        builder.AppendLine("  - 可选：");
                        IReadOnlyList<int> visibleIndices = null;
                        if (_mobileQuestContext.IsActivityMode)
                        {
                            visibleIndices = GameScene.MobileActivityRewardSelection.Refresh(
                                quest,
                                GameScene.User?.Class ?? MirClass.Warrior,
                                GameScene.User?.Gender ?? MirGender.Male);
                        }

                        for (int i = 0; i < info.RewardsSelectItem.Count; i++)
                        {
                            if (visibleIndices != null && !visibleIndices.Contains(i))
                                continue;

                            QuestItemReward reward = info.RewardsSelectItem[i];
                            string name = reward?.Item?.Name ?? "物品";
                            ushort count = reward?.Count ?? 1;
                            bool selected = visibleIndices != null &&
                                            GameScene.MobileActivityRewardSelection.SelectedOriginalIndex == i;
                            builder.AppendLine($"    {(selected ? "【已选】" : "- ")}{name} x{count}");
                        }

                        if (visibleIndices != null && visibleIndices.Count == 0)
                        {
                            builder.AppendLine("    - 当前职业/性别没有可选奖励");
                        }
                        else if (visibleIndices != null && visibleIndices.Count > 1)
                        {
                            builder.AppendLine("    - 请在下方奖励栏选择一项");
                        }
                    }
                }
            }

            return builder.ToString().Trim();
        }

        private static string FormatActivityDuration(int seconds)
        {
            if (seconds <= 0)
                return string.Empty;

            TimeSpan duration = TimeSpan.FromSeconds(seconds);
            if (duration.TotalHours >= 1)
                return $"{(int)duration.TotalHours}小时{duration.Minutes}分";
            if (duration.TotalMinutes >= 1)
                return $"{duration.Minutes}分{duration.Seconds}秒";
            return duration.Seconds + "秒";
        }

        private static void AppendLines(StringBuilder builder, string header, IList<string> lines)
        {
            if (builder == null || string.IsNullOrWhiteSpace(header) || lines == null || lines.Count == 0)
                return;

            builder.AppendLine(header);
            for (int i = 0; i < lines.Count; i++)
            {
                string line = lines[i] ?? string.Empty;
                line = line.Replace("\\r\\n", "\n").Replace("\r", "\n");
                line = line.Trim();
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                builder.Append("  - ").AppendLine(line);
            }
            builder.AppendLine();
        }

        private static void TryRefreshQuestButtons(MobileQuestWindowBinding binding, ClientQuestProgress selected)
        {
            if (binding == null)
                return;

            bool canAccept = selected != null && !selected.Taken && !selected.Completed;
            bool canFinish = selected != null && selected.Taken && selected.Completed;
            bool canAbandon = selected != null && selected.Taken && !selected.Completed;
            bool canShare = selected != null && selected.Taken && !selected.Completed;
            if (_mobileQuestContext.IsActivityMode && GameScene.MobileActivityState.RequestPending)
                canAccept = canFinish = canAbandon = canShare = false;

            try { if (binding.Accept != null && !binding.Accept._disposed) { binding.Accept.grayed = !canAccept; binding.Accept.touchable = canAccept; } } catch { }
            try { if (binding.Finish != null && !binding.Finish._disposed) { binding.Finish.grayed = !canFinish; binding.Finish.touchable = canFinish; } } catch { }
            try { if (binding.Abandon != null && !binding.Abandon._disposed) { binding.Abandon.grayed = !canAbandon; binding.Abandon.touchable = canAbandon; } } catch { }
            try { if (binding.Share != null && !binding.Share._disposed) { binding.Share.grayed = !canShare; binding.Share.touchable = canShare; } } catch { }
            try { if (binding.Track != null && !binding.Track._disposed) { binding.Track.grayed = selected == null; binding.Track.touchable = selected != null; } } catch { }
            UpdateMobileQuestTrackButton(binding, selected);
        }

        private static void UpdateMobileQuestTrackButton(MobileQuestWindowBinding binding, ClientQuestProgress selected)
        {
            if (binding?.Track == null || binding.Track._disposed)
                return;

            bool tracked = IsMobileQuestTracked(selected);
            string title = tracked ? "取消追踪" : "追踪";
            try { binding.Track.title = title; } catch { }
            try
            {
                if (binding.Track.GetChild("title") is GTextField label && !label._disposed)
                    label.text = title;
            }
            catch
            {
            }
        }

        private static bool IsMobileQuestTracked(ClientQuestProgress quest)
        {
            int questIndex = GetQuestIndex(quest);
            int[] tracked = Settings.TrackedQuests;
            if (questIndex <= 0 || tracked == null)
                return false;

            for (int i = 0; i < tracked.Length; i++)
            {
                if (tracked[i] == questIndex)
                    return true;
            }

            return false;
        }

        private static void TryRefreshMobileQuestRewardControls(MobileQuestWindowBinding binding, ClientQuestProgress selected)
        {
            if (binding == null)
                return;

            if (!_mobileQuestContext.IsActivityMode || selected == null || selected.QuestInfo == null)
            {
                GameScene.MobileActivityRewardSelection.Clear();
                RemoveMobileQuestRewardControls(binding);
                return;
            }

            IReadOnlyList<int> visibleIndices = GameScene.MobileActivityRewardSelection.Refresh(
                selected,
                GameScene.User?.Class ?? MirClass.Warrior,
                GameScene.User?.Gender ?? MirGender.Male);

            if (!MobileQuestBindingPolicy.ShouldCreateRewardBar(_mobileQuestContext.IsActivityMode, visibleIndices?.Count ?? 0))
            {
                RemoveMobileQuestRewardControls(binding);
                return;
            }

            bool sameCandidates = binding.RewardSelectionBar != null && !binding.RewardSelectionBar._disposed &&
                                  binding.RewardSelectionButtons.Count == visibleIndices.Count;
            if (sameCandidates)
            {
                for (int i = 0; i < visibleIndices.Count; i++)
                {
                    if (binding.RewardSelectionButtons[i] == null || binding.RewardSelectionButtons[i]._disposed ||
                        !(binding.RewardSelectionButtons[i].data is int rawIndex) || rawIndex != visibleIndices[i])
                    {
                        sameCandidates = false;
                        break;
                    }
                }
            }

            if (!sameCandidates)
            {
                RemoveMobileQuestRewardControls(binding);
                try
                {
                    var bar = new GComponent
                    {
                        name = "__codex_mobile_quest_reward_selection_bar",
                        // 与操作栏相同：允许子按钮命中，但不把整块透明区域当作遮挡层。
                        touchable = MobileQuestDynamicBarPolicy.ParentTouchable,
                        opaque = MobileQuestDynamicBarPolicy.ParentOpaque,
                    };
                    binding.Window.AddChild(bar);
                    binding.RewardSelectionBar = bar;

                    for (int i = 0; i < visibleIndices.Count; i++)
                    {
                        int rawIndex = visibleIndices[i];
                        QuestItemReward reward = selected.QuestInfo.RewardsSelectItem[rawIndex];
                        string title = GetMobileQuestRewardTitle(reward, rawIndex, i + 1);
                        GButton button = CreateMobileQuestRewardButton(bar, rawIndex, title);
                        if (button == null)
                            continue;

                        EventCallback0 click = () => OnMobileQuestRewardSelected(rawIndex);
                        button.onClick.Add(click);
                        button.data = rawIndex;
                        binding.RewardSelectionButtons.Add(button);
                        binding.RewardSelectionClicks.Add(click);
                    }

                    if (binding.RewardSelectionButtons.Count != visibleIndices.Count)
                    {
                        RemoveMobileQuestRewardControls(binding);
                        return;
                    }
                }
                catch
                {
                    RemoveMobileQuestRewardControls(binding);
                }
            }

            UpdateMobileQuestRewardButtonStates(binding, selected);
        }

        private static string GetMobileQuestRewardTitle(QuestItemReward reward, int rawIndex, int ordinal)
        {
            string name = reward?.Item?.Name;
            if (string.IsNullOrWhiteSpace(name))
                name = "物品";

            ushort count = reward?.Count ?? 1;
            return $"{ordinal}. {name} x{count}";
        }

        private static GButton CreateMobileQuestRewardButton(GComponent bar, int rawIndex, string title)
        {
            if (bar == null || bar._disposed)
                return null;

            try
            {
                var button = new GButton
                {
                    name = "__codex_mobile_quest_reward_" + rawIndex,
                    title = title,
                    touchable = true,
                    enabled = true,
                    grayed = false,
                    opaque = true,
                    changeStateOnClick = false,
                    data = rawIndex,
                };

                var background = new GGraph
                {
                    name = "background",
                    touchable = false,
                };
                background.DrawRect(1F, 1F, 2, new Color(105, 125, 155, 255), new Color(30, 45, 65, 245));
                button.AddChild(background);

                var label = new GTextField
                {
                    name = "title",
                    text = title,
                    touchable = false,
                    align = AlignType.Center,
                    verticalAlign = VertAlignType.Middle,
                    autoSize = AutoSizeType.None,
                    singleLine = true,
                };
                try
                {
                    label.textFormat.size = 13;
                    label.textFormat.color = Color.White;
                }
                catch
                {
                }
                button.AddChild(label);
                bar.AddChild(button);
                return button;
            }
            catch
            {
                return null;
            }
        }

        private static void UpdateMobileQuestRewardButtonStates(MobileQuestWindowBinding binding, ClientQuestProgress selected)
        {
            if (binding == null || selected == null)
                return;

            int selectedRawIndex = GameScene.MobileActivityRewardSelection.SelectedOriginalIndex;
            for (int i = 0; i < binding.RewardSelectionButtons.Count; i++)
            {
                GButton button = binding.RewardSelectionButtons[i];
                if (button == null || button._disposed)
                    continue;

                int rawIndex = button.data is int value ? value : -1;
                bool isSelected = rawIndex >= 0 && rawIndex == selectedRawIndex;
                try { button.grayed = false; button.touchable = true; button.alpha = isSelected ? 1F : 0.82F; } catch { }
                try
                {
                    if (button.GetChild("title") is GTextField label && !label._disposed)
                    {
                        QuestItemReward reward = selected.QuestInfo?.RewardsSelectItem != null &&
                                                 rawIndex >= 0 && rawIndex < selected.QuestInfo.RewardsSelectItem.Count
                            ? selected.QuestInfo.RewardsSelectItem[rawIndex]
                            : null;
                        string name = GetMobileQuestRewardTitle(reward, rawIndex, i + 1);
                        label.text = isSelected ? "✓ " + name : name;
                    }
                }
                catch
                {
                }
            }
        }

        private static void OnMobileQuestRewardSelected(int rawIndex)
        {
            try
            {
                ClientQuestProgress selected = TryGetSelectedQuest();
                if (selected == null)
                    return;

                bool changed = GameScene.MobileActivityRewardSelection.Select(
                    selected,
                    rawIndex,
                    GameScene.User?.Class ?? MirClass.Warrior,
                    GameScene.User?.Gender ?? MirGender.Male);
                if (changed)
                    MarkMobileQuestDirty();
            }
            catch (Exception ex)
            {
                CMain.SaveError("FairyGUI: 选择活动奖励失败：" + ex.Message);
            }
        }

        private static void RemoveMobileQuestRewardControls(MobileQuestWindowBinding binding)
        {
            if (binding == null)
                return;

            try
            {
                for (int i = 0; i < binding.RewardSelectionButtons.Count; i++)
                {
                    GButton button = binding.RewardSelectionButtons[i];
                    EventCallback0 click = i < binding.RewardSelectionClicks.Count ? binding.RewardSelectionClicks[i] : null;
                    if (button != null && click != null)
                        button.onClick.Remove(click);
                }
            }
            catch
            {
            }

            try
            {
                if (binding.RewardSelectionBar != null && binding.RewardSelectionBar.parent != null)
                    binding.RewardSelectionBar.parent.RemoveChild(binding.RewardSelectionBar, dispose: true);
                else
                    binding.RewardSelectionBar?.Dispose();
            }
            catch
            {
            }

            binding.RewardSelectionBar = null;
            binding.RewardSelectionButtons.Clear();
            binding.RewardSelectionClicks.Clear();
        }

        private static void TryLayoutMobileQuestControls(MobileQuestWindowBinding binding)
        {
            if (binding == null || binding.Window == null || binding.Window._disposed)
                return;

            float width;
            float height;
            try
            {
                width = Math.Max(1F, binding.Window.width);
                height = Math.Max(1F, binding.Window.height);
            }
            catch
            {
                return;
            }

            bool hasActionBar = binding.FallbackActionBar != null && !binding.FallbackActionBar._disposed;
            bool hasRewardBar = binding.RewardSelectionBar != null && !binding.RewardSelectionBar._disposed &&
                                binding.RewardSelectionButtons.Count > 1;
            const float sideMargin = 12F;
            const float gap = 8F;
            const float actionHeight = 62F;
            const float rewardHeight = 58F;

            float actionY = height - sideMargin - (hasActionBar ? actionHeight : 0F);
            float rewardY = actionY - (hasActionBar ? gap : 0F) - (hasRewardBar ? rewardHeight : 0F);
            if (hasActionBar)
            {
                actionY = Math.Max(sideMargin, actionY);
                try
                {
                    GComponent bar = binding.FallbackActionBar;
                    bar.visible = true;
                    bar.SetPosition(sideMargin, actionY);
                    bar.SetSize(Math.Max(1F, width - sideMargin * 2F), actionHeight);

                    int count = binding.FallbackActionButtons.Count;
                    float buttonGap = 6F;
                    float buttonWidth = count > 0
                        ? Math.Max(1F, (bar.width - buttonGap * Math.Max(0, count - 1)) / count)
                        : 1F;
                    for (int i = 0; i < count; i++)
                    {
                        GButton button = binding.FallbackActionButtons[i];
                        if (button == null || button._disposed)
                            continue;

                        button.SetPosition(i * (buttonWidth + buttonGap), 10F);
                        button.SetSize(buttonWidth, 42F);
                        if (button.GetChild("background") is GGraph background)
                            background.SetSize(buttonWidth, 42F);
                        if (button.GetChild("title") is GTextField label)
                            label.SetSize(buttonWidth, 42F);
                    }
                }
                catch
                {
                }
            }

            if (hasRewardBar)
            {
                rewardY = Math.Max(sideMargin, rewardY);
                try
                {
                    GComponent bar = binding.RewardSelectionBar;
                    bar.visible = true;
                    bar.SetPosition(sideMargin, rewardY);
                    bar.SetSize(Math.Max(1F, width - sideMargin * 2F), rewardHeight);

                    int count = binding.RewardSelectionButtons.Count;
                    float buttonGap = 6F;
                    float buttonWidth = count > 0
                        ? Math.Max(1F, (bar.width - buttonGap * Math.Max(0, count - 1)) / count)
                        : 1F;
                    for (int i = 0; i < count; i++)
                    {
                        GButton button = binding.RewardSelectionButtons[i];
                        if (button == null || button._disposed)
                            continue;

                        button.SetPosition(i * (buttonWidth + buttonGap), 8F);
                        button.SetSize(buttonWidth, 42F);
                        if (button.GetChild("background") is GGraph background)
                            background.SetSize(buttonWidth, 42F);
                        if (button.GetChild("title") is GTextField label)
                            label.SetSize(buttonWidth, 42F);
                    }
                }
                catch
                {
                }
            }

            // 若描述字段占满整个窗口，压缩其底部可用高度，确保动态栏不遮挡任务说明。
            float reservedTop = Math.Min(actionY, rewardY);
            if (!hasActionBar && !hasRewardBar)
                return;

            try
            {
                if (binding.Content != null && !binding.Content._disposed && binding.Content.height > 0F &&
                    binding.Content.y < reservedTop && binding.Content.y + binding.Content.height > reservedTop)
                {
                    float newHeight = Math.Max(1F, reservedTop - binding.Content.y - 4F);
                    if (newHeight < binding.Content.height)
                        binding.Content.SetSize(binding.Content.width, newHeight);
                }
            }
            catch
            {
            }
        }

        private static void OnMobileQuestAcceptClicked()
        {
            try
            {
                ClientQuestProgress selected = TryGetSelectedQuest();
                if (selected == null || selected.Taken || selected.Completed)
                    return;

                int questIndex = GetQuestIndex(selected);
                if (questIndex < 1)
                    return;

                if (_mobileQuestContext.IsActivityMode)
                {
                    GameScene.Scene?.TryAcceptMobileActivity(selected);
                    return;
                }

                Network.Enqueue(new C.AcceptQuest { QuestIndex = questIndex });
            }
            catch (Exception ex)
            {
                CMain.SaveError("FairyGUI: 发送接取任务失败：" + ex.Message);
            }
        }

        private static void OnMobileQuestFinishClicked()
        {
            try
            {
                ClientQuestProgress selected = TryGetSelectedQuest();
                if (selected == null || !selected.Taken || !selected.Completed)
                    return;

                int questIndex = GetQuestIndex(selected);
                if (questIndex < 1)
                    return;

                if (_mobileQuestContext.IsActivityMode)
                {
                    GameScene.Scene?.TryFinishMobileActivity(selected);
                    return;
                }

                Network.Enqueue(new C.FinishQuest { QuestIndex = questIndex });
            }
            catch (Exception ex)
            {
                CMain.SaveError("FairyGUI: 发送交付任务失败：" + ex.Message);
            }
        }

        private static void OnMobileQuestAbandonClicked()
        {
            try
            {
                ClientQuestProgress selected = TryGetSelectedQuest();
                if (selected == null || !selected.Taken)
                    return;

                int questIndex = GetQuestIndex(selected);
                if (questIndex < 1)
                    return;

                if (_mobileQuestContext.IsActivityMode)
                {
                    GameScene.Scene?.TryAbandonMobileActivity(selected);
                    return;
                }

                Network.Enqueue(new C.AbandonQuest { QuestIndex = questIndex });
            }
            catch (Exception ex)
            {
                CMain.SaveError("FairyGUI: 发送放弃任务失败：" + ex.Message);
            }
        }

        private static void OnMobileQuestShareClicked()
        {
            try
            {
                ClientQuestProgress selected = TryGetSelectedQuest();
                if (selected == null || !selected.Taken || selected.Completed)
                    return;

                int questIndex = GetQuestIndex(selected);
                if (questIndex < 1)
                    return;

                if (_mobileQuestContext.IsActivityMode)
                {
                    GameScene.Scene?.TryShareMobileActivity(selected);
                    return;
                }

                Network.Enqueue(new C.ShareQuest { QuestIndex = questIndex });
            }
            catch (Exception ex)
            {
                CMain.SaveError("FairyGUI: 分享任务失败：" + ex.Message);
            }
        }

        private static void OnMobileQuestTrackClicked()
        {
            try
            {
                ClientQuestProgress selected = TryGetSelectedQuest();
                if (selected == null)
                    return;

                int questIndex = GetQuestIndex(selected);
                if (questIndex < 1)
                    return;

                int[] tracked = Settings.TrackedQuests;
                if (tracked == null || tracked.Length == 0)
                    return;

                bool removed = false;
                for (int i = 0; i < tracked.Length; i++)
                {
                    if (tracked[i] == questIndex)
                    {
                        tracked[i] = 0;
                        removed = true;
                        break;
                    }
                }

                if (!removed)
                {
                    int slot = -1;
                    for (int i = 0; i < tracked.Length; i++)
                    {
                        if (tracked[i] == 0)
                        {
                            slot = i;
                            break;
                        }
                    }

                    if (slot < 0)
                        slot = tracked.Length - 1;

                    tracked[slot] = questIndex;
                }

                string name = GameScene.User?.Name ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(name))
                    Settings.SaveTrackedQuests(name);

                GameScene.Scene?.RefreshMobileQuestTrackingOverlay();
                MarkMobileQuestDirty();
            }
            catch (Exception ex)
            {
                CMain.SaveError("FairyGUI: 追踪任务失败：" + ex.Message);
            }
        }

        private static void TryDumpMobileQuestBindingsIfDue(MobileQuestWindowBinding binding)
        {
            if (!Settings.DebugMode)
                return;

            if (_mobileQuestBindingsDumped)
                return;

            if (binding == null || binding.Window == null || binding.Window._disposed)
                return;

            try
            {
                Directory.CreateDirectory(ClientResourceLayout.RuntimeRoot);
                string path = Path.Combine(ClientResourceLayout.RuntimeRoot, "FairyGui-MobileQuestBindings.txt");

                var builder = new StringBuilder(8 * 1024);
                builder.AppendLine("FairyGUI 移动端任务绑定报告");
                builder.AppendLine($"GeneratedAtUtc={DateTime.UtcNow:o}");
                builder.AppendLine($"WindowKey={binding.WindowKey}");
                if (!string.IsNullOrWhiteSpace(binding.ResolveInfo))
                    builder.AppendLine($"Resolved={binding.ResolveInfo}");
                builder.AppendLine();

                builder.AppendLine($"List={DescribeObject(binding.Window, binding.List)}");
                builder.AppendLine($"Title={DescribeObject(binding.Window, binding.Title)}");
                builder.AppendLine($"Content={DescribeObject(binding.Window, binding.Content)}");
                builder.AppendLine($"Accept={DescribeObject(binding.Window, binding.Accept)}");
                builder.AppendLine($"Finish={DescribeObject(binding.Window, binding.Finish)}");
                builder.AppendLine($"Abandon={DescribeObject(binding.Window, binding.Abandon)}");
                builder.AppendLine($"Share={DescribeObject(binding.Window, binding.Share)}");
                builder.AppendLine($"Track={DescribeObject(binding.Window, binding.Track)}");
                builder.AppendLine();

                builder.AppendLine("OverrideSpec:");
                builder.AppendLine($"  {MobileQuestListConfigKey}={binding.ListOverrideSpec}");
                builder.AppendLine($"  {MobileQuestTitleConfigKey}={binding.TitleOverrideSpec}");
                builder.AppendLine($"  {MobileQuestContentConfigKey}={binding.ContentOverrideSpec}");
                builder.AppendLine($"  {MobileQuestAcceptConfigKey}={binding.AcceptOverrideSpec}");
                builder.AppendLine($"  {MobileQuestFinishConfigKey}={binding.FinishOverrideSpec}");
                builder.AppendLine($"  {MobileQuestAbandonConfigKey}={binding.AbandonOverrideSpec}");
                builder.AppendLine($"  {MobileQuestShareConfigKey}={binding.ShareOverrideSpec}");
                builder.AppendLine($"  {MobileQuestTrackConfigKey}={binding.TrackOverrideSpec}");
                builder.AppendLine();

                builder.AppendLine("ResolveInfo:");
                builder.AppendLine($"  List={binding.ListResolveInfo}");
                builder.AppendLine($"  Title={binding.TitleResolveInfo}");
                builder.AppendLine($"  Content={binding.ContentResolveInfo}");
                builder.AppendLine($"  Accept={binding.AcceptResolveInfo}");
                builder.AppendLine($"  Finish={binding.FinishResolveInfo}");
                builder.AppendLine($"  Abandon={binding.AbandonResolveInfo}");
                builder.AppendLine($"  Share={binding.ShareResolveInfo}");
                builder.AppendLine($"  Track={binding.TrackResolveInfo}");
                builder.AppendLine();

                File.WriteAllText(path, builder.ToString(), Encoding.UTF8);
                _mobileQuestBindingsDumped = true;
                CMain.SaveLog("FairyGUI: 任务绑定报告已生成：" + path);
            }
            catch (Exception ex)
            {
                CMain.SaveError("FairyGUI: 写入任务绑定报告失败：" + ex.Message);
            }
        }
    }
}
