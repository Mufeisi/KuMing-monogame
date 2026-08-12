using System.Text;
using System.Text.Json;

namespace MobileBootstrapAudit;

internal static class Program
{
    private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    private const string RuntimePackageManifestFileName = "bootstrap-packages.json";
    private const string RuntimePackageManifestDirectoryName = "bootstrap-package-manifests";
    private const string AssetSourceBootstrap = "BootstrapAssets";
    private const string AssetSourceClientAssets = "ClientAssets";
    private const string DefaultUiVariantName = "复古";
    private static readonly IReadOnlyDictionary<string, string> UiAssetAliasTargets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Assets/UI/复古/UILoadingRes_r7r823.wav"] = "Sound/Log-in-long2.wav",
        ["Assets/UI/复古/UIRes_vth6j.wav"] = "Sound/sellect-loop2.wav",
    };

    public static int Main(string[] args)
    {
        try
        {
            var options = Options.Parse(args);
            var summary = BuildSummary(options.RepositoryRoot);

            if (options.ImportZeroGapCount > 0)
            {
                var importedMaps = ImportZeroGapCandidates(summary, options.RepositoryRoot, options.ImportZeroGapCount);
                if (importedMaps.Count > 0)
                {
                    Console.WriteLine($"- ImportedZeroGapMaps: {string.Join(", ", importedMaps)}");
                    summary = BuildSummary(options.RepositoryRoot);
                }
            }

            if (options.SyncManifest)
                WriteBootstrapManifest(summary);

            PrintSummary(summary);

            if (!string.IsNullOrWhiteSpace(options.MarkdownOutputPath))
                WriteOutputs(summary, options.MarkdownOutputPath);

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("[FAIL] MobileBootstrapAudit 执行失败：");
            Console.Error.WriteLine(ex.ToString());
            return 1;
        }
    }

    private static List<string> ImportZeroGapCandidates(AuditSummary summary, string repositoryRoot, int count)
    {
        var imported = new List<string>();
        if (count <= 0)
            return imported;

        var sourceRoot = Path.Combine(repositoryRoot, "Build", "Client_VorticeDX11", "Map");
        var targetRoot = Path.Combine(repositoryRoot, "Client_MonoGame.Shared", "BootstrapAssets", "Map");
        Directory.CreateDirectory(targetRoot);

        foreach (var audit in summary.CandidateMapAudits.Where(audit => audit.Supported && audit.MissingLibraries.Count == 0).Take(count))
        {
            var fileName = Path.GetFileName(audit.RelativePath);
            var sourcePath = Path.Combine(sourceRoot, fileName);
            var targetPath = Path.Combine(targetRoot, fileName);

            if (!File.Exists(sourcePath))
                continue;

            if (!File.Exists(targetPath))
            {
                File.Copy(sourcePath, targetPath, overwrite: false);
                imported.Add(fileName);
            }
        }

        return imported;
    }

    private static AuditSummary BuildSummary(string repositoryRoot)
    {
        var clientSharedRoot = Path.Combine(repositoryRoot, "Client_MonoGame.Shared");
        var bootstrapRoot = Path.Combine(clientSharedRoot, "BootstrapAssets");
        var uiVariantRoot = Path.Combine(clientSharedRoot, "Assets", "UI", DefaultUiVariantName);
        var pcClientMapRoot = Path.Combine(repositoryRoot, "Build", "Client_VorticeDX11", "Map");
        EnsureDirectory(bootstrapRoot, "BootstrapAssets");

        var bootstrapAssetFiles = Directory.GetFiles(bootstrapRoot, "*", SearchOption.AllDirectories);
        var uiAssetFiles = Directory.Exists(uiVariantRoot) ? Directory.GetFiles(uiVariantRoot, "*", SearchOption.AllDirectories) : Array.Empty<string>();

        var discovered = new List<(string AbsolutePath, string RelativePath, string SourceRoot)>(bootstrapAssetFiles.Length + uiAssetFiles.Length);
        foreach (var filePath in bootstrapAssetFiles)
        {
            discovered.Add((
                AbsolutePath: filePath,
                RelativePath: NormalizePath(Path.GetRelativePath(bootstrapRoot, filePath)),
                SourceRoot: AssetSourceBootstrap));
        }

        foreach (var filePath in uiAssetFiles)
        {
            discovered.Add((
                AbsolutePath: filePath,
                RelativePath: NormalizePath(Path.GetRelativePath(clientSharedRoot, filePath)),
                SourceRoot: AssetSourceClientAssets));
        }

        discovered = discovered
            .OrderBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.SourceRoot, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var relativeAssets = discovered
            .Select(item => item.RelativePath)
            .ToArray();

        var summary = new AuditSummary
        {
            RepositoryRoot = repositoryRoot,
            ClientSharedRoot = clientSharedRoot,
            BootstrapRoot = bootstrapRoot,
            UiVariantRoot = uiVariantRoot,
            AssetCount = discovered.Count,
            TotalBytes = discovered.Sum(item => new FileInfo(item.AbsolutePath).Length),
            HasLanguageIni = relativeAssets.Contains("Language.ini", StringComparer.OrdinalIgnoreCase),
            HasMir2ConfigIni = relativeAssets.Contains("Mir2Config.ini", StringComparer.OrdinalIgnoreCase),
            HasBootstrapManifest = relativeAssets.Contains("bootstrap-assets.txt", StringComparer.OrdinalIgnoreCase),
        };

        summary.SoundAssetCount = relativeAssets.Count(path => path.StartsWith("Sound/", StringComparison.OrdinalIgnoreCase));
        summary.DataAssetCount = relativeAssets.Count(path => path.StartsWith("Data/", StringComparison.OrdinalIgnoreCase));
        summary.MapSampleCount = relativeAssets.Count(path => path.StartsWith("Map/", StringComparison.OrdinalIgnoreCase) && path.EndsWith(".map", StringComparison.OrdinalIgnoreCase));

        for (var i = 0; i < discovered.Count; i++)
        {
            summary.Assets.Add(new BootstrapAsset
            {
                RelativePath = discovered[i].RelativePath,
                SizeBytes = new FileInfo(discovered[i].AbsolutePath).Length,
                SourceRoot = discovered[i].SourceRoot,
            });
        }

        foreach (var asset in relativeAssets)
        {
            if (asset.StartsWith("Map/", StringComparison.OrdinalIgnoreCase) && asset.EndsWith(".map", StringComparison.OrdinalIgnoreCase))
            {
                summary.MapAudits.Add(AuditMap(Path.Combine(bootstrapRoot, asset), asset, relativeAssets));
            }
        }

        if (Directory.Exists(pcClientMapRoot))
        {
            var existingBootstrapMapNames = new HashSet<string>(
                summary.MapAudits.Select(audit => Path.GetFileName(audit.RelativePath)),
                StringComparer.OrdinalIgnoreCase);

            var candidateMapFiles = Directory.GetFiles(pcClientMapRoot, "*.map", SearchOption.TopDirectoryOnly)
                .Where(path => !existingBootstrapMapNames.Contains(Path.GetFileName(path)))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            foreach (var candidatePath in candidateMapFiles)
            {
                var fileInfo = new FileInfo(candidatePath);
                if (fileInfo.Length > 256 * 1024)
                    continue;

                var candidateAudit = AuditMap(candidatePath, $"Candidate/{fileInfo.Name}", relativeAssets);
                summary.CandidateMapAudits.Add(candidateAudit);
            }

            summary.CandidateMapAudits = summary.CandidateMapAudits
                .OrderBy(audit => audit.MissingLibraries.Count)
                .ThenBy(audit => audit.RequiredLibraries.Count)
                .ThenBy(audit => audit.FileSizeBytes)
                .ThenBy(audit => audit.RelativePath, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        summary.CandidateGroups = BuildCandidateGroups(summary.CandidateMapAudits);
        summary.MissingRecommendations = BuildRecommendations(summary);
        return summary;
    }

    private static List<CandidateGroup> BuildCandidateGroups(IReadOnlyCollection<MapAudit> candidates)
    {
        return candidates
            .GroupBy(
                audit => $"{BuildGroupKey(audit.RequiredLibraries)}|{BuildGroupKey(audit.MissingLibraries)}",
                StringComparer.Ordinal)
            .Select(group =>
            {
                var first = group.First();
                return new CandidateGroup
                {
                    RequiredLibraries = first.RequiredLibraries.ToList(),
                    MissingLibraries = first.MissingLibraries.ToList(),
                    Count = group.Count(),
                    SampleMaps = group.Select(item => item.RelativePath).OrderBy(item => item, StringComparer.OrdinalIgnoreCase).Take(8).ToList(),
                };
            })
            .OrderBy(group => group.MissingLibraries.Count)
            .ThenBy(group => group.RequiredLibraries.Count)
            .ThenByDescending(group => group.Count)
            .ThenBy(group => string.Join("|", group.SampleMaps), StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static MapAudit AuditMap(string absoluteMapPath, string relativeMapPath, IReadOnlyCollection<string> availableAssets)
    {
        var bytes = File.ReadAllBytes(absoluteMapPath);
        var mapType = DetectMapType(bytes);
        var dependency = mapType switch
        {
            MapType.Type0 => ParseType0(bytes),
            MapType.Type1 => ParseType1(bytes),
            MapType.Type2 => ParseType2Or3(bytes, isType3: false),
            MapType.Type3 => ParseType2Or3(bytes, isType3: true),
            MapType.Type100 => ParseType100(bytes),
            _ => new MapDependencyInfo
            {
                Supported = false,
                Width = 0,
                Height = 0,
            }
        };

        var audit = new MapAudit
        {
            RelativePath = relativeMapPath,
            TypeName = mapType.ToString(),
            Width = dependency.Width,
            Height = dependency.Height,
            FileSizeBytes = new FileInfo(absoluteMapPath).Length,
            Supported = dependency.Supported,
        };

        if (!dependency.Supported)
        {
            audit.Notes.Add("当前工具暂未支持该地图格式，无法静态分析依赖。");
            return audit;
        }

        foreach (var libraryIndex in dependency.RequiredLibraryIndices.OrderBy(index => index))
        {
            var relativeLibraryPath = ResolveLibraryPath(libraryIndex);
            if (string.IsNullOrWhiteSpace(relativeLibraryPath))
            {
                audit.UnknownLibraryIndices.Add(libraryIndex);
                continue;
            }

            audit.RequiredLibraries.Add(relativeLibraryPath);

            if (availableAssets.Contains(relativeLibraryPath, StringComparer.OrdinalIgnoreCase))
                audit.PresentLibraries.Add(relativeLibraryPath);
            else
                audit.MissingLibraries.Add(relativeLibraryPath);
        }

        if (dependency.RequiredLibraryIndices.Count == 0)
            audit.Notes.Add("未发现可绘制的地图图库索引。");

        return audit;
    }

    private static MapType DetectMapType(byte[] bytes)
    {
        if (bytes.Length < 20)
            return MapType.Unknown;

        if (bytes[2] == 0x43 && bytes[3] == 0x23)
            return MapType.Type100;

        if (bytes[0] == 0)
            return MapType.Type5;

        if (bytes[0] == 0x0F && bytes[5] == 0x53 && bytes[14] == 0x33)
            return MapType.Type6;

        if (bytes[0] == 0x15 && bytes[4] == 0x32 && bytes[6] == 0x41 && bytes[19] == 0x31)
            return MapType.Type4;

        if (bytes[0] == 0x10 && bytes[2] == 0x61 && bytes[7] == 0x31 && bytes[14] == 0x31)
            return MapType.Type1;

        if (((bytes[4] == 0x0F) || (bytes[4] == 0x03)) && bytes[18] == 0x0D && bytes[19] == 0x0A)
        {
            var width = BitConverter.ToInt16(bytes, 0);
            var height = BitConverter.ToInt16(bytes, 2);
            return bytes.Length > (52 + (width * height * 14)) ? MapType.Type3 : MapType.Type2;
        }

        if (bytes[0] == 0x0D && bytes[1] == 0x4C && bytes[7] == 0x20 && bytes[11] == 0x6D)
            return MapType.Type7;

        return MapType.Type0;
    }

    private static MapDependencyInfo ParseType0(byte[] bytes)
    {
        var info = new MapDependencyInfo
        {
            Supported = true,
            Width = BitConverter.ToInt16(bytes, 0),
            Height = BitConverter.ToInt16(bytes, 2),
        };

        var offset = 52;
        for (var x = 0; x < info.Width; x++)
        {
            for (var y = 0; y < info.Height; y++)
            {
                var backImage = BitConverter.ToInt16(bytes, offset);
                offset += 2;
                var middleImage = BitConverter.ToInt16(bytes, offset);
                offset += 2;
                var frontImage = BitConverter.ToInt16(bytes, offset);
                offset += 2;
                offset += 2;
                offset += 2;
                var frontIndex = bytes[offset++] + 2;
                offset += 1;

                if (backImage != 0)
                    info.RequiredLibraryIndices.Add(0);
                if (middleImage > 0)
                    info.RequiredLibraryIndices.Add(1);
                if (frontImage > 0)
                    info.RequiredLibraryIndices.Add(frontIndex);
            }
        }

        return info;
    }

    private static MapDependencyInfo ParseType1(byte[] bytes)
    {
        var offset = 21;
        var widthSeed = BitConverter.ToInt16(bytes, offset);
        offset += 2;
        var xor = BitConverter.ToInt16(bytes, offset);
        offset += 2;
        var heightSeed = BitConverter.ToInt16(bytes, offset);

        var info = new MapDependencyInfo
        {
            Supported = true,
            Width = widthSeed ^ xor,
            Height = heightSeed ^ xor,
        };

        offset = 54;
        for (var x = 0; x < info.Width; x++)
        {
            for (var y = 0; y < info.Height; y++)
            {
                var backImage = BitConverter.ToInt32(bytes, offset) ^ unchecked((int)0xAA38AA38);
                var middleImage = (short)(BitConverter.ToInt16(bytes, offset += 4) ^ xor);
                var frontImage = (short)(BitConverter.ToInt16(bytes, offset += 2) ^ xor);
                offset += 2;
                offset += 1;
                offset += 1;
                offset += 1;
                var frontIndex = bytes[++offset] + 2;
                offset += 1;
                offset += 1;
                offset += 1;

                if (backImage != 0)
                    info.RequiredLibraryIndices.Add(0);
                if (middleImage > 0)
                    info.RequiredLibraryIndices.Add(1);
                if (frontImage > 0)
                    info.RequiredLibraryIndices.Add(frontIndex);
            }
        }

        return info;
    }

    private static MapDependencyInfo ParseType2Or3(byte[] bytes, bool isType3)
    {
        var info = new MapDependencyInfo
        {
            Supported = true,
            Width = BitConverter.ToInt16(bytes, 0),
            Height = BitConverter.ToInt16(bytes, 2),
        };

        var offset = 52;
        for (var x = 0; x < info.Width; x++)
        {
            for (var y = 0; y < info.Height; y++)
            {
                var backImage = BitConverter.ToInt16(bytes, offset);
                offset += 2;
                var middleImage = BitConverter.ToInt16(bytes, offset);
                offset += 2;
                var frontImage = BitConverter.ToInt16(bytes, offset);
                offset += 2;
                offset += 1;
                offset += 1;
                offset += 1;
                var frontIndex = bytes[offset++] + 120;
                offset += 1;
                var backIndex = bytes[offset++] + 100;
                var middleIndex = bytes[offset++] + 110;

                if (isType3)
                    offset += 24;

                if (backImage != 0)
                    info.RequiredLibraryIndices.Add(backIndex);
                if (middleImage > 0)
                    info.RequiredLibraryIndices.Add(middleIndex);
                if (frontImage > 0)
                    info.RequiredLibraryIndices.Add(frontIndex);
            }
        }

        return info;
    }

    private static MapDependencyInfo ParseType100(byte[] bytes)
    {
        var info = new MapDependencyInfo
        {
            Supported = true,
            Width = BitConverter.ToInt16(bytes, 4),
            Height = BitConverter.ToInt16(bytes, 6),
        };

        var offset = 8;
        for (var x = 0; x < info.Width; x++)
        {
            for (var y = 0; y < info.Height; y++)
            {
                var backIndex = BitConverter.ToInt16(bytes, offset);
                offset += 2;
                var backImage = BitConverter.ToInt32(bytes, offset);
                offset += 4;
                var middleIndex = BitConverter.ToInt16(bytes, offset);
                offset += 2;
                var middleImage = BitConverter.ToInt16(bytes, offset);
                offset += 2;
                var frontIndex = BitConverter.ToInt16(bytes, offset);
                offset += 2;
                var frontImage = BitConverter.ToInt16(bytes, offset);
                offset += 2;
                offset += 11;

                if (backImage != 0 && backIndex >= 0)
                    info.RequiredLibraryIndices.Add(backIndex);
                if (middleImage > 0 && middleIndex >= 0)
                    info.RequiredLibraryIndices.Add(middleIndex);
                if (frontImage > 0 && frontIndex >= 0)
                    info.RequiredLibraryIndices.Add(frontIndex);
            }
        }

        return info;
    }

    private static string ResolveLibraryPath(int libraryIndex)
    {
        if (libraryIndex == 0)
            return "Data/Map/WemadeMir2/Tiles.Lib";

        if (libraryIndex == 1)
            return "Data/Map/WemadeMir2/SmTiles.Lib";

        if (libraryIndex == 2)
            return "Data/Map/WemadeMir2/Objects.Lib";

        if (libraryIndex >= 3 && libraryIndex <= 27)
            return $"Data/Map/WemadeMir2/Objects{libraryIndex - 1}.Lib";

        if (libraryIndex >= 100 && libraryIndex <= 109)
            return libraryIndex == 100
                ? "Data/Map/ShandaMir2/Tiles.Lib"
                : $"Data/Map/ShandaMir2/Tiles{libraryIndex - 98}.Lib";

        if (libraryIndex >= 110 && libraryIndex <= 119)
            return libraryIndex == 110
                ? "Data/Map/ShandaMir2/SmTiles.Lib"
                : $"Data/Map/ShandaMir2/SmTiles{libraryIndex - 108}.Lib";

        if (libraryIndex >= 120 && libraryIndex <= 150)
            return libraryIndex == 120
                ? "Data/Map/ShandaMir2/Objects.Lib"
                : $"Data/Map/ShandaMir2/Objects{libraryIndex - 119}.Lib";

        if (libraryIndex == 190)
            return "Data/Map/ShandaMir2/AniTiles1.Lib";

        return string.Empty;
    }

    private static List<string> BuildRecommendations(AuditSummary summary)
    {
        var recommendations = new List<string>();

        if (!summary.HasLanguageIni)
            recommendations.Add("BootstrapAssets 缺少 Language.ini，移动端首次启动无法从包内回填语言配置。");

        if (!summary.HasMir2ConfigIni)
            recommendations.Add("BootstrapAssets 缺少 Mir2Config.ini，移动端首次启动无法从包内回填默认配置。");

        foreach (var mapAudit in summary.MapAudits.Where(audit => audit.MissingLibraries.Count > 0))
        {
            recommendations.Add($"地图样本 {mapAudit.RelativePath} 仍缺少 {mapAudit.MissingLibraries.Count} 个图库：{string.Join(", ", mapAudit.MissingLibraries.Take(6))}{BuildTail(mapAudit.MissingLibraries.Count)}");
        }

        var zeroMissingCandidates = summary.CandidateMapAudits
            .Where(audit => audit.Supported && audit.MissingLibraries.Count == 0)
            .Take(5)
            .ToArray();

        if (zeroMissingCandidates.Length > 0)
        {
            recommendations.Add($"可优先纳入的零缺口候选地图样本：{string.Join(", ", zeroMissingCandidates.Select(audit => audit.RelativePath))}");
        }

        var nextMissingGroup = summary.CandidateGroups.FirstOrDefault(group => group.MissingLibraries.Count > 0);
        if (nextMissingGroup != null)
        {
            recommendations.Add($"下一批有缺口的候选地图可优先按分组补库：缺少 {string.Join(", ", nextMissingGroup.MissingLibraries)}，可覆盖 {nextMissingGroup.Count} 张地图样本。");
        }

        if (recommendations.Count == 0)
            recommendations.Add("当前 bootstrap 样本资源未发现新的显式缺口，但这不代表完整移动端资源迁移已完成。");

        return recommendations;
    }

    private static void PrintSummary(AuditSummary summary)
    {
        Console.WriteLine("[OK] MobileBootstrapAudit 完成");
        Console.WriteLine($"- BootstrapRoot: {summary.BootstrapRoot}");
        Console.WriteLine($"- ClientSharedRoot: {summary.ClientSharedRoot}");
        Console.WriteLine($"- UiVariantRoot: {summary.UiVariantRoot}");
        Console.WriteLine($"- AssetCount: {summary.AssetCount}");
        Console.WriteLine($"- TotalSizeMB: {Math.Round(summary.TotalBytes / 1024d / 1024d, 2)}");
        Console.WriteLine($"- BootstrapAssetCount: {summary.Assets.Count(asset => asset.SourceRoot.Equals(AssetSourceBootstrap, StringComparison.OrdinalIgnoreCase))}");
        Console.WriteLine($"- ClientAssetCount: {summary.Assets.Count(asset => asset.SourceRoot.Equals(AssetSourceClientAssets, StringComparison.OrdinalIgnoreCase))}");
        Console.WriteLine($"- HasLanguageIni: {summary.HasLanguageIni}");
        Console.WriteLine($"- HasMir2ConfigIni: {summary.HasMir2ConfigIni}");
        Console.WriteLine($"- SoundAssetCount: {summary.SoundAssetCount}");
        Console.WriteLine($"- DataAssetCount: {summary.DataAssetCount}");
        Console.WriteLine($"- MapSampleCount: {summary.MapSampleCount}");

        foreach (var audit in summary.MapAudits)
        {
            Console.WriteLine($"- Map: {audit.RelativePath} | Type={audit.TypeName} | {audit.Width}x{audit.Height} | Required={audit.RequiredLibraries.Count} | Missing={audit.MissingLibraries.Count}");
            if (audit.MissingLibraries.Count > 0)
                Console.WriteLine($"  - Missing: {string.Join(", ", audit.MissingLibraries)}");
        }

        if (summary.CandidateMapAudits.Count > 0)
        {
            var displayedCandidates = summary.CandidateMapAudits.Take(20).ToArray();
            Console.WriteLine($"- 候选地图样本: 显示前 {displayedCandidates.Length} / 共 {summary.CandidateMapAudits.Count}");
            foreach (var audit in displayedCandidates)
            {
                Console.WriteLine($"  - {audit.RelativePath} | {Math.Round(audit.FileSizeBytes / 1024d, 2)} KB | Required={audit.RequiredLibraries.Count} | Missing={audit.MissingLibraries.Count}");
            }
        }

        if (summary.CandidateGroups.Count > 0)
        {
            Console.WriteLine("- 候选依赖分组:");
            foreach (var group in summary.CandidateGroups.Take(8))
            {
                Console.WriteLine($"  - Count={group.Count} | Missing={group.MissingLibraries.Count} | Required={string.Join(", ", group.RequiredLibraries)}");
            }
        }
    }

    private static void WriteOutputs(AuditSummary summary, string markdownOutputPath)
    {
        var fullMarkdownPath = Path.GetFullPath(markdownOutputPath);
        var outputDirectory = Path.GetDirectoryName(fullMarkdownPath);
        if (string.IsNullOrWhiteSpace(outputDirectory))
            throw new InvalidOperationException("无法确定输出目录。");

        Directory.CreateDirectory(outputDirectory);

        File.WriteAllText(fullMarkdownPath, BuildMarkdown(summary), Utf8NoBom);
        File.WriteAllText(Path.ChangeExtension(fullMarkdownPath, ".json"), JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }), Utf8NoBom);

        var packagePlan = BuildPackagePlan(summary);
        var packagePlanMarkdownPath = GetPackagePlanMarkdownPath(fullMarkdownPath);
        File.WriteAllText(packagePlanMarkdownPath, BuildPackagePlanMarkdown(packagePlan), Utf8NoBom);
        File.WriteAllText(Path.ChangeExtension(packagePlanMarkdownPath, ".json"), JsonSerializer.Serialize(packagePlan, new JsonSerializerOptions { WriteIndented = true }), Utf8NoBom);
    }

    private static void WriteBootstrapManifest(AuditSummary summary)
    {
        var manifestPath = Path.Combine(summary.BootstrapRoot, "bootstrap-assets.txt");
        var packagePlan = BuildPackagePlan(summary);
        File.WriteAllText(manifestPath, BuildBootstrapManifest(summary), Utf8NoBom);
        File.WriteAllText(Path.Combine(summary.BootstrapRoot, RuntimePackageManifestFileName), JsonSerializer.Serialize(packagePlan, new JsonSerializerOptions { WriteIndented = true }), Utf8NoBom);
        WriteRuntimePackageManifests(summary.BootstrapRoot, packagePlan);
    }

    private static string BuildBootstrapManifest(AuditSummary summary)
    {
        var builder = new StringBuilder();
        builder.AppendLine("移动端 bootstrap 资源清单（阶段性）");
        builder.AppendLine();
        builder.AppendLine("当前目录仅收口“登录界面与基础启动链路”所需的最小资源子集，用于：");
        builder.AppendLine("- Android / iOS 壳子项目的包内资源声明");
        builder.AppendLine("- ClientResourceLayout 在缺文件时的包内回填");
        builder.AppendLine();
        builder.AppendLine("当前已纳入：");
        foreach (var asset in summary.Assets
                     .Where(asset => asset.SourceRoot.Equals(AssetSourceBootstrap, StringComparison.OrdinalIgnoreCase))
                     .Where(asset => !asset.RelativePath.Equals("bootstrap-assets.txt", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(asset => asset.RelativePath, StringComparer.OrdinalIgnoreCase))
            builder.AppendLine($"- {asset.RelativePath}");
        builder.AppendLine();
        builder.AppendLine("当前未纳入：");
        builder.AppendLine("- 全量 Data/Map/Sound 资源");
        builder.AppendLine("- 进图后的地图与大批量图片库");
        builder.AppendLine("- 地图样本对应的大型 `Data/Map/*` 地图库全集");
        builder.AppendLine("- 更多地图分支库（当前样本暂未验证需要，但后续地图很可能需要）");
        builder.AppendLine("- 真机安装验证所需的完整资源集");
        builder.AppendLine();
        builder.AppendLine("因此该目录只代表“移动端启动资源 bootstrap 的主项目化”，不代表资源迁移已完成。");
        builder.AppendLine();
        builder.AppendLine("当前用途补充：");
        builder.AppendLine("- Android / iOS 壳子项目会递归打包本目录");
        builder.AppendLine("- Client_MonoGame.Shared 的 Windows 输出也会复制本目录");
        builder.AppendLine("- ClientResourceLayout 在资源缺失时会把本目录作为包内回填候选之一");
        builder.AppendLine("- 运行期 `Mir2Config.ini` / `Language.ini` 会优先回填到 `Cache/Mobile/Runtime/`");
        return builder.ToString();
    }

    private static void WriteRuntimePackageManifests(string bootstrapRoot, PackagePlan packagePlan)
    {
        var packageDirectory = Path.Combine(bootstrapRoot, RuntimePackageManifestDirectoryName);
        Directory.CreateDirectory(packageDirectory);

        var activeManifestNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < packagePlan.Packs.Count; i++)
        {
            var pack = packagePlan.Packs[i];
            var manifestName = Path.GetFileName(pack.ManifestPath);
            activeManifestNames.Add(manifestName);

            var runtimeManifest = new RuntimePackageManifest
            {
                Name = pack.Name,
                Kind = pack.Kind,
                Description = pack.Description,
                AssetCount = pack.AssetCount,
                TotalBytes = pack.TotalBytes,
                Assets = pack.Assets,
            };

            File.WriteAllText(
                Path.Combine(packageDirectory, manifestName),
                JsonSerializer.Serialize(runtimeManifest, new JsonSerializerOptions { WriteIndented = true }),
                Utf8NoBom);
        }

        foreach (var staleFile in Directory.GetFiles(packageDirectory, "*.json", SearchOption.TopDirectoryOnly))
        {
            if (!activeManifestNames.Contains(Path.GetFileName(staleFile)))
                File.Delete(staleFile);
        }
    }

    private static string GetPackagePlanMarkdownPath(string fullMarkdownPath)
    {
        var directory = Path.GetDirectoryName(fullMarkdownPath) ?? string.Empty;
        var fileName = Path.GetFileName(fullMarkdownPath);
        if (fileName.Contains("资源覆盖审计", StringComparison.OrdinalIgnoreCase))
            fileName = fileName.Replace("资源覆盖审计", "分包建议", StringComparison.OrdinalIgnoreCase);
        else
            fileName = Path.GetFileNameWithoutExtension(fullMarkdownPath) + "-pack-plan.md";

        return Path.Combine(directory, fileName);
    }

    private static PackagePlan BuildPackagePlan(AuditSummary summary)
    {
        var plan = new PackagePlan
        {
            RepositoryRoot = summary.RepositoryRoot,
            BootstrapRoot = summary.BootstrapRoot,
        };

        var assets = summary.Assets
            .Where(asset => !asset.RelativePath.Equals("bootstrap-assets.txt", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var coreAssets = assets
            .Where(asset => IsCoreBootstrapAsset(asset.RelativePath))
            .OrderBy(asset => asset.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (coreAssets.Length > 0)
            plan.Packs.Add(CreatePack("core-startup", "core", "启动核心资源与基础图库", coreAssets));

        var uiVariantPrefix = $"Assets/UI/{DefaultUiVariantName}/";
        var uiAssets = assets
            .Where(asset => asset.RelativePath.StartsWith(uiVariantPrefix, StringComparison.OrdinalIgnoreCase) && IsPackagedUiAsset(asset.RelativePath))
            .OrderBy(asset => asset.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (uiAssets.Length > 0)
        {
            plan.Packs.Add(CreatePack(
                "fui-retro",
                "ui",
                $"FairyGUI {DefaultUiVariantName} UI（publish 产物：Assets/UI/{DefaultUiVariantName}）",
                uiAssets));
        }

        var dataLibraryPacks = assets
            .Where(asset => IsOptionalDataLibraryAsset(asset.RelativePath))
            .GroupBy(asset => asset.RelativePath, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase);

        foreach (var group in dataLibraryPacks)
        {
            plan.Packs.Add(CreatePack(
                $"data-{SanitizePackName(Path.GetFileNameWithoutExtension(group.Key))}",
                "data-library",
                group.Key,
                group.OrderBy(asset => asset.RelativePath, StringComparer.OrdinalIgnoreCase).ToArray()));
        }

        var libraryPacks = assets
            .Where(asset => IsOptionalMapLibraryAsset(asset.RelativePath))
            .GroupBy(asset => asset.RelativePath, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase);

        foreach (var group in libraryPacks)
        {
            plan.Packs.Add(CreatePack(
                $"lib-{SanitizePackName(Path.GetFileNameWithoutExtension(group.Key))}",
                "map-library",
                group.Key,
                group.OrderBy(asset => asset.RelativePath, StringComparer.OrdinalIgnoreCase).ToArray()));
        }

        var mapPacks = assets
            .Where(asset => asset.RelativePath.StartsWith("Map/", StringComparison.OrdinalIgnoreCase)
                         && asset.RelativePath.EndsWith(".map", StringComparison.OrdinalIgnoreCase))
            .GroupBy(asset => BuildMapPackKey(asset.RelativePath), StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase);

        foreach (var group in mapPacks)
        {
            plan.Packs.Add(CreatePack(
                $"maps-{SanitizePackName(group.Key)}",
                "map-samples",
                $"地图样本组：{group.Key}",
                group.OrderBy(asset => asset.RelativePath, StringComparer.OrdinalIgnoreCase).ToArray()));
        }

        plan.Packs = plan.Packs
            .OrderBy(pack => GetPackKindOrder(pack.Kind))
            .ThenByDescending(pack => pack.TotalBytes)
            .ThenBy(pack => pack.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        plan.TotalAssets = plan.Packs.Sum(pack => pack.AssetCount);
        plan.TotalBytes = plan.Packs.Sum(pack => pack.TotalBytes);

        return plan;
    }

    private static PackagePlanPack CreatePack(string name, string kind, string description, BootstrapAsset[] assets)
    {
        return new PackagePlanPack
        {
            Name = name,
            Kind = kind,
            Description = description,
            AssetCount = assets.Length,
            TotalBytes = assets.Sum(asset => asset.SizeBytes),
            ManifestPath = BuildRuntimePackageManifestRelativePath(name),
            InstallRootHint = BuildRuntimePackageInstallRootHint(name),
            Assets = assets.Select(asset => asset.RelativePath).ToList(),
        };
    }

    private static string BuildPackagePlanMarkdown(PackagePlan plan)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# BootstrapAssets 分包建议");
        builder.AppendLine();
        builder.AppendLine($"- RepositoryRoot: `{plan.RepositoryRoot}`");
        builder.AppendLine($"- BootstrapRoot: `{plan.BootstrapRoot}`");
        builder.AppendLine($"- TotalAssets: **{plan.TotalAssets}**");
        builder.AppendLine($"- TotalSizeMB: **{Math.Round(plan.TotalBytes / 1024d / 1024d, 2)}**");
        builder.AppendLine($"- PackCount: **{plan.Packs.Count}**");
        builder.AppendLine();
        builder.AppendLine("## 说明");
        builder.AppendLine();
        builder.AppendLine("- `core`：建议随安装包首发的启动核心资源与基础图库。");
        builder.AppendLine("- `ui`：FairyGUI publish 成品 UI 资源包（当前仅接入复古，Micro 模式下默认走预登录分包安装）。");
        builder.AppendLine("- `data-library`：建议按库独立分包的非地图 `Data/*.Lib` 资源。");
        builder.AppendLine("- `map-library`：建议按库独立分包或远程包管理的地图对象/地表扩展库。");
        builder.AppendLine("- `map-samples`：建议按命名前缀分组的地图样本包，可作为后续按区块或玩法场景拆包的初始基线。");
        builder.AppendLine("- 本文件只提供“静态分包建议”，不代表分包运行时链路已完成。");
        builder.AppendLine();
        builder.AppendLine("## 汇总");
        builder.AppendLine();

        foreach (var group in plan.Packs.GroupBy(pack => pack.Kind).OrderBy(group => GetPackKindOrder(group.Key)))
        {
            builder.AppendLine($"- `{group.Key}` | Packs={group.Count()} | Assets={group.Sum(item => item.AssetCount)} | SizeMB={Math.Round(group.Sum(item => item.TotalBytes) / 1024d / 1024d, 2)}");
        }

        builder.AppendLine();
        builder.AppendLine("## 分包清单");
        builder.AppendLine();

        foreach (var pack in plan.Packs)
        {
            builder.AppendLine($"### `{pack.Name}`");
            builder.AppendLine();
            builder.AppendLine($"- Kind: `{pack.Kind}`");
            builder.AppendLine($"- Description: {pack.Description}");
            builder.AppendLine($"- AssetCount: **{pack.AssetCount}**");
            builder.AppendLine($"- TotalSizeMB: **{Math.Round(pack.TotalBytes / 1024d / 1024d, 2)}**");
            builder.AppendLine($"- Manifest: `{pack.ManifestPath}`");
            builder.AppendLine($"- InstallRootHint: `{pack.InstallRootHint}`");
            builder.AppendLine($"- SampleAssets: {string.Join(", ", pack.Assets.Take(8).Select(asset => $"`{asset}`"))}");
            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static bool IsCoreBootstrapAsset(string relativePath)
    {
        if (relativePath.Equals("Language.ini", StringComparison.OrdinalIgnoreCase)
            || relativePath.Equals("Mir2Config.ini", StringComparison.OrdinalIgnoreCase)
            || relativePath.StartsWith("Sound/", StringComparison.OrdinalIgnoreCase)
            || relativePath.Equals("Content/hm.ttf", StringComparison.OrdinalIgnoreCase))
            return true;

        return relativePath.Equals("Data/ChrSel.Lib", StringComparison.OrdinalIgnoreCase)
            || relativePath.Equals("Data/Prguse.Lib", StringComparison.OrdinalIgnoreCase)
            || relativePath.Equals("Data/Title.Lib", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPackagedUiAsset(string relativePath)
    {
        return !UiAssetAliasTargets.ContainsKey(relativePath);
    }

    private static bool IsOptionalDataLibraryAsset(string relativePath)
    {
        return relativePath.StartsWith("Data/", StringComparison.OrdinalIgnoreCase)
            && !relativePath.StartsWith("Data/Map/", StringComparison.OrdinalIgnoreCase)
            && relativePath.EndsWith(".Lib", StringComparison.OrdinalIgnoreCase)
            && !IsCoreBootstrapAsset(relativePath);
    }

    private static bool IsOptionalMapLibraryAsset(string relativePath)
    {
        return relativePath.StartsWith("Data/Map/", StringComparison.OrdinalIgnoreCase)
            && relativePath.EndsWith(".Lib", StringComparison.OrdinalIgnoreCase)
            && !IsCoreBootstrapAsset(relativePath);
    }

    private static string BuildMapPackKey(string relativePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(relativePath);
        if (string.IsNullOrWhiteSpace(fileName))
            return "misc";

        var end = fileName.Length - 1;
        while (end >= 0 && char.IsDigit(fileName[end]))
            end--;

        var trimmed = fileName.Substring(0, end + 1).TrimEnd('_', '-', ' ');
        if (string.IsNullOrWhiteSpace(trimmed))
            return "numeric";

        return trimmed;
    }

    private static string SanitizePackName(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch))
                builder.Append(char.ToLowerInvariant(ch));
            else
                builder.Append('-');
        }

        var sanitized = builder.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(sanitized) ? "misc" : sanitized;
    }

    private static int GetPackKindOrder(string kind)
    {
        return kind switch
        {
            "core" => 0,
            "ui" => 1,
            "data-library" => 2,
            "map-library" => 3,
            "map-samples" => 4,
            _ => 5,
        };
    }

    private static string BuildRuntimePackageManifestRelativePath(string packName)
    {
        return $"{RuntimePackageManifestDirectoryName}/{packName}.json";
    }

    private static string BuildRuntimePackageInstallRootHint(string packName)
    {
        return $"Cache/Mobile/Packages/{packName}/";
    }

    private static string BuildMarkdown(AuditSummary summary)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# MobileBootstrapAudit 报告");
        builder.AppendLine();
        builder.AppendLine($"- RepositoryRoot: `{summary.RepositoryRoot}`");
        builder.AppendLine($"- BootstrapRoot: `{summary.BootstrapRoot}`");
        builder.AppendLine($"- AssetCount: **{summary.AssetCount}**");
        builder.AppendLine($"- TotalSizeMB: **{Math.Round(summary.TotalBytes / 1024d / 1024d, 2)}**");
        builder.AppendLine($"- HasLanguageIni: **{summary.HasLanguageIni}**");
        builder.AppendLine($"- HasMir2ConfigIni: **{summary.HasMir2ConfigIni}**");
        builder.AppendLine($"- HasBootstrapManifest: **{summary.HasBootstrapManifest}**");
        builder.AppendLine($"- SoundAssetCount: **{summary.SoundAssetCount}**");
        builder.AppendLine($"- DataAssetCount: **{summary.DataAssetCount}**");
        builder.AppendLine($"- MapSampleCount: **{summary.MapSampleCount}**");
        builder.AppendLine();

        builder.AppendLine("## 建议");
        builder.AppendLine();
        foreach (var recommendation in summary.MissingRecommendations)
            builder.AppendLine($"- {recommendation}");

        builder.AppendLine();
        builder.AppendLine("## 地图样本审计");
        builder.AppendLine();
        if (summary.MapAudits.Count == 0)
        {
            builder.AppendLine("- 无地图样本。");
        }
        else
        {
            foreach (var audit in summary.MapAudits.OrderBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase))
            {
                builder.AppendLine($"### `{audit.RelativePath}`");
                builder.AppendLine();
                builder.AppendLine($"- Type: `{audit.TypeName}`");
                builder.AppendLine($"- Size: `{audit.Width} x {audit.Height}`");
                builder.AppendLine($"- Supported: `{audit.Supported}`");
                builder.AppendLine($"- RequiredLibraries: **{audit.RequiredLibraries.Count}**");
                builder.AppendLine($"- MissingLibraries: **{audit.MissingLibraries.Count}**");

                if (audit.RequiredLibraries.Count > 0)
                {
                    builder.AppendLine("- 已识别依赖:");
                    foreach (var item in audit.RequiredLibraries)
                        builder.AppendLine($"  - `{item}`");
                }

                if (audit.MissingLibraries.Count > 0)
                {
                    builder.AppendLine("- 缺失依赖:");
                    foreach (var item in audit.MissingLibraries)
                        builder.AppendLine($"  - `{item}`");
                }

                if (audit.UnknownLibraryIndices.Count > 0)
                {
                    builder.AppendLine("- 未映射的图库索引:");
                    foreach (var item in audit.UnknownLibraryIndices)
                        builder.AppendLine($"  - `{item}`");
                }

                if (audit.Notes.Count > 0)
                {
                    builder.AppendLine("- 备注:");
                    foreach (var item in audit.Notes)
                        builder.AppendLine($"  - {item}");
                }

                builder.AppendLine();
            }
        }

        builder.AppendLine("## 候选地图");
        builder.AppendLine();
        if (summary.CandidateMapAudits.Count == 0)
        {
            builder.AppendLine("- 无候选地图。");
        }
        else
        {
            var displayedCandidates = summary.CandidateMapAudits.Take(20).ToArray();
            builder.AppendLine($"- 当前共 {summary.CandidateMapAudits.Count} 个候选地图，以下仅展示前 {displayedCandidates.Length} 个。");
            foreach (var audit in displayedCandidates)
            {
                builder.AppendLine($"- `{audit.RelativePath}` | {Math.Round(audit.FileSizeBytes / 1024d, 2)} KB | Required={audit.RequiredLibraries.Count} | Missing={audit.MissingLibraries.Count}");
                if (audit.RequiredLibraries.Count > 0)
                    builder.AppendLine($"  - Required: {string.Join(", ", audit.RequiredLibraries)}");
                if (audit.MissingLibraries.Count > 0)
                    builder.AppendLine($"  - Missing: {string.Join(", ", audit.MissingLibraries)}");
            }
        }

        builder.AppendLine();
        builder.AppendLine("## 候选依赖分组");
        builder.AppendLine();
        if (summary.CandidateGroups.Count == 0)
        {
            builder.AppendLine("- 无候选依赖分组。");
        }
        else
        {
            foreach (var group in summary.CandidateGroups)
            {
                builder.AppendLine($"- Count={group.Count} | Missing={group.MissingLibraries.Count}");
                builder.AppendLine($"  - Required: {string.Join(", ", group.RequiredLibraries)}");
                if (group.MissingLibraries.Count > 0)
                    builder.AppendLine($"  - Missing: {string.Join(", ", group.MissingLibraries)}");
                builder.AppendLine($"  - SampleMaps: {string.Join(", ", group.SampleMaps)}");
            }
        }

        builder.AppendLine();
        builder.AppendLine("## 资源清单");
        builder.AppendLine();
        foreach (var asset in summary.Assets.OrderBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase))
            builder.AppendLine($"- `{asset.RelativePath}` ({Math.Round(asset.SizeBytes / 1024d, 2)} KB)");

        return builder.ToString();
    }

    private static string NormalizePath(string path)
    {
        return path.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
    }

    private static string BuildTail(int count)
    {
        return count > 6 ? $" 等 {count} 项" : string.Empty;
    }

    private static string BuildGroupKey(IEnumerable<string> values)
    {
        return string.Join("|", values.OrderBy(value => value, StringComparer.OrdinalIgnoreCase));
    }

    private static void EnsureDirectory(string path, string label)
    {
        if (!Directory.Exists(path))
            throw new DirectoryNotFoundException($"找不到目录 {label}: {path}");
    }

    private sealed class Options
    {
        public string RepositoryRoot { get; private set; }
        public string MarkdownOutputPath { get; private set; }
        public int ImportZeroGapCount { get; private set; }
        public bool SyncManifest { get; private set; }

        public static Options Parse(string[] args)
        {
            var result = new Options
            {
                RepositoryRoot = ResolveRepositoryRoot(Environment.CurrentDirectory),
                MarkdownOutputPath = string.Empty,
                ImportZeroGapCount = 0,
                SyncManifest = false,
            };

            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i] ?? string.Empty;
                if (string.IsNullOrWhiteSpace(arg))
                    continue;

                if (arg.Equals("--repo", StringComparison.OrdinalIgnoreCase))
                {
                    result.RepositoryRoot = Path.GetFullPath(ReadNextValue(args, ref i, "--repo"));
                    continue;
                }

                if (arg.Equals("--write", StringComparison.OrdinalIgnoreCase))
                {
                    result.MarkdownOutputPath = Path.GetFullPath(ReadNextValue(args, ref i, "--write"));
                    continue;
                }

                if (arg.Equals("--import-zero-gap", StringComparison.OrdinalIgnoreCase))
                {
                    if (!int.TryParse(ReadNextValue(args, ref i, "--import-zero-gap"), out var importCount) || importCount < 0)
                        throw new ArgumentException("--import-zero-gap 必须是非负整数。");

                    result.ImportZeroGapCount = importCount;
                    continue;
                }

                if (arg.Equals("--sync-manifest", StringComparison.OrdinalIgnoreCase))
                {
                    result.SyncManifest = true;
                    continue;
                }

                if (arg.Equals("-h", StringComparison.OrdinalIgnoreCase)
                    || arg.Equals("--help", StringComparison.OrdinalIgnoreCase)
                    || arg.Equals("/?", StringComparison.OrdinalIgnoreCase))
                {
                    PrintUsage();
                    Environment.Exit(0);
                }

                throw new ArgumentException($"未知参数：{arg}");
            }

            result.RepositoryRoot = ResolveRepositoryRoot(result.RepositoryRoot);
            return result;
        }

        private static string ReadNextValue(string[] args, ref int index, string optionName)
        {
            if (index + 1 >= args.Length)
                throw new ArgumentException($"{optionName} 缺少值。");

            index++;
            return args[index] ?? string.Empty;
        }

        private static string ResolveRepositoryRoot(string startPath)
        {
            var current = new DirectoryInfo(Path.GetFullPath(startPath));
            while (current != null)
            {
                if (File.Exists(Path.Combine(current.FullName, "Legend of Mir.sln")))
                    return current.FullName;

                current = current.Parent;
            }

            throw new DirectoryNotFoundException("无法从当前目录解析仓库根目录，请使用 --repo 指定。");
        }

        private static void PrintUsage()
        {
            Console.WriteLine("MobileBootstrapAudit 用法:");
            Console.WriteLine("  dotnet run --project Tools/MobileBootstrapAudit -- [--repo <仓库根目录>] [--write <输出Markdown路径>] [--import-zero-gap <数量>] [--sync-manifest]");
        }
    }

    private sealed class AuditSummary
    {
        public string RepositoryRoot { get; set; } = string.Empty;
        public string ClientSharedRoot { get; set; } = string.Empty;
        public string BootstrapRoot { get; set; } = string.Empty;
        public string UiVariantRoot { get; set; } = string.Empty;
        public int AssetCount { get; set; }
        public long TotalBytes { get; set; }
        public bool HasLanguageIni { get; set; }
        public bool HasMir2ConfigIni { get; set; }
        public bool HasBootstrapManifest { get; set; }
        public int SoundAssetCount { get; set; }
        public int DataAssetCount { get; set; }
        public int MapSampleCount { get; set; }
        public List<BootstrapAsset> Assets { get; } = new List<BootstrapAsset>();
        public List<MapAudit> MapAudits { get; } = new List<MapAudit>();
        public List<MapAudit> CandidateMapAudits { get; set; } = new List<MapAudit>();
        public List<CandidateGroup> CandidateGroups { get; set; } = new List<CandidateGroup>();
        public List<string> MissingRecommendations { get; set; } = new List<string>();
    }

    private sealed class BootstrapAsset
    {
        public string RelativePath { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public string SourceRoot { get; set; } = string.Empty;
    }

    private sealed class MapAudit
    {
        public string RelativePath { get; set; } = string.Empty;
        public string TypeName { get; set; } = string.Empty;
        public int Width { get; set; }
        public int Height { get; set; }
        public long FileSizeBytes { get; set; }
        public bool Supported { get; set; }
        public List<string> RequiredLibraries { get; } = new List<string>();
        public List<string> PresentLibraries { get; } = new List<string>();
        public List<string> MissingLibraries { get; } = new List<string>();
        public List<int> UnknownLibraryIndices { get; } = new List<int>();
        public List<string> Notes { get; } = new List<string>();
    }

    private sealed class CandidateGroup
    {
        public int Count { get; set; }
        public List<string> RequiredLibraries { get; set; } = new List<string>();
        public List<string> MissingLibraries { get; set; } = new List<string>();
        public List<string> SampleMaps { get; set; } = new List<string>();
    }

    private sealed class PackagePlan
    {
        public string RepositoryRoot { get; set; } = string.Empty;
        public string BootstrapRoot { get; set; } = string.Empty;
        public int TotalAssets { get; set; }
        public long TotalBytes { get; set; }
        public List<PackagePlanPack> Packs { get; set; } = new List<PackagePlanPack>();
    }

    private sealed class PackagePlanPack
    {
        public string Name { get; set; } = string.Empty;
        public string Kind { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public int AssetCount { get; set; }
        public long TotalBytes { get; set; }
        public string ManifestPath { get; set; } = string.Empty;
        public string InstallRootHint { get; set; } = string.Empty;
        public List<string> Assets { get; set; } = new List<string>();
    }

    private sealed class RuntimePackageManifest
    {
        public string Name { get; set; } = string.Empty;
        public string Kind { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public int AssetCount { get; set; }
        public long TotalBytes { get; set; }
        public string ManifestPath { get; set; } = string.Empty;
        public string InstallRootHint { get; set; } = string.Empty;
        public List<string> Assets { get; set; } = new List<string>();
    }

    private sealed class MapDependencyInfo
    {
        public bool Supported { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public HashSet<int> RequiredLibraryIndices { get; } = new HashSet<int>();
    }

    private enum MapType
    {
        Unknown,
        Type0,
        Type1,
        Type2,
        Type3,
        Type4,
        Type5,
        Type6,
        Type7,
        Type100,
    }
}
