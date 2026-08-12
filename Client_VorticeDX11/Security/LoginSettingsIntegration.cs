using Shared.Security;

namespace Client.Security;

public static class LoginSettingsIntegration
{
    public static PasswordStoragePolicy.LoginCredentials LoadFromSettings()
    {
        Settings.Load();
        return new PasswordStoragePolicy.LoginCredentials(Settings.AccountID, Settings.Password, false);
    }

    public static PasswordStoragePolicy.LoginCredentials Load(InIReader reader, string fallbackAccountId)
    {
        return PasswordStoragePolicy.LoadLoginCredentials(reader, "Game", fallbackAccountId);
    }

    public static ClientPackets.Login Submit(string enteredAccountId, string enteredPassword)
    {
        var credentials = PasswordStoragePolicy.PrepareLoginCredentials(
            enteredAccountId, enteredPassword, Settings.AccountID, Settings.Password);
        Settings.AccountID = credentials.AccountId;
        Settings.Password = credentials.Password;
        Settings.Save();
        var packet = new ClientPackets.Login
        {
            AccountID = credentials.AccountId,
            Password = credentials.Password,
        };
        Client.MirNetwork.Network.Enqueue(packet);
        return packet;
    }
}
