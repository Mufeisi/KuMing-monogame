using System;
using System.Collections.Generic;
using FairyGUI;
using Microsoft.Xna.Framework;
using MonoShare.MirNetwork;
using MonoShare.MirScenes;

namespace MonoShare;

internal static partial class FairyGuiHost
{
    private const string MobileMentorFallbackName = "__codex_mobile_mentor_fallback";

    private static readonly string[] MobileMentorStatusKeywords =
        { "status", "state", "summary", "relation", "mentor", "mentee", "状态", "关系", "师徒" };
    private static readonly string[] MobileMentorPartnerNameKeywords =
        { "partner", "name", "mentor", "mentee", "师傅", "徒弟", "姓名", "名字" };
    private static readonly string[] MobileMentorPartnerLevelKeywords =
        { "level", "lv", "等级", "级" };
    private static readonly string[] MobileMentorOnlineKeywords =
        { "online", "offline", "status", "在线", "离线" };
    private static readonly string[] MobileMentorExpKeywords =
        { "exp", "experience", "menteeexp", "经验" };
    private static readonly string[] MobileMentorErrorKeywords =
        { "error", "fail", "message", "错误", "失败", "提示" };
    private static readonly string[] MobileMentorRequestInputKeywords =
        { "request", "add", "name", "mentor", "拜师", "师傅", "角色名", "输入" };
    private static readonly string[] MobileMentorAddKeywords =
        { "add", "request", "apply", "mentor", "拜师", "添加", "申请" };
    private static readonly string[] MobileMentorRemoveKeywords =
        { "remove", "cancel", "break", "解除", "取消", "断绝" };
    private static readonly string[] MobileMentorAllowKeywords =
        { "allow", "toggle", "request", "允许", "拒绝", "拜师请求", "开关" };
    private static readonly string[] MobileMentorAcceptKeywords =
        { "accept", "agree", "yes", "ok", "同意", "接受", "确定" };
    private static readonly string[] MobileMentorRejectKeywords =
        { "reject", "deny", "no", "拒绝", "不同意" };

    private sealed class MobileMentorWindowBinding
    {
        public GComponent Window;
        public GTextField Status;
        public GTextField PartnerName;
        public GTextField PartnerLevel;
        public GTextField Online;
        public GTextField Exp;
        public GTextField Error;
        public GTextInput RequestInput;
        public GButton AddButton;
        public GButton RemoveButton;
        public GButton AllowButton;
        public GButton AcceptButton;
        public GButton RejectButton;
        public GComponent CancelDialog;
        public GButton CancelYesButton;
        public GButton CancelNoButton;
        public EventCallback0 AddCallback;
        public EventCallback0 RemoveCallback;
        public EventCallback0 AllowCallback;
        public EventCallback0 AcceptCallback;
        public EventCallback0 RejectCallback;
        public EventCallback0 CancelYesCallback;
        public EventCallback0 CancelNoCallback;
    }

    private static MobileMentorWindowBinding _mobileMentorBinding;
    private static DateTime _nextMobileMentorBindAttemptUtc = DateTime.MinValue;
    private static bool _mobileMentorDirty = true;

    private static bool TryCreateMobileMentorFallbackWindow(out GComponent component, out string resolveInfo)
    {
        component = null;
        resolveInfo = null;

        try
        {
            float rootWidth = Math.Max(480F, GRoot.inst?.width ?? 720F);
            float rootHeight = Math.Max(640F, GRoot.inst?.height ?? 1280F);
            component = new GComponent
            {
                name = MobileMentorFallbackName,
                touchable = true,
                opaque = false,
            };
            component.SetSize(rootWidth, rootHeight);

            float panelWidth = Math.Min(rootWidth - 32F, 760F);
            float panelHeight = Math.Min(rootHeight - 80F, 520F);
            panelWidth = Math.Max(420F, panelWidth);
            panelHeight = Math.Max(430F, panelHeight);
            var panel = new GComponent
            {
                name = "mentor_fallback_panel",
                touchable = true,
                opaque = true,
            };
            panel.SetSize(panelWidth, panelHeight);
            panel.SetPosition((rootWidth - panelWidth) / 2F, (rootHeight - panelHeight) / 2F);
            component.AddChild(panel);

            var panelBackground = new GGraph { name = "mentor_fallback_background", touchable = false };
            panelBackground.DrawRect(panelWidth, panelHeight, 2, new Color(90, 110, 150, 255), new Color(25, 30, 45, 245));
            panel.AddChild(panelBackground);

            AddMentorFallbackText(panel, "mentor_title", "师徒关系", 24F, 20F, panelWidth - 80F, 42F, 25, Color.White, bold: true);
            AddMentorFallbackText(panel, "mentor_status", "当前没有师徒关系。", 24F, 72F, panelWidth - 48F, 74F, 19, Color.White, bold: false);
            AddMentorFallbackText(panel, "mentor_partner_name", string.Empty, 24F, 154F, panelWidth - 48F, 32F, 19, Color.LightGray, bold: false);
            AddMentorFallbackText(panel, "mentor_partner_level", string.Empty, 24F, 190F, panelWidth - 48F, 30F, 18, Color.LightGray, bold: false);
            AddMentorFallbackText(panel, "mentor_online", string.Empty, 24F, 224F, panelWidth - 48F, 30F, 18, Color.LightGray, bold: false);
            AddMentorFallbackText(panel, "mentor_exp", string.Empty, 24F, 258F, panelWidth - 48F, 30F, 18, Color.LightGray, bold: false);
            AddMentorFallbackText(panel, "mentor_error", string.Empty, 24F, 292F, panelWidth - 48F, 34F, 17, new Color(255, 180, 120, 255), bold: false);

            var inputBackground = new GGraph { name = "mentor_request_input_background", touchable = false };
            inputBackground.DrawRect(panelWidth - 48F, 44F, 2, new Color(80, 95, 125, 255), new Color(10, 15, 25, 220));
            inputBackground.SetPosition(24F, 338F);
            panel.AddChild(inputBackground);

            var requestInput = new GTextInput
            {
                name = "mentor_request_input",
                touchable = true,
                editable = true,
                promptText = "输入师傅角色名",
                text = string.Empty,
                align = AlignType.Left,
                verticalAlign = VertAlignType.Middle,
                autoSize = AutoSizeType.None,
            };
            requestInput.SetPosition(34F, 342F);
            requestInput.SetSize(panelWidth - 68F, 36F);
            try
            {
                requestInput.textFormat.size = 18;
                requestInput.textFormat.color = Color.White;
            }
            catch
            {
            }
            panel.AddChild(requestInput);

            float buttonY = panelHeight - 66F;
            float buttonWidth = Math.Max(92F, (panelWidth - 48F - 16F) / 5F);
            AddMentorFallbackButton(panel, "mentor_add", "发起拜师", 24F, buttonY, buttonWidth, 44F);
            AddMentorFallbackButton(panel, "mentor_allow", "请求开关", 24F + (buttonWidth + 4F), buttonY, buttonWidth, 44F);
            AddMentorFallbackButton(panel, "mentor_accept", "同意", 24F + (buttonWidth + 4F) * 2F, buttonY, buttonWidth, 44F);
            AddMentorFallbackButton(panel, "mentor_reject", "拒绝", 24F + (buttonWidth + 4F) * 3F, buttonY, buttonWidth, 44F);
            AddMentorFallbackButton(panel, "mentor_remove", "解除关系", 24F + (buttonWidth + 4F) * 4F, buttonY, buttonWidth, 44F);

            GButton close = AddMentorFallbackButton(panel, "closeButton", "×", panelWidth - 56F, 16F, 36F, 34F);
            close.title = "关闭";
            resolveInfo = "fallback";
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

    private static GTextField AddMentorFallbackText(
        GComponent parent,
        string name,
        string text,
        float x,
        float y,
        float width,
        float height,
        int fontSize,
        Color color,
        bool bold)
    {
        var field = new GTextField
        {
            name = name,
            touchable = false,
            text = text ?? string.Empty,
            align = AlignType.Left,
            verticalAlign = VertAlignType.Middle,
            autoSize = AutoSizeType.None,
            singleLine = false,
        };
        field.SetPosition(x, y);
        field.SetSize(width, height);
        try
        {
            field.textFormat.size = fontSize;
            field.textFormat.color = color;
            field.textFormat.bold = bold;
        }
        catch
        {
        }
        parent.AddChild(field);
        return field;
    }

    private static GButton AddMentorFallbackButton(
        GComponent parent,
        string name,
        string title,
        float x,
        float y,
        float width,
        float height)
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
        button.SetPosition(x, y);
        button.SetSize(width, height);

        var background = new GGraph { name = name + "_background", touchable = false };
        background.DrawRect(width, height, 2, new Color(120, 150, 195, 255), new Color(50, 75, 110, 255));
        button.AddChild(background);

        var label = new GTextField
        {
            name = "title",
            text = title ?? string.Empty,
            touchable = false,
            align = AlignType.Center,
            verticalAlign = VertAlignType.Middle,
            autoSize = AutoSizeType.None,
        };
        label.SetSize(width, height);
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
        parent.AddChild(button);
        return button;
    }

    private static bool TryShowMobileMentorCancelConfirmation()
    {
        MobileMentorWindowBinding binding = _mobileMentorBinding;
        if (binding == null || binding.Window == null || binding.Window._disposed)
            return false;

        if (binding.CancelDialog != null && !binding.CancelDialog._disposed)
        {
            try
            {
                binding.CancelDialog.visible = true;
                binding.Window.SetChildIndex(binding.CancelDialog, binding.Window.numChildren - 1);
            }
            catch
            {
            }
            return true;
        }

        GComponent dialog = null;
        try
        {
            float windowWidth = Math.Max(1F, binding.Window.width);
            float windowHeight = Math.Max(1F, binding.Window.height);
            if (windowWidth < 360F)
                windowWidth = Math.Max(windowWidth, GRoot.inst?.width ?? 480F);
            if (windowHeight < 260F)
                windowHeight = Math.Max(windowHeight, GRoot.inst?.height ?? 640F);

            dialog = new GComponent
            {
                name = "__codex_mobile_mentor_cancel_dialog",
                touchable = true,
                opaque = true,
            };
            dialog.SetSize(windowWidth, windowHeight);

            var dim = new GGraph { name = "mentor_cancel_dim", touchable = false };
            dim.DrawRect(windowWidth, windowHeight, 0, Color.Transparent, new Color(0, 0, 0, 190));
            dialog.AddChild(dim);

            float panelWidth = Math.Min(620F, Math.Max(320F, windowWidth - 48F));
            float panelHeight = Math.Min(240F, Math.Max(205F, windowHeight - 48F));
            var panel = new GComponent
            {
                name = "mentor_cancel_panel",
                touchable = true,
                opaque = true,
            };
            panel.SetSize(panelWidth, panelHeight);
            panel.SetPosition((windowWidth - panelWidth) / 2F, (windowHeight - panelHeight) / 2F);
            dialog.AddChild(panel);

            var panelBackground = new GGraph { name = "mentor_cancel_background", touchable = false };
            panelBackground.DrawRect(panelWidth, panelHeight, 2,
                new Color(115, 145, 190, 255), new Color(28, 34, 52, 255));
            panel.AddChild(panelBackground);

            AddMentorFallbackText(panel, "mentor_cancel_title", "解除师徒关系", 24F, 20F,
                panelWidth - 48F, 38F, 23, Color.White, bold: true);
            AddMentorFallbackText(panel, "mentor_cancel_message",
                "解除后服务器会在一段时间内禁止再次使用师徒功能。\n确定继续吗？",
                24F, 64F, panelWidth - 48F, 72F, 18,
                new Color(240, 225, 190, 255), bold: false);

            float buttonWidth = Math.Max(120F, (panelWidth - 72F) / 2F);
            float buttonY = panelHeight - 62F;
            GButton yes = AddMentorFallbackButton(panel, "mentor_cancel_yes", "确定解除",
                24F, buttonY, buttonWidth, 44F);
            GButton no = AddMentorFallbackButton(panel, "mentor_cancel_no", "取消",
                48F + buttonWidth, buttonY, buttonWidth, 44F);

            binding.CancelDialog = dialog;
            binding.CancelYesButton = yes;
            binding.CancelNoButton = no;
            binding.CancelYesCallback = () =>
            {
                bool confirmed = false;
                try { confirmed = GameScene.Scene?.ConfirmMobileMentorCancellation() == true; } catch { }
                if (!confirmed)
                    GameScene.MobileMentorState.RejectCancelMentorship();
                CloseMobileMentorCancelDialog(binding, clearPending: false);
                MarkMobileMentorDirty();
            };
            binding.CancelNoCallback = () =>
            {
                try { GameScene.Scene?.RejectMobileMentorCancellation(); } catch { }
                CloseMobileMentorCancelDialog(binding, clearPending: false);
                MarkMobileMentorDirty();
            };
            BindMentorButton(binding.CancelYesButton, ref binding.CancelYesCallback, binding.CancelYesCallback);
            BindMentorButton(binding.CancelNoButton, ref binding.CancelNoCallback, binding.CancelNoCallback);

            binding.Window.AddChild(dialog);
            binding.Window.SetChildIndex(dialog, binding.Window.numChildren - 1);
            return true;
        }
        catch
        {
            try { dialog?.Dispose(); } catch { }
            CloseMobileMentorCancelDialog(binding, clearPending: true);
            return false;
        }
    }

    private static void CloseMobileMentorCancelDialog(MobileMentorWindowBinding binding, bool clearPending)
    {
        if (binding == null)
            return;

        if (clearPending)
        {
            try { GameScene.MobileMentorState.RejectCancelMentorship(); } catch { }
        }

        RemoveMentorCallback(binding.CancelYesButton, binding.CancelYesCallback);
        RemoveMentorCallback(binding.CancelNoButton, binding.CancelNoCallback);

        GComponent dialog = binding.CancelDialog;
        binding.CancelDialog = null;
        binding.CancelYesButton = null;
        binding.CancelNoButton = null;
        binding.CancelYesCallback = null;
        binding.CancelNoCallback = null;

        if (dialog == null || dialog._disposed)
            return;

        try
        {
            if (dialog.parent != null && !dialog.parent._disposed)
                dialog.parent.RemoveChild(dialog, dispose: true);
            else
                dialog.Dispose();
        }
        catch
        {
            try { dialog.Dispose(); } catch { }
        }
    }

    public static void MarkMobileMentorDirty()
    {
        _mobileMentorDirty = true;
        TryRefreshMobileMentorIfDue(force: false);
    }

    /// <summary>
    /// Clears Mentor-only transient UI when its HUD toggle hides the window.
    /// Other mobile windows keep their existing toggle lifecycle.
    /// </summary>
    internal static void ResetMobileMentorForHide()
    {
        ResetMobileMentorBindings();
    }

    private static void ResetMobileMentorBindings()
    {
        MobileMentorWindowBinding binding = _mobileMentorBinding;
        if (binding != null)
        {
            CloseMobileMentorCancelDialog(binding, clearPending: true);
            RemoveMentorCallback(binding.AddButton, binding.AddCallback);
            RemoveMentorCallback(binding.RemoveButton, binding.RemoveCallback);
            RemoveMentorCallback(binding.AllowButton, binding.AllowCallback);
            RemoveMentorCallback(binding.AcceptButton, binding.AcceptCallback);
            RemoveMentorCallback(binding.RejectButton, binding.RejectCallback);
        }
        else
        {
            try { GameScene.MobileMentorState.RejectCancelMentorship(); } catch { }
        }

        _mobileMentorBinding = null;
        _nextMobileMentorBindAttemptUtc = DateTime.MinValue;
        _mobileMentorDirty = true;
    }

    private static void RemoveMentorCallback(GButton button, EventCallback0 callback)
    {
        if (button == null || button._disposed || callback == null)
            return;

        try { button.onClick.Remove(callback); } catch { }
    }

    private static bool IsMobileMentorCancelObject(GObject target)
    {
        GObject current = target;
        int depth = 0;
        while (current != null && depth++ < 32)
        {
            if (string.Equals(current.name, "__codex_mobile_mentor_cancel_dialog", StringComparison.OrdinalIgnoreCase))
                return true;

            current = current.parent;
        }

        return false;
    }

    private static GTextField ResolveMobileMentorText(
        GComponent window,
        string[] keywords,
        ISet<GObject> used,
        int minScore)
    {
        try
        {
            List<(int Score, GObject Target)> candidates = CollectMobileChatCandidates(
                window,
                obj => obj is GTextField && obj is not GTextInput &&
                       !IsMobileMentorCancelObject(obj) &&
                       (used == null || !used.Contains(obj)),
                keywords,
                ScoreMobileShopTextCandidate);
            GTextField field = SelectMobileChatCandidate<GTextField>(candidates, minScore);
            if (field != null)
                used?.Add(field);
            return field;
        }
        catch
        {
            return null;
        }
    }

    private static GButton ResolveMobileMentorButton(
        GComponent window,
        string[] keywords,
        ISet<GObject> used,
        int minScore)
    {
        try
        {
            List<(int Score, GObject Target)> candidates = CollectMobileChatCandidates(
                window,
                obj => obj is GButton && !IsMobileMentorCancelObject(obj) &&
                       (used == null || !used.Contains(obj)),
                keywords,
                ScoreMobileShopButtonCandidate);
            GButton button = SelectMobileChatCandidate<GButton>(candidates, minScore);
            if (button != null)
                used?.Add(button);
            return button;
        }
        catch
        {
            return null;
        }
    }

    private static void TryBindMobileMentorWindowIfDue(string windowKey, GComponent window, string resolveInfo)
    {
        if (window == null || window._disposed)
            return;

        MobileMentorWindowBinding binding = _mobileMentorBinding;
        if (binding != null && (binding.Window == null || binding.Window._disposed || !ReferenceEquals(binding.Window, window)))
        {
            ResetMobileMentorBindings();
            binding = null;
        }

        if (binding == null)
        {
            binding = new MobileMentorWindowBinding { Window = window };
            _mobileMentorBinding = binding;
            _nextMobileMentorBindAttemptUtc = DateTime.MinValue;
        }

        if (DateTime.UtcNow < _nextMobileMentorBindAttemptUtc)
            return;
        _nextMobileMentorBindAttemptUtc = DateTime.UtcNow.AddSeconds(2);

        var used = new HashSet<GObject>();
        AddMentorUsed(used, binding.Status);
        AddMentorUsed(used, binding.PartnerName);
        AddMentorUsed(used, binding.PartnerLevel);
        AddMentorUsed(used, binding.Online);
        AddMentorUsed(used, binding.Exp);
        AddMentorUsed(used, binding.Error);
        AddMentorUsed(used, binding.RequestInput);
        AddMentorUsed(used, binding.AddButton);
        AddMentorUsed(used, binding.RemoveButton);
        AddMentorUsed(used, binding.AllowButton);
        AddMentorUsed(used, binding.AcceptButton);
        AddMentorUsed(used, binding.RejectButton);
        if (binding.Status == null || binding.Status._disposed)
            binding.Status = ResolveMobileMentorText(window, MobileMentorStatusKeywords, used, 15);
        if (binding.PartnerName == null || binding.PartnerName._disposed)
            binding.PartnerName = ResolveMobileMentorText(window, MobileMentorPartnerNameKeywords, used, 18);
        if (binding.PartnerLevel == null || binding.PartnerLevel._disposed)
            binding.PartnerLevel = ResolveMobileMentorText(window, MobileMentorPartnerLevelKeywords, used, 15);
        if (binding.Online == null || binding.Online._disposed)
            binding.Online = ResolveMobileMentorText(window, MobileMentorOnlineKeywords, used, 15);
        if (binding.Exp == null || binding.Exp._disposed)
            binding.Exp = ResolveMobileMentorText(window, MobileMentorExpKeywords, used, 15);
        if (binding.Error == null || binding.Error._disposed)
            binding.Error = ResolveMobileMentorText(window, MobileMentorErrorKeywords, used, 15);

        if (binding.RequestInput == null || binding.RequestInput._disposed)
        {
            try
            {
                List<(int Score, GObject Target)> candidates = CollectMobileChatCandidates(
                    window,
                    obj => obj is GTextInput && obj.touchable && !IsMobileMentorCancelObject(obj),
                    MobileMentorRequestInputKeywords,
                    ScoreMobileChatInputCandidate);
                binding.RequestInput = SelectMobileChatCandidate<GTextInput>(candidates, 20);
                if (binding.RequestInput != null)
                    used.Add(binding.RequestInput);
            }
            catch
            {
            }
        }

        if (binding.AddButton == null || binding.AddButton._disposed)
            binding.AddButton = ResolveMobileMentorButton(window, MobileMentorAddKeywords, used, 20);
        if (binding.RemoveButton == null || binding.RemoveButton._disposed)
            binding.RemoveButton = ResolveMobileMentorButton(window, MobileMentorRemoveKeywords, used, 20);
        if (binding.AllowButton == null || binding.AllowButton._disposed)
            binding.AllowButton = ResolveMobileMentorButton(window, MobileMentorAllowKeywords, used, 20);
        if (binding.AcceptButton == null || binding.AcceptButton._disposed)
            binding.AcceptButton = ResolveMobileMentorButton(window, MobileMentorAcceptKeywords, used, 20);
        if (binding.RejectButton == null || binding.RejectButton._disposed)
            binding.RejectButton = ResolveMobileMentorButton(window, MobileMentorRejectKeywords, used, 20);

        BindMentorButton(binding.AddButton, ref binding.AddCallback, OnMobileMentorAddClicked);
        BindMentorButton(binding.RemoveButton, ref binding.RemoveCallback, OnMobileMentorRemoveClicked);
        BindMentorButton(binding.AllowButton, ref binding.AllowCallback, OnMobileMentorAllowClicked);
        BindMentorButton(binding.AcceptButton, ref binding.AcceptCallback, OnMobileMentorAcceptClicked);
        BindMentorButton(binding.RejectButton, ref binding.RejectCallback, OnMobileMentorRejectClicked);
    }

    private static void AddMentorUsed(ISet<GObject> used, GObject target)
    {
        if (used == null || target == null || target._disposed)
            return;

        used.Add(target);
    }

    private static void BindMentorButton(GButton button, ref EventCallback0 callback, EventCallback0 handler)
    {
        if (button == null || button._disposed || handler == null)
            return;

        try
        {
            if (callback == null)
                callback = handler;
            button.onClick.Remove(callback);
            button.onClick.Add(callback);
            button.touchable = true;
        }
        catch
        {
        }
    }

    private static void OnMobileMentorAddClicked()
    {
        MobileMentorWindowBinding binding = _mobileMentorBinding;
        string name = string.Empty;
        try { name = binding?.RequestInput?.text ?? string.Empty; } catch { }
        if (string.IsNullOrWhiteSpace(name))
        {
            GameScene.Scene?.MobileReceiveChat("请输入师傅角色名。", ChatType.System);
            return;
        }

        GameScene.Scene?.TryBeginMobileMentorRequest(name);
    }

    private static void OnMobileMentorRemoveClicked()
    {
        GameScene scene = GameScene.Scene;
        if (scene == null || !scene.BeginMobileMentorCancellation())
            return;

        if (!TryShowMobileMentorCancelConfirmation())
        {
            GameScene.MobileMentorState.RejectCancelMentorship();
            MarkMobileMentorDirty();
        }
    }

    private static void OnMobileMentorAllowClicked()
    {
        GameScene.Scene?.ToggleMobileMentorRequests();
    }

    private static void OnMobileMentorAcceptClicked()
    {
        if (GameScene.MobileMentorState.HasPendingRequest)
            GameScene.Scene?.RespondToMobileMentorRequest(accepted: true);
    }

    private static void OnMobileMentorRejectClicked()
    {
        if (GameScene.MobileMentorState.HasPendingRequest)
            GameScene.Scene?.RespondToMobileMentorRequest(accepted: false);
    }

    private static void TryRefreshMobileMentorIfDue(bool force)
    {
        if (_stage == null || !_initialized || !_packagesLoaded)
            return;

        if (!MobileWindows.TryGetValue("Mentor", out GComponent window) || window == null || window._disposed)
        {
            if (_mobileMentorBinding != null)
                ResetMobileMentorBindings();
            return;
        }

        if (!window.visible)
            return;

        TryBindMobileMentorWindowIfDue("Mentor", window, resolveInfo: null);
        MobileMentorWindowBinding binding = _mobileMentorBinding;
        if (binding == null || binding.Window == null || binding.Window._disposed)
        {
            ResetMobileMentorBindings();
            return;
        }

        if (!force && !_mobileMentorDirty)
            return;
        _mobileMentorDirty = false;

        MobileMentorState state = GameScene.MobileMentorState;
        if (!state.CancelConfirmationPending && binding.CancelDialog != null)
            CloseMobileMentorCancelDialog(binding, clearPending: false);

        string status;
        if (state.HasMentorship)
        {
            string role = state.IsMentor ? "师傅" : "徒弟";
            status = $"{role}关系：{state.PartnerName}（等级 {state.PartnerLevel}）";
        }
        else
        {
            status = "当前没有师徒关系。";
        }

        if (state.HasPendingRequest)
            status += $"\n待处理拜师：{state.PendingRequestName}（等级 {state.PendingRequestLevel}）";
        else if (state.HasPendingOutgoingRequest)
            status += $"\n拜师请求已发送：{state.PendingOutgoingName}";

        SetMentorText(binding.Status, status);
        SetMentorText(binding.PartnerName, state.HasMentorship ? state.PartnerName : string.Empty);
        SetMentorText(binding.PartnerLevel, state.HasMentorship ? $"等级 {state.PartnerLevel}" : string.Empty);
        SetMentorText(binding.Online, state.HasMentorship ? (state.PartnerOnline ? "在线" : "离线") : string.Empty);
        SetMentorText(binding.Exp, state.ShouldShowMenteeExperience ? $"师徒经验：{state.MenteeEXP}" : string.Empty);
        SetMentorText(binding.Error, state.Error ?? string.Empty);

        bool pending = state.CanRespondToPendingRequest;
        SetMentorButtonAvailability(binding.AcceptButton, pending, hideWhenDisabled: true);
        SetMentorButtonAvailability(binding.RejectButton, pending, hideWhenDisabled: true);
        SetMentorButtonAvailability(binding.RemoveButton, state.CanCancelMentorship);
        SetMentorButtonAvailability(binding.AddButton, state.CanRequestMentor);
    }

    private static void SetMentorText(GTextField field, string text)
    {
        if (field == null || field._disposed)
            return;

        try { field.text = text ?? string.Empty; } catch { }
    }

    private static void SetMentorButtonAvailability(GButton button, bool enabled, bool hideWhenDisabled = false)
    {
        if (button == null || button._disposed)
            return;

        try
        {
            button.visible = enabled || !hideWhenDisabled;
            button.touchable = enabled;
            button.grayed = !enabled;
        }
        catch
        {
        }
    }
}
