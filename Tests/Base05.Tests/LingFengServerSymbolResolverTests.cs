using System.Globalization;
using Server.Scripting.ServerSymbols;
using Xunit;

namespace Base05.Tests;

public sealed class LingFengServerSymbolResolverTests
{
    [Fact]
    public void DefaultResultNeverMasqueradesAsResolved()
    {
        ServerSymbolResult result = default;

        Assert.Equal(ServerSymbolStatus.Faulted, result.Status);
        Assert.False(result.Success);
    }

    [Fact]
    public void ResolverNormalizesWrapperAliasCaseWhitespaceAndParameters()
    {
        ServerSymbolDefinition definition = Definition(
            "STR",
            ServerSymbolValueType.String,
            ServerSymbolContextKind.Player,
            aliases: new[] { "STRING" },
            parameterForm: "STR(name)");
        ServerSymbolCatalog catalog = Catalog(definition);
        ServerSymbolContext context = new ServerSymbolContext(
            ServerSymbolContextKind.Player,
            ServerSymbolBinding.Dynamic(
                ServerSymbolContextKind.Player,
                "STR",
                reference => ServerSymbolValue.FromString(
                    reference.NormalizedName + ":" + reference.Arguments.Single())));
        IServerSymbolResolver resolver = new ServerSymbolResolver(catalog);

        ServerSymbolResult result = resolver.Resolve(
            context,
            ServerSymbolReference.Parse("  <$ string( UserName ) >  "));

        Assert.Equal(ServerSymbolStatus.Resolved, result.Status);
        Assert.Equal(ServerSymbolValueType.String, result.Value.Type);
        Assert.Equal("STR:UserName", result.Value.Format());
        Assert.Equal("STR", result.CanonicalName);
    }

    [Fact]
    public void ResolverUsesIndexedCatalogIdentityAndPassesDynamicIndexAsArgument()
    {
        ServerSymbolCatalog catalog = Catalog(Definition(
            "BOXITEM[].NAME",
            ServerSymbolValueType.String,
            ServerSymbolContextKind.Item,
            parameterForm: "BOXITEM[index].NAME"));
        ServerSymbolContext context = new ServerSymbolContext(
            ServerSymbolContextKind.Item,
            ServerSymbolBinding.Dynamic(
                ServerSymbolContextKind.Item,
                "BOXITEM[].NAME",
                reference => ServerSymbolValue.FromString(reference.Arguments.Single())));
        IServerSymbolResolver resolver = new ServerSymbolResolver(catalog);

        ServerSymbolResult result = resolver.Resolve(
            context,
            ServerSymbolReference.Parse("<$ boxitem[ STR(N0) ].name >"));

        Assert.Equal(ServerSymbolStatus.Resolved, result.Status);
        Assert.Equal("BOXITEM[].NAME", result.CanonicalName);
        Assert.Equal("STR(N0)", result.Value.Format());
    }

    [Fact]
    public void ResolverFormatsNumericValuesWithoutDependingOnCurrentCulture()
    {
        ServerSymbolCatalog catalog = Catalog(Definition(
            "GAMEGOLD",
            ServerSymbolValueType.Decimal,
            ServerSymbolContextKind.Player));
        IServerSymbolResolver resolver = new ServerSymbolResolver(catalog);
        ServerSymbolContext context = new ServerSymbolContext(
            ServerSymbolContextKind.Player,
            ServerSymbolBinding.Value(
                ServerSymbolContextKind.Player,
                "GAMEGOLD",
                ServerSymbolValue.FromDecimal(1234.50m)));
        CultureInfo previous = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("zh-CN");
            ServerSymbolResult result = resolver.Resolve(context, ServerSymbolReference.Parse("GAMEGOLD"));

            Assert.Equal(ServerSymbolStatus.Resolved, result.Status);
            Assert.Equal("1234.5", result.Value.Format());
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void ResolverKeepsMissingContextAndMissingDependencyDistinct()
    {
        ServerSymbolCatalog catalog = Catalog(Definition(
            "USERNAME",
            ServerSymbolValueType.String,
            ServerSymbolContextKind.Player));
        IServerSymbolResolver resolver = new ServerSymbolResolver(catalog);

        ServerSymbolResult withoutPlayer = resolver.Resolve(
            ServerSymbolContext.Empty,
            ServerSymbolReference.Parse("<$USERNAME>"));
        ServerSymbolResult withoutValue = resolver.Resolve(
            new ServerSymbolContext(ServerSymbolContextKind.Player),
            ServerSymbolReference.Parse("<$USERNAME>"));

        Assert.Equal(ServerSymbolStatus.ContextUnavailable, withoutPlayer.Status);
        Assert.Equal(ServerSymbolStatus.DependencyMissing, withoutValue.Status);
    }

    [Fact]
    public void ResolverFailsClosedForSensitiveUnsupportedAndInvalidReferences()
    {
        ServerSymbolCatalog catalog = Catalog(Definition(
            "PASSWORD",
            ServerSymbolValueType.String,
            ServerSymbolContextKind.Player,
            securityClassification:
                ServerSymbolSecurityClassification.Privacy |
                ServerSymbolSecurityClassification.AccountInformation |
                ServerSymbolSecurityClassification.Credential,
            accessPolicy: ServerSymbolAccessPolicy.Denied));
        IServerSymbolResolver resolver = new ServerSymbolResolver(catalog);

        ServerSymbolResult sensitive = resolver.Resolve(
            new ServerSymbolContext(
                ServerSymbolContextKind.Player,
                ServerSymbolBinding.Value(
                    ServerSymbolContextKind.Player,
                    "PASSWORD",
                    ServerSymbolValue.FromString("不得泄漏"))),
            ServerSymbolReference.Parse("<$PASSWORD>"));
        ServerSymbolResult unsupported = resolver.Resolve(
            ServerSymbolContext.Empty,
            ServerSymbolReference.Parse("<$NOT_SUPPORTED>"));
        ServerSymbolResult invalid = resolver.Resolve(
            ServerSymbolContext.Empty,
            ServerSymbolReference.Parse("<$STR(>"));

        Assert.Equal(ServerSymbolStatus.SensitiveDenied, sensitive.Status);
        Assert.Equal(ServerSymbolStatus.Unsupported, unsupported.Status);
        Assert.Equal(ServerSymbolStatus.InvalidReference, invalid.Status);
        Assert.DoesNotContain("不得泄漏", sensitive.Diagnostic, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolverContainsAdapterFaultAndDoesNotLeakOrCrossContaminateContexts()
    {
        ServerSymbolCatalog catalog = Catalog(Definition(
            "USERNAME",
            ServerSymbolValueType.String,
            ServerSymbolContextKind.Player));
        IServerSymbolResolver resolver = new ServerSymbolResolver(catalog);
        ServerSymbolContext faultedContext = new ServerSymbolContext(
            ServerSymbolContextKind.Player,
            ServerSymbolBinding.Dynamic(
                ServerSymbolContextKind.Player,
                "USERNAME",
                _ => throw new InvalidOperationException("secret-player-id-42")));
        ServerSymbolContext healthyContext = new ServerSymbolContext(
            ServerSymbolContextKind.Player,
            ServerSymbolBinding.Value(
                ServerSymbolContextKind.Player,
                "USERNAME",
                ServerSymbolValue.FromString("独立玩家")));

        ServerSymbolResult faulted = resolver.Resolve(
            faultedContext,
            ServerSymbolReference.Parse("<$USERNAME>"));
        ServerSymbolResult healthy = resolver.Resolve(
            healthyContext,
            ServerSymbolReference.Parse("<$USERNAME>"));

        Assert.Equal(ServerSymbolStatus.Faulted, faulted.Status);
        Assert.DoesNotContain("secret-player-id-42", faulted.Diagnostic, StringComparison.Ordinal);
        Assert.Equal(ServerSymbolStatus.Resolved, healthy.Status);
        Assert.Equal("独立玩家", healthy.Value.Format());
    }

    [Fact]
    public void CatalogPublishesReadOnlySnapshotAndRejectsAliasOrContractConflicts()
    {
        ServerSymbolDefinition username = Definition(
            "USERNAME",
            ServerSymbolValueType.String,
            ServerSymbolContextKind.Player,
            aliases: new[] { "USER" });
        ServerSymbolCatalog catalog = Catalog(username);

        Assert.True(catalog.TryGet(" user ", out ServerSymbolDefinition resolved));
        Assert.Equal("USERNAME", resolved.CanonicalName);
        Assert.Single(catalog.Definitions);

        Assert.False(ServerSymbolCatalog.TryCreate(
            new[]
            {
                username,
                Definition("ACCOUNT", ServerSymbolValueType.String, ServerSymbolContextKind.Player,
                    aliases: new[] { "USER" })
            },
            out _,
            out string aliasDiagnostic));
        Assert.Contains("USER", aliasDiagnostic, StringComparison.Ordinal);

        Assert.False(ServerSymbolCatalog.TryCreate(
            new[]
            {
                username,
                Definition("USERNAME", ServerSymbolValueType.Integer, ServerSymbolContextKind.Server)
            },
            out _,
            out string contractDiagnostic));
        Assert.Contains("USERNAME", contractDiagnostic, StringComparison.Ordinal);

        Assert.False(ServerSymbolCatalog.TryCreate(
            new[]
            {
                Definition("LEVEL", ServerSymbolValueType.Integer, ServerSymbolContextKind.Player,
                    testIds: Array.Empty<string>())
            },
            out _,
            out string metadataDiagnostic));
        Assert.Contains("LEVEL", metadataDiagnostic, StringComparison.Ordinal);

        Assert.False(ServerSymbolCatalog.TryCreate(
            new[]
            {
                Definition("PASSWORD", ServerSymbolValueType.String, ServerSymbolContextKind.Player,
                    securityClassification: ServerSymbolSecurityClassification.Credential)
            },
            out _,
            out string credentialDiagnostic));
        Assert.Contains("PASSWORD", credentialDiagnostic, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolverRejectsBindingWhoseDeclaredContextViolatesCatalogContract()
    {
        ServerSymbolCatalog catalog = Catalog(Definition(
            "USERNAME",
            ServerSymbolValueType.String,
            ServerSymbolContextKind.Player));
        IServerSymbolResolver resolver = new ServerSymbolResolver(catalog);
        ServerSymbolContext context = new ServerSymbolContext(
            ServerSymbolContextKind.Player | ServerSymbolContextKind.Server,
            ServerSymbolBinding.Value(
                ServerSymbolContextKind.Server,
                "USERNAME",
                ServerSymbolValue.FromString("错误来源")));

        ServerSymbolResult result = resolver.Resolve(context, ServerSymbolReference.Parse("USERNAME"));

        Assert.Equal(ServerSymbolStatus.Faulted, result.Status);
        Assert.DoesNotContain("错误来源", result.Diagnostic, StringComparison.Ordinal);
    }

    private static ServerSymbolCatalog Catalog(params ServerSymbolDefinition[] definitions)
    {
        Assert.True(ServerSymbolCatalog.TryCreate(definitions, out ServerSymbolCatalog catalog, out string diagnostic), diagnostic);
        return catalog;
    }

    private static ServerSymbolDefinition Definition(
        string canonicalName,
        ServerSymbolValueType valueType,
        ServerSymbolContextKind requiredContext,
        IReadOnlyList<string>? aliases = null,
        string parameterForm = "",
        ServerSymbolSecurityClassification securityClassification = ServerSymbolSecurityClassification.Public,
        ServerSymbolAccessPolicy accessPolicy = ServerSymbolAccessPolicy.Allowed,
        IReadOnlyList<string>? testIds = null) =>
        new(
            canonicalName,
            aliases ?? Array.Empty<string>(),
            parameterForm,
            valueType,
            requiredContext,
            ServerSymbolNoContextBehavior.StructuredFailure,
            securityClassification,
            accessPolicy,
            "翎风服务器常量",
            "上下文快照",
            "D",
            new[] { "NPC", "命令参数", "系统触发", "ScriptApi" },
            "执行时",
            testIds ?? new[] { "LFENV03-UNIT" },
            "翎风服务器常量与整服Envir直接运行实施规格.md",
            1,
            new DateOnly(2026, 8, 16));
}
