using Server.Authoring;
using Server.MirDatabase;
using Xunit;

namespace Base05.Tests;

public sealed class SkillInspectionTests
{
    [Fact]
    public void Build_生成四个等级的真实消耗冷却和结果区间()
    {
        var info = new MagicInfo
        {
            Name = "测试技能",
            Spell = Spell.FireBall,
            Icon = 7,
            Range = 9,
            Level1 = 10,
            Level2 = 20,
            Level3 = 30,
            Need1 = 100,
            Need2 = 200,
            Need3 = 300,
            BaseCost = 3,
            LevelCost = 2,
            DelayBase = 1800,
            DelayReduction = 200,
            MPowerBase = 8,
            MPowerBonus = 3,
            PowerBase = 2,
            PowerBonus = 2,
            MultiplierBase = 1F,
            MultiplierBonus = .5F
        };

        SkillInspectionSnapshot snapshot = SkillInspector.Build(info, "测试技能书");

        Assert.Equal(4, snapshot.Levels.Count);
        Assert.Equal(new SkillLevelInspection(0, 0, 0, 3, 1800, 4, 6), snapshot.Levels[0]);
        Assert.Equal(new SkillLevelInspection(3, 30, 300, 9, 1200, 25, 32), snapshot.Levels[3]);
        Assert.True(snapshot.BookResolved);
        Assert.Equal("测试技能书", snapshot.BookName);
        Assert.Empty(snapshot.Diagnostics);
    }

    [Fact]
    public void Build_只读投影与诊断不会修改原始MagicInfo()
    {
        var info = new MagicInfo
        {
            Name = "冷却异常技能",
            Spell = Spell.FireBall,
            DelayBase = 100,
            DelayReduction = 60,
            MultiplierBase = 1F
        };

        SkillInspectionSnapshot snapshot = SkillInspector.Build(info, string.Empty);

        Assert.Equal(-80, snapshot.Levels[3].CooldownMilliseconds);
        Assert.Contains(snapshot.Diagnostics, value => value.Contains("冷却计算结果"));
        Assert.Contains(snapshot.Diagnostics, value => value.Contains("技能书"));
        Assert.Equal((uint)100, info.DelayBase);
        Assert.Equal((uint)60, info.DelayReduction);
        Assert.Equal("冷却异常技能", info.Name);
    }
}
