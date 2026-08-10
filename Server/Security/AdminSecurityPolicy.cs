using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace Server.Security;

internal enum AdminRole
{
    None,
    Operator,
    Administrator,
}

internal enum AdminAuthorizationStatus
{
    Authorized,
    Unauthorized,
    Forbidden,
    Unconfigured,
}

internal readonly record struct AdminAuthorizationResult(
    AdminAuthorizationStatus Status,
    AdminRole Role,
    string Action);

internal static class AdminSecurityPolicy
{
    private const int MaxBearerTokenLength = 512;

    internal static void ValidateListener(string prefix)
    {
        if (!Uri.TryCreate(prefix, UriKind.Absolute, out Uri uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new InvalidOperationException("管理 HTTP 监听地址必须是有效的 http/https 绝对地址");

        if (uri.IsLoopback)
            return;

        if (!IPAddress.TryParse(uri.Host, out IPAddress address) || !IsPrivateAddress(address))
            throw new InvalidOperationException("管理 HTTP 只允许监听回环或明确的内网 IP 地址");

        if (uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("非回环管理端点必须使用 HTTPS，禁止在内网上明文传输管理凭据");
    }

    internal static AdminAuthorizationResult Authorize(
        string authorizationHeader,
        string absolutePath,
        string administratorToken,
        string operatorToken)
    {
        string action = MapAction(absolutePath);
        if (string.IsNullOrWhiteSpace(administratorToken) && string.IsNullOrWhiteSpace(operatorToken))
            return new AdminAuthorizationResult(AdminAuthorizationStatus.Unconfigured, AdminRole.None, action);
        if (!string.IsNullOrWhiteSpace(administratorToken) &&
            !string.IsNullOrWhiteSpace(operatorToken) &&
            FixedTimeEquals(administratorToken, operatorToken))
            return new AdminAuthorizationResult(AdminAuthorizationStatus.Unconfigured, AdminRole.None, action);

        if (!TryReadBearerToken(authorizationHeader, out string suppliedToken))
            return new AdminAuthorizationResult(AdminAuthorizationStatus.Unauthorized, AdminRole.None, action);

        AdminRole role = FixedTimeEquals(suppliedToken, administratorToken) ? AdminRole.Administrator :
            FixedTimeEquals(suppliedToken, operatorToken) ? AdminRole.Operator : AdminRole.None;
        if (role == AdminRole.None)
            return new AdminAuthorizationResult(AdminAuthorizationStatus.Unauthorized, role, action);

        bool allowed = role == AdminRole.Administrator ||
            role == AdminRole.Operator && (action == "status" || action == "broadcast" ||
                                           action == "backup-status" || action == "operations-status");
        return new AdminAuthorizationResult(
            allowed ? AdminAuthorizationStatus.Authorized : AdminAuthorizationStatus.Forbidden,
            role,
            action);
    }

    internal static string BuildAuditLine(
        DateTimeOffset timestamp,
        string clientAddress,
        string method,
        AdminAuthorizationResult authorization) =>
        $"ADMIN_AUDIT time={timestamp:O} client_ref={HashReference(clientAddress)} method={Safe(method)} " +
        $"action={authorization.Action} principal={authorization.Role} result={authorization.Status}";

    private static string MapAction(string absolutePath) => (absolutePath ?? string.Empty).ToLowerInvariant() switch
    {
        "/" => "status",
        "/broadcast" => "broadcast",
        "/newaccount" => "new-account",
        "/addnamelist" => "add-name-list",
        "/backup/status" => "backup-status",
        "/backup/run" => "backup-run",
        "/operations/status" => "operations-status",
        _ => "unknown",
    };

    private static bool TryReadBearerToken(string header, out string token)
    {
        token = null;
        const string prefix = "Bearer ";
        if (string.IsNullOrWhiteSpace(header) || header.Length > prefix.Length + MaxBearerTokenLength ||
            !header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        token = header[prefix.Length..];
        return token.Length > 0 && token.Length <= MaxBearerTokenLength && token.Trim() == token;
    }

    private static bool FixedTimeEquals(string supplied, string expected)
    {
        if (string.IsNullOrEmpty(supplied) || string.IsNullOrEmpty(expected))
            return false;
        byte[] suppliedHash = SHA256.HashData(Encoding.UTF8.GetBytes(supplied));
        byte[] expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        return CryptographicOperations.FixedTimeEquals(suppliedHash, expectedHash);
    }

    private static bool IsPrivateAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address)) return true;
        byte[] bytes = address.GetAddressBytes();
        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            return bytes[0] == 10 || bytes[0] == 172 && bytes[1] is >= 16 and <= 31 ||
                   bytes[0] == 192 && bytes[1] == 168;
        return bytes.Length == 16 && ((bytes[0] & 0xFE) == 0xFC || bytes[0] == 0xFE && (bytes[1] & 0xC0) == 0x80);
    }

    private static string Safe(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "unknown";
        string safe = value.Replace('\r', '_').Replace('\n', '_').Replace(' ', '_');
        return safe.Length <= 96 ? safe : safe[..96];
    }

    private static string HashReference(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "unknown";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..16];
    }
}
