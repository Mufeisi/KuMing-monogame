using Shared.Security;
using Xunit;

namespace Base05.Tests;

public sealed class PasswordStoragePolicyTests
{
    [Fact]
    public void 客户端加载和保存密码策略始终为空且不记住密码()
    {
        var persisted = "legacy-password";

        Assert.False(PasswordStoragePolicy.RememberPassword);
        Assert.Equal(string.Empty,
            PasswordStoragePolicy.ClearOnLoad(value => persisted = value));
        Assert.Equal(string.Empty, persisted);

        var rememberPassword = true;
        Assert.False(PasswordStoragePolicy.ClearRememberPasswordOnLoad(value => rememberPassword = value));
        Assert.False(rememberPassword);

        persisted = "runtime-password";
        PasswordStoragePolicy.ClearOnSave(value => persisted = value);
        Assert.Equal(string.Empty, persisted);

        rememberPassword = true;
        PasswordStoragePolicy.ClearRememberPasswordOnSave(value => rememberPassword = value);
        Assert.False(rememberPassword);
    }

    [Fact]
    public void 配置往返后密码字段仍为空()
    {
        var path = Path.Combine(Path.GetTempPath(), $"base05-password-policy-{Guid.NewGuid():N}.ini");
        try
        {
            var reader = new InIReader(path);
            reader.Write("Game", "Password", "legacy-password");
            reader.Write("Launcher", "Password", "legacy-launcher-password");
            reader.Write("Game", "RememberPassword", true);
            reader.Write("Launcher", "RememberPassword", true);

            Assert.Equal(string.Empty,
                PasswordStoragePolicy.ClearOnLoad(value => reader.Write("Game", "Password", value)));
            Assert.Equal(string.Empty,
                PasswordStoragePolicy.ClearOnLoad(value => reader.Write("Launcher", "Password", value)));
            Assert.False(PasswordStoragePolicy.ClearRememberPasswordOnLoad(
                value => reader.Write("Game", "RememberPassword", value)));
            Assert.False(PasswordStoragePolicy.ClearRememberPasswordOnLoad(
                value => reader.Write("Launcher", "RememberPassword", value)));
            var afterLoad = new InIReader(path);
            Assert.Equal(string.Empty, afterLoad.ReadString("Game", "Password", string.Empty, writeWhenNull: false));
            Assert.Equal(string.Empty, afterLoad.ReadString("Launcher", "Password", string.Empty, writeWhenNull: false));
            Assert.False(afterLoad.ReadBoolean("Game", "RememberPassword", true, writeWhenNull: false));
            Assert.False(afterLoad.ReadBoolean("Launcher", "RememberPassword", true, writeWhenNull: false));
            Assert.Contains("Password=", File.ReadAllLines(path));

            reader.Write("Game", "Password", "runtime-password");
            reader.Write("Launcher", "Password", "runtime-launcher-password");
            reader.Write("Game", "RememberPassword", true);
            reader.Write("Launcher", "RememberPassword", true);
            PasswordStoragePolicy.ClearOnSave(value => reader.Write("Game", "Password", value));
            PasswordStoragePolicy.ClearOnSave(value => reader.Write("Launcher", "Password", value));
            PasswordStoragePolicy.ClearRememberPasswordOnSave(value => reader.Write("Game", "RememberPassword", value));
            PasswordStoragePolicy.ClearRememberPasswordOnSave(value => reader.Write("Launcher", "RememberPassword", value));
            var afterSave = new InIReader(path);
            Assert.Equal(string.Empty, afterSave.ReadString("Game", "Password", string.Empty, writeWhenNull: false));
            Assert.Equal(string.Empty, afterSave.ReadString("Launcher", "Password", string.Empty, writeWhenNull: false));
            Assert.False(afterSave.ReadBoolean("Game", "RememberPassword", true, writeWhenNull: false));
            Assert.False(afterSave.ReadBoolean("Launcher", "RememberPassword", true, writeWhenNull: false));
            Assert.Contains("Password=", File.ReadAllLines(path));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
