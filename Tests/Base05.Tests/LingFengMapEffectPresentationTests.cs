using System.Drawing;
using Xunit;

namespace Base05.Tests;

public sealed class LingFengMapEffectPresentationTests
{
    [Theory]
    [InlineData(-1, true, 0)]
    [InlineData(0, true, 0)]
    [InlineData(1, false, 0)]
    [InlineData(3, true, 1600)]
    public void 双端共用计划保留循环次数与结束时点(
        int repeatCount,
        bool expectedRepeat,
        long expectedRepeatUntil)
    {
        var packet = new ServerPackets.LingFengMapEffect
        {
            StartIndex = 10,
            FrameCount = 5,
            FrameDelay = 100,
            Layer = 1,
            Light = 2,
            AnchorObjectId = 77,
            PixelOffset = new Point(-85, -220),
            RepeatCount = repeatCount
        };

        Assert.True(LingFengMapEffectPresentationPlan.TryCreate(
            packet, 100, out LingFengMapEffectPresentationPlan plan));
        Assert.Equal(500, plan.Duration);
        Assert.Equal(77u, plan.AnchorObjectId);
        Assert.Equal(new Point(-85, -220), plan.PixelOffset);
        Assert.Equal(expectedRepeat, plan.Repeat);
        Assert.Equal(expectedRepeatUntil, plan.RepeatUntil);
    }

    [Fact]
    public void 锚点存在时随对象挂载缺失时回落地图坐标()
    {
        var packet = new ServerPackets.LingFengMapEffect
        {
            StartIndex = 1,
            FrameCount = 2,
            FrameDelay = 50,
            Layer = 0,
            AnchorObjectId = 22,
            RepeatCount = 1
        };
        Assert.True(LingFengMapEffectPresentationPlan.TryCreate(
            packet, 0, out LingFengMapEffectPresentationPlan plan));
        var objects = new[] { new Anchor(11), new Anchor(22) };

        Assert.Same(objects[1], plan.ResolveAnchor(objects, value => value.Id));
        Assert.Null(plan.ResolveAnchor(new[] { objects[0] }, value => value.Id));
    }

    [Fact]
    public void 无效帧参数在进入任一客户端渲染对象前拒绝()
    {
        var packet = new ServerPackets.LingFengMapEffect
        {
            StartIndex = 0,
            FrameCount = int.MaxValue,
            FrameDelay = int.MaxValue,
            Layer = 1,
            RepeatCount = 1
        };

        Assert.False(LingFengMapEffectPresentationPlan.TryCreate(packet, 0, out _));
    }

    private sealed record Anchor(uint Id);
}
