extern alias MobileClient;
extern alias PCClient;

using System.Reflection;
using Xunit;

namespace Sec01.ClientIntegration.Windows;

public sealed class ClientSettingsLoginIntegrationTests
{
    [Fact]
    public void 真实客户端宿主加载保存并由PC网络队列接收本次内存密码()
    {
        var originalDirectory = Environment.CurrentDirectory;
        var directory = Path.Combine(Path.GetTempPath(), "lyocrystal-sec01-client-" + Guid.NewGuid().ToString("N"));
        var mobileRoot = Path.Combine(directory, "mobile");
        Directory.CreateDirectory(directory);
        Directory.CreateDirectory(Path.Combine(mobileRoot, "BootstrapAssets"));

        try
        {
            const string legacyConfig = "[Game]\r\nAccountID=settingsuser\r\nPassword=persisted-secret\r\nRememberPassword=true\r\n";
            File.WriteAllText(Path.Combine(directory, "Mir2Config.ini"), legacyConfig);
            File.WriteAllText(Path.Combine(directory, "Language.ini"), "[Language]\r\n");
            File.WriteAllText(Path.Combine(mobileRoot, "BootstrapAssets", "Mir2Config.ini"), legacyConfig);
            File.WriteAllText(Path.Combine(mobileRoot, "BootstrapAssets", "Language.ini"), "[Language]\r\n");
            Environment.CurrentDirectory = directory;

            var pcLoaded = PCClient::Client.Security.LoginSettingsIntegration.LoadFromSettings();
            var mobileLoaded = MobileClient::MonoShare.Security.LoginSettingsIntegration.LoadFromSettings(mobileRoot);

            Assert.Equal("settingsuser", pcLoaded.AccountId);
            Assert.Empty(pcLoaded.Password);
            Assert.False(pcLoaded.RememberPassword);
            Assert.Equal("settingsuser", mobileLoaded.AccountId);
            Assert.Empty(mobileLoaded.Password);
            Assert.False(mobileLoaded.RememberPassword);

            var pcAssembly = typeof(PCClient::Client.Security.LoginSettingsIntegration).Assembly;
            var networkType = pcAssembly.GetType("Client.MirNetwork.Network", throwOnError: true)!;
            var sendListField = networkType.GetField("_sendList", BindingFlags.Static | BindingFlags.NonPublic)!;
            var sendQueue = Activator.CreateInstance(sendListField.FieldType)!;
            sendListField.SetValue(null, sendQueue);

            var packet = PCClient::Client.Security.LoginSettingsIntegration.Submit(string.Empty, "runtime12");
            var mobileCredentials = MobileClient::MonoShare.Security.LoginSettingsIntegration.PrepareAndPersist(
                string.Empty, "runtime12");

            var tryDequeue = sendQueue.GetType().GetMethod("TryDequeue")!;
            var dequeueArguments = new object?[] { null };
            Assert.True((bool)tryDequeue.Invoke(sendQueue, dequeueArguments)!);
            Assert.Same(packet, dequeueArguments[0]);
            Assert.Equal("settingsuser", packet.AccountID);
            Assert.Equal("runtime12", packet.Password);
            Assert.Equal(packet.AccountID, mobileCredentials.AccountId);
            Assert.Equal(packet.Password, mobileCredentials.Password);

            AssertConfigContainsNoPassword(Path.Combine(directory, "Mir2Config.ini"));
            AssertConfigContainsNoPassword(Path.Combine(mobileRoot, "Cache", "Mobile", "Runtime", "Mir2Config.ini"));
        }
        finally
        {
            Environment.CurrentDirectory = originalDirectory;
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void AssertConfigContainsNoPassword(string path)
    {
        var persisted = File.ReadAllText(path);
        Assert.DoesNotContain("Password=", persisted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RememberPassword=", persisted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("runtime12", persisted, StringComparison.Ordinal);
    }
}
