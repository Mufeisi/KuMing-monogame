using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LyoCrystal.MicroGateway.App;

internal static class WindowsGatewayOperations
{
    public static void ConfigureNetwork(GatewayProjectConfiguration project, string interactiveUserSid)
    {
        string statePath = NetworkStatePath();
        if (IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(endpoint => endpoint.Port == project.Port))
            throw new InvalidOperationException($"端口 {project.Port} 已被占用，请先停止占用程序。");
        string host = project.ListenAddress is "0.0.0.0" or "*" or "+" ? "+" : project.ListenAddress;
        string url = $"http://{host}:{project.Port}/";
        string rule = FirewallRule(project);
        string serviceSid = ServiceSid(ServiceName(project.ProjectId));
        string interactiveSid = new SecurityIdentifier(interactiveUserSid).Value;
        string marker = "LyoOwner:" + ShortIdentity(project.ProjectId + "|" + project.Port + "|network");
        NetworkState state = LoadState(statePath) ?? new NetworkState(project.ProjectId, project.Port, url, rule, serviceSid, interactiveSid, marker, false, false, false);
        if (state.ProjectId != project.ProjectId || state.Port != project.Port || state.Url != url || state.FirewallRule != rule || state.ServiceSid != serviceSid || state.InteractiveSid != interactiveSid || state.Marker != marker)
            throw new InvalidOperationException("网络事务记录与当前项目不匹配，未修改系统配置。");
        state = RecoverOwnership(state);
        SaveState(statePath, state);
        if (state.Completed && state.UrlCreated && state.FirewallCreated) return;
        SaveState(statePath, state);
        try
        {
            if (!state.UrlCreated)
            {
                Run("netsh.exe", ["http", "add", "urlacl", $"url={url}", $"sddl=D:(A;;GX;;;{serviceSid})(A;;GX;;;{interactiveSid})"]);
                state = state with { UrlCreated = true }; SaveState(statePath, state);
            }
            if (!state.FirewallCreated)
            {
                Run("netsh.exe", ["advfirewall", "firewall", "add", "rule", $"name={rule}", $"description={marker}", "dir=in", "action=allow", "protocol=TCP", $"localport={project.Port}"]);
                state = state with { FirewallCreated = true }; SaveState(statePath, state);
            }
            SaveState(statePath, state with { Completed = true });
        }
        catch { RollbackState(statePath, state); throw; }
    }

    public static void RollbackNetwork(GatewayProjectConfiguration project)
    {
        string path = NetworkStatePath();
        NetworkState? state = LoadState(path);
        if (state is null) return;
        string expectedUrl = $"http://{(project.ListenAddress is "0.0.0.0" or "*" or "+" ? "+" : project.ListenAddress)}:{project.Port}/";
        if (state.ProjectId != project.ProjectId || state.Port != project.Port || state.Url != expectedUrl || state.FirewallRule != FirewallRule(project))
            throw new InvalidOperationException("网络回滚记录与当前项目不匹配，未修改系统配置。");
        RollbackState(path, state);
    }

    public static void InstallService(string executablePath, string code)
    {
        string directory = Path.GetDirectoryName(executablePath)!;
        GatewayProjectConfiguration project = GatewayProjectConfiguration.TryLoad(directory) ?? throw new InvalidDataException("gateway-project.json 无效");
        string name = ServiceName(project.ProjectId);
        string statePath = ServiceStatePath();
        ServiceInstallState? existingState = LoadServiceState(statePath);
        bool exists = Run("sc.exe", ["query", name], allowFailure: true).ExitCode == 0;
        if (exists)
        {
            string configuration = Run("sc.exe", ["qc", name]).Output;
            if (existingState is null || existingState.ProjectId != project.ProjectId || existingState.ServiceName != name ||
                !string.Equals(existingState.ExecutablePath, Path.GetFullPath(executablePath), StringComparison.OrdinalIgnoreCase) ||
                !ServiceConfigurationMatches(configuration, existingState.ExecutablePath))
                throw new InvalidOperationException("同名 Windows Service 不属于当前部署包，未做任何修改。");
            if (existingState.Completed)
            {
                if (GetServiceState(name) != 4) Run("sc.exe", ["start", name]);
                if (!WaitServiceState(name, 4, TimeSpan.FromSeconds(15))) throw new InvalidOperationException("服务未在规定时间内进入运行状态。");
                return;
            }
        }
        if (string.IsNullOrEmpty(code)) throw new InvalidOperationException("未找到微端访问 Code，请先运行 GUI 完成凭据导入。");
        project.WriteServiceSecret(directory, code);
        ServiceInstallState state = existingState ?? new ServiceInstallState(project.ProjectId, name, Path.GetFullPath(executablePath), false, false);
        SaveServiceState(statePath, state);
        try
        {
            if (!exists)
            {
                Run("sc.exe", ["create", name, "binPath=", $"\"{executablePath}\" --service", "start=", "auto", "obj=", "LocalSystem", "DisplayName=", $"LyoCrystal 微端 {project.ProjectId}"]);
                state = state with { Created = true }; SaveServiceState(statePath, state);
            }
            else if (!state.Created) { state = state with { Created = true }; SaveServiceState(statePath, state); }
            Run("sc.exe", ["sidtype", name, "unrestricted"]);
            Run("sc.exe", ["description", name, "LyoCrystal 微端资源只读网关"]);
            if (GetServiceState(name) != 4) Run("sc.exe", ["start", name]);
            if (!WaitServiceState(name, 4, TimeSpan.FromSeconds(15))) throw new InvalidOperationException("服务未在规定时间内进入运行状态。");
            SaveServiceState(statePath, state with { Completed = true });
        }
        catch
        {
            if (state.Created) Run("sc.exe", ["delete", name], allowFailure: true);
            if (WaitServiceAbsent(name, TimeSpan.FromSeconds(5))) { try { File.Delete(statePath); } catch (FileNotFoundException) { } }
            throw;
        }
    }

    public static void UninstallService()
    {
        GatewayProjectConfiguration project = GatewayProjectConfiguration.TryLoad(AppContext.BaseDirectory) ?? throw new InvalidDataException("gateway-project.json 无效");
        string name = ServiceName(project.ProjectId);
        string statePath = ServiceStatePath();
        ServiceInstallState? state = LoadServiceState(statePath);
        string configuration = Run("sc.exe", ["qc", name], allowFailure: true).Output;
        if (state is null || state.ProjectId != project.ProjectId || state.ServiceName != name ||
            !string.Equals(state.ExecutablePath, Path.GetFullPath(Environment.ProcessPath!), StringComparison.OrdinalIgnoreCase) ||
            !ServiceConfigurationMatches(configuration, state.ExecutablePath))
            throw new InvalidOperationException("缺少当前部署包的服务安装记录，未删除任何服务。");
        Run("sc.exe", ["stop", name], allowFailure: true);
        Run("sc.exe", ["delete", name], allowFailure: true);
        if (!WaitServiceAbsent(name, TimeSpan.FromSeconds(10))) throw new InvalidOperationException("服务尚未完全删除，已保留安装记录供下次重试。");
        try { File.Delete(statePath); } catch (FileNotFoundException) { }
        try { File.Delete(Path.Combine(AppContext.BaseDirectory, "gateway-secret.service")); } catch (FileNotFoundException) { }
    }

    public static int RelaunchElevated(string operation)
    {
        string callerSid = WindowsIdentity.GetCurrent().User?.Value ?? throw new InvalidOperationException("无法读取当前用户 SID。");
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas" };
        start.ArgumentList.Add(operation);
        start.ArgumentList.Add("--caller-sid");
        start.ArgumentList.Add(callerSid);
        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("无法启动管理员操作。");
        process.WaitForExit();
        return process.ExitCode;
    }

    public static string ServiceName(string projectId) => "LyoMicro_" + ShortIdentity(projectId);
    public static bool IsServiceInstalled(string projectId) => Run("sc.exe", ["query", ServiceName(projectId)], allowFailure: true).ExitCode == 0;
    private static string FirewallRule(GatewayProjectConfiguration project) => "LyoCrystal MicroGateway " + ShortIdentity(project.ProjectId + "|" + project.Port);
    private static string ShortIdentity(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty)))[..16];
    private static string NetworkStatePath() => Path.Combine(AppContext.BaseDirectory, "gateway-network.state.json");
    private static string ServiceStatePath() => Path.Combine(AppContext.BaseDirectory, "gateway-service.state.json");

    private static string ServiceSid(string serviceName)
    {
        byte[] hash = SHA1.HashData(Encoding.Unicode.GetBytes(serviceName.ToUpperInvariant()));
        return "S-1-5-80-" + string.Join('-', Enumerable.Range(0, 5).Select(index => BitConverter.ToUInt32(hash, index * 4)));
    }

    internal static void ProtectServiceSecret(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("服务凭据文件不存在，请先由 GUI 导入。", path);
        string userSid = WindowsIdentity.GetCurrent().User?.Value ?? throw new InvalidOperationException("无法读取当前用户 SID。");
        Run("icacls.exe", [path, "/inheritance:r", "/grant:r", "*S-1-5-18:F", "*S-1-5-32-544:F", $"*{userSid}:F"]);
    }

    private static (int ExitCode, string Output) Run(string fileName, IEnumerable<string> arguments, bool allowFailure = false)
    {
        var start = new ProcessStartInfo(fileName) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException($"无法启动 {fileName}");
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (!allowFailure && process.ExitCode != 0) throw new InvalidOperationException($"{fileName} 执行失败（{process.ExitCode}）：{error}{output}");
        return (process.ExitCode, output + error);
    }

    private static void RollbackState(string path, NetworkState state)
    {
        NetworkState owned = RecoverOwnership(state, rejectForeign: false);
        if (owned.FirewallCreated) Run("netsh.exe", ["advfirewall", "firewall", "delete", "rule", $"name={state.FirewallRule}"], allowFailure: true);
        if (owned.UrlCreated) Run("netsh.exe", ["http", "delete", "urlacl", $"url={state.Url}"], allowFailure: true);
        NetworkState remaining = RecoverOwnership(state, rejectForeign: false);
        if (remaining.FirewallCreated || remaining.UrlCreated)
        {
            SaveState(path, remaining with { Completed = false });
            throw new InvalidOperationException("系统网络配置未完全撤销，已保留事务记录供下次重试。");
        }
        try { File.Delete(path); } catch (FileNotFoundException) { }
    }

    private static void SaveState(string path, NetworkState state)
    {
        string temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try { File.WriteAllText(temporary, JsonSerializer.Serialize(state)); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static NetworkState? LoadState(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("网络回滚记录不能是重解析点。");
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (input.Length > 16 * 1024) throw new InvalidDataException("网络回滚记录超过大小限制。");
            byte[] bytes = new byte[checked((int)input.Length)];
            input.ReadExactly(bytes);
            return JsonSerializer.Deserialize<NetworkState>(bytes);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { throw new InvalidDataException("网络回滚记录损坏，未修改系统配置。", error); }
    }

    private static void SaveServiceState(string path, ServiceInstallState state)
    {
        string temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try { File.WriteAllText(temporary, JsonSerializer.Serialize(state)); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static ServiceInstallState? LoadServiceState(string path)
    {
        if (!File.Exists(path)) return null;
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("服务安装记录不能是重解析点。");
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (input.Length > 16 * 1024) throw new InvalidDataException("服务安装记录超过大小限制。");
        byte[] bytes = new byte[checked((int)input.Length)]; input.ReadExactly(bytes);
        return JsonSerializer.Deserialize<ServiceInstallState>(bytes) ?? throw new InvalidDataException("服务安装记录损坏。");
    }

    private static bool WaitServiceAbsent(string name, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        do
        {
            if (Run("sc.exe", ["query", name], allowFailure: true).ExitCode != 0) return true;
            Thread.Sleep(250);
        } while (DateTime.UtcNow < deadline);
        return false;
    }

    private static int GetServiceState(string name)
    {
        var query = Run("sc.exe", ["query", name], allowFailure: true);
        if (query.ExitCode != 0) return 0;
        Match match = Regex.Match(query.Output, @"STATE\s*:\s*(\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success && int.TryParse(match.Groups[1].Value, out int state) ? state : -1;
    }

    private static bool WaitServiceState(string name, int expected, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        do
        {
            if (GetServiceState(name) == expected) return true;
            Thread.Sleep(250);
        } while (DateTime.UtcNow < deadline);
        return false;
    }

    private static bool ServiceConfigurationMatches(string output, string executablePath)
    {
        string pattern = "^\\s*BINARY_PATH_NAME\\s*:\\s*\"" + Regex.Escape(Path.GetFullPath(executablePath)) + "\"\\s+--service\\s*$";
        return Regex.IsMatch(output, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Multiline);
    }

    private static NetworkState RecoverOwnership(NetworkState state, bool rejectForeign = true)
    {
        string urlOutput = Run("netsh.exe", ["http", "show", "urlacl", $"url={state.Url}"], allowFailure: true).Output;
        bool urlExists = urlOutput.Contains(state.Url, StringComparison.OrdinalIgnoreCase);
        bool urlOwned = urlExists && urlOutput.Contains(state.ServiceSid, StringComparison.OrdinalIgnoreCase) && urlOutput.Contains(state.InteractiveSid, StringComparison.OrdinalIgnoreCase);
        if (urlExists && !urlOwned && rejectForeign) throw new InvalidOperationException("目标 URLACL 已被其他程序占用，未做任何修改。");
        var firewall = Run("netsh.exe", ["advfirewall", "firewall", "show", "rule", $"name={state.FirewallRule}", "verbose"], allowFailure: true);
        bool firewallExists = firewall.ExitCode == 0;
        bool firewallOwned = firewallExists && firewall.Output.Contains(state.Marker, StringComparison.Ordinal);
        if (firewallExists && !firewallOwned && rejectForeign) throw new InvalidOperationException("目标防火墙规则名已被其他程序占用，未做任何修改。");
        return state with { UrlCreated = urlOwned, FirewallCreated = firewallOwned };
    }

    private sealed record NetworkState(string ProjectId, int Port, string Url, string FirewallRule, string ServiceSid, string InteractiveSid, string Marker, bool UrlCreated, bool FirewallCreated, bool Completed);
    private sealed record ServiceInstallState(string ProjectId, string ServiceName, string ExecutablePath, bool Created, bool Completed);
}

internal static class WindowsServiceHost
{
    private const int ServiceWin32OwnProcess = 0x10, ServiceStartPending = 2, ServiceStopPending = 3, ServiceRunning = 4, ServiceStopped = 1, ServiceAcceptStop = 1, ServiceControlStop = 1;
    private static readonly ServiceMainDelegate ServiceMainCallback = ServiceMain;
    private static readonly HandlerDelegate HandlerCallback = Handler;
    private static IntPtr _statusHandle;
    private static CancellationTokenSource? _stopping;
    private static string _serviceName = string.Empty;

    public static int Run()
    {
        GatewayProjectConfiguration? project = GatewayProjectConfiguration.TryLoad(AppContext.BaseDirectory);
        if (project is null) return 2;
        _serviceName = WindowsGatewayOperations.ServiceName(project.ProjectId);
        ServiceTableEntry[] table = [new() { Name = _serviceName, Main = ServiceMainCallback }, new()];
        if (!StartServiceCtrlDispatcher(table)) return Marshal.GetLastWin32Error();
        return 0;
    }

    private static void ServiceMain(int argumentCount, IntPtr arguments)
    {
        _statusHandle = RegisterServiceCtrlHandlerEx(_serviceName, HandlerCallback, IntPtr.Zero);
        if (_statusHandle == IntPtr.Zero) return;
        SetStatus(ServiceStartPending, 0);
        _stopping = new CancellationTokenSource();
        try
        {
            GatewayProjectConfiguration project = GatewayProjectConfiguration.TryLoad(AppContext.BaseDirectory) ?? throw new InvalidDataException("gateway-project.json 无效");
            var runtime = new GatewayRuntime(AppContext.BaseDirectory, project, serviceMode: true);
            try { runtime.StartAsync().GetAwaiter().GetResult(); SetStatus(ServiceRunning, ServiceAcceptStop); _stopping.Token.WaitHandle.WaitOne(); SetStatus(ServiceStopPending, 0); runtime.StopAsync().GetAwaiter().GetResult(); }
            finally { runtime.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
        }
        catch (Exception error) { try { File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "gateway-service-error.log"), $"{DateTime.UtcNow:O}\t{error.GetType().Name}: {error.Message}{Environment.NewLine}"); } catch { } }
        finally { SetStatus(ServiceStopped, 0); _stopping?.Dispose(); _stopping = null; }
    }

    private static int Handler(int control, int eventType, IntPtr eventData, IntPtr context) { if (control == ServiceControlStop) _stopping?.Cancel(); return 0; }
    private static void SetStatus(int state, int accepted) { var status = new ServiceStatus { ServiceType = ServiceWin32OwnProcess, CurrentState = state, ControlsAccepted = accepted }; SetServiceStatus(_statusHandle, ref status); }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct ServiceTableEntry { [MarshalAs(UnmanagedType.LPWStr)] public string? Name; public ServiceMainDelegate? Main; }
    [StructLayout(LayoutKind.Sequential)] private struct ServiceStatus { public int ServiceType, CurrentState, ControlsAccepted, Win32ExitCode, ServiceSpecificExitCode, CheckPoint, WaitHint; }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void ServiceMainDelegate(int argumentCount, IntPtr arguments);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int HandlerDelegate(int control, int eventType, IntPtr eventData, IntPtr context);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool StartServiceCtrlDispatcher([In] ServiceTableEntry[] serviceTable);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr RegisterServiceCtrlHandlerEx(string serviceName, HandlerDelegate handler, IntPtr context);
    [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetServiceStatus(IntPtr statusHandle, ref ServiceStatus status);
}
