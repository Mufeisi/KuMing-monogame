using System.Text.Json;
using Shared.Release;
using Xunit;

namespace Base05.Tests;

public sealed class Release02TransactionalDeploymentTests
{
    [Fact]
    public void 验证通过后原子发布全部文件()
    {
        using var fixture = new DeploymentFixture();
        string existing = fixture.Target("Data/existing.bin", "old");
        string created = fixture.TargetPath("Data/created.bin");

        TransactionalFileDeploymentResult result = TransactionalFileDeployment.Apply(
            fixture.TransactionRoot,
            new[] { fixture.TargetRoot },
            new[]
            {
                fixture.Entry("incoming-existing.bin", "new-existing", existing),
                fixture.Entry("incoming-created.bin", "new-created", created),
            },
            verifyAfterPublish: () => File.ReadAllText(existing) == "new-existing" && File.ReadAllText(created) == "new-created");

        Assert.Equal(2, result.PublishedFileCount);
        Assert.True(result.Verified);
        Assert.Equal("new-existing", File.ReadAllText(existing));
        Assert.Equal("new-created", File.ReadAllText(created));
        Assert.Empty(Directory.GetDirectories(fixture.TransactionRoot));
    }

    [Fact]
    public void 发布后验证失败恢复上一版本并删除新增文件()
    {
        using var fixture = new DeploymentFixture();
        string existing = fixture.Target("Data/existing.bin", "old");
        string created = fixture.TargetPath("Data/created.bin");

        Assert.Throws<InvalidDataException>(() => TransactionalFileDeployment.Apply(
            fixture.TransactionRoot,
            new[] { fixture.TargetRoot },
            new[]
            {
                fixture.Entry("incoming-existing.bin", "new-existing", existing),
                fixture.Entry("incoming-created.bin", "new-created", created),
            },
            verifyAfterPublish: () => false));

        Assert.Equal("old", File.ReadAllText(existing));
        Assert.False(File.Exists(created));
        Assert.Empty(Directory.GetDirectories(fixture.TransactionRoot));
    }

    [Fact]
    public void 整版资源与版本队列同事务提交并可在重启后读取()
    {
        using var fixture = new DeploymentFixture();
        string asset = fixture.Target("Packages/pack-a/data.bin", "old-asset");
        string versions = fixture.Target("State/BootstrapPackageVersions.json", "{\"Packages\":[]}");
        string queue = fixture.Target("State/BootstrapPackageUpdateQueue.json", "{\"Packages\":[{\"Name\":\"pack-a\"}]}");

        TransactionalFileDeployment.Apply(
            fixture.TransactionRoot,
            new[] { fixture.TargetRoot },
            new[]
            {
                fixture.Entry("incoming/data.bin", "new-asset", asset),
                fixture.Entry("incoming/versions.json", "{\"Packages\":[{\"Name\":\"pack-a\",\"Sha256\":\"abc\"}]}", versions),
                fixture.Entry("incoming/queue.json", "{\"Packages\":[]}", queue),
            });

        Assert.Equal("new-asset", File.ReadAllText(asset));
        using JsonDocument restartedVersions = JsonDocument.Parse(File.ReadAllText(versions));
        using JsonDocument restartedQueue = JsonDocument.Parse(File.ReadAllText(queue));
        Assert.Equal("pack-a", restartedVersions.RootElement.GetProperty("Packages")[0].GetProperty("Name").GetString());
        Assert.Empty(restartedQueue.RootElement.GetProperty("Packages").EnumerateArray());
    }

    [Fact]
    public void 整版状态验证失败同时恢复资源版本和待更新队列()
    {
        using var fixture = new DeploymentFixture();
        string asset = fixture.Target("Packages/pack-a/data.bin", "old-asset");
        string versions = fixture.Target("State/BootstrapPackageVersions.json", "old-versions");
        string queue = fixture.Target("State/BootstrapPackageUpdateQueue.json", "old-queue");

        Assert.Throws<InvalidDataException>(() => TransactionalFileDeployment.Apply(
            fixture.TransactionRoot,
            new[] { fixture.TargetRoot },
            new[]
            {
                fixture.Entry("incoming/data.bin", "new-asset", asset),
                fixture.Entry("incoming/versions.json", "new-versions", versions),
                fixture.Entry("incoming/queue.json", "new-queue", queue),
            },
            verifyAfterPublish: () => false));

        Assert.Equal("old-asset", File.ReadAllText(asset));
        Assert.Equal("old-versions", File.ReadAllText(versions));
        Assert.Equal("old-queue", File.ReadAllText(queue));
    }

    [Fact]
    public void 重启恢复未完成事务并保持上一版本()
    {
        using var fixture = new DeploymentFixture();
        string existing = fixture.Target("Data/existing.bin", "new-after-crash");
        string created = fixture.Target("Data/created.bin", "new-created-after-crash");
        string transaction = Path.Combine(fixture.TransactionRoot, "txn-crash");
        string backupDirectory = Path.Combine(transaction, "backups");
        Directory.CreateDirectory(backupDirectory);
        string existingBackup = Path.Combine(backupDirectory, "000000.bak");
        string createdBackup = Path.Combine(backupDirectory, "000001.bak");
        File.WriteAllText(existingBackup, "old-before-crash");
        var journal = new
        {
            Format = TransactionalFileDeployment.JournalFormat,
            Status = "Applying",
            Entries = new object[]
            {
                new { SourcePath = fixture.Source("source-existing.bin", "source"), TargetPath = existing, BackupPath = existingBackup, Existed = true },
                new { SourcePath = fixture.Source("source-created.bin", "source"), TargetPath = created, BackupPath = createdBackup, Existed = false },
            },
        };
        File.WriteAllText(Path.Combine(transaction, TransactionalFileDeployment.JournalFileName), JsonSerializer.Serialize(journal));

        int recovered = TransactionalFileDeployment.RecoverIncomplete(fixture.TransactionRoot, new[] { fixture.TargetRoot });

        Assert.Equal(1, recovered);
        Assert.Equal("old-before-crash", File.ReadAllText(existing));
        Assert.False(File.Exists(created));
        Assert.False(Directory.Exists(transaction));
    }

    [Fact]
    public void 越出允许根目录的目标在写入前拒绝()
    {
        using var fixture = new DeploymentFixture();
        string outside = Path.Combine(fixture.Root, "outside.bin");
        TransactionalFileDeploymentEntry entry = fixture.Entry("incoming.bin", "new", outside);

        Assert.Throws<UnauthorizedAccessException>(() => TransactionalFileDeployment.Apply(
            fixture.TransactionRoot,
            new[] { fixture.TargetRoot },
            new[] { entry }));
        Assert.False(File.Exists(outside));
    }

    [Fact]
    public async Task 跨进程文件锁阻止恢复器碰触正在提交的事务()
    {
        using var fixture = new DeploymentFixture();
        string target = fixture.Target("Data/live.bin", "old");
        using var enteredVerify = new ManualResetEventSlim(false);
        using var releaseVerify = new ManualResetEventSlim(false);
        Task apply = Task.Run(() => TransactionalFileDeployment.Apply(
            fixture.TransactionRoot,
            new[] { fixture.TargetRoot },
            new[] { fixture.Entry("incoming-live.bin", "new", target) },
            () =>
            {
                enteredVerify.Set();
                releaseVerify.Wait(TimeSpan.FromSeconds(10));
                return true;
            }));
        Assert.True(enteredVerify.Wait(TimeSpan.FromSeconds(5)));

        Task<int> recover = Task.Run(() => TransactionalFileDeployment.RecoverIncomplete(fixture.TransactionRoot, new[] { fixture.TargetRoot }));
        await Task.Delay(200);
        Assert.False(recover.IsCompleted);
        releaseVerify.Set();
        await apply;

        Assert.Equal(0, await recover);
        Assert.Equal("new", File.ReadAllText(target));
    }

    private sealed class DeploymentFixture : IDisposable
    {
        public DeploymentFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), "lyocrystal-release02-" + Guid.NewGuid().ToString("N"));
            TargetRoot = Path.Combine(Root, "target");
            TransactionRoot = Path.Combine(Root, "transactions");
            Directory.CreateDirectory(TargetRoot);
            Directory.CreateDirectory(TransactionRoot);
        }

        public string Root { get; }
        public string TargetRoot { get; }
        public string TransactionRoot { get; }

        public TransactionalFileDeploymentEntry Entry(string sourceName, string content, string target) => new()
        {
            SourcePath = Source(sourceName, content),
            TargetPath = target,
        };

        public string Source(string name, string content)
        {
            string path = Path.Combine(Root, "source", name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
            return path;
        }

        public string Target(string relative, string content)
        {
            string path = TargetPath(relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
            return path;
        }

        public string TargetPath(string relative) => Path.Combine(TargetRoot, relative.Replace('/', Path.DirectorySeparatorChar));

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }
}
