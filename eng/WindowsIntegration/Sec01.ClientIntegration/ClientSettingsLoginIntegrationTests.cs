extern alias MobileClient;
extern alias PCClient;

using System.Reflection;
using Xunit;

namespace Sec01.ClientIntegration.Windows;

public sealed class ClientSettingsLoginIntegrationTests
{
    [Fact]
    public void PC冒烟配置忽略普通配置并只从环境变量激活()
    {
        var originalDirectory = Environment.CurrentDirectory;
        var originalPassword = Environment.GetEnvironmentVariable("LYOCRYSTAL_SMOKETEST_PASSWORD");
        var originalMode = Environment.GetEnvironmentVariable("LYOCRYSTAL_LEG01_SMOKE");
        var originalAccount = Environment.GetEnvironmentVariable("LYOCRYSTAL_SMOKETEST_ACCOUNT");
        var originalCharacter = Environment.GetEnvironmentVariable("LYOCRYSTAL_SMOKETEST_CHARACTER");
        var originalExistingAccount = Environment.GetEnvironmentVariable("LYOCRYSTAL_SMOKETEST_EXISTING_ACCOUNT");
        var directory = Path.Combine(Path.GetTempPath(), "lyocrystal-leg01-pc-smoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            File.WriteAllText(
                Path.Combine(directory, "Mir2Config.ini"),
                "[Game]\r\nAccountID=leg01user\r\n[SmokeTest]\r\nAutoLogin=True\r\nAutoCreateAccount=True\r\nAutoCreateCharacter=True\r\nAutoStartGame=True\r\nAutoMoveAndExit=True\r\nCharacterName=Leg01Hero\r\n");
            File.WriteAllText(Path.Combine(directory, "Language.ini"), "[Language]\r\n");
            Environment.CurrentDirectory = directory;
            Environment.SetEnvironmentVariable("LYOCRYSTAL_SMOKETEST_PASSWORD", "runtime-only-password");
            Environment.SetEnvironmentVariable("LYOCRYSTAL_LEG01_SMOKE", "1");
            Environment.SetEnvironmentVariable("LYOCRYSTAL_SMOKETEST_ACCOUNT", "runtimeuser");
            Environment.SetEnvironmentVariable("LYOCRYSTAL_SMOKETEST_CHARACTER", "RuntimeHero");

            var settingsType = typeof(PCClient::Client.Security.LoginSettingsIntegration).Assembly
                .GetType("Client.Settings", throwOnError: true)!;
            settingsType.GetMethod("Load", BindingFlags.Static | BindingFlags.Public)!.Invoke(null, null);

            Assert.False(ReadStaticBool(settingsType, "SmokeTestAutoLogin"));
            Assert.False(ReadStaticBool(settingsType, "SmokeTestAutoCreateAccount"));
            Assert.False(ReadStaticBool(settingsType, "SmokeTestAutoCreateCharacter"));
            Assert.False(ReadStaticBool(settingsType, "SmokeTestAutoStartGame"));
            Assert.False(ReadStaticBool(settingsType, "SmokeTestAutoMoveAndExit"));
            Assert.Equal(string.Empty, ReadStaticString(settingsType, "SmokeTestCharacterName"));

            var automationType = typeof(PCClient::Client.Security.LoginSettingsIntegration).Assembly
                .GetType("Client.PcSmokeTestAutomation", throwOnError: true)!;
            automationType.GetMethod("ApplyEnvironmentOverrides", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null);
            var resolvePassword = automationType.GetMethod("ResolveRuntimePassword", BindingFlags.Static | BindingFlags.NonPublic)!;
            Assert.Equal("runtime-only-password", resolvePassword.Invoke(null, null));
            Assert.Equal("runtimeuser", ReadStaticString(settingsType, "AccountID"));
            Assert.Equal("RuntimeHero", ReadStaticString(settingsType, "SmokeTestCharacterName"));
            Assert.True(ReadStaticBool(settingsType, "SmokeTestAutoLogin"));
            Assert.True(ReadStaticBool(settingsType, "SmokeTestAutoCreateAccount"));
            Assert.True(ReadStaticBool(settingsType, "SmokeTestAutoCreateCharacter"));
            Assert.True(ReadStaticBool(settingsType, "SmokeTestAutoStartGame"));
            Assert.True(ReadStaticBool(settingsType, "SmokeTestAutoMoveAndExit"));
            Assert.False(ReadStaticBool(settingsType, "FullScreen"));
            Assert.False(ReadStaticBool(settingsType, "Borderless"));
            Assert.False(ReadStaticBool(settingsType, "TopMost"));
            Assert.DoesNotContain("runtime-only-password", File.ReadAllText(Path.Combine(directory, "Mir2Config.ini")));

            Environment.SetEnvironmentVariable("LYOCRYSTAL_SMOKETEST_EXISTING_ACCOUNT", "1");
            automationType.GetMethod("ApplyEnvironmentOverrides", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null);
            Assert.False(ReadStaticBool(settingsType, "SmokeTestAutoCreateAccount"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("LYOCRYSTAL_SMOKETEST_PASSWORD", originalPassword);
            Environment.SetEnvironmentVariable("LYOCRYSTAL_LEG01_SMOKE", originalMode);
            Environment.SetEnvironmentVariable("LYOCRYSTAL_SMOKETEST_ACCOUNT", originalAccount);
            Environment.SetEnvironmentVariable("LYOCRYSTAL_SMOKETEST_CHARACTER", originalCharacter);
            Environment.SetEnvironmentVariable("LYOCRYSTAL_SMOKETEST_EXISTING_ACCOUNT", originalExistingAccount);
            Environment.CurrentDirectory = originalDirectory;
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void PC进图首帧在玩家信息尚未到达时经验条不应绘制()
    {
        var assembly = typeof(PCClient::Client.Security.LoginSettingsIntegration).Assembly;
        var type = assembly.GetType("Client.MirScenes.Dialogs.MainDialog", throwOnError: true)!;
        var method = type.GetMethod("TryGetExperienceProgress", BindingFlags.Static | BindingFlags.NonPublic)!;
        var weightMethod = type.GetMethod("TryGetWeightProgress", BindingFlags.Static | BindingFlags.NonPublic)!;
        var gameSceneType = assembly.GetType("Client.MirScenes.GameScene", throwOnError: true)!;
        var canDrawMethod = gameSceneType.GetMethod("CanDrawGameFrame", BindingFlags.Static | BindingFlags.NonPublic)!;

        object?[] arguments = { null, 0D };
        Assert.False((bool)method.Invoke(null, arguments)!);
        Assert.Equal(0D, arguments[1]);
        object?[] weightArguments = { null, 0D };
        Assert.False((bool)weightMethod.Invoke(null, weightArguments)!);
        Assert.Equal(0D, weightArguments[1]);
        Assert.False((bool)canDrawMethod.Invoke(null, new object?[] { null })!);
    }

    [Fact]
    public void PC收到未知完整帧时只跳过该帧并保留后续数据()
    {
        var assembly = typeof(PCClient::Client.Security.LoginSettingsIntegration).Assembly;
        var networkType = assembly.GetType("Client.MirNetwork.Network", throwOnError: true)!;
        var method = networkType.GetMethod("TrySkipCompleteFrame", BindingFlags.Static | BindingFlags.NonPublic)!;
        byte[] unknownFrame = { 6, 0, 19, 1, 0xAA, 0xBB };
        byte[] nextFrame = { 4, 0, 14, 0 };
        byte[] buffer = unknownFrame.Concat(nextFrame).ToArray();
        object?[] arguments = { buffer, null, (short)0 };

        Assert.True((bool)method.Invoke(null, arguments)!);
        Assert.Equal((short)275, arguments[2]);
        Assert.Equal(nextFrame, (byte[])arguments[1]!);
    }

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

    private static bool ReadStaticBool(Type type, string fieldName) =>
        (bool)type.GetField(fieldName, BindingFlags.Static | BindingFlags.Public)!.GetValue(null)!;

    private static string ReadStaticString(Type type, string fieldName) =>
        (string)type.GetField(fieldName, BindingFlags.Static | BindingFlags.Public)!.GetValue(null)!;
}
