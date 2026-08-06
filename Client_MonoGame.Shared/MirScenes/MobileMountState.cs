using System;

using S = ServerPackets;

namespace MonoShare.MirScenes;

/// <summary>
/// Main-thread state seam for the mobile mount flow.
///
/// The wire protocol has no dedicated ride request: the existing PC command
/// is a Chat packet containing <c>@ride</c>.  MountUpdate is authoritative and
/// is broadcast for every player, so callers must provide the local object id
/// before applying it.  World-player updates are deliberately ignored here;
/// their visual state is handled by PlayerObject/MountUpdate separately.
/// </summary>
public sealed class MobileMountState
{
    public const string RideCommand = "@ride";
    public const long OutgoingRequestTimeoutMs = 5000;

    private long _pendingSinceMs = -1;

    public bool IsOpen { get; private set; }
    public long LocalObjectId { get; private set; }
    public short MountType { get; private set; } = -1;
    public bool RidingMount { get; private set; }
    public bool HasMount => MountType >= 0;
    public bool HasPendingToggleRequest { get; private set; }
    public bool? LastRidingMount { get; private set; }
    public string Error { get; private set; }
    public int Revision { get; private set; }

    public bool CanRequestToggle => HasMount && !HasPendingToggleRequest;

    /// <summary>
    /// Pure transient eligibility check used by the HUD every frame.  The
    /// mount window content remains dirty-driven; only this short-lived
    /// button state is sampled while the window is visible.
    /// </summary>
    public static bool CanToggleAt(
        long nowMs, bool dead, short mountType, long mountTime,
        MirAction currentAction, bool pendingRequest)
    {
        return !dead && mountType >= 0 && !pendingRequest &&
               mountTime + 500 <= nowMs &&
               (currentAction == MirAction.Standing || currentAction == MirAction.MountStanding);
    }

    /// <summary>
    /// Identifies the repair notification for the currently equipped mount.
    /// Ordinary inventory/equipment repairs must not invalidate the mount
    /// window unless their unique id is the mount slot's unique id.
    /// </summary>
    public static bool IsEquippedMountRepair(ulong repairedUniqueId, ulong equippedMountUniqueId)
    {
        return repairedUniqueId != 0 && equippedMountUniqueId != 0 &&
               repairedUniqueId == equippedMountUniqueId;
    }

    /// <summary>Records a client-side guard failure without changing the authoritative snapshot.</summary>
    public void ReportLocalError(string message)
    {
        IsOpen = true;
        Error = string.IsNullOrWhiteSpace(message) ? "当前无法乘骑。" : message.Trim();
        Revision++;
    }

    /// <summary>Seeds the state from the UserInformation/equipment snapshot.</summary>
    public bool SetLocalSnapshot(long objectId, short mountType, bool ridingMount)
    {
        if (objectId <= 0 || mountType < -1 || (mountType < 0 && ridingMount))
        {
            SetFailure("坐骑状态无效。", clearSnapshot: false);
            return false;
        }

        LocalObjectId = objectId;
        MountType = mountType;
        RidingMount = ridingMount;
        LastRidingMount = ridingMount;
        HasPendingToggleRequest = false;
        _pendingSinceMs = -1;
        Error = null;
        Revision++;
        return true;
    }

    /// <summary>Starts one @ride request; repeated taps are rejected.</summary>
    public bool BeginToggleRide(long nowMs)
    {
        if (!HasMount)
        {
            SetFailure("当前没有可乘骑坐骑。", clearSnapshot: false);
            return false;
        }

        if (HasPendingToggleRequest)
        {
            SetFailure("乘骑请求已在处理中。", clearSnapshot: false);
            return false;
        }

        HasPendingToggleRequest = true;
        _pendingSinceMs = NormalizeClock(nowMs);
        LastRidingMount = null;
        Error = null;
        IsOpen = true;
        Revision++;
        return true;
    }

    /// <summary>
    /// Applies the authoritative MountUpdate for the local player only.
    /// Returns false for world-object updates or malformed packets.
    /// </summary>
    public bool ApplyMountUpdate(S.MountUpdate packet, long localObjectId)
    {
        if (packet == null)
        {
            SetFailure("坐骑状态更新为空。", clearSnapshot: false);
            return false;
        }

        long expectedId = localObjectId > 0 ? localObjectId : LocalObjectId;
        if (expectedId > 0 && packet.ObjectID != expectedId)
            return false;

        if (packet.ObjectID <= 0 || packet.MountType < -1 || (packet.MountType < 0 && packet.RidingMount))
        {
            SetFailure("坐骑状态更新无效。", clearSnapshot: false);
            return false;
        }

        LocalObjectId = packet.ObjectID;
        MountType = packet.MountType;
        RidingMount = packet.RidingMount;
        LastRidingMount = packet.RidingMount;
        HasPendingToggleRequest = false;
        _pendingSinceMs = -1;
        Error = null;
        IsOpen = true;
        Revision++;
        return true;
    }

    /// <summary>
    /// Mount failures are delivered through the existing system-chat path.
    /// Unknown messages do not release the one-shot guard; MountUpdate (or a
    /// bounded timeout) remains the only success/retry boundary.
    /// </summary>
    public bool ApplyServerSystemMessage(string message)
    {
        if (!HasPendingToggleRequest)
            return false;

        string text = (message ?? string.Empty).Trim();
        if (text.Length == 0 || !IsMountFailure(text))
            return false;

        HasPendingToggleRequest = false;
        _pendingSinceMs = -1;
        LastRidingMount = null;
        Error = text;
        IsOpen = true;
        Revision++;
        return true;
    }

    /// <summary>Releases a silent server failure after a bounded retry window.</summary>
    public bool Tick(long nowMs)
    {
        if (!HasPendingToggleRequest || !IsExpired(_pendingSinceMs, NormalizeClock(nowMs)))
            return false;

        HasPendingToggleRequest = false;
        _pendingSinceMs = -1;
        LastRidingMount = null;
        Error = "乘骑请求超时，可重试。";
        IsOpen = true;
        Revision++;
        return true;
    }

    public void ResetForSession()
    {
        IsOpen = false;
        LocalObjectId = 0;
        MountType = -1;
        RidingMount = false;
        HasPendingToggleRequest = false;
        _pendingSinceMs = -1;
        LastRidingMount = null;
        Error = null;
        Revision++;
    }

    private static bool IsMountFailure(string text)
    {
        string[] markers =
        {
            "没有坐骑",
            "没有可乘骑坐骑",
            "需装配马鞍",
            "装配马鞍",
            "禁止乘骑",
            "需装配缰绳",
            "装配缰绳",
            "不能使用坐骑",
            "无法使用坐骑",
            "坐骑忠诚度不足",
            "此类外形不能使用坐骑",
        };

        for (int i = 0; i < markers.Length; i++)
        {
            if (text.Contains(markers[i], StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private void SetFailure(string message, bool clearSnapshot)
    {
        if (clearSnapshot)
        {
            MountType = -1;
            RidingMount = false;
        }

        IsOpen = true;
        Error = string.IsNullOrWhiteSpace(message) ? "坐骑数据不可用。" : message;
        Revision++;
    }

    private static long NormalizeClock(long nowMs) => nowMs < 0 ? 0 : nowMs;

    private static bool IsExpired(long startedAtMs, long nowMs)
    {
        return startedAtMs >= 0 && nowMs >= startedAtMs && nowMs - startedAtMs >= OutgoingRequestTimeoutMs;
    }
}
