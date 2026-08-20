using System.Net;
using System.Text;

namespace Server.Library.Utils
{
    internal abstract class HttpService
    {
        private sealed class HttpResponseState
        {
            public bool Started;
            public bool Closed;
        }

        protected string Host;
        private HttpListener _listener;
        private bool _isActive = true;
        private int _receivedRequests;
        private static int _urlAclHintEnqueued;
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<HttpListenerResponse, HttpResponseState> ResponseStates = new();

        protected HttpService()
        {
        }

        private static string EnsureTrailingSlash(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var trimmed = value.Trim();
            return trimmed.EndsWith("/", StringComparison.Ordinal) ? trimmed : trimmed + "/";
        }

        private static System.Collections.Generic.List<string> BuildPrefixAttempts(string host)
        {
            var normalized = EnsureTrailingSlash(host);
            if (string.IsNullOrWhiteSpace(normalized))
                return new System.Collections.Generic.List<string>();

            if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri))
                return new System.Collections.Generic.List<string> { normalized };

            var attempts = new System.Collections.Generic.List<string>();

            var path = uri.AbsolutePath;
            if (!path.EndsWith("/", StringComparison.Ordinal))
                path += "/";

            static string MakePrefix(Uri uri, string hostName, string path)
            {
                return $"{uri.Scheme}://{hostName}:{uri.Port}{path}";
            }

            if (string.Equals(uri.Host, "0.0.0.0", StringComparison.Ordinal))
            {
                // HttpListener 不支持把 0.0.0.0 当作前缀 Host（这不是 socket bind）；优先尝试通配符，其次回退到 loopback。
                attempts.Add(MakePrefix(uri, "+", path));
                attempts.Add(MakePrefix(uri, "127.0.0.1", path));
            }
            else
            {
                attempts.Add(normalized);

                // 非 loopback 的前缀在未配置 URLACL/未提权时常见会失败：为保证服务可用，提供一次 loopback 回退。
                if (!uri.IsLoopback)
                    attempts.Add(MakePrefix(uri, "127.0.0.1", path));
            }

            // 去重（保序）
            var unique = new System.Collections.Generic.List<string>();
            foreach (var attempt in attempts)
            {
                bool exists = false;
                foreach (var existing in unique)
                {
                    if (string.Equals(existing, attempt, StringComparison.OrdinalIgnoreCase))
                    {
                        exists = true;
                        break;
                    }
                }

                if (!exists)
                    unique.Add(attempt);
            }

            return unique;
        }

        private static void TryAppendRuntimeLog(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            try
            {
                var baseDir = AppContext.BaseDirectory;
                if (string.IsNullOrWhiteSpace(baseDir))
                    baseDir = Environment.CurrentDirectory;

                var logDir = System.IO.Path.Combine(baseDir, "Logs");
                System.IO.Directory.CreateDirectory(logDir);

                var logPath = System.IO.Path.Combine(logDir, "HttpService-runtime.log");
                var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}";
                System.IO.File.AppendAllText(logPath, line, Encoding.UTF8);
            }
            catch
            {
            }
        }

        protected static void AppendRuntimeLog(string message)
        {
            TryAppendRuntimeLog(message);
        }

        private static HttpResponseState GetResponseState(HttpListenerResponse response)
        {
            return ResponseStates.GetOrCreateValue(response);
        }

        protected bool HasResponseStarted(HttpListenerResponse response)
        {
            if (response == null)
                return false;

            return ResponseStates.TryGetValue(response, out var state) && state.Started;
        }

        private static void MarkResponseClosed(HttpListenerResponse response)
        {
            if (response == null)
                return;

            var state = GetResponseState(response);
            lock (state)
            {
                state.Closed = true;
            }
        }

        private static bool TryPrepareResponse(HttpListenerResponse response, int? statusCode, long contentLength, string contentType)
        {
            if (response == null)
                return false;

            var state = GetResponseState(response);
            lock (state)
            {
                if (state.Closed || state.Started)
                    return false;

                try
                {
                    if (statusCode.HasValue)
                        response.StatusCode = statusCode.Value;

                    response.ContentLength64 = contentLength;

                    if (!string.IsNullOrWhiteSpace(contentType))
                        response.ContentType = contentType;

                    state.Started = true;
                    return true;
                }
                catch (ObjectDisposedException)
                {
                    state.Closed = true;
                    return false;
                }
                catch (InvalidOperationException)
                {
                    state.Closed = true;
                    return false;
                }
                catch (HttpListenerException)
                {
                    state.Closed = true;
                    return false;
                }
            }
        }

        protected bool TryBeginStreamingResponse(HttpListenerResponse response, HttpStatusCode statusCode, long contentLength, string contentType)
        {
            return TryPrepareResponse(response, (int)statusCode, contentLength, contentType);
        }

        private static bool TryGetPortFromUrl(string value, out int port)
        {
            port = 0;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            var normalized = EnsureTrailingSlash(value);

            if (Uri.TryCreate(normalized, UriKind.Absolute, out var uri))
            {
                port = uri.Port;
                return port > 0;
            }

            // 支持 http://+:7777/ 这类 HttpListener 前缀（Uri 无法解析 + / * Host）
            try
            {
                var schemeSep = normalized.IndexOf("://", StringComparison.Ordinal);
                if (schemeSep < 0)
                    return false;

                var afterScheme = schemeSep + 3;
                var colon = normalized.IndexOf(':', afterScheme);
                if (colon < 0)
                    return false;

                var slash = normalized.IndexOf('/', colon + 1);
                if (slash < 0)
                    slash = normalized.Length;

                var portText = normalized.Substring(colon + 1, slash - colon - 1);
                return int.TryParse(portText, out port) && port > 0;
            }
            catch
            {
                return false;
            }
        }

        private static string BuildUrlAclHint(string prefix, string rawHost)
        {
            if (!OperatingSystem.IsWindows())
                return string.Empty;

            if (string.IsNullOrWhiteSpace(prefix))
                return string.Empty;

            var url = EnsureTrailingSlash(prefix);

            var userDomain = Environment.UserDomainName;
            var userName = Environment.UserName;
            var user = string.IsNullOrWhiteSpace(userDomain) ? userName : $"{userDomain}\\{userName}";

            var port = 0;
            if (!TryGetPortFromUrl(rawHost, out port))
                TryGetPortFromUrl(prefix, out port);

            var portHint = port > 0 ? $"；并确保防火墙放行 TCP {port}" : string.Empty;

            return $"提示：监听 {url} 失败（拒绝访问/未授权）。解决：用管理员权限执行 netsh http add urlacl url={url} user=\"{user}\"（或 user=Everyone），然后重启服务端{portHint}。";
        }

        private static void EnqueueUrlAclHintOnce(string prefix, string rawHost)
        {
            if (System.Threading.Interlocked.Exchange(ref _urlAclHintEnqueued, 1) == 1)
                return;

            var hint = BuildUrlAclHint(prefix, rawHost);
            if (!string.IsNullOrWhiteSpace(hint))
                MessageQueue.Instance.Enqueue(hint);
        }

        private void QueueRequest(HttpListenerContext context)
        {
            if (context == null)
                return;

            _ = Task.Run(() => ProcessRequest(context));
        }

        private void ProcessRequest(HttpListenerContext context)
        {
            if (context == null)
                return;

            var request = context.Request;
            var response = context.Response;
            int requestId = Interlocked.Increment(ref _receivedRequests);

            try
            {
                if (requestId <= 20)
                    TryAppendRuntimeLog($"Request #{requestId}: {request.HttpMethod} {request.RawUrl} host={request.UserHostName} remote={request.RemoteEndPoint}");

                if (requestId <= 5)
                    TryAppendRuntimeLog($"Request #{requestId}: handler begin");

                if (string.Equals(request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase))
                    OnGetRequest(request, response);
                else
                    OnPostRequest(request, response);

                if (requestId <= 5)
                    TryAppendRuntimeLog($"Request #{requestId}: handler end");
            }
            catch (Exception error)
            {
                try
                {
                    if (!HasResponseStarted(response))
                        WriteStatusResponse(response, HttpStatusCode.InternalServerError, "request error: " + error.Message);
                }
                catch
                {
                }

                try
                {
                    MessageQueue.Instance.Enqueue("Http请求处理异常: " + error);
                }
                catch
                {
                }

                TryAppendRuntimeLog($"Request handler error: {error.Message}");
            }
            finally
            {
                Exception closeError = null;
                try
                {
                    response.Close();
                    MarkResponseClosed(response);
                }
                catch (Exception error)
                {
                    closeError = error;
                    MarkResponseClosed(response);
                }

                if (requestId <= 5)
                {
                    if (closeError == null)
                        TryAppendRuntimeLog($"Request #{requestId}: response closed");
                    else
                        TryAppendRuntimeLog($"Request #{requestId}: response close error: {closeError.Message}");
                }
            }
        }

        public void Listen(object obj)
        {
            CancellationToken token = (CancellationToken)obj;

            if (!HttpListener.IsSupported)
            {
                throw new InvalidOperationException(
                    "要使用Http服务器，操作系统必须是Windows XP SP2或Server 2003或更高版本");
            }

            var prefixAttempts = BuildPrefixAttempts(Host);
            if (prefixAttempts.Count == 0)
            {
                MessageQueue.Instance.Enqueue("Http服务器 启动失败! 错误: HTTPIPAddress 为空");
                return;
            }

            foreach (var prefix in prefixAttempts)
            {
                _listener = new HttpListener();
                try
                {
                    _listener.Prefixes.Add(prefix);
                    _listener.Start();

                    if (!string.Equals(prefix, EnsureTrailingSlash(Host), StringComparison.OrdinalIgnoreCase))
                        MessageQueue.Instance.Enqueue($"Http服务器 成功开启（回退）：{prefix}（原始={Host}）");
                    else
                        MessageQueue.Instance.Enqueue($"Http服务器 成功开启：{prefix}");

                    TryAppendRuntimeLog($"HttpListener started: prefix={prefix} rawHost={Host}");
                    break;
                }
                catch (Exception err)
                {
                    MessageQueue.Instance.Enqueue($"Http服务器 启动失败! 前缀={prefix} 错误:{err}");

                    if (err is HttpListenerException { ErrorCode: 5 })
                        EnqueueUrlAclHintOnce(prefix, Host);

                    TryAppendRuntimeLog($"HttpListener start failed: prefix={prefix} rawHost={Host} err={err.Message}");

                    try
                    {
                        _listener.Close();
                    }
                    catch
                    {
                    }

                    _listener = null;
                }
            }

            if (_listener == null || !_listener.IsListening)
            {
                MessageQueue.Instance.Enqueue($"Http服务器 启动失败! Host={Host}（已尝试：{string.Join(", ", prefixAttempts)}）");
                return;
            }

            while (_isActive && !token.IsCancellationRequested)
            {
                try
                {
                    var context = _listener.GetContext();
                    QueueRequest(context);
                }
                catch (Exception error)
                {
                    TryAppendRuntimeLog($"Http loop error: {error.Message}");
                }
            }
        }

        public void Stop()
        {
            _isActive = false;
            if (_listener != null && _listener.IsListening)
            {
                _listener.Stop();
            }
        }

        public abstract void OnGetRequest(HttpListenerRequest request, HttpListenerResponse response);
        public abstract void OnPostRequest(HttpListenerRequest request, HttpListenerResponse response);

        public void WriteResponse(HttpListenerResponse response, string responseString)
        {
            responseString ??= string.Empty;

            var payload = Encoding.UTF8.GetBytes(responseString);
            if (!TryPrepareResponse(response, null, payload.Length, "text/html; charset=UTF-8"))
                return;

            try
            {
                response.OutputStream.Write(payload, 0, payload.Length);
            }
            catch (ObjectDisposedException)
            {
                MarkResponseClosed(response);
            }
            catch (InvalidOperationException)
            {
                MarkResponseClosed(response);
            }
            catch (HttpListenerException)
            {
                MarkResponseClosed(response);
            }
        }

        public void WriteBytesResponse(HttpListenerResponse response, byte[] payload, string contentType = "application/octet-stream")
        {
            payload ??= Array.Empty<byte>();

            if (!TryPrepareResponse(response, null, payload.Length, contentType))
                return;

            try
            {
                response.OutputStream.Write(payload, 0, payload.Length);
            }
            catch (ObjectDisposedException)
            {
                MarkResponseClosed(response);
            }
            catch (InvalidOperationException)
            {
                MarkResponseClosed(response);
            }
            catch (HttpListenerException)
            {
                MarkResponseClosed(response);
            }
        }

        public void WriteStatusResponse(HttpListenerResponse response, HttpStatusCode statusCode, string responseString)
        {
            responseString ??= string.Empty;

            var payload = Encoding.UTF8.GetBytes(responseString);
            if (!TryPrepareResponse(response, (int)statusCode, payload.Length, "text/html; charset=UTF-8"))
                return;

            try
            {
                response.OutputStream.Write(payload, 0, payload.Length);
            }
            catch (ObjectDisposedException)
            {
                MarkResponseClosed(response);
            }
            catch (InvalidOperationException)
            {
                MarkResponseClosed(response);
            }
            catch (HttpListenerException)
            {
                MarkResponseClosed(response);
            }
        }

        public void WriteStatusBytesResponse(HttpListenerResponse response, HttpStatusCode statusCode, byte[] payload, string contentType = "application/octet-stream")
        {
            payload ??= Array.Empty<byte>();
            if (!TryPrepareResponse(response, (int)statusCode, payload.Length, contentType))
                return;

            try
            {
                response.OutputStream.Write(payload, 0, payload.Length);
            }
            catch (ObjectDisposedException)
            {
                MarkResponseClosed(response);
            }
            catch (InvalidOperationException)
            {
                MarkResponseClosed(response);
            }
            catch (HttpListenerException)
            {
                MarkResponseClosed(response);
            }
        }
    }
}
