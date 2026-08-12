using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Shared.Security;

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
        if (snapshot.WindowTitle.Length > 120 || snapshot.TaskbarName.Length > 120) throw new InvalidDataException("窗口标题或任务栏名称无效");
        if (!string.IsNullOrWhiteSpace(snapshot.RemoteReleaseBaseUrl) &&
            (!Uri.TryCreate(snapshot.RemoteReleaseBaseUrl, UriKind.Absolute, out Uri? releaseUri) || releaseUri.Scheme is not ("http" or "https")))
            throw new InvalidDataException("远程发布地址无效");
        if (snapshot.Theme is null || snapshot.Theme.CanvasWidth is < 640 or > 1920 || snapshot.Theme.CanvasHeight is < 420 or > 1080) throw new InvalidDataException("主题画布尺寸无效");
        if (!ColorPattern.IsMatch(snapshot.Theme.AccentColor ?? string.Empty)) throw new InvalidDataException("主题强调色无效");
        ValidateAssetPath(snapshot.Theme.BackgroundImage);
        ValidateAssetPath(snapshot.Theme.LaunchButtonImage);
        ValidateAssetPath(snapshot.Theme.LaunchButtonHoverImage);
        ValidateAssetPath(snapshot.Theme.LaunchButtonPressedImage);
        ValidateAssetPath(snapshot.Theme.LaunchButtonDisabledImage);
        if (snapshot.Theme.Controls is null || snapshot.Theme.Controls.Count > Enum.GetValues<LauncherControlId>().Length) throw new InvalidDataException("主题控件覆盖数量无效");
        var controlIds = new HashSet<LauncherControlId>();
        foreach (LauncherControlOverride control in snapshot.Theme.Controls)
        {
            if (!Enum.IsDefined(control.Id) || !controlIds.Add(control.Id) || control.X is < 0 or > 1919 || control.Y is < 0 or > 1079 || control.Width is < 1 or > 1920 || control.Height is < 1 or > 1080) throw new InvalidDataException("主题控件位置、尺寸或标识无效");
            if ((!string.IsNullOrEmpty(control.ForeColor) && !ColorPattern.IsMatch(control.ForeColor)) || (!string.IsNullOrEmpty(control.BackColor) && !ColorPattern.IsMatch(control.BackColor))) throw new InvalidDataException("主题控件颜色无效");
            if (control.FontName.Length > 64 || control.FontSize is < 0 or > 72 || control.FontSize is > 0 and < 6 || control.OpacityPercent is < 0 or > 100) throw new InvalidDataException("主题控件字体或透明度无效");
            ValidateAssetPath(control.BackgroundImage);
        }
        if (snapshot.Servers is null || snapshot.Servers.Count is < 1 or > 200) throw new InvalidDataException("区服数量必须为 1 到 200");
        ValidateMicro(snapshot.DefaultMicro);
        if (snapshot.LoginCoreResources is null || snapshot.LoginCoreResources.Count > 8) throw new InvalidDataException("登录核心资源清单无效");
        var corePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (LauncherCoreResource resource in snapshot.LoginCoreResources)
        {
            if (resource is null || string.IsNullOrWhiteSpace(resource.Path) || !corePaths.Add(resource.Path) || !Regex.IsMatch(resource.Path, "^Data/[A-Za-z0-9._-]+\\.Lib$", RegexOptions.CultureInvariant) ||
                resource.Size is < 1 or > 64L * 1024 * 1024 || !Regex.IsMatch(resource.Sha256 ?? string.Empty, "^[0-9a-f]{64}$", RegexOptions.CultureInvariant))
                throw new InvalidDataException("登录核心资源条目无效");
        }
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (LauncherServer server in snapshot.Servers)
        {
            if (server is null || !IdPattern.IsMatch(server.Id ?? string.Empty) || !ids.Add(server.Id!)) throw new InvalidDataException("区服标识无效或重复");
            if (string.IsNullOrWhiteSpace(server.Name) || server.Name.Length > 80 || server.SortOrder is < -100000 or > 100000 || !IsHost(server.Address) || server.Port is < 1 or > 65535) throw new InvalidDataException("区服连接信息无效");
            ValidateMicro(server.MicroOverride);
        }
        if (snapshot.Announcements is null || snapshot.Announcements.Count > 100) throw new InvalidDataException("公告数量超过上限");
        if (!Enum.IsDefined(snapshot.AnnouncementMode)) throw new InvalidDataException("公告显示模式无效");
        if (!string.IsNullOrWhiteSpace(snapshot.ExternalAnnouncementUrl) && (!Uri.TryCreate(snapshot.ExternalAnnouncementUrl, UriKind.Absolute, out Uri? announcementUri) || announcementUri.Scheme is not ("http" or "https"))) throw new InvalidDataException("外部公告地址无效");
        if (snapshot.AnnouncementMode == AnnouncementDisplayMode.ExternalPage && string.IsNullOrWhiteSpace(snapshot.ExternalAnnouncementUrl)) throw new InvalidDataException("外部公告模式必须设置 HTTP/HTTPS 地址");
        foreach (LauncherAnnouncement item in snapshot.Announcements)
        {
            if (item is null || string.IsNullOrWhiteSpace(item.Title) || item.Title.Length > 120 || item.Summary.Length > 1000) throw new InvalidDataException("公告内容无效");
            ValidateAssetPath(item.Image);
            if (!string.IsNullOrEmpty(item.ExternalUrl) && (!Uri.TryCreate(item.ExternalUrl, UriKind.Absolute, out Uri? uri) || uri.Scheme is not ("http" or "https"))) throw new InvalidDataException("公告链接无效");
        }
        if (snapshot.ActionLinks is null || snapshot.ActionLinks.Count > 12) throw new InvalidDataException("安全动作链接数量无效");
        foreach (LauncherActionLink link in snapshot.ActionLinks)
        {
            if (link is null || string.IsNullOrWhiteSpace(link.Text) || link.Text.Length > 24 || !LauncherActionDispatcher.IsWebAction(link.Action) || !LauncherActionDispatcher.TryGetHttpUri(link.Url, out _))
                throw new InvalidDataException("安全动作链接无效");
        }
        if (snapshot.TrustedReleaseKeys is null || snapshot.TrustedReleaseKeys.Count > 4) throw new InvalidDataException("启动器发布可信密钥数量无效");
        var releaseKeyIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (BootstrapManifestTrustedKey key in snapshot.TrustedReleaseKeys)
        {
            if (key is null || string.IsNullOrWhiteSpace(key.KeyId) || !releaseKeyIds.Add(key.KeyId) || key.NotBeforeSequence < 1 || key.NotAfterSequence > 0 && key.NotAfterSequence < key.NotBeforeSequence) throw new InvalidDataException("启动器发布可信密钥无效");
            try { using ECDsa ecdsa = ECDsa.Create(); byte[] spki = Convert.FromBase64String(key.SubjectPublicKeyInfo); ecdsa.ImportSubjectPublicKeyInfo(spki, out int read); if (read != spki.Length || ecdsa.KeySize != 256) throw new InvalidDataException("启动器发布公钥不是 P-256"); }
            catch (Exception ex) when (ex is FormatException or CryptographicException) { throw new InvalidDataException("启动器发布公钥无效", ex); }
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
