using Server.Authoring;
using Xunit;

namespace Base05.Tests;

public sealed class SkillTimelineInspectionTests
{
    [Fact]
    public void FireBall_展示施法飞行命中持续效果和服务端延迟()
    {
        SkillTimelineProfile profile = SkillTimelineInspector.Build(Spell.FireBall, sampleDistance: 5);

        Assert.True(profile.IsModeled);
        Assert.Contains(profile.Events, item => item.Phase == SkillTimelinePhase.Cast);
        Assert.Contains(profile.Events, item => item.Phase == SkillTimelinePhase.Flight);
        Assert.Contains(profile.Events, item => item.Phase == SkillTimelinePhase.Hit && item.ServerAuthoritative && item.Timing.Contains("750"));
        Assert.Contains(profile.Events, item => item.Phase == SkillTimelinePhase.PersistentEffect && item.DurationMilliseconds == 0);
        Assert.Contains(profile.Events, item => item.Description.Contains("音效 +2"));
    }

    [Fact]
    public void FireBall_两端代码资源一致但实体校验缺口保持可见()
    {
        SkillTimelineProfile profile = SkillTimelineInspector.Build(Spell.FireBall);

        Assert.Equal(2, profile.Resources.Count);
        Assert.All(profile.Resources, resource => Assert.True(resource.CodeParityVerified));
        Assert.All(profile.Resources, resource => Assert.False(resource.PhysicalAssetVerified));
        Assert.Contains(profile.Resources, resource => resource.LogicalReference.Contains("Libraries.Magic"));
        Assert.Contains(profile.Resources, resource => resource.LogicalReference.Contains("20000"));

        SkillVisualComparisonResult comparison = SkillVisualParityInspector.Compare(profile);
        Assert.True(comparison.IsComparable);
        Assert.Equal(new[] { "施法", "飞行", "命中", "音效" }, comparison.MatchingFields);
        Assert.Empty(comparison.Differences);
        Assert.Equal(2, comparison.VerificationGaps.Count);
    }

    [Fact]
    public void 双端表现不一致_差异定位到两个平台拥有者()
    {
        SkillTimelineProfile original = SkillTimelineInspector.Build(Spell.FireBall);
        SkillTimelineProfile changed = original with
        {
            AndroidContract = original.AndroidContract with { HitVisual = "Libraries.Magic[999]" }
        };

        SkillVisualComparisonResult comparison = SkillVisualParityInspector.Compare(changed);

        SkillVisualDifference difference = Assert.Single(comparison.Differences);
        Assert.Equal("命中", difference.Field);
        Assert.Contains("Client_VorticeDX11", difference.OwningSource);
        Assert.Contains("Client_MonoGame.Shared", difference.OwningSource);
    }

    [Fact]
    public void 未核对技能_不推断表现资源和时序()
    {
        SkillTimelineProfile profile = SkillTimelineInspector.Build(Spell.SummonSkeleton);

        Assert.False(profile.IsModeled);
        Assert.Empty(profile.Events);
        Assert.Empty(profile.Resources);
        Assert.Contains("不从", profile.Explanation);
    }

    [Fact]
    public void 负样例距离_被输入边界拒绝()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SkillTimelineInspector.Build(Spell.FireBall, -1));
    }
}
