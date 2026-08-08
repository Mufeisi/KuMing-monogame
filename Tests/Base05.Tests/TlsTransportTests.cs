using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Reflection;
using Server.MirEnvir;
using Server.MirNetwork;
using Shared.Security;
using Xunit;

namespace Base05.Tests;

public sealed class TlsTransportTests
{
    [Fact]
    public void 非回环默认拒绝明文监听而回环或显式开发开关允许()
    {
        Assert.False(TlsTransportPolicy.ShouldStartLegacyV1(IPAddress.Parse("0.0.0.0"), false));
        Assert.True(TlsTransportPolicy.ShouldStartLegacyV1(IPAddress.Loopback, false));
        Assert.True(TlsTransportPolicy.ShouldStartLegacyV1(IPAddress.Parse("192.0.2.10"), true));
    }

    [Fact]
    public void TLS端口必须有效且不能与V1冲突()
    {
        Assert.Throws<InvalidOperationException>(() => TlsTransportPolicy.ValidateTlsPorts(7000, 0));
        Assert.Throws<InvalidOperationException>(() => TlsTransportPolicy.ValidateTlsPorts(7000, 7000));
        TlsTransportPolicy.ValidateTlsPorts(7000, 7001);
    }

    [Fact]
    public void 客户端TLS策略要求目标主机并默认严格校验证书()
    {
        Assert.Throws<ArgumentException>(() => TlsClientPolicy.CreateOptions(string.Empty));
        var options = TlsClientPolicy.CreateOptions("localhost");
        Assert.Equal("localhost", options.TargetHost);
        Assert.Equal(TlsTransportPolicy.MinimumProtocols, options.EnabledSslProtocols);
        Assert.Null(options.RemoteCertificateValidationCallback);
        Assert.Null(options.CertificateChainPolicy);
        Assert.Null(options.ClientCertificates);
    }

    [Fact]
    public void 非回环地址禁止V1且回环允许开发连接()
    {
        Assert.True(TlsClientPolicy.IsLoopbackHost("127.0.0.1"));
        Assert.True(TlsClientPolicy.IsLoopbackHost("localhost"));
        Assert.False(TlsClientPolicy.IsLoopbackHost("192.0.2.10"));
    }

    [Fact]
    public void 停止网络同时释放V1和V2监听器()
    {
        var environment = new Envir();
        var legacyListener = new TcpListener(IPAddress.Loopback, 0);
        var tlsListener = new TcpListener(IPAddress.Loopback, 0);
        legacyListener.Start();
        tlsListener.Start();

        FieldInfo legacyField = typeof(Envir).GetField("_listener", BindingFlags.Instance | BindingFlags.NonPublic);
        FieldInfo tlsField = typeof(Envir).GetField("_tlsListener", BindingFlags.Instance | BindingFlags.NonPublic);
        MethodInfo stopMethod = typeof(Envir).GetMethod("StopNetwork", BindingFlags.Instance | BindingFlags.NonPublic);
        legacyField.SetValue(environment, legacyListener);
        tlsField.SetValue(environment, tlsListener);
        try
        {
            stopMethod.Invoke(environment, null);
            Assert.False(legacyListener.Server.IsBound);
            Assert.False(tlsListener.Server.IsBound);
        }
        finally
        {
            legacyListener.Stop();
            tlsListener.Stop();
        }
    }

    [Fact]
    public async Task 临时证书SslStream握手并完成现有Packet往返()
    {
        string directory = CreateTempDirectory();
        string path = Path.Combine(directory, "server.pfx");
        try
        {
            using var source = CreateCertificate("localhost", DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
            File.WriteAllBytes(path, source.Export(X509ContentType.Pfx));
            using var certificate = TlsTransportPolicy.LoadServerCertificate(path);
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var serverTask = Task.Run(() => ReceiveServerPacket(listener, certificate));

            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
            using var ssl = new SslStream(client.GetStream(), false);
            var options = TlsClientPolicy.CreateOptions("localhost");
            options.CertificateChainPolicy = new X509ChainPolicy
            {
                TrustMode = X509ChainTrustMode.CustomRootTrust,
                RevocationMode = X509RevocationMode.NoCheck,
            };
            options.CertificateChainPolicy.CustomTrustStore.Add(certificate);
            await ssl.AuthenticateAsClientAsync(options);

            bool previousIsServer = Packet.IsServer;
            try
            {
                Packet.IsServer = true;
                byte[] bytes = new ClientPackets.KeepAlive { Time = 1234 }.GetPacketBytes().ToArray();
                await ssl.WriteAsync(bytes);
                await ssl.FlushAsync();
                var parsed = await serverTask;
                Assert.IsType<ClientPackets.KeepAlive>(parsed);
            }
            finally
            {
                Packet.IsServer = previousIsServer;
            }
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void 无证书或过期证书拒绝启动()
    {
        Assert.Throws<InvalidOperationException>(() => TlsTransportPolicy.LoadServerCertificate(string.Empty));

        string directory = CreateTempDirectory();
        string path = Path.Combine(directory, "expired.pfx");
        try
        {
            using var expired = CreateCertificate("localhost", DateTimeOffset.UtcNow.AddDays(-2), DateTimeOffset.UtcNow.AddDays(-1));
            File.WriteAllBytes(path, expired.Export(X509ContentType.Pfx));
            Assert.Throws<InvalidOperationException>(() => TlsTransportPolicy.LoadServerCertificate(path));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void 证书密码只从运行时环境读取而不写入配置()
    {
        string directory = CreateTempDirectory();
        string path = Path.Combine(directory, "protected-server.pfx");
        const string password = "stage-a-test-password";
        string previous = Environment.GetEnvironmentVariable(TlsTransportPolicy.CertificatePasswordEnvironmentVariable);
        try
        {
            using var source = CreateCertificate("localhost", DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
            File.WriteAllBytes(path, source.Export(X509ContentType.Pfx, password));
            byte[] beforeLoad = File.ReadAllBytes(path);
            Environment.SetEnvironmentVariable(TlsTransportPolicy.CertificatePasswordEnvironmentVariable, password);
            using var certificate = TlsTransportPolicy.LoadServerCertificate(path);
            Assert.True(certificate.HasPrivateKey);
            Assert.Equal(beforeLoad, File.ReadAllBytes(path));
        }
        finally
        {
            Environment.SetEnvironmentVariable(TlsTransportPolicy.CertificatePasswordEnvironmentVariable, previous);
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task 客户端拒绝不受信任证书()
    {
        string directory = CreateTempDirectory();
        string path = Path.Combine(directory, "server.pfx");
        try
        {
            using var source = CreateCertificate("localhost", DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
            File.WriteAllBytes(path, source.Export(X509ContentType.Pfx));
            using var certificate = TlsTransportPolicy.LoadServerCertificate(path);
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var serverTask = Task.Run(() =>
            {
                try
                {
                    using var serverClient = listener.AcceptTcpClient();
                    using var ssl = TlsTransportPolicy.AuthenticateServer(serverClient.GetStream(), certificate);
                    Thread.Sleep(100);
                }
                catch
                {
                }
            });

            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
            using var sslClient = new SslStream(client.GetStream(), false);
            var error = await Record.ExceptionAsync(() => sslClient.AuthenticateAsClientAsync(
                TlsClientPolicy.CreateOptions("localhost")));
            Assert.NotNull(error);
            await serverTask;
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task 客户端拒绝错误主机名()
    {
        string directory = CreateTempDirectory();
        string path = Path.Combine(directory, "server.pfx");
        try
        {
            using var source = CreateCertificate("localhost", DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
            File.WriteAllBytes(path, source.Export(X509ContentType.Pfx));
            using var certificate = TlsTransportPolicy.LoadServerCertificate(path);
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var serverTask = Task.Run(() =>
            {
                try
                {
                    using var serverClient = listener.AcceptTcpClient();
                    using var ssl = TlsTransportPolicy.AuthenticateServer(serverClient.GetStream(), certificate);
                    Thread.Sleep(100);
                }
                catch
                {
                }
            });

            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
            using var sslClient = new SslStream(client.GetStream(), false);
            var options = TlsClientPolicy.CreateOptions("not-localhost");
            options.CertificateChainPolicy = CreateCustomRootPolicy(certificate);
            var error = await Record.ExceptionAsync(() => sslClient.AuthenticateAsClientAsync(options));
            Assert.NotNull(error);
            await serverTask;
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task 客户端拒绝过期证书()
    {
        string directory = CreateTempDirectory();
        try
        {
            using var certificate = CreateCertificate("localhost", DateTimeOffset.UtcNow.AddDays(-2), DateTimeOffset.UtcNow.AddDays(-1));
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var serverTask = Task.Run(() =>
            {
                try
                {
                    using var serverClient = listener.AcceptTcpClient();
                    using var ssl = new SslStream(serverClient.GetStream(), false);
                    ssl.AuthenticateAsServer(certificate, false, TlsTransportPolicy.MinimumProtocols, false);
                    Thread.Sleep(100);
                }
                catch
                {
                }
            });

            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
            using var sslClient = new SslStream(client.GetStream(), false);
            var options = TlsClientPolicy.CreateOptions("localhost");
            options.CertificateChainPolicy = CreateCustomRootPolicy(certificate);
            var error = await Record.ExceptionAsync(() => sslClient.AuthenticateAsClientAsync(options));
            Assert.NotNull(error);
            await serverTask;
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static X509ChainPolicy CreateCustomRootPolicy(X509Certificate2 certificate)
    {
        var policy = new X509ChainPolicy
        {
            TrustMode = X509ChainTrustMode.CustomRootTrust,
            RevocationMode = X509RevocationMode.NoCheck,
        };
        policy.CustomTrustStore.Add(certificate);
        return policy;
    }

    private static Packet ReceiveServerPacket(TcpListener listener, X509Certificate2 certificate)
    {
        using var client = listener.AcceptTcpClient();
        using var ssl = TlsTransportPolicy.AuthenticateServer(client.GetStream(), certificate);
        byte[] header = ReadExact(ssl, 4);
        ushort length = BitConverter.ToUInt16(header, 0);
        byte[] bytes = new byte[length];
        Buffer.BlockCopy(header, 0, bytes, 0, header.Length);
        Buffer.BlockCopy(ReadExact(ssl, length - 4), 0, bytes, 4, length - 4);
        return Packet.ReceivePacket(bytes, out _);
    }

    private static byte[] ReadExact(Stream stream, int length)
    {
        byte[] bytes = new byte[length];
        int offset = 0;
        while (offset < length)
        {
            int read = stream.Read(bytes, offset, length - offset);
            if (read <= 0) throw new EndOfStreamException();
            offset += read;
        }
        return bytes;
    }

    private static X509Certificate2 CreateCertificate(string dnsName, DateTimeOffset notBefore, DateTimeOffset notAfter)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest($"CN={dnsName}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        var sanBuilder = new SubjectAlternativeNameBuilder();
        sanBuilder.AddDnsName(dnsName);
        request.CertificateExtensions.Add(sanBuilder.Build());
        return request.CreateSelfSigned(notBefore, notAfter);
    }

    private static string CreateTempDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), "LyoCrystalTls-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
