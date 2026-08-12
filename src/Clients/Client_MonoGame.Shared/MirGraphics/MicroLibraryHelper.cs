using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace MonoShare.MirGraphics
{
    internal static class MicroLibraryHelper
    {
        private static readonly HttpClient HttpClient = new HttpClient();
        private static readonly SemaphoreSlim DownloadSemaphore = new SemaphoreSlim(4, 4);
        private static readonly ConcurrentDictionary<string, byte> PendingHeaderDownloads =
            new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, byte> PendingImageDownloads =
            new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> FileWriteLocks =
            new ConcurrentDictionary<string, SemaphoreSlim>(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, DateTime> RetryNotBeforeUtc =
            new ConcurrentDictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentQueue<string> PendingUserNotifications = new ConcurrentQueue<string>();
        private static readonly ConcurrentDictionary<string, DateTime> UserNotificationNotBeforeUtc =
            new ConcurrentDictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        private static readonly TimeSpan RetryBackoff = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan UserNotificationThrottle = TimeSpan.FromSeconds(15);

        private static DateTime _nextProbeUtc = DateTime.MinValue;
        private static int _probeFailures;

        static MicroLibraryHelper()
        {
            HttpClient.Timeout = TimeSpan.FromSeconds(15);
        }

        internal static SemaphoreSlim GetOrCreateFileLock(string localFilePath)
        {
            string key = string.IsNullOrWhiteSpace(localFilePath) ? "<unknown-lib>" : localFilePath;
            return FileWriteLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        }

        public static bool MicroServerActive { get; private set; }
        public static string LastError { get; private set; } = string.Empty;

        public static bool IsConfigured =>
            TryGetBaseUri(out _)
            && !string.IsNullOrWhiteSpace(Settings.MicroUser);

        public static void FlushPendingNotifications(Action<string> notify)
        {
            if (notify == null)
                return;

            int processed = 0;
            while (processed < 8 && PendingUserNotifications.TryDequeue(out string message))
            {
                processed++;

                if (string.IsNullOrWhiteSpace(message))
                    continue;

                notify(message);
            }
        }

        public static bool IsLibraryHeaderDownloadPending(string microRelativeFilePath)
        {
            string key = NormalizeMicroRelativePath(microRelativeFilePath);
            return PendingHeaderDownloads.ContainsKey(key);
        }

        public static bool IsLibraryImageDownloadPending(string microRelativeFilePath, int index)
        {
            string normalized = NormalizeMicroRelativePath(microRelativeFilePath);
            string key = $"{normalized}|{index}";
            return PendingImageDownloads.ContainsKey(key);
        }

        public static void QueueLibraryHeaderDownload(string microRelativeFilePath, string localFilePath)
        {
            if (!IsConfigured)
                return;

            if (string.IsNullOrWhiteSpace(microRelativeFilePath) || string.IsNullOrWhiteSpace(localFilePath))
                return;

            string key = NormalizeMicroRelativePath(microRelativeFilePath);
            if (!CanQueueDownload(key) || !PendingHeaderDownloads.TryAdd(key, 0))
                return;

            _ = Task.Run(async () =>
            {
                try
                {
                    await DownloadSemaphore.WaitAsync().ConfigureAwait(false);
                    try
                    {
                        if (!await EnsureMicroServerOnlineAsync().ConfigureAwait(false))
                            return;

                        string api = BuildLibraryApiPath("libheader", key);
                        byte[] payload = await DownloadBinaryAsync(api).ConfigureAwait(false);
                        if (payload == null || payload.Length < 12)
                        {
                            MarkDownloadFailed(key, $"HEADER empty: {key}");
                            QueueDownloadFailureNotification(key, "资源头");
                            return;
                        }

                        if (!TryParseLibraryHeaderPayload(payload, out long totalLength, out byte[] headerBytes))
                        {
                            MarkDownloadFailed(key, $"HEADER invalid: {key}");
                            QueueDownloadFailureNotification(key, "资源头");
                            return;
                        }

                        Directory.CreateDirectory(Path.GetDirectoryName(localFilePath) ?? ".");
                        var fileLock = GetOrCreateFileLock(localFilePath);
                        await fileLock.WaitAsync().ConfigureAwait(false);
                        try
                        {
                            using (var stream = new FileStream(localFilePath, FileMode.Create, FileAccess.ReadWrite, FileShare.ReadWrite))
                            {
                                stream.SetLength(totalLength);
                                stream.Write(headerBytes, 0, headerBytes.Length);
                            }
                        }
                        finally
                        {
                            fileLock.Release();
                        }

                        RetryNotBeforeUtc.TryRemove(key, out _);
                        MicroServerActive = true;
                        LastError = string.Empty;
                        AsynDownLoadResources.CreateInstance().TryNotify(localFilePath);
                    }
                    finally
                    {
                        DownloadSemaphore.Release();
                    }
                }
                catch (Exception ex)
                {
                    MarkDownloadFailed(key, $"HEADER exception: {key} -> {ex.Message}");
                    QueueDownloadFailureNotification(key, "资源头");
                }
                finally
                {
                    PendingHeaderDownloads.TryRemove(key, out _);
                }
            });
        }

        public static void QueueLibraryImageDownload(string microRelativeFilePath, string localFilePath, int index)
        {
            if (!IsConfigured)
                return;

            if (string.IsNullOrWhiteSpace(microRelativeFilePath) || string.IsNullOrWhiteSpace(localFilePath))
                return;

            if (index < 0)
                return;

            string normalized = NormalizeMicroRelativePath(microRelativeFilePath);
            string key = $"{normalized}|{index}";

            if (!CanQueueDownload(key) || !PendingImageDownloads.TryAdd(key, 0))
                return;

            _ = Task.Run(async () =>
            {
                try
                {
                    await DownloadSemaphore.WaitAsync().ConfigureAwait(false);
                    try
                    {
                        if (!await EnsureMicroServerOnlineAsync().ConfigureAwait(false))
                            return;

                        string api = BuildLibraryApiPath("libimage", normalized, index);
                        byte[] payload = await DownloadBinaryAsync(api).ConfigureAwait(false);
                        if (payload == null || payload.Length < 8)
                        {
                            MarkDownloadFailed(key, $"IMAGE empty: {key}");
                            QueueDownloadFailureNotification(key, "图块");
                            return;
                        }

                        if (!TryParseLibraryImagePayload(payload, out int position, out byte[] bytes))
                        {
                            MarkDownloadFailed(key, $"IMAGE invalid: {key}");
                            QueueDownloadFailureNotification(key, "图块");
                            return;
                        }

                        if (!File.Exists(localFilePath))
                        {
                            QueueLibraryHeaderDownload(normalized, localFilePath);
                            return;
                        }

                        var fileLock = GetOrCreateFileLock(localFilePath);
                        await fileLock.WaitAsync().ConfigureAwait(false);
                        try
                        {
                            using var stream = new FileStream(localFilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
                            stream.Seek(position, SeekOrigin.Begin);
                            stream.Write(bytes, 0, bytes.Length);
                        }
                        finally
                        {
                            fileLock.Release();
                        }

                        RetryNotBeforeUtc.TryRemove(key, out _);
                        MicroServerActive = true;
                        LastError = string.Empty;
                    }
                    finally
                    {
                        DownloadSemaphore.Release();
                    }
                }
                catch (Exception ex)
                {
                    MarkDownloadFailed(key, $"IMAGE exception: {key} -> {ex.Message}");
                    QueueDownloadFailureNotification(key, "图块");
                }
                finally
                {
                    PendingImageDownloads.TryRemove(key, out _);
                }
            });
        }

        private static bool CanQueueDownload(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return false;

            if (!RetryNotBeforeUtc.TryGetValue(key, out DateTime nextAttemptUtc))
                return true;

            return DateTime.UtcNow >= nextAttemptUtc;
        }

        private static void MarkDownloadFailed(string key, string message)
        {
            if (!string.IsNullOrWhiteSpace(key))
                RetryNotBeforeUtc[key] = DateTime.UtcNow.Add(RetryBackoff);

            SetError(message);
        }

        private static void QueueProbeUnavailableNotification()
        {
            QueueUserNotification(
                "probe",
                "微端资源服务当前不可用，客户端将稍后自动重试。",
                UserNotificationThrottle);
        }

        private static void QueueDownloadFailureNotification(string key, string stage)
        {
            if (string.IsNullOrWhiteSpace(key))
                return;

            string resource = key;
            int splitIndex = resource.IndexOf('|');
            if (splitIndex >= 0)
                resource = resource.Substring(0, splitIndex);

            resource = Path.GetFileName(resource);
            if (string.IsNullOrWhiteSpace(resource))
                resource = key;

            QueueUserNotification(
                $"download:{stage}:{resource}",
                $"微端{stage}拉取失败：{resource}，客户端将稍后自动重试。",
                UserNotificationThrottle);
        }

        private static void QueueHttpFailureNotification(string apiRelativePath, HttpStatusCode statusCode)
        {
            switch (statusCode)
            {
                case HttpStatusCode.Unauthorized:
                case HttpStatusCode.Forbidden:
                    QueueUserNotification(
                        "http:401",
                        "微端鉴权失败，请检查 Mir2Config.ini 中 [Micro] 的 User/Code 配置。",
                        UserNotificationThrottle);
                    break;
                case HttpStatusCode.NotFound:
                    QueueUserNotification(
                        "http:404",
                        "微端资源不存在（HTTP 404），请检查服务端 MicroResourcePath 与资源目录。",
                        UserNotificationThrottle);
                    break;
                case HttpStatusCode.ServiceUnavailable:
                    QueueUserNotification(
                        "http:503",
                        "微端服务端未完成配置（MicroAuthor/MicroResourcePath），请检查服务端 Setup.ini 的 [Micro] 段。",
                        UserNotificationThrottle);
                    break;
            }
        }

        private static void QueueUserNotification(string key, string message, TimeSpan throttle)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            string normalizedKey = string.IsNullOrWhiteSpace(key) ? message : key;
            DateTime now = DateTime.UtcNow;

            if (UserNotificationNotBeforeUtc.TryGetValue(normalizedKey, out DateTime nextAllowedUtc) && now < nextAllowedUtc)
                return;

            UserNotificationNotBeforeUtc[normalizedKey] = now.Add(throttle);
            PendingUserNotifications.Enqueue(message);
        }

        private static async Task<bool> EnsureMicroServerOnlineAsync()
        {
            if (!TryGetBaseUri(out Uri baseUri))
                return false;

            if (DateTime.UtcNow < _nextProbeUtc)
                return MicroServerActive;

            _nextProbeUtc = DateTime.UtcNow.AddSeconds(15);

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(baseUri, "health"));
                using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    MicroServerActive = true;
                    _probeFailures = 0;
                    LastError = string.Empty;
                    return true;
                }

                _probeFailures++;
                MicroServerActive = false;
                LastError = $"health: {(int)response.StatusCode}";
                if (_probeFailures == 1)
                {
                    SetError(LastError);
                    QueueProbeUnavailableNotification();
                }
                return false;
            }
            catch (Exception ex)
            {
                _probeFailures++;
                MicroServerActive = false;
                LastError = $"health: {ex.Message}";
                if (_probeFailures == 1)
                {
                    SetError(LastError);
                    QueueProbeUnavailableNotification();
                }
                return false;
            }
        }

        private static async Task<byte[]> DownloadBinaryAsync(string apiRelativePath)
        {
            if (!TryGetBaseUri(out Uri baseUri))
                return null;

            int retries = Math.Clamp(Settings.BootstrapDownloadRetryCount, 0, 20);
            for (int attempt = 0; attempt <= retries; attempt++)
            {
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(baseUri, apiRelativePath));
                    ApplyAuthHeaders(request);
                    using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                    {
                        SetError($"HTTP {(int)response.StatusCode} {apiRelativePath}");
                        QueueHttpFailureNotification(apiRelativePath, response.StatusCode);
                        MicroServerActive = false;

                        if (response.StatusCode == HttpStatusCode.Unauthorized
                            || response.StatusCode == HttpStatusCode.Forbidden
                            || response.StatusCode == HttpStatusCode.NotFound
                            || response.StatusCode == HttpStatusCode.ServiceUnavailable)
                            return null;
                        continue;
                    }

                    MicroServerActive = true;
                    return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    SetError($"{apiRelativePath}：{ex.Message}");
                    MicroServerActive = false;
                    await Task.Delay(200).ConfigureAwait(false);
                }
            }

            return null;
        }

        private static void ApplyAuthHeaders(HttpRequestMessage request)
        {
            request.Headers.Remove("User");
            request.Headers.Remove("Code");

            request.Headers.TryAddWithoutValidation("User", Settings.MicroUser ?? string.Empty);
            request.Headers.TryAddWithoutValidation("Code", Settings.MicroCode ?? string.Empty);
            request.Headers.Accept.Clear();
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
        }

        private static bool TryParseLibraryHeaderPayload(byte[] payload, out long totalLength, out byte[] headerBytes)
        {
            totalLength = 0;
            headerBytes = null;

            try
            {
                using var ms = new MemoryStream(payload);
                using var reader = new BinaryReader(ms);
                totalLength = reader.ReadInt64();
                int headerLength = reader.ReadInt32();
                if (totalLength <= 0 || headerLength <= 0 || headerLength > payload.Length)
                    return false;

                headerBytes = reader.ReadBytes(headerLength);
                return headerBytes.Length == headerLength;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryParseLibraryImagePayload(byte[] payload, out int position, out byte[] bytes)
        {
            position = 0;
            bytes = null;

            try
            {
                using var ms = new MemoryStream(payload);
                using var reader = new BinaryReader(ms);
                position = reader.ReadInt32();
                int length = reader.ReadInt32();
                if (position < 0 || length <= 0 || length > payload.Length)
                    return false;

                bytes = reader.ReadBytes(length);
                return bytes.Length == length;
            }
            catch
            {
                return false;
            }
        }

        private static string BuildLibraryApiPath(string action, string microRelativePath, int? index = null)
        {
            microRelativePath = NormalizeMicroRelativePath(microRelativePath);

            string name = Path.GetFileName(microRelativePath) ?? string.Empty;
            string path = Path.GetDirectoryName(microRelativePath)?.Replace('\\', '/').Replace('/', '_') ?? string.Empty;

            string encodedPath = Uri.EscapeDataString(path);
            string encodedName = Uri.EscapeDataString(name);

            if (index.HasValue)
                return $"{action}/{encodedPath}/{encodedName}/{index.Value}";

            return $"{action}/{encodedPath}/{encodedName}";
        }

        private static string NormalizeMicroRelativePath(string microRelativePath)
        {
            string normalized = (microRelativePath ?? string.Empty)
                .Replace('\\', '/')
                .TrimStart('/');

            while (normalized.StartsWith("./", StringComparison.Ordinal))
                normalized = normalized.Substring(2);

            return normalized;
        }

        private static bool TryGetBaseUri(out Uri baseUri)
        {
            baseUri = null;

            string baseUrl = (Settings.MicroBaseUrl ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(baseUrl))
                return false;

            if (!baseUrl.EndsWith("/", StringComparison.Ordinal))
                baseUrl += "/";

            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out baseUri))
                return false;

            return true;
        }

        private static void SetError(string message)
        {
            LastError = message ?? string.Empty;

            if (_probeFailures >= 5)
                _nextProbeUtc = DateTime.UtcNow.AddSeconds(15);

            try
            {
                if (Settings.LogErrors)
                    CMain.SaveError($"Micro: {LastError}");
            }
            catch
            {
            }
        }
    }
}
