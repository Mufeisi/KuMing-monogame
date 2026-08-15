using Server;
using Server.MirObjects;
using Server.Scripting;
using Xunit;

namespace Base05.Tests;

[Collection(nameof(PhysicalTextFileProviderCollection))]
public sealed class LingFengWorldCommandTests
{
    [Fact]
    public void 地图别名仅在显式兼容版本下复用既有检测和传送动作()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        try
        {
            Settings.TxtScriptsCompatibilityVersion = string.Empty;
            var disabled = Segment();
            disabled.ParseCheck("CHECKMAPNAME 0");
            disabled.ParseAct(disabled.ActList, "TELEPORT 0 333 333");
            Assert.Empty(disabled.CheckList);
            Assert.Empty(disabled.ActList);

            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            var segment = Segment();
            segment.ParseCheck("CHECKMAPNAME 0");
            segment.ParseCheck("ISONMAP 比奇省");
            segment.ParseAct(segment.ActList, "TELEPORT 0 333 333");

            Assert.All(segment.CheckList, check => Assert.Equal(CheckType.CheckMap, check.Type));
            Assert.Equal(ActionType.Move, segment.ActList[0].Type);
            Assert.Equal(new[] { "0", "333", "333" }, segment.ActList[0].Params);
        }
        finally
        {
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 传送计划拒绝半随机坐标和负坐标()
    {
        Assert.True(LingFengWorldCommandExecutor.TryPlanTeleport(
            "0", "333", "333", out var exact, out _));
        Assert.False(exact.Random);
        Assert.True(LingFengWorldCommandExecutor.TryPlanTeleport(
            "0", "0", "0", out var random, out _));
        Assert.True(random.Random);
        Assert.False(LingFengWorldCommandExecutor.TryPlanTeleport(
            "0", "0", "333", out _, out string halfRandom));
        Assert.Contains("0,0", halfRandom);
        Assert.False(LingFengWorldCommandExecutor.TryPlanTeleport(
            "0", "-1", "2", out _, out _));
    }

    [Fact]
    public void 宝宝计划严格限制数量和等级并保留既有命令路由()
    {
        Assert.True(LingFengWorldCommandExecutor.TryPlanPet(
            "虎卫", "5", "7", out var plan, out _));
        Assert.Equal((byte)5, plan.Count);
        Assert.Equal((byte)7, plan.Level);
        Assert.False(LingFengWorldCommandExecutor.TryPlanPet(
            "虎卫", "6", "0", out _, out _));
        Assert.False(LingFengWorldCommandExecutor.TryPlanPet(
            "虎卫", "1", "8", out _, out _));

        var segment = Segment();
        segment.ParseCheck("PETCOUNT >= 1");
        segment.ParseCheck("CHECKPET 虎卫");
        segment.ParseAct(segment.ActList, "GIVEPET 虎卫 2 3");
        segment.ParseAct(segment.ActList, "CLEARPETS");
        Assert.Equal(new[] { CheckType.PetCount, CheckType.CheckPet },
            segment.CheckList.Select(check => check.Type));
        Assert.Equal(new[] { ActionType.GivePet, ActionType.ClearPets },
            segment.ActList.Select(action => action.Type));
    }

    private static NPCSegment Segment() => new(
        new NPCPage("[@MAIN]"), new List<string>(), new List<string>(),
        new List<string>(), new List<string>(), new List<string>());
}
