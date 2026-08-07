using System;
using System.Drawing;

using S = ServerPackets;

namespace MonoShare.MirScenes;

/// <summary>
/// Main-thread state seam for the mobile fishing flow.
///
/// FishingUpdate is the only authoritative fishing status packet.  The
/// client keeps request gates and the user's latest intent here, but never
/// promotes a FishingChangeAutocast request into an authoritative enabled
/// flag (the wire protocol does not carry that state back to the client).
/// </summary>
public sealed class MobileFishingState
{
    public const long RequestTimeoutMs = 5000;
    public const long SlotRequestTimeoutMs = 5000;

    private long _castRequestSinceMs = -1;
    private long _autoCastRequestSinceMs = -1;
    private long _slotRequestSinceMs = -1;
    private bool _castRequestCastOut;
    private int _slotRequest = -1;
    private ulong _slotRequestSourceUniqueId;

    public bool IsOpen { get; private set; }
    public long LocalObjectId { get; private set; }
    public ulong FishingRodUniqueId { get; private set; }
    public bool HasFishingRod { get; private set; }
    public bool HasReel { get; private set; }
    public bool Fishing { get; private set; }
    public int ServerProgressPercent { get; private set; }
    public int ServerChancePercent { get; private set; }
    public int ProgressPercent { get; private set; }
    public int ChancePercent { get; private set; }
    public Point FishingPoint { get; private set; }
    public bool FoundFish { get; private set; }

    /// <summary>
    /// The most recent local auto-cast intent. This is deliberately not named
    /// or treated as a server-confirmed state: the wire protocol has no
    /// acknowledgement field for it. It is cleared only by an explicit local
    /// toggle, an explicit equipment-disable packet, or a session reset; a
    /// death/map transition preserves it until the next user toggle.
    /// </summary>
    public bool AutoCastIntent { get; private set; }
    public bool CastRequestPending => _castRequestSinceMs >= 0;
    public bool AutoCastRequestPending => _autoCastRequestSinceMs >= 0;
    public bool SlotRequestPending => _slotRequestSinceMs >= 0;
    public int PendingFishingSlot => _slotRequest;
    public ulong PendingSlotSourceUniqueId => _slotRequestSourceUniqueId;
    public string Error { get; private set; }
    public int Revision { get; private set; }

    /// <summary>
    /// Detects an authoritative rod/reel loss while auto-cast still looks
    /// enabled locally. A no-rod authoritative snapshot is deliberately not a
    /// disable trigger: the server ignores FishingChangeAutocast while no rod
    /// is equipped. For a present rod with reel/rod loss, the caller sends the
    /// explicit false packet after applying the new snapshot.
    /// </summary>
    public bool NeedsAutoCastDisableForEquipment(bool hasFishingRod, bool hasReel,
        ulong fishingRodUniqueId)
    {
        if (!AutoCastIntent || !hasFishingRod)
            return false;

        // A no-rod snapshot is handled separately: once a new rod is
        // authoritative, its non-zero identity is a real transition and the
        // server can now accept the explicit false packet.
        bool rodChanged = FishingRodUniqueId != fishingRodUniqueId;
        bool reelLost = HasReel && !hasReel;
        return rodChanged || reelLost;
    }

    public bool ShouldDisableAutoCastBeforeEquipmentChange(bool currentWeaponIsFishingRod)
    {
        return AutoCastIntent && currentWeaponIsFishingRod;
    }

    /// <summary>
    /// Explicitly disables local auto-cast intent. Returns true exactly once
    /// while a true intent is being cleared, so equipment transitions cannot
    /// enqueue duplicate false packets on repeated snapshots.
    /// </summary>
    public bool DisableAutoCastIntent()
    {
        bool shouldSendFalse = AutoCastIntent;
        bool changed = shouldSendFalse || AutoCastRequestPending;
        AutoCastIntent = false;
        _autoCastRequestSinceMs = -1;
        if (changed)
        {
            Error = null;
            Touch();
        }

        return shouldSendFalse;
    }

    public static bool CanRequestCastOutAt(long nowMs, long fishingTime, bool dead,
        bool ridingMount, MirAction currentAction, bool mapAllowsFishing)
    {
        if (dead || ridingMount || currentAction != MirAction.Standing || !mapAllowsFishing)
            return false;

        long now = NormalizeClock(nowMs);
        long last = NormalizeClock(fishingTime);
        return now >= last && now - last >= 1000;
    }

    public void ReportLocalError(string message)
    {
        SetError(message, clearFishing: false);
    }

    public bool SetEquipmentSnapshot(long objectId, bool hasFishingRod, bool hasReel, ulong fishingRodUniqueId = 0)
    {
        if (objectId <= 0)
        {
            SetError("钓鱼装备状态无效。", clearFishing: false);
            return false;
        }

        bool objectChanged = LocalObjectId != objectId;
        bool rodChanged = FishingRodUniqueId != fishingRodUniqueId;
        bool changed = objectChanged || HasFishingRod != hasFishingRod || HasReel != hasReel ||
                       rodChanged;
        LocalObjectId = objectId;
        HasFishingRod = hasFishingRod;
        HasReel = hasFishingRod && hasReel;
        FishingRodUniqueId = hasFishingRod ? fishingRodUniqueId : 0;

        if (!HasFishingRod || objectChanged || rodChanged)
        {
            Fishing = false;
            ProgressPercent = 0;
            ChancePercent = 0;
            ServerProgressPercent = 0;
            ServerChancePercent = 0;
            FoundFish = false;
            _castRequestSinceMs = -1;
            _castRequestCastOut = false;
            _autoCastRequestSinceMs = -1;
            ClearSlotRequest();
        }
        else if (!HasReel)
        {
            // A reel is required by the server for auto-cast.  Removing it
            // clears only the in-flight guard. The intent is cleared by the
            // caller's explicit false packet, not by this snapshot setter.
            _autoCastRequestSinceMs = -1;
        }

        if (changed)
        {
            Error = null;
            Touch();
        }

        return true;
    }

    public bool BeginCastRequest(bool castOut, long nowMs)
    {
        if (!HasFishingRod && castOut)
        {
            SetError("没有鱼竿。", clearFishing: false);
            return false;
        }

        bool hadPendingRequest = CastRequestPending;
        if (CastRequestPending && (castOut || !_castRequestCastOut))
        {
            SetError("钓鱼请求已在处理中。", clearFishing: false);
            return false;
        }

        if (castOut && Fishing)
        {
            SetError("当前已经抛竿。", clearFishing: false);
            return false;
        }

        if (!castOut && !Fishing && !hadPendingRequest)
        {
            SetError("当前没有正在进行的钓鱼。", clearFishing: false);
            return false;
        }

        _castRequestSinceMs = NormalizeClock(nowMs);
        _castRequestCastOut = castOut;
        Error = null;
        IsOpen = true;
        Touch();
        return true;
    }

    public bool BeginAutoCastRequest(bool enabled, long nowMs)
    {
        if (!HasFishingRod || !HasReel)
        {
            SetError("需要装备摇轮才能自动抛竿。", clearFishing: false);
            return false;
        }

        if (AutoCastRequestPending)
        {
            SetError("自动抛竿请求已在处理中。", clearFishing: false);
            return false;
        }

        AutoCastIntent = enabled;
        _autoCastRequestSinceMs = NormalizeClock(nowMs);
        Error = null;
        IsOpen = true;
        Touch();
        return true;
    }

    /// <summary>
    /// Starts a single equipment/merge/remove request from the fishing picker.
    /// A local gate prevents double taps from enqueueing duplicate packets
    /// before the server's slot refresh arrives.
    /// </summary>
    public bool BeginSlotRequest(int fishingSlot, ulong sourceUniqueId, long nowMs)
    {
        if (fishingSlot < 0 || fishingSlot >= 5 || sourceUniqueId == 0)
        {
            SetError("钓鱼配件请求无效。", clearFishing: false);
            return false;
        }

        if (SlotRequestPending)
        {
            SetError("钓鱼配件请求已在处理中。", clearFishing: false);
            return false;
        }

        _slotRequest = fishingSlot;
        _slotRequestSourceUniqueId = sourceUniqueId;
        _slotRequestSinceMs = NormalizeClock(nowMs);
        Error = null;
        IsOpen = true;
        Touch();
        return true;
    }

    public void CompleteSlotRequest()
    {
        if (!SlotRequestPending)
            return;

        ClearSlotRequest();
        Touch();
    }

    /// <summary>Applies only a local FishingUpdate; world-player updates are ignored.</summary>
    public bool ApplyFishingUpdate(S.FishingUpdate packet, long localObjectId)
    {
        if (packet == null)
        {
            SetError("钓鱼状态更新为空。", clearFishing: false);
            return false;
        }

        long expectedId = localObjectId > 0 ? localObjectId : LocalObjectId;
        if (expectedId > 0 && packet.ObjectID != expectedId)
            return false;

        if (packet.ObjectID <= 0 || packet.ProgressPercent < 0 || packet.ChancePercent < 0)
        {
            SetError("钓鱼状态更新无效。", clearFishing: false);
            return false;
        }

        LocalObjectId = packet.ObjectID;
        Fishing = packet.Fishing;
        ServerProgressPercent = packet.ProgressPercent;
        ServerChancePercent = packet.ChancePercent;
        ProgressPercent = ClampPercent(packet.ProgressPercent);
        ChancePercent = ClampPercent(packet.ChancePercent);
        FishingPoint = packet.FishingPoint;
        FoundFish = packet.FoundFish;
        _castRequestSinceMs = -1;
        _castRequestCastOut = false;

        // No AutoCast field exists in S.FishingUpdate.  End/reject updates
        // release only the in-flight toggle guard; keep the last local
        // intent so a later tap can explicitly send the opposite value.
        if (!packet.Fishing)
        {
            _autoCastRequestSinceMs = -1;
        }

        Error = null;
        IsOpen = true;
        Touch();
        return true;
    }

    /// <summary>Releases a pending request only for fishing-related failure chat.</summary>
    public bool ApplyServerSystemMessage(string message)
    {
        string text = (message ?? string.Empty).Trim();
        if (text.Length == 0 || (!CastRequestPending && !AutoCastRequestPending && !SlotRequestPending) ||
            !IsFishingFailure(text))
            return false;

        _castRequestSinceMs = -1;
        _castRequestCastOut = false;
        _autoCastRequestSinceMs = -1;
        ClearSlotRequest();
        Error = text;
        IsOpen = true;
        Touch();
        return true;
    }

    public bool Tick(long nowMs)
    {
        long now = NormalizeClock(nowMs);
        bool changed = false;

        if (IsExpired(_castRequestSinceMs, now))
        {
            _castRequestSinceMs = -1;
            _castRequestCastOut = false;
            Error = "钓鱼请求超时，可重试。";
            changed = true;
        }

        if (IsExpired(_autoCastRequestSinceMs, now))
        {
            _autoCastRequestSinceMs = -1;
            Error = "自动抛竿结果未确认，可再次点击切换。";
            changed = true;
        }

        if (IsExpired(_slotRequestSinceMs, now))
        {
            ClearSlotRequest();
            Error = "钓鱼配件请求超时，可重试。";
            changed = true;
        }

        if (!changed)
            return false;

        IsOpen = true;
        Touch();
        return true;
    }

    public void ResetForSession()
    {
        IsOpen = false;
        LocalObjectId = 0;
        FishingRodUniqueId = 0;
        HasFishingRod = false;
        HasReel = false;
        Fishing = false;
        ProgressPercent = 0;
        ChancePercent = 0;
        ServerProgressPercent = 0;
        ServerChancePercent = 0;
        FishingPoint = Point.Empty;
        FoundFish = false;
        AutoCastIntent = false;
        _castRequestSinceMs = -1;
        _castRequestCastOut = false;
        _autoCastRequestSinceMs = -1;
        ClearSlotRequest();
        Error = null;
        Revision++;
    }

    /// <summary>
    /// Clears transient fishing activity during death/map transitions while
    /// preserving the authoritative equipped rod/reel snapshot, its slots,
    /// and the last auto-cast intent. The protocol has no auto-cast ack, so a
    /// transition cannot safely invent a new toggle value; session reset or
    /// equipment invalidation still clears the intent.
    /// </summary>
    public void ResetActivityForTransition()
    {
        Fishing = false;
        ProgressPercent = 0;
        ChancePercent = 0;
        ServerProgressPercent = 0;
        ServerChancePercent = 0;
        FishingPoint = Point.Empty;
        FoundFish = false;
        _castRequestSinceMs = -1;
        _castRequestCastOut = false;
        _autoCastRequestSinceMs = -1;
        ClearSlotRequest();
        Error = null;
        IsOpen = true;
        Touch();
    }

    private void SetError(string message, bool clearFishing)
    {
        if (clearFishing)
            Fishing = false;

        Error = string.IsNullOrWhiteSpace(message) ? "钓鱼操作无效。" : message.Trim();
        IsOpen = true;
        Touch();
    }

    private void Touch() => Revision++;

    private void ClearSlotRequest()
    {
        _slotRequestSinceMs = -1;
        _slotRequest = -1;
        _slotRequestSourceUniqueId = 0;
    }

    private static int ClampPercent(int value) => Math.Max(0, Math.Min(100, value));

    private static long NormalizeClock(long nowMs) => nowMs < 0 ? 0 : nowMs;

    private static bool IsExpired(long startedAtMs, long nowMs)
    {
        return startedAtMs >= 0 && nowMs >= startedAtMs && nowMs - startedAtMs >= RequestTimeoutMs;
    }

    private static bool IsFishingFailure(string text)
    {
        string[] markers =
        {
            "鱼钩", "鱼饵", "鱼竿", "鱼脱钩", "鱼跑了", "钓鱼", "摇轮", "背包空间不足",
        };

        bool domain = false;
        for (int i = 0; i < markers.Length; i++)
        {
            if (text.IndexOf(markers[i], StringComparison.OrdinalIgnoreCase) >= 0)
            {
                domain = true;
                break;
            }
        }

        if (!domain)
            return false;

        return text.IndexOf("需要", StringComparison.OrdinalIgnoreCase) >= 0 ||
               text.IndexOf("没有", StringComparison.OrdinalIgnoreCase) >= 0 ||
               text.IndexOf("不足", StringComparison.OrdinalIgnoreCase) >= 0 ||
               text.IndexOf("失败", StringComparison.OrdinalIgnoreCase) >= 0 ||
               text.IndexOf("无法", StringComparison.OrdinalIgnoreCase) >= 0 ||
               text.IndexOf("不能", StringComparison.OrdinalIgnoreCase) >= 0 ||
               text.IndexOf("脱钩", StringComparison.OrdinalIgnoreCase) >= 0 ||
               text.IndexOf("跑了", StringComparison.OrdinalIgnoreCase) >= 0 ||
               text.IndexOf("空间", StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
