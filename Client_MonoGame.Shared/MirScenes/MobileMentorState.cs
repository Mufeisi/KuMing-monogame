using System;

using S = ServerPackets;

namespace MonoShare.MirScenes;

/// <summary>
/// Main-thread state seam for the server-authoritative mentor/mentee flow.
///
/// The wire protocol only carries the partner's name/level, so the role is
/// derived from the local level supplied by the scene.  No client-side rule
/// decides whether a relationship is allowed; this class only applies the
/// latest server snapshot and keeps request UI state.
/// </summary>
public sealed class MobileMentorState
{
    public enum Role
    {
        None,
        Mentor,
        Mentee,
    }

    public sealed class PartnerSnapshot
    {
        public string Name { get; internal set; }
        public ushort Level { get; internal set; }
        public bool Online { get; internal set; }
        public long MenteeEXP { get; internal set; }
    }

    private ushort _localLevel;

    public bool IsOpen { get; private set; }
    public PartnerSnapshot Partner { get; private set; }
    public Role RelationshipRole { get; private set; }
    public bool IsMentor => RelationshipRole == Role.Mentor;
    public bool IsMentee => RelationshipRole == Role.Mentee;
    public bool HasMentorship => Partner != null;
    public bool CanRequestMentor => !HasMentorship;
    public bool CanRespondToPendingRequest => HasPendingRequest;
    public bool CanCancelMentorship => HasMentorship && !CancelConfirmationPending;
    public bool ShouldShowMenteeExperience => IsMentor;

    public string PartnerName => Partner?.Name ?? string.Empty;
    public ushort PartnerLevel => Partner?.Level ?? 0;
    public bool PartnerOnline => Partner?.Online == true;
    public long MenteeEXP => Partner?.MenteeEXP ?? 0;

    public string PendingRequestName { get; private set; } = string.Empty;
    public ushort PendingRequestLevel { get; private set; }
    public bool HasPendingRequest => !string.IsNullOrWhiteSpace(PendingRequestName);

    public string PendingOutgoingName { get; private set; } = string.Empty;
    public bool HasPendingOutgoingRequest => !string.IsNullOrWhiteSpace(PendingOutgoingName);

    public bool CancelConfirmationPending { get; private set; }

    public bool? LastRequestAccepted { get; private set; }
    public string Error { get; private set; }
    public int Revision { get; private set; }

    /// <summary>Updates the local level used only to label the partner as mentor/mentee.</summary>
    public void SetLocalLevel(ushort localLevel)
    {
        _localLevel = localLevel;
        RecomputeRole();
    }

    /// <summary>Records an outgoing AddMentor intent before the server responds.</summary>
    public bool BeginMentorRequest(string name)
    {
        string trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            SetFailure("师徒名称不能为空。", clearSnapshot: false);
            return false;
        }

        PendingOutgoingName = trimmed;
        Error = null;
        IsOpen = true;
        Revision++;
        return true;
    }

    /// <summary>Applies the S.MentorRequest prompt sent to the proposed mentor.</summary>
    public bool ApplyMentorRequest(S.MentorRequest packet)
    {
        if (packet == null)
        {
            SetFailure("师徒请求为空。", clearSnapshot: false);
            return false;
        }

        string name = (packet.Name ?? string.Empty).Trim();
        if (name.Length == 0 || packet.Level == 0)
        {
            SetFailure("师徒请求无效。", clearSnapshot: false);
            return false;
        }

        PendingRequestName = name;
        PendingRequestLevel = packet.Level;
        LastRequestAccepted = null;
        Error = null;
        IsOpen = true;
        Revision++;
        return true;
    }

    /// <summary>
    /// Records the local response to the pending S.MentorRequest prompt.  The
    /// authoritative relationship still arrives later as S.MentorUpdate.
    /// </summary>
    public bool ApplyMentorRequestReply(bool accepted)
    {
        if (!HasPendingRequest)
        {
            SetFailure("没有待处理的师徒请求。", clearSnapshot: false);
            return false;
        }

        PendingRequestName = string.Empty;
        PendingRequestLevel = 0;
        LastRequestAccepted = accepted;
        Error = null;
        IsOpen = true;
        Revision++;
        return true;
    }

    /// <summary>Starts the local confirmation step before sending CancelMentor.</summary>
    public bool BeginCancelConfirmation()
    {
        if (!HasMentorship)
        {
            SetFailure("当前没有师徒关系。", clearSnapshot: false);
            return false;
        }

        if (CancelConfirmationPending)
        {
            SetFailure("解除师徒确认已在处理中。", clearSnapshot: false);
            return false;
        }

        CancelConfirmationPending = true;
        Error = null;
        IsOpen = true;
        Revision++;
        return true;
    }

    /// <summary>Confirms the local CancelMentor prompt; only the first call succeeds.</summary>
    public bool ConfirmCancelMentorship()
    {
        if (!CancelConfirmationPending || !HasMentorship)
            return false;

        CancelConfirmationPending = false;
        Revision++;
        return true;
    }

    /// <summary>Rejects the local CancelMentor prompt without changing the relationship.</summary>
    public bool RejectCancelMentorship()
    {
        if (!CancelConfirmationPending)
            return false;

        CancelConfirmationPending = false;
        Revision++;
        return true;
    }

    /// <summary>Applies the server's authoritative relationship snapshot.</summary>
    public bool ApplyMentorUpdate(S.MentorUpdate packet, ushort localLevel = 0)
    {
        if (packet == null)
        {
            SetFailure("师徒状态更新为空。", clearSnapshot: false);
            return false;
        }

        if (localLevel != 0)
            _localLevel = localLevel;

        string name = (packet.Name ?? string.Empty).Trim();
        if (name.Length == 0 && packet.Level == 0 && !packet.Online && packet.MenteeEXP == 0)
        {
            Partner = null;
            RelationshipRole = Role.None;
            PendingOutgoingName = string.Empty;
            CancelConfirmationPending = false;
            Error = null;
            IsOpen = true;
            Revision++;
            return true;
        }

        if (name.Length == 0 || packet.Level == 0 || packet.MenteeEXP < 0)
        {
            SetFailure("师徒状态更新无效。", clearSnapshot: false);
            return false;
        }

        Partner = new PartnerSnapshot
        {
            Name = name,
            Level = packet.Level,
            Online = packet.Online,
            MenteeEXP = packet.MenteeEXP,
        };
        PendingOutgoingName = string.Empty;
        CancelConfirmationPending = false;
        Error = null;
        IsOpen = true;
        RecomputeRole();
        Revision++;
        return true;
    }

    public void ResetForSession()
    {
        _localLevel = 0;
        IsOpen = false;
        Partner = null;
        RelationshipRole = Role.None;
        PendingRequestName = string.Empty;
        PendingRequestLevel = 0;
        PendingOutgoingName = string.Empty;
        CancelConfirmationPending = false;
        LastRequestAccepted = null;
        Error = null;
        Revision++;
    }

    private void RecomputeRole()
    {
        if (Partner == null)
        {
            RelationshipRole = Role.None;
            return;
        }

        RelationshipRole = _localLevel > Partner.Level ? Role.Mentor : Role.Mentee;
    }

    private void SetFailure(string message, bool clearSnapshot)
    {
        if (clearSnapshot)
        {
            Partner = null;
            RelationshipRole = Role.None;
            PendingOutgoingName = string.Empty;
            CancelConfirmationPending = false;
        }

        IsOpen = true;
        Error = string.IsNullOrWhiteSpace(message) ? "师徒数据不可用。" : message;
        Revision++;
    }
}
