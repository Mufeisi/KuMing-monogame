using System.Text.Json;

namespace LyoCrystal.MicroGateway.App;

internal sealed class GatewayRuntime : IAsyncDisposable
{
    private readonly string _baseDirectory;
    private readonly GatewayProjectConfiguration _project;
    private readonly MicroHttpListenerHost _host = new();
    private readonly bool _serviceMode;
    private readonly string _statePath;
    private readonly string _rescanRequestPath;
    private CancellationTokenSource? _monitorLifetime;
    private Task? _monitor;

    public GatewayRuntime(string baseDirectory, GatewayProjectConfiguration project, bool serviceMode)
    {
        _baseDirectory = baseDirectory;
        _project = project;
        _serviceMode = serviceMode;
        _statePath = Path.Combine(baseDirectory, "gateway-state.json");
        _rescanRequestPath = Path.Combine(baseDirectory, "gateway-rescan.request");
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        string resources = _project.ResolveOptionalDirectory(_baseDirectory, _project.ResourceDirectory);
        string launcher = _project.ResolveOptionalDirectory(_baseDirectory, _project.LauncherDirectory);
        string cache = string.IsNullOrWhiteSpace(_project.CacheDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "LyoCrystal", "MicroGateway", SafeProjectId(_project.ProjectId), "Cache")
            : _project.ResolveOptionalDirectory(_baseDirectory, _project.CacheDirectory);
        string code = _project.ReadSecret(_baseDirectory, _serviceMode);
        if (string.IsNullOrEmpty(code)) throw new InvalidOperationException("未找到微端访问 Code，请先用 GUI 导入部署凭据。");
        string listenHost = _project.ListenAddress is "0.0.0.0" or "*" or "+" ? "+" : _project.ListenAddress;
        await _host.StartAsync($"http://{listenHost}:{_project.Port}/", new MicroGatewayOptions(
            resources, _project.User, code, string.IsNullOrWhiteSpace(launcher) ? null : launcher,
            MemoryCacheMb: _project.MemoryCacheMb, DiskCacheMb: _project.DiskCacheMb, CacheRoot: cache,
            ResourceVersion: _project.ResourceVersion, SigningIdentity: _project.SigningIdentity)).ConfigureAwait(false);
        WriteState();
        AppendLog("网关已启动");
        _monitorLifetime = new CancellationTokenSource();
        _monitor = MonitorAsync(_monitorLifetime.Token);
    }

    public MicroGatewaySnapshot GetSnapshot() => _host.GetSnapshot();
    public Task<bool> ReconcileResourcesAsync(CancellationToken cancellationToken = default) => _host.ReconcileResourcesAsync(cancellationToken);

    public async Task StopAsync()
    {
        CancellationTokenSource? monitorLifetime = Interlocked.Exchange(ref _monitorLifetime, null);
        Task? monitor = Interlocked.Exchange(ref _monitor, null);
        monitorLifetime?.Cancel();
        if (monitor is not null) { try { await monitor.ConfigureAwait(false); } catch (OperationCanceledException) { } }
        monitorLifetime?.Dispose();
        await _host.StopAsync().ConfigureAwait(false);
        WriteState();
        AppendLog("网关已停止");
    }

    public void WriteState()
    {
        MicroGatewaySnapshot snapshot = _host.GetSnapshot();
        var state = new
        {
            format = "lyocrystal-micro-gateway-state-v1",
            projectId = _project.ProjectId,
            running = snapshot.IsRunning,
            requests = snapshot.RequestCount,
            activeRequests = snapshot.ActiveRequestCount,
            indexVersion = snapshot.IndexVersion,
            indexedFiles = snapshot.IndexedFileCount,
            indexedBytes = snapshot.IndexedBytes,
            cacheHits = snapshot.CacheHits,
            cacheMisses = snapshot.CacheMisses,
            memoryCacheBytes = snapshot.MemoryCacheBytes,
            diskCacheBytes = snapshot.DiskCacheBytes,
            lastError = Sanitize(snapshot.LastError),
            updatedUtc = DateTime.UtcNow,
        };
        string temporary = _statePath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(state));
            File.Move(temporary, _statePath, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static string SafeProjectId(string value) => string.Concat((value ?? string.Empty).Where(character => char.IsLetterOrDigit(character) || character is '-' or '_')) is { Length: > 0 } safe ? safe : "default";
    private static string? Sanitize(string? error) => string.IsNullOrWhiteSpace(error) ? null : Path.GetFileName(error.Replace('\\', '/'))[..Math.Min(256, Path.GetFileName(error.Replace('\\', '/')).Length)];
    public async ValueTask DisposeAsync() => await _host.DisposeAsync().ConfigureAwait(false);

    public void AppendLog(string message)
    {
        try
        {
            string directory = Path.Combine(_baseDirectory, "Logs");
            Directory.CreateDirectory(directory);
            File.AppendAllText(Path.Combine(directory, $"gateway-{DateTime.UtcNow:yyyyMMdd}.log"), $"{DateTime.UtcNow:O}\t{Sanitize(message)}{Environment.NewLine}");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }

    private async Task MonitorAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            if (_serviceMode && File.Exists(_rescanRequestPath) && (File.GetAttributes(_rescanRequestPath) & FileAttributes.ReparsePoint) == 0)
            {
                try { File.Delete(_rescanRequestPath); await ReconcileResourcesAsync(cancellationToken).ConfigureAwait(false); AppendLog("收到 GUI 手动重扫请求"); }
                catch (IOException) { }
            }
            WriteState();
        }
    }

    public static string? TryReadServiceStatus(string baseDirectory, string projectId)
    {
        string path = Path.Combine(baseDirectory, "gateway-state.json");
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > 64 * 1024 || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) return null;
            using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(path));
            JsonElement root = document.RootElement;
            if (!root.TryGetProperty("projectId", out JsonElement id) || id.GetString() != projectId ||
                !root.TryGetProperty("running", out JsonElement running) || !running.GetBoolean() ||
                !root.TryGetProperty("updatedUtc", out JsonElement updated) || !updated.TryGetDateTime(out DateTime time) || DateTime.UtcNow - time.ToUniversalTime() > TimeSpan.FromSeconds(4)) return null;
            long requests = root.GetProperty("requests").GetInt64();
            long active = root.GetProperty("activeRequests").GetInt64();
            int files = root.GetProperty("indexedFiles").GetInt32();
            long bytes = root.GetProperty("indexedBytes").GetInt64();
            return $"Windows Service 运行中｜请求 {requests}｜处理中 {active}｜索引 {files} 个文件 / {bytes / 1024 / 1024} MiB";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException) { return null; }
    }

    public static void RequestServiceRescan(string baseDirectory, string projectId)
    {
        if (TryReadServiceStatus(baseDirectory, projectId) is null) throw new InvalidOperationException("Windows Service 当前未运行。");
        string target = Path.Combine(baseDirectory, "gateway-rescan.request");
        string temporary = target + ".tmp-" + Guid.NewGuid().ToString("N");
        try { File.WriteAllText(temporary, projectId); File.Move(temporary, target, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
