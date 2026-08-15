using Server;
using Server.Scripting;
using Xunit;

namespace Base05.Tests;

[Collection(nameof(PhysicalTextFileProviderCollection))]
public sealed class LingFengTxtSystemHookAdapterTests
{
    [Fact]
    public void 基础生命周期触发解析到唯一系统脚本标签()
    {
        Assert.True(LingFengTxtSystemHookAdapter.TryResolve(
            ScriptHookKeys.OnPlayerLevelUp, out var levelUp));
        Assert.Equal("SystemScripts/QFunction-0", levelUp.ScriptKey);
        Assert.Equal("[@PLAYLEVELUP]", levelUp.Label);

        Assert.True(LingFengTxtSystemHookAdapter.TryResolve(
            ScriptHookKeys.OnPlayerLogin, out var login));
        Assert.Equal("SystemScripts/QManage", login.ScriptKey);
        Assert.Equal("[@LOGIN]", login.Label);

        Assert.False(LingFengTxtSystemHookAdapter.TryResolve(
            ScriptHookKeys.OnPlayerDie, out _));
    }

    [Fact]
    public void 已由CSharp处理时不重复执行Txt()
    {
        var provider = Provider("[@PLAYLEVELUP]", "#ACT", "GIVEGOLD 1");
        int executions = 0;
        bool handled = LingFengTxtSystemHookAdapter.TryDispatchAfterCSharp(
            true, provider, ScriptHookKeys.OnPlayerLevelUp, _ =>
            {
                executions++;
                return true;
            });

        Assert.True(handled);
        Assert.Equal(0, executions);
    }

    [Fact]
    public void 显式版本和精确标签同时满足时只派发一次()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        bool oldEnabled = Settings.TxtScriptsEnabled;
        try
        {
            Settings.TxtScriptsEnabled = true;
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            var provider = Provider("[@PLAYLEVELUP]", "#ACT", "GIVEGOLD 1");
            int executions = 0;

            Assert.True(LingFengTxtSystemHookAdapter.TryDispatchAfterCSharp(
                false, provider, ScriptHookKeys.OnPlayerLevelUp, target =>
                {
                    executions++;
                    Assert.Equal("[@PLAYLEVELUP]", target.Label);
                    return true;
                }));
            Assert.Equal(1, executions);

            Assert.False(LingFengTxtSystemHookAdapter.TryDispatchAfterCSharp(
                false, provider, ScriptHookKeys.OnPlayerDie, _ => true));
        }
        finally
        {
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
            Settings.TxtScriptsEnabled = oldEnabled;
        }
    }

    [Fact]
    public void 兼容关闭或标签缺失时不派发()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        bool oldEnabled = Settings.TxtScriptsEnabled;
        try
        {
            var provider = Provider("[@OTHER]");
            Settings.TxtScriptsEnabled = true;
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            Assert.False(LingFengTxtSystemHookAdapter.TryDispatchAfterCSharp(
                false, provider, ScriptHookKeys.OnPlayerLevelUp, _ => true));

            Settings.TxtScriptsCompatibilityVersion = string.Empty;
            Assert.False(LingFengTxtSystemHookAdapter.TryDispatchAfterCSharp(
                false, Provider("[@PLAYLEVELUP]"), ScriptHookKeys.OnPlayerLevelUp, _ => true));
        }
        finally
        {
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
            Settings.TxtScriptsEnabled = oldEnabled;
        }
    }

    [Fact]
    public void CSharp候选存在时全局回落开关控制Txt派发()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        bool oldEnabled = Settings.TxtScriptsEnabled;
        bool oldFallback = Settings.CSharpScriptsFallbackToTxt;
        try
        {
            Settings.TxtScriptsEnabled = true;
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            var provider = Provider("[@PLAYLEVELUP]", "#ACT", "GIVEGOLD 1");
            int executions = 0;

            Settings.CSharpScriptsFallbackToTxt = false;
            Assert.False(LingFengTxtSystemHookAdapter.TryDispatchAfterCSharp(
                false, provider, ScriptHookKeys.OnPlayerLevelUp, _ => { executions++; return true; }, true));
            Assert.Equal(0, executions);

            Settings.CSharpScriptsFallbackToTxt = true;
            Assert.True(LingFengTxtSystemHookAdapter.TryDispatchAfterCSharp(
                false, provider, ScriptHookKeys.OnPlayerLevelUp, _ => { executions++; return true; }, true));
            Assert.Equal(1, executions);
        }
        finally
        {
            Settings.CSharpScriptsFallbackToTxt = oldFallback;
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
            Settings.TxtScriptsEnabled = oldEnabled;
        }
    }

    private static ITextFileProvider Provider(params string[] lines) =>
        new SingleProvider(new TextFileDefinition("SystemScripts/QFunction-0").AddLines(lines));

    private sealed class SingleProvider : ITextFileProvider
    {
        private readonly TextFileDefinition _definition;

        public SingleProvider(TextFileDefinition definition) => _definition = definition;
        public IReadOnlyCollection<TextFileDefinition> GetAll() => new[] { _definition };
        public TextFileDefinition GetByKey(string key) =>
            LogicKey.NormalizeOrThrow(key) == _definition.Key ? _definition : null;
    }
}
