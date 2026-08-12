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
            string root = Path.GetFullPath(releaseRoot);
            string manifestPath = Path.Combine(root, LauncherReleaseVersionValidator.ManifestName);
            if (!File.Exists(manifestPath) || new FileInfo(manifestPath).Length > BootstrapManifestSignaturePolicy.MaximumJsonBytes) return false;
            string manifestJson = File.ReadAllText(manifestPath);
            BootstrapSignedManifest manifest = BootstrapManifestAcceptanceStore.VerifyForAcceptance(
                manifestJson,
                signatureStatePath,
                trustedKeys,
                clientVersion);
            LauncherReleaseVersionValidator.Validate(root, manifest);
            return BootstrapManifestAcceptanceStore.IsAcceptedManifest(
                signatureStatePath,
                manifestJson,
                trustedKeys,
                clientVersion);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException) { return false; }
    }
}

internal static class LauncherReleaseVersionValidator
{
    internal const string ManifestName = "bootstrap-manifest.json";
    internal const string DescriptorName = "launcher-release.json";
    private const string PlayerDescriptorName = "player-update.json";
    private const string PlayerEntryName = "player-entry.exe";

    internal static void Validate(string releaseRoot, BootstrapSignedManifest manifest)
    {
        string root = Path.GetFullPath(releaseRoot);
        if (!Directory.Exists(root) || (File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("启动器版本目录无效");
        if (Directory.EnumerateDirectories(root).Any()) throw new InvalidDataException("启动器版本包含未登记子目录");

        var signed = new Dictionary<string, BootstrapSignedPackage>(StringComparer.OrdinalIgnoreCase);
        foreach (BootstrapSignedPackage package in manifest.Packages)
        {
            if (package is null || Path.GetFileName(package.Name) != package.Name || !signed.TryAdd(package.Name, package) || package.Size < 0)
                throw new InvalidDataException("启动器签名索引文件无效");
        }
        if (!signed.TryGetValue(DescriptorName, out BootstrapSignedPackage? descriptorPackage))
            throw new InvalidDataException("启动器签名索引缺少发布描述");
        bool hasPlayerDescriptor = signed.ContainsKey(PlayerDescriptorName);
        bool hasPlayerEntry = signed.ContainsKey(PlayerEntryName);
        if (hasPlayerDescriptor != hasPlayerEntry) throw new InvalidDataException("玩家入口更新文件不完整");

        string descriptorPath = Path.Combine(root, DescriptorName);
        VerifyPackageFile(descriptorPath, descriptorPackage);
        if (new FileInfo(descriptorPath).Length > 1024 * 1024) throw new InvalidDataException("启动器发布描述超过大小上限");
        LauncherReleaseDescriptor descriptor = JsonSerializer.Deserialize(
            File.ReadAllText(descriptorPath),
            LauncherSnapshotJsonContext.Default.LauncherReleaseDescriptor) ?? throw new InvalidDataException("启动器发布描述为空");
        if (!string.Equals(descriptor.ResourceVersion, manifest.ResourceVersion, StringComparison.Ordinal) ||
            descriptor.Files is null || descriptor.Files.Count is < 1 or > 256)
            throw new InvalidDataException("启动器发布描述版本或文件列表无效");

        var described = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (LauncherReleaseFile file in descriptor.Files)
        {
            if (file is null || Path.GetFileName(file.Name) != file.Name || !described.Add(file.Name) ||
                !signed.TryGetValue(file.Name, out BootstrapSignedPackage? package) ||
                !string.Equals(package.Sha256, file.Sha256, StringComparison.Ordinal))
                throw new InvalidDataException("启动器发布描述与签名索引不一致");
        }
        if (!described.Contains("launcher-snapshot.json")) throw new InvalidDataException("启动器发布描述缺少快照");
        var expectedDescribed = signed.Keys
            .Where(name => !name.Equals(DescriptorName, StringComparison.OrdinalIgnoreCase) &&
                           !name.Equals(PlayerDescriptorName, StringComparison.OrdinalIgnoreCase) &&
                           !name.Equals(PlayerEntryName, StringComparison.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!described.SetEquals(expectedDescribed)) throw new InvalidDataException("启动器发布描述未精确覆盖签名资源");

        var expectedFiles = described.Append(DescriptorName).Append(ManifestName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        string[] actualFiles = Directory.EnumerateFiles(root).Select(Path.GetFileName).OfType<string>().ToArray();
        if (!expectedFiles.SetEquals(actualFiles)) throw new InvalidDataException("启动器版本包含缺失或未登记文件");
        foreach (string name in described.Append(DescriptorName)) VerifyPackageFile(Path.Combine(root, name), signed[name]);
    }

    private static void VerifyPackageFile(string path, BootstrapSignedPackage package)
    {
        if (!File.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0 || new FileInfo(path).Length != package.Size)
            throw new InvalidDataException("启动器版本文件缺失或长度不符：" + package.Name);
        BootstrapSignedPackageHashPolicy.VerifyFile(path, package.Sha256);
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

public static class MicroGatewayReadiness
{
    public static async Task<bool> ProbeAsync(MicroEndpoint endpoint, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (!endpoint.Enabled) return true;
        foreach ((string address, int port) in Endpoints(endpoint))
        {
            if (string.IsNullOrWhiteSpace(address) || port is < 1 or > 65535) continue;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            try
            {
                using var client = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(3) };
                using var request = new HttpRequestMessage(HttpMethod.Get, new UriBuilder(Uri.UriSchemeHttp, address, port, "api/health").Uri);
                using HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
                if (response.IsSuccessStatusCode) return true;
            }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or UriFormatException) { }
        }
        return false;
    }

    public static async Task<bool> EnsureCoreLibrariesAsync(
        MicroEndpoint endpoint,
        string projectId,
        string clientDirectory,
        IEnumerable<LauncherCoreResource> resources,
        IProgress<LauncherProgressState>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        LauncherSnapshotValidator.ValidateProjectId(projectId);
        string clientRoot = Path.GetFullPath(clientDirectory);
        if (!Directory.Exists(clientRoot)) throw new DirectoryNotFoundException("客户端目录不存在");
        RejectReparseChain(clientRoot, clientRoot);
        string code = Shared.Security.ProtectedClientSecretStore.ReadMicroCode(projectId);
        LauncherCoreResource[] files = resources.Where(value => value is not null).ToArray();
        if (!endpoint.Enabled) return true;
        if (files.Length != 3 || !files.Select(item => item.Path).ToHashSet(StringComparer.OrdinalIgnoreCase)
            .SetEquals(new[] { "Data/Title.Lib", "Data/ChrSel.Lib", "Data/Prguse.Lib" })) return false;
        if (files.All(resource => IsValidLocalResource(clientRoot, resource))) return true;
        for (int endpointIndex = 0; endpointIndex < 2; endpointIndex++)
        {
            (string address, int port) = endpointIndex == 0 ? (endpoint.Address, endpoint.Port) : (endpoint.BackupAddress, endpoint.BackupPort);
            if (string.IsNullOrWhiteSpace(address) || port is < 1 or > 65535) continue;
            try
            {
                using var client = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromMinutes(2) };
                for (int index = 0; index < files.Length; index++)
                {
                    LauncherCoreResource resource = files[index];
                    string relative = resource.Path.Replace('\\', '/').TrimStart('/');
                    string target = Path.GetFullPath(Path.Combine(clientRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
                    if (!target.StartsWith(clientRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("登录资源路径越界");
                    if (File.Exists(target) && new FileInfo(target).Length == resource.Size && HashMatches(target, resource.Sha256)) continue;
                    string directory = Path.GetDirectoryName(target)!;
                    RejectReparseChain(clientRoot, Path.GetDirectoryName(directory) ?? clientRoot);
                    Directory.CreateDirectory(directory);
                    RejectReparseChain(clientRoot, directory);
                    progress?.Report(new LauncherProgressState($"正在下载登录核心资源（{index + 1}/{files.Length}）", relative, 0, 0, index, files.Length, 0));
                    using var request = new HttpRequestMessage(HttpMethod.Get, new UriBuilder(Uri.UriSchemeHttp, address, port, "api/file/" + relative).Uri);
                    request.Headers.TryAddWithoutValidation("User", endpoint.User);
                    request.Headers.TryAddWithoutValidation("Code", code);
                    using HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode) throw new HttpRequestException($"登录资源不可用：{relative}（HTTP {(int)response.StatusCode}）");
                    long expected = response.Content.Headers.ContentLength ?? 0;
                    if (expected != resource.Size) throw new InvalidDataException("登录核心资源大小与受信清单不一致：" + relative);
                    string temporary = target + ".downloading-" + Guid.NewGuid().ToString("N");
                    try
                    {
                        await using Stream input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                        long received;
                        {
                            await using var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
                            byte[] buffer = new byte[256 * 1024];
                            received = 0;
                            int read;
                            while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                            {
                                received += read;
                                if (received > expected || received > 64L * 1024 * 1024) throw new InvalidDataException("登录核心资源长度超出声明：" + relative);
                                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                                progress?.Report(new LauncherProgressState($"正在下载登录核心资源（{index + 1}/{files.Length}）", relative, received, expected, index * expected + received, files.Length * expected, 0));
                            }
                            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                        }
                        if (received != expected) throw new InvalidDataException("登录核心资源下载不完整：" + relative);
                        if (!HashMatches(temporary, resource.Sha256)) throw new InvalidDataException("登录核心资源摘要与受信清单不一致：" + relative);
                        RejectReparseChain(clientRoot, directory);
                        if ((File.GetAttributes(temporary) & FileAttributes.ReparsePoint) != 0 || !string.Equals(Path.GetDirectoryName(Path.GetFullPath(temporary)), directory, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidDataException("登录核心资源临时文件路径无效");
                        if (File.Exists(target) && (File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0)
                            throw new InvalidDataException("登录核心资源目标文件不得为重解析点");
                        File.Move(temporary, target, overwrite: true);
                    }
                    finally { try { File.Delete(temporary); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { } }
                }
                return true;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or InvalidDataException or OperationCanceledException or UriFormatException)
            {
                if (cancellationToken.IsCancellationRequested) throw;
            }
        }
        return false;
    }

    private static bool HashMatches(string path, string expected)
    {
        try
        {
            using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan);
            return string.Equals(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream)), expected, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    private static bool IsValidLocalResource(string clientRoot, LauncherCoreResource resource)
    {
        string relative = resource.Path.Replace('\\', '/').TrimStart('/');
        string target = Path.GetFullPath(Path.Combine(clientRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
        try
        {
            RejectReparseChain(clientRoot, target);
            return target.StartsWith(clientRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                && File.Exists(target) && (File.GetAttributes(target) & FileAttributes.ReparsePoint) == 0
                && new FileInfo(target).Length == resource.Size && HashMatches(target, resource.Sha256);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException) { return false; }
    }

    private static void RejectReparseChain(string root, string path)
    {
        string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        string full = Path.GetFullPath(path);
        if (!full.Equals(fullRoot, StringComparison.OrdinalIgnoreCase) && !full.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("客户端资源路径越界");
        string current = Path.GetPathRoot(full) ?? string.Empty;
        foreach (string part in full[current.Length..].Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (part.Length == 0) continue;
            current = Path.Combine(current, part);
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("客户端资源路径不得经过重解析点");
        }
    }

    private static IEnumerable<(string Address, int Port)> Endpoints(MicroEndpoint endpoint)
    {
        yield return (endpoint.Address, endpoint.Port);
        if (!string.IsNullOrWhiteSpace(endpoint.BackupAddress) && endpoint.BackupPort > 0) yield return (endpoint.BackupAddress, endpoint.BackupPort);
    }
}
