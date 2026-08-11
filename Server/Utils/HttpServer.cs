using System.Net;
using Server.MirEnvir;
using System.Collections.Generic;
using Server.Security;
using Server.Persistence.Sql;
using Server.Operations;
using System.Text.Json;
using LyoCrystal.MicroGateway;
using S = ServerPackets;

namespace Server.Library.Utils
{
    class HttpServer : HttpService
    {
        Thread _thread;
        CancellationTokenSource tokenSource = new();
        private readonly string _administratorToken;
        private readonly string _operatorToken;
        private readonly SqliteBackupService _backupService;
        private readonly BasicOperationsMonitor _operationsMonitor;
        private readonly KillSwitchService _killSwitches;
        private readonly MicroGatewayCore _microGateway = new();
        private readonly object _microConfigurationLock = new();
        private int _stopping;

        public HttpServer(
            SqliteBackupService backupService = null,
            BasicOperationsMonitor operationsMonitor = null,
            KillSwitchService killSwitches = null)
        {
            Host = Settings.HTTPIPAddress;
            _administratorToken = ProtectedSecretStore.Read(ProtectedSecretStore.AdministratorToken);
            _operatorToken = ProtectedSecretStore.Read(ProtectedSecretStore.OperatorToken);
            _backupService = backupService;
            _operationsMonitor = operationsMonitor ?? new BasicOperationsMonitor(backupService);
            _killSwitches = killSwitches;
            if (Settings.MicroServerActive)
            {
                _microGateway.StartAsync(new MicroGatewayOptions(
                    Settings.MicroResourcePath,
                    Settings.MicroAuthor,
                    Settings.MicroCode,
                    ResourceUpdateEnabled: () => _killSwitches == null || _killSwitches.IsEnabled(KillSwitchFeature.ResourceUpdate)))
                    .GetAwaiter().GetResult();
            }
        }

        public void Start()
        {
            AdminSecurityPolicy.ValidateListener(Host);
            _operationsMonitor.Start();
            _thread = new Thread(Listen);
            _thread.Start(tokenSource.Token);
        }

        public new void Stop()
        {
            Interlocked.Exchange(ref _stopping, 1);
            base.Stop();
            
            tokenSource.Cancel();
            Thread.Sleep(1000);
            tokenSource.Dispose();
            _operationsMonitor.Dispose();
            lock (_microConfigurationLock)
                _microGateway.StopAsync().GetAwaiter().GetResult();

        }


        public override void OnGetRequest(HttpListenerRequest request, HttpListenerResponse response)
        {
            try
            {
                var path = request.Url?.AbsolutePath ?? "/";

                if (path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
                {
                    if (Volatile.Read(ref _stopping) != 0)
                    {
                        WriteStatusResponse(response, HttpStatusCode.ServiceUnavailable, "micro stopping");
                        return;
                    }

                    if (!Settings.MicroServerActive)
                    {
                        WriteStatusResponse(response, HttpStatusCode.NotFound, "micro disabled");
                        return;
                    }

                    var microOptions = new MicroGatewayOptions(
                        Settings.MicroResourcePath,
                        Settings.MicroAuthor,
                        Settings.MicroCode,
                        ResourceUpdateEnabled: () => _killSwitches == null || _killSwitches.IsEnabled(KillSwitchFeature.ResourceUpdate));
                    lock (_microConfigurationLock)
                    {
                        if (Volatile.Read(ref _stopping) != 0)
                        {
                            WriteStatusResponse(response, HttpStatusCode.ServiceUnavailable, "micro stopping");
                            return;
                        }
                        _microGateway.StartAsync(microOptions).GetAwaiter().GetResult();
                    }
                    HttpListenerMicroAdapter.HandleAsync(_microGateway, request, response).GetAwaiter().GetResult();
                    return;
                }

                if (!IsTrustedClient(request, response))
                    return;

                if (!AuthorizeAdminRequest(request, response, path))
                    return;

                switch (path.ToLowerInvariant())
                {
                    case "/":
                        WriteResponse(response, GameLanguage.GameName);
                        break;
                    case "/newaccount":
                        var id = request.QueryString["id"];
                        var psd = request.QueryString["psd"];
                        var email = request.QueryString["email"];
                        var name = request.QueryString["name"];
                        var question = request.QueryString["question"];
                        var answer = request.QueryString["answer"];
                        var ip = request.QueryString["ip"];
                        var p = new ClientPackets.NewAccount();
                        p.AccountID = id;
                        p.Password = psd;
                        p.EMailAddress = email;
                        p.UserName = name;
                        p.SecretQuestion = question;
                        p.SecretAnswer = answer;
                        var result = Envir.Main.HTTPNewAccount(p, ip);
                        WriteResponse(response, result.ToString());
                        break;                               
                    case "/addnamelist":
                        id = request.QueryString["id"];
                        var fileName = request.QueryString["fileName"];
                        AddNameList(id, fileName);
                        WriteResponse(response, "true");
                        break;              
                    case "/broadcast":
                        var msg = request.QueryString["msg"];
                        if (msg.Length < 5)
                        {
                            WriteResponse(response, "short");
                            return;
                        }
                        Envir.Main.Broadcast(new S.Chat
                        {
                            Message = msg.Trim(),
                            Type = ChatType.Shout2
                        });
                        WriteResponse(response, "true");
                        break;
                    case "/backup/status":
                        if (_backupService == null)
                        {
                            WriteStatusResponse(response, HttpStatusCode.ServiceUnavailable, "sqlite backup disabled");
                            break;
                        }
                        WriteJsonResponse(response, HttpStatusCode.OK, _backupService.GetStatus());
                        break;
                    case "/operations/status":
                        WriteJsonResponse(response, HttpStatusCode.OK, _operationsMonitor.CaptureStatus());
                        break;
                    case "/operations/kill-switches":
                        if (_killSwitches == null)
                        {
                            WriteStatusResponse(response, HttpStatusCode.ServiceUnavailable, "kill switches unavailable");
                            break;
                        }
                        WriteJsonResponse(response, HttpStatusCode.OK, _killSwitches.GetSnapshot());
                        break;
                    default:
                        WriteResponse(response, "error");
                        break;
                }
            }
            catch (Exception error)
            {
                try
                {
                    MessageQueue.Instance.Enqueue("Http GET请求处理异常: " + error);
                }
                catch
                {
                }

                AppendRuntimeLog("Http GET request error: " + error.Message);

                if (!HasResponseStarted(response))
                    WriteStatusResponse(response, HttpStatusCode.InternalServerError, "request error: " + error.Message);
            }
        }

        private bool IsTrustedClient(HttpListenerRequest request, HttpListenerResponse response)
        {
            if (request.RemoteEndPoint == null)
            {
                Audit(request, new AdminAuthorizationResult(
                    AdminAuthorizationStatus.Unauthorized, AdminRole.None, "source-check"), "unknown");
                WriteStatusResponse(response, HttpStatusCode.Forbidden, "forbidden");
                return false;
            }

            var clientIp = request.RemoteEndPoint.Address.ToString();
            if (clientIp == Settings.HTTPTrustedIPAddress)
                return true;

            Audit(request, new AdminAuthorizationResult(
                AdminAuthorizationStatus.Unauthorized, AdminRole.None, "source-check"), clientIp);
            WriteStatusResponse(response, HttpStatusCode.Forbidden, "forbidden");
            return false;
        }

        private bool AuthorizeAdminRequest(HttpListenerRequest request, HttpListenerResponse response, string path)
        {
            var authorization = AdminSecurityPolicy.Authorize(
                request.Headers["Authorization"],
                path,
                _administratorToken,
                _operatorToken);
            Audit(request, authorization, request.RemoteEndPoint?.Address.ToString());
            if (authorization.Status == AdminAuthorizationStatus.Authorized)
                return true;

            if (authorization.Status == AdminAuthorizationStatus.Unconfigured)
            {
                WriteStatusResponse(response, HttpStatusCode.ServiceUnavailable, "admin credentials not configured");
                return false;
            }

            if (authorization.Status == AdminAuthorizationStatus.Unauthorized)
                response.AddHeader("WWW-Authenticate", "Bearer");
            WriteStatusResponse(response,
                authorization.Status == AdminAuthorizationStatus.Forbidden
                    ? HttpStatusCode.Forbidden
                    : HttpStatusCode.Unauthorized,
                authorization.Status == AdminAuthorizationStatus.Forbidden ? "forbidden" : "unauthorized");
            return false;
        }

        private static void Audit(HttpListenerRequest request, AdminAuthorizationResult authorization, string clientIp)
        {
            string line = AdminSecurityPolicy.BuildAuditLine(
                DateTimeOffset.UtcNow, clientIp, request?.HttpMethod, authorization);
            Logger.GetLogger(LogType.Server).Warn(line);
        }

        void AddNameList(string playerName, string fileName)
        {
            if (string.IsNullOrWhiteSpace(playerName)) return;
            if (string.IsNullOrWhiteSpace(fileName)) return;

            Envir.Main.AddNameToNameList(fileName, playerName);
        }    

        public override void OnPostRequest(HttpListenerRequest request, HttpListenerResponse response)
        {
            string path = request.Url?.AbsolutePath ?? "/";
            if (!path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
            {
                if (!IsTrustedClient(request, response) || !AuthorizeAdminRequest(request, response, path))
                    return;
            }

            if (path.Equals("/backup/run", StringComparison.OrdinalIgnoreCase))
            {
                if (_backupService == null)
                {
                    WriteStatusResponse(response, HttpStatusCode.ServiceUnavailable, "sqlite backup disabled");
                    return;
                }

                if (!_backupService.TryQueueBackup("admin"))
                {
                    WriteJsonResponse(response, HttpStatusCode.Conflict, _backupService.GetStatus());
                    return;
                }

                WriteJsonResponse(response, HttpStatusCode.Accepted, _backupService.GetStatus());
                return;
            }
            if (path.Equals("/operations/kill-switches/set", StringComparison.OrdinalIgnoreCase))
            {
                HandleKillSwitchChange(request, response);
                return;
            }
            WriteStatusResponse(response, HttpStatusCode.MethodNotAllowed, "method not allowed");
        }

        private void HandleKillSwitchChange(HttpListenerRequest request, HttpListenerResponse response)
        {
            if (_killSwitches == null)
            {
                WriteStatusResponse(response, HttpStatusCode.ServiceUnavailable, "kill switches unavailable");
                return;
            }

            try
            {
                string body = ReadBoundedBody(request, 8 * 1024);
                var change = JsonSerializer.Deserialize<KillSwitchChangeRequest>(body, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                });
                if (change == null || !change.Enabled.HasValue ||
                    !KillSwitchService.TryParseFeature(change.Feature, out KillSwitchFeature feature))
                {
                    WriteStatusResponse(response, HttpStatusCode.BadRequest, "invalid kill switch request");
                    return;
                }

                KillSwitchSnapshot snapshot = _killSwitches.Set(
                    feature, change.Enabled.Value, change.Reason, AdminRole.Administrator.ToString());
                WriteJsonResponse(response, HttpStatusCode.OK, snapshot);
            }
            catch (Exception error) when (error is JsonException or ArgumentException)
            {
                WriteStatusResponse(response, HttpStatusCode.BadRequest, "kill switch change rejected: " + error.Message);
            }
            catch (InvalidOperationException error) when (error.Message == "request body too large")
            {
                WriteStatusResponse(response, HttpStatusCode.RequestEntityTooLarge, error.Message);
            }
            catch (Exception error) when (error is InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                WriteStatusResponse(response, HttpStatusCode.ServiceUnavailable, "kill switch change unavailable: " + error.Message);
            }
        }

        private static string ReadBoundedBody(HttpListenerRequest request, int maximumBytes)
        {
            if (request.ContentLength64 > maximumBytes)
                throw new InvalidOperationException("request body too large");

            using var memory = new MemoryStream();
            var buffer = new byte[1024];
            while (true)
            {
                int read = request.InputStream.Read(buffer, 0, buffer.Length);
                if (read <= 0) break;
                if (memory.Length + read > maximumBytes)
                    throw new InvalidOperationException("request body too large");
                memory.Write(buffer, 0, read);
            }
            return new System.Text.UTF8Encoding(false, true).GetString(memory.ToArray());
        }

        private void WriteJsonResponse(HttpListenerResponse response, HttpStatusCode statusCode, object value)
        {
            byte[] payload = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value);
            WriteStatusBytesResponse(response, statusCode, payload, "application/json; charset=UTF-8");
        }
    }

}
