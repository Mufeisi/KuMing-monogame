using System.Diagnostics;
using Server.MirEnvir;
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
            Assert.Equal("log2-sub-bucket-upper-bound", update.PercentileMethod);
            Assert.Equal(0.25D, update.PercentileMaxRelativeError);
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
            Assert.Equal("log2-sub-bucket-upper-bound", update.DurationPercentileMethod);
            // 4096 之后仍然会影响 p95/p99；固定子桶代表值为保守上界。
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
    public void QueueTrackerRebaselinesHighWaterWhenSessionChanges()
    {
        try
        {
            PerformanceMetrics.Configure(enabled: true, scenario: "queue-old");
            var tracker = new PerformanceQueueTracker();
            tracker.Enqueue();
            tracker.Enqueue();
            tracker.Dequeue(2);
            Assert.Equal(2, tracker.HighWater);

            PerformanceMetrics.StartSession("queue-new");
            tracker.Enqueue();
            Assert.Equal(1, tracker.HighWater);
        }
        finally
        {
            PerformanceMetrics.Configure(enabled: false);
        }
    }

    [Fact]
    public void HistogramPercentileUsesConservativeBoundedSubBucket()
    {
        try
        {
            PerformanceMetrics.Configure(enabled: true, scenario: "histogram-bound");
            for (var i = 0; i < 100; i++)
                PerformanceMetrics.RecordValue(PerformanceMetricKind.NetworkQueue, 100);

            var metric = PerformanceMetrics.CreateSnapshot().Metrics
                .Single(item => item.Name == nameof(PerformanceMetricKind.NetworkQueue));

            Assert.InRange(metric.P95Value!.Value, 100, 125);
            Assert.Equal("log2-sub-bucket-upper-bound", metric.ValuePercentileMethod);
            Assert.Equal(0.25D, metric.PercentileMaxRelativeError);

            PerformanceMetrics.Configure(enabled: true, scenario: "histogram-zero");
            PerformanceMetrics.RecordValue(PerformanceMetricKind.NetworkQueue, 0);
            var zeroMetric = PerformanceMetrics.CreateSnapshot().Metrics
                .Single(item => item.Name == nameof(PerformanceMetricKind.NetworkQueue));
            Assert.Equal(0L, zeroMetric.P95Value);
        }
        finally
        {
            PerformanceMetrics.Configure(enabled: false);
        }
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
    public void EnvironmentDisableEndsPreviousSessionAndKeepsExport()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lyocrystal-perf00-env-disable-{Guid.NewGuid():N}.json");
        var oldEnabled = Environment.GetEnvironmentVariable("LYOCRYSTAL_PERF00_ENABLED");
        var oldScenario = Environment.GetEnvironmentVariable("LYOCRYSTAL_PERF00_SCENARIO");
        var oldOutput = Environment.GetEnvironmentVariable("LYOCRYSTAL_PERF00_OUTPUT");
        try
        {
            Environment.SetEnvironmentVariable("LYOCRYSTAL_PERF00_ENABLED", "true");
            Environment.SetEnvironmentVariable("LYOCRYSTAL_PERF00_SCENARIO", "env-disable");
            Environment.SetEnvironmentVariable("LYOCRYSTAL_PERF00_OUTPUT", path);
            Assert.True(PerformanceMetrics.TryConfigureFromEnvironment(out var startReason), startReason);
            PerformanceMetrics.RecordValue(PerformanceMetricKind.NetworkQueue, 9);

            Environment.SetEnvironmentVariable("LYOCRYSTAL_PERF00_ENABLED", "false");
            Assert.False(PerformanceMetrics.TryConfigureFromEnvironment(out var stopReason), stopReason);
            Assert.False(PerformanceMetrics.Enabled);
            Assert.Contains("env-disable", File.ReadAllText(path));
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
    public void ServerStopFreezesAndExportsConfiguredSession()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lyocrystal-perf00-server-stop-{Guid.NewGuid():N}.json");
        var oldEnabled = Environment.GetEnvironmentVariable("LYOCRYSTAL_PERF00_ENABLED");
        var oldScenario = Environment.GetEnvironmentVariable("LYOCRYSTAL_PERF00_SCENARIO");
        var oldOutput = Environment.GetEnvironmentVariable("LYOCRYSTAL_PERF00_OUTPUT");
        try
        {
            Environment.SetEnvironmentVariable("LYOCRYSTAL_PERF00_ENABLED", "true");
            Environment.SetEnvironmentVariable("LYOCRYSTAL_PERF00_SCENARIO", "server-stop");
            Environment.SetEnvironmentVariable("LYOCRYSTAL_PERF00_OUTPUT", path);
            Assert.True(PerformanceMetrics.TryConfigureFromEnvironment(out var startReason), startReason);
            PerformanceMetrics.RecordValue(PerformanceMetricKind.Connections, 1);

            new Envir().Stop();

            Assert.False(PerformanceMetrics.Enabled);
            Assert.Contains("server-stop", File.ReadAllText(path));
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
    public void RuntimeSamplingIncludesGcBetweenSessionStartAndFirstSample()
    {
        try
        {
            PerformanceMetrics.Configure(enabled: true, scenario: "runtime-baseline");
            var garbage = new byte[1024 * 1024];
            garbage[0] = 1;
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            PerformanceMetrics.SampleRuntime();

            var metrics = PerformanceMetrics.CreateSnapshot().Metrics;
            var gc = metrics.Single(item => item.Name == nameof(PerformanceMetricKind.Gc));
            Assert.True(gc.TotalValue >= 1, $"预期会话开始后的强制 GC 被计入，实际为 {gc.TotalValue}");
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
