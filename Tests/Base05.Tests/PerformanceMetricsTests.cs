using System.Diagnostics;
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
            Assert.Equal("log2-histogram", update.PercentileMethod);
            Assert.Equal(100, update.PercentileSampleCount);
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
    public void PercentilesCoverSamplesBeyondLegacyReservoirCapacity()
    {
        try
        {
            PerformanceMetrics.Configure(enabled: true, scenario: "histogram-overflow");
            for (var i = 1; i <= 5000; i++)
                PerformanceMetrics.RecordDuration(PerformanceMetricKind.Update, i);

            var update = PerformanceMetrics.CreateSnapshot().Metrics
                .Single(item => item.Name == nameof(PerformanceMetricKind.Update));

            Assert.Equal(5000, update.Samples);
            Assert.Equal(5000, update.DurationPercentileSampleCount);
            Assert.Equal("log2-histogram", update.DurationPercentileMethod);
            // 4096 之后仍然会影响 p95/p99；直方图代表值允许按桶向下近似。
            Assert.True(update.P95Milliseconds >= 4096 * 1000D / Stopwatch.Frequency);
            Assert.True(update.P99Milliseconds >= update.P95Milliseconds);
        }
        finally
        {
            PerformanceMetrics.Configure(enabled: false);
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
    public void QueueTrackerRetainsHighWaterAfterQueueIsDrained()
    {
        var tracker = new PerformanceQueueTracker();
        tracker.Enqueue();
        tracker.Enqueue();
        tracker.Enqueue();
        tracker.Dequeue();
        tracker.Dequeue();
        tracker.Dequeue();

        Assert.Equal(0, tracker.Depth);
        Assert.Equal(3, tracker.HighWater);
        Assert.Equal(3, tracker.CaptureHighWater());
    }

    [Fact]
    public void EnvironmentEntryCanStartStopAndExportProductionSession()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lyocrystal-perf00-env-{Guid.NewGuid():N}.json");
        var oldEnabled = Environment.GetEnvironmentVariable("LYOCRYSTAL_PERF00_ENABLED");
        var oldScenario = Environment.GetEnvironmentVariable("LYOCRYSTAL_PERF00_SCENARIO");
        var oldOutput = Environment.GetEnvironmentVariable("LYOCRYSTAL_PERF00_OUTPUT");
        try
        {
            Environment.SetEnvironmentVariable("LYOCRYSTAL_PERF00_ENABLED", "true");
            Environment.SetEnvironmentVariable("LYOCRYSTAL_PERF00_SCENARIO", "env-entry");
            Environment.SetEnvironmentVariable("LYOCRYSTAL_PERF00_OUTPUT", path);

            Assert.True(PerformanceMetrics.TryConfigureFromEnvironment(out var reason), reason);
            PerformanceMetrics.RecordValue(PerformanceMetricKind.NetworkQueue, 2);
            Assert.True(PerformanceMetrics.TryStopAndWriteConfiguredSnapshot(out var snapshot, out var error), error);
            Assert.Equal("env-entry", snapshot.Scenario);
            Assert.Contains("env-entry", File.ReadAllText(path));
        }
        finally
        {
            Environment.SetEnvironmentVariable("LYOCRYSTAL_PERF00_ENABLED", oldEnabled);
            Environment.SetEnvironmentVariable("LYOCRYSTAL_PERF00_SCENARIO", oldScenario);
            Environment.SetEnvironmentVariable("LYOCRYSTAL_PERF00_OUTPUT", oldOutput);
            PerformanceMetrics.Configure(enabled: false);
            TryDelete(path);
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
            var gen0 = snapshot.Metrics.Single(item => item.Name == nameof(PerformanceMetricKind.GcGen0));
            var gen1 = snapshot.Metrics.Single(item => item.Name == nameof(PerformanceMetricKind.GcGen1));
            var gen2 = snapshot.Metrics.Single(item => item.Name == nameof(PerformanceMetricKind.GcGen2));
            var pause = snapshot.Metrics.Single(item => item.Name == nameof(PerformanceMetricKind.GcPause));
            Assert.True(gc.Available);
            Assert.True(gen0.Available && gen1.Available && gen2.Available);
            Assert.True(gen0.LastValue >= 0 && gen1.LastValue >= 0 && gen2.LastValue >= 0);
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
