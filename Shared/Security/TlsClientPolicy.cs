using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Net.Sockets;

namespace Shared.Security;

public static class TlsClientPolicy
{
    public const SslProtocols MinimumProtocols = SslProtocols.Tls12 | SslProtocols.Tls13;
    public const string SpkiSha256Prefix = "sha256/";
    private const int MaxConfiguredPins = 4;
    private const int MaxPinsTextLength = 1024;

    public static SslClientAuthenticationOptions CreateOptions(string targetHost, string spkiSha256Pins = null)
    {
        if (string.IsNullOrWhiteSpace(targetHost))
            throw new ArgumentException("TLS服务器名称不能为空", nameof(targetHost));

        byte[][] pins = ParseSpkiSha256Pins(spkiSha256Pins);

        return new SslClientAuthenticationOptions
        {
            TargetHost = targetHost.Trim(),
            EnabledSslProtocols = MinimumProtocols,
            CertificateRevocationCheckMode = X509RevocationMode.Online,
            RemoteCertificateValidationCallback = pins.Length == 0 ? null :
                (_, certificate, _, errors) =>
                    errors == SslPolicyErrors.None && MatchesSpkiSha256Pin(certificate, pins),
        };
    }

    public static string ComputeSpkiSha256Pin(X509Certificate2 certificate)
    {
        if (certificate == null) throw new ArgumentNullException(nameof(certificate));
        return SpkiSha256Prefix + Convert.ToBase64String(
            SHA256.HashData(certificate.PublicKey.ExportSubjectPublicKeyInfo()));
    }

    public static string FormatFailure(Exception error, string host, int port) =>
        $"TLS连接失败：{error.GetType().Name} {host}:{port}；请检查系统时间、TlsServerName 是否匹配证书 SAN、证书链和有效期，以及 TlsSpkiSha256Pins 固定值。";

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

    private static byte[][] ParseSpkiSha256Pins(string pinsText)
    {
        if (string.IsNullOrWhiteSpace(pinsText)) return Array.Empty<byte[]>();
        if (pinsText.Length > MaxPinsTextLength)
            throw new ArgumentException("TLS证书固定值配置过长", nameof(pinsText));

        string[] values = pinsText.Split(new[] { ';', ',', '\r', '\n' },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (values.Length == 0 || values.Length > MaxConfiguredPins)
            throw new ArgumentException($"TLS证书固定值必须配置 1 到 {MaxConfiguredPins} 项", nameof(pinsText));

        var pins = new List<byte[]>(values.Length);
        foreach (string value in values)
        {
            if (!value.StartsWith(SpkiSha256Prefix, StringComparison.Ordinal))
                throw new ArgumentException("TLS证书固定值必须使用 sha256/<Base64> 格式", nameof(pinsText));

            byte[] pin;
            try
            {
                pin = Convert.FromBase64String(value[SpkiSha256Prefix.Length..]);
            }
            catch (FormatException error)
            {
                throw new ArgumentException("TLS证书固定值不是有效 Base64", nameof(pinsText), error);
            }

            if (pin.Length != SHA256.HashSizeInBytes)
                throw new ArgumentException("TLS证书固定值必须是 32 字节 SHA-256 摘要", nameof(pinsText));
            if (!pins.Any(existing => CryptographicOperations.FixedTimeEquals(existing, pin)))
                pins.Add(pin);
        }

        return pins.ToArray();
    }

    private static bool MatchesSpkiSha256Pin(X509Certificate certificate, IReadOnlyList<byte[]> pins)
    {
        if (certificate == null) return false;
        X509Certificate2 certificate2 = certificate as X509Certificate2;
        bool dispose = certificate2 == null;
        try
        {
            certificate2 ??= new X509Certificate2(certificate);
            byte[] actual = SHA256.HashData(certificate2.PublicKey.ExportSubjectPublicKeyInfo());
            return pins.Any(pin => CryptographicOperations.FixedTimeEquals(pin, actual));
        }
        catch (CryptographicException)
        {
            return false;
        }
        finally
        {
            if (dispose) certificate2?.Dispose();
        }
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
