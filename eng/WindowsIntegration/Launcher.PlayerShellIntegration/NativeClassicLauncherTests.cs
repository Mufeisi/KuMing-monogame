using Launcher.ThemeRuntime;
using Xunit;

namespace Launcher.PlayerShellIntegration.Windows;

public sealed class NativeClassicLauncherTests
{
    [Fact]
    public void 原版玩家入口首次显示时遵循项目自动开始设置()
    {
        LauncherSnapshot snapshot = LauncherTemplateCatalog.Create(LauncherTemplateKind.Classic);

        snapshot.Defaults.AutoStart = true;
        Assert.True(Launcher.AMain.ShouldAutoStartNative(snapshot, alreadyTriggered: false));
        Assert.False(Launcher.AMain.ShouldAutoStartNative(snapshot, alreadyTriggered: true));

        snapshot.Defaults.AutoStart = false;
        Assert.False(Launcher.AMain.ShouldAutoStartNative(snapshot, alreadyTriggered: false));
    }
}
