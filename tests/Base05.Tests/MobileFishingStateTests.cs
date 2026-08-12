using System.Drawing;
using MonoShare.MirScenes;
using Xunit;

namespace Base05.Tests;

public sealed class MobileFishingStateTests
{
    [Fact]
    public void Fishing_usage_probe_smoke_projects_cast_request_and_server_update_to_ui_values()
    {
        var state = new MobileFishingState();
        Assert.True(state.SetEquipmentSnapshot(42, hasFishingRod: true, hasReel: true, fishingRodUniqueId: 1001));

        var request = new ClientPackets.FishingCast { CastOut = true };
        Assert.Equal((short)ClientPacketIds.FishingCast, request.Index);
        Assert.True(state.BeginCastRequest(request.CastOut, nowMs: 10_000));
        Assert.True(state.CastRequestPending);

        var update = new ServerPackets.FishingUpdate
        {
            ObjectID = 42,
            Fishing = true,
            ProgressPercent = 65,
            ChancePercent = 37,
            FishingPoint = new Point(11, 12),
            FoundFish = true,
        };
        Assert.Equal((short)ServerPacketIds.FishingUpdate, update.Index);
        Assert.True(state.ApplyFishingUpdate(update, localObjectId: 42));

        // FairyGuiHost.MobileFishing 刷新钓鱼窗口时读取这些公开投影。
        Assert.True(state.IsOpen);
        Assert.True(state.HasFishingRod);
        Assert.True(state.HasReel);
        Assert.True(state.Fishing);
        Assert.Equal(65, state.ProgressPercent);
        Assert.Equal(37, state.ChancePercent);
        Assert.Equal(new Point(11, 12), state.FishingPoint);
        Assert.True(state.FoundFish);
        Assert.False(state.CastRequestPending);
        Assert.Null(state.Error);
    }

    [Fact]
    public void Protocol_contract_exposes_fishing_packets_and_five_slots()
    {
        Assert.Equal((short)ServerPacketIds.FishingUpdate, new ServerPackets.FishingUpdate().Index);
        Assert.Equal((short)ClientPacketIds.FishingCast, new ClientPackets.FishingCast { CastOut = true }.Index);
        Assert.Equal((short)ClientPacketIds.FishingChangeAutocast,
            new ClientPackets.FishingChangeAutocast { AutoCast = true }.Index);
        Assert.Equal(5, (int)FishingSlot.Reel + 1);
    }

    [Fact]
    public void Local_fishing_update_is_authoritative_and_world_updates_are_ignored()
    {
        var state = new MobileFishingState();
        Assert.True(state.SetEquipmentSnapshot(42, hasFishingRod: true, hasReel: true));
        Assert.True(state.BeginCastRequest(castOut: true, nowMs: 1_000));

        Assert.False(state.ApplyFishingUpdate(new ServerPackets.FishingUpdate
        {
            ObjectID = 99,
            Fishing = true,
            ProgressPercent = 15,
            ChancePercent = 30,
        }, localObjectId: 42));
        Assert.True(state.CastRequestPending);

        Assert.True(state.ApplyFishingUpdate(new ServerPackets.FishingUpdate
        {
            ObjectID = 42,
            Fishing = true,
            ProgressPercent = 65,
            ChancePercent = 37,
            FishingPoint = new Point(11, 12),
            FoundFish = true,
        }, localObjectId: 42));
        Assert.False(state.CastRequestPending);
        Assert.True(state.Fishing);
        Assert.Equal(65, state.ProgressPercent);
        Assert.Equal(37, state.ChancePercent);
        Assert.True(state.FoundFish);
        Assert.Equal(new Point(11, 12), state.FishingPoint);
    }

    [Fact]
    public void Fishing_failure_chat_and_timeout_release_one_shot_cast_guard()
    {
        var state = new MobileFishingState();
        state.SetEquipmentSnapshot(42, hasFishingRod: true, hasReel: false);
        Assert.True(state.BeginCastRequest(castOut: true, nowMs: 2_000));
        Assert.False(state.BeginCastRequest(castOut: true, nowMs: 2_001));
        Assert.True(state.ApplyServerSystemMessage("需要鱼钩。"));
        Assert.False(state.CastRequestPending);
        Assert.Contains("鱼钩", state.Error);

        Assert.True(state.BeginCastRequest(castOut: true, nowMs: 3_000));
        Assert.True(state.Tick(3_000 + MobileFishingState.RequestTimeoutMs));
        Assert.False(state.CastRequestPending);
        Assert.Contains("超时", state.Error);
    }

    [Fact]
    public void Auto_cast_keeps_last_intent_until_explicit_toggle_or_transition()
    {
        var state = new MobileFishingState();
        state.SetEquipmentSnapshot(42, hasFishingRod: true, hasReel: true);
        Assert.True(state.BeginAutoCastRequest(enabled: true, nowMs: 4_000));
        Assert.True(state.AutoCastIntent);
        Assert.True(state.AutoCastRequestPending);

        // A FishingUpdate has no AutoCast field and therefore cannot be used
        // as a confirmation of the pending toggle.
        Assert.True(state.ApplyFishingUpdate(new ServerPackets.FishingUpdate
        {
            ObjectID = 42,
            Fishing = true,
            ProgressPercent = 10,
            ChancePercent = 10,
        }, localObjectId: 42));
        Assert.True(state.AutoCastIntent);
        Assert.True(state.AutoCastRequestPending);

        Assert.True(state.ApplyFishingUpdate(new ServerPackets.FishingUpdate
        {
            ObjectID = 42,
            Fishing = false,
            ProgressPercent = 0,
            ChancePercent = 0,
        }, localObjectId: 42));
        Assert.True(state.AutoCastIntent);
        Assert.False(state.AutoCastRequestPending);

        Assert.True(state.SetEquipmentSnapshot(42, hasFishingRod: true, hasReel: true));
        Assert.True(state.BeginAutoCastRequest(enabled: true, nowMs: 5_000));
        Assert.True(state.Tick(5_000 + MobileFishingState.RequestTimeoutMs));
        Assert.True(state.AutoCastIntent);

        // The next tap is the explicit local toggle and sends false.
        Assert.True(state.BeginAutoCastRequest(enabled: false, nowMs: 6_000));
        Assert.False(state.AutoCastIntent);
        Assert.True(state.ApplyFishingUpdate(new ServerPackets.FishingUpdate
        {
            ObjectID = 42,
            Fishing = false,
        }, localObjectId: 42));
        Assert.False(state.AutoCastIntent);

        state.SetEquipmentSnapshot(42, hasFishingRod: true, hasReel: true);
        Assert.True(state.BeginAutoCastRequest(enabled: true, nowMs: 7_000));
        Assert.True(state.SetEquipmentSnapshot(42, hasFishingRod: true, hasReel: false));
        Assert.True(state.AutoCastIntent);
        Assert.False(state.AutoCastRequestPending);
        Assert.True(state.DisableAutoCastIntent());
        Assert.False(state.AutoCastIntent);
        Assert.False(state.DisableAutoCastIntent());
    }

    [Fact]
    public void Switching_the_equipped_rod_clears_transient_state_but_requires_explicit_auto_disable()
    {
        var state = new MobileFishingState();
        state.SetEquipmentSnapshot(42, hasFishingRod: true, hasReel: true, fishingRodUniqueId: 1001);
        state.BeginAutoCastRequest(enabled: true, nowMs: 1_000);
        state.ApplyFishingUpdate(new ServerPackets.FishingUpdate
        {
            ObjectID = 42,
            Fishing = true,
            ProgressPercent = 30,
            ChancePercent = 20,
        }, localObjectId: 42);

        Assert.True(state.NeedsAutoCastDisableForEquipment(true, false, 2002));
        Assert.True(state.SetEquipmentSnapshot(42, hasFishingRod: true, hasReel: false, fishingRodUniqueId: 2002));
        Assert.Equal((ulong)2002, state.FishingRodUniqueId);
        Assert.False(state.Fishing);
        Assert.True(state.AutoCastIntent);
        Assert.False(state.AutoCastRequestPending);
        Assert.Equal(0, state.ProgressPercent);
        Assert.True(state.DisableAutoCastIntent());
        Assert.False(state.NeedsAutoCastDisableForEquipment(true, false, 2002));
        Assert.True(state.SetEquipmentSnapshot(42, hasFishingRod: true, hasReel: true, fishingRodUniqueId: 2002));
        Assert.True(state.BeginAutoCastRequest(enabled: true, nowMs: 2_000));
        Assert.True(state.AutoCastIntent);
    }

    [Fact]
    public void Authoritative_no_rod_snapshot_preserves_intent_until_new_rod_can_be_disabled()
    {
        var state = new MobileFishingState();
        state.SetEquipmentSnapshot(42, hasFishingRod: true, hasReel: true, fishingRodUniqueId: 1001);
        Assert.True(state.BeginAutoCastRequest(enabled: true, nowMs: 1_000));

        // UserSlotsRefresh may temporarily contain no weapon. The server
        // ignores a FishingChangeAutocast packet in that state, so no local
        // false transition is invented here.
        Assert.False(state.NeedsAutoCastDisableForEquipment(false, false, 0));
        Assert.True(state.SetEquipmentSnapshot(42, hasFishingRod: false, hasReel: false));
        Assert.True(state.AutoCastIntent);
        Assert.False(state.ShouldDisableAutoCastBeforeEquipmentChange(currentWeaponIsFishingRod: false));

        // Re-equipping a rod with a reel makes the server able to accept the
        // explicit false request. Sync applies the snapshot first, then sends
        // this one-shot disable.
        Assert.True(state.NeedsAutoCastDisableForEquipment(true, true, 2002));
        Assert.True(state.SetEquipmentSnapshot(42, hasFishingRod: true, hasReel: true, fishingRodUniqueId: 2002));
        Assert.True(state.AutoCastIntent);
        Assert.True(state.DisableAutoCastIntent());
        Assert.False(state.AutoCastIntent);
        Assert.False(state.DisableAutoCastIntent());
    }

    [Fact]
    public void Invalid_update_and_session_reset_do_not_leave_fishing_state_behind()
    {
        var state = new MobileFishingState();
        state.SetEquipmentSnapshot(42, hasFishingRod: true, hasReel: false);
        Assert.True(state.ApplyFishingUpdate(new ServerPackets.FishingUpdate
        {
            ObjectID = 42,
            Fishing = true,
            ProgressPercent = 101,
            ChancePercent = 130,
        }, localObjectId: 42));
        Assert.True(state.Fishing);
        Assert.Equal(101, state.ServerProgressPercent);
        Assert.Equal(130, state.ServerChancePercent);
        Assert.Equal(100, state.ProgressPercent);
        Assert.Equal(100, state.ChancePercent);

        // The server can emit the >100 progress tick before the terminal
        // Fishing=false packet. Both packets must be accepted. FishingUpdate
        // has no auto-cast acknowledgement, so terminal activity does not
        // rewrite the last local intent.
        state.SetEquipmentSnapshot(42, hasFishingRod: true, hasReel: true, fishingRodUniqueId: 1001);
        Assert.True(state.BeginAutoCastRequest(enabled: true, nowMs: 1_000));
        Assert.True(state.ApplyFishingUpdate(new ServerPackets.FishingUpdate
        {
            ObjectID = 42,
            Fishing = true,
            ProgressPercent = 135,
            ChancePercent = 140,
        }, localObjectId: 42));
        Assert.True(state.AutoCastRequestPending);
        Assert.True(state.ApplyFishingUpdate(new ServerPackets.FishingUpdate
        {
            ObjectID = 42,
            Fishing = false,
            ProgressPercent = 120,
            ChancePercent = 140,
        }, localObjectId: 42));
        Assert.False(state.Fishing);
        Assert.True(state.AutoCastIntent);
        Assert.False(state.AutoCastRequestPending);

        Assert.False(state.ApplyFishingUpdate(new ServerPackets.FishingUpdate
        {
            ObjectID = 42,
            Fishing = true,
            ProgressPercent = -1,
            ChancePercent = 30,
        }, localObjectId: 42));
        Assert.Contains("无效", state.Error);

        state.ResetForSession();
        Assert.False(state.IsOpen);
        Assert.Equal(0, state.LocalObjectId);
        Assert.False(state.HasFishingRod);
        Assert.False(state.Fishing);
        Assert.False(state.AutoCastIntent);
        Assert.Null(state.Error);
    }

    [Fact]
    public void Slot_request_gate_rejects_duplicates_and_expires()
    {
        var state = new MobileFishingState();
        state.SetEquipmentSnapshot(42, hasFishingRod: true, hasReel: false, fishingRodUniqueId: 9001);

        Assert.True(state.BeginSlotRequest((int)FishingSlot.Hook, 7001, 10_000));
        Assert.True(state.SlotRequestPending);
        Assert.Equal((int)FishingSlot.Hook, state.PendingFishingSlot);
        Assert.False(state.BeginSlotRequest((int)FishingSlot.Hook, 7002, 10_001));
        Assert.Contains("处理中", state.Error);

        Assert.True(state.Tick(10_000 + MobileFishingState.SlotRequestTimeoutMs));
        Assert.False(state.SlotRequestPending);
        Assert.Contains("配件请求", state.Error);

        Assert.True(state.BeginSlotRequest((int)FishingSlot.Bait, 7003, 20_000));
        state.CompleteSlotRequest();
        Assert.False(state.SlotRequestPending);
    }

    [Fact]
    public void Fishing_slot_success_or_failure_response_releases_picker_gate()
    {
        var state = new MobileFishingState();
        state.SetEquipmentSnapshot(42, hasFishingRod: true, hasReel: false, fishingRodUniqueId: 9001);

        Assert.True(state.BeginSlotRequest((int)FishingSlot.Bait, 7001, 30_000));
        // A successful MergeItem/EquipSlotItem handler completes the same gate
        // after applying its authoritative array mutation.
        state.CompleteSlotRequest();
        Assert.False(state.SlotRequestPending);

        Assert.True(state.BeginSlotRequest((int)FishingSlot.Bait, 7002, 31_000));
        // A failed response also clears the gate so the picker is immediately
        // usable again instead of remaining hidden/disabled forever.
        state.CompleteSlotRequest();
        Assert.False(state.SlotRequestPending);
    }

    [Fact]
    public void Transition_reset_preserves_equipment_but_clears_activity_and_slot_request()
    {
        var state = new MobileFishingState();
        state.SetEquipmentSnapshot(42, hasFishingRod: true, hasReel: true, fishingRodUniqueId: 9001);
        state.BeginAutoCastRequest(enabled: true, nowMs: 1_000);
        state.BeginSlotRequest((int)FishingSlot.Reel, 7001, 1_001);
        state.ApplyFishingUpdate(new ServerPackets.FishingUpdate
        {
            ObjectID = 42,
            Fishing = true,
            ProgressPercent = 20,
            ChancePercent = 30,
        }, localObjectId: 42);

        state.ResetActivityForTransition();

        Assert.True(state.HasFishingRod);
        Assert.True(state.HasReel);
        Assert.Equal((ulong)9001, state.FishingRodUniqueId);
        Assert.False(state.Fishing);
        Assert.True(state.AutoCastIntent);
        Assert.False(state.AutoCastRequestPending);
        Assert.False(state.SlotRequestPending);
        Assert.Equal(0, state.ProgressPercent);

        // The next UI tap explicitly toggles the preserved local intent off.
        Assert.True(state.BeginAutoCastRequest(enabled: false, nowMs: 2_000));
        Assert.False(state.AutoCastIntent);
    }

    [Fact]
    public void Cast_out_eligibility_matches_map_action_mount_and_cooldown_guards()
    {
        Assert.False(MobileFishingState.CanRequestCastOutAt(
            nowMs: 2_000, fishingTime: 0, dead: true, ridingMount: false,
            currentAction: MirAction.Standing, mapAllowsFishing: true));
        Assert.False(MobileFishingState.CanRequestCastOutAt(
            nowMs: 2_000, fishingTime: 0, dead: false, ridingMount: true,
            currentAction: MirAction.Standing, mapAllowsFishing: true));
        Assert.False(MobileFishingState.CanRequestCastOutAt(
            nowMs: 2_000, fishingTime: 0, dead: false, ridingMount: false,
            currentAction: MirAction.Walking, mapAllowsFishing: true));
        Assert.False(MobileFishingState.CanRequestCastOutAt(
            nowMs: 999, fishingTime: 0, dead: false, ridingMount: false,
            currentAction: MirAction.Standing, mapAllowsFishing: true));
        Assert.False(MobileFishingState.CanRequestCastOutAt(
            nowMs: 2_000, fishingTime: 0, dead: false, ridingMount: false,
            currentAction: MirAction.Standing, mapAllowsFishing: false));
        Assert.True(MobileFishingState.CanRequestCastOutAt(
            nowMs: 1_000, fishingTime: 0, dead: false, ridingMount: false,
            currentAction: MirAction.Standing, mapAllowsFishing: true));
    }
}
