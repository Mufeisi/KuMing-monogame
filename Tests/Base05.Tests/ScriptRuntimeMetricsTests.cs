using System.Diagnostics;
using Server;
using Server.MirObjects;
using Server.Scripting;
using Xunit;

namespace Base05.Tests;

[Collection(nameof(PhysicalTextFileProviderCollection))]
public sealed class ScriptRuntimeMetricsTests
{
    [Fact]
    public void 运行时指标报告最近样本的P95_P99与全期最大值()
    {
        bool oldEnabled = Settings.ScriptsRuntimeMetricsEnabled;
        int oldAutoDumpSeconds = Settings.ScriptsRuntimeMetricsAutoDumpSeconds;
        try
        {
            Settings.ScriptsRuntimeMetricsEnabled = true;
            Settings.ScriptsRuntimeMetricsAutoDumpSeconds = 0;
            ScriptRuntimeMetrics.Clear();

            for (int milliseconds = 1; milliseconds <= 100; milliseconds++)
            {
                long ticks = (long)Math.Round(milliseconds * Stopwatch.Frequency / 1000.0);
                ScriptRuntimeMetrics.RecordCSharpHandler("OnPlayerLogin", ticks);
            }

            ScriptRuntimeMetrics.EntrySnapshot entry = Assert.Single(
                ScriptRuntimeMetrics.CreateSnapshot().Entries);

            Assert.Equal(100, entry.Count);
            Assert.Equal(100, entry.RecentSampleCount);
            Assert.InRange(entry.P95Milliseconds, 94.99, 95.01);
            Assert.InRange(entry.P99Milliseconds, 98.99, 99.01);
            Assert.InRange(entry.MaximumMilliseconds, 99.99, 100.01);
        }
        finally
        {
            ScriptRuntimeMetrics.Clear();
            Settings.ScriptsRuntimeMetricsEnabled = oldEnabled;
            Settings.ScriptsRuntimeMetricsAutoDumpSeconds = oldAutoDumpSeconds;
        }
    }

    [Fact]
    public void 最近样本窗口有界且全期计数与最大值不丢失()
    {
        bool oldEnabled = Settings.ScriptsRuntimeMetricsEnabled;
        int oldAutoDumpSeconds = Settings.ScriptsRuntimeMetricsAutoDumpSeconds;
        try
        {
            Settings.ScriptsRuntimeMetricsEnabled = true;
            Settings.ScriptsRuntimeMetricsAutoDumpSeconds = 0;
            ScriptRuntimeMetrics.Clear();

            for (int index = 1; index <= 3000; index++)
                ScriptRuntimeMetrics.RecordLegacyNpcAction(ActionType.GiveGold, index);

            ScriptRuntimeMetrics.EntrySnapshot entry = Assert.Single(
                ScriptRuntimeMetrics.CreateSnapshot().Entries);

            Assert.Equal(3000, entry.Count);
            Assert.Equal(2048, entry.RecentSampleCount);
            Assert.Equal(3000 * 1000.0 / Stopwatch.Frequency, entry.MaximumMilliseconds, 8);
            Assert.True(entry.P99Milliseconds <= entry.MaximumMilliseconds);
        }
        finally
        {
            ScriptRuntimeMetrics.Clear();
            Settings.ScriptsRuntimeMetricsEnabled = oldEnabled;
            Settings.ScriptsRuntimeMetricsAutoDumpSeconds = oldAutoDumpSeconds;
        }
    }
}
