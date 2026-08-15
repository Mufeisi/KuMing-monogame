using System.Globalization;
using Server.Scripting.Variables;
using Server.Scripting;
using Server.MirObjects;
using Xunit;

namespace Base05.Tests;

public sealed class ScriptVariableModuleTests
{
    [Fact]
    public void DecimalArithmeticIsExactWhileLegacyIntegerDivisionStillTruncates()
    {
        ScriptVariableValue decimalResult = ScriptVariableArithmetic.Apply(
            ScriptVariableValue.FromDecimal(0.1m),
            ScriptVariableOperation.Add,
            ScriptVariableValue.FromDecimal(0.2m)).Value;
        ScriptVariableValue integerResult = ScriptVariableArithmetic.Apply(
            ScriptVariableValue.FromInteger(1),
            ScriptVariableOperation.Divide,
            ScriptVariableValue.FromInteger(4)).Value;
        ScriptVariableValue mixedResult = ScriptVariableArithmetic.Apply(
            ScriptVariableValue.FromInteger(2),
            ScriptVariableOperation.Multiply,
            ScriptVariableValue.FromDecimal(1.25m)).Value;

        Assert.Equal(ScriptVariableKind.Decimal, decimalResult.Kind);
        Assert.Equal(0.3m, decimalResult.Decimal);
        Assert.Equal(ScriptVariableKind.Integer, integerResult.Kind);
        Assert.Equal(0L, integerResult.Integer);
        Assert.Equal(ScriptVariableKind.Decimal, mixedResult.Kind);
        Assert.Equal(2.50m, mixedResult.Decimal);
    }

    [Fact]
    public void DecimalParsingAndDisplayAreCultureIndependentAndNeverSilentlyTruncate()
    {
        CultureInfo previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("zh-CN");
            Assert.True(ScriptVariableValue.TryParseDecimal("12.500", out ScriptVariableValue value));
            Assert.Equal("12.5", value.Format());
            Assert.Equal("12.50", value.Format(2));
            Assert.False(ScriptVariableValue.TryParseDecimal("12,5", out _));

            ScriptVariableResult converted = ScriptVariableArithmetic.ConvertToInteger(
                ScriptVariableValue.FromDecimal(1.9m), ScriptVariableRounding.Truncate);
            Assert.True(converted.Success);
            Assert.Equal(1L, converted.Value.Integer);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    [Fact]
    public void DeclarationReloadIsAtomicAndIdenticalDuplicatesAreIdempotent()
    {
        var catalog = new ScriptVariableDeclarationCatalog();
        var declaration = Declaration(ScriptVariableScope.P, "DropRate", ScriptVariableKind.Decimal, "1.0");

        ScriptVariableCatalogReloadResult first = catalog.TryReload(new[] { declaration, declaration });
        Assert.True(first.Success, first.Diagnostic);
        Assert.Equal(1L, catalog.Version);
        Assert.True(catalog.Current.TryGet(ScriptVariableScope.P, "droprate", out ScriptVariableDeclaration loaded));
        Assert.Equal(ScriptVariableKind.Decimal, loaded.Kind);

        ScriptVariableCatalogReloadResult conflict = catalog.TryReload(new[]
        {
            declaration,
            Declaration(ScriptVariableScope.P, "DropRate", ScriptVariableKind.Integer, "1")
        });

        Assert.False(conflict.Success);
        Assert.Equal(ScriptVariableErrorCode.DeclarationConflict, conflict.ErrorCode);
        Assert.Equal(1L, catalog.Version);
        Assert.Equal(ScriptVariableKind.Decimal, catalog.Current.GetRequired(ScriptVariableScope.P, "DropRate").Kind);
    }

    [Fact]
    public void ConversationVariablesShareCommandsAndResetWhenNpcChangesOrConversationCloses()
    {
        var catalog = new ScriptVariableDeclarationCatalog();
        Assert.True(catalog.TryReload(new[]
        {
            Declaration(ScriptVariableScope.P, "DropRate", ScriptVariableKind.Decimal, "0")
        }).Success);
        IScriptVariableModule module = new ScriptVariableModule(catalog);
        var owner = new object();
        ScriptVariableReference named = ScriptVariableReference.Named(ScriptVariableScope.P, "DropRate");
        ScriptVariableReference legacy = ScriptVariableReference.Legacy(ScriptVariableScope.P, 0);
        ScriptVariableContext npcOne = ScriptVariableContext.ForConversation(owner, npcObjectId: 100);

        Assert.True(module.Mutate(npcOne, ScriptVariableMutation.Set(named, ScriptVariableValue.FromDecimal(12.5m))).Success);
        Assert.True(module.Mutate(npcOne, ScriptVariableMutation.Apply(named, ScriptVariableOperation.Add, ScriptVariableValue.FromDecimal(0.25m))).Success);
        Assert.True(module.Mutate(npcOne, ScriptVariableMutation.Set(legacy, ScriptVariableValue.FromInteger(1))).Success);
        Assert.True(module.Mutate(npcOne, ScriptVariableMutation.Apply(legacy, ScriptVariableOperation.Divide, ScriptVariableValue.FromInteger(4))).Success);

        Assert.Equal("12.75", module.Read(npcOne, named).Value.Format());
        Assert.Equal(0L, module.Read(npcOne, legacy).Value.Integer);

        ScriptVariableContext npcTwo = ScriptVariableContext.ForConversation(owner, npcObjectId: 200);
        Assert.Equal("0", module.Read(npcTwo, named).Value.Format());
        Assert.False(module.Read(npcTwo, legacy).Found);

        Assert.True(module.Mutate(npcTwo, ScriptVariableMutation.Set(named, ScriptVariableValue.FromDecimal(5m))).Success);
        Assert.True(module.Reset(npcTwo, ScriptVariableSelector.Conversation()).Success);
        Assert.Equal("0", module.Read(npcTwo, named).Value.Format());
    }

    [Fact]
    public void RuntimeScopesFollowPlayerMapServerAndCallFrameLifecycles()
    {
        var catalog = new ScriptVariableDeclarationCatalog();
        Assert.True(catalog.TryReload(new[]
        {
            Declaration(ScriptVariableScope.D, "Rate", ScriptVariableKind.Decimal, "1.5"),
            Declaration(ScriptVariableScope.M, "Rate", ScriptVariableKind.Decimal, "2.5"),
            Declaration(ScriptVariableScope.I, "Rate", ScriptVariableKind.Decimal, "3.5"),
            Declaration(ScriptVariableScope.Call, "Rate", ScriptVariableKind.Decimal, "4.5")
        }).Success);
        var module = new ScriptVariableModule(catalog);
        var commands = new ScriptVariableCommands(module);
        var ownerOne = new object();
        var ownerTwo = new object();
        var mapOne = new object();
        var mapTwo = new object();
        var frameOne = new object();
        var frameTwo = new object();
        var first = ScriptVariableContext.ForConversation(ownerOne, 100, mapOne, frameOne);

        Assert.True(commands.Mutate(first, "D0", "MOV", "10").Success);
        Assert.True(commands.Mutate(first, "M0", "MOV", "20").Success);
        Assert.True(commands.Mutate(first, "N$Score", "MOV", "30").Success);
        Assert.True(commands.Mutate(first, "S$Label", "MOV", "在线").Success);
        Assert.True(commands.Mutate(first, "I0", "MOV", "40").Success);
        Assert.True(commands.Mutate(first, "D.Rate", "MOV", "1.75").Success);
        Assert.True(commands.Mutate(first, "Call.Rate", "MOV", "4.75").Success);

        var nextNpc = ScriptVariableContext.ForConversation(ownerOne, 200, mapOne, frameOne);
        Assert.Equal("10", commands.Format(nextNpc, "D0").Text);
        Assert.Equal("20", commands.Format(nextNpc, "M0").Text);
        Assert.Equal("30", commands.Format(nextNpc, "N$Score").Text);
        Assert.Equal("在线", commands.Format(nextNpc, "S$Label").Text);
        Assert.Equal("1.75", commands.Format(nextNpc, "D.Rate").Text);

        var nextMap = ScriptVariableContext.ForConversation(ownerOne, 200, mapTwo, frameOne);
        Assert.False(module.Read(nextMap, ScriptVariableReference.Legacy(ScriptVariableScope.M, 0)).Found);
        Assert.Equal("10", commands.Format(nextMap, "D0").Text);
        Assert.Equal("30", commands.Format(nextMap, "N$Score").Text);

        var otherOwner = ScriptVariableContext.ForConversation(ownerTwo, 300, mapOne, frameTwo);
        Assert.False(module.Read(otherOwner, ScriptVariableReference.Legacy(ScriptVariableScope.D, 0)).Found);
        Assert.Equal("40", commands.Format(otherOwner, "I0").Text);
        Assert.Equal("4.5", commands.Format(otherOwner, "Call.Rate").Text);
        Assert.Equal("4.75", commands.Format(first, "Call.Rate").Text);

        foreach (ScriptVariableScope scope in new[]
                 { ScriptVariableScope.D, ScriptVariableScope.M, ScriptVariableScope.N, ScriptVariableScope.S })
            Assert.True(module.Reset(nextMap, ScriptVariableSelector.ScopeOnly(scope)).Success);
        Assert.False(module.Read(nextMap, ScriptVariableReference.Legacy(ScriptVariableScope.D, 0)).Found);
        Assert.False(module.Read(nextMap, ScriptVariableReference.Named(ScriptVariableScope.N, "Score")).Found);
        Assert.False(module.Read(nextMap, ScriptVariableReference.Named(ScriptVariableScope.S, "Label")).Found);

        Assert.True(module.Reset(ScriptVariableContext.ForServer(),
            ScriptVariableSelector.ScopeOnly(ScriptVariableScope.I)).Success);
        Assert.False(module.Read(otherOwner, ScriptVariableReference.Legacy(ScriptVariableScope.I, 0)).Found);
    }

    [Fact]
    public void RuntimeScopesRejectMissingMapOrCallFrameContext()
    {
        var catalog = new ScriptVariableDeclarationCatalog();
        Assert.True(catalog.TryReload(new[]
        {
            Declaration(ScriptVariableScope.Call, "Rate", ScriptVariableKind.Decimal, "0")
        }).Success);
        var module = new ScriptVariableModule(catalog);
        var withoutMapOrFrame = ScriptVariableContext.ForPlayer(new object());

        ScriptVariableReadResult map = module.Read(
            withoutMapOrFrame, ScriptVariableReference.Legacy(ScriptVariableScope.M, 0));
        ScriptVariableReadResult call = module.Read(
            withoutMapOrFrame, ScriptVariableReference.Named(ScriptVariableScope.Call, "Rate"));

        Assert.False(map.Success);
        Assert.Equal(ScriptVariableErrorCode.ContextUnavailable, map.ErrorCode);
        Assert.False(call.Success);
        Assert.Equal(ScriptVariableErrorCode.ContextUnavailable, call.ErrorCode);
    }

    [Fact]
    public void FailedMutationKeepsOldValueAndReturnsStableError()
    {
        var catalog = new ScriptVariableDeclarationCatalog();
        Assert.True(catalog.TryReload(new[]
        {
            Declaration(ScriptVariableScope.P, "Rate", ScriptVariableKind.Decimal, "1")
        }).Success);
        IScriptVariableModule module = new ScriptVariableModule(catalog);
        var context = ScriptVariableContext.ForConversation(new object(), 1);
        var reference = ScriptVariableReference.Named(ScriptVariableScope.P, "Rate");

        Assert.True(module.Mutate(context, ScriptVariableMutation.Set(reference, ScriptVariableValue.FromDecimal(9m))).Success);
        ScriptVariableMutationResult failed = module.Mutate(
            context,
            ScriptVariableMutation.Apply(reference, ScriptVariableOperation.Divide, ScriptVariableValue.FromDecimal(0m)));

        Assert.False(failed.Success);
        Assert.Equal(ScriptVariableErrorCode.InvalidExpression, failed.ErrorCode);
        Assert.Equal(9m, module.Read(context, reference).Value.Decimal);
    }

    [Fact]
    public void OverflowScaleAndTypeFailuresKeepThePreviousValue()
    {
        var catalog = new ScriptVariableDeclarationCatalog();
        Assert.True(catalog.TryReload(new[]
        {
            Declaration(ScriptVariableScope.P, "Rate", ScriptVariableKind.Decimal, "1")
        }).Success);
        var module = new ScriptVariableModule(catalog);
        var commands = new ScriptVariableCommands(module);
        var context = ScriptVariableContext.ForConversation(new object(), 1);

        Assert.True(commands.Mutate(context, "P0", "MOV", long.MaxValue.ToString(CultureInfo.InvariantCulture)).Success);
        ScriptVariableMutationResult overflow = commands.Mutate(context, "P0", "INC", "1");
        Assert.False(overflow.Success);
        Assert.Equal(ScriptVariableErrorCode.Overflow, overflow.ErrorCode);
        Assert.Equal(long.MaxValue.ToString(CultureInfo.InvariantCulture), commands.Format(context, "P0").Text);

        Assert.True(commands.Mutate(context, "P.Rate", "MOV", "1").Success);
        ScriptVariableMutationResult scale = commands.Mutate(context, "P.Rate", "DIV", "3");
        Assert.False(scale.Success);
        Assert.Equal(ScriptVariableErrorCode.ScaleExceeded, scale.ErrorCode);
        Assert.Equal("1", commands.Format(context, "P.Rate").Text);

        ScriptVariableMutationResult type = commands.Mutate(context, "P0", "MOV", "1.5");
        Assert.False(type.Success);
        Assert.Equal(ScriptVariableErrorCode.TypeMismatch, type.ErrorCode);
        Assert.Equal(long.MaxValue.ToString(CultureInfo.InvariantCulture), commands.Format(context, "P0").Text);
    }

    [Fact]
    public void VariableStateRejectsReadsWritesAndResetsOutsideTheServerMainThread()
    {
        var catalog = new ScriptVariableDeclarationCatalog();
        var module = new ScriptVariableModule(catalog, canWrite: () => false);
        var context = ScriptVariableContext.ForConversation(new object(), 1);
        var reference = ScriptVariableReference.Legacy(ScriptVariableScope.P, 0);

        ScriptVariableReadResult read = module.Read(context, reference);
        ScriptVariableMutationResult mutation = module.Mutate(
            context,
            ScriptVariableMutation.Set(reference, ScriptVariableValue.FromInteger(1)));
        ScriptVariableResetResult reset = module.Reset(context, ScriptVariableSelector.Conversation());

        Assert.False(read.Success);
        Assert.Equal(ScriptVariableErrorCode.WrongThread, read.ErrorCode);
        Assert.False(mutation.Success);
        Assert.Equal(ScriptVariableErrorCode.WrongThread, mutation.ErrorCode);
        Assert.False(reset.Success);
        Assert.Equal(ScriptVariableErrorCode.WrongThread, reset.ErrorCode);
    }

    [Fact]
    public void RegistryOwnsAnImmutableDeclarationSnapshotAndRejectsConflictsBeforePublish()
    {
        var registry = new ScriptRegistry();
        ScriptVariableDeclaration declaration = Declaration(
            ScriptVariableScope.P, "DropRate", ScriptVariableKind.Decimal, "1.25");

        registry.RegisterVariable(declaration);
        registry.RegisterVariable(declaration);

        Assert.Equal(1, registry.VariableDeclarations.Count);
        Assert.Equal(1.25m, registry.VariableDeclarations
            .GetRequired(ScriptVariableScope.P, "DropRate").DefaultValue.Decimal);
        Assert.Throws<InvalidOperationException>(() => registry.RegisterVariable(
            Declaration(ScriptVariableScope.P, "DropRate", ScriptVariableKind.Integer, "1")));
        Assert.Equal(ScriptVariableKind.Decimal, registry.VariableDeclarations
            .GetRequired(ScriptVariableScope.P, "DropRate").Kind);
    }

    [Fact]
    public void HotReloadAllowsNewOrDefaultChangesButRejectsAnExistingTypeChange()
    {
        var current = new ScriptRegistry();
        current.RegisterVariable(Declaration(
            ScriptVariableScope.P, "Rate", ScriptVariableKind.Decimal, "1"));

        var compatible = new ScriptRegistry();
        compatible.RegisterVariable(Declaration(
            ScriptVariableScope.P, "Rate", ScriptVariableKind.Decimal, "2"));
        compatible.RegisterVariable(Declaration(
            ScriptVariableScope.P, "Bonus", ScriptVariableKind.Decimal, "0"));
        Assert.True(current.VariableDeclarations
            .ValidateCompatibleTransitionTo(compatible.VariableDeclarations).Success);

        var incompatible = new ScriptRegistry();
        incompatible.RegisterVariable(Declaration(
            ScriptVariableScope.P, "Rate", ScriptVariableKind.Integer, "1"));
        ScriptVariableCatalogReloadResult result = current.VariableDeclarations
            .ValidateCompatibleTransitionTo(incompatible.VariableDeclarations);
        Assert.False(result.Success);
        Assert.Equal(ScriptVariableErrorCode.DeclarationConflict, result.ErrorCode);
    }

    [Fact]
    public void VariableModuleObservesDeclarationChangesOnlyWhenRegistryReferenceIsPublished()
    {
        var oldRegistry = new ScriptRegistry();
        oldRegistry.RegisterVariable(Declaration(
            ScriptVariableScope.P, "Rate", ScriptVariableKind.Decimal, "1.5"));
        ScriptRegistry current = oldRegistry;
        IScriptVariableModule module = new ScriptVariableModule(() => current.VariableDeclarations);
        var context = ScriptVariableContext.ForConversation(new object(), 1);
        var reference = ScriptVariableReference.Named(ScriptVariableScope.P, "Rate");

        var candidate = new ScriptRegistry();
        candidate.RegisterVariable(Declaration(
            ScriptVariableScope.P, "Rate", ScriptVariableKind.Decimal, "2.5"));
        Assert.Equal(1.5m, module.Read(context, reference).Value.Decimal);

        current = candidate;
        Assert.Equal(2.5m, module.Read(context, reference).Value.Decimal);
    }

    [Theory]
    [InlineData("P0", ScriptVariableScope.P, true, 0, "")]
    [InlineData("p999", ScriptVariableScope.P, true, 999, "")]
    [InlineData("P.DropRate", ScriptVariableScope.P, false, -1, "DROPRATE")]
    [InlineData("P.Drop.Rate", ScriptVariableScope.P, false, -1, "DROP.RATE")]
    [InlineData("N$Score", ScriptVariableScope.N, false, -1, "SCORE")]
    [InlineData("S$标题", ScriptVariableScope.S, false, -1, "标题")]
    [InlineData("guild.GuildRate", ScriptVariableScope.Guild, false, -1, "GUILDRATE")]
    public void ReferenceParserAcceptsLegacyAndExplicitNamedReferences(
        string text,
        ScriptVariableScope scope,
        bool legacy,
        int legacyIndex,
        string key)
    {
        Assert.True(ScriptVariableReferenceParser.TryParse(text, out ScriptVariableReference reference));
        Assert.Equal(scope, reference.Scope);
        Assert.Equal(legacy, reference.IsLegacy);
        Assert.Equal(legacyIndex, reference.LegacyIndex);
        Assert.Equal(key, reference.Key);
    }

    [Theory]
    [InlineData("")]
    [InlineData("P1000")]
    [InlineData("P.1Rate")]
    [InlineData("P._Rate")]
    [InlineData("P.A..B")]
    [InlineData("P.A.")]
    [InlineData("Unknown.Rate")]
    public void ReferenceParserRejectsInvalidOrAmbiguousReferences(string text)
    {
        Assert.False(ScriptVariableReferenceParser.TryParse(text, out _));
    }

    [Fact]
    public void UnifiedCommandsHandleLegacyIntegerAndNamedDecimalWithComparisonAndFormatting()
    {
        var catalog = new ScriptVariableDeclarationCatalog();
        Assert.True(catalog.TryReload(new[]
        {
            Declaration(ScriptVariableScope.P, "DropRate", ScriptVariableKind.Decimal, "0")
        }).Success);
        var commands = new ScriptVariableCommands(new ScriptVariableModule(catalog));
        var context = ScriptVariableContext.ForConversation(new object(), 1);

        Assert.True(commands.Mutate(context, "P0", "MOV", "5").Success);
        Assert.True(commands.Mutate(context, "P0", "DIV", "2").Success);
        Assert.True(commands.Mutate(context, "P.DropRate", "MOV", "12.5").Success);
        Assert.True(commands.Mutate(context, "P.DropRate", "INC", "0.25").Success);
        Assert.True(commands.Mutate(context, "P.DropRate", "DEC", "0.5").Success);
        Assert.True(commands.Mutate(context, "P.DropRate", "MUL", "2").Success);
        Assert.True(commands.Mutate(context, "P.DropRate", "DIV", "2").Success);

        Assert.Equal("2", commands.Format(context, "P0").Text);
        Assert.Equal("12.25", commands.Format(context, "P.DropRate").Text);
        Assert.Equal("12.250", commands.Format(context, "P.DropRate", 3).Text);
        Assert.True(commands.Check(context, "P.DropRate", ">=", "12.25").Matched);
        Assert.False(commands.Check(context, "P.DropRate", "<", "12.25").Matched);

        Assert.True(commands.Mutate(context, "P.DropRate", "MOV", "1.9").Success);
        Assert.True(commands.Convert(context, "P0", "TRUNC", "P.DropRate").Success);
        Assert.Equal("1", commands.Format(context, "P0").Text);
    }

    [Fact]
    public void HotReloadCompilerExposesVariableDeclarationAndCSharpCommandApi()
    {
        string root = Path.Combine(Path.GetTempPath(), "lyo-var01-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "Variables.cs"), """
                using Server.MirObjects;
                using Server.Scripting;
                using Server.Scripting.Variables;

                public sealed class VariableModule : IScriptModule
                {
                    public void Register(ScriptRegistry registry)
                    {
                        registry.RegisterVariable(
                            ScriptVariableScope.P, "DropRate", ScriptVariableKind.Decimal, "1.5");
                    }

                    public static bool Exercise(ScriptApi api, PlayerObject player, NpcPageCall call)
                    {
                        if (!api.MutateVariable(player, call, "P0", "MOV", "1").Success) return false;
                        if (!api.MutateVariable(player, call, "P.DropRate", "INC", "0.25").Success) return false;
                        if (!api.CheckVariable(player, call, "P.DropRate", ">", "1").Matched) return false;
                        return api.FormatVariable(player, call, "P.DropRate", 2).Success;
                    }
                }
                """);

            ScriptCompileResult compiled = new ScriptCompiler().CompileFromDirectory(
                root, "LomScripts_Var01_" + Guid.NewGuid().ToString("N"), debugBuild: false);
            Assert.True(compiled.Success, string.Join(Environment.NewLine, compiled.Diagnostics));

            var loadContext = new ScriptLoadContext();
            try
            {
                using var assemblyStream = new MemoryStream(compiled.AssemblyBytes);
                using var symbolsStream = new MemoryStream(compiled.PdbBytes);
                var assembly = loadContext.LoadFromStream(assemblyStream, symbolsStream);
                var registry = new ScriptRegistry();
                ScriptManager.RegisterModules(assembly, registry);
                Assert.Equal(ScriptVariableKind.Decimal, registry.VariableDeclarations
                    .GetRequired(ScriptVariableScope.P, "DropRate").Kind);
            }
            finally
            {
                loadContext.Unload();
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void TxtNpcParserRoutesPVariablesToUnifiedCommandsWithoutChangingLegacyA0()
    {
        var page = new NPCPage("[@MAIN]");
        var segment = new NPCSegment(
            page, new List<string>(), new List<string>(), new List<string>(),
            new List<string>(), new List<string>());
        var actions = new List<NPCActions>();

        segment.ParseAct(actions, "MOV P0 5");
        segment.ParseAct(actions, "INC P.DropRate 0.25");
        segment.ParseAct(actions, "DIV P.DropRate 2");
        segment.ParseAct(actions, "MOV A0 legacy");
        segment.ParseAct(actions, "MOV P0 FLOOR P.DropRate");
        segment.ParseCheck("CHECK P.DropRate >= 12.5");

        Assert.Collection(actions,
            action =>
            {
                Assert.Equal(ActionType.VariableMutate, action.Type);
                Assert.Equal(new[] { "P0", "MOV", "5" }, action.Params);
            },
            action =>
            {
                Assert.Equal(ActionType.VariableMutate, action.Type);
                Assert.Equal(new[] { "P.DropRate", "INC", "0.25" }, action.Params);
            },
            action =>
            {
                Assert.Equal(ActionType.VariableMutate, action.Type);
                Assert.Equal(new[] { "P.DropRate", "DIV", "2" }, action.Params);
            },
            action => Assert.Equal(ActionType.Mov, action.Type),
            action =>
            {
                Assert.Equal(ActionType.VariableConvert, action.Type);
                Assert.Equal(new[] { "P0", "FLOOR", "P.DropRate" }, action.Params);
            });
        NPCChecks variableCheck = Assert.Single(segment.CheckList);
        Assert.Equal(CheckType.Variable, variableCheck.Type);
        Assert.Equal(new[] { "P.DropRate", ">=", "12.5" }, variableCheck.Params);
    }

    [Fact]
    public void TxtNpcParserRoutesAllVar02RuntimePrefixesToTheUnifiedModule()
    {
        var page = new NPCPage("[@MAIN]");
        var segment = new NPCSegment(
            page, new List<string>(), new List<string>(), new List<string>(),
            new List<string>(), new List<string>());

        foreach (string line in new[]
                 { "MOV D0 1", "INC M0 2", "MOV N$Score 3", "MOV S$Label 在线", "DIV I0 2" })
            segment.ParseAct(segment.ActList, line);
        segment.ParseCheck("CHECK N$Score >= 3");

        Assert.Equal(5, segment.ActList.Count);
        Assert.All(segment.ActList, action => Assert.Equal(ActionType.VariableMutate, action.Type));
        Assert.Equal(new[] { "D0", "MOV", "1" }, segment.ActList[0].Params);
        Assert.Equal(new[] { "M0", "INC", "2" }, segment.ActList[1].Params);
        Assert.Equal(new[] { "N$Score", "MOV", "3" }, segment.ActList[2].Params);
        Assert.Equal(new[] { "S$Label", "MOV", "在线" }, segment.ActList[3].Params);
        Assert.Equal(new[] { "I0", "DIV", "2" }, segment.ActList[4].Params);
        Assert.Equal(CheckType.Variable, Assert.Single(segment.CheckList).Type);
    }

    private static ScriptVariableDeclaration Declaration(
        ScriptVariableScope scope,
        string key,
        ScriptVariableKind kind,
        string defaultValue) =>
        new(scope, key, kind, defaultValue, "ScriptVariableModuleTests.cs", 1);
}
