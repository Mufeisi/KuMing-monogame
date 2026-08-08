using Shared.Security;
using System.Text;
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

    [Fact]
    public void 密码清除接缝删除目标节全部重复键且文件不留旧值()
    {
        var path = Path.Combine(Path.GetTempPath(), $"base05-password-clear-{Guid.NewGuid():N}.ini");
        try
        {
            File.WriteAllText(path,
                "[Game]\r\n" +
                "Password=legacy-game-one\r\n" +
                "Password=legacy-game-two\r\n" +
                "RememberPassword=true\r\n" +
                "RememberPassword=true\r\n" +
                "AccountID=keep-me\r\n" +
                "[Launcher]\r\n" +
                "Password=legacy-launcher-one\r\n" +
                "Password=legacy-launcher-two\r\n" +
                "RememberPassword=true\r\n" +
                "RememberPassword=true\r\n");

            var reader = new InIReader(path);
            Assert.Equal(4, PasswordStoragePolicy.ClearStoredCredentials(reader, "Game"));
            Assert.Equal(4, PasswordStoragePolicy.ClearStoredCredentials(reader, "Launcher"));

            var contents = File.ReadAllText(path);
            Assert.DoesNotContain("legacy-game", contents, StringComparison.Ordinal);
            Assert.DoesNotContain("legacy-launcher", contents, StringComparison.Ordinal);
            Assert.DoesNotContain("Password=", contents, StringComparison.Ordinal);
            Assert.DoesNotContain("RememberPassword=", contents, StringComparison.Ordinal);
            Assert.Contains("AccountID=keep-me", contents, StringComparison.Ordinal);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void 密码清除写盘失败会抛出而不是静默吞掉()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var path = Path.Combine(Path.GetTempPath(), $"base05-password-readonly-{Guid.NewGuid():N}.ini");
        try
        {
            File.WriteAllText(path, "[Game]\r\nPassword=legacy\r\nKeep=untouched\r\n");
            var original = Convert.ToHexString(File.ReadAllBytes(path));
            File.SetAttributes(path, FileAttributes.ReadOnly);
            var reader = new InIReader(path);

            Assert.ThrowsAny<Exception>(() => PasswordStoragePolicy.ClearStoredCredentials(reader, "Game"));
            Assert.Equal("legacy", reader.ReadString("Game", "Password", string.Empty, writeWhenNull: false));
            Assert.Equal(original, Convert.ToHexString(File.ReadAllBytes(path)));
        }
        finally
        {
            if (File.Exists(path))
            {
                File.SetAttributes(path, FileAttributes.Normal);
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void 构造读取锁解除后同一对象可重试密码清除()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var path = Path.Combine(Path.GetTempPath(), $"base05-password-read-lock-{Guid.NewGuid():N}.ini");
        var temporaryPattern = "." + Path.GetFileName(path) + ".*.tmp";
        try
        {
            File.WriteAllText(path, "[Game]\r\nPassword=legacy\r\nKeep=untouched\r\n");
            var original = Convert.ToHexString(File.ReadAllBytes(path));
            var reader = default(InIReader);
            using (var lockStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                reader = new InIReader(path);
                Assert.ThrowsAny<Exception>(() => PasswordStoragePolicy.ClearStoredCredentials(reader, "Game"));
                Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, temporaryPattern));
            }

            Assert.Equal(original, Convert.ToHexString(File.ReadAllBytes(path)));
            Assert.Equal(1, PasswordStoragePolicy.ClearStoredCredentials(reader!, "Game"));
            var contents = File.ReadAllText(path);
            Assert.DoesNotContain("Password=legacy", contents, StringComparison.Ordinal);
            Assert.Contains("Keep=untouched", contents, StringComparison.Ordinal);
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, temporaryPattern));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void 目标锁定时原子替换失败且解除后同一对象可重试()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var path = Path.Combine(Path.GetTempPath(), $"base05-password-replace-lock-{Guid.NewGuid():N}.ini");
        var temporaryPattern = "." + Path.GetFileName(path) + ".*.tmp";
        try
        {
            File.WriteAllText(path, "[Game]\r\nPassword=legacy\r\nKeep=untouched\r\n");
            var original = Convert.ToHexString(File.ReadAllBytes(path));
            var reader = new InIReader(path);
            using (var lockStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Assert.ThrowsAny<Exception>(() => PasswordStoragePolicy.ClearStoredCredentials(reader, "Game"));
                Assert.Equal("legacy", reader.ReadString("Game", "Password", string.Empty, writeWhenNull: false));
                Assert.Equal(original, Convert.ToHexString(File.ReadAllBytes(path)));
                Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, temporaryPattern));
            }

            Assert.Equal(1, PasswordStoragePolicy.ClearStoredCredentials(reader, "Game"));
            Assert.DoesNotContain("Password=legacy", File.ReadAllText(path), StringComparison.Ordinal);
            Assert.Contains("Keep=untouched", File.ReadAllText(path), StringComparison.Ordinal);
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, temporaryPattern));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void 密码清除保留UTF8BOM与原始CRLF并清理重复节()
    {
        var path = Path.Combine(Path.GetTempPath(), $"base05-password-format-{Guid.NewGuid():N}.ini");
        try
        {
            var text = "[Game]\r\n" +
                "Password=legacy-one\r\n" +
                "Keep=one\r\n" +
                "[Game]\r\n" +
                "RememberPassword=true\r\n" +
                "Keep=two\r\n";
            var body = new UTF8Encoding(false).GetBytes(text);
            var bytes = new byte[3 + body.Length];
            bytes[0] = 0xEF;
            bytes[1] = 0xBB;
            bytes[2] = 0xBF;
            Buffer.BlockCopy(body, 0, bytes, 3, body.Length);
            File.WriteAllBytes(path, bytes);

            Assert.Equal(2, PasswordStoragePolicy.ClearStoredCredentials(new InIReader(path), "Game"));
            var result = File.ReadAllBytes(path);
            Assert.True(result.Length >= 3);
            Assert.Equal(0xEF, result[0]);
            Assert.Equal(0xBB, result[1]);
            Assert.Equal(0xBF, result[2]);
            var resultText = new UTF8Encoding(false).GetString(result, 3, result.Length - 3);
            Assert.DoesNotContain("Password=legacy", resultText, StringComparison.Ordinal);
            Assert.DoesNotContain("RememberPassword=", resultText, StringComparison.Ordinal);
            Assert.Contains("Keep=one\r\n", resultText, StringComparison.Ordinal);
            Assert.Contains("Keep=two\r\n", resultText, StringComparison.Ordinal);
            Assert.DoesNotContain("\n", resultText.Replace("\r\n", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void 补丁凭据只从运行时环境解析且缺少密码时失败()
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [PasswordStoragePolicy.PatchUserEnvironmentVariable] = "runtime-user",
            [PasswordStoragePolicy.PatchPasswordEnvironmentVariable] = "runtime-secret",
        };

        Assert.True(PasswordStoragePolicy.TryResolvePatchCredentials(
            "config-user", name => values.TryGetValue(name, out var value) ? value : null,
            out var user, out var password));
        Assert.Equal("runtime-user", user);
        Assert.Equal("runtime-secret", password);

        values.Remove(PasswordStoragePolicy.PatchPasswordEnvironmentVariable);
        Assert.False(PasswordStoragePolicy.TryResolvePatchCredentials(
            "config-user", name => values.TryGetValue(name, out var value) ? value : null,
            out user, out password));
        Assert.Empty(user);
        Assert.Empty(password);

        values[PasswordStoragePolicy.PatchPasswordEnvironmentVariable] = "runtime-secret";
        values.Remove(PasswordStoragePolicy.PatchUserEnvironmentVariable);
        Assert.True(PasswordStoragePolicy.TryResolvePatchCredentials(
            "config-user", name => values.TryGetValue(name, out var value) ? value : null,
            out user, out password));
        Assert.Equal("config-user", user);
        Assert.Equal("runtime-secret", password);
    }
}
