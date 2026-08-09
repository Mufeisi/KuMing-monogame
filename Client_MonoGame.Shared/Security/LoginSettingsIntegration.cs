using System;
using System.IO;
using Shared.Security;

namespace MonoShare.Security;

public static class LoginSettingsIntegration
{
    public static PasswordStoragePolicy.LoginCredentials LoadFromSettings(string clientRoot)
    {
        Settings.ConfigureClientRoot(clientRoot);
        Settings.Load();
        return new PasswordStoragePolicy.LoginCredentials(Settings.AccountID, Settings.Password, false);
    }

    public static PasswordStoragePolicy.LoginCredentials Load(InIReader reader, string fallbackAccountId)
    {
        return PasswordStoragePolicy.LoadLoginCredentials(reader, "Game", fallbackAccountId);
    }

    public static PasswordStoragePolicy.LoginCredentials PrepareAndPersist(string enteredAccountId, string enteredPassword)
    {
        var credentials = PasswordStoragePolicy.PrepareLoginCredentials(
            enteredAccountId, enteredPassword, Settings.AccountID, Settings.Password);
        Settings.AccountID = credentials.AccountId;
        Settings.Password = credentials.Password;
        Settings.Save();
        return credentials;
    }

#if REAL_ANDROID
    public static PasswordStoragePolicy.LoginCredentials Submit(string enteredAccountId, string enteredPassword)
    {
        var credentials = PrepareAndPersist(enteredAccountId, enteredPassword);
        MonoShare.MirNetwork.Network.Enqueue(new ClientPackets.Login
        {
            AccountID = credentials.AccountId,
            Password = credentials.Password,
        });
        return credentials;
    }

    public static bool RunHostProbe()
    {
        const string probeAccountId = "sec01probe";
        const string probePassword = "runtime12";
        var originalAccountId = Settings.AccountID;
        var originalPassword = Settings.Password;
        try
        {
            var pendingBefore = MonoShare.MirNetwork.Network.PendingSendCount;
            var credentials = Submit(probeAccountId, probePassword);
            var pendingAfter = MonoShare.MirNetwork.Network.PendingSendCount;
            var persisted = File.ReadAllText(Settings.ConfigFilePath);

            return credentials.AccountId == probeAccountId &&
                   credentials.Password == probePassword &&
                   pendingAfter == pendingBefore + 1 &&
                   !persisted.Contains("Password=", StringComparison.OrdinalIgnoreCase) &&
                   !persisted.Contains("RememberPassword=", StringComparison.OrdinalIgnoreCase) &&
                   !persisted.Contains(probePassword, StringComparison.Ordinal);
        }
        finally
        {
            Settings.AccountID = originalAccountId;
            Settings.Password = originalPassword;
            Settings.Save();
        }
    }
#endif
}
