using Server.MirDatabase;
using Server.MirEnvir;
using Server.MirObjects;
using Server.Scripting;
using Server.Scripting.Variables;
using Server;
using System.Drawing;
using System.Reflection;
using Xunit;

namespace Base05.Tests;

[Collection(nameof(PhysicalTextFileProviderCollection))]
public sealed class LingFengMonsterDropProviderTests
{
    [Fact]
    public void Provider_ParsesSimpleGoldChildAndCallAsImmutableSnapshot()
    {
        ItemInfo sword = new() { Index = 1, Name = "木剑", Type = ItemType.武器 };
        ItemInfo shield = new() { Index = 2, Name = "魔法盾", Type = ItemType.技能书 };
        TextFileDefinition called = new TextFileDefinition("QuestDiary/爆率/大怪")
            .AddLines(["[@大怪]", "#ACT", "1/3 魔法盾 Q"]);
        TextFileDefinition source = new TextFileDefinition("Drops/鸡")
            .AddLines([
                "1/1 木剑 2",
                "1/2 金币 300",
                "#CHILD 1/4 RANDOM",
                "(",
                "1/1 木剑",
                "1/1 魔法盾",
                ")",
                "#CALL [\\QuestDiary\\爆率\\大怪.txt] @大怪"
            ]);

        Assert.True(LingFengMonsterDropProvider.TryCreate(
            [source],
            new Dictionary<string, TextFileDefinition>(StringComparer.Ordinal)
            {
                [called.Key] = called
            },
            name => name == sword.Name ? sword : name == shield.Name ? shield : null,
            out LingFengMonsterDropProvider provider,
            out IReadOnlyList<string> errors), string.Join(Environment.NewLine, errors));

        source.SetLines(["1/1 不应进入快照"]);
        IReadOnlyList<DropInfo> drops = provider.Get("Drops/鸡");

        Assert.Equal(4, drops.Count);
        Assert.Contains(drops, drop => drop.Item == sword && drop.Count == 2 && drop.Chance == 1);
        Assert.Contains(drops, drop => drop.Gold == 300 && drop.Chance == 2);
        DropInfo group = Assert.Single(drops, drop => drop.GroupedDrop != null);
        Assert.True(group.GroupedDrop.Random);
        Assert.Equal(2, group.GroupedDrop.Count);
        Assert.Contains(drops, drop => drop.Item == shield && drop.QuestRequired && drop.Chance == 3);
    }

    [Theory]
    [InlineData("#CHILD 1/1 RANDOM [N1]\n(\n1/1 木剑\n)", "LFENV11-DROP-004")]
    [InlineData("#CHILD 0 RANDOM\n(\n1/1 木剑\n)", "LFENV11-DROP-003")]
    public void Provider_FailsClosedForUnsupportedOrInvalidCandidate(string text, string expectedCode)
    {
        TextFileDefinition source = new TextFileDefinition("Drops/失败", "MonItems/失败.txt", "CP936", "CRLF")
            .AddLines(text.Split('\n'));

        Assert.False(LingFengMonsterDropProvider.TryCreate(
            [source],
            new Dictionary<string, TextFileDefinition>(StringComparer.Ordinal),
            _ => new ItemInfo(),
            out LingFengMonsterDropProvider provider,
            out IReadOnlyList<string> errors));

        Assert.Null(provider);
        Assert.Contains(errors, error => error.Contains(expectedCode, StringComparison.Ordinal));
    }

    [Fact]
    public void Provider_兼容裸物品纯比较空组与分隔行并登记缺失外部页()
    {
        ItemInfo item = new() { Name = "裸物品" };
        TextFileDefinition source = new TextFileDefinition("Drops/长尾")
            .AddLines([
                "----------------分隔说明----------------",
                "#CHILD 1/1 RANDOM [U171>=0]", "(", "裸物品", ")",
                "#CHILD 1/1 RANDOM", "(", ")",
                "#CHILD 1/1 RANDOM [N1<99999,7,@掉落前检测]", "(", "裸物品", ")",
                "#CALL [\\QuestDiary\\外部爆率.txt] @外部页"
            ]);

        Assert.True(LingFengMonsterDropProvider.TryCreate(
            [source],
            new Dictionary<string, TextFileDefinition>(StringComparer.Ordinal),
            name => name == item.Name ? item : null,
            (_, reference, comparison, operand) =>
                reference is "U171" or "N1" && comparison is ">=" or "<" &&
                operand is "0" or "99999",
            (_, _) => false,
            out LingFengMonsterDropProvider provider,
            out IReadOnlyList<string> errors), string.Join(Environment.NewLine, errors));

        Assert.Equal(2, provider.Get("Drops/长尾").Count);
        Assert.Contains(provider.GetDependencyRequirements(), dependency =>
            dependency.Level == LingFengDependencyLevel.E2 &&
            dependency.Key == "LingFeng/DropConditionCallback/@掉落前检测");
        Assert.Contains(provider.GetDependencyRequirements(), dependency =>
            dependency.Level == LingFengDependencyLevel.E2 &&
            dependency.Key.Contains("ExternalDropPage", StringComparison.Ordinal));
    }

    [Fact]
    public void Provider_兼容分母简写生成器说明与页尾隐式结束组()
    {
        ItemInfo item = new() { Name = "旧式物品" };
        TextFileDefinition source = new TextFileDefinition("Drops/旧式")
            .AddLines(["本行文件由工具自动生成", "140 旧式物品", "#CHILD 1/1", "(", "1/1 旧式物品"]);

        Assert.True(LingFengMonsterDropProvider.TryCreate(
            [source], new Dictionary<string, TextFileDefinition>(StringComparer.Ordinal),
            _ => item, out LingFengMonsterDropProvider provider,
            out IReadOnlyList<string> errors), string.Join(Environment.NewLine, errors));

        IReadOnlyList<DropInfo> drops = provider.Get(source.Key);
        Assert.Equal(2, drops.Count);
        Assert.Contains(drops, drop => drop.Item == item && drop.Chance == 140);
        Assert.Contains(drops, drop => drop.GroupedDrop?.Single().Item == item);
    }

    [Fact]
    public void Provider_兼容同一物理行粘连的两个概率项()
    {
        ItemInfo first = new() { Name = "MP强化药水" };
        ItemInfo second = new() { Name = "宠物经验丹[初级]" };
        TextFileDefinition source = new TextFileDefinition("Drops/粘连")
            .AddLine("1/30 MP强化药水1/3000 宠物经验丹[初级]");

        Assert.True(LingFengMonsterDropProvider.TryCreate(
            [source], new Dictionary<string, TextFileDefinition>(),
            name => name == first.Name ? first : name == second.Name ? second : null,
            out LingFengMonsterDropProvider provider, out IReadOnlyList<string> errors),
            string.Join(Environment.NewLine, errors));
        Assert.Equal(2, provider.Get(source.Key).Count);
    }

    [Fact]
    public void Provider_FailsClosedForCallCycle()
    {
        TextFileDefinition source = new TextFileDefinition("Drops/循环").AddLine("#CALL [\\QuestDiary\\循环.txt] @入口");
        TextFileDefinition called = new TextFileDefinition("QuestDiary/循环")
            .AddLines(["[@入口]", "#ACT", "#CALL [\\QuestDiary\\循环.txt] @入口"]);

        Assert.False(LingFengMonsterDropProvider.TryCreate(
            [source],
            new Dictionary<string, TextFileDefinition>(StringComparer.Ordinal) { [called.Key] = called },
            _ => new ItemInfo(),
            out _,
            out IReadOnlyList<string> errors));
        Assert.Contains(errors, error => error.Contains("LFENV11-DROP-009", StringComparison.Ordinal));
    }

    [Fact]
    public void DependencyValidation_RejectsMissingDropItem()
    {
        Assert.True(LingFengMonsterDropProvider.TryCreate(
            [new TextFileDefinition("Drops/缺物品").AddLine("1/1 不存在物品")],
            new Dictionary<string, TextFileDefinition>(StringComparer.Ordinal),
            _ => null,
            out LingFengMonsterDropProvider provider,
            out IReadOnlyList<string> syntaxErrors), string.Join(Environment.NewLine, syntaxErrors));

        Assert.Contains(provider.ValidateDependencies(),
            error => error.Contains("LFENV11-DROP-DEPENDENCY", StringComparison.Ordinal) &&
                     error.Contains("不存在物品", StringComparison.Ordinal));
    }

    [Fact]
    public void ConditionalChild_UsesRealPlayerVariableContext()
    {
        var callbacks = new List<string>();
        int callbackSideEffects = 0;
        bool callbackSucceeds = true;
        ItemInfo item = new() { Name = "条件物品" };
        TextFileDefinition qFunction = new TextFileDefinition("SystemScripts/QFunction-0")
            .AddLines(["[@条件命中]", "#ACT"]);
        TextFileDefinition source = new TextFileDefinition("Drops/条件")
            .AddLines(["#CHILD 1/1 RANDOM [U23=5,7,@条件命中]", "(", "1/1 条件物品", ")"]);
        using var variableManager = new ScriptManager();
        Assert.True(LingFengMonsterDropProvider.TryCreate(
            [source],
            new Dictionary<string, TextFileDefinition>(StringComparer.Ordinal) { [qFunction.Key] = qFunction },
            _ => item,
            (player, reference, comparison, operand) =>
            {
                ScriptVariableCheckResult result = variableManager.VariableCommands.Check(
                    ScriptVariableContext.ForPlayer(player, player.CurrentMap),
                    reference, comparison, operand);
                return result.Success && result.Matched;
            },
            (_, label) =>
            {
                callbacks.Add(label);
                callbackSideEffects += 9;
                return callbackSucceeds;
            },
            out LingFengMonsterDropProvider provider,
            out IReadOnlyList<string> errors), string.Join(Environment.NewLine, errors));
        DropInfo drop = Assert.Single(provider.Get("Drops/条件"));
        var player = new PlayerObject { Info = new CharacterInfo { Name = "条件玩家" }, Account = new AccountInfo() };
        player.Info.ScriptVariables.Set(ScriptVariableScope.U, "#23", ScriptVariableValue.FromInteger(5));

        DropRewardInfo matched = drop.AttemptDropWithRandom(_ => 0, (minimum, _) => minimum,
            dropRate: 1, context: new DropAttemptContext("monster", player, null, "Drops/条件"));
        Assert.Same(item, Assert.Single(matched.Items));
        Assert.Equal(["[@条件命中]"], callbacks);
        Assert.Equal(9, callbackSideEffects);
        player.Info.ScriptVariables.Set(ScriptVariableScope.U, "#23", ScriptVariableValue.FromInteger(4));
        Assert.Null(drop.AttemptDropWithRandom(_ => 0, (minimum, _) => minimum,
            dropRate: 1, context: new DropAttemptContext("monster", player, null, "Drops/条件")));
        Assert.Single(callbacks);
        player.Info.ScriptVariables.Set(ScriptVariableScope.U, "#23", ScriptVariableValue.FromInteger(5));
        callbackSucceeds = false;
        Assert.Null(drop.AttemptDropWithRandom(_ => 0, (minimum, _) => minimum,
            dropRate: 1, context: new DropAttemptContext("monster", player, null, "Drops/条件")));
        Assert.Equal(2, callbacks.Count);
    }

    [Fact]
    public void ConditionalChild_支持无随机关键字继承位与OR条件()
    {
        ItemInfo item = new() { Name = "继承条件物品" };
        TextFileDefinition source = new TextFileDefinition("Drops/继承条件")
            .AddLines(["#CHILD 1/1 [N1=5;U2=9|OR,2]", "(", "1/1 继承条件物品", ")"]);

        Assert.True(LingFengMonsterDropProvider.TryCreate(
            [source],
            new Dictionary<string, TextFileDefinition>(StringComparer.Ordinal),
            _ => item,
            (_, reference, _, _) => reference == "U2",
            (_, _) => true,
            out LingFengMonsterDropProvider provider,
            out IReadOnlyList<string> errors), string.Join(Environment.NewLine, errors));

        DropInfo drop = Assert.Single(provider.Get("Drops/继承条件"));
        var player = new PlayerObject { Info = new CharacterInfo { Name = "主人" } };
        var monster = new MonsterObject(new MonsterInfo { Name = "条件怪物" })
        {
            LingFengLastDamageActorKind = LingFengCombatActorKind.Pet,
            LingFengLastDamageVariableInheritanceBit = 2
        };

        DropRewardInfo reward = drop.AttemptDropWithRandom(_ => 0, (minimum, _) => minimum,
            dropRate: 1, context: new DropAttemptContext("monster", player, monster, source.Key));
        Assert.Same(item, Assert.Single(reward.Items));

        monster.LingFengLastDamageVariableInheritanceBit = 4;
        Assert.Null(drop.AttemptDropWithRandom(_ => 0, (minimum, _) => minimum,
            dropRate: 1, context: new DropAttemptContext("monster", player, monster, source.Key)));
    }

    [Fact]
    public void PhysicalEnvir_PublishesMonItemsIntoRealMonsterDropListAndRollsBackInvalidCandidate()
    {
        bool oldTxtEnabled = Settings.TxtScriptsEnabled;
        bool oldCSharpEnabled = Settings.CSharpScriptsEnabled;
        string oldTxtPath = Settings.TxtScriptsPath;
        TxtScriptLayout oldLayout = Settings.TxtScriptsLayout;
        long oldLimit = Settings.TxtScriptsMaxFileBytes;
        string root = Path.Combine(Path.GetTempPath(), "lfenv11-" + Guid.NewGuid().ToString("N"));
        ItemInfo item = new() { Index = 991101, Name = "翎风木剑", Type = ItemType.武器 };
        var envir = new Envir();
        var monster = new MonsterInfo { Index = 991102, Name = "翎风测试鸡" };
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "MonItems"));
            File.WriteAllText(Path.Combine(root, "MonItems", "翎风测试鸡.txt"),
                "1/1 翎风木剑 2\r\n1/1 金币 88\r\n", System.Text.Encoding.UTF8);
            Envir.Main.ItemInfoList.Add(item);
            envir.MonsterInfoList.Add(monster);
            Settings.TxtScriptsEnabled = true;
            Settings.CSharpScriptsEnabled = false;
            Settings.TxtScriptsPath = root;
            Settings.TxtScriptsLayout = TxtScriptLayout.LingFeng;
            Settings.TxtScriptsMaxFileBytes = 4096;

            envir.ApplyPhysicalTextFileDefinitions();
            envir.ReloadDrops();

            Assert.Equal(2, monster.Drops.Count);
            Assert.Contains(monster.Drops, drop => drop.Item == item && drop.Count == 2);
            Assert.Contains(monster.Drops, drop => drop.Gold == 88);
            DropRewardInfo reward = monster.Drops.Single(drop => drop.Item == item)
                .AttemptDropWithRandom(_ => 0, (minimum, _) => minimum, dropRate: 1);
            Assert.Equal(new[] { item, item }, reward.Items);
            IDropTableProvider published = envir.DropTableProvider;

            File.WriteAllText(Path.Combine(root, "MonItems", "翎风测试鸡.txt"),
                "#CHILD 1/1 RANDOM [N1]\r\n(\r\n1/1 翎风木剑\r\n)\r\n", System.Text.Encoding.UTF8);
            InvalidDataException failure = Assert.Throws<InvalidDataException>(
                envir.ApplyPhysicalTextFileDefinitions);

            Assert.Contains("LFENV11-DROP-004", failure.Message, StringComparison.Ordinal);
            Assert.Same(published, envir.DropTableProvider);
            envir.ReloadDrops();
            Assert.Equal(2, monster.Drops.Count);
        }
        finally
        {
            envir.MonsterInfoList.Clear();
            Envir.Main.ItemInfoList.Remove(item);
            Settings.TxtScriptsEnabled = oldTxtEnabled;
            Settings.CSharpScriptsEnabled = oldCSharpEnabled;
            Settings.TxtScriptsPath = oldTxtPath;
            Settings.TxtScriptsLayout = oldLayout;
            Settings.TxtScriptsMaxFileBytes = oldLimit;
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CompositeProvider_RespectsSourcePriorityAndFallbackGate()
    {
        ItemInfo csharpItem = new() { Name = "CSharp掉落" };
        ItemInfo txtItem = new() { Name = "Txt掉落" };
        IDropTableProvider csharp = new SingleDropProvider(csharpItem);
        IDropTableProvider txt = new SingleDropProvider(txtItem);

        Assert.Same(csharpItem, Assert.Single(new CompositeDropTableProvider(
            csharp, txt, true, false, TextFileSourcePriority.CSharpFirst).Get("Drops/测试")).Item);
        Assert.Same(txtItem, Assert.Single(new CompositeDropTableProvider(
            csharp, txt, true, false, TextFileSourcePriority.TxtFirst).Get("Drops/测试")).Item);
        Assert.Null(new CompositeDropTableProvider(
            null, txt, true, false, TextFileSourcePriority.CSharpFirst).Get("Drops/测试"));
        Assert.Same(txtItem, Assert.Single(new CompositeDropTableProvider(
            null, txt, true, true, TextFileSourcePriority.CSharpFirst).Get("Drops/测试")).Item);
        Assert.Same(txtItem, Assert.Single(new CompositeDropTableProvider(
            null, txt, false, false, TextFileSourcePriority.CSharpFirst).Get("Drops/测试")).Item);
    }

    [Theory]
    [InlineData("[Info]\nJob=0\nJob=1", "LFENV11-CONTENT-010")]
    [InlineData("[UseItems]\nUseItems1=木剑\nUseItems1=铁剑", "LFENV11-CONTENT-011")]
    [InlineData("[烈火剑法]\nLevel=1\nLevel=2", "LFENV11-CONTENT-012")]
    public void MonsterContent_RejectsDuplicateKeysInsideOneFile(string text, string expectedCode)
    {
        TextFileDefinition source = new TextFileDefinition(
                "MonsterUseItems/重复怪", "MonUseItems/重复怪.txt", "CP936", "CRLF")
            .AddLines(text.Split('\n'));

        Assert.False(LingFengMonsterContentProvider.TryCreate([source], [],
            out _, out IReadOnlyList<string> errors));
        Assert.Contains(errors, error => error.Contains(expectedCode, StringComparison.Ordinal));
    }

    [Fact]
    public void MonsterContent_将原版DEL填充识别为空槽或物品名终止符()
    {
        TextFileDefinition source = new TextFileDefinition(
                "MonsterUseItems/原版填充怪", "MonUseItems/原版填充怪.txt", "CP936", "CRLF")
            .AddLines(["[Info]", "Job=0", "[UseItems]", "UseItems1=\u007F", "UseItems2=原版法宝\u007F"]);

        Assert.True(LingFengMonsterContentProvider.TryCreate([source], [],
            out LingFengMonsterContentProvider provider, out IReadOnlyList<string> errors),
            string.Join(Environment.NewLine, errors));

        LingFengDependencyRequirement requirement = Assert.Single(
            provider.GetDependencyRequirements(), value =>
                value.Kind == LingFengDependencyKind.ItemName);
        Assert.Equal("原版法宝", requirement.Key);
    }

    [Theory]
    [InlineData(true, "LFENV11-CONTENT-007")]
    [InlineData(false, "LFENV11-CONTENT-008")]
    public void MonsterContent_RejectsSameMonsterFromDifferentSubdirectories(bool useItems, string expectedCode)
    {
        TextFileDefinition first = new TextFileDefinition("Domain/A", $"A/同名怪.{(useItems ? "txt" : "ini")}", "CP936", "CRLF")
            .AddLines(useItems ? ["[Info]", "Job=0"] : ["[ActWalk]", "PlayTime=100"]);
        TextFileDefinition second = new TextFileDefinition("Domain/B", $"B/同名怪.{(useItems ? "txt" : "ini")}", "CP936", "CRLF")
            .AddLines(useItems ? ["[Info]", "Job=1"] : ["[ActWalk]", "PlayTime=200"]);

        Assert.False(LingFengMonsterContentProvider.TryCreate(
            useItems ? [first, second] : [],
            useItems ? [] : [first, second],
            out _, out IReadOnlyList<string> errors));
        Assert.Contains(errors, error => error.Contains(expectedCode, StringComparison.Ordinal));
    }

    [Fact]
    public void MonsterContent_DeduplicatesByteEquivalentSmartMonsterCopies()
    {
        TextFileDefinition first = new TextFileDefinition(
                "Domain/A", "A/同名怪.ini", "CP936", "CRLF")
            .AddLines(["[ActWalk]", "PlayTime=100"]);
        TextFileDefinition second = new TextFileDefinition(
                "Domain/B", "B/同名怪.ini", "CP936", "CRLF")
            .AddLines(["[ActWalk]", "PlayTime=100"]);

        Assert.True(LingFengMonsterContentProvider.TryCreate(
            [], [first, second], out _, out IReadOnlyList<string> errors),
            string.Join(Environment.NewLine, errors));
    }

    [Fact]
    public void PhysicalEnvir_AppliesMonsterEquipmentStatsDropAndSmartSnapshot()
    {
        bool oldTxtEnabled = Settings.TxtScriptsEnabled;
        bool oldCSharpEnabled = Settings.CSharpScriptsEnabled;
        string oldTxtPath = Settings.TxtScriptsPath;
        TxtScriptLayout oldLayout = Settings.TxtScriptsLayout;
        TextFileSourcePriority oldPriority = Settings.TxtScriptsSourcePriority;
        long oldLimit = Settings.TxtScriptsMaxFileBytes;
        float oldDropRate = Settings.DropRate;
        string root = Path.Combine(Path.GetTempPath(), "lfenv11-content-" + Guid.NewGuid().ToString("N"));
        var item = new ItemInfo { Index = 991201, Name = "怪物佩剑", Type = ItemType.武器 };
        item.Stats[Stat.MaxDC] = 7;
        var info = new MonsterInfo { Index = 991202, Name = "装备怪", Stats = new Stats() };
        info.Stats[Stat.HP] = 100;
        var envir = new Envir();
        ScriptManager globalManager = Envir.Main.CSharpScripts;
        FieldInfo registryField = typeof(ScriptManager).GetField(
            "_currentRegistry", BindingFlags.Instance | BindingFlags.NonPublic)!;
        ScriptRegistry oldRegistry = globalManager.CurrentRegistry;
        bool oldManagerEnabled = globalManager.Enabled;
        using var isolatedRegistryOwner = new ScriptManager();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "MonUseItems"));
            Directory.CreateDirectory(Path.Combine(root, "SmartMonster"));
            File.WriteAllText(Path.Combine(root, "MonUseItems", "装备怪.txt"),
                "[Info]\r\nJob=0\r\nDropUseItem=1\r\nDropUseItemRate=1\r\nRunWithAttack=1\r\nRunWithAttackRate=5\r\nUseSkill=烈火剑法\r\n[UseItems]\r\nUseItems1=怪物佩剑\r\n[烈火剑法]\r\nLevel=3\r\nNewLevel=0\r\n");
            File.WriteAllText(Path.Combine(root, "SmartMonster", "装备怪.ini"),
                "[ActWalk]\r\nPlayTime=125\r\n[ActDefAttack]\r\nPlayTime=100\r\n");
            envir.ItemInfoList.Add(item);
            envir.MonsterInfoList.Add(info);
            Settings.TxtScriptsEnabled = true;
            Settings.CSharpScriptsEnabled = true;
            Settings.TxtScriptsSourcePriority = TextFileSourcePriority.CSharpFirst;
            Settings.TxtScriptsPath = root;
            Settings.TxtScriptsLayout = TxtScriptLayout.LingFeng;
            Settings.TxtScriptsMaxFileBytes = 4096;
            Settings.DropRate = 1;
            registryField.SetValue(globalManager, isolatedRegistryOwner.CurrentRegistry);
            typeof(ScriptManager).GetProperty(nameof(ScriptManager.Enabled), BindingFlags.Instance | BindingFlags.Public)!
                .SetValue(globalManager, true);
            globalManager.CurrentRegistry.RegisterOnMonsterDropBefore((_, _, _) => { });

            envir.ApplyPhysicalTextFileDefinitions();

            LingFengMonsterContentSnapshot content = Assert.IsType<LingFengMonsterContentSnapshot>(info.LingFengContent);
            Assert.Same(item, Assert.Single(content.Equipment));
            Assert.True(content.RunWithAttack);
            Assert.Equal("125", content.SmartSections["ActWalk"]["PlayTime"]);
            Assert.Equal(3, Assert.Single(content.Skills).Level);

            var player = new PlayerObject
            {
                Info = new CharacterInfo { Name = "装备掉落归属" },
                Account = new AccountInfo(),
                Stats = new Stats()
            };
            var monster = new TestMonster(info)
            {
                CurrentMap = TestMap(),
                CurrentLocation = Point.Empty,
                EXPOwner = player
            };
            monster.RefreshAll();
            Assert.Equal(7, monster.Stats[Stat.MaxDC]);
            monster.Die();
            Assert.Contains(monster.CurrentMap.GetCell(Point.Empty).Objects,
                value => value is ItemObject drop && drop.Item.Info == item);
        }
        finally
        {
            Settings.TxtScriptsEnabled = oldTxtEnabled;
            Settings.CSharpScriptsEnabled = oldCSharpEnabled;
            Settings.TxtScriptsPath = oldTxtPath;
            Settings.TxtScriptsLayout = oldLayout;
            Settings.TxtScriptsSourcePriority = oldPriority;
            Settings.TxtScriptsMaxFileBytes = oldLimit;
            Settings.DropRate = oldDropRate;
            registryField.SetValue(globalManager, oldRegistry);
            typeof(ScriptManager).GetProperty(nameof(ScriptManager.Enabled), BindingFlags.Instance | BindingFlags.Public)!
                .SetValue(globalManager, oldManagerEnabled);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void MonsterContent_HotPublishMakesExistingMonsterStatsAndDropsUseSameSnapshot()
    {
        var sword = new ItemInfo { Name = "热更木剑", Type = ItemType.武器 };
        sword.Stats[Stat.MaxDC] = 7;
        var blade = new ItemInfo { Name = "热更铁剑", Type = ItemType.武器 };
        blade.Stats[Stat.MaxDC] = 13;
        var info = new MonsterInfo { Name = "热更怪", Stats = new Stats() };
        var monster = new TestMonster(info) { CurrentMap = TestMap(), CurrentLocation = Point.Empty };
        monster.RefreshAll();

        LingFengMonsterContentProvider first = CreateContentProvider("热更木剑");
        Assert.Empty(first.Apply([info], name => name == sword.Name ? sword : null));
        monster.InvokeDrop();
        Assert.Equal(7, monster.Stats[Stat.MaxDC]);
        Assert.Contains(monster.CurrentMap.GetCell(Point.Empty).Objects,
            value => value is ItemObject item && item.Item.Info == sword);

        LingFengMonsterContentProvider second = CreateContentProvider("热更铁剑");
        Assert.Empty(second.Apply([info], name => name == blade.Name ? blade : null));
        monster.InvokeDrop();
        Assert.Equal(13, monster.Stats[Stat.MaxDC]);
        Assert.Contains(monster.CurrentMap.GetCell(Point.Empty).Objects,
            value => value is ItemObject item && item.Item.Info == blade);

        Assert.True(LingFengMonsterContentProvider.TryCreate([], [],
            out LingFengMonsterContentProvider empty, out IReadOnlyList<string> errors),
            string.Join(Environment.NewLine, errors));
        Assert.Empty(empty.Apply([info], _ => null));
        monster.InvokeDrop();
        Assert.Equal(0, monster.Stats[Stat.MaxDC]);
        Assert.Null(info.LingFengContent);
    }

    [Theory]
    [InlineData(@"D:\ChuanQi\服务端\01酷明传奇\MirServer_01\Mir200\Envir", "LFENV-ROOT-0002")]
    public void RepresentativeEnvir_StrictlyBuildsMonsterDomainCandidate(string root, string rootId)
    {
        if (!Directory.Exists(root))
            throw Xunit.Sdk.SkipException.ForSkip($"本机未挂载 {rootId} 代表语料。");

        var provider = new PhysicalTextFileProvider(new PhysicalTextFileProviderOptions(
            root, TxtScriptLayout.LingFeng) { MaxFileBytes = 2 * 1024 * 1024 });

        Assert.NotNull(provider.MonsterDropProvider);
        Assert.NotNull(provider.MonsterContentProvider);
        Assert.NotNull(provider.WorldContentProvider);
    }

    [Fact]
    public void PhysicalProvider_UsesCanonicalFilenameForWhitespaceAliasAndReportsIt()
    {
        string root = Path.Combine(Path.GetTempPath(), "lfenv11-alias-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "MonItems"));
            File.WriteAllText(Path.Combine(root, "MonItems", "别名怪 .txt"), "1/9 旧物品");
            File.WriteAllText(Path.Combine(root, "MonItems", "别名怪.txt"), "1/1 新物品");

            var provider = new PhysicalTextFileProvider(new PhysicalTextFileProviderOptions(
                root, TxtScriptLayout.LingFeng));

            Assert.Contains("LFENV11-DROP-ALIAS", Assert.Single(provider.DomainDiagnostics), StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private sealed class SingleDropProvider(ItemInfo item) : IDropTableProvider
    {
        public IReadOnlyList<DropInfo> Get(string key) => [new DropInfo { Chance = 1, Item = item }];
    }

    private sealed class TestMonster(MonsterInfo info) : MonsterObject(info)
    {
        public void InvokeDrop() => Drop();
    }

    private static LingFengMonsterContentProvider CreateContentProvider(string itemName)
    {
        TextFileDefinition source = new TextFileDefinition(
                "MonsterUseItems/热更怪", "MonUseItems/热更怪.txt", "CP936", "CRLF")
            .AddLines(["[Info]", "DropUseItem=1", "DropUseItemRate=1", "[UseItems]", $"UseItems1={itemName}"]);
        Assert.True(LingFengMonsterContentProvider.TryCreate([source], [],
            out LingFengMonsterContentProvider provider, out IReadOnlyList<string> errors),
            string.Join(Environment.NewLine, errors));
        return provider;
    }

    private static Map TestMap()
    {
        var map = new Map(new MapInfo { Index = 991203 })
        {
            Width = 1,
            Height = 1,
            Cells = new Cell[1, 1]
        };
        map.Cells[0, 0] = new Cell { Attribute = CellAttribute.Walk };
        return map;
    }
}
