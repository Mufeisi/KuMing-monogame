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
    public void 真实WAL强停副本先收敛为单文件回滚库再原子恢复()
    {
        using var fixture = new RestoreFixture();
        fixture.CreateDatabase(fixture.BackupPath, 99);
        fixture.CreateForcedStopCopy(fixture.TargetPath, initialValue: 7, committedWalValue: 8);
        Assert.True(File.Exists(fixture.TargetPath + "-wal"));
        Assert.True(File.Exists(fixture.TargetPath + "-shm"));

        SqliteRestoreResult result = SqliteRestoreService.Restore(fixture.BackupPath, fixture.TargetPath);

        Assert.Equal(99, fixture.ReadValue(fixture.TargetPath));
        Assert.Equal(8, fixture.ReadValue(result.RollbackPath));
        Assert.False(File.Exists(fixture.TargetPath + "-wal"));
        Assert.False(File.Exists(fixture.TargetPath + "-shm"));
        Assert.Equal(string.Empty, result.RollbackWalPath);
        Assert.Equal(string.Empty, result.RollbackShmPath);
    }

    [Fact]
    public void 原子发布前中断时旧库已独立且提交WAL数据不丢失()
    {
        using var fixture = new RestoreFixture();
        fixture.CreateDatabase(fixture.BackupPath, 99);
        fixture.CreateForcedStopCopy(fixture.TargetPath, initialValue: 7, committedWalValue: 8);
        var operations = new SqliteRestoreOperations
        {
            BeforePublish = () => throw new IOException("模拟原子发布前进程中断点"),
        };

        Assert.Throws<IOException>(() =>
            SqliteRestoreService.Restore(fixture.BackupPath, fixture.TargetPath, operations));

        Assert.Equal(8, fixture.ReadValue(fixture.TargetPath));
        Assert.False(File.Exists(fixture.TargetPath + "-wal"));
        Assert.False(File.Exists(fixture.TargetPath + "-shm"));
        Assert.Single(Directory.GetFiles(fixture.TargetDirectory, "*.pre-restore-*"));
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
    public void 携带Sidecar的来源副本在复制前拒绝()
    {
        using var fixture = new RestoreFixture();
        fixture.CreateDatabase(fixture.BackupPath, 12);
        File.WriteAllText(fixture.BackupPath + "-wal", "unexpected");

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            SqliteRestoreService.Restore(fixture.BackupPath, fixture.TargetPath));

        Assert.Contains("独立主库文件", error.Message);
        Assert.False(File.Exists(fixture.TargetPath));
    }

    [Fact]
    public void 正在使用的目标库拒绝恢复且不处理Sidecar()
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

    [Fact]
    public void 发布后最终校验失败会从独立回滚库恢复旧值()
    {
        using var fixture = new RestoreFixture();
        fixture.CreateDatabase(fixture.BackupPath, 99);
        fixture.CreateDatabase(fixture.TargetPath, 7);
        int targetValidations = 0;
        var operations = new SqliteRestoreOperations
        {
            ValidateIntegrity = path =>
            {
                if (string.Equals(Path.GetFullPath(path), Path.GetFullPath(fixture.TargetPath), StringComparison.OrdinalIgnoreCase) &&
                    ++targetValidations == 2)
                    throw new InvalidDataException("模拟发布后最终校验失败");
                SqliteBackupService.ValidateIntegrity(path);
            },
        };

        Assert.Throws<InvalidDataException>(() =>
            SqliteRestoreService.Restore(fixture.BackupPath, fixture.TargetPath, operations));

        Assert.Equal(7, fixture.ReadValue(fixture.TargetPath));
        Assert.Single(Directory.GetFiles(fixture.TargetDirectory, "*.pre-restore-*"));
    }

    [Fact]
    public void 发布失败且回滚失败时明确报告不完整()
    {
        using var fixture = new RestoreFixture();
        fixture.CreateDatabase(fixture.BackupPath, 99);
        fixture.CreateDatabase(fixture.TargetPath, 7);
        int targetValidations = 0;
        var operations = new SqliteRestoreOperations
        {
            ValidateIntegrity = path =>
            {
                if (string.Equals(Path.GetFullPath(path), Path.GetFullPath(fixture.TargetPath), StringComparison.OrdinalIgnoreCase) &&
                    ++targetValidations == 2)
                    throw new InvalidDataException("模拟发布后最终校验失败");
                SqliteBackupService.ValidateIntegrity(path);
            },
            Replace = (source, destination, backup) =>
            {
                if (source.Contains(".pre-restore-", StringComparison.Ordinal))
                    throw new IOException("模拟主库回滚失败");
                File.Replace(source, destination, backup, ignoreMetadataErrors: true);
            },
        };

        AggregateException error = Assert.Throws<AggregateException>(() =>
            SqliteRestoreService.Restore(fixture.BackupPath, fixture.TargetPath, operations));

        Assert.Contains("回滚不完整", error.Message, StringComparison.Ordinal);
        Assert.Equal(2, error.InnerExceptions.Count);
        Assert.Equal(99, fixture.ReadValue(fixture.TargetPath));
    }

    [Fact]
    public void 空环境发布后失败且目标删除失败时明确报告回滚不完整()
    {
        using var fixture = new RestoreFixture();
        fixture.CreateDatabase(fixture.BackupPath, 21);
        var operations = new SqliteRestoreOperations
        {
            ValidateIntegrity = path =>
            {
                if (string.Equals(Path.GetFullPath(path), Path.GetFullPath(fixture.TargetPath), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("模拟发布后最终校验失败");
                SqliteBackupService.ValidateIntegrity(path);
            },
            Delete = _ => throw new UnauthorizedAccessException("模拟目标删除失败"),
        };

        AggregateException error = Assert.Throws<AggregateException>(() =>
            SqliteRestoreService.Restore(fixture.BackupPath, fixture.TargetPath, operations));

        Assert.Contains("回滚不完整", error.Message, StringComparison.Ordinal);
        Assert.True(File.Exists(fixture.TargetPath));
        Assert.Contains(error.InnerExceptions, ex => ex.Message.Contains("空环境目标库回滚失败", StringComparison.Ordinal));
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

        internal void CreateForcedStopCopy(string targetPath, long initialValue, long committedWalValue)
        {
            string livePath = Path.Combine(_root, "forced-live", "server.db");
            Directory.CreateDirectory(Path.GetDirectoryName(livePath)!);
            using (var connection = new SqliteConnection($"Data Source={livePath};Pooling=False"))
            {
                connection.Open();
                using (SqliteCommand setup = connection.CreateCommand())
                {
                    setup.CommandText = "PRAGMA journal_mode=WAL; PRAGMA wal_autocheckpoint=0; CREATE TABLE restore_proof(value INTEGER NOT NULL); INSERT INTO restore_proof VALUES($value);";
                    setup.Parameters.AddWithValue("$value", initialValue);
                    setup.ExecuteNonQuery();
                }
                using (SqliteCommand checkpoint = connection.CreateCommand())
                {
                    checkpoint.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                    checkpoint.ExecuteNonQuery();
                }
                using (SqliteCommand update = connection.CreateCommand())
                {
                    update.CommandText = "UPDATE restore_proof SET value=$value;";
                    update.Parameters.AddWithValue("$value", committedWalValue);
                    update.ExecuteNonQuery();
                }
                Assert.True(File.Exists(livePath + "-wal"));
                Assert.True(new FileInfo(livePath + "-wal").Length > 0);
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                File.Copy(livePath, targetPath);
                File.Copy(livePath + "-wal", targetPath + "-wal");
                File.Copy(livePath + "-shm", targetPath + "-shm");
            }
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
