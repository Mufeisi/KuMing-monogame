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
}
