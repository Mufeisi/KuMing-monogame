using System.Globalization;
using Server.Scripting.Variables;
using Server.Scripting;
using Server.MirObjects;
using Server.MirDatabase;
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
    public void TxtNpcParserRoutesPVariablesAndOriginalA0ToUnifiedCommands()
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
            action =>
            {
                Assert.Equal(ActionType.VariableMutate, action.Type);
                Assert.Equal(new[] { "A0", "MOV", "legacy" }, action.Params);
            },
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

    [Fact]
    public void PrivatePersistentVariablesUseCharacterStorageAndRequestAutoSave()
    {
        var catalog = new ScriptVariableDeclarationCatalog();
        Assert.True(catalog.TryReload(new[]
        {
            Declaration(ScriptVariableScope.U, "DropRate", ScriptVariableKind.Decimal, "1.5"),
            Declaration(ScriptVariableScope.T, "Title", ScriptVariableKind.String, "无")
        }).Success);
        int saveRequests = 0;
        var module = new ScriptVariableModule(catalog, requestAutoSave: () => saveRequests++);
        var character = new CharacterInfo();
        ScriptVariableContext context = ScriptVariableContext.ForPlayer(character);

        Assert.True(module.Mutate(context, ScriptVariableMutation.Set(
            ScriptVariableReference.Named(ScriptVariableScope.U, "DropRate"),
            ScriptVariableValue.FromDecimal(12.75m))).Success);
        Assert.True(module.Mutate(context, ScriptVariableMutation.Set(
            ScriptVariableReference.Legacy(ScriptVariableScope.T, 0),
            ScriptVariableValue.FromString("勇者"))).Success);

        var reloggedModule = new ScriptVariableModule(catalog);
        Assert.Equal("12.75", reloggedModule.Read(context,
            ScriptVariableReference.Named(ScriptVariableScope.U, "DropRate")).Value.Format());
        Assert.Equal("勇者", reloggedModule.Read(context,
            ScriptVariableReference.Legacy(ScriptVariableScope.T, 0)).Value.Text);
        Assert.Equal(2, saveRequests);

        ScriptVariableReadResult outOfRange = module.Read(context,
            ScriptVariableReference.Legacy(ScriptVariableScope.U, 500));
        Assert.False(outOfRange.Success);
        Assert.Equal(ScriptVariableErrorCode.UnknownReference, outOfRange.ErrorCode);
    }

    [Fact]
    public void PrivatePersistentStoreBinaryRoundTripPreservesIntegerDecimalAndString()
    {
        var source = new CharacterScriptVariableStore();
        source.Set(ScriptVariableScope.U, "#0", ScriptVariableValue.FromInteger(long.MaxValue));
        source.Set(ScriptVariableScope.U, "droprate", ScriptVariableValue.FromDecimal(0.125m));
        source.Set(ScriptVariableScope.T, "#1", ScriptVariableValue.FromString("中文标题"));
        source.EnsureDailyPeriod(20260815);
        source.Set(ScriptVariableScope.J, "#2", ScriptVariableValue.FromInteger(7));
        source.Set(ScriptVariableScope.Z, "#3", ScriptVariableValue.FromString("今日"));
        source.Set(ScriptVariableScope.Human, "score", ScriptVariableValue.FromDecimal(2.5m));
        source.Set(ScriptVariableScope.Guild, "score", ScriptVariableValue.FromInteger(9));

        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
            source.Save(writer);
        stream.Position = 0;
        var restored = new CharacterScriptVariableStore();
        using (var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true))
            restored.Load(reader);

        Assert.Equal(7, restored.Count);
        Assert.Equal(20260815, restored.DailyResetPeriodId);
        Assert.True(restored.TryGet(ScriptVariableScope.U, "#0", out var integer));
        Assert.Equal(long.MaxValue, integer.Integer);
        Assert.True(restored.TryGet(ScriptVariableScope.U, "droprate", out var decimalValue));
        Assert.Equal(0.125m, decimalValue.Decimal);
        Assert.True(restored.TryGet(ScriptVariableScope.T, "#1", out var text));
        Assert.Equal("中文标题", text.Text);
        Assert.True(restored.TryGet(ScriptVariableScope.J, "#2", out var daily));
        Assert.Equal(7L, daily.Integer);
        Assert.True(restored.TryGet(ScriptVariableScope.Guild, "score", out var guild));
        Assert.Equal(9L, guild.Integer);
    }

    [Fact]
    public void CharacterStoreReadsVar03PayloadWithoutDailyPeriodHeader()
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(1);
            writer.Write((byte)ScriptVariableScope.U);
            writer.Write("#0");
            writer.Write((byte)ScriptVariableKind.Integer);
            writer.Write(42L);
        }
        stream.Position = 0;
        var restored = new CharacterScriptVariableStore();
        using (var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true))
            restored.Load(reader, includesDailyPeriod: false);

        Assert.Equal(0, restored.DailyResetPeriodId);
        Assert.True(restored.TryGet(ScriptVariableScope.U, "#0", out var value));
        Assert.Equal(42L, value.Integer);
    }

    [Fact]
    public void PrivatePersistentDeclarationsRejectScopeKindMismatch()
    {
        ArgumentException uString = Assert.Throws<ArgumentException>(() =>
            Declaration(ScriptVariableScope.U, "Title", ScriptVariableKind.String, "错误"));
        ArgumentException tDecimal = Assert.Throws<ArgumentException>(() =>
            Declaration(ScriptVariableScope.T, "Rate", ScriptVariableKind.Decimal, "1.5"));

        Assert.Contains("U 作用域", uString.Message);
        Assert.Contains("T 作用域", tDecimal.Message);
    }

    [Fact]
    public void ServerPersistentVariablesAreSharedAndRequestTheirOwnAutoSave()
    {
        var catalog = new ScriptVariableDeclarationCatalog();
        Assert.True(catalog.TryReload(new[]
        {
            Declaration(ScriptVariableScope.G, "EventRate", ScriptVariableKind.Decimal, "1.0"),
            Declaration(ScriptVariableScope.A, "Notice", ScriptVariableKind.String, "未开放")
        }).Success);
        var store = new ServerScriptVariableStore();
        int saveRequests = 0;
        var module = new ScriptVariableModule(
            catalog,
            serverPersistent: store,
            requestServerAutoSave: () => saveRequests++);
        ScriptVariableContext playerOne = ScriptVariableContext.ForPlayer(new object());
        ScriptVariableContext playerTwo = ScriptVariableContext.ForPlayer(new object());

        Assert.True(module.Mutate(playerOne, ScriptVariableMutation.Set(
            ScriptVariableReference.Named(ScriptVariableScope.G, "EventRate"),
            ScriptVariableValue.FromDecimal(2.75m))).Success);
        Assert.True(module.Mutate(playerTwo, ScriptVariableMutation.Set(
            ScriptVariableReference.Legacy(ScriptVariableScope.A, 0),
            ScriptVariableValue.FromString("全服公告"))).Success);

        Assert.Equal("2.75", module.Read(playerTwo,
            ScriptVariableReference.Named(ScriptVariableScope.G, "EventRate")).Value.Format());
        Assert.Equal("全服公告", module.Read(playerOne,
            ScriptVariableReference.Legacy(ScriptVariableScope.A, 0)).Value.Text);
        Assert.Equal(2, saveRequests);

        var restartedModule = new ScriptVariableModule(catalog, serverPersistent: store);
        Assert.Equal("2.75", restartedModule.Read(ScriptVariableContext.ForServer(),
            ScriptVariableReference.Named(ScriptVariableScope.G, "EventRate")).Value.Format());
    }

    [Fact]
    public void ServerPersistentJsonIsAtomicAndFallsBackToBackup()
    {
        string root = Path.Combine(Path.GetTempPath(), "LyoCrystal-GA-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(root, "Server.Variables.json");
        try
        {
            var store = new ServerScriptVariableStore();
            store.Set(ScriptVariableScope.G, "RATE", ScriptVariableValue.FromDecimal(1.25m));
            store.Set(ScriptVariableScope.A, "#0", ScriptVariableValue.FromString("第一版"));
            store.SaveJson(path);
            store.Set(ScriptVariableScope.G, "RATE", ScriptVariableValue.FromDecimal(2.5m));
            store.SaveJson(path);

            File.WriteAllText(path, "{损坏JSON");
            var restored = new ServerScriptVariableStore();
            restored.LoadJson(path);

            Assert.True(restored.TryGet(ScriptVariableScope.G, "rate", out var rate));
            Assert.Equal(1.25m, rate.Decimal);
            Assert.True(restored.TryGet(ScriptVariableScope.A, "#0", out var text));
            Assert.Equal("第一版", text.Text);
            Assert.False(File.Exists(path + ".tmp"));

            File.WriteAllText(path,
                "{\"schemaVersion\":1,\"variables\":[{\"namespace\":\"A\",\"key\":\"#0\",\"kind\":\"Integer\",\"integerValue\":9}]}");
            restored.LoadJson(path);
            Assert.True(restored.TryGet(ScriptVariableScope.A, "#0", out text));
            Assert.Equal("第一版", text.Text);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ServerPersistentDeclarationsRejectScopeKindMismatch()
    {
        Assert.Throws<ArgumentException>(() =>
            Declaration(ScriptVariableScope.G, "Notice", ScriptVariableKind.String, "错误"));
        Assert.Throws<ArgumentException>(() =>
            Declaration(ScriptVariableScope.A, "Rate", ScriptVariableKind.Decimal, "1.5"));
    }

    [Fact]
    public void ScriptManagerAutoSaveCallbacksBelongToTheirOwningEnvironment()
    {
        var envir = new Server.MirEnvir.Envir();
        var character = new CharacterInfo();

        Assert.True(envir.CSharpScripts.VariableModule.Mutate(
            ScriptVariableContext.ForPlayer(character),
            ScriptVariableMutation.Set(
                ScriptVariableReference.Legacy(ScriptVariableScope.U, 0),
                ScriptVariableValue.FromInteger(1))).Success);
        Assert.True(envir.CSharpScripts.VariableModule.Mutate(
            ScriptVariableContext.ForServer(),
            ScriptVariableMutation.Set(
                ScriptVariableReference.Legacy(ScriptVariableScope.G, 0),
                ScriptVariableValue.FromInteger(2))).Success);

        Assert.True(envir.HasPendingAutoSave);
        Assert.True(envir.HasPendingServerVariableAutoSave);
        Assert.True(envir.ScriptVariables.TryGet(
            ScriptVariableScope.G, "#0", out var global));
        Assert.Equal(2L, global.Integer);
    }

    [Fact]
    public void DailyVariablesResetForwardOnceAndIgnoreClockRollback()
    {
        long period = 20260815;
        int saveRequests = 0;
        var catalog = new ScriptVariableDeclarationCatalog();
        Assert.True(catalog.TryReload(new[]
        {
            Declaration(ScriptVariableScope.J, "Rate", ScriptVariableKind.Decimal, "1.5"),
            Declaration(ScriptVariableScope.Z, "Label", ScriptVariableKind.String, "未开始"),
            Declaration(ScriptVariableScope.Human, "Lifetime", ScriptVariableKind.Integer, "0")
        }).Success);
        var module = new ScriptVariableModule(
            catalog,
            requestAutoSave: () => saveRequests++,
            currentDailyPeriod: () => period);
        var character = new CharacterInfo();
        ScriptVariableContext context = ScriptVariableContext.ForPlayer(character);

        Assert.True(module.Mutate(context, ScriptVariableMutation.Set(
            ScriptVariableReference.Legacy(ScriptVariableScope.J, 0),
            ScriptVariableValue.FromInteger(9))).Success);
        Assert.True(module.Mutate(context, ScriptVariableMutation.Set(
            ScriptVariableReference.Named(ScriptVariableScope.Z, "Label"),
            ScriptVariableValue.FromString("进行中"))).Success);
        Assert.True(module.Mutate(context, ScriptVariableMutation.Set(
            ScriptVariableReference.Named(ScriptVariableScope.Human, "Lifetime"),
            ScriptVariableValue.FromInteger(88))).Success);

        period = 20260816;
        Assert.False(module.Read(context,
            ScriptVariableReference.Legacy(ScriptVariableScope.J, 0)).Found);
        Assert.Equal("未开始", module.Read(context,
            ScriptVariableReference.Named(ScriptVariableScope.Z, "Label")).Value.Text);
        Assert.Equal(88L, module.Read(context,
            ScriptVariableReference.Named(ScriptVariableScope.Human, "Lifetime")).Value.Integer);

        Assert.True(module.Mutate(context, ScriptVariableMutation.Set(
            ScriptVariableReference.Legacy(ScriptVariableScope.J, 0),
            ScriptVariableValue.FromInteger(5))).Success);
        period = 20260815;
        Assert.Equal(5L, module.Read(context,
            ScriptVariableReference.Legacy(ScriptVariableScope.J, 0)).Value.Integer);
        Assert.Equal(20260816, character.ScriptVariables.DailyResetPeriodId);
        Assert.True(saveRequests >= 5);
    }

    [Fact]
    public void DailyPeriodSupportsConfiguredHourAndRejectsInvalidConfiguration()
    {
        Assert.Equal(20260814,
            ScriptVariableDailyPeriod.FromServerTime(new DateTime(2026, 8, 15, 3, 59, 0), 4));
        Assert.Equal(20260815,
            ScriptVariableDailyPeriod.FromServerTime(new DateTime(2026, 8, 15, 4, 0, 0), 4));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ScriptVariableDailyPeriod.FromServerTime(DateTime.Now, 24));
    }

    [Fact]
    public void EnvironmentDailyBatchClearsCharactersAndHeroesAndRequestsSave()
    {
        var envir = new Server.MirEnvir.Envir();
        var character = new CharacterInfo();
        var hero = new HeroInfo();
        foreach (CharacterInfo owner in new CharacterInfo[] { character, hero })
        {
            owner.ScriptVariables.EnsureDailyPeriod(20260815);
            owner.ScriptVariables.Set(
                ScriptVariableScope.J, "#0", ScriptVariableValue.FromInteger(1));
            owner.ScriptVariables.Set(
                ScriptVariableScope.Human, "score", ScriptVariableValue.FromInteger(2));
        }
        envir.CharacterList.Add(character);
        envir.HeroList.Add(hero);

        envir.ProcessDailyScriptVariables(20260816);

        Assert.False(character.ScriptVariables.TryGet(
            ScriptVariableScope.J, "#0", out _));
        Assert.False(hero.ScriptVariables.TryGet(
            ScriptVariableScope.J, "#0", out _));
        Assert.True(character.ScriptVariables.TryGet(
            ScriptVariableScope.Human, "score", out _));
        Assert.True(envir.HasPendingAutoSave);
    }

    [Fact]
    public void HumanGuildAndGlobalVariablesFollowTheirOwners()
    {
        var catalog = new ScriptVariableDeclarationCatalog();
        Assert.True(catalog.TryReload(new[]
        {
            Declaration(ScriptVariableScope.Human, "Score", ScriptVariableKind.Decimal, "0"),
            Declaration(ScriptVariableScope.Guild, "Score", ScriptVariableKind.Integer, "0"),
            Declaration(ScriptVariableScope.Global, "Score", ScriptVariableKind.Integer, "0")
        }).Success);
        var server = new ServerScriptVariableStore();
        var module = new ScriptVariableModule(catalog, serverPersistent: server);
        var firstCharacter = new CharacterInfo();
        var secondCharacter = new CharacterInfo();
        var firstGuild = new GuildInfo { GuildIndex = 1, Name = "甲" };
        var secondGuild = new GuildInfo { GuildIndex = 2, Name = "乙" };

        Assert.True(module.Mutate(ScriptVariableContext.ForPlayer(firstCharacter),
            ScriptVariableMutation.Set(
                ScriptVariableReference.Named(ScriptVariableScope.Human, "Score"),
                ScriptVariableValue.FromDecimal(2.5m))).Success);
        Assert.False(module.Read(ScriptVariableContext.ForPlayer(secondCharacter),
            ScriptVariableReference.Named(ScriptVariableScope.Human, "Score")).Found);

        Assert.True(module.Mutate(ScriptVariableContext.ForPlayer(firstGuild),
            ScriptVariableMutation.Set(
                ScriptVariableReference.Named(ScriptVariableScope.Guild, "Score"),
                ScriptVariableValue.FromInteger(7))).Success);
        Assert.Equal(7L, module.Read(ScriptVariableContext.ForPlayer(firstGuild),
            ScriptVariableReference.Named(ScriptVariableScope.Guild, "Score")).Value.Integer);
        Assert.False(module.Read(ScriptVariableContext.ForPlayer(secondGuild),
            ScriptVariableReference.Named(ScriptVariableScope.Guild, "Score")).Found);
        Assert.True(firstGuild.NeedSave);

        Assert.True(module.Mutate(ScriptVariableContext.ForServer(),
            ScriptVariableMutation.Set(
                ScriptVariableReference.Named(ScriptVariableScope.Global, "Score"),
                ScriptVariableValue.FromInteger(99))).Success);
        Assert.Equal(99L, module.Read(ScriptVariableContext.ForPlayer(firstCharacter),
            ScriptVariableReference.Named(ScriptVariableScope.Global, "Score")).Value.Integer);
    }

    [Fact]
    public void Var05ScopesRejectInvalidKindsAndFixedCustomReferences()
    {
        Assert.Throws<ArgumentException>(() =>
            Declaration(ScriptVariableScope.J, "Text", ScriptVariableKind.String, "x"));
        Assert.Throws<ArgumentException>(() =>
            Declaration(ScriptVariableScope.Z, "Count", ScriptVariableKind.Integer, "0"));
        Assert.Throws<ArgumentException>(() =>
            Declaration(ScriptVariableScope.Guild, "Text", ScriptVariableKind.String, "x"));

        var module = new ScriptVariableModule(new ScriptVariableDeclarationCatalog());
        ScriptVariableReadResult result = module.Read(
            ScriptVariableContext.ForPlayer(new CharacterInfo()),
            ScriptVariableReference.Legacy(ScriptVariableScope.Human, 0));
        Assert.False(result.Success);
        Assert.Equal(ScriptVariableErrorCode.UnknownReference, result.ErrorCode);

        ScriptVariableResetResult guildReset = module.Reset(
            ScriptVariableContext.ForPlayer(new CharacterInfo()),
            ScriptVariableSelector.ScopeOnly(ScriptVariableScope.Guild));
        Assert.False(guildReset.Success);
        Assert.Equal(ScriptVariableErrorCode.ContextUnavailable, guildReset.ErrorCode);
    }

    [Fact]
    public void TxtNpcParserRoutesPrivatePersistentPrefixesToUnifiedCommands()
    {
        var page = new NPCPage("[@MAIN]");
        var segment = new NPCSegment(
            page, new List<string>(), new List<string>(), new List<string>(),
            new List<string>(), new List<string>());

        segment.ParseAct(segment.ActList, "MOV U0 7");
        segment.ParseAct(segment.ActList, "INC U.DropRate 0.25");
        segment.ParseAct(segment.ActList, "MOV T0 永久称号");
        segment.ParseCheck("CHECK U.DropRate >= 1.5");

        Assert.Equal(3, segment.ActList.Count);
        Assert.All(segment.ActList, action => Assert.Equal(ActionType.VariableMutate, action.Type));
        Assert.Equal(CheckType.Variable, Assert.Single(segment.CheckList).Type);
    }

    [Fact]
    public void TxtNpcParserRoutesServerPersistentPrefixesToUnifiedCommands()
    {
        var page = new NPCPage("[@MAIN]");
        var segment = new NPCSegment(
            page, new List<string>(), new List<string>(), new List<string>(),
            new List<string>(), new List<string>());

        segment.ParseAct(segment.ActList, "MOV G0 7");
        segment.ParseAct(segment.ActList, "INC G.EventRate 0.25");
        segment.ParseAct(segment.ActList, "MOV A.Notice 全服公告");
        segment.ParseCheck("CHECK G.EventRate >= 1.5");

        Assert.Equal(3, segment.ActList.Count);
        Assert.All(segment.ActList, action => Assert.Equal(ActionType.VariableMutate, action.Type));
        Assert.Equal(CheckType.Variable, Assert.Single(segment.CheckList).Type);
    }

    [Fact]
    public void TxtNpcParserRoutesVar05PrefixesToUnifiedCommands()
    {
        var segment = new NPCSegment(
            new NPCPage("[@MAIN]"), new List<string>(), new List<string>(), new List<string>(),
            new List<string>(), new List<string>());
        foreach (string line in new[]
                 { "MOV J0 1", "MOV Z0 今日", "INC HUMAN.Score 2", "INC GUILD.Score 3", "INC GLOBAL.Score 4" })
            segment.ParseAct(segment.ActList, line);

        Assert.Equal(5, segment.ActList.Count);
        Assert.All(segment.ActList, action => Assert.Equal(ActionType.VariableMutate, action.Type));
    }

    [Fact]
    public void CompositeListsSupportNegativeIndexesMutationSortSliceAndStrictQuota()
    {
        var module = new ScriptVariableModule(new ScriptVariableDeclarationCatalog());
        var commands = new ScriptVariableCommands(module);
        ScriptVariableContext context = ScriptVariableContext.ForPlayer(new object());

        Assert.True(commands.Mutate(context, "L$Bag", "MOV", "[11,33,22,aa]").Success);
        Assert.Equal("aa", commands.Composites.Read(context, "L$Bag[-1]").Value.Text);
        Assert.True(commands.Mutate(context, "L$Bag[1]", "MOV", "44").Success);
        Assert.True(commands.Composites.InsertList(context, "L$Bag", "20", 1).Success);
        Assert.True(commands.Composites.RemoveListByContent(context, "L$Bag", "aa").Success);
        Assert.True(commands.Composites.SortList(context, "L$Bag", "L$Sorted", false, true).Success);
        Assert.Equal("[11,20,22,44]", commands.Format(context, "L$Sorted").Text);
        Assert.True(commands.Composites.SliceList(context, "L$Sorted", "L$Slice", 1, -1, 2).Success);
        Assert.Equal("[20,44]", commands.Format(context, "L$Slice").Text);

        string overQuota = "[" + string.Join(',', Enumerable.Range(0, 257)) + "]";
        ScriptVariableMutationResult rejected = commands.Mutate(context, "L$TooMany", "MOV", overQuota);
        Assert.False(rejected.Success);
        Assert.Equal(ScriptVariableErrorCode.QuotaExceeded, rejected.ErrorCode);
    }

    [Fact]
    public void CompositeDictionariesSupportKeyMutationItemsChecksAndNumericExtremum()
    {
        var module = new ScriptVariableModule(new ScriptVariableDeclarationCatalog());
        var commands = new ScriptVariableCommands(module);
        ScriptVariableContext context = ScriptVariableContext.ForPlayer(new object());

        Assert.True(commands.Mutate(context, "D$Score", "MOV", "{张三:100,李四:200}").Success);
        Assert.True(commands.Mutate(context, "D$Score[王五]", "MOV", "300").Success);
        Assert.True(commands.Mutate(context, "D$Score", "DEC", "张三").Success);
        Assert.Equal("200", commands.Composites.Read(context, "D$Score[李四]").Value.Text);
        Assert.True(commands.Composites.Contains(context, "D$Score", "300", dictionaryValues: true).Matched);
        Assert.True(commands.Composites.DictionaryItems(context, "D$Score", "L$Keys", values: false).Success);
        Assert.Equal("[李四,王五]", commands.Format(context, "L$Keys").Text);
        ScriptCompositeResult maximum = commands.Composites.NumericExtremum(context, "D$Score", maximum: true);
        Assert.True(maximum.Success);
        Assert.Equal(300m, maximum.Value.Decimal);
        Assert.Equal("王五", maximum.Diagnostic);
    }

    [Fact]
    public void DecimalFormulaIsBoundedAtomicAndSupportsInclusiveControlledRandom()
    {
        var catalog = new ScriptVariableDeclarationCatalog();
        Assert.True(catalog.TryReload(new[]
        {
            Declaration(ScriptVariableScope.P, "Rate", ScriptVariableKind.Decimal, "1.5"),
            Declaration(ScriptVariableScope.P, "Result", ScriptVariableKind.Decimal, "9"),
            Declaration(ScriptVariableScope.P, "IntegerResult", ScriptVariableKind.Integer, "7")
        }).Success);
        var commands = new ScriptVariableCommands(new ScriptVariableModule(catalog));
        ScriptVariableContext context = ScriptVariableContext.ForConversation(new object(), 1);

        ScriptVariableMutationResult success = commands.Formulate(
            context, "(P.Rate+0.5)*2+Random(1,3)^2", "P.Result", (minimum, maximum) => maximum - 1);
        Assert.True(success.Success, success.Diagnostic);
        Assert.Equal(13m, success.NewValue.Decimal);

        ScriptVariableMutationResult divideByZero = commands.Formulate(context, "1/(3-3)", "P.Result");
        Assert.False(divideByZero.Success);
        Assert.Equal(ScriptVariableErrorCode.InvalidExpression, divideByZero.ErrorCode);
        Assert.Equal(13m, commands.Format(context, "P.Result").Success
            ? decimal.Parse(commands.Format(context, "P.Result").Text, CultureInfo.InvariantCulture)
            : -1m);

        ScriptVariableMutationResult implicitTruncate = commands.Formulate(context, "1/2", "P.IntegerResult");
        Assert.False(implicitTruncate.Success);
        Assert.Equal(ScriptVariableErrorCode.TypeMismatch, implicitTruncate.ErrorCode);
        Assert.False(commands.Formulate(context, new string('1', 1025), "P.Result").Success);
    }

    [Fact]
    public void ProbabilityUsesExplicitUnitsAndExactIntegerThresholdBoundaries()
    {
        Assert.True(ScriptVariableProbability.Check(12.5m, ScriptProbabilityUnit.Percent, 124_999).Matched);
        Assert.False(ScriptVariableProbability.Check(12.5m, ScriptProbabilityUnit.Percent, 125_000).Matched);
        Assert.True(ScriptVariableProbability.Check(0.5m, ScriptProbabilityUnit.Fraction, 499_999).Matched);
        Assert.False(ScriptVariableProbability.Check(0.5m, ScriptProbabilityUnit.Percent, 5_000).Matched);
        Assert.False(ScriptVariableProbability.Check(-0.1m, ScriptProbabilityUnit.Percent, 0).Success);
        Assert.False(ScriptVariableProbability.Check(100.01m, ScriptProbabilityUnit.Percent, 0).Success);
    }

    [Fact]
    public void TxtNpcParserRoutesCompositeFormulaAndChanceCommands()
    {
        var segment = new NPCSegment(
            new NPCPage("[@MAIN]"), new List<string>(), new List<string>(), new List<string>(),
            new List<string>(), new List<string>());

        segment.ParseAct(segment.ActList, "MOV L$Bag [1,2,3]");
        segment.ParseAct(segment.ActList, "MOV D$Score {张三:100}");
        segment.ParseAct(segment.ActList, "INSERTTOLIST L$Bag 9 -1");
        segment.ParseAct(segment.ActList, "GETDICTITEMS D$Score 0 L$Keys");
        segment.ParseAct(segment.ActList, "FORMULATION (P.Rate + 0.5) * 2 P.Result");
        segment.ParseCheck("CHANCE P.Rate PERCENT");
        segment.ParseCheck("CHECKVARINLIST L$Bag 2");

        Assert.Equal(ActionType.VariableMutate, segment.ActList[0].Type);
        Assert.Equal(ActionType.VariableMutate, segment.ActList[1].Type);
        Assert.Equal(ActionType.VariableComposite, segment.ActList[2].Type);
        Assert.Equal(ActionType.VariableComposite, segment.ActList[3].Type);
        Assert.Equal(ActionType.VariableFormulation, segment.ActList[4].Type);
        Assert.Equal(CheckType.VariableChance, segment.CheckList[0].Type);
        Assert.Equal(CheckType.VariableComposite, segment.CheckList[1].Type);
    }

    [Fact]
    public void FormulaEvaluationHasAConservativeMainThreadPerformanceGate()
    {
        var catalog = new ScriptVariableDeclarationCatalog();
        Assert.True(catalog.TryReload(new[]
        {
            Declaration(ScriptVariableScope.P, "Rate", ScriptVariableKind.Decimal, "1.25"),
            Declaration(ScriptVariableScope.P, "Result", ScriptVariableKind.Decimal, "0")
        }).Success);
        var commands = new ScriptVariableCommands(new ScriptVariableModule(catalog));
        ScriptVariableContext context = ScriptVariableContext.ForConversation(new object(), 1);
        var watch = System.Diagnostics.Stopwatch.StartNew();

        for (int i = 0; i < 1_000; i++)
            Assert.True(commands.Formulate(context, "(P.Rate+0.75)*2", "P.Result").Success);

        watch.Stop();
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(3),
            $"1000 次受控公式计算耗时 {watch.Elapsed}，超过 3 秒门禁。");
    }

    [Fact]
    public void TxtNpcParserRoutesCurrentTargetAndOnlineHumanTransferCommands()
    {
        var segment = new NPCSegment(
            new NPCPage("[@MAIN]"), new List<string>(), new List<string>(), new List<string>(),
            new List<string>(), new List<string>());

        segment.ParseAct(segment.ActList, "SETCURRTARGET S$TargetName");
        segment.ParseAct(segment.ActList, "SETHUMVAR S$TargetName HUMAN.Shared P.Rate");
        segment.ParseAct(segment.ActList, "GETHUMVAR S$TargetName HUMAN.Shared P.Result");

        Assert.Equal(ActionType.VariableSetCurrentTarget, segment.ActList[0].Type);
        Assert.Equal(ActionType.VariableSetHuman, segment.ActList[1].Type);
        Assert.Equal(ActionType.VariableGetHuman, segment.ActList[2].Type);
    }

    private static ScriptVariableDeclaration Declaration(
        ScriptVariableScope scope,
        string key,
        ScriptVariableKind kind,
        string defaultValue) =>
        new(scope, key, kind, defaultValue, "ScriptVariableModuleTests.cs", 1);
}
