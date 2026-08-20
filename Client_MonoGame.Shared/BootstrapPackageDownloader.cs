using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace MonoShare
{
    internal static class BootstrapPackageDownloader
    {
        private static readonly object Gate = new object();
        private static readonly object LogGate = new object();
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        private static readonly JsonSerializerOptions JsonWriteOptions = new JsonSerializerOptions { WriteIndented = true };

        private static DateTime _nextTickUtc = DateTime.MinValue;
        private static Task _activeTask;
        private static CancellationTokenSource _activeCts;

        private static string _activePackageName;
        private static string _activeStage;
        private static long _activeBytesReceived;
        private static long _activeTotalBytes;
        private static string _lastError;

        private static HttpClient _httpClient;

        private static readonly Dictionary<string, PackageDownloadRecord> Records =
            new Dictionary<string, PackageDownloadRecord>(StringComparer.OrdinalIgnoreCase);

        private const long LogRotateBytes = 2 * 1024 * 1024;
        private const int LogKeepCount = 3;

        public static void TryDownloadPendingPackagesIfDue()
        {
            if (DateTime.UtcNow < _nextTickUtc)
                return;

            _nextTickUtc = DateTime.UtcNow.AddSeconds(1);

            string repositoryRoot = ResolveRepositoryRoot(out bool useMicroAuth);
            bool autoDownloadEnabled = Settings.BootstrapAutoDownloadPackages;

            string[] pendingPackageNames = BootstrapPackageRuntime.GetPendingPackageNames();
            HashSet<string> updatePackageNames = BootstrapPackageUpdateRuntime.GetUpdatePackageNames();
            BootstrapPackageStateSnapshotView stateSnapshot = BootstrapPackageRuntime.LoadStateSnapshot();
            var statusByName = stateSnapshot.Packages
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Name))
                .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);

            lock (Gate)
            {
                EnsureRecordCoverage(pendingPackageNames);

                if (_activeTask != null && _activeTask.IsCompleted)
                {
                    try
                    {
                        _activeTask.GetAwaiter().GetResult();
                    }
                    catch (Exception ex)
                    {
                        _lastError = ex.Message;
                    }

                    _activeTask = null;
                    _activeCts?.Dispose();
                    _activeCts = null;
                    _activePackageName = null;
                    _activeStage = null;
                    _activeBytesReceived = 0;
                    _activeTotalBytes = 0;
                }

                if (_activeTask == null &&
                    autoDownloadEnabled &&
                    !string.IsNullOrWhiteSpace(repositoryRoot))
                {
                    string nextPackage = SelectNextPackage(pendingPackageNames, updatePackageNames, statusByName);
                    if (!string.IsNullOrWhiteSpace(nextPackage))
                    {
                        _activePackageName = nextPackage;
                        _activeStage = "starting";
                        _activeBytesReceived = 0;
                        _activeTotalBytes = 0;
                        _activeCts = new CancellationTokenSource();
                        _activeTask = Task.Run(() => DownloadSinglePackageAsync(nextPackage, repositoryRoot, useMicroAuth, _activeCts.Token));
                    }
                }

                TryWriteStateSnapshotLocked(repositoryRoot, useMicroAuth, autoDownloadEnabled, pendingPackageNames);
            }
        }

        public static BootstrapDownloadStateSnapshot GetStateSnapshot()
        {
            lock (Gate)
            {
                string repositoryRoot = ResolveRepositoryRoot(out bool useMicroAuth);
                return BuildSnapshotLocked(
                    repositoryRoot,
                    useMicroAuth,
                    Settings.BootstrapAutoDownloadPackages,
                    BootstrapPackageRuntime.GetPendingPackageNames());
            }
        }

        private static void EnsureRecordCoverage(IEnumerable<string> pendingPackageNames)
        {
            foreach (string name in pendingPackageNames ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                if (!Records.ContainsKey(name))
                {
                    Records[name] = new PackageDownloadRecord
                    {
                        Name = name,
                        Status = "pending",
                        AttemptCount = 0,
                        LastUpdatedUtc = DateTime.UtcNow,
                        NextAttemptUtc = DateTime.MinValue,
                    };
                }
            }
        }

        private static string SelectNextPackage(
            IReadOnlyCollection<string> pendingPackageNames,
            IReadOnlySet<string> updatePackageNames,
            IReadOnlyDictionary<string, BootstrapPackageStateView> statusByName)
        {
            if (pendingPackageNames == null || pendingPackageNames.Count == 0)
                return null;

            foreach (string name in pendingPackageNames)
            {
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                bool isUpdate = updatePackageNames != null && updatePackageNames.Contains(name);
                if (!isUpdate)
                {
                    if (statusByName != null &&
                        statusByName.TryGetValue(name, out BootstrapPackageStateView pack) &&
                        !string.Equals(pack?.Status, "declared", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                }

                if (Records.TryGetValue(name, out PackageDownloadRecord record))
                {
                    if (string.Equals(record.Status, "downloading", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(record.Status, "extracting", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(record.Status, "verifying", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(record.Status, "inbox", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (record.NextAttemptUtc > DateTime.UtcNow)
                        continue;
                }

                return name;
            }

            return null;
        }

        private static async Task DownloadSinglePackageAsync(string packageName, string repositoryRoot, bool useMicroAuth, CancellationToken cancellationToken)
        {
            string normalizedRepository = NormalizeRepositoryRoot(repositoryRoot);
            string zipUrl = $"{normalizedRepository}Packages/{Uri.EscapeDataString(packageName)}.zip";
            string shaUrl = $"{zipUrl}.sha256";

            try
            {
                ClientResourceLayout.EnsureWritableResourceDirectories();

                UpdatePackageStatus(packageName, "downloading", stage: "sha256", errorMessage: null);
                string expectedSha256 = Settings.BootstrapVerifyDownloadedPackages
                    ? await TryDownloadSha256Async(shaUrl, useMicroAuth, cancellationToken)
                    : null;

                string zipPath = Path.Combine(ClientResourceLayout.DownloadPackageRoot, $"{MakeSafeFileName(packageName)}.zip");
                UpdatePackageStatus(packageName, "downloading", stage: "download", errorMessage: null, zipUrl: zipUrl, sha256: expectedSha256, localZipPath: zipPath);

                await DownloadFileWithResumeAsync(zipUrl, zipPath, useMicroAuth, cancellationToken);

                if (!string.IsNullOrWhiteSpace(expectedSha256) && Settings.BootstrapVerifyDownloadedPackages)
                {
                    UpdatePackageStatus(packageName, "verifying", stage: "sha256-check", errorMessage: null);
                    VerifySha256(zipPath, expectedSha256);
                }

                UpdatePackageStatus(packageName, "extracting", stage: "unzip", errorMessage: null);
                string stagingDirectory = CreateBundleStagingDirectory(packageName);
                ExtractZipSafely(zipPath, stagingDirectory);

                if (!LooksLikeBundleDirectory(stagingDirectory, packageName))
                    throw new InvalidOperationException("资源包解压完成，但未找到可识别的分包目录（期望 Packages/<pack> 或 <pack>）。");

                TryWriteBundleMeta(stagingDirectory, new BundleDownloadMeta
                {
                    PackageName = packageName,
                    ZipUrl = zipUrl,
                    Sha256 = expectedSha256,
                    DownloadedAtUtc = DateTime.UtcNow.ToString("o"),
                    RepositoryRoot = normalizedRepository,
                });

                TryDeleteFile(zipPath);

                string bundleDirectory = PromoteBundleStagingDirectory(stagingDirectory);

                UpdatePackageStatus(packageName, "inbox", stage: "ready", errorMessage: null, bundleDirectory: bundleDirectory);
                TryAppendLog($"OK | Pack={packageName} | Bundle={bundleDirectory}");
            }
            catch (Exception ex)
            {
                string message = ex.Message;
                lock (Gate)
                {
                    _lastError = message;
                    if (Records.TryGetValue(packageName, out PackageDownloadRecord record))
                    {
                        record.Status = "failed";
                        record.ErrorMessage = message;
                        record.AttemptCount = Math.Max(0, record.AttemptCount) + 1;
                        record.LastUpdatedUtc = DateTime.UtcNow;

                        int maxRetries = Math.Max(0, Settings.BootstrapDownloadRetryCount);
                        if (maxRetries > 0 && record.AttemptCount <= maxRetries)
                        {
                            int backoffSeconds = Math.Clamp(2 + record.AttemptCount * 2, 2, 60);
                            record.NextAttemptUtc = DateTime.UtcNow.AddSeconds(backoffSeconds);
                        }
                    }
                }

                TryAppendLog($"FAIL | Pack={packageName} | Message={message}");
                if (Settings.LogErrors)
                    CMain.SaveError($"Bootstrap: Pack={packageName} 下载失败：{ex}");
            }
            finally
            {
                lock (Gate)
                {
                    _activeStage = null;
                    _activeBytesReceived = 0;
                    _activeTotalBytes = 0;
                }
            }
        }

        private static string NormalizeRepositoryRoot(string repositoryRoot)
        {
            string normalized = (repositoryRoot ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalized))
                return string.Empty;

            normalized = normalized.Replace('\\', '/');
            if (!normalized.EndsWith("/", StringComparison.Ordinal))
                normalized += "/";

            return normalized;
        }

        private static string ResolveRepositoryRoot(out bool useMicroAuth)
        {
            useMicroAuth = false;

            string repositoryRoot = (Settings.BootstrapPackageRepo ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(repositoryRoot))
                return repositoryRoot;

            string microBaseUrl = (Settings.MicroBaseUrl ?? string.Empty).Trim();
            string microUser = (Settings.MicroUser ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(microBaseUrl) || string.IsNullOrWhiteSpace(microUser))
                return string.Empty;

            useMicroAuth = true;

            string normalizedMicroBase = NormalizeRepositoryRoot(microBaseUrl);
            if (normalizedMicroBase.EndsWith("/file/", StringComparison.OrdinalIgnoreCase))
                return normalizedMicroBase;

            return normalizedMicroBase + "file/";
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

        private static string MakeSafeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "unknown";

            var builder = new StringBuilder(name.Length);
            foreach (char c in name)
            {
                if (c == '/' || c == '\\' || c == ':' || c == '*' || c == '?' || c == '"' || c == '<' || c == '>' || c == '|')
                    builder.Append('_');
                else
                    builder.Append(c);
            }

            string result = builder.ToString().Trim();
            return string.IsNullOrWhiteSpace(result) ? "unknown" : result;
        }

        private static async Task<string> TryDownloadSha256Async(string shaUrl, bool useMicroAuth, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(shaUrl))
                return null;

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, shaUrl);
                ApplyMicroAuthHeaders(request, useMicroAuth);
                using HttpResponseMessage response = await GetHttpClient().SendAsync(request, cancellationToken);
                if (response.StatusCode == HttpStatusCode.NotFound)
                    return null;

                response.EnsureSuccessStatusCode();
                string content = (await response.Content.ReadAsStringAsync(cancellationToken) ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(content))
                    return null;

                content = content.TrimStart('\uFEFF'); // tolerate UTF-8 BOM

                Match match = Regex.Match(content, "[0-9a-fA-F]{64}");
                if (!match.Success)
                    return null;

                return match.Value.ToLowerInvariant();
            }
            catch (Exception ex)
            {
                if (Settings.LogErrors)
                    CMain.SaveError($"Bootstrap: 下载 SHA256 失败 | Url={shaUrl} | {ex.Message}");
                return null;
            }
        }

        private static async Task DownloadFileWithResumeAsync(string url, string outputPath, bool useMicroAuth, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(url))
                throw new ArgumentException("下载地址为空。", nameof(url));

            if (string.IsNullOrWhiteSpace(outputPath))
                throw new ArgumentException("输出路径为空。", nameof(outputPath));

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ClientResourceLayout.DownloadPackageRoot);

            string tempPath = outputPath + ".part";
            long existingBytes = File.Exists(tempPath) ? new FileInfo(tempPath).Length : 0;

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (existingBytes > 0)
            {
                request.Headers.Range = new RangeHeaderValue(existingBytes, null);
            }

            ApplyMicroAuthHeaders(request, useMicroAuth);

            using HttpResponseMessage response = await GetHttpClient()
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
            {
                if (File.Exists(tempPath) && existingBytes > 0)
                {
                    File.Move(tempPath, outputPath, overwrite: true);
                    return;
                }

                existingBytes = 0;
            }

            if (response.StatusCode == HttpStatusCode.OK && existingBytes > 0)
            {
                existingBytes = 0;
                TryDeleteFile(tempPath);
            }

            response.EnsureSuccessStatusCode();

            long totalBytes = 0;
            if (response.Content.Headers.ContentRange?.Length != null)
                totalBytes = response.Content.Headers.ContentRange.Length.Value;
            else if (response.Content.Headers.ContentLength != null && existingBytes == 0)
                totalBytes = response.Content.Headers.ContentLength.Value;

            lock (Gate)
            {
                _activeStage = "download";
                _activeBytesReceived = existingBytes;
                _activeTotalBytes = totalBytes;
            }

            using Stream contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var fileStream = new FileStream(
                tempPath,
                existingBytes > 0 ? FileMode.Append : FileMode.Create,
                FileAccess.Write,
                FileShare.Read,
                bufferSize: 1024 * 64,
                useAsync: true);

            var buffer = new byte[1024 * 64];
            int read;
            long written = existingBytes;
            while ((read = await contentStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                written += read;

                lock (Gate)
                {
                    _activeBytesReceived = written;
                    if (_activeTotalBytes <= 0 && totalBytes > 0)
                        _activeTotalBytes = totalBytes;
                }
            }

            await fileStream.FlushAsync(cancellationToken);

            File.Move(tempPath, outputPath, overwrite: true);
        }

        private static void VerifySha256(string filePath, string expectedSha256)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("下载文件不存在，无法校验。", filePath);

            string expected = (expectedSha256 ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(expected))
                return;

            expected = expected.Replace(" ", string.Empty).Replace("\t", string.Empty).Trim();
            expected = expected.Length > 64 ? expected.Substring(0, 64) : expected;

            using var stream = File.OpenRead(filePath);
            using var sha = SHA256.Create();
            byte[] hashBytes = sha.ComputeHash(stream);
            string actual = Convert.ToHexString(hashBytes);

            if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"SHA256 校验失败：expected={expected} actual={actual}");
        }

        private static string CreateBundleStagingDirectory(string packageName)
        {
            string stagingRoot = Path.Combine(ClientResourceLayout.DownloadRoot, "BundleStaging");
            Directory.CreateDirectory(stagingRoot);

            string timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            string safeName = MakeSafeFileName(packageName);
            string bundleName = $"{timestamp}_{safeName}";

            string target = Path.Combine(stagingRoot, bundleName);
            int attempt = 0;
            while (Directory.Exists(target))
            {
                attempt++;
                target = Path.Combine(stagingRoot, $"{bundleName}_{attempt}");
                if (attempt >= 20)
                    throw new IOException("无法创建 BundleInbox 目录（重试次数过多）。");
            }

            Directory.CreateDirectory(target);
            return target;
        }

        private static string PromoteBundleStagingDirectory(string stagingDirectory)
        {
            if (string.IsNullOrWhiteSpace(stagingDirectory) || !Directory.Exists(stagingDirectory))
                throw new DirectoryNotFoundException("Bundle staging 目录不存在，无法投递到 BundleInbox。");

            Directory.CreateDirectory(ClientResourceLayout.BundleInboxRoot);

            string name = Path.GetFileName(stagingDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrWhiteSpace(name))
                throw new InvalidOperationException("Bundle staging 目录名为空，无法投递。");

            string targetDirectory = Path.Combine(ClientResourceLayout.BundleInboxRoot, name);
            int attempt = 0;
            while (Directory.Exists(targetDirectory))
            {
                attempt++;
                targetDirectory = Path.Combine(ClientResourceLayout.BundleInboxRoot, $"{name}_{attempt}");
                if (attempt >= 20)
                    throw new IOException("无法投递到 BundleInbox（目标目录冲突次数过多）。");
            }

            Directory.Move(stagingDirectory, targetDirectory);
            return targetDirectory;
        }

        private static void ExtractZipSafely(string zipPath, string targetDirectory)
        {
            if (string.IsNullOrWhiteSpace(zipPath) || !File.Exists(zipPath))
                throw new FileNotFoundException("Zip 文件不存在，无法解压。", zipPath);

            if (string.IsNullOrWhiteSpace(targetDirectory))
                throw new ArgumentException("目标目录为空。", nameof(targetDirectory));

            Directory.CreateDirectory(targetDirectory);

            string normalizedTarget = Path.GetFullPath(targetDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;

            using ZipArchive archive = ZipFile.OpenRead(zipPath);
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                if (string.IsNullOrWhiteSpace(entry.FullName))
                    continue;

                string relative = entry.FullName
                    .Replace('/', Path.DirectorySeparatorChar)
                    .Replace('\\', Path.DirectorySeparatorChar);
                if (relative.EndsWith(Path.DirectorySeparatorChar))
                    continue;

                string destination = Path.GetFullPath(Path.Combine(targetDirectory, relative));
                if (!destination.StartsWith(normalizedTarget, StringComparison.OrdinalIgnoreCase))
                    continue;

                string directory = Path.GetDirectoryName(destination);
                if (!string.IsNullOrWhiteSpace(directory))
                    Directory.CreateDirectory(directory);

                entry.ExtractToFile(destination, overwrite: true);
            }
        }

        private static bool LooksLikeBundleDirectory(string bundleDirectory, string packageName)
        {
            if (string.IsNullOrWhiteSpace(bundleDirectory) || !Directory.Exists(bundleDirectory))
                return false;

            string safePackage = string.IsNullOrWhiteSpace(packageName) ? string.Empty : packageName.Trim();
            if (string.IsNullOrWhiteSpace(safePackage))
                return true;

            return Directory.Exists(Path.Combine(bundleDirectory, "Packages", safePackage))
                   || Directory.Exists(Path.Combine(bundleDirectory, safePackage));
        }

        private static void UpdatePackageStatus(
            string packageName,
            string status,
            string stage,
            string errorMessage,
            string zipUrl = null,
            string sha256 = null,
            string localZipPath = null,
            string bundleDirectory = null)
        {
            lock (Gate)
            {
                _activeStage = stage;

                if (Records.TryGetValue(packageName, out PackageDownloadRecord record))
                {
                    record.Status = status ?? record.Status;
                    record.ErrorMessage = errorMessage;
                    record.ZipUrl = string.IsNullOrWhiteSpace(zipUrl) ? record.ZipUrl : zipUrl;
                    record.ExpectedSha256 = string.IsNullOrWhiteSpace(sha256) ? record.ExpectedSha256 : sha256;
                    record.LocalZipPath = string.IsNullOrWhiteSpace(localZipPath) ? record.LocalZipPath : localZipPath;
                    record.BundleDirectory = string.IsNullOrWhiteSpace(bundleDirectory) ? record.BundleDirectory : bundleDirectory;
                    record.LastUpdatedUtc = DateTime.UtcNow;
                }
                else
                {
                    Records[packageName] = new PackageDownloadRecord
                    {
                        Name = packageName,
                        Status = status,
                        AttemptCount = 0,
                        ErrorMessage = errorMessage,
                        ZipUrl = zipUrl,
                        ExpectedSha256 = sha256,
                        LocalZipPath = localZipPath,
                        BundleDirectory = bundleDirectory,
                        LastUpdatedUtc = DateTime.UtcNow,
                        NextAttemptUtc = DateTime.MinValue,
                    };
                }
            }
        }

        private static HttpClient GetHttpClient()
        {
            if (_httpClient != null)
                return _httpClient;

            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromMinutes(15);
            return _httpClient;
        }

        private static void TryWriteStateSnapshotLocked(string repositoryRoot, bool useMicroAuth, bool autoDownloadEnabled, IReadOnlyCollection<string> pendingPackageNames)
        {
            try
            {
                Directory.CreateDirectory(ClientResourceLayout.RuntimeRoot);
                BootstrapDownloadStateSnapshot snapshot = BuildSnapshotLocked(repositoryRoot, useMicroAuth, autoDownloadEnabled, pendingPackageNames);
                WriteJsonFileAtomic(ClientResourceLayout.DownloadStateSnapshotPath, snapshot);
            }
            catch (Exception)
            {
            }
        }

        private static BootstrapDownloadStateSnapshot BuildSnapshotLocked(
            string repositoryRoot,
            bool useMicroAuth,
            bool autoDownloadEnabled,
            IReadOnlyCollection<string> pendingPackageNames)
        {
            string normalizedRepositoryRoot = NormalizeRepositoryRoot(repositoryRoot);
            string normalizedPackageRepo = NormalizeRepositoryRoot(Settings.BootstrapPackageRepo);
            string normalizedMicroRepositoryRoot = ResolveMicroRepositoryRoot();

            var snapshot = new BootstrapDownloadStateSnapshot
            {
                GeneratedAtUtc = DateTime.UtcNow.ToString("o"),
                AutoDownloadEnabled = autoDownloadEnabled,
                DownloadReady = !string.IsNullOrWhiteSpace(normalizedRepositoryRoot),
                UseMicroAuth = useMicroAuth,
                RepositoryRoot = normalizedRepositoryRoot,
                RepositorySource = ResolveRepositorySource(normalizedRepositoryRoot, normalizedPackageRepo, normalizedMicroRepositoryRoot, useMicroAuth),
                ConfigurationHint = BuildConfigurationHint(normalizedRepositoryRoot, normalizedPackageRepo, normalizedMicroRepositoryRoot, useMicroAuth, autoDownloadEnabled),
                ActivePackageName = _activePackageName ?? string.Empty,
                ActiveStage = _activeStage ?? string.Empty,
                ActiveBytesReceived = _activeBytesReceived,
                ActiveTotalBytes = _activeTotalBytes,
                LastError = _lastError ?? string.Empty,
                PendingPackageCount = pendingPackageNames?.Count ?? 0,
                PendingPackagesSample = (pendingPackageNames ?? Array.Empty<string>()).Take(8).ToList(),
            };

            foreach (PackageDownloadRecord record in Records.Values.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
            {
                snapshot.Packages.Add(new BootstrapDownloadPackageState
                {
                    Name = record.Name,
                    Status = record.Status,
                    AttemptCount = record.AttemptCount,
                    LastUpdatedAtUtc = record.LastUpdatedUtc.ToString("o"),
                    NextAttemptAtUtc = record.NextAttemptUtc == DateTime.MinValue ? string.Empty : record.NextAttemptUtc.ToString("o"),
                    ZipUrl = record.ZipUrl,
                    ExpectedSha256 = record.ExpectedSha256,
                    LocalZipPath = record.LocalZipPath,
                    BundleDirectory = record.BundleDirectory,
                    ErrorMessage = record.ErrorMessage,
                });
            }

            return snapshot;
        }

        private static string ResolveMicroRepositoryRoot()
        {
            string microBaseUrl = (Settings.MicroBaseUrl ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(microBaseUrl))
                return string.Empty;

            string normalized = NormalizeRepositoryRoot(microBaseUrl);
            if (normalized.EndsWith("/file/", StringComparison.OrdinalIgnoreCase))
                return normalized;

            return normalized + "file/";
        }

        private static string ResolveRepositorySource(
            string repositoryRoot,
            string configuredPackageRepo,
            string configuredMicroRepositoryRoot,
            bool useMicroAuth)
        {
            if (string.IsNullOrWhiteSpace(repositoryRoot))
                return "未配置";

            if (!string.IsNullOrWhiteSpace(configuredPackageRepo) &&
                string.Equals(repositoryRoot, configuredPackageRepo, StringComparison.OrdinalIgnoreCase))
            {
                return "PackageRepo";
            }

            if (useMicroAuth ||
                (!string.IsNullOrWhiteSpace(configuredMicroRepositoryRoot) &&
                 string.Equals(repositoryRoot, configuredMicroRepositoryRoot, StringComparison.OrdinalIgnoreCase)))
            {
                return "MicroBaseUrl/file";
            }

            return "自定义";
        }

        private static string BuildConfigurationHint(
            string repositoryRoot,
            string configuredPackageRepo,
            string configuredMicroRepositoryRoot,
            bool useMicroAuth,
            bool autoDownloadEnabled)
        {
            if (!autoDownloadEnabled)
                return "自动下载已关闭；如需后台拉取分包，请在 [Bootstrap] AutoDownload=True。";

            if (!string.IsNullOrWhiteSpace(repositoryRoot))
            {
                if (useMicroAuth)
                    return "当前通过 [Micro] BaseUrl + User/Code 访问 /api/file，并支持 HTTP Range 断点续传。";

                if (!string.IsNullOrWhiteSpace(configuredPackageRepo) &&
                    string.Equals(repositoryRoot, configuredPackageRepo, StringComparison.OrdinalIgnoreCase))
                {
                    return "当前通过 [Bootstrap] PackageRepo 访问静态 Zip 分包仓库。";
                }

                return "当前通过已解析的下载源访问分包仓库。";
            }

            bool hasPackageRepo = !string.IsNullOrWhiteSpace(configuredPackageRepo);
            bool hasMicroBase = !string.IsNullOrWhiteSpace((Settings.MicroBaseUrl ?? string.Empty).Trim());
            bool hasMicroUser = !string.IsNullOrWhiteSpace((Settings.MicroUser ?? string.Empty).Trim());

            if (!hasPackageRepo && !hasMicroBase)
                return "未配置下载源；请设置 [Bootstrap] PackageRepo，或配置 [Micro] BaseUrl + User。";

            if (!hasPackageRepo && hasMicroBase && !hasMicroUser)
                return "已配置 [Micro] BaseUrl，但缺少 [Micro] User，无法回退到 /api/file。";

            if (!string.IsNullOrWhiteSpace(configuredMicroRepositoryRoot) && !hasMicroUser)
                return "检测到微端下载地址，但缺少 [Micro] User，当前无法携带鉴权头。";

            return "下载源配置不完整，请检查 Mir2Config.ini 中的 [Bootstrap]/[Micro] 段。";
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

        private static void TryWriteBundleMeta(string bundleDirectory, BundleDownloadMeta meta)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(bundleDirectory) || !Directory.Exists(bundleDirectory))
                    return;

                string path = Path.Combine(bundleDirectory, "bundle-download-meta.json");
                WriteJsonFileAtomic(path, meta ?? new BundleDownloadMeta());
            }
            catch (Exception)
            {
            }
        }

        private static void TryAppendLog(string message)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(message))
                    return;

                Directory.CreateDirectory(ClientResourceLayout.RuntimeRoot);

                lock (LogGate)
                {
                    TryRotateLogFile(ClientResourceLayout.DownloaderLogPath, LogRotateBytes, LogKeepCount);
                    using var writer = new StreamWriter(ClientResourceLayout.DownloaderLogPath, append: true, Utf8NoBom);
                    writer.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} | {message}");
                }
            }
            catch (Exception)
            {
            }
        }

        private static void TryRotateLogFile(string filePath, long maxBytes, int keepCount)
        {
            if (string.IsNullOrWhiteSpace(filePath) || maxBytes <= 0 || keepCount <= 0)
                return;

            try
            {
                var info = new FileInfo(filePath);
                if (!info.Exists || info.Length <= maxBytes)
                    return;

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

        private sealed class PackageDownloadRecord
        {
            public string Name { get; set; }
            public string Status { get; set; }
            public int AttemptCount { get; set; }
            public DateTime LastUpdatedUtc { get; set; }
            public DateTime NextAttemptUtc { get; set; }
            public string ZipUrl { get; set; }
            public string ExpectedSha256 { get; set; }
            public string LocalZipPath { get; set; }
            public string BundleDirectory { get; set; }
            public string ErrorMessage { get; set; }
        }

        internal sealed class BootstrapDownloadStateSnapshot
        {
            public string GeneratedAtUtc { get; set; }
            public bool AutoDownloadEnabled { get; set; }
            public bool DownloadReady { get; set; }
            public bool UseMicroAuth { get; set; }
            public string RepositoryRoot { get; set; }
            public string RepositorySource { get; set; }
            public string ConfigurationHint { get; set; }
            public string ActivePackageName { get; set; }
            public string ActiveStage { get; set; }
            public long ActiveBytesReceived { get; set; }
            public long ActiveTotalBytes { get; set; }
            public string LastError { get; set; }
            public int PendingPackageCount { get; set; }
            public List<string> PendingPackagesSample { get; set; } = new List<string>();
            public List<BootstrapDownloadPackageState> Packages { get; set; } = new List<BootstrapDownloadPackageState>();
        }

        internal sealed class BootstrapDownloadPackageState
        {
            public string Name { get; set; }
            public string Status { get; set; }
            public int AttemptCount { get; set; }
            public string LastUpdatedAtUtc { get; set; }
            public string NextAttemptAtUtc { get; set; }
            public string ZipUrl { get; set; }
            public string ExpectedSha256 { get; set; }
            public string LocalZipPath { get; set; }
            public string BundleDirectory { get; set; }
            public string ErrorMessage { get; set; }
        }

        private sealed class BundleDownloadMeta
        {
            public string DownloadedAtUtc { get; set; }
            public string RepositoryRoot { get; set; }
            public string PackageName { get; set; }
            public string ZipUrl { get; set; }
            public string Sha256 { get; set; }
        }
    }
}
