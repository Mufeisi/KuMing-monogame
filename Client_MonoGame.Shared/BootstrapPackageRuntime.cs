using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Microsoft.Xna.Framework;

namespace MonoShare
{
    public static class BootstrapPackageRuntime
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        public static BootstrapPackageRuntimeOverview LoadOverview(
            bool refreshState = false,
            bool reloadBootstrapMetadata = false,
            bool processPendingRequests = false)
        {
            if (reloadBootstrapMetadata)
                ClientResourceLayout.ReloadBootstrapMetadata();

            if (processPendingRequests)
            {
                ClientResourceLayout.ProcessPendingPackageRequestsNow();
            }
            else if (refreshState)
            {
                ClientResourceLayout.RefreshPackageStateSnapshot();
            }

            BootstrapPackageManifestView declared = LoadDeclaredPackages();
            BootstrapPackageStateSnapshotView state = LoadStateSnapshot();
            BootstrapMissingPackageQueueView queue = LoadMissingQueue();

            return BuildOverview(declared, state, queue);
        }

        public static BootstrapPackageRuntimeOverview RefreshOverview(
            bool reloadBootstrapMetadata = false,
            bool processPendingRequests = true)
        {
            return LoadOverview(
                refreshState: true,
                reloadBootstrapMetadata: reloadBootstrapMetadata,
                processPendingRequests: processPendingRequests);
        }

        public static string BuildDiagnosticsReport(
            bool refreshState = false,
            bool reloadBootstrapMetadata = false,
            bool processPendingRequests = false,
            int packageLimit = 12,
            int requestLimit = 12)
        {
            BootstrapPackageRuntimeOverview overview = LoadOverview(
                refreshState: refreshState,
                reloadBootstrapMetadata: reloadBootstrapMetadata,
                processPendingRequests: processPendingRequests);

            var builder = new StringBuilder();
            string generatedAtUtc = string.IsNullOrWhiteSpace(overview.State.GeneratedAtUtc)
                ? DateTime.UtcNow.ToString("o")
                : overview.State.GeneratedAtUtc;

            builder.AppendLine("Bootstrap 分包运行时诊断");
            builder.AppendLine($"生成时间(UTC)：{generatedAtUtc}");
            builder.AppendLine($"客户端根目录：{ClientResourceLayout.ClientRoot}");
            builder.AppendLine($"运行时目录：{ClientResourceLayout.RuntimeRoot}");
            builder.AppendLine($"声明包数：{overview.Summary.DeclaredPackageCount}，状态包数：{overview.Summary.StatePackageCount}");
            builder.AppendLine($"包状态：hydrated={overview.Summary.HydratedPackageCount}，staged={overview.Summary.StagedPackageCount}，partial={overview.Summary.PartialPackageCount}，declared={overview.Summary.PendingPackageCount}");
            builder.AppendLine($"请求队列：pending={overview.Summary.PendingRequestCount}，resolved={overview.Summary.ResolvedRequestCount}，涉及包={overview.Summary.PackagesWithPendingRequests}");
            builder.AppendLine();
            builder.AppendLine($"包摘要（最多 {Math.Max(1, packageLimit)} 项）：");

            BootstrapPackageStateView[] packages = overview.State.Packages
                .OrderByDescending(item => string.Equals(item.Name, "core-startup", StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(item => item.PendingRequestCount)
                .ThenBy(item => item.Status, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .Take(Math.Max(1, packageLimit))
                .ToArray();

            if (packages.Length == 0)
            {
                builder.AppendLine("- 当前没有可用的包状态。");
            }
            else
            {
                for (int i = 0; i < packages.Length; i++)
                {
                    BootstrapPackageStateView pack = packages[i];
                    builder.AppendLine($"- {pack.Name} | 状态={pack.Status} | hydrated={pack.HydratedAssetCount}/{pack.AssetCount} | staged={pack.StagedAssetCount} | pending={pack.PendingRequestCount} | 大小={FormatBytes(pack.TotalBytes)}");

                    if (pack.PendingResourcesSample.Count > 0)
                        builder.AppendLine($"  PendingSample: {string.Join(", ", pack.PendingResourcesSample)}");

                    if (pack.MissingAssetsSample.Count > 0)
                        builder.AppendLine($"  MissingSample: {string.Join(", ", pack.MissingAssetsSample)}");
                }
            }

            builder.AppendLine();
            builder.AppendLine($"待处理资源请求（最多 {Math.Max(1, requestLimit)} 项）：");

            BootstrapMissingPackageRequestView[] pendingRequests = overview.Queue.Requests
                .Where(item => item != null && !string.Equals(item.Status, "resolved", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(item => item.Occurrences)
                .ThenBy(item => item.ResourcePath, StringComparer.OrdinalIgnoreCase)
                .Take(Math.Max(1, requestLimit))
                .ToArray();

            if (pendingRequests.Length == 0)
            {
                builder.AppendLine("- 当前没有待处理缺包请求。");
            }
            else
            {
                for (int i = 0; i < pendingRequests.Length; i++)
                {
                    BootstrapMissingPackageRequestView request = pendingRequests[i];
                    string packagesText = request.Packages.Count == 0
                        ? "未定位到分包"
                        : string.Join(", ", request.Packages.Select(item => item.Name).Where(item => !string.IsNullOrWhiteSpace(item)));

                    builder.AppendLine($"- {request.ResourcePath} | 状态={request.Status} | 次数={request.Occurrences} | 包={packagesText}");
                }
            }

            return builder.ToString();
        }

        public static bool TryWriteDiagnosticsReport(
            string outputPath = null,
            bool refreshState = false,
            bool reloadBootstrapMetadata = false,
            bool processPendingRequests = false,
            int packageLimit = 12,
            int requestLimit = 12)
        {
            try
            {
                string report = BuildDiagnosticsReport(
                    refreshState: refreshState,
                    reloadBootstrapMetadata: reloadBootstrapMetadata,
                    processPendingRequests: processPendingRequests,
                    packageLimit: packageLimit,
                    requestLimit: requestLimit);

                string targetPath = string.IsNullOrWhiteSpace(outputPath)
                    ? ClientResourceLayout.PackageDiagnosticsReportPath
                    : outputPath;

                string directory = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrWhiteSpace(directory))
                    Directory.CreateDirectory(directory);

                File.WriteAllText(targetPath, report, new UTF8Encoding(false));
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static BootstrapPackageManifestView LoadDeclaredPackages(bool reloadBootstrapMetadata = false)
        {
            if (reloadBootstrapMetadata)
                ClientResourceLayout.ReloadBootstrapMetadata();

            BootstrapPackageManifestView manifest = LoadDeclaredPackageManifest()
                                                   ?? new BootstrapPackageManifestView();

            manifest.Packs ??= new List<BootstrapPackageManifestPackView>();

            var packsByName = new Dictionary<string, BootstrapPackageManifestPackView>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < manifest.Packs.Count; i++)
            {
                BootstrapPackageManifestPackView pack = manifest.Packs[i];
                if (string.IsNullOrWhiteSpace(pack?.Name))
                    continue;

                packsByName[pack.Name] = NormalizeDeclaredPack(pack);
            }

            foreach (string manifestPath in EnumerateDeclaredPackManifestPaths(manifest))
            {
                if (!TryLoadPackManifest(manifestPath, out BootstrapPackageManifestPackView pack))
                    continue;

                if (string.IsNullOrWhiteSpace(pack?.Name))
                    continue;

                if (packsByName.TryGetValue(pack.Name, out BootstrapPackageManifestPackView existing))
                    packsByName[pack.Name] = MergeDeclaredPack(existing, pack);
                else
                    packsByName[pack.Name] = NormalizeDeclaredPack(pack);
            }

            manifest.BootstrapRoot = string.IsNullOrWhiteSpace(manifest.BootstrapRoot)
                ? Path.Combine(ClientResourceLayout.ClientRoot, "BootstrapAssets")
                : manifest.BootstrapRoot;

            manifest.Packs = packsByName.Values
                .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (manifest.TotalAssets <= 0)
                manifest.TotalAssets = manifest.Packs.Sum(item => item.AssetCount);

            if (manifest.TotalBytes <= 0)
                manifest.TotalBytes = manifest.Packs.Sum(item => item.TotalBytes);

            return manifest;
        }

        public static BootstrapPackageStateSnapshotView LoadStateSnapshot()
        {
            if (!File.Exists(ClientResourceLayout.PackageStateSnapshotPath))
                return new BootstrapPackageStateSnapshotView();

            try
            {
                return JsonSerializer.Deserialize<BootstrapPackageStateSnapshotView>(
                           File.ReadAllText(ClientResourceLayout.PackageStateSnapshotPath),
                           JsonOptions)
                       ?? new BootstrapPackageStateSnapshotView();
            }
            catch (Exception)
            {
                return new BootstrapPackageStateSnapshotView();
            }
        }

        public static BootstrapMissingPackageQueueView LoadMissingQueue()
        {
            if (!File.Exists(ClientResourceLayout.MissingPackageQueuePath))
                return new BootstrapMissingPackageQueueView();

            try
            {
                return JsonSerializer.Deserialize<BootstrapMissingPackageQueueView>(
                           File.ReadAllText(ClientResourceLayout.MissingPackageQueuePath),
                           JsonOptions)
                       ?? new BootstrapMissingPackageQueueView();
            }
            catch (Exception)
            {
                return new BootstrapMissingPackageQueueView();
            }
        }

        public static BootstrapPackageStateView FindPackage(
            string packageName,
            bool refreshState = false,
            bool reloadBootstrapMetadata = false,
            bool processPendingRequests = false)
        {
            if (string.IsNullOrWhiteSpace(packageName))
                return null;

            BootstrapPackageRuntimeOverview overview = LoadOverview(
                refreshState: refreshState,
                reloadBootstrapMetadata: reloadBootstrapMetadata,
                processPendingRequests: processPendingRequests);

            return overview.State.Packages.FirstOrDefault(item =>
                string.Equals(item.Name, packageName, StringComparison.OrdinalIgnoreCase));
        }

        public static string[] GetPendingPackageNames()
        {
            return GetAllPendingPackageNames(packageName: null)
                .ToArray();
        }

        public static BootstrapPackageStageResultView TryStagePackage(
            string packageName,
            bool immediateOnly = false,
            bool refreshState = true,
            bool reloadBootstrapMetadata = false)
        {
            return StagePackages(
                string.IsNullOrWhiteSpace(packageName) ? Array.Empty<string>() : new[] { packageName },
                immediateOnly,
                refreshState,
                reloadBootstrapMetadata,
                processPendingRequests: refreshState);
        }

        public static BootstrapPackageStageResultView TryStagePendingPackages(
            string packageName = null,
            bool immediateOnly = false,
            bool refreshState = true,
            bool reloadBootstrapMetadata = false)
        {
            return StagePackages(
                GetAllPendingPackageNames(packageName),
                immediateOnly,
                refreshState,
                reloadBootstrapMetadata,
                processPendingRequests: refreshState);
        }

        private static IEnumerable<string> GetAllPendingPackageNames(string packageName)
        {
            IEnumerable<string> missing = GetPendingPackageNames(LoadMissingQueue(), packageName);

            IEnumerable<string> updates = BootstrapPackageUpdateRuntime.GetUpdatePackageNames();
            if (!string.IsNullOrWhiteSpace(packageName))
            {
                updates = updates.Where(item =>
                    string.Equals(item, packageName, StringComparison.OrdinalIgnoreCase));
            }

            return missing
                .Concat(updates)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(item => item, StringComparer.OrdinalIgnoreCase);
        }

        public static BootstrapPackageRuntimeOverview ProcessPendingRequests(bool reloadBootstrapMetadata = false)
        {
            return LoadOverview(
                refreshState: true,
                reloadBootstrapMetadata: reloadBootstrapMetadata,
                processPendingRequests: true);
        }

        public static BootstrapManifestInstallResultView TryInstallManifestBundleFromDirectory(
            string sourceDirectory,
            bool overwrite = true,
            bool refreshState = true,
            bool processPendingRequests = true)
        {
            string normalizedSourceDirectory = string.IsNullOrWhiteSpace(sourceDirectory)
                ? string.Empty
                : Path.GetFullPath(sourceDirectory);

            var result = new BootstrapManifestInstallResultView
            {
                SourceDirectory = normalizedSourceDirectory,
                SourceDirectoryExists = !string.IsNullOrWhiteSpace(normalizedSourceDirectory) && Directory.Exists(normalizedSourceDirectory),
                RuntimeManifestRoot = ClientResourceLayout.RuntimeManifestOverrideRoot,
                RuntimePackageManifestDirectory = ClientResourceLayout.RuntimePackageManifestDirectory,
            };

            if (!result.SourceDirectoryExists)
            {
                result.ErrorMessage = "源目录不存在，无法导入 manifest bundle。";
                result.Overview = LoadOverview();
                return result;
            }

            Directory.CreateDirectory(ClientResourceLayout.RuntimeManifestOverrideRoot);
            Directory.CreateDirectory(ClientResourceLayout.RuntimePackageManifestDirectory);

            if (!TryResolveManifestBundleFile(normalizedSourceDirectory, "bootstrap-packages.json", out string rootManifestSourcePath))
            {
                result.ErrorMessage = "源目录中未找到 bootstrap-packages.json。";
                result.Overview = LoadOverview();
                return result;
            }

            if (File.Exists(ClientResourceLayout.RuntimePackageManifestPath) && !overwrite)
            {
                result.RootManifestAlreadyPresent = true;
            }
            else
            {
                File.Copy(rootManifestSourcePath, ClientResourceLayout.RuntimePackageManifestPath, overwrite: true);
                result.RootManifestCopied = true;
                result.CopiedOverrideFiles.Add(ClientResourceLayout.RuntimePackageManifestPath);
            }

            if (TryResolveManifestBundleFile(normalizedSourceDirectory, "bootstrap-assets.txt", out string assetManifestSourcePath))
            {
                if (File.Exists(ClientResourceLayout.RuntimeBootstrapAssetManifestPath) && !overwrite)
                {
                    result.AssetManifestAlreadyPresent = true;
                }
                else
                {
                    File.Copy(assetManifestSourcePath, ClientResourceLayout.RuntimeBootstrapAssetManifestPath, overwrite: true);
                    result.AssetManifestCopied = true;
                    result.CopiedOverrideFiles.Add(ClientResourceLayout.RuntimeBootstrapAssetManifestPath);
                }
            }

            foreach (string sourceManifestDirectory in EnumerateManifestBundleDirectories(normalizedSourceDirectory))
            {
                foreach (string file in Directory.GetFiles(sourceManifestDirectory, "*.json", SearchOption.TopDirectoryOnly))
                {
                    string targetPath = Path.Combine(ClientResourceLayout.RuntimePackageManifestDirectory, Path.GetFileName(file));
                    if (File.Exists(targetPath) && !overwrite)
                    {
                        result.ExistingPackageManifestCount++;
                        continue;
                    }

                    File.Copy(file, targetPath, overwrite: true);
                    result.CopiedPackageManifestCount++;
                    result.CopiedOverrideFiles.Add(targetPath);
                }
            }

            ClientResourceLayout.ReloadBootstrapMetadata();
            result.Completed = result.RootManifestCopied || result.RootManifestAlreadyPresent;
            result.Overview = refreshState
                ? LoadOverview(
                    refreshState: true,
                    reloadBootstrapMetadata: false,
                    processPendingRequests: processPendingRequests)
                : LoadOverview(reloadBootstrapMetadata: false);
            result.DeclaredPackageCount = result.Overview.Declared.Packs.Count;

            if (!result.Completed && string.IsNullOrWhiteSpace(result.ErrorMessage))
                result.ErrorMessage = "manifest bundle 导入未完成。";

            return result;
        }

        public static BootstrapPackageInstallResultView TryInstallPackageFromDirectory(
            string packageName,
            string sourceDirectory,
            bool overwrite = false,
            bool refreshState = true,
            bool processPendingRequests = true,
            bool reloadBootstrapMetadata = false)
        {
            BootstrapPackageInstallBatchResultView batch = InstallPackagesFromDirectory(
                string.IsNullOrWhiteSpace(packageName) ? Array.Empty<string>() : new[] { packageName },
                sourceDirectory,
                overwrite,
                refreshState,
                processPendingRequests,
                reloadBootstrapMetadata);

            return batch.Packages.FirstOrDefault()
                   ?? new BootstrapPackageInstallResultView
                   {
                       PackageName = packageName,
                       SourceDirectory = sourceDirectory,
                       Overview = batch.Overview,
                       ErrorMessage = "未提供有效的分包名称。",
                   };
        }

        public static BootstrapPackageInstallBatchResultView TryInstallPendingPackagesFromDirectory(
            string sourceDirectory,
            bool overwrite = false,
            bool refreshState = true,
            bool processPendingRequests = true,
            bool reloadBootstrapMetadata = false)
        {
            return InstallPackagesFromDirectory(
                GetAllPendingPackageNames(packageName: null),
                sourceDirectory,
                overwrite,
                refreshState,
                processPendingRequests,
                reloadBootstrapMetadata);
        }

        public static BootstrapPackageRemoveResultView TryRemovePackage(
            string packageName,
            bool removeHydratedAssets = true,
            bool refreshState = true,
            bool reloadBootstrapMetadata = false)
        {
            if (reloadBootstrapMetadata)
                ClientResourceLayout.ReloadBootstrapMetadata();

            BootstrapPackageManifestView declared = LoadDeclaredPackages();
            BootstrapPackageManifestPackView pack = declared.Packs.FirstOrDefault(item =>
                string.Equals(item.Name, packageName, StringComparison.OrdinalIgnoreCase));

            var result = new BootstrapPackageRemoveResultView
            {
                PackageName = packageName,
                RemoveHydratedAssets = removeHydratedAssets,
            };

            if (pack == null)
            {
                result.ErrorMessage = "目标分包未在当前 bootstrap manifest 中声明。";
                result.Overview = LoadOverview();
                return result;
            }

            pack = NormalizeDeclaredPack(pack);
            result.InstallRoot = ResolveInstallRootAbsolutePath(pack);
            result.DeclaredAssetCount = pack.Assets.Count;

            try
            {
                if (Directory.Exists(result.InstallRoot))
                {
                    Directory.Delete(result.InstallRoot, recursive: true);
                    result.InstallRootRemoved = true;
                }
            }
            catch (Exception ex)
            {
                result.ErrorMessage = $"删除安装根失败：{ex.Message}";
            }

            if (removeHydratedAssets)
            {
                for (int i = 0; i < pack.Assets.Count; i++)
                {
                    if (!ClientResourceLayout.TryResolveBootstrapAssetTargetPath(pack.Assets[i], out string targetPath))
                        continue;

                    if (!File.Exists(targetPath))
                        continue;

                    try
                    {
                        File.Delete(targetPath);
                        result.RemovedHydratedAssetCount++;
                    }
                    catch (Exception ex)
                    {
                        result.FailedHydratedAssetCount++;
                        AddSample(result.FailedHydratedAssetsSample, $"{pack.Assets[i]} | {ex.Message}");
                    }
                }
            }

            result.Completed = string.IsNullOrWhiteSpace(result.ErrorMessage)
                && (result.InstallRootRemoved || !Directory.Exists(result.InstallRoot))
                && result.FailedHydratedAssetCount == 0;

            result.Overview = refreshState
                ? LoadOverview(
                    refreshState: true,
                    reloadBootstrapMetadata: false,
                    processPendingRequests: false)
                : LoadOverview();

            result.Package = result.Overview.State.Packages.FirstOrDefault(item =>
                string.Equals(item.Name, packageName, StringComparison.OrdinalIgnoreCase));

            if (!result.Completed && string.IsNullOrWhiteSpace(result.ErrorMessage))
                result.ErrorMessage = "分包已部分移除，但仍存在未删除的运行时资源。";

            return result;
        }

        public static BootstrapManifestResetResultView TryResetManifestOverrides(
            bool refreshState = true,
            bool reloadBootstrapMetadata = true)
        {
            var result = new BootstrapManifestResetResultView
            {
                RuntimeManifestRoot = ClientResourceLayout.RuntimeManifestOverrideRoot,
                RuntimePackageManifestDirectory = ClientResourceLayout.RuntimePackageManifestDirectory,
            };

            try
            {
                if (File.Exists(ClientResourceLayout.RuntimePackageManifestPath))
                {
                    File.Delete(ClientResourceLayout.RuntimePackageManifestPath);
                    result.RootManifestRemoved = true;
                }

                if (File.Exists(ClientResourceLayout.RuntimeBootstrapAssetManifestPath))
                {
                    File.Delete(ClientResourceLayout.RuntimeBootstrapAssetManifestPath);
                    result.AssetManifestRemoved = true;
                }

                if (Directory.Exists(ClientResourceLayout.RuntimePackageManifestDirectory))
                {
                    string[] manifestFiles = Directory.GetFiles(ClientResourceLayout.RuntimePackageManifestDirectory, "*.json", SearchOption.TopDirectoryOnly);
                    for (int i = 0; i < manifestFiles.Length; i++)
                    {
                        File.Delete(manifestFiles[i]);
                        result.RemovedPackageManifestCount++;
                    }
                }
            }
            catch (Exception ex)
            {
                result.ErrorMessage = ex.Message;
            }

            if (reloadBootstrapMetadata)
                ClientResourceLayout.ReloadBootstrapMetadata();

            result.Completed = string.IsNullOrWhiteSpace(result.ErrorMessage);
            result.Overview = refreshState
                ? LoadOverview(
                    refreshState: true,
                    reloadBootstrapMetadata: false,
                    processPendingRequests: false)
                : LoadOverview();

            if (!result.Completed && string.IsNullOrWhiteSpace(result.ErrorMessage))
                result.ErrorMessage = "运行时 manifest override 重置未完成。";

            return result;
        }

        public static BootstrapPackageApplyBundleResultView TryApplyPackageBundleFromDirectory(
            string sourceDirectory,
            bool overwrite = true,
            bool installManifestBundle = true,
            bool hydrateInstalledPackages = true,
            bool restrictToPendingPackages = false,
            bool rollbackOnFailure = false)
        {
            string normalizedSourceDirectory = string.IsNullOrWhiteSpace(sourceDirectory)
                ? string.Empty
                : Path.GetFullPath(sourceDirectory);

            var result = new BootstrapPackageApplyBundleResultView
            {
                SourceDirectory = normalizedSourceDirectory,
                SourceDirectoryExists = !string.IsNullOrWhiteSpace(normalizedSourceDirectory) && Directory.Exists(normalizedSourceDirectory),
                RollbackRequested = rollbackOnFailure,
            };

            if (!result.SourceDirectoryExists)
            {
                result.ErrorMessage = "源目录不存在，无法应用资源包。";
                result.Overview = LoadOverview();
                return result;
            }

            if (installManifestBundle && DirectoryContainsManifestBundle(normalizedSourceDirectory))
            {
                result.Manifest = TryInstallManifestBundleFromDirectory(
                    normalizedSourceDirectory,
                    overwrite: overwrite,
                    refreshState: false,
                    processPendingRequests: false);

                if (!result.Manifest.Completed)
                {
                    result.ErrorMessage = string.IsNullOrWhiteSpace(result.Manifest.ErrorMessage)
                        ? "manifest bundle 导入失败。"
                        : result.Manifest.ErrorMessage;
                }
            }

            BootstrapPackageManifestView declared = LoadDeclaredPackages(reloadBootstrapMetadata: false);
            List<string> preferredPackageNames = restrictToPendingPackages
                ? GetAllPendingPackageNames(packageName: null).ToList()
                : new List<string>();

            List<string> candidatePackageNames = DetectBundlePackageNames(normalizedSourceDirectory, declared, preferredPackageNames);
            result.CandidatePackageNames = candidatePackageNames;

            if (candidatePackageNames.Count == 0 && string.IsNullOrWhiteSpace(result.ErrorMessage))
                result.ErrorMessage = BuildNoImportablePackageMessage(normalizedSourceDirectory, declared);

            result.Install = InstallPackagesFromDirectory(
                candidatePackageNames,
                normalizedSourceDirectory,
                overwrite,
                refreshState: false,
                processPendingRequests: false,
                reloadBootstrapMetadata: false);

            if (hydrateInstalledPackages)
            {
                result.Hydration = HydrateInstalledPackages(
                    candidatePackageNames,
                    overwrite,
                    refreshState: false,
                    processPendingRequests: false,
                    reloadBootstrapMetadata: false);
            }

            result.Overview = LoadOverview(
                refreshState: true,
                reloadBootstrapMetadata: false,
                processPendingRequests: true);

            result.Completed =
                string.IsNullOrWhiteSpace(result.ErrorMessage)
                && (result.Manifest == null || result.Manifest.Completed)
                && (result.Install == null || result.Install.FailedPackageCount == 0)
                && (!hydrateInstalledPackages || result.Hydration == null || result.Hydration.FailedPackageCount == 0);

            if (!result.Completed && rollbackOnFailure)
            {
                result.Rollback = TryRollbackAppliedBundle(result, overwrite);
            }

            if (!result.Completed && string.IsNullOrWhiteSpace(result.ErrorMessage))
                result.ErrorMessage = "资源包已部分应用，但仍存在安装或激活失败项。";

            result.Overview = LoadOverview(
                refreshState: true,
                reloadBootstrapMetadata: false,
                processPendingRequests: true);

            return result;
        }

        public static BootstrapPackageBundlePreviewView PreviewPackageBundleFromDirectory(
            string sourceDirectory,
            bool restrictToPendingPackages = false,
            bool reloadBootstrapMetadata = false)
        {
            if (reloadBootstrapMetadata)
                ClientResourceLayout.ReloadBootstrapMetadata();

            string normalizedSourceDirectory = string.IsNullOrWhiteSpace(sourceDirectory)
                ? string.Empty
                : Path.GetFullPath(sourceDirectory);

            var result = new BootstrapPackageBundlePreviewView
            {
                SourceDirectory = normalizedSourceDirectory,
                SourceDirectoryExists = !string.IsNullOrWhiteSpace(normalizedSourceDirectory) && Directory.Exists(normalizedSourceDirectory),
                Overview = LoadOverview(),
            };

            if (!result.SourceDirectoryExists)
            {
                result.ErrorMessage = "源目录不存在，无法预检资源包。";
                return result;
            }

            result.HasManifestBundle = DirectoryContainsManifestBundle(normalizedSourceDirectory);

            BootstrapPackageManifestView declared = LoadDeclaredPackages(reloadBootstrapMetadata: false);
            result.DeclaredPackageCount = declared.Packs.Count;

            List<string> preferredPackageNames = restrictToPendingPackages
                ? GetAllPendingPackageNames(packageName: null).ToList()
                : new List<string>();

            result.CandidatePackageNames = DetectBundlePackageNames(normalizedSourceDirectory, declared, preferredPackageNames);
            result.CandidatePackageCount = result.CandidatePackageNames.Count;

            result.MissingPreferredPackageNames = preferredPackageNames
                .Where(item => !result.CandidatePackageNames.Contains(item, StringComparer.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
                .ToList();

            Dictionary<string, BootstrapPackageManifestPackView> declaredByName = declared.Packs
                .Where(item => !string.IsNullOrWhiteSpace(item?.Name))
                .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < result.CandidatePackageNames.Count; i++)
            {
                if (!declaredByName.TryGetValue(result.CandidatePackageNames[i], out BootstrapPackageManifestPackView pack))
                    continue;

                result.Packages.Add(PreviewSingleBundlePackage(normalizedSourceDirectory, pack));
            }

            result.ReadyToApply =
                result.CandidatePackageCount > 0
                && result.MissingPreferredPackageNames.Count == 0
                && result.Packages.All(item => item.ReadyToApply);

            if (!result.ReadyToApply && string.IsNullOrWhiteSpace(result.ErrorMessage))
            {
                if (result.CandidatePackageCount == 0)
                    result.ErrorMessage = BuildNoImportablePackageMessage(normalizedSourceDirectory, declared);
                else
                    result.ErrorMessage = "资源包目录预检未通过，仍存在缺失或未解析的资源。";
            }

            return result;
        }

        private static string BuildNoImportablePackageMessage(string sourceDirectory, BootstrapPackageManifestView declared)
        {
            int declaredCount = declared?.Packs?.Count ?? 0;
            const string baseMessage = "源目录中未检测到可导入的分包内容。";

            try
            {
                string packagesRoot = Path.Combine(sourceDirectory ?? string.Empty, "Packages");
                if (Directory.Exists(packagesRoot))
                {
                    string[] sample = Directory.GetDirectories(packagesRoot, "*", SearchOption.TopDirectoryOnly)
                        .Select(Path.GetFileName)
                        .Where(item => !string.IsNullOrWhiteSpace(item))
                        .Take(5)
                        .ToArray();

                    return $"{baseMessage} (Declared={declaredCount}, PackagesDir=YES, PackagesSample={string.Join(",", sample)})";
                }

                string[] topDirs = Directory.GetDirectories(sourceDirectory ?? string.Empty, "*", SearchOption.TopDirectoryOnly)
                    .Select(Path.GetFileName)
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .Take(5)
                    .ToArray();

                return $"{baseMessage} (Declared={declaredCount}, PackagesDir=NO, TopDirs={string.Join(",", topDirs)})";
            }
            catch (Exception)
            {
                return $"{baseMessage} (Declared={declaredCount})";
            }
        }

        public static BootstrapPackageHydrationResultView TryHydrateInstalledPackage(
            string packageName,
            bool overwrite = false,
            bool refreshState = true,
            bool processPendingRequests = true,
            bool reloadBootstrapMetadata = false)
        {
            BootstrapPackageHydrationBatchResultView batch = HydrateInstalledPackages(
                string.IsNullOrWhiteSpace(packageName) ? Array.Empty<string>() : new[] { packageName },
                overwrite,
                refreshState,
                processPendingRequests,
                reloadBootstrapMetadata);

            return batch.Packages.FirstOrDefault()
                   ?? new BootstrapPackageHydrationResultView
                   {
                       PackageName = packageName,
                       Overview = batch.Overview,
                       ErrorMessage = "未提供有效的分包名称。",
                   };
        }

        public static BootstrapPackageHydrationBatchResultView TryHydratePendingPackages(
            bool overwrite = false,
            bool refreshState = true,
            bool processPendingRequests = true,
            bool reloadBootstrapMetadata = false)
        {
            return HydrateInstalledPackages(
                GetAllPendingPackageNames(packageName: null),
                overwrite,
                refreshState,
                processPendingRequests,
                reloadBootstrapMetadata);
        }

        private static BootstrapPackageStageResultView StagePackages(
            IEnumerable<string> packageNames,
            bool immediateOnly,
            bool refreshState,
            bool reloadBootstrapMetadata,
            bool processPendingRequests)
        {
            if (reloadBootstrapMetadata)
                ClientResourceLayout.ReloadBootstrapMetadata();

            List<string> requestedPackageNames = packageNames
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var stagedPackageNames = new List<string>();
            for (int i = 0; i < requestedPackageNames.Count; i++)
            {
                if (ClientResourceLayout.TryStagePackage(requestedPackageNames[i], immediateOnly))
                    stagedPackageNames.Add(requestedPackageNames[i]);
            }

            BootstrapPackageRuntimeOverview overview = refreshState
                ? LoadOverview(
                    refreshState: false,
                    reloadBootstrapMetadata: false,
                    processPendingRequests: processPendingRequests)
                : LoadOverview();

            return new BootstrapPackageStageResultView
            {
                RequestedPackageCount = requestedPackageNames.Count,
                StagedPackageCount = stagedPackageNames.Count,
                RequestedPackageNames = requestedPackageNames,
                StagedPackageNames = stagedPackageNames,
                Overview = overview,
            };
        }

        private static BootstrapPackageHydrationBatchResultView HydrateInstalledPackages(
            IEnumerable<string> packageNames,
            bool overwrite,
            bool refreshState,
            bool processPendingRequests,
            bool reloadBootstrapMetadata)
        {
            if (reloadBootstrapMetadata)
                ClientResourceLayout.ReloadBootstrapMetadata();

            BootstrapPackageManifestView declared = LoadDeclaredPackages();
            var declaredByName = declared.Packs
                .Where(item => !string.IsNullOrWhiteSpace(item?.Name))
                .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);

            List<string> requestedPackageNames = packageNames
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var results = new List<BootstrapPackageHydrationResultView>();
            for (int i = 0; i < requestedPackageNames.Count; i++)
            {
                string packageName = requestedPackageNames[i];
                declaredByName.TryGetValue(packageName, out BootstrapPackageManifestPackView pack);
                if (pack == null)
                    TryLoadPackManifestByName(packageName, out pack);
                results.Add(HydrateSingleInstalledPackage(pack, requestedPackageNames[i], overwrite));
            }

            BootstrapPackageRuntimeOverview overview = refreshState
                ? LoadOverview(
                    refreshState: true,
                    reloadBootstrapMetadata: false,
                    processPendingRequests: processPendingRequests)
                : LoadOverview();

            for (int i = 0; i < results.Count; i++)
            {
                results[i].Overview = overview;
                results[i].Package = overview.State.Packages.FirstOrDefault(item =>
                    string.Equals(item.Name, results[i].PackageName, StringComparison.OrdinalIgnoreCase));
            }

            return new BootstrapPackageHydrationBatchResultView
            {
                RequestedPackageCount = requestedPackageNames.Count,
                CompletedPackageCount = results.Count(item => item.Completed),
                FailedPackageCount = results.Count(item => !item.Completed),
                Packages = results,
                Overview = overview,
            };
        }

        private static BootstrapPackageHydrationResultView HydrateSingleInstalledPackage(
            BootstrapPackageManifestPackView pack,
            string packageName,
            bool overwrite)
        {
            var result = new BootstrapPackageHydrationResultView
            {
                PackageName = packageName,
            };

            if (pack == null)
            {
                result.ErrorMessage = "目标分包未在当前 bootstrap manifest 中声明。";
                return result;
            }

            pack = NormalizeDeclaredPack(pack);
            result.InstallRoot = ResolveInstallRootAbsolutePath(pack);
            result.DeclaredAssetCount = pack.Assets.Count;

            for (int i = 0; i < pack.Assets.Count; i++)
            {
                string asset = pack.Assets[i];
                if (!ClientResourceLayout.TryResolveBootstrapAssetTargetPath(asset, out string targetPath))
                {
                    result.UnresolvedAssetCount++;
                    AddSample(result.UnresolvedAssetsSample, asset);
                    continue;
                }

                string installedPath = BuildInstalledPackageAssetPath(pack, asset);
                if (!File.Exists(installedPath))
                {
                    if (PackageResourceRegistry.TryEnsureSharedSoundAliasAvailable(asset, out _))
                    {
                        result.ExistingHydratedAssetCount++;
                        continue;
                    }

                    result.MissingInstalledAssetCount++;
                    AddSample(result.MissingInstalledAssetsSample, asset);
                    continue;
                }

                if (File.Exists(targetPath) && !overwrite)
                {
                    result.ExistingHydratedAssetCount++;
                    continue;
                }

                try
                {
                    string directory = Path.GetDirectoryName(targetPath);
                    if (!string.IsNullOrWhiteSpace(directory))
                        Directory.CreateDirectory(directory);

                    File.Copy(installedPath, targetPath, overwrite: true);
                    result.CopiedHydratedAssetCount++;
                    result.CopiedHydratedTargetPaths.Add(targetPath);
                }
                catch (Exception ex)
                {
                    result.FailedHydratedAssetCount++;
                    AddSample(result.FailedHydratedAssetsSample, $"{asset} | {ex.Message}");
                }
            }

            result.HydratedAssetCount = result.CopiedHydratedAssetCount + result.ExistingHydratedAssetCount;
            result.Completed = result.DeclaredAssetCount > 0
                && result.MissingInstalledAssetCount == 0
                && result.UnresolvedAssetCount == 0
                && result.FailedHydratedAssetCount == 0
                && result.HydratedAssetCount >= result.DeclaredAssetCount;

            if (!result.Completed && string.IsNullOrWhiteSpace(result.ErrorMessage))
                result.ErrorMessage = "分包已部分激活，但仍存在缺失、未解析或复制失败的资源。";

            return result;
        }

        private static BootstrapPackageBundlePreviewPackageView PreviewSingleBundlePackage(
            string sourceDirectory,
            BootstrapPackageManifestPackView pack)
        {
            pack = NormalizeDeclaredPack(pack);

            var result = new BootstrapPackageBundlePreviewPackageView
            {
                PackageName = pack.Name,
                InstallRoot = ResolveInstallRootAbsolutePath(pack),
                DeclaredAssetCount = pack.Assets.Count,
            };

            for (int i = 0; i < pack.Assets.Count; i++)
            {
                string asset = pack.Assets[i];

                if (TryResolveIncomingPackageAssetPath(sourceDirectory, pack.Name, asset, out _))
                {
                    result.FoundSourceAssetCount++;
                }
                else if (PackageResourceRegistry.TryEnsureSharedSoundAliasAvailable(asset, out _))
                {
                    result.FoundSourceAssetCount++;
                }
                else
                {
                    result.MissingSourceAssetCount++;
                    AddSample(result.MissingSourceAssetsSample, asset);
                }

                if (File.Exists(BuildInstalledPackageAssetPath(pack, asset)))
                    result.InstalledAssetCount++;

                if (!ClientResourceLayout.TryResolveBootstrapAssetTargetPath(asset, out string targetPath))
                {
                    result.UnresolvedTargetCount++;
                    AddSample(result.UnresolvedTargetAssetsSample, asset);
                    continue;
                }

                if (File.Exists(targetPath))
                    result.HydratedAssetCount++;
            }

            result.ReadyToApply =
                result.DeclaredAssetCount > 0
                && result.MissingSourceAssetCount == 0
                && result.UnresolvedTargetCount == 0;

            return result;
        }

        private static BootstrapPackageApplyRollbackResultView TryRollbackAppliedBundle(
            BootstrapPackageApplyBundleResultView result,
            bool overwrite)
        {
            var rollback = new BootstrapPackageApplyRollbackResultView
            {
                Attempted = true,
            };

            if (overwrite)
            {
                rollback.SkippedReason = "overwrite=true 时无法安全恢复被覆盖的既有文件，已跳过自动回滚。";
                return rollback;
            }

            if (result.Hydration?.Packages != null)
            {
                foreach (BootstrapPackageHydrationResultView package in result.Hydration.Packages)
                {
                    DeleteTrackedFiles(package.CopiedHydratedTargetPaths, rollback);
                }
            }

            if (result.Install?.Packages != null)
            {
                foreach (BootstrapPackageInstallResultView package in result.Install.Packages)
                {
                    DeleteTrackedFiles(package.CopiedAssetTargetPaths, rollback);
                    TryPruneEmptyDirectories(package.InstallRoot, rollback);
                }
            }

            if (result.Manifest?.CopiedOverrideFiles != null && result.Manifest.CopiedOverrideFiles.Count > 0)
            {
                rollback.ManifestResetTriggered = true;
                DeleteTrackedFiles(result.Manifest.CopiedOverrideFiles, rollback);
                TryPruneEmptyDirectories(ClientResourceLayout.RuntimePackageManifestDirectory, rollback);
                TryPruneEmptyDirectories(ClientResourceLayout.RuntimeManifestOverrideRoot, rollback);
                ClientResourceLayout.ReloadBootstrapMetadata();
            }

            rollback.Completed = rollback.FailedDeleteCount == 0;
            if (!rollback.Completed && string.IsNullOrWhiteSpace(rollback.ErrorMessage))
                rollback.ErrorMessage = "自动回滚执行完成，但仍有部分文件删除失败。";

            return rollback;
        }

        private static BootstrapPackageInstallBatchResultView InstallPackagesFromDirectory(
            IEnumerable<string> packageNames,
            string sourceDirectory,
            bool overwrite,
            bool refreshState,
            bool processPendingRequests,
            bool reloadBootstrapMetadata)
        {
            if (reloadBootstrapMetadata)
                ClientResourceLayout.ReloadBootstrapMetadata();

            BootstrapPackageManifestView declared = LoadDeclaredPackages();
            var declaredByName = declared.Packs
                .Where(item => !string.IsNullOrWhiteSpace(item?.Name))
                .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);

            List<string> requestedPackageNames = packageNames
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var results = new List<BootstrapPackageInstallResultView>();
            for (int i = 0; i < requestedPackageNames.Count; i++)
            {
                string packageName = requestedPackageNames[i];
                declaredByName.TryGetValue(packageName, out BootstrapPackageManifestPackView pack);
                if (pack == null)
                    TryLoadPackManifestByName(packageName, out pack);
                results.Add(InstallSinglePackageFromDirectory(pack, requestedPackageNames[i], sourceDirectory, overwrite));
            }

            BootstrapPackageRuntimeOverview overview = refreshState
                ? LoadOverview(
                    refreshState: false,
                    reloadBootstrapMetadata: false,
                    processPendingRequests: processPendingRequests)
                : LoadOverview();

            for (int i = 0; i < results.Count; i++)
            {
                results[i].Overview = overview;
                results[i].Package = overview.State.Packages.FirstOrDefault(item =>
                    string.Equals(item.Name, results[i].PackageName, StringComparison.OrdinalIgnoreCase));
            }

            return new BootstrapPackageInstallBatchResultView
            {
                SourceDirectory = sourceDirectory,
                RequestedPackageCount = requestedPackageNames.Count,
                CompletedPackageCount = results.Count(item => item.Completed),
                FailedPackageCount = results.Count(item => !item.Completed),
                Packages = results,
                Overview = overview,
            };
        }

        private static BootstrapPackageInstallResultView InstallSinglePackageFromDirectory(
            BootstrapPackageManifestPackView pack,
            string packageName,
            string sourceDirectory,
            bool overwrite)
        {
            string normalizedSourceDirectory = string.IsNullOrWhiteSpace(sourceDirectory)
                ? string.Empty
                : Path.GetFullPath(sourceDirectory);

            var result = new BootstrapPackageInstallResultView
            {
                PackageName = packageName,
                SourceDirectory = normalizedSourceDirectory,
                SourceDirectoryExists = !string.IsNullOrWhiteSpace(normalizedSourceDirectory) && Directory.Exists(normalizedSourceDirectory),
                Declared = pack != null,
            };

            if (pack == null)
            {
                result.ErrorMessage = "目标分包未在当前 bootstrap manifest 中声明。";
                return result;
            }

            pack = NormalizeDeclaredPack(pack);
            result.InstallRoot = ResolveInstallRootAbsolutePath(pack);
            result.AssetCount = pack.Assets.Count;

            if (!result.SourceDirectoryExists)
            {
                result.ErrorMessage = "源目录不存在，无法导入分包。";
                return result;
            }

            Directory.CreateDirectory(result.InstallRoot);

            for (int i = 0; i < pack.Assets.Count; i++)
            {
                string asset = pack.Assets[i];
                string targetPath = BuildInstalledPackageAssetPath(pack, asset);
                if (File.Exists(targetPath) && !overwrite)
                {
                    result.ExistingAssetCount++;
                    continue;
                }

                if (!TryResolveIncomingPackageAssetPath(normalizedSourceDirectory, pack.Name, asset, out string sourcePath))
                {
                    if (PackageResourceRegistry.TryEnsureSharedSoundAliasAvailable(asset, out _))
                    {
                        result.ExistingAssetCount++;
                        continue;
                    }

                    result.MissingSourceAssetCount++;
                    AddSample(result.MissingSourceAssetsSample, asset);
                    continue;
                }

                try
                {
                    string directory = Path.GetDirectoryName(targetPath);
                    if (!string.IsNullOrWhiteSpace(directory))
                        Directory.CreateDirectory(directory);

                    File.Copy(sourcePath, targetPath, overwrite: true);
                    result.CopiedAssetCount++;
                    result.CopiedAssetTargetPaths.Add(targetPath);
                }
                catch (Exception ex)
                {
                    result.FailedAssetCount++;
                    AddSample(result.FailedAssetsSample, $"{asset} | {ex.Message}");
                }
            }

            result.ResolvedAssetCount = result.CopiedAssetCount + result.ExistingAssetCount;
            result.Completed = result.AssetCount > 0
                && result.MissingSourceAssetCount == 0
                && result.FailedAssetCount == 0
                && result.ResolvedAssetCount >= result.AssetCount;

            if (!result.Completed && string.IsNullOrWhiteSpace(result.ErrorMessage))
                result.ErrorMessage = "分包目录已导入，但仍存在缺失或失败的资源文件。";

            return result;
        }

        private static BootstrapPackageRuntimeOverview BuildOverview(
            BootstrapPackageManifestView declared,
            BootstrapPackageStateSnapshotView state,
            BootstrapMissingPackageQueueView queue)
        {
            declared ??= new BootstrapPackageManifestView();
            declared.Packs ??= new List<BootstrapPackageManifestPackView>();

            state ??= new BootstrapPackageStateSnapshotView();
            state.Packages ??= new List<BootstrapPackageStateView>();

            queue ??= new BootstrapMissingPackageQueueView();
            queue.Requests ??= new List<BootstrapMissingPackageRequestView>();

            Dictionary<string, BootstrapPackageManifestPackView> declaredByName = declared.Packs
                .Where(item => !string.IsNullOrWhiteSpace(item?.Name))
                .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);

            EnrichQueue(queue, declaredByName);

            Dictionary<string, List<BootstrapMissingPackageRequestView>> pendingRequestsByPackage =
                BuildPendingRequestsByPackage(queue);

            var stateByName = new Dictionary<string, BootstrapPackageStateView>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < state.Packages.Count; i++)
            {
                BootstrapPackageStateView pack = NormalizeStatePack(state.Packages[i]);
                if (string.IsNullOrWhiteSpace(pack.Name))
                    continue;

                stateByName[pack.Name] = pack;
            }

            foreach (BootstrapPackageManifestPackView declaredPack in declared.Packs)
            {
                if (string.IsNullOrWhiteSpace(declaredPack?.Name))
                    continue;

                if (stateByName.TryGetValue(declaredPack.Name, out BootstrapPackageStateView existing))
                    stateByName[declaredPack.Name] = MergeStateWithDeclared(existing, declaredPack);
                else
                    stateByName[declaredPack.Name] = CreateStateFromDeclared(declaredPack);
            }

            foreach (KeyValuePair<string, BootstrapPackageStateView> item in stateByName.ToArray())
            {
                List<BootstrapMissingPackageRequestView> requests = pendingRequestsByPackage.TryGetValue(item.Key, out List<BootstrapMissingPackageRequestView> packageRequests)
                    ? packageRequests
                    : new List<BootstrapMissingPackageRequestView>();

                stateByName[item.Key] = ApplyPendingDetails(item.Value, requests);
            }

            state.Packages = stateByName.Values
                .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            RecalculateStateCounts(state);
            ApplyPendingDetailsToDeclaredPacks(declared.Packs, pendingRequestsByPackage);

            return new BootstrapPackageRuntimeOverview
            {
                Declared = declared,
                State = state,
                Queue = queue,
                Summary = BuildSummary(declared, state, queue, pendingRequestsByPackage),
            };
        }

        private static void EnrichQueue(
            BootstrapMissingPackageQueueView queue,
            IReadOnlyDictionary<string, BootstrapPackageManifestPackView> declaredByName)
        {
            for (int i = 0; i < queue.Requests.Count; i++)
            {
                BootstrapMissingPackageRequestView request = queue.Requests[i] ?? new BootstrapMissingPackageRequestView();
                request.Packages ??= new List<BootstrapMissingPackageReferenceView>();

                for (int j = 0; j < request.Packages.Count; j++)
                {
                    BootstrapMissingPackageReferenceView reference = request.Packages[j] ?? new BootstrapMissingPackageReferenceView();
                    if (declaredByName.TryGetValue(reference.Name ?? string.Empty, out BootstrapPackageManifestPackView declaredPack))
                    {
                        reference.Kind = string.IsNullOrWhiteSpace(reference.Kind) ? declaredPack.Kind : reference.Kind;
                        reference.Description = string.IsNullOrWhiteSpace(reference.Description) ? declaredPack.Description : reference.Description;
                        reference.ManifestPath = string.IsNullOrWhiteSpace(reference.ManifestPath) ? declaredPack.ManifestPath : reference.ManifestPath;
                        reference.InstallRootHint = string.IsNullOrWhiteSpace(reference.InstallRootHint) ? declaredPack.InstallRootHint : reference.InstallRootHint;
                        reference.AssetCount = reference.AssetCount > 0 ? reference.AssetCount : declaredPack.AssetCount;
                        reference.TotalBytes = reference.TotalBytes > 0 ? reference.TotalBytes : declaredPack.TotalBytes;
                    }

                    request.Packages[j] = reference;
                }

                request.Packages = request.Packages
                    .Where(item => !string.IsNullOrWhiteSpace(item?.Name))
                    .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                queue.Requests[i] = request;
            }

            queue.Requests = queue.Requests
                .OrderBy(item => item.ResourcePath, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static Dictionary<string, List<BootstrapMissingPackageRequestView>> BuildPendingRequestsByPackage(
            BootstrapMissingPackageQueueView queue)
        {
            var lookup = new Dictionary<string, List<BootstrapMissingPackageRequestView>>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < queue.Requests.Count; i++)
            {
                BootstrapMissingPackageRequestView request = queue.Requests[i];
                if (request == null || IsResolvedRequest(request))
                    continue;

                if (request.Packages == null || request.Packages.Count == 0)
                    continue;

                for (int j = 0; j < request.Packages.Count; j++)
                {
                    string packageName = request.Packages[j]?.Name;
                    if (string.IsNullOrWhiteSpace(packageName))
                        continue;

                    if (!lookup.TryGetValue(packageName, out List<BootstrapMissingPackageRequestView> requests))
                    {
                        requests = new List<BootstrapMissingPackageRequestView>();
                        lookup[packageName] = requests;
                    }

                    requests.Add(request);
                }
            }

            return lookup;
        }

        private static BootstrapPackageStateView NormalizeStatePack(BootstrapPackageStateView pack)
        {
            pack ??= new BootstrapPackageStateView();
            pack.MissingAssetsSample ??= new List<string>();
            pack.PendingResourcesSample ??= new List<string>();
            pack.AssetCount = Math.Max(pack.AssetCount, 0);
            pack.HydratedAssetCount = Math.Max(pack.HydratedAssetCount, 0);
            pack.StagedAssetCount = Math.Max(pack.StagedAssetCount, 0);
            pack.PendingRequestCount = Math.Max(pack.PendingRequestCount, 0);
            return pack;
        }

        private static BootstrapPackageStateView MergeStateWithDeclared(
            BootstrapPackageStateView statePack,
            BootstrapPackageManifestPackView declaredPack)
        {
            statePack = NormalizeStatePack(statePack);
            declaredPack = NormalizeDeclaredPack(declaredPack);

            statePack.Kind = string.IsNullOrWhiteSpace(statePack.Kind) ? declaredPack.Kind : statePack.Kind;
            statePack.Description = string.IsNullOrWhiteSpace(statePack.Description) ? declaredPack.Description : statePack.Description;
            statePack.ManifestPath = string.IsNullOrWhiteSpace(statePack.ManifestPath) ? declaredPack.ManifestPath : statePack.ManifestPath;
            statePack.InstallRootHint = string.IsNullOrWhiteSpace(statePack.InstallRootHint) ? declaredPack.InstallRootHint : statePack.InstallRootHint;
            statePack.AssetCount = statePack.AssetCount > 0 ? statePack.AssetCount : declaredPack.AssetCount;
            statePack.TotalBytes = statePack.TotalBytes > 0 ? statePack.TotalBytes : declaredPack.TotalBytes;
            return statePack;
        }

        private static BootstrapPackageStateView CreateStateFromDeclared(BootstrapPackageManifestPackView declaredPack)
        {
            declaredPack = NormalizeDeclaredPack(declaredPack);
            return new BootstrapPackageStateView
            {
                Name = declaredPack.Name,
                Kind = declaredPack.Kind,
                Description = declaredPack.Description,
                ManifestPath = declaredPack.ManifestPath,
                InstallRootHint = declaredPack.InstallRootHint,
                AssetCount = declaredPack.AssetCount,
                TotalBytes = declaredPack.TotalBytes,
                Status = "declared",
                MissingAssetsSample = new List<string>(),
                PendingResourcesSample = new List<string>(),
            };
        }

        private static BootstrapPackageStateView ApplyPendingDetails(
            BootstrapPackageStateView pack,
            List<BootstrapMissingPackageRequestView> requests)
        {
            pack = NormalizeStatePack(pack);
            requests ??= new List<BootstrapMissingPackageRequestView>();

            pack.PendingRequestCount = requests.Count;
            pack.PendingResourcesSample = requests
                .Select(item => item.ResourcePath)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(5)
                .ToList();

            return pack;
        }

        private static void ApplyPendingDetailsToDeclaredPacks(
            List<BootstrapPackageManifestPackView> packs,
            IReadOnlyDictionary<string, List<BootstrapMissingPackageRequestView>> pendingRequestsByPackage)
        {
            for (int i = 0; i < packs.Count; i++)
            {
                BootstrapPackageManifestPackView pack = NormalizeDeclaredPack(packs[i]);
                List<BootstrapMissingPackageRequestView> requests = pendingRequestsByPackage.TryGetValue(pack.Name ?? string.Empty, out List<BootstrapMissingPackageRequestView> packageRequests)
                    ? packageRequests
                    : new List<BootstrapMissingPackageRequestView>();

                pack.PendingRequestCount = requests.Count;
                pack.PendingResourcesSample = requests
                    .Select(item => item.ResourcePath)
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(5)
                    .ToList();

                packs[i] = pack;
            }
        }

        private static void RecalculateStateCounts(BootstrapPackageStateSnapshotView state)
        {
            state.PackageCount = state.Packages.Count;
            state.HydratedPackageCount = state.Packages.Count(item => string.Equals(item.Status, "hydrated", StringComparison.OrdinalIgnoreCase));
            state.StagedPackageCount = state.Packages.Count(item => string.Equals(item.Status, "staged", StringComparison.OrdinalIgnoreCase));
            state.PartialPackageCount = state.Packages.Count(item => string.Equals(item.Status, "partial", StringComparison.OrdinalIgnoreCase));
            state.PendingPackageCount = state.Packages.Count(item => string.Equals(item.Status, "declared", StringComparison.OrdinalIgnoreCase));
        }

        private static BootstrapPackageRuntimeSummaryView BuildSummary(
            BootstrapPackageManifestView declared,
            BootstrapPackageStateSnapshotView state,
            BootstrapMissingPackageQueueView queue,
            IReadOnlyDictionary<string, List<BootstrapMissingPackageRequestView>> pendingRequestsByPackage)
        {
            int pendingRequestCount = queue.Requests.Count(item => item != null && !IsResolvedRequest(item));
            int resolvedRequestCount = queue.Requests.Count(IsResolvedRequest);

            return new BootstrapPackageRuntimeSummaryView
            {
                DeclaredPackageCount = declared.Packs.Count,
                StatePackageCount = state.Packages.Count,
                PendingRequestCount = pendingRequestCount,
                ResolvedRequestCount = resolvedRequestCount,
                PackagesWithPendingRequests = pendingRequestsByPackage.Count,
                HydratedPackageCount = state.HydratedPackageCount,
                StagedPackageCount = state.StagedPackageCount,
                PartialPackageCount = state.PartialPackageCount,
                PendingPackageCount = state.PendingPackageCount,
            };
        }

        private static IEnumerable<string> GetPendingPackageNames(
            BootstrapMissingPackageQueueView queue,
            string packageName)
        {
            IEnumerable<string> names = queue.Requests
                .Where(item => item != null && !IsResolvedRequest(item))
                .SelectMany(item => item.Packages ?? new List<BootstrapMissingPackageReferenceView>())
                .Select(item => item?.Name)
                .Where(item => !string.IsNullOrWhiteSpace(item));

            if (!string.IsNullOrWhiteSpace(packageName))
            {
                names = names.Where(item =>
                    string.Equals(item, packageName, StringComparison.OrdinalIgnoreCase));
            }

            return names
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(item => item, StringComparer.OrdinalIgnoreCase);
        }

        private static bool IsResolvedRequest(BootstrapMissingPackageRequestView request)
        {
            return string.Equals(request?.Status, "resolved", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetDeclaredManifestFilePath()
        {
            return File.Exists(ClientResourceLayout.RuntimePackageManifestPath)
                ? ClientResourceLayout.RuntimePackageManifestPath
                : Path.Combine(ClientResourceLayout.ClientRoot, "BootstrapAssets", "bootstrap-packages.json");
        }

        private static IEnumerable<string> EnumerateDeclaredPackManifestPaths(BootstrapPackageManifestView manifest)
        {
            var manifestPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (Directory.Exists(ClientResourceLayout.PackageManifestDirectory))
            {
                foreach (string file in Directory.GetFiles(ClientResourceLayout.PackageManifestDirectory, "*.json", SearchOption.TopDirectoryOnly))
                {
                    string fileName = Path.GetFileName(file);
                    manifestPaths.Add(NormalizeManifestPath(
                        Path.Combine("BootstrapAssets", "bootstrap-package-manifests", fileName),
                        Path.GetFileNameWithoutExtension(fileName)));
                }
            }

            if (Directory.Exists(ClientResourceLayout.RuntimePackageManifestDirectory))
            {
                foreach (string file in Directory.GetFiles(ClientResourceLayout.RuntimePackageManifestDirectory, "*.json", SearchOption.TopDirectoryOnly))
                {
                    string fileName = Path.GetFileName(file);
                    manifestPaths.Add(NormalizeManifestPath(
                        Path.Combine("BootstrapAssets", "bootstrap-package-manifests", fileName),
                        Path.GetFileNameWithoutExtension(fileName)));
                }
            }

            for (int i = 0; i < manifest.Packs.Count; i++)
            {
                BootstrapPackageManifestPackView pack = manifest.Packs[i];
                if (string.IsNullOrWhiteSpace(pack?.Name))
                    continue;

                manifestPaths.Add(NormalizeManifestPath(pack.ManifestPath, pack.Name));
            }

            return manifestPaths;
        }

        private static bool TryLoadPackManifest(string manifestPath, out BootstrapPackageManifestPackView pack)
        {
            string normalizedPath = NormalizeManifestPath(manifestPath, Path.GetFileNameWithoutExtension(manifestPath ?? string.Empty));

            pack = null;

            string overridePath = ResolveRuntimeManifestAbsolutePath(normalizedPath);
            if (File.Exists(overridePath))
            {
                using Stream stream = File.OpenRead(overridePath);
                pack = LoadJsonFromStream<BootstrapPackageManifestPackView>(stream);
            }

            if (pack == null)
            {
                string localPath = Path.Combine(ClientResourceLayout.ClientRoot, normalizedPath.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(localPath))
                {
                    using Stream stream = File.OpenRead(localPath);
                    pack = LoadJsonFromStream<BootstrapPackageManifestPackView>(stream);
                }
            }

            if (pack == null)
            {
                using Stream stream = TryOpenTitleContainerStream(normalizedPath);
                pack = LoadJsonFromStream<BootstrapPackageManifestPackView>(stream);
            }

            if (pack == null)
                return false;

            if (string.IsNullOrWhiteSpace(pack.ManifestPath))
                pack.ManifestPath = manifestPath;

            pack = NormalizeDeclaredPack(pack);
            return true;
        }

        private static bool TryLoadPackManifestByName(string packageName, out BootstrapPackageManifestPackView pack)
        {
            pack = null;
            if (string.IsNullOrWhiteSpace(packageName))
                return false;

            string normalizedPath = NormalizeManifestPath(manifestPath: null, packageName: packageName.Trim());
            if (!TryLoadPackManifest(normalizedPath, out pack))
                return false;

            if (string.IsNullOrWhiteSpace(pack.Name))
                pack.Name = packageName.Trim();

            pack = NormalizeDeclaredPack(pack);
            return !string.IsNullOrWhiteSpace(pack.Name);
        }

        private static BootstrapPackageManifestView LoadDeclaredPackageManifest()
        {
            string overridePath = ClientResourceLayout.RuntimePackageManifestPath;
            if (File.Exists(overridePath))
            {
                BootstrapPackageManifestView manifest = LoadJsonFile<BootstrapPackageManifestView>(overridePath);
                if (manifest != null)
                    return manifest;
            }

            string localPath = Path.Combine(ClientResourceLayout.ClientRoot, "BootstrapAssets", "bootstrap-packages.json");
            if (File.Exists(localPath))
            {
                BootstrapPackageManifestView manifest = LoadJsonFile<BootstrapPackageManifestView>(localPath);
                if (manifest != null)
                    return manifest;
            }

            using Stream stream = TryOpenTitleContainerStream("BootstrapAssets/bootstrap-packages.json");
            return LoadJsonFromStream<BootstrapPackageManifestView>(stream);
        }

        private static Stream TryOpenPackageManifestStream(string normalizedPath)
        {
            string overridePath = ResolveRuntimeManifestAbsolutePath(normalizedPath);
            if (File.Exists(overridePath))
                return File.OpenRead(overridePath);

            string localPath = Path.Combine(ClientResourceLayout.ClientRoot, normalizedPath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(localPath))
                return File.OpenRead(localPath);

            return TryOpenTitleContainerStream(normalizedPath);
        }

        private static Stream TryOpenTitleContainerStream(string titleContainerPath)
        {
            if (string.IsNullOrWhiteSpace(titleContainerPath))
                return null;

            try
            {
                return TitleContainer.OpenStream(titleContainerPath.TrimStart('/'));
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static T LoadJsonFromStream<T>(Stream stream) where T : class
        {
            if (stream == null)
                return null;

            try
            {
                return JsonSerializer.Deserialize<T>(stream, JsonOptions);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string ResolveManifestAbsolutePath(string manifestPath)
        {
            string normalizedPath = NormalizeManifestPath(manifestPath, Path.GetFileNameWithoutExtension(manifestPath ?? string.Empty));
            string overridePath = ResolveRuntimeManifestAbsolutePath(normalizedPath);
            if (File.Exists(overridePath))
                return overridePath;

            return Path.Combine(ClientResourceLayout.ClientRoot, normalizedPath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static string ResolveRuntimeManifestAbsolutePath(string normalizedPath)
        {
            string relativePath = normalizedPath.StartsWith("BootstrapAssets/", StringComparison.OrdinalIgnoreCase)
                ? normalizedPath.Substring("BootstrapAssets/".Length)
                : normalizedPath;

            return Path.Combine(ClientResourceLayout.RuntimeManifestOverrideRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static BootstrapPackageManifestPackView NormalizeDeclaredPack(BootstrapPackageManifestPackView pack)
        {
            pack ??= new BootstrapPackageManifestPackView();
            pack.Assets ??= new List<string>();
            pack.PendingResourcesSample ??= new List<string>();

            pack.Assets = pack.Assets
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(NormalizeAssetPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            pack.AssetCount = pack.AssetCount > 0 ? pack.AssetCount : pack.Assets.Count;
            pack.TotalBytes = Math.Max(pack.TotalBytes, 0);
            pack.ManifestPath = NormalizeManifestPath(pack.ManifestPath, pack.Name);
            pack.InstallRootHint = NormalizeInstallRootHint(pack.InstallRootHint, pack.Name);
            pack.PendingRequestCount = Math.Max(pack.PendingRequestCount, 0);
            return pack;
        }

        private static BootstrapPackageManifestPackView MergeDeclaredPack(
            BootstrapPackageManifestPackView existing,
            BootstrapPackageManifestPackView incoming)
        {
            existing = NormalizeDeclaredPack(existing);
            incoming = NormalizeDeclaredPack(incoming);

            existing.Kind = string.IsNullOrWhiteSpace(incoming.Kind) ? existing.Kind : incoming.Kind;
            existing.Description = string.IsNullOrWhiteSpace(incoming.Description) ? existing.Description : incoming.Description;
            existing.ManifestPath = string.IsNullOrWhiteSpace(incoming.ManifestPath) ? existing.ManifestPath : incoming.ManifestPath;
            existing.InstallRootHint = string.IsNullOrWhiteSpace(incoming.InstallRootHint) ? existing.InstallRootHint : incoming.InstallRootHint;
            existing.TotalBytes = incoming.TotalBytes > 0 ? incoming.TotalBytes : existing.TotalBytes;

            if (incoming.Assets.Count > 0)
            {
                existing.Assets = existing.Assets
                    .Concat(incoming.Assets)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            existing.AssetCount = Math.Max(existing.AssetCount, incoming.AssetCount);
            existing.AssetCount = Math.Max(existing.AssetCount, existing.Assets.Count);
            return NormalizeDeclaredPack(existing);
        }

        private static string NormalizeManifestPath(string manifestPath, string packageName)
        {
            string normalizedPath = NormalizePath(manifestPath);
            if (string.IsNullOrWhiteSpace(normalizedPath))
            {
                string safePackageName = string.IsNullOrWhiteSpace(packageName) ? "unknown" : packageName;
                return $"BootstrapAssets/bootstrap-package-manifests/{safePackageName}.json";
            }

            if (normalizedPath.StartsWith("BootstrapAssets/", StringComparison.OrdinalIgnoreCase))
                return normalizedPath;

            if (normalizedPath.StartsWith("bootstrap-package-manifests/", StringComparison.OrdinalIgnoreCase))
                return "BootstrapAssets/" + normalizedPath;

            return "BootstrapAssets/bootstrap-package-manifests/" + Path.GetFileName(normalizedPath);
        }

        private static string ResolveInstallRootAbsolutePath(BootstrapPackageManifestPackView pack)
        {
            string normalizedInstallRoot = NormalizeInstallRootHint(pack?.InstallRootHint, pack?.Name);
            string installRootPath = normalizedInstallRoot.Replace('/', Path.DirectorySeparatorChar).TrimEnd(Path.DirectorySeparatorChar);

            if (Path.IsPathRooted(installRootPath))
                return Path.GetFullPath(installRootPath);

            return Path.GetFullPath(Path.Combine(ClientResourceLayout.ClientRoot, installRootPath));
        }

        private static string BuildInstalledPackageAssetPath(BootstrapPackageManifestPackView pack, string assetPath)
        {
            string installRoot = ResolveInstallRootAbsolutePath(pack);
            string normalizedAssetPath = NormalizeAssetPath(assetPath).Replace('/', Path.DirectorySeparatorChar);
            return Path.Combine(installRoot, normalizedAssetPath);
        }

        private static bool TryResolveIncomingPackageAssetPath(
            string sourceDirectory,
            string packageName,
            string assetPath,
            out string sourcePath)
        {
            string normalizedAssetPath = NormalizeAssetPath(assetPath);
            string[] candidates =
            {
                Path.Combine(sourceDirectory, normalizedAssetPath.Replace('/', Path.DirectorySeparatorChar)),
                Path.Combine(sourceDirectory, "BootstrapAssets", normalizedAssetPath.Replace('/', Path.DirectorySeparatorChar)),
                Path.Combine(sourceDirectory, packageName ?? string.Empty, normalizedAssetPath.Replace('/', Path.DirectorySeparatorChar)),
                Path.Combine(sourceDirectory, "Packages", packageName ?? string.Empty, normalizedAssetPath.Replace('/', Path.DirectorySeparatorChar)),
            };

            for (int i = 0; i < candidates.Length; i++)
            {
                if (File.Exists(candidates[i]))
                {
                    sourcePath = candidates[i];
                    return true;
                }
            }

            sourcePath = string.Empty;
            return false;
        }

        private static bool TryResolveManifestBundleFile(
            string sourceDirectory,
            string fileName,
            out string sourcePath)
        {
            string[] candidates =
            {
                Path.Combine(sourceDirectory, fileName),
                Path.Combine(sourceDirectory, "BootstrapAssets", fileName),
            };

            for (int i = 0; i < candidates.Length; i++)
            {
                if (File.Exists(candidates[i]))
                {
                    sourcePath = candidates[i];
                    return true;
                }
            }

            sourcePath = string.Empty;
            return false;
        }

        private static bool DirectoryContainsManifestBundle(string sourceDirectory)
        {
            return TryResolveManifestBundleFile(sourceDirectory, "bootstrap-packages.json", out _)
                || TryResolveManifestBundleFile(sourceDirectory, "bootstrap-assets.txt", out _)
                || EnumerateManifestBundleDirectories(sourceDirectory).Any();
        }

        private static List<string> DetectBundlePackageNames(
            string sourceDirectory,
            BootstrapPackageManifestView declared,
            IEnumerable<string> preferredPackageNames)
        {
            declared ??= new BootstrapPackageManifestView();
            declared.Packs ??= new List<BootstrapPackageManifestPackView>();

            List<string> preferred = preferredPackageNames
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            List<string> candidates = preferred
                .Where(name =>
                    declared.Packs.Any(pack => string.Equals(pack.Name, name, StringComparison.OrdinalIgnoreCase))
                    && DoesSourceDirectoryContainAnyAssetForPackage(
                        declared.Packs.First(pack => string.Equals(pack.Name, name, StringComparison.OrdinalIgnoreCase)),
                        sourceDirectory))
                .ToList();

            if (candidates.Count > 0)
                return candidates.OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToList();

            candidates = declared.Packs
                .Where(pack => DoesSourceDirectoryContainAnyAssetForPackage(pack, sourceDirectory))
                .Select(pack => pack.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (candidates.Count > 0)
                return candidates;

            // Fallback: declared manifest 可能未包含该分包，但 Bundle 目录结构本身包含 Packages/<pack>/...
            // 这种情况常见于 manifest override 不完整、或仅包含部分 pack 的场景。
            // 当 restrictToPendingPackages=true 时，preferredPackageNames 已经来自 missing queue/update queue，优先使用它们进行目录探测。
            candidates = preferred
                .Where(name => DoesSourceDirectoryContainPackageDirectory(sourceDirectory, name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (candidates.Count > 0)
                return candidates;

            // 最后兜底：直接扫描 sourceDirectory/Packages 下的目录名。
            try
            {
                string packagesRoot = Path.Combine(sourceDirectory, "Packages");
                if (Directory.Exists(packagesRoot))
                {
                    candidates = Directory.GetDirectories(packagesRoot, "*", SearchOption.TopDirectoryOnly)
                        .Select(Path.GetFileName)
                        .Where(item => !string.IsNullOrWhiteSpace(item))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    if (candidates.Count > 0)
                        return candidates;
                }
            }
            catch (Exception)
            {
            }

            return candidates;
        }

        private static bool DoesSourceDirectoryContainPackageDirectory(string sourceDirectory, string packageName)
        {
            if (string.IsNullOrWhiteSpace(sourceDirectory) || string.IsNullOrWhiteSpace(packageName))
                return false;

            string[] candidateDirectories =
            {
                Path.Combine(sourceDirectory, packageName),
                Path.Combine(sourceDirectory, "Packages", packageName),
                Path.Combine(sourceDirectory, "Cache", "Mobile", "Packages", packageName),
            };

            for (int i = 0; i < candidateDirectories.Length; i++)
            {
                if (Directory.Exists(candidateDirectories[i]))
                    return true;
            }

            return false;
        }

        private static bool DoesSourceDirectoryContainAnyAssetForPackage(
            BootstrapPackageManifestPackView pack,
            string sourceDirectory)
        {
            if (pack == null || string.IsNullOrWhiteSpace(pack.Name))
                return false;

            string[] candidateDirectories =
            {
                Path.Combine(sourceDirectory, pack.Name),
                Path.Combine(sourceDirectory, "Packages", pack.Name),
                Path.Combine(sourceDirectory, "Cache", "Mobile", "Packages", pack.Name),
            };

            for (int i = 0; i < candidateDirectories.Length; i++)
            {
                if (Directory.Exists(candidateDirectories[i]))
                    return true;
            }

            BootstrapPackageManifestPackView normalizedPack = NormalizeDeclaredPack(pack);
            for (int i = 0; i < normalizedPack.Assets.Count; i++)
            {
                if (TryResolveIncomingPackageAssetPath(sourceDirectory, normalizedPack.Name, normalizedPack.Assets[i], out _))
                    return true;
            }

            return false;
        }

        private static IEnumerable<string> EnumerateManifestBundleDirectories(string sourceDirectory)
        {
            string[] candidates =
            {
                Path.Combine(sourceDirectory, "bootstrap-package-manifests"),
                Path.Combine(sourceDirectory, "BootstrapAssets", "bootstrap-package-manifests"),
            };

            return candidates
                .Where(Directory.Exists)
                .Distinct(StringComparer.OrdinalIgnoreCase);
        }

        private static void DeleteTrackedFiles(
            IEnumerable<string> filePaths,
            BootstrapPackageApplyRollbackResultView rollback)
        {
            if (filePaths == null)
                return;

            foreach (string filePath in filePaths
                         .Where(item => !string.IsNullOrWhiteSpace(item))
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    if (File.Exists(filePath))
                    {
                        File.Delete(filePath);
                        rollback.DeletedFileCount++;
                        AddSample(rollback.DeletedFilesSample, filePath);
                    }
                }
                catch (Exception ex)
                {
                    rollback.FailedDeleteCount++;
                    AddSample(rollback.FailedDeleteSample, $"{filePath} | {ex.Message}");
                }
            }
        }

        private static void TryPruneEmptyDirectories(
            string rootPath,
            BootstrapPackageApplyRollbackResultView rollback)
        {
            if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
                return;

            try
            {
                PruneEmptyDirectoryTree(rootPath);
            }
            catch (Exception ex)
            {
                rollback.FailedDeleteCount++;
                AddSample(rollback.FailedDeleteSample, $"{rootPath} | {ex.Message}");
            }
        }

        private static bool PruneEmptyDirectoryTree(string path)
        {
            if (!Directory.Exists(path))
                return true;

            foreach (string directory in Directory.GetDirectories(path))
            {
                PruneEmptyDirectoryTree(directory);
            }

            if (Directory.EnumerateFileSystemEntries(path).Any())
                return false;

            Directory.Delete(path);
            return true;
        }

        private static void AddSample(List<string> samples, string value, int maxCount = 5)
        {
            if (samples == null || string.IsNullOrWhiteSpace(value) || samples.Count >= maxCount)
                return;

            if (!samples.Contains(value, StringComparer.OrdinalIgnoreCase))
                samples.Add(value);
        }

        private static string NormalizeInstallRootHint(string installRootHint, string packageName)
        {
            string normalized = NormalizePath(installRootHint);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                string safePackageName = string.IsNullOrWhiteSpace(packageName) ? string.Empty : packageName;
                normalized = $"Cache/Mobile/Packages/{safePackageName}";
            }

            return normalized.EndsWith("/", StringComparison.Ordinal) ? normalized : normalized + "/";
        }

        private static string NormalizeAssetPath(string assetPath)
        {
            return NormalizePath(assetPath);
        }

        private static string NormalizePath(string path)
        {
            return string.IsNullOrWhiteSpace(path)
                ? string.Empty
                : path.Replace('\\', '/').TrimStart('/');
        }

        private static string FormatBytes(long totalBytes)
        {
            if (totalBytes <= 0)
                return "0 B";

            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double size = totalBytes;
            int unitIndex = 0;

            while (size >= 1024D && unitIndex < units.Length - 1)
            {
                size /= 1024D;
                unitIndex++;
            }

            return $"{size:0.##} {units[unitIndex]}";
        }

        private static T LoadJsonFile<T>(string path) where T : class
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return null;

            try
            {
                return JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions);
            }
            catch (Exception)
            {
                return null;
            }
        }
    }

    public sealed class BootstrapPackageRuntimeOverview
    {
        public BootstrapPackageManifestView Declared { get; set; } = new BootstrapPackageManifestView();
        public BootstrapPackageStateSnapshotView State { get; set; } = new BootstrapPackageStateSnapshotView();
        public BootstrapMissingPackageQueueView Queue { get; set; } = new BootstrapMissingPackageQueueView();
        public BootstrapPackageRuntimeSummaryView Summary { get; set; } = new BootstrapPackageRuntimeSummaryView();
    }

    public sealed class BootstrapPackageRuntimeSummaryView
    {
        public int DeclaredPackageCount { get; set; }
        public int StatePackageCount { get; set; }
        public int PendingRequestCount { get; set; }
        public int ResolvedRequestCount { get; set; }
        public int PackagesWithPendingRequests { get; set; }
        public int HydratedPackageCount { get; set; }
        public int StagedPackageCount { get; set; }
        public int PartialPackageCount { get; set; }
        public int PendingPackageCount { get; set; }
    }

    public sealed class BootstrapPackageManifestView
    {
        public string RepositoryRoot { get; set; }
        public string BootstrapRoot { get; set; }
        public int TotalAssets { get; set; }
        public long TotalBytes { get; set; }
        public List<BootstrapPackageManifestPackView> Packs { get; set; } = new List<BootstrapPackageManifestPackView>();
    }

    public sealed class BootstrapPackageManifestPackView
    {
        public string Name { get; set; }
        public string Kind { get; set; }
        public string Description { get; set; }
        public int AssetCount { get; set; }
        public long TotalBytes { get; set; }
        public string ManifestPath { get; set; }
        public string InstallRootHint { get; set; }
        public int PendingRequestCount { get; set; }
        public List<string> PendingResourcesSample { get; set; } = new List<string>();
        public List<string> Assets { get; set; } = new List<string>();
    }

    public sealed class BootstrapPackageStageResultView
    {
        public int RequestedPackageCount { get; set; }
        public int StagedPackageCount { get; set; }
        public List<string> RequestedPackageNames { get; set; } = new List<string>();
        public List<string> StagedPackageNames { get; set; } = new List<string>();
        public BootstrapPackageRuntimeOverview Overview { get; set; } = new BootstrapPackageRuntimeOverview();
    }

    public sealed class BootstrapPackageInstallBatchResultView
    {
        public string SourceDirectory { get; set; }
        public int RequestedPackageCount { get; set; }
        public int CompletedPackageCount { get; set; }
        public int FailedPackageCount { get; set; }
        public List<BootstrapPackageInstallResultView> Packages { get; set; } = new List<BootstrapPackageInstallResultView>();
        public BootstrapPackageRuntimeOverview Overview { get; set; } = new BootstrapPackageRuntimeOverview();
    }

    public sealed class BootstrapPackageInstallResultView
    {
        public string PackageName { get; set; }
        public string SourceDirectory { get; set; }
        public string InstallRoot { get; set; }
        public bool Declared { get; set; }
        public bool SourceDirectoryExists { get; set; }
        public bool Completed { get; set; }
        public string ErrorMessage { get; set; }
        public int AssetCount { get; set; }
        public int ResolvedAssetCount { get; set; }
        public int CopiedAssetCount { get; set; }
        public int ExistingAssetCount { get; set; }
        public int MissingSourceAssetCount { get; set; }
        public int FailedAssetCount { get; set; }
        public List<string> MissingSourceAssetsSample { get; set; } = new List<string>();
        public List<string> FailedAssetsSample { get; set; } = new List<string>();
        public List<string> CopiedAssetTargetPaths { get; set; } = new List<string>();
        public BootstrapPackageStateView Package { get; set; }
        public BootstrapPackageRuntimeOverview Overview { get; set; } = new BootstrapPackageRuntimeOverview();
    }

    public sealed class BootstrapManifestInstallResultView
    {
        public string SourceDirectory { get; set; }
        public bool SourceDirectoryExists { get; set; }
        public bool RootManifestCopied { get; set; }
        public bool AssetManifestCopied { get; set; }
        public int CopiedPackageManifestCount { get; set; }
        public int ExistingPackageManifestCount { get; set; }
        public int DeclaredPackageCount { get; set; }
        public bool Completed { get; set; }
        public string ErrorMessage { get; set; }
        public string RuntimeManifestRoot { get; set; }
        public string RuntimePackageManifestDirectory { get; set; }
        public bool RootManifestAlreadyPresent { get; set; }
        public bool AssetManifestAlreadyPresent { get; set; }
        public List<string> CopiedOverrideFiles { get; set; } = new List<string>();
        public BootstrapPackageRuntimeOverview Overview { get; set; } = new BootstrapPackageRuntimeOverview();
    }

    public sealed class BootstrapPackageRemoveResultView
    {
        public string PackageName { get; set; }
        public string InstallRoot { get; set; }
        public bool RemoveHydratedAssets { get; set; }
        public bool InstallRootRemoved { get; set; }
        public bool Completed { get; set; }
        public string ErrorMessage { get; set; }
        public int DeclaredAssetCount { get; set; }
        public int RemovedHydratedAssetCount { get; set; }
        public int FailedHydratedAssetCount { get; set; }
        public List<string> FailedHydratedAssetsSample { get; set; } = new List<string>();
        public BootstrapPackageStateView Package { get; set; }
        public BootstrapPackageRuntimeOverview Overview { get; set; } = new BootstrapPackageRuntimeOverview();
    }

    public sealed class BootstrapPackageHydrationBatchResultView
    {
        public int RequestedPackageCount { get; set; }
        public int CompletedPackageCount { get; set; }
        public int FailedPackageCount { get; set; }
        public List<BootstrapPackageHydrationResultView> Packages { get; set; } = new List<BootstrapPackageHydrationResultView>();
        public BootstrapPackageRuntimeOverview Overview { get; set; } = new BootstrapPackageRuntimeOverview();
    }

    public sealed class BootstrapPackageHydrationResultView
    {
        public string PackageName { get; set; }
        public string InstallRoot { get; set; }
        public bool Completed { get; set; }
        public string ErrorMessage { get; set; }
        public int DeclaredAssetCount { get; set; }
        public int HydratedAssetCount { get; set; }
        public int CopiedHydratedAssetCount { get; set; }
        public int ExistingHydratedAssetCount { get; set; }
        public int MissingInstalledAssetCount { get; set; }
        public int UnresolvedAssetCount { get; set; }
        public int FailedHydratedAssetCount { get; set; }
        public List<string> MissingInstalledAssetsSample { get; set; } = new List<string>();
        public List<string> UnresolvedAssetsSample { get; set; } = new List<string>();
        public List<string> FailedHydratedAssetsSample { get; set; } = new List<string>();
        public List<string> CopiedHydratedTargetPaths { get; set; } = new List<string>();
        public BootstrapPackageStateView Package { get; set; }
        public BootstrapPackageRuntimeOverview Overview { get; set; } = new BootstrapPackageRuntimeOverview();
    }

    public sealed class BootstrapPackageApplyBundleResultView
    {
        public string SourceDirectory { get; set; }
        public bool SourceDirectoryExists { get; set; }
        public bool Completed { get; set; }
        public string ErrorMessage { get; set; }
        public List<string> CandidatePackageNames { get; set; } = new List<string>();
        public BootstrapManifestInstallResultView Manifest { get; set; }
        public BootstrapPackageInstallBatchResultView Install { get; set; }
        public BootstrapPackageHydrationBatchResultView Hydration { get; set; }
        public bool RollbackRequested { get; set; }
        public BootstrapPackageApplyRollbackResultView Rollback { get; set; }
        public BootstrapPackageRuntimeOverview Overview { get; set; } = new BootstrapPackageRuntimeOverview();
    }

    public sealed class BootstrapPackageApplyRollbackResultView
    {
        public bool Attempted { get; set; }
        public bool Completed { get; set; }
        public string SkippedReason { get; set; }
        public string ErrorMessage { get; set; }
        public int DeletedFileCount { get; set; }
        public int FailedDeleteCount { get; set; }
        public bool ManifestResetTriggered { get; set; }
        public List<string> DeletedFilesSample { get; set; } = new List<string>();
        public List<string> FailedDeleteSample { get; set; } = new List<string>();
    }

    public sealed class BootstrapPackageBundlePreviewView
    {
        public string SourceDirectory { get; set; }
        public bool SourceDirectoryExists { get; set; }
        public bool HasManifestBundle { get; set; }
        public bool ReadyToApply { get; set; }
        public string ErrorMessage { get; set; }
        public int DeclaredPackageCount { get; set; }
        public int CandidatePackageCount { get; set; }
        public List<string> CandidatePackageNames { get; set; } = new List<string>();
        public List<string> MissingPreferredPackageNames { get; set; } = new List<string>();
        public List<BootstrapPackageBundlePreviewPackageView> Packages { get; set; } = new List<BootstrapPackageBundlePreviewPackageView>();
        public BootstrapPackageRuntimeOverview Overview { get; set; } = new BootstrapPackageRuntimeOverview();
    }

    public sealed class BootstrapPackageBundlePreviewPackageView
    {
        public string PackageName { get; set; }
        public string InstallRoot { get; set; }
        public bool ReadyToApply { get; set; }
        public int DeclaredAssetCount { get; set; }
        public int FoundSourceAssetCount { get; set; }
        public int MissingSourceAssetCount { get; set; }
        public int InstalledAssetCount { get; set; }
        public int HydratedAssetCount { get; set; }
        public int UnresolvedTargetCount { get; set; }
        public List<string> MissingSourceAssetsSample { get; set; } = new List<string>();
        public List<string> UnresolvedTargetAssetsSample { get; set; } = new List<string>();
    }

    public sealed class BootstrapManifestResetResultView
    {
        public string RuntimeManifestRoot { get; set; }
        public string RuntimePackageManifestDirectory { get; set; }
        public bool RootManifestRemoved { get; set; }
        public bool AssetManifestRemoved { get; set; }
        public int RemovedPackageManifestCount { get; set; }
        public bool Completed { get; set; }
        public string ErrorMessage { get; set; }
        public BootstrapPackageRuntimeOverview Overview { get; set; } = new BootstrapPackageRuntimeOverview();
    }

    public sealed class BootstrapPackageStateSnapshotView
    {
        public string GeneratedAtUtc { get; set; }
        public int PackageCount { get; set; }
        public int HydratedPackageCount { get; set; }
        public int StagedPackageCount { get; set; }
        public int PartialPackageCount { get; set; }
        public int PendingPackageCount { get; set; }
        public List<BootstrapPackageStateView> Packages { get; set; } = new List<BootstrapPackageStateView>();
    }

    public sealed class BootstrapPackageStateView
    {
        public string Name { get; set; }
        public string Kind { get; set; }
        public string Description { get; set; }
        public string ManifestPath { get; set; }
        public string InstallRootHint { get; set; }
        public int AssetCount { get; set; }
        public long TotalBytes { get; set; }
        public int HydratedAssetCount { get; set; }
        public int StagedAssetCount { get; set; }
        public string Status { get; set; }
        public int PendingRequestCount { get; set; }
        public List<string> MissingAssetsSample { get; set; } = new List<string>();
        public List<string> PendingResourcesSample { get; set; } = new List<string>();
    }

    public sealed class BootstrapMissingPackageQueueView
    {
        public List<BootstrapMissingPackageRequestView> Requests { get; set; } = new List<BootstrapMissingPackageRequestView>();
    }

    public sealed class BootstrapMissingPackageRequestView
    {
        public string ResourcePath { get; set; }
        public string Status { get; set; }
        public string CreatedAtUtc { get; set; }
        public string LastSeenAtUtc { get; set; }
        public string ResolvedAtUtc { get; set; }
        public int Occurrences { get; set; }
        public List<BootstrapMissingPackageReferenceView> Packages { get; set; } = new List<BootstrapMissingPackageReferenceView>();
    }

    public sealed class BootstrapMissingPackageReferenceView
    {
        public string Name { get; set; }
        public string Kind { get; set; }
        public string Description { get; set; }
        public int AssetCount { get; set; }
        public long TotalBytes { get; set; }
        public string ManifestPath { get; set; }
        public string InstallRootHint { get; set; }
    }
}
