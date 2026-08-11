using System.Text.Json.Serialization;

namespace Launcher.ThemeRuntime;

public enum LauncherTemplateKind { Classic, Compact, Widescreen }
public enum ServerListMode { Dropdown, Sidebar }
public enum LauncherAction { LaunchGame, OpenSettings, OpenAnnouncementLink, DiagnoseServer, Minimize, Close }
public enum ServerOperatingStatus { Normal, Busy, Maintenance }

public sealed class LauncherSnapshot
{
    public const string CurrentFormat = "lyocrystal-launcher-snapshot-v1";
    public string Format { get; set; } = CurrentFormat;
    public string ProjectId { get; set; } = "default";
    public string ProjectName { get; set; } = "LyoCrystal";
    public string RemoteReleaseBaseUrl { get; set; } = string.Empty;
    public LauncherTheme Theme { get; set; } = new();
    public List<LauncherServer> Servers { get; set; } = new();
    public List<LauncherAnnouncement> Announcements { get; set; } = new();
    public LauncherPlayerSettings Defaults { get; set; } = new();
    public MicroEndpoint DefaultMicro { get; set; } = new();
}

public sealed class LauncherTheme
{
    public LauncherTemplateKind Template { get; set; } = LauncherTemplateKind.Classic;
    public ServerListMode ServerListMode { get; set; } = ServerListMode.Dropdown;
    public int CanvasWidth { get; set; } = 900;
    public int CanvasHeight { get; set; } = 600;
    public string AccentColor { get; set; } = "#D8A73A";
    public string BackgroundImage { get; set; } = string.Empty;
    public string LaunchButtonImage { get; set; } = string.Empty;
}

public sealed class LauncherServer
{
    public string Id { get; set; } = string.Empty;
    public string Group { get; set; } = "默认分组";
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 7000;
    public ServerOperatingStatus Status { get; set; } = ServerOperatingStatus.Normal;
    public MicroEndpoint? MicroOverride { get; set; }
}

public sealed class MicroEndpoint
{
    public bool Enabled { get; set; } = true;
    public string Address { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 8080;
    public string BackupAddress { get; set; } = string.Empty;
    public int BackupPort { get; set; }
    public string User { get; set; } = string.Empty;
}

public sealed class LauncherAnnouncement
{
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Date { get; set; } = string.Empty;
    public string Image { get; set; } = string.Empty;
    public string ExternalUrl { get; set; } = string.Empty;
}

public sealed class LauncherPlayerSettings
{
    public int Resolution { get; set; } = 1024;
    public bool FullScreen { get; set; }
    public bool Borderless { get; set; } = true;
    public bool FpsCap { get; set; } = true;
    public int MaxFps { get; set; } = 100;
    public int Volume { get; set; } = 100;
    public int MusicVolume { get; set; } = 100;
    public bool TopMost { get; set; } = true;
    public bool AutoStart { get; set; }
    public bool AdvancedLogs { get; set; }
    public int MicroCacheLimitMb { get; set; } = 2048;
}

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = false, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, WriteIndented = true)]
[JsonSerializable(typeof(LauncherSnapshot))]
[JsonSerializable(typeof(LauncherReleaseDescriptor))]
[JsonSerializable(typeof(LauncherProgressSnapshot))]
[JsonSerializable(typeof(LauncherPlayerSettings))]
public sealed partial class LauncherSnapshotJsonContext : JsonSerializerContext;

public sealed class LauncherReleaseDescriptor
{
    public string ResourceVersion { get; set; } = string.Empty;
    public List<LauncherReleaseFile> Files { get; set; } = new();
}

public sealed class LauncherReleaseFile
{
    public string Name { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
}
