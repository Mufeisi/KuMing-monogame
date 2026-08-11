using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Shared.Security;

namespace Launcher.ThemeRuntime;

/// <summary>下载完整不可变启动器版本；验签和逐文件校验全部通过后才切换当前版本指针。</summary>
public static class LauncherReleaseUpdater
{
    private const string ManifestName = "bootstrap-manifest.json";
    private const string DescriptorName = "launcher-release.json";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    public static async Task<bool> TryRefreshAsync(
        string baseUrl,
        string acceptedStore,
        string lastKnownGoodStore,
        string signatureStatePath,
        CancellationToken cancellationToken,
        HttpClient? httpClient = null,
        IReadOnlyDictionary<string, BootstrapManifestTrustedKey>? trustedKeys = null,
        Version? clientVersion = null,
        Action<string>? diagnostic = null)
    {
        try
        {
            Uri root = RequireBaseUri(baseUrl);
            HttpClient client = httpClient ?? Http;
            string manifestJson = Encoding.UTF8.GetString(await DownloadBytesAsync(client, new Uri(root, ManifestName), BootstrapManifestSignaturePolicy.MaximumJsonBytes, cancellationToken).ConfigureAwait(false));
            IReadOnlyDictionary<string, BootstrapManifestTrustedKey> keys = trustedKeys ?? BootstrapManifestTrustConfiguration.TrustedKeys;
            Version version = clientVersion ?? BootstrapManifestTrustConfiguration.CurrentClientCompatibilityVersion;
            BootstrapManifestVerificationResult signature = BootstrapManifestSignaturePolicy.Verify(manifestJson, keys, version);
            if (!signature.IsValid) throw new InvalidDataException(signature.Error);

            BootstrapSignedPackage descriptorPackage = FindPackage(signature.Manifest, DescriptorName);
            string staging = CreateStaging(acceptedStore);
            try
            {
                string descriptorPath = Path.Combine(staging, DescriptorName);
                await DownloadPackageAsync(client, root, descriptorPackage, descriptorPath, cancellationToken).ConfigureAwait(false);
                LauncherReleaseDescriptor descriptor = JsonSerializer.Deserialize(
                    await File.ReadAllTextAsync(descriptorPath, cancellationToken).ConfigureAwait(false),
                    LauncherSnapshotJsonContext.Default.LauncherReleaseDescriptor) ?? throw new InvalidDataException("启动器发布描述为空");
                if (!string.Equals(descriptor.ResourceVersion, signature.Manifest.ResourceVersion, StringComparison.Ordinal))
                    throw new InvalidDataException("启动器发布版本与签名索引不一致");
                if (descriptor.Files is null || descriptor.Files.Count is < 1 or > 256) throw new InvalidDataException("启动器发布文件列表无效");
                foreach (LauncherReleaseFile file in descriptor.Files)
                {
                    if (file is null || Path.GetFileName(file.Name) != file.Name) throw new InvalidDataException("启动器发布文件名无效");
                    BootstrapSignedPackage package = FindPackage(signature.Manifest, file.Name);
                    if (!string.Equals(package.Sha256, file.Sha256, StringComparison.Ordinal)) throw new InvalidDataException("发布描述与签名索引摘要不一致");
                    await DownloadPackageAsync(client, root, package, Path.Combine(staging, file.Name), cancellationToken).ConfigureAwait(false);
                }

                string versionName = BuildVersionName(signature.Manifest, manifestJson);
                string acceptedVersion = PromoteVersion(staging, acceptedStore, versionName);
                string lkgVersion = CopyVersion(acceptedVersion, lastKnownGoodStore, versionName);
                BootstrapManifestAcceptanceStore.VerifyAndAccept(manifestJson, signatureStatePath, keys, version);
                if (!LauncherReleaseAuthorization.IsAuthorized(acceptedVersion, signatureStatePath, keys, version) ||
                    !LauncherReleaseAuthorization.IsAuthorized(lkgVersion, signatureStatePath, keys, version))
                    throw new InvalidDataException("完整启动器版本未通过授权校验");
                WritePointer(acceptedStore, versionName);
                WritePointer(lastKnownGoodStore, versionName);
                return Directory.Exists(acceptedVersion) && Directory.Exists(lkgVersion);
            }
            finally { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException or JsonException or InvalidDataException or ArgumentException)
        {
            diagnostic?.Invoke(ex.Message);
            return false;
        }
    }

    public static string? ResolveCurrentRoot(
        string store,
        string? signatureStatePath = null,
        IReadOnlyDictionary<string, BootstrapManifestTrustedKey>? trustedKeys = null,
        Version? clientVersion = null)
    {
        try
        {
            string fullStore = Path.GetFullPath(store);
            string versions = Path.GetFullPath(Path.Combine(fullStore, "versions"));
            var candidates = new List<string>();
            string pointer = Path.Combine(fullStore, "current.txt");
            if (File.Exists(pointer) && new FileInfo(pointer).Length <= 256)
            {
                string name = File.ReadAllText(pointer).Trim();
                if (IsVersionName(name)) candidates.Add(Path.Combine(versions, name));
            }
            if (Directory.Exists(versions))
                candidates.AddRange(Directory.EnumerateDirectories(versions).OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase));
            foreach (string candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string path = Path.GetFullPath(candidate);
                if (!path.StartsWith(versions + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(path)) continue;
                if (string.IsNullOrWhiteSpace(signatureStatePath) || LauncherReleaseAuthorization.IsAuthorized(path, signatureStatePath, trustedKeys, clientVersion)) return path;
            }
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return null; }
    }

    private static bool IsVersionName(string name) => name.Length is >= 3 and <= 96 && name.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

    private static Uri RequireBaseUri(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) || uri.Scheme is not ("http" or "https")) throw new ArgumentException("远程发布地址无效", nameof(value));
        return new Uri(uri.AbsoluteUri.EndsWith('/') ? uri.AbsoluteUri : uri.AbsoluteUri + "/");
    }

    private static async Task<byte[]> DownloadBytesAsync(HttpClient client, Uri uri, long maximumBytes, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is long length && length > maximumBytes) throw new InvalidDataException("远程启动器文件超过大小上限");
        using Stream input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var output = new MemoryStream();
        byte[] buffer = new byte[64 * 1024];
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (output.Length + read > maximumBytes) throw new InvalidDataException("远程启动器文件超过大小上限");
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
        return output.ToArray();
    }

    private static BootstrapSignedPackage FindPackage(BootstrapSignedManifest manifest, string name)
    {
        BootstrapSignedPackage[] matches = manifest.Packages.Where(package => string.Equals(package.Name, name, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length != 1 || Path.GetFileName(matches[0].Name) != matches[0].Name || matches[0].Size < 0 || matches[0].Size > 16L * 1024 * 1024)
            throw new InvalidDataException("签名索引缺少有效启动器文件：" + name);
        return matches[0];
    }

    private static async Task DownloadPackageAsync(HttpClient client, Uri root, BootstrapSignedPackage package, string outputPath, CancellationToken cancellationToken)
    {
        byte[] bytes = await DownloadBytesAsync(client, new Uri(root, Uri.EscapeDataString(package.Name)), Math.Max(1, package.Size), cancellationToken).ConfigureAwait(false);
        if (bytes.LongLength != package.Size) throw new InvalidDataException("远程启动器文件长度不符：" + package.Name);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        await File.WriteAllBytesAsync(outputPath, bytes, cancellationToken).ConfigureAwait(false);
        BootstrapSignedPackageHashPolicy.VerifyFile(outputPath, package.Sha256);
    }

    private static string CreateStaging(string store)
    {
        string path = Path.Combine(Path.GetFullPath(store), ".staging-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string BuildVersionName(BootstrapSignedManifest manifest, string json)
    {
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant()[..16];
        return manifest.Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture) + "-" + hash;
    }

    private static string PromoteVersion(string staging, string store, string versionName)
    {
        string versions = Path.Combine(Path.GetFullPath(store), "versions");
        Directory.CreateDirectory(versions);
        string destination = Path.Combine(versions, versionName);
        if (Directory.Exists(destination)) return destination;
        Directory.Move(staging, destination);
        return destination;
    }

    private static string CopyVersion(string source, string store, string versionName)
    {
        string versions = Path.Combine(Path.GetFullPath(store), "versions");
        Directory.CreateDirectory(versions);
        string destination = Path.Combine(versions, versionName);
        if (Directory.Exists(destination)) return destination;
        string temporary = Path.Combine(versions, ".copying-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            foreach (string file in Directory.EnumerateFiles(source)) File.Copy(file, Path.Combine(temporary, Path.GetFileName(file)), overwrite: false);
            Directory.Move(temporary, destination);
            return destination;
        }
        finally { if (Directory.Exists(temporary)) Directory.Delete(temporary, recursive: true); }
    }

    private static void WritePointer(string store, string versionName)
    {
        Directory.CreateDirectory(store);
        string path = Path.Combine(store, "current.txt");
        string temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        File.WriteAllText(temporary, versionName, new UTF8Encoding(false));
        File.Move(temporary, path, overwrite: true);
    }
}
