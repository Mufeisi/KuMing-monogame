using MonoShare.MirScenes;
using Xunit;

namespace Base05.Tests;

public sealed class MobileMentorStateTests
{
    [Fact]
    public void Protocol_indexes_and_field_contract_match_server_packets()
    {
        Assert.Equal((short)ServerPacketIds.MentorRequest, new ServerPackets.MentorRequest().Index);
        Assert.Equal((short)ServerPacketIds.MentorUpdate, new ServerPackets.MentorUpdate().Index);
        Assert.Equal((short)ClientPacketIds.AddMentor, new ClientPackets.AddMentor().Index);
        Assert.Equal((short)ClientPacketIds.MentorReply, new ClientPackets.MentorReply().Index);
        Assert.Equal((short)ClientPacketIds.AllowMentor, new ClientPackets.AllowMentor().Index);
        Assert.Equal((short)ClientPacketIds.CancelMentor, new ClientPackets.CancelMentor().Index);

        var request = new ServerPackets.MentorRequest { Name = "师傅", Level = 42 };
        Assert.Equal("师傅", request.Name);
        Assert.Equal((ushort)42, request.Level);

        var update = new ServerPackets.MentorUpdate { Name = "徒弟", Level = 12, Online = true, MenteeEXP = 9876 };
        Assert.Equal("徒弟", update.Name);
        Assert.Equal((ushort)12, update.Level);
        Assert.True(update.Online);
        Assert.Equal(9876, update.MenteeEXP);
    }

    [Fact]
    public void Incoming_request_can_be_accepted_or_rejected_without_client_side_authority()
    {
        var state = new MobileMentorState();

        Assert.True(state.ApplyMentorRequest(new ServerPackets.MentorRequest { Name = "徒弟", Level = 12 }));
        Assert.True(state.HasPendingRequest);
        Assert.Equal("徒弟", state.PendingRequestName);
        Assert.True(state.ApplyMentorRequestReply(accepted: true));
        Assert.False(state.HasPendingRequest);
        Assert.True(state.LastRequestAccepted);

        Assert.True(state.ApplyMentorRequest(new ServerPackets.MentorRequest { Name = "另一个徒弟", Level = 11 }));
        Assert.True(state.ApplyMentorRequestReply(accepted: false));
        Assert.False(state.HasPendingRequest);
        Assert.False(state.LastRequestAccepted);
    }

    [Fact]
    public void Outgoing_request_is_tracked_until_server_update()
    {
        var state = new MobileMentorState();

        Assert.True(state.BeginMentorRequest("  师傅  "));
        Assert.Equal("师傅", state.PendingOutgoingName);
        Assert.True(state.ApplyMentorUpdate(new ServerPackets.MentorUpdate
        {
            Name = "师傅", Level = 50, Online = true, MenteeEXP = 0,
        }, localLevel: 20));
        Assert.False(state.HasPendingOutgoingRequest);
        Assert.True(state.IsMentee);
        Assert.Equal("师傅", state.PartnerName);
    }

    [Fact]
    public void Outgoing_request_can_be_retried_when_server_only_reports_failure_in_chat()
    {
        var state = new MobileMentorState();

        Assert.True(state.BeginMentorRequest("失败目标"));
        Assert.True(state.HasPendingOutgoingRequest);
        Assert.True(state.CanRequestMentor);

        // AddMentor has no failure packet; a chat-only failure must not lock
        // the next attempt behind the old optimistic name.
        Assert.True(state.BeginMentorRequest("重试目标"));
        Assert.Equal("重试目标", state.PendingOutgoingName);
        Assert.True(state.HasPendingOutgoingRequest);
        Assert.True(state.CanRequestMentor);
    }

    [Fact]
    public void Mentor_update_applies_online_and_exp_and_empty_update_removes_relationship()
    {
        var state = new MobileMentorState();
        state.SetLocalLevel(60);

        Assert.True(state.ApplyMentorUpdate(new ServerPackets.MentorUpdate
        {
            Name = "徒弟", Level = 20, Online = false, MenteeEXP = 123,
        }));
        Assert.True(state.IsMentor);
        Assert.False(state.PartnerOnline);
        Assert.Equal(123, state.MenteeEXP);

        Assert.True(state.ApplyMentorUpdate(new ServerPackets.MentorUpdate()));
        Assert.False(state.HasMentorship);
        Assert.Equal(MobileMentorState.Role.None, state.RelationshipRole);
        Assert.Equal(string.Empty, state.PartnerName);
        Assert.Null(state.Error);
    }

    [Fact]
    public void Cancel_confirmation_is_one_shot_and_reject_keeps_relationship()
    {
        var state = new MobileMentorState();
        state.SetLocalLevel(60);
        Assert.True(state.ApplyMentorUpdate(new ServerPackets.MentorUpdate
        {
            Name = "徒弟", Level = 20, Online = true, MenteeEXP = 10,
        }));

        Assert.True(state.BeginCancelConfirmation());
        Assert.True(state.CancelConfirmationPending);
        Assert.False(state.CanCancelMentorship);
        Assert.False(state.BeginCancelConfirmation());
        Assert.Contains("处理中", state.Error);
        Assert.True(state.RejectCancelMentorship());
        Assert.False(state.CancelConfirmationPending);
        Assert.True(state.HasMentorship);
        Assert.True(state.CanCancelMentorship);
        Assert.False(state.RejectCancelMentorship());

        Assert.True(state.BeginCancelConfirmation());
        Assert.True(state.ConfirmCancelMentorship());
        Assert.False(state.CancelConfirmationPending);
        // A repeated click cannot authorize another packet send.
        Assert.False(state.ConfirmCancelMentorship());
        Assert.True(state.HasMentorship);
    }

    [Fact]
    public void Mentor_button_state_exposes_only_role_specific_controls()
    {
        var state = new MobileMentorState();
        state.SetLocalLevel(60);
        Assert.True(state.ApplyMentorRequest(new ServerPackets.MentorRequest { Name = "待确认", Level = 20 }));
        Assert.True(state.CanRespondToPendingRequest);
        Assert.True(state.ApplyMentorRequestReply(accepted: true));
        Assert.False(state.CanRespondToPendingRequest);

        Assert.True(state.ApplyMentorUpdate(new ServerPackets.MentorUpdate
        {
            Name = "徒弟", Level = 20, Online = true, MenteeEXP = 77,
        }));
        Assert.True(state.ShouldShowMenteeExperience);

        state.SetLocalLevel(10);
        Assert.True(state.ApplyMentorUpdate(new ServerPackets.MentorUpdate
        {
            Name = "师傅", Level = 40, Online = true, MenteeEXP = 99,
        }));
        Assert.False(state.ShouldShowMenteeExperience);
    }

    [Fact]
    public void Null_or_invalid_update_preserves_last_good_snapshot_and_surfaces_error()
    {
        var state = new MobileMentorState();
        state.SetLocalLevel(40);
        state.ApplyMentorUpdate(new ServerPackets.MentorUpdate
        {
            Name = "师傅", Level = 50, Online = true, MenteeEXP = 9,
        });

        Assert.False(state.ApplyMentorUpdate(null));
        Assert.Equal("师傅", state.PartnerName);
        Assert.Equal((ushort)50, state.PartnerLevel);
        Assert.Contains("为空", state.Error);

        Assert.False(state.ApplyMentorUpdate(new ServerPackets.MentorUpdate { Name = "", Level = 50 }));
        Assert.Equal("师傅", state.PartnerName);
        Assert.Contains("无效", state.Error);
    }

    [Fact]
    public void Invalid_request_and_missing_reply_are_safe_failures()
    {
        var state = new MobileMentorState();

        Assert.False(state.ApplyMentorRequest(null));
        Assert.Contains("为空", state.Error);
        Assert.False(state.ApplyMentorRequest(new ServerPackets.MentorRequest { Name = "", Level = 0 }));
        Assert.False(state.ApplyMentorRequestReply(accepted: false));
        Assert.Contains("没有待处理", state.Error);
    }

    [Fact]
    public void Session_reset_discards_partner_requests_and_errors()
    {
        var state = new MobileMentorState();
        state.SetLocalLevel(50);
        state.ApplyMentorUpdate(new ServerPackets.MentorUpdate { Name = "徒弟", Level = 10, Online = true });
        state.BeginMentorRequest("别的角色");
        state.ApplyMentorRequest(new ServerPackets.MentorRequest { Name = "新请求", Level = 8 });
        state.ApplyMentorRequestReply(accepted: false);
        Assert.True(state.BeginCancelConfirmation());
        Assert.True(state.CancelConfirmationPending);

        state.ResetForSession();

        Assert.False(state.IsOpen);
        Assert.False(state.HasMentorship);
        Assert.False(state.HasPendingRequest);
        Assert.False(state.HasPendingOutgoingRequest);
        Assert.False(state.CancelConfirmationPending);
        Assert.Equal(MobileMentorState.Role.None, state.RelationshipRole);
        Assert.Null(state.Error);
    }
}
