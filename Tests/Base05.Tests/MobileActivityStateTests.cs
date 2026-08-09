using System.Collections.Generic;
using MonoShare;
using MonoShare.MirScenes;
using Xunit;

namespace Base05.Tests;

public sealed class MobileActivityStateTests
{
    [Fact]
    public void Quest_context_keeps_activity_mode_across_rebind_but_close_returns_to_ordinary()
    {
        var context = new MobileQuestContextState();

        context.EnterActivity();
        context.ResetForRebind();
        Assert.True(context.IsActivityMode);
        Assert.Equal(0u, context.NpcObjectId);
        Assert.Equal("活动/赏金", context.NpcName);

        context.EnterNpc(77, "任务 NPC");
        Assert.False(context.IsActivityMode);
        Assert.Equal(77u, context.NpcObjectId);

        context.EnterActivity();
        context.ResetForClose();
        Assert.False(context.IsActivityMode);
        Assert.Equal(0u, context.NpcObjectId);
        Assert.Empty(context.NpcName);

        context.EnterActivity();
        Assert.True(context.IsActivityMode);
    }

    [Fact]
    public void Quest_binding_policy_rejects_category_buttons_and_duplicate_targets()
    {
        string[] acceptKeywords = { "accept", "接取", "接受" };
        Assert.False(MobileQuestBindingPolicy.HasKeyword("BtnMainTask", "主线任务", acceptKeywords));
        Assert.False(MobileQuestBindingPolicy.HasKeyword("BtnBranch", "支线", acceptKeywords));
        Assert.False(MobileQuestBindingPolicy.HasKeyword("BtnGuide", "引导", acceptKeywords));
        Assert.False(MobileQuestBindingPolicy.HasKeyword("BtnFate", "奇遇", acceptKeywords));
        Assert.False(MobileQuestBindingPolicy.HasKeyword("BtnClose", "关闭", acceptKeywords));

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Assert.True(MobileQuestBindingPolicy.TryClaim(used, "action_accept"));
        Assert.False(MobileQuestBindingPolicy.TryClaim(used, "action_accept"));
        Assert.True(MobileQuestBindingPolicy.NeedsFallback(0));
        Assert.True(MobileQuestBindingPolicy.NeedsFallback(4));
        Assert.False(MobileQuestBindingPolicy.NeedsFallback(5));

        var reliable = new[] { "Accept", "Finish", "Abandon", "Share" };
        Assert.Contains("Track", MobileQuestBindingPolicy.MissingOperationTargets(reliable));
        Assert.True(MobileQuestBindingPolicy.ShouldCreateFallback(activityMode: true, reliableOperationCount: 4));
        Assert.False(MobileQuestBindingPolicy.ShouldCreateFallback(activityMode: false, reliableOperationCount: 4));
        Assert.True(MobileQuestBindingPolicy.ShouldCreateRewardBar(activityMode: true, visibleCandidateCount: 2));
        Assert.False(MobileQuestBindingPolicy.ShouldCreateRewardBar(activityMode: false, visibleCandidateCount: 2));
        Assert.True(MobileQuestBindingPolicy.ShouldUseWindowFallback("Quest", activityMode: true));
        Assert.False(MobileQuestBindingPolicy.ShouldUseWindowFallback("Quest", activityMode: false));
        Assert.False(MobileQuestBindingPolicy.ShouldUseWindowFallback("Npc", activityMode: true));
        Assert.True(MobileQuestBindingPolicy.ShouldReplaceExistingQuestWindow("Quest", activityMode: true, existingIsActivityFallback: false));
        Assert.True(MobileQuestBindingPolicy.ShouldReplaceExistingQuestWindow("Quest", activityMode: false, existingIsActivityFallback: true));
        Assert.False(MobileQuestBindingPolicy.ShouldReplaceExistingQuestWindow("Quest", activityMode: true, existingIsActivityFallback: true));
        Assert.False(MobileQuestBindingPolicy.ShouldReplaceExistingQuestWindow("Npc", activityMode: true, existingIsActivityFallback: false));
    }

    [Fact]
    public void Activity_fallback_paging_exposes_items_after_the_first_six_rows()
    {
        Assert.Equal(1, MobileQuestBindingPolicy.PageCount(itemCount: 0, pageSize: 6));
        Assert.Equal(2, MobileQuestBindingPolicy.PageCount(itemCount: 7, pageSize: 6));
        Assert.Equal(6, MobileQuestBindingPolicy.PageItemIndex(page: 1, row: 0, itemCount: 7, pageSize: 6));
        Assert.Equal(-1, MobileQuestBindingPolicy.PageItemIndex(page: 1, row: 1, itemCount: 7, pageSize: 6));
        Assert.Equal(1, MobileQuestBindingPolicy.ClampPage(page: 9, itemCount: 7, pageSize: 6));
    }

    [Fact]
    public void Dynamic_quest_bars_allow_child_hit_without_parent_occlusion()
    {
        Assert.True(MobileQuestDynamicBarPolicy.ParentTouchable);
        Assert.False(MobileQuestDynamicBarPolicy.ParentOpaque);
        Assert.True(MobileQuestDynamicBarPolicy.AllowsChildHit(
            MobileQuestDynamicBarPolicy.ParentTouchable,
            MobileQuestDynamicBarPolicy.ParentOpaque));
        Assert.False(MobileQuestDynamicBarPolicy.AllowsChildHit(parentTouchable: false, parentOpaque: false));
        Assert.False(MobileQuestDynamicBarPolicy.AllowsChildHit(parentTouchable: true, parentOpaque: true));
    }

    [Fact]
    public void Activity_projection_only_contains_daily_and_repeatable_quests()
    {
        var daily = Info(1, QuestType.每日);
        var repeatable = Info(2, QuestType.重复);
        var story = Info(3, QuestType.主线);
        var state = new MobileActivityState();

        state.SyncSnapshot(
            new[] { daily, repeatable, story },
            new[] { Progress(daily, taken: true), Progress(story, taken: true) },
            new[] { 0 });

        Assert.Equal(2, state.Activities.Count);
        Assert.Contains(state.Activities, item => item.Id == 1);
        Assert.Contains(state.Activities, item => item.Id == 2);
        Assert.DoesNotContain(state.Activities, item => item.Id == 3);
    }

    [Fact]
    public void Activity_request_gate_rejects_duplicate_and_releases_on_authoritative_change()
    {
        var daily = Info(11, QuestType.每日);
        var state = new MobileActivityState();
        state.SyncSnapshot(new[] { daily }, null, null);

        Assert.True(state.BeginRequest(MobileActivityOperation.Accept, 11, 1_000));
        Assert.False(state.BeginRequest(MobileActivityOperation.Accept, 11, 1_001));
        Assert.Contains("处理中", state.Error);

        Assert.True(state.ApplyQuestChange(Progress(daily, taken: true), QuestState.Add));
        Assert.False(state.RequestPending);
        Assert.True(state.Activities[0].Taken);
    }

    [Fact]
    public void Activity_failure_and_timeout_keep_projection_retryable()
    {
        var repeatable = Info(21, QuestType.重复);
        var state = new MobileActivityState();
        state.SyncSnapshot(new[] { repeatable }, new[] { Progress(repeatable, taken: true, completed: true) }, null);

        Assert.True(state.BeginRequest(MobileActivityOperation.Finish, 21, 2_000));
        Assert.True(state.ApplyServerSystemMessage("背包已满，清理后再提交任务"));
        Assert.False(state.RequestPending);
        Assert.Contains("背包已满", state.Error);

        Assert.True(state.BeginRequest(MobileActivityOperation.Finish, 21, 3_000));
        Assert.True(state.Tick(3_000 + MobileActivityState.RequestTimeoutMs));
        Assert.True(state.RequestPending);
        Assert.True(state.ResultUncertain);
        Assert.Contains("交付结果未确认", state.Error);
        Assert.False(state.BeginRequest(MobileActivityOperation.Finish, 21, 9_000));
        Assert.NotNull(state.SelectedQuest);
        Assert.True(state.SelectedQuest.Completed);
    }

    [Fact]
    public void Finish_waits_for_matching_authoritative_state_before_unlocking()
    {
        var repeatable = Info(23, QuestType.重复);
        var unrelated = Info(24, QuestType.重复);
        var state = new MobileActivityState();
        state.SyncSnapshot(
            new[] { repeatable, unrelated },
            new[] { Progress(repeatable, taken: true, completed: true), Progress(unrelated, taken: true, completed: true) },
            null);

        Assert.True(state.BeginRequest(MobileActivityOperation.Finish, 23, 1_000));
        Assert.True(state.ApplyQuestChange(Progress(unrelated, taken: true, completed: true), QuestState.Update));
        Assert.True(state.RequestPending);
        Assert.True(state.ApplyQuestChange(Progress(repeatable, taken: true, completed: true), QuestState.Update));
        Assert.True(state.RequestPending);

        Assert.True(state.ApplyQuestChange(Progress(repeatable, taken: true, completed: true), QuestState.Remove));
        Assert.False(state.RequestPending);
    }

    [Fact]
    public void Finish_complete_snapshot_unlocks_only_matching_quest()
    {
        var daily = Info(25, QuestType.每日);
        var unrelated = Info(26, QuestType.每日);
        var state = new MobileActivityState();
        state.SyncSnapshot(
            new[] { daily, unrelated },
            new[] { Progress(daily, taken: true, completed: true), Progress(unrelated, taken: true, completed: true) },
            null);

        Assert.True(state.BeginRequest(MobileActivityOperation.Finish, 25, 1_000));
        Assert.True(state.ApplyCompleted(new[] { 26 }));
        Assert.True(state.RequestPending);
        Assert.True(state.ApplyCompleted(new[] { 25, 26 }));
        Assert.False(state.RequestPending);
    }

    [Fact]
    public void Finish_uncertain_snapshot_keeps_lock_and_prompt_until_authoritative_result()
    {
        var repeatable = Info(28, QuestType.重复);
        var state = new MobileActivityState();
        var active = Progress(repeatable, taken: true, completed: true);
        state.SyncSnapshot(new[] { repeatable }, new[] { active }, null);

        Assert.True(state.BeginRequest(MobileActivityOperation.Finish, 28, 1_000));
        Assert.True(state.Tick(1_000 + MobileActivityState.RequestTimeoutMs));
        Assert.True(state.ResultUncertain);

        state.SyncSnapshot(new[] { repeatable }, new[] { Progress(repeatable, taken: true, completed: true) }, null);
        Assert.True(state.RequestPending);
        Assert.True(state.ResultUncertain);
        Assert.Contains("交付结果未确认", state.Error);

        state.ResetForSession();
        Assert.False(state.RequestPending);
        Assert.False(state.ResultUncertain);
    }

    [Fact]
    public void Accept_abandon_and_share_timeouts_are_retryable_with_distinct_messages()
    {
        var repeatable = Info(27, QuestType.重复);
        var state = new MobileActivityState();
        state.SyncSnapshot(new[] { repeatable }, null, null);

        Assert.True(state.BeginRequest(MobileActivityOperation.Accept, 27, 500));
        Assert.True(state.Tick(500 + MobileActivityState.RequestTimeoutMs));
        Assert.False(state.RequestPending);

        state.SyncSnapshot(new[] { repeatable }, new[] { Progress(repeatable, taken: true) }, null);
        Assert.True(state.BeginRequest(MobileActivityOperation.Abandon, 27, 1_000));
        Assert.True(state.Tick(1_000 + MobileActivityState.RequestTimeoutMs));
        Assert.False(state.RequestPending);

        state.SyncSnapshot(new[] { repeatable }, new[] { Progress(repeatable, taken: true) }, null);
        Assert.True(state.BeginRequest(MobileActivityOperation.Share, 27, 2_000));
        Assert.True(state.Tick(2_000 + MobileActivityState.RequestTimeoutMs));
        Assert.False(state.RequestPending);
        Assert.False(state.ResultUncertain);
        Assert.Contains("分享结果未确认", state.Error);
        Assert.True(state.BeginRequest(MobileActivityOperation.Share, 27, 9_000));

        state.ResetForSession();
        Assert.False(state.RequestPending);
    }

    [Fact]
    public void Unrelated_system_chat_does_not_release_activity_request_gate()
    {
        var repeatable = Info(22, QuestType.重复);
        var state = new MobileActivityState();
        state.SyncSnapshot(new[] { repeatable }, new[] { Progress(repeatable, taken: true) }, null);

        Assert.True(state.BeginRequest(MobileActivityOperation.Abandon, 22, 4_000));
        Assert.False(state.ApplyServerSystemMessage("活动已开放，欢迎前往公告栏查看"));
        Assert.True(state.RequestPending);

        Assert.False(state.ApplyServerSystemMessage("无法放弃任务，请稍后重试"));
        Assert.True(state.RequestPending);
        state.ResetForSession();
    }

    [Fact]
    public void Activity_system_failures_match_the_pending_operation_only()
    {
        var repeatable = Info(29, QuestType.重复);
        var active = Progress(repeatable, taken: true, completed: true);
        var state = new MobileActivityState();
        state.SyncSnapshot(new[] { repeatable }, new[] { active }, null);

        Assert.True(state.BeginRequest(MobileActivityOperation.Finish, 29, 1_000));
        Assert.False(state.ApplyServerSystemMessage("背包已满"));
        Assert.True(state.RequestPending);
        Assert.False(state.ApplyServerSystemMessage("无法接受另一任务"));
        Assert.True(state.RequestPending);
        Assert.True(state.ApplyServerSystemMessage("背包已满，清理后再提交任务"));
        Assert.False(state.RequestPending);

        state.SyncSnapshot(new[] { repeatable }, new[] { Progress(repeatable, taken: true) }, null);
        Assert.True(state.BeginRequest(MobileActivityOperation.Share, 29, 2_000));
        Assert.False(state.ApplyServerSystemMessage("无法接受任务"));
        Assert.True(state.RequestPending);
        Assert.True(state.ApplyServerSystemMessage("任务无法共享"));
        Assert.False(state.RequestPending);

        state.SyncSnapshot(new[] { repeatable }, new[] { Progress(repeatable, taken: true) }, null);
        Assert.True(state.BeginRequest(MobileActivityOperation.Abandon, 29, 3_000));
        Assert.False(state.ApplyServerSystemMessage("无法接受任务"));
        Assert.True(state.RequestPending);
    }

    [Fact]
    public void Daily_completion_is_removed_until_server_daily_reset()
    {
        var daily = Info(31, QuestType.每日);
        var state = new MobileActivityState();
        state.SyncSnapshot(new[] { daily }, null, new[] { 31 });
        Assert.Empty(state.Activities);

        state.ApplyCompleted(new List<int>());
        Assert.Single(state.Activities);
        Assert.Equal(31, state.Activities[0].Id);
    }

    [Fact]
    public void Reward_selection_filters_candidates_but_submits_original_index()
    {
        var info = Info(41, QuestType.重复);
        info.RewardsSelectItem.Add(new QuestItemReward
        {
            Item = new ItemInfo { Name = "战士男奖励", RequiredClass = RequiredClass.Warrior, RequiredGender = RequiredGender.Male },
        });
        info.RewardsSelectItem.Add(new QuestItemReward
        {
            Item = new ItemInfo { Name = "法师女奖励", RequiredClass = RequiredClass.Wizard, RequiredGender = RequiredGender.Female },
        });
        info.RewardsSelectItem.Add(new QuestItemReward
        {
            Item = new ItemInfo { Name = "战士男奖励二", RequiredClass = RequiredClass.Warrior, RequiredGender = RequiredGender.Male },
        });
        var progress = Progress(info, taken: true, completed: true);
        var selection = new MobileActivityRewardSelection();

        Assert.Equal(new[] { 0, 2 }, selection.Refresh(progress, MirClass.Warrior, MirGender.Male));
        Assert.False(selection.TryResolve(progress, MirClass.Warrior, MirGender.Male, out _, out string error));
        Assert.Contains("选择", error);
        Assert.True(selection.Select(progress, 2, MirClass.Warrior, MirGender.Male));
        Assert.True(selection.TryResolve(progress, MirClass.Warrior, MirGender.Male, out int rawIndex, out _));
        Assert.Equal(2, rawIndex);

        var singleVisible = new ClientQuestProgress
        {
            Id = 42,
            QuestInfo = new ClientQuestInfo
            {
                Index = 42,
                Type = QuestType.重复,
                RewardsSelectItem = new List<QuestItemReward>
                {
                    new QuestItemReward { Item = new ItemInfo { Name = "法师女", RequiredClass = RequiredClass.Wizard, RequiredGender = RequiredGender.Female } },
                    new QuestItemReward { Item = new ItemInfo { Name = "战士男", RequiredClass = RequiredClass.Warrior, RequiredGender = RequiredGender.Male } },
                },
            },
            Taken = true,
            Completed = true,
        };
        Assert.Equal(new[] { 1 }, selection.Refresh(singleVisible, MirClass.Warrior, MirGender.Male));
        Assert.True(selection.TryResolve(singleVisible, MirClass.Warrior, MirGender.Male, out rawIndex, out _));
        Assert.Equal(1, rawIndex);

        selection.Clear();
        var noRewards = Progress(Info(43, QuestType.重复), taken: true, completed: true);
        Assert.True(selection.TryResolve(noRewards, MirClass.Warrior, MirGender.Male, out rawIndex, out _));
        Assert.Equal(-1, rawIndex);
    }

    private static ClientQuestInfo Info(int index, QuestType type) => new()
    {
        Index = index,
        Name = "活动 " + index,
        Type = type,
        NPCIndex = 1000u + (uint)index,
    };

    private static ClientQuestProgress Progress(ClientQuestInfo info, bool taken, bool completed = false) => new()
    {
        Id = info.Index,
        QuestInfo = info,
        Taken = taken,
        Completed = completed,
    };
}
