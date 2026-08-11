using System.Drawing.Imaging;

namespace Launcher.ThemeRuntime;

public static class LauncherRuntimeHost
{
    public static int Run(string clientDirectory, Action<string, LauncherServer, MicroEndpoint, LauncherPlayerSettings> launchGame)
    {
        string localRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LyoCrystal", "Launcher");
        string builtIn = Path.Combine(clientDirectory, "Launcher", "BuiltIn");
        string cacheStore = Path.Combine(localRoot, "LastKnownGood");
        string remoteStore = Path.Combine(localRoot, "AcceptedRemote");
        string signatureState = Path.Combine(localRoot, "BootstrapManifestSecurityState.json");
        LoadedLauncherSnapshot builtInSnapshot = LauncherSnapshotLoader.Load(null, null, builtIn);
        string? remote = LauncherReleaseUpdater.ResolveCurrentRoot(remoteStore, signatureState);
        string? cache = LauncherReleaseUpdater.ResolveCurrentRoot(cacheStore, signatureState);
        LoadedLauncherSnapshot loaded;
        try { loaded = LauncherSnapshotLoader.Load(remote, cache, builtIn, (_, root) => LauncherReleaseAuthorization.IsAuthorized(root, signatureState)); }
        catch (InvalidDataException) { loaded = new LoadedLauncherSnapshot(LauncherTemplateCatalog.Create(LauncherTemplateKind.Classic), builtIn, SnapshotSource.BuiltIn); }
        ProvisionMicroCredential(loaded);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        using var form = new LauncherForm(loaded, clientDirectory, launchGame);
        StartBoundedBackgroundRefresh(builtInSnapshot.Snapshot.RemoteReleaseBaseUrl, remoteStore, cacheStore, signatureState);
        Application.Run(form);
        return 0;
    }

    private static void StartBoundedBackgroundRefresh(string remoteBaseUrl, string remoteStore, string cacheStore, string signatureState)
    {
        if (string.IsNullOrWhiteSpace(remoteBaseUrl)) return;
        _ = Task.Run(async () =>
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try
            {
                // 当前窗口立即使用已验签的远程/LKG/内置快照；新版本原子落盘后在下次启动启用。
                await LauncherReleaseUpdater.TryRefreshAsync(remoteBaseUrl, remoteStore, cacheStore, signatureState, timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (Exception) { }
        });
    }

    private static void ProvisionMicroCredential(LoadedLauncherSnapshot loaded)
    {
        string path = Path.Combine(loaded.Root, "micro.credential");
        if (!File.Exists(path)) return;
        byte[] envelope = File.ReadAllBytes(path);
        if (envelope.Length > 1024) throw new InvalidDataException("微端凭据封装超过大小上限");
        string code = Shared.Security.MicroCredentialEnvelope.Open(loaded.Snapshot.ProjectId, envelope);
        Shared.Security.ProtectedClientSecretStore.WriteMicroCode(loaded.Snapshot.ProjectId, code);
    }

    public static Bitmap RenderTemplateForEvidence(LauncherSnapshot snapshot, string assetRoot, float scale)
    {
        int dpi = (int)Math.Round(96 * scale);
        if (dpi is not (96 or 120 or 144 or 192)) throw new ArgumentOutOfRangeException(nameof(scale));
        LauncherSnapshotValidator.Validate(snapshot);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        using var form = new LauncherForm(new LoadedLauncherSnapshot(snapshot, assetRoot, SnapshotSource.BuiltIn), assetRoot, (_, _, _, _) => { });
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(-32000, -32000);
        form.Show();
        Application.DoEvents();
        LauncherDpiLayoutResult layout = form.ValidateDpiMessage(dpi);
        if (!layout.AllControlsInsideCanvas || !layout.ClickTargetsMatch || !layout.TextFits) throw new InvalidDataException("DPI 布局验证失败：" + layout.Details);
        var bitmap = new Bitmap(form.Width, form.Height, PixelFormat.Format32bppArgb);
        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
        form.Hide();
        return bitmap;
    }

    public static LauncherDpiLayoutResult ValidatePerMonitorDpiForEvidence(LauncherSnapshot snapshot, string assetRoot, int dpi)
    {
        if (dpi is not (96 or 120 or 144 or 192)) throw new ArgumentOutOfRangeException(nameof(dpi));
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        using var form = new LauncherForm(new LoadedLauncherSnapshot(snapshot, assetRoot, SnapshotSource.BuiltIn), assetRoot, (_, _, _, _) => { });
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(-32000, -32000);
        form.Show();
        try { return form.ValidateDpiMessage(dpi); }
        finally { form.Hide(); }
    }
}

public sealed record LauncherDpiLayoutResult(bool AllControlsInsideCanvas, bool ClickTargetsMatch, int VisibleControlCount, int ActualDpi = 96, string Details = "", bool TextFits = true);
