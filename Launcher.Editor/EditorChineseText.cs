using System.ComponentModel;
using System.Globalization;
using Launcher.ThemeRuntime;

namespace LyoCrystal.LauncherEditor;

internal sealed record ChineseChoice<T>(T Value, string Text)
{
    public override string ToString() => Text;
}

internal static class EditorChineseText
{
    public static string Template(LauncherTemplateKind value) => value switch
    {
        LauncherTemplateKind.Classic => "经典布局",
        LauncherTemplateKind.Compact => "紧凑下拉布局",
        LauncherTemplateKind.Widescreen => "宽屏侧栏布局",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    public static string ServerList(ServerListMode value) => value switch
    {
        ServerListMode.Dropdown => "下拉选择",
        ServerListMode.Sidebar => "侧边栏展开",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    public static string Announcement(AnnouncementDisplayMode value) => value switch
    {
        AnnouncementDisplayMode.NativeCards => "内置公告卡片",
        AnnouncementDisplayMode.ExternalPage => "外部网页公告",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    public static string Delivery(ClientDeliveryMode value) => value switch
    {
        ClientDeliveryMode.MicroOnDemand => "微端按需下载（推荐）",
        ClientDeliveryMode.FullClient => "完整客户端下载",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    public static string Update(PlayerUpdateMode value) => value switch
    {
        PlayerUpdateMode.None => "不更新玩家入口",
        PlayerUpdateMode.Normal => "普通更新（失败仍可进入）",
        PlayerUpdateMode.Required => "强制更新（不兼容时阻止进入）",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    public static string ServerStatus(ServerOperatingStatus value) => value switch
    {
        ServerOperatingStatus.Normal => "正常",
        ServerOperatingStatus.Busy => "火爆",
        ServerOperatingStatus.Recommended => "推荐",
        ServerOperatingStatus.NewServer => "新区",
        ServerOperatingStatus.Maintenance => "维护",
        ServerOperatingStatus.ComingSoon => "即将开放",
        ServerOperatingStatus.Hidden => "隐藏",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    public static string Action(LauncherAction value) => value switch
    {
        LauncherAction.LaunchGame => "进入游戏",
        LauncherAction.OpenSettings => "打开游戏设置",
        LauncherAction.OpenAnnouncementLink => "打开公告链接",
        LauncherAction.DiagnoseServer => "检测服务器连接",
        LauncherAction.Minimize => "最小化启动器",
        LauncherAction.Close => "关闭启动器",
        LauncherAction.RegisterAccount => "注册账号",
        LauncherAction.ChangePassword => "修改密码",
        LauncherAction.RecoverPassword => "找回密码",
        LauncherAction.OfficialWebsite => "官方网站",
        LauncherAction.Recharge => "游戏充值",
        LauncherAction.CustomerService => "联系客服",
        LauncherAction.CheckUpdate => "检查更新",
        LauncherAction.RepairClient => "修复客户端",
        LauncherAction.ChooseClient => "选择客户端",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    public static string Control(LauncherControlId value) => value switch
    {
        LauncherControlId.ServerList => "区服列表",
        LauncherControlId.Announcements => "公告区域",
        LauncherControlId.LaunchButton => "进入游戏按钮",
        LauncherControlId.OverallProgress => "总体进度条",
        LauncherControlId.CurrentProgress => "当前文件进度条",
        LauncherControlId.ProgressText => "进度文字",
        LauncherControlId.SettingsButton => "游戏设置按钮",
        LauncherControlId.DiagnoseButton => "连接检测按钮",
        LauncherControlId.ChooseClientButton => "选择客户端按钮",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    public static ChineseChoice<T>[] Choices<T>(IEnumerable<T> values, Func<T, string> translate) => values.Select(value => new ChineseChoice<T>(value, translate(value))).ToArray();
}

internal abstract class ChineseEnumConverter<T> : EnumConverter where T : struct, Enum
{
    protected ChineseEnumConverter() : base(typeof(T)) { }
    protected abstract string Translate(T value);
    public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
        => destinationType == typeof(string) && value is T typed ? Translate(typed) : base.ConvertTo(context, culture, value, destinationType);
    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
    {
        if (value is string text)
            foreach (T item in Enum.GetValues<T>()) if (string.Equals(Translate(item), text, StringComparison.Ordinal)) return item;
        return base.ConvertFrom(context, culture, value);
    }
}

internal sealed class TemplateKindChineseConverter : ChineseEnumConverter<LauncherTemplateKind> { protected override string Translate(LauncherTemplateKind value) => EditorChineseText.Template(value); }
internal sealed class ServerListModeChineseConverter : ChineseEnumConverter<ServerListMode> { protected override string Translate(ServerListMode value) => EditorChineseText.ServerList(value); }
internal sealed class AnnouncementModeChineseConverter : ChineseEnumConverter<AnnouncementDisplayMode> { protected override string Translate(AnnouncementDisplayMode value) => EditorChineseText.Announcement(value); }
internal sealed class DeliveryModeChineseConverter : ChineseEnumConverter<ClientDeliveryMode> { protected override string Translate(ClientDeliveryMode value) => EditorChineseText.Delivery(value); }
internal sealed class UpdateModeChineseConverter : ChineseEnumConverter<PlayerUpdateMode> { protected override string Translate(PlayerUpdateMode value) => EditorChineseText.Update(value); }

internal sealed class ChineseBooleanConverter : BooleanConverter
{
    public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
        => destinationType == typeof(string) && value is bool boolean ? boolean ? "是" : "否" : base.ConvertTo(context, culture, value, destinationType);
    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
        => value is string text && text is "是" or "否" ? text == "是" : base.ConvertFrom(context, culture, value);
}
