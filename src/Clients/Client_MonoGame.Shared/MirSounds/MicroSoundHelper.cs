using MonoShare.MirGraphics;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MonoShare.MirSounds
{
    internal static class MicroSoundHelper
    {
        private static readonly HttpClient HttpClient = new HttpClient();
        private static readonly SemaphoreSlim DownloadSemaphore = new SemaphoreSlim(2, 2);
        private static readonly ConcurrentDictionary<string, byte> PendingDownloads =
            new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, byte> MissingSounds =
            new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, DateTime> RetryNotBeforeUtc =
            new ConcurrentDictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        private static readonly TimeSpan NotFoundBackoff = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan RetryBackoff = TimeSpan.FromSeconds(10);

        private static DateTime _nextProbeUtc = DateTime.MinValue;
        private static int _probeFailures;

        static MicroSoundHelper()
        {
            HttpClient.Timeout = TimeSpan.FromSeconds(10);
        }

        public static bool MicroServerActive { get; private set; }
        public static string LastError { get; private set; } = string.Empty;

        public static bool IsConfigured =>
            TryGetBaseUri(out _)
            && !string.IsNullOrWhiteSpace(Settings.MicroUser);

        public static void QueueSoundDownload(string soundName, string localFilePath)
        {
            if (!IsConfigured)
                return;

            if (string.IsNullOrWhiteSpace(soundName) || string.IsNullOrWhiteSpace(localFilePath))
                return;

            string normalizedName = Path.GetFileNameWithoutExtension(soundName)?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(normalizedName))
                return;

            if (MissingSounds.ContainsKey(normalizedName))
                return;

            if (RetryNotBeforeUtc.TryGetValue(normalizedName, out DateTime notBeforeUtc) && DateTime.UtcNow < notBeforeUtc)
                return;

            string normalizedPath = Path.GetFullPath(localFilePath);
            if (File.Exists(normalizedPath))
                return;

            if (!PendingDownloads.TryAdd(normalizedPath, 0))
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

                        byte[] payload = await DownloadSoundAsync(normalizedName).ConfigureAwait(false);
                        if (payload == null || payload.Length == 0)
                            return;

                        Directory.CreateDirectory(Path.GetDirectoryName(normalizedPath) ?? ".");
                        string tempPath = normalizedPath + ".download";
                        await File.WriteAllBytesAsync(tempPath, payload).ConfigureAwait(false);
                        File.Move(tempPath, normalizedPath, overwrite: true);
                        MissingSounds.TryRemove(normalizedName, out _);
                        RetryNotBeforeUtc.TryRemove(normalizedName, out _);

                        if (Settings.LogErrors)
                            CMain.SaveLog($"MicroSound: 已下载 {normalizedName}.wav -> {normalizedPath}");

                        AsynDownLoadResources.CreateInstance().TryNotify(normalizedPath);
                    }
                    finally
                    {
                        DownloadSemaphore.Release();
                    }
                }
                catch (Exception ex)
                {
                    SetError($"微端声音下载失败：{normalizedName} -> {ex.Message}");
                    RetryNotBeforeUtc[normalizedName] = DateTime.UtcNow.Add(RetryBackoff);
                }
                finally
                {
                    PendingDownloads.TryRemove(normalizedPath, out _);
                }
            });
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
                ApplyAuthHeaders(request);
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
                SetError($"health: {(int)response.StatusCode}");
                return false;
            }
            catch (Exception ex)
            {
                _probeFailures++;
                MicroServerActive = false;
                SetError($"health: {ex.Message}");
                return false;
            }
        }

        private static async Task<byte[]> DownloadSoundAsync(string soundName)
        {
            using var stream = new MemoryStream();

            int current = 1;
            int max = 1;
            while (current <= max)
            {
                MicroSoundResponse chunk = await DownloadSoundChunkAsync(soundName, current).ConfigureAwait(false);
                if (chunk?.Bytes == null || chunk.Bytes.Length == 0)
                    return null;

                stream.Write(chunk.Bytes, 0, chunk.Bytes.Length);

                max = Math.Max(1, chunk.Max);
                current++;
            }

            return stream.ToArray();
        }

        private static async Task<MicroSoundResponse> DownloadSoundChunkAsync(string soundName, int chunkIndex)
        {
            if (!TryGetBaseUri(out Uri baseUri))
                return null;

            int retries = Math.Clamp(Settings.BootstrapDownloadRetryCount, 0, 20);
            string relative = $"sound/{Uri.EscapeDataString(soundName)}/{chunkIndex}";

            for (int attempt = 0; attempt <= retries; attempt++)
            {
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(baseUri, relative));
                    ApplyAuthHeaders(request);

                    using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                    {
                        SetError($"sound/{soundName}/{chunkIndex}: HTTP {(int)response.StatusCode}");
                        if ((int)response.StatusCode == 404)
                        {
                            MissingSounds[soundName] = 0;
                            RetryNotBeforeUtc[soundName] = DateTime.UtcNow.Add(NotFoundBackoff);
                            return null;
                        }
                        continue;
                    }

                    using Stream stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                    MicroSoundResponse payload = await JsonSerializer.DeserializeAsync<MicroSoundResponse>(stream).ConfigureAwait(false);
                    if (payload == null || payload.Bytes == null || payload.Bytes.Length == 0)
                    {
                        SetError($"sound/{soundName}/{chunkIndex}: 空响应。");
                        continue;
                    }

                    MicroServerActive = true;
                    return payload;
                }
                catch (Exception ex)
                {
                    SetError($"sound/{soundName}/{chunkIndex}: {ex.Message}");
                    RetryNotBeforeUtc[soundName] = DateTime.UtcNow.Add(RetryBackoff);
                    await Task.Delay(200).ConfigureAwait(false);
                }
            }

            return null;
        }

        private static void ApplyAuthHeaders(HttpRequestMessage request)
        {
            if (request == null)
                return;

            request.Headers.Remove("User");
            request.Headers.Remove("Code");
            request.Headers.TryAddWithoutValidation("User", Settings.MicroUser ?? string.Empty);
            request.Headers.TryAddWithoutValidation("Code", Settings.MicroCode ?? string.Empty);
        }

        private static bool TryGetBaseUri(out Uri baseUri)
        {
            baseUri = null;

            string baseUrl = (Settings.MicroBaseUrl ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(baseUrl))
                return false;

            if (!baseUrl.EndsWith("/", StringComparison.Ordinal))
                baseUrl += "/";

            return Uri.TryCreate(baseUrl, UriKind.Absolute, out baseUri);
        }

        private static void SetError(string message)
        {
            LastError = message ?? string.Empty;

            if (_probeFailures >= 5)
                _nextProbeUtc = DateTime.UtcNow.AddSeconds(15);

            if (Settings.LogErrors && !string.IsNullOrWhiteSpace(LastError))
                CMain.SaveError($"MicroSound: {LastError}");
        }

        private sealed class MicroSoundResponse
        {
            public byte[] Bytes { get; set; }
            public int Max { get; set; }
            public int Current { get; set; }
        }
    }
}
