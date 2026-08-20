using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Microsoft.Maui.Storage;
using Microsoft.Xna.Framework;
using MonoShare;
using MonoShare.Maui.Services;
using AView = Android.Views.View;

namespace Client_MonoGame.Android;

[Activity(
    Theme = "@style/Maui.SplashTheme",
    MainLauncher = true,
    LaunchMode = LaunchMode.SingleTop,
    ScreenOrientation = ScreenOrientation.Landscape,
    ConfigurationChanges = ConfigChanges.ScreenSize |
                           ConfigChanges.Orientation |
                           ConfigChanges.UiMode |
                           ConfigChanges.ScreenLayout |
                           ConfigChanges.SmallestScreenSize |
                           ConfigChanges.Density)]
public sealed class MainActivity : AndroidGameActivity
{
    private CMain? _game;
    private AView? _view;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        var coordinator = new MobileBootstrapCoordinator();
        coordinator.EnsureInitializedAsync().GetAwaiter().GetResult();

        _game = new CMain(FileSystem.AppDataDirectory)
        {
            IsMouseVisible = false,
        };

        _view = _game.Services.GetService(typeof(AView)) as AView
            ?? throw new InvalidOperationException("MonoGame 未返回 Android 原生游戏视图。");

        SetContentView(_view);
        _game.Run();
    }
}
