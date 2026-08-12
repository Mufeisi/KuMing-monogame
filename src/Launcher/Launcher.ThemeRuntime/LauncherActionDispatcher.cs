using System.Diagnostics;

namespace Launcher.ThemeRuntime;

public sealed class LauncherActionDispatcher
{
    private readonly Action<Uri> _openWeb;
    private readonly Action<LauncherAction> _invokeLocal;

    public LauncherActionDispatcher(Action<Uri>? openWeb = null, Action<LauncherAction>? invokeLocal = null)
    {
        _openWeb = openWeb ?? (uri => Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }));
        _invokeLocal = invokeLocal ?? (_ => { });
    }

    public void Execute(LauncherAction action, string? url = null)
    {
        if (!Enum.IsDefined(action)) throw new InvalidOperationException("启动器动作不在安全白名单中");
        if (IsWebAction(action))
        {
            if (!TryGetHttpUri(url, out Uri? uri)) throw new InvalidDataException("外部动作只允许 HTTP/HTTPS 地址");
            _openWeb(uri!);
            return;
        }
        _invokeLocal(action);
    }

    public static bool IsWebAction(LauncherAction action) => action is LauncherAction.OpenAnnouncementLink or LauncherAction.RegisterAccount or LauncherAction.ChangePassword or LauncherAction.RecoverPassword or LauncherAction.OfficialWebsite or LauncherAction.Recharge or LauncherAction.CustomerService;

    public static bool TryGetHttpUri(string? value, out Uri? uri)
    {
        bool valid = Uri.TryCreate(value, UriKind.Absolute, out uri) && uri.Scheme is "http" or "https";
        if (!valid) uri = null;
        return valid;
    }
}
