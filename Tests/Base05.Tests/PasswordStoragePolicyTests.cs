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

        persisted = "runtime-password";
        PasswordStoragePolicy.ClearOnSave(value => persisted = value);
        Assert.Equal(string.Empty, persisted);
    }

    [Fact]
    public void 配置往返后密码字段仍为空()
    {
        var path = Path.Combine(Path.GetTempPath(), $"base05-password-policy-{Guid.NewGuid():N}.ini");
        try
        {
            var reader = new InIReader(path);
            reader.Write("Game", "Password", "legacy-password");

            Assert.Equal(string.Empty,
                PasswordStoragePolicy.ClearOnLoad(value => reader.Write("Game", "Password", value)));
            var afterLoad = new InIReader(path);
            Assert.Equal(string.Empty, afterLoad.ReadString("Game", "Password", string.Empty, writeWhenNull: false));
            Assert.Contains("Password=", File.ReadAllLines(path));

            reader.Write("Game", "Password", "runtime-password");
            PasswordStoragePolicy.ClearOnSave(value => reader.Write("Game", "Password", value));
            var afterSave = new InIReader(path);
            Assert.Equal(string.Empty, afterSave.ReadString("Game", "Password", string.Empty, writeWhenNull: false));
            Assert.Contains("Password=", File.ReadAllLines(path));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
