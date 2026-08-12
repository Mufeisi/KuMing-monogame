using Microsoft.Data.Sqlite;
using Server.Persistence.Sql;
using Xunit;

namespace Base05.Tests;

public sealed class SqliteBackupServiceTests
{
    [Fact]
    public void 在线备份只包含已提交状态并生成通过完整性检查的本地与异地副本()
    {
        using var fixture = new BackupFixture(retentionCount: 3, withOffsite: true);
        fixture.InitializeSource();

        using var writer = fixture.OpenSource();
        using var transaction = writer.BeginTransaction();
        using (var insert = writer.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO values_table(value) VALUES (2);";
            insert.ExecuteNonQuery();
        }

        SqliteBackupStatus status = fixture.Service.RunNow("manual-test");

        Assert.Equal(SqliteBackupState.Succeeded, status.State);
        Assert.Equal("ok", status.IntegrityResult);
        Assert.True(File.Exists(status.LastLocalPath));
        Assert.True(File.Exists(status.LastOffsitePath));
        Assert.Equal(new long[] { 1 }, ReadValues(status.LastLocalPath));
        Assert.Equal(new long[] { 1 }, ReadValues(status.LastOffsitePath));
        SqliteBackupService.ValidateIntegrity(status.LastLocalPath);
        SqliteBackupService.ValidateIntegrity(status.LastOffsitePath);

        transaction.Commit();
        Assert.Equal(new long[] { 1, 2 }, ReadValues(fixture.SourcePath));
    }

    [Fact]
    public void 自动保留仅删除受管备份且本地与异地各保留最新数量()
    {
        using var fixture = new BackupFixture(retentionCount: 2, withOffsite: true);
        fixture.InitializeSource();
        string unrelatedLocal = Path.Combine(fixture.LocalDirectory, "operator-note.db");
        string unrelatedOffsite = Path.Combine(fixture.OffsiteDirectory, "operator-note.db");
        File.WriteAllText(unrelatedLocal, "保留");
        File.WriteAllText(unrelatedOffsite, "保留");

        fixture.Service.RunNow("retention-1");
        fixture.Service.RunNow("retention-2");
        fixture.Service.RunNow("retention-3");

        Assert.Equal(2, Directory.GetFiles(fixture.LocalDirectory, "lyocrystal-sqlite-*.db").Length);
        Assert.Equal(2, Directory.GetFiles(fixture.OffsiteDirectory, "lyocrystal-sqlite-*.db").Length);
        Assert.True(File.Exists(unrelatedLocal));
        Assert.True(File.Exists(unrelatedOffsite));
    }

    [Fact]
    public void 损坏副本被完整性检查拒绝且失败状态跨服务实例保留()
    {
        using var fixture = new BackupFixture(retentionCount: 2, withOffsite: false);
        Directory.CreateDirectory(fixture.LocalDirectory);
        string corrupted = Path.Combine(fixture.LocalDirectory, "corrupted.db");
        File.WriteAllBytes(corrupted, new byte[] { 1, 2, 3, 4, 5 });
        Assert.ThrowsAny<Exception>(() => SqliteBackupService.ValidateIntegrity(corrupted));

        SqliteBackupStatus failed = fixture.Service.RunNow("missing-source");
        Assert.Equal(SqliteBackupState.Failed, failed.State);
        Assert.Contains("FileNotFoundException", failed.LastError, StringComparison.Ordinal);

        using var reloaded = new SqliteBackupService(fixture.Options);
        SqliteBackupStatus persisted = reloaded.GetStatus();
        Assert.Equal(SqliteBackupState.Failed, persisted.State);
        Assert.Equal(failed.LastAttemptUtc, persisted.LastAttemptUtc);
        Assert.Equal("missing-source", persisted.Trigger);
    }

    [Fact]
    public void 状态文件中的未完成备份在重启后标记为失败()
    {
        using var fixture = new BackupFixture(retentionCount: 2, withOffsite: false);
        fixture.Service.Dispose();
        Directory.CreateDirectory(fixture.LocalDirectory);
        string statusPath = Path.Combine(fixture.LocalDirectory, "backup-status.json");
        File.WriteAllText(statusPath, "{\"State\":\"Running\",\"Trigger\":\"automatic\",\"LastAttemptUtc\":\"2026-08-10T00:00:00Z\"}");

        using var reloaded = new SqliteBackupService(fixture.Options);
        SqliteBackupStatus status = reloaded.GetStatus();

        Assert.Equal(SqliteBackupState.Failed, status.State);
        Assert.Contains("Interrupted", status.LastError, StringComparison.Ordinal);
        Assert.Contains("\"State\":\"Failed\"", File.ReadAllText(statusPath), StringComparison.Ordinal);
    }

    [Fact]
    public void 备份目录拒绝根目录及本地异地嵌套配置()
    {
        string root = Path.Combine(Path.GetTempPath(), "base05-db03-options-" + Guid.NewGuid().ToString("N"));
        string source = Path.Combine(root, "source.db");
        try
        {
            var rootDirectory = new SqliteBackupOptions
            {
                SourcePath = source,
                BackupDirectory = Path.GetPathRoot(root)!,
                RetentionCount = 1,
                Interval = TimeSpan.FromHours(1),
            };
            Assert.Throws<InvalidOperationException>(() => rootDirectory.Validate(requireOffsite: false));

            var nested = new SqliteBackupOptions
            {
                SourcePath = source,
                BackupDirectory = Path.Combine(root, "backup"),
                OffsiteDirectory = Path.Combine(root, "backup", "offsite"),
                RetentionCount = 1,
                Interval = TimeSpan.FromHours(1),
            };
            Assert.Throws<InvalidOperationException>(() => nested.Validate(requireOffsite: true));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public void 自动服务启动后立即生成首份备份()
    {
        using var fixture = new BackupFixture(retentionCount: 2, withOffsite: true);
        fixture.InitializeSource();

        fixture.Service.StartAutomatic();
        Assert.True(fixture.Service.WaitForIdle(TimeSpan.FromSeconds(10)));
        SqliteBackupStatus status = fixture.Service.GetStatus();

        Assert.Equal(SqliteBackupState.Succeeded, status.State);
        Assert.Equal("automatic", status.Trigger);
        Assert.True(File.Exists(status.LastLocalPath));
        Assert.True(File.Exists(status.LastOffsitePath));
    }

    [Fact]
    public void 异地复制失败时保留已验证本地副本并持久化失败状态()
    {
        using var fixture = new BackupFixture(retentionCount: 2, withOffsite: true);
        fixture.InitializeSource();
        Directory.Delete(fixture.OffsiteDirectory);
        File.WriteAllText(fixture.OffsiteDirectory, "阻止创建目录");

        SqliteBackupStatus status = fixture.Service.RunNow("offsite-failure");

        Assert.Equal(SqliteBackupState.Failed, status.State);
        Assert.True(File.Exists(status.LastLocalPath));
        Assert.Equal(string.Empty, status.LastOffsitePath);
        SqliteBackupService.ValidateIntegrity(status.LastLocalPath);

        File.Delete(fixture.OffsiteDirectory);
        Directory.CreateDirectory(fixture.OffsiteDirectory);
        using var reloaded = new SqliteBackupService(fixture.Options);
        SqliteBackupStatus persisted = reloaded.GetStatus();
        Assert.Equal(SqliteBackupState.Failed, persisted.State);
        Assert.Equal(status.LastLocalPath, persisted.LastLocalPath);
    }

    [Fact]
    public void 保留清理失败时状态仍指向本次已验证副本()
    {
        using var fixture = new BackupFixture(retentionCount: 1, withOffsite: false);
        fixture.InitializeSource();
        SqliteBackupStatus first = fixture.Service.RunNow("retention-base");

        using var failing = new SqliteBackupService(
            fixture.Options,
            _ => throw new IOException("模拟保留清理失败"));
        SqliteBackupStatus failed = failing.RunNow("retention-failure");

        Assert.Equal(SqliteBackupState.Failed, failed.State);
        Assert.NotEqual(first.LastLocalPath, failed.LastLocalPath);
        Assert.True(File.Exists(failed.LastLocalPath));
        SqliteBackupService.ValidateIntegrity(failed.LastLocalPath);
    }

    [Fact]
    public void 损坏状态文件恢复为失败且目录文件冲突阻止服务构造()
    {
        using var fixture = new BackupFixture(retentionCount: 2, withOffsite: false);
        fixture.Service.Dispose();
        string statusPath = Path.Combine(fixture.LocalDirectory, "backup-status.json");
        File.WriteAllText(statusPath, "{not-json");

        using (var reloaded = new SqliteBackupService(fixture.Options))
        {
            SqliteBackupStatus status = reloaded.GetStatus();
            Assert.Equal(SqliteBackupState.Failed, status.State);
            Assert.Contains("StatusCorrupted", status.LastError, StringComparison.Ordinal);
        }

        string fileInsteadOfDirectory = Path.Combine(Path.GetDirectoryName(fixture.LocalDirectory)!, "not-a-directory");
        File.WriteAllText(fileInsteadOfDirectory, "x");
        var invalid = new SqliteBackupOptions
        {
            SourcePath = fixture.SourcePath,
            BackupDirectory = fileInsteadOfDirectory,
            RetentionCount = 1,
            Interval = TimeSpan.FromHours(1),
        };
        Assert.Throws<InvalidOperationException>(() => new SqliteBackupService(invalid));
    }

    [Fact]
    public void 探针无法删除时服务构造失败关闭且不遗留探针()
    {
        string root = Path.Combine(Path.GetTempPath(), "base05-db03-probe-delete-" + Guid.NewGuid().ToString("N"));
        string local = Path.Combine(root, "local");
        var options = new SqliteBackupOptions
        {
            SourcePath = Path.Combine(root, "source.db"),
            BackupDirectory = local,
            RetentionCount = 1,
            Interval = TimeSpan.FromHours(1),
        };

        try
        {
            IOException error = Assert.Throws<IOException>(() => new SqliteBackupService(
                options,
                deleteProbe: _ => throw new UnauthorizedAccessException("模拟目录禁止删除")));
            Assert.Contains("不可写或不可删除", error.Message, StringComparison.Ordinal);
            Assert.Empty(Directory.GetFiles(local, ".lyocrystal-backup-write-probe-*"));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public void 正式异地门禁拒绝同卷兄弟目录并在可用第二卷执行真实复制()
    {
        string primaryRoot = Path.GetPathRoot(Path.GetTempPath()) ?? string.Empty;
        string secondRoot = DriveInfo.GetDrives()
            .Where(drive => drive.IsReady && drive.DriveType != DriveType.CDRom)
            .Select(drive => drive.RootDirectory.FullName)
            .FirstOrDefault(root => !string.Equals(root, primaryRoot, StringComparison.OrdinalIgnoreCase));
        string root = Path.Combine(Path.GetTempPath(), "base05-db03-offsite-policy-" + Guid.NewGuid().ToString("N"));
        string source = Path.Combine(root, "source.db");
        string local = Path.Combine(root, "local");
        string sameVolume = Path.Combine(root, "offsite");
        var sameVolumeOptions = new SqliteBackupOptions
        {
            SourcePath = source,
            BackupDirectory = local,
            OffsiteDirectory = sameVolume,
            RetentionCount = 1,
            Interval = TimeSpan.FromHours(1),
        };
        Assert.Throws<InvalidOperationException>(() => sameVolumeOptions.Validate(requireOffsite: true));

        if (string.IsNullOrEmpty(secondRoot))
        {
            var unc = new SqliteBackupOptions
            {
                SourcePath = source,
                BackupDirectory = local,
                OffsiteDirectory = @"\\backup-server\LyoCrystaltests\SQLite",
                RetentionCount = 1,
                Interval = TimeSpan.FromHours(1),
            };
            unc.Validate(requireOffsite: true);
            return;
        }

        string offsite = Path.Combine(secondRoot, "LyoCrystalDb03Tests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            using (var connection = new SqliteConnection($"Data Source={source};Pooling=False"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "CREATE TABLE proof(value INTEGER NOT NULL); INSERT INTO proof(value) VALUES (3);";
                command.ExecuteNonQuery();
            }
            var options = new SqliteBackupOptions
            {
                SourcePath = source,
                BackupDirectory = local,
                OffsiteDirectory = offsite,
                RetentionCount = 1,
                Interval = TimeSpan.FromHours(1),
            };
            options.Validate(requireOffsite: true);
            using var service = new SqliteBackupService(options);
            SqliteBackupStatus status = service.RunNow("different-volume-proof");
            Assert.Equal(SqliteBackupState.Succeeded, status.State);
            Assert.NotEqual(Path.GetPathRoot(status.LastLocalPath), Path.GetPathRoot(status.LastOffsitePath));
            SqliteBackupService.ValidateIntegrity(status.LastOffsitePath);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(root, recursive: true); } catch { }
            try { Directory.Delete(offsite, recursive: true); } catch { }
        }
    }

    private static long[] ReadValues(string path)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        };
        using var connection = new SqliteConnection(builder.ConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM values_table ORDER BY value;";
        using var reader = command.ExecuteReader();
        var values = new List<long>();
        while (reader.Read()) values.Add(reader.GetInt64(0));
        return values.ToArray();
    }

    private sealed class BackupFixture : IDisposable
    {
        private readonly string _root;
        internal string SourcePath { get; }
        internal string LocalDirectory { get; }
        internal string OffsiteDirectory { get; }
        internal SqliteBackupOptions Options { get; }
        internal SqliteBackupService Service { get; }

        internal BackupFixture(int retentionCount, bool withOffsite)
        {
            _root = Path.Combine(Path.GetTempPath(), "base05-db03-" + Guid.NewGuid().ToString("N"));
            SourcePath = Path.Combine(_root, "source", "server.db");
            LocalDirectory = Path.Combine(_root, "local");
            OffsiteDirectory = Path.Combine(_root, "offsite");
            Directory.CreateDirectory(Path.GetDirectoryName(SourcePath)!);
            Directory.CreateDirectory(LocalDirectory);
            if (withOffsite) Directory.CreateDirectory(OffsiteDirectory);
            Options = new SqliteBackupOptions
            {
                SourcePath = SourcePath,
                BackupDirectory = LocalDirectory,
                OffsiteDirectory = withOffsite ? OffsiteDirectory : string.Empty,
                RetentionCount = retentionCount,
                Interval = TimeSpan.FromHours(1),
            };
            Service = new SqliteBackupService(Options);
        }

        internal void InitializeSource()
        {
            using var connection = OpenSource();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA journal_mode=WAL; CREATE TABLE values_table(value INTEGER NOT NULL); INSERT INTO values_table(value) VALUES (1);";
            command.ExecuteNonQuery();
        }

        internal SqliteConnection OpenSource()
        {
            var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = SourcePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false,
            }.ConnectionString);
            connection.Open();
            return connection;
        }

        public void Dispose()
        {
            Service.Dispose();
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(_root, recursive: true); } catch { }
        }
    }
}
