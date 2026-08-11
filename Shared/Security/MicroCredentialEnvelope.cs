#nullable enable
using System.Security.Cryptography;
using System.Text;

namespace Shared.Security;

/// <summary>
/// 将客户端协议必需的共享秘密封装为不可直接读取的二进制数据。
/// 该封装用于避免秘密出现在 INI、JSON 和命令行中；客户端协议本身仍属于共享秘密模型。
/// </summary>
public static class MicroCredentialEnvelope
{
    private static readonly byte[] Magic = "LYOMICRO1"u8.ToArray();
    private static readonly byte[] Key = SHA256.HashData("LyoCrystal.MicroCredentialEnvelope.v1.2026"u8.ToArray());

    public static byte[] Create(string projectId, string code)
    {
        byte[] associatedData = GetProjectBytes(projectId);
        byte[] plain = Encoding.UTF8.GetBytes(code?.Trim() ?? string.Empty);
        if (plain.Length is < 1 or > 512) throw new ArgumentOutOfRangeException(nameof(code), "微端 Code 长度无效");
        byte[] nonce = RandomNumberGenerator.GetBytes(12);
        byte[] cipher = new byte[plain.Length];
        byte[] tag = new byte[16];
        using (var aes = new AesGcm(Key, tag.Length)) aes.Encrypt(nonce, plain, cipher, tag, associatedData);
        byte[] result = new byte[Magic.Length + nonce.Length + tag.Length + cipher.Length];
        Buffer.BlockCopy(Magic, 0, result, 0, Magic.Length);
        Buffer.BlockCopy(nonce, 0, result, Magic.Length, nonce.Length);
        Buffer.BlockCopy(tag, 0, result, Magic.Length + nonce.Length, tag.Length);
        Buffer.BlockCopy(cipher, 0, result, Magic.Length + nonce.Length + tag.Length, cipher.Length);
        CryptographicOperations.ZeroMemory(plain);
        return result;
    }

    public static string Open(string projectId, ReadOnlySpan<byte> envelope)
    {
        int header = Magic.Length + 12 + 16;
        if (envelope.Length <= header || envelope.Length > header + 512 || !envelope[..Magic.Length].SequenceEqual(Magic))
            throw new InvalidDataException("微端凭据封装格式无效");
        byte[] plain = new byte[envelope.Length - header];
        try
        {
            using var aes = new AesGcm(Key, 16);
            aes.Decrypt(envelope.Slice(Magic.Length, 12), envelope[header..], envelope.Slice(Magic.Length + 12, 16), plain, GetProjectBytes(projectId));
            return Encoding.UTF8.GetString(plain);
        }
        catch (CryptographicException ex) { throw new InvalidDataException("微端凭据与项目不匹配或已损坏", ex); }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }

    private static byte[] GetProjectBytes(string projectId)
    {
        projectId = projectId?.Trim() ?? string.Empty;
        if (projectId.Length is < 1 or > 64 || projectId.Any(character =>
                !(character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '.' or '_' or '-')))
            throw new ArgumentException("项目标识无效", nameof(projectId));
        return Encoding.UTF8.GetBytes(projectId);
    }
}
