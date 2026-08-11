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
    public string ResourceDirectory { get; set; } = string.Empty;
    public string LauncherDirectory { get; set; } = string.Empty;

    public static GatewayProjectConfiguration? TryLoad(string baseDirectory)
    {
        string path = Path.Combine(baseDirectory, "gateway-project.json");
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > 64 * 1024) return null;
            GatewayProjectConfiguration? value = JsonSerializer.Deserialize(File.ReadAllBytes(path), GatewayProjectJsonContext.Default.GatewayProjectConfiguration);
            if (value?.Format != "lyocrystal-micro-gateway-project-v1" || value.Port is < 1 or > 65535 || string.IsNullOrWhiteSpace(value.User)) return null;
            return value;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return null; }
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
        byte[] envelope = File.ReadAllBytes(path);
        if (envelope.Length > 1024) throw new InvalidDataException("微端凭据导入材料超过大小限制");
        string code = MicroCredentialEnvelope.Open(ProjectId, envelope);
        ProtectedClientSecretStore.WriteMicroCode(ProjectId, code);
        try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(GatewayProjectConfiguration))]
internal sealed partial class GatewayProjectJsonContext : JsonSerializerContext;
