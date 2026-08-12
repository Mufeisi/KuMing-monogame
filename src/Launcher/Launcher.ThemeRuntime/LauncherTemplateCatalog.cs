namespace Launcher.ThemeRuntime;

public static class LauncherTemplateCatalog
{
    public static LauncherSnapshot Create(LauncherTemplateKind kind)
    {
        bool wide = kind == LauncherTemplateKind.Widescreen;
        return new LauncherSnapshot
        {
            ProjectId = "builtin-" + kind.ToString().ToLowerInvariant(),
            ProjectName = kind switch { LauncherTemplateKind.Compact => "LyoCrystal 紧凑启动器", LauncherTemplateKind.Widescreen => "LyoCrystal 宽屏启动器", _ => "LyoCrystal 经典启动器" },
            Theme = new LauncherTheme
            {
                Template = kind,
                ServerListMode = wide ? ServerListMode.Sidebar : ServerListMode.Dropdown,
                CanvasWidth = wide ? 1180 : kind == LauncherTemplateKind.Compact ? 760 : 801,
                CanvasHeight = wide ? 680 : kind == LauncherTemplateKind.Compact ? 520 : 554,
                AccentColor = kind switch { LauncherTemplateKind.Compact => "#43A5D5", LauncherTemplateKind.Widescreen => "#B570E8", _ => "#D8A73A" },
            },
            DefaultMicro = new MicroEndpoint { Enabled = true, Address = "127.0.0.1", Port = 8080, BackupAddress = "127.0.0.1", BackupPort = 8081, User = "player" },
            Servers = new List<LauncherServer>
            {
                new() { Id = "s1", Group = "经典专区", Name = "传奇归来", Address = "127.0.0.1", Port = 7000, Status = ServerOperatingStatus.Busy },
                new() { Id = "s2", Group = "经典专区", Name = "绿色怀旧", Address = "127.0.0.1", Port = 7001, Status = ServerOperatingStatus.Normal },
                new() { Id = "s3", Group = "测试专区", Name = "版本预览", Address = "127.0.0.1", Port = 7002, Status = ServerOperatingStatus.Maintenance },
            },
            Announcements = new List<LauncherAnnouncement>
            {
                new() { Title = "欢迎来到 LyoCrystal", Summary = "启动核心完成后即可进入游戏，其余资源会在游戏过程中按需下载。", Date = "2026-08-11" },
                new() { Title = "微端入口支持自动切换", Summary = "主入口三秒不可达时，本次会话自动改用 GM 配置的备用入口。", Date = "2026-08-11" },
            },
        };
    }
}
