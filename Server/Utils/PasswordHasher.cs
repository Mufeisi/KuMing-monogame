using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace Server.Utils;

internal enum PasswordVerificationResult
{
    Invalid,
    Valid,
    ValidNeedsUpgrade,
}

/// <summary>
/// 账户密码哈希边界：新密码统一使用 Argon2id PHC，旧 PBKDF2-SHA1 仅用于一次兼容验证。
/// </summary>
internal static class PasswordHasher
{
    internal const int SaltLength = 16;
    internal const int HashLength = 32;
    internal const int MemoryCostKiB = 32 * 1024;
    internal const int TimeCost = 3;
    internal const int Parallelism = 1;
    internal const int Version = 19;

    private const int MinimumMemoryCostKiB = 16 * 1024;
    private const int MaximumMemoryCostKiB = 256 * 1024;
    private const int MaximumTimeCost = 10;
    private const int MaximumParallelism = 8;
    private const int MinimumSaltLength = 16;
    private const int MaximumSaltLength = 64;
    private const int MinimumHashLength = 16;
    private const int MaximumHashLength = 64;
    private const string Prefix = "$argon2id$";

    internal static string Hash(string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(SaltLength);
        try
        {
            byte[] hash = Derive(password, salt, MemoryCostKiB, TimeCost, Parallelism, HashLength);
            return "$argon2id$v=19$m=" + MemoryCostKiB + ",t=" + TimeCost + ",p=" + Parallelism + "$" +
                   EncodePhcBase64(salt) + "$" + EncodePhcBase64(hash);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(salt);
        }
    }

    internal static bool IsArgon2idHash(string storedHash)
    {
        return !string.IsNullOrEmpty(storedHash) && storedHash.StartsWith(Prefix, StringComparison.Ordinal);
    }

    internal static PasswordVerificationResult Verify(string storedHash, string password, byte[] legacySalt)
    {
        if (IsArgon2idHash(storedHash))
            return VerifyArgon2id(storedHash, password);

        return VerifyLegacy(storedHash, password, legacySalt);
    }

    private static PasswordVerificationResult VerifyArgon2id(string storedHash, string password)
    {
        if (!TryParsePhc(storedHash, out var parameters))
            return PasswordVerificationResult.Invalid;

        byte[] derived = null;
        byte[] expected = parameters.Hash;
        try
        {
            derived = Derive(password, parameters.Salt, parameters.MemoryCostKiB, parameters.TimeCost,
                parameters.Parallelism, expected.Length);
            if (!CryptographicOperations.FixedTimeEquals(derived, expected))
                return PasswordVerificationResult.Invalid;

            return parameters.MemoryCostKiB == MemoryCostKiB &&
                   parameters.TimeCost == TimeCost &&
                   parameters.Parallelism == Parallelism &&
                   parameters.Salt.Length == SaltLength &&
                   expected.Length == HashLength
                ? PasswordVerificationResult.Valid
                : PasswordVerificationResult.ValidNeedsUpgrade;
        }
        catch
        {
            return PasswordVerificationResult.Invalid;
        }
        finally
        {
            if (derived != null)
                CryptographicOperations.ZeroMemory(derived);
            CryptographicOperations.ZeroMemory(expected);
            CryptographicOperations.ZeroMemory(parameters.Salt);
        }
    }

    private static PasswordVerificationResult VerifyLegacy(string storedHash, string password, byte[] legacySalt)
    {
        if (string.IsNullOrEmpty(storedHash) || legacySalt == null || legacySalt.Length != Crypto.SaltSize)
            return PasswordVerificationResult.Invalid;

        string calculated;
        try
        {
            calculated = Crypto.HashPassword(password ?? string.Empty, legacySalt);
        }
        catch
        {
            return PasswordVerificationResult.Invalid;
        }

        byte[] expectedBytes = Encoding.UTF8.GetBytes(storedHash);
        byte[] calculatedBytes = Encoding.UTF8.GetBytes(calculated);
        try
        {
            return CryptographicOperations.FixedTimeEquals(expectedBytes, calculatedBytes)
                ? PasswordVerificationResult.ValidNeedsUpgrade
                : PasswordVerificationResult.Invalid;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(expectedBytes);
            CryptographicOperations.ZeroMemory(calculatedBytes);
        }
    }

    private static byte[] Derive(string password, byte[] salt, int memoryCostKiB, int timeCost, int parallelism,
        int hashLength)
    {
        byte[] passwordBytes = Encoding.UTF8.GetBytes(password ?? string.Empty);
        try
        {
            using var argon2 = new Argon2id(passwordBytes)
            {
                Salt = salt,
                MemorySize = memoryCostKiB,
                Iterations = timeCost,
                DegreeOfParallelism = parallelism,
            };
            return argon2.GetBytes(hashLength);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
        }
    }

    private static bool TryParsePhc(string storedHash, out PhcParameters parameters)
    {
        parameters = default;
        try
        {
            string[] fields = storedHash.Split('$');
            if (fields.Length != 6 || fields[0].Length != 0 || fields[1] != "argon2id" || fields[2] != "v=19")
                return false;

            if (!TryParseParameters(fields[3], out int memoryCostKiB, out int timeCost, out int parallelism))
                return false;
            if (memoryCostKiB < MinimumMemoryCostKiB || memoryCostKiB > MaximumMemoryCostKiB ||
                timeCost < 1 || timeCost > MaximumTimeCost || parallelism < 1 || parallelism > MaximumParallelism)
                return false;
            byte[] salt = null;
            byte[] hash = null;
            if (!TryDecodePhcBase64(fields[4], out salt) ||
                !TryDecodePhcBase64(fields[5], out hash) ||
                salt.Length < MinimumSaltLength || salt.Length > MaximumSaltLength ||
                hash.Length < MinimumHashLength || hash.Length > MaximumHashLength)
            {
                if (salt != null) CryptographicOperations.ZeroMemory(salt);
                if (hash != null) CryptographicOperations.ZeroMemory(hash);
                return false;
            }

            parameters = new PhcParameters(memoryCostKiB, timeCost, parallelism, salt, hash);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryParseParameters(string value, out int memoryCostKiB, out int timeCost,
        out int parallelism)
    {
        memoryCostKiB = 0;
        timeCost = 0;
        parallelism = 0;
        string[] parts = value.Split(',');
        if (parts.Length != 3) return false;

        var seen = 0;
        for (var i = 0; i < parts.Length; i++)
        {
            string[] pair = parts[i].Split('=');
            if (pair.Length != 2 || !int.TryParse(pair[1], out int parsed) || parsed <= 0)
                return false;

            switch (pair[0])
            {
                case "m" when (seen & 1) == 0:
                    memoryCostKiB = parsed;
                    seen |= 1;
                    break;
                case "t" when (seen & 2) == 0:
                    timeCost = parsed;
                    seen |= 2;
                    break;
                case "p" when (seen & 4) == 0:
                    parallelism = parsed;
                    seen |= 4;
                    break;
                default:
                    return false;
            }
        }

        return seen == 7;
    }

    private static string EncodePhcBase64(byte[] bytes)
    {
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '.');
    }

    private static bool TryDecodePhcBase64(string value, out byte[] bytes)
    {
        bytes = null;
        if (string.IsNullOrEmpty(value)) return false;
        for (var i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (!(c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '/'))
                return false;
        }

        string standard = value.Replace('.', '+');
        var remainder = standard.Length % 4;
        if (remainder == 1) return false;
        if (remainder != 0) standard += new string('=', 4 - remainder);

        try
        {
            bytes = Convert.FromBase64String(standard);
            return bytes.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    private readonly struct PhcParameters
    {
        internal readonly int MemoryCostKiB;
        internal readonly int TimeCost;
        internal readonly int Parallelism;
        internal readonly byte[] Salt;
        internal readonly byte[] Hash;

        internal PhcParameters(int memoryCostKiB, int timeCost, int parallelism, byte[] salt, byte[] hash)
        {
            MemoryCostKiB = memoryCostKiB;
            TimeCost = timeCost;
            Parallelism = parallelism;
            Salt = salt;
            Hash = hash;
        }
    }
}
