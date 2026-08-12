using System.Drawing.Imaging;

namespace Launcher.ThemeRuntime;

public static class LauncherRuntimeHost
{
    public static int Run(
        string clientDirectory,
        Action<string, string, LauncherServer, MicroEndpoint, LauncherPlayerSettings> launchGame,
        Action<LoadedLauncherSnapshot, string, string>? runNativeClassic = null)
    {
        string builtIn = Path.Combine(clientDirectory, "Launcher", "BuiltIn");
        LoadedLauncherSnapshot builtInSnapshot = LauncherSnapshotLoader.Load(null, null, builtIn);
        string localRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LyoCrystal", "Launcher", builtInSnapshot.Snapshot.ProjectId);
        string cacheStore = Path.Combine(localRoot, "LastKnownGood");
        string remoteStore = Path.Combine(localRoot, "AcceptedRemote");
        string signatureState = Path.Combine(localRoot, "BootstrapManifestSecurityState.json");
        string requiredUpdateBarrier = Path.Combine(localRoot, "RequiredPlayerUpdate.json");
        string trustChain = Path.Combine(localRoot, "ReleaseTrustChain");
        IReadOnlyDictionary<string, Shared.Security.BootstrapManifestTrustedKey> anchorKeys = builtInSnapshot.Snapshot.TrustedReleaseKeys.ToDictionary(key => key.KeyId, StringComparer.Ordinal);
        IReadOnlyDictionary<string, Shared.Security.BootstrapManifestTrustedKey> trustedKeys = Shared.Security.BootstrapTrustChainStore.Resolve(trustChain, anchorKeys, Shared.Security.BootstrapManifestTrustConfiguration.CurrentClientCompatibilityVersion);
        string? remote = LauncherReleaseUpdater.ResolveCurrentRoot(remoteStore, signatureState, trustedKeys);
        string? cache = LauncherReleaseUpdater.ResolveCurrentRoot(cacheStore, signatureState, trustedKeys);
        LoadedLauncherSnapshot loaded;
        try { loaded = LauncherSnapshotLoader.Load(remote, cache, builtIn, (_, root) => LauncherReleaseAuthorization.IsAuthorized(root, signatureState, trustedKeys)); }
        catch (InvalidDataException) { loaded = new LoadedLauncherSnapshot(LauncherTemplateCatalog.Create(LauncherTemplateKind.Classic), builtIn, SnapshotSource.BuiltIn); }
        if (loaded.Source is SnapshotSource.Remote or SnapshotSource.Cache)
        {
            try { Shared.Security.BootstrapTrustChainStore.Record(loaded.Root, trustChain, trustedKeys, Shared.Security.BootstrapManifestTrustConfiguration.CurrentClientCompatibilityVersion); } catch { }
        }
        ProvisionMicroCredential(loaded);
        if (runNativeClassic is not null && UsesOriginalClassicLauncher(loaded.Snapshot))
        {
            string nativeSourceExecutable = Environment.GetEnvironmentVariable("LYOCRYSTAL_PLAYER_SOURCE_EXECUTABLE") ?? string.Empty;
            StartBoundedBackgroundRefresh(builtInSnapshot.Snapshot.RemoteReleaseBaseUrl, remoteStore, cacheStore, signatureState, trustedKeys);
            if (!EnsureNativeClassicEntryReady(builtInSnapshot.Snapshot.RemoteReleaseBaseUrl, nativeSourceExecutable, signatureState, requiredUpdateBarrier, trustedKeys)) return 0;
            ClientSelectionResult? selected = ClientSelection.Resolve(new NativeWindow(), loaded.Snapshot.ProjectId, clientDirectory, loaded.Snapshot.LoginCoreResources);
            if (selected is null) return 0;
            ClientSettingsWriter.ValidateWritableDirectory(selected.ResourceDirectory);
            LauncherPlayerSettings settings = ClientSettingsWriter.Read(selected.ResourceDirectory, loaded.Snapshot.Defaults);
            ClientSettingsWriter.Write(selected.ResourceDirectory, settings);
            runNativeClassic(loaded, selected.ExecutableDirectory, selected.ResourceDirectory);
            return 0;
        }
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        using var form = new LauncherForm(loaded, clientDirectory, launchGame);
        string sourceExecutable = Environment.GetEnvironmentVariable("LYOCRYSTAL_PLAYER_SOURCE_EXECUTABLE") ?? string.Empty;
        string requiredBarrierMessage = string.Empty;
        Version? requiredBarrierVersion = null;
        bool requiredBarrierActive = !string.IsNullOrWhiteSpace(sourceExecutable) && PlayerEntryUpdateService.IsRequiredBarrierActive(requiredUpdateBarrier, sourceExecutable, trustedKeys, out requiredBarrierMessage, out requiredBarrierVersion);
        if (!string.IsNullOrWhiteSpace(sourceExecutable) && (!string.IsNullOrWhiteSpace(builtInSnapshot.Snapshot.RemoteReleaseBaseUrl) || requiredBarrierActive)) form.SetEntryUpdateChecking();
        StartBoundedBackgroundRefresh(builtInSnapshot.Snapshot.RemoteReleaseBaseUrl, remoteStore, cacheStore, signatureState, trustedKeys);
        StartPlayerEntryUpdate(form, builtInSnapshot.Snapshot.RemoteReleaseBaseUrl, sourceExecutable, signatureState, requiredUpdateBarrier, requiredBarrierActive, requiredBarrierVersion, requiredBarrierMessage, trustedKeys);
        Application.Run(form);
        return 0;
    }

    private static bool EnsureNativeClassicEntryReady(
        string remoteBaseUrl,
        string sourceExecutable,
        string signatureState,
        string barrierPath,
        IReadOnlyDictionary<string, Shared.Security.BootstrapManifestTrustedKey> trustedKeys)
    {
        if (string.IsNullOrWhiteSpace(sourceExecutable)) return true;
        bool barrierActive = PlayerEntryUpdateService.IsRequiredBarrierActive(barrierPath, sourceExecutable, trustedKeys, out string barrierMessage, out Version? barrierVersion);
        if (string.IsNullOrWhiteSpace(remoteBaseUrl))
        {
            if (barrierActive) MessageBox.Show(barrierMessage, "必须更新尚未完成", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return !barrierActive;
        }
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            PlayerEntryUpdatePlan? plan = PlayerEntryUpdateService.InspectAsync(remoteBaseUrl, sourceExecutable, signatureState, trustedKeys, timeout.Token).GetAwaiter().GetResult();
            if (plan is null)
            {
                if (barrierActive) MessageBox.Show(barrierMessage, "必须更新尚未完成", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return !barrierActive;
            }
            Version offered = Version.Parse(plan.Descriptor.Version);
            if (barrierActive && (barrierVersion is null || offered < barrierVersion))
            {
                MessageBox.Show(barrierMessage, "必须更新尚未完成", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            bool blocking = barrierActive || plan.Descriptor.Required;
            if (!blocking)
            {
                _ = Task.Run(async () =>
                {
                    using var stageTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
                    try { await PlayerEntryUpdateService.StageAsync(plan, sourceExecutable, signatureState, trustedKeys, stageTimeout.Token).ConfigureAwait(false); } catch { }
                });
                return true;
            }
            if (plan.Descriptor.Required) PlayerEntryUpdateService.PersistRequiredBarrier(plan, barrierPath);
            using var requiredTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            PlayerEntryUpdateService.StageAsync(plan, sourceExecutable, signatureState, trustedKeys, requiredTimeout.Token).GetAwaiter().GetResult();
            MessageBox.Show("必须更新已准备完成，请关闭并重新打开启动器。", "玩家入口更新", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }
        catch (Exception ex)
        {
            if (!barrierActive && !File.Exists(barrierPath)) return true;
            MessageBox.Show(barrierMessage + "\r\n更新检查失败：" + ex.Message, "必须更新尚未完成", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
    }

    internal static bool UsesOriginalClassicLauncher(LauncherSnapshot snapshot) =>
        snapshot.Theme.Template == LauncherTemplateKind.Classic &&
        string.IsNullOrWhiteSpace(snapshot.Theme.BackgroundImage) &&
        string.IsNullOrWhiteSpace(snapshot.Theme.LaunchButtonImage) &&
        string.IsNullOrWhiteSpace(snapshot.Theme.LaunchButtonHoverImage) &&
        string.IsNullOrWhiteSpace(snapshot.Theme.LaunchButtonPressedImage) &&
        string.IsNullOrWhiteSpace(snapshot.Theme.LaunchButtonDisabledImage) &&
        snapshot.Theme.Controls.Count == 0;

    private sealed class NativeWindow : IWin32Window { public IntPtr Handle => IntPtr.Zero; }

    private static void StartPlayerEntryUpdate(LauncherForm form, string remoteBaseUrl, string sourceExecutable, string signatureState, string barrierPath, bool barrierActive, Version? barrierVersion, string barrierMessage, IReadOnlyDictionary<string, Shared.Security.BootstrapManifestTrustedKey> trustedKeys)
    {
        if (string.IsNullOrWhiteSpace(sourceExecutable)) return;
        form.Shown += async (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(remoteBaseUrl)) { if (barrierActive) form.BlockForRequiredEntryUpdate(barrierMessage); return; }
            using var inspectTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            PlayerEntryUpdatePlan? plan;
            try
            {
                plan = await PlayerEntryUpdateService.InspectAsync(remoteBaseUrl, sourceExecutable, signatureState, trustedKeys, inspectTimeout.Token);
            }
            catch (Exception ex)
            {
                if (barrierActive) form.BlockForRequiredEntryUpdate(barrierMessage + " 更新检查失败：" + ex.Message);
                else form.ReleaseEntryUpdateGate("入口更新检查失败，继续使用当前版本：" + ex.Message);
                return;
            }
            if (plan is null)
            {
                if (barrierActive) form.BlockForRequiredEntryUpdate(barrierMessage);
                else form.ReleaseEntryUpdateGate("启动核心已就绪，可进入游戏");
                return;
            }
            Version offeredVersion = Version.Parse(plan.Descriptor.Version);
            if (barrierActive && (barrierVersion is null || offeredVersion < barrierVersion)) { form.BlockForRequiredEntryUpdate(barrierMessage); return; }
            bool blockingDownload = plan.Descriptor.Required || barrierActive;
            if (!blockingDownload)
            {
                form.ReleaseEntryUpdateGate("发现普通入口更新；后台下载失败也不影响进入游戏");
                _ = Task.Run(async () =>
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
                    try { await PlayerEntryUpdateService.StageAsync(plan, sourceExecutable, signatureState, trustedKeys, timeout.Token).ConfigureAwait(false); }
                    catch { }
                });
                return;
            }
            try { if (plan.Descriptor.Required) PlayerEntryUpdateService.PersistRequiredBarrier(plan, barrierPath); }
            catch (Exception ex) { form.BlockForRequiredEntryUpdate("无法持久化必须更新门槛，拒绝进入游戏：" + ex.Message); return; }
            form.BlockForRequiredEntryUpdate("正在下载必须安装的玩家入口更新…");
            using var stageTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            try
            {
                await PlayerEntryUpdateService.StageAsync(plan, sourceExecutable, signatureState, trustedKeys, stageTimeout.Token);
                form.BlockForRequiredEntryUpdate("必须更新已准备完成，请关闭并重新打开启动器。");
            }
            catch (Exception ex)
            {
                form.BlockForRequiredEntryUpdate("必须更新失败，暂不能进入游戏：" + ex.Message);
            }
        };
    }

    private static void StartBoundedBackgroundRefresh(string remoteBaseUrl, string remoteStore, string cacheStore, string signatureState, IReadOnlyDictionary<string, Shared.Security.BootstrapManifestTrustedKey> trustedKeys)
    {
        if (string.IsNullOrWhiteSpace(remoteBaseUrl)) return;
        _ = Task.Run(async () =>
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try
            {
                // 当前窗口立即使用已验签的远程/LKG/内置快照；新版本原子落盘后在下次启动启用。
                await LauncherReleaseUpdater.TryRefreshAsync(remoteBaseUrl, remoteStore, cacheStore, signatureState, timeout.Token, trustedKeys: trustedKeys).ConfigureAwait(false);
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
        using var form = new LauncherForm(new LoadedLauncherSnapshot(snapshot, assetRoot, SnapshotSource.BuiltIn), assetRoot, (_, _, _, _, _) => { });
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
        using var form = new LauncherForm(new LoadedLauncherSnapshot(snapshot, assetRoot, SnapshotSource.BuiltIn), assetRoot, (_, _, _, _, _) => { });
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(-32000, -32000);
        form.Show();
        try { return form.ValidateDpiMessage(dpi); }
        finally { form.Hide(); }
    }
}

public sealed record LauncherDpiLayoutResult(bool AllControlsInsideCanvas, bool ClickTargetsMatch, int VisibleControlCount, int ActualDpi = 96, string Details = "", bool TextFits = true);
