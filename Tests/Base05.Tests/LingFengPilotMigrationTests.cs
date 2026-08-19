using System.Security.Cryptography;
using Server;
using Server.Scripting;
using Xunit;

namespace Base05.Tests;

[Collection(nameof(PhysicalTextFileProviderCollection))]
public sealed class LingFengPilotMigrationTests
{
    [Fact]
    public void 灰度候选可作为完整物理快照加载并通过严格校验()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        bool oldStrict = Settings.TxtScriptsStrictCompatibility;
        bool oldHighRisk = Settings.TxtScriptsHighRiskCapabilitiesEnabled;
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            Settings.TxtScriptsStrictCompatibility = true;
            Settings.TxtScriptsHighRiskCapabilitiesEnabled = false;
            PhysicalTextFileProvider provider = CreatePilotProvider();

            Assert.Equal(3, provider.GetAll().Count);
            Assert.NotNull(provider.GetByKey("NPCs/TXT灰度向导"));
            Assert.NotNull(provider.GetByKey("SystemScripts/QManage"));
            Assert.NotNull(provider.GetByKey("SystemScripts/QFunction-0"));
            Assert.Empty(TxtScriptSnapshotValidator.Validate(provider));
        }
        finally
        {
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
            Settings.TxtScriptsStrictCompatibility = oldStrict;
            Settings.TxtScriptsHighRiskCapabilitiesEnabled = oldHighRisk;
        }
    }

    [Fact]
    public void 灰度候选摘要逐文件匹配且清单无遗漏()
    {
        string pilotRoot = PilotRoot();
        string contentRoot = Path.Combine(pilotRoot, "Content");
        string[] manifestLines = File.ReadAllLines(Path.Combine(pilotRoot, "snapshot.sha256"));
        var manifest = manifestLines
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => line.Split("  ", 2, StringSplitOptions.None))
            .ToDictionary(parts => parts[1], parts => parts[0], StringComparer.Ordinal);
        string[] files = Directory.GetFiles(contentRoot, "*.txt", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(contentRoot, path).Replace('\\', '/'))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(files, manifest.Keys.OrderBy(path => path, StringComparer.Ordinal));
        foreach (string relativePath in files)
        {
            string content = File.ReadAllText(Path.Combine(contentRoot, relativePath))
                .Replace("\r\n", "\n", StringComparison.Ordinal);
            string actual = Convert.ToHexString(SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
            Assert.Equal(manifest[relativePath], actual);
        }
    }

    [Fact]
    public void 同名来源按显式优先级选中且只发布一次()
    {
        PhysicalTextFileProvider txt = CreatePilotProvider();
        var csharpDefinition = new TextFileDefinition("NPCs/TXT灰度向导", "注册表.cs", "C#", "NONE")
            .AddLine("C#灰度占位");
        var csharp = new SingleProvider(csharpDefinition);

        var csharpFirst = new CompositeTextFileProvider(csharp, txt, TextFileSourcePriority.CSharpFirst);
        var txtFirst = new CompositeTextFileProvider(csharp, txt, TextFileSourcePriority.TxtFirst);

        Assert.Equal("C#灰度占位", Assert.Single(csharpFirst.GetByKey("NPCs/TXT灰度向导").Lines));
        Assert.Contains("[@MAIN]", txtFirst.GetByKey("NPCs/TXT灰度向导").Lines);
        Assert.Single(csharpFirst.Conflicts);
        Assert.Single(txtFirst.Conflicts);
        Assert.Equal(3, csharpFirst.GetAll().Count);
        Assert.Equal(3, txtFirst.GetAll().Count);
    }

    [Fact]
    public void 登录和升级生命周期仅在精确候选标签存在时派发()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        bool oldEnabled = Settings.TxtScriptsEnabled;
        try
        {
            Settings.TxtScriptsEnabled = true;
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            PhysicalTextFileProvider provider = CreatePilotProvider();
            var targets = new List<LingFengTxtHookTarget>();

            Assert.True(LingFengTxtSystemHookAdapter.TryDispatchAfterCSharp(
                false, provider, ScriptHookKeys.OnPlayerLogin, target => { targets.Add(target); return true; }));
            Assert.True(LingFengTxtSystemHookAdapter.TryDispatchAfterCSharp(
                false, provider, ScriptHookKeys.OnPlayerLevelUp, target => { targets.Add(target); return true; }));

            Assert.Equal(new[] { "[@LOGIN]", "[@PLAYLEVELUP]" }, targets.Select(target => target.Label));
        }
        finally
        {
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
            Settings.TxtScriptsEnabled = oldEnabled;
        }
    }

    [Fact]
    public void 回滚片段关闭物理来源并恢复安全优先级()
    {
        string setup = File.ReadAllText(Path.Combine(PilotRoot(), "Setup.fragment.ini"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);
        string rollback = File.ReadAllText(Path.Combine(PilotRoot(), "rollback.fragment.ini"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Contains("[ScriptMetrics]\nEnabled=true\n", setup, StringComparison.Ordinal);
        Assert.Contains("CSharpScriptsFallbackToTxt=true", setup, StringComparison.Ordinal);
        Assert.Contains("AutoDumpSeconds=60", setup, StringComparison.Ordinal);
        Assert.Contains("TxtScriptsEnabled=false", rollback, StringComparison.Ordinal);
        Assert.Contains("[CSharpScripts]\nCSharpScriptsFallbackToTxt=false\n", rollback, StringComparison.Ordinal);
        Assert.Contains("TxtScriptsSourcePriority=CSharpFirst", rollback, StringComparison.Ordinal);
        Assert.Contains("TxtScriptsHighRiskCapabilitiesEnabled=false", rollback, StringComparison.Ordinal);
        Assert.Contains("[ScriptMetrics]\nEnabled=false\n", rollback, StringComparison.Ordinal);
        Assert.Contains("AutoDumpSeconds=0", rollback, StringComparison.Ordinal);
    }

    private static PhysicalTextFileProvider CreatePilotProvider() => new(new PhysicalTextFileProviderOptions(
        Path.Combine(PilotRoot(), "Content"), TxtScriptLayout.LyoCrystal)
    {
        MaxFileBytes = 1024 * 1024
    });

    private static string PilotRoot() => Path.Combine(RepositoryRoot(), "Configs", "LingFengTxtPilot");

    private static string RepositoryRoot()
    {
        DirectoryInfo current = new(AppContext.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Docs", "design", "scripting", "翎风TXT脚本兼容迁移实施规格.md")))
                return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("未找到 LyoCrystal 仓库根目录。");
    }

    private sealed class SingleProvider : ITextFileProvider
    {
        private readonly TextFileDefinition _definition;

        public SingleProvider(TextFileDefinition definition) => _definition = definition;
        public IReadOnlyCollection<TextFileDefinition> GetAll() => new[] { _definition };
        public TextFileDefinition GetByKey(string key) =>
            LogicKey.NormalizeOrThrow(key) == _definition.Key ? _definition : null;
    }
}
