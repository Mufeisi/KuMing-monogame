using Server;
using Server.Scripting;
using Xunit;

namespace Base05.Tests;

[Collection(nameof(PhysicalTextFileProviderCollection))]
public sealed class LingFengUnsupportedTriggerGuardTests
{
    [Theory]
    [InlineData("[@MAGICATTACK]")]
    [InlineData("[@PLAYDIE]")]
    public void 已知缺失上下文的触发在严格快照阶段失败关闭(string label)
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        bool oldStrict = Settings.TxtScriptsStrictCompatibility;
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            Settings.TxtScriptsStrictCompatibility = true;
            var definition = new TextFileDefinition("SystemScripts/QFunction-0")
                .AddLines(new[] { label, "#ACT", "GIVEGOLD 1" });

            IReadOnlyList<string> errors = TxtScriptSnapshotValidator.Validate(new SingleProvider(definition));
            Assert.Contains(errors, error =>
                error.Contains("TXT-SNAPSHOT-016", StringComparison.Ordinal) &&
                error.Contains(label, StringComparison.Ordinal));
        }
        finally
        {
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
            Settings.TxtScriptsStrictCompatibility = oldStrict;
        }
    }

    [Theory]
    [InlineData("[@ATTACKDAMAGE]")]
    [InlineData("[@STRUCKDAMAGE]")]
    [InlineData("[@ATTACK]")]
    [InlineData("[@STRUCK]")]
    [InlineData("[@KILLMON]")]
    [InlineData("[@M2DROPITEM]")]
    [InlineData("[@PICKUPITEMEX]")]
    public void 已接入特殊事件的标签不再被严格护栏拒绝(string label)
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        bool oldStrict = Settings.TxtScriptsStrictCompatibility;
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            Settings.TxtScriptsStrictCompatibility = true;
            var definition = new TextFileDefinition("SystemScripts/QFunction-0")
                .AddLines(new[] { label, "#ACT", "MOV P0 1" });

            Assert.DoesNotContain(TxtScriptSnapshotValidator.Validate(new SingleProvider(definition)),
                error => error.Contains("TXT-SNAPSHOT-016", StringComparison.Ordinal));
        }
        finally
        {
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
            Settings.TxtScriptsStrictCompatibility = oldStrict;
        }
    }

    [Fact]
    public void 已支持的人物升级标签不被护栏误伤()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        bool oldStrict = Settings.TxtScriptsStrictCompatibility;
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            Settings.TxtScriptsStrictCompatibility = true;
            var definition = new TextFileDefinition("SystemScripts/QFunction-0")
                .AddLines(new[] { "[@PLAYLEVELUP]", "#ACT", "GIVEGOLD 1" });

            Assert.DoesNotContain(TxtScriptSnapshotValidator.Validate(new SingleProvider(definition)),
                error => error.Contains("TXT-SNAPSHOT-016", StringComparison.Ordinal));
        }
        finally
        {
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
            Settings.TxtScriptsStrictCompatibility = oldStrict;
        }
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
