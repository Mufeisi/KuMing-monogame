using Shared.Diagnostics;
using Xunit;

namespace Base05.Tests;

[Collection("PerformanceMetrics")]
public sealed class PerformanceMetricsTests
{
    [Fact]
    public void DisabledMetricsDoNotAccumulate()
    {
        try
        {
            PerformanceMetrics.Configure(enabled: false, scenario: "disabled-test");

            PerformanceMetrics.RecordDuration(PerformanceMetricKind.Update, 100);
            PerformanceMetrics.Increment(PerformanceMetricKind.DrawCall, 4);
            PerformanceMetrics.SetGauge(PerformanceMetricKind.NetworkQueue, 3);

            var metric = PerformanceMetrics.CreateSnapshot().Metrics
                .Single(item => item.Name == nameof(PerformanceMetricKind.Update));

            Assert.False(PerformanceMetrics.Enabled);
            Assert.Equal(0, metric.Samples);
            Assert.False(metric.Available);
        }
        finally
        {
            PerformanceMetrics.Configure(enabled: false);
        }
    }

    [Fact]
    public void EnabledMetricsCapturePercentilesAndExport()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lyocrystal-perf00-{Guid.NewGuid():N}.json");
        try
        {
            PerformanceMetrics.Configure(enabled: true, scenario: "unit-test");

            for (var i = 1; i <= 100; i++)
                PerformanceMetrics.RecordDuration(PerformanceMetricKind.Update, i);

            PerformanceMetrics.Increment(PerformanceMetricKind.DrawCall, 3);
            PerformanceMetrics.SetGauge(PerformanceMetricKind.NetworkQueue, 7);

            var snapshot = PerformanceMetrics.CreateSnapshot();
            var update = snapshot.Metrics.Single(item => item.Name == nameof(PerformanceMetricKind.Update));
            var drawCalls = snapshot.Metrics.Single(item => item.Name == nameof(PerformanceMetricKind.DrawCall));
            var queue = snapshot.Metrics.Single(item => item.Name == nameof(PerformanceMetricKind.NetworkQueue));

            Assert.Equal("unit-test", snapshot.Scenario);
            Assert.Equal(100, update.Samples);
            Assert.True(update.P95Milliseconds > 0);
            Assert.True(update.P99Milliseconds >= update.P95Milliseconds);
            Assert.Equal(3, drawCalls.TotalValue);
            Assert.Equal(7, queue.LastValue);

            Assert.True(PerformanceMetrics.TryWriteSnapshot(path, out var error), error);
            Assert.Contains("unit-test", File.ReadAllText(path));
        }
        finally
        {
            PerformanceMetrics.Configure(enabled: false);
            TryDelete(path);
        }
    }

    [Fact]
    public void ScopeFromFrozenSessionCannotWriteToNextSession()
    {
        PerformanceMetricsSession oldSession = null;
        try
        {
            oldSession = PerformanceMetrics.StartSession("old");
            var oldScope = PerformanceMetrics.Begin(PerformanceMetricKind.Update);

            PerformanceMetrics.StartSession("new");
            oldScope.Dispose();
            PerformanceMetrics.RecordDuration(PerformanceMetricKind.Update, 10);

            var snapshot = PerformanceMetrics.CreateSnapshot();
            Assert.Equal("new", snapshot.Scenario);
            var update = snapshot.Metrics.Single(item => item.Name == nameof(PerformanceMetricKind.Update));
            Assert.Equal(1, update.Samples);
            Assert.NotEqual(oldSession.SessionId, snapshot.SessionId);
        }
        finally
        {
            PerformanceMetrics.Configure(enabled: false);
        }
    }

    [Fact]
    public async Task ConcurrentFreezeExportsToSamePathRemainValid()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lyocrystal-perf00-concurrent-{Guid.NewGuid():N}.json");
        try
        {
            PerformanceMetrics.Configure(enabled: true, scenario: "concurrent");
            for (var i = 0; i < 100; i++)
                PerformanceMetrics.RecordValue(PerformanceMetricKind.NetworkQueue, i);

            var writes = Enumerable.Range(0, 8)
                .Select(_ => Task.Run(() =>
                {
                    var ok = PerformanceMetrics.TryFreezeAndWriteSnapshot(path, out var snapshot, out var error);
                    return (ok, snapshot, error);
                }))
                .ToArray();

            var results = await Task.WhenAll(writes);
            Assert.All(results, result => Assert.True(result.ok, result.error));
            Assert.All(results, result => Assert.Equal("concurrent", result.snapshot.Scenario));
            Assert.Contains("concurrent", File.ReadAllText(path));
        }
        finally
        {
            PerformanceMetrics.Configure(enabled: false);
            TryDelete(path);
        }
    }

    [Fact]
    public void RuntimeSamplingSeparatesGcPauseFromCollectionCount()
    {
        try
        {
            PerformanceMetrics.Configure(enabled: true, scenario: "runtime");
            PerformanceMetrics.SampleRuntime();
            PerformanceMetrics.SampleRuntime();

            var snapshot = PerformanceMetrics.CreateSnapshot();
            var gc = snapshot.Metrics.Single(item => item.Name == nameof(PerformanceMetricKind.Gc));
            var pause = snapshot.Metrics.Single(item => item.Name == nameof(PerformanceMetricKind.GcPause));
            Assert.True(gc.Available);
            Assert.True(pause.Available || pause.UnavailableReason != null);
            Assert.NotEqual(nameof(PerformanceMetricKind.Gc), nameof(PerformanceMetricKind.GcPause));
        }
        finally
        {
            PerformanceMetrics.Configure(enabled: false);
        }
    }

    [Fact]
    public void UnavailableMetricKeepsReasonWithoutInventingZero()
    {
        try
        {
            PerformanceMetrics.Configure(enabled: true, scenario: "unavailable");
            PerformanceMetrics.MarkUnavailable(
                PerformanceMetricKind.GpuMemory,
                "测试后端未提供显存预算 API");

            var gpu = PerformanceMetrics.CreateSnapshot().Metrics
                .Single(item => item.Name == nameof(PerformanceMetricKind.GpuMemory));

            Assert.False(gpu.Available);
            Assert.Equal(0, gpu.Samples);
            Assert.Null(gpu.LastValue);
            Assert.Equal("测试后端未提供显存预算 API", gpu.UnavailableReason);
        }
        finally
        {
            PerformanceMetrics.Configure(enabled: false);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
        }
    }
}
