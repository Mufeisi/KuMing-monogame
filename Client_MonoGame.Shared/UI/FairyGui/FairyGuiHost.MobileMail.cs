using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using C = ClientPackets;
using FairyGUI;
using MonoShare.MirControls;
using MonoShare.MirNetwork;
using MonoShare.MirScenes;

namespace MonoShare
{
    internal static partial class FairyGuiHost
    {
        private const string MobileMailListConfigKey = "MobileMail.List";
        private const string MobileMailSendConfigKey = "MobileMail.Send";
        private const string MobileMailReplyConfigKey = "MobileMail.Reply";
        private const string MobileMailReadConfigKey = "MobileMail.Read";
        private const string MobileMailDeleteConfigKey = "MobileMail.Delete";
        private const string MobileMailLockConfigKey = "MobileMail.Lock";
        private const string MobileMailCollectConfigKey = "MobileMail.Collect";
        private const string MobileMailSenderConfigKey = "MobileMail.Sender";
        private const string MobileMailDateConfigKey = "MobileMail.Date";
        private const string MobileMailMessageConfigKey = "MobileMail.Message";
        private const string MobileMailGoldConfigKey = "MobileMail.Gold";
        private const string MobileMailCostConfigKey = "MobileMail.Cost";

        private static readonly string[] DefaultMailListKeywords = { "mail", "inbox", "邮件", "信件", "列表", "list" };
        private static readonly string[] DefaultMailSendKeywords = { "send", "compose", "write", "new", "寄", "发送", "写信", "新建", "撰写" };
        private static readonly string[] DefaultMailReplyKeywords = { "reply", "回复", "回信" };
        private static readonly string[] DefaultMailReadKeywords = { "read", "open", "查看", "阅读" };
        private static readonly string[] DefaultMailDeleteKeywords = { "delete", "del", "remove", "删除", "丢弃" };
        private static readonly string[] DefaultMailLockKeywords = { "lock", "locked", "锁", "锁定" };
        private static readonly string[] DefaultMailCollectKeywords = { "collect", "extract", "领取", "收取", "提取" };
        private static readonly string[] DefaultMailSenderKeywords = { "sender", "from", "发件", "寄件", "sendername", "name", "发件人" };
        private static readonly string[] DefaultMailDateKeywords = { "date", "time", "日期", "时间" };
        private static readonly string[] DefaultMailMessageKeywords = { "message", "content", "text", "信息", "内容", "正文" };
        private static readonly string[] DefaultMailGoldKeywords = { "gold", "coin", "金币", "金额" };
        private static readonly string[] DefaultMailCostKeywords = { "cost", "fee", "postage", "邮费", "费用", "手续费" };

        private sealed class MobileMailItemView
        {
            public GComponent Root;
            public GTextField Sender;
            public GTextField Message;
            public GTextField Date;

            public GObject UnreadMarker;
            public GObject LockedMarker;
            public GObject ParcelMarker;

            public EventCallback0 ClickCallback;
            public float OriginalAlpha;
            public bool OriginalAlphaCaptured;
        }

        private sealed class MobileMailWindowBinding
        {
            public string WindowKey;
            public GComponent Window;
            public string ResolveInfo;

            public GList List;
            public string ListResolveInfo;
            public string ListOverrideSpec;
            public string[] ListOverrideKeywords;
            public ListItemRenderer ItemRenderer;

            public GButton SendButton;
            public string SendResolveInfo;
            public string SendOverrideSpec;
            public string[] SendOverrideKeywords;
            public EventCallback0 SendClickCallback;

            public GButton ReplyButton;
            public string ReplyResolveInfo;
            public string ReplyOverrideSpec;
            public string[] ReplyOverrideKeywords;
            public EventCallback0 ReplyClickCallback;

            public GButton ReadButton;
            public string ReadResolveInfo;
            public string ReadOverrideSpec;
            public string[] ReadOverrideKeywords;
            public EventCallback0 ReadClickCallback;

            public GButton DeleteButton;
            public string DeleteResolveInfo;
            public string DeleteOverrideSpec;
            public string[] DeleteOverrideKeywords;
            public EventCallback0 DeleteClickCallback;

            public GButton LockButton;
            public string LockResolveInfo;
            public string LockOverrideSpec;
            public string[] LockOverrideKeywords;
            public EventCallback0 LockClickCallback;

            public GButton CollectButton;
            public string CollectResolveInfo;
            public string CollectOverrideSpec;
            public string[] CollectOverrideKeywords;
            public EventCallback0 CollectClickCallback;

            public GTextField Sender;
            public string SenderResolveInfo;
            public string SenderOverrideSpec;
            public string[] SenderOverrideKeywords;

            public GTextField Date;
            public string DateResolveInfo;
            public string DateOverrideSpec;
            public string[] DateOverrideKeywords;

            public GTextField Message;
            public string MessageResolveInfo;
            public string MessageOverrideSpec;
            public string[] MessageOverrideKeywords;

            public GTextField Gold;
            public string GoldResolveInfo;
            public string GoldOverrideSpec;
            public string[] GoldOverrideKeywords;

            public GTextField Cost;
            public string CostResolveInfo;
            public string CostOverrideSpec;
            public string[] CostOverrideKeywords;
        }

        private static MobileMailWindowBinding _mobileMailBinding;
        private static DateTime _nextMobileMailBindAttemptUtc = DateTime.MinValue;
        private static bool _mobileMailBindingsDumped;
        private static bool _mobileMailDirty;
        private static bool _mobileMailWindowWasVisible;

        private static ulong _mobileMailSelectedMailId;
        private static uint _mobileMailCostPreview;

        private static readonly object MobileMailComposeGate = new object();
        private static string _mobileMailComposeRecipient = string.Empty;
        private static string _mobileMailComposeMessage = string.Empty;
        private static uint _mobileMailComposeGold;
        private static ulong _mobileMailComposeItem0;
        private static bool _mobileMailComposePendingParcel;

        private static bool _mobileMailAttachmentSelectionActive;
        private static int _mobileMailAttachmentSelectionSlotIndex = -1;
        private static Action<ulong> _mobileMailAttachmentSelectionOnSelected;
        private static Action _mobileMailAttachmentSelectionOnCancel;

        public static void UpdateMobileMailList(IList<ClientMail> mails) => MarkMobileMailDirty();

        public static void UpdateMobileMailCost(uint cost)
        {
            _mobileMailCostPreview = cost;

            try { GameScene.Scene?.OutputMessage($"邮费：{cost:#,##0}"); } catch { }
            MarkMobileMailDirty();
        }

        public static void BeginMobileMailRead(ulong mailId)
        {
            if (mailId < 1)
                return;

            _mobileMailSelectedMailId = mailId;
            MarkMobileMailDirty();
            TrySendReadMailIfNeeded(mailId);
        }

        public static void BeginMobileMailCompose(string recipientName, bool preferParcel)
        {
            string cleaned = (recipientName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(cleaned))
                return;

            lock (MobileMailComposeGate)
            {
                _mobileMailComposeRecipient = cleaned;
                _mobileMailComposeMessage = string.Empty;
                _mobileMailComposeGold = 0;
                _mobileMailComposeItem0 = 0;
                _mobileMailComposePendingParcel = preferParcel;
            }

            _mobileMailCostPreview = 0;

            if (preferParcel)
                BeginParcelComposeWithPrompts(cleaned);
            else
                BeginLetterComposeWithPrompts(cleaned);
        }

        public static void BeginMobileMailAttachmentSelection(int slotIndex)
        {
            if (slotIndex < 0)
                return;

            lock (MobileMailComposeGate)
            {
                _mobileMailAttachmentSelectionActive = true;
                _mobileMailAttachmentSelectionSlotIndex = slotIndex;
            }

            MarkMobileMailDirty();
        }

        public static void CancelMobileMailAttachmentSelection()
        {
            Action onCancel = null;
            lock (MobileMailComposeGate)
            {
                _mobileMailAttachmentSelectionActive = false;
                _mobileMailAttachmentSelectionSlotIndex = -1;
                _mobileMailAttachmentSelectionOnSelected = null;
                onCancel = _mobileMailAttachmentSelectionOnCancel;
                _mobileMailAttachmentSelectionOnCancel = null;
            }

            try { onCancel?.Invoke(); } catch { }
        }

        public static void HandleMobileMailAttachmentSelected(int slotIndex, ulong uniqueId)
        {
            if (uniqueId < 1)
                return;

            Action<ulong> onSelected = null;

            lock (MobileMailComposeGate)
            {
                if (!_mobileMailAttachmentSelectionActive || slotIndex != _mobileMailAttachmentSelectionSlotIndex)
                    return;

                _mobileMailAttachmentSelectionActive = false;
                _mobileMailAttachmentSelectionSlotIndex = -1;

                onSelected = _mobileMailAttachmentSelectionOnSelected;
                _mobileMailAttachmentSelectionOnSelected = null;
                _mobileMailAttachmentSelectionOnCancel = null;
            }

            SetMobileMailItemLocked(uniqueId, locked: true);

            try { onSelected?.Invoke(uniqueId); } catch { }
            MarkMobileMailDirty();
        }

        private static bool TryHandleMobileMailAttachmentSelection(int inventorySlotIndex)
        {
            int attachmentSlotIndex;
            bool active;

            lock (MobileMailComposeGate)
            {
                active = _mobileMailAttachmentSelectionActive;
                attachmentSlotIndex = _mobileMailAttachmentSelectionSlotIndex;
            }

            if (!active || attachmentSlotIndex < 0)
                return false;

            UserItem item = null;
            try
            {
                UserItem[] inventory = GameScene.User?.Inventory;
                if (inventory != null && inventorySlotIndex >= 0 && inventorySlotIndex < inventory.Length)
                    item = inventory[inventorySlotIndex];
            }
            catch
            {
                item = null;
            }

            if (item == null || item.Info == null)
            {
                GameScene.Scene?.OutputMessage("请选择背包中的物品。");
                return true;
            }

            if (IsMobileItemLocked(item.UniqueID))
            {
                GameScene.Scene?.OutputMessage("物品已锁定，无法作为邮件附件。");
                return true;
            }

            try
            {
                HandleMobileMailAttachmentSelected(attachmentSlotIndex, item.UniqueID);
            }
            catch
            {
            }

            try
            {
                if (TryShowMobileWindowByKeywords("Mail", new[] { "MailWinStay1", "MailWinStay2", "邮件", "信件", "Mail", "Inbox" }))
                    HideAllMobileWindowsExcept("Mail");
            }
            catch
            {
            }

            return true;
        }

        public static void MarkMobileMailDirty()
        {
            try { _mobileMailDirty = true; } catch { }
            TryRefreshMobileMailIfDue(force: false);
        }

        private static void ResetMobileMailBindings()
        {
            try
            {
                MobileMailWindowBinding binding = _mobileMailBinding;
                if (binding != null)
                {
                    try { if (binding.SendButton != null && binding.SendClickCallback != null) binding.SendButton.onClick.Remove(binding.SendClickCallback); } catch { }
                    try { if (binding.ReplyButton != null && binding.ReplyClickCallback != null) binding.ReplyButton.onClick.Remove(binding.ReplyClickCallback); } catch { }
                    try { if (binding.ReadButton != null && binding.ReadClickCallback != null) binding.ReadButton.onClick.Remove(binding.ReadClickCallback); } catch { }
                    try { if (binding.DeleteButton != null && binding.DeleteClickCallback != null) binding.DeleteButton.onClick.Remove(binding.DeleteClickCallback); } catch { }
                    try { if (binding.LockButton != null && binding.LockClickCallback != null) binding.LockButton.onClick.Remove(binding.LockClickCallback); } catch { }
                    try { if (binding.CollectButton != null && binding.CollectClickCallback != null) binding.CollectButton.onClick.Remove(binding.CollectClickCallback); } catch { }
                }
            }
            catch
            {
            }

            _mobileMailBinding = null;
            _nextMobileMailBindAttemptUtc = DateTime.MinValue;
            _mobileMailBindingsDumped = false;
            _mobileMailDirty = true;
            _mobileMailWindowWasVisible = false;
            _mobileMailSelectedMailId = 0;

            CancelMobileMailAttachmentSelection();
        }

        private static void TryBindMobileMailWindowIfDue(string windowKey, GComponent window, string resolveInfo)
        {
            if (window == null || window._disposed)
                return;

            if (_mobileMailBinding != null && _mobileMailBinding.Window != null && _mobileMailBinding.Window._disposed)
                ResetMobileMailBindings();

            if (_mobileMailBinding == null || _mobileMailBinding.Window == null || _mobileMailBinding.Window._disposed || !ReferenceEquals(_mobileMailBinding.Window, window))
            {
                ResetMobileMailBindings();
                _mobileMailBinding = new MobileMailWindowBinding
                {
                    WindowKey = windowKey,
                    Window = window,
                    ResolveInfo = resolveInfo,
                };
            }

            if (DateTime.UtcNow < _nextMobileMailBindAttemptUtc)
                return;

            MobileMailWindowBinding binding = _mobileMailBinding;
            if (binding == null)
                return;

            bool listBound = binding.List != null && !binding.List._disposed;
            if (listBound && binding.SendButton != null && !binding.SendButton._disposed && binding.DeleteButton != null && !binding.DeleteButton._disposed)
                return;

            _nextMobileMailBindAttemptUtc = DateTime.UtcNow.AddSeconds(2);

            string listSpec = string.Empty;
            string sendSpec = string.Empty;
            string replySpec = string.Empty;
            string readSpec = string.Empty;
            string deleteSpec = string.Empty;
            string lockSpec = string.Empty;
            string collectSpec = string.Empty;
            string senderSpec = string.Empty;
            string dateSpec = string.Empty;
            string messageSpec = string.Empty;
            string goldSpec = string.Empty;
            string costSpec = string.Empty;

            try
            {
                InIReader reader = TryCreateConfigReader();
                if (reader != null)
                {
                    listSpec = reader.ReadString(FairyGuiConfigSectionName, MobileMailListConfigKey, string.Empty, writeWhenNull: false);
                    sendSpec = reader.ReadString(FairyGuiConfigSectionName, MobileMailSendConfigKey, string.Empty, writeWhenNull: false);
                    replySpec = reader.ReadString(FairyGuiConfigSectionName, MobileMailReplyConfigKey, string.Empty, writeWhenNull: false);
                    readSpec = reader.ReadString(FairyGuiConfigSectionName, MobileMailReadConfigKey, string.Empty, writeWhenNull: false);
                    deleteSpec = reader.ReadString(FairyGuiConfigSectionName, MobileMailDeleteConfigKey, string.Empty, writeWhenNull: false);
                    lockSpec = reader.ReadString(FairyGuiConfigSectionName, MobileMailLockConfigKey, string.Empty, writeWhenNull: false);
                    collectSpec = reader.ReadString(FairyGuiConfigSectionName, MobileMailCollectConfigKey, string.Empty, writeWhenNull: false);
                    senderSpec = reader.ReadString(FairyGuiConfigSectionName, MobileMailSenderConfigKey, string.Empty, writeWhenNull: false);
                    dateSpec = reader.ReadString(FairyGuiConfigSectionName, MobileMailDateConfigKey, string.Empty, writeWhenNull: false);
                    messageSpec = reader.ReadString(FairyGuiConfigSectionName, MobileMailMessageConfigKey, string.Empty, writeWhenNull: false);
                    goldSpec = reader.ReadString(FairyGuiConfigSectionName, MobileMailGoldConfigKey, string.Empty, writeWhenNull: false);
                    costSpec = reader.ReadString(FairyGuiConfigSectionName, MobileMailCostConfigKey, string.Empty, writeWhenNull: false);
                }
            }
            catch
            {
                listSpec = string.Empty;
                sendSpec = string.Empty;
                replySpec = string.Empty;
                readSpec = string.Empty;
                deleteSpec = string.Empty;
                lockSpec = string.Empty;
                collectSpec = string.Empty;
                senderSpec = string.Empty;
                dateSpec = string.Empty;
                messageSpec = string.Empty;
                goldSpec = string.Empty;
                costSpec = string.Empty;
            }

            listSpec = listSpec?.Trim() ?? string.Empty;
            sendSpec = sendSpec?.Trim() ?? string.Empty;
            replySpec = replySpec?.Trim() ?? string.Empty;
            readSpec = readSpec?.Trim() ?? string.Empty;
            deleteSpec = deleteSpec?.Trim() ?? string.Empty;
            lockSpec = lockSpec?.Trim() ?? string.Empty;
            collectSpec = collectSpec?.Trim() ?? string.Empty;
            senderSpec = senderSpec?.Trim() ?? string.Empty;
            dateSpec = dateSpec?.Trim() ?? string.Empty;
            messageSpec = messageSpec?.Trim() ?? string.Empty;
            goldSpec = goldSpec?.Trim() ?? string.Empty;
            costSpec = costSpec?.Trim() ?? string.Empty;

            binding.ListOverrideSpec = listSpec;
            binding.SendOverrideSpec = sendSpec;
            binding.ReplyOverrideSpec = replySpec;
            binding.ReadOverrideSpec = readSpec;
            binding.DeleteOverrideSpec = deleteSpec;
            binding.LockOverrideSpec = lockSpec;
            binding.CollectOverrideSpec = collectSpec;
            binding.SenderOverrideSpec = senderSpec;
            binding.DateOverrideSpec = dateSpec;
            binding.MessageOverrideSpec = messageSpec;
            binding.GoldOverrideSpec = goldSpec;
            binding.CostOverrideSpec = costSpec;

            // List
            if (binding.List == null || binding.List._disposed)
            {
                string[] keywordsUsed = DefaultMailListKeywords;
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
                    List<(int Score, GObject Target)> candidates = CollectMobileChatCandidates(window, obj => obj is GList && obj.touchable, keywordsUsed, ScoreMobileShopListCandidate);
                    list = SelectMobileChatCandidate<GList>(candidates, minScore: 10);
                    binding.ListResolveInfo = list != null ? "auto " + DescribeObject(window, list) : "auto (miss)";
                }

                binding.List = list;
                binding.ListOverrideKeywords = keywordsUsed;
            }

            // Buttons (auto bind + optional override)
            BindMailButton(window, ref binding.SendButton, ref binding.SendResolveInfo, sendSpec, DefaultMailSendKeywords, out binding.SendOverrideKeywords);
            BindMailButton(window, ref binding.ReplyButton, ref binding.ReplyResolveInfo, replySpec, DefaultMailReplyKeywords, out binding.ReplyOverrideKeywords);
            BindMailButton(window, ref binding.ReadButton, ref binding.ReadResolveInfo, readSpec, DefaultMailReadKeywords, out binding.ReadOverrideKeywords);
            BindMailButton(window, ref binding.DeleteButton, ref binding.DeleteResolveInfo, deleteSpec, DefaultMailDeleteKeywords, out binding.DeleteOverrideKeywords);
            BindMailButton(window, ref binding.LockButton, ref binding.LockResolveInfo, lockSpec, DefaultMailLockKeywords, out binding.LockOverrideKeywords);
            BindMailButton(window, ref binding.CollectButton, ref binding.CollectResolveInfo, collectSpec, DefaultMailCollectKeywords, out binding.CollectOverrideKeywords);

            // Detail fields (optional)
            BindMailText(window, ref binding.Sender, ref binding.SenderResolveInfo, senderSpec, DefaultMailSenderKeywords, out binding.SenderOverrideKeywords);
            BindMailText(window, ref binding.Date, ref binding.DateResolveInfo, dateSpec, DefaultMailDateKeywords, out binding.DateOverrideKeywords);
            BindMailText(window, ref binding.Message, ref binding.MessageResolveInfo, messageSpec, DefaultMailMessageKeywords, out binding.MessageOverrideKeywords);
            BindMailText(window, ref binding.Gold, ref binding.GoldResolveInfo, goldSpec, DefaultMailGoldKeywords, out binding.GoldOverrideKeywords);
            BindMailText(window, ref binding.Cost, ref binding.CostResolveInfo, costSpec, DefaultMailCostKeywords, out binding.CostOverrideKeywords);

            // Callbacks
            try
            {
                if (binding.SendButton != null && !binding.SendButton._disposed && binding.SendClickCallback == null)
                {
                    binding.SendClickCallback = OnMobileMailSendClicked;
                    binding.SendButton.onClick.Add(binding.SendClickCallback);
                }

                if (binding.ReplyButton != null && !binding.ReplyButton._disposed && binding.ReplyClickCallback == null)
                {
                    binding.ReplyClickCallback = OnMobileMailReplyClicked;
                    binding.ReplyButton.onClick.Add(binding.ReplyClickCallback);
                }

                if (binding.ReadButton != null && !binding.ReadButton._disposed && binding.ReadClickCallback == null)
                {
                    binding.ReadClickCallback = OnMobileMailReadClicked;
                    binding.ReadButton.onClick.Add(binding.ReadClickCallback);
                }

                if (binding.DeleteButton != null && !binding.DeleteButton._disposed && binding.DeleteClickCallback == null)
                {
                    binding.DeleteClickCallback = OnMobileMailDeleteClicked;
                    binding.DeleteButton.onClick.Add(binding.DeleteClickCallback);
                }

                if (binding.LockButton != null && !binding.LockButton._disposed && binding.LockClickCallback == null)
                {
                    binding.LockClickCallback = OnMobileMailLockClicked;
                    binding.LockButton.onClick.Add(binding.LockClickCallback);
                }

                if (binding.CollectButton != null && !binding.CollectButton._disposed && binding.CollectClickCallback == null)
                {
                    binding.CollectClickCallback = OnMobileMailCollectClicked;
                    binding.CollectButton.onClick.Add(binding.CollectClickCallback);
                }
            }
            catch
            {
            }

            TryDumpMobileMailBindingsIfDue(binding);
        }

        private static void TryRefreshMobileMailIfDue(bool force)
        {
            MobileMailWindowBinding binding = _mobileMailBinding;
            if (binding == null)
                return;

            if (binding.Window == null || binding.Window._disposed)
            {
                ResetMobileMailBindings();
                return;
            }

            bool visible;
            try { visible = binding.Window.visible; } catch { visible = false; }

            if (!visible)
            {
                if (_mobileMailWindowWasVisible)
                {
                    _mobileMailWindowWasVisible = false;
                    CancelMobileMailAttachmentSelection();
                }

                return;
            }

            _mobileMailWindowWasVisible = true;

            if (!force && !_mobileMailDirty)
                return;

            _mobileMailDirty = false;

            var user = GameScene.User;
            List<ClientMail> mails = null;
            try { mails = user?.Mail; } catch { mails = null; }

            ClientMail selected = null;
            if (mails != null && mails.Count > 0)
            {
                ulong selectedId = _mobileMailSelectedMailId;
                if (selectedId < 1)
                    selectedId = mails[0].MailID;

                for (int i = 0; i < mails.Count; i++)
                {
                    if (mails[i] != null && mails[i].MailID == selectedId)
                    {
                        selected = mails[i];
                        _mobileMailSelectedMailId = selectedId;
                        break;
                    }
                }

                if (selected == null)
                {
                    selected = mails[0];
                    _mobileMailSelectedMailId = selected?.MailID ?? 0;
                }
            }
            else
            {
                _mobileMailSelectedMailId = 0;
            }

            TryRefreshMobileMailList(binding, mails, selected);
            TryRefreshMobileMailDetails(binding, selected);
            TryRefreshMobileMailButtons(binding, selected);
        }

        private static void BindMailButton(
            GComponent window,
            ref GButton target,
            ref string resolveInfo,
            string overrideSpec,
            string[] defaultKeywords,
            out string[] usedKeywords)
        {
            usedKeywords = defaultKeywords;

            if (target != null && !target._disposed)
                return;

            GButton button = null;

            if (!string.IsNullOrWhiteSpace(overrideSpec))
            {
                if (TryResolveMobileMainHudObjectBySpec(window, overrideSpec, out GObject resolved, out string[] overrideKeywords))
                {
                    if (resolved is GButton resolvedButton && !resolvedButton._disposed)
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
            }

            if (button == null)
            {
                List<(int Score, GObject Target)> candidates = CollectMobileChatCandidates(window, obj => obj is GButton && obj.touchable, usedKeywords, ScoreMobileShopButtonCandidate);
                button = SelectMobileChatCandidate<GButton>(candidates, minScore: 10);
                resolveInfo = button != null ? "auto " + DescribeObject(window, button) : "auto (miss)";
            }

            target = button;
        }

        private static void BindMailText(
            GComponent window,
            ref GTextField target,
            ref string resolveInfo,
            string overrideSpec,
            string[] defaultKeywords,
            out string[] usedKeywords)
        {
            usedKeywords = defaultKeywords;

            if (target != null && !target._disposed)
                return;

            GTextField field = null;

            if (!string.IsNullOrWhiteSpace(overrideSpec))
            {
                if (TryResolveMobileMainHudObjectBySpec(window, overrideSpec, out GObject resolved, out string[] overrideKeywords))
                {
                    if (resolved is GTextField resolvedText && resolved is not GTextInput && !resolvedText._disposed)
                    {
                        field = resolvedText;
                        resolveInfo = "override " + DescribeObject(window, resolved);
                    }
                    else if (overrideKeywords != null && overrideKeywords.Length > 0)
                    {
                        usedKeywords = overrideKeywords;
                        resolveInfo = "override keywords=" + string.Join("|", usedKeywords);
                    }
                }
            }

            if (field == null)
            {
                List<(int Score, GObject Target)> candidates = CollectMobileChatCandidates(window, obj => obj is GTextField && obj is not GTextInput, usedKeywords, ScoreMobileShopTextCandidate);
                field = SelectMobileChatCandidate<GTextField>(candidates, minScore: 10);
                resolveInfo = field != null ? "auto " + DescribeObject(window, field) : "auto (miss)";
            }

            target = field;
        }

        private static void TryDumpMobileMailBindingsIfDue(MobileMailWindowBinding binding)
        {
            if (!Settings.DebugMode)
                return;

            if (_mobileMailBindingsDumped)
                return;

            if (binding == null || binding.Window == null || binding.Window._disposed)
                return;

            try
            {
                Directory.CreateDirectory(ClientResourceLayout.RuntimeRoot);
                string path = Path.Combine(ClientResourceLayout.RuntimeRoot, "FairyGui-MobileMailBindings.txt");

                var builder = new StringBuilder(8 * 1024);
                builder.AppendLine("FairyGUI 移动端邮件绑定报告");
                builder.AppendLine($"GeneratedAtUtc={DateTime.UtcNow:o}");
                builder.AppendLine($"WindowKey={binding.WindowKey}");
                if (!string.IsNullOrWhiteSpace(binding.ResolveInfo))
                    builder.AppendLine($"Resolved={binding.ResolveInfo}");
                builder.AppendLine();

                builder.AppendLine($"List={DescribeObject(binding.Window, binding.List)}");
                builder.AppendLine($"SendButton={DescribeObject(binding.Window, binding.SendButton)}");
                builder.AppendLine($"ReplyButton={DescribeObject(binding.Window, binding.ReplyButton)}");
                builder.AppendLine($"ReadButton={DescribeObject(binding.Window, binding.ReadButton)}");
                builder.AppendLine($"DeleteButton={DescribeObject(binding.Window, binding.DeleteButton)}");
                builder.AppendLine($"LockButton={DescribeObject(binding.Window, binding.LockButton)}");
                builder.AppendLine($"CollectButton={DescribeObject(binding.Window, binding.CollectButton)}");
                builder.AppendLine($"Sender={DescribeObject(binding.Window, binding.Sender)}");
                builder.AppendLine($"Date={DescribeObject(binding.Window, binding.Date)}");
                builder.AppendLine($"Message={DescribeObject(binding.Window, binding.Message)}");
                builder.AppendLine($"Gold={DescribeObject(binding.Window, binding.Gold)}");
                builder.AppendLine($"Cost={DescribeObject(binding.Window, binding.Cost)}");
                builder.AppendLine();

                builder.AppendLine("OverrideSpec:");
                builder.AppendLine($"  {MobileMailListConfigKey}={binding.ListOverrideSpec}");
                builder.AppendLine($"  {MobileMailSendConfigKey}={binding.SendOverrideSpec}");
                builder.AppendLine($"  {MobileMailReplyConfigKey}={binding.ReplyOverrideSpec}");
                builder.AppendLine($"  {MobileMailReadConfigKey}={binding.ReadOverrideSpec}");
                builder.AppendLine($"  {MobileMailDeleteConfigKey}={binding.DeleteOverrideSpec}");
                builder.AppendLine($"  {MobileMailLockConfigKey}={binding.LockOverrideSpec}");
                builder.AppendLine($"  {MobileMailCollectConfigKey}={binding.CollectOverrideSpec}");
                builder.AppendLine($"  {MobileMailSenderConfigKey}={binding.SenderOverrideSpec}");
                builder.AppendLine($"  {MobileMailDateConfigKey}={binding.DateOverrideSpec}");
                builder.AppendLine($"  {MobileMailMessageConfigKey}={binding.MessageOverrideSpec}");
                builder.AppendLine($"  {MobileMailGoldConfigKey}={binding.GoldOverrideSpec}");
                builder.AppendLine($"  {MobileMailCostConfigKey}={binding.CostOverrideSpec}");
                builder.AppendLine();

                File.WriteAllText(path, builder.ToString(), Encoding.UTF8);
                _mobileMailBindingsDumped = true;
                CMain.SaveLog("FairyGUI: 邮件绑定报告已生成：" + path);
            }
            catch (Exception ex)
            {
                CMain.SaveError("FairyGUI: 写入邮件绑定报告失败：" + ex.Message);
            }
        }

        private static void TryRefreshMobileMailList(MobileMailWindowBinding binding, List<ClientMail> mails, ClientMail selected)
        {
            if (binding == null || binding.List == null || binding.List._disposed)
                return;

            try
            {
                if (binding.ItemRenderer == null)
                {
                    binding.ItemRenderer = (index, obj) => RenderMobileMailListItem(index, obj, mails, selected);
                    binding.List.itemRenderer = binding.ItemRenderer;
                }

                binding.List.numItems = mails?.Count ?? 0;
            }
            catch
            {
            }

            try
            {
                if (binding.Cost != null && !binding.Cost._disposed)
                {
                    uint cost = _mobileMailCostPreview;
                    binding.Cost.text = cost > 0 ? cost.ToString("#,##0") : string.Empty;
                }
            }
            catch
            {
            }
        }

        private static void RenderMobileMailListItem(int index, GObject obj, List<ClientMail> mails, ClientMail selected)
        {
            if (obj is not GComponent itemRoot || itemRoot._disposed)
                return;

            ClientMail mail = null;
            try
            {
                if (mails != null && index >= 0 && index < mails.Count)
                    mail = mails[index];
            }
            catch
            {
                mail = null;
            }

            MobileMailItemView view = GetOrCreateMobileMailItemView(itemRoot);
            if (view == null)
                return;

            try
            {
                if (view.ClickCallback != null)
                    itemRoot.onClick.Remove(view.ClickCallback);
            }
            catch
            {
            }

            try
            {
                int stableIndex = index;
                view.ClickCallback = () => OnMobileMailListItemClicked(stableIndex);
                itemRoot.onClick.Add(view.ClickCallback);
            }
            catch
            {
            }

            if (mail == null)
            {
                try { itemRoot.visible = false; } catch { }
                return;
            }

            try { itemRoot.visible = true; } catch { }

            bool isSelected = selected != null && mail.MailID == selected.MailID;

            try
            {
                if (!view.OriginalAlphaCaptured)
                {
                    view.OriginalAlpha = itemRoot.alpha;
                    view.OriginalAlphaCaptured = true;
                }

                itemRoot.alpha = isSelected ? view.OriginalAlpha : Math.Max(0.2f, view.OriginalAlpha * 0.8f);
            }
            catch
            {
            }

            try { if (view.Sender != null && !view.Sender._disposed) view.Sender.text = mail.SenderName ?? string.Empty; } catch { }

            try
            {
                if (view.Message != null && !view.Message._disposed)
                {
                    string msg = mail.Message ?? string.Empty;
                    msg = msg.Replace("\\r\\n", " ").Replace("\r", " ").Replace("\n", " ");
                    if (msg.Length > 22)
                        msg = msg.Substring(0, 22) + "...";
                    view.Message.text = msg;
                }
            }
            catch
            {
            }

            try { if (view.Date != null && !view.Date._disposed) view.Date.text = mail.DateSent == DateTime.MinValue ? string.Empty : mail.DateSent.ToString("MM-dd HH:mm"); } catch { }

            try { if (view.UnreadMarker != null && !view.UnreadMarker._disposed) view.UnreadMarker.visible = !mail.Opened; } catch { }
            try { if (view.LockedMarker != null && !view.LockedMarker._disposed) view.LockedMarker.visible = mail.Locked; } catch { }
            try { if (view.ParcelMarker != null && !view.ParcelMarker._disposed) view.ParcelMarker.visible = mail.Gold > 0 || (mail.Items?.Count ?? 0) > 0; } catch { }
        }

        private static MobileMailItemView GetOrCreateMobileMailItemView(GComponent itemRoot)
        {
            if (itemRoot == null || itemRoot._disposed)
                return null;

            if (itemRoot.data is MobileMailItemView existing && existing.Root != null && !existing.Root._disposed)
                return existing;

            var view = new MobileMailItemView { Root = itemRoot };

            try
            {
                var senderCandidates = CollectMobileChatCandidates(itemRoot, o => o is GTextField && o is not GTextInput, DefaultMailSenderKeywords, ScoreMobileShopTextCandidate);
                view.Sender = SelectMobileChatCandidate<GTextField>(senderCandidates, minScore: 10);
            }
            catch
            {
            }

            try
            {
                var msgCandidates = CollectMobileChatCandidates(itemRoot, o => o is GTextField && o is not GTextInput, DefaultMailMessageKeywords, ScoreMobileShopTextCandidate);
                view.Message = SelectMobileChatCandidate<GTextField>(msgCandidates, minScore: 10);
            }
            catch
            {
            }

            try
            {
                var dateCandidates = CollectMobileChatCandidates(itemRoot, o => o is GTextField && o is not GTextInput, DefaultMailDateKeywords, ScoreMobileShopTextCandidate);
                view.Date = SelectMobileChatCandidate<GTextField>(dateCandidates, minScore: 10);
            }
            catch
            {
            }

            try { view.UnreadMarker = FindBestMarker(itemRoot, new[] { "unread", "new", "未读", "newmail" }); } catch { }
            try { view.LockedMarker = FindBestMarker(itemRoot, new[] { "lock", "locked", "锁" }); } catch { }
            try { view.ParcelMarker = FindBestMarker(itemRoot, new[] { "parcel", "gift", "item", "附件", "包裹" }); } catch { }

            itemRoot.data = view;
            return view;
        }

        private static GObject FindBestMarker(GComponent root, string[] keywords)
        {
            if (root == null || root._disposed)
                return null;

            if (keywords == null || keywords.Length == 0)
                return null;

            GObject best = null;
            int bestScore = 0;

            foreach (GObject obj in Enumerate(root))
            {
                if (obj == null || obj._disposed)
                    continue;

                if (ReferenceEquals(obj, root))
                    continue;

                int score = ScoreAnyField(obj, keywords, equalsWeight: 160, startsWithWeight: 80, containsWeight: 35);
                if (score <= bestScore)
                    continue;

                bestScore = score;
                best = obj;
            }

            return bestScore >= 60 ? best : null;
        }

        private static void TryRefreshMobileMailDetails(MobileMailWindowBinding binding, ClientMail selected)
        {
            if (binding == null)
                return;

            try { if (binding.Sender != null && !binding.Sender._disposed) binding.Sender.text = selected?.SenderName ?? string.Empty; } catch { }
            try { if (binding.Date != null && !binding.Date._disposed) binding.Date.text = selected == null ? string.Empty : selected.DateSent.ToString("yyyy-MM-dd HH:mm"); } catch { }
            try { if (binding.Message != null && !binding.Message._disposed) binding.Message.text = selected == null ? string.Empty : (selected.Message ?? string.Empty).Replace("\\r\\n", "\r\n"); } catch { }

            try
            {
                if (binding.Gold != null && !binding.Gold._disposed)
                {
                    uint gold = selected?.Gold ?? 0;
                    binding.Gold.text = gold > 0 ? gold.ToString("#,##0") : string.Empty;
                }
            }
            catch
            {
            }
        }

        private static void TryRefreshMobileMailButtons(MobileMailWindowBinding binding, ClientMail selected)
        {
            if (binding == null)
                return;

            try
            {
                if (binding.ReplyButton != null && !binding.ReplyButton._disposed)
                {
                    bool canReply = selected != null && selected.CanReply;
                    binding.ReplyButton.visible = canReply;
                    binding.ReplyButton.touchable = canReply;
                }
            }
            catch
            {
            }

            try
            {
                if (binding.DeleteButton != null && !binding.DeleteButton._disposed)
                {
                    bool canDelete = selected != null && !selected.Locked;
                    binding.DeleteButton.grayed = !canDelete;
                    binding.DeleteButton.touchable = canDelete;
                }
            }
            catch
            {
            }

            try
            {
                if (binding.LockButton != null && !binding.LockButton._disposed)
                {
                    bool has = selected != null;
                    binding.LockButton.visible = has;
                    binding.LockButton.touchable = has;
                    if (has)
                        binding.LockButton.title = selected.Locked ? "解锁" : "锁定";
                }
            }
            catch
            {
            }

            try
            {
                if (binding.CollectButton != null && !binding.CollectButton._disposed)
                {
                    bool hasParcel = selected != null && ((selected.Items?.Count ?? 0) > 0 || selected.Gold > 0);
                    bool canCollect = hasParcel && selected.Collected;
                    binding.CollectButton.visible = hasParcel;
                    binding.CollectButton.touchable = canCollect;
                    binding.CollectButton.grayed = !canCollect;
                }
            }
            catch
            {
            }
        }

        private static void OnMobileMailListItemClicked(int index)
        {
            var user = GameScene.User;
            if (user == null || user.Mail == null)
                return;

            if (index < 0 || index >= user.Mail.Count)
                return;

            ClientMail mail = user.Mail[index];
            if (mail == null)
                return;

            _mobileMailSelectedMailId = mail.MailID;
            _mobileMailDirty = true;
            TrySendReadMailIfNeeded(mail.MailID);
        }

        private static bool TryGetSelectedMobileMail(out ClientMail mail)
        {
            mail = null;

            ulong selectedId = _mobileMailSelectedMailId;
            if (selectedId < 1)
                return false;

            try
            {
                var user = GameScene.User;
                if (user == null || user.Mail == null)
                    return false;

                for (int i = 0; i < user.Mail.Count; i++)
                {
                    ClientMail candidate = user.Mail[i];
                    if (candidate != null && candidate.MailID == selectedId)
                    {
                        mail = candidate;
                        return true;
                    }
                }
            }
            catch
            {
                mail = null;
                return false;
            }

            return false;
        }

        private static void OnMobileMailSendClicked()
        {
            GameScene.Scene?.PromptMobileText(
                title: "写邮件",
                message: "请输入收件人角色名",
                initialText: string.Empty,
                maxLength: 20,
                onOk: name =>
                {
                    string trimmed = (name ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(trimmed))
                    {
                        GameScene.Scene?.OutputMessage("收件人不能为空。");
                        return;
                    }

                    BeginMobileMailCompose(trimmed, preferParcel: false);
                });
        }

        private static void OnMobileMailReplyClicked()
        {
            if (!TryGetSelectedMobileMail(out ClientMail mail))
                return;

            if (mail == null || !mail.CanReply)
                return;

            BeginMobileMailCompose(mail.SenderName, preferParcel: false);
        }

        private static void OnMobileMailReadClicked()
        {
            if (!TryGetSelectedMobileMail(out ClientMail mail))
                return;

            if (mail == null)
                return;

            TrySendReadMailIfNeeded(mail.MailID);

            MobileMailWindowBinding binding = _mobileMailBinding;
            bool hasDetail = binding != null && binding.Message != null && !binding.Message._disposed;
            if (hasDetail)
                return;

            try
            {
                string message = (mail.Message ?? string.Empty).Replace("\\r\\n", "\r\n");
                GameScene.Scene?.OutputMessage(message);
            }
            catch
            {
            }
        }

        private static void OnMobileMailDeleteClicked()
        {
            if (!TryGetSelectedMobileMail(out ClientMail mail))
                return;

            if (mail == null)
                return;

            if (mail.Locked)
            {
                GameScene.Scene?.OutputMessage("邮件已锁定，无法删除。");
                return;
            }

            bool hasParcel = mail.Gold > 0 || (mail.Items?.Count ?? 0) > 0;
            if (!hasParcel)
            {
                try { Network.Enqueue(new C.DeleteMail { MailID = mail.MailID }); } catch { }
                _mobileMailSelectedMailId = 0;
                return;
            }

            try
            {
                var box = new MirMessageBox("确定要删除邮件中的物品或金币？", MirMessageBoxButtons.YesNo);
                if (box.YesButton != null)
                {
                    box.YesButton.Click += (o, e) =>
                    {
                        try { Network.Enqueue(new C.DeleteMail { MailID = mail.MailID }); } catch { }
                        _mobileMailSelectedMailId = 0;
                    };
                }
                box.Show();
            }
            catch
            {
            }
        }

        private static void OnMobileMailLockClicked()
        {
            if (!TryGetSelectedMobileMail(out ClientMail mail))
                return;

            if (mail == null)
                return;

            try
            {
                mail.Locked = !mail.Locked;
                Network.Enqueue(new C.LockMail { MailID = mail.MailID, Lock = mail.Locked });
            }
            catch
            {
            }

            MarkMobileMailDirty();
        }

        private static void OnMobileMailCollectClicked()
        {
            if (!TryGetSelectedMobileMail(out ClientMail mail))
                return;

            if (mail == null)
                return;

            if (!mail.Collected)
            {
                GameScene.Scene?.OutputMessage("邮件必须到客栈领取。");
                return;
            }

            try { Network.Enqueue(new C.CollectParcel { MailID = mail.MailID }); } catch { }
        }

        private static void TrySendReadMailIfNeeded(ulong mailId)
        {
            if (mailId < 1)
                return;

            ClientMail mail = null;
            try
            {
                var user = GameScene.User;
                if (user != null && user.Mail != null)
                {
                    for (int i = 0; i < user.Mail.Count; i++)
                    {
                        ClientMail candidate = user.Mail[i];
                        if (candidate != null && candidate.MailID == mailId)
                        {
                            mail = candidate;
                            break;
                        }
                    }
                }
            }
            catch
            {
                mail = null;
            }

            if (mail == null || mail.Opened)
                return;

            try { Network.Enqueue(new C.ReadMail { MailID = mailId }); } catch { }
        }

        private static void BeginLetterComposeWithPrompts(string recipient)
        {
            GameScene.Scene?.PromptMobileText(
                title: "写邮件",
                message: "请输入内容",
                initialText: string.Empty,
                maxLength: 500,
                onOk: message =>
                {
                    lock (MobileMailComposeGate)
                    {
                        _mobileMailComposeMessage = (message ?? string.Empty).Trim();
                    }

                    TrySendPendingMailCompose(letterOnly: true);
                },
                onCancel: ResetPendingMailCompose);
        }

        private static void BeginParcelComposeWithPrompts(string recipient)
        {
            GameScene.Scene?.PromptMobileText(
                title: "寄包裹",
                message: "请输入留言（可为空）",
                initialText: string.Empty,
                maxLength: 500,
                onOk: message =>
                {
                    lock (MobileMailComposeGate)
                    {
                        _mobileMailComposeMessage = (message ?? string.Empty).Trim();
                    }

                    PromptParcelGoldThenMaybeItem();
                },
                onCancel: ResetPendingMailCompose);
        }

        private static void PromptParcelGoldThenMaybeItem()
        {
            uint maxGold = 0;
            try { maxGold = GameScene.Gold; } catch { maxGold = 0; }

            GameScene.Scene?.PromptMobileText(
                title: "寄包裹",
                message: $"请输入随信金币数量（0-{maxGold:#,##0}）",
                initialText: "0",
                maxLength: 10,
                numericOnly: true,
                onOk: raw =>
                {
                    uint amount = 0;
                    try { uint.TryParse((raw ?? string.Empty).Trim(), out amount); } catch { amount = 0; }

                    if (amount > maxGold)
                        amount = maxGold;

                    lock (MobileMailComposeGate)
                    {
                        _mobileMailComposeGold = amount;
                    }

                    RequestMobileMailCostPreview(gold: amount, item0: 0, stamped: false);

                    try
                    {
                        var box = new MirMessageBox("是否附带一个物品作为包裹附件？（无邮票仅支持 1 个）", MirMessageBoxButtons.YesNo);
                        if (box.YesButton != null)
                            box.YesButton.Click += (o, e) => BeginParcelItemSelectionThenSend();
                        if (box.NoButton != null)
                            box.NoButton.Click += (o, e) => TrySendPendingMailCompose(letterOnly: false);
                        box.Show();
                    }
                    catch
                    {
                        TrySendPendingMailCompose(letterOnly: false);
                    }
                },
                onCancel: ResetPendingMailCompose);
        }

        private static void BeginParcelItemSelectionThenSend()
        {
            lock (MobileMailComposeGate)
            {
                _mobileMailAttachmentSelectionActive = true;
                _mobileMailAttachmentSelectionSlotIndex = 0;
                _mobileMailAttachmentSelectionOnCancel = ResetPendingMailCompose;
                _mobileMailAttachmentSelectionOnSelected = uniqueId =>
                {
                    uint goldToPreview = 0;
                    lock (MobileMailComposeGate)
                    {
                        _mobileMailComposeItem0 = uniqueId;
                        goldToPreview = _mobileMailComposeGold;
                    }

                    RequestMobileMailCostPreview(gold: goldToPreview, item0: uniqueId, stamped: false);
                    TrySendPendingMailCompose(letterOnly: false);
                };
            }

            try
            {
                if (TryShowMobileWindowByKeywords("Inventory", new[] { "背包_DBagUI", "背包", "Bag", "Inventory" }))
                    HideAllMobileWindowsExcept("Inventory");
            }
            catch
            {
            }
        }

        private static void TrySendPendingMailCompose(bool letterOnly)
        {
            string recipient;
            string message;
            uint gold;
            ulong item0;
            bool preferParcel;

            lock (MobileMailComposeGate)
            {
                recipient = _mobileMailComposeRecipient;
                message = _mobileMailComposeMessage;
                gold = _mobileMailComposeGold;
                item0 = _mobileMailComposeItem0;
                preferParcel = _mobileMailComposePendingParcel;
            }

            if (string.IsNullOrWhiteSpace(recipient))
                return;

            message ??= string.Empty;

            try
            {
                var packet = new C.SendMail
                {
                    Name = recipient,
                    Message = message,
                };

                if (!letterOnly || preferParcel)
                {
                    packet.Gold = gold;
                    packet.ItemsIdx = new ulong[5];
                    packet.ItemsIdx[0] = item0;
                    packet.Stamped = false;
                }

                Network.Enqueue(packet);
            }
            catch (Exception ex)
            {
                CMain.SaveError("FairyGUI: 发送邮件失败：" + ex.Message);
            }
            finally
            {
                ResetPendingMailCompose();
            }
        }

        private static void RequestMobileMailCostPreview(uint gold, ulong item0, bool stamped)
        {
            try
            {
                var packet = new C.MailCost
                {
                    Gold = gold,
                    Stamped = stamped,
                };

                if (packet.ItemsIdx != null && packet.ItemsIdx.Length > 0)
                    packet.ItemsIdx[0] = item0;

                Network.Enqueue(packet);
            }
            catch (Exception ex)
            {
                CMain.SaveError("FairyGUI: 请求邮件邮费失败：" + ex.Message);
            }
        }

        private static void ResetPendingMailCompose()
        {
            lock (MobileMailComposeGate)
            {
                _mobileMailComposeRecipient = string.Empty;
                _mobileMailComposeMessage = string.Empty;
                _mobileMailComposeGold = 0;
                _mobileMailComposeItem0 = 0;
                _mobileMailComposePendingParcel = false;

                _mobileMailAttachmentSelectionActive = false;
                _mobileMailAttachmentSelectionSlotIndex = -1;
                _mobileMailAttachmentSelectionOnSelected = null;
                _mobileMailAttachmentSelectionOnCancel = null;
            }
        }
    }
}
