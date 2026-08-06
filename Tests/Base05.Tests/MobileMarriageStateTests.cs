using System;
using MonoShare.MirScenes;
using Xunit;

namespace Base05.Tests;

public sealed class MobileMarriageStateTests
{
    [Fact]
    public void Protocol_indexes_and_fields_match_marriage_contract()
    {
        Assert.Equal((short)ServerPacketIds.MarriageRequest, new ServerPackets.MarriageRequest().Index);
        Assert.Equal((short)ServerPacketIds.DivorceRequest, new ServerPackets.DivorceRequest().Index);
        Assert.Equal((short)ServerPacketIds.LoverUpdate, new ServerPackets.LoverUpdate().Index);
        Assert.Equal((short)ClientPacketIds.MarriageRequest, new ClientPackets.MarriageRequest().Index);
        Assert.Equal((short)ClientPacketIds.MarriageReply, new ClientPackets.MarriageReply().Index);
        Assert.Equal((short)ClientPacketIds.ChangeMarriage, new ClientPackets.ChangeMarriage().Index);
        Assert.Equal((short)ClientPacketIds.DivorceRequest, new ClientPackets.DivorceRequest().Index);
        Assert.Equal((short)ClientPacketIds.DivorceReply, new ClientPackets.DivorceReply().Index);

        DateTime date = new DateTime(2026, 8, 6, 12, 0, 0, DateTimeKind.Utc);
        var update = new ServerPackets.LoverUpdate
        {
            Name = "伴侣",
            Date = date,
            MapName = "盟重省",
            MarriedDays = 17,
        };
        Assert.Equal("伴侣", update.Name);
        Assert.Equal(date, update.Date);
        Assert.Equal("盟重省", update.MapName);
        Assert.Equal((short)17, update.MarriedDays);

        var proposal = new ServerPackets.MarriageRequest { Name = "求婚者" };
        var divorce = new ServerPackets.DivorceRequest { Name = "申请人" };
        Assert.Equal("求婚者", proposal.Name);
        Assert.Equal("申请人", divorce.Name);
    }

    [Fact]
    public void Incoming_marriage_request_is_one_shot_and_reply_clears_pending()
    {
        var state = new MobileMarriageState();

        Assert.True(state.ApplyMarriageRequest(new ServerPackets.MarriageRequest { Name = "  求婚者  " }));
        Assert.True(state.HasPendingMarriageRequest);
        Assert.Equal("求婚者", state.PendingMarriageRequestName);
        Assert.True(state.ApplyMarriageReply(accepted: true));
        Assert.False(state.HasPendingMarriageRequest);
        Assert.True(state.LastMarriageAccepted);
        Assert.False(state.ApplyMarriageReply(accepted: false));
    }

    [Fact]
    public void Mobile_prompt_copy_distinguishes_incoming_requests_from_outgoing_confirmation()
    {
        Assert.Equal(
            "求婚者 向你求婚，是否同意？",
            MobileMarriageState.GetPromptMessage(" 求婚者 ", MobileMarriageState.PromptKind.IncomingMarriageProposal));
        Assert.Equal(
            "申请人 请求与你离婚，是否同意？",
            MobileMarriageState.GetPromptMessage("申请人", MobileMarriageState.PromptKind.IncomingDivorceRequest));
        Assert.Equal(
            "确认向伴侣提出离婚？",
            MobileMarriageState.GetPromptMessage("对方", MobileMarriageState.PromptKind.OutgoingDivorceConfirmation));
        Assert.Equal("确认离婚", MobileMarriageState.GetPromptTitle(MobileMarriageState.PromptKind.OutgoingDivorceConfirmation));
    }

    [Fact]
    public void Change_marriage_label_and_result_follow_pre_and_post_marriage_server_semantics()
    {
        var state = new MobileMarriageState();

        Assert.False(state.HasRelationship);
        Assert.Equal(MobileMarriageState.MarriagePermissionActionLabel, state.ChangeMarriageActionLabel);
        Assert.Equal(MobileMarriageState.MarriagePermissionChangedMessage, state.ChangeMarriageResultMessage);

        Assert.True(state.ApplyLoverUpdate(new ServerPackets.LoverUpdate
        {
            Name = "伴侣", Date = DateTime.UtcNow, MapName = "盟重省", MarriedDays = 1,
        }));
        Assert.Equal(MobileMarriageState.LoverRecallActionLabel, state.ChangeMarriageActionLabel);
        Assert.Equal(MobileMarriageState.LoverRecallChangedMessage, state.ChangeMarriageResultMessage);

        Assert.True(state.ApplyLoverUpdate(new ServerPackets.LoverUpdate
        {
            Name = "", Date = DateTime.UtcNow, MapName = "", MarriedDays = 0,
        }));
        Assert.Equal(MobileMarriageState.MarriagePermissionActionLabel, state.ChangeMarriageActionLabel);
    }

    [Fact]
    public void Divorce_confirmation_sends_once_and_reject_keeps_snapshot()
    {
        var state = new MobileMarriageState();
        Assert.True(state.ApplyLoverUpdate(new ServerPackets.LoverUpdate
        {
            Name = "伴侣", Date = DateTime.UtcNow, MapName = "盟重省", MarriedDays = 9,
        }));

        Assert.True(state.BeginDivorceConfirmation());
        Assert.True(state.DivorceConfirmationPending);
        Assert.False(state.BeginDivorceConfirmation());
        Assert.True(state.RejectDivorceRequest());
        Assert.False(state.DivorceConfirmationPending);
        Assert.True(state.HasRelationship);

        Assert.True(state.BeginDivorceConfirmation());
        Assert.True(state.ConfirmDivorceRequest());
        Assert.True(state.HasPendingOutgoingDivorceRequest);
        Assert.False(state.ConfirmDivorceRequest());
        Assert.True(state.HasRelationship);
    }

    [Fact]
    public void Server_divorce_failure_chat_releases_guard_and_allows_retry()
    {
        var state = new MobileMarriageState();
        Assert.True(state.ApplyLoverUpdate(new ServerPackets.LoverUpdate
        {
            Name = "伴侣", Date = DateTime.UtcNow, MapName = "盟重省", MarriedDays = 9,
        }));

        Assert.True(state.BeginDivorceConfirmation());
        Assert.True(state.ConfirmDivorceRequest());
        Assert.True(state.HasPendingOutgoingDivorceRequest);
        Assert.False(state.ConfirmDivorceRequest());

        // DivorceRequest has no failure packet.  The server reports a
        // face-to-face/range error via S.Chat(System), so the state must
        // release only this known terminal result and keep the relationship.
        Assert.True(state.ApplyServerSystemMessage("必须面对面才能完成离婚"));
        Assert.False(state.HasPendingOutgoingDivorceRequest);
        Assert.True(state.HasRelationship);
        Assert.Contains("面对面", state.Error);

        Assert.True(state.BeginDivorceConfirmation());
        Assert.True(state.ConfirmDivorceRequest());
        Assert.True(state.HasPendingOutgoingDivorceRequest);
    }

    [Fact]
    public void Marriage_failure_chats_release_guard_for_range_error_and_rejection()
    {
        var state = new MobileMarriageState();

        Assert.True(state.BeginMarriageRequest(10_000));
        Assert.True(state.ApplyServerSystemMessage("需要面对面才能完成结婚"));
        Assert.False(state.HasPendingOutgoingMarriageRequest);
        Assert.Contains("面对面", state.Error);

        Assert.True(state.BeginMarriageRequest(11_000));
        Assert.True(state.ApplyServerSystemMessage("求婚者拒绝求婚"));
        Assert.False(state.HasPendingOutgoingMarriageRequest);
        Assert.Contains("拒绝求婚", state.Error);
        Assert.True(state.BeginMarriageRequest(12_000));
    }

    [Fact]
    public void Unrelated_system_chat_does_not_release_success_guard()
    {
        var state = new MobileMarriageState();
        state.ApplyLoverUpdate(new ServerPackets.LoverUpdate
        {
            Name = "伴侣", Date = DateTime.UtcNow, MapName = "盟重省", MarriedDays = 1,
        });
        state.BeginDivorceConfirmation();
        state.ConfirmDivorceRequest();

        Assert.False(state.ApplyServerSystemMessage("系统维护将在五分钟后开始"));
        Assert.True(state.HasPendingOutgoingDivorceRequest);
    }

    [Fact]
    public void Silent_divorce_request_is_guarded_until_timeout_then_can_retry()
    {
        var state = new MobileMarriageState();
        state.ApplyLoverUpdate(new ServerPackets.LoverUpdate
        {
            Name = "伴侣", Date = DateTime.UtcNow, MapName = "盟重省", MarriedDays = 2,
        });

        const long sentAt = 10_000;
        Assert.True(state.BeginDivorceConfirmation());
        Assert.True(state.ConfirmDivorceRequest(sentAt));
        Assert.True(state.HasPendingOutgoingDivorceRequest);
        Assert.False(state.BeginDivorceConfirmation());
        Assert.False(state.Tick(sentAt + MobileMarriageState.OutgoingRequestTimeoutMs - 1));
        Assert.True(state.HasPendingOutgoingDivorceRequest);

        Assert.True(state.Tick(sentAt + MobileMarriageState.OutgoingRequestTimeoutMs));
        Assert.False(state.HasPendingOutgoingDivorceRequest);
        Assert.Contains("超时", state.Error);
        Assert.True(state.BeginDivorceConfirmation());
        Assert.True(state.ConfirmDivorceRequest(sentAt + MobileMarriageState.OutgoingRequestTimeoutMs + 1));
    }

    [Fact]
    public void Silent_marriage_request_is_guarded_until_timeout_then_can_retry()
    {
        var state = new MobileMarriageState();
        const long sentAt = 20_000;

        Assert.True(state.BeginMarriageRequest(sentAt));
        Assert.False(state.BeginMarriageRequest(sentAt + 1));
        Assert.True(state.HasPendingOutgoingMarriageRequest);
        Assert.False(state.Tick(sentAt + MobileMarriageState.OutgoingRequestTimeoutMs - 1));
        Assert.True(state.HasPendingOutgoingMarriageRequest);

        Assert.True(state.Tick(sentAt + MobileMarriageState.OutgoingRequestTimeoutMs));
        Assert.False(state.HasPendingOutgoingMarriageRequest);
        Assert.Contains("超时", state.Error);
        Assert.True(state.BeginMarriageRequest(sentAt + MobileMarriageState.OutgoingRequestTimeoutMs + 1));
    }

    [Fact]
    public void Success_chat_keeps_guard_until_authoritative_relationship_update()
    {
        var state = new MobileMarriageState();
        state.ApplyLoverUpdate(new ServerPackets.LoverUpdate
        {
            Name = "伴侣", Date = DateTime.UtcNow, MapName = "盟重省", MarriedDays = 4,
        });
        state.BeginDivorceConfirmation();
        state.ConfirmDivorceRequest(30_000);

        Assert.False(state.ApplyServerSystemMessage("你已离婚了"));
        Assert.True(state.HasPendingOutgoingDivorceRequest);
        Assert.True(state.ApplyLoverUpdate(new ServerPackets.LoverUpdate
        {
            Name = "", Date = DateTime.UtcNow, MapName = "", MarriedDays = 0,
        }));
        Assert.False(state.HasPendingOutgoingDivorceRequest);
    }

    [Fact]
    public void Lover_update_tracks_online_offline_and_empty_update_removes_relationship()
    {
        var state = new MobileMarriageState();
        DateTime date = DateTime.UtcNow;
        Assert.True(state.ApplyLoverUpdate(new ServerPackets.LoverUpdate
        {
            Name = "伴侣", Date = date, MapName = "盟重省", MarriedDays = 12,
        }));
        Assert.True(state.HasRelationship);
        Assert.True(state.PartnerOnline);
        Assert.Equal("盟重省", state.PartnerMapName);
        Assert.Equal((short)12, state.MarriedDays);

        Assert.True(state.ApplyLoverUpdate(new ServerPackets.LoverUpdate
        {
            Name = "伴侣", Date = date, MapName = "", MarriedDays = 12,
        }));
        Assert.True(state.HasRelationship);
        Assert.False(state.PartnerOnline);
        Assert.Equal(string.Empty, state.PartnerMapName);

        DateTime divorced = date.AddDays(1);
        Assert.True(state.ApplyLoverUpdate(new ServerPackets.LoverUpdate
        {
            Name = "", Date = divorced, MapName = "", MarriedDays = 0,
        }));
        Assert.False(state.HasRelationship);
        Assert.Equal(divorced, state.LastRelationshipDate);
        Assert.False(state.HasPendingOutgoingDivorceRequest);
    }

    [Fact]
    public void Invalid_update_preserves_last_good_snapshot_and_reset_clears_session()
    {
        var state = new MobileMarriageState();
        DateTime date = DateTime.UtcNow;
        state.ApplyLoverUpdate(new ServerPackets.LoverUpdate
        {
            Name = "伴侣", Date = date, MapName = "盟重省", MarriedDays = 3,
        });
        Assert.False(state.ApplyLoverUpdate(null));
        Assert.Equal("伴侣", state.PartnerName);
        Assert.Contains("为空", state.Error);
        Assert.False(state.ApplyLoverUpdate(new ServerPackets.LoverUpdate
        {
            Name = "伴侣", Date = date, MapName = "盟重省", MarriedDays = -1,
        }));
        Assert.Equal("伴侣", state.PartnerName);
        Assert.Contains("无效", state.Error);

        state.ApplyMarriageRequest(new ServerPackets.MarriageRequest { Name = "求婚者" });
        Assert.True(state.HasPendingMarriageRequest);
        state.ResetForSession();
        Assert.False(state.IsOpen);
        Assert.False(state.HasRelationship);
        Assert.False(state.HasPendingMarriageRequest);
        Assert.False(state.HasPendingOutgoingDivorceRequest);
        Assert.Null(state.Error);
    }
}
