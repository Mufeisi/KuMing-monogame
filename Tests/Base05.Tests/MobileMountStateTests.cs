using MonoShare.MirScenes;
using Xunit;

namespace Base05.Tests;

public sealed class MobileMountStateTests
{
    [Fact]
    public void Mount_usage_probe_smoke_projects_ride_request_and_server_update_to_ui_values()
    {
        var state = new MobileMountState();
        Assert.True(state.SetLocalSnapshot(42, mountType: 7, ridingMount: false));

        var request = new ClientPackets.Chat { Message = MobileMountState.RideCommand };
        Assert.Equal((short)ClientPacketIds.Chat, request.Index);
        Assert.Equal("@ride", request.Message);
        Assert.True(state.BeginToggleRide(10_000));
        Assert.True(state.HasPendingToggleRequest);
        Assert.False(state.CanRequestToggle);

        var update = new ServerPackets.MountUpdate
        {
            ObjectID = 42,
            MountType = 7,
            RidingMount = true,
        };
        Assert.Equal((short)ServerPacketIds.MountUpdate, update.Index);
        Assert.True(state.ApplyMountUpdate(update, localObjectId: 42));

        // FairyGuiHost.MobileMount 刷新坐骑窗口时读取这些公开投影。
        Assert.True(state.IsOpen);
        Assert.True(state.HasMount);
        Assert.Equal((short)7, state.MountType);
        Assert.True(state.RidingMount);
        Assert.False(state.HasPendingToggleRequest);
        Assert.True(state.CanRequestToggle);
        Assert.True(state.LastRidingMount);
    }

    [Fact]
    public void Protocol_indexes_and_ride_command_fields_match_existing_mount_contract()
    {
        Assert.Equal((short)ServerPacketIds.MountUpdate, new ServerPackets.MountUpdate().Index);
        Assert.Equal((short)ServerPacketIds.EquipSlotItem, new ServerPackets.EquipSlotItem().Index);
        Assert.Equal((short)ServerPacketIds.RemoveSlotItem, new ServerPackets.RemoveSlotItem().Index);
        Assert.Equal((short)ClientPacketIds.Chat, new ClientPackets.Chat { Message = "@ride" }.Index);
        Assert.Equal((short)ClientPacketIds.EquipSlotItem, new ClientPackets.EquipSlotItem().Index);
        Assert.Equal((short)ClientPacketIds.RemoveSlotItem, new ClientPackets.RemoveSlotItem().Index);

        var update = new ServerPackets.MountUpdate { ObjectID = 42, MountType = 7, RidingMount = true };
        Assert.Equal(42, update.ObjectID);
        Assert.Equal((short)7, update.MountType);
        Assert.True(update.RidingMount);
    }

    [Fact]
    public void Ride_request_is_one_shot_until_authoritative_mount_update()
    {
        var state = new MobileMountState();
        Assert.True(state.SetLocalSnapshot(42, mountType: 7, ridingMount: false));

        Assert.True(state.BeginToggleRide(10_000));
        Assert.Equal("@ride", MobileMountState.RideCommand);
        Assert.True(state.HasPendingToggleRequest);
        Assert.False(state.BeginToggleRide(10_001));
        Assert.True(state.HasPendingToggleRequest);

        Assert.True(state.ApplyMountUpdate(new ServerPackets.MountUpdate
        {
            ObjectID = 42,
            MountType = 7,
            RidingMount = true,
        }, localObjectId: 42));
        Assert.False(state.HasPendingToggleRequest);
        Assert.True(state.RidingMount);
        Assert.True(state.LastRidingMount);
    }

    [Fact]
    public void World_mount_update_is_ignored_without_mutating_local_snapshot()
    {
        var state = new MobileMountState();
        Assert.True(state.SetLocalSnapshot(42, mountType: 7, ridingMount: false));

        Assert.False(state.ApplyMountUpdate(new ServerPackets.MountUpdate
        {
            ObjectID = 99,
            MountType = 3,
            RidingMount = true,
        }, localObjectId: 42));
        Assert.Equal(42, state.LocalObjectId);
        Assert.Equal((short)7, state.MountType);
        Assert.False(state.RidingMount);
    }

    [Fact]
    public void Invalid_mount_snapshot_and_update_are_rejected()
    {
        var state = new MobileMountState();
        Assert.False(state.SetLocalSnapshot(0, mountType: 7, ridingMount: false));
        Assert.Contains("无效", state.Error);

        Assert.True(state.SetLocalSnapshot(42, mountType: 7, ridingMount: false));
        Assert.False(state.ApplyMountUpdate(new ServerPackets.MountUpdate
        {
            ObjectID = 42,
            MountType = -1,
            RidingMount = true,
        }, localObjectId: 42));
        Assert.Equal((short)7, state.MountType);
        Assert.False(state.RidingMount);
        Assert.Contains("无效", state.Error);
    }

    [Fact]
    public void Mount_failure_chat_releases_guard_but_unrelated_chat_does_not()
    {
        var state = new MobileMountState();
        state.SetLocalSnapshot(42, mountType: 7, ridingMount: false);
        Assert.True(state.BeginToggleRide(10_000));

        Assert.False(state.ApplyServerSystemMessage("系统维护将在五分钟后开始"));
        Assert.True(state.HasPendingToggleRequest);
        Assert.True(state.ApplyServerSystemMessage("需装配马鞍才能乘骑"));
        Assert.False(state.HasPendingToggleRequest);
        Assert.Contains("马鞍", state.Error);
        Assert.True(state.BeginToggleRide(10_100));
    }

    [Fact]
    public void Silent_ride_request_times_out_and_can_retry()
    {
        var state = new MobileMountState();
        state.SetLocalSnapshot(42, mountType: 7, ridingMount: false);
        const long sentAt = 20_000;

        Assert.True(state.BeginToggleRide(sentAt));
        Assert.False(state.Tick(sentAt + MobileMountState.OutgoingRequestTimeoutMs - 1));
        Assert.True(state.HasPendingToggleRequest);
        Assert.True(state.Tick(sentAt + MobileMountState.OutgoingRequestTimeoutMs));
        Assert.False(state.HasPendingToggleRequest);
        Assert.Contains("超时", state.Error);
        Assert.True(state.BeginToggleRide(sentAt + MobileMountState.OutgoingRequestTimeoutMs + 1));
    }

    [Fact]
    public void No_mount_cannot_start_ride_and_session_reset_clears_state()
    {
        var state = new MobileMountState();
        Assert.True(state.SetLocalSnapshot(42, mountType: -1, ridingMount: false));
        Assert.False(state.BeginToggleRide(100));
        Assert.False(state.HasPendingToggleRequest);
        Assert.Contains("没有", state.Error);
        state.ReportLocalError("当前无法乘骑。");
        Assert.Contains("无法乘骑", state.Error);

        state.SetLocalSnapshot(42, mountType: 7, ridingMount: true);
        state.BeginToggleRide(200);
        state.ResetForSession();
        Assert.False(state.IsOpen);
        Assert.Equal(0, state.LocalObjectId);
        Assert.Equal((short)-1, state.MountType);
        Assert.False(state.RidingMount);
        Assert.False(state.HasPendingToggleRequest);
        Assert.Null(state.Error);
    }
}
