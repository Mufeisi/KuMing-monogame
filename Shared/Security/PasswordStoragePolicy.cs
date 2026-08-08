using System;

namespace Shared.Security;

/// <summary>
/// 客户端密码配置策略。正式版仅允许运行时内存使用，配置文件永远写入空值。
/// </summary>
public static class PasswordStoragePolicy
{
    public const string PatchUserEnvironmentVariable = "LYOCRYSTAL_PATCH_USER";
    public const string PatchPasswordEnvironmentVariable = "LYOCRYSTAL_PATCH_PASSWORD";

    public static bool RememberPassword => false;

    public static int ClearStoredCredentials(InIReader reader, string section)
    {
        if (reader == null)
            throw new ArgumentNullException(nameof(reader));

        return reader.ClearKeys(section, "Password", "RememberPassword");
    }

    public static bool TryResolvePatchCredentials(string configuredUser, out string user, out string password)
    {
        return TryResolvePatchCredentials(configuredUser, Environment.GetEnvironmentVariable, out user, out password);
    }

    public static bool TryResolvePatchCredentials(string configuredUser,
        Func<string, string> environmentReader, out string user, out string password)
    {
        user = string.Empty;
        password = string.Empty;
        if (environmentReader == null)
            return false;

        string environmentUser = environmentReader(PatchUserEnvironmentVariable);
        user = string.IsNullOrWhiteSpace(environmentUser)
            ? (configuredUser ?? string.Empty).Trim()
            : environmentUser.Trim();
        password = environmentReader(PatchPasswordEnvironmentVariable) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(password))
        {
            user = string.Empty;
            password = string.Empty;
            return false;
        }

        return true;
    }

    public static string ClearOnLoad(Action<string> clearStoredValue)
    {
        clearStoredValue?.Invoke(string.Empty);
        return string.Empty;
    }

    public static bool ClearRememberPasswordOnLoad(Action<bool> clearStoredValue)
    {
        clearStoredValue?.Invoke(false);
        return false;
    }

    public static void ClearOnSave(Action<string> writeValue)
    {
        writeValue?.Invoke(string.Empty);
    }

    public static void ClearRememberPasswordOnSave(Action<bool> writeValue)
    {
        writeValue?.Invoke(false);
    }
}
