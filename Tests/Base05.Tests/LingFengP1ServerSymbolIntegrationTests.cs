using Server;
using Server.MirDatabase;
using Server.MirEnvir;
using Server.MirObjects;
using Server.Scripting;
using Server.Scripting.ServerSymbols;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Text;
using Xunit;

namespace Base05.Tests;

[Collection(nameof(PhysicalTextFileProviderCollection))]
public sealed class LingFengP1ServerSymbolIntegrationTests : IDisposable
{
    private static readonly string[] SpecificationP1Names =
    {
        "KILLMONNAME", "KILLMONX", "KILLMONY", "GETEXP",
        "CURITEMNAME", "CURITEMPOS", "USEITEMNAME", "PICKDROPITEMNAME",
        "KILLER", "CURRRTARGETNAME", "DAMAGEVALUE", "STRUCKHP", "PKPOWER", "CURRRUSEMAGICID",
        "ATTACKMONSTER_NAME", "ATTACKMONSTER_NAMEEX", "ATTACKMONSTER_X", "ATTACKMONSTER_XEX",
        "ATTACKMONSTER_Y", "ATTACKMONSTER_YEX", "ATTACKMONSTER_HP", "ATTACKMONSTER_HPEX",
        "ATTACKMONSTER_MAXHP", "ATTACKMONSTER_MAXHPEX",
        "SCRIPTPARAM1", "SCRIPTPARAM2", "SCRIPTPARAM3", "SCRIPTPARAM4", "SCRIPTPARAM5",
        "SCRIPTPARAM6", "SCRIPTPARAM7", "SCRIPTPARAM8", "SCRIPTPARAM9",
        "GROUPMEMBERCOUNT", "TEAM0", "TEAM1", "TEAM2", "TEAM3", "TEAM4", "TEAM5", "TEAM6", "TEAM7", "TEAM8", "TEAM9",
        "RECALLREMAININGTIME", "KILLMONEXPRATE", "KILLMONEXPRATETIME", "KILLMONBURSTRATE", "KILLMONBURSTRATETIME",
        "POWERRATE", "POWERRATETIME", "ATTACKMONPOWERRATE", "ATTACKMONPOWERRATETIME"
    };

    private readonly string _previousCompatibilityVersion;
    private readonly bool _previousTxtEnabled;

    public LingFengP1ServerSymbolIntegrationTests()
    {
        _previousCompatibilityVersion = Settings.TxtScriptsCompatibilityVersion;
        _previousTxtEnabled = Settings.TxtScriptsEnabled;
        Settings.TxtScriptsCompatibilityVersion = "LFM2-2026.08";
        Settings.TxtScriptsEnabled = true;
    }

    public void Dispose()
    {
        Settings.TxtScriptsCompatibilityVersion = _previousCompatibilityVersion;
        Settings.TxtScriptsEnabled = _previousTxtEnabled;
    }

    [Fact]
    public void 运行时P1目录与阶段规格清单完全一致()
    {
        Assert.Equal(
            SpecificationP1Names.OrderBy(name => name, StringComparer.Ordinal),
            LingFengP0ServerSymbols.P1CanonicalNames.OrderBy(name => name, StringComparer.Ordinal));
    }

    [Fact]
    public void 击杀常量只在当前不可变事件作用域内可见且嵌套后恢复()
    {
        PlayerObject player = Player("击杀者");
        var outer = new LingFengMonsterKillEvent("外层怪物", 11, 22, 33);
        var inner = new LingFengMonsterKillEvent("内层怪物", 44, 55, 66);
        var segment = new NPCSegment(
            new NPCPage("[@MAIN]"), new List<string>(), new List<string>(),
            new List<string>(), new List<string>(), new List<string>());
        const string source = "<$KILLMONNAME>|<$KILLMONX>|<$KILLMONY>|<$GETEXP>";

        Assert.Equal(source, segment.ReplaceValue(player, source));
        using (LingFengTxtTriggerContext.Push(outer))
        {
            Assert.Equal("外层怪物|11|22|33", segment.ReplaceValue(player, source));
            using (LingFengTxtTriggerContext.Push(inner))
                Assert.Equal("内层怪物|44|55|66", segment.ReplaceValue(player, source));
            Assert.Equal("外层怪物|11|22|33", segment.ReplaceValue(player, source));
        }

        Assert.Equal(source, segment.ReplaceValue(player, source));
        Assert.Null(LingFengTxtTriggerContext.Current);
    }

    [Fact]
    public void 拾取物品名称只来自当前事件快照且事件外不可见()
    {
        PlayerObject player = Player("拾取者");
        var segment = new NPCSegment(
            new NPCPage("[@MAIN]"), new List<string>(), new List<string>(),
            new List<string>(), new List<string>(), new List<string>());
        const string source = "<$PICKDROPITEMNAME>|<$CURITEMNAME>|<$CURITEMPOS>";

        Assert.Equal(source, segment.ReplaceValue(player, source));
        using (LingFengTxtTriggerContext.Push(
                   new LingFengItemTriggerEvent(LingFengItemTriggerKind.Pickup, "测试戒指", null, 0)))
        {
            Assert.Equal("测试戒指|测试戒指|<$CURITEMPOS>", segment.ReplaceValue(player, source));
        }

        Assert.Equal(source, segment.ReplaceValue(player, source));
    }

    [Theory]
    [InlineData(PlayerDamagePerspective.Outgoing, "攻击目标", "攻击者", "攻击目标", "18", "<$STRUCKHP>")]
    [InlineData(PlayerDamagePerspective.Incoming, "受击者", "攻击者", "受击者", "<$PKPOWER>", "18")]
    public void 战斗结果常量按人物视角解析且作用域外不可见(
        PlayerDamagePerspective perspective,
        string currentTarget,
        string attacker,
        string target,
        string expectedPkPower,
        string expectedStruckHp)
    {
        PlayerObject player = Player("脚本人物");
        var segment = new NPCSegment(
            new NPCPage("[@MAIN]"), new List<string>(), new List<string>(),
            new List<string>(), new List<string>(), new List<string>());
        const string source = "<$CURRRTARGETNAME>|<$KILLER>|<$PKPOWER>|<$STRUCKHP>";
        var payload = new LingFengDamageEvent(perspective, attacker, target, currentTarget, 20, 18, true);

        Assert.Equal(source, segment.ReplaceValue(player, source));
        using (LingFengTxtTriggerContext.Push(payload))
            Assert.Equal($"{currentTarget}|{attacker}|{expectedPkPower}|{expectedStruckHp}",
                segment.ReplaceValue(player, source));
        Assert.Equal(source, segment.ReplaceValue(player, source));
    }

    [Fact]
    public void 攻击怪物常量来自当前目标快照且人物目标不冒充怪物()
    {
        PlayerObject player = Player("攻击者");
        var segment = new NPCSegment(
            new NPCPage("[@MAIN]"), new List<string>(), new List<string>(),
            new List<string>(), new List<string>(), new List<string>());
        const string source =
            "<$ATTACKMONSTER_NAME>|<$ATTACKMONSTER_NAMEEX>|<$ATTACKMONSTER_X>|<$ATTACKMONSTER_Y>|" +
            "<$ATTACKMONSTER_HP>|<$ATTACKMONSTER_MAXHP>|<$CURRRUSEMAGICID>";
        var monsterPayload = new LingFengDamageEvent(
            PlayerDamagePerspective.Outgoing, "攻击者", "赤月恶魔", "赤月恶魔", 25, 23, true,
            true, 101, 202, 303, 404, "26");

        using (LingFengTxtTriggerContext.Push(monsterPayload))
            Assert.Equal("赤月恶魔|赤月恶魔|101|202|303|404|26", segment.ReplaceValue(player, source));

        var playerPayload = new LingFengDamageEvent(
            PlayerDamagePerspective.Outgoing, "攻击者", "人物目标", "人物目标", 25, 23, true);
        using (LingFengTxtTriggerContext.Push(playerPayload))
            Assert.Equal(source.Replace("<$CURRRUSEMAGICID>", "0", StringComparison.Ordinal),
                segment.ReplaceValue(player, source));
    }

    [Fact]
    public void 队伍常量按稳定成员顺序快照且越界保留原文()
    {
        PlayerObject leader = Player("队长");
        PlayerObject member = Player("队员");
        leader.GroupMembers = new List<PlayerObject> { leader, member };
        var segment = new NPCSegment(
            new NPCPage("[@MAIN]"), new List<string>(), new List<string>(),
            new List<string>(), new List<string>(), new List<string>());

        Assert.Equal("队长|队员|<$TEAM2>",
            segment.ReplaceValue(leader, "<$TEAM0>|<$TEAM1>|<$TEAM2>"));
    }

    [Fact]
    public void 队伍人数召回倒计时和经验爆率读取当前人物只读状态()
    {
        PlayerObject player = Player("状态人物");
        player.GroupMembers = new List<PlayerObject> { player, Player("队员") };
        player.Stats[Stat.经验增长数率] = 100;
        player.Stats[Stat.物品掉落数率] = 200;
        var expBuff = (Buff)RuntimeHelpers.GetUninitializedObject(typeof(Buff));
        expBuff.Info = new BuffInfo { Type = BuffType.获取经验提升 };
        expBuff.ExpireTime = 2500;
        var dropBuff = (Buff)RuntimeHelpers.GetUninitializedObject(typeof(Buff));
        dropBuff.Info = new BuffInfo { Type = BuffType.物品掉落提升 };
        dropBuff.ExpireTime = 1500;
        player.Buffs.Add(expBuff);
        player.Buffs.Add(dropBuff);
        player.ActionList.Add(new DelayedAction(
            DelayedType.NPC, Envir.Main.Time + 2500, 1u, 2, "[@返回]", null, Point.Empty));
        var segment = new NPCSegment(
            new NPCPage("[@MAIN]"), new List<string>(), new List<string>(),
            new List<string>(), new List<string>(), new List<string>());

        Assert.Equal("2|3|2|3|3",
            segment.ReplaceValue(player,
                "<$GROUPMEMBERCOUNT>|<$RECALLREMAININGTIME>|<$KILLMONEXPRATE>|" +
                "<$KILLMONBURSTRATE>|<$KILLMONEXPRATETIME>"));
    }

    [Fact]
    public void 当前模型无独立攻人攻怪倍率时返回显式兼容基线并保留诊断()
    {
        PlayerObject player = Player("倍率人物");

        var result = Server.Scripting.ServerSymbols.LingFengP0ServerSymbols.Render(
            player,
            "<$POWERRATE>|<$POWERRATETIME>|<$ATTACKMONPOWERRATE>|<$ATTACKMONPOWERRATETIME>");

        Assert.Equal("1|0|1|0", result.Text);
        Assert.Equal(ScriptTextRenderStatus.CompletedWithDiagnostics, result.Status);
        Assert.Equal(4, result.Diagnostics.Count);
        Assert.All(result.Diagnostics, diagnostic =>
            Assert.Equal(ServerSymbolStatus.CompatibilitySubstitute, diagnostic.SymbolStatus));
    }

    [Fact]
    public void 使用物品真实延迟链只在对应脚本页暴露物品快照()
    {
        bool oldTxtEnabled = Settings.TxtScriptsEnabled;
        bool oldCSharpEnabled = Settings.CSharpScriptsEnabled;
        string oldTxtPath = Settings.TxtScriptsPath;
        TxtScriptLayout oldLayout = Settings.TxtScriptsLayout;
        NPCScript oldDefaultNpc = Envir.Main.DefaultNPC;
        string root = Path.Combine(Path.GetTempPath(), "lyo-lfenv06-useitem-" + Guid.NewGuid().ToString("N"));
        NPCScript script = null;

        try
        {
            Directory.CreateDirectory(Path.Combine(root, "NPCs"));
            File.WriteAllText(
                Path.Combine(root, "NPCs", "00Default.txt"),
                "[@_USEITEM(77)]\n#SAY\n<$USEITEMNAME>|<$CURITEMNAME>|<$CURITEMPOS>",
                new UTF8Encoding(false));
            Settings.CSharpScriptsEnabled = false;
            Settings.TxtScriptsPath = root;
            Settings.TxtScriptsLayout = TxtScriptLayout.LyoCrystal;
            Envir.Main.ApplyPhysicalTextFileDefinitions();
            script = NPCScript.GetOrAdd(uint.MaxValue - 606, "00Default", NPCScriptType.AutoPlayer);
            Envir.Main.DefaultNPC = script;

            PlayerObject player = Player("物品使用者");
            player.Report = new Reporting(player);
            const int inventoryIndex = 3;
            var item = new UserItem(new ItemInfo
            {
                Index = 606,
                Name = "脚本令牌",
                Type = ItemType.特殊消耗品,
                Shape = 77
            })
            {
                UniqueID = 60606,
                Count = 1
            };
            player.Info.Inventory[inventoryIndex] = item;

            player.UseItem(item.UniqueID);
            DelayedAction action = Assert.Single(player.ActionList,
                candidate => candidate.Type == DelayedType.NPC);
            player.ActionList.Remove(action);
            player.Process(action);

            Assert.Equal("脚本令牌|脚本令牌|3", Assert.Single(player.NPCSpeech));
            Assert.Null(LingFengTxtTriggerContext.Current);
            var segment = new NPCSegment(
                new NPCPage("[@MAIN]"), new List<string>(), new List<string>(),
                new List<string>(), new List<string>(), new List<string>());
            Assert.Equal("<$USEITEMNAME>|<$CURITEMNAME>|<$CURITEMPOS>",
                segment.ReplaceValue(player, "<$USEITEMNAME>|<$CURITEMNAME>|<$CURITEMPOS>"));
        }
        finally
        {
            if (script != null) Envir.Main.Scripts.Remove(script.ScriptID);
            Envir.Main.DefaultNPC = oldDefaultNpc;
            Settings.TxtScriptsEnabled = false;
            Envir.Main.ApplyPhysicalTextFileDefinitions();
            Settings.CSharpScriptsEnabled = oldCSharpEnabled;
            Settings.TxtScriptsPath = oldTxtPath;
            Settings.TxtScriptsLayout = oldLayout;
            Settings.TxtScriptsEnabled = oldTxtEnabled;
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void 参数化NPC页面按当前页参数解析且调用结束后不可见()
    {
        bool oldTxtEnabled = Settings.TxtScriptsEnabled;
        bool oldCSharpEnabled = Settings.CSharpScriptsEnabled;
        string oldTxtPath = Settings.TxtScriptsPath;
        TxtScriptLayout oldLayout = Settings.TxtScriptsLayout;
        string root = Path.Combine(Path.GetTempPath(), "lyo-lfenv06-params-" + Guid.NewGuid().ToString("N"));
        NPCScript script = null;

        try
        {
            Directory.CreateDirectory(Path.Combine(root, "NPCs"));
            File.WriteAllText(
                Path.Combine(root, "NPCs", "参数页.txt"),
                "[@MAIN]\n#SAY\n<进入/@参数页(甲,乙,3)>\n" +
                "[@参数页()]\n#SAY\n<$SCRIPTPARAM1>|<$SCRIPTPARAM2>|<$SCRIPTPARAM3>|<$SCRIPTPARAM4>",
                new UTF8Encoding(false));
            Settings.TxtScriptsPath = root;
            Settings.TxtScriptsLayout = TxtScriptLayout.LyoCrystal;
            Settings.CSharpScriptsEnabled = false;
            Envir.Main.ApplyPhysicalTextFileDefinitions();
            script = NPCScript.GetOrAdd(uint.MaxValue - 607, "参数页", NPCScriptType.Normal);

            PlayerObject player = Player("参数调用者");
            player.NPCDelayed = true;
            Assert.True(script.NPCPages.Any(page => page.Args.Count > 0),
                string.Join(";", script.NPCPages.Select(page => $"{page.Key}[{string.Join(",", page.Args)}]")));
            NPCPage parameterPage = Assert.Single(script.NPCPages, page => page.Args.Count > 0);
            script.Call(player, script.LoadedObjectID, parameterPage.Key);

            Assert.Equal("甲|乙|3|<$SCRIPTPARAM4>", Assert.Single(player.NPCSpeech));
            Assert.Null(LingFengTxtTriggerContext.Current);
            var segment = new NPCSegment(
                new NPCPage("[@MAIN]"), new List<string>(), new List<string>(),
                new List<string>(), new List<string>(), new List<string>());
            Assert.Equal("<$SCRIPTPARAM1>", segment.ReplaceValue(player, "<$SCRIPTPARAM1>"));
        }
        finally
        {
            if (script != null) Envir.Main.Scripts.Remove(script.ScriptID);
            Settings.TxtScriptsEnabled = false;
            Envir.Main.ApplyPhysicalTextFileDefinitions();
            Settings.TxtScriptsPath = oldTxtPath;
            Settings.TxtScriptsLayout = oldLayout;
            Settings.CSharpScriptsEnabled = oldCSharpEnabled;
            Settings.TxtScriptsEnabled = oldTxtEnabled;
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void 脚本参数与嵌套事件组合后分别恢复且不串号()
    {
        PlayerObject player = Player("嵌套调用者");
        var segment = new NPCSegment(
            new NPCPage("[@MAIN]"), new List<string>(), new List<string>(),
            new List<string>(), new List<string>(), new List<string>());
        const string source = "<$SCRIPTPARAM1>|<$KILLMONNAME>";

        using (LingFengTxtTriggerContext.PushScriptParameters(new[] { "外层参数" }))
        {
            IList<string> immutableSnapshot = Assert.IsAssignableFrom<IList<string>>(
                LingFengTxtTriggerContext.Current.ScriptParameters);
            Assert.Throws<NotSupportedException>(() => immutableSnapshot[0] = "污染参数");
            Assert.Equal("外层参数|<$KILLMONNAME>", segment.ReplaceValue(player, source));
            using (LingFengTxtTriggerContext.Push(new LingFengMonsterKillEvent("内层怪物", 1, 2, 3)))
                Assert.Equal("外层参数|内层怪物", segment.ReplaceValue(player, source));
            Assert.Equal("外层参数|<$KILLMONNAME>", segment.ReplaceValue(player, source));
        }

        Assert.Equal(source, segment.ReplaceValue(player, source));
        Assert.Null(LingFengTxtTriggerContext.Current);
    }

    private static PlayerObject Player(string name) => new()
    {
        Info = new CharacterInfo { Name = name, Level = 1 },
        Account = new AccountInfo(),
        Stats = new Stats()
    };

}
