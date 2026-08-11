using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LyoCrystal.LauncherEditor;

public static class ProjectReleaseKeyStore
{
    private const int RecoveryIterations = 600_000;

    public static void EnsureProvisioned(EditorProject project, string projectRoot)
    {
        string secrets = PrepareSecretsDirectory(projectRoot);
        if (string.IsNullOrWhiteSpace(project.Release.CurrentKeyId) && string.IsNullOrWhiteSpace(project.Release.NextKeyId))
        {
            (project.Release.CurrentKeyId, project.Release.CurrentPublicKey) = CreateKey(project.Snapshot.ProjectId, "current", secrets);
            (project.Release.NextKeyId, project.Release.NextPublicKey) = CreateKey(project.Snapshot.ProjectId, "next", secrets);
            project.Release.CurrentKeyNotBeforeSequence = 1;
            project.Release.NextKeyNotBeforeSequence = 1;
            project.Release.NextSequence = Math.Max(1, project.Release.NextSequence);
        }
        ValidateMetadata(project.Release);
    }

    public static bool HasPrivateKeys(EditorProject project, string projectRoot)
    {
        string secrets = PrepareSecretsDirectory(projectRoot);
        return File.Exists(PrivateKeyPath(secrets, project.Release.CurrentKeyId)) && File.Exists(PrivateKeyPath(secrets, project.Release.NextKeyId));
    }

    public static byte[] LoadCurrentPrivateKey(EditorProject project, string projectRoot)
    {
        ValidateMetadata(project.Release);
        return Unprotect(project.Snapshot.ProjectId, project.Release.CurrentKeyId, PrivateKeyPath(PrepareSecretsDirectory(projectRoot), project.Release.CurrentKeyId));
    }

    public static void Rotate(EditorProject project, string projectRoot)
    {
        EnsureProvisioned(project, projectRoot);
        string secrets = PrepareSecretsDirectory(projectRoot);
        project.Release.RetiredPublicKeys.Add(new Shared.Security.BootstrapManifestTrustedKey
        {
            KeyId = project.Release.CurrentKeyId,
            SubjectPublicKeyInfo = project.Release.CurrentPublicKey,
            NotBeforeSequence = project.Release.CurrentKeyNotBeforeSequence,
            NotAfterSequence = Math.Max(1, project.Release.NextSequence - 1),
        });
        project.Release.CurrentKeyId = project.Release.NextKeyId;
        project.Release.CurrentPublicKey = project.Release.NextPublicKey;
        project.Release.CurrentKeyNotBeforeSequence = project.Release.NextKeyNotBeforeSequence;
        (project.Release.NextKeyId, project.Release.NextPublicKey) = CreateKey(project.Snapshot.ProjectId, "next", secrets);
        project.Release.NextKeyNotBeforeSequence = checked(project.Release.NextSequence + 1);
    }

    public static void ExportRecovery(EditorProject project, string projectRoot, string passphrase, string outputPath)
    {
        ValidatePassphrase(passphrase);
        byte[] current = LoadPrivate(project, projectRoot, project.Release.CurrentKeyId);
        byte[] next = LoadPrivate(project, projectRoot, project.Release.NextKeyId);
        byte[] plain = JsonSerializer.SerializeToUtf8Bytes(new ProjectRecoveryPayload
        {
            ProjectId = project.Snapshot.ProjectId,
            CurrentKeyId = project.Release.CurrentKeyId,
            CurrentPublicKey = project.Release.CurrentPublicKey,
            CurrentPrivateKey = Convert.ToBase64String(current),
            NextKeyId = project.Release.NextKeyId,
            NextPublicKey = project.Release.NextPublicKey,
            NextPrivateKey = Convert.ToBase64String(next),
        }, ProjectRecoveryJsonContext.Default.ProjectRecoveryPayload);
        byte[] salt = RandomNumberGenerator.GetBytes(16), nonce = RandomNumberGenerator.GetBytes(12), tag = new byte[16], cipher = new byte[plain.Length];
        byte[] key = Rfc2898DeriveBytes.Pbkdf2(passphrase, salt, RecoveryIterations, HashAlgorithmName.SHA256, 32);
        string output = Path.GetFullPath(outputPath);
        if (File.Exists(output)) throw new IOException("项目恢复包已存在，拒绝覆盖");
        try
        {
            using (var aes = new AesGcm(key, tag.Length)) aes.Encrypt(nonce, plain, cipher, tag, Encoding.UTF8.GetBytes(project.Snapshot.ProjectId));
            byte[] json = JsonSerializer.SerializeToUtf8Bytes(new ProjectRecoveryEnvelope
            {
                ProjectId = project.Snapshot.ProjectId, Iterations = RecoveryIterations,
                Salt = Convert.ToBase64String(salt), Nonce = Convert.ToBase64String(nonce), Tag = Convert.ToBase64String(tag), Ciphertext = Convert.ToBase64String(cipher),
            }, ProjectRecoveryJsonContext.Default.ProjectRecoveryEnvelope);
            WriteAtomic(output, json);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(current); CryptographicOperations.ZeroMemory(next); CryptographicOperations.ZeroMemory(plain); CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(cipher);
        }
    }

    public static void ImportRecovery(EditorProject project, string projectRoot, string passphrase, string inputPath)
    {
        ValidatePassphrase(passphrase);
        string input = Path.GetFullPath(inputPath);
        if (!File.Exists(input) || new FileInfo(input).Length > 64 * 1024) throw new InvalidDataException("项目恢复包不存在或超过大小限制");
        ProjectRecoveryEnvelope envelope = JsonSerializer.Deserialize(File.ReadAllBytes(input), ProjectRecoveryJsonContext.Default.ProjectRecoveryEnvelope) ?? throw new InvalidDataException("项目恢复包为空");
        if (envelope.ProjectId != project.Snapshot.ProjectId || envelope.Iterations != RecoveryIterations) throw new InvalidDataException("项目恢复包与当前项目不匹配");
        byte[] salt = Convert.FromBase64String(envelope.Salt), nonce = Convert.FromBase64String(envelope.Nonce), tag = Convert.FromBase64String(envelope.Tag), cipher = Convert.FromBase64String(envelope.Ciphertext);
        byte[] key = Rfc2898DeriveBytes.Pbkdf2(passphrase, salt, envelope.Iterations, HashAlgorithmName.SHA256, 32);
        byte[] plain = new byte[cipher.Length];
        try
        {
            using (var aes = new AesGcm(key, tag.Length)) aes.Decrypt(nonce, cipher, tag, plain, Encoding.UTF8.GetBytes(project.Snapshot.ProjectId));
            ProjectRecoveryPayload payload = JsonSerializer.Deserialize(plain, ProjectRecoveryJsonContext.Default.ProjectRecoveryPayload) ?? throw new InvalidDataException("项目恢复载荷为空");
            if (payload.ProjectId != project.Snapshot.ProjectId || payload.CurrentKeyId != project.Release.CurrentKeyId || payload.NextKeyId != project.Release.NextKeyId ||
                payload.CurrentPublicKey != project.Release.CurrentPublicKey || payload.NextPublicKey != project.Release.NextPublicKey) throw new InvalidDataException("项目恢复密钥身份不匹配");
            string secrets = PrepareSecretsDirectory(projectRoot);
            WriteProtectedRecovered(project.Snapshot.ProjectId, payload.CurrentKeyId, payload.CurrentPrivateKey, payload.CurrentPublicKey, secrets);
            WriteProtectedRecovered(project.Snapshot.ProjectId, payload.NextKeyId, payload.NextPrivateKey, payload.NextPublicKey, secrets);
        }
        catch (CryptographicException ex) { throw new InvalidDataException("项目恢复密码错误或恢复包已损坏", ex); }
        finally { CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(plain); CryptographicOperations.ZeroMemory(cipher); }
    }

    private static byte[] LoadPrivate(EditorProject project, string projectRoot, string keyId) => Unprotect(project.Snapshot.ProjectId, keyId, PrivateKeyPath(PrepareSecretsDirectory(projectRoot), keyId));

    private static (string KeyId, string PublicKey) CreateKey(string projectId, string slot, string secrets)
    {
        string keyId = projectId + "-" + slot + "-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant();
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        byte[] privateKey = key.ExportPkcs8PrivateKey();
        try { WriteProtected(projectId, keyId, privateKey, PrivateKeyPath(secrets, keyId)); }
        finally { CryptographicOperations.ZeroMemory(privateKey); }
        return (keyId, Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()));
    }

    private static void WriteProtectedRecovered(string projectId, string keyId, string privateBase64, string expectedPublic, string secrets)
    {
        byte[] privateKey = Convert.FromBase64String(privateBase64);
        try
        {
            using ECDsa key = ECDsa.Create(); key.ImportPkcs8PrivateKey(privateKey, out int read);
            if (read != privateKey.Length || Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()) != expectedPublic) throw new InvalidDataException("恢复私钥与项目公钥不匹配");
            string target = PrivateKeyPath(secrets, keyId);
            if (File.Exists(target)) { byte[] existing = Unprotect(projectId, keyId, target); try { if (!existing.AsSpan().SequenceEqual(privateKey)) throw new IOException("当前机器已有不同的项目私钥，拒绝覆盖"); return; } finally { CryptographicOperations.ZeroMemory(existing); } }
            WriteProtected(projectId, keyId, privateKey, target);
        }
        finally { CryptographicOperations.ZeroMemory(privateKey); }
    }

    private static void WriteProtected(string projectId, string keyId, byte[] privateKey, string path)
    {
        byte[] protectedKey = ProtectedData.Protect(privateKey, Entropy(projectId, keyId), DataProtectionScope.CurrentUser);
        try { WriteAtomic(path, protectedKey); } finally { CryptographicOperations.ZeroMemory(protectedKey); }
    }

    private static byte[] Unprotect(string projectId, string keyId, string path)
    {
        if (!File.Exists(path) || new FileInfo(path).Length > 4096) throw new InvalidDataException("项目签名私钥缺失，请导入项目恢复包");
        return ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy(projectId, keyId), DataProtectionScope.CurrentUser);
    }

    private static byte[] Entropy(string projectId, string keyId) => SHA256.HashData(Encoding.UTF8.GetBytes("LyoCrystal.LauncherProjectKey.v1:" + projectId + ":" + keyId));
    private static string PrivateKeyPath(string secrets, string keyId) => Path.Combine(secrets, keyId + ".dpapi");

    private static string PrepareSecretsDirectory(string projectRoot)
    {
        string root = Path.GetFullPath(projectRoot); if (!Directory.Exists(root)) throw new DirectoryNotFoundException("项目目录不存在");
        RejectReparse(root); string secrets = Path.Combine(root, ".secrets"); if (Directory.Exists(secrets)) RejectReparse(secrets); else Directory.CreateDirectory(secrets); RejectReparse(secrets); return secrets;
    }

    private static void ValidateMetadata(ProjectReleaseMetadata value)
    {
        if (value.NextSequence < 1 || value.CurrentKeyNotBeforeSequence < 1 || value.NextKeyNotBeforeSequence < 1 || !IsKeyId(value.CurrentKeyId) || !IsKeyId(value.NextKeyId) || value.CurrentKeyId == value.NextKeyId ||
            string.IsNullOrWhiteSpace(value.CurrentPublicKey) || string.IsNullOrWhiteSpace(value.NextPublicKey) ||
            value.RetiredPublicKeys.Count > 30 || value.RetiredPublicKeys.Any(key => !IsKeyId(key.KeyId) || string.IsNullOrWhiteSpace(key.SubjectPublicKeyInfo))) throw new InvalidDataException("项目发布密钥元数据无效");
    }
    private static bool IsKeyId(string value) => value.Length is >= 3 and <= 64 && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-');
    private static void ValidatePassphrase(string value) { if (string.IsNullOrWhiteSpace(value) || value.Length < 12 || value.Length > 256) throw new ArgumentException("恢复密码必须为 12 到 256 个字符"); }
    private static void RejectReparse(string path) { string full = Path.GetFullPath(path); string? current = Path.GetPathRoot(full); foreach (string part in full[(current?.Length ?? 0)..].Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) { if (part.Length == 0) continue; current = Path.Combine(current ?? string.Empty, part); if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("项目密钥路径不得经过重解析点"); } }
    private static void WriteAtomic(string path, byte[] bytes) { Directory.CreateDirectory(Path.GetDirectoryName(path)!); string temp = path + ".tmp-" + Guid.NewGuid().ToString("N"); try { using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough)) { stream.Write(bytes); stream.Flush(true); } File.Move(temp, path); } finally { if (File.Exists(temp)) File.Delete(temp); } }
}

public sealed class ProjectRecoveryEnvelope { public string ProjectId { get; set; } = string.Empty; public int Iterations { get; set; } public string Salt { get; set; } = string.Empty; public string Nonce { get; set; } = string.Empty; public string Tag { get; set; } = string.Empty; public string Ciphertext { get; set; } = string.Empty; }
public sealed class ProjectRecoveryPayload { public string ProjectId { get; set; } = string.Empty; public string CurrentKeyId { get; set; } = string.Empty; public string CurrentPublicKey { get; set; } = string.Empty; public string CurrentPrivateKey { get; set; } = string.Empty; public string NextKeyId { get; set; } = string.Empty; public string NextPublicKey { get; set; } = string.Empty; public string NextPrivateKey { get; set; } = string.Empty; }

[System.Text.Json.Serialization.JsonSourceGenerationOptions(WriteIndented = true, UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
[System.Text.Json.Serialization.JsonSerializable(typeof(ProjectRecoveryEnvelope))]
[System.Text.Json.Serialization.JsonSerializable(typeof(ProjectRecoveryPayload))]
internal sealed partial class ProjectRecoveryJsonContext : System.Text.Json.Serialization.JsonSerializerContext;
