using Server;
using Server.MirObjects;
using Server.Scripting;
using Xunit;

namespace Base05.Tests;

[Collection(nameof(PhysicalTextFileProviderCollection))]
public sealed class LingFengPlayerCommandTests
{
    [Fact]
    public void 高频比较与取反通过真实NPC检测链执行()
    {
        Assert.Equal(56, (int)CheckType.IsGuildLeader);
        Assert.Equal(57, (int)CheckType.LingFengCompare);
        bool oldEnabled = Settings.TxtScriptsEnabled;
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        try
        {
            Settings.TxtScriptsEnabled = true;
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            var player = new PlayerObject();

            var matching = Segment();
            matching.ParseCheck("EQUAL 奖励甲 奖励甲");
            matching.ParseCheck("LARGE 30 29");
            matching.ParseCheck("SMALL 29 30");
            matching.ParseCheck("NOT EQUAL 玩家甲 玩家乙");
            matching.ParseCheck("!SMALL 30 30");

            Assert.True(matching.Check(player));
            Assert.Equal(5, matching.CheckList.Count);
            Assert.True(matching.CheckList[3].Negated);
            Assert.True(matching.CheckList[4].Negated);

            var existingCheck = Segment();
            existingCheck.ParseCheck("NOT CHECKLEVEL > 100");
            Assert.True(existingCheck.CheckList.Single().Negated);

            var failing = Segment();
            failing.ParseCheck("LARGE 非数字 1");
            Assert.False(failing.Check(player));
        }
        finally
        {
            Settings.TxtScriptsEnabled = oldEnabled;
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 人物数值检测支持别名与双边界并拒绝非法参数()
    {
        var segment = Segment();
        segment.ParseCheck("CHECKLEVEL >= 40");
        segment.ParseCheck("CHECKJOB 战士");
        segment.ParseCheck("CHECKPKPOINTEX < 100");
        segment.ParseCheck("CHECKEXP >= 1000 < 2000");
        segment.ParseCheck("CHECKHP > 0 <= 500");
        segment.ParseCheck("CHECKMP = 25");

        Assert.Equal(
            new[]
            {
                CheckType.Level, CheckType.CheckClass, CheckType.CheckPkPoint,
                CheckType.CheckExperience, CheckType.CheckHP, CheckType.CheckMP
            },
            segment.CheckList.Select(check => check.Type));
        Assert.Equal(new[] { ">=", "1000", "<", "2000" }, segment.CheckList[3].Params);

        Assert.True(LingFengNumericCommandExecutor.TryCheck(
            1500, segment.CheckList[3].Params, out bool matched, out _));
        Assert.True(matched);
        Assert.True(LingFengNumericCommandExecutor.TryCheck(
            2000, segment.CheckList[3].Params, out matched, out _));
        Assert.False(matched);
        Assert.False(LingFengNumericCommandExecutor.TryCheck(
            1, new[] { "??", "1" }, out _, out string invalidOperator));
        Assert.Contains("操作符", invalidOperator);
    }

    [Fact]
    public void GoldCount和ChangePkPoint形成类型化动作()
    {
        var segment = Segment();
        segment.ParseAct(segment.ActList, "GOLDCOUNT + 100");
        segment.ParseAct(segment.ActList, "GOLDCOUNT = 200");
        segment.ParseAct(segment.ActList, "CHANGEPKPOINT - 50");

        Assert.Collection(segment.ActList,
            action =>
            {
                Assert.Equal(ActionType.ChangeGold, action.Type);
                Assert.Equal(new[] { "+", "100" }, action.Params);
            },
            action =>
            {
                Assert.Equal(ActionType.ChangeGold, action.Type);
                Assert.Equal(new[] { "=", "200" }, action.Params);
            },
            action =>
            {
                Assert.Equal(ActionType.ChangePkPoint, action.Type);
                Assert.Equal(new[] { "-", "50" }, action.Params);
            });
    }

    [Fact]
    public void 真实语料高频金币和基础物品命令保持既有路由()
    {
        var segment = Segment();
        segment.ParseCheck("CHECKGOLD >= 100");
        segment.ParseCheck("CHECKITEM 回城卷 2");
        segment.ParseAct(segment.ActList, "GIVEGOLD 50");
        segment.ParseAct(segment.ActList, "TAKEGOLD 25");
        segment.ParseAct(segment.ActList, "GIVEITEM 回城卷 2");
        segment.ParseAct(segment.ActList, "TAKEITEM 回城卷 1");

        Assert.Equal(new[] { CheckType.CheckGold, CheckType.CheckItem },
            segment.CheckList.Select(check => check.Type));
        Assert.Equal(new[]
        {
            ActionType.GiveGold, ActionType.TakeGold, ActionType.GiveItem, ActionType.TakeItem
        }, segment.ActList.Select(action => action.Type));
        Assert.Equal(5u, LingFengNumericCommandExecutor.PlanGoldGain(uint.MaxValue - 5, 20));
        Assert.Equal(10u, LingFengNumericCommandExecutor.PlanGoldTake(10, 20));
    }

    [Fact]
    public void 金币调整越界失败且Pk减少不会低于零()
    {
        Assert.True(LingFengNumericCommandExecutor.TryAdjust(
            100, "+", "50", 0, 2_100_000_000, false, out long gold, out _));
        Assert.Equal(150, gold);

        Assert.False(LingFengNumericCommandExecutor.TryAdjust(
            2_100_000_000, "+", "1", 0, 2_100_000_000, false, out gold, out _));
        Assert.Equal(2_100_000_000, gold);
        Assert.False(LingFengNumericCommandExecutor.TryAdjust(
            20, "-", "21", 0, 2_100_000_000, false, out gold, out _));
        Assert.Equal(20, gold);

        Assert.True(LingFengNumericCommandExecutor.TryAdjust(
            20, "-", "21", 0, int.MaxValue, true, out long pk, out _));
        Assert.Equal(0, pk);
    }

    [Fact]
    public void 翎风物品参数只在显式兼容版本下路由且不伪造缺失模型()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            var segment = Segment();
            segment.ParseCheck("CHECKITEM 麻痹 1 1 0");
            segment.ParseAct(segment.ActList, "GIVE 回城卷 2");
            segment.ParseAct(segment.ActList, "TAKE 麻痹 1 0 1 1 -1");

            Assert.Equal(CheckType.CheckItemLingFeng, Assert.Single(segment.CheckList).Type);
            Assert.Equal(new[] { "麻痹", "1", "1", "0" }, segment.CheckList[0].Params);
            Assert.Equal(ActionType.GiveItem, segment.ActList[0].Type);
            Assert.Equal(ActionType.TakeItemLingFeng, segment.ActList[1].Type);
            Assert.Equal(new[] { "麻痹", "1", "0", "1", "1", "-1" }, segment.ActList[1].Params);

            Assert.Throws<InvalidDataException>(() =>
                segment.ParseAct(segment.ActList, "GIVE 屠龙 1 0 0 0 0 0 0 -1"));
        }
        finally
        {
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 物品名称与持久过滤具有稳定边界()
    {
        Assert.True(LingFengItemCommandExecutor.NameMatches("麻痹戒指", "麻痹", true));
        Assert.False(LingFengItemCommandExecutor.NameMatches("麻痹戒指", "麻痹", false));
        Assert.True(LingFengItemCommandExecutor.NameMatches("麻痹戒指", "麻痹戒指", false));
        Assert.True(LingFengItemCommandExecutor.DurabilityMatches(1000, 1000, -1));
        Assert.False(LingFengItemCommandExecutor.DurabilityMatches(999, 1000, -1));
        Assert.True(LingFengItemCommandExecutor.DurabilityMatches(999, 1000, -2));
        Assert.False(LingFengItemCommandExecutor.DurabilityMatches(1, 1, 2));
    }

    [Fact]
    public void 不可映射的扩展物品参数在候选快照发布前拒绝()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            var definition = new TextFileDefinition("NPCs/扩展物品")
                .AddLines(new[]
                {
                    "[@MAIN]", "#ACT", "GIVE 屠龙 1 0 0 0 0 0 0 -1",
                    "TAKE 麻痹 1 0 1 0 -1"
                });
            IReadOnlyList<string> errors = TxtScriptSnapshotValidator.Validate(new SingleProvider(definition));

            Assert.Equal(2, errors.Count(error => error.Contains("TXT-SNAPSHOT-013", StringComparison.Ordinal)));
        }
        finally
        {
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 严格翎风版本在发布前拒绝未知检测和动作命令()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        bool oldStrict = Settings.TxtScriptsStrictCompatibility;
        try
        {
            var definition = new TextFileDefinition("NPCs/未知命令")
                .AddLines(new[]
                {
                    "[@MAIN]", "#IF", "NOT_A_CHECK 1", "NOT UNKNOWN_CHECK 1",
                    "#ACT", "NOT_AN_ACTION 2"
                });
            var provider = new SingleProvider(definition);

            Settings.TxtScriptsCompatibilityVersion = string.Empty;
            Settings.TxtScriptsStrictCompatibility = true;
            Assert.DoesNotContain(TxtScriptSnapshotValidator.Validate(provider),
                error => error.Contains("TXT-SNAPSHOT-014", StringComparison.Ordinal));

            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            IReadOnlyList<string> errors = TxtScriptSnapshotValidator.Validate(provider);
            Assert.Equal(3, errors.Count(error => error.Contains("TXT-SNAPSHOT-014", StringComparison.Ordinal)));
            Assert.Contains(errors, error => error.Contains("NOT_A_CHECK", StringComparison.Ordinal));
            Assert.Contains(errors, error => error.Contains("UNKNOWN_CHECK", StringComparison.Ordinal));
            Assert.Contains(errors, error => error.Contains("NOT_AN_ACTION", StringComparison.Ordinal));
        }
        finally
        {
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
            Settings.TxtScriptsStrictCompatibility = oldStrict;
        }
    }

    [Fact]
    public void 高频人物物品金币闭环快照严格预检无未知命令()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        bool oldStrict = Settings.TxtScriptsStrictCompatibility;
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            Settings.TxtScriptsStrictCompatibility = true;
            var definition = new TextFileDefinition("NPCs/人物物品金币闭环")
                .AddLines(new[]
                {
                    "[@MAIN]", "#IF", "CHECKLEVEL >= 40", "CHECKGOLD >= 1000",
                    "CHECKITEM 回城卷 1 0 0", "#ACT", "TAKEGOLD 1000",
                    "GIVEITEM 奖励卷 1", "GOLDCOUNT + 50", "CHANGEPKPOINT - 10",
                    "TAKE 回城卷 1 0 0 1 0", "#SAY", "兑换完成"
                });

            Assert.Empty(TxtScriptSnapshotValidator.Validate(new SingleProvider(definition)));
        }
        finally
        {
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
            Settings.TxtScriptsStrictCompatibility = oldStrict;
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
