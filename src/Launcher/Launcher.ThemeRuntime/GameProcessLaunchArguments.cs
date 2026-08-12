namespace Launcher.ThemeRuntime;

public static class GameProcessLaunchArguments
{
    public static IReadOnlyList<string> Create(LauncherServer server, MicroEndpoint micro, ClientLaunchCapability capability)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(micro);
        if (capability != ClientLaunchCapability.Current15Arguments) throw new InvalidOperationException("客户端不支持安全启动协议");
        var arguments = new List<string>
        {
            "--game-instance", "--server-address", server.Address,
            "--server-port", server.Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--micro-enabled", micro.Enabled.ToString().ToLowerInvariant(),
            "--micro-address", micro.Enabled ? micro.Address : string.Empty,
            "--micro-port", (micro.Enabled ? micro.Port : 0).ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
        arguments.Add("--micro-backup-address");
        arguments.Add(micro.Enabled ? micro.BackupAddress : string.Empty);
        arguments.Add("--micro-backup-port");
        arguments.Add((micro.Enabled ? micro.BackupPort : 0).ToString(System.Globalization.CultureInfo.InvariantCulture));
        return arguments;
    }
}
