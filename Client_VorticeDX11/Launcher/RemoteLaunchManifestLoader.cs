using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Launcher.Remote
{
    public enum LaunchManifestSource
    {
        Remote,
        Cache,
        Local,
    }

    public sealed class LaunchManifestLoadResult
    {
        public RemoteLaunchManifest Manifest { get; }
        public LaunchManifestSource Source { get; }
        public string Warning { get; }

        public LaunchManifestLoadResult(RemoteLaunchManifest manifest, LaunchManifestSource source, string warning)
        {
            Manifest = manifest;
            Source = source;
            Warning = warning ?? string.Empty;
        }
    }

    public sealed class RemoteLaunchManifestLoader
    {
        public const int MaximumResponseBytes = 1024 * 1024;

        private static readonly UTF8Encoding Utf8NoBomStrict = new(false, true);
        private readonly HttpClient _httpClient;
        private readonly string _cachePath;
        private readonly TimeSpan _timeout;

        public RemoteLaunchManifestLoader(HttpClient httpClient, string launcherCacheRoot, TimeSpan timeout)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            if (string.IsNullOrWhiteSpace(launcherCacheRoot)) throw new ArgumentException("缓存目录不能为空", nameof(launcherCacheRoot));
            if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
            _cachePath = Path.Combine(launcherCacheRoot, "RemoteLaunchManifest.json");
            _timeout = timeout;
        }

        public async Task<LaunchManifestLoadResult> LoadAsync(
            string serverListUrl,
            RemoteLaunchManifest localFallback,
            CancellationToken cancellationToken = default) =>
            await LoadAsync(serverListUrl, () => localFallback, cancellationToken).ConfigureAwait(false);

        public async Task<LaunchManifestLoadResult> LoadAsync(
            string serverListUrl,
            Func<RemoteLaunchManifest> localFallbackFactory,
            CancellationToken cancellationToken = default)
        {
            if (localFallbackFactory == null) throw new ArgumentNullException(nameof(localFallbackFactory));

            string remoteFailure = string.Empty;
            string remoteHost = string.Empty;
            try
            {
                Uri uri = ValidateListUri(serverListUrl);
                remoteHost = uri.Host;
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(_timeout);
                using HttpResponseMessage response = await _httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                string json = await ReadLimitedUtf8Async(response.Content, timeout.Token).ConfigureAwait(false);
                RemoteLaunchManifest manifest = RemoteLaunchManifest.ParseAndValidate(json);
                string cacheWarning = string.Empty;
                try
                {
                    await WriteAtomicAsync(_cachePath, json, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    cacheWarning = $"远程区服列表有效，但缓存写入失败：{ex.GetType().Name}";
                }

                return new LaunchManifestLoadResult(manifest, LaunchManifestSource.Remote, cacheWarning);
            }
            catch (Exception ex) when (ex is HttpRequestException
                                       or TaskCanceledException
                                       or InvalidLaunchManifestException
                                       or InvalidDataException
                                       or ArgumentException
                                       or DecoderFallbackException)
            {
                remoteFailure = DescribeRemoteFailure(remoteHost, ex);
            }

            try
            {
                string cachedJson = await File.ReadAllTextAsync(_cachePath, Utf8NoBomStrict, cancellationToken).ConfigureAwait(false);
                RemoteLaunchManifest cached = RemoteLaunchManifest.ParseAndValidate(cachedJson);
                return new LaunchManifestLoadResult(cached, LaunchManifestSource.Cache,
                    string.IsNullOrEmpty(remoteFailure) ? "已使用上次区服列表" : remoteFailure + "；已使用上次区服列表");
            }
            catch (Exception ex) when (ex is IOException
                                       or UnauthorizedAccessException
                                       or InvalidLaunchManifestException
                                       or DecoderFallbackException)
            {
                string cacheFailure = $"本地区服列表缓存不可用：{ex.GetType().Name}: {ex.Message}";
                string warning = string.IsNullOrEmpty(remoteFailure)
                    ? cacheFailure + "；已使用本地配置"
                    : remoteFailure + "；" + cacheFailure + "；已使用本地配置";
                RemoteLaunchManifest localFallback = localFallbackFactory()
                    ?? throw new InvalidOperationException("本地保底配置为空");
                return new LaunchManifestLoadResult(localFallback, LaunchManifestSource.Local, warning);
            }
        }

        private static Uri ValidateListUri(string value)
        {
            if (!Uri.TryCreate((value ?? string.Empty).Trim(), UriKind.Absolute, out Uri uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                || string.IsNullOrWhiteSpace(uri.Host)
                || !string.IsNullOrEmpty(uri.UserInfo))
            {
                throw new ArgumentException("Launcher.ServerListUrl 必须是 HTTP/HTTPS 绝对地址");
            }

            return uri;
        }

        private static string DescribeRemoteFailure(string host, Exception exception)
        {
            string source = string.IsNullOrEmpty(host) ? "配置地址" : host;
            return exception switch
            {
                InvalidLaunchManifestException manifest => $"远程区服列表配置无效（{source}）：{manifest.Message}",
                InvalidDataException data => $"远程区服列表读取失败（{source}）：{data.Message}",
                DecoderFallbackException => $"远程区服列表读取失败（{source}）：不是有效 UTF-8 文档",
                TaskCanceledException => $"远程区服列表读取超时（{source}）",
                HttpRequestException http when http.StatusCode.HasValue =>
                    $"远程区服列表读取失败（{source}）：HTTP {(int)http.StatusCode.Value}",
                HttpRequestException => $"远程区服列表读取失败（{source}）：网络连接失败",
                _ => $"远程区服列表读取失败（{source}）：地址格式无效",
            };
        }

        private static async Task<string> ReadLimitedUtf8Async(HttpContent content, CancellationToken cancellationToken)
        {
            if (content.Headers.ContentLength > MaximumResponseBytes)
                throw new InvalidDataException("远程启动清单超过 1 MiB 限制");

            await using Stream stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var buffer = new MemoryStream();
            byte[] chunk = new byte[8192];
            while (true)
            {
                int read = await stream.ReadAsync(chunk.AsMemory(0, chunk.Length), cancellationToken).ConfigureAwait(false);
                if (read == 0) break;
                if (buffer.Length + read > MaximumResponseBytes)
                    throw new InvalidDataException("远程启动清单超过 1 MiB 限制");
                buffer.Write(chunk, 0, read);
            }

            byte[] bytes = buffer.ToArray();
            int offset = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
            return Utf8NoBomStrict.GetString(bytes, offset, bytes.Length - offset);
        }

        private static async Task WriteAtomicAsync(string path, string content, CancellationToken cancellationToken)
        {
            string directory = Path.GetDirectoryName(path) ?? throw new InvalidOperationException("缓存路径缺少目录");
            Directory.CreateDirectory(directory);
            string temporaryPath = Path.Combine(directory, Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                await File.WriteAllTextAsync(temporaryPath, content, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
                if (File.Exists(path))
                    File.Replace(temporaryPath, path, null);
                else
                    File.Move(temporaryPath, path);
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
        }
    }
}
