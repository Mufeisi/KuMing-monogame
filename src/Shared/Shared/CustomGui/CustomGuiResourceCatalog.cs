#nullable enable

using System.Text.Json;
using Shared.Release;
using Shared.Security;

namespace Shared.CustomGui;

public sealed record CustomGuiResourceBinding(string Id, string PackageName, string AssetPath, string? AtlasPath = null);
public sealed record CustomGuiFontBinding(string Id, string PackageName, string AssetPath);

public sealed class CustomGuiResourceBindingsDocument
{
    public int SchemaVersion { get; set; } = 1;
    public List<CustomGuiResourceBinding> Assets { get; set; } = [];
    public List<CustomGuiFontBinding> Fonts { get; set; } = [];
}

public static class CustomGuiResourceBindingsCodec
{
    public static byte[] Serialize(CustomGuiResourceBindingsDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.SchemaVersion != 1) throw new CustomGuiValidationException("GUI05-RESOURCE-001", "资源绑定版本不受支持");
        return JsonSerializer.SerializeToUtf8Bytes(document, CustomGuiJsonContext.Default.CustomGuiResourceBindingsDocument);
    }

    public static CustomGuiResourceBindingsDocument Deserialize(ReadOnlySpan<byte> utf8Json)
    {
        if (utf8Json.IsEmpty || utf8Json.Length > CustomGuiValidationLimits.MaximumResourceBindingsBytes)
            throw new CustomGuiValidationException("GUI05-LIMIT-001", "资源绑定为空或超过 128 KiB 上限");
        try
        {
            using JsonDocument parsed = JsonDocument.Parse(utf8Json.ToArray());
            if (BootstrapManifestSignaturePolicy.ContainsDuplicateProperty(parsed.RootElement))
                throw new CustomGuiValidationException("GUI05-RESOURCE-001", "资源绑定包含重复 JSON 字段");
            CustomGuiResourceBindingsDocument? document = parsed.RootElement.Deserialize(
                CustomGuiJsonContext.Default.CustomGuiResourceBindingsDocument);
            if (document is null || document.SchemaVersion != 1 || document.Assets is null || document.Fonts is null)
                throw new CustomGuiValidationException("GUI05-RESOURCE-001", "资源绑定格式或版本无效");
            return document;
        }
        catch (CustomGuiValidationException) { throw; }
        catch (Exception error) when (error is JsonException or NotSupportedException)
        {
            throw new CustomGuiValidationException("GUI05-RESOURCE-001", "资源绑定 JSON 无效或含未知字段", error);
        }
    }
}

public sealed class CustomGuiResourceCatalog
{
    private readonly IReadOnlyDictionary<string, CustomGuiResourceBinding> _assets;
    private readonly IReadOnlyDictionary<string, CustomGuiFontBinding> _fonts;

    private CustomGuiResourceCatalog(
        IReadOnlyDictionary<string, CustomGuiResourceBinding> assets,
        IReadOnlyDictionary<string, CustomGuiFontBinding> fonts)
    {
        _assets = assets;
        _fonts = fonts;
    }

    public static CustomGuiResourceCatalog Empty { get; } = new(
        new Dictionary<string, CustomGuiResourceBinding>(StringComparer.Ordinal),
        new Dictionary<string, CustomGuiFontBinding>(StringComparer.Ordinal));

    public static CustomGuiResourceCatalog FromBootstrapManifest(
        BootstrapPackageManifestDocument manifest,
        IEnumerable<CustomGuiResourceBinding>? assets,
        IEnumerable<CustomGuiFontBinding>? fonts)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var packages = new Dictionary<string, BootstrapPackageManifestEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (BootstrapPackageManifestEntry? package in manifest.Packs ?? [])
        {
            if (package is null || string.IsNullOrWhiteSpace(package.Name)) continue;
            if (!packages.TryAdd(package.Name, package))
                throw new CustomGuiValidationException("GUI05-RESOURCE-001", $"Bootstrap 资源清单包含重复包名：{package.Name}");
        }
        CustomGuiResourceBinding[] assetArray = assets?.ToArray() ?? [];
        CustomGuiFontBinding[] fontArray = fonts?.ToArray() ?? [];
        if (assetArray.Length + fontArray.Length > CustomGuiValidationLimits.MaximumResourceBindings)
            throw new CustomGuiValidationException("GUI05-LIMIT-001", "GUI 资源绑定数量超过上限");

        var acceptedAssets = new Dictionary<string, CustomGuiResourceBinding>(StringComparer.Ordinal);
        foreach (CustomGuiResourceBinding binding in assetArray)
        {
            if (binding is null) throw new CustomGuiValidationException("GUI05-RESOURCE-001", "逻辑资源绑定为空");
            ValidateBinding(binding.Id, binding.PackageName, binding.AssetPath, packages);
            if (!string.IsNullOrWhiteSpace(binding.AtlasPath)) ValidatePhysicalAsset(binding.PackageName, binding.AtlasPath, packages, "图集");
            if (!acceptedAssets.TryAdd(binding.Id, binding))
                throw new CustomGuiValidationException("GUI05-RESOURCE-001", $"逻辑资源标识重复：{binding.Id}");
        }

        var acceptedFonts = new Dictionary<string, CustomGuiFontBinding>(StringComparer.Ordinal);
        foreach (CustomGuiFontBinding binding in fontArray)
        {
            if (binding is null) throw new CustomGuiValidationException("GUI05-RESOURCE-001", "字体绑定为空");
            ValidateBinding(binding.Id, binding.PackageName, binding.AssetPath, packages);
            if (!acceptedFonts.TryAdd(binding.Id, binding))
                throw new CustomGuiValidationException("GUI05-RESOURCE-001", $"字体标识重复：{binding.Id}");
        }
        return new CustomGuiResourceCatalog(acceptedAssets, acceptedFonts);
    }

    public bool ContainsAsset(string id) => !string.IsNullOrWhiteSpace(id) && _assets.ContainsKey(id);
    public bool ContainsFont(string id) => !string.IsNullOrWhiteSpace(id) && _fonts.ContainsKey(id);

    private static void ValidateBinding(
        string? id,
        string? packageName,
        string? assetPath,
        IReadOnlyDictionary<string, BootstrapPackageManifestEntry> packages)
    {
        if (!CustomGuiValidationPolicy.IsLogicalResourceId(id))
            throw new CustomGuiValidationException("GUI05-RESOURCE-001", $"逻辑资源标识无效：{id}");
        ValidatePhysicalAsset(packageName, assetPath, packages, "资源");
    }

    private static void ValidatePhysicalAsset(
        string? packageName,
        string? assetPath,
        IReadOnlyDictionary<string, BootstrapPackageManifestEntry> packages,
        string kind)
    {
        if (string.IsNullOrWhiteSpace(packageName) || !packages.TryGetValue(packageName, out BootstrapPackageManifestEntry? package))
            throw new CustomGuiValidationException("GUI05-RESOURCE-001", $"{kind}所属 Bootstrap 包不存在：{packageName}");
        string normalized = NormalizePath(assetPath);
        if (normalized.Length == 0 || Path.IsPathRooted(normalized) || normalized.Split('/').Any(part => part == ".."))
            throw new CustomGuiValidationException("GUI05-RESOURCE-001", $"{kind}路径无效：{assetPath}");
        if (!(package.Assets ?? []).Any(item => string.Equals(NormalizePath(item), normalized, StringComparison.OrdinalIgnoreCase)))
            throw new CustomGuiValidationException("GUI05-RESOURCE-001", $"{kind}不在 Bootstrap 资源清单中：{packageName}/{normalized}");
    }

    private static string NormalizePath(string? value) => (value ?? string.Empty).Trim().Replace('\\', '/');
}
