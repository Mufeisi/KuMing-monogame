using Server.MirDatabase;
using Server.MirEnvir;
using Server.MirObjects;
using Server.MirNetwork;
using Server.Persistence.Sql;
using Server.Scripting;
using Server;
using System.Text;
using System.Runtime.CompilerServices;
using Xunit;

namespace Base05.Tests;

[Collection(nameof(PhysicalTextFileProviderCollection))]
public sealed class LingFengCommerceContentProviderTests
{
    [Fact]
    public void CommerceAndRuleListCandidatesKeepSeparateImmutableFacts()
    {
        TextFileDefinition recipes = Definition("Commerce/MakeItem", "Makeitem.txt",
            "[灰色药粉(少量)]",
            "食人树叶\t4",
            "毒蜘蛛牙齿 2");
        TextFileDefinition shop = Definition("Commerce/ShopItems", "Shopitemlist.txt",
            "2\t黑铁矿石\t284\t20|0\t380\t1\t沙巴克炼武器|1元宝10块|\t1\t1\t99",
            "0\t随机传送石\t16123\t1000000|1\t380\t1\t随机传送|\t1\t1\t40");
        TextFileDefinition deny = Definition("RuleLists/DenyAccountList", "Denyaccountlist.txt",
            "; 注释", "测试账号", "第二账号");

        Assert.True(LingFengCommerceContentProvider.TryCreate(
            shop, recipes, out LingFengCommerceContentProvider provider,
            out IReadOnlyList<string> errors), string.Join(Environment.NewLine, errors));
        Assert.True(LingFengRuleListContentProvider.TryCreate(
            [deny], out LingFengRuleListContentProvider ruleProvider, out errors),
            string.Join(Environment.NewLine, errors));

        recipes.SetLines(["[不应进入快照]"]);
        deny.SetLines(["不应进入快照"]);
        LingFengRecipeDefinition recipe = Assert.Single(provider.Recipes);
        Assert.Equal("灰色药粉(少量)", recipe.OutputItemName);
        Assert.Collection(recipe.Ingredients,
            ingredient => Assert.Equal(("食人树叶", (ushort)4), (ingredient.ItemName, ingredient.Count)),
            ingredient => Assert.Equal(("毒蜘蛛牙齿", (ushort)2), (ingredient.ItemName, ingredient.Count)));
        Assert.Equal(2, provider.ShopProducts.Count);
        Assert.Equal(LingFengShopCurrency.Credit, provider.ShopProducts[0].Currency);
        Assert.Equal((uint)20, provider.ShopProducts[0].UnitPrice);
        Assert.Equal(LingFengShopCurrency.Gold, provider.ShopProducts[1].Currency);
        Assert.Equal(["测试账号", "第二账号"], ruleProvider.Lists["rulelists/denyaccountlist"]);
        Assert.Empty(provider.CompatibilityDiagnostics);
    }

    [Fact]
    public void DuplicateRuleListLogicKeyFailsWholeCandidate()
    {
        Assert.False(LingFengRuleListContentProvider.TryCreate(
            [
                Definition("RuleLists/DenyAccountList", "A.txt", "A"),
                Definition("RuleLists/DenyAccountList", "B.txt", "B")
            ], out _, out IReadOnlyList<string> errors));
        Assert.Contains(errors, value => value.Contains("LFENV13-RULE-DUPLICATE", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("[成品]\n材料 1\n[成品]\n材料 2", "LFENV13-RECIPE-DUPLICATE")]
    [InlineData("[成品]\n材料 0", "LFENV13-RECIPE-SYNTAX")]
    public void DuplicateOrInvalidRecipeFailsWholeCandidate(string source, string expectedCode)
    {
        Assert.False(LingFengCommerceContentProvider.TryCreate(
            null, Definition("Commerce/MakeItem", "Makeitem.txt", source.Split('\n')),
            out _, out IReadOnlyList<string> errors));
        Assert.Contains(errors, error => error.Contains(expectedCode, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("0\t物品\t1\t10|9\t380\t1\t说明\t1\t1\t1", "LFENV13-SHOP-CURRENCY")]
    [InlineData("0\t物品\t1\t10|0\t380\t0\t说明\t1\t1\t1", "LFENV13-SHOP-SYNTAX")]
    public void InvalidShopRowFailsWholeCandidate(string source, string expectedCode)
    {
        Assert.False(LingFengCommerceContentProvider.TryCreate(
            Definition("Commerce/ShopItems", "Shopitemlist.txt", source), null,
            out _, out IReadOnlyList<string> errors));
        Assert.Contains(errors, error => error.Contains(expectedCode, StringComparison.Ordinal));
    }

    [Fact]
    public void NonItemAndUnsupportedCurrencyProductsRemainFactsWithStableCompatibilityDiagnostics()
    {
        Assert.True(LingFengCommerceContentProvider.TryCreate(
            Definition("Commerce/ShopItems", "Shopitemlist.txt",
                "0\t服务商品\t0\t1|1\t380\t1\t说明\t1\t0\t1",
                "0\t三号货币商品\t2\t10|3\t380\t1\t说明\t1\t0\t1",
                "0\t特殊货币商品\t1\t10|4\t380\t1\t说明\t1\t0\t1"),
            null, out LingFengCommerceContentProvider provider,
            out IReadOnlyList<string> errors), string.Join(Environment.NewLine, errors));

        Assert.Equal(3, provider.ShopProducts.Count);
        Assert.Contains(provider.CompatibilityDiagnostics,
            value => value.StartsWith("LFENV13-SHOP-VIRTUAL", StringComparison.Ordinal));
        Assert.Contains(provider.CompatibilityDiagnostics,
            value => value.StartsWith("LFENV13-SHOP-CURRENCY3", StringComparison.Ordinal));
        Assert.Contains(provider.CompatibilityDiagnostics,
            value => value.StartsWith("LFENV13-SHOP-CURRENCY4", StringComparison.Ordinal));
        Assert.Contains(provider.GetDependencyRequirements(), requirement =>
            requirement.Level == LingFengDependencyLevel.E2 &&
            requirement.Key == "LingFeng/ShopCurrency/3");
        Assert.Contains(provider.GetDependencyRequirements(), requirement =>
            requirement.Level == LingFengDependencyLevel.E2 &&
            requirement.Key == "LingFeng/ShopCurrency/4");
    }

    [Fact]
    public void CompositeCommerceProvidersHonorRuntimePriorityAndFallbackPolicy()
    {
        var csharpRecipe = new RecipeDefinition("Recipe/冲突") { OutputItemName = "C#" };
        var txtRecipe = new RecipeDefinition("Recipe/冲突") { OutputItemName = "TXT" };
        var txtOnlyRecipe = new RecipeDefinition("Recipe/仅TXT") { OutputItemName = "仅TXT" };
        var csharpRecipes = new CSharpRecipeProvider(new Dictionary<string, RecipeDefinition>
        {
            [csharpRecipe.Key] = csharpRecipe
        });
        var txtRecipes = new CSharpRecipeProvider(new Dictionary<string, RecipeDefinition>
        {
            [txtRecipe.Key] = txtRecipe,
            [txtOnlyRecipe.Key] = txtOnlyRecipe
        });

        var blockedFallback = new CompositeRecipeProvider(
            csharpRecipes, txtRecipes, true, false, TextFileSourcePriority.CSharpFirst);
        Assert.Same(csharpRecipe, Assert.Single(blockedFallback.GetAll()));
        Assert.Null(blockedFallback.GetByKey(txtOnlyRecipe.Key));

        var txtFirst = new CompositeRecipeProvider(
            csharpRecipes, txtRecipes, true, false, TextFileSourcePriority.TxtFirst);
        Assert.Same(txtRecipe, txtFirst.GetByKey(txtRecipe.Key));
        Assert.Same(txtOnlyRecipe, txtFirst.GetByKey(txtOnlyRecipe.Key));

        var csharpList = new NameListDefinition("NameLists/冲突").Add("C#");
        var txtList = new NameListDefinition("NameLists/冲突").Add("TXT");
        var mergedLists = new CompositeNameListProvider(
            new CSharpNameListProvider(new Dictionary<string, NameListDefinition>
                { [csharpList.Key] = csharpList }),
            new CSharpNameListProvider(new Dictionary<string, NameListDefinition>
                { [txtList.Key] = txtList }),
            true, true, TextFileSourcePriority.TxtFirst);
        Assert.Contains("TXT", mergedLists.GetByKey("NameLists/冲突").Values);
    }

    [Fact]
    public void DependencyBuildIsAtomicAndProducesExistingRecipeAndGameShopModels()
    {
        TextFileDefinition recipes = Definition("Commerce/MakeItem", "Makeitem.txt",
            "[成品]", "材料 2");
        TextFileDefinition shop = Definition("Commerce/ShopItems", "Shopitemlist.txt",
            "0\t成品\t100\t50|1\t380\t1\t说明\t1\t1\t7",
            "0\t成品\t100\t1|1\t380\t1\t停用商品\t0\t0\t0");
        Assert.True(LingFengCommerceContentProvider.TryCreate(shop, recipes,
            out LingFengCommerceContentProvider provider, out IReadOnlyList<string> errors));
        var output = new ItemInfo { Index = 100, Name = "成品", StackSize = 10 };
        var ingredient = new ItemInfo { Index = 101, Name = "材料", StackSize = 10 };

        Assert.True(provider.TryBuildSnapshot(
            name => name == "成品" ? output : name == "材料" ? ingredient : null,
            index => index == 100 ? output : null,
            out LingFengCommerceSnapshot snapshot, out errors), string.Join(Environment.NewLine, errors));
        RecipeDefinition recipe = Assert.Single(snapshot.RecipeProvider.GetAll());
        Assert.Equal("recipe/成品", recipe.Key);
        Assert.Equal((ushort)2, Assert.Single(recipe.Ingredients).Count);
        GameShopItem product = Assert.Single(snapshot.GameShopItems);
        Assert.Same(output, product.Info);
        Assert.True(product.CanBuyGold);
        Assert.Equal((uint)50, product.GoldPrice);
        Assert.Equal(7, product.Stock);

        Assert.False(provider.TryBuildSnapshot(
            name => name == "成品" ? output : null,
            index => index == 100 ? output : null,
            out _, out errors));
        Assert.Contains(errors, error => error.Contains("材料", StringComparison.Ordinal));
        Assert.Equal((uint)50, product.GoldPrice);
    }

    [Fact]
    public void PhysicalCandidatePublishesRecipesRulesAndShopTogetherAndRollsBackInvalidReload()
    {
        bool oldTxtEnabled = Settings.TxtScriptsEnabled;
        bool oldCSharpEnabled = Settings.CSharpScriptsEnabled;
        string oldPath = Settings.TxtScriptsPath;
        TxtScriptLayout oldLayout = Settings.TxtScriptsLayout;
        string root = Path.Combine(Path.GetTempPath(), "lyo-lfenv13-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var envir = new Envir();
        var output = new ItemInfo { Index = 100, Name = "成品", StackSize = 10 };
        var ingredient = new ItemInfo { Index = 101, Name = "材料", StackSize = 10 };
        envir.ItemInfoList.AddRange([output, ingredient]);
        try
        {
            Write(root, "Makeitem.txt", "[成品]\r\n材料 2\r\n");
            Write(root, "Shopitemlist.txt", "0\t成品\t100\t50|1\t380\t1\t说明\t1\t1\t7\r\n");
            Write(root, "Denyaccountlist.txt", "测试账号\r\n");
            Settings.CSharpScriptsEnabled = false;
            Settings.TxtScriptsEnabled = true;
            Settings.TxtScriptsPath = root;
            Settings.TxtScriptsLayout = TxtScriptLayout.LingFeng;

            envir.ApplyPhysicalTextFileDefinitions();

            LingFengCommerceContentProvider published = envir.PhysicalCommerceContentProvider;
            Assert.NotNull(published);
            RecipeInfo recipe = Assert.Single(envir.RecipeInfoList);
            Assert.Same(output, recipe.Item.Info);
            Assert.Equal((ushort)2, Assert.Single(recipe.Ingredients).Count);
            Assert.Contains("测试账号", envir.NameListProvider.GetByKey("NameLists/DenyAccountList").Values);
            Assert.True(envir.NameListContains("DenyAccountList", "测试账号"));
            GameShopItem product = Assert.Single(envir.ActiveGameShopList);
            Assert.Same(output, product.Info);
            Assert.Equal((uint)50, product.GoldPrice);
            Assert.Empty(envir.GameShopList);
            Assert.Empty(SqlWorldRelationsStore.Capture(envir).GameShopItems);

            Write(root, "Makeitem.txt", "[成品]\r\n缺失材料 1\r\n");
            InvalidDataException failure = Assert.Throws<InvalidDataException>(
                envir.ApplyPhysicalTextFileDefinitions);
            Assert.Contains("缺失材料", failure.Message, StringComparison.Ordinal);
            Assert.Same(published, envir.PhysicalCommerceContentProvider);
            Assert.Same(output, Assert.Single(envir.RecipeInfoList).Item.Info);
            Assert.Equal((uint)50, Assert.Single(envir.ActiveGameShopList).GoldPrice);
        }
        finally
        {
            Settings.TxtScriptsEnabled = false;
            envir.ApplyPhysicalTextFileDefinitions();
            Settings.CSharpScriptsEnabled = oldCSharpEnabled;
            Settings.TxtScriptsPath = oldPath;
            Settings.TxtScriptsLayout = oldLayout;
            Settings.TxtScriptsEnabled = oldTxtEnabled;
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void RealGameShopPurchaseChargesAndDeliversOnceAndEnforcesIndividualStock()
    {
        var item = new ItemInfo
        {
            Index = 913001,
            Name = "LFENV13商城物品",
            StackSize = 1
        };
        var product = new GameShopItem
        {
            ItemIndex = item.Index,
            GIndex = 913001,
            Info = item,
            GoldPrice = 10,
            Count = 2,
            Stock = 1,
            iStock = true,
            CanBuyGold = true
        };
        var player = new SilentPlayer
        {
            Info = new CharacterInfo { Index = 913001, Name = "LFENV13购买人物" },
            Account = new AccountInfo { Gold = 100 }
        };
        player.Report = new Reporting(player);
        ulong oldNextItemId = Envir.Main.NextUserItemID;
        ulong oldNextMailId = Envir.Main.NextMailID;
        try
        {
            Envir.Main.ItemInfoList.Add(item);
            Envir.Main.GameShopList.Add(product);

            var poorPlayer = new SilentPlayer
            {
                Info = new CharacterInfo { Index = 913002, Name = "LFENV13余额不足人物" },
                Account = new AccountInfo { Gold = 9 }
            };
            poorPlayer.Report = new Reporting(poorPlayer);
            poorPlayer.GameshopBuy(product.GIndex, 1, 1);
            Assert.Equal((uint)9, poorPlayer.Account.Gold);
            Assert.Empty(poorPlayer.Info.Mail);
            Assert.Empty(poorPlayer.Info.GSpurchases);

            player.GameshopBuy(product.GIndex, 1, 1);

            Assert.Equal((uint)90, player.Account.Gold);
            MailInfo mail = Assert.Single(player.Info.Mail);
            Assert.Equal(2, mail.Items.Count);
            Assert.All(mail.Items, value => Assert.Same(item, value.Info));
            Assert.Equal(1, player.Info.GSpurchases[item.Index]);
            Assert.Equal(1, Envir.Main.GameshopLog[item.Index]);

            player.GameshopBuy(product.GIndex, 1, 1);

            Assert.Equal((uint)90, player.Account.Gold);
            Assert.Single(player.Info.Mail);
            Assert.Equal(1, player.Info.GSpurchases[item.Index]);
        }
        finally
        {
            Envir.Main.ItemInfoList.Remove(item);
            Envir.Main.GameShopList.Remove(product);
            Envir.Main.GameshopLog.Remove(item.Index);
            Envir.Main.NextUserItemID = oldNextItemId;
            Envir.Main.NextMailID = oldNextMailId;
        }
    }

    [Fact]
    public void PhysicalRecipeRunsThroughRealNpcCraftAndInvalidSlotCannotPartiallyConsume()
    {
        bool oldTxtEnabled = Settings.TxtScriptsEnabled;
        bool oldCSharpEnabled = Settings.CSharpScriptsEnabled;
        string oldPath = Settings.TxtScriptsPath;
        TxtScriptLayout oldLayout = Settings.TxtScriptsLayout;
        string root = Path.Combine(Path.GetTempPath(), "lyo-lfenv13-craft-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Market_Def"));
        var output = new ItemInfo { Index = 913011, Name = "LFENV13成品", StackSize = 10 };
        var ingredientInfo = new ItemInfo { Index = 913012, Name = "LFENV13材料", StackSize = 10 };
        NPCScript script = null;
        ulong oldNextItemId = Envir.Main.NextUserItemID;
        ulong oldNextMailId = Envir.Main.NextMailID;
        try
        {
            Write(root, "Makeitem.txt", "[LFENV13成品]\r\nLFENV13材料 2\r\n");
            Write(root, "Shopitemlist.txt",
                "0\tLFENV13成品\t913011\t10|1\t380\t1\t说明\t1\t1\t2\r\n");
            Write(root, "Market_Def/LFENV13工匠.txt", "[RECIPE]\r\nLFENV13成品\r\n[@MAIN]\r\n");
            Envir.Main.ItemInfoList.AddRange([output, ingredientInfo]);
            Settings.CSharpScriptsEnabled = false;
            Settings.TxtScriptsEnabled = true;
            Settings.TxtScriptsPath = root;
            Settings.TxtScriptsLayout = TxtScriptLayout.LingFeng;
            Envir.Main.ApplyPhysicalTextFileDefinitions();
            Assert.NotNull(Envir.Main.RecipeProvider);
            Assert.Single(Envir.Main.RecipeProvider.GetAll());
            Assert.Single(Envir.Main.RecipeInfoList);
            GameShopItem physicalProduct = Assert.Single(Envir.Main.ActiveGameShopList);
            var buyer = new SilentPlayer
            {
                Info = new CharacterInfo { Index = 913015, Name = "LFENV13物理商城人物" },
                Account = new AccountInfo { Gold = 20 }
            };
            buyer.Report = new Reporting(buyer);
            buyer.GameshopBuy(physicalProduct.GIndex, 1, 1);
            Assert.Equal((uint)10, buyer.Account.Gold);
            Assert.Same(output, Assert.Single(Assert.Single(buyer.Info.Mail).Items).Info);
            Assert.NotNull(Envir.Main.TextFileProvider.GetByKey("NPCs/LFENV13工匠"));
            script = NPCScript.GetOrAdd(913013, "LFENV13工匠", NPCScriptType.Normal);
            RecipeInfo recipe = Assert.Single(script.CraftGoods);

            var rejectedPlayer = PlayerWithIngredient(ingredientInfo, 2);
            script.Craft(rejectedPlayer, recipe.Item.UniqueID, 1, [rejectedPlayer.Info.Inventory.Length]);
            Assert.Equal((ushort)2, rejectedPlayer.Info.Inventory[0].Count);
            Assert.DoesNotContain(rejectedPlayer.Info.Inventory,
                item => item?.Info == output);

            var player = PlayerWithIngredient(ingredientInfo, 2);
            MirConnection connection = (MirConnection)RuntimeHelpers.GetUninitializedObject(typeof(MirConnection));
            connection.SentItemInfo = [output];
            connection.SentHeroInfo = [];
            player.Connection = connection;
            script.Craft(player, recipe.Item.UniqueID, 1, [0]);
            Assert.Null(player.Info.Inventory[0]);
            UserItem crafted = Assert.Single(player.Info.Inventory, item => item?.Info == output);
            Assert.Equal((ushort)1, crafted.Count);
        }
        finally
        {
            if (script != null) Envir.Main.Scripts.Remove(script.ScriptID);
            Settings.TxtScriptsEnabled = false;
            Envir.Main.ApplyPhysicalTextFileDefinitions();
            Envir.Main.ItemInfoList.Remove(output);
            Envir.Main.ItemInfoList.Remove(ingredientInfo);
            Envir.Main.GameshopLog.Remove(output.Index);
            Envir.Main.NextUserItemID = oldNextItemId;
            Envir.Main.NextMailID = oldNextMailId;
            Settings.CSharpScriptsEnabled = oldCSharpEnabled;
            Settings.TxtScriptsPath = oldPath;
            Settings.TxtScriptsLayout = oldLayout;
            Settings.TxtScriptsEnabled = oldTxtEnabled;
            Envir.Main.ApplyPhysicalTextFileDefinitions();
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void CommerceOnlyReloadChangesSnapshotDigestAndReportsDomainKey()
    {
        string root = Path.Combine(Path.GetTempPath(), "lyo-lfenv13-reload-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Write(root, "Makeitem.txt", "[成品]\r\n材料 1\r\n");
            Write(root, "Enablemakeitem.txt", "成品\r\n");
            Write(root, "Myshopitems.txt", "成品\r\n");
            var options = new PhysicalTextFileProviderOptions(root, TxtScriptLayout.LingFeng)
            {
                MaxFileBytes = 4096
            };
            using var coordinator = new TxtScriptReloadCoordinator(options, 0, _ => true);

            TxtScriptReloadResult first = coordinator.ReloadNow();
            Write(root, "Makeitem.txt", "[成品]\r\n材料 2\r\n");
            TxtScriptReloadResult second = coordinator.ReloadNow();

            Assert.True(first.Published);
            Assert.True(second.Published);
            Assert.Contains("commerce/makeitem", first.Snapshot.Keys);
            Assert.Contains("rulelists/enablemakeitem", first.Snapshot.Keys);
            Assert.Contains("rulelists/myshopitems", first.Snapshot.Keys);
            Assert.NotEqual(first.Snapshot.Digest, second.Snapshot.Digest);
            Assert.Equal(["commerce/makeitem"], second.Snapshot.ChangedKeys);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static TextFileDefinition Definition(string key, string path, params string[] lines) =>
        new TextFileDefinition(key, path, "CP936", "CRLF").AddLines(lines);

    private static void Write(string root, string name, string text)
    {
        string path = Path.Combine(root, name.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text, new UTF8Encoding(false));
    }

    private static SilentPlayer PlayerWithIngredient(ItemInfo ingredientInfo, ushort count)
    {
        var player = new SilentPlayer
        {
            Info = new CharacterInfo { Index = 913014, Name = "LFENV13合成人物" },
            Account = new AccountInfo(),
            Stats = new Stats()
        };
        player.Info.Inventory[0] = new UserItem(ingredientInfo)
        {
            UniqueID = ++Envir.Main.NextUserItemID,
            Count = count
        };
        return player;
    }

    private sealed class SilentPlayer : PlayerObject
    {
        public override void Enqueue(Packet packet) { }
        public override void Broadcast(Packet packet) { }
    }
}
