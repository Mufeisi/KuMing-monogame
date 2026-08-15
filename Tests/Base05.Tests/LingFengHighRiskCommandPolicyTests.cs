using Server;
using Server.Scripting;
using Xunit;

namespace Base05.Tests;

[Collection(nameof(PhysicalTextFileProviderCollection))]
public sealed class LingFengHighRiskCommandPolicyTests
{
    [Fact]
    public void OpenBrowser默认关闭且KillSwitch可独立阻断()
    {
        Assert.False(LingFengHighRiskCommandPolicy.CanOpenBrowser(
            "https://docs.example.com/help", false, "docs.example.com", true, out _, out _));
        Assert.False(LingFengHighRiskCommandPolicy.CanOpenBrowser(
            "https://docs.example.com/help", true, "docs.example.com", false, out _, out string killed));
        Assert.Contains("Kill Switch", killed);
    }

    [Theory]
    [InlineData("http://docs.example.com/help")]
    [InlineData("https://user:secret@docs.example.com/help")]
    [InlineData("https://evil.example.com/help")]
    [InlineData("https://sub.docs.example.com/help")]
    [InlineData("https://docs.example.com:444/help")]
    public void OpenBrowser拒绝非Https凭据非白名单子域和异常端口(string url)
    {
        Assert.False(LingFengHighRiskCommandPolicy.CanOpenBrowser(
            url, true, "docs.example.com", true, out _, out _));
    }

    [Fact]
    public void OpenBrowser仅接受精确Https白名单()
    {
        Assert.True(LingFengHighRiskCommandPolicy.CanOpenBrowser(
            "https://docs.example.com/help?q=1", true,
            "support.example.com;docs.example.com", true, out Uri uri, out _));
        Assert.Equal("docs.example.com", uri.DnsSafeHost);
    }

    [Fact]
    public void 严格快照在配置关闭和Ssrf输入时失败关闭()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        bool oldStrict = Settings.TxtScriptsStrictCompatibility;
        bool oldEnabled = Settings.TxtScriptsHighRiskCapabilitiesEnabled;
        string oldHosts = Settings.TxtScriptsAllowedHttpsHosts;
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            Settings.TxtScriptsStrictCompatibility = true;
            Settings.TxtScriptsHighRiskCapabilitiesEnabled = false;
            Settings.TxtScriptsAllowedHttpsHosts = "docs.example.com";
            Assert.Contains(TxtScriptSnapshotValidator.Validate(Provider("OPENBROWSER https://docs.example.com/help")),
                error => error.Contains("TXT-SNAPSHOT-017", StringComparison.Ordinal));

            Settings.TxtScriptsHighRiskCapabilitiesEnabled = true;
            Assert.Contains(TxtScriptSnapshotValidator.Validate(Provider("OPENBROWSER http://127.0.0.1/admin")),
                error => error.Contains("TXT-SNAPSHOT-017", StringComparison.Ordinal));
            Assert.Empty(TxtScriptSnapshotValidator.Validate(Provider("OPENBROWSER https://docs.example.com/help")));
        }
        finally
        {
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
            Settings.TxtScriptsStrictCompatibility = oldStrict;
            Settings.TxtScriptsHighRiskCapabilitiesEnabled = oldEnabled;
            Settings.TxtScriptsAllowedHttpsHosts = oldHosts;
        }
    }

    private static ITextFileProvider Provider(string action) =>
        new SingleProvider(new TextFileDefinition("NPCs/安全入口")
            .AddLines(new[] { "[@MAIN]", "#ACT", action }));

    private sealed class SingleProvider : ITextFileProvider
    {
        private readonly TextFileDefinition _definition;
        public SingleProvider(TextFileDefinition definition) => _definition = definition;
        public IReadOnlyCollection<TextFileDefinition> GetAll() => new[] { _definition };
        public TextFileDefinition GetByKey(string key) =>
            LogicKey.NormalizeOrThrow(key) == _definition.Key ? _definition : null;
    }
}
