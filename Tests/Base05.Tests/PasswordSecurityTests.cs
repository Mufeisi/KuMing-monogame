using Server.MirDatabase;
using Server.Utils;
using Konscious.Security.Cryptography;
using System.Text;
using Xunit;

namespace Base05.Tests;

public sealed class PasswordSecurityTests
{
    [Fact]
    public void 固定Argon2标准PHC向量与旧点号兼容()
    {
        const string password = "SEC-01-fixed-password";
        const string saltBase64 = "+//u3cy7qpmId2ZVRDMiEQ";
        const string expected = "$argon2id$v=19$m=32768,t=3,p=1$+//u3cy7qpmId2ZVRDMiEQ$xZMXVRj/GaGvCNLyFtG8MyYXBMhe26VWFLTAeYXAgUU";
        var salt = Convert.FromBase64String(saltBase64 + "==");
        var passwordBytes = Encoding.UTF8.GetBytes(password);
        byte[] digest;
        using (var argon2 = new Argon2id(passwordBytes)
        {
            Salt = salt,
            MemorySize = PasswordHasher.MemoryCostKiB,
            Iterations = PasswordHasher.TimeCost,
            DegreeOfParallelism = PasswordHasher.Parallelism,
        })
        {
            digest = argon2.GetBytes(PasswordHasher.HashLength);
        }

        var stored = "$argon2id$v=19$m=32768,t=3,p=1$" + saltBase64 + "$" +
                     Convert.ToBase64String(digest).TrimEnd('=');
        Assert.Equal(expected, stored);
        var account = new AccountInfo();
        account.SetPasswordHashAndSalt(stored, Array.Empty<byte>());
        Assert.Equal(PasswordVerificationResult.Valid, account.VerifyPassword(password));

        var legacyDotVector = stored.Replace('+', '.');
        account.SetPasswordHashAndSalt(legacyDotVector, Array.Empty<byte>());
        Assert.Equal(PasswordVerificationResult.Valid, account.VerifyPassword(password));
    }

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

        Assert.InRange(legacyHash.Length, 1, Crypto.HashSize);

        Assert.Equal(PasswordVerificationResult.Invalid, account.VerifyPassword("wrong-password"));
        Assert.Equal(legacyHash, account.Password);

        Assert.Equal(PasswordVerificationResult.ValidNeedsUpgrade, account.VerifyPassword("legacy-password"));
        Assert.StartsWith("$argon2id$v=19$", account.Password, StringComparison.Ordinal);
        Assert.Empty(account.Salt);
        Assert.Equal(PasswordVerificationResult.Valid, account.VerifyPassword("legacy-password"));
    }

    [Fact]
    public void 旧格式超长或非法字符在派生前拒绝且不升级()
    {
        var salt = Crypto.GenerateSalt();
        var maliciousInputs = new[]
        {
            new string('x', Crypto.HashSize + 1),
            new string('\uD800', Crypto.HashSize),
        };

        foreach (var storedHash in maliciousInputs)
        {
            var account = new AccountInfo();
            account.SetPasswordHashAndSalt(storedHash, salt);

            var exception = Record.Exception(() => account.VerifyPassword("any-password"));

            Assert.Null(exception);
            Assert.Equal(PasswordVerificationResult.Invalid, account.VerifyPassword("any-password"));
            Assert.Equal(storedHash, account.Password);
        }
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

    [Fact]
    public void 恶意PHC长度和参数在计算前拒绝()
    {
        var maliciousInputs = new[]
        {
            "$argon2id$v=19$m=2147483647,t=3,p=1$AAAAAAAAAAAAAAAAAAAAAA$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
            "$argon2id$v=19$m=32768,t=2147483647,p=1$AAAAAAAAAAAAAAAAAAAAAA$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
            "$argon2id$v=19$m=32768,t=3,p=2147483647$AAAAAAAAAAAAAAAAAAAAAA$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
            "$argon2id$v=19$m=32768,t=3,p=1$" + new string('A', 200) + "$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
            "$argon2id$v=19$m=32768,t=3,p=1$AAAAAAAAAAAAAAAAAAAAAA$" + new string('A', 200),
            "$argon2id$v=19$m=32768,t=3,p=1$AAAAAAAAAAAAAAAAAAAAAA$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" + new string('x', 220),
        };

        foreach (var storedHash in maliciousInputs)
        {
            var account = new AccountInfo();
            account.SetPasswordHashAndSalt(storedHash, Array.Empty<byte>());
            var exception = Record.Exception(() => account.VerifyPassword("any-password"));

            Assert.Null(exception);
            Assert.Equal(PasswordVerificationResult.Invalid, account.VerifyPassword("any-password"));
        }
    }
}
