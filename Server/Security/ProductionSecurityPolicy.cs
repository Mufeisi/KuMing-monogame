using System.Net;
using System.Security.Cryptography;
using System.Text;
using Server.Persistence.Sql;
using Server.Operations;

namespace Server.Security;

internal static class ProductionSecurityPolicy
{
    internal const string ImportTlsPassword = "LYOCRYSTAL_IMPORT_TLS_CERT_PASSWORD";
    internal const string ImportAdministratorToken = "LYOCRYSTAL_IMPORT_ADMIN_TOKEN";
    internal const string ImportOperatorToken = "LYOCRYSTAL_IMPORT_OPERATOR_TOKEN";
    internal const string ImportGameMasterPassword = "LYOCRYSTAL_IMPORT_GM_PASSWORD";
    internal const string ImportMySqlConnectionString = "LYOCRYSTAL_IMPORT_MYSQL_CONNECTION_STRING";
    internal const string ImportMicroCode = "LYOCRYSTAL_IMPORT_MICRO_CODE";
    internal const string ImportAiApiKey = "LYOCRYSTAL_IMPORT_AI_API_KEY";

    internal static void ValidateAndApply()
    {
        ImportTransientSecrets();
        ProductionRpoPolicy.ValidateConfiguredSaveDelay();

        string gameMasterPassword = Require(ProtectedSecretStore.GameMasterPassword, "游戏 GM 口令");
        if (gameMasterPassword == "@123456" || gameMasterPassword.Length < 12)
            throw new InvalidOperationException("正式服游戏 GM 口令不得使用默认值且至少 12 个字符");
        Settings.GMPassword = gameMasterPassword;

        if (Settings.TlsEnabled)
            _ = Require(ProtectedSecretStore.TlsCertificatePassword, "TLS 证书密码");

        if (Settings.StartHTTPService)
        {
            AdminSecurityPolicy.ValidateListener(Settings.HTTPIPAddress);
            BasicOperationsThresholds.FromSettings();
            ValidateTrustedAddress(Settings.HTTPTrustedIPAddress);
            string administrator = Require(ProtectedSecretStore.AdministratorToken, "管理端 Administrator 令牌");
            if (administrator.Length < 32)
                throw new InvalidOperationException("正式服 Administrator 令牌至少 32 个字符");
            string operatorToken = ProtectedSecretStore.Read(ProtectedSecretStore.OperatorToken);
            if (!string.IsNullOrEmpty(operatorToken))
            {
                if (operatorToken.Length < 32)
                    throw new InvalidOperationException("正式服 Operator 令牌至少 32 个字符");
                if (FixedTimeEquals(administrator, operatorToken))
                    throw new InvalidOperationException("Administrator 与 Operator 令牌不得相同");
            }
        }

        if (Settings.DatabaseProvider.Equals("MySql", StringComparison.OrdinalIgnoreCase) ||
            Settings.DatabaseProvider.Equals("MySQL", StringComparison.OrdinalIgnoreCase))
            Settings.MySqlConnectionString = Require(ProtectedSecretStore.MySqlConnectionString, "MySQL 连接字符串");
        else if (Settings.DatabaseProvider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            if (!Settings.SqliteBackupEnabled)
                throw new InvalidOperationException("正式服 SQLite 自动备份不得关闭");
            SqliteBackupOptions.FromSettings().Validate(requireOffsite: true);
        }

        if (Settings.MicroServerActive)
            Settings.MicroCode = Require(ProtectedSecretStore.MicroCode, "微端访问 Code");

        if (Settings.AiScriptsEnabled)
            Settings.AiScriptsApiKey = Require(ProtectedSecretStore.AiApiKey, "AI API Key");
    }

    private static void ImportTransientSecrets()
    {
        ProtectedSecretStore.ImportAndClearEnvironment(ProtectedSecretStore.TlsCertificatePassword, ImportTlsPassword);
        ProtectedSecretStore.ImportAndClearEnvironment(ProtectedSecretStore.AdministratorToken, ImportAdministratorToken);
        ProtectedSecretStore.ImportAndClearEnvironment(ProtectedSecretStore.OperatorToken, ImportOperatorToken);
        ProtectedSecretStore.ImportAndClearEnvironment(ProtectedSecretStore.GameMasterPassword, ImportGameMasterPassword);
        ProtectedSecretStore.ImportAndClearEnvironment(ProtectedSecretStore.MySqlConnectionString, ImportMySqlConnectionString);
        ProtectedSecretStore.ImportAndClearEnvironment(ProtectedSecretStore.MicroCode, ImportMicroCode);
        ProtectedSecretStore.ImportAndClearEnvironment(ProtectedSecretStore.AiApiKey, ImportAiApiKey);

        string[] legacyVariables = { "LYOCRYSTAL_TLS_CERT_PASSWORD", "LYOCRYSTAL_ADMIN_TOKEN", "LYOCRYSTAL_OPERATOR_TOKEN", "OPENAI_API_KEY" };
        var foundLegacy = new List<string>();
        foreach (string variable in legacyVariables)
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(variable))) continue;
            Environment.SetEnvironmentVariable(variable, null);
            foundLegacy.Add(variable);
        }
        if (foundLegacy.Count > 0)
            throw new InvalidOperationException(
                "检测到并清除了已停用的普通秘密环境变量：" + string.Join(", ", foundLegacy) +
                "；请改用一次性 LYOCRYSTAL_IMPORT_* 导入后重启。");
    }

    private static string Require(string name, string displayName)
    {
        string value = ProtectedSecretStore.Read(name);
        if (string.IsNullOrEmpty(value))
            throw new InvalidOperationException($"正式服缺少受保护秘密：{displayName}");
        return value;
    }

    private static void ValidateTrustedAddress(string value)
    {
        if (!IPAddress.TryParse(value, out IPAddress address))
            throw new InvalidOperationException("HTTPTrustedIPAddress 必须是明确的回环或内网 IP");
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address)) return;
        byte[] bytes = address.GetAddressBytes();
        bool isPrivate = address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
            ? bytes[0] == 10 || bytes[0] == 172 && bytes[1] is >= 16 and <= 31 || bytes[0] == 192 && bytes[1] == 168
            : bytes.Length == 16 && ((bytes[0] & 0xFE) == 0xFC || bytes[0] == 0xFE && (bytes[1] & 0xC0) == 0x80);
        if (!isPrivate) throw new InvalidOperationException("HTTPTrustedIPAddress 不得是公网地址");
    }

    private static bool FixedTimeEquals(string left, string right)
    {
        byte[] leftHash = SHA256.HashData(Encoding.UTF8.GetBytes(left));
        byte[] rightHash = SHA256.HashData(Encoding.UTF8.GetBytes(right));
        return CryptographicOperations.FixedTimeEquals(leftHash, rightHash);
    }
}
