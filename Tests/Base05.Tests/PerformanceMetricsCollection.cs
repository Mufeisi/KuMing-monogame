using Xunit;

namespace Base05.Tests;

/// <summary>
/// PerformanceMetrics 是进程级单例；涉及 Configure 的测试必须串行，避免不同测试
/// 互相替换采样会话，导致断言读到别的场景。
/// </summary>
[CollectionDefinition("PerformanceMetrics", DisableParallelization = true)]
public sealed class PerformanceMetricsCollection
{
}
