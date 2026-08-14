namespace Shared.CustomGui;

/// <summary>
/// LEG-07 首个动态样板窗口。PC 与 Android 从同一签名文档加载此结构，业务数值始终由服务端状态覆盖。
/// </summary>
public static class CustomGuiActivityExchangeTemplate
{
    public const string DocumentId = "activity.exchange";
    public const string SubmitActionId = "exchange.submit";
    public const string OfferId = "credit.10";

    public static CustomGuiRuntimeDocument Create() => new()
    {
        DocumentId = DocumentId,
        Revision = 1,
        Viewport = new(1280, 720, CustomGuiScaleMode.Fit, CustomGuiSafeAreaMode.Required),
        Elements =
        [
            new CustomGuiWindow
            {
                Id = "exchange", Layout = new(0, 0, 720, 500,
                    CustomGuiHorizontalAnchor.Center, CustomGuiVerticalAnchor.Center),
                Title = "限时兑换", ZIndex = 0
            },
            new CustomGuiPanel
            {
                Id = "exchange.header", ParentId = "exchange", Layout = At(36, 30, 648, 76),
                BackgroundColor = "#202B3A", ZIndex = 1
            },
            new CustomGuiText
            {
                Id = "exchange.title", ParentId = "exchange.header", Layout = At(24, 14, 600, 42),
                Content = "限时兑换", FontSize = 24, Color = "#F4D88B", ZIndex = 2
            },
            new CustomGuiText
            {
                Id = "exchange.status", ParentId = "exchange", Layout = At(52, 126, 616, 36),
                Content = "正在读取活动状态", FontSize = 16, Color = "#FFFFFF", ZIndex = 3
            },
            new CustomGuiText
            {
                Id = "exchange.balance", ParentId = "exchange", Layout = At(52, 170, 616, 32),
                Content = "余额由服务端提供", FontSize = 15, Color = "#C9D4E2", ZIndex = 4
            },
            new CustomGuiList
            {
                Id = "exchange.options", ParentId = "exchange", Layout = At(52, 220, 616, 92),
                SelectionBindingKey = "exchange.selection", Spacing = 8,
                Items = [new CustomGuiListItem(OfferId, "1000 金币兑换 10 信用点", "每个角色限一次", string.Empty)],
                ZIndex = 5
            },
            new CustomGuiProgressBar
            {
                Id = "exchange.progress", ParentId = "exchange", Layout = At(52, 334, 616, 28),
                Minimum = 0, Maximum = 1, Value = 0, Text = "0 / 1", BindingKey = "exchange.progress", ZIndex = 6
            },
            new CustomGuiButton
            {
                Id = "exchange.submit", ParentId = "exchange", Layout = At(448, 394, 220, 52),
                Text = "确认兑换", ActionId = SubmitActionId, Action = CustomGuiActionKind.SubmitSelection, ZIndex = 7
            }
        ]
    };

    private static CustomGuiLayout At(int x, int y, int width, int height) => new(x, y, width, height);
}
