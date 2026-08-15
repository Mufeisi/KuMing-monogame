using Server.Scripting;
using Xunit;

namespace Base05.Tests;

public sealed class TxtScriptExecutionBudgetTests
{
    [Fact]
    public void 即时跳转预算到达上限后稳定拒绝后续步骤()
    {
        var budget = new TxtScriptExecutionBudget(3);

        Assert.True(budget.TryConsume());
        Assert.True(budget.TryConsume());
        Assert.True(budget.TryConsume());
        Assert.False(budget.TryConsume());
        Assert.False(budget.TryConsume());
        Assert.Equal(3, budget.ConsumedSteps);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void 非正数预算失败关闭为一步(int configuredMaximum)
    {
        var budget = new TxtScriptExecutionBudget(configuredMaximum);

        Assert.Equal(1, budget.MaximumSteps);
        Assert.True(budget.TryConsume());
        Assert.False(budget.TryConsume());
    }
}
