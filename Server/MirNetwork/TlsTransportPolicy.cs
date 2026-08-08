using System.Net;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

namespace Server.MirNetwork;

public static class TlsTransportPolicy
{
    public const string CertificatePasswordEnvironmentVariable = "LYOCRYSTAL_TLS_CERT_PASSWORD";
    public const SslProtocols MinimumProtocols = SslProtocols.Tls12 | SslProtocols.Tls13;

    public static bool ShouldStartLegacyV1(IPAddress address, bool allowLegacyV1)
    {
        if (address == null) return false;
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any)) return false;
        if (IPAddress.IsLoopback(address)) return true;
        if (!allowLegacyV1) return false;

        byte[] bytes = address.GetAddressBytes();
        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            return bytes[0] == 10 || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
                (bytes[0] == 192 && bytes[1] == 168);

        return address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 &&
            ((bytes[0] & 0xFE) == 0xFC || (bytes[0] == 0xFE && (bytes[1] & 0xC0) == 0x80));
    }

    public static void ValidateTlsPorts(ushort legacyPort, ushort tlsPort)
    {
        if (tlsPort == 0)
            throw new InvalidOperationException("TLS端口未配置");
        if (tlsPort == legacyPort)
            throw new InvalidOperationException("TLS端口不能与V1端口相同");
    }

    public static X509Certificate2 LoadServerCertificate(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException("TLS证书路径未配置");

        var password = Environment.GetEnvironmentVariable(CertificatePasswordEnvironmentVariable) ?? string.Empty;
        var certificate = X509CertificateLoader.LoadPkcs12FromFile(path, password, X509KeyStorageFlags.UserKeySet);
        try
        {
            ValidateServerCertificate(certificate);
            return certificate;
        }
        catch
        {
            certificate.Dispose();
            throw;
        }
    }

    public static SslStream AuthenticateServer(Stream innerStream, X509Certificate2 certificate)
    {
        if (innerStream == null) throw new ArgumentNullException(nameof(innerStream));
        ValidateServerCertificate(certificate);

        var ssl = new SslStream(innerStream, leaveInnerStreamOpen: false);
        try
        {
            ssl.AuthenticateAsServer(certificate, clientCertificateRequired: false, MinimumProtocols, checkCertificateRevocation: false);
            return ssl;
        }
        catch
        {
            ssl.Dispose();
            throw;
        }
    }

    public static async Task<SslStream> AuthenticateServerAsync(Stream innerStream, X509Certificate2 certificate, CancellationToken cancellationToken)
    {
        if (innerStream == null) throw new ArgumentNullException(nameof(innerStream));
        ValidateServerCertificate(certificate);

        var ssl = new SslStream(innerStream, leaveInnerStreamOpen: false);
        try
        {
            await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
            {
                ServerCertificate = certificate,
                ClientCertificateRequired = false,
                EnabledSslProtocols = MinimumProtocols,
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
            }, cancellationToken).ConfigureAwait(false);
            return ssl;
        }
        catch
        {
            ssl.Dispose();
            throw;
        }
    }

    private static void ValidateServerCertificate(X509Certificate2 certificate)
    {
        if (certificate == null || !certificate.HasPrivateKey)
            throw new InvalidOperationException("TLS证书缺少私钥");

        var now = DateTime.UtcNow;
        if (certificate.NotBefore.ToUniversalTime() > now || certificate.NotAfter.ToUniversalTime() <= now)
            throw new InvalidOperationException("TLS证书已过期或尚未生效");

        using var rsa = certificate.GetRSAPrivateKey();
        using var ecdsa = certificate.GetECDsaPrivateKey();
        if (rsa == null && ecdsa == null)
            throw new InvalidOperationException("TLS证书私钥算法不受支持");
        if (rsa != null && rsa.KeySize < 2048)
            throw new InvalidOperationException("TLS证书RSA密钥长度不足");
        if (ecdsa != null && ecdsa.KeySize < 256)
            throw new InvalidOperationException("TLS证书椭圆曲线密钥长度不足");
    }
}
