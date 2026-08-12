using System.Collections.Generic;

namespace MonoShare.MirScenes;

/// <summary>
/// 活动交付奖励的可见候选与原始索引选择。
/// 服务端接收的是 RewardsSelectItem 原列表索引，过滤后不能改写索引。
/// </summary>
public sealed class MobileActivityRewardSelection
{
    private readonly List<int> _visibleOriginalIndices = new List<int>();

    public int QuestIndex { get; private set; }
    public int SelectedOriginalIndex { get; private set; } = -1;
    public IReadOnlyList<int> VisibleOriginalIndices => _visibleOriginalIndices;

    public void Clear()
    {
        QuestIndex = 0;
        SelectedOriginalIndex = -1;
        _visibleOriginalIndices.Clear();
    }

    public IReadOnlyList<int> Refresh(ClientQuestProgress progress, MirClass playerClass, MirGender playerGender)
    {
        int questIndex = progress?.Id ?? progress?.QuestInfo?.Index ?? 0;
        ClientQuestInfo info = progress?.QuestInfo;
        _visibleOriginalIndices.Clear();

        if (questIndex <= 0 || info == null || info.RewardsSelectItem == null)
        {
            Clear();
            return _visibleOriginalIndices;
        }

        if (QuestIndex != questIndex)
            SelectedOriginalIndex = -1;

        QuestIndex = questIndex;
        for (int i = 0; i < info.RewardsSelectItem.Count; i++)
        {
            QuestItemReward reward = info.RewardsSelectItem[i];
            if (IsVisible(reward, playerClass, playerGender))
                _visibleOriginalIndices.Add(i);
        }

        if (!_visibleOriginalIndices.Contains(SelectedOriginalIndex))
            SelectedOriginalIndex = _visibleOriginalIndices.Count == 1 ? _visibleOriginalIndices[0] : -1;

        return _visibleOriginalIndices;
    }

    public bool Select(ClientQuestProgress progress, int originalIndex, MirClass playerClass, MirGender playerGender)
    {
        Refresh(progress, playerClass, playerGender);
        if (!_visibleOriginalIndices.Contains(originalIndex))
            return false;

        SelectedOriginalIndex = originalIndex;
        return true;
    }

    public bool TryResolve(ClientQuestProgress progress, MirClass playerClass, MirGender playerGender, out int originalIndex, out string error)
    {
        Refresh(progress, playerClass, playerGender);
        originalIndex = -1;
        error = null;

        if (progress?.QuestInfo == null || progress.QuestInfo.RewardsSelectItem == null ||
            progress.QuestInfo.RewardsSelectItem.Count == 0)
            return true;

        if (_visibleOriginalIndices.Count == 0)
        {
            error = "当前职业/性别没有可选奖励。";
            return false;
        }

        if (_visibleOriginalIndices.Count > 1 && !_visibleOriginalIndices.Contains(SelectedOriginalIndex))
        {
            error = "请先选择一个活动奖励。";
            return false;
        }

        originalIndex = SelectedOriginalIndex;
        return originalIndex >= 0;
    }

    public static bool IsVisible(QuestItemReward reward, MirClass playerClass, MirGender playerGender)
    {
        ItemInfo item = reward?.Item;
        if (item == null)
            return false;

        RequiredClass requiredClass = RequiredClassFor(playerClass);
        RequiredGender requiredGender = playerGender == MirGender.Female || playerGender == MirGender.女性
            ? RequiredGender.Female
            : RequiredGender.Male;

        bool classAllowed = item.RequiredClass == 0 || item.RequiredClass.HasFlag(requiredClass);
        bool genderAllowed = item.RequiredGender == 0 || item.RequiredGender.HasFlag(requiredGender);
        return classAllowed && genderAllowed;
    }

    private static RequiredClass RequiredClassFor(MirClass playerClass)
    {
        return playerClass switch
        {
            MirClass.Warrior => RequiredClass.Warrior,
            MirClass.Wizard => RequiredClass.Wizard,
            MirClass.Taoist => RequiredClass.Taoist,
            MirClass.Assassin => RequiredClass.Assassin,
            MirClass.Archer => RequiredClass.Archer,
            _ => RequiredClass.None,
        };
    }
}
