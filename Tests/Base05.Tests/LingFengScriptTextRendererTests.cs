using Server.Scripting.ServerSymbols;
using Xunit;

namespace Base05.Tests;

public sealed class LingFengScriptTextRendererTests
{
    [Fact]
    public void RendererHandlesMultipleAdjacentNestedAndChineseButtonTextWithoutTouchingClientBindings()
    {
        IScriptTextRenderer renderer = new ScriptTextRenderer(new FixtureResolver());
        const string source = "你好，<$USERNAME><$LEVEL>，<$STR(<$USERNAME>)><确定/@OK> $$GAMEGOLD";

        ScriptTextRenderResult result = renderer.Render(ServerSymbolContext.Empty, source);

        Assert.Equal(ScriptTextRenderStatus.Rendered, result.Status);
        Assert.Equal("你好，阿明42，[阿明]<确定/@OK> $$GAMEGOLD", result.Text);
        Assert.Empty(result.Diagnostics);
        Assert.Equal(4, result.PlaceholderCount);
    }

    [Fact]
    public void RendererPreservesFailedPlaceholderButReturnsStructuredDiagnostics()
    {
        IScriptTextRenderer renderer = new ScriptTextRenderer(new FixtureResolver());

        ScriptTextRenderResult result = renderer.Render(
            ServerSymbolContext.Empty,
            "未知=<$UNKNOWN> 缺上下文=<$NEEDSPLAYER>");

        Assert.Equal(ScriptTextRenderStatus.CompletedWithDiagnostics, result.Status);
        Assert.Equal("未知=<$UNKNOWN> 缺上下文=<$NEEDSPLAYER>", result.Text);
        Assert.Collection(
            result.Diagnostics,
            diagnostic => Assert.Equal(ServerSymbolStatus.Unsupported, diagnostic.SymbolStatus),
            diagnostic => Assert.Equal(ServerSymbolStatus.ContextUnavailable, diagnostic.SymbolStatus));
    }

    [Fact]
    public void RendererKeepsQuotedCommaParenthesisAndComparisonInsideOneFunctionArgument()
    {
        IScriptTextRenderer renderer = new ScriptTextRenderer(new FixtureResolver());

        ScriptTextRenderResult result = renderer.Render(
            ServerSymbolContext.Empty,
            "<$STR(\"甲,乙(丙)>\")>");

        Assert.Equal(ScriptTextRenderStatus.Rendered, result.Status);
        Assert.Equal("[\"甲,乙(丙)>\"]", result.Text);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void RendererRejectsUnclosedPlaceholderWithoutReturningPartialOutput()
    {
        IScriptTextRenderer renderer = new ScriptTextRenderer(new FixtureResolver());
        const string source = "正常<$USERNAME>，随后<$LEVEL";

        ScriptTextRenderResult result = renderer.Render(ServerSymbolContext.Empty, source);

        Assert.Equal(ScriptTextRenderStatus.InvalidSyntax, result.Status);
        Assert.Equal(source, result.Text);
        Assert.Single(result.Diagnostics);
        Assert.Equal(ServerSymbolStatus.InvalidReference, result.Diagnostics[0].SymbolStatus);
    }

    [Theory]
    [InlineData("123456", 5, 8, 3, 32)]
    [InlineData("<$USERNAME><$LEVEL>", 64, 1, 3, 32)]
    [InlineData("<$STR(<$STR(<$USERNAME>)>)>", 64, 8, 1, 64)]
    [InlineData("<$USERNAME>", 64, 8, 3, 1)]
    public void RendererFailsAtomicallyWhenAnyResourceLimitIsExceeded(
        string source,
        int maximumInputLength,
        int maximumPlaceholders,
        int maximumNestingDepth,
        int maximumOutputLength)
    {
        IScriptTextRenderer renderer = new ScriptTextRenderer(new FixtureResolver());
        var limits = new ScriptTextRenderLimits(
            maximumInputLength,
            maximumPlaceholders,
            maximumNestingDepth,
            maximumOutputLength);

        ScriptTextRenderResult result = renderer.Render(ServerSymbolContext.Empty, source, limits);

        Assert.Equal(ScriptTextRenderStatus.LimitExceeded, result.Status);
        Assert.Equal(source, result.Text);
        Assert.Single(result.Diagnostics);
    }

    private sealed class FixtureResolver : IServerSymbolResolver
    {
        public ServerSymbolResult Resolve(ServerSymbolContext context, ServerSymbolReference reference)
        {
            return reference.NormalizedName switch
            {
                "USERNAME" => ServerSymbolResult.Resolved("USERNAME", ServerSymbolValue.FromString("阿明")),
                "LEVEL" => ServerSymbolResult.Resolved("LEVEL", ServerSymbolValue.FromInteger(42)),
                "STR" when reference.Arguments.Count == 1 =>
                    ServerSymbolResult.Resolved("STR", ServerSymbolValue.FromString($"[{reference.Arguments[0]}]")),
                "NEEDSPLAYER" => ServerSymbolResult.Fail(
                    ServerSymbolStatus.ContextUnavailable,
                    "NEEDSPLAYER",
                    "当前事件缺少人物上下文。"),
                _ => ServerSymbolResult.Fail(
                    ServerSymbolStatus.Unsupported,
                    reference.NormalizedName,
                    "服务器常量尚未登记支持。")
            };
        }
    }
}
