using System;

using S = ServerPackets;

namespace MonoShare.MirScenes;

/// <summary>
/// Main-thread state seam for the server-authoritative marriage flow.
///
/// Marriage and divorce requests contain no target id: the server resolves
/// the player in front of the local character.  The client therefore tracks
/// only transient prompt/confirmation state and the latest <see
/// cref="S.LoverUpdate"/> snapshot; it never infers or mutates a relationship
/// without an authoritative update.
/// </summary>
public sealed class MobileMarriageState
{
    /// <summary>
    /// The mobile Allow action keeps the server's dual meaning visible: before
    /// marriage C.ChangeMarriage toggles marriage requests; after marriage it
    /// toggles the spouse summon/recall permission.
    /// </summary>
    public const string MarriagePermissionActionLabel = "允许/禁止结婚";
    public const string LoverRecallActionLabel = "允许/阻止召唤";
    public const string MarriagePermissionChangedMessage = "已切换结婚请求开关。";
    public const string LoverRecallChangedMessage = "已切换伴侣召唤权限。";

    public string ChangeMarriageActionLabel => HasRelationship
        ? LoverRecallActionLabel
        : MarriagePermissionActionLabel;

    public string ChangeMarriageResultMessage => HasRelationship
        ? LoverRecallChangedMessage
        : MarriagePermissionChangedMessage;

    public enum PromptKind
    {
        IncomingMarriageProposal,
        IncomingDivorceRequest,
        OutgoingDivorceConfirmation,
    }

    /// <summary>Returns the title used by the mobile same-layer prompt.</summary>
    public static string GetPromptTitle(PromptKind kind)
    {
        return kind switch
        {
            PromptKind.IncomingMarriageProposal => "求婚请求",
            PromptKind.IncomingDivorceRequest => "离婚请求",
            PromptKind.OutgoingDivorceConfirmation => "确认离婚",
            _ => "关系请求",
        };
    }

    /// <summary>
    /// Builds the prompt copy while keeping incoming request acceptance and
    /// outgoing divorce confirmation as distinct actions.
    /// </summary>
    public static string GetPromptMessage(string name, PromptKind kind)
    {
        string safeName = string.IsNullOrWhiteSpace(name) ? "对方" : name.Trim();
        return kind switch
        {
            PromptKind.IncomingMarriageProposal => safeName + " 向你求婚，是否同意？",
            PromptKind.IncomingDivorceRequest => safeName + " 请求与你离婚，是否同意？",
            PromptKind.OutgoingDivorceConfirmation => "确认向伴侣提出离婚？",
            _ => "请确认关系请求。",
        };
    }

    /// <summary>
    /// A server request has no result packet for several validation paths.
    /// Keep the single-send guard for a short bounded window, then allow a
    /// retry rather than permanently disabling the mobile action.
    /// </summary>
    public const long OutgoingRequestTimeoutMs = 5000;

    public sealed class PartnerSnapshot
    {
        public string Name { get; internal set; } = string.Empty;
        public DateTime Date { get; internal set; }
        public string MapName { get; internal set; } = string.Empty;
        public short MarriedDays { get; internal set; }
        public bool Online { get; internal set; }
    }

    public bool IsOpen { get; private set; }
    public PartnerSnapshot Partner { get; private set; }
    public bool HasRelationship => Partner != null && !string.IsNullOrWhiteSpace(Partner.Name);
    public string PartnerName => Partner?.Name ?? string.Empty;
    public DateTime MarriedDate => Partner?.Date ?? LastRelationshipDate;
    public string PartnerMapName => Partner?.MapName ?? string.Empty;
    public short MarriedDays => Partner?.MarriedDays ?? 0;
    public bool PartnerOnline => Partner?.Online == true;

    /// <summary>The date carried by an empty LoverUpdate (divorce/no relationship).</summary>
    public DateTime LastRelationshipDate { get; private set; }

    public string PendingMarriageRequestName { get; private set; } = string.Empty;
    public bool HasPendingMarriageRequest => !string.IsNullOrWhiteSpace(PendingMarriageRequestName);
    public string PendingDivorceRequestName { get; private set; } = string.Empty;
    public bool HasPendingDivorceRequest => !string.IsNullOrWhiteSpace(PendingDivorceRequestName);

    public bool HasPendingOutgoingMarriageRequest { get; private set; }
    public bool HasPendingOutgoingDivorceRequest { get; private set; }
    public bool DivorceConfirmationPending { get; private set; }

    public bool? LastMarriageAccepted { get; private set; }
    public bool? LastDivorceAccepted { get; private set; }
    public string Error { get; private set; }
    public int Revision { get; private set; }

    private long _pendingOutgoingMarriageSinceMs = -1;
    private long _pendingOutgoingDivorceSinceMs = -1;

    private static readonly string[] MarriageRequestResultMarkers =
    {
        "你已结婚",
        "需要面对面才能完成结婚",
        "不能结婚",
        "结婚要求等级",
        "结婚对象要求等级",
        "对方拒绝了你的结婚请求",
        "拒绝求婚",
        "不能跟自己结婚",
        "结婚对象角色死亡",
        "已有结婚邀请",
        "非结婚范围内",
        "对方已婚",
        "暂不支持向同性求婚",
        "恭喜！你现在迎娶了",
        "你现在嫁给了",
    };

    private static readonly string[] DivorceRequestResultMarkers =
    {
        "必须面对面才能完成离婚",
        "不能自己离婚",
        "离婚对象角色死亡",
        "不在离婚范围内",
        "你还没有嫁给",
        "未婚，所以不需要离婚",
        "拒绝和你离婚",
        "你现在离婚了",
        "你已离婚了",
    };

    /// <summary>Records an outgoing marriage action before a server update.</summary>
    public bool BeginMarriageRequest()
    {
        return BeginMarriageRequest(nowMs: -1);
    }

    /// <summary>Records an outgoing marriage action at a caller-supplied clock tick.</summary>
    public bool BeginMarriageRequest(long nowMs)
    {
        if (HasPendingOutgoingMarriageRequest)
        {
            SetFailure("求婚请求已在处理中。", clearSnapshot: false);
            return false;
        }

        if (HasRelationship)
        {
            SetFailure("你已结婚。", clearSnapshot: false);
            return false;
        }

        HasPendingOutgoingMarriageRequest = true;
        _pendingOutgoingMarriageSinceMs = NormalizeClock(nowMs);
        LastMarriageAccepted = null;
        Error = null;
        IsOpen = true;
        Revision++;
        return true;
    }

    /// <summary>Applies the server's incoming proposal prompt.</summary>
    public bool ApplyMarriageRequest(S.MarriageRequest packet)
    {
        if (packet == null)
        {
            SetFailure("求婚请求为空。", clearSnapshot: false);
            return false;
        }

        string name = (packet.Name ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            SetFailure("求婚请求无效。", clearSnapshot: false);
            return false;
        }

        PendingMarriageRequestName = name;
        LastMarriageAccepted = null;
        Error = null;
        IsOpen = true;
        Revision++;
        return true;
    }

    /// <summary>Completes exactly one response to the pending proposal prompt.</summary>
    public bool ApplyMarriageReply(bool accepted)
    {
        if (!HasPendingMarriageRequest)
        {
            SetFailure("没有待处理的求婚请求。", clearSnapshot: false);
            return false;
        }

        PendingMarriageRequestName = string.Empty;
        LastMarriageAccepted = accepted;
        Error = null;
        IsOpen = true;
        Revision++;
        return true;
    }

    /// <summary>Starts the local confirmation step before sending DivorceRequest.</summary>
    public bool BeginDivorceConfirmation()
    {
        if (!HasRelationship)
        {
            SetFailure("当前没有婚姻关系。", clearSnapshot: false);
            return false;
        }

        if (DivorceConfirmationPending || HasPendingOutgoingDivorceRequest)
        {
            SetFailure("离婚确认已在处理中。", clearSnapshot: false);
            return false;
        }

        DivorceConfirmationPending = true;
        Error = null;
        IsOpen = true;
        Revision++;
        return true;
    }

    /// <summary>Confirms one divorce request; repeated clicks cannot send twice.</summary>
    public bool ConfirmDivorceRequest()
    {
        return ConfirmDivorceRequest(nowMs: -1);
    }

    /// <summary>Confirms one divorce request at a caller-supplied clock tick.</summary>
    public bool ConfirmDivorceRequest(long nowMs)
    {
        if (!DivorceConfirmationPending || !HasRelationship)
            return false;

        DivorceConfirmationPending = false;
        HasPendingOutgoingDivorceRequest = true;
        _pendingOutgoingDivorceSinceMs = NormalizeClock(nowMs);
        LastDivorceAccepted = null;
        Error = null;
        Revision++;
        return true;
    }

    /// <summary>Closes the confirmation modal without changing the relationship.</summary>
    public bool RejectDivorceRequest()
    {
        if (!DivorceConfirmationPending)
            return false;

        DivorceConfirmationPending = false;
        Error = null;
        Revision++;
        return true;
    }

    /// <summary>Applies the server's incoming divorce prompt.</summary>
    public bool ApplyDivorceRequest(S.DivorceRequest packet)
    {
        if (packet == null)
        {
            SetFailure("离婚请求为空。", clearSnapshot: false);
            return false;
        }

        string name = (packet.Name ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            SetFailure("离婚请求无效。", clearSnapshot: false);
            return false;
        }

        PendingDivorceRequestName = name;
        LastDivorceAccepted = null;
        Error = null;
        IsOpen = true;
        Revision++;
        return true;
    }

    /// <summary>Completes exactly one response to the pending divorce prompt.</summary>
    public bool ApplyDivorceReply(bool accepted)
    {
        if (!HasPendingDivorceRequest)
        {
            SetFailure("没有待处理的离婚请求。", clearSnapshot: false);
            return false;
        }

        PendingDivorceRequestName = string.Empty;
        LastDivorceAccepted = accepted;
        Error = null;
        IsOpen = true;
        Revision++;
        return true;
    }

    /// <summary>
    /// Releases an outgoing request when the server reports a terminal result
    /// through its existing system-chat path.  Marriage/Divorce have no
    /// failure packet, so leaving these flags set would permanently disable
    /// the corresponding mobile button after a face-to-face/range failure or
    /// a partner rejection.  Unknown system messages deliberately do not
    /// release the guard; a successful request remains single-shot until the
    /// authoritative LoverUpdate arrives.
    /// </summary>
    public bool ApplyServerSystemMessage(string message)
    {
        string text = (message ?? string.Empty).Trim();
        if (text.Length == 0)
            return false;

        bool marriageResult = HasPendingOutgoingMarriageRequest && ContainsMarker(text, MarriageRequestResultMarkers);
        bool divorceResult = HasPendingOutgoingDivorceRequest && ContainsMarker(text, DivorceRequestResultMarkers);
        if (!marriageResult && !divorceResult)
            return false;

        bool isSuccess = text.Contains("恭喜！你现在迎娶了", StringComparison.Ordinal) ||
                         text.Contains("你现在嫁给了", StringComparison.Ordinal) ||
                         text.Contains("你现在离婚了", StringComparison.Ordinal) ||
                         text.Contains("你已离婚了", StringComparison.Ordinal);

        // A success chat is followed by LoverUpdate.  Keep the guard until
        // that authoritative snapshot arrives so the user cannot send the
        // same request twice during the small delivery gap.
        if (isSuccess)
            return false;

        if (marriageResult)
        {
            HasPendingOutgoingMarriageRequest = false;
            _pendingOutgoingMarriageSinceMs = -1;
        }
        if (divorceResult)
        {
            HasPendingOutgoingDivorceRequest = false;
            _pendingOutgoingDivorceSinceMs = -1;
        }

        Error = isSuccess ? null : text;
        IsOpen = true;
        Revision++;
        return true;
    }

    /// <summary>
    /// Advances the bounded request guards on the game/main thread.  Returns
    /// true exactly when a timeout released a guard, allowing the scene to
    /// refresh its FUI button state without polling or sleeping in tests.
    /// </summary>
    public bool Tick(long nowMs)
    {
        long now = NormalizeClock(nowMs);
        bool changed = false;

        if (HasPendingOutgoingMarriageRequest && IsExpired(_pendingOutgoingMarriageSinceMs, now))
        {
            HasPendingOutgoingMarriageRequest = false;
            _pendingOutgoingMarriageSinceMs = -1;
            Error = "求婚请求超时，可重试。";
            IsOpen = true;
            Revision++;
            changed = true;
        }

        if (HasPendingOutgoingDivorceRequest && IsExpired(_pendingOutgoingDivorceSinceMs, now))
        {
            HasPendingOutgoingDivorceRequest = false;
            _pendingOutgoingDivorceSinceMs = -1;
            Error = "离婚请求超时，可重试。";
            IsOpen = true;
            Revision++;
            changed = true;
        }

        return changed;
    }

    /// <summary>Applies an authoritative relationship snapshot.</summary>
    public bool ApplyLoverUpdate(S.LoverUpdate packet)
    {
        if (packet == null)
        {
            SetFailure("婚姻状态更新为空。", clearSnapshot: false);
            return false;
        }

        string name = (packet.Name ?? string.Empty).Trim();
        string mapName = (packet.MapName ?? string.Empty).Trim();
        if (packet.MarriedDays < 0)
        {
            SetFailure("婚姻状态更新无效。", clearSnapshot: false);
            return false;
        }

        LastRelationshipDate = packet.Date;

        // The server intentionally sends Name="" for divorce/no relationship.
        // Keep the date for the PC-equivalent history label but remove the
        // active partner and all pending actions.
        if (name.Length == 0)
        {
            Partner = null;
            PendingMarriageRequestName = string.Empty;
            PendingDivorceRequestName = string.Empty;
            HasPendingOutgoingMarriageRequest = false;
            _pendingOutgoingMarriageSinceMs = -1;
            HasPendingOutgoingDivorceRequest = false;
            _pendingOutgoingDivorceSinceMs = -1;
            DivorceConfirmationPending = false;
            LastMarriageAccepted = null;
            LastDivorceAccepted = null;
            Error = null;
            IsOpen = true;
            Revision++;
            return true;
        }

        Partner = new PartnerSnapshot
        {
            Name = name,
            Date = packet.Date,
            MapName = mapName,
            MarriedDays = packet.MarriedDays,
            // Server uses an empty map name for an offline spouse.
            Online = mapName.Length > 0,
        };
        PendingMarriageRequestName = string.Empty;
        PendingDivorceRequestName = string.Empty;
        HasPendingOutgoingMarriageRequest = false;
        _pendingOutgoingMarriageSinceMs = -1;
        HasPendingOutgoingDivorceRequest = false;
        _pendingOutgoingDivorceSinceMs = -1;
        DivorceConfirmationPending = false;
        LastMarriageAccepted = null;
        LastDivorceAccepted = null;
        Error = null;
        IsOpen = true;
        Revision++;
        return true;
    }

    public void ResetForSession()
    {
        IsOpen = false;
        Partner = null;
        LastRelationshipDate = default;
        PendingMarriageRequestName = string.Empty;
        PendingDivorceRequestName = string.Empty;
        HasPendingOutgoingMarriageRequest = false;
        _pendingOutgoingMarriageSinceMs = -1;
        HasPendingOutgoingDivorceRequest = false;
        _pendingOutgoingDivorceSinceMs = -1;
        DivorceConfirmationPending = false;
        LastMarriageAccepted = null;
        LastDivorceAccepted = null;
        Error = null;
        Revision++;
    }

    private static long NormalizeClock(long nowMs)
    {
        return nowMs < 0 ? 0 : nowMs;
    }

    private static bool IsExpired(long startedAtMs, long nowMs)
    {
        return startedAtMs >= 0 && nowMs >= startedAtMs && nowMs - startedAtMs >= OutgoingRequestTimeoutMs;
    }

    private void SetFailure(string message, bool clearSnapshot)
    {
        if (clearSnapshot)
        {
            Partner = null;
            LastRelationshipDate = default;
        }

        IsOpen = true;
        Error = string.IsNullOrWhiteSpace(message) ? "婚姻数据不可用。" : message;
        Revision++;
    }

    private static bool ContainsMarker(string text, string[] markers)
    {
        if (markers == null)
            return false;

        for (int i = 0; i < markers.Length; i++)
        {
            string marker = markers[i];
            if (!string.IsNullOrWhiteSpace(marker) && text.Contains(marker, StringComparison.Ordinal))
                return true;
        }

        return false;
    }
}
