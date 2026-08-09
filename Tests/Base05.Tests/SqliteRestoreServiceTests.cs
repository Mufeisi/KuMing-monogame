using Microsoft.Data.Sqlite;
using Server.Persistence.Sql;
using Xunit;

namespace Base05.Tests;

public sealed class SqliteRestoreServiceTests
{
    [Fact]
    public void 空白路径在任何文件操作前拒绝()
    {
        Assert.Throws<InvalidOperationException>(() => SqliteRestoreService.Restore(" ", "target.db"));
        Assert.Throws<InvalidOperationException>(() => SqliteRestoreService.Restore("backup.db", " "));
    }

    [Fact]
    public void 空环境从完整副本恢复并可立即读取()
    {
        using var fixture = new RestoreFixture();
        fixture.CreateDatabase(fixture.BackupPath, 42);

        SqliteRestoreResult result = SqliteRestoreService.Restore(fixture.BackupPath, fixture.TargetPath);

        Assert.Equal(42, fixture.ReadValue(fixture.TargetPath));
        Assert.Equal(string.Empty, result.RollbackPath);
        Assert.True(result.DurationMilliseconds < TimeSpan.FromMinutes(30).TotalMilliseconds);
        Assert.Empty(Directory.GetFiles(fixture.TargetDirectory, "*.partial"));
    }

    [Fact]
    public void DB03在线备份产物可直接进入DB04空环境恢复()
    {
        using var fixture = new RestoreFixture();
        string source = Path.Combine(fixture.Root, "live", "server.db");
        string backupDirectory = Path.Combine(fixture.Root, "online-backups");
        fixture.CreateDatabase(source, 314);
        var options = new SqliteBackupOptions
        {
            SourcePath = source,
            BackupDirectory = backupDirectory,
            RetentionCount = 1,
            Interval = TimeSpan.FromHours(1),
        };
        using var backupService = new SqliteBackupService(options);
        SqliteBackupStatus backup = backupService.RunNow("db04-integration");

        SqliteRestoreService.Restore(backup.LastLocalPath, fixture.TargetPath);

        Assert.Equal(SqliteBackupState.Succeeded, backup.State);
        Assert.Equal(314, fixture.ReadValue(fixture.TargetPath));
    }

    [Fact]
    public void 强停遗留库和Sidecar被替换并保留为回滚工件()
    {
        using var fixture = new RestoreFixture();
        fixture.CreateDatabase(fixture.BackupPath, 99);
        fixture.CreateDatabase(fixture.TargetPath, 7);
        File.WriteAllText(fixture.TargetPath + "-wal", "forced-stop-wal");
        File.WriteAllText(fixture.TargetPath + "-shm", "forced-stop-shm");

        SqliteRestoreResult result = SqliteRestoreService.Restore(fixture.BackupPath, fixture.TargetPath);

        Assert.Equal(99, fixture.ReadValue(fixture.TargetPath));
        Assert.Equal(7, fixture.ReadValue(result.RollbackPath));
        Assert.Equal("forced-stop-wal", File.ReadAllText(result.RollbackWalPath));
        Assert.Equal("forced-stop-shm", File.ReadAllText(result.RollbackShmPath));
        Assert.False(File.Exists(fixture.TargetPath + "-wal"));
        Assert.False(File.Exists(fixture.TargetPath + "-shm"));
    }

    [Fact]
    public void 损坏副本在覆盖前拒绝且原库保持不变()
    {
        using var fixture = new RestoreFixture();
        fixture.CreateDatabase(fixture.TargetPath, 5);
        Directory.CreateDirectory(Path.GetDirectoryName(fixture.BackupPath)!);
        File.WriteAllText(fixture.BackupPath, "not-a-sqlite-database");

        Assert.ThrowsAny<Exception>(() => SqliteRestoreService.Restore(fixture.BackupPath, fixture.TargetPath));

        Assert.Equal(5, fixture.ReadValue(fixture.TargetPath));
        Assert.Empty(Directory.GetFiles(fixture.TargetDirectory, "*.pre-restore-*"));
        Assert.Empty(Directory.GetFiles(fixture.TargetDirectory, "*.partial"));
    }

    [Fact]
    public void 正在使用的目标库拒绝恢复且不移动强停Sidecar()
    {
        using var fixture = new RestoreFixture();
        fixture.CreateDatabase(fixture.BackupPath, 12);
        fixture.CreateDatabase(fixture.TargetPath, 6);
        File.WriteAllText(fixture.TargetPath + "-wal", "keep-wal");
        using var lockStream = new FileStream(fixture.TargetPath, FileMode.Open, FileAccess.Read, FileShare.Read);

        IOException error = Assert.Throws<IOException>(() =>
            SqliteRestoreService.Restore(fixture.BackupPath, fixture.TargetPath));

        Assert.Contains("服务器停止后执行", error.Message, StringComparison.Ordinal);
        Assert.Equal("keep-wal", File.ReadAllText(fixture.TargetPath + "-wal"));
    }

    private sealed class RestoreFixture : IDisposable
    {
        private readonly string _root;
        internal string Root => _root;
        internal string BackupPath { get; }
        internal string TargetDirectory { get; }
        internal string TargetPath { get; }

        internal RestoreFixture()
        {
            _root = Path.Combine(Path.GetTempPath(), "base05-db04-" + Guid.NewGuid().ToString("N"));
            BackupPath = Path.Combine(_root, "backup", "lyocrystal-sqlite-proof.db");
            TargetDirectory = Path.Combine(_root, "empty-server", "Data");
            TargetPath = Path.Combine(TargetDirectory, "server.db");
        }

        internal void CreateDatabase(string path, long value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE restore_proof(value INTEGER NOT NULL); INSERT INTO restore_proof(value) VALUES ($value);";
            command.Parameters.AddWithValue("$value", value);
            command.ExecuteNonQuery();
        }

        internal long ReadValue(string path)
        {
            using var connection = new SqliteConnection($"Data Source={path};Mode=ReadOnly;Pooling=False");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT value FROM restore_proof;";
            return (long)command.ExecuteScalar()!;
        }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(_root, recursive: true); } catch { }
        }
    }
}
