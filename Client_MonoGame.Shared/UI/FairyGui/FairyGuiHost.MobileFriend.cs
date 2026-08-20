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
        private static void OnMobileFriendItemClicked(MobileFriendItemView view)
        {
            if (view == null)
                return;

            MobileFriendWindowBinding binding = _mobileFriendBinding;
            if (binding == null || binding.Window == null || binding.Window._disposed)
                return;

            int index = view.Index;
            if (index < 0 || index >= MobileFriendEntries.Count)
                return;

            if (binding.SelectedIndex == index)
                return;

            binding.SelectedIndex = index;
            binding.SelectedFriendCharacterIndex = MobileFriendEntries[index]?.Index ?? -1;

            try
            {
                _mobileFriendDirty = true;
            }
            catch
            {
            }
        }

        private static MobileFriendItemView GetOrCreateMobileFriendItemView(GComponent itemRoot)
        {
            if (itemRoot == null || itemRoot._disposed)
                return null;

            if (itemRoot.data is MobileFriendItemView existing && existing.Root != null && !existing.Root._disposed)
                return existing;

            var view = new MobileFriendItemView
            {
                Root = itemRoot,
                Index = -1,
            };

            try
            {
                List<(int Score, GObject Target)> nameCandidates =
                    CollectMobileChatCandidates(itemRoot, obj => obj is GTextField && obj is not GTextInput, DefaultFriendItemNameKeywords, ScoreMobileShopTextCandidate);
                view.Name = SelectMobileChatCandidate<GTextField>(nameCandidates, minScore: 15);
            }
            catch
            {
            }

            try
            {
                List<(int Score, GObject Target)> memoCandidates =
                    CollectMobileChatCandidates(itemRoot, obj => obj is GTextField && obj is not GTextInput, DefaultFriendItemMemoKeywords, ScoreMobileShopTextCandidate);
                view.Memo = SelectMobileChatCandidate<GTextField>(memoCandidates, minScore: 15);
            }
            catch
            {
            }

            try
            {
                List<(int Score, GObject Target)> statusCandidates =
                    CollectMobileChatCandidates(itemRoot, obj => obj is GTextField && obj is not GTextInput, DefaultFriendItemStatusKeywords, ScoreMobileShopTextCandidate);
                view.Status = SelectMobileChatCandidate<GTextField>(statusCandidates, minScore: 15);
            }
            catch
            {
            }

            try
            {
                List<(int Score, GObject Target)> blockedCandidates =
                    CollectMobileChatCandidates(itemRoot, obj => obj is GTextField && obj is not GTextInput, DefaultFriendItemBlockedKeywords, ScoreMobileShopTextCandidate);
                view.Blocked = SelectMobileChatCandidate<GTextField>(blockedCandidates, minScore: 15);
            }
            catch
            {
            }

            try
            {
                if (view.ClickCallback == null)
                {
                    view.ClickCallback = () => OnMobileFriendItemClicked(view);
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

        private static void ClearMobileFriendItemView(MobileFriendItemView view)
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
                if (view.Memo != null && !view.Memo._disposed)
                    view.Memo.text = string.Empty;
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
                if (view.Blocked != null && !view.Blocked._disposed)
                    view.Blocked.text = string.Empty;
            }
            catch
            {
            }
        }

        private static void RenderMobileFriendListItem(int index, GObject itemObject)
        {
            if (itemObject == null || itemObject._disposed)
                return;

            if (itemObject is not GComponent itemRoot || itemRoot._disposed)
                return;

            MobileFriendItemView view = GetOrCreateMobileFriendItemView(itemRoot);
            if (view == null)
                return;

            view.Index = index;

            MobileFriendWindowBinding binding = _mobileFriendBinding;
            bool selected = binding != null && binding.SelectedIndex == index;

            try
            {
                if (itemObject is GButton button && !button._disposed)
                    button.selected = selected;
            }
            catch
            {
            }

            ClientFriend friend = null;
            if (index >= 0 && index < MobileFriendEntries.Count)
                friend = MobileFriendEntries[index];

            if (friend == null)
            {
                ClearMobileFriendItemView(view);
                return;
            }

            string name = friend.Name ?? string.Empty;
            string memo = friend.Memo ?? string.Empty;
            string status = friend.Online ? "在线" : "离线";
            string blocked = friend.Blocked ? "黑名单" : string.Empty;

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
                if (view.Memo != null && !view.Memo._disposed)
                    view.Memo.text = memo;
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
                if (view.Blocked != null && !view.Blocked._disposed)
                    view.Blocked.text = blocked;
            }
            catch
            {
            }
        }

        private static int FindMobileFriendIndexByCharacterIndex(int characterIndex)
        {
            if (characterIndex < 0)
                return -1;

            for (int i = 0; i < MobileFriendEntries.Count; i++)
            {
                ClientFriend friend = MobileFriendEntries[i];
                if (friend != null && friend.Index == characterIndex)
                    return i;
            }

            return -1;
        }

        private static ClientFriend TryGetSelectedMobileFriend(MobileFriendWindowBinding binding)
        {
            if (binding == null)
                return null;

            if (binding.SelectedFriendCharacterIndex >= 0)
            {
                int mapped = FindMobileFriendIndexByCharacterIndex(binding.SelectedFriendCharacterIndex);
                if (mapped >= 0)
                    binding.SelectedIndex = mapped;
                else
                {
                    binding.SelectedIndex = -1;
                    binding.SelectedFriendCharacterIndex = -1;
                }
            }

            int index = binding.SelectedIndex;
            if (index < 0 || index >= MobileFriendEntries.Count)
                return null;

            return MobileFriendEntries[index];
        }

        private static void TrySendMobileFriendAdd(string name, bool blocked)
        {
            if (!TryValidateCharacterName(name, blocked ? "拉黑" : "添加好友", out string cleaned))
                return;

            try
            {
                MonoShare.MirNetwork.Network.Enqueue(new C.AddFriend { Name = cleaned, Blocked = blocked });
            }
            catch (Exception ex)
            {
                CMain.SaveError("FairyGUI: 发送添加好友失败：" + ex.Message);
                MobileHint("网络异常：添加好友失败。");
            }
        }

        private static void TrySendMobileFriendRemove(ClientFriend friend)
        {
            if (friend == null)
                return;

            try
            {
                MonoShare.MirNetwork.Network.Enqueue(new C.RemoveFriend { CharacterIndex = friend.Index });
            }
            catch (Exception ex)
            {
                CMain.SaveError("FairyGUI: 发送删除好友失败：" + ex.Message);
                MobileHint("网络异常：删除好友失败。");
            }
        }

        private static void TrySendMobileFriendMemo(ClientFriend friend, string memo)
        {
            if (friend == null)
                return;

            if (!TryNormalizeFriendMemo(memo, out string cleaned))
                return;

            try
            {
                MonoShare.MirNetwork.Network.Enqueue(new C.AddMemo { CharacterIndex = friend.Index, Memo = cleaned });
            }
            catch (Exception ex)
            {
                CMain.SaveError("FairyGUI: 发送好友备注失败：" + ex.Message);
                MobileHint("网络异常：保存备注失败。");
            }
        }

        private static void TrySendMobileFriendRefresh()
        {
            try
            {
                MonoShare.MirNetwork.Network.Enqueue(new C.RefreshFriends());
            }
            catch (Exception ex)
            {
                CMain.SaveError("FairyGUI: 发送刷新好友失败：" + ex.Message);
                MobileHint("网络异常：刷新好友失败。");
            }
        }

        private static void TryDumpMobileFriendBindingsReportIfDue(
            MobileFriendWindowBinding binding,
            string[] listKeywordsUsed,
            List<(int Score, GObject Target)> listCandidates)
        {
            if (!Settings.DebugMode || _mobileFriendBindingsDumped)
                return;

            if (binding == null || binding.Window == null || binding.Window._disposed)
                return;

            try
            {
                Directory.CreateDirectory(ClientResourceLayout.RuntimeRoot);

                string path = Path.Combine(ClientResourceLayout.RuntimeRoot, "FairyGui-MobileFriendBindings.txt");

                var builder = new StringBuilder(14 * 1024);
                builder.AppendLine("FairyGUI 好友窗口绑定报告（Friend）");
                builder.AppendLine($"GeneratedAtUtc={DateTime.UtcNow:o}");
                builder.AppendLine($"WindowKey={binding.WindowKey ?? "Friend"}");
                builder.AppendLine($"WindowResolveInfo={binding.ResolveInfo ?? "-"}");
                builder.AppendLine();

                builder.AppendLine($"FriendList={DescribeObject(binding.Window, binding.FriendList)}");
                builder.AppendLine($"FriendListResolveInfo={binding.FriendListResolveInfo ?? "-"}");
                builder.AppendLine($"OverrideSpec={binding.FriendListOverrideSpec ?? "-"}");
                builder.AppendLine($"OverrideKeywords={(binding.FriendListOverrideKeywords == null ? "-" : string.Join("|", binding.FriendListOverrideKeywords))}");
                builder.AppendLine($"KeywordsUsed={(listKeywordsUsed == null ? "-" : string.Join("|", listKeywordsUsed))}");
                builder.AppendLine($"Friends={MobileFriendEntries.Count}");
                builder.AppendLine();

                builder.AppendLine($"AddInput={DescribeObject(binding.Window, binding.AddInput)} OverrideSpec={binding.AddInputOverrideSpec ?? "-"}");
                builder.AppendLine($"AddButton={DescribeObject(binding.Window, binding.AddButton)} OverrideSpec={binding.AddButtonOverrideSpec ?? "-"}");
                builder.AppendLine($"RemoveButton={DescribeObject(binding.Window, binding.RemoveButton)} OverrideSpec={binding.RemoveButtonOverrideSpec ?? "-"}");
                builder.AppendLine($"BlockButton={DescribeObject(binding.Window, binding.BlockButton)} OverrideSpec={binding.BlockButtonOverrideSpec ?? "-"}");
                builder.AppendLine($"MemoInput={DescribeObject(binding.Window, binding.MemoInput)} OverrideSpec={binding.MemoInputOverrideSpec ?? "-"}");
                builder.AppendLine($"MemoButton={DescribeObject(binding.Window, binding.MemoButton)} OverrideSpec={binding.MemoButtonOverrideSpec ?? "-"}");
                builder.AppendLine($"RefreshButton={DescribeObject(binding.Window, binding.RefreshButton)} OverrideSpec={binding.RefreshButtonOverrideSpec ?? "-"}");
                builder.AppendLine();

                builder.AppendLine("FriendList Candidates(top 12):");
                int top = Math.Min(12, listCandidates?.Count ?? 0);
                for (int i = 0; i < top; i++)
                {
                    (int score, GObject target) = listCandidates[i];
                    builder.AppendLine($"  - score={score} obj={DescribeObject(binding.Window, target)}");
                }

                if ((listCandidates?.Count ?? 0) > top)
                    builder.AppendLine($"  ... ({listCandidates.Count - top} more)");

                builder.AppendLine();
                builder.AppendLine("配置覆盖（可选）：Mir2Config.ini -> [FairyGUI]");
                builder.AppendLine($"  {MobileFriendListConfigKey}=idx:... 或 path:... 或 name:/item:/url:/title:...");
                builder.AppendLine($"  {MobileFriendAddInputConfigKey}=idx:...（同上）");
                builder.AppendLine($"  {MobileFriendAddButtonConfigKey}=idx:...（同上）");
                builder.AppendLine($"  {MobileFriendRemoveButtonConfigKey}=idx:...（同上）");
                builder.AppendLine($"  {MobileFriendBlockButtonConfigKey}=idx:...（同上）");
                builder.AppendLine($"  {MobileFriendMemoInputConfigKey}=idx:...（同上）");
                builder.AppendLine($"  {MobileFriendMemoButtonConfigKey}=idx:...（同上）");
                builder.AppendLine($"  {MobileFriendRefreshButtonConfigKey}=idx:...（同上）");
                builder.AppendLine("说明：idx/path 均相对好友窗口 Root。建议先查看窗口树（FairyGui-MobileWindow-Friend-Tree.txt），再填入精确覆盖。");

                File.WriteAllText(path, builder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                _mobileFriendBindingsDumped = true;
                CMain.SaveLog($"FairyGUI: 已导出好友窗口绑定报告到 {path}");
            }
            catch (Exception ex)
            {
                CMain.SaveError("FairyGUI: 导出好友窗口绑定报告失败：" + ex.Message);
            }
        }

        private static void TryBindMobileFriendWindowIfDue(string windowKey, GComponent window, string resolveInfo)
        {
            if (window == null || window._disposed)
                return;

            MobileFriendWindowBinding binding = _mobileFriendBinding;
            if (binding != null && (binding.Window == null || binding.Window._disposed))
            {
                ResetMobileFriendBindings();
                binding = null;
            }

            if (binding == null || !ReferenceEquals(binding.Window, window))
            {
                ResetMobileFriendBindings();
                binding = new MobileFriendWindowBinding
                {
                    WindowKey = windowKey,
                    Window = window,
                    ResolveInfo = resolveInfo,
                };

                _mobileFriendBinding = binding;
                _mobileFriendBindingsDumped = false;
                _nextMobileFriendBindAttemptUtc = DateTime.MinValue;
            }

            bool listOk = binding.FriendList != null && !binding.FriendList._disposed && binding.FriendItemRenderer != null;
            bool controlsOk =
                binding.AddInput != null && !binding.AddInput._disposed &&
                binding.AddButton != null && !binding.AddButton._disposed &&
                binding.RemoveButton != null && !binding.RemoveButton._disposed &&
                binding.BlockButton != null && !binding.BlockButton._disposed &&
                binding.MemoInput != null && !binding.MemoInput._disposed &&
                binding.MemoButton != null && !binding.MemoButton._disposed &&
                binding.RefreshButton != null && !binding.RefreshButton._disposed;

            if (listOk && controlsOk)
                return;

            if (DateTime.UtcNow < _nextMobileFriendBindAttemptUtc)
                return;

            _nextMobileFriendBindAttemptUtc = DateTime.UtcNow.AddSeconds(2);

            string listSpec = string.Empty;
            string addInputSpec = string.Empty;
            string addButtonSpec = string.Empty;
            string removeButtonSpec = string.Empty;
            string blockButtonSpec = string.Empty;
            string memoInputSpec = string.Empty;
            string memoButtonSpec = string.Empty;
            string refreshButtonSpec = string.Empty;

            try
            {
                InIReader reader = TryCreateConfigReader();
                if (reader != null)
                {
                    listSpec = reader.ReadString(FairyGuiConfigSectionName, MobileFriendListConfigKey, string.Empty, writeWhenNull: false);
                    addInputSpec = reader.ReadString(FairyGuiConfigSectionName, MobileFriendAddInputConfigKey, string.Empty, writeWhenNull: false);
                    addButtonSpec = reader.ReadString(FairyGuiConfigSectionName, MobileFriendAddButtonConfigKey, string.Empty, writeWhenNull: false);
                    removeButtonSpec = reader.ReadString(FairyGuiConfigSectionName, MobileFriendRemoveButtonConfigKey, string.Empty, writeWhenNull: false);
                    blockButtonSpec = reader.ReadString(FairyGuiConfigSectionName, MobileFriendBlockButtonConfigKey, string.Empty, writeWhenNull: false);
                    memoInputSpec = reader.ReadString(FairyGuiConfigSectionName, MobileFriendMemoInputConfigKey, string.Empty, writeWhenNull: false);
                    memoButtonSpec = reader.ReadString(FairyGuiConfigSectionName, MobileFriendMemoButtonConfigKey, string.Empty, writeWhenNull: false);
                    refreshButtonSpec = reader.ReadString(FairyGuiConfigSectionName, MobileFriendRefreshButtonConfigKey, string.Empty, writeWhenNull: false);
                }
            }
            catch
            {
                listSpec = string.Empty;
                addInputSpec = string.Empty;
                addButtonSpec = string.Empty;
                removeButtonSpec = string.Empty;
                blockButtonSpec = string.Empty;
                memoInputSpec = string.Empty;
                memoButtonSpec = string.Empty;
                refreshButtonSpec = string.Empty;
            }

            listSpec = listSpec?.Trim() ?? string.Empty;
            addInputSpec = addInputSpec?.Trim() ?? string.Empty;
            addButtonSpec = addButtonSpec?.Trim() ?? string.Empty;
            removeButtonSpec = removeButtonSpec?.Trim() ?? string.Empty;
            blockButtonSpec = blockButtonSpec?.Trim() ?? string.Empty;
            memoInputSpec = memoInputSpec?.Trim() ?? string.Empty;
            memoButtonSpec = memoButtonSpec?.Trim() ?? string.Empty;
            refreshButtonSpec = refreshButtonSpec?.Trim() ?? string.Empty;

            binding.FriendListOverrideSpec = listSpec;
            binding.FriendListOverrideKeywords = null;

            binding.AddInputOverrideSpec = addInputSpec;
            binding.AddInputOverrideKeywords = null;
            binding.AddButtonOverrideSpec = addButtonSpec;
            binding.AddButtonOverrideKeywords = null;
            binding.RemoveButtonOverrideSpec = removeButtonSpec;
            binding.RemoveButtonOverrideKeywords = null;
            binding.BlockButtonOverrideSpec = blockButtonSpec;
            binding.BlockButtonOverrideKeywords = null;
            binding.MemoInputOverrideSpec = memoInputSpec;
            binding.MemoInputOverrideKeywords = null;
            binding.MemoButtonOverrideSpec = memoButtonSpec;
            binding.MemoButtonOverrideKeywords = null;
            binding.RefreshButtonOverrideSpec = refreshButtonSpec;
            binding.RefreshButtonOverrideKeywords = null;

            GList list = binding.FriendList;
            if (list != null && list._disposed)
                list = null;

            GComponent searchRoot = window;
            string listResolveInfo = null;
            List<(int Score, GObject Target)> listCandidates = null;

            if (!string.IsNullOrWhiteSpace(listSpec))
            {
                if (TryResolveMobileMainHudObjectBySpec(window, listSpec, out GObject resolved, out string[] keywords))
                {
                    if (resolved is GList resolvedList && !resolvedList._disposed)
                    {
                        list = resolvedList;
                        listResolveInfo = DescribeObject(window, resolvedList) + " (override)";
                    }
                    else if (resolved is GComponent resolvedComponent && !resolvedComponent._disposed)
                    {
                        searchRoot = resolvedComponent;
                        listResolveInfo = DescribeObject(window, resolvedComponent) + " (searchRoot override)";
                    }
                    else if (keywords != null && keywords.Length > 0)
                    {
                        binding.FriendListOverrideKeywords = keywords;
                    }
                }
                else
                {
                    binding.FriendListOverrideKeywords = SplitKeywords(listSpec);
                }
            }

            string[] listKeywordsUsed = binding.FriendListOverrideKeywords != null && binding.FriendListOverrideKeywords.Length > 0
                ? binding.FriendListOverrideKeywords
                : DefaultFriendListKeywords;

            if (list == null)
            {
                int minScore = binding.FriendListOverrideKeywords != null && binding.FriendListOverrideKeywords.Length > 0 ? 40 : 60;
                listCandidates = CollectMobileChatCandidates(searchRoot, obj => obj is GList && obj.touchable, listKeywordsUsed, ScoreMobileShopListCandidate);
                list = SelectMobileChatCandidate<GList>(listCandidates, minScore);
                if (list != null && !list._disposed)
                    listResolveInfo = DescribeObject(window, list) + (binding.FriendListOverrideKeywords != null && binding.FriendListOverrideKeywords.Length > 0 ? " (keywords)" : " (auto)");
            }

            if (list == null || list._disposed)
            {
                CMain.SaveError("FairyGUI: 好友窗口未找到好友列表（Friend）。可在 Mir2Config.ini 设置 [" + FairyGuiConfigSectionName + "] " +
                                MobileFriendListConfigKey + "=idx:... 指定好友列表（或 path:/name:/item:/url:/title: / 关键字列表 a|b|c）。");
                return;
            }

            binding.FriendList = list;
            binding.FriendListResolveInfo = listResolveInfo;

            try
            {
                if (!binding.FriendList.isVirtual && binding.FriendList.scrollPane != null)
                    binding.FriendList.SetVirtual();
            }
            catch
            {
            }

            if (binding.FriendItemRenderer == null)
                binding.FriendItemRenderer = RenderMobileFriendListItem;

            try
            {
                binding.FriendList.itemRenderer = binding.FriendItemRenderer;
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

            if (binding.AddInput == null || binding.AddInput._disposed)
                binding.AddInput = ResolveTextInput(addInputSpec, DefaultFriendAddInputKeywords, out binding.AddInputOverrideKeywords);

            if (binding.AddButton == null || binding.AddButton._disposed)
                binding.AddButton = ResolveButton(addButtonSpec, DefaultFriendAddButtonKeywords, out binding.AddButtonOverrideKeywords);

            if (binding.RemoveButton == null || binding.RemoveButton._disposed)
                binding.RemoveButton = ResolveButton(removeButtonSpec, DefaultFriendRemoveButtonKeywords, out binding.RemoveButtonOverrideKeywords);

            if (binding.BlockButton == null || binding.BlockButton._disposed)
                binding.BlockButton = ResolveButton(blockButtonSpec, DefaultFriendBlockButtonKeywords, out binding.BlockButtonOverrideKeywords);

            if (binding.MemoInput == null || binding.MemoInput._disposed)
                binding.MemoInput = ResolveTextInput(memoInputSpec, DefaultFriendMemoInputKeywords, out binding.MemoInputOverrideKeywords);

            if (binding.MemoButton == null || binding.MemoButton._disposed)
                binding.MemoButton = ResolveButton(memoButtonSpec, DefaultFriendMemoButtonKeywords, out binding.MemoButtonOverrideKeywords);

            if (binding.RefreshButton == null || binding.RefreshButton._disposed)
                binding.RefreshButton = ResolveButton(refreshButtonSpec, DefaultFriendRefreshButtonKeywords, out binding.RefreshButtonOverrideKeywords);

            try
            {
                if (binding.AddClickCallback == null)
                {
                    binding.AddClickCallback = () =>
                    {
                        string text = string.Empty;
                        try { text = binding.AddInput?.text ?? string.Empty; } catch { text = string.Empty; }
                        if (!TryValidateCharacterName(text, "添加好友", out string cleaned))
                            return;

                        ApplyMobileButtonCooldown(binding.AddButton);
                        TrySendMobileFriendAdd(cleaned, blocked: false);
                        try { if (binding.AddInput != null && !binding.AddInput._disposed) binding.AddInput.text = string.Empty; } catch { }
                    };
                }

                if (binding.AddButton != null && !binding.AddButton._disposed)
                {
                    binding.AddButton.onClick.Remove(binding.AddClickCallback);
                    binding.AddButton.onClick.Add(binding.AddClickCallback);
                }
            }
            catch
            {
            }

            try
            {
                if (binding.RemoveClickCallback == null)
                {
                    binding.RemoveClickCallback = () =>
                    {
                        ClientFriend friend = TryGetSelectedMobileFriend(binding);
                        if (friend == null)
                        {
                            MobileHint("请先选择好友。");
                            return;
                        }

                        ApplyMobileButtonCooldown(binding.RemoveButton);
                        TrySendMobileFriendRemove(friend);
                    };
                }

                if (binding.RemoveButton != null && !binding.RemoveButton._disposed)
                {
                    binding.RemoveButton.onClick.Remove(binding.RemoveClickCallback);
                    binding.RemoveButton.onClick.Add(binding.RemoveClickCallback);
                }
            }
            catch
            {
            }

            try
            {
                if (binding.BlockClickCallback == null)
                {
                    binding.BlockClickCallback = () =>
                    {
                        ClientFriend friend = TryGetSelectedMobileFriend(binding);
                        if (friend != null)
                        {
                            ApplyMobileButtonCooldown(binding.BlockButton);
                            TrySendMobileFriendRemove(friend);
                            TrySendMobileFriendAdd(friend.Name, blocked: true);
                            return;
                        }

                        string text = string.Empty;
                        try { text = binding.AddInput?.text ?? string.Empty; } catch { text = string.Empty; }
                        if (!TryValidateCharacterName(text, "拉黑", out string cleaned))
                            return;

                        ApplyMobileButtonCooldown(binding.BlockButton);
                        TrySendMobileFriendAdd(cleaned, blocked: true);
                        try { if (binding.AddInput != null && !binding.AddInput._disposed) binding.AddInput.text = string.Empty; } catch { }
                    };
                }

                if (binding.BlockButton != null && !binding.BlockButton._disposed)
                {
                    binding.BlockButton.onClick.Remove(binding.BlockClickCallback);
                    binding.BlockButton.onClick.Add(binding.BlockClickCallback);
                }
            }
            catch
            {
            }

            try
            {
                if (binding.MemoClickCallback == null)
                {
                    binding.MemoClickCallback = () =>
                    {
                        ClientFriend friend = TryGetSelectedMobileFriend(binding);
                        if (friend == null)
                        {
                            MobileHint("请先选择好友。");
                            return;
                        }

                        string text = string.Empty;
                        try { text = binding.MemoInput?.text ?? string.Empty; } catch { text = string.Empty; }
                        if (!TryNormalizeFriendMemo(text, out string cleaned))
                            return;

                        ApplyMobileButtonCooldown(binding.MemoButton);
                        TrySendMobileFriendMemo(friend, cleaned);
                        try { if (binding.MemoInput != null && !binding.MemoInput._disposed) binding.MemoInput.text = string.Empty; } catch { }
                    };
                }

                if (binding.MemoButton != null && !binding.MemoButton._disposed)
                {
                    binding.MemoButton.onClick.Remove(binding.MemoClickCallback);
                    binding.MemoButton.onClick.Add(binding.MemoClickCallback);
                }
            }
            catch
            {
            }

            try
            {
                if (binding.RefreshClickCallback == null)
                {
                    binding.RefreshClickCallback = () =>
                    {
                        ApplyMobileButtonCooldown(binding.RefreshButton);
                        TrySendMobileFriendRefresh();
                    };
                }

                if (binding.RefreshButton != null && !binding.RefreshButton._disposed)
                {
                    binding.RefreshButton.onClick.Remove(binding.RefreshClickCallback);
                    binding.RefreshButton.onClick.Add(binding.RefreshClickCallback);
                }
            }
            catch
            {
            }

            _mobileFriendDirty = true;
            TryDumpMobileFriendBindingsReportIfDue(binding, listKeywordsUsed, listCandidates ?? new List<(int Score, GObject Target)>());
        }

        private static void TryRefreshMobileFriendIfDue(bool force)
        {
            if (_stage == null || !_initialized || !_packagesLoaded)
                return;

            if (!MobileWindows.TryGetValue("Friend", out GComponent window) || window == null || window._disposed)
            {
                if (_mobileFriendBinding != null)
                    ResetMobileFriendBindings();
                return;
            }

            if (!window.visible)
                return;

            TryBindMobileFriendWindowIfDue("Friend", window, resolveInfo: null);

            MobileFriendWindowBinding binding = _mobileFriendBinding;
            if (binding == null || binding.Window == null || binding.Window._disposed)
            {
                ResetMobileFriendBindings();
                return;
            }

            if (binding.FriendList == null || binding.FriendList._disposed)
                return;

            if (!force && !_mobileFriendDirty)
                return;

            _mobileFriendDirty = false;

            int count = MobileFriendEntries.Count;
            if (binding.SelectedFriendCharacterIndex >= 0)
            {
                int mapped = FindMobileFriendIndexByCharacterIndex(binding.SelectedFriendCharacterIndex);
                binding.SelectedIndex = mapped;
                if (mapped < 0)
                    binding.SelectedFriendCharacterIndex = -1;
            }
            else
            {
                if (binding.SelectedIndex < 0 || binding.SelectedIndex >= count)
                    binding.SelectedIndex = -1;

                if (binding.SelectedIndex >= 0 && binding.SelectedIndex < count)
                    binding.SelectedFriendCharacterIndex = MobileFriendEntries[binding.SelectedIndex]?.Index ?? -1;
            }

            try
            {
                if (binding.FriendItemRenderer == null)
                    binding.FriendItemRenderer = RenderMobileFriendListItem;

                binding.FriendList.itemRenderer = binding.FriendItemRenderer;
                binding.FriendList.numItems = count;
            }
            catch (Exception ex)
            {
                CMain.SaveError("FairyGUI: 刷新好友窗口失败：" + ex.Message);
                _nextMobileFriendBindAttemptUtc = DateTime.MinValue;
                _mobileFriendDirty = true;
            }

            try
            {
                if (binding.MemoInput != null && !binding.MemoInput._disposed)
                {
                    ClientFriend selectedFriend = TryGetSelectedMobileFriend(binding);
                    if (selectedFriend != null)
                    {
                        string memo = selectedFriend.Memo ?? string.Empty;
                        if (!string.Equals(binding.MemoInput.text, memo, StringComparison.Ordinal))
                            binding.MemoInput.text = memo;
                    }
                    else
                    {
                        if (!string.IsNullOrWhiteSpace(binding.MemoInput.text))
                            binding.MemoInput.text = string.Empty;
                    }
                }
            }
            catch
            {
            }
        }
    }
}
