using Server.CustomGui;
using Server.MirDatabase;
using Server.MirObjects;
using Server.Scripting;
using Shared.CustomGui;
using C = ClientPackets;
using S = ServerPackets;
using Xunit;

namespace Base05.Tests;

public sealed class ActivityExchangeWindowTests
{
    [Fact]
    public void SharedActivityDocumentIsValidAndContainsOnlyWhitelistedSubmitAction()
    {
        CustomGuiRuntimeDocument document = CustomGuiActivityExchangeTemplate.Create();

        CustomGuiValidationReport report = CustomGuiValidationPolicy.Validate(document, CustomGuiResourceCatalog.Empty);

        Assert.True(report.IsValid, string.Join(Environment.NewLine, report.Diagnostics.Select(x => $"{x.Code}:{x.Message}")));
        CustomGuiButton submit = Assert.IsType<CustomGuiButton>(document.Elements.Single(x => x.Id == "exchange.submit"));
        Assert.Equal(CustomGuiActivityExchangeTemplate.SubmitActionId, submit.ActionId);
        Assert.Single(document.Elements.OfType<CustomGuiButton>());
    }

    [Fact]
    public void RealExchangeCommitsFactsPublishesNextStateAndRejectsReplayAndTampering()
    {
        PlayerObject player = Player(gold: 1_500, credit: 7);
        long now = 100_000;
        List<Packet> sent = new();
        CustomGuiSessionController? sessions = null;
        var registry = new CustomGuiScriptRegistry();
        ActivityExchangeWindow.Register(registry, (_, action, state) =>
            sessions!.UpdateState(action.WindowInstanceId, expectedStateRevision: 1, state.ToList()));
        CustomGuiScriptPlanResult plan = registry.PrepareOpen(
            new ScriptContext(), player, CustomGuiActivityExchangeTemplate.DocumentId,
            DateTimeOffset.FromUnixTimeMilliseconds(now));
        Assert.True(plan.Success, plan.Diagnostic);
        var authority = new CustomGuiActionAuthority();
        authority.RegisterDocumentSnapshot(plan.Plan.Actions);
        sessions = new CustomGuiSessionController(
            sent.Add, () => true, () => now,
            () => Guid.Parse("77777777-7777-7777-7777-777777777777"),
            () => 77,
            (action, revision) => authority.Handle(player, action, revision));
        S.CustomGuiOpen opened = sessions.Open(
            plan.Plan.DocumentId, plan.Plan.DocumentRevision, plan.Plan.PackageSequence,
            plan.Plan.ExpiresAtUnixMilliseconds, plan.Plan.StateRevision, plan.Plan.State.ToList());
        sent.Clear();

        C.CustomGuiAction legal = Action(opened, CustomGuiActivityExchangeTemplate.OfferId);
        CustomGuiSessionDecision accepted = sessions.Handle(legal);

        Assert.True(accepted.Accepted);
        Assert.Equal((uint)2, accepted.StateRevision);
        Assert.Equal((uint)500, player.Account.Gold);
        Assert.Equal((uint)17, player.Account.Credit);
        Assert.True(player.Info.Flags[ActivityExchangeWindow.ClaimFlagIndex]);
        S.CustomGuiStateDelta delta = Assert.IsType<S.CustomGuiStateDelta>(sent[0]);
        Assert.Contains(delta.State, x => x.BindingKey == "exchange.status" && x.TextValue.Contains("已完成"));
        Assert.Contains(delta.State, x => x.BindingKey == "exchange.submit.enabled" && !x.BooleanValue);
        S.CustomGuiActionResult result = Assert.IsType<S.CustomGuiActionResult>(sent[1]);
        Assert.Equal(CustomGuiActionResultKind.Accepted, result.Result);
        Assert.Equal((uint)2, result.StateRevision);

        sent.Clear();
        CustomGuiSessionDecision replay = sessions.Handle(legal);
        Assert.Contains("GUI08-SESSION-REPLAY", replay.Message);
        Assert.Equal((uint)500, player.Account.Gold);
        Assert.Equal((uint)17, player.Account.Credit);
        C.CustomGuiAction forged = Action("credit.999999");
        Assert.Contains("GUI09-AUTH-SELECTION", authority.Handle(Player(1_500, 7), forged, 1).Message);
    }

    [Fact]
    public void InsufficientBalanceAndPublishFailureLeaveAllPersistentFactsUnchanged()
    {
        PlayerObject poor = Player(gold: ActivityExchangeWindow.GoldCost - 1, credit: 3);
        var poorRegistry = new CustomGuiScriptRegistry();
        ActivityExchangeWindow.Register(poorRegistry, (_, _, _) => throw new Xunit.Sdk.XunitException("不应发布"));
        CustomGuiScriptOpenPlan poorPlan = poorRegistry.PrepareOpen(
            new ScriptContext(), poor, CustomGuiActivityExchangeTemplate.DocumentId, DateTimeOffset.UtcNow).Plan;
        var poorAuthority = new CustomGuiActionAuthority();
        poorAuthority.RegisterDocumentSnapshot(poorPlan.Actions);

        S.CustomGuiActionResult insufficient = poorAuthority.Handle(poor, Action(CustomGuiActivityExchangeTemplate.OfferId), 1);

        Assert.Contains("GUI09-AUTH-CURRENCY", insufficient.Message);
        Assert.Equal(ActivityExchangeWindow.GoldCost - 1, poor.Account.Gold);
        Assert.Equal((uint)3, poor.Account.Credit);
        Assert.False(poor.Info.Flags[ActivityExchangeWindow.ClaimFlagIndex]);

        PlayerObject retryable = Player(gold: 1_500, credit: 3);
        var failedRegistry = new CustomGuiScriptRegistry();
        ActivityExchangeWindow.Register(failedRegistry, (_, _, _) => throw new IOException("状态发送失败"));
        CustomGuiScriptOpenPlan failedPlan = failedRegistry.PrepareOpen(
            new ScriptContext(), retryable, CustomGuiActivityExchangeTemplate.DocumentId, DateTimeOffset.UtcNow).Plan;
        var failedAuthority = new CustomGuiActionAuthority();
        failedAuthority.RegisterDocumentSnapshot(failedPlan.Actions);

        S.CustomGuiActionResult failed = failedAuthority.Handle(retryable, Action(CustomGuiActivityExchangeTemplate.OfferId), 1);

        Assert.Contains("GUI09-AUTH-TRANSACTION", failed.Message);
        Assert.Equal((uint)1_500, retryable.Account.Gold);
        Assert.Equal((uint)3, retryable.Account.Credit);
        Assert.False(retryable.Info.Flags[ActivityExchangeWindow.ClaimFlagIndex]);
    }

    [Fact]
    public void ClaimFlagSurvivesExistingCharacterPersistenceRoundTrip()
    {
        var source = new CharacterInfo
        {
            Index = 42,
            Name = "持久化兑换玩家",
            CreationIP = "127.0.0.1",
            Heroes = new HeroInfo[1]
        };
        source.Flags[ActivityExchangeWindow.ClaimFlagIndex] = true;
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
            source.Save(writer);
        stream.Position = 0;

        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        var restored = new CharacterInfo(reader, Server.MirEnvir.Envir.Version, Server.MirEnvir.Envir.CustomVersion);

        Assert.True(restored.Flags[ActivityExchangeWindow.ClaimFlagIndex]);
    }

    [Fact]
    public void BuiltInRegistryReconnectExpiryAndVersionSwitchFailClosed()
    {
        using var manager = new ScriptManager();
        Assert.Contains(CustomGuiActivityExchangeTemplate.DocumentId, manager.CurrentRegistry.CustomGui.DocumentIds);

        PlayerObject claimed = Player(500, 17);
        claimed.Info.Flags[ActivityExchangeWindow.ClaimFlagIndex] = true;
        IReadOnlyList<CustomGuiStateEntry> reconnected = ActivityExchangeWindow.BuildState(claimed);
        Assert.Contains(reconnected, state => state.BindingKey == "exchange.submit.enabled" && !state.BooleanValue);

        long now = 200_000;
        var sent = new List<Packet>();
        var sessions = new CustomGuiSessionController(
            sent.Add, () => true, () => now, () => Guid.NewGuid(), () => 81);
        S.CustomGuiOpen opened = sessions.Open(
            CustomGuiActivityExchangeTemplate.DocumentId, 1, 1, now + 50, 1, reconnected.ToList());
        sent.Clear();
        Assert.Equal(1, sessions.InvalidatePackageSequence(2));
        Assert.Equal(CustomGuiCloseReason.VersionChanged, Assert.IsType<S.CustomGuiClose>(Assert.Single(sent)).Reason);

        sent.Clear();
        opened = sessions.Open(
            CustomGuiActivityExchangeTemplate.DocumentId, 1, 2, now + 50, 1, reconnected.ToList());
        now += 50;
        CustomGuiSessionDecision expired = sessions.Handle(Action(opened, CustomGuiActivityExchangeTemplate.OfferId));
        Assert.Equal(CustomGuiActionResultKind.Expired, expired.Result);
        Assert.Contains("GUI08-SESSION-EXPIRED", expired.Message);
    }

    private static PlayerObject Player(uint gold, uint credit) => new()
    {
        Info = new CharacterInfo { Name = "兑换测试玩家" },
        Account = new AccountInfo { Gold = gold, Credit = credit }
    };

    private static C.CustomGuiAction Action(string selection) => new()
    {
        WindowInstanceId = 77,
        DocumentId = CustomGuiActivityExchangeTemplate.DocumentId,
        DocumentRevision = 1,
        PackageSequence = 1,
        SessionNonce = Guid.Parse("77777777-7777-7777-7777-777777777777"),
        RequestSequence = 1,
        Action = CustomGuiActionKind.SubmitSelection,
        ActionId = CustomGuiActivityExchangeTemplate.SubmitActionId,
        SelectionIds = [selection]
    };

    private static C.CustomGuiAction Action(S.CustomGuiOpen opened, string selection)
    {
        C.CustomGuiAction action = Action(selection);
        action.WindowInstanceId = opened.WindowInstanceId;
        action.DocumentId = opened.DocumentId;
        action.DocumentRevision = opened.DocumentRevision;
        action.PackageSequence = opened.PackageSequence;
        action.SessionNonce = opened.SessionNonce;
        return action;
    }
}
