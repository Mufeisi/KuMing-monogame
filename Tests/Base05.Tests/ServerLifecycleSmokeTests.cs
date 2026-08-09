using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Server;
using Server.MirEnvir;
using Server.MirNetwork;
using Server.Security;
using Shared.Security;
using Xunit;

namespace Base05.Tests;

[Collection("TLS环境")]
public sealed class ServerLifecycleSmokeTests : IDisposable
{
    private readonly string _secretRoot = Path.Combine(Path.GetTempPath(), "LyoCrystalLifecycleSecrets-" + Guid.NewGuid().ToString("N"));
    private readonly IDisposable _secretScope;

    public ServerLifecycleSmokeTests()
    {
        _secretScope = ProtectedSecretStore.UseTestRoot(_secretRoot);
    }

    public void Dispose()
    {
        _secretScope.Dispose();
        if (Directory.Exists(_secretRoot)) Directory.Delete(_secretRoot, true);
    }
    [Fact]
    public void Minimal_server_start_stop_is_isolated_and_repeatable()
    {
        var envir = new Envir();
        var options = new EnvirStartOptions
        {
            EnforceProductionSecurity = false,
            LoadResources = false,
            BindNetwork = false,
            StartScripts = false,
            StartHttp = false,
            SaveOnStop = false,
            Multithreaded = false,
        };

        envir.Start(options);
        try
        {
            var startupCompleted = SpinWait.SpinUntil(
                () => envir.StartState is EnvirStartState.Ready or EnvirStartState.Failed,
                TimeSpan.FromSeconds(2));

            Assert.True(startupCompleted, "服务器启动未在有界时间内完成。");
            Assert.Equal(EnvirStartState.Ready, envir.StartState);
            Assert.Null(envir.StartFailure);
            Assert.True(envir.Running);
            Assert.False(envir.IsNetworkBound);
        }
        finally
        {
            envir.Stop();
        }

        Assert.False(envir.Running);
        Assert.Equal(EnvirStartState.Stopped, envir.StartState);

        envir.Stop();
        Assert.False(envir.Running);
    }

    [Fact]
    public void 无游戏监听器启动失败后可重试且不进入Ready()
    {
        string oldAddress = Settings.IPAddress;
        bool oldTls = Settings.TlsEnabled;
        bool oldLegacy = Settings.AllowLegacyV1;
        var envir = new Envir();
        var failOptions = new EnvirStartOptions
        {
            EnforceProductionSecurity = false,
            LoadResources = false,
            BindNetwork = true,
            StartScripts = false,
            StartHttp = false,
            SaveOnStop = false,
            Multithreaded = false,
        };
        try
        {
            Settings.IPAddress = "203.0.113.10";
            Settings.TlsEnabled = false;
            Settings.AllowLegacyV1 = true;
            envir.Start(failOptions);
            Assert.True(SpinWait.SpinUntil(() => envir.StartState == EnvirStartState.Failed, TimeSpan.FromSeconds(2)));
            Assert.False(envir.Running);
            Assert.Contains("没有可用的游戏监听器", envir.StartFailure?.Message);

            envir.Stop();
            envir.Start(new EnvirStartOptions
            {
                EnforceProductionSecurity = false,
                LoadResources = false,
                BindNetwork = false,
                StartScripts = false,
                StartHttp = false,
                SaveOnStop = false,
                Multithreaded = false,
            });
            Assert.True(SpinWait.SpinUntil(() => envir.StartState == EnvirStartState.Ready, TimeSpan.FromSeconds(2)));
            Assert.True(envir.Running);
        }
        finally
        {
            envir.Stop();
            Settings.IPAddress = oldAddress;
            Settings.TlsEnabled = oldTls;
            Settings.AllowLegacyV1 = oldLegacy;
        }
    }

    [Fact]
    public void 真实Server错误PFX密码失败后修正可重启()
    {
        using var scope = new ServerNetworkScope();
        scope.SetCertificatePassword("wrong-password");
        scope.Start();
        Assert.True(SpinWait.SpinUntil(() => scope.ServerEnvironment.StartState == EnvirStartState.Failed, TimeSpan.FromSeconds(8)), scope.ServerEnvironment.StartFailure?.ToString());
        Assert.False(scope.ServerEnvironment.Running);

        scope.Stop();
        scope.SetCertificatePassword(scope.CertificatePassword);
        scope.Start();
        Assert.True(SpinWait.SpinUntil(() => scope.ServerEnvironment.StartState == EnvirStartState.Ready, TimeSpan.FromSeconds(8)));
        Assert.True(scope.ServerEnvironment.Running);
        Assert.True(scope.ServerEnvironment.IsNetworkBound);
    }

    [Fact]
    public async Task 真实ServerTLS路径完成KeepAlive()
    {
        using var scope = new ServerNetworkScope();
        scope.SetCertificatePassword(scope.CertificatePassword);
        scope.Start();
        Assert.True(SpinWait.SpinUntil(() => scope.ServerEnvironment.StartState == EnvirStartState.Ready, TimeSpan.FromSeconds(8)));
        Assert.True(scope.GetListener("_tlsListener")?.Server.IsBound == true);
        Assert.True(await SendKeepAliveAndObserveAsync(scope.ServerEnvironment, scope.TlsPort, useTls: true, scope.Certificate));
    }

    [Fact]
    public async Task 真实Server停止重启取消旧TLS握手代次()
    {
        using var scope = new ServerNetworkScope();
        scope.SetCertificatePassword(scope.CertificatePassword);
        scope.Start();
        Assert.True(SpinWait.SpinUntil(() => scope.ServerEnvironment.StartState == EnvirStartState.Ready, TimeSpan.FromSeconds(8)));
        using var slowClient = new TcpClient();
        await slowClient.ConnectAsync(IPAddress.Loopback, scope.TlsPort);
        scope.Stop();
        scope.Start();
        Assert.True(SpinWait.SpinUntil(() => scope.ServerEnvironment.StartState == EnvirStartState.Ready, TimeSpan.FromSeconds(8)));
        Assert.True(await SendKeepAliveAndObserveAsync(scope.ServerEnvironment, scope.TlsPort, useTls: true, scope.Certificate));
    }

    [Fact]
    public async Task 真实Server回环V1路径完成KeepAlive()
    {
        using var scope = new ServerNetworkScope(tlsEnabled: false);
        scope.Start();
        Assert.True(SpinWait.SpinUntil(() => scope.ServerEnvironment.StartState == EnvirStartState.Ready, TimeSpan.FromSeconds(8)));
        Assert.True(scope.GetListener("_listener")?.Server.IsBound == true);
        Assert.True(await SendKeepAliveAndObserveAsync(scope.ServerEnvironment, scope.LegacyPort, useTls: false, scope.Certificate));
    }

    [Fact]
    public void 真实Server端口占用失败释放后可重启()
    {
        using var scope = new ServerNetworkScope();
        scope.SetCertificatePassword(scope.CertificatePassword);
        using var occupied = new TcpListener(IPAddress.Loopback, scope.TlsPort);
        occupied.Start();

        scope.Start();
        Assert.True(SpinWait.SpinUntil(() => scope.ServerEnvironment.StartState == EnvirStartState.Failed, TimeSpan.FromSeconds(8)), scope.ServerEnvironment.StartFailure?.ToString());
        Assert.False(scope.ServerEnvironment.Running);
        scope.Stop();

        occupied.Stop();
        scope.Start();
        Assert.True(SpinWait.SpinUntil(() => scope.ServerEnvironment.StartState == EnvirStartState.Ready, TimeSpan.FromSeconds(8)));
        Assert.True(scope.ServerEnvironment.Running);
    }

    [Fact]
    public async Task 真实ServerMaxUser与MaxIP准入在同一临界区生效()
    {
        using var userScope = new ServerNetworkScope(maxUser: 1, maxIp: 5, ipBlockSeconds: 0);
        Envir.IPBlocks.Clear();
        userScope.SetCertificatePassword(userScope.CertificatePassword);
        userScope.Start();
        Assert.True(SpinWait.SpinUntil(() => userScope.ServerEnvironment.StartState == EnvirStartState.Ready, TimeSpan.FromSeconds(8)), userScope.ServerEnvironment.StartFailure?.ToString());
        var first = await userScope.ConnectTlsClientAsync();
        using var firstClient = first.Client;
        using var firstSsl = first.Ssl;
        Assert.True(SpinWait.SpinUntil(() => userScope.ServerEnvironment.Connections.Count(c => c.Connected) == 1, TimeSpan.FromSeconds(4)));
        var second = await userScope.ConnectTlsClientAsync();
        using var secondClient = second.Client;
        using var secondSsl = second.Ssl;
        Assert.True(SpinWait.SpinUntil(() => userScope.ServerEnvironment.Connections.Count(c => c.Connected) == 1, TimeSpan.FromSeconds(2)));
        userScope.Stop();

        using var ipScope = new ServerNetworkScope(maxUser: 5, maxIp: 1, ipBlockSeconds: 0);
        Envir.IPBlocks.Clear();
        ipScope.SetCertificatePassword(ipScope.CertificatePassword);
        ipScope.Start();
        Assert.True(SpinWait.SpinUntil(() => ipScope.ServerEnvironment.StartState == EnvirStartState.Ready, TimeSpan.FromSeconds(8)), ipScope.ServerEnvironment.StartFailure?.ToString());
        var ipFirst = await ipScope.ConnectTlsClientAsync();
        using var ipFirstClient = ipFirst.Client;
        using var ipFirstSsl = ipFirst.Ssl;
        Assert.True(SpinWait.SpinUntil(() => ipScope.ServerEnvironment.Connections.Count(c => c.Connected) == 1, TimeSpan.FromSeconds(4)));
        var ipSecond = await ipScope.ConnectTlsClientAsync();
        using var ipSecondClient = ipSecond.Client;
        using var ipSecondSsl = ipSecond.Ssl;
        Assert.True(SpinWait.SpinUntil(() => ipScope.ServerEnvironment.Connections.Count(c => c.Connected) == 1, TimeSpan.FromSeconds(2)));
    }

    private sealed class ServerNetworkScope : IDisposable
    {
        public const string DefaultCertificatePassword = "c3-test-password";
        public readonly string Directory;
        public readonly string CertificatePath;
        public readonly string CertificatePassword = DefaultCertificatePassword;
        public readonly int LegacyPort;
        public readonly int TlsPort;
        public readonly X509Certificate2 Certificate;
        public readonly Envir ServerEnvironment;

        private readonly string _oldAddress;
        private readonly ushort _oldPort;
        private readonly ushort _oldTlsPort;
        private readonly bool _oldTls;
        private readonly bool _oldLegacy;
        private readonly string _oldCertificatePath;
        private readonly string _oldProvider;
        private readonly string _oldSqlitePath;
        private readonly bool _oldAutoApply;
        private readonly bool _oldAutoImport;
        private readonly bool _oldPacketDirection;
        private readonly ushort _oldMaxUser;
        private readonly ushort _oldMaxIP;
        private readonly int _oldIPBlockSeconds;

        public ServerNetworkScope(bool tlsEnabled = true, ushort maxUser = 500, ushort maxIp = 5, int ipBlockSeconds = 5)
        {
            Directory = CreateTempDirectory();
            CertificatePath = Path.Combine(Directory, "server.pfx");
            LegacyPort = GetFreePort();
            TlsPort = GetFreePort();
            if (LegacyPort == TlsPort) TlsPort = GetFreePort();
            Certificate = CreateCertificate();
            File.WriteAllBytes(CertificatePath, Certificate.Export(X509ContentType.Pfx, CertificatePassword));

            _oldAddress = Settings.IPAddress;
            _oldPort = Settings.Port;
            _oldTlsPort = Settings.TlsPort;
            _oldTls = Settings.TlsEnabled;
            _oldLegacy = Settings.AllowLegacyV1;
            _oldCertificatePath = Settings.TlsCertificatePath;
            _oldProvider = Settings.DatabaseProvider;
            _oldSqlitePath = Settings.SqlitePath;
            _oldAutoApply = Settings.AutoApplySchemaOnStartup;
            _oldAutoImport = Settings.AutoImportLegacyOnEmpty;
            _oldPacketDirection = Packet.IsServer;
            _oldMaxUser = Settings.MaxUser;
            _oldMaxIP = Settings.MaxIP;
            _oldIPBlockSeconds = Settings.IPBlockSeconds;

            Settings.IPAddress = "127.0.0.1";
            Settings.Port = (ushort)LegacyPort;
            Settings.TlsPort = (ushort)TlsPort;
            Settings.TlsEnabled = tlsEnabled;
            Settings.AllowLegacyV1 = true;
            Settings.TlsCertificatePath = CertificatePath;
            Settings.DatabaseProvider = "Sqlite";
            Settings.SqlitePath = Path.Combine(Directory, "server.db");
            Settings.AutoApplySchemaOnStartup = true;
            Settings.AutoImportLegacyOnEmpty = false;
            Settings.MaxUser = maxUser;
            Settings.MaxIP = maxIp;
            Settings.IPBlockSeconds = ipBlockSeconds;

            ServerEnvironment = new Envir();
            typeof(Envir).GetField("StatusPortEnabled", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(ServerEnvironment, false);
        }

        public EnvirStartOptions StartOptions => new EnvirStartOptions
        {
            EnforceProductionSecurity = false,
            LoadResources = false,
            BindNetwork = true,
            StartScripts = false,
            StartHttp = false,
            SaveOnStop = false,
            Multithreaded = false,
        };

        public void SetCertificatePassword(string password)
        {
            ProtectedSecretStore.Write(ProtectedSecretStore.TlsCertificatePassword, password);
        }

        public void Start() => ServerEnvironment.Start(StartOptions);

        public async Task<(TcpClient Client, SslStream Ssl)> ConnectTlsClientAsync()
        {
            var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, TlsPort);
            var ssl = new SslStream(client.GetStream(), leaveInnerStreamOpen: false);
            var options = TlsClientPolicy.CreateOptions("localhost");
            options.CertificateChainPolicy = new X509ChainPolicy
            {
                TrustMode = X509ChainTrustMode.CustomRootTrust,
                RevocationMode = X509RevocationMode.NoCheck,
            };
            options.CertificateChainPolicy.CustomTrustStore.Add(Certificate);
            await ssl.AuthenticateAsClientAsync(options);
            return (client, ssl);
        }

        public void Stop() => ServerEnvironment.Stop();

        public TcpListener GetListener(string fieldName) =>
            (TcpListener)typeof(Envir).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(ServerEnvironment);

        public void Dispose()
        {
            ServerEnvironment.Stop();
            Packet.IsServer = _oldPacketDirection;
            Settings.IPAddress = _oldAddress;
            Settings.Port = _oldPort;
            Settings.TlsPort = _oldTlsPort;
            Settings.TlsEnabled = _oldTls;
            Settings.AllowLegacyV1 = _oldLegacy;
            Settings.TlsCertificatePath = _oldCertificatePath;
            Settings.DatabaseProvider = _oldProvider;
            Settings.SqlitePath = _oldSqlitePath;
            Settings.AutoApplySchemaOnStartup = _oldAutoApply;
            Settings.AutoImportLegacyOnEmpty = _oldAutoImport;
            Settings.MaxUser = _oldMaxUser;
            Settings.MaxIP = _oldMaxIP;
            Settings.IPBlockSeconds = _oldIPBlockSeconds;
            Certificate.Dispose();
            TryDeleteDirectory(Directory);
        }
    }

    private static async Task<bool> SendKeepAliveAndObserveAsync(Envir envir, int port, bool useTls, X509Certificate2 certificate)
    {
        TcpClient client = null;
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                client = new TcpClient();
                await client.ConnectAsync(IPAddress.Loopback, port);
                break;
            }
            catch (SocketException) when (attempt < 40)
            {
                client?.Dispose();
                client = null;
                await Task.Delay(50);
            }
        }
        using (client)
        {
            using var ssl = useTls ? new SslStream(client.GetStream(), leaveInnerStreamOpen: false) : null;
            Stream stream = client.GetStream();
            if (ssl != null)
            {
                var options = TlsClientPolicy.CreateOptions("localhost");
                options.CertificateChainPolicy = new X509ChainPolicy
                {
                    TrustMode = X509ChainTrustMode.CustomRootTrust,
                    RevocationMode = X509RevocationMode.NoCheck,
                };
                options.CertificateChainPolicy.CustomTrustStore.Add(certificate);
                await ssl.AuthenticateAsClientAsync(options);
                stream = ssl;
            }

            bool previous = Packet.IsServer;
            Packet.IsServer = true;
            try
            {
                byte[] packet = new ClientPackets.KeepAlive { Time = 42 }.GetPacketBytes().ToArray();
                await stream.WriteAsync(packet);
                await stream.FlushAsync();
                MirConnection connection = null;
                Assert.True(SpinWait.SpinUntil(() =>
                {
                    lock (envir.Connections)
                        connection = envir.Connections.FirstOrDefault(item => item.Connected);
                    return connection != null;
                }, TimeSpan.FromSeconds(4)));

                bool processed = SpinWait.SpinUntil(
                    () => connection.ReceiveQueueHighWater > 0 && connection.ReceiveQueueDepth == 0,
                    TimeSpan.FromSeconds(4));
                connection.Disconnect(0);
                return processed;
            }
            finally
            {
                Packet.IsServer = previous;
            }
        }
    }

    private static int GetFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static X509Certificate2 CreateCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("localhost");
        request.CertificateExtensions.Add(san.Build());
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "lyocrystal-c3-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            Directory.Delete(path, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"临时测试目录清理失败，保留路径：{path}（{ex.GetType().Name}）");
        }
    }
}
