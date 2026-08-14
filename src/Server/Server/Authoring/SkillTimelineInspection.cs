namespace Server.Authoring;

public enum SkillTimelinePhase
{
    Cast,
    Flight,
    Hit,
    PersistentEffect,
    Sound
}

public sealed record SkillTimelineEvent(
    SkillTimelinePhase Phase,
    string Timing,
    int? DurationMilliseconds,
    string Description,
    bool ServerAuthoritative);

public sealed record SkillResourceReference(
    string Kind,
    string LogicalReference,
    string PcReference,
    string AndroidReference,
    bool CodeParityVerified,
    bool PhysicalAssetVerified,
    string VerificationNote);

public sealed record SkillPlatformVisualContract(
    string Platform,
    string CastVisual,
    string FlightVisual,
    string HitVisual,
    string SoundSequence,
    string SourceOwner);

public sealed record SkillVisualDifference(
    string Field,
    string PcValue,
    string AndroidValue,
    string OwningSource);

public sealed record SkillVisualComparisonResult(
    Spell Spell,
    bool IsComparable,
    IReadOnlyList<string> MatchingFields,
    IReadOnlyList<SkillVisualDifference> Differences,
    IReadOnlyList<string> VerificationGaps);

public sealed record SkillTimelineProfile(
    Spell Spell,
    bool IsModeled,
    int SampleDistance,
    IReadOnlyList<SkillTimelineEvent> Events,
    IReadOnlyList<SkillResourceReference> Resources,
    SkillPlatformVisualContract PcContract,
    SkillPlatformVisualContract AndroidContract,
    string BehaviorEvidence,
    string Explanation);

public static class SkillTimelineInspector
{
    public static SkillTimelineProfile Build(Spell spell, int sampleDistance = 5)
    {
        if (sampleDistance < 0)
            throw new ArgumentOutOfRangeException(nameof(sampleDistance));

        return spell switch
        {
            Spell.FireBall => FireBall(sampleDistance),
            _ => Unknown(spell, sampleDistance)
        };
    }

    private static SkillTimelineProfile FireBall(int distance)
    {
        int serverHitOffset = 500 + (distance * 50);
        var events = new[]
        {
            new SkillTimelineEvent(SkillTimelinePhase.Cast, "t=0", null,
                "PC 与 Android 播放 Magic[0..9] 施法效果和技能音效 +0。", false),
            new SkillTimelineEvent(SkillTimelinePhase.Flight, "施法动作发出后", null,
                "PC 与 Android 创建 Magic[10] 起始、6 帧、速度 30 的跟踪投射物，并播放技能音效 +1。", false),
            new SkillTimelineEvent(SkillTimelinePhase.Hit, $"服务端 t={serverHitOffset} ms（距离 {distance} 格样例）", null,
                "服务端按 500 + 距离×50 ms 执行延迟命中；目标与位置仍使用施法时快照复核。", true),
            new SkillTimelineEvent(SkillTimelinePhase.Hit, "客户端投射物 Complete", 600,
                "存活目标播放 Magic[170..179] 命中效果和技能音效 +2。", false),
            new SkillTimelineEvent(SkillTimelinePhase.PersistentEffect, "命中后", 0,
                "火球术没有由该路径创建持续地面或 Buff 效果。", true)
        };

        var resources = new[]
        {
            new SkillResourceReference("图像", "Libraries.Magic：0..9、10..15、170..179",
                "Client_VorticeDX11/PlayerObject.cs", "Client_MonoGame.Shared/PlayerObject.cs",
                true, false, "两端代码引用一致；外部 Magic 资源库实体未在本次源码快照中独立校验。"),
            new SkillResourceReference("音效", "20000 + (ushort)Spell.FireBall * 10 + 0/1/2",
                "Client_VorticeDX11/PlayerObject.cs", "Client_MonoGame.Shared/PlayerObject.cs",
                true, false, "两端音效编号公式一致；外部音频实体未在本次源码快照中独立校验。")
        };

        var pc = new SkillPlatformVisualContract(
            "PC",
            "Libraries.Magic[0..9]",
            "Libraries.Magic[10..15], 速度30",
            "Libraries.Magic[170..179], 600ms",
            "Spell×10 + 0/1/2",
            "src/Clients/Client_VorticeDX11/MirObjects/PlayerObject.cs");
        var android = new SkillPlatformVisualContract(
            "Android",
            "Libraries.Magic[0..9]",
            "Libraries.Magic[10..15], 速度30",
            "Libraries.Magic[170..179], 600ms",
            "Spell×10 + 0/1/2",
            "src/Clients/Client_MonoGame.Shared/MirObjects/PlayerObject.cs");

        return new SkillTimelineProfile(
            Spell.FireBall,
            true,
            distance,
            Array.AsReadOnly(events),
            Array.AsReadOnly(resources),
            pc,
            android,
            "服务端 HumanObject.Fireball；PC 与 Android/MonoGame PlayerObject 的 FireBall 施法和投射物分支。",
            "时间线区分客户端表现与服务端权威命中；飞行视觉完成时刻不替代服务端延迟裁决。");
    }

    private static SkillTimelineProfile Unknown(Spell spell, int distance)
    {
        return new SkillTimelineProfile(
            spell,
            false,
            distance,
            Array.Empty<SkillTimelineEvent>(),
            Array.Empty<SkillResourceReference>(),
            null,
            null,
            "尚未建立经过服务端与双端代码共同核对的时间线档案。",
            "未知时间线保持未建模，不从技能编号、Range 或相邻技能推断资源和时序。");
    }
}

public static class SkillVisualParityInspector
{
    public static SkillVisualComparisonResult Compare(SkillTimelineProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (!profile.IsModeled || profile.PcContract == null || profile.AndroidContract == null)
            return new SkillVisualComparisonResult(profile.Spell, false, Array.Empty<string>(), Array.Empty<SkillVisualDifference>(),
                Array.AsReadOnly(new[] { "该技能没有可比较的双端表现档案。" }));

        var matches = new List<string>();
        var differences = new List<SkillVisualDifference>();
        CompareField("施法", profile.PcContract.CastVisual, profile.AndroidContract.CastVisual);
        CompareField("飞行", profile.PcContract.FlightVisual, profile.AndroidContract.FlightVisual);
        CompareField("命中", profile.PcContract.HitVisual, profile.AndroidContract.HitVisual);
        CompareField("音效", profile.PcContract.SoundSequence, profile.AndroidContract.SoundSequence);

        var gaps = profile.Resources
            .Where(resource => !resource.PhysicalAssetVerified)
            .Select(resource => $"{resource.Kind}实体未核验：{resource.LogicalReference}")
            .ToList();

        return new SkillVisualComparisonResult(
            profile.Spell,
            true,
            matches.AsReadOnly(),
            differences.AsReadOnly(),
            gaps.AsReadOnly());

        void CompareField(string field, string pcValue, string androidValue)
        {
            if (string.Equals(pcValue, androidValue, StringComparison.Ordinal))
            {
                matches.Add(field);
                return;
            }

            differences.Add(new SkillVisualDifference(
                field,
                pcValue,
                androidValue,
                $"PC={profile.PcContract.SourceOwner}；Android={profile.AndroidContract.SourceOwner}"));
        }
    }
}
