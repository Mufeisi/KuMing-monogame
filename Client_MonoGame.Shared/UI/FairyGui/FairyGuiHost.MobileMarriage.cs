using System;
using System.Collections.Generic;
using FairyGUI;
using Microsoft.Xna.Framework;
using MonoShare.MirScenes;

namespace MonoShare;

/// <summary>FairyGUI seam for the Android marriage/relationship flow.</summary>
internal static partial class FairyGuiHost
{
    private const string MobileMarriageWindowKey = "Relationship";
    private const string MobileMarriagePromptWindowKey = "MarriagePrompt";
    private const string MobileMarriageFallbackName = "__codex_mobile_marriage_fallback";

    private static readonly string[] MobileMarriageWindowKeywords =
        { "关系", "夫妻", "婚姻", "结婚", "离婚", "Marriage", "Relationship", "Lover" };
    private static readonly string[] MobileMarriagePartnerKeywords =
        { "partner", "lover", "spouse", "name", "伴侣", "配偶", "恋人", "姓名" };
    private static readonly string[] MobileMarriageStatusKeywords =
        { "status", "state", "relation", "relationship", "marriage", "关系", "婚姻", "状态" };
    private static readonly string[] MobileMarriageMapKeywords =
        { "map", "location", "place", "online", "offline", "位置", "地图", "在线", "离线" };
    private static readonly string[] MobileMarriageDateKeywords =
        { "date", "married", "days", "日期", "结婚", "时长", "天数" };
    private static readonly string[] MobileMarriageRequestKeywords =
        { "request", "marriage", "propose", "结婚", "求婚", "申请" };
    private static readonly string[] MobileMarriageDivorceKeywords =
        { "divorce", "break", "离婚", "解除" };
    private static readonly string[] MobileMarriageAllowKeywords =
        { "allow", "switch", "toggle", "permission", "允许", "开关", "请求" };

    private sealed class MobileMarriageWindowBinding
    {
        public GComponent Window;
        public GTextField Status;
        public GTextField Partner;
        public GTextField Map;
        public GTextField Date;
        public GTextField Error;
        public GButton Request;
        public GButton Divorce;
        public GButton Allow;
        public EventCallback0 RequestCallback;
        public EventCallback0 DivorceCallback;
        public EventCallback0 AllowCallback;
    }

    private sealed class MobileMarriagePromptBinding
    {
        public GComponent Root;
        public GTextField Title;
        public GTextField Message;
        public GButton Yes;
        public GButton No;
        public EventCallback0 YesCallback;
        public EventCallback0 NoCallback;
        public EventCallback0 DimCallback;
        public Action<bool> Response;
    }

    private static MobileMarriageWindowBinding _mobileMarriageBinding;
    private static MobileMarriagePromptBinding _mobileMarriagePrompt;
    private static bool _mobileMarriageDirty = true;
    private static DateTime _nextMobileMarriageBindAttemptUtc = DateTime.MinValue;

    public static void MarkMobileMarriageDirty()
    {
        _mobileMarriageDirty = true;
        TryRefreshMobileMarriageIfDue(force: false);
    }

    /// <summary>
    /// Toggles the real published Relationship component when available, or a
    /// visible fallback component when the publish has no relationship window.
    /// </summary>
    public static bool TryToggleMobileMarriageWindow(out bool nowVisible)
    {
        nowVisible = false;
        if (_stage == null || !_initialized || !_packagesLoaded)
            return false;

        try
        {
            if (MobileWindows.TryGetValue(MobileMarriageWindowKey, out GComponent existing) &&
                existing != null && !existing._disposed)
            {
                existing.visible = !existing.visible;
                nowVisible = existing.visible;
                if (nowVisible)
                {
                    BringToFront(existing);
                    TryBindMobileMarriageWindow(existing);
                    TryRefreshMobileMarriageIfDue(force: true);
                }

                return true;
            }

            if (!TryCreateMobileWindowComponent(MobileMarriageWindowKey, MobileMarriageWindowKeywords,
                    out GComponent component, out string resolveInfo))
            {
                if (!TryCreateMobileMarriageFallbackWindow(out component, out resolveInfo))
                    return false;
            }

            GComponent layer = _mobileOverlaySafeAreaRoot != null && !_mobileOverlaySafeAreaRoot._disposed
                ? _mobileOverlaySafeAreaRoot
                : (_uiManager?.OverlayLayer ?? GRoot.inst);
            layer.AddChild(component);
            component.AddRelation(layer, RelationType.Size);
            MobileWindows[MobileMarriageWindowKey] = component;
            component.visible = true;
            nowVisible = true;
            TryBindMobileWindowCloseButton(MobileMarriageWindowKey, component);
            TryBindMobileMarriageWindow(component);
            TryRefreshMobileMarriageIfDue(force: true);
            if (Settings.DebugMode && !string.IsNullOrWhiteSpace(resolveInfo))
                CMain.SaveLog("FairyGUI: 关系窗口已创建 -> " + resolveInfo);
            return true;
        }
        catch (Exception ex)
        {
            CMain.SaveError("FairyGUI: 关系窗口切换异常：" + ex.Message);
            return false;
        }
    }

    /// <summary>Shows the incoming proposal/divorce decision prompt.</summary>
    public static bool TryShowMobileMarriagePrompt(string name, bool isDivorce, Action<bool> response)
    {
        return TryShowMobileMarriagePromptCore(
            name,
            isDivorce ? MobileMarriageState.PromptKind.IncomingDivorceRequest : MobileMarriageState.PromptKind.IncomingMarriageProposal,
            response);
    }

    /// <summary>Shows the confirmation prompt before sending our own divorce request.</summary>
    public static bool TryShowMobileDivorceConfirmation(Action<bool> response)
    {
        return TryShowMobileMarriagePromptCore(
            "伴侣",
            MobileMarriageState.PromptKind.OutgoingDivorceConfirmation,
            response);
    }

    private static bool TryShowMobileMarriagePromptCore(string name, MobileMarriageState.PromptKind kind, Action<bool> response)
    {
        if (_stage == null || !_initialized || !_packagesLoaded || response == null)
            return false;

        CloseMobileMarriagePrompt(invokeResponse: false);
        if (!TryCreateMobileMarriagePrompt(out MobileMarriagePromptBinding binding))
            return false;

        binding.Response = response;
        try
        {
            if (binding.Title != null)
                binding.Title.text = MobileMarriageState.GetPromptTitle(kind);
            if (binding.Message != null)
                binding.Message.text = MobileMarriageState.GetPromptMessage(name, kind);
            SetMarriageButtonTitle(binding.Yes, kind == MobileMarriageState.PromptKind.OutgoingDivorceConfirmation ? "确认" : "同意");
            SetMarriageButtonTitle(binding.No, kind == MobileMarriageState.PromptKind.OutgoingDivorceConfirmation ? "取消" : "拒绝");
            binding.Root.visible = true;
            BringToFront(binding.Root);
            _mobileMarriagePrompt = binding;
            return true;
        }
        catch
        {
            CloseMobileMarriagePrompt(invokeResponse: false);
            return false;
        }
    }

    internal static void ResetMobileMarriagePromptForHide()
    {
        CloseMobileMarriagePrompt(invokeResponse: true);
    }

    private static bool TryCreateMobileMarriageFallbackWindow(out GComponent component, out string resolveInfo)
    {
        component = null;
        resolveInfo = null;
        try
        {
            float width = Math.Max(480F, GRoot.inst?.width ?? 720F);
            float height = Math.Max(640F, GRoot.inst?.height ?? 1280F);
            component = new GComponent
            {
                name = MobileMarriageFallbackName,
                touchable = true,
                opaque = false,
            };
            component.SetSize(width, height);

            float panelWidth = Math.Min(width - 32F, 760F);
            float panelHeight = Math.Min(height - 80F, 500F);
            panelWidth = Math.Max(420F, panelWidth);
            panelHeight = Math.Max(420F, panelHeight);
            var panel = new GComponent { name = "marriage_fallback_panel", touchable = true, opaque = true };
            panel.SetSize(panelWidth, panelHeight);
            panel.SetPosition((width - panelWidth) / 2F, (height - panelHeight) / 2F);
            component.AddChild(panel);

            var bg = new GGraph { name = "marriage_fallback_background", touchable = false };
            bg.DrawRect(panelWidth, panelHeight, 2, new Color(110, 145, 185, 255), new Color(28, 34, 52, 245));
            panel.AddChild(bg);
            AddMarriageFallbackText(panel, "marriage_status", "当前未婚。", 24F, 72F, panelWidth - 48F, 74F, 20, Color.White, true);
            AddMarriageFallbackText(panel, "marriage_partner", string.Empty, 24F, 158F, panelWidth - 48F, 34F, 19, Color.LightGray, false);
            AddMarriageFallbackText(panel, "marriage_map", string.Empty, 24F, 198F, panelWidth - 48F, 34F, 18, Color.LightGray, false);
            AddMarriageFallbackText(panel, "marriage_date", string.Empty, 24F, 238F, panelWidth - 48F, 34F, 18, Color.LightGray, false);
            AddMarriageFallbackText(panel, "marriage_error", string.Empty, 24F, 278F, panelWidth - 48F, 42F, 17, new Color(255, 180, 120, 255), false);

            float buttonWidth = Math.Max(112F, (panelWidth - 48F - 16F * 2F) / 3F);
            float buttonY = panelHeight - 66F;
            AddMarriageFallbackButton(panel, "marriage_request", "求婚", 24F, buttonY, buttonWidth, 44F);
            AddMarriageFallbackButton(panel, "marriage_allow", MobileMarriageState.MarriagePermissionActionLabel, 24F + buttonWidth + 8F, buttonY, buttonWidth, 44F);
            AddMarriageFallbackButton(panel, "marriage_divorce", "离婚", 24F + (buttonWidth + 8F) * 2F, buttonY, buttonWidth, 44F);

            GButton close = AddMarriageFallbackButton(panel, "closeButton", "×", panelWidth - 56F, 16F, 36F, 34F);
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

    private static GTextField AddMarriageFallbackText(GComponent parent, string name, string text,
        float x, float y, float width, float height, int size, Color color, bool bold)
    {
        var field = new GTextField
        {
            name = name,
            text = text ?? string.Empty,
            touchable = false,
            align = AlignType.Left,
            verticalAlign = VertAlignType.Middle,
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

    private static GButton AddMarriageFallbackButton(GComponent parent, string name, string title,
        float x, float y, float width, float height)
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
        catch { }
        button.AddChild(label);
        parent.AddChild(button);
        return button;
    }

    private static bool TryCreateMobileMarriagePrompt(out MobileMarriagePromptBinding binding)
    {
        binding = null;
        try
        {
            GComponent layer = _mobileOverlaySafeAreaRoot != null && !_mobileOverlaySafeAreaRoot._disposed
                ? _mobileOverlaySafeAreaRoot
                : (_uiManager?.OverlayLayer ?? GRoot.inst);
            float width = Math.Max(1F, layer.width);
            float height = Math.Max(1F, layer.height);
            var root = new GComponent
            {
                name = MobileMarriagePromptWindowKey,
                touchable = true,
                opaque = true,
                visible = false,
            };
            root.SetSize(width, height);
            root.AddRelation(layer, RelationType.Size);
            var dim = new GGraph { name = "MarriagePromptDim", touchable = true };
            dim.DrawRect(width, height, 0, Color.Transparent, new Color(0, 0, 0, 185));
            root.AddChild(dim);
            var panel = new GComponent { name = "MarriagePromptPanel", touchable = true, opaque = true };
            float panelWidth = Math.Min(680F, Math.Max(320F, width - 48F));
            float panelHeight = 250F;
            panel.SetSize(panelWidth, panelHeight);
            panel.SetPosition((width - panelWidth) / 2F, (height - panelHeight) / 2F);
            root.AddChild(panel);
            var panelBg = new GGraph { name = "MarriagePromptBackground", touchable = false };
            panelBg.DrawRect(panelWidth, panelHeight, 2, new Color(120, 150, 195, 255), new Color(28, 34, 52, 255));
            panel.AddChild(panelBg);
            GTextField title = AddMarriageFallbackText(panel, "MarriagePromptTitle", "关系请求", 24F, 18F, panelWidth - 48F, 40F, 23, Color.White, true);
            GTextField message = AddMarriageFallbackText(panel, "MarriagePromptMessage", string.Empty, 24F, 68F, panelWidth - 48F, 80F, 19, new Color(240, 225, 190, 255), false);
            GButton yes = AddMarriageFallbackButton(panel, "MarriagePromptYes", "同意", 24F, panelHeight - 66F, (panelWidth - 64F) / 2F, 44F);
            GButton no = AddMarriageFallbackButton(panel, "MarriagePromptNo", "拒绝", 40F + (panelWidth - 64F) / 2F, panelHeight - 66F, (panelWidth - 64F) / 2F, 44F);
            layer.AddChild(root);
            binding = new MobileMarriagePromptBinding { Root = root, Title = title, Message = message, Yes = yes, No = no };
            binding.YesCallback = InvokeMobileMarriagePromptYes;
            binding.NoCallback = InvokeMobileMarriagePromptNo;
            binding.DimCallback = InvokeMobileMarriagePromptNo;
            yes.onClick.Add(binding.YesCallback);
            no.onClick.Add(binding.NoCallback);
            dim.onClick.Add(binding.DimCallback);
            MobileWindows[MobileMarriagePromptWindowKey] = root;
            return true;
        }
        catch
        {
            try { binding?.Root?.Dispose(); } catch { }
            binding = null;
            return false;
        }
    }

    private static void InvokeMobileMarriagePromptYes() => InvokeMobileMarriagePrompt(accepted: true);
    private static void InvokeMobileMarriagePromptNo() => InvokeMobileMarriagePrompt(accepted: false);

    private static void InvokeMobileMarriagePrompt(bool accepted)
    {
        MobileMarriagePromptBinding binding = _mobileMarriagePrompt;
        if (binding == null || binding.Root == null || binding.Root._disposed || !binding.Root.visible)
            return;
        Action<bool> response = binding.Response;
        CloseMobileMarriagePrompt(invokeResponse: false);
        try { response?.Invoke(accepted); } catch (Exception ex) { CMain.SaveError("FairyGUI: 关系请求回调异常：" + ex.Message); }
    }

    private static void CloseMobileMarriagePrompt(bool invokeResponse)
    {
        MobileMarriagePromptBinding binding = _mobileMarriagePrompt;
        _mobileMarriagePrompt = null;
        if (binding == null)
            return;
        Action<bool> response = binding.Response;
        binding.Response = null;
        try { binding.Yes?.onClick.Remove(binding.YesCallback); } catch { }
        try { binding.No?.onClick.Remove(binding.NoCallback); } catch { }
        try { binding.Root?.GetChild("MarriagePromptDim")?.onClick.Remove(binding.DimCallback); } catch { }
        try
        {
            if (binding.Root != null && !binding.Root._disposed)
            {
                if (binding.Root.parent != null && !binding.Root.parent._disposed)
                    binding.Root.parent.RemoveChild(binding.Root, dispose: true);
                else
                    binding.Root.Dispose();
            }
        }
        catch { }
        MobileWindows.Remove(MobileMarriagePromptWindowKey);
        if (invokeResponse)
        {
            try { response?.Invoke(false); } catch { }
        }
    }

    private static void TryBindMobileMarriageWindow(GComponent window)
    {
        if (window == null || window._disposed)
            return;
        MobileMarriageWindowBinding binding = _mobileMarriageBinding;
        if (binding != null && (!ReferenceEquals(binding.Window, window) || binding.Window._disposed))
            ResetMobileMarriageBindings();
        binding = _mobileMarriageBinding;
        if (binding == null)
        {
            binding = new MobileMarriageWindowBinding { Window = window };
            var used = new HashSet<GObject>();
            binding.Status = FindMarriageText(window, MobileMarriageStatusKeywords, used);
            binding.Partner = FindMarriageText(window, MobileMarriagePartnerKeywords, used);
            binding.Map = FindMarriageText(window, MobileMarriageMapKeywords, used);
            binding.Date = FindMarriageText(window, MobileMarriageDateKeywords, used);
            binding.Error = FindMarriageText(window, new[] { "error", "fail", "错误", "失败" }, used);
            binding.Request = FindMarriageButton(window, MobileMarriageRequestKeywords, used);
            binding.Divorce = FindMarriageButton(window, MobileMarriageDivorceKeywords, used);
            binding.Allow = FindMarriageButton(window, MobileMarriageAllowKeywords, used);
            _mobileMarriageBinding = binding;

            if (binding.Request != null)
            {
                binding.RequestCallback = () => GameScene.Scene?.TryBeginMobileMarriageRequest();
                binding.Request.onClick.Add(binding.RequestCallback);
            }
            if (binding.Divorce != null)
            {
                binding.DivorceCallback = OnMobileMarriageDivorceClicked;
                binding.Divorce.onClick.Add(binding.DivorceCallback);
            }
            if (binding.Allow != null)
            {
                binding.AllowCallback = () => GameScene.Scene?.ToggleMobileMarriagePermission();
                binding.Allow.onClick.Add(binding.AllowCallback);
            }
        }
        _mobileMarriageDirty = true;
        _nextMobileMarriageBindAttemptUtc = DateTime.UtcNow.AddMilliseconds(650);
    }

    private static void OnMobileMarriageDivorceClicked()
    {
        if (GameScene.Scene?.BeginMobileDivorceConfirmation() != true)
            return;
        if (!TryShowMobileDivorceConfirmation(accepted =>
            {
                if (accepted)
                    GameScene.Scene?.ConfirmMobileDivorceRequest();
                else
                    GameScene.Scene?.RejectMobileDivorceRequest();
            }))
        {
            GameScene.Scene?.RejectMobileDivorceRequest();
        }
    }

    private static void TryRefreshMobileMarriageIfDue(bool force)
    {
        MobileMarriageWindowBinding binding = _mobileMarriageBinding;
        if (binding == null || binding.Window == null || binding.Window._disposed || !binding.Window.visible)
            return;
        if (!force && !_mobileMarriageDirty && DateTime.UtcNow < _nextMobileMarriageBindAttemptUtc)
            return;

        _mobileMarriageDirty = false;
        _nextMobileMarriageBindAttemptUtc = DateTime.UtcNow.AddMilliseconds(650);
        MobileMarriageState state = GameScene.MobileMarriageState;
        string status = state.HasRelationship
            ? (state.PartnerOnline ? "婚姻关系：在线" : "婚姻关系：离线")
            : "当前未婚。";
        if (state.HasPendingMarriageRequest)
            status = state.PendingMarriageRequestName + " 向你求婚，等待确认。";
        else if (state.HasPendingDivorceRequest)
            status = state.PendingDivorceRequestName + " 请求离婚，等待确认。";

        SetMarriageText(binding.Status, status);
        SetMarriageText(binding.Partner, state.HasRelationship ? "伴侣：" + state.PartnerName : string.Empty);
        SetMarriageText(binding.Map, state.HasRelationship
            ? (state.PartnerOnline ? "位置：" + state.PartnerMapName : "位置：离线")
            : string.Empty);
        SetMarriageText(binding.Date, state.HasRelationship
            ? "结婚日期：" + state.MarriedDate.ToShortDateString() + "  时长：" + state.MarriedDays + "天"
            : (state.LastRelationshipDate == default ? string.Empty : "离婚日期：" + state.LastRelationshipDate.ToShortDateString()));
        SetMarriageText(binding.Error, state.Error ?? string.Empty);
        SetMarriageButtonTitle(binding.Allow, state.ChangeMarriageActionLabel);
        SetMarriageButtonEnabled(binding.Request,
            enabled: !state.HasRelationship && !state.HasPendingOutgoingMarriageRequest);
        SetMarriageButtonEnabled(binding.Divorce,
            enabled: state.HasRelationship && !state.HasPendingOutgoingDivorceRequest && !state.DivorceConfirmationPending);
    }

    private static void SetMarriageButtonEnabled(GButton button, bool enabled)
    {
        try
        {
            if (button == null || button._disposed)
                return;
            button.enabled = enabled;
            button.grayed = !enabled;
        }
        catch { }
    }

    private static void SetMarriageButtonTitle(GButton button, string title)
    {
        try
        {
            if (button == null || button._disposed)
                return;

            string safeTitle = title ?? string.Empty;
            button.title = safeTitle;
            for (int i = 0; i < button.numChildren; i++)
            {
                if (button.GetChildAt(i) is GTextField field &&
                    string.Equals(field.name, "title", StringComparison.OrdinalIgnoreCase) &&
                    !field._disposed)
                {
                    field.text = safeTitle;
                }
            }
        }
        catch { }
    }

    private static void SetMarriageText(GTextField field, string value)
    {
        try { if (field != null && !field._disposed) field.text = value ?? string.Empty; } catch { }
    }

    private static void ResetMobileMarriageBindings()
    {
        MobileMarriageWindowBinding binding = _mobileMarriageBinding;
        if (binding != null)
        {
            try { binding.Request?.onClick.Remove(binding.RequestCallback); } catch { }
            try { binding.Divorce?.onClick.Remove(binding.DivorceCallback); } catch { }
            try { binding.Allow?.onClick.Remove(binding.AllowCallback); } catch { }
        }
        _mobileMarriageBinding = null;
        _nextMobileMarriageBindAttemptUtc = DateTime.MinValue;
        _mobileMarriageDirty = true;
    }

    private static GTextField FindMarriageText(GComponent root, string[] keywords, ISet<GObject> used)
    {
        foreach (GObject obj in EnumerateMarriageObjects(root))
        {
            if (obj is not GTextField field || obj is GTextInput || used.Contains(obj))
                continue;
            if (MarriageKeywordScore(obj, keywords) <= 0)
                continue;
            used.Add(obj);
            return field;
        }
        return null;
    }

    private static GButton FindMarriageButton(GComponent root, string[] keywords, ISet<GObject> used)
    {
        foreach (GObject obj in EnumerateMarriageObjects(root))
        {
            if (obj is not GButton button || used.Contains(obj))
                continue;
            if (MarriageKeywordScore(obj, keywords) <= 0)
                continue;
            used.Add(obj);
            return button;
        }
        return null;
    }

    private static IEnumerable<GObject> EnumerateMarriageObjects(GComponent root)
    {
        if (root == null || root._disposed)
            yield break;
        yield return root;
        for (int i = 0; i < root.numChildren; i++)
        {
            GObject child = root.GetChildAt(i);
            if (child == null)
                continue;
            if (child is GComponent component)
            {
                foreach (GObject nested in EnumerateMarriageObjects(component))
                    yield return nested;
            }
            else
                yield return child;
        }
    }

    private static int MarriageKeywordScore(GObject obj, string[] keywords)
    {
        if (obj == null || keywords == null)
            return 0;
        string haystack = (obj.name ?? string.Empty) + " " + (obj.packageItem?.name ?? string.Empty) + " " + (obj.resourceURL ?? string.Empty);
        if (obj is GButton button)
            haystack += " " + (button.title ?? string.Empty);
        int score = 0;
        foreach (string keyword in keywords)
        {
            if (!string.IsNullOrWhiteSpace(keyword) && haystack.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                score++;
        }
        return score;
    }
}
