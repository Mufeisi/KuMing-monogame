using System.Security.Cryptography;
using System.Text.Json;

namespace Shared.Security;

/// <summary>保存“前任可信键签名的新快照”，逐跳扩展项目公钥；记录本身不作为信任锚。</summary>
public static class BootstrapTrustChainStore
{
    private const string ManifestName = "bootstrap-manifest.json";
    private const string SnapshotName = "launcher-snapshot.json";

    public static IReadOnlyDictionary<string, BootstrapManifestTrustedKey> Resolve(
        string chainRoot,
        IReadOnlyDictionary<string, BootstrapManifestTrustedKey> anchorKeys,
        Version clientVersion)
    {
        var keys = new Dictionary<string, BootstrapManifestTrustedKey>(anchorKeys, StringComparer.Ordinal);
        if (!Directory.Exists(chainRoot)) return keys;
        var candidates = new List<(long Sequence, string Directory)>();
        foreach (string directory in Directory.EnumerateDirectories(chainRoot).Take(65))
        {
            try
            {
                string manifestJson = ReadBounded(Path.Combine(directory, ManifestName), BootstrapManifestSignaturePolicy.MaximumJsonBytes);
                using JsonDocument document = JsonDocument.Parse(manifestJson);
                if (document.RootElement.TryGetProperty("Sequence", out JsonElement sequence) && sequence.TryGetInt64(out long value)) candidates.Add((value, directory));
            }
            catch { }
        }
        if (candidates.Count > 64) return keys;
        long previous = 0;
        foreach ((long sequence, string directory) in candidates.OrderBy(item => item.Sequence))
        {
            try
            {
                if (sequence <= previous) continue;
                string manifestJson = ReadBounded(Path.Combine(directory, ManifestName), BootstrapManifestSignaturePolicy.MaximumJsonBytes);
                BootstrapManifestVerificationResult verification = BootstrapManifestSignaturePolicy.Verify(manifestJson, keys, clientVersion);
                if (!verification.IsValid || verification.Manifest.Sequence != sequence) continue;
                byte[] snapshotBytes = File.ReadAllBytes(Path.Combine(directory, SnapshotName));
                BootstrapSignedPackage package = verification.Manifest.Packages.Single(item => string.Equals(item.Name, SnapshotName, StringComparison.Ordinal));
                if (snapshotBytes.LongLength != package.Size || !string.Equals(Convert.ToHexString(SHA256.HashData(snapshotBytes)), package.Sha256, StringComparison.OrdinalIgnoreCase)) continue;
                foreach (BootstrapManifestTrustedKey key in ParseSnapshotKeys(snapshotBytes))
                {
                    if (keys.TryGetValue(key.KeyId, out BootstrapManifestTrustedKey existing) && existing.SubjectPublicKeyInfo != key.SubjectPublicKeyInfo) throw new InvalidDataException("启动器轮换信任链公钥标识冲突");
                    keys[key.KeyId] = key;
                }
                if (keys.Count > 128) throw new InvalidDataException("启动器轮换信任链公钥过多");
                previous = sequence;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or InvalidOperationException or CryptographicException) { }
        }
        return keys;
    }

    public static void Record(string versionRoot, string chainRoot, IReadOnlyDictionary<string, BootstrapManifestTrustedKey> currentKeys, Version clientVersion)
    {
        string source = Path.GetFullPath(versionRoot);
        string manifestJson = ReadBounded(Path.Combine(source, ManifestName), BootstrapManifestSignaturePolicy.MaximumJsonBytes);
        BootstrapManifestVerificationResult verification = BootstrapManifestSignaturePolicy.Verify(manifestJson, currentKeys, clientVersion);
        if (!verification.IsValid) throw new InvalidDataException("启动器轮换信任记录签名无效：" + verification.Error);
        string snapshotPath = Path.Combine(source, SnapshotName);
        if (!File.Exists(snapshotPath) || new FileInfo(snapshotPath).Length > 8L * 1024 * 1024) throw new InvalidDataException("启动器轮换信任快照无效");
        byte[] snapshot = File.ReadAllBytes(snapshotPath);
        BootstrapSignedPackage package = verification.Manifest.Packages.Single(item => string.Equals(item.Name, SnapshotName, StringComparison.Ordinal));
        if (snapshot.LongLength != package.Size || !string.Equals(Convert.ToHexString(SHA256.HashData(snapshot)), package.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("启动器轮换信任快照摘要无效");
        IReadOnlyList<BootstrapManifestTrustedKey> introduced = ParseSnapshotKeys(snapshot);
        bool changesTrust = introduced.Any(key => !currentKeys.TryGetValue(key.KeyId, out BootstrapManifestTrustedKey existing) ||
            existing.SubjectPublicKeyInfo != key.SubjectPublicKeyInfo || existing.NotBeforeSequence != key.NotBeforeSequence || existing.NotAfterSequence != key.NotAfterSequence);
        if (!changesTrust) return;
        Directory.CreateDirectory(chainRoot);
        string hash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(manifestJson))).ToLowerInvariant()[..16];
        string destination = Path.Combine(chainRoot, verification.Manifest.Sequence + "-" + hash);
        if (Directory.Exists(destination)) return;
        string temporary = destination + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            Directory.CreateDirectory(temporary);
            File.WriteAllText(Path.Combine(temporary, ManifestName), manifestJson);
            File.WriteAllBytes(Path.Combine(temporary, SnapshotName), snapshot);
            Directory.Move(temporary, destination);
        }
        finally { if (Directory.Exists(temporary)) Directory.Delete(temporary, true); }
    }

    private static IReadOnlyList<BootstrapManifestTrustedKey> ParseSnapshotKeys(byte[] snapshot)
    {
        using JsonDocument document = JsonDocument.Parse(snapshot);
        var result = new List<BootstrapManifestTrustedKey>();
        foreach (JsonElement element in document.RootElement.GetProperty("TrustedReleaseKeys").EnumerateArray())
        {
            var key = new BootstrapManifestTrustedKey
            {
                KeyId = element.GetProperty("KeyId").GetString() ?? string.Empty,
                SubjectPublicKeyInfo = element.GetProperty("SubjectPublicKeyInfo").GetString() ?? string.Empty,
                NotBeforeSequence = element.GetProperty("NotBeforeSequence").GetInt64(),
                NotAfterSequence = element.TryGetProperty("NotAfterSequence", out JsonElement end) ? end.GetInt64() : 0,
            };
            if (key.KeyId.Length is < 3 or > 64 || key.KeyId.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '.' and not '_' and not '-') || key.NotBeforeSequence < 1) throw new InvalidDataException("启动器轮换公钥元数据无效");
            byte[] publicKey = Convert.FromBase64String(key.SubjectPublicKeyInfo);
            using ECDsa verifier = ECDsa.Create(); verifier.ImportSubjectPublicKeyInfo(publicKey, out int read);
            if (read != publicKey.Length || verifier.KeySize != 256) throw new InvalidDataException("启动器轮换公钥格式无效");
            result.Add(key);
        }
        if (result.Count is < 1 or > 4 || result.Select(key => key.KeyId).Distinct(StringComparer.Ordinal).Count() != result.Count) throw new InvalidDataException("启动器轮换公钥列表无效");
        return result;
    }

    private static string ReadBounded(string path, long maximum)
    {
        if (!File.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0 || new FileInfo(path).Length > maximum) throw new InvalidDataException("启动器轮换信任文件无效");
        return File.ReadAllText(path);
    }
}
