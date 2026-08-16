using System.Drawing;
using Server;
using Server.MirDatabase;
using Server.MirEnvir;
using Server.MirObjects;
using Server.Scripting.ServerSymbols;
using Server.Scripting;
using Xunit;

namespace Base05.Tests;

[Collection(nameof(PhysicalTextFileProviderCollection))]
public sealed class LingFengP0ServerSymbolIntegrationTests : IDisposable
{
    private readonly string _previousCompatibilityVersion;
    private readonly bool _previousTxtEnabled;
    private readonly bool _previousCSharpEnabled;

    public LingFengP0ServerSymbolIntegrationTests()
    {
        _previousCompatibilityVersion = Settings.TxtScriptsCompatibilityVersion;
        _previousTxtEnabled = Settings.TxtScriptsEnabled;
        _previousCSharpEnabled = Settings.CSharpScriptsEnabled;
        Settings.TxtScriptsCompatibilityVersion = "LFM2-2026.08";
        Settings.TxtScriptsEnabled = true;
        Settings.CSharpScriptsEnabled = false;
    }

    public void Dispose()
    {
        Settings.TxtScriptsCompatibilityVersion = _previousCompatibilityVersion;
        Settings.TxtScriptsEnabled = _previousTxtEnabled;
        Settings.CSharpScriptsEnabled = _previousCSharpEnabled;
    }

    private static readonly string[] SpecificationP0Names =
    {
        "USERNAME", "USERALLNAME", "LEVEL", "JOB", "GENDER", "HP", "MAXHP", "MP", "MAXMP", "EXP", "MAXEXP", "PKPOINT",
        "AC", "MAXAC", "MAC", "MAXMAC", "DC", "MAXDC", "MC", "MAXMC", "SC", "MAXSC", "HIT", "SPD", "LUCK",
        "MAP", "MAPTITLE", "X", "Y", "FBMAP", "FBMAPNAME",
        "GUILDNAME", "RANKNAME", "GUILDMEMBERCOUNT",
        "GOLDCOUNT", "GAMEGOLD", "GAMEPOINT", "GAMEDIAMOND", "GAMEGIRD", "JADE", "GAMEGLORY", "CREDITPOINT",
        "DATE", "TIME", "DATETIME", "YEAR", "MONTH", "DAY", "HOUR", "MINUTE", "SECOND", "SERVERNAME", "USERCOUNT", "ONUSERCOUNT", "DUMMYCOUNT",
        "DRESS", "WEAPON", "HELMET", "NECKLACE", "RING_L", "RING_R", "ARMRING_L", "ARMRING_R", "BELT", "BOOTS", "BUJUK", "CHARM", "SHIELD",
        "FASHIONDRESS", "FASHIONWEAPON", "FASHIONHELMET", "FASHIONNECKLACE", "FASHIONRINGL", "FASHIONRINGR", "FASHIONRING_L", "FASHIONRING_R",
        "FASHIONARMRINGL", "FASHIONARMRINGR", "FASHIONBELT", "FASHIONBOOTS", "FASHIONCHARM", "FASHIONRIGHTHAND"
    };

    [Fact]
    public void 运行时P0目录与实施规格清单完全一致()
    {
        Assert.Equal(
            SpecificationP0Names.OrderBy(x => x, StringComparer.Ordinal),
            LingFengP0ServerSymbols.CanonicalNames.OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public void P0目录中的每个常量都能从人物快照解析()
    {
        PlayerObject player = CreatePlayer();
        ServerSymbolContext context = LingFengP0ServerSymbols.CreateContext(player);

        foreach (string name in LingFengP0ServerSymbols.CanonicalNames)
        {
            ScriptTextRenderResult result = LingFengP0ServerSymbols.Render(player, $"<${name}>");

            Assert.True(
                result.Status is ScriptTextRenderStatus.Rendered or ScriptTextRenderStatus.CompletedWithDiagnostics,
                $"{name}: {string.Join(";", result.Diagnostics.Select(x => x.Message))}");
            Assert.DoesNotContain("<$", result.Text, StringComparison.Ordinal);
        }

        Assert.True(context.AvailableContexts.HasFlag(ServerSymbolContextKind.Player));
        Assert.True(context.AvailableContexts.HasFlag(ServerSymbolContextKind.Server));
    }

    [Fact]
    public void 真实NPC页面一次渲染多个常量并保留按钮中文和客户端变量()
    {
        PlayerObject player = CreatePlayer();
        const string pageText = "你好 <$USERNAME>，<$JOB> <$LEVEL>级，位于<$MAPTITLE>(<$X>,<$Y>)。武器<$WEAPON>，余额<$GAMEGOLD>。<下一页/@NEXT> $$GAMEGOLD";
        var segment = new NPCSegment(
            new NPCPage("[@MAIN]"), new List<string> { pageText }, new List<string>(),
            new List<string>(), new List<string>(), new List<string>());

        Assert.True(segment.Check(player));
        string result = Assert.Single(player.NPCSpeech);

        Assert.Equal("你好 兼容测试员，Warrior 35级，位于测试地图(123,456)。武器测试剑，余额888。<下一页/@NEXT> $$GAMEGOLD", result);
    }

    [Theory]
    [InlineData("CLASS", "Warrior")]
    [InlineData("MAPNAME", "测试地图")]
    [InlineData("X_COORD", "123")]
    [InlineData("Y_COORD", "456")]
    [InlineData("CREDIT", "99")]
    [InlineData("ARMOUR", "空")]
    [InlineData("USERNAME", "兼容测试员")]
    [InlineData("LEVEL", "35")]
    [InlineData("MAP", "TEST01")]
    [InlineData("HP", "321")]
    [InlineData("MAXHP", "500")]
    [InlineData("MP", "123")]
    [InlineData("MAXMP", "250")]
    [InlineData("GAMEGOLD", "888")]
    [InlineData("PKPOINT", "8")]
    [InlineData("GUILDNAME", "未入行会")]
    [InlineData("WEAPON", "测试剑")]
    [InlineData("HELMET", "空")]
    [InlineData("NECKLACE", "空")]
    [InlineData("RING_L", "空")]
    [InlineData("RING_R", "空")]
    [InlineData("BRACELET_L", "空")]
    [InlineData("BRACELET_R", "空")]
    [InlineData("BELT", "空")]
    [InlineData("BOOTS", "空")]
    [InlineData("AMULET", "空")]
    [InlineData("STONE", "空")]
    [InlineData("TORCH", "空")]
    public void 旧名称通过别名保持原有结果(string legacyName, string expected)
    {
        PlayerObject player = CreatePlayer();
        var segment = new NPCSegment(
            new NPCPage("[@MAIN]"), new List<string>(), new List<string>(),
            new List<string>(), new List<string>(), new List<string>());

        Assert.Equal(expected, segment.ReplaceValue(player, $"<${legacyName}>"));
    }

    [Fact]
    public void 当前模型缺失的货币时装盾牌和假人计数使用显式兼容值()
    {
        PlayerObject player = CreatePlayer();

        ScriptTextRenderResult result = LingFengP0ServerSymbols.Render(
            player,
            "<$GAMEPOINT>/<$GAMEDIAMOND>/<$GAMEGLORY>/<$FASHIONDRESS>/<$SHIELD>/<$DUMMYCOUNT>");

        Assert.Equal("0/0/0/空/空/0", result.Text);
        Assert.Equal(ScriptTextRenderStatus.CompletedWithDiagnostics, result.Status);
        Assert.Equal(6, result.Diagnostics.Count);
        Assert.All(result.Diagnostics, diagnostic =>
            Assert.Equal(ServerSymbolStatus.CompatibilitySubstitute, diagnostic.SymbolStatus));
    }

    [Fact]
    public void ScriptApi与NPC入口共享同一P0渲染结果()
    {
        PlayerObject player = CreatePlayer();
        var api = new ScriptApi();

        Assert.Equal("兼容测试员/35/测试地图", api.ResolveLegacyToken(
            player,
            "<$USERNAME>/<$LEVEL>/<$MAPTITLE>"));
    }

    [Fact]
    public void NPC条件与动作参数都通过统一P0渲染入口()
    {
        PlayerObject player = CreatePlayer();
        var segment = new NPCSegment(
            new NPCPage("[@MAIN]"), new List<string>(), new List<string>(),
            new List<string>(), new List<string>(), new List<string>());
        segment.ParseCheck("CHECKLEVEL == <$LEVEL>");
        segment.ParseAct(segment.ActList, "GIVEGOLD <$LEVEL>");

        NPCActions action = Assert.Single(segment.ActList);
        Assert.Equal(ActionType.GiveGold, action.Type);
        Assert.Equal("<$LEVEL>", Assert.Single(action.Params));
        Assert.Equal("35", segment.ReplaceValue(player, "<$LEVEL>"));
        Assert.True(segment.Check(player));
        Assert.Equal(923u, player.Account.Gold);
    }

    [Fact]
    public void P0接入不截断旧变量与旧专用分支()
    {
        PlayerObject player = CreatePlayer();
        player.NPCData["NPCRollResult"] = 17;
        var segment = new NPCSegment(
            new NPCPage("[@MAIN]"), new List<string>(), new List<string>(),
            new List<string>(), new List<string>(), new List<string>());

        Assert.Equal("17", segment.ReplaceValue(player, "<$ROLLRESULT>"));
        Assert.Equal("原文", segment.ReplaceValue(player, "原文"));
    }

    [Fact]
    public void 非翎风模式保持旧DATE格式与旧分支路径()
    {
        PlayerObject player = CreatePlayer();
        Settings.TxtScriptsCompatibilityVersion = "";
        var segment = new NPCSegment(
            new NPCPage("[@MAIN]"), new List<string>(), new List<string>(),
            new List<string>(), new List<string>(), new List<string>());

        Assert.Equal(Envir.Main.Now.ToShortDateString(), segment.ReplaceValue(player, "<$DATE>"));
    }

    [Fact]
    public void 未登记常量在真实NPC入口保留原文但产生结构化调试诊断()
    {
        PlayerObject player = CreatePlayer();
        while (MessageQueue.Instance.DebugLog.TryDequeue(out _)) { }
        var segment = new NPCSegment(
            new NPCPage("[@MAIN]"), new List<string> { "未知：<$NOTREAL>" }, new List<string>(),
            new List<string>(), new List<string>(), new List<string>());

        Assert.True(segment.Check(player));

        Assert.Equal("未知：<$NOTREAL>", Assert.Single(player.NPCSpeech));
        Assert.Contains(MessageQueue.Instance.DebugLog, message =>
            message.Contains("Unsupported", StringComparison.Ordinal) &&
            message.Contains("NOTREAL", StringComparison.Ordinal));
    }

    [Fact]
    public void 真实NPC页面遇到非法引用时整行原子保留()
    {
        PlayerObject player = CreatePlayer();
        const string source = "<$USERNAME> <$1BAD>";
        var segment = new NPCSegment(
            new NPCPage("[@MAIN]"), new List<string> { source }, new List<string>(),
            new List<string>(), new List<string>(), new List<string>());

        Assert.True(segment.Check(player));

        Assert.Equal(source, Assert.Single(player.NPCSpeech));
    }

    [Fact]
    public void 人物快照异常时真实入口保留原文且不泄漏异常正文()
    {
        PlayerObject player = CreatePlayer();
        player.Info = null;
        const string source = "玩家：<$USERNAME>";
        var segment = new NPCSegment(
            new NPCPage("[@MAIN]"), new List<string> { source }, new List<string>(),
            new List<string>(), new List<string>(), new List<string>());

        Assert.True(segment.Check(player));

        Assert.Equal(source, Assert.Single(player.NPCSpeech));
    }

    private static PlayerObject CreatePlayer()
    {
        var info = new CharacterInfo
        {
            Name = "兼容测试员",
            Level = 35,
            Class = MirClass.Warrior,
            Gender = MirGender.Male,
            HP = 321,
            MP = 123,
            Experience = 4567,
            PKPoints = 8,
            CurrentLocation = new Point(123, 456)
        };
        info.Equipment[(int)EquipmentSlot.Weapon] = new UserItem(new ItemInfo { Name = "测试剑" });
        var player = new PlayerObject
        {
            Info = info,
            Account = new AccountInfo { Gold = 888, Credit = 99 },
            Stats = new Stats(),
            MaxExperience = 9999,
            CurrentMap = new Map(new MapInfo { FileName = "TEST01", Title = "测试地图" })
        };
        player.Stats[Stat.HP] = 500;
        player.Stats[Stat.MP] = 250;
        player.Stats[Stat.MinDC] = 11;
        player.Stats[Stat.MaxDC] = 22;
        return player;
    }
}
