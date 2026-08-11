using System.Text.Json.Serialization;
using Launcher.ThemeRuntime;

namespace LyoCrystal.LauncherEditor;

public sealed class EditorProject
{
    public const string CurrentFormat = "lyocrystal-launcher-editor-project-v1";
    public string Format { get; set; } = CurrentFormat;
    public LauncherSnapshot Snapshot { get; set; } = LauncherTemplateCatalog.Create(LauncherTemplateKind.Classic);
    public BrandMetadata Brand { get; set; } = new();
    public GatewayDeploymentSettings Gateway { get; set; } = new();
    public string ImportedClientDirectory { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public void SynchronizeMicroIdentity()
    {
        Gateway.User = Snapshot.DefaultMicro.User;
        foreach (LauncherServer server in Snapshot.Servers)
            if (server.MicroOverride is not null) server.MicroOverride.User = Snapshot.DefaultMicro.User;
    }
}

public sealed class BrandMetadata
{
    public string OutputFileName { get; set; } = "传奇登录器.exe";
    public string ProductName { get; set; } = "传奇登录器";
    public string FileDescription { get; set; } = "LyoCrystal 玩家入口";
    public string CompanyName { get; set; } = string.Empty;
    public string Copyright { get; set; } = string.Empty;
    public string FileVersion { get; set; } = "1.0.0.0";
    public string ProductVersion { get; set; } = "1.0.0.0";
    public string WindowTitle { get; set; } = "传奇登录器";
    public string TaskbarName { get; set; } = "传奇登录器";
    public string IconPath { get; set; } = string.Empty;
}

public sealed class GatewayDeploymentSettings
{
    public string ListenAddress { get; set; } = "0.0.0.0";
    public int Port { get; set; } = 8080;
    public string User { get; set; } = "player";
    public string ResourceDirectory { get; set; } = string.Empty;
}

public sealed record ImportPreview(IReadOnlyList<string> MappedFields, IReadOnlyList<string> UnknownFields, bool SensitiveValuesOmitted);

[JsonSourceGenerationOptions(WriteIndented = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(EditorProject))]
internal sealed partial class EditorProjectJsonContext : JsonSerializerContext;
