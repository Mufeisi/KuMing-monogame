using System.Text.Json.Serialization;
using Launcher.ThemeRuntime;

namespace LyoCrystal.LauncherEditor;

public enum PlayerUpdateMode { None, Normal, Required }
public enum ClientDeliveryMode { MicroOnDemand, FullClient }

public sealed class EditorProjectCreationOptions
{
    public string ProjectId { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;
    public LauncherTemplateKind Template { get; set; }
    public ClientDeliveryMode DeliveryMode { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public string RemoteReleaseBaseUrl { get; set; } = string.Empty;
    public string ImportedClientDirectory { get; set; } = string.Empty;
    public string ServerAddress { get; set; } = "127.0.0.1";
    public int ServerPort { get; set; } = 7000;
    public string MicroAddress { get; set; } = "127.0.0.1";
    public int MicroPort { get; set; } = 8080;
    public string BackupMicroAddress { get; set; } = string.Empty;
    public int BackupMicroPort { get; set; }
    public int Resolution { get; set; } = 1024;
    public bool FullScreen { get; set; }
    public string AnnouncementTitle { get; set; } = "欢迎公告";
    public string AnnouncementSummary { get; set; } = "欢迎进入游戏。";
    public PlayerUpdateMode PlayerUpdateMode { get; set; }
    public string GatewayCacheDirectory { get; set; } = "Cache";
    public int GatewayMemoryCacheMb { get; set; } = 128;
    public int GatewayDiskCacheMb { get; set; } = 2048;
}

public sealed class EditorProject
{
    public const string CurrentFormat = "lyocrystal-launcher-editor-project-v1";
    public string Format { get; set; } = CurrentFormat;
    public LauncherSnapshot Snapshot { get; set; } = LauncherTemplateCatalog.Create(LauncherTemplateKind.Classic);
    public BrandMetadata Brand { get; set; } = new();
    public GatewayDeploymentSettings Gateway { get; set; } = new();
    public ProjectReleaseMetadata Release { get; set; } = new();
    public string ImportedClientDirectory { get; set; } = string.Empty;
    public bool OptimizeImportedImages { get; set; } = true;
    public ClientDeliveryMode DeliveryMode { get; set; }
    public bool RegenerateMicroUserOnFirstLoad { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public void SynchronizeMicroIdentity()
    {
        if (string.IsNullOrWhiteSpace(Snapshot.DefaultMicro.ResourceVersion)) Snapshot.DefaultMicro.ResourceVersion = Snapshot.ProjectId;
        if (string.IsNullOrWhiteSpace(Snapshot.DefaultMicro.SigningIdentity)) Snapshot.DefaultMicro.SigningIdentity = Release.CurrentKeyId;
        Gateway.User = Snapshot.DefaultMicro.User;
        foreach (LauncherServer server in Snapshot.Servers)
            if (server.MicroOverride is not null)
            {
                server.MicroOverride.User = Snapshot.DefaultMicro.User;
                if (string.IsNullOrWhiteSpace(server.MicroOverride.ResourceVersion)) server.MicroOverride.ResourceVersion = Snapshot.DefaultMicro.ResourceVersion;
                if (string.IsNullOrWhiteSpace(server.MicroOverride.SigningIdentity)) server.MicroOverride.SigningIdentity = Snapshot.DefaultMicro.SigningIdentity;
            }
    }
}

public sealed class ProjectReleaseMetadata
{
    public long NextSequence { get; set; } = 1;
    public string LastPublishRoot { get; set; } = string.Empty;
    public PlayerUpdateMode PlayerUpdateMode { get; set; }
    public string PlayerUpdateFile { get; set; } = string.Empty;
    public string PlayerUpdateVersion { get; set; } = "1.0.0.0";
    public string CurrentKeyId { get; set; } = string.Empty;
    public string CurrentPublicKey { get; set; } = string.Empty;
    public long CurrentKeyNotBeforeSequence { get; set; } = 1;
    public string NextKeyId { get; set; } = string.Empty;
    public string NextPublicKey { get; set; } = string.Empty;
    public long NextKeyNotBeforeSequence { get; set; } = 1;
    public List<Shared.Security.BootstrapManifestTrustedKey> RetiredPublicKeys { get; set; } = new();
    public List<ProjectReleaseHistoryItem> History { get; set; } = new();
}

public sealed class ProjectReleaseHistoryItem
{
    public long Sequence { get; set; }
    public string VersionName { get; set; } = string.Empty;
    public string CreatedAtUtc { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public string ContentSha256 { get; set; } = string.Empty;
    public long? RolledBackFromSequence { get; set; }
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
    public string CacheDirectory { get; set; } = "Cache";
    public int MemoryCacheMb { get; set; } = 128;
    public int DiskCacheMb { get; set; } = 2048;
}

public sealed record ImportPreview(IReadOnlyList<string> MappedFields, IReadOnlyList<string> UnknownFields, bool SensitiveValuesOmitted);

[JsonSourceGenerationOptions(WriteIndented = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(EditorProject))]
[JsonSerializable(typeof(LauncherTheme))]
internal sealed partial class EditorProjectJsonContext : JsonSerializerContext;
