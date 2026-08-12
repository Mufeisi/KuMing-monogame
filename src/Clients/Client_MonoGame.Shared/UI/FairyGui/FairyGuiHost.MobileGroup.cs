using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using C = ClientPackets;
using FairyGUI;
using MonoShare.MirScenes;

namespace MonoShare
{
    internal static partial class FairyGuiHost
    {
        private static bool CanCurrentUserOperateMobileGroupAsLeader(string actionText, bool assumeLeaderIsSelfWhenNotInGroup)
        {
            try
            {
                GameScene scene = GameScene.Scene;
                if (scene == null)
                    return true;

                if (!scene.MobileGroupActive)
                {
                    if (assumeLeaderIsSelfWhenNotInGroup)
                        scene.AssumeMobileGroupLeaderIsSelf();
                    return true;
                }

                string leaderName = scene.MobileGroupLeaderName ?? string.Empty;
                if (string.IsNullOrWhiteSpace(leaderName))
                    return true;

                string myName = GameScene.User?.Name ?? string.Empty;
                if (string.IsNullOrWhiteSpace(myName))
                    return true;

                if (string.Equals(leaderName, myName, StringComparison.OrdinalIgnoreCase))
                    return true;

                scene.MobileReceiveChat("[组队] 只有队长可以" + (actionText ?? "操作") + "。", ChatType.Hint);
                return false;
            }
            catch
            {
                return true;
            }
        }

        private static void OnMobileGroupMemberItemClicked(MobileGroupMemberItemView view)
        {
            if (view == null)
                return;

            MobileGroupWindowBinding binding = _mobileGroupBinding;
            if (binding == null || binding.Window == null || binding.Window._disposed)
                return;

            int index = view.Index;
            if (index < 0 || index >= binding.MemberNames.Count)
                return;

            if (binding.SelectedIndex == index)
                return;

            binding.SelectedIndex = index;

            try
            {
                _mobileGroupDirty = true;
            }
            catch
            {
            }
        }

        private static MobileGroupMemberItemView GetOrCreateMobileGroupMemberItemView(GComponent itemRoot)
        {
            if (itemRoot == null || itemRoot._disposed)
                return null;

            if (itemRoot.data is MobileGroupMemberItemView existing && existing.Root != null && !existing.Root._disposed)
                return existing;

            var view = new MobileGroupMemberItemView
            {
                Root = itemRoot,
                Index = -1,
            };

            try
            {
                List<(int Score, GObject Target)> nameCandidates =
                    CollectMobileChatCandidates(itemRoot, obj => obj is GTextField && obj is not GTextInput, DefaultGroupMemberNameKeywords, ScoreMobileShopTextCandidate);
                view.Name = SelectMobileChatCandidate<GTextField>(nameCandidates, minScore: 15);
            }
            catch
            {
            }

            try
            {
                List<(int Score, GObject Target)> mapCandidates =
                    CollectMobileChatCandidates(itemRoot, obj => obj is GTextField && obj is not GTextInput, DefaultGroupMemberMapKeywords, ScoreMobileShopTextCandidate);
                view.Map = SelectMobileChatCandidate<GTextField>(mapCandidates, minScore: 15);
            }
            catch
            {
            }

            try
            {
                List<(int Score, GObject Target)> locCandidates =
                    CollectMobileChatCandidates(itemRoot, obj => obj is GTextField && obj is not GTextInput, DefaultGroupMemberLocationKeywords, ScoreMobileShopTextCandidate);
                view.Location = SelectMobileChatCandidate<GTextField>(locCandidates, minScore: 15);
            }
            catch
            {
            }

            try
            {
                if (view.ClickCallback == null)
                {
                    view.ClickCallback = () => OnMobileGroupMemberItemClicked(view);
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

        private static void ClearMobileGroupMemberItemView(MobileGroupMemberItemView view)
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
                if (view.Map != null && !view.Map._disposed)
                    view.Map.text = string.Empty;
            }
            catch
            {
            }

            try
            {
                if (view.Location != null && !view.Location._disposed)
                    view.Location.text = string.Empty;
            }
            catch
            {
            }
        }

        private static void RenderMobileGroupMemberListItem(int index, GObject itemObject)
        {
            if (itemObject == null || itemObject._disposed)
                return;

            if (itemObject is not GComponent itemRoot || itemRoot._disposed)
                return;

            MobileGroupWindowBinding binding = _mobileGroupBinding;
            if (binding == null || binding.Window == null || binding.Window._disposed)
                return;

            MobileGroupMemberItemView view = GetOrCreateMobileGroupMemberItemView(itemRoot);
            if (view == null)
                return;

            view.Index = index;

            bool selected = binding.SelectedIndex == index;

            try
            {
                if (itemObject is GButton button && !button._disposed)
                    button.selected = selected;
            }
            catch
            {
            }

            if (index < 0 || index >= binding.MemberNames.Count)
            {
                ClearMobileGroupMemberItemView(view);
                return;
            }

            string memberName = binding.MemberNames[index] ?? string.Empty;

            string mapName = string.Empty;
            string locationText = string.Empty;

            try
            {
                IReadOnlyDictionary<string, string> maps = GameScene.Scene?.MobileGroupMemberMaps;
                if (maps != null && maps.TryGetValue(memberName, out string mapValue))
                    mapName = mapValue ?? string.Empty;
            }
            catch
            {
                mapName = string.Empty;
            }

            try
            {
                IReadOnlyDictionary<string, System.Drawing.Point> locations = GameScene.Scene?.MobileGroupMemberLocations;
                if (locations != null && locations.TryGetValue(memberName, out System.Drawing.Point location) && location != default)
                    locationText = $"{location.X}:{location.Y}";
            }
            catch
            {
                locationText = string.Empty;
            }

            try
            {
                if (view.Name != null && !view.Name._disposed)
                    view.Name.text = memberName;
            }
            catch
            {
            }

            try
            {
                if (view.Map != null && !view.Map._disposed)
                    view.Map.text = mapName;
            }
            catch
            {
            }

            try
            {
                if (view.Location != null && !view.Location._disposed)
                    view.Location.text = locationText;
            }
            catch
            {
            }
        }

        private static void TrySendMobileGroupInvite(string name)
        {
            if (!TryValidateCharacterName(name, "组队邀请", out string cleaned))
                return;

            try
            {
                MonoShare.MirNetwork.Network.Enqueue(new C.AddMember { Name = cleaned });
            }
            catch (Exception ex)
            {
                CMain.SaveError("FairyGUI: 发送组队邀请失败：" + ex.Message);
                MobileHint("网络异常：组队邀请发送失败。");
            }
        }

        private static void TrySendMobileGroupKick(string name)
        {
            if (!TryValidateCharacterName(name, "组队踢人", out string cleaned))
                return;

            try
            {
                MonoShare.MirNetwork.Network.Enqueue(new C.DelMember { Name = cleaned });
            }
            catch (Exception ex)
            {
                CMain.SaveError("FairyGUI: 发送组队踢人失败：" + ex.Message);
                MobileHint("网络异常：组队踢人失败。");
            }
        }

        private static void TrySendMobileGroupLeave()
        {
            try
            {
                MonoShare.MirNetwork.Network.Enqueue(new C.SwitchGroup { AllowGroup = false });
            }
            catch (Exception ex)
            {
                CMain.SaveError("FairyGUI: 发送退出组队失败：" + ex.Message);
                MobileHint("网络异常：退出组队失败。");
            }
        }

        private static void TryDumpMobileGroupBindingsReportIfDue(
            MobileGroupWindowBinding binding,
            string[] memberListKeywordsUsed,
            List<(int Score, GObject Target)> memberListCandidates)
        {
            if (!Settings.DebugMode || _mobileGroupBindingsDumped)
                return;

            if (binding == null || binding.Window == null || binding.Window._disposed)
                return;

            try
            {
                Directory.CreateDirectory(ClientResourceLayout.RuntimeRoot);

                string path = Path.Combine(ClientResourceLayout.RuntimeRoot, "FairyGui-MobileGroupBindings.txt");

                var builder = new StringBuilder(14 * 1024);
                builder.AppendLine("FairyGUI 组队窗口绑定报告（Group）");
                builder.AppendLine($"GeneratedAtUtc={DateTime.UtcNow:o}");
                builder.AppendLine($"WindowKey={binding.WindowKey ?? "Group"}");
                builder.AppendLine($"WindowResolveInfo={binding.ResolveInfo ?? "-"}");
                builder.AppendLine();

                builder.AppendLine($"MemberList={DescribeObject(binding.Window, binding.MemberList)}");
                builder.AppendLine($"MemberListResolveInfo={binding.MemberListResolveInfo ?? "-"}");
                builder.AppendLine($"OverrideSpec={binding.MemberListOverrideSpec ?? "-"}");
                builder.AppendLine($"OverrideKeywords={(binding.MemberListOverrideKeywords == null ? "-" : string.Join("|", binding.MemberListOverrideKeywords))}");
                builder.AppendLine($"KeywordsUsed={(memberListKeywordsUsed == null ? "-" : string.Join("|", memberListKeywordsUsed))}");
                builder.AppendLine($"Members={binding.MemberNames.Count}");
                builder.AppendLine();

                builder.AppendLine($"InviteInput={DescribeObject(binding.Window, binding.InviteInput)} OverrideSpec={binding.InviteInputOverrideSpec ?? "-"}");
                builder.AppendLine($"InviteButton={DescribeObject(binding.Window, binding.InviteButton)} OverrideSpec={binding.InviteButtonOverrideSpec ?? "-"}");
                builder.AppendLine($"KickButton={DescribeObject(binding.Window, binding.KickButton)} OverrideSpec={binding.KickButtonOverrideSpec ?? "-"}");
                builder.AppendLine($"LeaveButton={DescribeObject(binding.Window, binding.LeaveButton)} OverrideSpec={binding.LeaveButtonOverrideSpec ?? "-"}");
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
                builder.AppendLine($"  {MobileGroupMemberListConfigKey}=idx:...（成员列表 GList）");
                builder.AppendLine($"  {MobileGroupInviteInputConfigKey}=idx:...（邀请输入框）");
                builder.AppendLine($"  {MobileGroupInviteButtonConfigKey}=idx:...（邀请按钮）");
                builder.AppendLine($"  {MobileGroupKickButtonConfigKey}=idx:...（踢人按钮）");
                builder.AppendLine($"  {MobileGroupLeaveButtonConfigKey}=idx:...（离开按钮）");
                builder.AppendLine("说明：idx/path 均相对组队窗口 Root。建议先查看窗口树（FairyGui-MobileWindow-Group-Tree.txt），再填入精确覆盖。");

                File.WriteAllText(path, builder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                _mobileGroupBindingsDumped = true;
                CMain.SaveLog($"FairyGUI: 已导出组队窗口绑定报告到 {path}");
            }
            catch (Exception ex)
            {
                CMain.SaveError("FairyGUI: 导出组队窗口绑定报告失败：" + ex.Message);
            }
        }

        private static void TryBindMobileGroupWindowIfDue(string windowKey, GComponent window, string resolveInfo)
        {
            if (window == null || window._disposed)
                return;

            MobileGroupWindowBinding binding = _mobileGroupBinding;
            if (binding != null && (binding.Window == null || binding.Window._disposed))
            {
                ResetMobileGroupBindings();
                binding = null;
            }

            if (binding == null || !ReferenceEquals(binding.Window, window))
            {
                ResetMobileGroupBindings();
                binding = new MobileGroupWindowBinding
                {
                    WindowKey = windowKey,
                    Window = window,
                    ResolveInfo = resolveInfo,
                };

                _mobileGroupBinding = binding;
                _mobileGroupBindingsDumped = false;
                _nextMobileGroupBindAttemptUtc = DateTime.MinValue;
            }

            bool memberListOk = binding.MemberList != null && !binding.MemberList._disposed && binding.MemberItemRenderer != null;
            bool controlsOk =
                binding.InviteInput != null && !binding.InviteInput._disposed &&
                binding.InviteButton != null && !binding.InviteButton._disposed &&
                binding.KickButton != null && !binding.KickButton._disposed &&
                binding.LeaveButton != null && !binding.LeaveButton._disposed;

            if (memberListOk && controlsOk)
                return;

            if (DateTime.UtcNow < _nextMobileGroupBindAttemptUtc)
                return;

            _nextMobileGroupBindAttemptUtc = DateTime.UtcNow.AddSeconds(2);

            string memberListSpec = string.Empty;
            string inviteInputSpec = string.Empty;
            string inviteButtonSpec = string.Empty;
            string kickButtonSpec = string.Empty;
            string leaveButtonSpec = string.Empty;

            try
            {
                InIReader reader = TryCreateConfigReader();
                if (reader != null)
                {
                    memberListSpec = reader.ReadString(FairyGuiConfigSectionName, MobileGroupMemberListConfigKey, string.Empty, writeWhenNull: false);
                    inviteInputSpec = reader.ReadString(FairyGuiConfigSectionName, MobileGroupInviteInputConfigKey, string.Empty, writeWhenNull: false);
                    inviteButtonSpec = reader.ReadString(FairyGuiConfigSectionName, MobileGroupInviteButtonConfigKey, string.Empty, writeWhenNull: false);
                    kickButtonSpec = reader.ReadString(FairyGuiConfigSectionName, MobileGroupKickButtonConfigKey, string.Empty, writeWhenNull: false);
                    leaveButtonSpec = reader.ReadString(FairyGuiConfigSectionName, MobileGroupLeaveButtonConfigKey, string.Empty, writeWhenNull: false);
                }
            }
            catch
            {
                memberListSpec = string.Empty;
                inviteInputSpec = string.Empty;
                inviteButtonSpec = string.Empty;
                kickButtonSpec = string.Empty;
                leaveButtonSpec = string.Empty;
            }

            memberListSpec = memberListSpec?.Trim() ?? string.Empty;
            inviteInputSpec = inviteInputSpec?.Trim() ?? string.Empty;
            inviteButtonSpec = inviteButtonSpec?.Trim() ?? string.Empty;
            kickButtonSpec = kickButtonSpec?.Trim() ?? string.Empty;
            leaveButtonSpec = leaveButtonSpec?.Trim() ?? string.Empty;

            binding.MemberListOverrideSpec = memberListSpec;
            binding.MemberListOverrideKeywords = null;
            binding.InviteInputOverrideSpec = inviteInputSpec;
            binding.InviteInputOverrideKeywords = null;
            binding.InviteButtonOverrideSpec = inviteButtonSpec;
            binding.InviteButtonOverrideKeywords = null;
            binding.KickButtonOverrideSpec = kickButtonSpec;
            binding.KickButtonOverrideKeywords = null;
            binding.LeaveButtonOverrideSpec = leaveButtonSpec;
            binding.LeaveButtonOverrideKeywords = null;

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
                : DefaultGroupMemberListKeywords;

            if (memberList == null)
            {
                int minScore = binding.MemberListOverrideKeywords != null && binding.MemberListOverrideKeywords.Length > 0 ? 40 : 60;
                memberListCandidates = CollectMobileChatCandidates(searchRoot, obj => obj is GList && obj.touchable, memberListKeywordsUsed, ScoreMobileShopListCandidate);
                memberList = SelectMobileChatCandidate<GList>(memberListCandidates, minScore);
                if (memberList != null && !memberList._disposed)
                    memberListResolveInfo = DescribeObject(window, memberList) + (binding.MemberListOverrideKeywords != null && binding.MemberListOverrideKeywords.Length > 0 ? " (keywords)" : " (auto)");
            }

            if (memberList == null || memberList._disposed)
            {
                CMain.SaveError("FairyGUI: 组队窗口未找到成员列表（Group）。可在 Mir2Config.ini 设置 [" + FairyGuiConfigSectionName + "] " +
                                MobileGroupMemberListConfigKey + "=idx:... 指定成员列表（或 path:/name:/item:/url:/title: / 关键字列表 a|b|c）。");
                return;
            }

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
                binding.MemberItemRenderer = RenderMobileGroupMemberListItem;

            try
            {
                binding.MemberList.itemRenderer = binding.MemberItemRenderer;
            }
            catch
            {
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

            if (binding.InviteInput == null || binding.InviteInput._disposed)
                binding.InviteInput = ResolveTextInput(inviteInputSpec, DefaultGroupInviteInputKeywords, out binding.InviteInputOverrideKeywords);

            if (binding.InviteButton == null || binding.InviteButton._disposed)
                binding.InviteButton = ResolveButton(inviteButtonSpec, DefaultGroupInviteButtonKeywords, out binding.InviteButtonOverrideKeywords);

            if (binding.KickButton == null || binding.KickButton._disposed)
                binding.KickButton = ResolveButton(kickButtonSpec, DefaultGroupKickButtonKeywords, out binding.KickButtonOverrideKeywords);

            if (binding.LeaveButton == null || binding.LeaveButton._disposed)
                binding.LeaveButton = ResolveButton(leaveButtonSpec, DefaultGroupLeaveButtonKeywords, out binding.LeaveButtonOverrideKeywords);

            try
            {
                if (binding.InviteClickCallback == null)
                {
                    binding.InviteClickCallback = () =>
                    {
                        if (!CanCurrentUserOperateMobileGroupAsLeader("邀请成员", assumeLeaderIsSelfWhenNotInGroup: true))
                            return;

                        string text = string.Empty;
                        try { text = binding.InviteInput?.text ?? string.Empty; } catch { text = string.Empty; }
                        if (!TryValidateCharacterName(text, "组队邀请", out string cleaned))
                            return;

                        ApplyMobileButtonCooldown(binding.InviteButton);
                        TrySendMobileGroupInvite(cleaned);
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
                        if (!CanCurrentUserOperateMobileGroupAsLeader("踢人", assumeLeaderIsSelfWhenNotInGroup: false))
                            return;

                        int selected = binding.SelectedIndex;
                        if (selected < 0 || selected >= binding.MemberNames.Count)
                        {
                            MobileHint("请先选择要踢出的成员。");
                            return;
                        }

                        string myName = GameScene.User?.Name ?? string.Empty;
                        string name = binding.MemberNames[selected] ?? string.Empty;
                        if (!string.IsNullOrWhiteSpace(myName) && string.Equals(name, myName, StringComparison.OrdinalIgnoreCase))
                        {
                            GameScene.Scene?.MobileReceiveChat("[组队] 不能踢出自己。", ChatType.Hint);
                            return;
                        }

                        if (!TryValidateCharacterName(name, "组队踢人", out string cleaned))
                            return;

                        ApplyMobileButtonCooldown(binding.KickButton);
                        TrySendMobileGroupKick(cleaned);
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

            try
            {
                if (binding.LeaveClickCallback == null)
                {
                    binding.LeaveClickCallback = () =>
                    {
                        ApplyMobileButtonCooldown(binding.LeaveButton);
                        TrySendMobileGroupLeave();
                    };
                }

                if (binding.LeaveButton != null && !binding.LeaveButton._disposed)
                {
                    binding.LeaveButton.onClick.Remove(binding.LeaveClickCallback);
                    binding.LeaveButton.onClick.Add(binding.LeaveClickCallback);
                }
            }
            catch
            {
            }

            _mobileGroupDirty = true;
            TryDumpMobileGroupBindingsReportIfDue(binding, memberListKeywordsUsed, memberListCandidates ?? new List<(int Score, GObject Target)>());
        }

        private static void TryRefreshMobileGroupIfDue(bool force)
        {
            if (_stage == null || !_initialized || !_packagesLoaded)
                return;

            if (!MobileWindows.TryGetValue("Group", out GComponent window) || window == null || window._disposed)
            {
                if (_mobileGroupBinding != null)
                    ResetMobileGroupBindings();
                return;
            }

            if (!window.visible)
                return;

            TryBindMobileGroupWindowIfDue("Group", window, resolveInfo: null);

            MobileGroupWindowBinding binding = _mobileGroupBinding;
            if (binding == null || binding.Window == null || binding.Window._disposed)
            {
                ResetMobileGroupBindings();
                return;
            }

            if (binding.MemberList == null || binding.MemberList._disposed)
                return;

            if (!force && !_mobileGroupDirty)
                return;

            _mobileGroupDirty = false;

            try
            {
                binding.MemberNames.Clear();

                var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    IReadOnlyDictionary<string, System.Drawing.Point> locations = GameScene.Scene?.MobileGroupMemberLocations;
                    if (locations != null)
                    {
                        foreach (string key in locations.Keys)
                        {
                            if (!string.IsNullOrWhiteSpace(key))
                                set.Add(key);
                        }
                    }
                }
                catch
                {
                }

                try
                {
                    IReadOnlyDictionary<string, string> maps = GameScene.Scene?.MobileGroupMemberMaps;
                    if (maps != null)
                    {
                        foreach (string key in maps.Keys)
                        {
                            if (!string.IsNullOrWhiteSpace(key))
                                set.Add(key);
                        }
                    }
                }
                catch
                {
                }

                string myName = GameScene.User?.Name ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(myName) && set.Remove(myName))
                    binding.MemberNames.Add(myName);

                var others = new List<string>(set);
                others.Sort(StringComparer.OrdinalIgnoreCase);
                binding.MemberNames.AddRange(others);
            }
            catch
            {
            }

            try
            {
                GameScene scene = GameScene.Scene;
                if (scene != null && scene.MobileGroupActive)
                {
                    string myName = GameScene.User?.Name ?? string.Empty;
                    string leader = scene.MobileGroupLeaderName ?? string.Empty;

                    if (binding.MemberNames.Count <= 1 && !string.IsNullOrWhiteSpace(myName))
                    {
                        if (!string.Equals(leader, myName, StringComparison.OrdinalIgnoreCase))
                            scene.SetMobileGroupLeaderName(myName);
                    }
                    else if (!string.IsNullOrWhiteSpace(leader))
                    {
                        bool leaderExists = false;
                        for (int i = 0; i < binding.MemberNames.Count; i++)
                        {
                            string name = binding.MemberNames[i];
                            if (string.Equals(name, leader, StringComparison.OrdinalIgnoreCase))
                            {
                                leaderExists = true;
                                break;
                            }
                        }

                        if (!leaderExists && !string.IsNullOrWhiteSpace(myName))
                            scene.SetMobileGroupLeaderName(myName);
                    }
                }
            }
            catch
            {
            }

            int count = binding.MemberNames.Count;
            if (binding.SelectedIndex >= count)
                binding.SelectedIndex = count - 1;
            if (binding.SelectedIndex < 0 || count == 0)
                binding.SelectedIndex = -1;

            try
            {
                bool canOperate = true;
                GameScene scene = GameScene.Scene;
                if (scene != null && scene.MobileGroupActive)
                {
                    string leaderName = scene.MobileGroupLeaderName ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(leaderName))
                    {
                        string myName = GameScene.User?.Name ?? string.Empty;
                        if (!string.IsNullOrWhiteSpace(myName) && !string.Equals(leaderName, myName, StringComparison.OrdinalIgnoreCase))
                            canOperate = false;
                    }
                }

                try { if (binding.InviteInput != null && !binding.InviteInput._disposed) binding.InviteInput.grayed = !canOperate; } catch { }
                try { if (binding.InviteButton != null && !binding.InviteButton._disposed) binding.InviteButton.grayed = !canOperate; } catch { }
                try { if (binding.KickButton != null && !binding.KickButton._disposed) binding.KickButton.grayed = !canOperate; } catch { }
            }
            catch
            {
            }

            try
            {
                if (binding.MemberItemRenderer == null)
                    binding.MemberItemRenderer = RenderMobileGroupMemberListItem;

                binding.MemberList.itemRenderer = binding.MemberItemRenderer;
                binding.MemberList.numItems = count;
            }
            catch (Exception ex)
            {
                CMain.SaveError("FairyGUI: 刷新组队窗口失败：" + ex.Message);
                _nextMobileGroupBindAttemptUtc = DateTime.MinValue;
                _mobileGroupDirty = true;
            }
        }
    }
}
