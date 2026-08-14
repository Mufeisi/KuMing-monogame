#nullable enable

using System.Globalization;
using System.Text;
using Shared.Security;

namespace Shared.CustomGui;

public sealed class CustomGuiAcceptedReleaseStoreRequest
{
    public string? StoreRoot { get; set; }
    public string? AcceptanceStatePath { get; set; }
    public IReadOnlyDictionary<string, BootstrapManifestTrustedKey>? TrustedKeys { get; set; }
    public Version? CurrentClientVersion { get; set; }
}

/// <summary>
/// 将 Bootstrap 已验签的核心清单包与 GUI 包组成一个不可见的原子激活版本。
/// 单包下载只进入 pending；两包均校验通过后才切换 current 指针，旧版本始终可继续加载。
/// </summary>
public static class CustomGuiAcceptedReleaseStore
{
    public const string GuiPackageName = "custom-gui";
    public const string ResourcePackageName = "core-startup";
    private const string SignedIndexFileName = "bootstrap-package-index.signed.json";
    private static readonly object Gate = new();
    private static readonly UTF8Encoding Utf8NoBom = new(false, true);

    public static bool StagePackage(CustomGuiAcceptedReleaseStoreRequest request, string packageName, string sourcePackagePath)
    {
        ArgumentNullException.ThrowIfNull(request);
        string name = NormalizePackageName(packageName);
        string root = NormalizeRoot(request.StoreRoot);
        string source = Path.GetFullPath(sourcePackagePath ?? string.Empty);
        if (!File.Exists(source)) throw new FileNotFoundException("待激活 GUI 资源包不存在", source);
        RejectReparseChain(source);

        lock (Gate)
        {
            IReadOnlyDictionary<string, BootstrapManifestTrustedKey> keys = request.TrustedKeys
                ?? BootstrapManifestTrustConfiguration.TrustedKeys;
            Version clientVersion = request.CurrentClientVersion
                ?? BootstrapManifestTrustConfiguration.CurrentClientCompatibilityVersion;
            string manifestJson = BootstrapManifestAcceptanceStore.ReadAcceptedManifestJson(
                request.AcceptanceStatePath ?? string.Empty, keys, clientVersion);
            BootstrapManifestVerificationResult verified = BootstrapManifestSignaturePolicy.Verify(manifestJson, keys, clientVersion);
            if (!verified.IsValid) throw new InvalidDataException("已接受清单复核失败：" + verified.Error);
            BootstrapSignedPackage signed = verified.Manifest.Packages.SingleOrDefault(
                item => string.Equals(item.Name, name, StringComparison.Ordinal))
                ?? throw new InvalidDataException("已接受清单未登记 GUI 激活包：" + name);
            if (new FileInfo(source).Length != signed.Size)
                throw new InvalidDataException("GUI 激活包大小与已接受清单不一致：" + name);
            BootstrapSignedPackageHashPolicy.VerifyFile(source, signed.Sha256);

            Directory.CreateDirectory(root);
            RejectReparseChain(root);
            string pendingRoot = Contained(root, Path.Combine("pending", verified.Manifest.Sequence.ToString(CultureInfo.InvariantCulture)));
            Directory.CreateDirectory(pendingRoot);
            RejectReparseChain(pendingRoot);
            CopyAtomic(source, Contained(pendingRoot, name + ".zip"));
            WriteTextAtomic(Contained(pendingRoot, SignedIndexFileName), manifestJson);

            string gui = Contained(pendingRoot, GuiPackageName + ".zip");
            string resource = Contained(pendingRoot, ResourcePackageName + ".zip");
            if (!File.Exists(gui) || !File.Exists(resource)) return false;

            CustomGuiAcceptedPackage accepted = LoadDirectory(pendingRoot, keys, clientVersion);
            if (accepted.Sequence != verified.Manifest.Sequence)
                throw new InvalidDataException("GUI 激活版本与已接受清单序列不一致");
            EnsureExactFiles(pendingRoot);

            string versionsRoot = Contained(root, "versions");
            Directory.CreateDirectory(versionsRoot);
            string versionName = verified.Manifest.Sequence.ToString(CultureInfo.InvariantCulture);
            string destination = Contained(versionsRoot, versionName);
            if (Directory.Exists(destination))
            {
                CustomGuiAcceptedPackage existing = LoadDirectory(destination, keys, clientVersion);
                if (!string.Equals(existing.PackageSha256, accepted.PackageSha256, StringComparison.Ordinal))
                    throw new InvalidDataException("同序列 GUI 激活目录内容不一致");
                Directory.Delete(pendingRoot, recursive: true);
            }
            else
            {
                Directory.Move(pendingRoot, destination);
            }
            WriteTextAtomic(Contained(root, "current.txt"), versionName + "\n");
            return true;
        }
    }

    public static CustomGuiAcceptedPackage? TryLoadCurrent(CustomGuiAcceptedReleaseStoreRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        string root = NormalizeRoot(request.StoreRoot);
        string pointer = Contained(root, "current.txt");
        if (!File.Exists(pointer)) return null;
        lock (Gate)
        {
            RejectReparseChain(pointer);
            if (new FileInfo(pointer).Length is <= 0 or > 32) throw new InvalidDataException("GUI 当前版本指针无效");
            string version = File.ReadAllText(pointer, Utf8NoBom).Trim();
            if (version.Length == 0 || version.Any(character => !char.IsAsciiDigit(character)))
                throw new InvalidDataException("GUI 当前版本指针格式无效");
            string directory = Contained(root, Path.Combine("versions", version));
            RejectReparseChain(directory);
            return LoadDirectory(
                directory,
                request.TrustedKeys ?? BootstrapManifestTrustConfiguration.TrustedKeys,
                request.CurrentClientVersion ?? BootstrapManifestTrustConfiguration.CurrentClientCompatibilityVersion);
        }
    }

    public static bool HasCurrentPackageSha256(string storeRoot, string packageName, string expectedSha256)
    {
        string root = NormalizeRoot(storeRoot);
        string pointer = Contained(root, "current.txt");
        if (!File.Exists(pointer) || string.IsNullOrWhiteSpace(expectedSha256)) return false;
        try
        {
            string version = File.ReadAllText(pointer, Utf8NoBom).Trim();
            if (version.Length == 0 || version.Any(character => !char.IsAsciiDigit(character))) return false;
            string package = Contained(root, Path.Combine("versions", version, NormalizePackageName(packageName) + ".zip"));
            if (!File.Exists(package)) return false;
            BootstrapSignedPackageHashPolicy.VerifyFile(package, expectedSha256);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return false;
        }
    }

    private static CustomGuiAcceptedPackage LoadDirectory(
        string directory,
        IReadOnlyDictionary<string, BootstrapManifestTrustedKey> keys,
        Version clientVersion) => CustomGuiSignedReleaseLoader.Load(new CustomGuiSignedReleaseRequest
    {
        PackagesRoot = directory,
        TrustedKeys = keys,
        CurrentClientVersion = clientVersion,
    });

    private static string NormalizePackageName(string value)
    {
        string name = (value ?? string.Empty).Trim();
        if (name is not (GuiPackageName or ResourcePackageName))
            throw new ArgumentException("GUI 激活存储只接受 core-startup 与 custom-gui", nameof(value));
        return name;
    }

    private static string NormalizeRoot(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("GUI 激活存储根目录不能为空", nameof(value));
        return Path.GetFullPath(value).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static string Contained(string root, string relative)
    {
        string path = Path.GetFullPath(Path.Combine(root, relative));
        string prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("GUI 激活路径越出存储根目录");
        return path;
    }

    private static void CopyAtomic(string source, string target)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        string temporary = target + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.WriteThrough))
            {
                input.CopyTo(output);
                output.Flush(flushToDisk: true);
            }
            File.Move(temporary, target, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static void WriteTextAtomic(string target, string value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        string temporary = target + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            using (var writer = new StreamWriter(output, Utf8NoBom))
            {
                writer.Write(value);
                writer.Flush();
                output.Flush(flushToDisk: true);
            }
            File.Move(temporary, target, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static void EnsureExactFiles(string directory)
    {
        if (Directory.EnumerateDirectories(directory).Any()) throw new InvalidDataException("GUI 待激活目录包含未知子目录");
        var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            SignedIndexFileName, GuiPackageName + ".zip", ResourcePackageName + ".zip"
        };
        if (Directory.EnumerateFiles(directory).Select(Path.GetFileName).Any(name => name is null || !expected.Contains(name)))
            throw new InvalidDataException("GUI 待激活目录包含未知文件");
    }

    private static void RejectReparseChain(string path)
    {
        string full = Path.GetFullPath(path);
        string current = Path.GetPathRoot(full) ?? string.Empty;
        foreach (string part in full[current.Length..].Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (part.Length == 0) continue;
            current = Path.Combine(current, part);
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("GUI 激活路径不得经过重解析点");
        }
    }
}
