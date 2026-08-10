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

        try
        {
            StartGame();
        }
        catch (Exception exception)
        {
            MobileCrashDiagnostics.Capture(exception, FileSystem.AppDataDirectory);
            throw;
        }
    }

    private void StartGame()
    {
        var coordinator = new MobileBootstrapCoordinator();
        coordinator.EnsureInitializedAsync().GetAwaiter().GetResult();

        if (Intent?.GetBooleanExtra("sec01HostProbe", false) == true)
        {
            var passed = MonoShare.Security.LoginSettingsIntegration.RunHostProbe();
            global::Android.Util.Log.Info("LomMir2", $"SEC01_HOST_PROBE:{(passed ? "PASS" : "FAIL")}");
            FinishAndRemoveTask();
            global::Android.OS.Process.KillProcess(global::Android.OS.Process.MyPid());
            return;
        }

        if (Intent?.GetBooleanExtra("sec02TlsHostProbe", false) == true)
        {
            var host = Intent.GetStringExtra("sec02TlsHostProbeHost");
            var port = Intent.GetIntExtra("sec02TlsHostProbePort", 0);
            var serverName = Intent.GetStringExtra("sec02TlsHostProbeServerName");
            var passed = MonoShare.Security.LoginSettingsIntegration.RunTlsHostProbe(host, port, serverName);
            var diagnostic = passed ? string.Empty : ":" + MonoShare.Security.LoginSettingsIntegration.LastTlsHostProbeFailure;
            global::Android.Util.Log.Info("LomMir2", $"SEC02_TLS_HOST_PROBE:{(passed ? "PASS" : "FAIL")}{diagnostic}");
            FinishAndRemoveTask();
            global::Android.OS.Process.KillProcess(global::Android.OS.Process.MyPid());
            return;
        }

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
