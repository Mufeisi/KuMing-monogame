using System;
using System.Collections.Generic;
using System.Linq;

namespace MonoShare.MirScenes;

/// <summary>
/// 移动端“活动/赏金”投影。
///
/// 服务端没有独立的 Activity/Bounty 协议；每日任务和重复任务沿用
/// QuestInfo/QuestProgress 以及 Accept/Finish/Abandon/Share 请求。此状态只
/// 做客户端投影和一次性请求门，不判定目标进度、奖励或结算结果。
/// </summary>
public sealed class MobileActivityState
{
    public const long RequestTimeoutMs = 5000;

    private readonly Dictionary<int, ClientQuestInfo> _definitions = new Dictionary<int, ClientQuestInfo>();
    private readonly Dictionary<int, ClientQuestProgress> _active = new Dictionary<int, ClientQuestProgress>();
    private readonly HashSet<int> _completed = new HashSet<int>();
    private readonly List<ClientQuestProgress> _activities = new List<ClientQuestProgress>();
    private long _pendingSinceMs = -1;

    public bool IsOpen { get; private set; }
    public int SelectedQuestIndex { get; private set; }
    public MobileActivityOperation PendingOperation { get; private set; }
    public int PendingQuestIndex { get; private set; }
    public bool ResultUncertain { get; private set; }
    public bool RequestPending => PendingOperation != MobileActivityOperation.None;
    public string Error { get; private set; }
    public int Revision { get; private set; }
    public bool HasActivities => _activities.Count > 0;
    public IReadOnlyList<ClientQuestProgress> Activities => _activities;

    public ClientQuestProgress SelectedQuest
    {
        get
        {
            if (SelectedQuestIndex <= 0)
                return null;

            return Find(SelectedQuestIndex);
        }
    }

    public static bool IsActivity(ClientQuestInfo info)
    {
        return info != null && (info.Type == QuestType.Daily || info.Type == QuestType.Repeatable);
    }

    public static string TypeLabel(ClientQuestInfo info)
    {
        if (info == null)
            return "活动/赏金";

        return info.Type == QuestType.Daily ? "每日活动" :
            info.Type == QuestType.Repeatable ? "重复赏金" : "任务";
    }

    public void ResetForSession()
    {
        IsOpen = false;
        SelectedQuestIndex = 0;
        PendingOperation = MobileActivityOperation.None;
        PendingQuestIndex = 0;
        ResultUncertain = false;
        _pendingSinceMs = -1;
        Error = null;
        _definitions.Clear();
        _active.Clear();
        _completed.Clear();
        _activities.Clear();
        Revision++;
    }

    /// <summary>
    /// 以登录/重登和服务端增量同步提供的快照重建投影。
    /// </summary>
    public void SyncSnapshot(
        IEnumerable<ClientQuestInfo> definitions,
        IEnumerable<ClientQuestProgress> active,
        IEnumerable<int> completed)
    {
        bool hadPending = RequestPending;
        _definitions.Clear();
        _active.Clear();
        _completed.Clear();

        if (definitions != null)
        {
            foreach (ClientQuestInfo info in definitions)
            {
                if (info == null || info.Index <= 0)
                    continue;

                _definitions[info.Index] = info;
            }
        }

        if (completed != null)
        {
            foreach (int index in completed)
            {
                if (index > 0)
                    _completed.Add(index);
            }
        }

        if (active != null)
        {
            foreach (ClientQuestProgress progress in active)
            {
                if (progress == null)
                    continue;

                int index = progress.Id > 0 ? progress.Id : progress.QuestInfo?.Index ?? 0;
                if (index <= 0)
                    continue;

                if (progress.QuestInfo != null)
                    _definitions[index] = progress.QuestInfo;

                ClientQuestInfo info = progress.QuestInfo ?? GetDefinition(index);
                if (!IsActivity(info))
                    continue;

                progress.Id = index;
                progress.QuestInfo = info;
                _active[index] = progress;
            }
        }

        RebuildProjection();
        if (hadPending)
            ReconcilePendingWithSnapshot();
        else
            Error = null;
        IsOpen = true;
        Revision++;
    }

    public bool ApplyQuestInfo(ClientQuestInfo info)
    {
        if (info == null || info.Index <= 0)
            return false;

        _definitions[info.Index] = info;
        RebuildProjection();
        Revision++;
        return IsActivity(info);
    }

    /// <summary>应用服务端 ChangeQuest；只有匹配的活动请求会释放请求门。</summary>
    public bool ApplyQuestChange(ClientQuestProgress progress, QuestState state)
    {
        if (progress == null)
            return false;

        int index = progress.Id > 0 ? progress.Id : progress.QuestInfo?.Index ?? 0;
        if (index <= 0)
            return false;

        if (progress.QuestInfo != null)
            _definitions[index] = progress.QuestInfo;

        ClientQuestInfo info = progress.QuestInfo ?? GetDefinition(index);
        if (!IsActivity(info))
            return false;

        progress.Id = index;
        progress.QuestInfo = info;

        switch (state)
        {
            case QuestState.Add:
            case QuestState.Update:
                _active[index] = progress;
                break;
            case QuestState.Remove:
                _active.Remove(index);
                break;
            default:
                return false;
        }

        if (MatchesPending(index, state))
            ClearPending();

        RebuildProjection();
        IsOpen = true;
        Revision++;
        return true;
    }

    public bool ApplyCompleted(IEnumerable<int> completed)
    {
        if (completed == null)
            return false;

        _completed.Clear();
        foreach (int index in completed)
        {
            if (index > 0)
                _completed.Add(index);
        }

        if (RequestPending && PendingOperation == MobileActivityOperation.Finish &&
            _completed.Contains(PendingQuestIndex))
        {
            ClearPending();
        }

        RebuildProjection();
        IsOpen = true;
        Revision++;
        return true;
    }

    public bool Select(int questIndex)
    {
        if (Find(questIndex) == null)
            return false;

        SelectedQuestIndex = questIndex;
        IsOpen = true;
        Error = null;
        Revision++;
        return true;
    }

    public bool BeginRequest(MobileActivityOperation operation, int questIndex, long nowMs)
    {
        if (operation == MobileActivityOperation.None)
            return false;

        ClientQuestProgress quest = Find(questIndex);
        if (quest == null || quest.QuestInfo == null || !IsActivity(quest.QuestInfo))
        {
            SetFailure("活动/赏金不存在或已刷新。", clearPending: false);
            return false;
        }

        if (RequestPending)
        {
            SetFailure(ResultUncertain ? Error : "活动/赏金请求已在处理中。", clearPending: false);
            return false;
        }

        bool allowed = operation switch
        {
            MobileActivityOperation.Accept => !quest.Taken && !quest.Completed && !_completed.Contains(questIndex),
            MobileActivityOperation.Finish => quest.Taken && quest.Completed,
            MobileActivityOperation.Abandon => quest.Taken && !quest.Completed,
            MobileActivityOperation.Share => quest.Taken && !quest.Completed,
            _ => false,
        };

        if (!allowed)
        {
            SetFailure("当前活动/赏金状态不允许此操作。", clearPending: false);
            return false;
        }

        PendingOperation = operation;
        PendingQuestIndex = questIndex;
        ResultUncertain = false;
        _pendingSinceMs = NormalizeClock(nowMs);
        SelectedQuestIndex = questIndex;
        IsOpen = true;
        Error = null;
        Revision++;
        return true;
    }

    /// <summary>
    /// 服务端通过 SystemChat 返回的任务失败。未知聊天不释放请求门。
    /// </summary>
    public bool ApplyServerSystemMessage(string message)
    {
        if (!RequestPending)
            return false;

        string text = (message ?? string.Empty).Trim();
        if (text.Length == 0 || !IsOperationFailure(PendingOperation, text))
            return false;

        ClearPending();
        Error = text;
        IsOpen = true;
        Revision++;
        return true;
    }

    /// <summary>
    /// 处理服务端无响应：接取/放弃/分享可重试；交付保留结果未确认锁，
    /// 只能由权威状态、明确失败或重登解除。
    /// </summary>
    public bool Tick(long nowMs)
    {
        if (!RequestPending || ResultUncertain || !IsExpired(_pendingSinceMs, NormalizeClock(nowMs)))
            return false;

        MobileActivityOperation operation = PendingOperation;
        if (operation == MobileActivityOperation.Finish)
        {
            ResultUncertain = true;
            Error = "交付结果未确认，请等待任务状态同步。";
            IsOpen = true;
            Revision++;
            return true;
        }

        ClearPending();
        Error = operation == MobileActivityOperation.Share
            ? "分享结果未确认，可谨慎重试。"
            : "活动/赏金请求超时，可重试。";
        IsOpen = true;
        Revision++;
        return true;
    }

    public bool ReportLocalError(string message)
    {
        SetFailure(message, clearPending: false);
        return false;
    }

    private ClientQuestProgress Find(int questIndex)
    {
        if (questIndex <= 0)
            return null;

        for (int i = 0; i < _activities.Count; i++)
        {
            ClientQuestProgress progress = _activities[i];
            if (progress?.Id == questIndex)
                return progress;
        }

        return null;
    }

    private ClientQuestInfo GetDefinition(int index)
    {
        return _definitions.TryGetValue(index, out ClientQuestInfo info) ? info : null;
    }

    private void RebuildProjection()
    {
        _activities.Clear();

        foreach (ClientQuestProgress progress in _active.Values)
        {
            if (progress?.QuestInfo == null || !IsActivity(progress.QuestInfo))
                continue;

            if (_completed.Contains(progress.Id))
                continue;

            _activities.Add(progress);
        }

        foreach (ClientQuestInfo info in _definitions.Values)
        {
            if (!IsActivity(info) || _completed.Contains(info.Index) || _active.ContainsKey(info.Index))
                continue;

            _activities.Add(new ClientQuestProgress
            {
                Id = info.Index,
                QuestInfo = info,
                New = true,
            });
        }

        _activities.Sort((left, right) =>
        {
            int leftType = left?.QuestInfo?.Type == QuestType.Daily ? 0 : 1;
            int rightType = right?.QuestInfo?.Type == QuestType.Daily ? 0 : 1;
            int typeCompare = leftType.CompareTo(rightType);
            if (typeCompare != 0)
                return typeCompare;

            return (left?.Id ?? 0).CompareTo(right?.Id ?? 0);
        });

        if (SelectedQuestIndex > 0 && Find(SelectedQuestIndex) == null)
            SelectedQuestIndex = _activities.Count > 0 ? _activities[0].Id : 0;
        else if (SelectedQuestIndex <= 0 && _activities.Count > 0)
            SelectedQuestIndex = _activities[0].Id;
    }

    private bool MatchesPending(int index, QuestState state)
    {
        if (!RequestPending || PendingQuestIndex != index)
            return false;

        if (PendingOperation == MobileActivityOperation.Accept && state == QuestState.Add)
            return true;

        if (PendingOperation == MobileActivityOperation.Abandon && state == QuestState.Remove)
            return true;

        return PendingOperation == MobileActivityOperation.Finish &&
               state == QuestState.Remove;
    }

    private void ReconcilePendingWithSnapshot()
    {
        if (!RequestPending)
            return;

        bool hasActive = _active.TryGetValue(PendingQuestIndex, out ClientQuestProgress active);
        bool completed = _completed.Contains(PendingQuestIndex);

        switch (PendingOperation)
        {
            case MobileActivityOperation.Accept:
                if (hasActive && active != null && active.Taken)
                    ClearPending();
                break;
            case MobileActivityOperation.Abandon:
                if (!hasActive)
                    ClearPending();
                break;
            case MobileActivityOperation.Finish:
                if (completed || !hasActive)
                    ClearPending();
                else if (ResultUncertain && string.IsNullOrWhiteSpace(Error))
                    Error = "交付结果未确认，请等待任务状态同步。";
                break;
        }
    }

    private void ClearPending()
    {
        PendingOperation = MobileActivityOperation.None;
        PendingQuestIndex = 0;
        ResultUncertain = false;
        _pendingSinceMs = -1;
        Error = null;
    }

    private void SetFailure(string message, bool clearPending)
    {
        if (clearPending)
            ClearPending();

        IsOpen = true;
        Error = string.IsNullOrWhiteSpace(message) ? "活动/赏金请求无效。" : message.Trim();
        Revision++;
    }

    private static bool IsOperationFailure(MobileActivityOperation operation, string text)
    {
        switch (operation)
        {
            case MobileActivityOperation.Share:
                return text.Contains("任务无法共享", StringComparison.Ordinal);
            case MobileActivityOperation.Accept:
                return text.Contains("无法接受", StringComparison.Ordinal) ||
                       text.Contains("已完成任务的最大数量", StringComparison.Ordinal) ||
                       text.Contains("任务已经完成", StringComparison.Ordinal) ||
                       text.Contains("任务已完成", StringComparison.Ordinal);
            case MobileActivityOperation.Finish:
                if (!text.Contains("提交", StringComparison.Ordinal) &&
                    !text.Contains("交付", StringComparison.Ordinal))
                    return false;

                return text.Contains("背包已满", StringComparison.Ordinal) ||
                       text.Contains("无法", StringComparison.Ordinal) ||
                       text.Contains("不能", StringComparison.Ordinal) ||
                       text.Contains("失败", StringComparison.Ordinal) ||
                       text.Contains("不足", StringComparison.Ordinal) ||
                       text.Contains("没有", StringComparison.Ordinal) ||
                       text.Contains("未完成", StringComparison.Ordinal);
            default:
                return false;
        }
    }

    private static long NormalizeClock(long nowMs) => nowMs < 0 ? 0 : nowMs;

    private static bool IsExpired(long startedAtMs, long nowMs)
    {
        return startedAtMs >= 0 && nowMs >= startedAtMs && nowMs - startedAtMs >= RequestTimeoutMs;
    }
}

public enum MobileActivityOperation : byte
{
    None = 0,
    Accept = 1,
    Finish = 2,
    Abandon = 3,
    Share = 4,
}
