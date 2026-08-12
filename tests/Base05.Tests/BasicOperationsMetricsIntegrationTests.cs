using System.Diagnostics;
using Server.Operations;
using Server.Persistence.Sql;
using Shared.Diagnostics;
using Xunit;

namespace Base05.Tests;

[Collection("PerformanceMetrics")]
public sealed class BasicOperationsMetricsIntegrationTests
{
    [Fact]
    public void 基础监控直接消费Perf00真实快照()
    {
        try
        {
            PerformanceMetrics.Configure(enabled: true, scenario: "ops-basic-01-test");
            PerformanceMetrics.SetGauge(PerformanceMetricKind.ActiveConnections, 17);
            PerformanceMetrics.SetGauge(PerformanceMetricKind.NetworkQueue, 6);
            PerformanceMetrics.RecordDuration(PerformanceMetricKind.Update, MillisecondsToTicks(25));
            PerformanceMetrics.RecordDuration(PerformanceMetricKind.Save, MillisecondsToTicks(80));
            DateTimeOffset now = DateTimeOffset.UtcNow;

            using var monitor = new BasicOperationsMonitor(
                PerformanceMetrics.CreateSnapshot,
                () => new SqliteBackupStatus
                {
                    State = SqliteBackupState.Succeeded,
                    LastSuccessUtc = now,
                    IntegrityResult = "ok",
                },
                clock: () => now);

            BasicOperationsStatus status = monitor.CaptureStatus();

            Assert.Equal(17, status.OnlinePlayers);
            Assert.Equal(6, status.NetworkQueueDepth);
            Assert.InRange(status.TickP95Milliseconds!.Value, 25, 32);
            Assert.InRange(status.SaveP95Milliseconds!.Value, 80, 100);
        }
        finally
        {
            PerformanceMetrics.Configure(enabled: false);
        }
    }

    private static long MillisecondsToTicks(long milliseconds) =>
        Math.Max(1, milliseconds * Stopwatch.Frequency / 1000);
}
