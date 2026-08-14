namespace Client;

using Shared.Security;

internal static class PcSmokeTestAutomation
{
    internal static bool Active =>
        string.Equals(Environment.GetEnvironmentVariable("LYOCRYSTAL_LEG01_SMOKE"), "1", StringComparison.Ordinal);

    internal static bool CustomGuiActive => Active &&
        string.Equals(Environment.GetEnvironmentVariable("LYOCRYSTAL_GUI13_SMOKE"), "1", StringComparison.Ordinal);

    internal static void ApplyEnvironmentOverrides()
    {
        if (!Active) return;

        Settings.SmokeTestAutoLogin = true;
        Settings.SmokeTestAutoCreateAccount = !string.Equals(
            Environment.GetEnvironmentVariable("LYOCRYSTAL_SMOKETEST_EXISTING_ACCOUNT"),
            "1",
            StringComparison.Ordinal);
        Settings.SmokeTestAutoCreateCharacter = true;
        Settings.SmokeTestAutoStartGame = true;
        Settings.SmokeTestAutoMoveAndExit = true;
        Settings.FullScreen = false;
        Settings.Borderless = false;
        Settings.TopMost = false;
        Settings.TracePackets = true;
        Settings.AccountID = Environment.GetEnvironmentVariable("LYOCRYSTAL_SMOKETEST_ACCOUNT")?.Trim() ?? string.Empty;
        Settings.SmokeTestCharacterName = Environment.GetEnvironmentVariable("LYOCRYSTAL_SMOKETEST_CHARACTER")?.Trim() ?? string.Empty;
    }

    internal static string ResolveRuntimePassword()
    {
        // 冒烟密码只允许来自本次进程环境变量，绝不从配置读取或保存。
        return Environment.GetEnvironmentVariable("LYOCRYSTAL_SMOKETEST_PASSWORD") ?? string.Empty;
    }

    internal static IReadOnlyDictionary<string, BootstrapManifestTrustedKey> ResolveCustomGuiTrustedKeys()
    {
        if (!CustomGuiActive) return null;
        string keyId = Environment.GetEnvironmentVariable("LYOCRYSTAL_GUI13_TRUSTED_KEY_ID")?.Trim() ?? string.Empty;
        string publicKey = Environment.GetEnvironmentVariable("LYOCRYSTAL_GUI13_TRUSTED_PUBLIC_KEY")?.Trim() ?? string.Empty;
        if (keyId.Length == 0 || publicKey.Length == 0) return null;
        return new Dictionary<string, BootstrapManifestTrustedKey>(StringComparer.Ordinal)
        {
            [keyId] = new BootstrapManifestTrustedKey
            {
                KeyId = keyId,
                SubjectPublicKeyInfo = publicKey,
                NotBeforeSequence = 1,
            },
        };
    }
}
