using Server.MirDatabase;

namespace Server.Authoring;

public sealed record SkillLevelInspection(
    byte SkillLevel,
    byte RequiredCharacterLevel,
    ushort RequiredExperience,
    int MpCost,
    long CooldownMilliseconds,
    int MinimumResult,
    int MaximumResult);

public sealed record SkillInspectionSnapshot(
    string Name,
    Spell Spell,
    byte Icon,
    byte Range,
    string BookName,
    bool BookResolved,
    IReadOnlyList<SkillLevelInspection> Levels,
    IReadOnlyList<string> Diagnostics)
{
    public const string ConfigurationOwner = "Server.MirDatabase.MagicInfo";
    public const string RuntimeOwner = "Server.MirObjects.PlayerObject";
    public const string ClientProjection = "ClientMagic（只读投影，战斗结果仍由服务端决定）";
}

public static class SkillInspector
{
    public static SkillInspectionSnapshot Build(MagicInfo info, string bookName)
    {
        ArgumentNullException.ThrowIfNull(info);

        var levels = new SkillLevelInspection[4];
        var requiredLevels = new[] { (byte)0, info.Level1, info.Level2, info.Level3 };
        var requiredExperience = new[] { (ushort)0, info.Need1, info.Need2, info.Need3 };
        var diagnostics = new List<string>();

        for (byte level = 0; level < levels.Length; level++)
        {
            long cooldown = (long)info.DelayBase - ((long)level * info.DelayReduction);
            if (cooldown < 0)
                diagnostics.Add($"等级 {level} 的冷却计算结果为 {cooldown} ms；保存前必须阻断该参数组合。");

            levels[level] = new SkillLevelInspection(
                level,
                requiredLevels[level],
                requiredExperience[level],
                info.BaseCost + (level * info.LevelCost),
                cooldown,
                CalculateResult(info, level, useMaximum: false),
                CalculateResult(info, level, useMaximum: true));
        }

        if (string.IsNullOrWhiteSpace(info.Name))
            diagnostics.Add("技能名称为空。");
        if (info.MultiplierBase < 0 || info.MultiplierBonus < 0)
            diagnostics.Add("伤害倍率包含负值；只读页会照实展示，但安全编辑不得保存。");
        if (string.IsNullOrWhiteSpace(bookName))
            diagnostics.Add("物品库没有解析到对应技能书。");

        return new SkillInspectionSnapshot(
            info.Name,
            info.Spell,
            info.Icon,
            info.Range,
            bookName ?? string.Empty,
            !string.IsNullOrWhiteSpace(bookName),
            Array.AsReadOnly(levels),
            diagnostics.AsReadOnly());
    }

    private static int CalculateResult(MagicInfo info, byte level, bool useMaximum)
    {
        int magicPower = useMaximum ? GetRuntimeMaximum(info.MPowerBase, info.MPowerBonus) : info.MPowerBase;
        int fixedPower = useMaximum ? GetRuntimeMaximum(info.PowerBase, info.PowerBonus) : info.PowerBase;
        float multiplier = info.MultiplierBase + (level * info.MultiplierBonus);
        return (int)Math.Round((((magicPower / 4F) * (level + 1)) + fixedPower) * multiplier);
    }

    private static int GetRuntimeMaximum(ushort minimum, ushort randomWidth)
    {
        return randomWidth == 0 ? minimum : minimum + randomWidth - 1;
    }
}
