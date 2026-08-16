using Server;
using Server.MirDatabase;
using Server.MirEnvir;
using Server.MirObjects;
using Server.Scripting;
using Server.Scripting.ServerSymbols;
using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;

namespace Base05.Tests;

[Collection(nameof(PhysicalTextFileProviderCollection))]
public sealed class LingFengP3ServerSymbolIntegrationTests : IDisposable
{
    private static readonly string[] SpecificationP3Names =
    {
        "CASTLEGOLD", "CASTLENAME", "CASTLEWARDATE", "GUILDMASTER1", "GUILDMASTER2",
        "GUILDWARFEE", "LISTOFWAR", "OWNERGUILD", "REQUESTBUILDGUILDITEM"
    };

    private readonly string _previousCompatibilityVersion = Settings.TxtScriptsCompatibilityVersion;
    private readonly bool _previousTxtEnabled = Settings.TxtScriptsEnabled;
    private readonly uint _previousGuildWarCost = Settings.Guild_WarCost;
    private readonly GuildItemVolume[] _previousCreationCosts = Settings.Guild_CreationCostList.ToArray();
    private readonly List<GuildObject> _createdGuilds = new();
    private readonly List<ConquestObject> _createdConquests = new();
    private readonly List<NPCObject> _createdNpcs = new();

    public LingFengP3ServerSymbolIntegrationTests()
    {
        Settings.TxtScriptsCompatibilityVersion = "LFM2-2026.08";
        Settings.TxtScriptsEnabled = true;
    }

    public void Dispose()
    {
        foreach (NPCObject npc in _createdNpcs) Envir.Main.NPCs.Remove(npc);
        foreach (ConquestObject conquest in _createdConquests) Envir.Main.Conquests.Remove(conquest);
        foreach (GuildObject guild in _createdGuilds) Envir.Main.Guilds.Remove(guild);
        Settings.Guild_CreationCostList.Clear();
        Settings.Guild_CreationCostList.AddRange(_previousCreationCosts);
        Settings.Guild_WarCost = _previousGuildWarCost;
        Settings.TxtScriptsCompatibilityVersion = _previousCompatibilityVersion;
        Settings.TxtScriptsEnabled = _previousTxtEnabled;
    }

    [Fact]
    public void 运行时P3目录只登记当前有等价领域来源的常量()
    {
        Assert.Equal(
            SpecificationP3Names.OrderBy(name => name, StringComparer.Ordinal),
            LingFengP0ServerSymbols.P3CanonicalNames.OrderBy(name => name, StringComparer.Ordinal));
    }

    [Fact]
    public void 行会常量从当前行会和公开配置只读快照解析()
    {
        PlayerObject player = Player("成员甲");
        GuildObject guild = Guild("测试行会", "会长甲", "副会长乙");
        player.MyGuild = guild;
        player.MyGuildRank = guild.Ranks[1];
        Settings.Guild_WarCost = 4567;
        Settings.Guild_CreationCostList.Clear();
        Settings.Guild_CreationCostList.Add(new GuildItemVolume { ItemName = "沃玛号角", Amount = 1 });
        Settings.Guild_CreationCostList.Add(new GuildItemVolume { ItemName = "金条", Amount = 2 });

        const string source =
            "<$GUILDMASTER1>|<$GUILDMASTER2>|<$GUILDWARFEE>|<$REQUESTBUILDGUILDITEM>";

        Assert.Equal("会长甲|副会长乙|4567|沃玛号角*1,金条*2", Segment().ReplaceValue(player, source));
    }

    [Fact]
    public void 城堡常量使用确定城堡并区分占领者与攻城申请列表()
    {
        PlayerObject player = Player("城主管理员");
        GuildObject owner = Guild("沙城行会", "沙城主");
        GuildObject attacker = Guild("攻城行会", "攻城主");
        ConquestObject castle = Conquest(2, "沙巴克", owner, 8765, 0, new DateTime(2026, 8, 16, 20, 0, 0));
        ConquestObject second = Conquest(3, "苍月城", attacker, 4321, attacker.Guildindex, default);
        owner.Conquest = castle;
        player.MyGuild = attacker;
        player.MyGuildRank = attacker.Ranks[0];

        const string source =
            "<$CASTLENAME>|<$OWNERGUILD>|<$CASTLEGOLD>|<$CASTLEWARDATE>|<$LISTOFWAR>";

        Assert.Equal(
            "沙巴克|沙城行会|8765|2026-08-16 20:00:00|苍月城",
            Segment().ReplaceValue(player, source));
        ScriptTextRenderResult rendered = LingFengP0ServerSymbols.Render(player, source);
        Assert.Contains(rendered.Diagnostics, diagnostic =>
            diagnostic.CanonicalName == "CASTLEWARDATE" &&
            diagnostic.SymbolStatus == ServerSymbolStatus.CompatibilitySubstitute);
        Assert.Contains(rendered.Diagnostics, diagnostic =>
            diagnostic.CanonicalName == "LISTOFWAR" &&
            diagnostic.SymbolStatus == ServerSymbolStatus.CompatibilitySubstitute);

        NPCObject npc = Npc(second);
        player.NPCObjectID = npc.ObjectID;
        Assert.Equal(
            "苍月城|攻城行会|4321|<$CASTLEWARDATE>|苍月城",
            Segment().ReplaceValue(player, source));

        player.NPCObjectID = 0;
        attacker.Conquest = second;
        Assert.Equal(
            "苍月城|攻城行会|4321|<$CASTLEWARDATE>|苍月城",
            Segment().ReplaceValue(player, source));
    }

    [Fact]
    public void 无行会无城堡时失败路径不泄露其他玩家行会信息()
    {
        PlayerObject player = Player("散人");

        Assert.Equal(
            "<$GUILDMASTER1>|<$GUILDMASTER2>|3000|无|<$CASTLENAME>|<$OWNERGUILD>",
            Segment().ReplaceValue(
                player,
                "<$GUILDMASTER1>|<$GUILDMASTER2>|<$GUILDWARFEE>|<$REQUESTBUILDGUILDITEM>|<$CASTLENAME>|<$OWNERGUILD>"));
    }

    [Fact]
    public void 真实TXT与CSharpNPC页面及ScriptApi均使用本次调用上下文而非残留会话()
    {
        PlayerObject player = Player("跨城访客");
        GuildObject firstOwner = Guild("旧城行会", "旧城主");
        GuildObject secondOwner = Guild("新城行会", "新城主");
        ConquestObject first = Conquest(21, "旧城", firstOwner, 21, 0, default);
        ConquestObject second = Conquest(22, "新城", secondOwner, 22, 0, default);
        NPCObject firstNpc = Npc(first);
        NPCObject secondNpc = Npc(second);
        string suffix = Guid.NewGuid().ToString("N");
        NPCScript txtScript = null;
        NPCScript csharpScript = null;
        NPCScript plainCsharpScript = null;
        bool oldCSharpSetting = Settings.CSharpScriptsEnabled;
        ScriptManager globalManager = Envir.Main.CSharpScripts;
        bool oldManagerEnabled = globalManager.Enabled;
        FieldInfo registryField = typeof(ScriptManager).GetField(
            "_currentRegistry", BindingFlags.Instance | BindingFlags.NonPublic)!;
        ScriptRegistry oldRegistry = globalManager.CurrentRegistry;

        try
        {
            Settings.CSharpScriptsEnabled = false;
            txtScript = NPCScript.GetOrAdd(secondNpc.ObjectID, "LFENV08-TXT-" + suffix, NPCScriptType.Normal);
            var txtPage = new NPCPage(NPCScript.MainKey);
            txtPage.SegmentList.Add(new NPCSegment(
                txtPage, new List<string> { "<$CASTLENAME>|<$OWNERGUILD>|<$CASTLEGOLD>" },
                new List<string>(), new List<string>(), new List<string>(), new List<string>()));
            txtScript.NPCPages.Add(txtPage);
            player.NPCObjectID = firstNpc.ObjectID;

            txtScript.Call(player, secondNpc.ObjectID, NPCScript.MainKey);

            Assert.Equal(new[] { "新城|新城行会|22" }, player.NPCSpeech);

            using var isolatedRegistryOwner = new ScriptManager();
            registryField.SetValue(globalManager, isolatedRegistryOwner.CurrentRegistry);
            typeof(ScriptManager).GetProperty(nameof(ScriptManager.Enabled))!
                .SetValue(globalManager, true);
            Settings.CSharpScriptsEnabled = true;
            string csharpFile = "LFENV08-CSHARP-" + suffix;
            new NpcRegistry(globalManager.CurrentRegistry).RegisterPage(
                csharpFile, NPCScript.MainKey,
                (_, _, _, dialog) =>
                {
                    dialog.Say("<$CASTLENAME>|<$OWNERGUILD>|<$CASTLEGOLD>");
                    return true;
                });
            csharpScript = NPCScript.GetOrAdd(secondNpc.ObjectID, csharpFile, NPCScriptType.Normal);
            player.NPCObjectID = firstNpc.ObjectID;

            csharpScript.Call(player, secondNpc.ObjectID, NPCScript.MainKey);

            Assert.Equal(new[] { "新城|新城行会|22" }, player.NPCSpeech);

            NPCObject plainNpc = Npc(null);
            secondOwner.Conquest = second;
            player.MyGuild = secondOwner;
            player.MyGuildRank = secondOwner.Ranks[0];
            string plainCsharpFile = "LFENV08-PLAIN-CSHARP-" + suffix;
            new NpcRegistry(globalManager.CurrentRegistry).RegisterPage(
                plainCsharpFile, NPCScript.MainKey,
                (_, _, _, dialog) =>
                {
                    dialog.Say("<$CASTLENAME>|<$OWNERGUILD>|<$CASTLEGOLD>");
                    return true;
                });
            plainCsharpScript = NPCScript.GetOrAdd(
                plainNpc.ObjectID, plainCsharpFile, NPCScriptType.Normal);
            player.NPCObjectID = firstNpc.ObjectID;

            plainCsharpScript.Call(player, plainNpc.ObjectID, NPCScript.MainKey);

            Assert.Equal(new[] { "新城|新城行会|22" }, player.NPCSpeech);
            var call = new NpcPageCall(
                plainCsharpFile, plainNpc.ObjectID, plainCsharpScript.ScriptID,
                NPCScript.MainKey, NPCScript.MainKey, Array.Empty<string>(), string.Empty);
            player.NPCObjectID = firstNpc.ObjectID;
            Assert.Equal(
                "新城|新城行会|22",
                new ScriptApi().ResolveLegacyToken(
                    player, call, "<$CASTLENAME>|<$OWNERGUILD>|<$CASTLEGOLD>"));
        }
        finally
        {
            if (txtScript != null) Envir.Main.Scripts.Remove(txtScript.ScriptID);
            if (csharpScript != null) Envir.Main.Scripts.Remove(csharpScript.ScriptID);
            if (plainCsharpScript != null) Envir.Main.Scripts.Remove(plainCsharpScript.ScriptID);
            registryField.SetValue(globalManager, oldRegistry);
            typeof(ScriptManager).GetProperty(nameof(ScriptManager.Enabled))!
                .SetValue(globalManager, oldManagerEnabled);
            Settings.CSharpScriptsEnabled = oldCSharpSetting;
        }
    }

    private GuildObject Guild(string name, params string[] masters)
    {
        int index = 9100 + _createdGuilds.Count;
        var masterRank = new GuildRank { Name = "会长", Index = 0 };
        foreach (string master in masters)
            masterRank.Members.Add(new GuildMember { Name = master, Id = index });
        var memberRank = new GuildRank { Name = "会员", Index = 1 };
        var guild = new GuildObject(new GuildInfo
        {
            GuildIndex = index,
            Name = name,
            Ranks = new List<GuildRank> { masterRank, memberRank },
            Membercount = masters.Length
        });
        _createdGuilds.Add(guild);
        return guild;
    }

    private ConquestObject Conquest(
        int index,
        string name,
        GuildObject owner,
        uint gold,
        int attackerId,
        DateTime warStart)
    {
        var conquest = new ConquestObject(new ConquestGuildInfo
        {
            Info = new ConquestInfo { Index = index, Name = name },
            Owner = owner?.Guildindex ?? 0,
            GoldStorage = gold,
            AttackerID = attackerId
        })
        {
            Guild = owner,
            WarStartTime = warStart
        };
        Envir.Main.Conquests.Add(conquest);
        _createdConquests.Add(conquest);
        return conquest;
    }

    private NPCObject Npc(ConquestObject conquest)
    {
        var npc = (NPCObject)RuntimeHelpers.GetUninitializedObject(typeof(NPCObject));
        typeof(MapObject).GetField(nameof(MapObject.ObjectID))!
            .SetValue(npc, uint.MaxValue - (uint)_createdNpcs.Count);
        npc.Info = new NPCInfo { Index = 9800 + _createdNpcs.Count };
        npc.UsedGoods = new List<UserItem>();
        npc.Conq = conquest;
        Envir.Main.NPCs.Add(npc);
        _createdNpcs.Add(npc);
        return npc;
    }

    private static NPCSegment Segment() => new(
        new NPCPage("[@MAIN]"), new List<string>(), new List<string>(),
        new List<string>(), new List<string>(), new List<string>());

    private static PlayerObject Player(string name) => new TestPlayer
    {
        Info = new CharacterInfo { Name = name, Level = 1 },
        Account = new AccountInfo(),
        Stats = new Stats()
    };

    private sealed class TestPlayer : PlayerObject
    {
        public override void Enqueue(Packet packet) { }
        public override void Broadcast(Packet packet) { }
    }
}
