extern alias ShareProtocol;

using Microsoft.Data.Sqlite;
using Server;
using Server.MirDatabase;
using Server.MirEnvir;
using Server.MirNetwork;
using Server.MirObjects;
using Server.Persistence;
using Server.Persistence.Sql;
using Server.Scripting;
using Server.Scripting.Variables;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Shared;
using Xunit;
using AndroidServerPackets = ShareProtocol::ServerPackets;

namespace Base05.Tests;

[Collection(nameof(PhysicalTextFileProviderCollection))]
public sealed class LingFengCompleteSliceTests
{
    [Fact]
    public void 封神原版法宝收录扣币回收登记并在重启后保留()
    {
        string sourceRoot = @"D:\ChuanQi\服务端\封神\MirServer_法宝玩法精简提取包\MirServer\Mir200\Envir";
        string sourceDatabase = @"D:\ChuanQi\服务端\封神\MirServer\Mud2\DB\ApexM2.DB";
        if (!Directory.Exists(sourceRoot) || !File.Exists(sourceDatabase))
            throw Xunit.Sdk.SkipException.ForSkip("本机未挂载封神原版法宝语料或数据库。");

        string sourceDigest = ComputeDirectoryDigest(sourceRoot);
        string root = Path.Combine(Path.GetTempPath(), $"lfenv16-original-treasure-{Guid.NewGuid():N}");
        string databasePath = Path.Combine(Path.GetTempPath(), $"lfenv16-original-treasure-{Guid.NewGuid():N}.db");
        bool oldTxtEnabled = Settings.TxtScriptsEnabled;
        bool oldCSharpEnabled = Settings.CSharpScriptsEnabled;
        bool oldStrict = Settings.TxtScriptsStrictCompatibility;
        string oldTxtPath = Settings.TxtScriptsPath;
        TxtScriptLayout oldLayout = Settings.TxtScriptsLayout;
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        LingFengDependencyLevel oldLevel = Settings.TxtScriptsDependencyLevel;
        ItemInfo[] oldItems = Envir.Main.ItemInfoList.ToArray();
        string[] oldMessages = MessageQueue.Instance.MessageLog.ToArray();
        string[] oldDebugMessages = MessageQueue.Instance.DebugLog.ToArray();
        NPCScript script = null;
        try
        {
            CopyOriginalFile(sourceRoot, root,
                @"QuestDiary\【2功能脚本】\法宝收录.txt");
            CopyOriginalFile(sourceRoot, root,
                @"QuestDiary\【3表格脚本】\法宝图鉴.csv");

            Settings.CSharpScriptsEnabled = false;
            Settings.TxtScriptsEnabled = true;
            Settings.TxtScriptsPath = root;
            Settings.TxtScriptsLayout = TxtScriptLayout.LingFeng;
            Settings.TxtScriptsStrictCompatibility = true;
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            Settings.TxtScriptsDependencyLevel = LingFengDependencyLevel.None;
            Envir.Main.ApplyPhysicalTextFileDefinitions();

            TextFileDefinition definition = Assert.Single(
                Envir.Main.TextFileProvider.GetAll(), value =>
                    value.SourcePath.EndsWith("法宝收录.txt", StringComparison.OrdinalIgnoreCase));
            script = NPCScript.GetOrAdd(0, definition.Key, NPCScriptType.Called);

            ItemInfo treasure = LoadLegacyItem(sourceDatabase, "【法宝】乾坤圈");
            Envir.Main.ItemInfoList.Add(treasure);
            var account = new AccountInfo { Index = 916170, AccountID = "lfenv16-original-treasure" };
            var character = new CharacterInfo
            {
                Index = 916171,
                Name = "封神原版法宝人物",
                AccountInfo = account
            };
            account.Characters.Add(character);
            character.Inventory[0] = new UserItem(treasure) { Count = 1 };
            character.LingFengProgress.SetGameGird(200);
            var player = new TestPlayer
            {
                Info = character,
                Account = account,
                Stats = new Stats(),
                CurrentMap = new Map(new MapInfo
                {
                    Index = 916172,
                    FileName = "LFENV16-ORIGINAL-TREASURE"
                }),
                NPCDelayed = true
            };
            character.Mount = new MountInfo(player);
            player.Report = new Reporting(player);

            Assert.True(script.CallSystem(
                player, "[@法宝开始收录](1,人品,13,13)"));
            NPCPage collectionPage = Assert.Single(script.NPCPages, page =>
                page.Key.StartsWith("[@法宝开始收录]", StringComparison.OrdinalIgnoreCase));
            var rowResult = Envir.Main.CSharpScripts.VariableCommands.Format(
                ScriptVariableContext.ForPlayer(player), "N$收录法宝位置");
            Assert.True(rowResult.Success);
            Assert.Equal("13", rowResult.Text);
            bool directCellFound = Envir.Main.PhysicalCsvContentProvider.TryGetCell(
                "法宝图鉴", 13, 13, out string directCell);
            Assert.True(directCellFound);
            Assert.Equal("200", directCell);
            string literalCostCell = collectionPage.SegmentList[1].ReplaceValue(
                player, "<$法宝图鉴(13,13)>");
            Assert.Equal("200", literalCostCell);
            string costCell = collectionPage.SegmentList[1].ReplaceValue(
                player, "<$法宝图鉴(<$Str(N$收录法宝位置)>,13)>");
            Assert.Equal("200", costCell);
            Assert.True(character.Flags[403]);
            Assert.Equal(0, character.LingFengProgress.GameGird);
            Assert.DoesNotContain(character.Inventory, item => item?.Info == treasure);

            var persistence = new SqlServerPersistence(DatabaseProviderKind.Sqlite,
                new SqlDatabaseOptions { SqlitePath = databasePath });
            var source = new Envir();
            source.ItemInfoList.Add(treasure);
            source.AccountList.Add(account);
            source.CharacterList.Add(character);
            persistence.SaveAccounts(source);
            ((IPendingSaveCoordinator)persistence).DrainPendingSaves();

            var restarted = new Envir();
            restarted.ItemInfoList.Add(LoadLegacyItem(sourceDatabase, treasure.Name));
            persistence.LoadAccounts(restarted);
            CharacterInfo restored = Assert.Single(restarted.CharacterList);
            Assert.True(restored.Flags[403]);
            Assert.Equal(0, restored.LingFengProgress.GameGird);
            Assert.DoesNotContain(restored.Inventory, item => item?.Info.Name == treasure.Name);
            Assert.Equal(sourceDigest, ComputeDirectoryDigest(sourceRoot));
        }
        finally
        {
            if (script != null) Envir.Main.Scripts.Remove(script.ScriptID);
            Settings.CSharpScriptsEnabled = oldCSharpEnabled;
            Settings.TxtScriptsEnabled = oldTxtEnabled;
            Settings.TxtScriptsPath = oldTxtPath;
            Settings.TxtScriptsLayout = oldLayout;
            Settings.TxtScriptsStrictCompatibility = oldStrict;
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
            Settings.TxtScriptsDependencyLevel = oldLevel;
            Envir.Main.ItemInfoList.Clear();
            Envir.Main.ItemInfoList.AddRange(oldItems);
            Envir.Main.ApplyPhysicalTextFileDefinitions();
            RestoreQueue(MessageQueue.Instance.MessageLog, oldMessages);
            RestoreQueue(MessageQueue.Instance.DebugLog, oldDebugMessages);
            if (Directory.Exists(root)) Directory.Delete(root, true);
            TryDelete(databasePath);
        }
    }

    [Fact]
    public void 法宝玩法从登录到击杀提交保存重启形成可核账闭环()
    {
        string sourceDatabase = @"D:\ChuanQi\服务端\封神\MirServer\Mud2\DB\ApexM2.DB";
        string sourceMap = @"D:\ChuanQi\服务端\封神\MirServer\Mir200\Map\新龙城.map";
        if (!File.Exists(sourceDatabase) || !File.Exists(sourceMap))
            throw Xunit.Sdk.SkipException.ForSkip("本机未挂载封神法宝数据库或新龙城地图资源。");

        ItemInfo questItem = LoadLegacyItem(sourceDatabase, "【法宝】乾坤圈");
        ItemInfo rewardItem = LoadLegacyItem(sourceDatabase, "【法宝】九龙神火罩");
        MonsterInfo monsterInfo = LoadLegacyMonster(sourceDatabase, "试炼守卫");
        var mapInfo = new MapInfo { Index = 916160, FileName = "新龙城", Title = "迁移前标题" };
        PhysicalTextFileProvider provider = new(new PhysicalTextFileProviderOptions(
            SliceRoot(), TxtScriptLayout.LingFeng));
        LingFengDependencyReport report = provider.ExternalDependencyManifest.Evaluate(
            LingFengDependencyLevel.E2,
            new LingFengDependencyProbe(
                name => name == questItem.Name || name == rewardItem.Name,
                index => index == questItem.Index || index == rewardItem.Index,
                name => name == monsterInfo.Name,
                name => provider.WorldContentProvider?.DefinesMapReference(name) == true ||
                        name == mapInfo.FileName,
                key => key == "Maps/新龙城.map" && File.Exists(sourceMap),
                _ => false));
        Assert.True(report.Success, string.Join(Environment.NewLine,
            report.Missing.Select(value => $"{value.Kind}:{value.Key}:{value.SourceKey}")));

        bool oldTxtEnabled = Settings.TxtScriptsEnabled;
        bool oldCSharpEnabled = Settings.CSharpScriptsEnabled;
        bool oldFallback = Settings.CSharpScriptsFallbackToTxt;
        bool oldStrict = Settings.TxtScriptsStrictCompatibility;
        string oldTxtPath = Settings.TxtScriptsPath;
        TxtScriptLayout oldLayout = Settings.TxtScriptsLayout;
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        LingFengDependencyLevel oldLevel = Settings.TxtScriptsDependencyLevel;
        string oldContracts = Settings.TxtScriptsClientContracts;
        float oldDropRate = Settings.DropRate;
        NPCScript oldDefaultNpc = Envir.Main.DefaultNPC;
        NPCScript npc = null;
        NPCScript qManage = null;
        NPCScript qFunction = null;
        string databasePath = Path.Combine(Path.GetTempPath(), $"lfenv16-{Guid.NewGuid():N}.db");
        try
        {
            Envir.Main.ItemInfoList.Add(questItem);
            Envir.Main.ItemInfoList.Add(rewardItem);
            Envir.Main.MonsterInfoList.Add(monsterInfo);
            Envir.Main.MapInfoList.Add(mapInfo);
            Settings.CSharpScriptsEnabled = false;
            Settings.CSharpScriptsFallbackToTxt = true;
            Settings.TxtScriptsEnabled = true;
            Settings.TxtScriptsPath = SliceRoot();
            Settings.TxtScriptsLayout = TxtScriptLayout.LingFeng;
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            Settings.TxtScriptsStrictCompatibility = true;
            Settings.TxtScriptsDependencyLevel = LingFengDependencyLevel.E2;
            Settings.TxtScriptsClientContracts = "Maps/新龙城.map";
            Settings.DropRate = 1;

            Envir.Main.ApplyPhysicalTextFileDefinitions();
            Assert.NotNull(Envir.Main.TextFileProvider?.GetByKey("NPCs/法宝收录使者"));
            Envir.Main.ApplyPhysicalWorldContentForColdStart();
            Envir.Main.ReloadDrops();
            Assert.Equal("法宝试炼", mapInfo.Title);
            Assert.Equal("法宝试炼", mapInfo.LingFengAlias);
            Assert.Single(mapInfo.Respawns);
            Assert.NotEmpty(monsterInfo.Drops);

            npc = NPCScript.GetOrAdd(916161, "法宝收录使者", NPCScriptType.Normal);
            NPCPage mainPage = Assert.Single(npc.NPCPages, page => page.Key == NPCScript.MainKey);
            Assert.Equal(2, mainPage.SegmentList.Count);
            Assert.All(mainPage.SegmentList, segment => Assert.NotEmpty(segment.Say));
            qManage = NPCScript.GetOrAdd(0, "SystemScripts/QManage", NPCScriptType.Called);
            qFunction = NPCScript.GetOrAdd(0, "SystemScripts/QFunction-0", NPCScriptType.Called);
            Envir.Main.DefaultNPC = qManage;

            Map map = TestMap(mapInfo);
            var account = new AccountInfo { Index = 916162, AccountID = "lfenv16-account" };
            var character = new CharacterInfo
            {
                Index = 916163,
                Name = "法宝闭环人物",
                AccountInfo = account,
                Level = ushort.MaxValue,
                CurrentMapIndex = mapInfo.Index,
                CurrentLocation = Point.Empty,
                HP = 100
            };
            account.Characters.Add(character);
            var player = new TestPlayer
            {
                Info = character,
                Account = account,
                Stats = new Stats(),
                CurrentMap = map,
                CurrentLocation = Point.Empty,
                Node = new LinkedListNode<MapObject>(null!)
            };
            player.Node.Value = player;
            character.Mount = new MountInfo(player);
            player.Report = new Reporting(player);
            player.Stats[Stat.HP] = 100;
            MirConnection connection = (MirConnection)RuntimeHelpers.GetUninitializedObject(typeof(MirConnection));
            connection.SentItemInfo = [questItem, rewardItem];
            connection.SentHeroInfo = [];
            player.Connection = connection;

            player.CallDefaultNPC(DefaultNPCType.Login);
            Assert.Contains(player.Packets.OfType<ServerPackets.Chat>(), packet =>
                packet.Message == "法宝收录使者正在等待你的帮助。");

            npc.Call(player, 916161, NPCScript.MainKey);
            ServerPackets.NPCResponse pcDialog = Assert.Single(
                player.Packets.OfType<ServerPackets.NPCResponse>());
            Assert.Contains(pcDialog.Page,
                line => line.Contains("接受任务", StringComparison.Ordinal));
            var androidDialog = new AndroidServerPackets.NPCResponse { Page = [.. pcDialog.Page] };
            Assert.Equal(pcDialog.GetPacketBytes().ToArray(), androidDialog.GetPacketBytes().ToArray());
            npc.Call(player, 916161, "[@接受]");
            Assert.True(character.Flags[950]);

            var monster = new TestMonster(monsterInfo)
            {
                CurrentMap = map,
                CurrentLocation = Point.Empty,
                EXPOwner = player,
                HP = 1,
                Node = new LinkedListNode<MapObject>(null!)
            };
            monster.Node.Value = monster;
            monster.Stats[Stat.HP] = 1;
            map.GetCell(Point.Empty).Add(monster);
            monster.Die();
            Assert.True(character.Flags[951]);
            Assert.Contains(map.GetCell(Point.Empty).Objects,
                value => value is ItemObject item && item.Item.Info == questItem);
            player.PickUp();
            Assert.Contains(character.Inventory, item => item?.Info == questItem);

            npc.Call(player, 916161, NPCScript.MainKey);
            npc.Call(player, 916161, "[@提交]");
            Assert.True(character.Flags[950]);
            Assert.True(character.Flags[952]);
            Assert.DoesNotContain(character.Inventory, item => item?.Info == questItem);
            Assert.Contains(character.Inventory, item => item?.Info == rewardItem);
            Assert.Equal(200u, account.Gold);

            var persistence = new SqlServerPersistence(DatabaseProviderKind.Sqlite,
                new SqlDatabaseOptions { SqlitePath = databasePath });
            var source = new Envir();
            source.ItemInfoList.Add(questItem);
            source.ItemInfoList.Add(rewardItem);
            source.AccountList.Add(account);
            source.CharacterList.Add(character);
            persistence.SaveAccounts(source);
            ((IPendingSaveCoordinator)persistence).DrainPendingSaves();

            var restarted = new Envir();
            restarted.ItemInfoList.Add(LoadLegacyItem(sourceDatabase, questItem.Name));
            restarted.ItemInfoList.Add(LoadLegacyItem(sourceDatabase, rewardItem.Name));
            persistence.LoadAccounts(restarted);
            CharacterInfo restored = Assert.Single(restarted.CharacterList);
            Assert.True(restored.Flags[950]);
            Assert.True(restored.Flags[951]);
            Assert.True(restored.Flags[952]);
            Assert.Contains(restored.Inventory, item => item?.Info.Name == rewardItem.Name);
            Assert.DoesNotContain(restored.Inventory, item => item?.Info.Name == questItem.Name);
            Assert.Equal(200u, Assert.Single(restarted.AccountList).Gold);
        }
        finally
        {
            if (npc != null) Envir.Main.Scripts.Remove(npc.ScriptID);
            if (qManage != null) Envir.Main.Scripts.Remove(qManage.ScriptID);
            if (qFunction != null) Envir.Main.Scripts.Remove(qFunction.ScriptID);
            Envir.Main.DefaultNPC = oldDefaultNpc;
            Envir.Main.ItemInfoList.Remove(questItem);
            Envir.Main.ItemInfoList.Remove(rewardItem);
            Envir.Main.MonsterInfoList.Remove(monsterInfo);
            Envir.Main.MapInfoList.Remove(mapInfo);
            Settings.CSharpScriptsEnabled = oldCSharpEnabled;
            Settings.CSharpScriptsFallbackToTxt = oldFallback;
            Settings.TxtScriptsEnabled = oldTxtEnabled;
            Settings.TxtScriptsPath = oldTxtPath;
            Settings.TxtScriptsLayout = oldLayout;
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
            Settings.TxtScriptsStrictCompatibility = oldStrict;
            Settings.TxtScriptsDependencyLevel = oldLevel;
            Settings.TxtScriptsClientContracts = oldContracts;
            Settings.DropRate = oldDropRate;
            Envir.Main.ApplyPhysicalTextFileDefinitions();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            TryDelete(databasePath);
            TryDelete(databasePath + "-wal");
            TryDelete(databasePath + "-shm");
        }
    }

    [Fact]
    public void 法宝完整玩法候选通过严格预检且Setup与回滚对称()
    {
        string root = SliceRoot();
        bool oldStrict = Settings.TxtScriptsStrictCompatibility;
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        try
        {
            Settings.TxtScriptsStrictCompatibility = true;
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            var provider = new PhysicalTextFileProvider(
                new PhysicalTextFileProviderOptions(root, TxtScriptLayout.LingFeng));

            Assert.Empty(TxtScriptSnapshotValidator.Validate(provider));
            Assert.Contains(provider.ExternalDependencyManifest.Requirements,
                value => value.Kind == LingFengDependencyKind.ItemName &&
                         value.Key == "【法宝】乾坤圈");
            Assert.Contains(provider.ExternalDependencyManifest.Requirements,
                value => value.Kind == LingFengDependencyKind.ItemName &&
                         value.Key == "【法宝】九龙神火罩");
            Assert.Contains(provider.ExternalDependencyManifest.Requirements,
                value => value.Kind == LingFengDependencyKind.Monster && value.Key == "试炼守卫");
            Assert.Contains(provider.ExternalDependencyManifest.Requirements,
                value => value.Kind == LingFengDependencyKind.ClientContract &&
                         value.Key == "Maps/新龙城.map" && value.Level == LingFengDependencyLevel.E2);

            string configRoot = Directory.GetParent(root)!.FullName;
            string setup = File.ReadAllText(Path.Combine(configRoot, "Setup.fragment.ini"))
                .Replace("\r\n", "\n", StringComparison.Ordinal);
            string rollback = File.ReadAllText(Path.Combine(configRoot, "rollback.fragment.ini"))
                .Replace("\r\n", "\n", StringComparison.Ordinal);
            Assert.Contains("TxtScriptsDependencyLevel=E2", setup, StringComparison.Ordinal);
            Assert.Contains("CSharpScriptsFallbackToTxt=true", setup, StringComparison.Ordinal);
            Assert.Contains("TxtScriptsEnabled=false", rollback, StringComparison.Ordinal);
            Assert.Contains("TxtScriptsDependencyLevel=None", rollback, StringComparison.Ordinal);
            Assert.Contains("CSharpScriptsFallbackToTxt=false", rollback, StringComparison.Ordinal);
        }
        finally
        {
            Settings.TxtScriptsStrictCompatibility = oldStrict;
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 酷明原样Envir通过严格E1脚本预检()
    {
        AssertStrictSnapshot(
            @"D:\ChuanQi\服务端\01酷明传奇\MirServer_01\Mir200\Envir",
            "LFENV-ROOT-0002");
    }

    [Fact]
    public void 酷明原样机器人脚本通过真实运行时解析()
    {
        const string sourceRoot = @"D:\ChuanQi\服务端\01酷明传奇\MirServer_01\Mir200\Envir";
        if (!Directory.Exists(sourceRoot))
            throw Xunit.Sdk.SkipException.ForSkip("本机未挂载 01酷明原版 Envir。");

        bool oldTxtEnabled = Settings.TxtScriptsEnabled;
        bool oldCSharpEnabled = Settings.CSharpScriptsEnabled;
        bool oldStrict = Settings.TxtScriptsStrictCompatibility;
        string oldTxtPath = Settings.TxtScriptsPath;
        TxtScriptLayout oldLayout = Settings.TxtScriptsLayout;
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        LingFengDependencyLevel oldLevel = Settings.TxtScriptsDependencyLevel;
        NPCScript? robot = null;
        try
        {
            Settings.CSharpScriptsEnabled = false;
            Settings.TxtScriptsEnabled = true;
            Settings.TxtScriptsPath = sourceRoot;
            Settings.TxtScriptsLayout = TxtScriptLayout.LingFeng;
            Settings.TxtScriptsStrictCompatibility = true;
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            Settings.TxtScriptsDependencyLevel = LingFengDependencyLevel.None;
            Envir.Main.ApplyPhysicalTextFileDefinitions();

            robot = NPCScript.GetOrAdd(uint.MaxValue - 1616,
                Settings.RobotNPCFilename, NPCScriptType.Robot);
            Assert.NotEmpty(robot.NPCPages);
        }
        finally
        {
            if (robot != null) Envir.Main.Scripts.Remove(robot.ScriptID);
            Settings.CSharpScriptsEnabled = oldCSharpEnabled;
            Settings.TxtScriptsEnabled = oldTxtEnabled;
            Settings.TxtScriptsPath = oldTxtPath;
            Settings.TxtScriptsLayout = oldLayout;
            Settings.TxtScriptsStrictCompatibility = oldStrict;
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
            Settings.TxtScriptsDependencyLevel = oldLevel;
            Envir.Main.ApplyPhysicalTextFileDefinitions();
        }
    }

    [Fact]
    public void 酷明原样Envir真实资源缺口被E1审计准确报告()
    {
        const string sourceRoot = @"D:\ChuanQi\服务端\01酷明传奇\MirServer_01\Mir200\Envir";
        const string sourceMaps = @"D:\ChuanQi\服务端\01酷明传奇\MirServer_01\Mir200\MAP";
        const string sourceDatabase = @"D:\ChuanQi\服务端\01酷明传奇\MirServer_01\Mud2\DB\ApexM2.DB";
        if (!Directory.Exists(sourceRoot) || !Directory.Exists(sourceMaps) || !File.Exists(sourceDatabase))
            throw Xunit.Sdk.SkipException.ForSkip("本机未挂载 01酷明原版 Envir、地图或数据库。");

        var provider = new PhysicalTextFileProvider(
            new PhysicalTextFileProviderOptions(sourceRoot, TxtScriptLayout.LingFeng)
            {
                MaxFileBytes = 2 * 1024 * 1024
            });
        var items = new List<ItemInfo>();
        var monsters = new List<MonsterInfo>();
        LoadAllLegacyItems(sourceDatabase, items);
        LoadAllLegacyMonsters(sourceDatabase, monsters);
        var itemNames = items.Select(item => item.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var itemIndexes = items.Select(item => item.Index).ToHashSet();
        var monsterNames = monsters.Select(monster => monster.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var mapNames = Directory.EnumerateFiles(sourceMaps, "*.map", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileNameWithoutExtension)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        LingFengDependencyReport report = provider.ExternalDependencyManifest.Evaluate(
            LingFengDependencyLevel.E1,
            new LingFengDependencyProbe(
                itemNames.Contains,
                itemIndexes.Contains,
                monsterNames.Contains,
                mapNames.Contains,
                _ => false,
                _ => false));

        Assert.False(report.Success,
            "酷明原始资源已满足 E1；请将本阻断测试升级为真实生产冷启动验收。");
        Assert.NotEmpty(report.Missing);
        Assert.All(report.Missing, requirement =>
        {
            bool exists = requirement.Kind switch
            {
                LingFengDependencyKind.ItemName => itemNames.Contains(requirement.Key),
                LingFengDependencyKind.ItemIndex => int.TryParse(requirement.Key, out int index) &&
                                                    itemIndexes.Contains(index),
                LingFengDependencyKind.Monster => monsterNames.Contains(requirement.Key),
                LingFengDependencyKind.Map => mapNames.Contains(requirement.Key),
                _ => false
            };
            Assert.False(exists,
                $"审计误报：{requirement.Kind}:{requirement.Key}:{requirement.SourceKey}");
        });
    }

    [Fact]
    public void INI调用唯一全局跳转与缺失外部页分别进入真实执行和E2依赖()
    {
        string root = Path.Combine(Path.GetTempPath(), $"lfenv16-structure-{Guid.NewGuid():N}");
        bool oldTxtEnabled = Settings.TxtScriptsEnabled;
        bool oldCSharpEnabled = Settings.CSharpScriptsEnabled;
        bool oldStrict = Settings.TxtScriptsStrictCompatibility;
        string oldTxtPath = Settings.TxtScriptsPath;
        TxtScriptLayout oldLayout = Settings.TxtScriptsLayout;
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        LingFengDependencyLevel oldLevel = Settings.TxtScriptsDependencyLevel;
        var createdScripts = new List<int>();
        try
        {
            foreach (int staleScriptId in Envir.Main.Scripts.Values
                         .Where(script => script.FileName.Equals("入口", StringComparison.OrdinalIgnoreCase) ||
                                          script.FileName.Equals("questdiary/跨文件", StringComparison.OrdinalIgnoreCase))
                         .Select(script => script.ScriptID).ToArray())
                Envir.Main.Scripts.Remove(staleScriptId);
            WriteUtf8(root, "Market_Def/入口.txt",
                "[@MAIN]\r\n#ACT\r\nGOTO @跨文件页\r\n");
            WriteUtf8(root, "QuestDiary/跨文件.ini",
                "[@跨文件页]\r\n#ACT\r\nGIVEGOLD 7\r\n");
            WriteUtf8(root, "QuestDiary/外部回调.txt",
                "[@MAIN]\r\n#ACT\r\nGOTO @_@MONBUFF\r\n");
            WriteUtf8(root, "Npc_def/武馆教头-0137.txt",
                "[@WateUnMaster]\r\n第一段\r\n[@WateUnMaster]\r\n第二段\r\n");
            WriteUtf8(root, "Robot_def/AUTORUNROBOT.TXT",
                "#AutoRun Npc Runonday 22:02 @Mir2_沙城奖励Rm\r\n");
            WriteUtf8(root, "Robot_def/ROBOTMANAGE.TXT",
                "[@其他任务]\r\n#ACT\r\nBREAK\r\n");

            Settings.TxtScriptsStrictCompatibility = true;
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            var provider = new PhysicalTextFileProvider(
                new PhysicalTextFileProviderOptions(root, TxtScriptLayout.LingFeng));

            Assert.NotNull(provider.GetByKey("QuestDiary/跨文件"));
            Assert.Empty(TxtScriptSnapshotValidator.Validate(provider));
            Assert.Contains(provider.ExternalDependencyManifest.Requirements, value =>
                value.Level == LingFengDependencyLevel.E2 &&
                value.Kind == LingFengDependencyKind.DomainAdapter &&
                value.Key == "LingFeng/ExternalScriptPage/@_@MONBUFF");
            Assert.Contains(provider.ExternalDependencyManifest.Requirements, value =>
                value.Level == LingFengDependencyLevel.E2 &&
                value.Kind == LingFengDependencyKind.DomainAdapter &&
                value.Key == "LingFeng/RobotPage/@Mir2_沙城奖励Rm");

            Settings.CSharpScriptsEnabled = false;
            Settings.TxtScriptsEnabled = true;
            Settings.TxtScriptsPath = root;
            Settings.TxtScriptsLayout = TxtScriptLayout.LingFeng;
            Settings.TxtScriptsDependencyLevel = LingFengDependencyLevel.None;
            Envir.Main.ApplyPhysicalTextFileDefinitions();

            NPCScript source = NPCScript.GetOrAdd(0, "入口", NPCScriptType.Normal);
            createdScripts.Add(source.ScriptID);
            Assert.Equal(new[] { NPCScript.MainKey }, source.NPCPages.Select(page => page.Key));
            var account = new AccountInfo();
            var player = new TestPlayer
            {
                Info = new CharacterInfo { Name = "命格跨页人物" },
                Account = account,
                Stats = new Stats(),
                NPCDelayed = true
            };
            source.Call(player, 0, NPCScript.MainKey);
            Assert.Single(player.ActionList);

            player.Process(Assert.Single(player.ActionList));

            Assert.Contains(Envir.Main.Scripts.Values, script =>
                script.FileName.Equals("questdiary/跨文件", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(7u, account.Gold);
            createdScripts.AddRange(Envir.Main.Scripts.Values
                .Where(script => script.FileName.Equals(
                    "questdiary/跨文件", StringComparison.OrdinalIgnoreCase))
                .Select(script => script.ScriptID));
        }
        finally
        {
            foreach (int scriptId in createdScripts.Concat(Envir.Main.Scripts.Values
                         .Where(script => script.FileName.Equals("入口", StringComparison.OrdinalIgnoreCase) ||
                                          script.FileName.Equals("questdiary/跨文件", StringComparison.OrdinalIgnoreCase))
                         .Select(script => script.ScriptID)).Distinct().ToArray())
                Envir.Main.Scripts.Remove(scriptId);
            Settings.CSharpScriptsEnabled = oldCSharpEnabled;
            Settings.TxtScriptsEnabled = oldTxtEnabled;
            Settings.TxtScriptsPath = oldTxtPath;
            Settings.TxtScriptsLayout = oldLayout;
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
            Settings.TxtScriptsStrictCompatibility = oldStrict;
            Settings.TxtScriptsDependencyLevel = oldLevel;
            Envir.Main.ApplyPhysicalTextFileDefinitions();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void 酷明命格完整目录未知命令为零且跨玩法调用保留为外部依赖()
    {
        string source = @"D:\ChuanQi\服务端\01酷明传奇\MirServer_01\Mir200\Envir\QuestDiary\11命格系统";
        if (!Directory.Exists(source))
            throw Xunit.Sdk.SkipException.ForSkip("本机未挂载 01酷明命格原始脚本。");

        string root = Path.Combine(Path.GetTempPath(), $"lfenv16-fate-{Guid.NewGuid():N}");
        string target = Path.Combine(root, "QuestDiary", "11命格系统");
        Directory.CreateDirectory(target);
        try
        {
            foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                string destination = Path.Combine(target, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(file, destination);
            }
            bool oldStrict = Settings.TxtScriptsStrictCompatibility;
            string oldVersion = Settings.TxtScriptsCompatibilityVersion;
            try
            {
                Settings.TxtScriptsStrictCompatibility = true;
                Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
                var provider = new PhysicalTextFileProvider(
                    new PhysicalTextFileProviderOptions(root, TxtScriptLayout.LingFeng)
                    {
                        MaxFileBytes = 2 * 1024 * 1024
                    });
                IReadOnlyList<string> errors = TxtScriptSnapshotValidator.Validate(provider);

                Assert.DoesNotContain(errors,
                    error => error.StartsWith("TXT-SNAPSHOT-014", StringComparison.Ordinal));
                Assert.All(errors,
                    error => Assert.StartsWith("TXT-SNAPSHOT-004", error, StringComparison.Ordinal));
                Assert.Contains(errors,
                    error => error.Contains(@"\06属性刷新\01属性刷新", StringComparison.OrdinalIgnoreCase));
                Assert.Contains(errors,
                    error => error.Contains(@"\08锦囊系统\09临时属性", StringComparison.OrdinalIgnoreCase));
            }
            finally
            {
                Settings.TxtScriptsStrictCompatibility = oldStrict;
                Settings.TxtScriptsCompatibilityVersion = oldVersion;
            }
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void 封神法宝精简包通过严格脚本预检()
    {
        AssertStrictSnapshot(
            @"D:\ChuanQi\服务端\封神\MirServer_法宝玩法精简提取包\MirServer\Mir200\Envir",
            "LFENV-ROOT-0018");
    }

    private static void AssertStrictSnapshot(string root, string rootId)
    {
        if (!Directory.Exists(root))
            throw Xunit.Sdk.SkipException.ForSkip($"本机未挂载 {rootId} 代表语料。");

        string sourceDigest = ComputeDirectoryDigest(root);
        bool oldStrict = Settings.TxtScriptsStrictCompatibility;
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        try
        {
            Settings.TxtScriptsStrictCompatibility = true;
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            var provider = new PhysicalTextFileProvider(
                new PhysicalTextFileProviderOptions(root, TxtScriptLayout.LingFeng)
                {
                    MaxFileBytes = 2 * 1024 * 1024
                });

            IReadOnlyList<string> errors = TxtScriptSnapshotValidator.Validate(provider);
            Assert.True(errors.Count == 0,
                Summarize(errors) + Environment.NewLine +
                string.Join(Environment.NewLine, errors
                    .Where(error => error.StartsWith("TXT-SNAPSHOT-013", StringComparison.Ordinal))
                    .Take(20)) + Environment.NewLine +
                string.Join(Environment.NewLine, errors.Take(20)) + Environment.NewLine +
                $"{rootId} 严格预检错误总数：{errors.Count}");
            Assert.Equal(sourceDigest, ComputeDirectoryDigest(root));
        }
        finally
        {
            Settings.TxtScriptsStrictCompatibility = oldStrict;
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    private static void CopyOriginalFile(string sourceRoot, string targetRoot,
        string relativePath)
    {
        string source = Path.Combine(sourceRoot, relativePath);
        string target = Path.Combine(targetRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(source, target);
        Assert.Equal(SHA256.HashData(File.ReadAllBytes(source)),
            SHA256.HashData(File.ReadAllBytes(target)));
    }

    private static void LoadAllLegacyItems(string databasePath, ICollection<ItemInfo> target)
    {
        using var connection = new SqliteConnection($"Data Source={databasePath};Mode=ReadOnly;Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Idx, Name FROM StdItems WHERE Name <> '' ORDER BY Idx";
        using SqliteDataReader reader = command.ExecuteReader();
        var indexes = new HashSet<int>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read())
        {
            int index = reader.GetInt32(0);
            string name = reader.GetString(1).Trim();
            if (!indexes.Add(index) || !names.Add(name)) continue;
            target.Add(new ItemInfo
            {
                Index = index,
                Name = name,
                Type = ItemType.Nothing,
                StackSize = ushort.MaxValue,
                Durability = 1
            });
        }
    }

    private static void LoadAllLegacyMonsters(string databasePath, ICollection<MonsterInfo> target)
    {
        using var connection = new SqliteConnection($"Data Source={databasePath};Mode=ReadOnly;Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT rowid, Name FROM Monster WHERE Name <> '' ORDER BY rowid";
        using SqliteDataReader reader = command.ExecuteReader();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read())
        {
            string name = reader.GetString(1).Trim();
            if (!names.Add(name)) continue;
            var monster = new MonsterInfo
            {
                Index = checked((int)reader.GetInt64(0)),
                Name = name,
                Image = Monster.Guard,
                Level = 1,
                MoveSpeed = 1000,
                AttackSpeed = 1000
            };
            monster.Stats[Stat.HP] = 1;
            target.Add(monster);
        }
    }

    private static string ComputeDirectoryDigest(string root)
    {
        using IncrementalHash digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                     .OrderBy(path => Path.GetRelativePath(root, path)
                         .Replace('\\', '/'), StringComparer.Ordinal))
        {
            string relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            digest.AppendData(Encoding.UTF8.GetBytes(relative));
            digest.AppendData([0]);
            digest.AppendData(SHA256.HashData(File.ReadAllBytes(file)));
        }
        return Convert.ToHexString(digest.GetHashAndReset());
    }

    private static string Summarize(IEnumerable<string> errors)
    {
        return string.Join(Environment.NewLine, errors
            .Select(error => Regex.Match(error,
                @"^(?<code>[^：]+)：(?:未知 (?<section>IF|ACT)? ?命令 (?<command>[^（]+)|未知段落指令 #(?<directive>[^（]+)|(?<other>.*?))（"))
            .Select(match => match.Success
                ? $"{match.Groups["code"].Value}|{match.Groups["section"].Value}|" +
                  $"{match.Groups["command"].Value}{match.Groups["directive"].Value}" +
                  (match.Groups["other"].Success ? $"|{match.Groups["other"].Value}" : string.Empty)
                : "其他")
            .GroupBy(value => value, StringComparer.Ordinal)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .Take(100)
            .Select(group => $"{group.Key}|{group.Count()}"));
    }

    private static string SliceRoot() => Path.Combine(
        RepositoryRoot(), "Configs", "LingFengEnvirSlice", "Content");

    private static void WriteUtf8(string root, string relativePath, string content)
    {
        string path = Path.Combine(root,
            relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, new UTF8Encoding(false));
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo current = new(AppContext.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Docs", "design", "scripting",
                    "翎风服务器常量与整服Envir直接运行实施规格.md")))
                return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("未找到 LyoCrystal 仓库根目录。");
    }

    private static ItemInfo LoadLegacyItem(string databasePath, string name)
    {
        using var connection = new SqliteConnection($"Data Source={databasePath};Mode=ReadOnly;Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT Idx, Name, StdMode, Shape, Weight, Looks, DuraMax, Ac, Ac2, Mac, Mac2, " +
            "Dc, Dc2, Mc, Mc2, Sc, Sc2, NeedLevel, Price, OverLap, HP, MP " +
            "FROM StdItems WHERE Name = $name";
        command.Parameters.AddWithValue("$name", name);
        using SqliteDataReader reader = command.ExecuteReader();
        Assert.True(reader.Read(), $"封神数据库缺少物品：{name}");
        Assert.Equal(26, reader.GetInt32(2));
        var item = new ItemInfo
        {
            Index = reader.GetInt32(0),
            Name = reader.GetString(1),
            Type = ItemType.Gem,
            Shape = checked((short)Value(reader, 3)),
            Weight = checked((byte)Value(reader, 4)),
            Image = checked((ushort)Value(reader, 5)),
            Durability = checked((ushort)Value(reader, 6)),
            RequiredAmount = checked((byte)Math.Clamp(Value(reader, 17), 0, byte.MaxValue)),
            Price = checked((uint)Math.Max(0, Value(reader, 18))),
            StackSize = 1
        };
        item.Stats[Stat.MinAC] = Value(reader, 7);
        item.Stats[Stat.MaxAC] = Value(reader, 8);
        item.Stats[Stat.MinMAC] = Value(reader, 9);
        item.Stats[Stat.MaxMAC] = Value(reader, 10);
        item.Stats[Stat.MinDC] = Value(reader, 11);
        item.Stats[Stat.MaxDC] = Value(reader, 12);
        item.Stats[Stat.MinMC] = Value(reader, 13);
        item.Stats[Stat.MaxMC] = Value(reader, 14);
        item.Stats[Stat.MinSC] = Value(reader, 15);
        item.Stats[Stat.MaxSC] = Value(reader, 16);
        item.Stats[Stat.HP] = Value(reader, 20);
        item.Stats[Stat.MP] = Value(reader, 21);
        return item;
    }

    private static MonsterInfo LoadLegacyMonster(string databasePath, string name)
    {
        using var connection = new SqliteConnection($"Data Source={databasePath};Mode=ReadOnly;Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT rowid, Name, Race, Appr, Lvl, Undead, CoolEye, Exp, HP, Ac, MAC, DC, DCMAX, " +
            "MC, SC, WALK_SPD, ATTACK_SPD FROM Monster WHERE Name = $name";
        command.Parameters.AddWithValue("$name", name);
        using SqliteDataReader reader = command.ExecuteReader();
        Assert.True(reader.Read(), $"封神数据库缺少怪物：{name}");
        var monster = new MonsterInfo
        {
            Index = checked((int)reader.GetInt64(0)),
            Name = reader.GetString(1),
            AI = checked((ushort)Value(reader, 2)),
            Image = (Monster)checked((ushort)Value(reader, 3)),
            Level = checked((ushort)Value(reader, 4)),
            Undead = Value(reader, 5) != 0,
            CoolEye = checked((byte)Value(reader, 6)),
            Experience = checked((uint)Math.Max(0, Value(reader, 7))),
            MoveSpeed = checked((ushort)Math.Clamp(Value(reader, 15), 1, ushort.MaxValue)),
            AttackSpeed = checked((ushort)Math.Clamp(Value(reader, 16), 1, ushort.MaxValue))
        };
        monster.Stats[Stat.HP] = Math.Max(1, Value(reader, 8));
        monster.Stats[Stat.MinAC] = Value(reader, 9);
        monster.Stats[Stat.MaxAC] = Value(reader, 9);
        monster.Stats[Stat.MinMAC] = Value(reader, 10);
        monster.Stats[Stat.MaxMAC] = Value(reader, 10);
        monster.Stats[Stat.MinDC] = Value(reader, 11);
        monster.Stats[Stat.MaxDC] = Value(reader, 12);
        monster.Stats[Stat.MinMC] = Value(reader, 13);
        monster.Stats[Stat.MaxMC] = Value(reader, 13);
        monster.Stats[Stat.MinSC] = Value(reader, 14);
        monster.Stats[Stat.MaxSC] = Value(reader, 14);
        return monster;
    }

    private static int Value(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? 0 : Convert.ToInt32(reader.GetValue(ordinal));

    private static Map TestMap(MapInfo info)
    {
        var map = new Map(info) { Width = 1, Height = 1, Cells = new Cell[1, 1] };
        map.Cells[0, 0] = new Cell { Attribute = CellAttribute.Walk };
        return map;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { }
    }

    private static void RestoreQueue(
        System.Collections.Concurrent.ConcurrentQueue<string> queue,
        IEnumerable<string> values)
    {
        queue.Clear();
        foreach (string value in values) queue.Enqueue(value);
    }

    private sealed class TestMonster : MonsterObject
    {
        public TestMonster(MonsterInfo info) : base(info) { }
    }

    private sealed class TestPlayer : PlayerObject
    {
        public List<Packet> Packets { get; } = new();
        public override void Enqueue(Packet packet) => Packets.Add(packet);
        public override void Broadcast(Packet packet) { }
    }
}
