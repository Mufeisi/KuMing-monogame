extern alias MonoClient;

using M = MonoClient::MonoShare.CustomGui;
using S = MonoClient::Shared.CustomGui;
using Xunit;

namespace Launcher.PlayerShellIntegration;

public sealed class MobileCustomGuiAdapterTests
{
    [Fact]
    public void DynamicStateProjectsThroughMobileHostWithSameRevisionRules()
    {
        S.CustomGuiRuntimeDocument document = CreateDocument();
        var factory = new RecordingFactory();
        using M.MobileCustomGuiHost host = M.MobileCustomGuiAdapter.Create(document, 720, 1280, factory);
        var session = new S.CustomGuiClientStateSession(document, 7, host);
        session.Open(new S.CustomGuiOpenState(1, document.DocumentId, (uint)document.Revision, 7, Guid.NewGuid(),
            DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeMilliseconds(), 1,
            [
                S.CustomGuiStateEntry.Text("title", "服务端活动"),
                S.CustomGuiStateEntry.Progress("progress", 6, 7),
                S.CustomGuiStateEntry.ButtonVisible("claim.visible", false),
                S.CustomGuiStateEntry.ButtonEnabled("claim.enabled", false),
            ]));

        Assert.Equal("服务端活动", factory.ById["title"].State!.TextValue);
        Assert.Equal(6, factory.ById["progress"].State!.CurrentValue);
        Assert.False(factory.ById["claim"].States.Single(value => value.Kind == S.CustomGuiStateKind.ButtonVisible).BooleanValue);
        Assert.False(factory.ById["claim"].States.Single(value => value.Kind == S.CustomGuiStateKind.ButtonEnabled).BooleanValue);
    }

    [Fact]
    public void SameActivityExchangeBytesSelectSubmitRefreshAndCloseThroughMobileHost()
    {
        byte[] sharedBytes = global::Shared.CustomGui.CustomGuiDocumentCodec.Serialize(
            global::Shared.CustomGui.CustomGuiActivityExchangeTemplate.Create());
        S.CustomGuiRuntimeDocument document = S.CustomGuiDocumentCodec.Deserialize(sharedBytes);
        Assert.Equal(sharedBytes, S.CustomGuiDocumentCodec.Serialize(document));
        var factory = new RecordingFactory();
        using M.MobileCustomGuiHost host = M.MobileCustomGuiAdapter.Create(document, 720, 1280, factory);
        var session = new S.CustomGuiClientStateSession(document, 1, host);
        Guid nonce = Guid.NewGuid();
        session.Open(new S.CustomGuiOpenState(
            72, document.DocumentId, 1, 1, nonce,
            DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeMilliseconds(), 1,
            [
                S.CustomGuiStateEntry.Text("exchange.status", "活动可用"),
                S.CustomGuiStateEntry.List("exchange.options", [new("credit.10", "兑换", "限一次", string.Empty)]),
                S.CustomGuiStateEntry.ButtonEnabled("exchange.submit.enabled", true),
            ]));
        var sent = new List<S.CustomGuiClientAction>();
        host.BindActions(session, sent.Add);

        host.Select("exchange.options", "credit.10");
        factory.ById["exchange.submit"].Activate();
        S.CustomGuiClientAction action = Assert.Single(sent);

        Assert.Equal(S.CustomGuiActionKind.SubmitSelection, action.Action);
        Assert.Equal(new[] { "credit.10" }, action.SelectionIds);
        session.ApplyDelta(new S.CustomGuiDeltaState(
            72, document.DocumentId, 1, 1, nonce, 2,
            [
                S.CustomGuiStateEntry.Text("exchange.status", "兑换已完成"),
                S.CustomGuiStateEntry.ButtonEnabled("exchange.submit.enabled", false),
            ]));
        session.AcceptActionResult(72, action.RequestSequence, 2, S.CustomGuiActionResultKind.Accepted, "兑换成功");
        Assert.Equal("兑换已完成", factory.ById["exchange.status"].State!.TextValue);
        Assert.True(session.Close(72));
        Assert.False(session.IsOpen);
    }

    [Fact]
    public void AdapterProjectsEveryV1ElementThroughFactoryAtFitScale()
    {
        S.CustomGuiRuntimeDocument document = CreateDocument();
        var factory = new RecordingFactory();

        using M.MobileCustomGuiHost host = M.MobileCustomGuiAdapter.Create(document, 720, 1280, factory);

        Assert.Equal(0.5625f, host.Scale);
        Assert.Equal(437.5f, host.ViewportOffsetY);
        Assert.Equal(10, factory.Nodes.Count);
        Assert.Equal(M.MobileCustomGuiNodeKind.Root, factory.Nodes[0].Spec.Kind);
        Assert.Equal(
            Enum.GetValues<M.MobileCustomGuiNodeKind>().Where(kind => kind != M.MobileCustomGuiNodeKind.Root).Order(),
            factory.Nodes.Skip(1).Select(node => node.Spec.Kind).Order());
        Assert.Equal(new M.MobileCustomGuiBounds(135, 488.125f, 450, 303.75f), factory.ById["event"].Spec.Bounds);
        Assert.Equal(new M.MobileCustomGuiBounds(225, 241.875f, 123.75f, 27), factory.ById["claim"].Spec.Bounds);
        Assert.Same(factory.ById["event"], factory.ById["claim"].Parent);
        Assert.Equal("event.claim", Assert.IsType<S.CustomGuiButton>(factory.ById["claim"].Spec.Element).ActionId);
        Assert.Equal(2, Assert.IsType<S.CustomGuiList>(factory.ById["rewards"].Spec.Element).Items.Count);
    }

    [Fact]
    public void AdapterUsesSharedLayoutWithoutMutatingDocumentAndDisposesTree()
    {
        S.CustomGuiRuntimeDocument document = CreateDocument();
        byte[] before = S.CustomGuiDocumentCodec.Serialize(document);
        var factory = new RecordingFactory();

        M.MobileCustomGuiHost host = M.MobileCustomGuiAdapter.Create(document, 1024, 768, factory);

        Assert.Equal(0.8f, host.Scale);
        Assert.Equal(96f, host.ViewportOffsetY);
        Assert.Equal(before, S.CustomGuiDocumentCodec.Serialize(document));
        host.Dispose();
        host.Dispose();
        Assert.True(factory.Nodes[0].Disposed);
        Assert.All(factory.Nodes.Skip(1), node => Assert.True(node.Disposed));
    }

    [Fact]
    public void AdapterFailsClosedBeforeFactoryMutationWhenParentGraphCycles()
    {
        S.CustomGuiRuntimeDocument document = CreateDocument();
        document.Elements.Single(element => element.Id == "event").ParentId = "panel";
        var factory = new RecordingFactory();

        S.CustomGuiLayoutException error = Assert.Throws<S.CustomGuiLayoutException>(
            () => M.MobileCustomGuiAdapter.Create(document, 720, 1280, factory));

        Assert.Equal("GUI03-LAYOUT-001", error.Code);
        Assert.Empty(factory.Nodes);
    }

    [Fact]
    public void AdapterDisposesMaterializedTreeWhenFactoryFails()
    {
        S.CustomGuiRuntimeDocument document = CreateDocument();
        var factory = new RecordingFactory(failOnId: "banner");

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => M.MobileCustomGuiAdapter.Create(document, 720, 1280, factory));

        Assert.Equal("测试工厂失败", error.Message);
        Assert.True(factory.Nodes[0].Disposed);
        Assert.All(factory.Nodes.Skip(1), node => Assert.True(node.Disposed));
    }

    [Fact]
    public void AdapterDisposesUnattachedNodeWhenParentRejectsIt()
    {
        S.CustomGuiRuntimeDocument document = CreateDocument();
        var factory = new RecordingFactory(failOnAddChildId: "banner");

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => M.MobileCustomGuiAdapter.Create(document, 720, 1280, factory));

        Assert.Equal("测试父级拒绝子节点", error.Message);
        Assert.True(factory.Nodes.Single(node => node.Spec.Id == "banner").Disposed);
        Assert.All(factory.Nodes, node => Assert.True(node.Disposed));
    }

    private static S.CustomGuiRuntimeDocument CreateDocument() => new()
    {
        DocumentId = "gui04-mobile",
        Revision = 4,
        Viewport = new(1280, 720, S.CustomGuiScaleMode.Fit, S.CustomGuiSafeAreaMode.Required),
        Elements =
        [
            new S.CustomGuiWindow { Id = "event", Layout = new(240, 90, 800, 540), Title = "七日庆典" },
            new S.CustomGuiPanel { Id = "panel", ParentId = "event", Layout = new(40, 50, 720, 420), ClipChildren = true, BackgroundColor = "#20252E" },
            new S.CustomGuiImage { Id = "banner", ParentId = "panel", Layout = new(20, 20, 680, 80), AssetId = "events/banner" },
            new S.CustomGuiText { Id = "title", ParentId = "panel", Layout = new(20, 112, 400, 40), Content = "登录奖励", FontSize = 20 },
            new S.CustomGuiTextInput { Id = "code", ParentId = "panel", Layout = new(20, 165, 300, 48), Placeholder = "输入兑换码", MaxLength = 16 },
            new S.CustomGuiList { Id = "rewards", ParentId = "panel", Layout = new(20, 225, 420, 150), Items = [new("1", "第一天", "武器", "sword"), new("2", "第二天", "金币", "gold")] },
            new S.CustomGuiProgressBar { Id = "progress", ParentId = "panel", Layout = new(460, 225, 220, 42), Minimum = 0, Maximum = 7, Value = 3, Text = "3/7" },
            new S.CustomGuiItemSlot { Id = "slot", ParentId = "panel", Layout = new(460, 285, 110, 90), AssetId = "sword", DisplayName = "新手剑", Quantity = 1 },
            new S.CustomGuiButton { Id = "claim", ParentId = "event", Layout = new(400, 430, 220, 48), Text = "领取", ActionId = "event.claim" },
        ],
    };

    private sealed class RecordingFactory(string? failOnId = null, string? failOnAddChildId = null) : M.IMobileCustomGuiFactory
    {
        public List<RecordingNode> Nodes { get; } = [];
        public Dictionary<string, RecordingNode> ById => Nodes.Where(node => node.Spec.Kind != M.MobileCustomGuiNodeKind.Root).ToDictionary(node => node.Spec.Id);

        public M.IMobileCustomGuiNode Create(M.MobileCustomGuiNodeSpec spec)
        {
            if (string.Equals(spec.Id, failOnId, StringComparison.Ordinal)) throw new InvalidOperationException("测试工厂失败");
            var node = new RecordingNode(spec, failOnAddChildId);
            Nodes.Add(node);
            return node;
        }
    }

    private sealed class RecordingNode(M.MobileCustomGuiNodeSpec spec, string? failOnAddChildId) : M.IMobileCustomGuiNode, M.IMobileCustomGuiInteractiveNode
    {
        private readonly List<RecordingNode> _children = [];
        private readonly HashSet<string> _availableItems = new(
            (spec.Element as S.CustomGuiList)?.Items.Select(item => item.Id) ?? [], StringComparer.Ordinal);
        public M.MobileCustomGuiNodeSpec Spec { get; } = spec;
        public RecordingNode? Parent { get; private set; }
        public bool Disposed { get; private set; }
        public List<S.CustomGuiStateEntry> States { get; } = [];
        public S.CustomGuiStateEntry? State => States.LastOrDefault();
        public event Action? Activated;
        public event Action<string>? SelectionChanged;

        public void Activate() => Activated?.Invoke();
        public void Select(string itemId)
        {
            if (!_availableItems.Contains(itemId)) throw new InvalidOperationException("测试选择项不存在");
            SelectionChanged?.Invoke(itemId);
        }

        public void AddChild(M.IMobileCustomGuiNode child)
        {
            var typed = Assert.IsType<RecordingNode>(child);
            if (string.Equals(typed.Spec.Id, failOnAddChildId, StringComparison.Ordinal)) throw new InvalidOperationException("测试父级拒绝子节点");
            typed.Parent = this;
            _children.Add(typed);
        }

        public void ApplyState(S.CustomGuiStateEntry state)
        {
            States.Add(state);
            if (state.Kind != S.CustomGuiStateKind.List) return;
            _availableItems.Clear();
            foreach (S.CustomGuiStateListItem item in state.ListItems) _availableItems.Add(item.Id);
        }

        public void Dispose()
        {
            if (Disposed) return;
            Disposed = true;
            foreach (RecordingNode child in _children) child.Dispose();
        }
    }
}
