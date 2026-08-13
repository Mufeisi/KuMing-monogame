using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Shared.Security;

namespace Client.Bootstrap
{
    internal static class PcBootstrapHttp
    {
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        private static readonly JsonSerializerOptions JsonWriteOptions = new JsonSerializerOptions { WriteIndented = true };

        private static HttpClient _httpClient;

        public static string ResolveRepositoryRoot(out bool useMicroAuth)
        {
            IReadOnlyList<PcBootstrapRepositoryCandidate> candidates = ResolveRepositoryCandidates();
            if (candidates.Count == 0)
            {
                useMicroAuth = false;
                return string.Empty;
            }

            useMicroAuth = candidates[0].UseMicroAuth;
            return candidates[0].RepositoryRoot;
        }

        public static IReadOnlyList<PcBootstrapRepositoryCandidate> ResolveRepositoryCandidates()
        {
            string repositoryRoot = (Settings.BootstrapPackageRepo ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(repositoryRoot))
            {
                return new[]
                {
                    new PcBootstrapRepositoryCandidate(NormalizeRepositoryRoot(repositoryRoot), useMicroAuth: false),
                };
            }

            string microUser = (Settings.MicroUser ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(microUser))
                return Array.Empty<PcBootstrapRepositoryCandidate>();

            var candidates = new List<PcBootstrapRepositoryCandidate>();
            AddMicroRepositoryCandidate(candidates, Settings.MicroBaseUrl);
            AddMicroRepositoryCandidate(candidates, Settings.MicroBackupBaseUrl);
            return candidates;
        }

        private static void AddMicroRepositoryCandidate(ICollection<PcBootstrapRepositoryCandidate> candidates, string microBaseUrl)
        {
            string normalizedMicroBase = NormalizeRepositoryRoot((microBaseUrl ?? string.Empty).Trim());
            if (string.IsNullOrWhiteSpace(normalizedMicroBase))
                return;

            string repositoryRoot = normalizedMicroBase.EndsWith("/file/", StringComparison.OrdinalIgnoreCase)
                ? normalizedMicroBase
                : normalizedMicroBase + "file/";
            foreach (PcBootstrapRepositoryCandidate existing in candidates)
            {
                if (string.Equals(existing.RepositoryRoot, repositoryRoot, StringComparison.OrdinalIgnoreCase))
                    return;
            }

            candidates.Add(new PcBootstrapRepositoryCandidate(repositoryRoot, useMicroAuth: true));
        }

        public static string BuildRemoteIndexUrl(string repositoryRoot)
        {
            string normalized = NormalizeRepositoryRoot(repositoryRoot);
            if (string.IsNullOrWhiteSpace(normalized))
                return string.Empty;

            return normalized + "Packages/bootstrap-package-index.json";
        }

        public static string BuildRemotePackageZipUrl(string repositoryRoot, string packageName)
        {
            string normalized = NormalizeRepositoryRoot(repositoryRoot);
            if (string.IsNullOrWhiteSpace(normalized))
                return string.Empty;

            string name = (packageName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(name))
                return string.Empty;

            return normalized + "Packages/" + Uri.EscapeDataString(name) + ".zip";
        }

        public static async Task<PcBootstrapPackageIndexView> TryDownloadPackageIndexAsync(
            string indexUrl,
            bool useMicroAuth,
            CancellationToken cancellationToken)
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
                BootstrapSignedManifest signed = BootstrapManifestAcceptanceStore.VerifyAndAccept(
                    json,
                    PcBootstrapLayout.ManifestSecurityStatePath,
                    PcBootstrapAcceptanceContext.TrustedKeys,
                    currentClientVersion: PcBootstrapLayout.ClientCompatibilityVersion);
                return new PcBootstrapPackageIndexView
                {
                    GeneratedAtUtc = signed.GeneratedAtUtc,
                    ResourceVersion = signed.ResourceVersion,
                    Packages = signed.Packages.ConvertAll(package => new PcBootstrapPackageIndexPackageView
                    {
                        Name = package.Name,
                        Sha256 = package.Sha256,
                        Size = package.Size,
                    }),
                };
            }
            catch (InvalidDataException)
            {
                throw;
            }
            catch (Exception ex)
            {
                PcBootstrapPreLoginUpdateService.TryAppendLog($"FAIL | DownloadIndex | Url={indexUrl} | Error={ex.Message}");
                if (Settings.LogErrors)
                    CMain.SaveError($"PC PreLoginUpdate 下载版本索引失败：Url={indexUrl} Error={ex.Message}");
                return null;
            }
        }

        public static void TryWriteRemoteIndexCache(PcBootstrapPackageIndexView index)
        {
            try
            {
                string json = JsonSerializer.Serialize(index ?? new PcBootstrapPackageIndexView(), JsonWriteOptions);
                WriteTextFileAtomic(PcBootstrapLayout.RemoteIndexCachePath, json ?? string.Empty);
            }
            catch (Exception)
            {
            }
        }

        public static async Task<string> DownloadPackageZipAsync(
            string packageName,
            string zipUrl,
            long expectedSize,
            bool useMicroAuth,
            IProgress<PcBootstrapProgress> progress,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(packageName))
                throw new ArgumentException("packageName 为空。", nameof(packageName));
            if (string.IsNullOrWhiteSpace(zipUrl))
                throw new ArgumentException("zipUrl 为空。", nameof(zipUrl));

            Directory.CreateDirectory(PcBootstrapLayout.DownloadPackagesRoot);
            string safeName = MakeSafeFileName(packageName);
            string localZipPath = Path.Combine(PcBootstrapLayout.DownloadPackagesRoot, safeName + ".zip");

            await DownloadFileWithResumeAsync(
                zipUrl,
                localZipPath,
                expectedSize,
                useMicroAuth,
                progress,
                stage: "download",
                packageName: packageName,
                cancellationToken: cancellationToken);

            return localZipPath;
        }

        public static bool VerifyZipSha256IfEnabled(string packageName, string localZipPath, string expectedSha256)
        {
            if (!Settings.BootstrapVerifySha256)
                return true;

            string expected = (expectedSha256 ?? string.Empty).Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(expected))
                return true;

            string actual = ComputeSha256LowerHex(localZipPath);
            if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            {
                PcBootstrapPreLoginUpdateService.TryAppendLog($"FAIL | VerifySha256 | Pack={packageName} | Expected={expected} | Actual={actual}");
                return false;
            }

            PcBootstrapPreLoginUpdateService.TryAppendLog($"OK | VerifySha256 | Pack={packageName} | Sha256={actual}");
            return true;
        }

        public static bool VerifyZipSha256(string packageName, string localZipPath, string expectedSha256)
        {
            try
            {
                string actual = BootstrapSignedPackageHashPolicy.VerifyFile(localZipPath, expectedSha256);
                PcBootstrapPreLoginUpdateService.TryAppendLog($"OK | VerifySha256 | Pack={packageName} | Sha256={actual}");
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                PcBootstrapPreLoginUpdateService.TryAppendLog($"FAIL | VerifySha256 | Pack={packageName} | Error={ex.Message}");
                return false;
            }
        }

        public static string ComputeSha256LowerHex(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                return string.Empty;

            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(stream);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        private static async Task DownloadFileWithResumeAsync(
            string url,
            string outputPath,
            long expectedSize,
            bool useMicroAuth,
            IProgress<PcBootstrapProgress> progress,
            string stage,
            string packageName,
            CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? PcBootstrapLayout.DownloadsRoot);

            long existingBytes = 0;
            if (File.Exists(outputPath))
            {
                try
                {
                    existingBytes = new FileInfo(outputPath).Length;
                }
                catch (Exception)
                {
                    existingBytes = 0;
                }
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (existingBytes > 0)
                request.Headers.Range = new RangeHeaderValue(existingBytes, null);

            ApplyMicroAuthHeaders(request, useMicroAuth);

            using HttpResponseMessage response = await GetHttpClient().SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            bool append = existingBytes > 0 && response.StatusCode == HttpStatusCode.PartialContent;
            if (!append && existingBytes > 0 && response.StatusCode == HttpStatusCode.OK)
            {
                try
                {
                    File.Delete(outputPath);
                }
                catch (Exception)
                {
                }

                existingBytes = 0;
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
                throw new FileNotFoundException("远端资源不存在（HTTP 404）。", url);

            response.EnsureSuccessStatusCode();

            long totalBytes = ResolveTotalLength(response, existingBytes, expectedSize);

            progress?.Report(new PcBootstrapProgress
            {
                Stage = stage,
                PackageName = packageName,
                ReceivedBytes = existingBytes,
                TotalBytes = totalBytes,
                Message = $"开始下载：{packageName}",
            });

            using Stream contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var fileStream = new FileStream(
                outputPath,
                append ? FileMode.Append : FileMode.Create,
                FileAccess.Write,
                FileShare.Read);

            byte[] buffer = new byte[128 * 1024];
            long received = existingBytes;
            int read;
            while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
            {
                await fileStream.WriteAsync(buffer, 0, read, cancellationToken);
                received += read;

                progress?.Report(new PcBootstrapProgress
                {
                    Stage = stage,
                    PackageName = packageName,
                    ReceivedBytes = received,
                    TotalBytes = totalBytes,
                    Message = $"下载中：{packageName}",
                });
            }

            try
            {
                fileStream.Flush(flushToDisk: true);
            }
            catch (Exception)
            {
            }

            if (expectedSize > 0)
            {
                long actualSize = new FileInfo(outputPath).Length;
                if (actualSize != expectedSize)
                    throw new IOException($"下载大小不一致：Expected={expectedSize}, Actual={actualSize}");
            }
        }

        private static long ResolveTotalLength(HttpResponseMessage response, long existingBytes, long expectedSize)
        {
            try
            {
                long? rangeTotal = response.Content.Headers.ContentRange?.Length;
                if (rangeTotal.HasValue && rangeTotal.Value > 0)
                    return rangeTotal.Value;

                long? contentLength = response.Content.Headers.ContentLength;
                if (contentLength.HasValue && contentLength.Value > 0)
                    return existingBytes + contentLength.Value;

                if (expectedSize > 0)
                    return expectedSize;
            }
            catch (Exception)
            {
            }

            return 0;
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

    internal sealed class PcBootstrapRepositoryCandidate
    {
        public PcBootstrapRepositoryCandidate(string repositoryRoot, bool useMicroAuth)
        {
            RepositoryRoot = repositoryRoot ?? string.Empty;
            UseMicroAuth = useMicroAuth;
        }

        public string RepositoryRoot { get; }
        public bool UseMicroAuth { get; }
    }
}

