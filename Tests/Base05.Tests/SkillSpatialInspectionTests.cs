using Server.Authoring;
using Xunit;

namespace Base05.Tests;

public sealed class SkillSpatialInspectionTests
{
    [Fact]
    public void FireBall_展示敌对目标和飞行路径条件()
    {
        SkillSpatialProfile profile = SkillSpatialInspector.Build(Spell.FireBall, 3);

        Assert.True(profile.IsModeled);
        Assert.Equal(SkillTargetCondition.HostileObjectWithFlightPath, profile.TargetCondition);
        Assert.Equal(SkillCenterKind.Target, profile.CenterKind);
        Assert.Single(profile.Points);
        Assert.Contains("CanFly", profile.BehaviorEvidence);
    }

    [Fact]
    public void FireBang_展示以目标坐标为中心的三乘三范围()
    {
        SkillSpatialProfile profile = SkillSpatialInspector.Build(Spell.FireBang, 3);

        Assert.True(profile.IsModeled);
        Assert.Equal(SkillTargetCondition.MapLocation, profile.TargetCondition);
        Assert.Equal(SkillCenterKind.SelectedLocation, profile.CenterKind);
        Assert.Equal(9, profile.Points.Count);
        Assert.Contains(profile.Points, point => point == new SkillGridPoint(0, 0, SkillGridPointRole.Center));
        Assert.Equal("主主主\n主中主\n主主主".Replace("\n", Environment.NewLine), profile.RenderGrid());
    }

    [Fact]
    public void HellFire_三级展示主方向和两条附加方向线()
    {
        SkillSpatialProfile profile = SkillSpatialInspector.Build(Spell.HellFire, 3);

        Assert.True(profile.IsModeled);
        Assert.Equal(SkillTargetCondition.SelfDirection, profile.TargetCondition);
        Assert.Equal(13, profile.Points.Count);
        Assert.Equal(8, profile.Points.Count(point => point.Role == SkillGridPointRole.Additional));
        Assert.Contains(profile.Points, point => point == new SkillGridPoint(0, -4, SkillGridPointRole.Primary));
    }

    [Fact]
    public void 未核对技能_明确未建模且不根据Range猜测()
    {
        SkillSpatialProfile profile = SkillSpatialInspector.Build(Spell.SummonSkeleton, 3);

        Assert.False(profile.IsModeled);
        Assert.Equal(SkillTargetCondition.Unknown, profile.TargetCondition);
        Assert.Empty(profile.Points);
        Assert.Contains("未建模", profile.RenderGrid());
        Assert.Contains("不推断", profile.Explanation);
    }
}
