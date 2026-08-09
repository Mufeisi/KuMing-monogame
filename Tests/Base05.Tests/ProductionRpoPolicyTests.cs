using Server;
using Server.Security;
using Xunit;

namespace Base05.Tests;

public sealed class ProductionRpoPolicyTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    public void 正式保存间隔接受一分钟和五分钟边界(int minutes)
    {
        ProductionRpoPolicy.ValidateSaveDelay(minutes, enforceProductionMaximum: true);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(60)]
    public void 正式保存间隔拒绝越界值(int minutes)
    {
        Assert.Throws<InvalidOperationException>(() =>
            ProductionRpoPolicy.ValidateSaveDelay(minutes, enforceProductionMaximum: true));
    }

    [Fact]
    public void 测试服只放宽上限但仍拒绝零和负数()
    {
        ProductionRpoPolicy.ValidateSaveDelay(60, enforceProductionMaximum: false);
        Assert.Throws<InvalidOperationException>(() =>
            ProductionRpoPolicy.ValidateSaveDelay(0, enforceProductionMaximum: false));
    }

    [Fact]
    public void 故障注入在下一次保存前一毫秒崩溃时RPO仍小于五分钟()
    {
        const long lastCommittedAt = 123456;
        long nextSaveDeadline = ProductionRpoPolicy.GetNextAutoSaveDeadline(
            lastCommittedAt,
            ProductionRpoPolicy.MaximumSaveDelayMinutes);

        long injectedCrashAt = nextSaveDeadline - 1;
        long unpersistedWindow = injectedCrashAt - lastCommittedAt;

        Assert.True(unpersistedWindow < TimeSpan.FromMinutes(5).TotalMilliseconds);
        Assert.Equal((long)TimeSpan.FromMinutes(5).TotalMilliseconds, nextSaveDeadline - lastCommittedAt);
    }
}
