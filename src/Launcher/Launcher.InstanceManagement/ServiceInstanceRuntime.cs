using System.Diagnostics;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace LyoCrystal.InstanceManagement;

public enum ServiceInstanceRuntimeState
{
    Stopped,
    Starting,
    Healthy,
    Stopping,
    Failed
}

public enum ServiceComponentRuntimeState
{
    Pending,
    Starting,
    Healthy,
    Stopping,
    Stopped,
    Failed
}

public sealed record ServiceComponentRuntimeSnapshot(
    string ComponentId,
    ServiceComponentRuntimeState State,
    int? ProcessId,
    DateTimeOffset? StartedAtUtc,
    TimeSpan Uptime,
    string Version,
    string LogPath,
    string Message);

public sealed record ServiceInstanceAuditEvent(DateTimeOffset TimestampUtc, string Action, string ComponentId, string Message);

public sealed record ServiceInstanceRuntimeSnapshot(
    string InstanceId,
    ServiceInstanceRuntimeState State,
    IReadOnlyList<ServiceComponentRuntimeSnapshot> Components,
    IReadOnlyList<ServiceInstanceAuditEvent> AuditEvents);

public sealed class ServiceInstanceRuntimeOptions
{
    public bool AllowProduction { get; init; }
    public TimeSpan HealthPollInterval { get; init; } = TimeSpan.FromMilliseconds(250);
}

public sealed partial class ServiceInstanceRuntime : IAsyncDisposable
{
    private readonly ServiceInstanceProfile profile;
    private readonly ServiceInstanceRuntimeOptions options;
    private readonly HttpClient httpClient;
    private readonly Dictionary<string, RunningComponent> running = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ServiceComponentRuntimeSnapshot> snapshots = new(StringComparer.Ordinal);
    private readonly List<ServiceInstanceAuditEvent> audit = [];
    private readonly SemaphoreSlim gate = new(1, 1);
    private ServiceInstanceRuntimeState state = ServiceInstanceRuntimeState.Stopped;
    private FileStream? instanceLock;
    private bool disposed;

    public ServiceInstanceRuntime(ServiceInstanceProfile profile, ServiceInstanceRuntimeOptions? options = null, HttpMessageHandler? httpHandler = null)
    {
        this.profile = profile ?? throw new ArgumentNullException(nameof(profile));
        this.options = options ?? new ServiceInstanceRuntimeOptions();
        httpClient = httpHandler is null ? new HttpClient() : new HttpClient(httpHandler, disposeHandler: true);
        foreach (ServiceComponentProfile component in profile.Components)
            snapshots[component.Id] = CreateSnapshot(component, ServiceComponentRuntimeState.Pending, null, null, "尚未启动");
    }

    public ServiceInstanceRuntimeSnapshot GetSnapshot()
    {
        lock (audit)
        {
            RefreshExitedProcesses();
            return new ServiceInstanceRuntimeSnapshot(
                profile.InstanceId,
                state,
                profile.Components.Select(item => snapshots[item.Id]).ToArray(),
                audit.ToArray());
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (state != ServiceInstanceRuntimeState.Stopped)
                throw new InvalidOperationException("实例只有在已停止状态才能启动。");
            IReadOnlyList<InstanceDiagnostic> diagnostics = ServiceInstanceProfileValidator.Validate(profile, inspectFileSystem: true);
            InstanceDiagnostic[] errors = diagnostics.Where(item => item.Severity == InstanceDiagnosticSeverity.Error).ToArray();
            if (errors.Length > 0)
                throw new InvalidDataException(string.Join("；", errors.Select(item => $"{item.Code} {item.Message}")));
            if (profile.Environment == ServiceEnvironmentKind.Production && !options.AllowProduction)
                throw new InvalidOperationException("正式实例默认禁止自动启动，必须使用独立生产授权入口。");
            EnsurePortsAvailable();
            instanceLock = new FileStream(Path.Combine(profile.RootDirectory, ".lyocrystal-instance.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);

            state = ServiceInstanceRuntimeState.Starting;
            AddAudit("instance-start", string.Empty, "开始按依赖顺序启动实例。");
            try
            {
                foreach (ServiceComponentProfile component in TopologicalOrder())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await StartComponentAsync(component, cancellationToken).ConfigureAwait(false);
                }
                state = ServiceInstanceRuntimeState.Healthy;
                AddAudit("instance-healthy", string.Empty, "全部组件已通过健康探针。");
            }
            catch (Exception ex)
            {
                state = ServiceInstanceRuntimeState.Failed;
                AddAudit("instance-start-failed", string.Empty, Sanitize(ex.Message));
                await StopStartedComponentsAsync(forceConfirmed: true, rollback: true, CancellationToken.None).ConfigureAwait(false);
                ReleaseInstanceLock();
                state = ServiceInstanceRuntimeState.Stopped;
                throw;
            }
        }
        finally { gate.Release(); }
    }

    public async Task RefreshHealthAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (state is not (ServiceInstanceRuntimeState.Healthy or ServiceInstanceRuntimeState.Failed))
                throw new InvalidOperationException("实例未运行，无法刷新健康状态。");
            bool allHealthy = true;
            foreach (ServiceComponentProfile component in profile.Components)
            {
                if (!running.TryGetValue(component.Id, out RunningComponent? item) || item.Process.HasExited || !await ProbeAsync(component, cancellationToken).ConfigureAwait(false))
                {
                    allHealthy = false;
                    UpdateSnapshot(component, ServiceComponentRuntimeState.Failed, item, "健康探针失败");
                }
                else UpdateSnapshot(component, ServiceComponentRuntimeState.Healthy, item, "健康");
            }
            state = allHealthy ? ServiceInstanceRuntimeState.Healthy : ServiceInstanceRuntimeState.Failed;
            AddAudit("health-refresh", string.Empty, allHealthy ? "全部组件健康。" : "一个或多个组件不健康。");
        }
        finally { gate.Release(); }
    }

    public async Task StopAsync(bool forceConfirmed = false, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (state == ServiceInstanceRuntimeState.Stopped) return;
            state = ServiceInstanceRuntimeState.Stopping;
            await StopStartedComponentsAsync(forceConfirmed, rollback: false, cancellationToken).ConfigureAwait(false);
            ReleaseInstanceLock();
            state = ServiceInstanceRuntimeState.Stopped;
            AddAudit("instance-stopped", string.Empty, "实例已停止。");
        }
        catch
        {
            state = ServiceInstanceRuntimeState.Failed;
            throw;
        }
        finally { gate.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        try
        {
            if (state != ServiceInstanceRuntimeState.Stopped)
                await StopAsync(forceConfirmed: true).ConfigureAwait(false);
        }
        finally
        {
            disposed = true;
            httpClient.Dispose();
            gate.Dispose();
        }
    }

    private async Task StartComponentAsync(ServiceComponentProfile component, CancellationToken cancellationToken)
    {
        UpdateSnapshot(component, ServiceComponentRuntimeState.Starting, null, "正在启动");
        string executable = ResolvePath(component.ExecutablePath);
        string workingDirectory = ResolvePath(component.WorkingDirectory);
        string logPath = component.LogPath.Length == 0
            ? ResolvePath(Path.Combine("logs", component.Id + ".log"))
            : ResolvePath(component.LogPath);
        string version = GetVersion(executable);
        if (component.ExpectedVersion.Length > 0 && !string.Equals(component.ExpectedVersion, version, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"组件 {component.Id} 版本不匹配：期望 {component.ExpectedVersion}，实际 {version}。");
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);

        var startInfo = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (string argument in component.Arguments) startInfo.ArgumentList.Add(argument);
        startInfo.Environment["LYOCRYSTAL_INSTANCE_ID"] = profile.InstanceId;
        startInfo.Environment["LYOCRYSTAL_COMPONENT_ID"] = component.Id;
        startInfo.Environment["LYOCRYSTAL_SERVER_ID"] = profile.ServerId;
        startInfo.Environment["LYOCRYSTAL_EFFECTIVE_PORT"] = EffectivePort(component).ToString();

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var log = new StreamWriter(new FileStream(logPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite), new UTF8Encoding(false)) { AutoFlush = true };
        process.OutputDataReceived += (_, args) => WriteLog(log, "OUT", args.Data);
        process.ErrorDataReceived += (_, args) => WriteLog(log, "ERR", args.Data);
        try
        {
            if (!process.Start()) throw new InvalidOperationException($"组件 {component.Id} 未能创建进程。");
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            var item = new RunningComponent(component, process, log, DateTimeOffset.UtcNow, version, logPath);
            running.Add(component.Id, item);
            AddAudit("component-started", component.Id, $"进程已启动，PID={process.Id}。");
            UpdateSnapshot(component, ServiceComponentRuntimeState.Starting, item, "等待健康探针");

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(component.StartTimeoutSeconds));
            while (!await ProbeAsync(component, timeout.Token).ConfigureAwait(false))
            {
                if (process.HasExited) throw new InvalidOperationException($"组件 {component.Id} 在健康确认前退出，代码 {process.ExitCode}。");
                await Task.Delay(options.HealthPollInterval, timeout.Token).ConfigureAwait(false);
            }
            UpdateSnapshot(component, ServiceComponentRuntimeState.Healthy, item, "健康");
            AddAudit("component-healthy", component.Id, "健康探针通过。");
        }
        catch
        {
            if (!running.ContainsKey(component.Id))
            {
                log.Dispose();
                process.Dispose();
            }
            UpdateSnapshot(component, ServiceComponentRuntimeState.Failed, running.GetValueOrDefault(component.Id), "启动或健康确认失败");
            throw;
        }
    }

    private async Task StopStartedComponentsAsync(bool forceConfirmed, bool rollback, CancellationToken cancellationToken)
    {
        foreach (ServiceComponentProfile component in TopologicalOrder().Reverse())
        {
            if (!running.TryGetValue(component.Id, out RunningComponent? item)) continue;
            UpdateSnapshot(component, ServiceComponentRuntimeState.Stopping, item, rollback ? "失败回滚中" : "正在停止");
            if (!item.Process.HasExited && component.StopPath.Length > 0)
            {
                try
                {
                    using var response = await httpClient.PostAsync(BuildHttpUri(component, component.StopPath), null, cancellationToken).ConfigureAwait(false);
                    AddAudit("component-stop-request", component.Id, $"停止端点返回 {(int)response.StatusCode}。");
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
                {
                    AddAudit("component-stop-request-failed", component.Id, Sanitize(ex.Message));
                }
            }
            if (!item.Process.HasExited)
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(component.StopTimeoutSeconds));
                try { await item.Process.WaitForExitAsync(timeout.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    if (!forceConfirmed && !rollback)
                        throw new InvalidOperationException($"组件 {component.Id} 未在超时内正常停止；强制停止需要显式确认。");
                    item.Process.Kill(entireProcessTree: true);
                    await item.Process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                    AddAudit(rollback ? "component-rollback-force" : "component-force-stopped", component.Id, rollback ? "启动失败，已自动清理本次创建的进程树。" : "已确认并强制停止进程树。");
                }
            }
            item.Log.Dispose();
            item.Process.Dispose();
            running.Remove(component.Id);
            UpdateSnapshot(component, ServiceComponentRuntimeState.Stopped, null, rollback ? "已回滚" : "已停止");
        }
    }

    private async Task<bool> ProbeAsync(ServiceComponentProfile component, CancellationToken cancellationToken)
    {
        try
        {
            if (component.HealthProbe == ServiceHealthProbeKind.Http)
            {
                using HttpResponseMessage response = await httpClient.GetAsync(BuildHttpUri(component, component.HealthPath), cancellationToken).ConfigureAwait(false);
                return response.IsSuccessStatusCode;
            }
            using var client = new TcpClient();
            await client.ConnectAsync(profile.LoginAddress, EffectivePort(component), cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is SocketException or HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested) cancellationToken.ThrowIfCancellationRequested();
            return false;
        }
    }

    private IReadOnlyList<ServiceComponentProfile> TopologicalOrder()
    {
        var byId = profile.Components.ToDictionary(item => item.Id, StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<ServiceComponentProfile>();
        void Visit(ServiceComponentProfile component)
        {
            if (!visited.Add(component.Id)) return;
            foreach (string dependency in component.DependsOn) Visit(byId[dependency]);
            result.Add(component);
        }
        foreach (ServiceComponentProfile component in profile.Components) Visit(component);
        return result;
    }

    private Uri BuildHttpUri(ServiceComponentProfile component, string path) => new($"http://{profile.LoginAddress}:{EffectivePort(component)}{path}");
    private int EffectivePort(ServiceComponentProfile component) => component.BasePort + profile.PortOffset;
    private string ResolvePath(string relative) => Path.GetFullPath(Path.Combine(profile.RootDirectory, relative));

    private void UpdateSnapshot(ServiceComponentProfile component, ServiceComponentRuntimeState componentState, RunningComponent? item, string message)
    {
        snapshots[component.Id] = CreateSnapshot(component, componentState, item, item?.StartedAtUtc, message);
    }

    private ServiceComponentRuntimeSnapshot CreateSnapshot(ServiceComponentProfile component, ServiceComponentRuntimeState componentState, RunningComponent? item, DateTimeOffset? startedAt, string message)
    {
        TimeSpan uptime = startedAt is null ? TimeSpan.Zero : DateTimeOffset.UtcNow - startedAt.Value;
        return new ServiceComponentRuntimeSnapshot(component.Id, componentState, item?.Process.HasExited == false ? item.Process.Id : null, startedAt, uptime, item?.Version ?? component.ExpectedVersion, item?.LogPath ?? (component.LogPath.Length == 0 ? Path.Combine(profile.RootDirectory, "logs", component.Id + ".log") : ResolvePath(component.LogPath)), message);
    }

    private void RefreshExitedProcesses()
    {
        foreach ((string id, RunningComponent item) in running)
            if (item.Process.HasExited && snapshots[id].State is ServiceComponentRuntimeState.Healthy or ServiceComponentRuntimeState.Starting)
                snapshots[id] = snapshots[id] with { State = ServiceComponentRuntimeState.Failed, ProcessId = null, Uptime = DateTimeOffset.UtcNow - item.StartedAtUtc, Message = $"进程已退出，代码 {item.Process.ExitCode}" };
    }

    private void AddAudit(string action, string componentId, string message)
    {
        lock (audit) audit.Add(new ServiceInstanceAuditEvent(DateTimeOffset.UtcNow, action, componentId, Sanitize(message)));
    }

    private static void WriteLog(StreamWriter writer, string channel, string? line)
    {
        if (line is null) return;
        lock (writer) writer.WriteLine($"{DateTimeOffset.UtcNow:O} [{channel}] {Sanitize(line)}");
    }

    private static string GetVersion(string executable)
    {
        FileVersionInfo info = FileVersionInfo.GetVersionInfo(executable);
        return info.FileVersion ?? info.ProductVersion ?? "未知";
    }

    private static string Sanitize(string value) => SecretAssignmentPattern().Replace(value, "$1=***");
    private void EnsurePortsAvailable()
    {
        IEnumerable<int> ports = profile.Components.Select(EffectivePort).Append(profile.LoginBasePort + profile.PortOffset).Distinct();
        foreach (int port in ports)
        {
            TcpListener? listener = null;
            try
            {
                listener = new TcpListener(System.Net.IPAddress.Loopback, port);
                listener.Start();
            }
            catch (SocketException ex)
            {
                throw new InvalidOperationException($"端口 {port} 已被占用或无法绑定。", ex);
            }
            finally { listener?.Stop(); }
        }
    }

    private void ReleaseInstanceLock()
    {
        instanceLock?.Dispose();
        instanceLock = null;
    }
    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);

    private sealed record RunningComponent(ServiceComponentProfile Profile, Process Process, StreamWriter Log, DateTimeOffset StartedAtUtc, string Version, string LogPath);

    [GeneratedRegex("(?i)(password|passwd|token|secret|api[-_]?key)\\s*=\\s*[^\\s;&]+")]
    private static partial Regex SecretAssignmentPattern();
}
