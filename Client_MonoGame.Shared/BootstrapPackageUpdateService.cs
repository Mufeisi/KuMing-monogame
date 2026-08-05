using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;

namespace MonoShare
{
    internal static class BootstrapPackageUpdateService
    {
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        private static readonly JsonSerializerOptions JsonReadOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        private static readonly JsonSerializerOptions JsonWriteOptions = new JsonSerializerOptions { WriteIndented = true };

        private static HttpClient _httpClient;

        public static async Task<BootstrapPreLoginUpdatePlanView> TryEnsurePreLoginUpdateQueueAsync(CancellationToken cancellationToken)
        {
            try
            {
                ClientResourceLayout.EnsureWritableResourceDirectories();

                BootstrapPackageDownloader.BootstrapDownloadStateSnapshot snapshot = BootstrapPackageDownloader.GetStateSnapshot();
                if (snapshot == null)
                    return BootstrapPreLoginUpdatePlanView.Skip("未获取到下载器状态快照。");

                if (!snapshot.DownloadReady || string.IsNullOrWhiteSpace(snapshot.RepositoryRoot))
                    return BootstrapPreLoginUpdatePlanView.Skip(snapshot.ConfigurationHint);

                if (!snapshot.AutoDownloadEnabled)
                    return BootstrapPreLoginUpdatePlanView.Skip("自动下载已关闭，无法在登录前执行资源更新（请在 Mir2Config.ini 的 [Bootstrap] AutoDownload=True）。");

                // 若已有更新队列（例如上次未完成），优先继续，不覆盖。
                BootstrapPackageUpdateQueueView existingQueue = BootstrapPackageUpdateRuntime.LoadUpdateQueue();
                if (existingQueue.Packages != null && existingQueue.Packages.Count > 0)
                {
                    return new BootstrapPreLoginUpdatePlanView
                    {
                        Skipped = false,
                        RepositoryRoot = snapshot.RepositoryRoot ?? string.Empty,
                        ResourceVersion = existingQueue.ResourceVersion ?? string.Empty,
                        PackagesToUpdate = existingQueue.Packages.Select(item => item?.Name).Where(item => !string.IsNullOrWhiteSpace(item)).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                        Message = "检测到未完成的更新队列，继续执行。",
                    };
                }

                // 1) 读取壳子自带的 baseline 版本索引，用于避免“已随包携带的 core-startup”被重复全量下载。
                TrySeedInstalledVersionsFromBaselineIndex();

                // 2) 下载远端版本索引（微端通过 /api/file/Packages/bootstrap-package-index.json 提供）
                string indexUrl = BuildRemoteIndexUrl(snapshot.RepositoryRoot);
                BootstrapPackageIndexView remoteIndex = await TryDownloadPackageIndexAsync(indexUrl, snapshot.UseMicroAuth, cancellationToken);
                if (remoteIndex == null || remoteIndex.Packages == null || remoteIndex.Packages.Count == 0)
                    return BootstrapPreLoginUpdatePlanView.Skip("远端未提供 bootstrap-package-index.json 或内容为空。");

                // 缓存一份到运行时目录，便于排查（不影响流程）
                TryWriteRemoteIndexCache(remoteIndex);

                var remoteByName = remoteIndex.Packages
                    .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Name))
                    .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);

                HashSet<string> installedPackages = BootstrapPackageUpdateRuntime.GetInstalledPackageNames();
                installedPackages.Add("core-startup");

                var updates = new List<BootstrapPackageUpdateEntryView>();
                foreach (string packageName in installedPackages.OrderBy(item => item, StringComparer.OrdinalIgnoreCase))
                {
                    if (!remoteByName.TryGetValue(packageName, out BootstrapPackageIndexPackageView remote))
                        continue;

                    string remoteSha = (remote.Sha256 ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(remoteSha))
                        continue;

                    string localSha = BootstrapPackageUpdateRuntime.GetInstalledSha256(packageName);

                    // 仅对“已记录版本”的包做差异更新（core-startup 通过 baseline seeding 保证有版本），
                    // 避免老版本用户升级后，由于缺少版本记录导致非必要的全量下载。
                    if (!string.Equals(packageName, "core-startup", StringComparison.OrdinalIgnoreCase) &&
                        string.IsNullOrWhiteSpace(localSha))
                    {
                        continue;
                    }

                    if (string.Equals(localSha, remoteSha, StringComparison.OrdinalIgnoreCase))
                        continue;

                    updates.Add(new BootstrapPackageUpdateEntryView
                    {
                        Name = packageName,
                        DesiredSha256 = remoteSha,
                        Reason = "远端版本变更",
                    });
                }

                AppendPreLoginRequiredPackages(remoteByName, updates);

                if (updates.Count == 0)
                {
                    BootstrapPackageUpdateRuntime.ReplaceUpdateQueue(remoteIndex.ResourceVersion ?? remoteIndex.GeneratedAtUtc ?? string.Empty, Array.Empty<BootstrapPackageUpdateEntryView>());
                    return new BootstrapPreLoginUpdatePlanView
                    {
                        Skipped = false,
                        RepositoryRoot = snapshot.RepositoryRoot ?? string.Empty,
                        ResourceVersion = remoteIndex.ResourceVersion ?? remoteIndex.GeneratedAtUtc ?? string.Empty,
                        PackagesToUpdate = new List<string>(),
                        Message = "资源已是最新，无需更新。",
                    };
                }

                BootstrapPackageUpdateRuntime.ReplaceUpdateQueue(remoteIndex.ResourceVersion ?? remoteIndex.GeneratedAtUtc ?? string.Empty, updates);

                return new BootstrapPreLoginUpdatePlanView
                {
                    Skipped = false,
                    RepositoryRoot = snapshot.RepositoryRoot ?? string.Empty,
                    ResourceVersion = remoteIndex.ResourceVersion ?? remoteIndex.GeneratedAtUtc ?? string.Empty,
                    PackagesToUpdate = updates.Select(item => item.Name).ToList(),
                    Message = $"将更新/安装 {updates.Count} 个资源包。",
                };
            }
            catch (Exception ex)
            {
                if (Settings.LogErrors)
                    CMain.SaveError($"PreLoginUpdate 规划失败：{ex}");

                return BootstrapPreLoginUpdatePlanView.Fail(ex.Message);
            }
        }

        private static void AppendPreLoginRequiredPackages(
            IReadOnlyDictionary<string, BootstrapPackageIndexPackageView> remoteByName,
            ICollection<BootstrapPackageUpdateEntryView> updates)
        {
            try
            {
                if (!ShouldEnsureMobilePackagesAtPreLogin())
                    return;

                if (remoteByName == null || remoteByName.Count == 0)
                    return;

                if (updates == null)
                    return;

                // 移动端必需包：在“无随包携带 UI”的策略下，首次启动必须确保 UI 包可下载并安装。
                // 注意：Windows 的 SmokeTest 通过 Settings.UIProfileId=Mobile 来模拟移动端行为。
                string[] requiredPackages = { "fui-retro" };

                var existing = new HashSet<string>(
                    updates.Where(item => item != null && !string.IsNullOrWhiteSpace(item.Name)).Select(item => item.Name),
                    StringComparer.OrdinalIgnoreCase);

                for (int i = 0; i < requiredPackages.Length; i++)
                {
                    string packageName = (requiredPackages[i] ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(packageName))
                        continue;

                    if (existing.Contains(packageName))
                        continue;

                    if (!remoteByName.TryGetValue(packageName, out BootstrapPackageIndexPackageView remote))
                        continue;

                    string remoteSha = (remote?.Sha256 ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(remoteSha))
                        continue;

                    string localSha = BootstrapPackageUpdateRuntime.GetInstalledSha256(packageName);

                    // 若本地无“已安装版本记录”，说明从未安装过该包（或版本记录丢失），需要在预登录阶段强制安装。
                    bool needInstall = string.IsNullOrWhiteSpace(localSha);

                    // 若版本记录存在但包目录缺失（例如用户清空缓存），同样需要修复安装。
                    if (!needInstall)
                    {
                        string stagedMarker = Path.Combine(ClientResourceLayout.PackageCacheRoot, packageName, "Assets", "UI", "复古", "UI_fui.bytes");
                        if (!File.Exists(stagedMarker))
                        {
                            needInstall = true;
                        }
                    }

                    if (!needInstall)
                        continue;

                    updates.Add(new BootstrapPackageUpdateEntryView
                    {
                        Name = packageName,
                        DesiredSha256 = remoteSha,
                        Reason = string.IsNullOrWhiteSpace(localSha) ? "首次安装（移动端必需）" : "修复安装（本地缓存缺失）",
                    });
                }
            }
            catch (Exception)
            {
            }
        }

        private static bool ShouldEnsureMobilePackagesAtPreLogin()
        {
            // Android/iOS：一定要确保移动端必需包（UI）存在。
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
                return true;

            // Windows：允许通过 UIProfileId=Mobile 来模拟移动端（用于工具链与验收）。
            return string.Equals(Settings.UIProfileId, "Mobile", StringComparison.OrdinalIgnoreCase);
        }

        private static void TrySeedInstalledVersionsFromBaselineIndex()
        {
            try
            {
                string currentCore = BootstrapPackageUpdateRuntime.GetInstalledSha256("core-startup");
                if (!string.IsNullOrWhiteSpace(currentCore))
                    return;

                string json = string.Empty;

                // 优先从 TitleContainer 读取（Android/iOS 的 BootstrapAssets 通常打进包内，不一定存在物理文件）。
                try
                {
                    using Stream stream = TitleContainer.OpenStream("BootstrapAssets/" + BootstrapPackageUpdateRuntime.PackageIndexFileName);
                    using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                    json = reader.ReadToEnd() ?? string.Empty;
                }
                catch (Exception)
                {
                }

                // 兼容 Windows/桌面：若目录里存在物理文件，则可直接读文件。
                if (string.IsNullOrWhiteSpace(json))
                {
                    string baselinePath = Path.Combine(ClientResourceLayout.BootstrapAssetRoot, BootstrapPackageUpdateRuntime.PackageIndexFileName);
                    if (!File.Exists(baselinePath))
                        return;

                    json = File.ReadAllText(baselinePath) ?? string.Empty;
                }

                BootstrapPackageIndexView baseline = JsonSerializer.Deserialize<BootstrapPackageIndexView>(
                                                         (json ?? string.Empty).TrimStart('\uFEFF'),
                                                         JsonReadOptions)
                                                     ?? new BootstrapPackageIndexView();

                BootstrapPackageIndexPackageView core = (baseline.Packages ?? new List<BootstrapPackageIndexPackageView>())
                    .LastOrDefault(item => string.Equals(item?.Name, "core-startup", StringComparison.OrdinalIgnoreCase));

                string sha = (core?.Sha256 ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(sha))
                    return;

                BootstrapPackageUpdateRuntime.UpsertInstalledVersion("core-startup", sha, source: "bootstrap-assets");
            }
            catch (Exception)
            {
            }
        }

        private static string BuildRemoteIndexUrl(string repositoryRoot)
        {
            string normalized = (repositoryRoot ?? string.Empty).Trim().Replace('\\', '/');
            if (!normalized.EndsWith("/", StringComparison.Ordinal))
                normalized += "/";

            return normalized + "Packages/" + BootstrapPackageUpdateRuntime.PackageIndexFileName;
        }

        private static void ApplyMicroAuthHeaders(HttpRequestMessage request, bool useMicroAuth)
        {
            if (!useMicroAuth || request == null)
                return;

            string user = (Settings.MicroUser ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(user))
                request.Headers.TryAddWithoutValidation("User", user);

            string code = (Settings.MicroCode ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(code))
                request.Headers.TryAddWithoutValidation("Code", code);
        }

        private static HttpClient GetHttpClient()
        {
            if (_httpClient != null)
                return _httpClient;

            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
            return _httpClient;
        }

        private static async Task<BootstrapPackageIndexView> TryDownloadPackageIndexAsync(string indexUrl, bool useMicroAuth, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(indexUrl))
                return null;

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, indexUrl);
                ApplyMicroAuthHeaders(request, useMicroAuth);
                using HttpResponseMessage response = await GetHttpClient().SendAsync(request, cancellationToken);
                if (response.StatusCode == HttpStatusCode.NotFound)
                    return null;

                response.EnsureSuccessStatusCode();
                string json = await response.Content.ReadAsStringAsync(cancellationToken);
                if (string.IsNullOrWhiteSpace(json))
                    return null;

                json = json.TrimStart('\uFEFF'); // tolerate UTF-8 BOM
                return JsonSerializer.Deserialize<BootstrapPackageIndexView>(json, JsonReadOptions);
            }
            catch (Exception ex)
            {
                if (Settings.LogErrors)
                    CMain.SaveError($"PreLoginUpdate 下载版本索引失败：Url={indexUrl} Error={ex.Message}");
                return null;
            }
        }

        private static void TryWriteRemoteIndexCache(BootstrapPackageIndexView index)
        {
            try
            {
                Directory.CreateDirectory(ClientResourceLayout.RuntimeRoot);
                string path = Path.Combine(ClientResourceLayout.RuntimeRoot, "BootstrapRemotePackageIndex.json");
                string json = JsonSerializer.Serialize(index ?? new BootstrapPackageIndexView(), JsonWriteOptions);
                WriteTextFileAtomic(path, json ?? string.Empty);
            }
            catch (Exception)
            {
            }
        }

        private static void WriteTextFileAtomic(string outputPath, string content)
        {
            if (string.IsNullOrWhiteSpace(outputPath))
                return;

            string directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            string tempPath = outputPath + ".tmp";
            File.WriteAllText(tempPath, content ?? string.Empty, Utf8NoBom);
            File.Move(tempPath, outputPath, overwrite: true);
        }
    }

    internal sealed class BootstrapPackageIndexView
    {
        public string GeneratedAtUtc { get; set; }
        public string ResourceVersion { get; set; }
        public List<BootstrapPackageIndexPackageView> Packages { get; set; } = new List<BootstrapPackageIndexPackageView>();
    }

    internal sealed class BootstrapPackageIndexPackageView
    {
        public string Name { get; set; }
        public string Sha256 { get; set; }
        public long Size { get; set; }
    }

    internal sealed class BootstrapPreLoginUpdatePlanView
    {
        public bool Skipped { get; set; }
        public bool Failed { get; set; }
        public string RepositoryRoot { get; set; }
        public string ResourceVersion { get; set; }
        public List<string> PackagesToUpdate { get; set; } = new List<string>();
        public string Message { get; set; }

        public static BootstrapPreLoginUpdatePlanView Skip(string message)
        {
            return new BootstrapPreLoginUpdatePlanView
            {
                Skipped = true,
                Failed = false,
                Message = string.IsNullOrWhiteSpace(message) ? "已跳过资源更新。" : message,
            };
        }

        public static BootstrapPreLoginUpdatePlanView Fail(string message)
        {
            return new BootstrapPreLoginUpdatePlanView
            {
                Skipped = false,
                Failed = true,
                Message = string.IsNullOrWhiteSpace(message) ? "资源更新失败。" : message,
            };
        }
    }
}
