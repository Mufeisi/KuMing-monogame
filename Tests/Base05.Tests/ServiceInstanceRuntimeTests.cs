using System.Net;
using LyoCrystal.InstanceManagement;
using Xunit;

namespace Base05.Tests;

public sealed class ServiceInstanceRuntimeTests
{
    [Fact]
    public async Task 真实隐藏组件_按依赖启动健康并正常停止()
    {
        using var fixture = new RuntimeFixture();
        ServiceInstanceProfile profile = fixture.CreateProfile(twoComponents: true, endless: false);
        await using var runtime = new ServiceInstanceRuntime(profile, new ServiceInstanceRuntimeOptions
        {
            HealthPollInterval = TimeSpan.FromMilliseconds(25)
        }, new PortHealthHandler());

        await runtime.StartAsync();
        ServiceInstanceRuntimeSnapshot healthy = runtime.GetSnapshot();
        await runtime.StopAsync();
        ServiceInstanceRuntimeSnapshot stopped = runtime.GetSnapshot();

        Assert.Equal(ServiceInstanceRuntimeState.Healthy, healthy.State);
        Assert.All(healthy.Components, item => Assert.Equal(ServiceComponentRuntimeState.Healthy, item.State));
        Assert.All(healthy.Components, item => Assert.NotNull(item.ProcessId));
        Assert.True(healthy.AuditEvents.FindIndex(item => item.Action == "component-started" && item.ComponentId == "database") <
                    healthy.AuditEvents.FindIndex(item => item.Action == "component-started" && item.ComponentId == "server"));
        Assert.Equal(ServiceInstanceRuntimeState.Stopped, stopped.State);
        Assert.All(stopped.Components, item => Assert.Equal(ServiceComponentRuntimeState.Stopped, item.State));
        Assert.DoesNotContain(stopped.AuditEvents, item => item.Action == "component-force-stopped");
        Assert.Contains(stopped.AuditEvents, item => item.Action == "component-stop-request");
        string log = File.ReadAllText(Path.Combine(fixture.Root, "logs", "server.log"));
        Assert.Contains("token=***", log, StringComparison.Ordinal);
        Assert.DoesNotContain("fixture-secret", log, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 后置组件健康超时_逆序回滚并审计强制清理()
    {
        using var fixture = new RuntimeFixture();
        ServiceInstanceProfile profile = fixture.CreateProfile(twoComponents: true, endless: true);
        await using var runtime = new ServiceInstanceRuntime(profile, new ServiceInstanceRuntimeOptions
        {
            HealthPollInterval = TimeSpan.FromMilliseconds(25)
        }, new PortHealthHandler(failPort: 7200 + profile.PortOffset));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runtime.StartAsync());
        ServiceInstanceRuntimeSnapshot snapshot = runtime.GetSnapshot();

        Assert.Equal(ServiceInstanceRuntimeState.Stopped, snapshot.State);
        Assert.All(snapshot.Components, item => Assert.Null(item.ProcessId));
        Assert.Contains(snapshot.AuditEvents, item => item.Action == "instance-start-failed");
        Assert.Contains(snapshot.AuditEvents, item => item.Action == "component-rollback-force");
    }

    [Fact]
    public async Task 正式实例未获得独立授权_运行入口失败关闭()
    {
        using var fixture = new RuntimeFixture();
        ServiceInstanceProfile profile = fixture.CreateProfile(twoComponents: false, endless: false);
        profile.Environment = ServiceEnvironmentKind.Production;
        profile.SecretReference = "secret://production-test";
        await using var runtime = new ServiceInstanceRuntime(profile, httpHandler: new PortHealthHandler());

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.StartAsync());

        Assert.Contains("正式实例默认禁止", error.Message, StringComparison.Ordinal);
        Assert.Equal(ServiceInstanceRuntimeState.Stopped, runtime.GetSnapshot().State);
    }

    private sealed class RuntimeFixture : IDisposable
    {
        public RuntimeFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), "LEG09-runtime-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(Root, "runtime"));
            File.Copy(Environment.GetEnvironmentVariable("ComSpec") ?? @"C:\Windows\System32\cmd.exe", Path.Combine(Root, "runtime", "component.exe"));
        }

        public string Root { get; }

        public ServiceInstanceProfile CreateProfile(bool twoComponents, bool endless)
        {
            string command = endless ? "echo token=fixture-secret & ping 127.0.0.1 -t > nul" : "echo token=fixture-secret & ping 127.0.0.1 -n 2 > nul";
            File.WriteAllText(Path.Combine(Root, "runtime", "component-script.cmd"), "@echo off\r\n" + command + "\r\n");
            var database = new ServiceComponentProfile
            {
                Id = "database",
                Role = ServiceComponentRole.Auxiliary,
                ExecutablePath = "runtime/component.exe",
                WorkingDirectory = "runtime",
                Arguments = ["/d", "/s", "/c", "component-script.cmd"],
                BasePort = 7100,
                HealthProbe = ServiceHealthProbeKind.Http,
                StopPath = "/shutdown",
                StartTimeoutSeconds = 1,
                StopTimeoutSeconds = 3,
                LogPath = "logs/database.log"
            };
            var profile = new ServiceInstanceProfile
            {
                InstanceId = "runtime-test",
                Environment = ServiceEnvironmentKind.Test,
                ServerId = "runtime-server",
                PortOffset = Random.Shared.Next(1000, 3000),
                RootDirectory = Root,
                LoginAddress = "127.0.0.1",
                LoginBasePort = 7000,
                Components = [database]
            };
            if (twoComponents)
                profile.Components.Add(new ServiceComponentProfile
                {
                    Id = "server",
                    Role = ServiceComponentRole.GameServer,
                    ExecutablePath = "runtime/component.exe",
                    WorkingDirectory = "runtime",
                    Arguments = ["/d", "/s", "/c", "component-script.cmd"],
                    BasePort = 7200,
                    HealthProbe = ServiceHealthProbeKind.Http,
                    StopPath = "/shutdown",
                    StartTimeoutSeconds = 1,
                    StopTimeoutSeconds = 3,
                    LogPath = "logs/server.log",
                    DependsOn = ["database"]
                });
            return profile;
        }

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, true);
        }
    }

    private sealed class PortHealthHandler(int? failPort = null) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            int port = request.RequestUri!.Port;
            HttpStatusCode code = port == failPort ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK;
            return Task.FromResult(new HttpResponseMessage(code));
        }
    }
}

internal static class RuntimeAuditAssertions
{
    public static int FindIndex<T>(this IReadOnlyList<T> values, Predicate<T> predicate)
    {
        for (int index = 0; index < values.Count; index++)
            if (predicate(values[index])) return index;
        return -1;
    }
}
