using System.Text;
using Server;
using Server.MirDatabase;
using Server.MirEnvir;
using Server.Scripting;
using Xunit;

namespace Base05.Tests;

[Collection(nameof(PhysicalTextFileProviderCollection))]
public sealed class LingFengExternalDependencyManifestTests
{
    [Fact]
    public void E1与E2分层评估且缺失项保持结构化只读快照()
    {
        var manifest = new LingFengExternalDependencyManifest(new[]
        {
            new LingFengDependencyRequirement(LingFengDependencyKind.ItemName, "金条",
                LingFengDependencyLevel.E1, "monitems/稻草人"),
            new LingFengDependencyRequirement(LingFengDependencyKind.Monster, "稻草人",
                LingFengDependencyLevel.E1, "world/mongen"),
            new LingFengDependencyRequirement(LingFengDependencyKind.ClientContract, "MonIcons/怪物图标.txt",
                LingFengDependencyLevel.E2, "MonIcons/怪物图标.txt"),
            new LingFengDependencyRequirement(LingFengDependencyKind.DomainAdapter, "Market_Upg/升级.upg",
                LingFengDependencyLevel.E2, "Market_Upg/升级.upg")
        });
        var probe = new LingFengDependencyProbe(
            value => value == "金条", _ => false, _ => false, _ => false, _ => false, _ => false);

        LingFengDependencyReport e1 = manifest.Evaluate(LingFengDependencyLevel.E1, probe);
        LingFengDependencyReport e2 = manifest.Evaluate(LingFengDependencyLevel.E2, probe);

        Assert.Single(e1.Satisfied);
        Assert.Equal("稻草人", Assert.Single(e1.Missing).Key);
        Assert.Equal(3, e2.Missing.Count);
        Assert.Contains(e2.Missing, value => value.Kind == LingFengDependencyKind.ClientContract);
        Assert.Contains(e2.Missing, value => value.Kind == LingFengDependencyKind.DomainAdapter);
        Assert.Throws<NotSupportedException>(() =>
            ((IList<LingFengDependencyRequirement>)e2.Missing).Clear());
        Assert.Throws<NotSupportedException>(() =>
            ((IList<LingFengDependencyRequirement>)manifest.Requirements).Clear());
    }

    [Fact]
    public void 物理候选把客户端契约与未接管领域文件列入E2而不送入脚本解释器()
    {
        string root = TempRoot();
        try
        {
            Write(root, "Market_Def/老兵.txt", "[@MAIN]\r\n欢迎");
            Write(root, "MonIcons/怪物图标.txt", "1 100");
            Write(root, "Market_Upg/升级.upg", "二进制领域契约");
            Write(root, "Market_Prices/商店.prc", "运行态二进制领域契约");

            var provider = new PhysicalTextFileProvider(
                new PhysicalTextFileProviderOptions(root, TxtScriptLayout.LingFeng));

            Assert.Single(provider.GetAll());
            Assert.Equal(3, provider.ExternalDependencyManifest.Requirements.Count);
            Assert.Equal(new LingFengEnvirPreflightSummary(1, 0, 3, 0), provider.PreflightSummary);
            Assert.Contains(provider.ExternalDependencyManifest.Requirements, value =>
                value.Kind == LingFengDependencyKind.ClientContract && value.Key == "MonIcons/怪物图标.txt");
            Assert.Contains(provider.ExternalDependencyManifest.Requirements, value =>
                value.Kind == LingFengDependencyKind.DomainAdapter && value.Key == "Market_Upg/升级.upg");
            Assert.Contains(provider.ExternalDependencyManifest.Requirements, value =>
                value.Kind == LingFengDependencyKind.DomainAdapter && value.Key == "Market_Prices/商店.prc");
            Assert.Null(provider.GetByKey("MonIcons/怪物图标"));
            Assert.Null(provider.GetByKey("Market_Upg/升级"));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void 脚本静态物品怪物地图引用进入E1而动态引用不伪造依赖()
    {
        string root = TempRoot();
        try
        {
            Write(root, "Market_Def/依赖.txt",
                "[@MAIN]\r\n#IF\r\nCHECKITEM 金条 1\r\nCHECKMAP 0\r\nCHECKACCOUNTLIST ..\\..\\..\\通区充值\\账号.txt\r\n#ACT\r\nMONGEN 稻草人 1\r\nGIVE <$动态物品> 1\r\nSETARRBUFF 1 1 1 130 10 3 130 4 测试\r\n<$CURRRTARGETNAME>.SETARRBUFF 1 1 1 130 10 3 130 4 目标测试\r\nADDBUTTON 3 1 283 284 285 10 200 1 -1\r\nSETICON 2 39 199 0 -30 10 0 0 250\r\nSETSNDACASKET 1\r\nACTIVATIONCASKET\r\nSETUPGRADEITEM BoxItem1\r\nOPENITEMBOXEX 91 1 放入物品\r\nCHANGEITEMNAME 1 新名字\r\nSETBODYCOLOR 151 120 1\r\nEXTBAGPAGECOUNT + 5\r\nEXTBAGOPENITEMCOUNT + 20\r\nSETBIGSTORAGECOUNT + 49\r\nOPENAUTOPICKITEM 1 0 5 1 0 0 1000\r\nCLOSEAUTOPICKITEM\r\nOPENBIGDIALOGBOX 3 216 1 4 0 -65 1 720 10\r\nOPENMERCHANTBIGDLG 1 653 1 4 0 -65 1 480 0 1\r\nOPENITEMBOX 稻草人\r\nBREAKADDSELLPLAYER\r\nSTOPTAKEON\r\nSETITEMFROM -1 0 2\r\nHCALL 目标人物 @目标页\r\nADDATTACKSABUKALL 0\r\nAUTOTAKEONITEM 命格装备 2\r\nCHANGEHUMNAME 新名字\r\nCREATEMYSHOP 命格商店\r\nOPENGODBLESS 0\r\nPLAYSOUNDEXT WAV\\8200-6.wav 1 0\r\nSETOFFLINEPLAY ON\r\nSETRANKLEVELNAME 命格榜首\r\nSHOWGODBLESS 1\r\nSTARTAUTOPLAYGAME\r\nSTOPAUTOPLAYGAME\r\nSTOPBUYUSER\r\nSTOPTAKEOFF\r\nSUPERMOVEMSG 0 9 0 16 200 1 命格公告\r\nTAKEPOSW 17\r\nUNALLOWITEMINTOBOX\r\nRETURNBOXITEM 0\r\nCHANGESLAVEABILITY 9 99 稻草人\r\nRECALCSLAVEABILITY 稻草人\r\nTAKEBAGITEM 金条 1 0 0 0 0 N1 0\r\nADDNAMEDATETIMELIST ..\\QuestDiary\\会员名单.txt 30 0 0\r\nADDACCOUNTLIST ..\\..\\..\\通区充值\\账号.txt\r\nDELACCOUNTLIST ..\\..\\..\\通区充值\\账号.txt\r\n#IF\r\nCHECKMYSHOP\r\nCHECKSHOPNAME 命格商店");
            var provider = new PhysicalTextFileProvider(
                new PhysicalTextFileProviderOptions(root, TxtScriptLayout.LingFeng));

            Assert.Contains(provider.ExternalDependencyManifest.Requirements, value =>
                value.Level == LingFengDependencyLevel.E1 && value.Kind == LingFengDependencyKind.ItemName &&
                value.Key == "金条");
            Assert.Contains(provider.ExternalDependencyManifest.Requirements, value =>
                value.Kind == LingFengDependencyKind.Map && value.Key == "0");
            Assert.Contains(provider.ExternalDependencyManifest.Requirements, value =>
                value.Kind == LingFengDependencyKind.Monster && value.Key == "稻草人");
            Assert.Contains(provider.ExternalDependencyManifest.Requirements, value =>
                value.Level == LingFengDependencyLevel.E2 && value.Kind == LingFengDependencyKind.DomainAdapter &&
                value.Key.StartsWith("ScriptDynamic/", StringComparison.Ordinal));
            Assert.Contains(provider.ExternalDependencyManifest.Requirements, value =>
                value.Level == LingFengDependencyLevel.E2 && value.Kind == LingFengDependencyKind.ClientContract &&
                value.Key == "LingFeng/AutoArrangedBuff");
            Assert.Contains(provider.ExternalDependencyManifest.Requirements, value =>
                value.Level == LingFengDependencyLevel.E2 && value.Kind == LingFengDependencyKind.ClientContract &&
                value.Key == "LingFeng/CustomButton");
            Assert.Contains(provider.ExternalDependencyManifest.Requirements, value =>
                value.Level == LingFengDependencyLevel.E2 && value.Kind == LingFengDependencyKind.ClientContract &&
                value.Key == "LingFeng/OverheadIcon");
            Assert.Contains(provider.ExternalDependencyManifest.Requirements, value =>
                value.Level == LingFengDependencyLevel.E2 && value.Kind == LingFengDependencyKind.ClientContract &&
                value.Key == "LingFeng/JewelryCasket");
            Assert.Contains(provider.ExternalDependencyManifest.Requirements, value =>
                value.Level == LingFengDependencyLevel.E2 && value.Kind == LingFengDependencyKind.ClientContract &&
                value.Key == "LingFeng/CustomItemBox");
            Assert.Contains(provider.ExternalDependencyManifest.Requirements, value =>
                value.Level == LingFengDependencyLevel.E2 && value.Kind == LingFengDependencyKind.ClientContract &&
                value.Key == "LingFeng/LegacyItemBox");
            Assert.Contains(provider.ExternalDependencyManifest.Requirements, value =>
                value.Level == LingFengDependencyLevel.E2 && value.Kind == LingFengDependencyKind.ClientContract &&
                value.Key == "LingFeng/BodyColor");
            Assert.Contains(provider.ExternalDependencyManifest.Requirements, value =>
                value.Level == LingFengDependencyLevel.E2 && value.Kind == LingFengDependencyKind.ClientContract &&
                value.Key == "LingFeng/ExtendedBag");
            Assert.Contains(provider.ExternalDependencyManifest.Requirements, value =>
                value.Level == LingFengDependencyLevel.E2 && value.Kind == LingFengDependencyKind.ClientContract &&
                value.Key == "LingFeng/BigDialog");
            Assert.Contains(provider.ExternalDependencyManifest.Requirements, value =>
                value.Level == LingFengDependencyLevel.E2 && value.Kind == LingFengDependencyKind.ClientContract &&
                value.Key == "LingFeng/NpcStyleDialog");
            Assert.Contains(provider.ExternalDependencyManifest.Requirements, value =>
                value.Level == LingFengDependencyLevel.E2 && value.Kind == LingFengDependencyKind.DomainAdapter &&
                value.Key == "LingFeng/SlaveAbilityBatch");
            Assert.Contains(provider.ExternalDependencyManifest.Requirements, value =>
                value.Level == LingFengDependencyLevel.E2 && value.Kind == LingFengDependencyKind.DomainAdapter &&
                value.Key == "LingFeng/BagRecycleExtendedRewards");
            Assert.Contains(provider.ExternalDependencyManifest.Requirements, value =>
                value.Level == LingFengDependencyLevel.E2 && value.Kind == LingFengDependencyKind.DomainAdapter &&
                value.Key == "LingFeng/TimedNameListImport");
            Assert.Contains(provider.ExternalDependencyManifest.Requirements, value =>
                value.Level == LingFengDependencyLevel.E2 && value.Kind == LingFengDependencyKind.DomainAdapter &&
                value.Key == "LingFeng/ItemInstanceName");
            Assert.Contains(provider.ExternalDependencyManifest.Requirements, value =>
                value.Level == LingFengDependencyLevel.E2 && value.Kind == LingFengDependencyKind.DomainAdapter &&
                value.Key == "LingFeng/AutoPickItem");
            Assert.Contains(provider.ExternalDependencyManifest.Requirements, value =>
                value.Level == LingFengDependencyLevel.E2 && value.Kind == LingFengDependencyKind.DomainAdapter &&
                value.Key == "LingFeng/PlayerSaleTransaction");
            Assert.Contains(provider.ExternalDependencyManifest.Requirements, value =>
                value.Level == LingFengDependencyLevel.E2 && value.Kind == LingFengDependencyKind.DomainAdapter &&
                value.Key == "LingFeng/ItemProvenance");
            Assert.Contains(provider.ExternalDependencyManifest.Requirements, value =>
                value.Level == LingFengDependencyLevel.E2 && value.Kind == LingFengDependencyKind.ClientContract &&
                value.Key == "LingFeng/GodBlessBag");
            Assert.Contains(provider.ExternalDependencyManifest.Requirements, value =>
                value.Level == LingFengDependencyLevel.E2 && value.Kind == LingFengDependencyKind.ClientContract &&
                value.Key == "LingFeng/SuperMoveMessage");
            Assert.Contains(provider.ExternalDependencyManifest.Requirements, value =>
                value.Level == LingFengDependencyLevel.E2 && value.Kind == LingFengDependencyKind.DomainAdapter &&
                value.Key == "LingFeng/PersonalShop");
            Assert.Contains(provider.ExternalDependencyManifest.Requirements, value =>
                value.Level == LingFengDependencyLevel.E2 && value.Kind == LingFengDependencyKind.DomainAdapter &&
                value.Key == "LingFeng/EquipmentTransaction");
            Assert.Contains(provider.ExternalDependencyManifest.Requirements, value =>
                value.Level == LingFengDependencyLevel.E2 && value.Kind == LingFengDependencyKind.DomainAdapter &&
                value.Key == "LingFeng/OfflinePlay");
            Assert.Equal(3, provider.ExternalDependencyManifest.Requirements.Count(value =>
                value.Level == LingFengDependencyLevel.E2 &&
                value.Kind == LingFengDependencyKind.DomainAdapter &&
                value.Key == "LingFeng/ExternalAccountList"));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void 翎风Give与Take金币不生成物品数据库依赖()
    {
        var definition = new TextFileDefinition("NPCs/金币兼容")
            .AddLines(["[@MAIN]", "#ACT", "GIVE 金币 50", "TAKE 金币 25", "GIVEITEM 金条 1"]);

        LingFengDependencyRequirement[] requirements =
            LingFengScriptDependencyExtractor.Extract([definition]).ToArray();

        LingFengDependencyRequirement item = Assert.Single(requirements);
        Assert.Equal(LingFengDependencyKind.ItemName, item.Kind);
        Assert.Equal("金条", item.Key);
    }

    [Fact]
    public void 缺失E1数据依赖降级不阻断且报告缺失()
    {
        bool oldEnabled = Settings.TxtScriptsEnabled;
        string oldPath = Settings.TxtScriptsPath;
        TxtScriptLayout oldLayout = Settings.TxtScriptsLayout;
        LingFengDependencyLevel oldLevel = Settings.TxtScriptsDependencyLevel;
        string oldClients = Settings.TxtScriptsClientContracts;
        string oldAdapters = Settings.TxtScriptsDomainAdapters;
        string baselineRoot = TempRoot();
        string candidateRoot = TempRoot();
        var envir = new Envir();
        try
        {
            Write(baselineRoot, "Market_Def/基线.txt", "[@MAIN]\r\n基线");
            envir.ItemInfoList.Add(new ItemInfo { Index = 910001, Name = "已有物品" });
            envir.MonsterInfoList.Add(new MonsterInfo { Index = 910002, Name = "稻草人" });
            envir.MapInfoList.Add(new MapInfo { Index = 910003, FileName = "0" });

            Settings.TxtScriptsEnabled = true;
            Settings.TxtScriptsLayout = TxtScriptLayout.LingFeng;
            Settings.TxtScriptsDependencyLevel = LingFengDependencyLevel.None;
            Settings.TxtScriptsPath = baselineRoot;
            envir.ApplyPhysicalTextFileDefinitions();
            Assert.NotNull(envir.TextFileProvider.GetByKey("NPCs/基线"));

            // E1 数据缺项（MonItems 引用不存在的物品）→ 运行时安全的数据降级：
            // 不再构成启动阻断（ApplyPhysicalTextFileDefinitions 不抛 InvalidDataException），
            // 缺失仍被如实报告，不伪造为已满足。
            Settings.TxtScriptsDependencyLevel = LingFengDependencyLevel.E1;
            Settings.TxtScriptsPath = candidateRoot;
            Write(candidateRoot, "MonItems/稻草人.txt", "1/1 不存在物品 1");
            envir.ApplyPhysicalTextFileDefinitions();

            // 不伪造：缺掉的物品仍如实出现在依赖清单而不是被当作已满足。
            var directProvider = new PhysicalTextFileProvider(
                new PhysicalTextFileProviderOptions(candidateRoot, TxtScriptLayout.LingFeng)
                {
                    MaxFileBytes = 2 * 1024 * 1024
                });
            LingFengDependencyReport report = directProvider.ExternalDependencyManifest.Evaluate(
                LingFengDependencyLevel.E1,
                new LingFengDependencyProbe(
                    itemName => envir.GetItemInfo(itemName) != null,
                    itemIndex => envir.GetItemInfo(itemIndex) != null,
                    monsterName => envir.MonsterInfoList.Any(value =>
                        string.Equals(value.Name, monsterName, StringComparison.OrdinalIgnoreCase)),
                    mapName => envir.MapInfoList.Any(value =>
                        string.Equals(value.FileName, mapName, StringComparison.OrdinalIgnoreCase)),
                    _ => false,
                    _ => false));
            Assert.Contains(report.Missing,
                value => value.Kind == LingFengDependencyKind.ItemName && value.Key == "不存在物品");
        }
        finally
        {
            Settings.TxtScriptsEnabled = false;
            Settings.TxtScriptsDependencyLevel = LingFengDependencyLevel.None;
            envir.ApplyPhysicalTextFileDefinitions();
            Settings.TxtScriptsEnabled = oldEnabled;
            Settings.TxtScriptsPath = oldPath;
            Settings.TxtScriptsLayout = oldLayout;
            Settings.TxtScriptsDependencyLevel = oldLevel;
            Settings.TxtScriptsClientContracts = oldClients;
            Settings.TxtScriptsDomainAdapters = oldAdapters;
            Directory.Delete(baselineRoot, true);
            Directory.Delete(candidateRoot, true);
        }
    }

    [ExternalResourceFact(
        "外部资源阻塞：本机未挂载 LFENV-ROOT-0002 代表语料。",
        @"D:\ChuanQi\服务端\01酷明传奇\MirServer_01\Mir200\Envir")]
    public void 代表Envir的每个文件进入四类预检计数且依赖清单可重算()
    {
        const string root = @"D:\ChuanQi\服务端\01酷明传奇\MirServer_01\Mir200\Envir";
        if (!Directory.Exists(root))
            throw Xunit.Sdk.SkipException.ForSkip("本机未挂载 LFENV-ROOT-0002 代表语料。");

        var provider = new PhysicalTextFileProvider(
            new PhysicalTextFileProviderOptions(root, TxtScriptLayout.LingFeng)
            {
                MaxFileBytes = 2 * 1024 * 1024
            });
        int fileCount = Directory.EnumerateFiles(root, "*", new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = false,
            AttributesToSkip = FileAttributes.ReparsePoint,
            MatchCasing = MatchCasing.CaseInsensitive
        }).Count();

        Assert.Equal(fileCount,
            provider.PreflightSummary.Accepted + provider.PreflightSummary.RuntimeData +
            provider.PreflightSummary.ExternalDependency + provider.PreflightSummary.Rejected);
        Assert.Equal(0, provider.PreflightSummary.Rejected);
        Assert.True(provider.PreflightSummary.Accepted > 0);
        Assert.True(provider.PreflightSummary.RuntimeData > 0);
        Assert.True(provider.PreflightSummary.ExternalDependency > 0);
        Assert.Contains(provider.ExternalDependencyManifest.Requirements,
            value => value.Kind == LingFengDependencyKind.ItemName);
        Assert.Contains(provider.ExternalDependencyManifest.Requirements,
            value => value.Kind == LingFengDependencyKind.Monster);
        Assert.Contains(provider.ExternalDependencyManifest.Requirements,
            value => value.Kind == LingFengDependencyKind.Map);
        Assert.Contains(provider.ExternalDependencyManifest.Requirements,
            value => value.Kind == LingFengDependencyKind.ClientContract);
        Assert.Contains(provider.ExternalDependencyManifest.Requirements,
            value => value.Kind == LingFengDependencyKind.DomainAdapter);
    }

    private static string TempRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "lfenv15-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void Write(string root, string relative, string content)
    {
        string path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, new UTF8Encoding(false, true));
    }
}
