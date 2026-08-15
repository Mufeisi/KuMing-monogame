using Server;
using Server.MirObjects;
using Server.Scripting;
using Xunit;

namespace Base05.Tests;

[Collection(nameof(PhysicalTextFileProviderCollection))]
public sealed class LingFengSocialCommandTests
{
    [Fact]
    public void 任务状态别名仅在显式版本启用并映射统一任务检测()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        try
        {
            Settings.TxtScriptsCompatibilityVersion = string.Empty;
            var disabled = Segment();
            disabled.ParseCheck("ISQUESTCOMPLETED 10");
            Assert.Empty(disabled.CheckList);

            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            var segment = Segment();
            segment.ParseCheck("ISQUESTACTIVE 10");
            segment.ParseCheck("ISQUESTCOMPLETED 11");

            Assert.All(segment.CheckList, check => Assert.Equal(CheckType.CheckQuest, check.Type));
            Assert.Equal(new[] { "10", "ACTIVE" }, segment.CheckList[0].Params);
            Assert.Equal(new[] { "11", "COMPLETE" }, segment.CheckList[1].Params);
        }
        finally
        {
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 任务编号与行会经验在提交前失败关闭()
    {
        Assert.True(LingFengSocialCommandExecutor.TryParseQuestIndex("10", out int index, out _));
        Assert.Equal(10, index);
        Assert.False(LingFengSocialCommandExecutor.TryParseQuestIndex("0", out _, out _));
        Assert.False(LingFengSocialCommandExecutor.TryParseQuestIndex("bad", out _, out _));

        Assert.True(LingFengSocialCommandExecutor.TryPlanGuildExperience(
            true, "100", out uint amount, out _));
        Assert.Equal((uint)100, amount);
        Assert.False(LingFengSocialCommandExecutor.TryPlanGuildExperience(
            false, "100", out _, out string noGuild));
        Assert.Contains("不属于", noGuild);
        Assert.False(LingFengSocialCommandExecutor.TryPlanGuildExperience(
            true, "0", out _, out _));
        Assert.False(LingFengSocialCommandExecutor.TryPlanGuildExperience(
            true, "-1", out _, out _));
    }

    [Fact]
    public void 行会加入退出与经验动作保持既有领域路由()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            var segment = Segment();
            segment.ParseCheck("INGUILD 沙巴克");
            segment.ParseAct(segment.ActList, "ADDTOGUILD 沙巴克");
            segment.ParseAct(segment.ActList, "TRYREMOVEFROMGUILD");
            segment.ParseAct(segment.ActList, "GIVEGUILDEXP 100");

            Assert.Equal(CheckType.InGuild, Assert.Single(segment.CheckList).Type);
            Assert.Equal(
                new[] { ActionType.AddToGuild, ActionType.RemoveFromGuild, ActionType.GiveGuildExp },
                segment.ActList.Select(action => action.Type));
        }
        finally
        {
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 社会任务闭环快照拒绝非法事务参数()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            var valid = new TextFileDefinition("NPCs/社会任务闭环")
                .AddLines(new[]
                {
                    "[@MAIN]", "#IF", "ISQUESTCOMPLETED 10", "INGUILD 沙巴克",
                    "#ACT", "GIVEGUILDEXP 100", "TRYREMOVEFROMGUILD"
                });
            Assert.Empty(TxtScriptSnapshotValidator.Validate(new SingleProvider(valid)));

            var invalid = new TextFileDefinition("NPCs/社会任务非法参数")
                .AddLines(new[]
                {
                    "[@MAIN]", "#IF", "ISQUESTACTIVE 0", "#ACT",
                    "GIVEGUILDEXP -1", "TRYREMOVEFROMGUILD 多余参数"
                });
            Assert.Equal(3, TxtScriptSnapshotValidator.Validate(new SingleProvider(invalid))
                .Count(error => error.Contains("TXT-SNAPSHOT-015", StringComparison.Ordinal)));
        }
        finally
        {
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    private static NPCSegment Segment() => new(
        new NPCPage("[@MAIN]"), new List<string>(), new List<string>(),
        new List<string>(), new List<string>(), new List<string>());

    private sealed class SingleProvider : ITextFileProvider
    {
        private readonly TextFileDefinition _definition;

        public SingleProvider(TextFileDefinition definition) => _definition = definition;

        public IReadOnlyCollection<TextFileDefinition> GetAll() => new[] { _definition };

        public TextFileDefinition GetByKey(string key) =>
            LogicKey.NormalizeOrThrow(key) == _definition.Key ? _definition : null;
    }
}
