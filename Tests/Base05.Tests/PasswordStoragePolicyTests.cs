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
}
