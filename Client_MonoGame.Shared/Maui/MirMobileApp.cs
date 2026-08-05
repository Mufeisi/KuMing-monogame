using Microsoft.Maui.Controls;
using MonoShare.Maui.Pages;
using MonoShare.Maui.Services;

namespace MonoShare.Maui;

public sealed class MirMobileApp : Application
{
    public MirMobileApp(MobileBootstrapCoordinator coordinator)
    {
        MainPage = new GameHostPage();
        _ = coordinator.EnsureInitializedAsync();
    }
}
