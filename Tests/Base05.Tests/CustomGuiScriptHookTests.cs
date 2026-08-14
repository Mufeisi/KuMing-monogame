using Server.CustomGui;
using Server.MirDatabase;
using Server.MirObjects;
using Server.Scripting;
using Shared.CustomGui;
using C = ClientPackets;
using Xunit;

namespace Base05.Tests;

public sealed class CustomGuiScriptHookTests
{
    [Fact]
    public void RegisteredWindowBuildsBoundedSnapshotAndReusesAuthorityRules()
    {
        var registry = new CustomGuiScriptRegistry();
        var sourceState = new List<CustomGuiStateEntry>
        {
            CustomGuiStateEntry.Text("title", "兑换活动")
        };
        int commits = 0;
        registry.Register(new CustomGuiScriptWindowDefinition
        {
            DocumentId = "activity.exchange",
            DocumentRevision = 3,
            PackageSequence = 7,
            InitialStateRevision = 2,
            Lifetime = TimeSpan.FromMinutes(5),
            ProvideState = (_, _) => sourceState,
            Actions = new[]
            {
                new CustomGuiActionRule
                {
                    ActionId = "exchange.submit",
                    Action = CustomGuiActionKind.SubmitSelection,
                    MinimumSelections = 1,
                    MaximumSelections = 1,
                    AllowedSelections = new HashSet<string>(StringComparer.Ordinal) { "reward.a" },
                    Prepare = (_, _) => new CustomGuiDelegateTransaction(
                        () => { commits++; return "兑换成功"; },
                        () => commits--)
                }
            }
        });

        CustomGuiScriptPlanResult prepared = registry.PrepareOpen(
            new ScriptContext(), Player(), "activity.exchange",
            DateTimeOffset.Parse("2026-08-14T10:00:00Z"));
        Assert.True(prepared.Success, prepared.Diagnostic);
        CustomGuiScriptOpenPlan plan = Assert.IsType<CustomGuiScriptOpenPlan>(prepared.Plan);
        Assert.Equal((uint)3, plan.DocumentRevision);
        Assert.Equal(7, plan.PackageSequence);
        Assert.Equal((uint)2, plan.StateRevision);
        Assert.Equal("兑换活动", Assert.Single(plan.State).TextValue);

        sourceState[0].TextValue = "被外部改写";
        Assert.Equal("兑换活动", Assert.Single(plan.State).TextValue);

        var authority = new CustomGuiActionAuthority();
        authority.RegisterBatch(plan.Actions);
        C.CustomGuiAction action = new()
        {
            WindowInstanceId = 1,
            DocumentId = "activity.exchange",
            DocumentRevision = 3,
            PackageSequence = 7,
            SessionNonce = Guid.NewGuid(),
            RequestSequence = 1,
            Action = CustomGuiActionKind.SubmitSelection,
            ActionId = "exchange.submit",
            SelectionIds = new() { "reward.a" }
        };
        ServerPackets.CustomGuiActionResult result = authority.Handle(Player(), action, 2);
        Assert.Equal(CustomGuiActionResultKind.Accepted, result.Result);
        Assert.Equal(1, commits);
    }

    [Fact]
    public void UnknownWindowAndStateProviderFailureAreIsolatedWithStableDiagnostics()
    {
        var errors = new List<(string Code, Type Type)>();
        var registry = new CustomGuiScriptRegistry((code, error) => errors.Add((code, error.GetType())));
        registry.Register(new CustomGuiScriptWindowDefinition
        {
            DocumentId = "activity.failure",
            DocumentRevision = 1,
            PackageSequence = 1,
            ProvideState = (_, _) => throw new InvalidOperationException("不应泄漏的内部信息"),
            Actions = new[] { Rule("submit") }
        });

        CustomGuiScriptPlanResult missing = registry.PrepareOpen(
            new ScriptContext(), Player(), "missing", DateTimeOffset.UtcNow);
        CustomGuiScriptPlanResult failed = registry.PrepareOpen(
            new ScriptContext(), Player(), "activity.failure", DateTimeOffset.UtcNow);

        Assert.False(missing.Success);
        Assert.Contains("GUI11-HOOK-WINDOW", missing.Diagnostic);
        Assert.False(failed.Success);
        Assert.Contains("GUI11-HOOK-DATA", failed.Diagnostic);
        Assert.DoesNotContain("内部信息", failed.Diagnostic);
        Assert.Equal(("GUI11-HOOK-DATA", typeof(InvalidOperationException)), Assert.Single(errors));
    }

    [Fact]
    public void InvalidStateAndUnregisteredActionFailClosedBeforeHookExecution()
    {
        int prepareCalls = 0;
        var registry = new CustomGuiScriptRegistry();
        registry.Register(new CustomGuiScriptWindowDefinition
        {
            DocumentId = "activity.exchange",
            DocumentRevision = 1,
            PackageSequence = 1,
            ProvideState = (_, _) => new[]
            {
                CustomGuiStateEntry.Text("duplicate", "a"),
                CustomGuiStateEntry.Text("duplicate", "b")
            },
            Actions = new[] { Rule("known", () => prepareCalls++) }
        });

        CustomGuiScriptPlanResult invalid = registry.PrepareOpen(
            new ScriptContext(), Player(), "activity.exchange", DateTimeOffset.UtcNow);
        Assert.False(invalid.Success);
        Assert.Contains("GUI11-HOOK-STATE", invalid.Diagnostic);
        Assert.Equal(0, prepareCalls);

        var authority = new CustomGuiActionAuthority();
        authority.RegisterBatch(new[] { Rule("known", () => prepareCalls++) });
        C.CustomGuiAction unknown = new()
        {
            WindowInstanceId = 1,
            DocumentId = "activity.exchange",
            DocumentRevision = 1,
            PackageSequence = 1,
            SessionNonce = Guid.NewGuid(),
            RequestSequence = 1,
            Action = CustomGuiActionKind.RequestAction,
            ActionId = "forged"
        };
        Assert.Contains("GUI09-AUTH-ACTION", authority.Handle(Player(), unknown, 1).Message);
        Assert.Equal(0, prepareCalls);
    }

    [Fact]
    public void RegistrationRejectsDuplicateOrUnboundedDeclarations()
    {
        var registry = new CustomGuiScriptRegistry();
        CustomGuiScriptWindowDefinition valid = new()
        {
            DocumentId = "activity.exchange",
            DocumentRevision = 1,
            PackageSequence = 1,
            ProvideState = (_, _) => Array.Empty<CustomGuiStateEntry>(),
            Actions = new[] { Rule("submit") }
        };
        registry.Register(valid);

        Assert.Throws<InvalidOperationException>(() => registry.Register(valid));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CustomGuiScriptRegistry().Register(new CustomGuiScriptWindowDefinition
        {
            DocumentId = "activity.long",
            DocumentRevision = 1,
            PackageSequence = 1,
            Lifetime = TimeSpan.FromMinutes(31),
            ProvideState = (_, _) => Array.Empty<CustomGuiStateEntry>(),
            Actions = new[] { Rule("submit") }
        }));
    }

    [Fact]
    public void ExistingHotReloadCompilerCanRegisterWindowThroughStableScriptApi()
    {
        string root = Path.Combine(Path.GetTempPath(), "lyo-gui11-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "GuiWindow.cs"), """
                using System;
                using Server.CustomGui;
                using Server.Scripting;
                using Shared.CustomGui;

                public sealed class GuiWindowModule : IScriptModule
                {
                    public void Register(ScriptRegistry registry)
                    {
                        registry.RegisterCustomGuiWindow(new CustomGuiScriptWindowDefinition
                        {
                            DocumentId = "activity.scripted",
                            DocumentRevision = 1,
                            PackageSequence = 1,
                            ProvideState = (_, _) => new[] { CustomGuiStateEntry.Text("title", "脚本窗口") },
                            Actions = new[]
                            {
                                new CustomGuiActionRule
                                {
                                    ActionId = "submit",
                                    Action = CustomGuiActionKind.RequestAction,
                                    Prepare = (_, _) => new CustomGuiDelegateTransaction(() => "完成", () => { })
                                }
                            }
                        });
                    }
                }
                """);

            var compiler = new ScriptCompiler();
            ScriptCompileResult compiled = compiler.CompileFromDirectory(
                root, "LomScripts_Gui11_" + Guid.NewGuid().ToString("N"), debugBuild: false);
            Assert.True(compiled.Success, string.Join(Environment.NewLine, compiled.Diagnostics));

            var loadContext = new ScriptLoadContext();
            try
            {
                using var assemblyStream = new MemoryStream(compiled.AssemblyBytes);
                using var symbolsStream = new MemoryStream(compiled.PdbBytes);
                var assembly = loadContext.LoadFromStream(assemblyStream, symbolsStream);
                var registry = new ScriptRegistry();
                ScriptManager.RegisterModules(assembly, registry);
                Assert.Equal(1, registry.CustomGui.Count);
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
    public void SameVersionHotReloadAtomicallyReplacesHooksAndDowngradeKeepsCurrentSnapshot()
    {
        int oldCalls = 0;
        int currentCalls = 0;
        var authority = new CustomGuiActionAuthority();
        CustomGuiActionRule oldRule = Rule("submit", () => oldCalls++);
        oldRule.DocumentRevision = 2;
        oldRule.PackageSequence = 5;
        authority.RegisterDocumentSnapshot(new[] { oldRule });

        CustomGuiActionRule currentRule = Rule("submit", () => currentCalls++);
        currentRule.DocumentRevision = 2;
        currentRule.PackageSequence = 5;
        authority.RegisterDocumentSnapshot(new[] { currentRule });

        CustomGuiActionRule downgrade = Rule("submit");
        downgrade.DocumentRevision = 1;
        downgrade.PackageSequence = 5;
        Assert.Throws<InvalidOperationException>(() => authority.RegisterDocumentSnapshot(new[] { downgrade }));

        C.CustomGuiAction action = new()
        {
            WindowInstanceId = 1,
            DocumentId = "activity.exchange",
            DocumentRevision = 2,
            PackageSequence = 5,
            SessionNonce = Guid.NewGuid(),
            RequestSequence = 1,
            Action = CustomGuiActionKind.RequestAction,
            ActionId = "submit"
        };
        Assert.Equal(CustomGuiActionResultKind.Accepted, authority.Handle(Player(), action, 1).Result);
        Assert.Equal(0, oldCalls);
        Assert.Equal(1, currentCalls);
    }

    [Fact]
    public void HotReloadInvalidatesOnlyScriptDocumentsAndReleasesTheirActionDelegates()
    {
        long now = 10_000;
        ulong windowId = 10;
        var sent = new List<Packet>();
        var sessions = new CustomGuiSessionController(
            sent.Add, () => true, () => now, () => Guid.NewGuid(), () => windowId++);
        sessions.Open("activity.scripted", 1, 1, now + 1_000, 1, new());
        sessions.Open("activity.native", 1, 1, now + 1_000, 1, new());
        sent.Clear();

        int nativeCalls = 0;
        var authority = new CustomGuiActionAuthority();
        CustomGuiActionRule scripted = Rule("script.submit");
        scripted.DocumentId = "activity.scripted";
        authority.RegisterDocumentSnapshot(new[] { scripted });
        CustomGuiActionRule native = Rule("native.submit", () => nativeCalls++);
        native.DocumentId = "activity.native";
        authority.RegisterDocumentSnapshot(new[] { native });

        var affected = new HashSet<string>(StringComparer.Ordinal) { "activity.scripted" };
        Assert.Equal(1, sessions.InvalidateDocuments(affected));
        Assert.Equal(1, authority.RemoveDocuments(affected));

        ServerPackets.CustomGuiClose closed = Assert.IsType<ServerPackets.CustomGuiClose>(Assert.Single(sent));
        Assert.Equal(CustomGuiCloseReason.VersionChanged, closed.Reason);
        Assert.Contains("GUI11-HOOK-RELOAD", closed.Message);
        Assert.Equal(1, sessions.ActiveCount);

        C.CustomGuiAction removed = ActionFor("activity.scripted", "script.submit");
        Assert.Contains("GUI09-AUTH-ACTION", authority.Handle(Player(), removed, 1).Message);
        C.CustomGuiAction retained = ActionFor("activity.native", "native.submit");
        Assert.Equal(CustomGuiActionResultKind.Accepted, authority.Handle(Player(), retained, 1).Result);
        Assert.Equal(1, nativeCalls);
    }

    private static CustomGuiActionRule Rule(string actionId, Action? prepared = null) => new()
    {
        DocumentId = "activity.exchange",
        DocumentRevision = 1,
        PackageSequence = 1,
        ActionId = actionId,
        Action = CustomGuiActionKind.RequestAction,
        Prepare = (_, _) =>
        {
            prepared?.Invoke();
            return new CustomGuiDelegateTransaction(() => string.Empty, () => { });
        }
    };

    private static PlayerObject Player() => new()
    {
        Info = new CharacterInfo { Name = "脚本测试玩家" },
        Account = new AccountInfo()
    };

    private static C.CustomGuiAction ActionFor(string documentId, string actionId) => new()
    {
        WindowInstanceId = 1,
        DocumentId = documentId,
        DocumentRevision = 1,
        PackageSequence = 1,
        SessionNonce = Guid.NewGuid(),
        RequestSequence = 1,
        Action = CustomGuiActionKind.RequestAction,
        ActionId = actionId
    };
}
