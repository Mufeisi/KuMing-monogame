#if ANDROID
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoShare.CustomGui;
using Shared.CustomGui;
using Shared.Security;

namespace MonoShare;

public static class MobileCustomGuiRenderSmoke
{
    private static CustomGuiRuntimeDocument? _document;
    private static string? _outputPath;
    private static MobileCustomGuiHost? _host;
    private static int _frames;
    private static bool _captured;

    public static bool IsConfigured => _document is not null && !string.IsNullOrWhiteSpace(_outputPath);
    public static bool Completed { get; private set; }
    public static string Failure { get; private set; } = string.Empty;

    public static void Configure(string packagesRoot, string keyId, string publicKey, string outputPath)
    {
        var trusted = new Dictionary<string, BootstrapManifestTrustedKey>(StringComparer.Ordinal)
        {
            [keyId] = new BootstrapManifestTrustedKey { KeyId = keyId, SubjectPublicKeyInfo = publicKey, NotBeforeSequence = 1 },
        };
        CustomGuiAcceptedPackage accepted = CustomGuiSignedReleaseLoader.Load(new CustomGuiSignedReleaseRequest
        {
            PackagesRoot = packagesRoot,
            TrustedKeys = trusted,
            CurrentClientVersion = new Version(2, 0, 0),
        });
        _document = accepted.Document;
        _outputPath = Path.GetFullPath(outputPath);
        Completed = false;
        Failure = string.Empty;
        _frames = 0;
        _captured = false;
    }

    internal static void TryAttach()
    {
        if (!IsConfigured || _host is not null) return;
        _host = FairyGuiHost.AttachCustomGui(_document!);
    }

    internal static void TryCaptureAfterDraw(CMain game)
    {
        if (!IsConfigured || _captured || ++_frames < 30) return;
        _captured = true;
        try
        {
            GraphicsDevice device = game.GraphicsDevice;
            int width = device.PresentationParameters.BackBufferWidth;
            int height = device.PresentationParameters.BackBufferHeight;
            var pixels = new Color[checked(width * height)];
            device.GetBackBufferData(pixels);
            using var image = new Texture2D(device, width, height, false, SurfaceFormat.Color);
            image.SetData(pixels);
            Directory.CreateDirectory(Path.GetDirectoryName(_outputPath!)!);
            using FileStream output = new(_outputPath!, FileMode.Create, FileAccess.Write, FileShare.None);
            image.SaveAsPng(output, width, height);
            output.Flush(true);
            Completed = true;
            global::Android.Util.Log.Info("LomMir2", "GUI06_ANDROID_RENDER:PASS");
        }
        catch (Exception error)
        {
            Failure = error.ToString();
            global::Android.Util.Log.Error("LomMir2", "GUI06_ANDROID_RENDER:FAIL:" + Failure);
        }
        finally
        {
            game.Exit();
        }
    }
}
#endif
