using Shared.CustomGui;
using Xunit;

namespace Base05.Tests;

public sealed class CustomGuiStateProjectionTests
{
    [Fact]
    public void OpenAndExactDeltaProjectAllBoundedStateKinds()
    {
        CustomGuiRuntimeDocument document = CreateDocument();
        var target = new RecordingTarget();
        var session = new CustomGuiClientStateSession(document, packageSequence: 9, target);
        Guid nonce = Guid.NewGuid();

        session.Open(new CustomGuiOpenState(
            41, document.DocumentId, (uint)document.Revision, 9, nonce,
            DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeMilliseconds(), 3,
            [
                CustomGuiStateEntry.Text("title", "活动已开启"),
                CustomGuiStateEntry.Boolean("panel", true),
                CustomGuiStateEntry.Integer("count", 2),
                CustomGuiStateEntry.Progress("progress", 3, 7),
                CustomGuiStateEntry.List("rewards", [new("one", "第一天", "武器", "sword")]),
                CustomGuiStateEntry.ForItemSlots("slot", [new("one", 88, "sword", "新手剑", 1, true)]),
                CustomGuiStateEntry.ButtonVisible("claim.visible", true),
                CustomGuiStateEntry.ButtonEnabled("claim.enabled", false),
            ]));

        session.ApplyDelta(new CustomGuiDeltaState(41, document.DocumentId, (uint)document.Revision, 9, nonce, 4,
            [CustomGuiStateEntry.Integer("count", 1)]));

        Assert.Equal((uint)4, session.StateRevision);
        Assert.Equal(8, target.State.Count);
        Assert.Equal(1, target.State["count"].IntegerValue);
        Assert.Equal("活动已开启", target.State["title"].TextValue);
    }

    [Theory]
    [InlineData("identity")]
    [InlineData("revision")]
    [InlineData("binding")]
    public void InvalidDeltaFailsClosedAndPreservesLastProjection(string failure)
    {
        CustomGuiRuntimeDocument document = CreateDocument();
        var target = new RecordingTarget();
        var session = new CustomGuiClientStateSession(document, 9, target);
        Guid nonce = Guid.NewGuid();
        session.Open(Open(document, nonce));
        IReadOnlyDictionary<string, CustomGuiStateEntry> before = target.State;
        var delta = new CustomGuiDeltaState(failure == "identity" ? 99UL : 41UL, document.DocumentId,
            (uint)document.Revision, 9, nonce, failure == "revision" ? 5U : 4U,
            [CustomGuiStateEntry.Text(failure == "binding" ? "unknown" : "title", "篡改")]);

        CustomGuiStateProjectionException error = Assert.Throws<CustomGuiStateProjectionException>(() => session.ApplyDelta(delta));

        Assert.StartsWith("GUI10-STATE-", error.Code);
        Assert.Equal((uint)3, session.StateRevision);
        Assert.Same(before, target.State);
        Assert.Equal("初始", target.State["title"].TextValue);
    }

    [Fact]
    public void TargetFailureDoesNotAdvanceStateAndCloseRequiresMatchingWindow()
    {
        CustomGuiRuntimeDocument document = CreateDocument();
        var target = new RecordingTarget();
        var session = new CustomGuiClientStateSession(document, 9, target);
        Guid nonce = Guid.NewGuid();
        session.Open(Open(document, nonce));
        session.AcceptActionResult(41, 1, 3, CustomGuiActionResultKind.Accepted, "已接受");
        Assert.Equal(CustomGuiActionResultKind.Accepted, session.LastResult);
        Assert.Throws<CustomGuiStateProjectionException>(() => session.AcceptActionResult(41, 1, 3, CustomGuiActionResultKind.Accepted, "重放"));
        target.FailNext = true;

        Assert.Throws<InvalidOperationException>(() => session.ApplyDelta(new CustomGuiDeltaState(
            41, document.DocumentId, 2, 9, nonce, 4, [CustomGuiStateEntry.Text("title", "失败")])));
        Assert.Equal((uint)3, session.StateRevision);
        Assert.Equal("初始", session.State["title"].TextValue);
        Assert.False(session.Close(99));
        Assert.True(session.Close(41));
        Assert.False(session.IsOpen);
        Assert.Empty(target.State);
    }

    [Fact]
    public void ClientActionOwnsSessionIdentityAndFailedSendDoesNotConsumeSequence()
    {
        CustomGuiRuntimeDocument document = CreateDocument();
        var session = new CustomGuiClientStateSession(document, 9, new RecordingTarget());
        Guid nonce = Guid.NewGuid();
        session.Open(Open(document, nonce));

        Assert.Throws<IOException>(() => session.SendAction(
            _ => throw new IOException("网络队列失败"), CustomGuiActionKind.SubmitSelection, "claim", selectionIds: ["one"]));
        CustomGuiClientAction first = session.SendAction(
            _ => { }, CustomGuiActionKind.SubmitSelection, "claim", selectionIds: ["one"]);
        CustomGuiClientAction second = session.SendAction(
            _ => { }, CustomGuiActionKind.CloseWindow, "close");

        Assert.Equal((uint)1, first.RequestSequence);
        Assert.Equal((uint)2, second.RequestSequence);
        Assert.Equal((ulong)41, first.WindowInstanceId);
        Assert.Equal(document.DocumentId, first.DocumentId);
        Assert.Equal(nonce, first.SessionNonce);
        Assert.Equal(new[] { "one" }, first.SelectionIds);
    }

    private static CustomGuiOpenState Open(CustomGuiRuntimeDocument document, Guid nonce) => new(
        41, document.DocumentId, 2, 9, nonce, DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeMilliseconds(), 3,
        [CustomGuiStateEntry.Text("title", "初始")]);

    private static CustomGuiRuntimeDocument CreateDocument() => new()
    {
        DocumentId = "event", Revision = 2,
        Elements =
        [
            new CustomGuiWindow { Id = "window", Layout = new(0, 0, 400, 300) },
            new CustomGuiPanel { Id = "panel", ParentId = "window", Layout = new(0, 0, 400, 300) },
            new CustomGuiText { Id = "title", ParentId = "panel", Layout = new(0, 0, 100, 20) },
            new CustomGuiText { Id = "count", ParentId = "panel", Layout = new(0, 20, 100, 20) },
            new CustomGuiProgressBar { Id = "progress", ParentId = "panel", Layout = new(0, 40, 100, 20), BindingKey = "progress" },
            new CustomGuiList { Id = "rewards", ParentId = "panel", Layout = new(0, 60, 100, 80) },
            new CustomGuiItemSlot { Id = "slot", ParentId = "panel", Layout = new(100, 60, 80, 80), BindingKey = "slot" },
            new CustomGuiButton { Id = "claim", ParentId = "panel", Layout = new(0, 150, 100, 30) },
        ],
    };

    private sealed class RecordingTarget : ICustomGuiStateProjectionTarget
    {
        public IReadOnlyDictionary<string, CustomGuiStateEntry> State { get; private set; } = new Dictionary<string, CustomGuiStateEntry>();
        public bool FailNext { get; set; }
        public void Apply(IReadOnlyDictionary<string, CustomGuiStateEntry> state)
        {
            if (FailNext) { FailNext = false; throw new InvalidOperationException("目标失败"); }
            State = state;
        }
    }
}
