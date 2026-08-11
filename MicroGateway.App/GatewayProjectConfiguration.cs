using System.Text.Json;
using System.Text.Json.Serialization;
using Shared.Security;

namespace LyoCrystal.MicroGateway.App;

internal sealed class GatewayProjectConfiguration
{
    public string Format { get; set; } = "lyocrystal-micro-gateway-project-v1";
    public string ProjectId { get; set; } = string.Empty;
    public string ListenAddress { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 7000;
    public string User { get; set; } = "MicroUser";
    public string ResourceVersion { get; set; } = string.Empty;
    public string SigningIdentity { get; set; } = string.Empty;
    public string ResourceDirectory { get; set; } = string.Empty;
    public string LauncherDirectory { get; set; } = string.Empty;
    public int MemoryCacheMb { get; set; } = 128;
    public int DiskCacheMb { get; set; } = 2048;
    public string CacheDirectory { get; set; } = string.Empty;
    public List<BootstrapManifestTrustedKey> TrustedReleaseKeys { get; set; } = new();

    public static GatewayProjectConfiguration? TryLoad(string baseDirectory)
    {
        string path = Path.Combine(baseDirectory, "gateway-project.json");
        try
        {
            if (!File.Exists(path)) return null;
            GatewayProjectConfiguration? value = JsonSerializer.Deserialize(ReadLimitedFile(path, 64 * 1024), GatewayProjectJsonContext.Default.GatewayProjectConfiguration);
            if (value?.Format != "lyocrystal-micro-gateway-project-v1" || value.Port is < 1 or > 65535 || string.IsNullOrWhiteSpace(value.User)) return null;
            return value;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    public void Save(string baseDirectory)
    {
        string target = Path.Combine(baseDirectory, "gateway-project.json");
        string temporary = target + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(this, GatewayProjectJsonContext.Default.GatewayProjectConfiguration));
            File.Move(temporary, target, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public string ResolveOptionalDirectory(string baseDirectory, string configured)
    {
        if (string.IsNullOrWhiteSpace(configured)) return string.Empty;
        return Path.GetFullPath(Path.IsPathRooted(configured) ? configured : Path.Combine(baseDirectory, configured));
    }

    public void ImportSecretIfPresent(string baseDirectory)
    {
        string path = Path.Combine(baseDirectory, "gateway-secret.import");
        if (!File.Exists(path)) return;
        byte[] envelope = ReadLimitedFile(path, 1024);
        string code = MicroCredentialEnvelope.Open(ProjectId, envelope);
        ProtectedClientSecretStore.WriteMicroCode(ProjectId, code);
        try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    public string ReadSecret(string baseDirectory, bool serviceMode)
    {
        if (!serviceMode) return ProtectedClientSecretStore.ReadMicroCode(ProjectId);
        string path = Path.Combine(baseDirectory, "gateway-secret.service");
        if (!File.Exists(path)) return string.Empty;
        try
        {
            byte[] protectedBytes = ReadLimitedFile(path, 4096);
            return System.Text.Encoding.UTF8.GetString(System.Security.Cryptography.ProtectedData.Unprotect(
                protectedBytes, System.Text.Encoding.UTF8.GetBytes(ProjectId), System.Security.Cryptography.DataProtectionScope.LocalMachine));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException) { return string.Empty; }
    }

    public void WriteServiceSecret(string baseDirectory, string code)
    {
        if (string.IsNullOrWhiteSpace(ProjectId)) throw new InvalidDataException("项目标识无效");
        string target = Path.Combine(baseDirectory, "gateway-secret.service");
        string temporary = target + ".tmp-" + Guid.NewGuid().ToString("N");
        byte[] protectedBytes = System.Security.Cryptography.ProtectedData.Protect(
            System.Text.Encoding.UTF8.GetBytes(code ?? string.Empty), System.Text.Encoding.UTF8.GetBytes(ProjectId),
            System.Security.Cryptography.DataProtectionScope.LocalMachine);
        try
        {
            File.WriteAllBytes(temporary, protectedBytes);
            File.SetAttributes(temporary, FileAttributes.Hidden);
            WindowsGatewayOperations.ProtectServiceSecret(temporary);
            File.Move(temporary, target, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static byte[] ReadLimitedFile(string path, int maximumBytes)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("配置文件不能是重解析点。");
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (input.Length < 0 || input.Length > maximumBytes) throw new InvalidDataException("配置文件超过大小限制。");
        byte[] bytes = new byte[checked((int)input.Length)];
        input.ReadExactly(bytes);
        return bytes;
    }
}

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(GatewayProjectConfiguration))]
internal sealed partial class GatewayProjectJsonContext : JsonSerializerContext;
