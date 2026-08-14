using LyoCrystal.Workbench;
using Xunit;

namespace Base05.Tests;

public sealed class WorkbenchOverviewTests
{
    [Fact]
    public async Task 聚合保留事实所有者且单个提供器失败不吞掉其他事实()
    {
        var service = new WorkbenchOverviewService([
            new StaticProvider("发行体", [new WorkbenchFact("resource", WorkbenchFactKind.Version, "资源版本", "v1", "发行体", WorkbenchFactStatus.Passed)]),
            new FailingProvider("实例运行")
        ]);

        WorkbenchOverviewSnapshot snapshot = await service.CollectAsync();

        Assert.Contains(snapshot.Facts, item => item.Id == "resource" && item.Owner == "发行体");
        Assert.Contains(snapshot.Facts, item => item.Id == "provider-failure/实例运行" && item.Owner == "实例运行" && item.Status == WorkbenchFactStatus.Failed);
        Assert.False(snapshot.Passed);
    }

    [Fact]
    public void 版本差异按稳定标识输出新增删除修改与未变()
    {
        var before = Snapshot(
            Version("changed", "旧"), Version("removed", "有"), Version("same", "相同"));
        var after = Snapshot(
            Version("added", "新"), Version("changed", "新"), Version("same", "相同"));

        IReadOnlyList<WorkbenchVersionChange> changes = WorkbenchVersionDiff.Compare(before, after);

        Assert.Equal(WorkbenchVersionChangeKind.Added, changes.Single(item => item.Id == "added").Change);
        Assert.Equal(WorkbenchVersionChangeKind.Removed, changes.Single(item => item.Id == "removed").Change);
        Assert.Equal(WorkbenchVersionChangeKind.Changed, changes.Single(item => item.Id == "changed").Change);
        Assert.Equal(WorkbenchVersionChangeKind.Unchanged, changes.Single(item => item.Id == "same").Change);
    }

    private static WorkbenchFact Version(string id, string value) => new(id, WorkbenchFactKind.Version, id, value, "版本源", WorkbenchFactStatus.Passed);
    private static WorkbenchOverviewSnapshot Snapshot(params WorkbenchFact[] facts) => new(DateTimeOffset.UtcNow, facts);

    private sealed class StaticProvider(string owner, IReadOnlyList<WorkbenchFact> facts) : IWorkbenchFactProvider
    {
        public string Owner => owner;
        public Task<IReadOnlyList<WorkbenchFact>> CollectAsync(CancellationToken cancellationToken) => Task.FromResult(facts);
    }

    private sealed class FailingProvider(string owner) : IWorkbenchFactProvider
    {
        public string Owner => owner;
        public Task<IReadOnlyList<WorkbenchFact>> CollectAsync(CancellationToken cancellationToken) => throw new IOException("测试失败");
    }
}
