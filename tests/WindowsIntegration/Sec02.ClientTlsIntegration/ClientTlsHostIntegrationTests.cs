extern alias PCClient;

using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Xunit;

namespace Sec02.ClientTlsIntegration.Windows;

public sealed class ClientTlsHostIntegrationTests
{
    [Fact]
    public async Task PC真实Network拒绝不受信TLS且不降级V1()
    {
        using X509Certificate2 certificate = CreateCertificate();
        Assembly pc = typeof(PCClient::Client.Security.LoginSettingsIntegration).Assembly;
        await AssertClientAsync(pc.GetType("Client.Settings", true)!, pc, certificate);
    }

    private static async Task AssertClientAsync(Type settings, Assembly assembly, X509Certificate2 certificate)
    {
        using var tlsListener = new TcpListener(IPAddress.Loopback, 0);
        using var legacyListener = new TcpListener(IPAddress.Loopback, 0);
        tlsListener.Start();
        legacyListener.Start();
        Type network = assembly.GetType(settings.Namespace!.StartsWith("Client")
            ? "Client.MirNetwork.Network" : "MonoShare.MirNetwork.Network", true)!;
        MethodInfo disconnect = network.GetMethod("Disconnect", BindingFlags.Static | BindingFlags.Public)!;
        var original = (Get(settings, "IPAddress"), Get(settings, "UseTlsV2"), Get(settings, "TlsPort"),
            Get(settings, "Port"), Get(settings, "TlsServerName"));
        try
        {
            Set(settings, "IPAddress", "127.0.0.1");
            Set(settings, "UseTlsV2", true);
            Set(settings, "TlsPort", ((IPEndPoint)tlsListener.LocalEndpoint).Port);
            Set(settings, "Port", ((IPEndPoint)legacyListener.LocalEndpoint).Port);
            Set(settings, "TlsServerName", "localhost");
            disconnect.Invoke(null, null);
            Task<TcpClient> accepted = tlsListener.AcceptTcpClientAsync();
            network.GetMethod("Connect", BindingFlags.Static | BindingFlags.Public)!.Invoke(null, null);
            using TcpClient server = await accepted.WaitAsync(TimeSpan.FromSeconds(5));
            using var ssl = new SslStream(server.GetStream(), false);
            try
            {
                await ssl.AuthenticateAsServerAsync(certificate, false, SslProtocols.Tls12 | SslProtocols.Tls13, false);
            }
            catch (AuthenticationException)
            {
                // TLS 版本不同可能在服务端或客户端一侧先观察到拒绝。
            }

            FieldInfo clientField = network.GetField("_client", BindingFlags.Static | BindingFlags.NonPublic)!;
            await WaitUntilAsync(() => clientField.GetValue(null) == null, TimeSpan.FromSeconds(5));
            Assert.False(legacyListener.Pending());
        }
        finally
        {
            disconnect.Invoke(null, null);
            Set(settings, "IPAddress", original.Item1!);
            Set(settings, "UseTlsV2", original.Item2!);
            Set(settings, "TlsPort", original.Item3!);
            Set(settings, "Port", original.Item4!);
            Set(settings, "TlsServerName", original.Item5!);
        }
    }

    private static void Set(Type type, string name, object value) =>
        type.GetField(name, BindingFlags.Static | BindingFlags.Public)!.SetValue(null, value);

    private static object? Get(Type type, string name) =>
        type.GetField(name, BindingFlags.Static | BindingFlags.Public)!.GetValue(null);

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        using var cancellation = new CancellationTokenSource(timeout);
        while (!condition()) await Task.Delay(20, cancellation.Token);
    }

    private static X509Certificate2 CreateCertificate()
    {
        using RSA rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("localhost");
        request.CertificateExtensions.Add(san.Build());
        using X509Certificate2 ephemeral = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        return X509CertificateLoader.LoadPkcs12(ephemeral.Export(X509ContentType.Pkcs12), null, X509KeyStorageFlags.UserKeySet);
    }
}
