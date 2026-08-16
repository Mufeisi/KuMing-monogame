using Server;
using Server.MirDatabase;
using Server.MirEnvir;
using Server.MirObjects;
using Server.MirObjects.Monsters;
using Server.Scripting;
using Server.Scripting.ServerSymbols;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Text;
using Xunit;

namespace Base05.Tests;

[Collection(nameof(PhysicalTextFileProviderCollection))]
public sealed class LingFengP2ServerSymbolIntegrationTests : IDisposable
{
    private static readonly string[] SpecificationP2Names =
    {
        "HERONAME", "H.USERNAME", "H.LEVEL", "H.EXP", "H.JOB", "H.GENDER",
        "H.HP", "H.MAXHP", "H.MP", "H.MAXMP", "H.PKPOINT",
        "H.MAXAC", "H.MAXMAC", "H.MAXDC", "H.MAXMC", "H.SC", "H.MAXSC", "H.HIT", "H.SPD", "H.LUCK",
        "H.MAP", "H.X", "H.Y", "H.RIGHTHAND", "H.WEAPON", "H.HELMET", "H.NECKLACE",
        "H.RING_L", "H.RING_R", "H.ARMRING_L", "H.ARMRING_R", "H.BELT", "H.BOOTS", "H.BUJUK",
        "H.CURRRTARGETNAME", "H.ATTACKMONSTER_NAME", "H.ATTACKMONSTER_NAMEEX",
        "H.ATTACKMONSTER_X", "H.ATTACKMONSTER_XEX", "H.ATTACKMONSTER_Y", "H.ATTACKMONSTER_YEX",
        "H.ATTACKMONSTER_HP", "H.ATTACKMONSTER_HPEX", "H.ATTACKMONSTER_MAXHP", "H.ATTACKMONSTER_MAXHPEX",
        "H.DAMAGEVALUE", "H.PKPOWER", "H.STRUCKHP", "H.CURRRUSEMAGICID", "H.MAGICID",
        "H.KILLMONNAME", "H.GETEXP",
        "SLAVECOUNT", "SLAVEX", "SLAVEY", "SLAVETARGETX", "SLAVETARGETY",
        "PET.X", "PET.Y", "PET.HP", "PET.MAXHP", "PET.CURTARGETFULLNAME", "PET.CURTARGETNAME",
        "PET.CURTARGETHP", "PET.CURTARGETMAXHP", "PET.CURTARGETX", "PET.CURTARGETY",
        "PET.KILLMONNAME"
    };

    private readonly string _previousCompatibilityVersion;
    private readonly bool _previousTxtEnabled;

    public LingFengP2ServerSymbolIntegrationTests()
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
    public void 运行时P2目录与有真实模型的阶段清单完全一致()
    {
        Assert.Equal(
            SpecificationP2Names.OrderBy(name => name, StringComparer.Ordinal),
            LingFengP0ServerSymbols.P2CanonicalNames.OrderBy(name => name, StringComparer.Ordinal));
    }

    [Fact]
    public void 伤害契约保留旧构造签名且新增实际领域来源只读属性()
    {
        PlayerObject owner = Player("契约人物");
        HeroObject actor = Hero(owner, "契约英雄");
        TestMonster target = Monster("契约目标", 0, 0);
        var legacyRequest = new PlayerDamageRequest(
            PlayerDamagePerspective.Outgoing, owner, target, 10, 0, DefenceType.AC, false);
        var request = new PlayerDamageRequest(
            PlayerDamagePerspective.Outgoing, owner, target, 10, 0, DefenceType.AC, false, actor);
        var legacyResult = new PlayerDamageResult(
            PlayerDamagePerspective.Outgoing, owner, target, 10, 0, DefenceType.AC,
            false, false, 10, ScriptHookDecision.Continue);
        var result = new PlayerDamageResult(
            PlayerDamagePerspective.Outgoing, owner, target, 10, 0, DefenceType.AC,
            false, false, 10, ScriptHookDecision.Continue, actor);

        Assert.Same(owner, legacyRequest.Actor);
        Assert.Same(actor, request.Actor);
        Assert.Same(owner, legacyResult.Actor);
        Assert.Same(actor, result.Actor);
        Assert.False(typeof(PlayerDamageRequest).GetProperty(nameof(PlayerDamageRequest.Actor))!.CanWrite);
        Assert.False(typeof(PlayerDamageResult).GetProperty(nameof(PlayerDamageResult.Actor))!.CanWrite);
        Assert.Contains(typeof(PlayerDamageRequest).GetConstructors(), constructor =>
            constructor.GetParameters().Length == 7);
        Assert.Contains(typeof(PlayerDamageResult).GetConstructors(), constructor =>
            constructor.GetParameters().Length == 10);
        Assert.Contains(typeof(LingFengMonsterKillEvent).GetConstructors(), constructor =>
            constructor.GetParameters().Length == 4);
        Assert.Contains(typeof(LingFengDamageEvent).GetConstructors(), constructor =>
            constructor.GetParameters().Length == 13);
        Assert.Contains(typeof(LingFengMonsterKillEvent).GetMethods(), method =>
            method.Name == "Deconstruct" && method.GetParameters().Length == 4);
        Assert.Contains(typeof(LingFengDamageEvent).GetMethods(), method =>
            method.Name == "Deconstruct" && method.GetParameters().Length == 13);

        var legacyKill = new LingFengMonsterKillEvent("旧击杀", 1, 2, 3);
        (string killName, int killX, int killY, uint killExp) = legacyKill;
        Assert.Equal(("旧击杀", 1, 2, 3u), (killName, killX, killY, killExp));

        var legacyDamage = new LingFengDamageEvent(
            PlayerDamagePerspective.Outgoing, "旧攻击者", "旧目标", "旧当前目标", 4, 3, true,
            true, 1, 2, 3, 4, "26");
        (PlayerDamagePerspective damagePerspective, string damageAttacker, string damageTarget,
            string damageCurrentTarget, int damageValue, int appliedDamage, bool isAfter,
            bool targetIsMonster, int targetX, int targetY, int targetHp, int targetMaxHp,
            string magicId) = legacyDamage;
        Assert.Equal(PlayerDamagePerspective.Outgoing, damagePerspective);
        Assert.Equal(("旧攻击者", "旧目标", "旧当前目标", 4, 3, true, true, 1, 2, 3, 4, "26"),
            (damageAttacker, damageTarget, damageCurrentTarget, damageValue, appliedDamage, isAfter,
                targetIsMonster, targetX, targetY, targetHp, targetMaxHp, magicId));
    }

    [Fact]
    public void 英雄镜像读取独立英雄模型而不是冒充人物()
    {
        PlayerObject player = Player("人物甲");
        HeroObject hero = Hero(player, "英雄乙");
        hero.HInfo.Level = 18;
        hero.HInfo.Experience = 1234;
        hero.HInfo.Class = MirClass.道士;
        hero.HInfo.Gender = MirGender.Female;
        hero.HP = 66;
        hero.MP = 44;
        hero.Stats[Stat.HP] = 99;
        hero.Stats[Stat.MP] = 88;
        hero.Stats[Stat.MaxDC] = 17;
        hero.CurrentLocation = new Point(12, 34);
        hero.HInfo.Equipment[(int)EquipmentSlot.Weapon] = new UserItem(new ItemInfo { Name = "英雄木剑" });
        player.Info.Heroes[0] = hero.HInfo;
        player.CurrentHero = hero.HInfo;
        player.Hero = hero;

        var segment = Segment();
        const string source =
            "<$USERNAME>|<$HERONAME>|<$H.USERNAME>|<$H.LEVEL>|<$H.EXP>|<$H.JOB>|<$H.GENDER>|" +
            "<$H.HP>|<$H.MAXHP>|<$H.MP>|<$H.MAXMP>|<$H.MAXDC>|<$H.X>|<$H.Y>|<$H.RIGHTHAND>|<$H.WEAPON>";

        Assert.Equal(
            "人物甲|英雄乙|英雄乙|18|1234|Taoist|Female|66|99|44|88|17|12|34|英雄木剑|英雄木剑",
            segment.ReplaceValue(player, source));
    }

    [Fact]
    public void 未召唤英雄只暴露持久身份属性而不伪造运行时位置和生命()
    {
        PlayerObject player = Player("人物");
        var info = new HeroInfo { Name = "仓库英雄", Level = 9, Equipment = new UserItem[14] };
        player.Info.Heroes[0] = info;
        player.CurrentHero = info;
        var segment = Segment();

        Assert.Equal("仓库英雄|9|<$H.HP>|<$H.X>",
            segment.ReplaceValue(player, "<$HERONAME>|<$H.LEVEL>|<$H.HP>|<$H.X>"));
    }

    [Fact]
    public void 多宝宝按集合顺序稳定选择首个存活在场对象且不选择死亡对象()
    {
        PlayerObject player = Player("主人");
        TestMonster dead = Monster("死亡宝宝", 1, 2, true);
        TestMonster firstAlive = Monster("神兽7", 11, 12);
        TestMonster secondAlive = Monster("月灵2", 21, 22);
        TestMonster target = Monster("稻草人99", 31, 32);
        target.HP = 45;
        target.Stats[Stat.HP] = 90;
        firstAlive.Target = target;
        player.Pets.Add(dead);
        player.Pets.Add(firstAlive);
        player.Pets.Add(secondAlive);

        string source =
            "<$SLAVECOUNT>|<$SLAVEX>|<$SLAVEY>|<$SLAVETARGETX>|<$SLAVETARGETY>|" +
            "<$PET.X>|<$PET.Y>|<$PET.HP>|<$PET.MAXHP>|<$PET.CURTARGETFULLNAME>|" +
            "<$PET.CURTARGETNAME>|<$PET.CURTARGETHP>|<$PET.CURTARGETMAXHP>|<$PET.CURTARGETX>|<$PET.CURTARGETY>";

        Assert.Equal("2|11|12|31|32|11|12|70|100|稻草人99|稻草人|45|90|31|32",
            Segment().ReplaceValue(player, source));
    }

    [Fact]
    public void 没有存活宝宝时数量为零而对象常量保留原文()
    {
        PlayerObject player = Player("主人");
        player.Pets.Add(Monster("死亡宝宝", 1, 2, true));

        Assert.Equal("0|<$PET.X>|<$SLAVEX>",
            Segment().ReplaceValue(player, "<$SLAVECOUNT>|<$PET.X>|<$SLAVEX>"));
    }

    [Fact]
    public void 英雄战斗与击杀常量只接受英雄事件且不与人物事件串号()
    {
        PlayerObject player = Player("主人");
        HeroObject hero = Hero(player, "战斗英雄");
        player.Info.Heroes[0] = hero.HInfo;
        player.CurrentHero = hero.HInfo;
        player.Hero = hero;
        var heroDamage = new LingFengDamageEvent(
            PlayerDamagePerspective.Outgoing, "主人", "白野猪", "白野猪", 25, 23, true,
            true, 51, 52, 80, 100, "26", LingFengCombatActorKind.Hero);
        const string damageSource =
            "<$H.CURRRTARGETNAME>|<$H.DAMAGEVALUE>|<$H.PKPOWER>|<$H.CURRRUSEMAGICID>|" +
            "<$H.ATTACKMONSTER_NAME>|<$H.ATTACKMONSTER_X>|<$H.ATTACKMONSTER_HP>|<$H.ATTACKMONSTER_MAXHP>";

        using (LingFengTxtTriggerContext.Push(heroDamage))
            Assert.Equal("白野猪|25|23|26|白野猪|51|80|100", Segment().ReplaceValue(player, damageSource));

        using (LingFengTxtTriggerContext.Push(heroDamage with { ActorKind = LingFengCombatActorKind.Player }))
            Assert.Equal(damageSource, Segment().ReplaceValue(player, damageSource));

        using (LingFengTxtTriggerContext.Push(
                   new LingFengMonsterKillEvent("英雄击杀怪", 7, 8, 99, LingFengCombatActorKind.Hero)))
            Assert.Equal("英雄击杀怪|99", Segment().ReplaceValue(player, "<$H.KILLMONNAME>|<$H.GETEXP>"));

        using (LingFengTxtTriggerContext.Push(
                   new LingFengMonsterKillEvent("人物击杀怪", 7, 8, 99, LingFengCombatActorKind.Player)))
            Assert.Equal("<$H.KILLMONNAME>|<$H.GETEXP>",
                Segment().ReplaceValue(player, "<$H.KILLMONNAME>|<$H.GETEXP>"));
    }

    [Fact]
    public void 英雄真实伤害与击杀领域链携带英雄事件身份()
    {
        bool oldCSharpEnabled = Settings.CSharpScriptsEnabled;
        string oldTxtPath = Settings.TxtScriptsPath;
        TxtScriptLayout oldLayout = Settings.TxtScriptsLayout;
        string root = Path.Combine(Path.GetTempPath(), "lyo-lfenv07-hero-" + Guid.NewGuid().ToString("N"));
        NPCScript script = null;
        TestPlayer registeredPlayer = null;
        TestPlayer priorOwner = null;

        try
        {
            Directory.CreateDirectory(Path.Combine(root, "SystemScripts"));
            File.WriteAllText(Path.Combine(root, "SystemScripts", "QFunction-0.txt"),
                "[@ATTACK]\n#ACT\nLOCALMESSAGE \"英雄伤害:<$H.CURRRTARGETNAME>|<$H.PKPOWER>|<$H.CURRRUSEMAGICID>\" System\n" +
                "[@KILLMON]\n#ACT\nLOCALMESSAGE \"英雄击杀:<$H.KILLMONNAME>|<$H.GETEXP>\" System\n" +
                "LOCALMESSAGE \"宠物击杀:<$PET.KILLMONNAME>\" System\n",
                new UTF8Encoding(false));
            Settings.CSharpScriptsEnabled = false;
            Settings.TxtScriptsPath = root;
            Settings.TxtScriptsLayout = TxtScriptLayout.LyoCrystal;
            Envir.Main.ApplyPhysicalTextFileDefinitions();
            script = NPCScript.GetOrAdd(uint.MaxValue - 707, "SystemScripts/QFunction-0", NPCScriptType.Called);

            Map map = TestMap();
            TestPlayer player = registeredPlayer = (TestPlayer)Player("英雄主人");
            priorOwner = (TestPlayer)Player("先手人物");
            Envir.Main.Players.Add(priorOwner);
            Envir.Main.Players.Add(player);
            player.CurrentMap = map;
            player.AMode = AttackMode.All;
            player.Node = new LinkedListNode<MapObject>(player);
            HeroObject hero = Hero(player, "领域英雄");
            hero.CurrentMap = map;
            hero.CurrentLocation = Point.Empty;
            hero.Node = new LinkedListNode<MapObject>(hero);
            player.Info.Heroes[0] = hero.HInfo;
            player.CurrentHero = hero.HInfo;
            player.Hero = hero;

            TestMonster target = Monster("英雄领域目标", 1, 0);
            target.CurrentMap = map;
            target.Node = new LinkedListNode<MapObject>(target);
            map.GetCell(target.CurrentLocation).Add(target);
            Assert.Equal(25, target.Attacked(hero, 25, (DefenceType)byte.MaxValue, false));
            Assert.Contains(player.Packets.OfType<ServerPackets.Chat>(), packet =>
                packet.Message == "英雄伤害:英雄领域目标|25|0");

            TestMonster dying = Monster("英雄领域击杀", 1, 0);
            dying.CurrentMap = map;
            dying.Node = new LinkedListNode<MapObject>(dying);
            dying.HP = 10;
            dying.Info.Experience = 321;
            dying.Master = Monster("阻止掉落主人", 0, 0);
            dying.EXPOwner = priorOwner;
            Assert.Equal(10, dying.Attacked(hero, 10, (DefenceType)byte.MaxValue, false));
            Assert.Contains(player.Packets.OfType<ServerPackets.Chat>(), packet =>
                packet.Message == "英雄击杀:英雄领域击杀|321");
            Assert.DoesNotContain(priorOwner.Packets.OfType<ServerPackets.Chat>(), packet =>
                packet.Message.Contains("英雄领域击杀", StringComparison.Ordinal));

            TestMonster pet = Monster("领域宝宝", 0, 0);
            pet.Master = player;
            pet.CurrentMap = map;
            pet.Node = new LinkedListNode<MapObject>(pet);
            player.Pets.Add(pet);
            TestMonster petTarget = Monster("宝宝领域击杀", 1, 0);
            petTarget.CurrentMap = map;
            petTarget.Node = new LinkedListNode<MapObject>(petTarget);
            petTarget.HP = 10;
            petTarget.Info.Experience = 456;
            petTarget.Master = Monster("阻止宝宝掉落主人", 0, 0);
            petTarget.EXPOwner = priorOwner;
            Assert.Equal(10, petTarget.Attacked(pet, 10, (DefenceType)byte.MaxValue));
            Assert.Contains(player.Packets.OfType<ServerPackets.Chat>(), packet =>
                packet.Message == "宠物击杀:宝宝领域击杀");
            Assert.DoesNotContain(priorOwner.Packets.OfType<ServerPackets.Chat>(), packet =>
                packet.Message.Contains("宝宝领域击杀", StringComparison.Ordinal));

            player.Pets.Remove(pet);
            TestMonster heroPet = Monster("英雄召唤宝宝", 0, 0);
            heroPet.Master = hero;
            heroPet.CurrentMap = map;
            hero.Pets.Add(heroPet);
            TestMonster heroPetTarget = Monster("英雄宝宝击杀", 1, 0);
            heroPetTarget.CurrentMap = map;
            heroPetTarget.HP = 10;
            heroPetTarget.Info.Experience = 654;
            heroPetTarget.Master = Monster("阻止英雄宝宝掉落主人", 0, 0);
            heroPetTarget.EXPOwner = priorOwner;
            Assert.Equal(10, heroPetTarget.Attacked(heroPet, 10, (DefenceType)byte.MaxValue));
            Assert.Contains(player.Packets.OfType<ServerPackets.Chat>(), packet =>
                packet.Message == "宠物击杀:英雄宝宝击杀");
            Assert.DoesNotContain(priorOwner.Packets.OfType<ServerPackets.Chat>(), packet =>
                packet.Message.Contains("英雄宝宝击杀", StringComparison.Ordinal));

            var derivedTarget = new CityGate(new MonsterInfo { Name = "派生城门击杀", Experience = 789 })
            {
                CurrentMap = map,
                CurrentLocation = new Point(1, 0),
                HP = 1,
                ArmourRate = 1,
                DamageRate = 1,
                Master = Monster("阻止派生掉落主人", 0, 0),
                EXPOwner = priorOwner
            };
            derivedTarget.Node = new LinkedListNode<MapObject>(derivedTarget);
            derivedTarget.Stats[Stat.HP] = 1;
            Assert.Equal(1, derivedTarget.Attacked(hero, 1, (DefenceType)byte.MaxValue, false));
            Assert.Contains(player.Packets.OfType<ServerPackets.Chat>(), packet =>
                packet.Message == "英雄击杀:派生城门击杀|789");
            Assert.DoesNotContain(priorOwner.Packets.OfType<ServerPackets.Chat>(), packet =>
                packet.Message.Contains("派生城门击杀", StringComparison.Ordinal));
            Assert.Null(LingFengTxtTriggerContext.Current);
        }
        finally
        {
            if (script != null) Envir.Main.Scripts.Remove(script.ScriptID);
            if (registeredPlayer != null) Envir.Main.Players.Remove(registeredPlayer);
            if (priorOwner != null) Envir.Main.Players.Remove(priorOwner);
            Settings.TxtScriptsEnabled = false;
            Envir.Main.ApplyPhysicalTextFileDefinitions();
            Settings.CSharpScriptsEnabled = oldCSharpEnabled;
            Settings.TxtScriptsPath = oldTxtPath;
            Settings.TxtScriptsLayout = oldLayout;
            Settings.TxtScriptsEnabled = true;
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static NPCSegment Segment() => new(
        new NPCPage("[@MAIN]"), new List<string>(), new List<string>(),
        new List<string>(), new List<string>(), new List<string>());

    private static PlayerObject Player(string name) => new TestPlayer
    {
        Info = new CharacterInfo { Name = name, Level = 1, Heroes = new HeroInfo[1] },
        Account = new AccountInfo(),
        Stats = new Stats()
    };

    private static HeroObject Hero(PlayerObject owner, string name)
    {
        var hero = (HeroObject)RuntimeHelpers.GetUninitializedObject(typeof(HeroObject));
        var info = new HeroInfo { Name = name, Level = 1, Equipment = new UserItem[14] };
        hero.Info = info;
        hero.HInfo = info;
        hero.Owner = owner;
        hero.Stats = new Stats();
        hero.Pets = new List<MonsterObject>();
        return hero;
    }

    private static TestMonster Monster(string name, int x, int y, bool dead = false)
    {
        var monster = new TestMonster(new MonsterInfo { Name = name })
        {
            CurrentLocation = new Point(x, y),
            HP = 70,
            Dead = dead,
            ArmourRate = 1,
            DamageRate = 1
        };
        monster.Stats[Stat.HP] = 100;
        monster.Node = new LinkedListNode<MapObject>(monster);
        return monster;
    }

    private static Map TestMap()
    {
        var map = new Map(new MapInfo { Index = 9707, FileName = "LFENV07", Title = "LFENV07" })
        {
            Width = 2,
            Height = 1,
            Cells = new Cell[2, 1]
        };
        map.Cells[0, 0] = new Cell { Attribute = CellAttribute.Walk };
        map.Cells[1, 0] = new Cell { Attribute = CellAttribute.Walk };
        return map;
    }

    private sealed class TestPlayer : PlayerObject
    {
        public List<Packet> Packets { get; } = new();
        public override void Enqueue(Packet packet) => Packets.Add(packet);
        public override void Broadcast(Packet packet) { }
    }

    private sealed class TestMonster : MonsterObject
    {
        public TestMonster(MonsterInfo info) : base(info) { }
    }
}
