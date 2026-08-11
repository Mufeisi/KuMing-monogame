using System.Net.Sockets;
using System.Text.Json;
using Shared.Security;

namespace Launcher.ThemeRuntime;

public enum SnapshotSource { Remote, Cache, BuiltIn }
public sealed record LoadedLauncherSnapshot(LauncherSnapshot Snapshot, string Root, SnapshotSource Source);

public static class LauncherSnapshotLoader
{
    public static LoadedLauncherSnapshot Load(string? acceptedRemoteRoot, string? cacheRoot, string builtInRoot, Func<SnapshotSource, string, bool>? authorize = null)
    {
        foreach ((string? root, SnapshotSource source) in new[] { (acceptedRemoteRoot, SnapshotSource.Remote), (cacheRoot, SnapshotSource.Cache), (builtInRoot, SnapshotSource.BuiltIn) })
        {
            if (string.IsNullOrWhiteSpace(root)) continue;
            try
            {
                string fullRoot = Path.GetFullPath(root);
                if (source != SnapshotSource.BuiltIn && (authorize is null || !authorize(source, fullRoot))) continue;
                string jsonPath = Path.Combine(fullRoot, "launcher-snapshot.json");
                if (!File.Exists(jsonPath)) continue;
                LauncherSnapshot snapshot = JsonSerializer.Deserialize(File.ReadAllText(jsonPath), LauncherSnapshotJsonContext.Default.LauncherSnapshot) ?? throw new InvalidDataException("启动器快照为空");
                LauncherSnapshotValidator.Validate(snapshot);
                ValidateReferencedAssets(snapshot, fullRoot);
                return new LoadedLauncherSnapshot(snapshot, fullRoot, source);
            }
            catch (Exception ex) when (source != SnapshotSource.BuiltIn && ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
            {
                // 远程或缓存版本必须完整有效，否则继续回退。
            }
        }
        throw new InvalidDataException("内置启动器快照缺失或损坏");
    }

    private static void ValidateReferencedAssets(LauncherSnapshot snapshot, string root)
    {
        foreach (string relative in new[] { snapshot.Theme.BackgroundImage, snapshot.Theme.LaunchButtonImage, snapshot.Theme.LaunchButtonHoverImage, snapshot.Theme.LaunchButtonPressedImage, snapshot.Theme.LaunchButtonDisabledImage }.Concat(snapshot.Theme.Controls.Select(x => x.BackgroundImage)).Concat(snapshot.Announcements.Select(x => x.Image)))
        {
            string path = LauncherSnapshotValidator.ResolveAsset(root, relative);
            if (!string.IsNullOrEmpty(path) && (!File.Exists(path) || new FileInfo(path).Length > 16 * 1024 * 1024)) throw new InvalidDataException("主题图片缺失或超过 16 MiB");
        }
    }
}

public static class LauncherReleaseAuthorization
{
    public static bool IsAuthorized(
        string releaseRoot,
        string signatureStatePath,
        IReadOnlyDictionary<string, BootstrapManifestTrustedKey>? trustedKeys = null,
        Version? clientVersion = null)
    {
        try
        {
            string descriptorPath = Path.Combine(Path.GetFullPath(releaseRoot), "launcher-release.json");
            if (!File.Exists(descriptorPath) || new FileInfo(descriptorPath).Length > 1024 * 1024) return false;
            LauncherReleaseDescriptor descriptor = JsonSerializer.Deserialize(File.ReadAllText(descriptorPath), LauncherSnapshotJsonContext.Default.LauncherReleaseDescriptor) ?? throw new InvalidDataException();
            if (string.IsNullOrWhiteSpace(descriptor.ResourceVersion) || descriptor.Files is null || descriptor.Files.Count is < 1 or > 256) return false;
            var packages = new List<BootstrapManifestAuthorizedPackage>();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (LauncherReleaseFile file in descriptor.Files)
            {
                if (file is null || Path.GetFileName(file.Name) != file.Name || !names.Add(file.Name)) return false;
                BootstrapSignedPackageHashPolicy.VerifyFile(Path.Combine(releaseRoot, file.Name), file.Sha256);
                packages.Add(new BootstrapManifestAuthorizedPackage { Name = file.Name, Sha256 = file.Sha256 });
            }
            if (!names.Contains("launcher-snapshot.json")) return false;
            return BootstrapManifestAcceptanceStore.IsAuthorizedUpdateQueue(signatureStatePath, descriptor.ResourceVersion, packages, trustedKeys, clientVersion);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException) { return false; }
    }
}

public static class ClientLocator
{
    public static IReadOnlyList<string> Find(
        string entryFileName,
        IEnumerable<string> roots,
        int maximumDepth = 3,
        Func<string, bool>? candidateFilter = null,
        TimeSpan? timeBudget = null)
    {
        if (string.IsNullOrWhiteSpace(entryFileName) || Path.GetFileName(entryFileName) != entryFileName) throw new ArgumentException("客户端入口文件名无效", nameof(entryFileName));
        var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        TimeSpan budget = timeBudget ?? TimeSpan.FromSeconds(5);
        int inspectedDirectories = 0;
        foreach (string root in roots.Where(Directory.Exists))
        {
            int inspectedInRoot = 0;
            var queue = new Queue<(string Path, int Depth)>();
            queue.Enqueue((Path.GetFullPath(root), 0));
            while (queue.Count > 0 && results.Count < 32)
            {
                if (clock.Elapsed >= budget) return results.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
                if (++inspectedDirectories > 20_000 || ++inspectedInRoot > 2_500) break;
                (string current, int depth) = queue.Dequeue();
                try
                {
                    if (File.Exists(Path.Combine(current, entryFileName)) && (candidateFilter is null || candidateFilter(current))) results.Add(current);
                    if (depth >= maximumDepth) continue;
                    foreach (string child in Directory.EnumerateDirectories(current))
                    {
                        if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0) queue.Enqueue((child, depth + 1));
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
        return results.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
    }
}

public sealed class MicroEndpointSession
{
    private readonly MicroEndpoint _configured;
    private bool _usingBackup;
    public MicroEndpointSession(MicroEndpoint configured) { LauncherSnapshotValidator.ValidateMicro(configured); _configured = configured; }
    public (string Address, int Port) Current => _usingBackup ? (_configured.BackupAddress, _configured.BackupPort) : (_configured.Address, _configured.Port);
    public bool TryFailOver()
    {
        if (_usingBackup || string.IsNullOrWhiteSpace(_configured.BackupAddress) || _configured.BackupPort == 0) return false;
        _usingBackup = true;
        return true;
    }
}

public sealed class ConsecutiveFailureFailover
{
    private readonly int _threshold;
    private int _failures;
    private int _usingBackup;
    public ConsecutiveFailureFailover(int threshold = 3) => _threshold = threshold is >= 1 and <= 20 ? threshold : throw new ArgumentOutOfRangeException(nameof(threshold));
    public bool UsingBackup => Volatile.Read(ref _usingBackup) != 0;
    public int FailureCount => Volatile.Read(ref _failures);
    public bool RegisterFailure(bool backupAvailable)
    {
        if (UsingBackup) return false;
        int failures = Interlocked.Increment(ref _failures);
        return backupAvailable && failures >= _threshold && Interlocked.CompareExchange(ref _usingBackup, 1, 0) == 0;
    }
    public void RegisterSuccess()
    {
        if (!UsingBackup) Interlocked.Exchange(ref _failures, 0);
    }
}

public static class ServerConnectivityDiagnostic
{
    public static async Task<TimeSpan?> ProbeAsync(string host, int port, CancellationToken cancellationToken)
    {
        var start = System.Diagnostics.Stopwatch.StartNew();
        using var client = new TcpClient();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        try { await client.ConnectAsync(host, port, timeout.Token).ConfigureAwait(false); return start.Elapsed; }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException) { return null; }
    }
}
