#nullable enable

using System.IO.Compression;
using System.Security.Cryptography;
using Shared.Release;
using Shared.Security;

namespace Shared.CustomGui;

public sealed class CustomGuiPackageVerificationRequest
{
    public string? BootstrapManifestJson { get; set; }
    public IReadOnlyDictionary<string, BootstrapManifestTrustedKey>? TrustedKeys { get; set; }
    public Version? CurrentClientVersion { get; set; }
    public BootstrapManifestAcceptedState? AcceptedState { get; set; }
    public string? PackageName { get; set; }
    public string? PackagePath { get; set; }
    public BootstrapPackageManifestDocument? BootstrapResourceManifest { get; set; }
    public CustomGuiAcceptedDocumentState? AcceptedDocumentState { get; set; }
}

public sealed record CustomGuiAcceptedPackage(
    string ResourceVersion,
    long Sequence,
    CustomGuiRuntimeDocument Document,
    string PackageSha256,
    string DocumentSha256,
    CustomGuiResourceCatalog Resources);

public sealed record CustomGuiAcceptedDocumentState(string DocumentId, long Revision, string DocumentSha256);

public static class CustomGuiPackageVerifier
{
    public const string DocumentEntryName = "custom-gui/document.json";
    public const string ResourceBindingsEntryName = "custom-gui/resources.json";

    public static CustomGuiAcceptedPackage VerifyAndLoad(CustomGuiPackageVerificationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        BootstrapManifestVerificationResult verification = BootstrapManifestSignaturePolicy.Verify(
            request.BootstrapManifestJson ?? string.Empty,
            request.TrustedKeys ?? new Dictionary<string, BootstrapManifestTrustedKey>(),
            request.CurrentClientVersion ?? new Version(0, 0),
            request.AcceptedState);
        if (!verification.IsValid)
            throw new CustomGuiValidationException("GUI05-SIGN-001", verification.Error);

        BootstrapSignedPackage? signedPackage = verification.Manifest.Packages.SingleOrDefault(
            package => string.Equals(package.Name, request.PackageName, StringComparison.Ordinal));
        if (signedPackage is null) throw new CustomGuiValidationException("GUI05-SIGN-001", "签名清单未登记 GUI 资源包");
        if (signedPackage.Size > CustomGuiValidationLimits.MaximumPackageBytes)
            throw new CustomGuiValidationException("GUI05-LIMIT-001", "已签名 GUI 资源包超过 32 MiB 上限");
        if (string.IsNullOrWhiteSpace(request.PackagePath) || !File.Exists(request.PackagePath))
            throw new CustomGuiValidationException("GUI05-SIGN-001", "GUI 资源包不存在");
        long actualSize = new FileInfo(request.PackagePath).Length;
        if (actualSize > CustomGuiValidationLimits.MaximumPackageBytes)
            throw new CustomGuiValidationException("GUI05-LIMIT-001", "GUI 资源包超过 32 MiB 上限");
        if (actualSize != signedPackage.Size)
            throw new CustomGuiValidationException("GUI05-SIGN-001", "GUI 资源包大小与签名清单不一致");
        string packageSha256;
        try { packageSha256 = BootstrapSignedPackageHashPolicy.VerifyFile(request.PackagePath, signedPackage.Sha256); }
        catch (Exception error) when (error is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            throw new CustomGuiValidationException("GUI05-SIGN-001", "GUI 资源包摘要校验失败", error);
        }

        byte[] documentBytes;
        byte[] bindingBytes;
        try
        {
            using FileStream packageStream = File.OpenRead(request.PackagePath);
            using var archive = new ZipArchive(packageStream, ZipArchiveMode.Read, leaveOpen: false);
            if (archive.Entries.Count == 0 || archive.Entries.Count > CustomGuiValidationLimits.MaximumArchiveEntries)
                throw new CustomGuiValidationException("GUI05-LIMIT-001", "GUI ZIP 条目数量超出允许范围");
            var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
            long totalUncompressed = 0;
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                string normalized = NormalizeArchiveEntry(entry.FullName);
                if (!entries.TryAdd(normalized, entry))
                    throw new CustomGuiValidationException("GUI05-SIGN-001", $"GUI ZIP 包含重复条目：{normalized}");
                totalUncompressed = checked(totalUncompressed + entry.Length);
                if (totalUncompressed > CustomGuiValidationLimits.MaximumUncompressedPackageBytes)
                    throw new CustomGuiValidationException("GUI05-LIMIT-001", "GUI ZIP 解压总量超过 64 MiB 上限");
            }
            if (!entries.TryGetValue(DocumentEntryName, out ZipArchiveEntry? documentEntry) ||
                !entries.TryGetValue(ResourceBindingsEntryName, out ZipArchiveEntry? bindingsEntry))
                throw new CustomGuiValidationException("GUI05-SIGN-001", "GUI ZIP 缺少固定运行描述或资源绑定条目");
            documentBytes = ReadBounded(documentEntry, CustomGuiValidationLimits.MaximumDocumentBytes, "GUI 运行描述");
            bindingBytes = ReadBounded(bindingsEntry, CustomGuiValidationLimits.MaximumResourceBindingsBytes, "GUI 资源绑定");
        }
        catch (CustomGuiValidationException) { throw; }
        catch (Exception error) when (error is InvalidDataException or IOException or UnauthorizedAccessException or OverflowException)
        {
            throw new CustomGuiValidationException("GUI05-SIGN-001", "GUI ZIP 结构无效", error);
        }

        CustomGuiRuntimeDocument document = CustomGuiDocumentCodec.Deserialize(documentBytes);
        string documentSha256 = Convert.ToHexString(SHA256.HashData(documentBytes)).ToLowerInvariant();
        ValidateAcceptedDocument(document, documentSha256, request.AcceptedDocumentState);
        CustomGuiResourceBindingsDocument bindings = CustomGuiResourceBindingsCodec.Deserialize(bindingBytes);
        CustomGuiResourceCatalog resources = CustomGuiResourceCatalog.FromBootstrapManifest(
            request.BootstrapResourceManifest ?? throw new CustomGuiValidationException("GUI05-RESOURCE-001", "Bootstrap 资源清单为空"),
            bindings.Assets,
            bindings.Fonts);
        CustomGuiValidationPolicy.EnsureValid(document, resources);
        return new CustomGuiAcceptedPackage(
            verification.Manifest.ResourceVersion,
            verification.Manifest.Sequence,
            document,
            packageSha256,
            documentSha256,
            resources);
    }

    private static void ValidateAcceptedDocument(
        CustomGuiRuntimeDocument document,
        string documentSha256,
        CustomGuiAcceptedDocumentState? accepted)
    {
        if (accepted is null) return;
        if (!string.Equals(document.DocumentId, accepted.DocumentId, StringComparison.Ordinal))
            throw new CustomGuiValidationException("GUI05-DOC-001", "签名包替换了已接受的 GUI 文档标识");
        if (document.Revision < accepted.Revision)
            throw new CustomGuiValidationException("GUI05-DOC-001", "GUI 文档修订号低于已接受版本，拒绝降级");
        if (document.Revision == accepted.Revision &&
            !string.Equals(documentSha256, accepted.DocumentSha256, StringComparison.Ordinal))
            throw new CustomGuiValidationException("GUI05-DOC-001", "GUI 文档复用了已接受修订号但内容不同");
    }

    private static string NormalizeArchiveEntry(string value)
    {
        string normalized = (value ?? string.Empty).Replace('\\', '/');
        if (normalized.Length == 0 || normalized.StartsWith('/') || Path.IsPathRooted(normalized) ||
            normalized.Split('/').Any(part => part is "" or "." or ".."))
            throw new CustomGuiValidationException("GUI05-SIGN-001", $"GUI ZIP 条目路径无效：{value}");
        return normalized;
    }

    private static byte[] ReadBounded(ZipArchiveEntry entry, int maximumBytes, string label)
    {
        if (entry.Length <= 0 || entry.Length > maximumBytes)
            throw new CustomGuiValidationException("GUI05-LIMIT-001", $"{label}为空或超过上限");
        using Stream source = entry.Open();
        using var target = new MemoryStream((int)entry.Length);
        var buffer = new byte[81920];
        int read;
        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (target.Length + read > maximumBytes)
                throw new CustomGuiValidationException("GUI05-LIMIT-001", $"{label}解压后超过上限");
            target.Write(buffer, 0, read);
        }
        return target.ToArray();
    }
}
