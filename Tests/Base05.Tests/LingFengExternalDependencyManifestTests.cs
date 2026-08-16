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
                "[@MAIN]\r\n#IF\r\nCHECKITEM 金条 1\r\nCHECKMAP 0\r\n#ACT\r\nMONGEN 稻草人 1\r\nGIVE <$动态物品> 1");
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
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void 缺失E1依赖在候选发布前阻断且保留上一文本快照()
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
            Write(candidateRoot, "MonItems/稻草人.txt", "1/1 不存在物品 1");
            envir.ItemInfoList.Add(new ItemInfo { Index = 910001, Name = "已有物品" });
            envir.MonsterInfoList.Add(new MonsterInfo { Index = 910002, Name = "稻草人" });
            envir.MapInfoList.Add(new MapInfo { Index = 910003, FileName = "0" });

            Settings.TxtScriptsEnabled = true;
            Settings.TxtScriptsLayout = TxtScriptLayout.LingFeng;
            Settings.TxtScriptsDependencyLevel = LingFengDependencyLevel.None;
            Settings.TxtScriptsPath = baselineRoot;
            envir.ApplyPhysicalTextFileDefinitions();
            Assert.NotNull(envir.TextFileProvider.GetByKey("NPCs/基线"));

            Settings.TxtScriptsDependencyLevel = LingFengDependencyLevel.E1;
            Settings.TxtScriptsPath = candidateRoot;
            InvalidDataException error = Assert.Throws<InvalidDataException>(
                envir.ApplyPhysicalTextFileDefinitions);

            Assert.Contains("LFENV15-DEPENDENCY-MISSING", error.Message, StringComparison.Ordinal);
            Assert.Contains("kind=ItemName", error.Message, StringComparison.Ordinal);
            Assert.Contains("key=不存在物品", error.Message, StringComparison.Ordinal);
            Assert.NotNull(envir.TextFileProvider.GetByKey("NPCs/基线"));

            File.Delete(Path.Combine(candidateRoot, "MonItems", "稻草人.txt"));
            Write(candidateRoot, "Market_Def/候选.txt", "[@MAIN]\r\n#ACT\r\nGIVE 已有物品 1");
            envir.ApplyPhysicalTextFileDefinitions();
            Assert.NotNull(envir.TextFileProvider.GetByKey("NPCs/候选"));
            Assert.Null(envir.TextFileProvider.GetByKey("NPCs/基线"));
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

    [Fact]
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
