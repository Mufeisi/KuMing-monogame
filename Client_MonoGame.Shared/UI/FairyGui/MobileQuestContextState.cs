using System;
using System.Collections.Generic;

namespace MonoShare;

/// <summary>
/// 任务窗口的业务上下文，与 FairyGUI 控件绑定生命周期分离。
/// 重新绑定只清理控件引用；用户关闭窗口才清理活动/普通任务上下文。
/// </summary>
internal sealed class MobileQuestContextState
{
    public bool IsActivityMode { get; private set; }
    public uint NpcObjectId { get; private set; }
    public string NpcName { get; private set; } = string.Empty;
    public int SelectedQuestIndex { get; private set; }

    public void EnterNpc(uint npcObjectId, string npcName)
    {
        IsActivityMode = false;
        NpcObjectId = npcObjectId;
        NpcName = npcName ?? string.Empty;
    }

    public void EnterActivity()
    {
        IsActivityMode = true;
        NpcObjectId = 0;
        NpcName = "活动/赏金";
    }

    public void Select(int questIndex)
    {
        if (questIndex > 0)
            SelectedQuestIndex = questIndex;
    }

    public void ResetForRebind()
    {
        SelectedQuestIndex = 0;
    }

    public void ResetForClose()
    {
        IsActivityMode = false;
        NpcObjectId = 0;
        NpcName = string.Empty;
        SelectedQuestIndex = 0;
    }
}

internal static class MobileQuestBindingPolicy
{
    public static readonly string[] RequiredOperationKeys = { "Accept", "Finish", "Abandon", "Share", "Track" };

    public static bool HasKeyword(string candidateName, string candidateTitle, IReadOnlyList<string> keywords)
    {
        if (keywords == null || keywords.Count == 0)
            return false;

        string name = candidateName ?? string.Empty;
        string title = candidateTitle ?? string.Empty;
        for (int i = 0; i < keywords.Count; i++)
        {
            string keyword = keywords[i]?.Trim();
            if (string.IsNullOrWhiteSpace(keyword))
                continue;

            if (name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0 ||
                title.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }

        return false;
    }

    public static bool TryClaim(HashSet<string> usedKeys, string candidateKey)
    {
        if (usedKeys == null || string.IsNullOrWhiteSpace(candidateKey))
            return false;

        return usedKeys.Add(candidateKey.Trim());
    }

    public static bool NeedsFallback(int reliableOperationCount)
    {
        return reliableOperationCount < RequiredOperationKeys.Length;
    }

    public static bool ShouldCreateFallback(bool activityMode, int reliableOperationCount)
    {
        return activityMode && NeedsFallback(reliableOperationCount);
    }

    public static bool ShouldCreateRewardBar(bool activityMode, int visibleCandidateCount)
    {
        return activityMode && visibleCandidateCount > 1;
    }

    public static bool ShouldUseWindowFallback(string windowKey, bool activityMode)
    {
        return activityMode && string.Equals(windowKey, "Quest", StringComparison.OrdinalIgnoreCase);
    }

    public static IReadOnlyList<string> MissingOperationTargets(IEnumerable<string> reliableOperationKeys)
    {
        var reliable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (reliableOperationKeys != null)
        {
            foreach (string key in reliableOperationKeys)
            {
                if (!string.IsNullOrWhiteSpace(key))
                    reliable.Add(key.Trim());
            }
        }

        var missing = new List<string>();
        for (int i = 0; i < RequiredOperationKeys.Length; i++)
        {
            if (!reliable.Contains(RequiredOperationKeys[i]))
                missing.Add(RequiredOperationKeys[i]);
        }

        return missing;
    }
}

/// <summary>
/// 动态任务栏的 FairyGUI 命中约束。
/// DisplayObject.InternalHitTest 会先拒绝 touchable=false 的父层；
/// opaque=true 又会让父层拦截其空白范围，所以动态栏必须使用这组值。
/// </summary>
internal static class MobileQuestDynamicBarPolicy
{
    public const bool ParentTouchable = true;
    public const bool ParentOpaque = false;

    public static bool AllowsChildHit(bool parentTouchable, bool parentOpaque)
    {
        return parentTouchable && !parentOpaque;
    }
}
