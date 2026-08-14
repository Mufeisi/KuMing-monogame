using Shared.CustomGui;
using Xunit;

namespace Base05.Tests;

public sealed class CustomGuiSchemaTests
{
    [Fact]
    public void 仓库示例运行描述由生产Codec直接读取()
    {
        string repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        string sample = Path.Combine(repositoryRoot, "Docs", "samples", "custom-gui", "new-player-event.v1.json");

        CustomGuiRuntimeDocument document = CustomGuiDocumentCodec.Deserialize(File.ReadAllBytes(sample));

        Assert.Equal("new-player-event", document.DocumentId);
        Assert.Equal(9, document.Elements.Count);
    }

    [Fact]
    public void 首版控件族与跨端布局可确定性往返()
    {
        CustomGuiRuntimeDocument source = CreateDocument();

        byte[] first = CustomGuiDocumentCodec.Serialize(source);
        byte[] second = CustomGuiDocumentCodec.Serialize(source);
        CustomGuiRuntimeDocument restored = CustomGuiDocumentCodec.Deserialize(first);

        Assert.Equal(first, second);
        Assert.Equal(CustomGuiSchema.CurrentVersion, restored.SchemaVersion);
        Assert.Equal(CustomGuiSafeAreaMode.Required, restored.Viewport.SafeArea);
        Assert.Equal(CustomGuiScaleMode.Fit, restored.Viewport.ScaleMode);
        Assert.Collection(restored.Elements,
            element => Assert.IsType<CustomGuiWindow>(element),
            element => Assert.IsType<CustomGuiPanel>(element),
            element => Assert.IsType<CustomGuiImage>(element),
            element => Assert.IsType<CustomGuiText>(element),
            element => Assert.IsType<CustomGuiButton>(element),
            element => Assert.IsType<CustomGuiTextInput>(element),
            element => Assert.IsType<CustomGuiList>(element),
            element => Assert.IsType<CustomGuiProgressBar>(element),
            element => Assert.IsType<CustomGuiItemSlot>(element));

        CustomGuiPanel panel = Assert.IsType<CustomGuiPanel>(restored.Elements[1]);
        Assert.Equal(CustomGuiFlowDirection.Vertical, panel.Flow.Direction);
        Assert.Equal(new CustomGuiThickness(12, 10, 12, 10), panel.Flow.Padding);
        Assert.Equal(CustomGuiHorizontalAnchor.Stretch, panel.Layout.HorizontalAnchor);
        Assert.Equal(CustomGuiVerticalAnchor.Stretch, panel.Layout.VerticalAnchor);
    }

    [Theory]
    [InlineData("{\"schemaVersion\":1,\"documentId\":\"x\",\"revision\":1,\"viewport\":{\"referenceWidth\":1280,\"referenceHeight\":720,\"scaleMode\":\"fit\",\"safeArea\":\"required\"},\"elements\":[{\"type\":\"video\",\"id\":\"x\",\"layout\":{\"x\":0,\"y\":0,\"width\":10,\"height\":10,\"horizontalAnchor\":\"left\",\"verticalAnchor\":\"top\",\"margin\":{\"left\":0,\"top\":0,\"right\":0,\"bottom\":0}}}]}", "GUI01-SCHEMA-001")]
    [InlineData("{\"schemaVersion\":1,\"documentId\":\"x\",\"revision\":1,\"unknown\":true,\"viewport\":{\"referenceWidth\":1280,\"referenceHeight\":720,\"scaleMode\":\"fit\",\"safeArea\":\"required\"},\"elements\":[]}", "GUI01-SCHEMA-001")]
    [InlineData("{\"schemaVersion\":1,\"documentId\":\"x\",\"revision\":1,\"viewport\":{\"referenceWidth\":1280,\"referenceHeight\":720,\"scaleMode\":\"stretch\",\"safeArea\":\"required\"},\"elements\":[]}", "GUI01-SCHEMA-001")]
    [InlineData("{\"schemaVersion\":1,\"documentId\":\"x\",\"revision\":1,\"viewport\":{\"referenceWidth\":1280,\"referenceHeight\":720,\"scaleMode\":\"fit\",\"safeArea\":\"required\"}}", "GUI01-SCHEMA-001")]
    [InlineData("{\"schemaVersion\":1,\"documentId\":\"x\",\"revision\":1,\"viewport\":{\"referenceWidth\":1280,\"referenceHeight\":720,\"scaleMode\":\"fit\",\"safeArea\":\"required\"},\"elements\":[{\"type\":\"window\",\"id\":null,\"layout\":{\"x\":0,\"y\":0,\"width\":10,\"height\":10},\"title\":\"x\"}]}", "GUI01-SCHEMA-001")]
    [InlineData("{\"schemaVersion\":2,\"documentId\":\"x\",\"revision\":1,\"viewport\":{\"referenceWidth\":1280,\"referenceHeight\":720,\"scaleMode\":\"fit\",\"safeArea\":\"required\"},\"elements\":[]}", "GUI01-SCHEMA-002")]
    public void 未知控件未知属性与不兼容版本全部失败关闭(string json, string expectedCode)
    {
        CustomGuiSchemaException error = Assert.Throws<CustomGuiSchemaException>(() =>
            CustomGuiDocumentCodec.Deserialize(System.Text.Encoding.UTF8.GetBytes(json)));

        Assert.Equal(expectedCode, error.Code);
    }

    [Fact]
    public void 共享布局引擎统一解析父级锚点拉伸与横向流()
    {
        CustomGuiRuntimeDocument document = new()
        {
            DocumentId = "layout",
            Viewport = new(1000, 600, CustomGuiScaleMode.Fit, CustomGuiSafeAreaMode.Required),
            Elements =
            [
                new CustomGuiWindow { Id = "root", Layout = new(0, 0, 400, 200, CustomGuiHorizontalAnchor.Center, CustomGuiVerticalAnchor.Center) },
                new CustomGuiPanel { Id = "flow", ParentId = "root", Layout = new(10, 20, 20, 30, CustomGuiHorizontalAnchor.Stretch, CustomGuiVerticalAnchor.Stretch), Flow = new(CustomGuiFlowDirection.Horizontal, 5, new(10, 10, 10, 10)) },
                new CustomGuiText { Id = "first", ParentId = "flow", Layout = new(99, 0, 50, 20, Margin: new(2, 3, 4, 5)), Content = "一" },
                new CustomGuiText { Id = "second", ParentId = "flow", Layout = new(88, 0, 60, 20, Margin: new(1, 3, 2, 5)), Content = "二" },
            ],
        };

        IReadOnlyDictionary<string, CustomGuiResolvedBounds> result = CustomGuiLayoutEngine.Resolve(document);

        Assert.Equal(new CustomGuiResolvedBounds(300, 200, 400, 200), result["root"]);
        Assert.Equal(new CustomGuiResolvedBounds(310, 220, 370, 150), result["flow"]);
        Assert.Equal(new CustomGuiResolvedBounds(322, 233, 50, 20), result["first"]);
        Assert.Equal(new CustomGuiResolvedBounds(382, 233, 60, 20), result["second"]);
    }

    [Fact]
    public void 共享布局引擎对循环父级稳定失败关闭()
    {
        CustomGuiRuntimeDocument document = CreateDocument();
        document.Elements[0].ParentId = "content";

        CustomGuiLayoutException error = Assert.Throws<CustomGuiLayoutException>(() => CustomGuiLayoutEngine.Resolve(document));

        Assert.Equal("GUI03-LAYOUT-001", error.Code);
    }

    private static CustomGuiRuntimeDocument CreateDocument()
    {
        CustomGuiLayout root = new(0, 0, 960, 540, CustomGuiHorizontalAnchor.Center, CustomGuiVerticalAnchor.Center, CustomGuiThickness.Zero);
        CustomGuiLayout fill = new(0, 0, 0, 0, CustomGuiHorizontalAnchor.Stretch, CustomGuiVerticalAnchor.Stretch, new CustomGuiThickness(16, 16, 16, 16));
        return new CustomGuiRuntimeDocument
        {
            DocumentId = "new-player-event",
            Revision = 7,
            Viewport = new CustomGuiViewport(1280, 720, CustomGuiScaleMode.Fit, CustomGuiSafeAreaMode.Required),
            Elements =
            [
                new CustomGuiWindow { Id = "root", Layout = root, Title = "新手活动" },
                new CustomGuiPanel { Id = "content", ParentId = "root", Layout = fill, Flow = new CustomGuiFlow(CustomGuiFlowDirection.Vertical, 8, new CustomGuiThickness(12, 10, 12, 10)), ClipChildren = true },
                new CustomGuiImage { Id = "banner", ParentId = "content", Layout = new(0, 0, 600, 160), AssetId = "activity/banner", Stretch = CustomGuiImageStretch.UniformToFill },
                new CustomGuiText { Id = "title", ParentId = "content", Layout = new(0, 0, 600, 40), Content = "欢迎参加活动", Format = CustomGuiTextFormat.Rich },
                new CustomGuiButton { Id = "claim", ParentId = "content", Layout = new(0, 0, 180, 48), Text = "领取", ActionId = "claim" },
                new CustomGuiTextInput { Id = "code", ParentId = "content", Layout = new(0, 0, 280, 40), Placeholder = "输入兑换码", MaxLength = 32 },
                new CustomGuiList { Id = "rewards", ParentId = "content", Layout = new(0, 0, 600, 160), Orientation = CustomGuiListOrientation.Horizontal, Items = [new("reward-1", "木剑", "数量 1", "items/wood-sword")] },
                new CustomGuiProgressBar { Id = "progress", ParentId = "content", Layout = new(0, 0, 600, 24), Minimum = 0, Maximum = 100, Value = 35, Text = "35%" },
                new CustomGuiItemSlot { Id = "slot", ParentId = "content", Layout = new(0, 0, 48, 48), AssetId = "items/wood-sword", DisplayName = "木剑", Quantity = 1 },
            ]
        };
    }
}
