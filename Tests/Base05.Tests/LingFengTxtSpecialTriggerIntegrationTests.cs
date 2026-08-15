using System.Text;
using System.Drawing;
using System.Reflection;
using Server;
using Server.MirDatabase;
using Server.MirEnvir;
using Server.MirObjects;
using Server.Scripting;
using Xunit;

namespace Base05.Tests;

[Collection(nameof(PhysicalTextFileProviderCollection))]
public sealed class LingFengTxtSpecialTriggerIntegrationTests
{
    [Fact]
    public void 战斗物品怪物触发经真实脚本管理接缝单次派发且前置可取消()
    {
        bool oldTxtEnabled = Settings.TxtScriptsEnabled;
        bool oldCSharpEnabled = Settings.CSharpScriptsEnabled;
        bool oldFallback = Settings.CSharpScriptsFallbackToTxt;
        string oldTxtPath = Settings.TxtScriptsPath;
        TxtScriptLayout oldLayout = Settings.TxtScriptsLayout;
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        string root = Path.Combine(Path.GetTempPath(), "lyo-txt11-" + Guid.NewGuid().ToString("N"));
        NPCScript loadedScript = null;

        try
        {
            Directory.CreateDirectory(Path.Combine(root, "SystemScripts"));
            File.WriteAllText(Path.Combine(root, "SystemScripts", "QFunction-0.txt"),
                "[@ATTACKDAMAGE]\n#ACT\nCHANGEDAMAGEVALUE 0 = 0\n" +
                "[@STRUCKDAMAGE]\n#ACT\nCHANGEDAMAGEVALUE 0 = 0\n" +
                "[@ATTACK]\n#ACT\nGIVEGOLD 1\n" +
                "[@PICKUPITEMEX]\n#ACT\nGIVEGOLD 2\n" +
                "[@KILLMON]\n#ACT\nGIVEGOLD 4\n" +
                "[@M2DROPITEM]\n#ACT\nGIVEGOLD 8\n",
                new UTF8Encoding(false));

            Settings.CSharpScriptsEnabled = false;
            Settings.TxtScriptsEnabled = true;
            Settings.TxtScriptsPath = root;
            Settings.TxtScriptsLayout = TxtScriptLayout.LyoCrystal;
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            Envir.Main.ApplyPhysicalTextFileDefinitions();
            Assert.NotNull(Envir.Main.TextFileProvider?.GetByKey("SystemScripts/QFunction-0"));
            loadedScript = NPCScript.GetOrAdd(0, "SystemScripts/QFunction-0", NPCScriptType.Called);

            var player = Player("TXT11执行者");
            var target = Player("TXT11目标");
            var request = new PlayerDamageRequest(
                PlayerDamagePerspective.Outgoing, player, target, 25, 5, DefenceType.ACAgility, false);

            Assert.True(Envir.Main.CSharpScripts.TryHandlePlayerDamageBefore(player, request));
            Assert.Equal(ScriptHookDecision.Cancel, request.Decision);
            Assert.Equal(0, request.Damage);

            var incomingMonster = new TestMonster(new MonsterInfo { Index = 900, Name = "TXT11攻击怪" });
            Assert.Equal(0, target.Attacked(incomingMonster, 25, (DefenceType)byte.MaxValue));

            var damageResult = new PlayerDamageResult(
                PlayerDamagePerspective.Outgoing, player, target, 25, 5,
                DefenceType.ACAgility, false, false, 20, ScriptHookDecision.Continue);
            Assert.True(Envir.Main.CSharpScripts.TryHandlePlayerDamageAfter(player, damageResult));
            Assert.Equal(1u, player.Account.Gold);
            Assert.Equal(20, damageResult.AppliedDamage);
            Assert.All(typeof(PlayerDamageResult).GetProperties(), property => Assert.False(property.CanWrite));

            Assert.True(Envir.Main.CSharpScripts.TryHandlePlayerItemPickupAfter(
                player,
                new PlayerItemPickupResult(PlayerItemPickupSource.Player, player, null, 10)));
            Assert.Equal(3u, player.Account.Gold);

            var monster = new TestMonster(new MonsterInfo { Index = 901, Name = "TXT11怪物" })
            {
                EXPOwner = player
            };
            Assert.False(Envir.Main.CSharpScripts.TryHandleMonsterDie(monster));
            Assert.Equal(7u, player.Account.Gold);

            var emptyDropResult = new MonsterDropResult(
                monster, player, player, 0, "Drops/TXT11", 0, 0, 1, 0,
                Array.Empty<UserItem>(), true, ScriptHookDecision.Continue);
            Assert.False(Envir.Main.CSharpScripts.TryHandleMonsterDropAfter(monster, emptyDropResult));
            Assert.Equal(7u, player.Account.Gold);

            var dropResult = new MonsterDropResult(
                monster, player, player, 0, "Drops/TXT11", 0, 0, 1, 1,
                Array.Empty<UserItem>(), true, ScriptHookDecision.Continue);
            Assert.True(Envir.Main.CSharpScripts.TryHandleMonsterDropAfter(monster, dropResult));
            Assert.Equal(15u, player.Account.Gold);

            var fallbackPlayer = Player("TXT11回落策略");
            var fallbackResult = new PlayerDamageResult(
                PlayerDamagePerspective.Outgoing, fallbackPlayer, target, 25, 5,
                DefenceType.ACAgility, false, false, 20, ScriptHookDecision.Continue);
            bool oldManagerEnabled = Envir.Main.CSharpScripts.Enabled;
            try
            {
                typeof(ScriptManager).GetProperty(nameof(ScriptManager.Enabled), BindingFlags.Instance | BindingFlags.Public)!
                    .SetValue(Envir.Main.CSharpScripts, true);
                Settings.CSharpScriptsEnabled = true;
                Settings.CSharpScriptsFallbackToTxt = false;
                Assert.False(Envir.Main.CSharpScripts.TryHandlePlayerDamageAfter(fallbackPlayer, fallbackResult));
                Assert.Equal(0u, fallbackPlayer.Account.Gold);
                Settings.CSharpScriptsFallbackToTxt = true;
                Assert.True(Envir.Main.CSharpScripts.TryHandlePlayerDamageAfter(fallbackPlayer, fallbackResult));
                Assert.Equal(1u, fallbackPlayer.Account.Gold);
            }
            finally
            {
                typeof(ScriptManager).GetProperty(nameof(ScriptManager.Enabled), BindingFlags.Instance | BindingFlags.Public)!
                    .SetValue(Envir.Main.CSharpScripts, oldManagerEnabled);
            }

            Assert.Null(LingFengTxtTriggerContext.Current);
        }
        finally
        {
            if (loadedScript != null) Envir.Main.Scripts.Remove(loadedScript.ScriptID);
            Settings.TxtScriptsEnabled = false;
            Envir.Main.ApplyPhysicalTextFileDefinitions();
            Settings.CSharpScriptsEnabled = oldCSharpEnabled;
            Settings.CSharpScriptsFallbackToTxt = oldFallback;
            Settings.TxtScriptsEnabled = oldTxtEnabled;
            Settings.TxtScriptsPath = oldTxtPath;
            Settings.TxtScriptsLayout = oldLayout;
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void 每个已支持特殊触发均从真实领域入口派发()
    {
        bool oldTxtEnabled = Settings.TxtScriptsEnabled;
        bool oldCSharpEnabled = Settings.CSharpScriptsEnabled;
        bool oldDropGold = Settings.DropGold;
        int oldBudget = Settings.TxtScriptsMaxImmediateTransitions;
        string oldTxtPath = Settings.TxtScriptsPath;
        TxtScriptLayout oldLayout = Settings.TxtScriptsLayout;
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        string root = Path.Combine(Path.GetTempPath(), "lyo-txt11-domain-" + Guid.NewGuid().ToString("N"));
        NPCScript loadedScript = null;
        NPCScript loadedManage = null;
        NPCScript loadedMonster = null;
        NPCScript oldMonsterNpc = Envir.Main.MonsterNPC;

        try
        {
            Directory.CreateDirectory(Path.Combine(root, "SystemScripts"));
            Directory.CreateDirectory(Path.Combine(root, "NPCs"));
            File.WriteAllText(Path.Combine(root, "SystemScripts", "QManage.txt"),
                "[@LOGIN]\n#ACT\nGIVEGOLD 64\n",
                new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(root, "NPCs", "00Monster.txt"),
                "[@_DIE(915)]\n#ACT\nGIVEHP 7\n",
                new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(root, "SystemScripts", "QFunction-0.txt"),
                "[@ATTACKDAMAGE]\n#ACT\nCHANGEDAMAGEVALUE 0 = 0\n" +
                "[@ATTACK]\n#ACT\nGIVEGOLD 1\n" +
                "[@STRUCKDAMAGE]\n#ACT\nCHANGEDAMAGEVALUE 0 = 1\n" +
                "[@STRUCK]\n#ACT\nGIVEGOLD 2\n" +
                "[@PICKUPITEMEX]\n#ACT\nGIVEGOLD 4\n" +
                "[@KILLMON]\n#ACT\nGIVEGOLD 8\n" +
                "[@M2DROPITEM]\n#ACT\nGIVEGOLD 16\n" +
                "[@PLAYLEVELUP]\n#ACT\nGIVEGOLD 32\n" +
                "[@LOOP]\n#SAY\n系统输出不得污染现有对话\n#ACT\nDELAYGOTO 0 @LOOP\nGOTO @LOOP\n",
                new UTF8Encoding(false));

            Settings.CSharpScriptsEnabled = false;
            Settings.TxtScriptsEnabled = true;
            Settings.TxtScriptsPath = root;
            Settings.TxtScriptsLayout = TxtScriptLayout.LyoCrystal;
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            Settings.DropGold = false;
            Envir.Main.ApplyPhysicalTextFileDefinitions();
            loadedScript = NPCScript.GetOrAdd(0, "SystemScripts/QFunction-0", NPCScriptType.Called);
            loadedManage = NPCScript.GetOrAdd(0, "SystemScripts/QManage", NPCScriptType.Called);
            loadedMonster = NPCScript.GetOrAdd(3999991, "00Monster", NPCScriptType.AutoMonster);
            Envir.Main.MonsterNPC = loadedMonster;

            Map map = TestMap();
            var attacker = Player("TXT11领域攻击者", map);
            var targetMonster = Monster(910, "TXT11受击怪", map);

            // 前置真实入口：取消后不会产生伤害和后置 @ATTACK。
            Assert.Equal(0, targetMonster.Attacked(attacker, 25, (DefenceType)byte.MaxValue, false));
            Assert.Equal(0u, attacker.Account.Gold);

            // 去掉前置标签后重新发布同一物理快照，真实伤害链应派发 @ATTACK。
            File.WriteAllText(Path.Combine(root, "SystemScripts", "QFunction-0.txt"),
                "[@ATTACK]\n#ACT\nGIVEGOLD 1\n" +
                "[@STRUCKDAMAGE]\n#ACT\nCHANGEDAMAGEVALUE 0 = 1\n" +
                "[@STRUCK]\n#ACT\nGIVEGOLD 2\n" +
                "[@PICKUPITEMEX]\n#ACT\nGIVEGOLD 4\n" +
                "[@KILLMON]\n#ACT\nGIVEGOLD 8\n" +
                "[@M2DROPITEM]\n#ACT\nGIVEGOLD 16\n" +
                "[@PLAYLEVELUP]\n#ACT\nGIVEGOLD 32\n" +
                "[@LOOP]\n#SAY\n系统输出不得污染现有对话\n#ACT\nDELAYGOTO 0 @LOOP\nGOTO @LOOP\n",
                new UTF8Encoding(false));
            Envir.Main.ApplyPhysicalTextFileDefinitions();
            Envir.Main.Scripts.Remove(loadedScript.ScriptID);
            loadedScript = NPCScript.GetOrAdd(0, "SystemScripts/QFunction-0", NPCScriptType.Called);
            var legacyDiePage = new NPCPage("[@_DIE(915)]");
            var legacyDieSegment = new NPCSegment(
                legacyDiePage, new List<string>(), new List<string>(),
                new List<string>(), new List<string>(), new List<string>());
            legacyDieSegment.ActList.Add(new NPCActions(ActionType.GiveHP, "7"));
            legacyDiePage.SegmentList.Add(legacyDieSegment);
            loadedMonster.NPCPages.Clear();
            loadedMonster.NPCPages.Add(legacyDiePage);

            Assert.Equal(25, targetMonster.Attacked(attacker, 25, (DefenceType)byte.MaxValue, false));
            Assert.Equal(1u, attacker.Account.Gold);

            var struckPlayer = Player("TXT11领域受击者", map);
            var incomingMonster = Monster(911, "TXT11领域攻击怪", map);
            Assert.Equal(1, struckPlayer.Attacked(incomingMonster, 25, (DefenceType)byte.MaxValue));
            Assert.Equal(2u, struckPlayer.Account.Gold);

            var gold = new ItemObject(attacker, 10);
            map.GetCell(attacker.CurrentLocation).Add(gold);
            gold.Spawned();
            attacker.PickUp();
            Assert.Equal(15u, attacker.Account.Gold);
            Assert.DoesNotContain(gold, map.GetCell(attacker.CurrentLocation).Objects ?? new List<MapObject>());

            var dyingMonster = Monster(912, "TXT11领域死亡怪", map);
            dyingMonster.EXPOwner = attacker;
            dyingMonster.Master = Monster(913, "TXT11跳过掉落的主人", map);
            dyingMonster.Die();
            Assert.Equal(23u, attacker.Account.Gold);

            uint beforeCoexist = attacker.Account.Gold;
            var coexistMonster = Monster(915, "TXT11共存死亡怪", map);
            coexistMonster.Info.HasDieScript = true;
            coexistMonster.EXPOwner = attacker;
            coexistMonster.Master = Monster(916, "TXT11共存跳过掉落主人", map);
            coexistMonster.Die();
            Assert.Equal(8u, attacker.Account.Gold - beforeCoexist);
            Assert.Equal(7, coexistMonster.HP);

            var droppingMonster = Monster(914, "TXT11领域掉落怪", map);
            droppingMonster.EXPOwner = attacker;
            droppingMonster.Info.Drops.Add(new DropInfo { Chance = 1, Gold = 2 });
            uint beforeDrop = attacker.Account.Gold;
            droppingMonster.InvokeDrop();
            Assert.InRange(attacker.Account.Gold - beforeDrop, 17u, 18u);

            uint beforeLifecycle = attacker.Account.Gold;
            attacker.CallDefaultNPC(DefaultNPCType.Login);
            attacker.CallDefaultNPC(DefaultNPCType.LevelUp);
            Assert.Equal(96u, attacker.Account.Gold - beforeLifecycle);

            var activePage = new NPCPage("[@ACTIVE]");
            var activeSpeech = new List<string> { "进行中的对话" };
            attacker.NPCObjectID = 123;
            attacker.NPCScriptID = 456;
            attacker.NPCPage = activePage;
            attacker.NPCSpeech = activeSpeech;
            Settings.TxtScriptsMaxImmediateTransitions = 3;
            Assert.True(loadedScript.CallSystem(attacker, "[@LOOP]"));
            Assert.Equal(123u, attacker.NPCObjectID);
            Assert.Equal(456, attacker.NPCScriptID);
            Assert.Same(activePage, attacker.NPCPage);
            Assert.Same(activeSpeech, attacker.NPCSpeech);
            Assert.Equal(new[] { "进行中的对话" }, activeSpeech);
            Assert.DoesNotContain(attacker.ActionList, action => action.Type == DelayedType.NPC);
            Assert.Null(LingFengTxtTriggerContext.Current);
        }
        finally
        {
            if (loadedScript != null) Envir.Main.Scripts.Remove(loadedScript.ScriptID);
            if (loadedManage != null) Envir.Main.Scripts.Remove(loadedManage.ScriptID);
            if (loadedMonster != null) Envir.Main.Scripts.Remove(loadedMonster.ScriptID);
            Envir.Main.MonsterNPC = oldMonsterNpc;
            Settings.TxtScriptsEnabled = false;
            Envir.Main.ApplyPhysicalTextFileDefinitions();
            Settings.DropGold = oldDropGold;
            Settings.TxtScriptsMaxImmediateTransitions = oldBudget;
            Settings.CSharpScriptsEnabled = oldCSharpEnabled;
            Settings.TxtScriptsEnabled = oldTxtEnabled;
            Settings.TxtScriptsPath = oldTxtPath;
            Settings.TxtScriptsLayout = oldLayout;
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void 精确CSharp未处理时保持调用通用处理器且不回落Txt()
    {
        using var manager = new ScriptManager();
        typeof(ScriptManager).GetProperty(nameof(ScriptManager.Enabled), BindingFlags.Instance | BindingFlags.Public)!
            .SetValue(manager, true);
        var calls = new List<string>();
        manager.CurrentRegistry.RegisterOnPlayerDamageBefore(PlayerDamagePerspective.Outgoing, (_, _, _) => calls.Add("精确伤害"));
        manager.CurrentRegistry.RegisterOnPlayerDamageBefore((_, _, _) => calls.Add("通用伤害"));
        manager.CurrentRegistry.RegisterOnMonsterDie(920, (_, _) => { calls.Add("精确死亡"); return false; });
        manager.CurrentRegistry.RegisterOnMonsterDie((_, _) => { calls.Add("通用死亡"); return true; });

        var player = Player("CSharp优先级");
        var request = new PlayerDamageRequest(
            PlayerDamagePerspective.Outgoing, player, Monster(921, "目标"), 10, 0,
            DefenceType.ACAgility, false);
        Assert.False(manager.TryHandlePlayerDamageBefore(player, request));
        Assert.True(manager.TryHandleMonsterDie(Monster(920, "死亡目标")));
        Assert.Equal(new[] { "精确伤害", "通用伤害", "精确死亡", "通用死亡" }, calls);
    }

    [Fact]
    public void 精确CSharp后置处理器异常时继续通用处理器且不回落Txt()
    {
        using var manager = new ScriptManager();
        typeof(ScriptManager).GetProperty(nameof(ScriptManager.Enabled), BindingFlags.Instance | BindingFlags.Public)!
            .SetValue(manager, true);
        var calls = new List<string>();
        manager.CurrentRegistry.RegisterOnPlayerDamageAfter(PlayerDamagePerspective.Outgoing, (_, _, _) =>
        {
            calls.Add("精确伤害后置");
            throw new InvalidOperationException("测试精确伤害后置异常");
        });
        manager.CurrentRegistry.RegisterOnPlayerDamageAfter((_, _, _) => calls.Add("通用伤害后置"));
        manager.CurrentRegistry.RegisterOnMonsterDropAfter(920, (_, _, _) =>
        {
            calls.Add("精确掉落后置");
            throw new InvalidOperationException("测试精确掉落后置异常");
        });
        manager.CurrentRegistry.RegisterOnMonsterDropAfter((_, _, _) => calls.Add("通用掉落后置"));

        var player = Player("CSharp后置优先级");
        var monster = Monster(920, "CSharp后置目标");
        var damageResult = new PlayerDamageResult(
            PlayerDamagePerspective.Outgoing, player, monster, 10, 0,
            DefenceType.ACAgility, false, false, 10, ScriptHookDecision.Continue);
        var dropResult = new MonsterDropResult(
            monster, player, player, 0, "Drops/CSharp", 0, 0, 1, 1,
            Array.Empty<UserItem>(), true, ScriptHookDecision.Continue);

        Assert.True(manager.TryHandlePlayerDamageAfter(player, damageResult));
        Assert.True(manager.TryHandleMonsterDropAfter(monster, dropResult));
        Assert.Equal(
            new[] { "精确伤害后置", "通用伤害后置", "精确掉落后置", "通用掉落后置" },
            calls);
    }

    [Fact]
    public void CSharp处理器异常时禁止Txt重复执行且返回未处理()
    {
        bool oldTxtEnabled = Settings.TxtScriptsEnabled;
        bool oldCSharpEnabled = Settings.CSharpScriptsEnabled;
        bool oldFallback = Settings.CSharpScriptsFallbackToTxt;
        string oldTxtPath = Settings.TxtScriptsPath;
        TxtScriptLayout oldLayout = Settings.TxtScriptsLayout;
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        string root = Path.Combine(Path.GetTempPath(), "lyo-txt11-fault-" + Guid.NewGuid().ToString("N"));
        NPCScript loadedFunction = null;
        NPCScript loadedManage = null;

        try
        {
            Directory.CreateDirectory(Path.Combine(root, "SystemScripts"));
            File.WriteAllText(Path.Combine(root, "SystemScripts", "QManage.txt"),
                "[@LOGIN]\n#ACT\nGIVEGOLD 1\n", new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(root, "SystemScripts", "QFunction-0.txt"),
                "[@PLAYLEVELUP]\n#ACT\nGIVEGOLD 2\n" +
                "[@ATTACK]\n#ACT\nGIVEGOLD 4\n" +
                "[@PICKUPITEMEX]\n#ACT\nGIVEGOLD 6\n" +
                "[@M2DROPITEM]\n#ACT\nGIVEGOLD 8\n", new UTF8Encoding(false));

            Settings.CSharpScriptsEnabled = true;
            Settings.CSharpScriptsFallbackToTxt = true;
            Settings.TxtScriptsEnabled = true;
            Settings.TxtScriptsPath = root;
            Settings.TxtScriptsLayout = TxtScriptLayout.LyoCrystal;
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            Envir.Main.ApplyPhysicalTextFileDefinitions();
            loadedFunction = NPCScript.GetOrAdd(0, "SystemScripts/QFunction-0", NPCScriptType.Called);
            loadedManage = NPCScript.GetOrAdd(0, "SystemScripts/QManage", NPCScriptType.Called);

            using var manager = new ScriptManager();
            typeof(ScriptManager).GetProperty(nameof(ScriptManager.Enabled), BindingFlags.Instance | BindingFlags.Public)!
                .SetValue(manager, true);
            manager.CurrentRegistry.RegisterOnPlayerLogin((_, _) => throw new InvalidOperationException("登录异常"));
            manager.CurrentRegistry.RegisterOnPlayerLevelUp((_, _) => throw new InvalidOperationException("升级异常"));
            manager.CurrentRegistry.RegisterOnPlayerDamageAfter(PlayerDamagePerspective.Outgoing,
                (_, _, _) => throw new InvalidOperationException("伤害后置异常"));
            manager.CurrentRegistry.RegisterOnPlayerItemPickupAfter(
                (_, _, _) => throw new InvalidOperationException("拾取后置异常"));
            manager.CurrentRegistry.RegisterOnMonsterDropAfter(920,
                (_, _, _) => throw new InvalidOperationException("掉落后置异常"));

            var player = Player("CSharp异常隔离");
            var monster = Monster(920, "CSharp异常目标");
            var damageResult = new PlayerDamageResult(
                PlayerDamagePerspective.Outgoing, player, monster, 10, 0,
                DefenceType.ACAgility, false, false, 10, ScriptHookDecision.Continue);
            var dropResult = new MonsterDropResult(
                monster, player, player, 0, "Drops/CSharpFault", 0, 0, 1, 1,
                Array.Empty<UserItem>(), true, ScriptHookDecision.Continue);

            Assert.False(manager.TryHandlePlayerLogin(player));
            Assert.False(manager.TryHandlePlayerLevelUp(player));
            Assert.False(manager.TryHandlePlayerDamageAfter(player, damageResult));
            Assert.False(manager.TryHandlePlayerItemPickupAfter(
                player, new PlayerItemPickupResult(PlayerItemPickupSource.Player, player, null, 1)));
            Assert.False(manager.TryHandleMonsterDropAfter(monster, dropResult));
            Assert.Equal(0u, player.Account.Gold);

            ScriptManager globalManager = Envir.Main.CSharpScripts;
            FieldInfo registryField = typeof(ScriptManager).GetField(
                "_currentRegistry", BindingFlags.Instance | BindingFlags.NonPublic)!;
            ScriptRegistry oldRegistry = globalManager.CurrentRegistry;
            bool oldManagerEnabled = globalManager.Enabled;
            using var isolatedRegistryOwner = new ScriptManager();
            try
            {
                registryField.SetValue(globalManager, isolatedRegistryOwner.CurrentRegistry);
                typeof(ScriptManager).GetProperty(nameof(ScriptManager.Enabled), BindingFlags.Instance | BindingFlags.Public)!
                    .SetValue(globalManager, true);
                globalManager.CurrentRegistry.RegisterOnPlayerLogin((_, _) => throw new InvalidOperationException("真实登录链异常"));
                globalManager.CurrentRegistry.RegisterOnPlayerLevelUp((_, _) => throw new InvalidOperationException("真实升级链异常"));

                int delayedBefore = player.ActionList.Count;
                player.CallDefaultNPC(DefaultNPCType.Login);
                player.CallDefaultNPC(DefaultNPCType.LevelUp);
                Assert.Equal(delayedBefore, player.ActionList.Count);
                Assert.Equal(0u, player.Account.Gold);
            }
            finally
            {
                registryField.SetValue(globalManager, oldRegistry);
                typeof(ScriptManager).GetProperty(nameof(ScriptManager.Enabled), BindingFlags.Instance | BindingFlags.Public)!
                    .SetValue(globalManager, oldManagerEnabled);
            }
        }
        finally
        {
            if (loadedFunction != null) Envir.Main.Scripts.Remove(loadedFunction.ScriptID);
            if (loadedManage != null) Envir.Main.Scripts.Remove(loadedManage.ScriptID);
            Settings.TxtScriptsEnabled = false;
            Envir.Main.ApplyPhysicalTextFileDefinitions();
            Settings.CSharpScriptsEnabled = oldCSharpEnabled;
            Settings.CSharpScriptsFallbackToTxt = oldFallback;
            Settings.TxtScriptsEnabled = oldTxtEnabled;
            Settings.TxtScriptsPath = oldTxtPath;
            Settings.TxtScriptsLayout = oldLayout;
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void CSharp已接管时同一特殊触发不回落Txt()
    {
        var player = Player("优先级执行者");
        var request = new PlayerDamageRequest(
            PlayerDamagePerspective.Outgoing, player, Player("目标"), 10, 0, DefenceType.ACAgility, false);

        Assert.True(LingFengTxtSystemHookAdapter.TryDispatchPlayerDamageBefore(
            true, new SingleProvider("[@ATTACKDAMAGE]"), player, request));
        Assert.Equal(10, request.Damage);
        Assert.Equal(ScriptHookDecision.Continue, request.Decision);
    }

    private static PlayerObject Player(string name, Map map = null)
    {
        var player = new PlayerObject
        {
            Info = new CharacterInfo { Name = name, HP = 100 },
            Account = new AccountInfo(),
            Stats = new Stats(),
            ArmourRate = 1,
            DamageRate = 1,
            CurrentMap = map,
            CurrentLocation = Point.Empty
        };
        player.Stats[Stat.HP] = 100;
        return player;
    }

    private static TestMonster Monster(int index, string name, Map map = null)
    {
        var monster = new TestMonster(new MonsterInfo { Index = index, Name = name })
        {
            ArmourRate = 1,
            DamageRate = 1,
            CurrentMap = map,
            CurrentLocation = Point.Empty,
            HP = 100
        };
        monster.Stats[Stat.HP] = 100;
        return monster;
    }

    private static Map TestMap()
    {
        var map = new Map(new MapInfo { Index = 9911 })
        {
            Width = 1,
            Height = 1,
            Cells = new Cell[1, 1]
        };
        map.Cells[0, 0] = new Cell { Attribute = CellAttribute.Walk };
        return map;
    }

    private sealed class TestMonster : MonsterObject
    {
        public TestMonster(MonsterInfo info) : base(info) { }
        public void InvokeDrop() => Drop();
    }

    private sealed class SingleProvider : ITextFileProvider
    {
        private readonly TextFileDefinition _definition;
        public SingleProvider(params string[] lines) =>
            _definition = new TextFileDefinition("SystemScripts/QFunction-0").AddLines(lines);
        public IReadOnlyCollection<TextFileDefinition> GetAll() => new[] { _definition };
        public TextFileDefinition GetByKey(string key) =>
            LogicKey.NormalizeOrThrow(key) == _definition.Key ? _definition : null;
    }
}
