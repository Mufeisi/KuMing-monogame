using Shared.Diagnostics;
using Server.Persistence.Sql;

namespace Server.Operations;

internal sealed class BasicOperationsThresholds
{
    internal double TickP95WarningMilliseconds { get; init; } = 100;
    internal double SaveP95WarningMilliseconds { get; init; } = 30_000;
    internal long NetworkQueueWarningDepth { get; init; } = 100;
    internal TimeSpan BackupMaximumAge { get; init; } = TimeSpan.FromHours(2);
    internal TimeSpan AlertCheckInterval { get; init; } = TimeSpan.FromSeconds(10);

    internal static BasicOperationsThresholds FromSettings()
    {
        var thresholds = new BasicOperationsThresholds
        {
            TickP95WarningMilliseconds = Settings.OperationsTickP95WarningMilliseconds,
            SaveP95WarningMilliseconds = Settings.OperationsSaveP95WarningMilliseconds,
            NetworkQueueWarningDepth = Settings.OperationsNetworkQueueWarningDepth,
            AlertCheckInterval = TimeSpan.FromSeconds(Settings.OperationsAlertCheckSeconds),
            BackupMaximumAge = TimeSpan.FromMinutes(Math.Max(2L, (long)Settings.SqliteBackupIntervalMinutes * 2L)),
        };
        thresholds.Validate();
        return thresholds;
    }

    internal void Validate()
    {
        if (TickP95WarningMilliseconds is < 1 or > 60_000)
            throw new InvalidOperationException("Tick p95 告警阈值必须在 1～60000ms 之间");
        if (SaveP95WarningMilliseconds is < 1 or > 3_600_000)
            throw new InvalidOperationException("保存 p95 告警阈值必须在 1～3600000ms 之间");
        if (NetworkQueueWarningDepth is < 1 or > 1_000_000)
            throw new InvalidOperationException("网络队列告警阈值必须在 1～1000000 之间");
        if (AlertCheckInterval < TimeSpan.FromSeconds(1) || AlertCheckInterval > TimeSpan.FromHours(1))
            throw new InvalidOperationException("运维告警检查间隔必须在 1 秒～1 小时之间");
        if (BackupMaximumAge < TimeSpan.FromMinutes(2) || BackupMaximumAge > TimeSpan.FromDays(14))
            throw new InvalidOperationException("备份时效告警窗口必须在 2 分钟～14 天之间");
    }
}

internal sealed class BasicOperationsAlert
{
    public string Code { get; init; } = string.Empty;
    public string Severity { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}

internal sealed class BasicOperationsStatus
{
    public DateTimeOffset GeneratedAtUtc { get; init; }
    public bool MetricsEnabled { get; init; }
    public long? OnlinePlayers { get; init; }
    public double? TickP95Milliseconds { get; init; }
    public double? SaveP95Milliseconds { get; init; }
    public long? SaveFailures { get; init; }
    public long? NetworkQueueDepth { get; init; }
    public bool BackupRequired { get; init; }
    public SqliteBackupStatus Backup { get; init; }
    public IReadOnlyList<BasicOperationsAlert> Alerts { get; init; } = Array.Empty<BasicOperationsAlert>();
}

/// <summary>
/// 发布前基础运维深模块：组合现有 PERF-00 与 DB-03 快照，并对告警状态转换去重。
/// </summary>
internal sealed class BasicOperationsMonitor : IDisposable
{
    private readonly object _gate = new object();
    private readonly Func<PerformanceSnapshot> _metricsSnapshot;
    private readonly Func<SqliteBackupStatus> _backupStatus;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Action<string> _alertSink;
    private readonly BasicOperationsThresholds _thresholds;
    private readonly bool _backupRequired;
    private readonly DateTimeOffset _monitorStartedAtUtc;
    private readonly HashSet<string> _activeAlertCodes = new(StringComparer.Ordinal);
    private readonly ManualResetEventSlim _idle = new(initialState: true);
    private Timer _timer;
    private bool _checking;
    private bool _disposed;

    internal BasicOperationsMonitor(
        SqliteBackupService backupService,
        BasicOperationsThresholds thresholds = null,
        Func<PerformanceSnapshot> metricsSnapshot = null,
        Func<DateTimeOffset> clock = null,
        Action<string> alertSink = null)
        : this(
            metricsSnapshot ?? PerformanceMetrics.CreateSnapshot,
            backupService == null ? null : backupService.GetStatus,
            thresholds,
            clock,
            alertSink,
            Settings.DatabaseProvider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
    {
    }

    internal BasicOperationsMonitor(
        Func<PerformanceSnapshot> metricsSnapshot,
        Func<SqliteBackupStatus> backupStatus,
        BasicOperationsThresholds thresholds = null,
        Func<DateTimeOffset> clock = null,
        Action<string> alertSink = null,
        bool backupRequired = true)
    {
        _metricsSnapshot = metricsSnapshot ?? throw new ArgumentNullException(nameof(metricsSnapshot));
        _backupStatus = backupStatus;
        _thresholds = thresholds ?? BasicOperationsThresholds.FromSettings();
        _thresholds.Validate();
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _alertSink = alertSink ?? (line => Logger.GetLogger(LogType.Server).Warn(line));
        _backupRequired = backupRequired;
        _monitorStartedAtUtc = _clock();
    }

    internal void Start()
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            _timer ??= new Timer(_ => CheckAndPublish(), null, TimeSpan.Zero, _thresholds.AlertCheckInterval);
        }
    }

    internal BasicOperationsStatus CaptureStatus()
    {
        PerformanceSnapshot metrics = _metricsSnapshot() ?? new PerformanceSnapshot();
        SqliteBackupStatus backup = _backupStatus?.Invoke();
        DateTimeOffset now = _clock();
        PerformanceMetricSnapshot online = Find(metrics, PerformanceMetricKind.ActiveConnections);
        PerformanceMetricSnapshot tick = Find(metrics, PerformanceMetricKind.Update);
        PerformanceMetricSnapshot save = Find(metrics, PerformanceMetricKind.Save);
        PerformanceMetricSnapshot saveFailure = Find(metrics, PerformanceMetricKind.SaveFailure);
        PerformanceMetricSnapshot queue = Find(metrics, PerformanceMetricKind.NetworkQueue);

        var status = new BasicOperationsStatus
        {
            GeneratedAtUtc = now,
            MetricsEnabled = metrics.Enabled,
            OnlinePlayers = online?.LastValue,
            TickP95Milliseconds = tick?.P95Milliseconds,
            SaveP95Milliseconds = save?.P95Milliseconds,
            SaveFailures = saveFailure?.TotalValue,
            NetworkQueueDepth = queue?.LastValue,
            BackupRequired = _backupRequired,
            Backup = backup,
        };

        return new BasicOperationsStatus
        {
            GeneratedAtUtc = status.GeneratedAtUtc,
            MetricsEnabled = status.MetricsEnabled,
            OnlinePlayers = status.OnlinePlayers,
            TickP95Milliseconds = status.TickP95Milliseconds,
            SaveP95Milliseconds = status.SaveP95Milliseconds,
            SaveFailures = status.SaveFailures,
            NetworkQueueDepth = status.NetworkQueueDepth,
            BackupRequired = status.BackupRequired,
            Backup = status.Backup,
            Alerts = EvaluateAlerts(status),
        };
    }

    internal void CheckAndPublish()
    {
        lock (_gate)
        {
            if (_disposed || _checking) return;
            _checking = true;
            _idle.Reset();
        }

        try
        {
            BasicOperationsStatus status = CaptureStatus();
            lock (_gate)
            {
                if (_disposed) return;
                var currentCodes = status.Alerts.Select(alert => alert.Code).ToHashSet(StringComparer.Ordinal);
                foreach (BasicOperationsAlert alert in status.Alerts)
                {
                    if (_activeAlertCodes.Contains(alert.Code)) continue;
                    _alertSink($"OPS_ALERT state=triggered severity={alert.Severity} code={alert.Code} message={alert.Message}");
                    _activeAlertCodes.Add(alert.Code);
                }

                foreach (string recovered in _activeAlertCodes.Where(code => !currentCodes.Contains(code)).ToArray())
                {
                    _alertSink($"OPS_ALERT state=recovered code={recovered}");
                    _activeAlertCodes.Remove(recovered);
                }
            }
        }
        catch (Exception error)
        {
            try { _alertSink("OPS_ALERT state=monitor-failed severity=critical message=" + Safe(error.Message)); }
            catch { }
        }
        finally
        {
            lock (_gate)
            {
                _checking = false;
                _idle.Set();
            }
        }
    }

    public void Dispose()
    {
        Timer timer;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            timer = _timer;
            _timer = null;
        }
        timer?.Dispose();
        _idle.Wait();
        _idle.Dispose();
    }

    private IReadOnlyList<BasicOperationsAlert> EvaluateAlerts(BasicOperationsStatus status)
    {
        var alerts = new List<BasicOperationsAlert>();
        if (!status.MetricsEnabled)
            Add(alerts, "metrics-unavailable", "critical", "服务端运行指标未启用");
        if (status.TickP95Milliseconds >= _thresholds.TickP95WarningMilliseconds)
            Add(alerts, "tick-p95-high", "warning", $"Tick p95 达到 {status.TickP95Milliseconds:F2}ms");
        if (status.SaveP95Milliseconds >= _thresholds.SaveP95WarningMilliseconds)
            Add(alerts, "save-p95-high", "warning", $"保存 p95 达到 {status.SaveP95Milliseconds:F2}ms");
        if (status.SaveFailures > 0)
            Add(alerts, "save-failed", "critical", $"当前指标会话已有 {status.SaveFailures} 次最终保存失败");
        if (status.NetworkQueueDepth >= _thresholds.NetworkQueueWarningDepth)
            Add(alerts, "network-queue-high", "warning", $"网络队列积压达到 {status.NetworkQueueDepth}");

        if (status.BackupRequired && status.Backup == null)
        {
            Add(alerts, "backup-unavailable", "critical", "SQLite 备份服务未启用");
        }
        else if (status.BackupRequired && status.Backup.State == SqliteBackupState.Failed)
        {
            Add(alerts, "backup-failed", "critical", "最近一次 SQLite 备份失败");
        }
        else if (status.BackupRequired && IsBackupStale(status))
        {
            Add(alerts, "backup-stale", "warning", "SQLite 备份已超过允许时效");
        }

        return alerts;
    }

    private static PerformanceMetricSnapshot Find(PerformanceSnapshot snapshot, PerformanceMetricKind kind) =>
        snapshot.Metrics?.FirstOrDefault(metric => metric.Name == kind.ToString() && metric.Available);

    private static void Add(List<BasicOperationsAlert> alerts, string code, string severity, string message) =>
        alerts.Add(new BasicOperationsAlert { Code = code, Severity = severity, Message = message });

    private bool IsBackupStale(BasicOperationsStatus status)
    {
        DateTimeOffset reference = status.Backup.LastSuccessUtc ??
                                   status.Backup.LastAttemptUtc ??
                                   _monitorStartedAtUtc;
        return status.GeneratedAtUtc - reference > _thresholds.BackupMaximumAge;
    }

    private static string Safe(string value) =>
        string.IsNullOrWhiteSpace(value) ? "unknown" : value.Replace('\r', '_').Replace('\n', '_');

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(BasicOperationsMonitor));
    }
}
