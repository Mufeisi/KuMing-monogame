using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.Storage;

namespace MonoShare.Maui.Services;

public sealed class MobileBootstrapCoordinator
{
    private readonly SemaphoreSlim _initializeGate = new(1, 1);
    private CancellationTokenSource? _pumpCts;
    private Task? _pumpTask;
    private bool _initialized;

    public string ClientRoot => FileSystem.AppDataDirectory;
    public string RuntimeRoot => ClientResourceLayout.RuntimeRoot;
    public string DiagnosticsPath => ClientResourceLayout.PackageDiagnosticsReportPath;
    public string UpdateQueuePath => ClientResourceLayout.PackageUpdateQueuePath;

    public async Task EnsureInitializedAsync()
    {
        if (_initialized)
            return;

        await _initializeGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_initialized)
                return;

            PackageResourceRegistry.Configure(OpenPackageStream);

            Settings.ConfigureClientRoot(FileSystem.AppDataDirectory);
            Settings.Load();
            ClientResourceLayout.EnsureWritableResourceDirectories();
            ClientResourceLayout.EnsureCoreBootstrapAssetsAvailable();

            _pumpCts = new CancellationTokenSource();
            _pumpTask = Task.Run(() => BackgroundPumpAsync(_pumpCts.Token));
            _initialized = true;
        }
        finally
        {
            _initializeGate.Release();
        }
    }

    public async Task<BootstrapPackageRuntimeOverview> LoadOverviewAsync(
        bool refreshState = true,
        bool reloadBootstrapMetadata = false,
        bool processPendingRequests = false)
    {
        await EnsureInitializedAsync().ConfigureAwait(false);
        return BootstrapPackageRuntime.LoadOverview(
            refreshState: refreshState,
            reloadBootstrapMetadata: reloadBootstrapMetadata,
            processPendingRequests: processPendingRequests);
    }

    public async Task<string> BuildDiagnosticsAsync()
    {
        await EnsureInitializedAsync().ConfigureAwait(false);
        return BootstrapPackageRuntime.BuildDiagnosticsReport(
            refreshState: true,
            reloadBootstrapMetadata: false,
            processPendingRequests: true,
            packageLimit: 24,
            requestLimit: 24);
    }

    public async Task<bool> WriteDiagnosticsAsync()
    {
        await EnsureInitializedAsync().ConfigureAwait(false);
        return BootstrapPackageRuntime.TryWriteDiagnosticsReport(
            refreshState: true,
            reloadBootstrapMetadata: false,
            processPendingRequests: true,
            packageLimit: 24,
            requestLimit: 24);
    }

    internal async Task<BootstrapPreLoginUpdatePlanView> EnsurePreLoginUpdatePlanAsync(CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync().ConfigureAwait(false);
        return await BootstrapPackageUpdateService.TryEnsurePreLoginUpdateQueueAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task TickBootstrapAsync()
    {
        await EnsureInitializedAsync().ConfigureAwait(false);
        ClientResourceLayout.ProcessPendingPackageRequestsNow();
        BootstrapPackageDownloader.TryDownloadPendingPackagesIfDue();
        ClientResourceLayout.TryApplyBundleInboxIfDue();
        ClientResourceLayout.RefreshPackageStateSnapshot();
    }

    private static Stream? OpenPackageStream(string relativePath)
    {
        try
        {
            string normalizedPath = relativePath.Replace('\\', '/').TrimStart('/');
            return FileSystem.OpenAppPackageFileAsync(normalizedPath).GetAwaiter().GetResult();
        }
        catch
        {
            return null;
        }
    }

    private static async Task BackgroundPumpAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            try
            {
                ClientResourceLayout.ProcessPendingPackageRequestsIfDue();
                BootstrapPackageDownloader.TryDownloadPendingPackagesIfDue();
                ClientResourceLayout.TryApplyBundleInboxIfDue();
            }
            catch (Exception ex)
            {
                CMain.SaveError("MAUI 后台资源泵异常：" + ex);
            }
        }
    }
}
