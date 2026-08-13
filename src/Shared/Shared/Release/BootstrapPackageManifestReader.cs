using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Shared.Release;

public sealed class BootstrapPackageManifestDocument
{
    public List<BootstrapPackageManifestEntry> Packs { get; set; } = new();
}

public sealed class BootstrapPackageManifestEntry
{
    public string Name { get; set; }
    public string Kind { get; set; }
    public string Description { get; set; }
    public int AssetCount { get; set; }
    public long TotalBytes { get; set; }
    public string ManifestPath { get; set; }
    public string InstallRootHint { get; set; }
    public List<string> Assets { get; set; } = new();
}

/// <summary>
/// 统一读取 bootstrap 主清单与分包清单。调用方只负责提供平台对应的流。
/// </summary>
public static class BootstrapPackageManifestReader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static BootstrapPackageManifestDocument Load(
        Stream mainManifest,
        Func<BootstrapPackageManifestDocument, IEnumerable<string>> enumerateManifestPaths,
        Func<string, Stream> openManifest)
    {
        BootstrapPackageManifestDocument document = ReadMain(mainManifest);
        var packsByName = new Dictionary<string, BootstrapPackageManifestEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (BootstrapPackageManifestEntry pack in document.Packs)
        {
            if (!string.IsNullOrWhiteSpace(pack?.Name))
                packsByName[pack.Name] = Normalize(pack);
        }

        foreach (string manifestPath in enumerateManifestPaths?.Invoke(document) ?? Array.Empty<string>())
        {
            using Stream stream = openManifest?.Invoke(manifestPath);
            BootstrapPackageManifestEntry incoming = TryReadPack(stream, manifestPath);
            if (string.IsNullOrWhiteSpace(incoming?.Name)) continue;

            packsByName[incoming.Name] = packsByName.TryGetValue(incoming.Name, out BootstrapPackageManifestEntry existing)
                ? Merge(existing, incoming)
                : Normalize(incoming);
        }

        document.Packs = packsByName.Values
            .OrderBy(pack => pack.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return document;
    }

    private static BootstrapPackageManifestDocument ReadMain(Stream stream)
    {
        if (stream == null) return new BootstrapPackageManifestDocument();
        try
        {
            BootstrapPackageManifestDocument document = JsonSerializer.Deserialize<BootstrapPackageManifestDocument>(stream, Options);
            document ??= new BootstrapPackageManifestDocument();
            document.Packs ??= new List<BootstrapPackageManifestEntry>();
            return document;
        }
        catch (Exception)
        {
            return new BootstrapPackageManifestDocument();
        }
    }

    private static BootstrapPackageManifestEntry TryReadPack(Stream stream, string manifestPath)
    {
        if (stream == null) return null;
        try
        {
            BootstrapPackageManifestEntry pack = JsonSerializer.Deserialize<BootstrapPackageManifestEntry>(stream, Options);
            if (pack != null && string.IsNullOrWhiteSpace(pack.ManifestPath))
                pack.ManifestPath = NormalizePath(manifestPath);
            return pack;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static BootstrapPackageManifestEntry Merge(
        BootstrapPackageManifestEntry existing,
        BootstrapPackageManifestEntry incoming)
    {
        existing.Kind = string.IsNullOrWhiteSpace(incoming.Kind) ? existing.Kind : incoming.Kind;
        existing.Description = string.IsNullOrWhiteSpace(incoming.Description) ? existing.Description : incoming.Description;
        existing.ManifestPath = string.IsNullOrWhiteSpace(incoming.ManifestPath) ? existing.ManifestPath : incoming.ManifestPath;
        existing.InstallRootHint = string.IsNullOrWhiteSpace(incoming.InstallRootHint) ? existing.InstallRootHint : incoming.InstallRootHint;
        if (incoming.AssetCount > 0) existing.AssetCount = incoming.AssetCount;
        if (incoming.TotalBytes > 0) existing.TotalBytes = incoming.TotalBytes;
        if (incoming.Assets is { Count: > 0 }) existing.Assets = incoming.Assets;
        return Normalize(existing);
    }

    private static BootstrapPackageManifestEntry Normalize(BootstrapPackageManifestEntry pack)
    {
        pack.ManifestPath = string.IsNullOrWhiteSpace(pack.ManifestPath)
            ? $"BootstrapAssets/bootstrap-package-manifests/{pack.Name}.json"
            : NormalizePath(pack.ManifestPath);
        pack.InstallRootHint = string.IsNullOrWhiteSpace(pack.InstallRootHint)
            ? $"Cache/Mobile/Packages/{pack.Name}/"
            : NormalizePath(pack.InstallRootHint);
        pack.Assets ??= new List<string>();
        return pack;
    }

    private static string NormalizePath(string path) => (path ?? string.Empty).Replace('\\', '/');
}
