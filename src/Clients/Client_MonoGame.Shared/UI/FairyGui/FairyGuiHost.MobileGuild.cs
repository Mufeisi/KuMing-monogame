using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using C = ClientPackets;
using FairyGUI;

namespace MonoShare
{
    internal static partial class FairyGuiHost
    {
        private static void OnMobileGuildMemberItemClicked(MobileGuildMemberItemView view)
        {
            if (view == null)
                return;

            MobileGuildWindowBinding binding = _mobileGuildBinding;
            if (binding == null || binding.Window == null || binding.Window._disposed)
                return;

            int index = view.Index;
            if (index < 0 || index >= MobileGuildMembers.Count)
                return;

            if (binding.SelectedIndex == index)
                return;

            binding.SelectedIndex = index;

            try
            {
                _mobileGuildDirty = true;
            }
            catch
            {
            }
        }

        private static MobileGuildMemberItemView GetOrCreateMobileGuildMemberItemView(GComponent itemRoot)
        {
            if (itemRoot == null || itemRoot._disposed)
                return null;

            if (itemRoot.data is MobileGuildMemberItemView existing && existing.Root != null && !existing.Root._disposed)
                return existing;

            var view = new MobileGuildMemberItemView
            {
                Root = itemRoot,
                Index = -1,
            };

            try
            {
                List<(int Score, GObject Target)> nameCandidates =
                    CollectMobileChatCandidates(itemRoot, obj => obj is GTextField && obj is not GTextInput, DefaultGuildMemberNameKeywords, ScoreMobileShopTextCandidate);
                view.Name = SelectMobileChatCandidate<GTextField>(nameCandidates, minScore: 15);
            }
            catch
            {
            }

            try
            {
                List<(int Score, GObject Target)> rankCandidates =
                    CollectMobileChatCandidates(itemRoot, obj => obj is GTextField && obj is not GTextInput, DefaultGuildMemberRankKeywords, ScoreMobileShopTextCandidate);
                view.Rank = SelectMobileChatCandidate<GTextField>(rankCandidates, minScore: 15);
            }
            catch
            {
            }

            try
            {
                List<(int Score, GObject Target)> statusCandidates =
                    CollectMobileChatCandidates(itemRoot, obj => obj is GTextField && obj is not GTextInput, DefaultGuildMemberStatusKeywords, ScoreMobileShopTextCandidate);
                view.Status = SelectMobileChatCandidate<GTextField>(statusCandidates, minScore: 15);
            }
            catch
            {
            }

            try
            {
                string[] lastLoginKeywords = { "last", "login", "time", "date", "最近", "登陆", "登录", "时间", "日期" };
                List<(int Score, GObject Target)> lastLoginCandidates =
                    CollectMobileChatCandidates(itemRoot, obj => obj is GTextField && obj is not GTextInput, lastLoginKeywords, ScoreMobileShopTextCandidate);
                view.LastLogin = SelectMobileChatCandidate<GTextField>(lastLoginCandidates, minScore: 15);
            }
            catch
            {
            }

            try
            {
                if (view.ClickCallback == null)
                {
                    view.ClickCallback = () => OnMobileGuildMemberItemClicked(view);
                    itemRoot.onClick.Add(view.ClickCallback);
                }
            }
            catch
            {
            }

            try
            {
                itemRoot.data = view;
            }
            catch
            {
            }

            return view;
        }

        private static void ClearMobileGuildMemberItemView(MobileGuildMemberItemView view)
        {
            if (view == null)
                return;

            try
            {
                if (view.Name != null && !view.Name._disposed)
                    view.Name.text = string.Empty;
            }
            catch
            {
            }

            try
            {
                if (view.Rank != null && !view.Rank._disposed)
                    view.Rank.text = string.Empty;
            }
            catch
            {
            }

            try
            {
                if (view.Status != null && !view.Status._disposed)
                    view.Status.text = string.Empty;
            }
            catch
            {
            }

            try
            {
                if (view.LastLogin != null && !view.LastLogin._disposed)
                    view.LastLogin.text = string.Empty;
            }
            catch
            {
            }
        }

        private static void RenderMobileGuildMemberListItem(int index, GObject itemObject)
        {
            if (itemObject == null || itemObject._disposed)
                return;

            if (itemObject is not GComponent itemRoot || itemRoot._disposed)
                return;

            MobileGuildMemberItemView view = GetOrCreateMobileGuildMemberItemView(itemRoot);
            if (view == null)
                return;

            view.Index = index;

            MobileGuildWindowBinding binding = _mobileGuildBinding;
            bool selected = binding != null && binding.SelectedIndex == index;

            try
            {
                if (itemObject is GButton button && !button._disposed)
                    button.selected = selected;
            }
            catch
            {
            }

            MobileGuildMemberEntry entry = null;
            if (index >= 0 && index < MobileGuildMembers.Count)
                entry = MobileGuildMembers[index];

            if (entry == null)
            {
                ClearMobileGuildMemberItemView(view);
                return;
            }

            string name = entry.Name ?? string.Empty;
            string rank = entry.RankName ?? string.Empty;
            string status = entry.Online ? "在线" : "离线";
            string lastLogin = entry.LastLogin == default ? string.Empty : entry.LastLogin.ToString("yyyy-MM-dd");

            try
            {
                if (view.Name != null && !view.Name._disposed)
                    view.Name.text = name;
            }
            catch
            {
            }

            try
            {
                if (view.Rank != null && !view.Rank._disposed)
                    view.Rank.text = rank;
            }
            catch
            {
            }

            try
            {
                if (view.Status != null && !view.Status._disposed)
                    view.Status.text = status;
            }
            catch
            {
            }

            try
            {
                if (view.LastLogin != null && !view.LastLogin._disposed)
                    view.LastLogin.text = lastLogin;
            }
            catch
            {
            }
        }

        private static void TrySendMobileGuildInvite(string name)
        {
            if (!TryValidateCharacterName(name, "工会邀请", out string cleaned))
                return;

            try
            {
                MonoShare.MirNetwork.Network.Enqueue(new C.EditGuildMember { ChangeType = 0, Name = cleaned });
            }
            catch (Exception ex)
            {
                CMain.SaveError("FairyGUI: 发送工会邀请失败：" + ex.Message);
                MobileHint("网络异常：工会邀请发送失败。");
            }
        }

        private static void TrySendMobileGuildKick(string name)
        {
            if (!TryValidateCharacterName(name, "工会踢人", out string cleaned))
                return;

            try
            {
                MonoShare.MirNetwork.Network.Enqueue(new C.EditGuildMember { ChangeType = 1, Name = cleaned });
            }
            catch (Exception ex)
            {
                CMain.SaveError("FairyGUI: 发送工会踢人失败：" + ex.Message);
                MobileHint("网络异常：工会踢人失败。");
            }
        }

        private static void TryDumpMobileGuildBindingsReportIfDue(
            MobileGuildWindowBinding binding,
            string[] memberListKeywordsUsed,
            List<(int Score, GObject Target)> memberListCandidates)
        {
            if (!Settings.DebugMode || _mobileGuildBindingsDumped)
                return;

            if (binding == null || binding.Window == null || binding.Window._disposed)
                return;

            try
            {
                Directory.CreateDirectory(ClientResourceLayout.RuntimeRoot);

                string path = Path.Combine(ClientResourceLayout.RuntimeRoot, "FairyGui-MobileGuildBindings.txt");

                var builder = new StringBuilder(16 * 1024);
                builder.AppendLine("FairyGUI 工会窗口绑定报告（Guild）");
                builder.AppendLine($"GeneratedAtUtc={DateTime.UtcNow:o}");
                builder.AppendLine($"WindowKey={binding.WindowKey ?? "Guild"}");
                builder.AppendLine($"WindowResolveInfo={binding.ResolveInfo ?? "-"}");
                builder.AppendLine();

                builder.AppendLine($"NoticeField={DescribeObject(binding.Window, binding.NoticeField)}");
                builder.AppendLine($"NoticeResolveInfo={binding.NoticeResolveInfo ?? "-"}");
                builder.AppendLine($"OverrideSpec={binding.NoticeOverrideSpec ?? "-"}");
                builder.AppendLine($"OverrideKeywords={(binding.NoticeOverrideKeywords == null ? "-" : string.Join("|", binding.NoticeOverrideKeywords))}");
                builder.AppendLine();

                builder.AppendLine($"MemberList={DescribeObject(binding.Window, binding.MemberList)}");
                builder.AppendLine($"MemberListResolveInfo={binding.MemberListResolveInfo ?? "-"}");
                builder.AppendLine($"OverrideSpec={binding.MemberListOverrideSpec ?? "-"}");
                builder.AppendLine($"OverrideKeywords={(binding.MemberListOverrideKeywords == null ? "-" : string.Join("|", binding.MemberListOverrideKeywords))}");
                builder.AppendLine($"KeywordsUsed={(memberListKeywordsUsed == null ? "-" : string.Join("|", memberListKeywordsUsed))}");
                builder.AppendLine($"Members={MobileGuildMembers.Count}");
                builder.AppendLine();

                builder.AppendLine($"InviteInput={DescribeObject(binding.Window, binding.InviteInput)} OverrideSpec={binding.InviteInputOverrideSpec ?? "-"}");
                builder.AppendLine($"InviteButton={DescribeObject(binding.Window, binding.InviteButton)} OverrideSpec={binding.InviteButtonOverrideSpec ?? "-"}");
                builder.AppendLine($"KickButton={DescribeObject(binding.Window, binding.KickButton)} OverrideSpec={binding.KickButtonOverrideSpec ?? "-"}");
                builder.AppendLine();

                builder.AppendLine("状态字段（可选，GuildStatus）：");
                builder.AppendLine($"GuildName={DescribeObject(binding.Window, binding.StatusGuildName)} OverrideSpec={binding.StatusGuildNameOverrideSpec ?? "-"}");
                builder.AppendLine($"GuildRank={DescribeObject(binding.Window, binding.StatusGuildRank)} OverrideSpec={binding.StatusGuildRankOverrideSpec ?? "-"}");
                builder.AppendLine($"Level={DescribeObject(binding.Window, binding.StatusLevel)} OverrideSpec={binding.StatusLevelOverrideSpec ?? "-"}");
                builder.AppendLine($"Exp={DescribeObject(binding.Window, binding.StatusExp)} OverrideSpec={binding.StatusExpOverrideSpec ?? "-"}");
                builder.AppendLine($"ExpBar={DescribeObject(binding.Window, binding.StatusExpBar)} OverrideSpec={binding.StatusExpBarOverrideSpec ?? "-"}");
                builder.AppendLine($"Members={DescribeObject(binding.Window, binding.StatusMembers)} OverrideSpec={binding.StatusMembersOverrideSpec ?? "-"}");
                builder.AppendLine($"Gold={DescribeObject(binding.Window, binding.StatusGold)} OverrideSpec={binding.StatusGoldOverrideSpec ?? "-"}");
                builder.AppendLine($"Points={DescribeObject(binding.Window, binding.StatusPoints)} OverrideSpec={binding.StatusPointsOverrideSpec ?? "-"}");
                builder.AppendLine($"Options={DescribeObject(binding.Window, binding.StatusOptions)} OverrideSpec={binding.StatusOptionsOverrideSpec ?? "-"}");
                builder.AppendLine();

                builder.AppendLine("MemberList Candidates(top 12):");
                int top = Math.Min(12, memberListCandidates?.Count ?? 0);
                for (int i = 0; i < top; i++)
                {
                    (int score, GObject target) = memberListCandidates[i];
                    builder.AppendLine($"  - score={score} obj={DescribeObject(binding.Window, target)}");
                }

                if ((memberListCandidates?.Count ?? 0) > top)
                    builder.AppendLine($"  ... ({memberListCandidates.Count - top} more)");

                builder.AppendLine();
                builder.AppendLine("配置覆盖（可选）：Mir2Config.ini -> [FairyGUI]");
                builder.AppendLine($"  {MobileGuildNoticeConfigKey}=idx:...（公告文本控件）");
                builder.AppendLine($"  {MobileGuildMemberListConfigKey}=idx:...（成员列表 GList）");
                builder.AppendLine($"  {MobileGuildInviteInputConfigKey}=idx:...（邀请输入框）");
                builder.AppendLine($"  {MobileGuildInviteButtonConfigKey}=idx:...（邀请按钮）");
                builder.AppendLine($"  {MobileGuildKickButtonConfigKey}=idx:...（踢人按钮）");
                builder.AppendLine($"  {MobileGuildNameConfigKey}=idx:...（工会名文本，可选）");
                builder.AppendLine($"  {MobileGuildRankConfigKey}=idx:...（职位文本，可选）");
                builder.AppendLine($"  {MobileGuildLevelConfigKey}=idx:...（等级文本，可选）");
                builder.AppendLine($"  {MobileGuildExpConfigKey}=idx:...（经验文本，可选）");
                builder.AppendLine($"  {MobileGuildExpBarConfigKey}=idx:...（经验进度条，可选）");
                builder.AppendLine($"  {MobileGuildMemberCountConfigKey}=idx:...（成员数文本，可选）");
                builder.AppendLine($"  {MobileGuildGoldConfigKey}=idx:...（基金/金币文本，可选）");
                builder.AppendLine($"  {MobileGuildPointsConfigKey}=idx:...（剩余点数文本，可选）");
                builder.AppendLine($"  {MobileGuildOptionsConfigKey}=idx:...（权限文本，可选）");
                builder.AppendLine("说明：idx/path 均相对工会窗口 Root。建议先查看窗口树（FairyGui-MobileWindow-Guild-Tree.txt），再填入精确覆盖。");

                File.WriteAllText(path, builder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                _mobileGuildBindingsDumped = true;
                CMain.SaveLog($"FairyGUI: 已导出工会窗口绑定报告到 {path}");
            }
            catch (Exception ex)
            {
                CMain.SaveError("FairyGUI: 导出工会窗口绑定报告失败：" + ex.Message);
            }
        }

        private static void TryBindMobileGuildWindowIfDue(string windowKey, GComponent window, string resolveInfo)
        {
            if (window == null || window._disposed)
                return;

            MobileGuildWindowBinding binding = _mobileGuildBinding;
            if (binding != null && (binding.Window == null || binding.Window._disposed))
            {
                ResetMobileGuildBindings();
                binding = null;
            }

            if (binding == null || !ReferenceEquals(binding.Window, window))
            {
                ResetMobileGuildBindings();
                binding = new MobileGuildWindowBinding
                {
                    WindowKey = windowKey,
                    Window = window,
                    ResolveInfo = resolveInfo,
                };

                _mobileGuildBinding = binding;
                _mobileGuildBindingsDumped = false;
                _nextMobileGuildBindAttemptUtc = DateTime.MinValue;
            }

            bool memberListOk = binding.MemberList != null && !binding.MemberList._disposed && binding.MemberItemRenderer != null;
            bool controlsOk =
                binding.NoticeField != null && !binding.NoticeField._disposed &&
                binding.InviteInput != null && !binding.InviteInput._disposed &&
                binding.InviteButton != null && !binding.InviteButton._disposed &&
                binding.KickButton != null && !binding.KickButton._disposed;

            bool statusOk =
                binding.StatusLevel != null && !binding.StatusLevel._disposed &&
                binding.StatusExp != null && !binding.StatusExp._disposed &&
                binding.StatusMembers != null && !binding.StatusMembers._disposed &&
                binding.StatusOptions != null && !binding.StatusOptions._disposed;

            if (memberListOk && controlsOk && statusOk)
                return;

            if (DateTime.UtcNow < _nextMobileGuildBindAttemptUtc)
                return;

            _nextMobileGuildBindAttemptUtc = DateTime.UtcNow.AddSeconds(2);

            string noticeSpec = string.Empty;
            string memberListSpec = string.Empty;
            string inviteInputSpec = string.Empty;
            string inviteButtonSpec = string.Empty;
            string kickButtonSpec = string.Empty;
            string guildNameSpec = string.Empty;
            string guildRankSpec = string.Empty;
            string levelSpec = string.Empty;
            string expSpec = string.Empty;
            string expBarSpec = string.Empty;
            string memberCountSpec = string.Empty;
            string goldSpec = string.Empty;
            string pointsSpec = string.Empty;
            string optionsSpec = string.Empty;

            try
            {
                InIReader reader = TryCreateConfigReader();
                if (reader != null)
                {
                    noticeSpec = reader.ReadString(FairyGuiConfigSectionName, MobileGuildNoticeConfigKey, string.Empty, writeWhenNull: false);
                    memberListSpec = reader.ReadString(FairyGuiConfigSectionName, MobileGuildMemberListConfigKey, string.Empty, writeWhenNull: false);
                    inviteInputSpec = reader.ReadString(FairyGuiConfigSectionName, MobileGuildInviteInputConfigKey, string.Empty, writeWhenNull: false);
                    inviteButtonSpec = reader.ReadString(FairyGuiConfigSectionName, MobileGuildInviteButtonConfigKey, string.Empty, writeWhenNull: false);
                    kickButtonSpec = reader.ReadString(FairyGuiConfigSectionName, MobileGuildKickButtonConfigKey, string.Empty, writeWhenNull: false);
                    guildNameSpec = reader.ReadString(FairyGuiConfigSectionName, MobileGuildNameConfigKey, string.Empty, writeWhenNull: false);
                    guildRankSpec = reader.ReadString(FairyGuiConfigSectionName, MobileGuildRankConfigKey, string.Empty, writeWhenNull: false);
                    levelSpec = reader.ReadString(FairyGuiConfigSectionName, MobileGuildLevelConfigKey, string.Empty, writeWhenNull: false);
                    expSpec = reader.ReadString(FairyGuiConfigSectionName, MobileGuildExpConfigKey, string.Empty, writeWhenNull: false);
                    expBarSpec = reader.ReadString(FairyGuiConfigSectionName, MobileGuildExpBarConfigKey, string.Empty, writeWhenNull: false);
                    memberCountSpec = reader.ReadString(FairyGuiConfigSectionName, MobileGuildMemberCountConfigKey, string.Empty, writeWhenNull: false);
                    goldSpec = reader.ReadString(FairyGuiConfigSectionName, MobileGuildGoldConfigKey, string.Empty, writeWhenNull: false);
                    pointsSpec = reader.ReadString(FairyGuiConfigSectionName, MobileGuildPointsConfigKey, string.Empty, writeWhenNull: false);
                    optionsSpec = reader.ReadString(FairyGuiConfigSectionName, MobileGuildOptionsConfigKey, string.Empty, writeWhenNull: false);
                }
            }
            catch
            {
                noticeSpec = string.Empty;
                memberListSpec = string.Empty;
                inviteInputSpec = string.Empty;
                inviteButtonSpec = string.Empty;
                kickButtonSpec = string.Empty;
                guildNameSpec = string.Empty;
                guildRankSpec = string.Empty;
                levelSpec = string.Empty;
                expSpec = string.Empty;
                expBarSpec = string.Empty;
                memberCountSpec = string.Empty;
                goldSpec = string.Empty;
                pointsSpec = string.Empty;
                optionsSpec = string.Empty;
            }

            noticeSpec = noticeSpec?.Trim() ?? string.Empty;
            memberListSpec = memberListSpec?.Trim() ?? string.Empty;
            inviteInputSpec = inviteInputSpec?.Trim() ?? string.Empty;
            inviteButtonSpec = inviteButtonSpec?.Trim() ?? string.Empty;
            kickButtonSpec = kickButtonSpec?.Trim() ?? string.Empty;
            guildNameSpec = guildNameSpec?.Trim() ?? string.Empty;
            guildRankSpec = guildRankSpec?.Trim() ?? string.Empty;
            levelSpec = levelSpec?.Trim() ?? string.Empty;
            expSpec = expSpec?.Trim() ?? string.Empty;
            expBarSpec = expBarSpec?.Trim() ?? string.Empty;
            memberCountSpec = memberCountSpec?.Trim() ?? string.Empty;
            goldSpec = goldSpec?.Trim() ?? string.Empty;
            pointsSpec = pointsSpec?.Trim() ?? string.Empty;
            optionsSpec = optionsSpec?.Trim() ?? string.Empty;

            binding.NoticeOverrideSpec = noticeSpec;
            binding.NoticeOverrideKeywords = null;
            binding.MemberListOverrideSpec = memberListSpec;
            binding.MemberListOverrideKeywords = null;
            binding.InviteInputOverrideSpec = inviteInputSpec;
            binding.InviteInputOverrideKeywords = null;
            binding.InviteButtonOverrideSpec = inviteButtonSpec;
            binding.InviteButtonOverrideKeywords = null;
            binding.KickButtonOverrideSpec = kickButtonSpec;
            binding.KickButtonOverrideKeywords = null;

            binding.StatusGuildNameOverrideSpec = guildNameSpec;
            binding.StatusGuildNameOverrideKeywords = null;
            binding.StatusGuildRankOverrideSpec = guildRankSpec;
            binding.StatusGuildRankOverrideKeywords = null;
            binding.StatusLevelOverrideSpec = levelSpec;
            binding.StatusLevelOverrideKeywords = null;
            binding.StatusExpOverrideSpec = expSpec;
            binding.StatusExpOverrideKeywords = null;
            binding.StatusExpBarOverrideSpec = expBarSpec;
            binding.StatusExpBarOverrideKeywords = null;
            binding.StatusMembersOverrideSpec = memberCountSpec;
            binding.StatusMembersOverrideKeywords = null;
            binding.StatusGoldOverrideSpec = goldSpec;
            binding.StatusGoldOverrideKeywords = null;
            binding.StatusPointsOverrideSpec = pointsSpec;
            binding.StatusPointsOverrideKeywords = null;
            binding.StatusOptionsOverrideSpec = optionsSpec;
            binding.StatusOptionsOverrideKeywords = null;

            if (binding.NoticeField == null || binding.NoticeField._disposed)
            {
                if (!string.IsNullOrWhiteSpace(noticeSpec))
                {
                    if (TryResolveMobileMainHudObjectBySpec(window, noticeSpec, out GObject resolved, out string[] keywords))
                    {
                        if (resolved is GTextField resolvedField && resolved is not GTextInput && !resolvedField._disposed)
                        {
                            binding.NoticeField = resolvedField;
                            binding.NoticeResolveInfo = DescribeObject(window, resolvedField) + " (override)";
                        }
                        else if (keywords != null && keywords.Length > 0)
                        {
                            binding.NoticeOverrideKeywords = keywords;
                        }
                    }
                    else
                    {
                        binding.NoticeOverrideKeywords = SplitKeywords(noticeSpec);
                    }
                }

                if (binding.NoticeField == null)
                {
                    string[] keywordsUsed = binding.NoticeOverrideKeywords != null && binding.NoticeOverrideKeywords.Length > 0
                        ? binding.NoticeOverrideKeywords
                        : DefaultGuildNoticeKeywords;

                    List<(int Score, GObject Target)> candidates = CollectMobileChatCandidates(window, obj => obj is GTextField && obj is not GTextInput, keywordsUsed, ScoreMobileShopTextCandidate);
                    GTextField field = SelectMobileChatCandidate<GTextField>(candidates, minScore: 40);
                    if (field != null && !field._disposed)
                    {
                        binding.NoticeField = field;
                        binding.NoticeResolveInfo = DescribeObject(window, field) + (binding.NoticeOverrideKeywords != null && binding.NoticeOverrideKeywords.Length > 0 ? " (keywords)" : " (auto)");
                    }
                }
            }

            GList memberList = binding.MemberList;
            if (memberList != null && memberList._disposed)
                memberList = null;

            GComponent searchRoot = window;
            string memberListResolveInfo = null;
            List<(int Score, GObject Target)> memberListCandidates = null;

            if (!string.IsNullOrWhiteSpace(memberListSpec))
            {
                if (TryResolveMobileMainHudObjectBySpec(window, memberListSpec, out GObject resolved, out string[] keywords))
                {
                    if (resolved is GList resolvedList && !resolvedList._disposed)
                    {
                        memberList = resolvedList;
                        memberListResolveInfo = DescribeObject(window, resolvedList) + " (override)";
                    }
                    else if (resolved is GComponent resolvedComponent && !resolvedComponent._disposed)
                    {
                        searchRoot = resolvedComponent;
                        memberListResolveInfo = DescribeObject(window, resolvedComponent) + " (searchRoot override)";
                    }
                    else if (keywords != null && keywords.Length > 0)
                    {
                        binding.MemberListOverrideKeywords = keywords;
                    }
                }
                else
                {
                    binding.MemberListOverrideKeywords = SplitKeywords(memberListSpec);
                }
            }

            string[] memberListKeywordsUsed = binding.MemberListOverrideKeywords != null && binding.MemberListOverrideKeywords.Length > 0
                ? binding.MemberListOverrideKeywords
                : DefaultGuildMemberListKeywords;

            if (memberList == null)
            {
                int minScore = binding.MemberListOverrideKeywords != null && binding.MemberListOverrideKeywords.Length > 0 ? 40 : 60;
                memberListCandidates = CollectMobileChatCandidates(searchRoot, obj => obj is GList && obj.touchable, memberListKeywordsUsed, ScoreMobileShopListCandidate);
                memberList = SelectMobileChatCandidate<GList>(memberListCandidates, minScore);
                if (memberList != null && !memberList._disposed)
                    memberListResolveInfo = DescribeObject(window, memberList) + (binding.MemberListOverrideKeywords != null && binding.MemberListOverrideKeywords.Length > 0 ? " (keywords)" : " (auto)");
            }

            if (memberList != null && !memberList._disposed)
            {
                binding.MemberList = memberList;
                binding.MemberListResolveInfo = memberListResolveInfo;

                try
                {
                    if (!binding.MemberList.isVirtual && binding.MemberList.scrollPane != null)
                        binding.MemberList.SetVirtual();
                }
                catch
                {
                }

                if (binding.MemberItemRenderer == null)
                    binding.MemberItemRenderer = RenderMobileGuildMemberListItem;

                try
                {
                    binding.MemberList.itemRenderer = binding.MemberItemRenderer;
                }
                catch
                {
                }
            }

            GTextInput ResolveTextInput(string spec, string[] defaultKeywords, out string[] overrideKeywords)
            {
                overrideKeywords = null;

                if (!string.IsNullOrWhiteSpace(spec))
                {
                    if (TryResolveMobileMainHudObjectBySpec(window, spec, out GObject resolved, out string[] keywords))
                    {
                        if (resolved is GTextInput resolvedInput && !resolvedInput._disposed)
                            return resolvedInput;

                        if (keywords != null && keywords.Length > 0)
                            overrideKeywords = keywords;
                    }
                    else
                    {
                        overrideKeywords = SplitKeywords(spec);
                    }
                }

                string[] keywordsUsed = overrideKeywords != null && overrideKeywords.Length > 0 ? overrideKeywords : defaultKeywords;
                List<(int Score, GObject Target)> candidates = CollectMobileChatCandidates(window, obj => obj is GTextInput && obj.touchable, keywordsUsed, ScoreMobileChatInputCandidate);
                return SelectMobileChatCandidate<GTextInput>(candidates, minScore: 40);
            }

            GButton ResolveButton(string spec, string[] defaultKeywords, out string[] overrideKeywords)
            {
                overrideKeywords = null;

                if (!string.IsNullOrWhiteSpace(spec))
                {
                    if (TryResolveMobileMainHudObjectBySpec(window, spec, out GObject resolved, out string[] keywords))
                    {
                        if (resolved is GButton resolvedButton && !resolvedButton._disposed)
                            return resolvedButton;

                        if (keywords != null && keywords.Length > 0)
                            overrideKeywords = keywords;
                    }
                    else
                    {
                        overrideKeywords = SplitKeywords(spec);
                    }
                }

                string[] keywordsUsed = overrideKeywords != null && overrideKeywords.Length > 0 ? overrideKeywords : defaultKeywords;
                List<(int Score, GObject Target)> candidates = CollectMobileChatCandidates(window, obj => obj is GButton && obj.touchable, keywordsUsed, ScoreMobileShopButtonCandidate);
                return SelectMobileChatCandidate<GButton>(candidates, minScore: 30);
            }

            bool IsDescendantOf(GObject child, GObject ancestor)
            {
                if (child == null || ancestor == null)
                    return false;

                try
                {
                    GObject current = child;
                    while (current != null && !current._disposed)
                    {
                        if (ReferenceEquals(current, ancestor))
                            return true;
                        current = current.parent;
                    }
                }
                catch
                {
                }

                return false;
            }

            int ScoreMobileGuildStatusTextCandidate(GObject obj, string[] keywords)
            {
                int score = ScoreAnyField(obj, keywords, equalsWeight: 160, startsWithWeight: 80, containsWeight: 25);
                score += ScoreRect(obj, preferLower: false, areaDivisor: 1000, maxAreaScore: 200);

                try
                {
                    var rect = obj.LocalToGlobal(new System.Drawing.RectangleF(0, 0, obj.width, obj.height));
                    float y = rect.Y;
                    score += (int)Math.Min(80, Math.Max(0, 520f - y) / 10f);
                }
                catch
                {
                }

                if (obj.packageItem?.exported == true)
                    score += 10;

                return score;
            }

            int ScoreMobileGuildStatusBarCandidate(GObject obj, string[] keywords)
            {
                int score = ScoreAnyField(obj, keywords, equalsWeight: 140, startsWithWeight: 70, containsWeight: 20);
                score += ScoreRect(obj, preferLower: false, areaDivisor: 900, maxAreaScore: 220);

                try
                {
                    var rect = obj.LocalToGlobal(new System.Drawing.RectangleF(0, 0, obj.width, obj.height));
                    float y = rect.Y;
                    score += (int)Math.Min(60, Math.Max(0, 520f - y) / 10f);
                }
                catch
                {
                }

                if (obj.packageItem?.exported == true)
                    score += 10;

                return score;
            }

            bool ShouldExcludeFromStatusBinding(GObject obj)
            {
                if (obj == null || obj._disposed)
                    return true;

                if (binding.MemberList != null && !binding.MemberList._disposed && IsDescendantOf(obj, binding.MemberList))
                    return true;

                if (binding.NoticeField != null && !binding.NoticeField._disposed && ReferenceEquals(obj, binding.NoticeField))
                    return true;

                return false;
            }

            GTextField ResolveStatusTextField(string spec, string[] defaultKeywords, out string resolveInfoOut, out string[] overrideKeywords)
            {
                resolveInfoOut = null;
                overrideKeywords = null;

                if (!string.IsNullOrWhiteSpace(spec))
                {
                    if (TryResolveMobileMainHudObjectBySpec(window, spec, out GObject resolved, out string[] keywords))
                    {
                        if (resolved is GTextField resolvedField && resolved is not GTextInput && !resolvedField._disposed)
                        {
                            resolveInfoOut = DescribeObject(window, resolvedField) + " (override)";
                            return resolvedField;
                        }

                        if (keywords != null && keywords.Length > 0)
                            overrideKeywords = keywords;
                    }
                    else
                    {
                        overrideKeywords = SplitKeywords(spec);
                    }
                }

                string[] keywordsUsed = overrideKeywords != null && overrideKeywords.Length > 0 ? overrideKeywords : defaultKeywords;
                List<(int Score, GObject Target)> candidates = CollectMobileChatCandidates(
                    window,
                    obj => obj is GTextField && obj is not GTextInput && !ShouldExcludeFromStatusBinding(obj),
                    keywordsUsed,
                    ScoreMobileGuildStatusTextCandidate);

                GTextField selected = SelectMobileChatCandidate<GTextField>(candidates, minScore: 35);
                if (selected != null && !selected._disposed)
                {
                    resolveInfoOut = DescribeObject(window, selected) + (overrideKeywords != null && overrideKeywords.Length > 0 ? " (keywords)" : " (auto)");
                }

                return selected;
            }

            GProgressBar ResolveStatusProgressBar(string spec, string[] defaultKeywords, out string resolveInfoOut, out string[] overrideKeywords)
            {
                resolveInfoOut = null;
                overrideKeywords = null;

                if (!string.IsNullOrWhiteSpace(spec))
                {
                    if (TryResolveMobileMainHudObjectBySpec(window, spec, out GObject resolved, out string[] keywords))
                    {
                        if (resolved is GProgressBar resolvedBar && !resolvedBar._disposed)
                        {
                            resolveInfoOut = DescribeObject(window, resolvedBar) + " (override)";
                            return resolvedBar;
                        }

                        if (keywords != null && keywords.Length > 0)
                            overrideKeywords = keywords;
                    }
                    else
                    {
                        overrideKeywords = SplitKeywords(spec);
                    }
                }

                string[] keywordsUsed = overrideKeywords != null && overrideKeywords.Length > 0 ? overrideKeywords : defaultKeywords;
                List<(int Score, GObject Target)> candidates = CollectMobileChatCandidates(
                    window,
                    obj => obj is GProgressBar && !ShouldExcludeFromStatusBinding(obj),
                    keywordsUsed,
                    ScoreMobileGuildStatusBarCandidate);

                GProgressBar selected = SelectMobileChatCandidate<GProgressBar>(candidates, minScore: 30);
                if (selected != null && !selected._disposed)
                {
                    resolveInfoOut = DescribeObject(window, selected) + (overrideKeywords != null && overrideKeywords.Length > 0 ? " (keywords)" : " (auto)");
                }

                return selected;
            }

            if (binding.InviteInput == null || binding.InviteInput._disposed)
                binding.InviteInput = ResolveTextInput(inviteInputSpec, DefaultGuildInviteInputKeywords, out binding.InviteInputOverrideKeywords);

            if (binding.InviteButton == null || binding.InviteButton._disposed)
                binding.InviteButton = ResolveButton(inviteButtonSpec, DefaultGuildInviteButtonKeywords, out binding.InviteButtonOverrideKeywords);

            if (binding.KickButton == null || binding.KickButton._disposed)
                binding.KickButton = ResolveButton(kickButtonSpec, DefaultGuildKickButtonKeywords, out binding.KickButtonOverrideKeywords);

            if (binding.StatusGuildName == null || binding.StatusGuildName._disposed)
                binding.StatusGuildName = ResolveStatusTextField(guildNameSpec, DefaultGuildNameKeywords, out binding.StatusGuildNameResolveInfo, out binding.StatusGuildNameOverrideKeywords);

            if (binding.StatusGuildRank == null || binding.StatusGuildRank._disposed)
                binding.StatusGuildRank = ResolveStatusTextField(guildRankSpec, DefaultGuildRankNameKeywords, out binding.StatusGuildRankResolveInfo, out binding.StatusGuildRankOverrideKeywords);

            if (binding.StatusLevel == null || binding.StatusLevel._disposed)
                binding.StatusLevel = ResolveStatusTextField(levelSpec, DefaultGuildLevelKeywords, out binding.StatusLevelResolveInfo, out binding.StatusLevelOverrideKeywords);

            if (binding.StatusExp == null || binding.StatusExp._disposed)
                binding.StatusExp = ResolveStatusTextField(expSpec, DefaultGuildExpKeywords, out binding.StatusExpResolveInfo, out binding.StatusExpOverrideKeywords);

            if (binding.StatusExpBar == null || binding.StatusExpBar._disposed)
                binding.StatusExpBar = ResolveStatusProgressBar(expBarSpec, DefaultGuildExpBarKeywords, out binding.StatusExpBarResolveInfo, out binding.StatusExpBarOverrideKeywords);

            if (binding.StatusMembers == null || binding.StatusMembers._disposed)
                binding.StatusMembers = ResolveStatusTextField(memberCountSpec, DefaultGuildMemberCountKeywords, out binding.StatusMembersResolveInfo, out binding.StatusMembersOverrideKeywords);

            if (binding.StatusGold == null || binding.StatusGold._disposed)
                binding.StatusGold = ResolveStatusTextField(goldSpec, DefaultGuildGoldKeywords, out binding.StatusGoldResolveInfo, out binding.StatusGoldOverrideKeywords);

            if (binding.StatusPoints == null || binding.StatusPoints._disposed)
                binding.StatusPoints = ResolveStatusTextField(pointsSpec, DefaultGuildPointsKeywords, out binding.StatusPointsResolveInfo, out binding.StatusPointsOverrideKeywords);

            if (binding.StatusOptions == null || binding.StatusOptions._disposed)
                binding.StatusOptions = ResolveStatusTextField(optionsSpec, DefaultGuildOptionsKeywords, out binding.StatusOptionsResolveInfo, out binding.StatusOptionsOverrideKeywords);

            try
            {
                if (binding.InviteClickCallback == null)
                {
                    binding.InviteClickCallback = () =>
                    {
                        string text = string.Empty;
                        try { text = binding.InviteInput?.text ?? string.Empty; } catch { text = string.Empty; }
                        if (!TryValidateCharacterName(text, "工会邀请", out string cleaned))
                            return;

                        ApplyMobileButtonCooldown(binding.InviteButton);
                        TrySendMobileGuildInvite(cleaned);
                        try { if (binding.InviteInput != null && !binding.InviteInput._disposed) binding.InviteInput.text = string.Empty; } catch { }
                    };
                }

                if (binding.InviteButton != null && !binding.InviteButton._disposed)
                {
                    binding.InviteButton.onClick.Remove(binding.InviteClickCallback);
                    binding.InviteButton.onClick.Add(binding.InviteClickCallback);
                }
            }
            catch
            {
            }

            try
            {
                if (binding.KickClickCallback == null)
                {
                    binding.KickClickCallback = () =>
                    {
                        int selected = binding.SelectedIndex;
                        if (selected < 0 || selected >= MobileGuildMembers.Count)
                        {
                            MobileHint("请先选择要踢出的成员。");
                            return;
                        }

                        string name = MobileGuildMembers[selected]?.Name ?? string.Empty;
                        if (!TryValidateCharacterName(name, "工会踢人", out string cleaned))
                            return;

                        ApplyMobileButtonCooldown(binding.KickButton);
                        TrySendMobileGuildKick(cleaned);
                    };
                }

                if (binding.KickButton != null && !binding.KickButton._disposed)
                {
                    binding.KickButton.onClick.Remove(binding.KickClickCallback);
                    binding.KickButton.onClick.Add(binding.KickClickCallback);
                }
            }
            catch
            {
            }

            _mobileGuildDirty = true;
            TryDumpMobileGuildBindingsReportIfDue(binding, memberListKeywordsUsed, memberListCandidates ?? new List<(int Score, GObject Target)>());
        }

        private static void TryRefreshMobileGuildIfDue(bool force)
        {
            if (_stage == null || !_initialized || !_packagesLoaded)
                return;

            if (!MobileWindows.TryGetValue("Guild", out GComponent window) || window == null || window._disposed)
            {
                if (_mobileGuildBinding != null)
                    ResetMobileGuildBindings();
                return;
            }

            if (!window.visible)
                return;

            TryBindMobileGuildWindowIfDue("Guild", window, resolveInfo: null);

            MobileGuildWindowBinding binding = _mobileGuildBinding;
            if (binding == null || binding.Window == null || binding.Window._disposed)
            {
                ResetMobileGuildBindings();
                return;
            }

            if (!force && !_mobileGuildDirty)
                return;

            _mobileGuildDirty = false;

            int count = MobileGuildMembers.Count;
            if (binding.SelectedIndex >= count)
                binding.SelectedIndex = count - 1;
            if (binding.SelectedIndex < 0 || count == 0)
                binding.SelectedIndex = -1;

            try
            {
                if (binding.NoticeField != null && !binding.NoticeField._disposed)
                {
                    string text = MobileGuildNoticeLines.Count > 0 ? string.Join("\n", MobileGuildNoticeLines) : string.Empty;
                    if (!string.Equals(binding.NoticeField.text, text, StringComparison.Ordinal))
                        binding.NoticeField.text = text;
                }
            }
            catch
            {
            }

            try
            {
                bool hasGuild = !string.IsNullOrWhiteSpace(_mobileGuildStatusGuildName);

                string guildName = hasGuild ? (_mobileGuildStatusGuildName ?? string.Empty) : string.Empty;
                string guildRank = hasGuild ? (_mobileGuildStatusRankName ?? string.Empty) : string.Empty;

                string levelText = hasGuild && _mobileGuildStatusLevel > 0 ? _mobileGuildStatusLevel.ToString() : string.Empty;

                long exp = hasGuild ? _mobileGuildStatusExperience : 0;
                long maxExp = hasGuild ? _mobileGuildStatusMaxExperience : 0;
                string expText = hasGuild
                    ? (maxExp > 0 ? (exp + "/" + maxExp) : exp.ToString())
                    : string.Empty;

                string membersText = hasGuild
                    ? (_mobileGuildStatusMemberCount + "/" + _mobileGuildStatusMaxMembers)
                    : string.Empty;

                string goldText = hasGuild ? _mobileGuildStatusGold.ToString() : string.Empty;
                string pointsText = hasGuild ? _mobileGuildStatusSparePoints.ToString() : string.Empty;
                string optionsText = hasGuild ? FormatMobileGuildOptions(_mobileGuildStatusMyOptions) : string.Empty;

                if (binding.StatusGuildName != null && !binding.StatusGuildName._disposed && !string.Equals(binding.StatusGuildName.text, guildName, StringComparison.Ordinal))
                    binding.StatusGuildName.text = guildName;

                if (binding.StatusGuildRank != null && !binding.StatusGuildRank._disposed && !string.Equals(binding.StatusGuildRank.text, guildRank, StringComparison.Ordinal))
                    binding.StatusGuildRank.text = guildRank;

                if (binding.StatusLevel != null && !binding.StatusLevel._disposed && !string.Equals(binding.StatusLevel.text, levelText, StringComparison.Ordinal))
                    binding.StatusLevel.text = levelText;

                if (binding.StatusExp != null && !binding.StatusExp._disposed && !string.Equals(binding.StatusExp.text, expText, StringComparison.Ordinal))
                    binding.StatusExp.text = expText;

                if (binding.StatusMembers != null && !binding.StatusMembers._disposed && !string.Equals(binding.StatusMembers.text, membersText, StringComparison.Ordinal))
                    binding.StatusMembers.text = membersText;

                if (binding.StatusGold != null && !binding.StatusGold._disposed && !string.Equals(binding.StatusGold.text, goldText, StringComparison.Ordinal))
                    binding.StatusGold.text = goldText;

                if (binding.StatusPoints != null && !binding.StatusPoints._disposed && !string.Equals(binding.StatusPoints.text, pointsText, StringComparison.Ordinal))
                    binding.StatusPoints.text = pointsText;

                if (binding.StatusOptions != null && !binding.StatusOptions._disposed && !string.Equals(binding.StatusOptions.text, optionsText, StringComparison.Ordinal))
                    binding.StatusOptions.text = optionsText;

                if (binding.StatusExpBar != null && !binding.StatusExpBar._disposed)
                {
                    double barMax = Math.Max(1, maxExp);
                    double barValue = Math.Clamp(exp, 0, (long)barMax);

                    if (binding.StatusExpBar.max != barMax)
                        binding.StatusExpBar.max = barMax;

                    if (binding.StatusExpBar.value != barValue)
                        binding.StatusExpBar.value = barValue;
                }
            }
            catch
            {
            }

            try
            {
                if (binding.MemberList != null && !binding.MemberList._disposed)
                {
                    if (binding.MemberItemRenderer == null)
                        binding.MemberItemRenderer = RenderMobileGuildMemberListItem;

                    binding.MemberList.itemRenderer = binding.MemberItemRenderer;
                    binding.MemberList.numItems = count;
                }
            }
            catch (Exception ex)
            {
                CMain.SaveError("FairyGUI: 刷新工会窗口失败：" + ex.Message);
                _nextMobileGuildBindAttemptUtc = DateTime.MinValue;
                _mobileGuildDirty = true;
            }
        }
    }
}
