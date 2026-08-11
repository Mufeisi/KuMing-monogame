namespace Launcher.ThemeRuntime;

public enum LauncherSettingEditorKind { Boolean, Integer, Choice }

public sealed class LauncherSettingDescriptor
{
    public required string Key { get; init; }
    public required string Label { get; init; }
    public required LauncherSettingEditorKind Editor { get; init; }
    public int Minimum { get; init; }
    public int Maximum { get; init; }
    public Func<IReadOnlyList<object>> Options { get; init; } = () => Array.Empty<object>();
    public Func<LauncherPlayerSettings, object> Read { get; init; } = null!;
    public Action<LauncherPlayerSettings, object> Write { get; init; } = null!;
}

/// <summary>玩家入口和后续 GM 编辑器共用的实际设置能力注册表。</summary>
public static class LauncherSettingsRegistry
{
    public static IReadOnlyList<LauncherSettingDescriptor> All { get; } = new LauncherSettingDescriptor[]
    {
        new() { Key = "resolution", Label = "分辨率", Editor = LauncherSettingEditorKind.Choice, Options = () => DisplayModeCatalog.GetSupportedModes().Cast<object>().ToArray(), Read = value => DisplayModeCatalog.GetSupportedModes().FirstOrDefault(mode => mode.Width == value.Resolution), Write = (value, selected) => value.Resolution = selected is LauncherDisplayMode mode ? mode.Width : 1024 },
        new() { Key = "fullScreen", Label = "全屏游戏", Editor = LauncherSettingEditorKind.Boolean, Read = value => value.FullScreen, Write = (value, selected) => value.FullScreen = (bool)selected },
        new() { Key = "borderless", Label = "无边框窗口", Editor = LauncherSettingEditorKind.Boolean, Read = value => value.Borderless, Write = (value, selected) => value.Borderless = (bool)selected },
        new() { Key = "fpsCap", Label = "限制帧率", Editor = LauncherSettingEditorKind.Boolean, Read = value => value.FpsCap, Write = (value, selected) => value.FpsCap = (bool)selected },
        new() { Key = "maxFps", Label = "最高帧率", Editor = LauncherSettingEditorKind.Integer, Minimum = 30, Maximum = 240, Read = value => value.MaxFps, Write = (value, selected) => value.MaxFps = Convert.ToInt32(selected, System.Globalization.CultureInfo.InvariantCulture) },
        new() { Key = "topMost", Label = "窗口置顶", Editor = LauncherSettingEditorKind.Boolean, Read = value => value.TopMost, Write = (value, selected) => value.TopMost = (bool)selected },
        new() { Key = "autoStart", Label = "自动开始", Editor = LauncherSettingEditorKind.Boolean, Read = value => value.AutoStart, Write = (value, selected) => value.AutoStart = (bool)selected },
        new() { Key = "volume", Label = "音效音量", Editor = LauncherSettingEditorKind.Integer, Minimum = 0, Maximum = 100, Read = value => value.Volume, Write = (value, selected) => value.Volume = Convert.ToInt32(selected, System.Globalization.CultureInfo.InvariantCulture) },
        new() { Key = "musicVolume", Label = "音乐音量", Editor = LauncherSettingEditorKind.Integer, Minimum = 0, Maximum = 100, Read = value => value.MusicVolume, Write = (value, selected) => value.MusicVolume = Convert.ToInt32(selected, System.Globalization.CultureInfo.InvariantCulture) },
        new() { Key = "microCacheLimitMb", Label = "微端响应缓存上限(MiB)", Editor = LauncherSettingEditorKind.Integer, Minimum = 256, Maximum = 16384, Read = value => value.MicroCacheLimitMb, Write = (value, selected) => value.MicroCacheLimitMb = Convert.ToInt32(selected, System.Globalization.CultureInfo.InvariantCulture) },
        new() { Key = "advancedLogs", Label = "高级诊断日志", Editor = LauncherSettingEditorKind.Boolean, Read = value => value.AdvancedLogs, Write = (value, selected) => value.AdvancedLogs = (bool)selected },
    };
}
