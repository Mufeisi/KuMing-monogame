using System;

namespace Shared.Security;

/// <summary>
/// 客户端密码配置策略。正式版仅允许运行时内存使用，配置文件永远写入空值。
/// </summary>
public static class PasswordStoragePolicy
{
    public static bool RememberPassword => false;

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
