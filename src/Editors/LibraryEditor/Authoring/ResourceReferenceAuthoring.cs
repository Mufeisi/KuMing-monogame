using System.Collections.ObjectModel;
using System.Security.Cryptography;
using Shared.Release;

namespace LibraryEditor.Authoring;

public sealed record ResourceAsset(string ResourcePath, long Size, string ContentHash);

public sealed record ResourceReference(string Owner, string ResourcePath, string OwnerPath = "");

public sealed record ResourceReferenceDiagnostic(
    string Code,
    string Message,
    string ResourcePath,
    string Owner);

public sealed record ResourceDuplicateCandidate(
    long Size,
    string ContentHash,
    IReadOnlyList<string> ResourcePaths);

public sealed class ResourceReferenceReport
{
    private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _ownersByResource;

    internal ResourceReferenceReport(
        IReadOnlyList<ResourceReferenceDiagnostic> missingReferences,
        IReadOnlyDictionary<string, IReadOnlyList<string>> ownersByResource,
        IReadOnlyList<ResourceDuplicateCandidate> duplicateCandidates,
        IReadOnlyList<string> unusedCandidates)
    {
        MissingReferences = missingReferences;
        _ownersByResource = ownersByResource;
        DuplicateCandidates = duplicateCandidates;
        UnusedCandidates = unusedCandidates;
    }

    public IReadOnlyList<ResourceReferenceDiagnostic> MissingReferences { get; }

    public IReadOnlyList<ResourceDuplicateCandidate> DuplicateCandidates { get; }

    public IReadOnlyList<string> UnusedCandidates { get; }

    public IReadOnlyList<string> GetOwners(string resourcePath)
    {
        string key = ResourceReferenceAnalyzer.NormalizePath(resourcePath);
        return _ownersByResource.TryGetValue(key, out IReadOnlyList<string> owners)
            ? owners
            : Array.Empty<string>();
    }
}

public static class ResourceReferenceAnalyzer
{
    public static ResourceReferenceReport Analyze(
        IEnumerable<ResourceAsset> assets,
        IEnumerable<ResourceReference> references)
    {
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(references);

        ResourceAsset[] normalizedAssets = assets
            .Where(asset => !string.IsNullOrWhiteSpace(asset.ResourcePath))
            .Select(asset => asset with { ResourcePath = NormalizePath(asset.ResourcePath) })
            .GroupBy(asset => asset.ResourcePath, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(asset => asset.ResourcePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        ResourceReference[] normalizedReferences = references
            .Where(reference => !string.IsNullOrWhiteSpace(reference.Owner) &&
                                !string.IsNullOrWhiteSpace(reference.ResourcePath))
            .Select(reference => reference with
            {
                Owner = reference.Owner.Trim(),
                ResourcePath = NormalizePath(reference.ResourcePath)
            })
            .DistinctBy(reference => (reference.Owner.ToUpperInvariant(), reference.ResourcePath.ToUpperInvariant()))
            .OrderBy(reference => reference.ResourcePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(reference => reference.Owner, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var assetsByPath = normalizedAssets.ToDictionary(
            asset => asset.ResourcePath,
            StringComparer.OrdinalIgnoreCase);
        var ownersByResource = normalizedReferences
            .GroupBy(reference => reference.ResourcePath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)new ReadOnlyCollection<string>(group
                    .Select(reference => reference.Owner)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(owner => owner, StringComparer.OrdinalIgnoreCase)
                    .ToArray()),
                StringComparer.OrdinalIgnoreCase);

        ResourceReferenceDiagnostic[] missing = normalizedReferences
            .Where(reference => !assetsByPath.ContainsKey(reference.ResourcePath))
            .Select(reference => new ResourceReferenceDiagnostic(
                "CONTENT05-RESOURCE-001",
                $"资源引用不存在：{reference.ResourcePath}（来源：{reference.Owner}）",
                reference.ResourcePath,
                reference.Owner))
            .ToArray();
        ResourceDuplicateCandidate[] duplicates = normalizedAssets
            .Where(asset => asset.Size > 0 && !string.IsNullOrWhiteSpace(asset.ContentHash))
            .GroupBy(asset => (asset.Size, Hash: asset.ContentHash.Trim().ToUpperInvariant()))
            .Where(group => group.Count() > 1)
            .Select(group => new ResourceDuplicateCandidate(
                group.Key.Size,
                group.Key.Hash,
                group.Select(asset => asset.ResourcePath)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToArray()))
            .OrderBy(candidate => candidate.ResourcePaths[0], StringComparer.OrdinalIgnoreCase)
            .ToArray();
        string[] unused = normalizedAssets
            .Where(asset => !ownersByResource.ContainsKey(asset.ResourcePath))
            .Select(asset => asset.ResourcePath)
            .ToArray();

        return new ResourceReferenceReport(missing, ownersByResource, duplicates, unused);
    }

    internal static string NormalizePath(string path)
    {
        return (path ?? string.Empty)
            .Trim()
            .Replace('\\', '/')
            .TrimStart('/');
    }
}

public sealed class ResourceReferenceWorkspace
{
    private ResourceReferenceWorkspace(
        string resourceRoot,
        IReadOnlyList<ResourceAsset> assets,
        IReadOnlyList<ResourceReference> references,
        ResourceReferenceReport report)
    {
        ResourceRoot = resourceRoot;
        Assets = assets;
        References = references;
        Report = report;
    }

    public IReadOnlyList<ResourceAsset> Assets { get; }

    public string ResourceRoot { get; }

    public string PackageManifestPath { get; private set; }

    public IReadOnlyList<ResourceReference> References { get; }

    public ResourceReferenceReport Report { get; }

    public static ResourceReferenceWorkspace Load(string resourceRoot, string packageManifestPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageManifestPath);

        string root = Path.GetFullPath(resourceRoot);
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"资源目录不存在：{root}");
        if (!File.Exists(packageManifestPath))
            throw new FileNotFoundException("资源包清单不存在。", packageManifestPath);

        string fullManifestPath = Path.GetFullPath(packageManifestPath);
        ResourceReference[] references = ReadReferences(root, fullManifestPath);
        ResourceAsset[] assets = ReadAssets(root, fullManifestPath);
        var workspace = new ResourceReferenceWorkspace(
            root,
            assets,
            references,
            ResourceReferenceAnalyzer.Analyze(assets, references));
        workspace.PackageManifestPath = fullManifestPath;
        return workspace;
    }

    public string GetOwnerPath(string owner)
    {
        return References.FirstOrDefault(reference =>
            string.Equals(reference.Owner, owner, StringComparison.OrdinalIgnoreCase))?.OwnerPath ?? string.Empty;
    }

    private static ResourceReference[] ReadReferences(string root, string packageManifestPath)
    {
        using FileStream mainStream = File.OpenRead(packageManifestPath);
        BootstrapPackageManifestDocument document = BootstrapPackageManifestReader.Load(
            mainStream,
            manifest => EnumerateManifestPaths(root, packageManifestPath, manifest),
            path => OpenManifest(root, packageManifestPath, path));

        var references = new List<ResourceReference>();
        foreach (BootstrapPackageManifestEntry pack in document.Packs)
        {
            string ownerPath = ResolveManifestPath(root, packageManifestPath, pack.ManifestPath);
            if (!File.Exists(ownerPath)) ownerPath = packageManifestPath;
            foreach (string asset in pack.Assets ?? new List<string>())
                references.Add(new ResourceReference(pack.Name, asset, ownerPath));
        }

        return references.ToArray();
    }

    private static IEnumerable<string> EnumerateManifestPaths(
        string root,
        string packageManifestPath,
        BootstrapPackageManifestDocument document)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (BootstrapPackageManifestEntry pack in document.Packs)
            if (!string.IsNullOrWhiteSpace(pack?.ManifestPath)) paths.Add(pack.ManifestPath);

        foreach (string directory in new[]
                 {
                     Path.Combine(Path.GetDirectoryName(packageManifestPath) ?? root, "bootstrap-package-manifests"),
                     Path.Combine(root, "BootstrapAssets", "bootstrap-package-manifests")
                 })
            if (Directory.Exists(directory))
                foreach (string file in Directory.GetFiles(directory, "*.json", SearchOption.TopDirectoryOnly))
                    paths.Add(Path.GetRelativePath(root, file).Replace('\\', '/'));
        return paths;
    }

    private static Stream OpenManifest(string root, string packageManifestPath, string manifestPath)
    {
        string path = ResolveManifestPath(root, packageManifestPath, manifestPath);
        return File.Exists(path) ? File.OpenRead(path) : null;
    }

    private static string ResolveManifestPath(string root, string packageManifestPath, string manifestPath)
    {
        if (string.IsNullOrWhiteSpace(manifestPath)) return packageManifestPath;
        string normalized = manifestPath.Replace('/', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(normalized)) return Path.GetFullPath(normalized);
        string bootstrapPrefix = "BootstrapAssets" + Path.DirectorySeparatorChar;
        if (Path.GetFileName(root).Equals("BootstrapAssets", StringComparison.OrdinalIgnoreCase) &&
            normalized.StartsWith(bootstrapPrefix, StringComparison.OrdinalIgnoreCase))
            normalized = normalized.Substring(bootstrapPrefix.Length);
        string rootCandidate = Path.GetFullPath(Path.Combine(root, normalized));
        if (File.Exists(rootCandidate)) return rootCandidate;
        return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(packageManifestPath) ?? root, normalized));
    }

    private static ResourceAsset[] ReadAssets(string root, string packageManifestPath)
    {
        var enumeration = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = false,
            AttributesToSkip = FileAttributes.ReparsePoint
        };
        FileInfo[] files = new DirectoryInfo(root)
            .EnumerateFiles("*", enumeration)
            .Where(file => !IsManifestMetadata(root, packageManifestPath, file.FullName))
            .OrderBy(file => file.FullName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (IGrouping<long, FileInfo> group in files.GroupBy(file => file.Length).Where(group => group.Count() > 1))
        {
            foreach (FileInfo file in group)
            {
                using FileStream stream = file.OpenRead();
                hashes[file.FullName] = Convert.ToHexString(SHA256.HashData(stream));
            }
        }

        return files.Select(file => new ResourceAsset(
                ResourceReferenceAnalyzer.NormalizePath(Path.GetRelativePath(root, file.FullName)),
                file.Length,
                hashes.GetValueOrDefault(file.FullName, string.Empty)))
            .ToArray();
    }

    private static bool IsManifestMetadata(string root, string packageManifestPath, string fullPath)
    {
        if (string.Equals(Path.GetFullPath(fullPath), packageManifestPath, StringComparison.OrdinalIgnoreCase))
            return true;

        string relativePath = ResourceReferenceAnalyzer.NormalizePath(Path.GetRelativePath(root, fullPath));
        return string.Equals(relativePath, "bootstrap-package-index.json", StringComparison.OrdinalIgnoreCase) ||
               relativePath.StartsWith("bootstrap-package-manifests/", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class LibraryContentEditingSession : IDisposable
{
    private IReadOnlyList<LibraryImageSnapshot> _baseline;
    private IReadOnlyList<LibraryFrameSnapshot> _frameBaseline;
    private readonly Func<string, MLibraryV2> _loadLibrary;

    public LibraryContentEditingSession(MLibraryV2 draft, Func<string, MLibraryV2> loadLibrary = null)
    {
        _loadLibrary = loadLibrary ?? (fileName => new MLibraryV2(fileName));
        Fact = draft ?? throw new ArgumentNullException(nameof(draft));
        Draft = Fact.CloneForEditing();
        _baseline = Capture(Draft);
        _frameBaseline = CaptureFrames(Draft);
    }

    public MLibraryV2 Fact { get; private set; }

    public MLibraryV2 Draft { get; private set; }

    public bool IsDirty => !_baseline.SequenceEqual(Capture(Draft)) ||
                           !_frameBaseline.SequenceEqual(CaptureFrames(Draft));

    public string DescribeChanges()
    {
        IReadOnlyList<LibraryImageSnapshot> current = Capture(Draft);
        var changes = new List<string>();
        int count = Math.Max(_baseline.Count, current.Count);
        for (var index = 0; index < count; index++)
        {
            if (index >= _baseline.Count)
                changes.Add($"图像 {index}：新增");
            else if (index >= current.Count)
                changes.Add($"图像 {index}：删除");
            else if (_baseline[index] != current[index])
                changes.Add($"图像 {index}：内容或位置已修改");
        }
        if (!_frameBaseline.SequenceEqual(CaptureFrames(Draft)))
            changes.Add("帧表：已修改");

        return changes.Count == 0 ? "无变更" : string.Join(Environment.NewLine, changes);
    }

    public bool TryCommit(Action<MLibraryV2> persist, out string error)
    {
        ArgumentNullException.ThrowIfNull(persist);
        MLibraryV2 replacementFact = null;
        MLibraryV2 replacementDraft = null;
        try
        {
            replacementFact = Draft.CloneForEditing();
            replacementDraft = replacementFact.CloneForEditing();
            IReadOnlyList<LibraryImageSnapshot> replacementBaseline = Capture(replacementDraft);
            IReadOnlyList<LibraryFrameSnapshot> replacementFrameBaseline = CaptureFrames(replacementDraft);
            persist(Draft);
            MLibraryV2 oldFact = Fact;
            MLibraryV2 oldDraft = Draft;
            Fact = replacementFact;
            Draft = replacementDraft;
            _baseline = replacementBaseline;
            _frameBaseline = replacementFrameBaseline;
            replacementFact = null;
            replacementDraft = null;
            DisposeWithoutThrow(oldFact);
            DisposeWithoutThrow(oldDraft);
            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            DisposeWithoutThrow(replacementDraft);
            DisposeWithoutThrow(replacementFact);
            error = ex.Message;
            return false;
        }
    }

    public IReadOnlyList<LibraryContentDiagnostic> Validate()
    {
        var diagnostics = new List<LibraryContentDiagnostic>();
        for (var index = 0; index < Draft.Images.Count; index++)
        {
            MLibraryV2.MImage image = Draft.Images[index];
            if (image == null)
            {
                diagnostics.Add(new LibraryContentDiagnostic(
                    "CONTENT05-LIB-001",
                    $"图像槽位 {index} 为空，无法保存。",
                    index));
                continue;
            }
            if (image.Width < 0 || image.Height < 0 || image.FBytes == null)
                diagnostics.Add(new LibraryContentDiagnostic(
                    "CONTENT05-LIB-003",
                    $"图像槽位 {index} 的尺寸或内容无效。",
                    index));
        }
        foreach ((MirAction action, Frame frame) in Draft.Frames)
        {
            if (frame.Count < 0 || frame.Interval < 0 || frame.EffectCount < 0 || frame.EffectInterval < 0)
                diagnostics.Add(new LibraryContentDiagnostic(
                    "CONTENT05-LIB-002",
                    $"帧动作 {action} 的数量或时间不能为负数。",
                    null));
        }
        return diagnostics;
    }

    public bool TryValidateAndCommit(
        Action<MLibraryV2> persist,
        out IReadOnlyList<LibraryContentDiagnostic> diagnostics,
        out string error)
    {
        diagnostics = Validate();
        if (diagnostics.Count != 0)
        {
            error = string.Join(Environment.NewLine, diagnostics.Select(item => $"{item.Code} {item.Message}"));
            return false;
        }
        return TryCommit(persist, out error);
    }

    public void Reload()
    {
        string fileName = Fact.FileName;
        MLibraryV2 replacementFact = null;
        MLibraryV2 replacementDraft = null;
        try
        {
            replacementFact = _loadLibrary(fileName);
            replacementDraft = replacementFact.CloneForEditing();
            IReadOnlyList<LibraryImageSnapshot> replacementBaseline = Capture(replacementDraft);
            IReadOnlyList<LibraryFrameSnapshot> replacementFrameBaseline = CaptureFrames(replacementDraft);
            MLibraryV2 oldFact = Fact;
            MLibraryV2 oldDraft = Draft;
            Fact = replacementFact;
            Draft = replacementDraft;
            _baseline = replacementBaseline;
            _frameBaseline = replacementFrameBaseline;
            replacementFact = null;
            replacementDraft = null;
            DisposeWithoutThrow(oldFact);
            DisposeWithoutThrow(oldDraft);
        }
        catch
        {
            DisposeWithoutThrow(replacementDraft);
            DisposeWithoutThrow(replacementFact);
            throw;
        }
    }

    public void Dispose()
    {
        Fact?.Dispose();
        if (!ReferenceEquals(Draft, Fact)) Draft?.Dispose();
        Fact = null;
        Draft = null;
    }

    private static void DisposeWithoutThrow(MLibraryV2 library)
    {
        try
        {
            library?.Dispose();
        }
        catch (Exception)
        {
        }
    }

    private static IReadOnlyList<LibraryImageSnapshot> Capture(MLibraryV2 library)
    {
        return library.Images.Select(image => new LibraryImageSnapshot(
                image?.Width ?? 0,
                image?.Height ?? 0,
                image?.X ?? 0,
                image?.Y ?? 0,
                image?.ShadowX ?? 0,
                image?.ShadowY ?? 0,
                image?.Shadow ?? 0,
                Hash(image?.FBytes),
                image?.MaskWidth ?? 0,
                image?.MaskHeight ?? 0,
                image?.MaskX ?? 0,
                image?.MaskY ?? 0,
                Hash(image?.MaskFBytes)))
            .ToArray();
    }

    private static string Hash(byte[] bytes)
    {
        return bytes == null || bytes.Length == 0
            ? string.Empty
            : Convert.ToHexString(SHA256.HashData(bytes));
    }

    private static IReadOnlyList<LibraryFrameSnapshot> CaptureFrames(MLibraryV2 library)
    {
        return library.Frames
            .OrderBy(entry => entry.Key)
            .Select(entry => new LibraryFrameSnapshot(
                entry.Key,
                entry.Value.Start,
                entry.Value.Count,
                entry.Value.Skip,
                entry.Value.Interval,
                entry.Value.EffectStart,
                entry.Value.EffectCount,
                entry.Value.EffectSkip,
                entry.Value.EffectInterval,
                entry.Value.Reverse,
                entry.Value.Blend))
            .ToArray();
    }

    private sealed record LibraryImageSnapshot(
        short Width,
        short Height,
        short X,
        short Y,
        short ShadowX,
        short ShadowY,
        byte Shadow,
        string ContentHash,
        short MaskWidth,
        short MaskHeight,
        short MaskX,
        short MaskY,
        string MaskHash);

    private sealed record LibraryFrameSnapshot(
        MirAction Action,
        int Start,
        int Count,
        int Skip,
        int Interval,
        int EffectStart,
        int EffectCount,
        int EffectSkip,
        int EffectInterval,
        bool Reverse,
        bool Blend);
}

public sealed record LibraryContentDiagnostic(string Code, string Message, int? ImageIndex);
