using Server.CustomGui;
using Shared.CustomGui;
using C = ClientPackets;
using S = ServerPackets;
using Xunit;

namespace Base05.Tests;

public sealed class CustomGuiSessionGateTests
{
    [Fact]
    public void OpenOwnsIdentityAndReplacesSameDocument()
    {
        long now = 10_000;
        Guid firstNonce = Guid.Parse("11111111-1111-1111-1111-111111111111");
        Guid secondNonce = Guid.Parse("22222222-2222-2222-2222-222222222222");
        Queue<Guid> nonces = new(new[] { firstNonce, secondNonce });
        Queue<ulong> windows = new(new ulong[] { 41, 42 });
        List<Packet> sent = new();
        var controller = CreateController(sent, () => now, () => nonces.Dequeue(), () => windows.Dequeue());

        List<CustomGuiStateEntry> initialState = new() { CustomGuiStateEntry.Text("title", "兑换活动") };
        S.CustomGuiOpen first = controller.Open("activity.exchange", 3, 71, now + 1_000, 1, initialState);
        S.CustomGuiOpen second = controller.Open("activity.exchange", 4, 72, now + 2_000, 2, new());
        initialState[0].TextValue = "已篡改";
        first.State[0].TextValue = "返回值也已篡改";

        Assert.Equal((ulong)41, first.WindowInstanceId);
        Assert.Equal(firstNonce, first.SessionNonce);
        Assert.Equal((ulong)42, second.WindowInstanceId);
        Assert.Equal(secondNonce, second.SessionNonce);
        Assert.Equal(1, controller.ActiveCount);
        Assert.Collection(sent,
            packet =>
            {
                S.CustomGuiOpen sentOpen = Assert.IsType<S.CustomGuiOpen>(packet);
                Assert.NotSame(first, sentOpen);
                Assert.Equal("兑换活动", sentOpen.State[0].TextValue);
            },
            packet =>
            {
                S.CustomGuiClose close = Assert.IsType<S.CustomGuiClose>(packet);
                Assert.Equal((ulong)41, close.WindowInstanceId);
                Assert.Equal(CustomGuiCloseReason.Replaced, close.Reason);
            },
            packet =>
            {
                S.CustomGuiOpen sentOpen = Assert.IsType<S.CustomGuiOpen>(packet);
                Assert.NotSame(second, sentOpen);
                Assert.Equal(second.WindowInstanceId, sentOpen.WindowInstanceId);
            });
    }

    [Fact]
    public void ActionRequiresStrictlyIncreasingSequenceAndConsumesAcceptedSequence()
    {
        long now = 20_000;
        List<Packet> sent = new();
        var controller = CreateController(sent, () => now);
        S.CustomGuiOpen opened = controller.Open("activity.exchange", 1, 91, now + 1_000, 5, new());
        sent.Clear();

        CustomGuiSessionDecision accepted = controller.Handle(ActionFor(opened, 1));
        CustomGuiSessionDecision replay = controller.Handle(ActionFor(opened, 1));
        CustomGuiSessionDecision outOfOrder = controller.Handle(ActionFor(opened, 3));
        CustomGuiSessionDecision next = controller.Handle(ActionFor(opened, 2));

        Assert.True(accepted.Accepted);
        Assert.Equal(CustomGuiActionResultKind.Stale, replay.Result);
        Assert.Contains("GUI08-SESSION-REPLAY", replay.Message);
        Assert.Equal(CustomGuiActionResultKind.Stale, outOfOrder.Result);
        Assert.Contains("GUI08-SESSION-ORDER", outOfOrder.Message);
        Assert.True(next.Accepted);
        S.CustomGuiActionResult[] results = sent.Cast<S.CustomGuiActionResult>().ToArray();
        Assert.Equal(new uint[] { 1, 1, 3, 2 }, results.Select(x => x.RequestSequence));
        Assert.Equal(CustomGuiActionResultKind.Rejected, results[0].Result);
        Assert.Contains("GUI08-ACTION-UNHANDLED", results[0].Message);
        Assert.Equal(CustomGuiActionResultKind.Rejected, results[3].Result);
    }

    [Fact]
    public void ExpiredAndForeignIdentityFailClosedWithoutRevivingSession()
    {
        long now = 30_000;
        List<Packet> sent = new();
        var controller = CreateController(sent, () => now);
        S.CustomGuiOpen opened = controller.Open("activity.exchange", 7, 101, now + 100, 2, new());
        sent.Clear();

        C.CustomGuiAction foreign = ActionFor(opened, 1);
        foreign.SessionNonce = Guid.NewGuid();
        CustomGuiSessionDecision foreignDecision = controller.Handle(foreign);
        Assert.Equal(CustomGuiActionResultKind.Invalid, foreignDecision.Result);
        Assert.Equal(1, controller.ActiveCount);

        C.CustomGuiAction staleVersion = ActionFor(opened, 1);
        staleVersion.PackageSequence++;
        CustomGuiSessionDecision staleDecision = controller.Handle(staleVersion);
        Assert.Equal(CustomGuiActionResultKind.Stale, staleDecision.Result);
        Assert.Contains("GUI08-SESSION-VERSION", staleDecision.Message);
        Assert.Equal(1, controller.ActiveCount);

        now += 101;
        CustomGuiSessionDecision expired = controller.Handle(ActionFor(opened, 1));
        Assert.Equal(CustomGuiActionResultKind.Expired, expired.Result);
        Assert.Contains("GUI08-SESSION-EXPIRED", expired.Message);
        Assert.Equal(0, controller.ActiveCount);
        Assert.Equal(CustomGuiActionResultKind.Stale, controller.Handle(ActionFor(opened, 1)).Result);
    }

    [Fact]
    public void PackageVersionInvalidationClosesOnlyStaleWindows()
    {
        long now = 40_000;
        Queue<ulong> windows = new(new ulong[] { 11, 12 });
        List<Packet> sent = new();
        var controller = CreateController(sent, () => now, windowFactory: () => windows.Dequeue());
        controller.Open("activity.one", 1, 201, now + 2_000, 1, new());
        controller.Open("activity.two", 1, 202, now + 2_000, 1, new());
        sent.Clear();

        int closed = controller.InvalidatePackageSequence(202);

        Assert.Equal(1, closed);
        Assert.Equal(1, controller.ActiveCount);
        S.CustomGuiClose close = Assert.IsType<S.CustomGuiClose>(Assert.Single(sent));
        Assert.Equal((ulong)11, close.WindowInstanceId);
        Assert.Equal(CustomGuiCloseReason.VersionChanged, close.Reason);
        Assert.Throws<ArgumentOutOfRangeException>(() => controller.InvalidatePackageSequence(0));
    }

    [Fact]
    public void CloseActionRemovesSessionAndDisconnectedPlayerCannotOpenOrAct()
    {
        long now = 50_000;
        bool inGame = true;
        List<Packet> sent = new();
        var controller = CreateController(sent, () => now, inGame: () => inGame);
        S.CustomGuiOpen opened = controller.Open("activity.exchange", 1, 301, now + 1_000, 1, new());
        sent.Clear();

        C.CustomGuiAction closeAction = ActionFor(opened, 1);
        closeAction.Action = CustomGuiActionKind.CloseWindow;
        CustomGuiSessionDecision decision = controller.Handle(closeAction);

        Assert.True(decision.Accepted);
        Assert.Equal(0, controller.ActiveCount);
        Assert.IsType<S.CustomGuiActionResult>(sent[0]);
        Assert.Equal(CustomGuiCloseReason.Requested, Assert.IsType<S.CustomGuiClose>(sent[1]).Reason);

        inGame = false;
        Assert.Throws<InvalidOperationException>(() =>
            controller.Open("activity.exchange", 1, 301, now + 1_000, 1, new()));
        CustomGuiSessionDecision disconnected = controller.Handle(ActionFor(opened, 2));
        Assert.Equal(CustomGuiActionResultKind.Invalid, disconnected.Result);
        Assert.Contains("GUI08-SESSION-PLAYER", disconnected.Message);
    }

    [Fact]
    public void ActiveSessionLimitFailsClosed()
    {
        long now = 60_000;
        ulong nextWindow = 1;
        List<Packet> sent = new();
        var controller = CreateController(sent, () => now, windowFactory: () => nextWindow++);

        for (int i = 0; i < CustomGuiSessionController.MaximumActiveSessions; i++)
            controller.Open("activity." + i, 1, 401, now + 1_000, 1, new());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            controller.Open("activity.overflow", 1, 401, now + 1_000, 1, new()));
        Assert.Contains("GUI08-SESSION-LIMIT", error.Message);
        Assert.Equal(CustomGuiSessionController.MaximumActiveSessions, controller.ActiveCount);

        InvalidOperationException lifetime = Assert.Throws<InvalidOperationException>(() =>
            CreateController(new(), () => now).Open(
                "activity.too-long", 1, 401,
                now + CustomGuiSessionController.MaximumSessionLifetimeMilliseconds + 1,
                1, new()));
        Assert.Contains("GUI08-SESSION-LIFETIME", lifetime.Message);
    }

    [Fact]
    public void ExpirySweepClosesAtDeadlineAndFreesCapacity()
    {
        long now = 70_000;
        ulong nextWindow = 1;
        List<Packet> sent = new();
        var controller = CreateController(sent, () => now, windowFactory: () => nextWindow++);
        for (int i = 0; i < CustomGuiSessionController.MaximumActiveSessions; i++)
            controller.Open("activity." + i, 1, 501, now + 100, 1, new());
        sent.Clear();

        now += 100;
        int expired = controller.ExpireDueSessions();

        Assert.Equal(CustomGuiSessionController.MaximumActiveSessions, expired);
        Assert.Equal(0, controller.ActiveCount);
        Assert.All(sent.Cast<S.CustomGuiClose>(), close => Assert.Equal(CustomGuiCloseReason.Expired, close.Reason));
        controller.Open("activity.reopened", 1, 501, now + 100, 1, new());
        Assert.Equal(1, controller.ActiveCount);
    }

    [Fact]
    public void ActionHandlerFailureIsContainedAndSequenceCannotBeRetried()
    {
        long now = 80_000;
        Exception? observedError = null;
        List<Packet> sent = new();
        var controller = new CustomGuiSessionController(
            sent.Add,
            () => true,
            () => now,
            () => Guid.NewGuid(),
            () => 700,
            (_, _) => throw new InvalidOperationException("业务故障"),
            actionErrorSink: error => observedError = error);
        S.CustomGuiOpen opened = controller.Open("activity.exchange", 1, 601, now + 1_000, 1, new());
        sent.Clear();

        CustomGuiSessionDecision decision = controller.Handle(ActionFor(opened, 1));
        S.CustomGuiActionResult failure = Assert.IsType<S.CustomGuiActionResult>(Assert.Single(sent));
        Assert.True(decision.Accepted);
        Assert.Equal(CustomGuiActionResultKind.Rejected, failure.Result);
        Assert.Contains("GUI08-ACTION-ERROR", failure.Message);
        Assert.IsType<InvalidOperationException>(observedError);

        sent.Clear();
        Assert.Equal(CustomGuiActionResultKind.Stale, controller.Handle(ActionFor(opened, 1)).Result);
        Assert.Contains("GUI08-SESSION-REPLAY", Assert.IsType<S.CustomGuiActionResult>(Assert.Single(sent)).Message);
    }

    [Fact]
    public void ExistingActivitiesKillSwitchClosesSessionsAndBlocksNewOpen()
    {
        long now = 90_000;
        bool enabled = true;
        List<Packet> sent = new();
        var controller = new CustomGuiSessionController(
            sent.Add,
            () => true,
            () => now,
            () => Guid.NewGuid(),
            () => 800,
            featureEnabled: () => enabled);
        controller.Open("activity.exchange", 1, 701, now + 1_000, 1, new());
        sent.Clear();

        enabled = false;
        int closed = controller.EnforceAvailability();

        Assert.Equal(1, closed);
        Assert.Equal(0, controller.ActiveCount);
        Assert.Equal(CustomGuiCloseReason.Invalidated, Assert.IsType<S.CustomGuiClose>(Assert.Single(sent)).Reason);
        InvalidOperationException blocked = Assert.Throws<InvalidOperationException>(() =>
            controller.Open("activity.exchange", 1, 701, now + 1_000, 1, new()));
        Assert.Contains("GUI08-SESSION-DISABLED", blocked.Message);
    }

    [Fact]
    public void StateDeltaRequiresExactRevisionAndDoesNotAdvanceWhenSendFails()
    {
        long now = 100_000;
        bool failDelta = false;
        List<Packet> sent = new();
        var controller = new CustomGuiSessionController(
            packet =>
            {
                if (failDelta && packet is S.CustomGuiStateDelta) throw new IOException("发送失败");
                sent.Add(packet);
            },
            () => true,
            () => now,
            () => Guid.Parse("33333333-3333-3333-3333-333333333333"),
            () => 901);
        S.CustomGuiOpen opened = controller.Open(
            "activity.exchange", 1, 1, now + 1_000, 1,
            new() { CustomGuiStateEntry.Text("exchange.status", "可兑换") });
        sent.Clear();

        S.CustomGuiStateDelta updated = controller.UpdateState(
            opened.WindowInstanceId, expectedStateRevision: 1,
            new() { CustomGuiStateEntry.Text("exchange.status", "兑换成功") });

        Assert.Equal((uint)2, updated.StateRevision);
        Assert.Equal(opened.SessionNonce, updated.SessionNonce);
        Assert.Equal("兑换成功", Assert.IsType<S.CustomGuiStateDelta>(Assert.Single(sent)).State[0].TextValue);
        Assert.Throws<InvalidOperationException>(() => controller.UpdateState(
            opened.WindowInstanceId, expectedStateRevision: 1, new()));

        failDelta = true;
        Assert.Throws<IOException>(() => controller.UpdateState(
            opened.WindowInstanceId, expectedStateRevision: 2, new()));
        failDelta = false;
        S.CustomGuiStateDelta retry = controller.UpdateState(
            opened.WindowInstanceId, expectedStateRevision: 2, new());
        Assert.Equal((uint)3, retry.StateRevision);
    }

    private static CustomGuiSessionController CreateController(
        List<Packet> sent,
        Func<long> now,
        Func<Guid>? nonceFactory = null,
        Func<ulong>? windowFactory = null,
        Func<bool>? inGame = null)
    {
        return new CustomGuiSessionController(
            sent.Add,
            inGame ?? (() => true),
            now,
            nonceFactory ?? (() => Guid.NewGuid()),
            windowFactory ?? (() => 99));
    }

    private static C.CustomGuiAction ActionFor(S.CustomGuiOpen opened, uint requestSequence)
    {
        return new C.CustomGuiAction
        {
            WindowInstanceId = opened.WindowInstanceId,
            DocumentId = opened.DocumentId,
            DocumentRevision = opened.DocumentRevision,
            PackageSequence = opened.PackageSequence,
            SessionNonce = opened.SessionNonce,
            RequestSequence = requestSequence,
            Action = CustomGuiActionKind.RequestAction,
            ActionId = "exchange.submit"
        };
    }
}
