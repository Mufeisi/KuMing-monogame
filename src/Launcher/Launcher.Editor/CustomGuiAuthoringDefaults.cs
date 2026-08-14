using Shared.CustomGui;

namespace LyoCrystal.LauncherEditor;

internal static class CustomGuiAuthoringDefaults
{
    public static CustomGuiRuntimeDocument Create() => new()
    {
        DocumentId = "new-player-event",
        Revision = 1,
        Viewport = new(1280, 720, CustomGuiScaleMode.Fit, CustomGuiSafeAreaMode.Required),
        Elements =
        [
            new CustomGuiWindow { Id = "event", Layout = new(0, 0, 800, 540, CustomGuiHorizontalAnchor.Center, CustomGuiVerticalAnchor.Center), Title = "新玩家活动", ZIndex = 0 },
            new CustomGuiPanel { Id = "header", ParentId = "event", Layout = At(40, 38, 720, 72), BackgroundColor = "#202B3A", ZIndex = 1 },
            new CustomGuiImage { Id = "banner", ParentId = "header", Layout = At(20, 12, 144, 48), AssetId = "event-banner", AlternateText = "活动横幅", ZIndex = 2 },
            new CustomGuiText { Id = "title", ParentId = "header", Layout = At(188, 16, 300, 42), Content = "七日登录礼", FontSize = 24, Color = "#F4D88B", ZIndex = 3 },
            new CustomGuiList { Id = "rewards", ParentId = "event", Layout = At(52, 136, 696, 214), Spacing = 12, ZIndex = 4 },
            new CustomGuiItemSlot { Id = "reward-slot", ParentId = "rewards", Layout = At(28, 26, 96, 96), AssetId = "starter-sword", DisplayName = "新手武器", Quantity = 1, ZIndex = 5 },
            new CustomGuiProgressBar { Id = "progress", ParentId = "event", Layout = At(80, 378, 640, 28), Value = 3, Maximum = 7, Text = "3 / 7", BindingKey = "event.loginDays", ZIndex = 6 },
            new CustomGuiTextInput { Id = "gift-code", ParentId = "event", Layout = At(80, 430, 380, 44), Placeholder = "输入礼包码", MaxLength = 32, BindingKey = "event.giftCode", ZIndex = 7 },
            new CustomGuiButton { Id = "claim", ParentId = "event", Layout = At(500, 430, 220, 48), Text = "领取奖励", ActionId = "event.claim", ZIndex = 8 },
        ],
    };

    private static CustomGuiLayout At(int x, int y, int width, int height) => new(x, y, width, height);
}
