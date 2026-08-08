using System;
using System.Net;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace Shared.Security;

public static class TlsClientPolicy
{
    public const SslProtocols MinimumProtocols = SslProtocols.Tls12 | SslProtocols.Tls13;

    public static SslClientAuthenticationOptions CreateOptions(string targetHost)
    {
        if (string.IsNullOrWhiteSpace(targetHost))
            throw new ArgumentException("TLS服务器名称不能为空", nameof(targetHost));

        return new SslClientAuthenticationOptions
        {
            TargetHost = targetHost.Trim(),
            EnabledSslProtocols = MinimumProtocols,
            CertificateRevocationCheckMode = X509RevocationMode.Online,
        };
    }

    public static string FormatFailure(Exception error, string host, int port) =>
        $"TLS连接失败：{error.GetType().Name} {host}:{port}；请检查系统时间、TlsServerName 是否匹配证书 SAN，以及证书链和有效期。";

    public static bool IsLoopbackHost(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
            return false;

        host = host.Trim();
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
            return true;

        return IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address);
    }
}
