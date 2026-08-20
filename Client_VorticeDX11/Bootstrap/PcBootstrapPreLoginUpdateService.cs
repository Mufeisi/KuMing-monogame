using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Client.Bootstrap
{
    internal static class PcBootstrapPreLoginUpdateService
    {
        private static readonly object LogGate = new object();
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        private static readonly JsonSerializerOptions JsonReadOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        private static readonly JsonSerializerOptions JsonWriteOptions = new JsonSerializerOptions { WriteIndented = true };

        public static async Task<PcBootstrapPreLoginUpdatePlanView> TryEnsurePreLoginUpdateQueueAsync(CancellationToken cancellationToken)
        {
            try
            {
                PcBootstrapLayout.EnsureWritableDirectories();

                if (!Settings.BootstrapPreLoginUpdate)
                    return PcBootstrapPreLoginUpdatePlanView.Skip("已关闭登录前资源更新（Mir2Config.ini：[Bootstrap] PreLoginUpdate=False）。");

                string repositoryRoot = PcBootstrapHttp.ResolveRepositoryRoot(out bool useMicroAuth);
                if (string.IsNullOrWhiteSpace(repositoryRoot))
                    return PcBootstrapPreLoginUpdatePlanView.Skip("未配置分包仓库地址。请配置 [Bootstrap] PackageRepo，或配置 [Micro] BaseUrl/User 以自动使用 MicroBaseUrl + file/。");

                if (!Settings.BootstrapAutoDownload)
                    return PcBootstrapPreLoginUpdatePlanView.Skip("自动下载已关闭，无法在登录前执行资源更新（Mir2Config.ini：[Bootstrap] AutoDownload=True）。");

                // 若已存在更新队列（例如上次未完成），优先继续，不覆盖。
                BootstrapPackageUpdateQueueView existingQueue = PcBootstrapUpdateRuntime.LoadUpdateQueue();
                if (existingQueue.Packages != null && existingQueue.Packages.Count > 0)
                {
                    return new PcBootstrapPreLoginUpdatePlanView
                    {
                        Skipped = false,
                        Failed = false,
                        RepositoryRoot = repositoryRoot,
                        ResourceVersion = existingQueue.ResourceVersion ?? string.Empty,
                        PackagesToUpdate = existingQueue.Packages
                            .Select(item => item?.Name)
                            .Where(item => !string.IsNullOrWhiteSpace(item))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList(),
                        Message = "检测到未完成的更新队列，继续执行。",
                    };
                }

                TrySeedInstalledVersionsFromBaselineIndex();

                string indexUrl = PcBootstrapHttp.BuildRemoteIndexUrl(repositoryRoot);
                PcBootstrapPackageIndexView remoteIndex = await PcBootstrapHttp.TryDownloadPackageIndexAsync(indexUrl, useMicroAuth, cancellationToken);
                if (remoteIndex == null || remoteIndex.Packages == null || remoteIndex.Packages.Count == 0)
                    return PcBootstrapPreLoginUpdatePlanView.Skip("远端未提供 bootstrap-package-index.json 或内容为空。");

                PcBootstrapHttp.TryWriteRemoteIndexCache(remoteIndex);

                var remoteByName = remoteIndex.Packages
                    .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Name))
                    .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);

                HashSet<string> installedPackages = PcBootstrapUpdateRuntime.GetInstalledPackageNames();
                if (installedPackages.Count == 0)
                {
                    return PcBootstrapPreLoginUpdatePlanView.Skip("本机未发现已安装的资源包记录（BootstrapPackageVersions.json 为空）。请确认 PC 端已携带 baseline 索引或已执行过分包安装。");
                }

                var updates = new List<BootstrapPackageUpdateEntryView>();
                foreach (string packageName in installedPackages.OrderBy(item => item, StringComparer.OrdinalIgnoreCase))
                {
                    if (!remoteByName.TryGetValue(packageName, out PcBootstrapPackageIndexPackageView remote))
                        continue;

                    string remoteSha = (remote.Sha256 ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(remoteSha))
                        continue;

                    string localSha = PcBootstrapUpdateRuntime.GetInstalledSha256(packageName);
                    if (string.IsNullOrWhiteSpace(localSha))
                        continue;

                    if (!string.Equals(localSha, remoteSha, StringComparison.OrdinalIgnoreCase))
                    {
                        updates.Add(new BootstrapPackageUpdateEntryView
                        {
                            Name = packageName,
                            DesiredSha256 = remoteSha,
                            Reason = "remote-index-sha-changed",
                        });
                    }
                }

                string resourceVersion = remoteIndex.ResourceVersion ?? remoteIndex.GeneratedAtUtc ?? string.Empty;

                if (updates.Count == 0)
                {
                    PcBootstrapUpdateRuntime.ReplaceUpdateQueue(resourceVersion, Array.Empty<BootstrapPackageUpdateEntryView>(), "已是最新版本，无需更新。");
                    return new PcBootstrapPreLoginUpdatePlanView
                    {
                        Skipped = false,
                        Failed = false,
                        RepositoryRoot = repositoryRoot,
                        ResourceVersion = resourceVersion,
                        PackagesToUpdate = new List<string>(),
                        Message = "资源已是最新。",
                    };
                }

                PcBootstrapUpdateRuntime.ReplaceUpdateQueue(resourceVersion, updates, $"将更新 {updates.Count} 个资源包。");

                return new PcBootstrapPreLoginUpdatePlanView
                {
                    Skipped = false,
                    Failed = false,
                    RepositoryRoot = repositoryRoot,
                    ResourceVersion = resourceVersion,
                    PackagesToUpdate = updates.Select(item => item.Name).ToList(),
                    Message = $"将更新 {updates.Count} 个资源包。",
                };
            }
            catch (Exception ex)
            {
                TryAppendLog($"FAIL | EnsureQueue | Error={ex}");
                return PcBootstrapPreLoginUpdatePlanView.Fail(ex.Message);
            }
        }

        public static async Task<PcBootstrapApplyResultView> TryApplyUpdateQueueAsync(IProgress<PcBootstrapProgress> progress, CancellationToken cancellationToken)
        {
            try
            {
                PcBootstrapLayout.EnsureWritableDirectories();

                if (!Settings.BootstrapPreLoginUpdate)
                    return PcBootstrapApplyResultView.Skip("已关闭登录前资源更新（Mir2Config.ini：[Bootstrap] PreLoginUpdate=False）。");

                string repositoryRoot = PcBootstrapHttp.ResolveRepositoryRoot(out bool useMicroAuth);
                if (string.IsNullOrWhiteSpace(repositoryRoot))
                    return PcBootstrapApplyResultView.Skip("未配置分包仓库地址。");

                BootstrapPackageUpdateQueueView queue = PcBootstrapUpdateRuntime.LoadUpdateQueue();
                if (queue.Packages == null || queue.Packages.Count == 0)
                {
                    return new PcBootstrapApplyResultView
                    {
                        Completed = true,
                        Skipped = false,
                        Failed = false,
                        ResourceVersion = queue.ResourceVersion ?? string.Empty,
                        Message = "无待更新资源包。",
                    };
                }

                // 可选：下载一次索引，用于获得 size（不影响主流程）
                Dictionary<string, long> remoteSizeByName = null;
                try
                {
                    string indexUrl = PcBootstrapHttp.BuildRemoteIndexUrl(repositoryRoot);
                    PcBootstrapPackageIndexView index = await PcBootstrapHttp.TryDownloadPackageIndexAsync(indexUrl, useMicroAuth, cancellationToken);
                    if (index?.Packages != null)
                    {
                        remoteSizeByName = index.Packages
                            .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Name))
                            .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                            .ToDictionary(group => group.Key, group => group.Last().Size, StringComparer.OrdinalIgnoreCase);
                    }
                }
                catch (Exception)
                {
                    remoteSizeByName = null;
                }

                var updatedPackages = new List<string>();
                int maxRetries = Math.Max(0, Settings.BootstrapRetryCount);

                foreach (BootstrapPackageUpdateEntryView entry in queue.Packages.ToList())
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    string packageName = (entry?.Name ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(packageName))
                        continue;

                    string desiredSha = (entry?.DesiredSha256 ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(desiredSha))
                    {
                        TryAppendLog($"SKIP | Pack={packageName} | Reason=DesiredSha256Empty");
                        PcBootstrapUpdateRuntime.RemovePackagesFromUpdateQueue(new[] { packageName });
                        continue;
                    }

                    string zipUrl = PcBootstrapHttp.BuildRemotePackageZipUrl(repositoryRoot, packageName);
                    long expectedSize = 0;
                    if (remoteSizeByName != null && remoteSizeByName.TryGetValue(packageName, out long size))
                        expectedSize = Math.Max(0, size);

                    Exception lastError = null;
                    for (int attempt = 0; attempt <= maxRetries; attempt++)
                    {
                        try
                        {
                            string attemptTag = attempt == 0 ? "first" : $"retry-{attempt}";
                            TryAppendLog($"INFO | Pack={packageName} | Stage=Start | Attempt={attemptTag} | Url={zipUrl}");

                            progress?.Report(new PcBootstrapProgress
                            {
                                Stage = "prepare",
                                PackageName = packageName,
                                Message = $"准备更新：{packageName}",
                            });

                            string localZipPath = await PcBootstrapHttp.DownloadPackageZipAsync(
                                packageName,
                                zipUrl,
                                expectedSize,
                                useMicroAuth,
                                progress,
                                cancellationToken);

                            if (!PcBootstrapHttp.VerifyZipSha256IfEnabled(packageName, localZipPath, desiredSha))
                            {
                                TryDeleteFile(localZipPath);
                                throw new InvalidDataException("SHA256 校验失败。");
                            }

                            progress?.Report(new PcBootstrapProgress
                            {
                                Stage = "extract",
                                PackageName = packageName,
                                Message = $"解压中：{packageName}",
                            });

                            string stagingRoot = PcBootstrapZipInstaller.ExtractZipToStaging(localZipPath, packageName);

                            progress?.Report(new PcBootstrapProgress
                            {
                                Stage = "install",
                                PackageName = packageName,
                                Message = $"安装中：{packageName}",
                            });

                            int installedFiles = PcBootstrapZipInstaller.InstallExtractedPackageToClient(stagingRoot, packageName);

                            TryAppendLog($"OK | Pack={packageName} | InstalledFiles={installedFiles}");

                            PcBootstrapUpdateRuntime.UpsertInstalledVersion(packageName, desiredSha, source: "download");
                            PcBootstrapUpdateRuntime.RemovePackagesFromUpdateQueue(new[] { packageName });

                            updatedPackages.Add(packageName);

                            TryDeleteDirectory(stagingRoot);
                            TryDeleteFile(localZipPath);

                            break;
                        }
                        catch (Exception ex)
                        {
                            lastError = ex;
                            TryAppendLog($"FAIL | Pack={packageName} | Attempt={attempt} | Error={ex.Message}");

                            if (attempt >= maxRetries)
                                throw;

                            int backoffSeconds = Math.Clamp(2 + attempt * 2, 2, 30);
                            await Task.Delay(TimeSpan.FromSeconds(backoffSeconds), cancellationToken);
                        }
                    }
                }

                return new PcBootstrapApplyResultView
                {
                    Completed = true,
                    Skipped = false,
                    Failed = false,
                    ResourceVersion = queue.ResourceVersion ?? string.Empty,
                    UpdatedPackageCount = updatedPackages.Count,
                    UpdatedPackages = updatedPackages,
                    Message = updatedPackages.Count == 0 ? "资源已是最新。" : $"已更新 {updatedPackages.Count} 个资源包。",
                };
            }
            catch (OperationCanceledException)
            {
                return PcBootstrapApplyResultView.Skip("已取消资源更新。");
            }
            catch (Exception ex)
            {
                TryAppendLog($"FAIL | ApplyQueue | Error={ex}");
                if (Settings.LogErrors)
                    CMain.SaveError($"PC PreLoginUpdate 应用更新队列失败：{ex}");
                return PcBootstrapApplyResultView.Fail(ex.Message);
            }
        }

        public static async Task<PcBootstrapApplyResultView> TryRunPreLoginUpdateAsync(IProgress<PcBootstrapProgress> progress, CancellationToken cancellationToken)
        {
            PcBootstrapPreLoginUpdatePlanView plan = await TryEnsurePreLoginUpdateQueueAsync(cancellationToken);
            if (plan == null)
                return PcBootstrapApplyResultView.Skip("未获取到更新计划。");

            if (plan.Failed)
                return PcBootstrapApplyResultView.Fail(plan.Message);

            if (plan.Skipped)
                return PcBootstrapApplyResultView.Skip(plan.Message);

            if ((plan.PackagesToUpdate?.Count ?? 0) == 0)
            {
                return new PcBootstrapApplyResultView
                {
                    Completed = true,
                    Skipped = false,
                    Failed = false,
                    ResourceVersion = plan.ResourceVersion ?? string.Empty,
                    Message = plan.Message ?? "资源已是最新。",
                };
            }

            return await TryApplyUpdateQueueAsync(progress, cancellationToken);
        }

        private static void TrySeedInstalledVersionsFromBaselineIndex()
        {
            try
            {
                BootstrapPackageVersionsSnapshotView snapshot = PcBootstrapUpdateRuntime.LoadInstalledVersions();
                if ((snapshot.Packages?.Count ?? 0) > 0)
                    return;

                string baselinePath = PcBootstrapLayout.BaselinePackageIndexPath;
                if (!File.Exists(baselinePath))
                    return;

                string json = File.ReadAllText(baselinePath) ?? string.Empty;
                json = (json ?? string.Empty).TrimStart('\uFEFF');

                PcBootstrapPackageIndexView baseline = JsonSerializer.Deserialize<PcBootstrapPackageIndexView>(json, JsonReadOptions)
                                                    ?? new PcBootstrapPackageIndexView();

                if (baseline.Packages == null || baseline.Packages.Count == 0)
                    return;

                DateTime nowUtc = DateTime.UtcNow;
                string nowStamp = nowUtc.ToString("o");

                var packages = baseline.Packages
                    .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Name) && !string.IsNullOrWhiteSpace(item.Sha256))
                    .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.Last())
                    .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(item => new BootstrapPackageVersionEntryView
                    {
                        Name = item.Name.Trim(),
                        Sha256 = (item.Sha256 ?? string.Empty).Trim(),
                        Source = "bootstrap-assets",
                        InstalledAtUtc = nowStamp,
                    })
                    .ToList();

                if (packages.Count == 0)
                    return;

                var seeded = new BootstrapPackageVersionsSnapshotView
                {
                    GeneratedAtUtc = nowStamp,
                    Packages = packages,
                };

                WriteJsonFileAtomic(PcBootstrapLayout.VersionsPath, seeded);
                TryAppendLog($"OK | SeedBaseline | Packages={packages.Count}");
            }
            catch (Exception)
            {
            }
        }

        internal static void TryAppendLog(string message)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(message))
                    return;

                Directory.CreateDirectory(PcBootstrapLayout.RuntimeRoot);

                lock (LogGate)
                {
                    string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}";
                    File.AppendAllText(PcBootstrapLayout.PreLoginUpdateLogPath, line, Utf8NoBom);
                }
            }
            catch (Exception)
            {
            }
        }

        private static void WriteJsonFileAtomic<T>(string outputPath, T payload)
        {
            if (string.IsNullOrWhiteSpace(outputPath))
                return;

            string directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            string tempPath = outputPath + ".tmp";
            string json = JsonSerializer.Serialize(payload ?? new object(), JsonWriteOptions);
            File.WriteAllText(tempPath, json ?? string.Empty, Utf8NoBom);
            File.Move(tempPath, outputPath, overwrite: true);
        }

        private static void TryDeleteFile(string filePath)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath))
                    File.Delete(filePath);
            }
            catch (Exception)
            {
            }
        }

        private static void TryDeleteDirectory(string directoryPath)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(directoryPath) && Directory.Exists(directoryPath))
                    Directory.Delete(directoryPath, recursive: true);
            }
            catch (Exception)
            {
            }
        }
    }
}

