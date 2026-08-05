using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;

namespace MonoShare
{
    internal static class ClientResourceLayout
    {
        private static string _clientRoot = NormalizeRoot(AppContext.BaseDirectory);
        private static readonly object BootstrapManifestGate = new object();
        private static readonly object BootstrapWarmupGate = new object();
        private static readonly object MissingPackageLogGate = new object();
        private static readonly object PackageStateGate = new object();
        private static readonly object FileWriteGate = new object();
        private static readonly object BundleInboxGate = new object();
        private static Task _pendingPackageProcessTask;
        private static Task _bundleInboxApplyTask;
        private static DateTime _nextPendingPackageProcessUtc = DateTime.MinValue;
        private static DateTime _nextBundleInboxProcessUtc = DateTime.MinValue;
        private static string[] _bootstrapManifestAssets;
        private static BootstrapPackageManifest _bootstrapPackageManifest;
        private static bool _bootstrapPackageManifestLoaded;
        private static bool _bootstrapWarmupStarted;
        private static readonly HashSet<string> ReportedMissingPackageRequests = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        private const long MissingPackageLogRotateBytes = 2 * 1024 * 1024;
        private const int MissingPackageLogKeepCount = 3;
        private const long BundleInboxLogRotateBytes = 1 * 1024 * 1024;
        private const int BundleInboxLogKeepCount = 3;

        private static readonly string[] BootstrapWarmupPriorityAssets =
        {
            "Language.ini",
            "Mir2Config.ini",
            "Sound/SoundList.lst",
            "Sound/100.wav",
            "Sound/Log-in-long2.wav",
            "Sound/sellect-loop2.wav",
            "Data/Background.Lib",
            "Data/BuffIcon.Lib",
            "Data/ChrSel.Lib",
            "Data/MMap.Lib",
            "Data/Prguse.Lib",
            "Data/Prguse2.Lib",
            "Data/Prguse3.Lib",
            "Data/Title.Lib",
            "Content/hm.ttf",
        };

        public static string ClientRoot => _clientRoot;
        public static string ContentRoot => CombineClientPath("Content");
        public static string AssetsRoot => CombineClientPath("Assets");
        public static string DataRoot => CombineClientPath("Data");
        public static string MapRoot => CombineClientPath("Map");
        public static string SoundRoot => CombineClientPath("Sound");
        public static string CacheRoot => CombineClientPath(Path.Combine("Cache", "Mobile"));
        public static string RuntimeRoot => Path.Combine(CacheRoot, "Runtime");
        public static string PackageCacheRoot => Path.Combine(CacheRoot, "Packages");
        public static string DownloadRoot => Path.Combine(CacheRoot, "Downloads");
        public static string DownloadPackageRoot => Path.Combine(DownloadRoot, "Packages");
        public static string FontCacheRoot => Path.Combine(CacheRoot, "Fonts");
        public static string LibraryCacheRoot => Path.Combine(CacheRoot, "Libraries");
        public static string MapCacheRoot => Path.Combine(CacheRoot, "Maps");
        public static string SoundCacheRoot => Path.Combine(CacheRoot, "Sounds");
        public static string ConfigFilePath => Path.Combine(RuntimeRoot, "Mir2Config.ini");
        public static string LanguageFilePath => Path.Combine(RuntimeRoot, "Language.ini");
        public static string MissingPackageLogPath => Path.Combine(RuntimeRoot, "BootstrapMissingPackages.log");
        public static string MissingPackageQueuePath => Path.Combine(RuntimeRoot, "BootstrapMissingPackages.json");
        public static string PackageStateSnapshotPath => Path.Combine(RuntimeRoot, "BootstrapPackageState.json");
        public static string PackageUpdateQueuePath => Path.Combine(RuntimeRoot, "BootstrapPackageUpdateQueue.json");
        public static string PackageVersionsPath => Path.Combine(RuntimeRoot, "BootstrapPackageVersions.json");
        public static string PackageDiagnosticsReportPath => Path.Combine(RuntimeRoot, "BootstrapPackageDiagnostics.txt");
        public static string BundleInboxRoot => Path.Combine(CacheRoot, "BundleInbox");
        public static string BundleInboxProcessedRoot => Path.Combine(BundleInboxRoot, "Processed");
        public static string BundleInboxFailedRoot => Path.Combine(BundleInboxRoot, "Failed");
        public static string BundleInboxLogPath => Path.Combine(RuntimeRoot, "BootstrapBundleInbox.log");
        public static string DownloaderLogPath => Path.Combine(RuntimeRoot, "BootstrapDownloader.log");
        public static string DownloadStateSnapshotPath => Path.Combine(RuntimeRoot, "BootstrapDownloadState.json");
        public static string BootstrapAssetRoot => Path.Combine(ClientRoot, "BootstrapAssets");
        public static string PackageManifestDirectory => Path.Combine(ClientRoot, "BootstrapAssets", "bootstrap-package-manifests");
        public static string RuntimeManifestOverrideRoot => Path.Combine(RuntimeRoot, "BootstrapManifestOverrides");
        public static string RuntimeBootstrapAssetManifestPath => Path.Combine(RuntimeManifestOverrideRoot, "bootstrap-assets.txt");
        public static string RuntimePackageManifestPath => Path.Combine(RuntimeManifestOverrideRoot, "bootstrap-packages.json");
        public static string RuntimePackageManifestDirectory => Path.Combine(RuntimeManifestOverrideRoot, "bootstrap-package-manifests");

        public static void Configure(string clientRoot)
        {
            _clientRoot = NormalizeRoot(clientRoot);
        }

        public static void ReloadBootstrapMetadata()
        {
            lock (BootstrapManifestGate)
            {
                _bootstrapManifestAssets = null;
                _bootstrapPackageManifest = null;
                _bootstrapPackageManifestLoaded = false;
            }
        }

        public static bool TryResolveBootstrapAssetTargetPath(string assetRelativePath, out string targetPath)
        {
            if (!TryGetBootstrapAssetTargetPath(assetRelativePath, out string logicalPath))
            {
                targetPath = string.Empty;
                return false;
            }

            if (TryGetPackageHydrationPlan(logicalPath, out string hydratedTargetPath, out _))
            {
                targetPath = hydratedTargetPath;
                return true;
            }

            targetPath = logicalPath;
            return true;
        }

        private static void TryWritePackageDiagnosticsReport()
        {
            try
            {
                BootstrapPackageRuntime.TryWriteDiagnosticsReport(PackageDiagnosticsReportPath);
            }
            catch (Exception)
            {
            }
        }

        public static void EnsureWritableResourceDirectories()
        {
            string[] directories =
            {
                ContentRoot,
                AssetsRoot,
                DataRoot,
                MapRoot,
                SoundRoot,
                CacheRoot,
                RuntimeRoot,
                PackageCacheRoot,
                DownloadRoot,
                DownloadPackageRoot,
                FontCacheRoot,
                LibraryCacheRoot,
                MapCacheRoot,
                SoundCacheRoot,
                RuntimeManifestOverrideRoot,
                RuntimePackageManifestDirectory,
            };

            for (int i = 0; i < directories.Length; i++)
            {
                Directory.CreateDirectory(directories[i]);
            }
        }

        public static void EnsureCoreBootstrapAssetsAvailable()
        {
            EnsureWritableResourceDirectories();

            // Config and language must be ready before the rest of startup settings are read.
            TryHydrateFileFromPackage(ConfigFilePath, out _);
            TryHydrateFileFromPackage(LanguageFilePath, out _);

            foreach (string asset in GetBootstrapWarmupAssets().Where(IsImmediateBootstrapAsset))
            {
                TryHydrateBootstrapAsset(asset);
            }

            EnsurePackageInstalled("core-startup", immediateOnly: true);

            RefreshPackageStateSnapshot();
        }

        public static void StartBootstrapWarmup()
        {
            string[] assets = GetBootstrapWarmupAssets();
            if (assets.Length == 0)
                return;

            lock (BootstrapWarmupGate)
            {
                if (_bootstrapWarmupStarted)
                    return;

                _bootstrapWarmupStarted = true;
            }

            _ = Task.Run(() =>
            {
                try
                {
                    EnsurePackageInstalled("core-startup");
                    ProcessPendingPackageRequests();
                }
                catch (Exception)
                {
                }

                for (int i = 0; i < assets.Length; i++)
                {
                    try
                    {
                        TryHydrateBootstrapAsset(assets[i]);
                    }
                    catch (Exception)
                    {
                    }
                }

                try
                {
                    ProcessPendingPackageRequests();
                    RefreshPackageStateSnapshot();
                }
                catch (Exception)
                {
                }
            });
        }

        public static bool TryStagePackage(string packageName, bool immediateOnly = false)
        {
            bool copied = EnsurePackageInstalled(packageName, immediateOnly);
            if (copied)
                RefreshPackageStateSnapshot();

            return copied;
        }

        public static void ProcessPendingPackageRequestsNow()
        {
            ProcessPendingPackageRequests();
            RefreshPackageStateSnapshot();
        }

        public static void ProcessPendingPackageRequestsIfDue()
        {
            if (DateTime.UtcNow < _nextPendingPackageProcessUtc)
                return;

            _nextPendingPackageProcessUtc = DateTime.UtcNow.AddSeconds(1);

            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
            {
                if (_pendingPackageProcessTask == null || _pendingPackageProcessTask.IsCompleted)
                {
                    _pendingPackageProcessTask = Task.Run(() =>
                    {
                        try
                        {
                            ProcessPendingPackageRequests();
                        }
                        catch (Exception ex)
                        {
                            if (Settings.LogErrors) CMain.SaveError($"ProcessPendingPackageRequests: {ex}");
                        }
                    });
                }

                return;
            }

            ProcessPendingPackageRequests();
        }

        public static void TryApplyBundleInboxIfDue()
        {
            if (DateTime.UtcNow < _nextBundleInboxProcessUtc)
                return;

            lock (BundleInboxGate)
            {
                if (DateTime.UtcNow < _nextBundleInboxProcessUtc)
                    return;

                _nextBundleInboxProcessUtc = DateTime.UtcNow.AddSeconds(2);

                if (Environment.OSVersion.Platform != PlatformID.Win32NT)
                {
                    if (_bundleInboxApplyTask == null || _bundleInboxApplyTask.IsCompleted)
                    {
                        _bundleInboxApplyTask = Task.Run(() =>
                        {
                            try
                            {
                                TryApplyBundleInbox();
                            }
                            catch (Exception ex)
                            {
                                if (Settings.LogErrors) CMain.SaveError($"TryApplyBundleInbox: {ex}");
                            }
                        });
                    }

                    return;
                }

                TryApplyBundleInbox();
            }
        }

        public static string ResolvePath(string relativeOrAbsolutePath)
        {
            if (string.IsNullOrWhiteSpace(relativeOrAbsolutePath))
                return ClientRoot;

            if (Uri.TryCreate(relativeOrAbsolutePath, UriKind.Absolute, out Uri uri) && !uri.IsFile)
                return relativeOrAbsolutePath;

            if (Path.IsPathRooted(relativeOrAbsolutePath))
                return Path.GetFullPath(relativeOrAbsolutePath);

            return CombineClientPath(relativeOrAbsolutePath);
        }

        private static void TryApplyBundleInbox()
        {
            if (!Directory.Exists(BundleInboxRoot))
                return;

            HashSet<string> updatePackages = BootstrapPackageUpdateRuntime.GetUpdatePackageNames();

            string[] bundleDirectories = Directory.GetDirectories(BundleInboxRoot, "*", SearchOption.TopDirectoryOnly);
            if (bundleDirectories.Length == 0)
                return;

            var candidates = new List<string>();
            for (int i = 0; i < bundleDirectories.Length; i++)
            {
                string name = Path.GetFileName(bundleDirectories[i].TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                if (string.Equals(name, "Processed", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, "Failed", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                candidates.Add(bundleDirectories[i]);
            }

            if (candidates.Count == 0)
                return;

            candidates.Sort(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < candidates.Count; i++)
            {
                string bundleDirectory = candidates[i];
                if (!Directory.Exists(bundleDirectory))
                    continue;

                BootstrapPackageApplyBundleResultView result;
                try
                {
                    bool overwrite = false;
                    if (updatePackages.Count > 0 &&
                        BootstrapPackageUpdateRuntime.TryReadBundleDownloadMeta(bundleDirectory, out BundleDownloadMetaView meta) &&
                        !string.IsNullOrWhiteSpace(meta?.PackageName) &&
                        updatePackages.Contains(meta.PackageName))
                    {
                        overwrite = true;
                    }

                    result = BootstrapPackageRuntime.TryApplyPackageBundleFromDirectory(
                        bundleDirectory,
                        overwrite: overwrite,
                        installManifestBundle: true,
                        hydrateInstalledPackages: true,
                        restrictToPendingPackages: true,
                        rollbackOnFailure: true);
                }
                catch (Exception ex)
                {
                    result = new BootstrapPackageApplyBundleResultView
                    {
                        SourceDirectory = bundleDirectory,
                        SourceDirectoryExists = true,
                        ErrorMessage = ex.Message,
                    };
                }

                TryWriteBundleInboxApplyResult(bundleDirectory, result);
                TryAppendBundleInboxLog(bundleDirectory, result);

                try
                {
                    BootstrapPackageUpdateRuntime.TryOnBundleApplied(bundleDirectory, result);
                }
                catch (Exception)
                {
                }

                if (result == null || !result.Completed)
                {
                    try
                    {
                        string directoryName = string.IsNullOrWhiteSpace(bundleDirectory)
                            ? string.Empty
                            : Path.GetFileName(bundleDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                        string message = result == null ? "未知结果" : (string.IsNullOrWhiteSpace(result.ErrorMessage) ? "未提供错误信息" : result.ErrorMessage);
                        if (Settings.LogErrors)
                            CMain.SaveError($"BundleInbox: Apply 失败 | Bundle={directoryName} | Message={message}");
                    }
                    catch
                    {
                    }
                }

                if (result != null && result.Completed)
                {
                    TryMoveBundleInboxDirectory(bundleDirectory, BundleInboxProcessedRoot, "applied");
                }
                else
                {
                    TryMoveBundleInboxDirectory(bundleDirectory, BundleInboxFailedRoot, "failed");
                }
            }
        }

        private static void TryWriteBundleInboxApplyResult(string bundleDirectory, BootstrapPackageApplyBundleResultView result)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(bundleDirectory))
                    return;

                string outputPath = Path.Combine(bundleDirectory, "bundle-apply-result.json");
                WriteJsonFileAtomic(outputPath, result ?? new BootstrapPackageApplyBundleResultView());
            }
            catch (Exception)
            {
            }
        }

        private static void TryAppendBundleInboxLog(string bundleDirectory, BootstrapPackageApplyBundleResultView result)
        {
            try
            {
                Directory.CreateDirectory(RuntimeRoot);

                TryRotateLogFile(BundleInboxLogPath, BundleInboxLogRotateBytes, BundleInboxLogKeepCount);

                string directoryName = string.IsNullOrWhiteSpace(bundleDirectory)
                    ? string.Empty
                    : Path.GetFileName(bundleDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

                string status = result != null && result.Completed ? "OK" : "FAIL";
                string message = result == null ? "未知结果" : (string.IsNullOrWhiteSpace(result.ErrorMessage) ? "" : result.ErrorMessage);

                using var writer = new StreamWriter(BundleInboxLogPath, append: true, Utf8NoBom);
                writer.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} | Bundle={directoryName} | Status={status} | Message={message}");
            }
            catch (Exception)
            {
            }
        }

        private static void TryMoveBundleInboxDirectory(string sourceDirectory, string targetRoot, string suffix)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(sourceDirectory) || !Directory.Exists(sourceDirectory))
                    return;

                Directory.CreateDirectory(targetRoot);

                string name = Path.GetFileName(sourceDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                string timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
                string safeSuffix = string.IsNullOrWhiteSpace(suffix) ? "done" : suffix.Trim();

                string targetDirectory = Path.Combine(targetRoot, $"{name}_{safeSuffix}_{timestamp}");
                int attempt = 0;
                while (Directory.Exists(targetDirectory))
                {
                    attempt++;
                    targetDirectory = Path.Combine(targetRoot, $"{name}_{safeSuffix}_{timestamp}_{attempt}");
                    if (attempt >= 20)
                        return;
                }

                Directory.Move(sourceDirectory, targetDirectory);
            }
            catch (Exception)
            {
            }
        }

        public static string ResolveLibraryDirectory(string relativeOrAbsoluteDirectory)
        {
            string primary = ResolvePath(relativeOrAbsoluteDirectory);
            if (Directory.Exists(primary))
                return primary;

            string fallback = BuildCacheMirrorPath(primary, DataRoot, LibraryCacheRoot);
            if (Directory.Exists(fallback))
                return fallback;

            return fallback;
        }

        public static string ResolveLibraryFilePath(string relativeOrAbsoluteFilePath)
        {
            string primary = ResolvePath(relativeOrAbsoluteFilePath);
            if (File.Exists(primary))
                return primary;

            string fallback = BuildCacheMirrorPath(primary, DataRoot, LibraryCacheRoot);
            if (File.Exists(fallback))
                return fallback;

            if (TryHydrateFileFromPackage(primary, out string hydratedPath) && File.Exists(hydratedPath))
                return hydratedPath;

            return fallback;
        }

        public static string ResolveMapFilePath(string relativeOrAbsoluteFilePath)
        {
            return ResolveFileWithCache(relativeOrAbsoluteFilePath, MapRoot, MapCacheRoot);
        }

        public static string ResolveSoundFilePath(string relativeOrAbsoluteFilePath)
        {
            return ResolveFileWithCache(relativeOrAbsoluteFilePath, SoundRoot, SoundCacheRoot);
        }

        public static string ResolveFontFilePath(string fileName)
        {
            string[] candidates = GetFontCandidates(fileName).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            for (int i = 0; i < candidates.Length; i++)
            {
                if (File.Exists(candidates[i]))
                    return candidates[i];
            }

            string cacheTarget = Path.Combine(FontCacheRoot, Path.GetFileName(fileName));
            if (TryHydrateFileFromPackage(cacheTarget, out string hydratedPath) && File.Exists(hydratedPath))
                return hydratedPath;

            return cacheTarget;
        }

        public static Stream OpenReadStream(string relativeOrAbsolutePath, params string[] titleContainerCandidates)
        {
            string resolvedPath = ResolvePath(relativeOrAbsolutePath);
            if (File.Exists(resolvedPath))
                return File.OpenRead(resolvedPath);

            // Prefer installed packages (hotfix) without copying to targetPath.
            // This avoids blocking the main thread on large synchronous hydration I/O (FileWriteGate),
            // while still allowing patched assets to override bundled ones.
            try
            {
                if (TryGetPackageHydrationPlan(resolvedPath, out _, out string[] candidates) && candidates.Length > 0)
                {
                    BootstrapPackageManifestPack[] packs = FindOwningBootstrapPacks(resolvedPath);
                    for (int i = 0; i < packs.Length; i++)
                    {
                        for (int j = 0; j < candidates.Length; j++)
                        {
                            string installedAssetPath = BuildInstalledPackageAssetPath(packs[i], candidates[j]);
                            if (!File.Exists(installedAssetPath))
                                continue;

                            return File.OpenRead(installedAssetPath);
                        }
                    }
                }
            }
            catch (Exception)
            {
            }

            for (int i = 0; i < titleContainerCandidates.Length; i++)
            {
                string candidate = titleContainerCandidates[i];
                if (string.IsNullOrWhiteSpace(candidate))
                    continue;

                try
                {
                    return TitleContainer.OpenStream(NormalizeTitleContainerPath(candidate));
                }
                catch (Exception)
                {
                }
            }

            if (TryHydrateFileFromPackage(resolvedPath, out string hydratedPath) && File.Exists(hydratedPath))
                return File.OpenRead(hydratedPath);

            ReportMissingPackageRequest(resolvedPath);
            throw new FileNotFoundException($"资源文件不存在且无法从 BootstrapAssets/已安装分包回填：{resolvedPath}", resolvedPath);
        }

        public static bool TryHydrateFileFromPackage(string relativeOrAbsolutePath, out string availablePath)
        {
            string absolutePath = ResolvePath(relativeOrAbsolutePath);
            if (File.Exists(absolutePath))
            {
                availablePath = absolutePath;
                return true;
            }

            if (!TryGetPackageHydrationPlan(absolutePath, out string targetPath, out string[] candidates))
            {
                availablePath = absolutePath;
                return false;
            }

            lock (FileWriteGate)
            {
                if (File.Exists(targetPath))
                {
                    availablePath = targetPath;
                    return true;
                }

                for (int i = 0; i < candidates.Length; i++)
                {
                    string candidate = NormalizeTitleContainerPath(candidates[i]);
                    try
                    {
                        using Stream sourceStream = TitleContainer.OpenStream(candidate);
                        if (TryCopyStreamToFileAtomic(sourceStream, targetPath))
                        {
                            availablePath = targetPath;
                            return true;
                        }
                    }
                    catch (Exception)
                    {
                    }
                }

                if (TryHydrateFromInstalledPackages(targetPath, candidates, out string installedPath))
                {
                    availablePath = installedPath;
                    return true;
                }
            }

            availablePath = targetPath;
            return false;
        }

        public static void ReportMissingPackageRequest(string relativeOrAbsolutePath)
        {
            string absolutePath = ResolvePath(relativeOrAbsolutePath);
            if (File.Exists(absolutePath))
                return;

            BootstrapPackageManifestPack[] packs = FindOwningBootstrapPacks(absolutePath);
            if (packs.Length == 0)
                return;

            lock (MissingPackageLogGate)
            {
                if (!ReportedMissingPackageRequests.Add(absolutePath))
                    return;

                Directory.CreateDirectory(RuntimeRoot);
                UpsertMissingPackageQueue(absolutePath, packs);
                RefreshPackageStateSnapshot();
                TryRotateLogFile(MissingPackageLogPath, MissingPackageLogRotateBytes, MissingPackageLogKeepCount);
                using var writer = new StreamWriter(MissingPackageLogPath, append: true, Utf8NoBom);
                writer.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} | MissingResource={absolutePath}");
                for (int i = 0; i < packs.Length; i++)
                {
                    string description = string.IsNullOrWhiteSpace(packs[i].Description) ? packs[i].Name : packs[i].Description;
                    writer.WriteLine($"  Pack={packs[i].Name} | Kind={packs[i].Kind} | Description={description} | Manifest={GetPackageManifestHintPath(packs[i])} | InstallRoot={GetPackageInstallRootHint(packs[i])}");
                }
            }
        }

        public static void MarkMissingPackageResolved(string relativeOrAbsolutePath)
        {
            string absolutePath = ResolvePath(relativeOrAbsolutePath);

            lock (MissingPackageLogGate)
            {
                ReportedMissingPackageRequests.Remove(absolutePath);

                if (!File.Exists(MissingPackageQueuePath))
                    return;

                BootstrapMissingPackageQueue queue = LoadMissingPackageQueue();
                BootstrapMissingPackageRequest request = queue.Requests.FirstOrDefault(item =>
                    string.Equals(item.ResourcePath, absolutePath, StringComparison.OrdinalIgnoreCase));
                if (request == null)
                    return;

                request.Status = "resolved";
                request.ResolvedAtUtc = DateTime.UtcNow.ToString("o");
                request.LastSeenAtUtc = request.ResolvedAtUtc;
                WriteJsonFileAtomic(MissingPackageQueuePath, queue);
            }

            RefreshPackageStateSnapshot();
        }

        private static IEnumerable<string> GetFontCandidates(string fileName)
        {
            string safeName = Path.GetFileName(fileName);

            yield return ResolvePath(safeName);
            yield return Path.Combine(ContentRoot, safeName);
            yield return Path.Combine(DataRoot, safeName);
            yield return Path.Combine(DataRoot, "Fonts", safeName);
            yield return Path.Combine(FontCacheRoot, safeName);
        }

        private static string ResolveFileWithCache(string relativeOrAbsoluteFilePath, string primaryRoot, string cacheRoot)
        {
            string primary = ResolvePrimaryFilePath(relativeOrAbsoluteFilePath, primaryRoot);
            if (File.Exists(primary))
                return primary;

            string fallback = BuildCacheMirrorPath(primary, primaryRoot, cacheRoot);
            if (File.Exists(fallback))
                return fallback;

            if (TryHydrateFileFromPackage(primary, out string hydratedPath) && File.Exists(hydratedPath))
                return hydratedPath;

            return fallback;
        }

        private static string ResolvePrimaryFilePath(string relativeOrAbsoluteFilePath, string primaryRoot)
        {
            if (string.IsNullOrWhiteSpace(relativeOrAbsoluteFilePath))
                return primaryRoot;

            if (LooksLikeCompoundPath(relativeOrAbsoluteFilePath))
                return ResolvePath(relativeOrAbsoluteFilePath);

            return Path.Combine(primaryRoot, NormalizeRelativePath(relativeOrAbsoluteFilePath));
        }

        private static bool LooksLikeCompoundPath(string path)
        {
            return Path.IsPathRooted(path)
                || path.StartsWith(".", StringComparison.Ordinal)
                || path.Contains(Path.DirectorySeparatorChar)
                || path.Contains(Path.AltDirectorySeparatorChar)
                || (Uri.TryCreate(path, UriKind.Absolute, out Uri uri) && !uri.IsFile);
        }

        private static string BuildCacheMirrorPath(string absolutePath, string primaryRoot, string cacheRoot)
        {
            string fullAbsolutePath = Path.GetFullPath(absolutePath);
            string normalizedPrimaryRoot = EnsureTrailingSeparator(Path.GetFullPath(primaryRoot));
            string normalizedCacheRoot = EnsureTrailingSeparator(Path.GetFullPath(cacheRoot));

            // 若调用方传入的路径本身已在 cacheRoot 下（例如 ResolveLibraryDirectory() 返回的目录拼出的绝对路径），
            // 则不应再“二次镜像”并丢失子目录层级（否则会把 CArmour/00.Lib 映射成 Libraries/00.Lib，造成冲突与 404）。
            if (fullAbsolutePath.StartsWith(normalizedCacheRoot, StringComparison.OrdinalIgnoreCase))
                return fullAbsolutePath;

            if (!fullAbsolutePath.StartsWith(normalizedPrimaryRoot, StringComparison.OrdinalIgnoreCase))
                return Path.Combine(normalizedCacheRoot, Path.GetFileName(fullAbsolutePath));

            string relativePath = fullAbsolutePath.Substring(normalizedPrimaryRoot.Length);
            return Path.Combine(normalizedCacheRoot, relativePath);
        }

        private static string CombineClientPath(string relativePath)
        {
            return Path.GetFullPath(Path.Combine(ClientRoot, NormalizeRelativePath(relativePath)));
        }

        private static string NormalizeRelativePath(string path)
        {
            string normalizedPath = path
                .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar);

            while (normalizedPath.StartsWith("." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                normalizedPath = normalizedPath.Substring(2);
            }

            return normalizedPath.TrimStart(Path.DirectorySeparatorChar);
        }

        private static string NormalizeRoot(string clientRoot)
        {
            string root = string.IsNullOrWhiteSpace(clientRoot) ? AppContext.BaseDirectory : clientRoot;

            if (File.Exists(root))
                root = Path.GetDirectoryName(root) ?? AppContext.BaseDirectory;

            return Path.GetFullPath(root);
        }

        private static string EnsureTrailingSeparator(string path)
        {
            return path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? path
                : path + Path.DirectorySeparatorChar;
        }

        private static bool TryGetPackageHydrationPlan(string absolutePath, out string targetPath, out string[] candidates)
        {
            string fullPath = Path.GetFullPath(absolutePath);

            if (TryBuildHydrationPlan(fullPath, DataRoot, LibraryCacheRoot, "Data", out targetPath, out candidates))
            {
                candidates = candidates
                    .Concat(new[] { Path.Combine("BootstrapAssets", "Data", Path.GetFileName(fullPath)) })
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                return true;
            }

            if (TryBuildHydrationPlan(fullPath, MapRoot, MapCacheRoot, "Map", out targetPath, out candidates))
            {
                candidates = candidates
                    .Concat(new[] { Path.Combine("BootstrapAssets", "Map", Path.GetFileName(fullPath)) })
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                return true;
            }

            if (TryBuildHydrationPlan(fullPath, SoundRoot, SoundCacheRoot, "Sound", out targetPath, out candidates))
            {
                candidates = candidates
                    .Concat(new[] { Path.Combine("BootstrapAssets", "Sound", Path.GetFileName(fullPath)) })
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                return true;
            }

            if (TryBuildHydrationPlan(fullPath, LibraryCacheRoot, LibraryCacheRoot, "Data", out targetPath, out candidates))
                return true;

            if (TryBuildHydrationPlan(fullPath, MapCacheRoot, MapCacheRoot, "Map", out targetPath, out candidates))
                return true;

            if (TryBuildHydrationPlan(fullPath, SoundCacheRoot, SoundCacheRoot, "Sound", out targetPath, out candidates))
                return true;

            if (TryBuildHydrationPlan(fullPath, FontCacheRoot, FontCacheRoot, "Content", out targetPath, out candidates))
            {
                candidates = candidates
                    .Concat(new[]
                    {
                        Path.Combine("Data", "Fonts", Path.GetFileName(fullPath)),
                        Path.GetFileName(fullPath),
                    })
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                return true;
            }

            if (TryBuildHydrationPlan(fullPath, ContentRoot, FontCacheRoot, "Content", out targetPath, out candidates))
            {
                candidates = candidates
                    .Concat(new[] { Path.GetFileName(fullPath) })
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                return true;
            }

            // Assets/ 下的资源（例如 FairyGUI publish 产物）不走 Data/Map/Sound/Content 的专用缓存目录，
            // 但同样需要支持从已安装分包/BootstrapAssets 回填。
            if (TryBuildHydrationPlan(fullPath, AssetsRoot, AssetsRoot, "Assets", out targetPath, out candidates))
                return true;

            if (IsRuntimeManagedClientFile(fullPath, "Language.ini"))
            {
                targetPath = Path.Combine(RuntimeRoot, "Language.ini");
                candidates = new[] { "Language.ini", "Content/Language.ini", "BootstrapAssets/Language.ini" };
                return true;
            }

            if (IsRuntimeManagedClientFile(fullPath, "Mir2Config.ini"))
            {
                targetPath = Path.Combine(RuntimeRoot, "Mir2Config.ini");
                candidates = new[] { "Mir2Config.ini", "Content/Mir2Config.ini", "BootstrapAssets/Mir2Config.ini" };
                return true;
            }

            targetPath = fullPath;
            candidates = Array.Empty<string>();
            return false;
        }

        private static bool TryBuildHydrationPlan(string fullPath, string sourceRoot, string targetRoot, string packagePrefix, out string targetPath, out string[] candidates)
        {
            if (!TryGetRelativePath(fullPath, sourceRoot, out string relativePath))
            {
                targetPath = fullPath;
                candidates = Array.Empty<string>();
                return false;
            }

            targetPath = Path.Combine(Path.GetFullPath(targetRoot), relativePath);
            candidates = new[]
            {
                Path.Combine(packagePrefix, relativePath),
                Path.Combine("Content", packagePrefix, relativePath),
                Path.Combine("BootstrapAssets", packagePrefix, relativePath),
                relativePath,
            }
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
            return true;
        }

        private static bool TryGetRelativePath(string fullPath, string rootPath, out string relativePath)
        {
            string normalizedRoot = EnsureTrailingSeparator(Path.GetFullPath(rootPath));
            if (!fullPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            {
                relativePath = string.Empty;
                return false;
            }

            relativePath = fullPath.Substring(normalizedRoot.Length);
            return true;
        }

        private static string NormalizeTitleContainerPath(string candidatePath)
        {
            return candidatePath.Replace(Path.DirectorySeparatorChar, '/').Replace('\\', '/');
        }

        private static bool IsRuntimeManagedClientFile(string fullPath, string fileName)
        {
            string clientFile = Path.Combine(ClientRoot, fileName);
            string runtimeFile = Path.Combine(RuntimeRoot, fileName);
            return string.Equals(fullPath, Path.GetFullPath(clientFile), StringComparison.OrdinalIgnoreCase)
                || string.Equals(fullPath, Path.GetFullPath(runtimeFile), StringComparison.OrdinalIgnoreCase);
        }

        private static string[] GetBootstrapManifestAssets()
        {
            lock (BootstrapManifestGate)
            {
                if (_bootstrapManifestAssets != null)
                    return _bootstrapManifestAssets;

                var assets = new List<string>();
                using Stream stream = OpenBootstrapManifestStream();
                if (stream != null)
                {
                    using var reader = new StreamReader(stream);
                    bool inIncludedSection = false;

                    while (!reader.EndOfStream)
                    {
                        string rawLine = reader.ReadLine() ?? string.Empty;
                        string line = rawLine.Trim();

                        if (!inIncludedSection)
                        {
                            if (string.Equals(line, "当前已纳入：", StringComparison.Ordinal))
                                inIncludedSection = true;

                            continue;
                        }

                        if (string.IsNullOrWhiteSpace(line))
                            continue;

                        if (line.EndsWith("：", StringComparison.Ordinal) && !string.Equals(line, "当前已纳入：", StringComparison.Ordinal))
                            break;

                        if (!line.StartsWith("- ", StringComparison.Ordinal))
                            continue;

                        string candidate = line.Substring(2).Trim();
                        if (LooksLikeBootstrapAsset(candidate))
                            assets.Add(NormalizeTitleContainerPath(candidate));
                    }
                }

                _bootstrapManifestAssets = assets
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                return _bootstrapManifestAssets;
            }
        }

        private static BootstrapPackageManifest GetBootstrapPackageManifest()
        {
            lock (BootstrapManifestGate)
            {
                if (_bootstrapPackageManifestLoaded)
                    return _bootstrapPackageManifest;

                BootstrapPackageManifest manifest = null;
                using Stream stream = OpenBootstrapPackageManifestStream();
                if (stream != null)
                {
                    try
                    {
                        manifest = JsonSerializer.Deserialize<BootstrapPackageManifest>(
                            stream,
                            new JsonSerializerOptions
                            {
                                PropertyNameCaseInsensitive = true
                            });
                    }
                    catch (Exception)
                    {
                        manifest = null;
                    }
                }

                manifest ??= new BootstrapPackageManifest();
                manifest.Packs ??= new List<BootstrapPackageManifestPack>();

                var packsByName = new Dictionary<string, BootstrapPackageManifestPack>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < manifest.Packs.Count; i++)
                {
                    BootstrapPackageManifestPack pack = manifest.Packs[i];
                    if (string.IsNullOrWhiteSpace(pack?.Name))
                        continue;

                    packsByName[pack.Name] = NormalizeBootstrapPackagePack(pack);
                }

                foreach (string manifestPath in EnumeratePackageManifestPaths(manifest))
                {
                    if (!TryLoadBootstrapPackagePackManifest(manifestPath, out BootstrapPackageManifestPack pack))
                        continue;

                    if (string.IsNullOrWhiteSpace(pack?.Name))
                        continue;

                    if (packsByName.TryGetValue(pack.Name, out BootstrapPackageManifestPack existing))
                        packsByName[pack.Name] = MergeBootstrapPackagePack(existing, pack);
                    else
                        packsByName[pack.Name] = NormalizeBootstrapPackagePack(pack);
                }

                manifest.Packs = packsByName.Values
                    .OrderBy(pack => pack.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                _bootstrapPackageManifest = manifest;

                _bootstrapPackageManifestLoaded = true;
                return _bootstrapPackageManifest;
            }
        }

        private static IEnumerable<string> EnumeratePackageManifestPaths(BootstrapPackageManifest manifest)
        {
            var manifestPaths = new List<string>();
            var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            AppendManifestDirectoryFiles(PackageManifestDirectory, manifestPaths, seenPaths);
            AppendManifestDirectoryFiles(RuntimePackageManifestDirectory, manifestPaths, seenPaths);

            if (manifest?.Packs != null)
            {
                foreach (BootstrapPackageManifestPack pack in manifest.Packs)
                {
                    if (string.IsNullOrWhiteSpace(pack?.ManifestPath))
                        continue;

                    string normalizedPath = NormalizeTitleContainerPath(pack.ManifestPath);
                    if (seenPaths.Add(normalizedPath))
                        manifestPaths.Add(normalizedPath);
                }
            }

            return manifestPaths;
        }

        private static void AppendManifestDirectoryFiles(
            string directoryPath,
            ICollection<string> manifestPaths,
            ISet<string> seenPaths)
        {
            if (!Directory.Exists(directoryPath))
                return;

            foreach (string file in Directory.GetFiles(directoryPath, "*.json", SearchOption.TopDirectoryOnly))
            {
                string normalizedPath = NormalizeTitleContainerPath(
                    Path.Combine("BootstrapAssets", "bootstrap-package-manifests", Path.GetFileName(file)));

                if (seenPaths.Add(normalizedPath))
                    manifestPaths.Add(normalizedPath);
            }
        }

        private static bool TryLoadBootstrapPackagePackManifest(string manifestPath, out BootstrapPackageManifestPack pack)
        {
            using Stream stream = OpenBootstrapPackageManifestStream(manifestPath);
            if (stream != null)
            {
                try
                {
                    pack = JsonSerializer.Deserialize<BootstrapPackageManifestPack>(
                        stream,
                        new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });

                    if (pack != null && string.IsNullOrWhiteSpace(pack.ManifestPath))
                        pack.ManifestPath = NormalizeTitleContainerPath(manifestPath);

                    return pack != null;
                }
                catch (Exception)
                {
                }
            }

            pack = null;
            return false;
        }

        private static BootstrapPackageManifestPack NormalizeBootstrapPackagePack(BootstrapPackageManifestPack pack)
        {
            pack.ManifestPath = GetPackageManifestHintPath(pack);
            pack.InstallRootHint = GetPackageInstallRootHint(pack);
            pack.Assets ??= new List<string>();
            return pack;
        }

        private static BootstrapPackageManifestPack MergeBootstrapPackagePack(BootstrapPackageManifestPack existing, BootstrapPackageManifestPack incoming)
        {
            existing.Kind = string.IsNullOrWhiteSpace(incoming.Kind) ? existing.Kind : incoming.Kind;
            existing.Description = string.IsNullOrWhiteSpace(incoming.Description) ? existing.Description : incoming.Description;
            existing.ManifestPath = string.IsNullOrWhiteSpace(incoming.ManifestPath) ? existing.ManifestPath : incoming.ManifestPath;
            existing.InstallRootHint = string.IsNullOrWhiteSpace(incoming.InstallRootHint) ? existing.InstallRootHint : incoming.InstallRootHint;

            if (incoming.AssetCount > 0)
                existing.AssetCount = incoming.AssetCount;

            if (incoming.TotalBytes > 0)
                existing.TotalBytes = incoming.TotalBytes;

            if (incoming.Assets != null && incoming.Assets.Count > 0)
                existing.Assets = incoming.Assets;

            return NormalizeBootstrapPackagePack(existing);
        }

        private static BootstrapPackageManifestPack[] FindOwningBootstrapPacks(string relativeOrAbsolutePath)
        {
            BootstrapPackageManifest manifest = GetBootstrapPackageManifest();
            if (manifest?.Packs == null || manifest.Packs.Count == 0)
                return Array.Empty<BootstrapPackageManifestPack>();

            string absolutePath = ResolvePath(relativeOrAbsolutePath);
            if (!TryGetPackageHydrationPlan(absolutePath, out _, out string[] candidates))
                return Array.Empty<BootstrapPackageManifestPack>();

            var normalizedCandidates = new HashSet<string>(
                candidates.Select(candidate => NormalizeTitleContainerPath(candidate).TrimStart('/')),
                StringComparer.OrdinalIgnoreCase);

            return manifest.Packs
                .Where(pack => pack.Assets != null && pack.Assets.Any(asset => normalizedCandidates.Contains(NormalizeTitleContainerPath(asset).TrimStart('/'))))
                .OrderBy(pack => pack.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static bool TryHydrateFromInstalledPackages(string targetPath, string[] candidates, out string availablePath)
        {
            BootstrapPackageManifestPack[] packs = FindOwningBootstrapPacks(targetPath);
            for (int i = 0; i < packs.Length; i++)
            {
                for (int j = 0; j < candidates.Length; j++)
                {
                    string installedAssetPath = BuildInstalledPackageAssetPath(packs[i], candidates[j]);
                    if (!File.Exists(installedAssetPath))
                        continue;

                    if (TryCopyFileToFileAtomic(installedAssetPath, targetPath, overwrite: true))
                    {
                        availablePath = targetPath;
                        return true;
                    }
                }
            }

            availablePath = targetPath;
            return false;
        }

        private static bool EnsurePackageInstalled(string packageName, bool immediateOnly = false)
        {
            BootstrapPackageManifest manifest = GetBootstrapPackageManifest();
            BootstrapPackageManifestPack pack = manifest?.Packs?.FirstOrDefault(item =>
                string.Equals(item.Name, packageName, StringComparison.OrdinalIgnoreCase));
            if (pack == null || pack.Assets == null || pack.Assets.Count == 0)
                return false;

            bool copiedAny = false;

            for (int i = 0; i < pack.Assets.Count; i++)
            {
                string asset = NormalizeTitleContainerPath(pack.Assets[i]).TrimStart('/');
                if (immediateOnly && !IsImmediateBootstrapAsset(asset))
                    continue;

                string installPath = BuildInstalledPackageAssetPath(pack, asset);
                if (File.Exists(installPath))
                    continue;

                copiedAny |= TryCopyBootstrapAssetToInstallRoot(asset, installPath);
            }

            return copiedAny;
        }

        private static bool TryCopyBootstrapAssetToInstallRoot(string assetRelativePath, string installPath)
        {
            string[] candidates = GetBootstrapPackageAssetCandidates(assetRelativePath);
            for (int i = 0; i < candidates.Length; i++)
            {
                string localCandidate = Path.Combine(ClientRoot, candidates[i].Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(localCandidate))
                {
                    return TryCopyFileToFileAtomic(localCandidate, installPath, overwrite: true);
                }

                try
                {
                    using Stream stream = TitleContainer.OpenStream(NormalizeTitleContainerPath(candidates[i]));
                    return TryCopyStreamToFileAtomic(stream, installPath);
                }
                catch (Exception)
                {
                }
            }

            return false;
        }

        private static string[] GetBootstrapPackageAssetCandidates(string assetRelativePath)
        {
            string normalizedAsset = NormalizeTitleContainerPath(assetRelativePath).TrimStart('/');
            var candidates = new List<string>
            {
                normalizedAsset,
                "BootstrapAssets/" + normalizedAsset,
            };

            if (normalizedAsset.StartsWith("Content/", StringComparison.OrdinalIgnoreCase))
            {
                candidates.Add(Path.GetFileName(normalizedAsset));
            }

            return candidates
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static string GetPackageManifestHintPath(BootstrapPackageManifestPack pack)
        {
            return string.IsNullOrWhiteSpace(pack.ManifestPath) ? GetPackageManifestHintPath(pack.Name) : pack.ManifestPath;
        }

        private static string GetPackageManifestHintPath(string packageName)
        {
            return Path.Combine("BootstrapAssets", "bootstrap-package-manifests", packageName + ".json").Replace('\\', '/');
        }

        private static string GetPackageInstallRootHint(BootstrapPackageManifestPack pack)
        {
            return string.IsNullOrWhiteSpace(pack.InstallRootHint)
                ? Path.Combine("Cache", "Mobile", "Packages", pack.Name ?? string.Empty).Replace('\\', '/') + "/"
                : pack.InstallRootHint;
        }

        private static string BuildInstalledPackageAssetPath(BootstrapPackageManifestPack pack, string assetRelativePath)
        {
            string relativeAsset = NormalizeTitleContainerPath(assetRelativePath).TrimStart('/');
            return Path.Combine(PackageCacheRoot, pack.Name ?? string.Empty, relativeAsset.Replace('/', Path.DirectorySeparatorChar));
        }

        private static void UpsertMissingPackageQueue(string absolutePath, BootstrapPackageManifestPack[] packs)
        {
            BootstrapMissingPackageQueue queue = LoadMissingPackageQueue();
            BootstrapMissingPackageRequest request = queue.Requests.FirstOrDefault(item =>
                string.Equals(item.ResourcePath, absolutePath, StringComparison.OrdinalIgnoreCase));

            string now = DateTime.UtcNow.ToString("o");
            if (request == null)
            {
                request = new BootstrapMissingPackageRequest
                {
                    ResourcePath = absolutePath,
                    Status = "pending",
                    CreatedAtUtc = now,
                };
                queue.Requests.Add(request);
            }

            request.LastSeenAtUtc = now;
            request.Status = "pending";
            request.ResolvedAtUtc = null;
            request.Occurrences++;
            request.Packages = packs
                .Select(pack => new BootstrapMissingPackageReference
                {
                    Name = pack.Name,
                    Kind = pack.Kind,
                    Description = pack.Description,
                    AssetCount = pack.AssetCount,
                    TotalBytes = pack.TotalBytes,
                    ManifestPath = GetPackageManifestHintPath(pack),
                    InstallRootHint = GetPackageInstallRootHint(pack),
                })
                .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            queue.Requests = queue.Requests
                .OrderBy(item => item.ResourcePath, StringComparer.OrdinalIgnoreCase)
                .ToList();

            WriteJsonFileAtomic(MissingPackageQueuePath, queue);
        }

        private static void ProcessPendingPackageRequests()
        {
            BootstrapMissingPackageQueue queue = LoadMissingPackageQueue();
            if (queue.Requests.Count == 0)
                return;

            bool changed = false;
            string now = DateTime.UtcNow.ToString("o");

            foreach (BootstrapMissingPackageRequest request in queue.Requests)
            {
                if (string.Equals(request.Status, "resolved", StringComparison.OrdinalIgnoreCase))
                    continue;

                bool stagedAny = false;
                for (int i = 0; i < request.Packages.Count; i++)
                {
                    stagedAny |= EnsurePackageInstalled(request.Packages[i].Name);
                }

                if (TryHydrateFileFromPackage(request.ResourcePath, out string availablePath) && File.Exists(availablePath))
                {
                    request.Status = "resolved";
                    request.ResolvedAtUtc = now;
                    request.LastSeenAtUtc = now;
                    ReportedMissingPackageRequests.Remove(request.ResourcePath);
                    changed = true;
                    continue;
                }

                string nextStatus = stagedAny ? "staged" : request.Status;
                if (!string.Equals(request.Status, nextStatus, StringComparison.OrdinalIgnoreCase))
                {
                    request.Status = nextStatus;
                    changed = true;
                }

                request.LastSeenAtUtc = now;
            }

            if (changed)
            {
                WriteJsonFileAtomic(MissingPackageQueuePath, queue);
                RefreshPackageStateSnapshot();
            }
        }

        public static void RefreshPackageStateSnapshot()
        {
            BootstrapPackageManifest manifest = GetBootstrapPackageManifest();
            if (manifest?.Packs == null || manifest.Packs.Count == 0)
                return;

            lock (PackageStateGate)
            {
                var snapshot = new BootstrapPackageStateSnapshot
                {
                    GeneratedAtUtc = DateTime.UtcNow.ToString("o"),
                    PackageCount = manifest.Packs.Count,
                };

                for (int i = 0; i < manifest.Packs.Count; i++)
                {
                    BootstrapPackageManifestPack pack = manifest.Packs[i];
                    string[] assets = (pack.Assets ?? new List<string>())
                        .Select(asset => NormalizeTitleContainerPath(asset).TrimStart('/'))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray();

                    int hydratedCount = 0;
                    int stagedCount = 0;
                    var missingAssets = new List<string>();
                    for (int j = 0; j < assets.Length; j++)
                    {
                        if (TryResolveBootstrapAssetTargetPath(assets[j], out string targetPath) && File.Exists(targetPath))
                        {
                            hydratedCount++;
                        }
                        else if (File.Exists(BuildInstalledPackageAssetPath(pack, assets[j])))
                        {
                            stagedCount++;
                        }
                        else if (PackageResourceRegistry.TryEnsureSharedSoundAliasAvailable(assets[j], out _))
                        {
                            hydratedCount++;
                        }
                        else
                        {
                            missingAssets.Add(assets[j]);
                        }
                    }

                    snapshot.Packages.Add(new BootstrapPackageState
                    {
                        Name = pack.Name,
                        Kind = pack.Kind,
                        Description = pack.Description,
                        ManifestPath = GetPackageManifestHintPath(pack),
                        InstallRootHint = GetPackageInstallRootHint(pack),
                        AssetCount = assets.Length,
                        HydratedAssetCount = hydratedCount,
                        StagedAssetCount = stagedCount,
                        Status = ResolvePackageStateStatus(assets.Length, hydratedCount, stagedCount),
                        MissingAssetsSample = missingAssets.Take(5).ToList(),
                    });
                }

                snapshot.HydratedPackageCount = snapshot.Packages.Count(item => string.Equals(item.Status, "hydrated", StringComparison.OrdinalIgnoreCase));
                snapshot.StagedPackageCount = snapshot.Packages.Count(item => string.Equals(item.Status, "staged", StringComparison.OrdinalIgnoreCase));
                snapshot.PartialPackageCount = snapshot.Packages.Count(item => string.Equals(item.Status, "partial", StringComparison.OrdinalIgnoreCase));
                snapshot.PendingPackageCount = snapshot.Packages.Count(item => string.Equals(item.Status, "declared", StringComparison.OrdinalIgnoreCase));

                WriteJsonFileAtomic(PackageStateSnapshotPath, snapshot);
                TryWritePackageDiagnosticsReport();
            }
        }

        private static string ResolvePackageStateStatus(int assetCount, int hydratedCount, int stagedCount)
        {
            if (assetCount <= 0)
                return "empty";

            if (hydratedCount <= 0 && stagedCount <= 0)
                return "declared";

            if (hydratedCount >= assetCount)
                return "hydrated";

            if (hydratedCount == 0 && stagedCount >= assetCount)
                return "staged";

            return "partial";
        }

        private static BootstrapMissingPackageQueue LoadMissingPackageQueue()
        {
            if (!File.Exists(MissingPackageQueuePath))
                return new BootstrapMissingPackageQueue();

            try
            {
                return JsonSerializer.Deserialize<BootstrapMissingPackageQueue>(File.ReadAllText(MissingPackageQueuePath))
                       ?? new BootstrapMissingPackageQueue();
            }
            catch (Exception)
            {
                return new BootstrapMissingPackageQueue();
            }
        }

        private static Stream OpenBootstrapManifestStream()
        {
            if (File.Exists(RuntimeBootstrapAssetManifestPath))
                return File.OpenRead(RuntimeBootstrapAssetManifestPath);

            string localPath = Path.Combine(ClientRoot, "BootstrapAssets", "bootstrap-assets.txt");
            if (File.Exists(localPath))
                return File.OpenRead(localPath);

            try
            {
                return TitleContainer.OpenStream("BootstrapAssets/bootstrap-assets.txt");
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static Stream OpenBootstrapPackageManifestStream()
        {
            if (File.Exists(RuntimePackageManifestPath))
                return File.OpenRead(RuntimePackageManifestPath);

            string localPath = Path.Combine(ClientRoot, "BootstrapAssets", "bootstrap-packages.json");
            if (File.Exists(localPath))
                return File.OpenRead(localPath);

            try
            {
                return TitleContainer.OpenStream("BootstrapAssets/bootstrap-packages.json");
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static Stream OpenBootstrapPackageManifestStream(string manifestPath)
        {
            if (string.IsNullOrWhiteSpace(manifestPath))
                return null;

            string normalizedPath = NormalizeTitleContainerPath(manifestPath).TrimStart('/');
            string overridePath = GetRuntimeManifestOverrideAbsolutePath(normalizedPath);
            if (File.Exists(overridePath))
                return File.OpenRead(overridePath);

            string localPath = Path.Combine(ClientRoot, normalizedPath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(localPath))
                return File.OpenRead(localPath);

            try
            {
                return TitleContainer.OpenStream(normalizedPath);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string GetRuntimeManifestOverrideAbsolutePath(string normalizedPath)
        {
            string relativePath = normalizedPath.StartsWith("BootstrapAssets/", StringComparison.OrdinalIgnoreCase)
                ? normalizedPath.Substring("BootstrapAssets/".Length)
                : normalizedPath;

            return Path.Combine(RuntimeManifestOverrideRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static bool LooksLikeBootstrapAsset(string candidate)
        {
            return candidate.IndexOf('/') >= 0
                || candidate.EndsWith(".ini", StringComparison.OrdinalIgnoreCase)
                || candidate.EndsWith(".lst", StringComparison.OrdinalIgnoreCase)
                || candidate.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase);
        }

        private static string[] GetBootstrapWarmupAssets()
        {
            var corePackAssets = GetBootstrapPackageManifest()?.Packs
                ?.Where(pack => string.Equals(pack.Kind, "core", StringComparison.OrdinalIgnoreCase))
                .SelectMany(pack => pack.Assets ?? new List<string>())
                .Select(asset => NormalizeTitleContainerPath(asset))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
                ?? Array.Empty<string>();

            var manifestAssets = new HashSet<string>(
                corePackAssets.Length > 0 ? corePackAssets : GetBootstrapManifestAssets(),
                StringComparer.OrdinalIgnoreCase);
            var warmupAssets = new List<string>();

            for (int i = 0; i < BootstrapWarmupPriorityAssets.Length; i++)
            {
                string asset = NormalizeTitleContainerPath(BootstrapWarmupPriorityAssets[i]);
                if (asset.StartsWith("Content/", StringComparison.OrdinalIgnoreCase)
                    || manifestAssets.Contains(asset))
                {
                    warmupAssets.Add(asset);
                }
            }

            foreach (string asset in manifestAssets)
            {
                if (IsImmediateBootstrapAsset(asset) && !warmupAssets.Contains(asset, StringComparer.OrdinalIgnoreCase))
                    warmupAssets.Add(asset);
            }

            return warmupAssets
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static bool IsImmediateBootstrapAsset(string asset)
        {
            string extension = Path.GetExtension(asset);
            return extension.Equals(".ini", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".lst", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".ttf", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryHydrateBootstrapAsset(string assetRelativePath)
        {
            if (!TryResolveBootstrapAssetTargetPath(assetRelativePath, out string targetPath))
                return false;

            return TryHydrateFileFromPackage(targetPath, out _);
        }

        private static bool TryGetBootstrapAssetTargetPath(string assetRelativePath, out string targetPath)
        {
            string normalizedAsset = NormalizeTitleContainerPath(assetRelativePath).TrimStart('/');

            if (string.Equals(normalizedAsset, "Language.ini", StringComparison.OrdinalIgnoreCase))
            {
                targetPath = LanguageFilePath;
                return true;
            }

            if (string.Equals(normalizedAsset, "Mir2Config.ini", StringComparison.OrdinalIgnoreCase))
            {
                targetPath = ConfigFilePath;
                return true;
            }

            if (string.Equals(normalizedAsset, "hm.ttf", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalizedAsset, "Content/hm.ttf", StringComparison.OrdinalIgnoreCase))
            {
                targetPath = Path.Combine(ContentRoot, "hm.ttf");
                return true;
            }

            if (normalizedAsset.StartsWith("Data/", StringComparison.OrdinalIgnoreCase))
            {
                targetPath = Path.Combine(DataRoot, normalizedAsset.Substring("Data/".Length).Replace('/', Path.DirectorySeparatorChar));
                return true;
            }

            if (normalizedAsset.StartsWith("Map/", StringComparison.OrdinalIgnoreCase))
            {
                targetPath = Path.Combine(MapRoot, normalizedAsset.Substring("Map/".Length).Replace('/', Path.DirectorySeparatorChar));
                return true;
            }

            if (normalizedAsset.StartsWith("Sound/", StringComparison.OrdinalIgnoreCase))
            {
                targetPath = Path.Combine(SoundRoot, normalizedAsset.Substring("Sound/".Length).Replace('/', Path.DirectorySeparatorChar));
                return true;
            }

            if (normalizedAsset.StartsWith("Content/", StringComparison.OrdinalIgnoreCase))
            {
                targetPath = Path.Combine(ContentRoot, normalizedAsset.Substring("Content/".Length).Replace('/', Path.DirectorySeparatorChar));
                return true;
            }

            targetPath = ResolvePath(normalizedAsset);
            return true;
        }

        private static void WriteJsonFileAtomic<T>(string filePath, T value)
        {
            string json = JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true });
            WriteTextFileAtomic(filePath, json);
        }

        private static void WriteTextFileAtomic(string filePath, string contents)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return;

            lock (FileWriteGate)
            {
                string directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrWhiteSpace(directory))
                    Directory.CreateDirectory(directory);

                string tempPath = filePath + ".tmp-" + Guid.NewGuid().ToString("N");
                try
                {
                    File.WriteAllText(tempPath, contents ?? string.Empty, Utf8NoBom);
                    File.Move(tempPath, filePath, overwrite: true);
                }
                finally
                {
                    try
                    {
                        if (File.Exists(tempPath))
                            File.Delete(tempPath);
                    }
                    catch (Exception)
                    {
                    }
                }
            }
        }

        private static bool TryCopyStreamToFileAtomic(Stream sourceStream, string targetPath)
        {
            if (sourceStream == null || string.IsNullOrWhiteSpace(targetPath))
                return false;

            lock (FileWriteGate)
            {
                string directory = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrWhiteSpace(directory))
                    Directory.CreateDirectory(directory);

                string tempPath = targetPath + ".tmp-" + Guid.NewGuid().ToString("N");
                try
                {
                    using (FileStream destinationStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.Read))
                    {
                        sourceStream.CopyTo(destinationStream);
                    }

                    File.Move(tempPath, targetPath, overwrite: true);
                    return true;
                }
                catch (Exception)
                {
                    return false;
                }
                finally
                {
                    try
                    {
                        if (File.Exists(tempPath))
                            File.Delete(tempPath);
                    }
                    catch (Exception)
                    {
                    }
                }
            }
        }

        private static bool TryCopyFileToFileAtomic(string sourcePath, string targetPath, bool overwrite)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || string.IsNullOrWhiteSpace(targetPath))
                return false;

            if (!File.Exists(sourcePath))
                return false;

            if (!overwrite && File.Exists(targetPath))
                return true;

            try
            {
                using Stream stream = File.OpenRead(sourcePath);
                return TryCopyStreamToFileAtomic(stream, targetPath);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static void TryRotateLogFile(string filePath, long maxBytes, int keepCount)
        {
            if (string.IsNullOrWhiteSpace(filePath) || keepCount <= 0 || maxBytes <= 0)
                return;

            lock (FileWriteGate)
            {
                try
                {
                    var info = new FileInfo(filePath);
                    if (!info.Exists || info.Length <= maxBytes)
                        return;

                    // filePath -> filePath.1 -> ... -> filePath.keepCount
                    for (int i = keepCount; i >= 1; i--)
                    {
                        string suffix = i == 1 ? string.Empty : "." + (i - 1);
                        string source = filePath + suffix;
                        string destination = filePath + "." + i;

                        if (!File.Exists(source))
                            continue;

                        File.Move(source, destination, overwrite: true);
                    }
                }
                catch (Exception)
                {
                }
            }
        }

        private sealed class BootstrapPackageManifest
        {
            public List<BootstrapPackageManifestPack> Packs { get; set; } = new List<BootstrapPackageManifestPack>();
        }

        private sealed class BootstrapPackageManifestPack
        {
            public string Name { get; set; }
            public string Kind { get; set; }
            public string Description { get; set; }
            public int AssetCount { get; set; }
            public long TotalBytes { get; set; }
            public string ManifestPath { get; set; }
            public string InstallRootHint { get; set; }
            public List<string> Assets { get; set; } = new List<string>();
        }

        private sealed class BootstrapMissingPackageQueue
        {
            public List<BootstrapMissingPackageRequest> Requests { get; set; } = new List<BootstrapMissingPackageRequest>();
        }

        private sealed class BootstrapMissingPackageRequest
        {
            public string ResourcePath { get; set; }
            public string Status { get; set; }
            public string CreatedAtUtc { get; set; }
            public string LastSeenAtUtc { get; set; }
            public string ResolvedAtUtc { get; set; }
            public int Occurrences { get; set; }
            public List<BootstrapMissingPackageReference> Packages { get; set; } = new List<BootstrapMissingPackageReference>();
        }

        private sealed class BootstrapMissingPackageReference
        {
            public string Name { get; set; }
            public string Kind { get; set; }
            public string Description { get; set; }
            public int AssetCount { get; set; }
            public long TotalBytes { get; set; }
            public string ManifestPath { get; set; }
            public string InstallRootHint { get; set; }
        }

        private sealed class BootstrapPackageStateSnapshot
        {
            public string GeneratedAtUtc { get; set; }
            public int PackageCount { get; set; }
            public int HydratedPackageCount { get; set; }
            public int StagedPackageCount { get; set; }
            public int PartialPackageCount { get; set; }
            public int PendingPackageCount { get; set; }
            public List<BootstrapPackageState> Packages { get; set; } = new List<BootstrapPackageState>();
        }

        private sealed class BootstrapPackageState
        {
            public string Name { get; set; }
            public string Kind { get; set; }
            public string Description { get; set; }
            public string ManifestPath { get; set; }
            public string InstallRootHint { get; set; }
            public int AssetCount { get; set; }
            public int HydratedAssetCount { get; set; }
            public int StagedAssetCount { get; set; }
            public string Status { get; set; }
            public List<string> MissingAssetsSample { get; set; } = new List<string>();
        }
    }
}
