#nullable enable

using System.IO.Compression;
using System.Text;
using Shared.Release;
using Shared.Security;

namespace Shared.CustomGui;

public sealed class CustomGuiSignedReleaseRequest
{
    public string? PackagesRoot { get; set; }
    public string SignedIndexFileName { get; set; } = "bootstrap-package-index.signed.json";
    public string GuiPackageName { get; set; } = "custom-gui";
    public string ResourceManifestPackageName { get; set; } = "core-startup";
    public IReadOnlyDictionary<string, BootstrapManifestTrustedKey>? TrustedKeys { get; set; }
    public Version? CurrentClientVersion { get; set; }
    public BootstrapManifestAcceptedState? AcceptedState { get; set; }
    public CustomGuiAcceptedDocumentState? AcceptedDocumentState { get; set; }
}

public static class CustomGuiSignedReleaseLoader
{
    public const string ResourceManifestEntryName = "bootstrap-packages.json";
    private const int MaximumResourceManifestBytes = 2 * 1024 * 1024;

    public static CustomGuiAcceptedPackage Load(CustomGuiSignedReleaseRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        string root = Path.GetFullPath(request.PackagesRoot ?? string.Empty);
        if (!Directory.Exists(root)) throw new CustomGuiValidationException("GUI05-SIGN-001", "GUI 发布包目录不存在");
        string indexPath = ResolveFile(root, request.SignedIndexFileName);
        if (!File.Exists(indexPath) || new FileInfo(indexPath).Length is <= 0 or > BootstrapManifestSignaturePolicy.MaximumJsonBytes)
            throw new CustomGuiValidationException("GUI05-SIGN-001", "GUI 发布签名索引不存在或超过上限");
        string manifestJson = File.ReadAllText(indexPath, new UTF8Encoding(false, true));
        IReadOnlyDictionary<string, BootstrapManifestTrustedKey> trusted = request.TrustedKeys
            ?? new Dictionary<string, BootstrapManifestTrustedKey>();
        Version clientVersion = request.CurrentClientVersion ?? new Version(0, 0);
        BootstrapManifestVerificationResult verified = BootstrapManifestSignaturePolicy.Verify(
            manifestJson,
            trusted,
            clientVersion,
            request.AcceptedState);
        if (!verified.IsValid) throw new CustomGuiValidationException("GUI05-SIGN-001", verified.Error);

        BootstrapSignedPackage resourcePackage = verified.Manifest.Packages.SingleOrDefault(
            package => string.Equals(package.Name, request.ResourceManifestPackageName, StringComparison.Ordinal))
            ?? throw new CustomGuiValidationException("GUI05-RESOURCE-001", "签名索引未登记 Bootstrap 资源清单包");
        string resourcePackagePath = ResolveFile(root, resourcePackage.Name + ".zip");
        VerifyPackageFile(resourcePackagePath, resourcePackage);
        BootstrapPackageManifestDocument resourceManifest = ReadResourceManifest(resourcePackagePath);

        return CustomGuiPackageVerifier.VerifyAndLoad(new CustomGuiPackageVerificationRequest
        {
            BootstrapManifestJson = manifestJson,
            TrustedKeys = trusted,
            CurrentClientVersion = clientVersion,
            AcceptedState = request.AcceptedState,
            PackageName = request.GuiPackageName,
            PackagePath = ResolveFile(root, request.GuiPackageName + ".zip"),
            BootstrapResourceManifest = resourceManifest,
            AcceptedDocumentState = request.AcceptedDocumentState,
        });
    }

    private static BootstrapPackageManifestDocument ReadResourceManifest(string packagePath)
    {
        try
        {
            using ZipArchive archive = ZipFile.OpenRead(packagePath);
            ZipArchiveEntry entry = archive.GetEntry(ResourceManifestEntryName)
                ?? throw new CustomGuiValidationException("GUI05-RESOURCE-001", "Bootstrap 资源包缺少资源清单");
            if (entry.Length is <= 0 or > MaximumResourceManifestBytes)
                throw new CustomGuiValidationException("GUI05-LIMIT-001", "Bootstrap 资源清单为空或超过 2 MiB 上限");
            using Stream source = entry.Open();
            using var bounded = new MemoryStream((int)entry.Length);
            byte[] buffer = new byte[81920];
            int read;
            while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
            {
                if (bounded.Length + read > MaximumResourceManifestBytes)
                    throw new CustomGuiValidationException("GUI05-LIMIT-001", "Bootstrap 资源清单解压后超过 2 MiB 上限");
                bounded.Write(buffer, 0, read);
            }
            bounded.Position = 0;
            return BootstrapPackageManifestReader.Load(bounded, _ => [], _ => Stream.Null);
        }
        catch (CustomGuiValidationException) { throw; }
        catch (Exception error) when (error is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            throw new CustomGuiValidationException("GUI05-RESOURCE-001", "Bootstrap 资源清单包无效", error);
        }
    }

    private static void VerifyPackageFile(string path, BootstrapSignedPackage package)
    {
        if (!File.Exists(path) || new FileInfo(path).Length != package.Size)
            throw new CustomGuiValidationException("GUI05-SIGN-001", $"签名资源包不存在或大小不符：{package.Name}");
        try { BootstrapSignedPackageHashPolicy.VerifyFile(path, package.Sha256); }
        catch (Exception error) when (error is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            throw new CustomGuiValidationException("GUI05-SIGN-001", $"签名资源包摘要不符：{package.Name}", error);
        }
    }

    private static string ResolveFile(string root, string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || Path.GetFileName(fileName) != fileName)
            throw new CustomGuiValidationException("GUI05-SIGN-001", "GUI 发布文件名无效");
        string path = Path.GetFullPath(Path.Combine(root, fileName));
        string prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new CustomGuiValidationException("GUI05-SIGN-001", "GUI 发布文件越出包目录");
        return path;
    }
}
