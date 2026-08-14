using Client.CustomGui;
using Client.MirControls;
using Client.MirGraphics;
using System.Drawing;
using LyoCrystal.LauncherEditor;
using Shared.CustomGui;
using Xunit;

namespace Launcher.PlayerShellIntegration;

public sealed class PcCustomGuiAdapterTests
{
    [Fact]
    public void DynamicStateProjectsIntoExistingMirControls()
    {
        CustomGuiRuntimeDocument document = CustomGuiAuthoringDefaults.Create();
        using PcCustomGuiHost host = PcCustomGuiAdapter.Create(document, new Size(1280, 720));
        var session = new CustomGuiClientStateSession(document, 7, host);
        Guid nonce = Guid.NewGuid();
        session.Open(new CustomGuiOpenState(1, document.DocumentId, (uint)document.Revision, 7, nonce,
            DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeMilliseconds(), 1,
            [
                CustomGuiStateEntry.Text("title", "服务端活动"),
                CustomGuiStateEntry.Progress("event.loginDays", 6, 7),
                CustomGuiStateEntry.ButtonVisible("claim.visible", false),
                CustomGuiStateEntry.ButtonEnabled("claim.enabled", false),
            ]));

        Assert.Equal("服务端活动", Assert.IsType<MirLabel>(host.Controls["title"]).Text);
        Assert.Equal("6/7", Assert.IsType<MirLabel>(host.Controls["progress"].Controls[1]).Text);
        Assert.False(host.Controls["claim"].Visible);
        Assert.False(host.Controls["claim"].Enabled);

        session.ApplyDelta(new CustomGuiDeltaState(1, document.DocumentId, (uint)document.Revision, 7,
            nonce, 2, [CustomGuiStateEntry.Progress("event.loginDays", 0, 7)]));
        MirControl progressFill = host.Controls["progress"].Controls[0];
        Assert.False(progressFill.Visible);
        Assert.True(progressFill.Size.Width > 0);
        Assert.True(progressFill.Size.Height > 0);
    }

    [Fact]
    public void ActivityExchangeSelectsSubmitsRefreshesAndClosesThroughPcHost()
    {
        CustomGuiRuntimeDocument document = CustomGuiActivityExchangeTemplate.Create();
        using PcCustomGuiHost host = PcCustomGuiAdapter.Create(document, new Size(1280, 720));
        var session = new CustomGuiClientStateSession(document, 1, host);
        Guid nonce = Guid.NewGuid();
        session.Open(new CustomGuiOpenState(
            71, document.DocumentId, 1, 1, nonce,
            DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeMilliseconds(), 1,
            [
                CustomGuiStateEntry.Text("exchange.status", "活动可用"),
                CustomGuiStateEntry.List("exchange.options", [new(CustomGuiActivityExchangeTemplate.OfferId, "兑换", "限一次", string.Empty)]),
                CustomGuiStateEntry.ButtonEnabled("exchange.submit.enabled", true),
            ]));
        var sent = new List<CustomGuiClientAction>();
        host.BindActions(session, sent.Add);

        host.Select("exchange.options", CustomGuiActivityExchangeTemplate.OfferId);
        host.Controls["exchange.submit"].InvokeMouseClick(EventArgs.Empty);
        CustomGuiClientAction action = Assert.Single(sent);

        Assert.Equal(CustomGuiActionKind.SubmitSelection, action.Action);
        Assert.Equal(new[] { CustomGuiActivityExchangeTemplate.OfferId }, action.SelectionIds);
        session.ApplyDelta(new CustomGuiDeltaState(
            71, document.DocumentId, 1, 1, nonce, 2,
            [
                CustomGuiStateEntry.Text("exchange.status", "兑换已完成"),
                CustomGuiStateEntry.ButtonEnabled("exchange.submit.enabled", false),
            ]));
        session.AcceptActionResult(71, action.RequestSequence, 2, CustomGuiActionResultKind.Accepted, "兑换成功");
        Assert.Equal("兑换已完成", Assert.IsType<MirLabel>(host.Controls["exchange.status"]).Text);
        Assert.False(host.Controls["exchange.submit"].Enabled);
        Assert.True(session.Close(71));
        Assert.False(session.IsOpen);
    }

    [Fact]
    public void MirScenePacketPathOpensAdvancesAndClosesAcceptedPackageWindow()
    {
        CustomGuiRuntimeDocument document = CustomGuiAuthoringDefaults.Create();
        PcCustomGuiRuntime.Reset();
        PcCustomGuiRuntime.RegisterAcceptedPackage(new CustomGuiAcceptedPackage(
            "gui10-test", 7, document, "package", "document", CustomGuiResourceCatalog.Empty));
        using var scene = new TestScene { Size = new Size(1280, 720) };
        Guid nonce = Guid.NewGuid();

        scene.ProcessPacket(new ServerPackets.CustomGuiOpen
        {
            WindowInstanceId = 90, DocumentId = document.DocumentId, DocumentRevision = (uint)document.Revision,
            PackageSequence = 7, SessionNonce = nonce, StateRevision = 1,
            ExpiresAtUnixMilliseconds = DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeMilliseconds(),
            State = [CustomGuiStateEntry.Text("title", "收包打开")],
        });
        scene.ProcessPacket(new ServerPackets.CustomGuiStateDelta
        {
            WindowInstanceId = 90, DocumentId = document.DocumentId, DocumentRevision = (uint)document.Revision,
            PackageSequence = 7, SessionNonce = nonce, StateRevision = 2,
            State = [CustomGuiStateEntry.ButtonEnabled("claim.enabled", false)],
        });
        scene.ProcessPacket(new ServerPackets.CustomGuiStateDelta
        {
            WindowInstanceId = 90, DocumentId = document.DocumentId, DocumentRevision = (uint)document.Revision,
            PackageSequence = 7, SessionNonce = nonce, StateRevision = 4,
            State = [CustomGuiStateEntry.Text("title", "跳号不应生效")],
        });

        Assert.True(PcCustomGuiRuntime.IsOpen);
        Assert.Equal((uint)2, PcCustomGuiRuntime.StateRevision);
        Assert.Single(scene.Controls);
        scene.ProcessPacket(new ServerPackets.CustomGuiClose { WindowInstanceId = 90, Reason = CustomGuiCloseReason.Requested });
        Assert.False(PcCustomGuiRuntime.IsOpen);
        Assert.Empty(scene.Controls);
        PcCustomGuiRuntime.Reset();
    }

    [Fact]
    public void AdapterMaterializesEveryV1ElementAsMirControlAtFitScale()
    {
        CustomGuiRuntimeDocument document = CustomGuiAuthoringDefaults.Create();
        ((CustomGuiList)document.Elements.Single(element => element.Id == "rewards")).Items =
        [
            new("day-1", "第一天", "新手武器", "starter-sword"),
            new("day-2", "第二天", "金币", "gold"),
        ];

        var library = new MLibrary(Path.Combine(Path.GetTempPath(), "gui03-test-library"));
        using PcCustomGuiHost host = PcCustomGuiAdapter.Create(document, new Size(1920, 1080), new FixedAssetResolver(library));

        Assert.Equal(1.5f, host.Scale);
        Assert.Equal(Point.Empty, host.ViewportOffset);
        Assert.Equal(9, host.Controls.Count);
        Assert.All(host.Controls.Values, control => Assert.IsAssignableFrom<MirControl>(control));
        Assert.IsType<PcCustomGuiWindowControl>(host.Controls["event"]);
        Assert.IsType<PcCustomGuiPanelControl>(host.Controls["header"]);
        Assert.IsType<PcCustomGuiImageControl>(host.Controls["banner"]);
        Assert.True(((PcCustomGuiImageControl)host.Controls["banner"]).AssetResolved);
        Assert.IsType<MirLabel>(host.Controls["title"]);
        Assert.IsType<PcCustomGuiListControl>(host.Controls["rewards"]);
        Assert.IsType<PcCustomGuiItemSlotControl>(host.Controls["reward-slot"]);
        Assert.IsType<PcCustomGuiProgressBarControl>(host.Controls["progress"]);
        Assert.IsType<PcCustomGuiTextInputControl>(host.Controls["gift-code"]);
        Assert.IsType<PcCustomGuiButtonControl>(host.Controls["claim"]);
        Assert.Equal(new Point(360, 135), host.Controls["event"].Location);
        Assert.Equal(new Size(1200, 810), host.Controls["event"].Size);
        Assert.Equal(new Point(750, 645), host.Controls["claim"].Location);
        Assert.Equal(new Size(330, 72), host.Controls["claim"].Size);
        Assert.Equal(2, ((PcCustomGuiListControl)host.Controls["rewards"]).StaticItems.Count);
        Assert.Equal(3m / 7m, ((PcCustomGuiProgressBarControl)host.Controls["progress"]).Ratio);
        Assert.Equal("event.claim", ((PcCustomGuiButtonControl)host.Controls["claim"]).ActionId);
        using var sceneRoot = new MirControl();
        host.AttachTo(sceneRoot);
        Assert.Same(sceneRoot, host.Root.Parent);
        Assert.Contains(host.Root, sceneRoot.Controls);
        MirLabel title = Assert.IsType<MirLabel>(host.Controls["title"]);
        host.Dispose();
        Assert.True(title.IsDisposed);
        Assert.DoesNotContain(host.Root, sceneRoot.Controls);
    }

    [Fact]
    public void AdapterUsesSharedParentAnchorLayoutAndDoesNotMutateRuntimeDocument()
    {
        CustomGuiRuntimeDocument document = CustomGuiAuthoringDefaults.Create();
        byte[] before = CustomGuiDocumentCodec.Serialize(document);

        using PcCustomGuiHost host = PcCustomGuiAdapter.Create(document, new Size(1024, 768));

        Assert.Equal(0.8f, host.Scale);
        Assert.Equal(new Point(0, 96), host.ViewportOffset);
        Assert.Equal(new Point(192, 168), host.Controls["event"].Location);
        Assert.Equal(new Point(32, 30), host.Controls["header"].Location);
        Assert.Equal(new Point(400, 344), host.Controls["claim"].Location);
        Assert.Equal(before, CustomGuiDocumentCodec.Serialize(document));
        PcCustomGuiImageControl fallback = Assert.IsType<PcCustomGuiImageControl>(host.Controls["banner"]);
        Assert.False(fallback.AssetResolved);
        Assert.NotEqual(Color.Transparent, fallback.BackColour);
        Assert.Contains(fallback.Controls, control => control is MirLabel label && label.Text == "活动横幅");
    }

    [Fact]
    public void AdapterFailsClosedBeforeCreatingControlsWhenParentGraphCycles()
    {
        CustomGuiRuntimeDocument document = CustomGuiAuthoringDefaults.Create();
        document.Elements.Single(element => element.Id == "event").ParentId = "header";

        CustomGuiLayoutException error = Assert.Throws<CustomGuiLayoutException>(() => PcCustomGuiAdapter.Create(document, new Size(1280, 720)));

        Assert.Equal("GUI03-LAYOUT-001", error.Code);
    }

    private sealed class FixedAssetResolver(MLibrary library) : IPcCustomGuiAssetResolver
    {
        public bool TryResolve(string assetId, out PcCustomGuiAsset asset)
        {
            asset = new PcCustomGuiAsset(library, 1);
            return !string.IsNullOrWhiteSpace(assetId);
        }
    }

    private sealed class TestScene : MirScene { public override void Process() { } }
}
