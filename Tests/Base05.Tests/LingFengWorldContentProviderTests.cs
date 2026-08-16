using System.Drawing;
using System.Text;
using Server;
using Server.MirDatabase;
using Server.MirEnvir;
using Server.MirObjects;
using Server.Persistence.Sql;
using Server.Scripting;
using Xunit;

namespace Base05.Tests;

[Collection(nameof(PhysicalTextFileProviderCollection))]
public sealed class LingFengWorldContentProviderTests
{
    [Fact]
    public void CandidateBuildsMapAliasesMovementsRespawnsAndMapQuestReferences()
    {
        TextFileDefinition mapInfo = Definition("World/MapInfo", "MapInfo.txt",
            "[逻辑一|M101 第一地图] NORECONNECT(逻辑二) NORECALL NORANDOMMOVE NODRUG NOPOSITIONMOVE NOTHROWITEM DARK FIGHT",
            "[逻辑二|M102 第二地图] SAFE",
            "逻辑一 10,11 -> 逻辑二 20,21");
        TextFileDefinition mongen = Definition("World/Mongen", "Mongen.txt",
            "逻辑一 30 31 测试怪 5 2 60 7 251 9");
        TextFileDefinition mapQuest = Definition("World/MapQuest", "MapQuest.txt",
            "逻辑一 [401] 1 测试怪 * 击杀任务");
        TextFileDefinition quest = Definition("MapQuests/击杀任务", "MapQuest_def/击杀任务.txt",
            "[@MAIN]", "#ACT", "GIVEGOLD 1");

        Assert.True(LingFengWorldContentProvider.TryCreate(
            mapInfo, mongen, mapQuest,
            new Dictionary<string, TextFileDefinition>(StringComparer.Ordinal)
            {
                [quest.Key] = quest
            },
            out LingFengWorldContentProvider provider,
            out IReadOnlyList<string> errors), string.Join(Environment.NewLine, errors));

        var first = new MapInfo { Index = 101, FileName = "M101", Title = "旧标题" };
        var second = new MapInfo { Index = 102, FileName = "M102", Title = "旧标题二" };
        var monster = new MonsterInfo { Index = 201, Name = "测试怪" };
        Assert.True(provider.TryBuildPlan([first, second], [monster], out LingFengWorldContentPlan plan,
            out errors), string.Join(Environment.NewLine, errors));

        plan.Commit();

        Assert.Equal("第一地图", first.Title);
        Assert.Equal("逻辑一", first.LingFengAlias);
        Assert.True(first.NoReconnect);
        Assert.Equal("逻辑二", first.NoReconnectMap);
        Assert.True(first.NoRecall);
        Assert.True(first.NoRandom);
        Assert.True(first.NoDrug);
        Assert.True(first.NoPosition);
        Assert.True(first.NoThrowItem);
        Assert.True(first.Fight);
        Assert.Equal(LightSetting.Night, first.Light);
        MovementInfo movement = Assert.Single(first.Movements);
        Assert.Equal(new Point(10, 11), movement.Source);
        Assert.Equal(102, movement.MapIndex);
        Assert.Equal(new Point(20, 21), movement.Destination);
        RespawnInfo respawn = Assert.Single(first.Respawns);
        Assert.Equal(201, respawn.MonsterIndex);
        Assert.Equal(new Point(30, 31), respawn.Location);
        Assert.Equal((ushort)5, respawn.Spread);
        Assert.Equal((ushort)2, respawn.Count);
        Assert.Equal((ushort)60, respawn.Delay);
        Assert.Equal((ushort)7, respawn.RandomDelay);
        Assert.Equal((byte)251, respawn.Direction);
        Assert.Equal((ushort)9, respawn.RespawnTicks);
        Assert.Equal("逻辑二", first.LingFengOptions["NORECONNECT"]);
        Assert.Single(provider.MapQuests);
        Assert.Throws<InvalidOperationException>(plan.Commit);
    }

    [Theory]
    [InlineData("[逻辑一|M101 第一地图] UNKNOWNOPTION", "LFENV12-MAP-OPTION")]
    [InlineData("逻辑一 10,11 -> 缺失地图 20,21", "LFENV12-MAP-DEPENDENCY")]
    public void InvalidMapCandidateFailsClosed(string mapLine, string expectedCode)
    {
        TextFileDefinition mapInfo = expectedCode == "LFENV12-MAP-OPTION"
            ? Definition("World/MapInfo", "MapInfo.txt", mapLine)
            : Definition("World/MapInfo", "MapInfo.txt", "[逻辑一|M101 第一地图]", mapLine);
        bool created = LingFengWorldContentProvider.TryCreate(mapInfo, null, null,
            new Dictionary<string, TextFileDefinition>(StringComparer.Ordinal),
            out LingFengWorldContentProvider provider, out IReadOnlyList<string> parseErrors);
        if (expectedCode == "LFENV12-MAP-OPTION")
        {
            Assert.False(created);
            Assert.Contains(parseErrors, error => error.Contains(expectedCode, StringComparison.Ordinal));
            return;
        }
        Assert.True(created, string.Join(Environment.NewLine, parseErrors));
        var map = new MapInfo { Index = 101, FileName = "M101" };

        Assert.False(provider.TryBuildPlan([map], [], out _, out IReadOnlyList<string> errors));
        Assert.Contains(errors, error => error.Contains(expectedCode, StringComparison.Ordinal));
        Assert.Equal("M101", map.FileName);
        Assert.Empty(map.Movements);
    }

    [Fact]
    public void MissingMonsterAndMapQuestPageRejectPlanWithoutPartialMutation()
    {
        TextFileDefinition mapInfo = Definition("World/MapInfo", "MapInfo.txt",
            "[逻辑一|M101 新标题]");
        TextFileDefinition mongen = Definition("World/Mongen", "Mongen.txt",
            "逻辑一 1 2 缺失怪 3 1 10 0 1");
        TextFileDefinition mapQuest = Definition("World/MapQuest", "MapQuest.txt",
            "逻辑一 [1] 1 缺失怪 * 缺页");
        Assert.False(LingFengWorldContentProvider.TryCreate(mapInfo, mongen, mapQuest,
            new Dictionary<string, TextFileDefinition>(StringComparer.Ordinal),
            out _, out IReadOnlyList<string> errors));
        Assert.Contains(errors, error => error.Contains("LFENV12-MAPQUEST-PAGE", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("逻辑一 -1 2 测试怪 0 1 60 0 0", "", "LFENV12-MONGEN-SYNTAX")]
    [InlineData("", "逻辑一 [1999] 1 测试怪 * 击杀任务", "LFENV12-MAPQUEST-SYNTAX")]
    public void InvalidCoordinatesAndFlagIndicesFailCandidate(
        string mongenLine, string mapQuestLine, string expectedCode)
    {
        TextFileDefinition quest = Definition("MapQuests/击杀任务", "MapQuest_def/击杀任务.txt", "[@MAIN]");
        Assert.False(LingFengWorldContentProvider.TryCreate(
            Definition("World/MapInfo", "MapInfo.txt", "[逻辑一|M101 第一地图]"),
            mongenLine.Length == 0 ? null : Definition("World/Mongen", "Mongen.txt", mongenLine),
            mapQuestLine.Length == 0 ? null : Definition("World/MapQuest", "MapQuest.txt", mapQuestLine),
            new Dictionary<string, TextFileDefinition>(StringComparer.Ordinal) { [quest.Key] = quest },
            out _, out IReadOnlyList<string> errors));
        Assert.Contains(errors, error => error.Contains(expectedCode, StringComparison.Ordinal));
    }

    [Fact]
    public void DanglingReconnectAndInsufficientPhysicalInstancesFailPlan()
    {
        TextFileDefinition dangling = Definition("World/MapInfo", "MapInfo.txt",
            "[A|M101 A] NORECONNECT(缺图)");
        Assert.True(LingFengWorldContentProvider.TryCreate(dangling, null, null,
            new Dictionary<string, TextFileDefinition>(), out LingFengWorldContentProvider provider,
            out IReadOnlyList<string> errors), string.Join(Environment.NewLine, errors));
        Assert.False(provider.TryBuildPlan([new MapInfo { Index = 1, FileName = "M101" }], [], out _, out errors));
        Assert.Contains(errors, error => error.Contains("NORECONNECT", StringComparison.Ordinal));

        TextFileDefinition aliases = Definition("World/MapInfo", "MapInfo.txt", "[A|M101 A]", "[B|M101 B]");
        Assert.True(LingFengWorldContentProvider.TryCreate(aliases, null, null,
            new Dictionary<string, TextFileDefinition>(), out provider, out errors));
        Assert.False(provider.TryBuildPlan([new MapInfo { Index = 1, FileName = "M101" }], [], out _, out errors));
        Assert.Contains(errors, error => error.Contains("逻辑别名数", StringComparison.Ordinal));
        var first = new MapInfo { Index = 1, FileName = "M101" };
        var second = new MapInfo { Index = 2, FileName = "M101" };
        Assert.True(provider.TryBuildPlan([first, second], [], out LingFengWorldContentPlan plan, out errors),
            string.Join(Environment.NewLine, errors));
        plan.Commit();
        Assert.Equal("A", first.LingFengAlias);
        Assert.Equal("B", second.LingFengAlias);
    }

    [Fact]
    public void SemanticDuplicateMapQuestRulesAreRejectedCaseInsensitively()
    {
        TextFileDefinition page = Definition("MapQuests/击杀任务", "MapQuest_def/击杀任务.txt", "[@MAIN]");
        Assert.False(LingFengWorldContentProvider.TryCreate(null, null,
            Definition("World/MapQuest", "MapQuest.txt",
                "A [1] 1 Monster * 击杀任务",
                "a [1] 1 monster * 击杀任务"),
            new Dictionary<string, TextFileDefinition> { [page.Key] = page },
            out _, out IReadOnlyList<string> errors));
        Assert.Contains(errors, error => error.Contains("LFENV12-MAPQUEST-DUPLICATE", StringComparison.Ordinal));
    }

    [Fact]
    public void MapQuestMatchesMapFlagAndMonsterDeterministically()
    {
        TextFileDefinition mapQuest = Definition("World/MapQuest", "MapQuest.txt",
            "逻辑一 [401] 1 测试怪 * 击杀任务");
        TextFileDefinition quest = Definition("MapQuests/击杀任务", "MapQuest_def/击杀任务.txt",
            "[@MAIN]", "#ACT");
        Assert.True(LingFengWorldContentProvider.TryCreate(null, null, mapQuest,
            new Dictionary<string, TextFileDefinition>(StringComparer.Ordinal) { [quest.Key] = quest },
            out LingFengWorldContentProvider provider, out IReadOnlyList<string> errors),
            string.Join(Environment.NewLine, errors));
        var player = new PlayerObject { Info = new CharacterInfo { Name = "任务人物" }, Account = new AccountInfo() };
        var monster = new TestMonster(new MonsterInfo { Name = "测试怪" })
        {
            CurrentMap = new Map(new MapInfo { FileName = "M101", LingFengAlias = "逻辑一" })
        };

        player.Info.Flags[401] = true;
        Assert.Equal("mapquests/击杀任务", Assert.Single(provider.MatchMapQuests(monster, player)).ScriptKey);
        player.Info.Flags[401] = false;
        Assert.Empty(provider.MatchMapQuests(monster, player));
    }

    [Fact]
    public void PhysicalProviderPublishesWorldCandidateAndOnlyReferencedMapQuestPages()
    {
        string root = TempRoot();
        try
        {
            Write(root, "MapInfo.txt", "[逻辑一|M101 第一地图] DARK\r\n");
            Write(root, "Mongen.txt", "逻辑一 0 0 测试怪 0 1 60 0 0\r\n");
            Write(root, "MapQuest.txt", "逻辑一 [1] 1 测试怪 * 任务\\击杀\r\n");
            Write(root, "MapQuest_def/任务/击杀.txt", "[@MAIN]\r\n#ACT\r\nGIVEGOLD 1\r\n");
            Write(root, "MapQuest_def/未引用.txt", "这不是脚本页面\r\n");

            var physical = new PhysicalTextFileProvider(
                new PhysicalTextFileProviderOptions(root, TxtScriptLayout.LingFeng));

            Assert.NotNull(physical.WorldContentProvider);
            Assert.NotNull(physical.GetByKey("MapQuests/任务/击杀"));
            Assert.Null(physical.GetByKey("MapQuests/未引用"));
            Assert.Null(physical.GetByKey("World/MapInfo"));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void LingFengAliasParticipatesInRealMapLookupAndRespawnCreatesMonster()
    {
        Envir envir = Envir.Main;
        var info = new MapInfo { Index = 901201, FileName = "M901", LingFengAlias = "别名地图" };
        var map = TestMap(info);
        var destination = TestMap(new MapInfo { Index = 901207, FileName = "M902", LingFengAlias = "目标地图" });
        info.Movements.Add(new MovementInfo { Source = Point.Empty, MapIndex = destination.Info.Index, Destination = Point.Empty });
        var monsterInfo = new MonsterInfo { Index = 901202, Name = "刷怪探针" };
        monsterInfo.Stats[Stat.HP] = 10;
        int oldMonsterCount = envir.MonsterCount;
        bool oldMultithreaded = Settings.Multithreaded;
        NPCScript oldDefaultNpc = envir.DefaultNPC;
        NPCScript defaultNpc = NPCScript.GetOrAdd(uint.MaxValue - 1201, "LFENV12-Default", NPCScriptType.AutoPlayer);
        envir.DefaultNPC = defaultNpc;
        envir.MapList.Add(map);
        envir.MapList.Add(destination);
        envir.MonsterInfoList.Add(monsterInfo);
        var player = new TestPlayer
        {
            Info = new CharacterInfo { Name = "地图切换人物" },
            Account = new AccountInfo(),
            Stats = new Stats(),
            CurrentMap = map,
            CurrentLocation = Point.Empty
        };
        try
        {
            Settings.Multithreaded = false;
            Assert.Same(map, envir.GetMapByNameAndInstance("别名地图"));
            map.AddObject(player);
            Assert.True(player.CheckMovement(Point.Empty));
            Assert.Same(destination, player.CurrentMap);
            Assert.Equal(Point.Empty, player.CurrentLocation);
            var respawn = new MapRespawn(new RespawnInfo
            {
                MonsterIndex = monsterInfo.Index,
                Location = Point.Empty,
                Count = 1,
                Delay = 60
            })
            {
                Map = map,
                WalkableCells = [Point.Empty]
            };

            Assert.True(respawn.Spawn());
            MonsterObject spawned = Assert.Single(map.GetCell(Point.Empty).Objects.OfType<MonsterObject>());
            Assert.Same(monsterInfo, spawned.Info);
            Assert.Same(map, spawned.CurrentMap);
            spawned.Despawn();
        }
        finally
        {
            envir.MapList.Remove(map);
            destination.RemoveObject(player);
            envir.MapList.Remove(destination);
            envir.MonsterInfoList.Remove(monsterInfo);
            envir.MonsterCount = oldMonsterCount;
            Settings.Multithreaded = oldMultithreaded;
            envir.DefaultNPC = oldDefaultNpc;
            envir.Scripts.Remove(defaultNpc.ScriptID);
        }
    }

    [Fact]
    public void MonsterDeathDispatchesMatchingMapQuestPageOnceThroughPhysicalRuntime()
    {
        bool oldTxtEnabled = Settings.TxtScriptsEnabled;
        bool oldCSharpEnabled = Settings.CSharpScriptsEnabled;
        string oldTxtPath = Settings.TxtScriptsPath;
        TxtScriptLayout oldLayout = Settings.TxtScriptsLayout;
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        string root = TempRoot();
        int oldMonsterCount = Envir.Main.MonsterCount;
        try
        {
            Write(root, "MapInfo.txt", "[逻辑一|M101 第一地图]\r\n");
            Write(root, "Mongen.txt", string.Empty);
            Write(root, "MapQuest.txt", "M101 [401] 1 测试怪 * 击杀任务\r\n");
            Write(root, "MapQuest_def/击杀任务.txt", "[@MAIN]\r\n#ACT\r\nGIVEGOLD 1\r\n");

            Settings.CSharpScriptsEnabled = false;
            Settings.TxtScriptsEnabled = true;
            Settings.TxtScriptsPath = root;
            Settings.TxtScriptsLayout = TxtScriptLayout.LingFeng;
            Settings.TxtScriptsCompatibilityVersion = "LFM2-LFENV12";
            Envir.Main.ApplyPhysicalTextFileDefinitions();

            var player = new PlayerObject
            {
                Info = new CharacterInfo { Name = "地图任务人物" },
                Account = new AccountInfo(),
                Stats = new Stats()
            };
            player.Info.Flags[401] = true;
            Map map = TestMap(new MapInfo { Index = 901203, FileName = "M101", LingFengAlias = "逻辑一" });
            var monster = new TestMonster(new MonsterInfo { Index = 901204, Name = "测试怪", Experience = 0 })
            {
                CurrentMap = map,
                CurrentLocation = Point.Empty,
                EXPOwner = player,
                HP = 1
            };

            monster.Die();

            Assert.Equal(1u, player.Account.Gold);
            Assert.True(monster.Dead);
        }
        finally
        {
            Settings.CSharpScriptsEnabled = oldCSharpEnabled;
            Settings.TxtScriptsPath = oldTxtPath;
            Settings.TxtScriptsLayout = oldLayout;
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
            Settings.TxtScriptsEnabled = oldTxtEnabled;
            Envir.Main.ApplyPhysicalTextFileDefinitions();
            Envir.Main.MonsterCount = oldMonsterCount;
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void ColdStartProductionSeamAtomicallyAppliesWorldPlanBeforeMapCreation()
    {
        bool oldTxtEnabled = Settings.TxtScriptsEnabled;
        string oldTxtPath = Settings.TxtScriptsPath;
        TxtScriptLayout oldLayout = Settings.TxtScriptsLayout;
        string root = TempRoot();
        var envir = new Envir();
        try
        {
            Write(root, "MapInfo.txt",
                "[逻辑一|M101 新标题] NORECONNECT(逻辑一) DARK\r\n");
            Write(root, "Mongen.txt", "逻辑一 0 0 冷启动怪 0 1 60 0 0\r\n");
            Settings.TxtScriptsEnabled = true;
            Settings.TxtScriptsPath = root;
            Settings.TxtScriptsLayout = TxtScriptLayout.LingFeng;
            envir.ApplyPhysicalTextFileDefinitions();
            var map = new MapInfo { Index = 901205, FileName = "M101", Title = "旧标题" };
            var monster = new MonsterInfo { Index = 901206, Name = "冷启动怪" };
            envir.MapInfoList.Add(map);
            envir.MonsterInfoList.Add(monster);

            envir.ApplyPhysicalWorldContentForColdStart();

            Assert.Equal("逻辑一", map.LingFengAlias);
            Assert.Equal("新标题", map.Title);
            Assert.True(map.NoReconnect);
            Assert.Equal(LightSetting.Night, map.Light);
            Assert.Equal(monster.Index, Assert.Single(map.Respawns).MonsterIndex);
            MapInfo persistence = map.GetPersistenceView();
            Assert.Equal("旧标题", persistence.Title);
            Assert.False(persistence.NoReconnect);
            Assert.Empty(persistence.Respawns);
            Assert.Equal("旧标题", Assert.Single(SqlWorldRelationsStore.Capture(envir).MapInfos).Title);
            Assert.Empty(SqlWorldRelationsStore.Capture(envir).MapRespawns);

            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true)) map.Save(writer);
            stream.Position = 0;
            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
            Assert.Equal(map.Index, reader.ReadInt32());
            Assert.Equal("M101", reader.ReadString());
            Assert.Equal("旧标题", reader.ReadString());
        }
        finally
        {
            Settings.TxtScriptsEnabled = false;
            envir.ApplyPhysicalTextFileDefinitions();
            Settings.TxtScriptsPath = oldTxtPath;
            Settings.TxtScriptsLayout = oldLayout;
            Settings.TxtScriptsEnabled = oldTxtEnabled;
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void InvalidWorldReloadRetainsPreviousCompleteProvider()
    {
        bool oldTxtEnabled = Settings.TxtScriptsEnabled;
        string oldTxtPath = Settings.TxtScriptsPath;
        TxtScriptLayout oldLayout = Settings.TxtScriptsLayout;
        string root = TempRoot();
        var envir = new Envir();
        try
        {
            Write(root, "MapInfo.txt", "[逻辑一|M101 第一地图] DARK\r\n");
            Write(root, "Mongen.txt", string.Empty);
            Settings.TxtScriptsEnabled = true;
            Settings.TxtScriptsPath = root;
            Settings.TxtScriptsLayout = TxtScriptLayout.LingFeng;
            envir.ApplyPhysicalTextFileDefinitions();
            LingFengWorldContentProvider published = envir.PhysicalWorldContentProvider;
            Assert.NotNull(published);

            Write(root, "MapInfo.txt", "[逻辑一|M101 第一地图] UNKNOWNOPTION\r\n");

            InvalidDataException error = Assert.Throws<InvalidDataException>(
                envir.ApplyPhysicalTextFileDefinitions);
            Assert.Contains("LFENV12-MAP-OPTION", error.Message, StringComparison.Ordinal);
            Assert.Same(published, envir.PhysicalWorldContentProvider);
        }
        finally
        {
            Settings.TxtScriptsEnabled = false;
            envir.ApplyPhysicalTextFileDefinitions();
            Settings.TxtScriptsPath = oldTxtPath;
            Settings.TxtScriptsLayout = oldLayout;
            Settings.TxtScriptsEnabled = oldTxtEnabled;
            Directory.Delete(root, true);
        }
    }

    private static TextFileDefinition Definition(string key, string sourcePath, params string[] lines) =>
        new TextFileDefinition(key, sourcePath, "CP936", "CRLF").AddLines(lines);

    private static string TempRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "lyo-lfenv12-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void Write(string root, string relativePath, string text)
    {
        string file = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, text, new UTF8Encoding(false));
    }

    private static Map TestMap(MapInfo info)
    {
        var map = new Map(info)
        {
            Width = 1,
            Height = 1,
            Cells = new Cell[1, 1]
        };
        map.Cells[0, 0] = new Cell { Attribute = CellAttribute.Walk };
        return map;
    }

    private sealed class TestMonster(MonsterInfo info) : MonsterObject(info);

    private sealed class TestPlayer : PlayerObject
    {
        public override void Enqueue(Packet packet) { }
        public override void Broadcast(Packet packet) { }
    }
}
