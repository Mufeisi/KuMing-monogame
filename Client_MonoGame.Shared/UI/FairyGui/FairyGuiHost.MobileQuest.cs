using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using C = ClientPackets;
using FairyGUI;
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
        private const string MobileQuestTrackConfigKey = "MobileQuest.Track";

        private static readonly string[] DefaultQuestListKeywords = { "任务_DA2EWindow1UI", "任务", "quest", "diary", "log", "list" };
        private static readonly string[] DefaultQuestTitleKeywords = { "任务", "quest", "title", "name", "标题", "名称" };
        private static readonly string[] DefaultQuestContentKeywords = { "任务", "quest", "content", "desc", "detail", "text", "内容", "说明", "目标", "进度" };
        private static readonly string[] DefaultQuestAcceptKeywords = { "accept", "take", "接取", "接受", "领取" };
        private static readonly string[] DefaultQuestFinishKeywords = { "finish", "complete", "交付", "提交", "完成", "领奖" };
        private static readonly string[] DefaultQuestAbandonKeywords = { "abandon", "giveup", "drop", "放弃", "取消" };
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

            public GButton Track;
            public string TrackResolveInfo;
            public string TrackOverrideSpec;
            public string[] TrackOverrideKeywords;
            public EventCallback0 TrackClick;
        }

        private static MobileQuestWindowBinding _mobileQuestBinding;
        private static DateTime _nextMobileQuestBindAttemptUtc = DateTime.MinValue;
        private static bool _mobileQuestBindingsDumped;
        private static bool _mobileQuestDirty;
        private static bool _mobileQuestWindowWasVisible;

        private static uint _mobileQuestNpcObjectId;
        private static string _mobileQuestNpcName = string.Empty;
        private static int _mobileQuestSelectedQuestIndex;

        public static void UpdateMobileQuestContext(uint npcObjectId, string npcName)
        {
            _mobileQuestNpcObjectId = npcObjectId;
            _mobileQuestNpcName = npcName ?? string.Empty;
            MarkMobileQuestDirty();
        }

        public static void BeginMobileQuestDetail(ClientQuestProgress quest)
        {
            int questIndex = 0;
            try { questIndex = quest?.QuestInfo?.Index ?? quest?.Id ?? 0; } catch { questIndex = 0; }

            if (questIndex > 0)
                _mobileQuestSelectedQuestIndex = questIndex;

            MarkMobileQuestDirty();
        }

        public static void MarkMobileQuestDirty()
        {
            try { _mobileQuestDirty = true; } catch { }
            TryRefreshMobileQuestIfDue(force: false);
        }

        private static void ResetMobileQuestBindings()
        {
            try
            {
                MobileQuestWindowBinding binding = _mobileQuestBinding;
                if (binding != null)
                {
                    try { if (binding.Accept != null && binding.AcceptClick != null) binding.Accept.onClick.Remove(binding.AcceptClick); } catch { }
                    try { if (binding.Finish != null && binding.FinishClick != null) binding.Finish.onClick.Remove(binding.FinishClick); } catch { }
                    try { if (binding.Abandon != null && binding.AbandonClick != null) binding.Abandon.onClick.Remove(binding.AbandonClick); } catch { }
                    try { if (binding.Track != null && binding.TrackClick != null) binding.Track.onClick.Remove(binding.TrackClick); } catch { }
                    try { if (binding.List != null && !binding.List._disposed) binding.List.itemRenderer = null; } catch { }
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
            _mobileQuestSelectedQuestIndex = 0;
        }

        private static void TryBindMobileQuestWindowIfDue(string windowKey, GComponent window, string resolveInfo)
        {
            if (window == null || window._disposed)
                return;

            if (_mobileQuestBinding != null && _mobileQuestBinding.Window != null && _mobileQuestBinding.Window._disposed)
                ResetMobileQuestBindings();

            if (_mobileQuestBinding == null || _mobileQuestBinding.Window == null || _mobileQuestBinding.Window._disposed || !ReferenceEquals(_mobileQuestBinding.Window, window))
            {
                ResetMobileQuestBindings();
                _mobileQuestBinding = new MobileQuestWindowBinding
                {
                    WindowKey = windowKey,
                    Window = window,
                    ResolveInfo = resolveInfo,
                };
            }

            if (DateTime.UtcNow < _nextMobileQuestBindAttemptUtc)
                return;

            MobileQuestWindowBinding binding = _mobileQuestBinding;
            if (binding == null)
                return;

            bool listBound = binding.List != null && !binding.List._disposed;
            if (listBound && binding.Accept != null && !binding.Accept._disposed && binding.Finish != null && !binding.Finish._disposed)
                return;

            _nextMobileQuestBindAttemptUtc = DateTime.UtcNow.AddSeconds(2);

            string listSpec = string.Empty;
            string titleSpec = string.Empty;
            string contentSpec = string.Empty;
            string acceptSpec = string.Empty;
            string finishSpec = string.Empty;
            string abandonSpec = string.Empty;
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
                trackSpec = string.Empty;
            }

            listSpec = listSpec?.Trim() ?? string.Empty;
            titleSpec = titleSpec?.Trim() ?? string.Empty;
            contentSpec = contentSpec?.Trim() ?? string.Empty;
            acceptSpec = acceptSpec?.Trim() ?? string.Empty;
            finishSpec = finishSpec?.Trim() ?? string.Empty;
            abandonSpec = abandonSpec?.Trim() ?? string.Empty;
            trackSpec = trackSpec?.Trim() ?? string.Empty;

            binding.ListOverrideSpec = listSpec;
            binding.TitleOverrideSpec = titleSpec;
            binding.ContentOverrideSpec = contentSpec;
            binding.AcceptOverrideSpec = acceptSpec;
            binding.FinishOverrideSpec = finishSpec;
            binding.AbandonOverrideSpec = abandonSpec;
            binding.TrackOverrideSpec = trackSpec;

            // List
            if (binding.List == null || binding.List._disposed)
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
            BindMailButton(window, ref binding.Accept, ref binding.AcceptResolveInfo, acceptSpec, DefaultQuestAcceptKeywords, out binding.AcceptOverrideKeywords);
            BindMailButton(window, ref binding.Finish, ref binding.FinishResolveInfo, finishSpec, DefaultQuestFinishKeywords, out binding.FinishOverrideKeywords);
            BindMailButton(window, ref binding.Abandon, ref binding.AbandonResolveInfo, abandonSpec, DefaultQuestAbandonKeywords, out binding.AbandonOverrideKeywords);
            BindMailButton(window, ref binding.Track, ref binding.TrackResolveInfo, trackSpec, DefaultQuestTrackKeywords, out binding.TrackOverrideKeywords);

            // Callbacks
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

                if (binding.Track != null && !binding.Track._disposed && binding.TrackClick == null)
                {
                    binding.TrackClick = OnMobileQuestTrackClicked;
                    binding.Track.onClick.Add(binding.TrackClick);
                }
            }
            catch
            {
            }

            TryDumpMobileQuestBindingsIfDue(binding);
        }

        private static void TryRefreshMobileQuestIfDue(bool force)
        {
            MobileQuestWindowBinding binding = _mobileQuestBinding;
            if (binding == null)
                return;

            if (binding.Window == null || binding.Window._disposed)
            {
                ResetMobileQuestBindings();
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

            if (!force && !_mobileQuestDirty)
                return;

            _mobileQuestDirty = false;

            List<ClientQuestProgress> quests = BuildMobileQuestList(_mobileQuestNpcObjectId);

            ClientQuestProgress selected = null;
            int selectedIndex = _mobileQuestSelectedQuestIndex;

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

                _mobileQuestSelectedQuestIndex = selectedIndex;
            }
            else
            {
                _mobileQuestSelectedQuestIndex = 0;
            }

            TryRefreshQuestList(binding, quests, _mobileQuestSelectedQuestIndex);
            TryRefreshQuestDetails(binding, selected);
            TryRefreshQuestButtons(binding, selected);
        }

        private static List<ClientQuestProgress> BuildMobileQuestList(uint npcObjectId)
        {
            var user = GameScene.User;
            if (user == null)
                return new List<ClientQuestProgress>();

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
            int questIndex = _mobileQuestSelectedQuestIndex;
            if (questIndex < 1)
                return null;

            List<ClientQuestProgress> quests = BuildMobileQuestList(_mobileQuestNpcObjectId);
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
            if (binding == null || binding.List == null || binding.List._disposed)
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

            _mobileQuestSelectedQuestIndex = questIndex;
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
                if (!string.IsNullOrWhiteSpace(_mobileQuestNpcName))
                    builder.Append("NPC：").AppendLine(_mobileQuestNpcName.Trim());
            }
            catch
            {
            }

            builder.Append("状态：").AppendLine(quest.Completed ? "可交付" : (quest.Taken ? "进行中" : "可接取"));
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
                        for (int i = 0; i < info.RewardsSelectItem.Count; i++)
                        {
                            QuestItemReward reward = info.RewardsSelectItem[i];
                            string name = reward?.Item?.Name ?? "物品";
                            ushort count = reward?.Count ?? 1;
                            builder.AppendLine($"    - {name} x{count}");
                        }
                    }
                }
            }

            return builder.ToString().Trim();
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

            try { if (binding.Accept != null && !binding.Accept._disposed) { binding.Accept.grayed = !canAccept; binding.Accept.touchable = canAccept; } } catch { }
            try { if (binding.Finish != null && !binding.Finish._disposed) { binding.Finish.grayed = !canFinish; binding.Finish.touchable = canFinish; } } catch { }
            try { if (binding.Abandon != null && !binding.Abandon._disposed) { binding.Abandon.grayed = !canAbandon; binding.Abandon.touchable = canAbandon; } } catch { }
            try { if (binding.Track != null && !binding.Track._disposed) { binding.Track.grayed = selected == null; binding.Track.touchable = selected != null; } } catch { }
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

                Network.Enqueue(new C.AbandonQuest { QuestIndex = questIndex });
            }
            catch (Exception ex)
            {
                CMain.SaveError("FairyGUI: 发送放弃任务失败：" + ex.Message);
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
                builder.AppendLine($"Track={DescribeObject(binding.Window, binding.Track)}");
                builder.AppendLine();

                builder.AppendLine("OverrideSpec:");
                builder.AppendLine($"  {MobileQuestListConfigKey}={binding.ListOverrideSpec}");
                builder.AppendLine($"  {MobileQuestTitleConfigKey}={binding.TitleOverrideSpec}");
                builder.AppendLine($"  {MobileQuestContentConfigKey}={binding.ContentOverrideSpec}");
                builder.AppendLine($"  {MobileQuestAcceptConfigKey}={binding.AcceptOverrideSpec}");
                builder.AppendLine($"  {MobileQuestFinishConfigKey}={binding.FinishOverrideSpec}");
                builder.AppendLine($"  {MobileQuestAbandonConfigKey}={binding.AbandonOverrideSpec}");
                builder.AppendLine($"  {MobileQuestTrackConfigKey}={binding.TrackOverrideSpec}");
                builder.AppendLine();

                builder.AppendLine("ResolveInfo:");
                builder.AppendLine($"  List={binding.ListResolveInfo}");
                builder.AppendLine($"  Title={binding.TitleResolveInfo}");
                builder.AppendLine($"  Content={binding.ContentResolveInfo}");
                builder.AppendLine($"  Accept={binding.AcceptResolveInfo}");
                builder.AppendLine($"  Finish={binding.FinishResolveInfo}");
                builder.AppendLine($"  Abandon={binding.AbandonResolveInfo}");
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
