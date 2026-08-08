using Server.MirDatabase;
using Server.Utils;
using Xunit;

namespace Base05.Tests;

public sealed class PasswordSecurityTests
{
    [Fact]
    public void 新密码使用Argon2id_PH格式并可验证()
    {
        var account = new AccountInfo { Password = "P@ssword-2026" };

        Assert.StartsWith("$argon2id$v=19$", account.Password, StringComparison.Ordinal);
        Assert.Empty(account.Salt);
        Assert.Equal(PasswordVerificationResult.Valid, account.VerifyPassword("P@ssword-2026"));
        Assert.Equal(PasswordVerificationResult.Invalid, account.VerifyPassword("wrong-password"));
    }

    [Fact]
    public void 旧PBKDF2密码验证成功后透明升级且错误密码不升级()
    {
        var salt = Crypto.GenerateSalt();
        var legacyHash = Crypto.HashPassword("legacy-password", salt);
        var account = new AccountInfo();
        account.SetPasswordHashAndSalt(legacyHash, salt);

        Assert.Equal(PasswordVerificationResult.Invalid, account.VerifyPassword("wrong-password"));
        Assert.Equal(legacyHash, account.Password);

        Assert.Equal(PasswordVerificationResult.ValidNeedsUpgrade, account.VerifyPassword("legacy-password"));
        Assert.StartsWith("$argon2id$v=19$", account.Password, StringComparison.Ordinal);
        Assert.Empty(account.Salt);
        Assert.Equal(PasswordVerificationResult.Valid, account.VerifyPassword("legacy-password"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("$argon2id$v=19$m=bad,t=3,p=1$not-base64$not-base64")]
    [InlineData("$argon2i$v=19$m=32768,t=3,p=1$YWJjZGVmZ2hpamtsbW5vcA$YWJjZGVmZ2hpamtsbW5vcA")]
    [InlineData("$argon2id$v=18$m=32768,t=3,p=1$YWJjZGVmZ2hpamtsbW5vcA$YWJjZGVmZ2hpamtsbW5vcA")]
    public void 畸形或不支持的PHC安全失败(string storedHash)
    {
        var account = new AccountInfo();
        account.SetPasswordHashAndSalt(storedHash, Array.Empty<byte>());

        var exception = Record.Exception(() => account.VerifyPassword("any-password"));

        Assert.Null(exception);
        Assert.Equal(PasswordVerificationResult.Invalid, account.VerifyPassword("any-password"));
    }
}
