using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Net.Sockets;

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

    public static string ClassifyFailure(Exception error)
    {
        if (error == null) return "未知";
        string details = string.Join(" | ", Enumerate(error).Select(item => $"{item.GetType().Name}:{item.Message}"));
        string normalized = details.ToLowerInvariant();
        string category = error is SocketException ? "网络端点" :
            error is OperationCanceledException ? "握手超时" :
            error is AuthenticationException &&
                (normalized.Contains("revocation") || normalized.Contains("revoked") ||
                 normalized.Contains("吊销") || normalized.Contains("撤销")) ? "在线吊销检查" :
            error is AuthenticationException ? "证书链或域名" : "传输异常";
        string singleLine = details.Replace('\r', ' ').Replace('\n', ' ');
        if (singleLine.Length > 320) singleLine = singleLine[..320];
        return $"{category};{singleLine}";
    }

    private static IEnumerable<Exception> Enumerate(Exception error)
    {
        for (Exception current = error; current != null; current = current.InnerException)
            yield return current;
    }

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
