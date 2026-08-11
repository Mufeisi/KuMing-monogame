using System.Net;
using System.Text.RegularExpressions;

namespace Launcher.ThemeRuntime;

public static class LauncherSnapshotValidator
{
    private static readonly Regex IdPattern = new("^[A-Za-z0-9._-]{1,64}$", RegexOptions.CultureInvariant);
    private static readonly Regex ColorPattern = new("^#[0-9A-Fa-f]{6}$", RegexOptions.CultureInvariant);

    public static void Validate(LauncherSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!string.Equals(snapshot.Format, LauncherSnapshot.CurrentFormat, StringComparison.Ordinal)) throw new InvalidDataException("启动器快照格式不受支持");
        if (!IdPattern.IsMatch(snapshot.ProjectId ?? string.Empty)) throw new InvalidDataException("项目标识无效");
        if (string.IsNullOrWhiteSpace(snapshot.ProjectName) || snapshot.ProjectName.Length > 80) throw new InvalidDataException("项目名称无效");
        if (!string.IsNullOrWhiteSpace(snapshot.RemoteReleaseBaseUrl) &&
            (!Uri.TryCreate(snapshot.RemoteReleaseBaseUrl, UriKind.Absolute, out Uri? releaseUri) || releaseUri.Scheme is not ("http" or "https")))
            throw new InvalidDataException("远程发布地址无效");
        if (snapshot.Theme is null || snapshot.Theme.CanvasWidth is < 640 or > 1920 || snapshot.Theme.CanvasHeight is < 420 or > 1080) throw new InvalidDataException("主题画布尺寸无效");
        if (!ColorPattern.IsMatch(snapshot.Theme.AccentColor ?? string.Empty)) throw new InvalidDataException("主题强调色无效");
        ValidateAssetPath(snapshot.Theme.BackgroundImage);
        ValidateAssetPath(snapshot.Theme.LaunchButtonImage);
        if (snapshot.Servers is null || snapshot.Servers.Count is < 1 or > 200) throw new InvalidDataException("区服数量必须为 1 到 200");
        ValidateMicro(snapshot.DefaultMicro);
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (LauncherServer server in snapshot.Servers)
        {
            if (server is null || !IdPattern.IsMatch(server.Id ?? string.Empty) || !ids.Add(server.Id!)) throw new InvalidDataException("区服标识无效或重复");
            if (string.IsNullOrWhiteSpace(server.Name) || server.Name.Length > 80 || !IsHost(server.Address) || server.Port is < 1 or > 65535) throw new InvalidDataException("区服连接信息无效");
            ValidateMicro(server.MicroOverride);
        }
        if (snapshot.Announcements is null || snapshot.Announcements.Count > 100) throw new InvalidDataException("公告数量超过上限");
        foreach (LauncherAnnouncement item in snapshot.Announcements)
        {
            if (item is null || string.IsNullOrWhiteSpace(item.Title) || item.Title.Length > 120 || item.Summary.Length > 1000) throw new InvalidDataException("公告内容无效");
            ValidateAssetPath(item.Image);
            if (!string.IsNullOrEmpty(item.ExternalUrl) && (!Uri.TryCreate(item.ExternalUrl, UriKind.Absolute, out Uri? uri) || uri.Scheme is not ("http" or "https"))) throw new InvalidDataException("公告链接无效");
        }
        if (snapshot.Defaults.Resolution is not (1024 or 1280 or 1366 or 1920) || snapshot.Defaults.MaxFps is < 30 or > 240 ||
            snapshot.Defaults.Volume is < 0 or > 100 || snapshot.Defaults.MusicVolume is < 0 or > 100 || snapshot.Defaults.MicroCacheLimitMb is < 256 or > 16384)
            throw new InvalidDataException("玩家默认设置无效");
    }

    public static void ValidateProjectId(string? projectId)
    {
        if (!IdPattern.IsMatch(projectId ?? string.Empty)) throw new InvalidDataException("项目标识无效");
    }

    public static void ValidateMicro(MicroEndpoint? endpoint)
    {
        if (endpoint is null) return;
        if (!endpoint.Enabled) return;
        if (!IsHost(endpoint.Address) || endpoint.Port is < 1 or > 65535 || endpoint.User.Length > 128) throw new InvalidDataException("微端入口无效");
        bool hasBackup = !string.IsNullOrWhiteSpace(endpoint.BackupAddress) || endpoint.BackupPort != 0;
        if (hasBackup && (!IsHost(endpoint.BackupAddress) || endpoint.BackupPort is < 1 or > 65535)) throw new InvalidDataException("备用微端入口无效");
    }

    public static string ResolveAsset(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative)) return string.Empty;
        ValidateAssetPath(relative);
        string fullRoot = Path.GetFullPath(root);
        string full = Path.GetFullPath(Path.Combine(fullRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("主题资源越界");
        string current = fullRoot;
        foreach (string segment in Path.GetRelativePath(fullRoot, full).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            current = Path.Combine(current, segment);
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("主题资源路径不得经过重解析点");
        }
        return full;
    }

    private static void ValidateAssetPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        if (Path.IsPathRooted(path) || path.Contains("..", StringComparison.Ordinal) || path.Contains(':')) throw new InvalidDataException("主题资源路径无效");
        string extension = Path.GetExtension(path);
        if (!extension.Equals(".png", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("主题图片仅支持 PNG、BMP、JPG");
    }

    private static bool IsHost(string? value)
    {
        value = value?.Trim();
        if (string.IsNullOrEmpty(value) || value.Contains('/') || value.Contains('\\') || value.Contains("://", StringComparison.Ordinal)) return false;
        return Uri.CheckHostName(value) is not (UriHostNameType.Unknown or UriHostNameType.Basic) || IPAddress.TryParse(value, out _);
    }
}
