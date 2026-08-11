using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Launcher.PlayerShell;
using Shared.Security;

namespace Launcher.ThemeRuntime;

public sealed record PlayerEntryUpdatePlan(
    Uri PublishedRoot,
    string SignedManifestJson,
    byte[] DescriptorBytes,
    PlayerUpdateDescriptor Descriptor,
    BootstrapSignedPackage Package);

/// <summary>只把已签名的新入口写到当前 EXE 同目录并登记替换日志；实际替换由下次启动的 Native AOT 外壳完成。</summary>
public static class PlayerEntryUpdateService
{
    private const string ManifestName = "bootstrap-manifest.json";
    private const string DescriptorName = "player-update.json";

    public static async Task<PlayerEntryUpdatePlan?> InspectAsync(
        string baseUrl,
        string currentExecutable,
        string acceptedStatePath,
        IReadOnlyDictionary<string, BootstrapManifestTrustedKey> trustedKeys,
        CancellationToken cancellationToken,
        HttpClient? httpClient = null)
    {
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(currentExecutable)) return null;
        HttpClient client = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        Uri root = await LauncherReleaseUpdater.ResolvePublishedRootAsync(client, LauncherReleaseUpdater.RequireBaseUri(baseUrl), cancellationToken).ConfigureAwait(false);
        string manifestJson = Encoding.UTF8.GetString(await LauncherReleaseUpdater.DownloadBytesAsync(client, new Uri(root, ManifestName), BootstrapManifestSignaturePolicy.MaximumJsonBytes, cancellationToken).ConfigureAwait(false));
        BootstrapSignedManifest manifest = BootstrapManifestAcceptanceStore.VerifyForAcceptance(manifestJson, acceptedStatePath, trustedKeys, BootstrapManifestTrustConfiguration.CurrentClientCompatibilityVersion);
        BootstrapSignedPackage descriptorPackage = RequirePackage(manifest, DescriptorName, 64 * 1024);
        byte[] descriptorBytes = await DownloadVerifiedAsync(client, root, descriptorPackage, cancellationToken).ConfigureAwait(false);
        PlayerUpdateDescriptor descriptor = JsonSerializer.Deserialize(descriptorBytes, LauncherSnapshotJsonContext.Default.PlayerUpdateDescriptor) ?? throw new InvalidDataException("玩家入口更新描述为空");
        if (!string.Equals(descriptor.Format, PlayerUpdateDescriptor.CurrentFormat, StringComparison.Ordinal) ||
            !Version.TryParse(descriptor.Version, out Version? offeredVersion) ||
            Path.GetFileName(descriptor.PackageName) != descriptor.PackageName)
            throw new InvalidDataException("玩家入口更新描述无效");
        Version currentVersion = ReadCurrentVersion(currentExecutable);
        if (offeredVersion <= currentVersion) return null;
        BootstrapSignedPackage package = RequirePackage(manifest, descriptor.PackageName, PlayerPayloadPackage.MaximumPlayerExecutableBytes);
        return new PlayerEntryUpdatePlan(root, manifestJson, descriptorBytes, descriptor, package);
    }

    public static void PersistRequiredBarrier(PlayerEntryUpdatePlan plan, string barrierPath)
    {
        if (!plan.Descriptor.Required) return;
        string path = Path.GetFullPath(barrierPath); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(new PlayerRequiredUpdateBarrier
        {
            SignedManifestJson = plan.SignedManifestJson,
            DescriptorBase64 = Convert.ToBase64String(plan.DescriptorBytes),
        }, LauncherSnapshotJsonContext.Default.PlayerRequiredUpdateBarrier);
        string temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try { using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough)) { output.Write(bytes); output.Flush(true); } File.Move(temporary, path, overwrite: true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static bool IsRequiredBarrierActive(string barrierPath, string currentExecutable, IReadOnlyDictionary<string, BootstrapManifestTrustedKey> trustedKeys, out string message, out Version? requiredVersion)
    {
        message = string.Empty; requiredVersion = null; string path = Path.GetFullPath(barrierPath);
        if (!File.Exists(path)) return false;
        try
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0 || new FileInfo(path).Length > BootstrapManifestSignaturePolicy.MaximumJsonBytes) throw new InvalidDataException("必须更新门槛文件无效");
            PlayerRequiredUpdateBarrier barrier = JsonSerializer.Deserialize(File.ReadAllBytes(path), LauncherSnapshotJsonContext.Default.PlayerRequiredUpdateBarrier) ?? throw new InvalidDataException("必须更新门槛为空");
            if (barrier.Format != PlayerRequiredUpdateBarrier.CurrentFormat) throw new InvalidDataException("必须更新门槛格式无效");
            BootstrapManifestVerificationResult verification = BootstrapManifestSignaturePolicy.Verify(barrier.SignedManifestJson, trustedKeys, BootstrapManifestTrustConfiguration.CurrentClientCompatibilityVersion);
            if (!verification.IsValid) throw new InvalidDataException("必须更新门槛签名无效");
            byte[] descriptorBytes = Convert.FromBase64String(barrier.DescriptorBase64);
            BootstrapSignedPackage descriptorPackage = RequirePackage(verification.Manifest, DescriptorName, 64 * 1024);
            if (descriptorBytes.LongLength != descriptorPackage.Size || !string.Equals(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(descriptorBytes)), descriptorPackage.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("必须更新描述摘要无效");
            PlayerUpdateDescriptor descriptor = JsonSerializer.Deserialize(descriptorBytes, LauncherSnapshotJsonContext.Default.PlayerUpdateDescriptor) ?? throw new InvalidDataException("必须更新描述为空");
            if (!descriptor.Required || !Version.TryParse(descriptor.Version, out requiredVersion)) throw new InvalidDataException("必须更新描述无效");
            if (ReadCurrentVersion(currentExecutable) >= requiredVersion) { try { File.Delete(path); } catch { } return false; }
            message = "已验签的必须更新 " + requiredVersion + " 尚未安装，离线状态也不能绕过。"; return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or FormatException or CryptographicException)
        {
            requiredVersion = null; message = "必须更新门槛损坏，拒绝在无法确认兼容性时进入游戏：" + ex.Message; return true;
        }
    }

    public static async Task StageAsync(
        PlayerEntryUpdatePlan plan,
        string currentExecutable,
        string acceptedStatePath,
        IReadOnlyDictionary<string, BootstrapManifestTrustedKey> trustedKeys,
        CancellationToken cancellationToken,
        HttpClient? httpClient = null)
    {
        string target = Path.GetFullPath(currentExecutable);
        if (!File.Exists(target)) throw new FileNotFoundException("当前玩家入口不存在", target);
        string staged = target + ".new";
        string journal = Path.Combine(Path.GetDirectoryName(target)!, "player-replacement.json");
        HttpClient client = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        string temporary = staged + ".downloading-" + Guid.NewGuid().ToString("N");
        try
        {
            using HttpResponseMessage response = await client.GetAsync(new Uri(plan.PublishedRoot, Uri.EscapeDataString(plan.Package.Name)), HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is long length && length != plan.Package.Size) throw new InvalidDataException("新版玩家入口长度不符");
            await using Stream input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                byte[] buffer = new byte[64 * 1024]; long written = 0; int read;
                while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    written = checked(written + read);
                    if (written > plan.Package.Size || written > PlayerPayloadPackage.MaximumPlayerExecutableBytes) throw new InvalidDataException("新版玩家入口超过签名大小");
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                }
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                if (written != plan.Package.Size) throw new InvalidDataException("新版玩家入口下载不完整");
            }
            BootstrapSignedPackageHashPolicy.VerifyFile(temporary, plan.Package.Sha256);
            File.Move(temporary, staged, overwrite: true);
            PlayerReplacementCoordinator.PreparePending(journal, target, plan.SignedManifestJson, plan.Package.Name, trustedKeys, BootstrapManifestTrustConfiguration.CurrentClientCompatibilityVersion, acceptedStatePath);
        }
        catch
        {
            try { if (File.Exists(staged)) File.Delete(staged); } catch { }
            throw;
        }
        finally { try { if (File.Exists(temporary)) File.Delete(temporary); } catch { } }
    }

    private static async Task<byte[]> DownloadVerifiedAsync(HttpClient client, Uri root, BootstrapSignedPackage package, CancellationToken cancellationToken)
    {
        byte[] bytes = await LauncherReleaseUpdater.DownloadBytesAsync(client, new Uri(root, Uri.EscapeDataString(package.Name)), package.Size, cancellationToken).ConfigureAwait(false);
        if (bytes.LongLength != package.Size) throw new InvalidDataException("签名文件长度不符：" + package.Name);
        string actual = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
        if (!string.Equals(actual, package.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("签名文件摘要不符：" + package.Name);
        return bytes;
    }

    private static BootstrapSignedPackage RequirePackage(BootstrapSignedManifest manifest, string name, long maximumBytes)
    {
        BootstrapSignedPackage[] matches = manifest.Packages.Where(item => string.Equals(item.Name, name, StringComparison.Ordinal)).ToArray();
        if (matches.Length != 1 || Path.GetFileName(matches[0].Name) != matches[0].Name || matches[0].Size < 1 || matches[0].Size > maximumBytes)
            throw new InvalidDataException("签名索引缺少有效玩家入口文件：" + name);
        return matches[0];
    }

    private static Version ReadCurrentVersion(string executable)
    {
        string? value = FileVersionInfo.GetVersionInfo(Path.GetFullPath(executable)).FileVersion;
        if (!Version.TryParse(value, out Version? version)) throw new InvalidDataException("当前玩家入口文件版本无效");
        return version;
    }
}
