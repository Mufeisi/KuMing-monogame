using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text.Json;
using Server;
using Server.Library.Utils;
using Server.Operations;
using Server.Persistence.Sql;
using Server.Security;
using Shared.Diagnostics;
using Xunit;

namespace Base05.Tests;

[Collection("SEC04环境")]
public sealed class BasicOperationsMonitorTests
{
    [Fact]
    public void 状态快照组合既有核心指标和备份状态()
    {
        DateTimeOffset now = new(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);
        var backup = SucceededBackup(now);
        using var monitor = new BasicOperationsMonitor(
            () => Snapshot(online: 42, tickP95: 18.5, saveP95: 220.25, queue: 7),
            () => backup,
            clock: () => now);

        BasicOperationsStatus status = monitor.CaptureStatus();

        Assert.True(status.MetricsEnabled);
        Assert.Equal(42, status.OnlinePlayers);
        Assert.Equal(18.5, status.TickP95Milliseconds);
        Assert.Equal(220.25, status.SaveP95Milliseconds);
        Assert.Equal(7, status.NetworkQueueDepth);
        Assert.Same(backup, status.Backup);
        Assert.Empty(status.Alerts);
    }

    [Fact]
    public void 超阈值和保存备份失败均生成明确告警()
    {
        DateTimeOffset now = new(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);
        using var monitor = new BasicOperationsMonitor(
            () => Snapshot(online: 1, tickP95: 101, saveP95: 30_001, queue: 100, saveFailures: 2),
            () => new SqliteBackupStatus { State = SqliteBackupState.Failed, LastAttemptUtc = now },
            clock: () => now);

        string[] codes = monitor.CaptureStatus().Alerts.Select(alert => alert.Code).Order().ToArray();

        Assert.Equal(new[]
        {
            "backup-failed", "network-queue-high", "save-failed", "save-p95-high", "tick-p95-high",
        }, codes);
    }

    [Fact]
    public void 告警只在触发和恢复状态转换时写入一次()
    {
        DateTimeOffset now = new(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);
        PerformanceSnapshot current = Snapshot(online: 1, tickP95: 10, saveP95: 20, queue: 101);
        var lines = new List<string>();
        using var monitor = new BasicOperationsMonitor(
            () => current,
            () => SucceededBackup(now),
            clock: () => now,
            alertSink: lines.Add);

        monitor.CheckAndPublish();
        monitor.CheckAndPublish();
        current = Snapshot(online: 1, tickP95: 10, saveP95: 20, queue: 0);
        monitor.CheckAndPublish();
        monitor.CheckAndPublish();

        Assert.Equal(2, lines.Count);
        Assert.Contains("state=triggered", lines[0]);
        Assert.Contains("code=network-queue-high", lines[0]);
        Assert.Contains("state=recovered", lines[1]);
        Assert.Contains("code=network-queue-high", lines[1]);
    }

    [Fact]
    public void 非Sqlite部署不会把无备份服务误报为故障()
    {
        using var monitor = new BasicOperationsMonitor(
            () => Snapshot(online: 1, tickP95: 10, saveP95: 20, queue: 0),
            backupStatus: null,
            backupRequired: false);

        BasicOperationsStatus status = monitor.CaptureStatus();

        Assert.False(status.BackupRequired);
        Assert.DoesNotContain(status.Alerts, alert => alert.Code == "backup-unavailable");
    }

    [Fact]
    public void 首次备份长期运行或从未开始都会触发时效告警()
    {
        DateTimeOffset now = new(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);
        var thresholds = new BasicOperationsThresholds { BackupMaximumAge = TimeSpan.FromHours(2) };
        using (var running = new BasicOperationsMonitor(
                   () => Snapshot(online: 0, tickP95: 1, saveP95: 1, queue: 0),
                   () => new SqliteBackupStatus
                   {
                       State = SqliteBackupState.Running,
                       LastAttemptUtc = now - TimeSpan.FromHours(3),
                   },
                   thresholds,
                   () => now))
        {
            Assert.Contains(running.CaptureStatus().Alerts, alert => alert.Code == "backup-stale");
        }

        DateTimeOffset clock = now;
        using var idle = new BasicOperationsMonitor(
            () => Snapshot(online: 0, tickP95: 1, saveP95: 1, queue: 0),
            () => new SqliteBackupStatus { State = SqliteBackupState.Idle },
            thresholds,
            () => clock);
        clock = now + TimeSpan.FromHours(3);

        Assert.Contains(idle.CaptureStatus().Alerts, alert => alert.Code == "backup-stale");
    }

    [Fact]
    public void 告警日志失败后保留未发布状态并在下轮重试()
    {
        DateTimeOffset now = new(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);
        PerformanceSnapshot current = Snapshot(online: 1, tickP95: 10, saveP95: 20, queue: 101);
        int triggerAttempts = 0;
        int recoveryAttempts = 0;
        var delivered = new List<string>();
        void Sink(string line)
        {
            if (line.Contains("state=triggered") && ++triggerAttempts == 1)
                throw new IOException("注入触发日志失败");
            if (line.Contains("state=recovered") && ++recoveryAttempts == 1)
                throw new IOException("注入恢复日志失败");
            delivered.Add(line);
        }

        using var monitor = new BasicOperationsMonitor(
            () => current,
            () => SucceededBackup(now),
            clock: () => now,
            alertSink: Sink);

        monitor.CheckAndPublish();
        monitor.CheckAndPublish();
        current = Snapshot(online: 1, tickP95: 10, saveP95: 20, queue: 0);
        monitor.CheckAndPublish();
        monitor.CheckAndPublish();

        Assert.Equal(2, triggerAttempts);
        Assert.Equal(2, recoveryAttempts);
        Assert.Contains(delivered, line => line.Contains("state=triggered") && line.Contains("network-queue-high"));
        Assert.Contains(delivered, line => line.Contains("state=recovered") && line.Contains("network-queue-high"));
    }

    [Fact]
    public async Task 操作员凭据可读取真实Http监控Json()
    {
        string root = Path.Combine(Path.GetTempPath(), "base05-ops01-http-" + Guid.NewGuid().ToString("N"));
        int port = GetFreePort();
        string originalAddress = Settings.HTTPIPAddress;
        string originalTrustedAddress = Settings.HTTPTrustedIPAddress;
        IDisposable secretScope = ProtectedSecretStore.UseTestRoot(Path.Combine(root, "secrets"));
        HttpServer server = null;
        DateTimeOffset now = new(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);

        try
        {
            Settings.HTTPIPAddress = $"http://127.0.0.1:{port}/";
            Settings.HTTPTrustedIPAddress = "127.0.0.1";
            ProtectedSecretStore.Write(ProtectedSecretStore.AdministratorToken, "administrator-secret-32-characters-minimum");
            ProtectedSecretStore.Write(ProtectedSecretStore.OperatorToken, "operator-secret-32-characters-minimum");
            var monitor = new BasicOperationsMonitor(
                () => Snapshot(online: 9, tickP95: 12.5, saveP95: 33.5, queue: 4),
                () => SucceededBackup(now),
                clock: () => now);
            server = new HttpServer(operationsMonitor: monitor);
            server.Start();

            using var handler = new HttpClientHandler { UseProxy = false };
            using var client = new HttpClient(handler) { BaseAddress = new Uri(Settings.HTTPIPAddress) };
            using HttpResponseMessage response = await SendWhenReady(client, "/operations/status");
            string json = await response.Content.ReadAsStringAsync();
            using JsonDocument document = JsonDocument.Parse(json);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(9, document.RootElement.GetProperty("OnlinePlayers").GetInt64());
            Assert.Equal(12.5, document.RootElement.GetProperty("TickP95Milliseconds").GetDouble());
            Assert.Equal(33.5, document.RootElement.GetProperty("SaveP95Milliseconds").GetDouble());
            Assert.Equal(4, document.RootElement.GetProperty("NetworkQueueDepth").GetInt64());
            Assert.Equal("Succeeded", document.RootElement.GetProperty("Backup").GetProperty("State").GetString());
        }
        finally
        {
            server?.Stop();
            secretScope.Dispose();
            Settings.HTTPIPAddress = originalAddress;
            Settings.HTTPTrustedIPAddress = originalTrustedAddress;
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static PerformanceSnapshot Snapshot(
        long online,
        double tickP95,
        double saveP95,
        long queue,
        long saveFailures = 0) => new PerformanceSnapshot
    {
        Enabled = true,
        State = PerformanceSessionState.Active.ToString(),
        Metrics = new List<PerformanceMetricSnapshot>
        {
            Metric(PerformanceMetricKind.ActiveConnections, lastValue: online),
            Metric(PerformanceMetricKind.Update, p95Milliseconds: tickP95),
            Metric(PerformanceMetricKind.Save, p95Milliseconds: saveP95),
            Metric(PerformanceMetricKind.NetworkQueue, lastValue: queue),
            Metric(PerformanceMetricKind.SaveFailure, totalValue: saveFailures),
        },
    };

    private static PerformanceMetricSnapshot Metric(
        PerformanceMetricKind kind,
        double? p95Milliseconds = null,
        long? lastValue = null,
        long? totalValue = null) => new PerformanceMetricSnapshot
    {
        Name = kind.ToString(),
        Available = true,
        Samples = 1,
        P95Milliseconds = p95Milliseconds,
        LastValue = lastValue,
        TotalValue = totalValue,
    };

    private static SqliteBackupStatus SucceededBackup(DateTimeOffset now) => new SqliteBackupStatus
    {
        State = SqliteBackupState.Succeeded,
        LastSuccessUtc = now - TimeSpan.FromMinutes(5),
        IntegrityResult = "ok",
    };

    private static async Task<HttpResponseMessage> SendWhenReady(HttpClient client, string path)
    {
        Exception last = null;
        for (int attempt = 0; attempt < 30; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, path);
                request.Headers.Authorization = new AuthenticationHeaderValue(
                    "Bearer", "operator-secret-32-characters-minimum");
                return await client.SendAsync(request);
            }
            catch (HttpRequestException error)
            {
                last = error;
                await Task.Delay(50);
            }
        }
        throw new InvalidOperationException("管理 HTTP 测试服务未在期限内启动", last);
    }

    private static int GetFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
