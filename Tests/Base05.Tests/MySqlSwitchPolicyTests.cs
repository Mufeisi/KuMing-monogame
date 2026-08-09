using Microsoft.Data.Sqlite;
using Server;
using Server.Persistence;
using Server.Persistence.Sql;
using Xunit;

namespace Base05.Tests;

[Collection("数据库Provider设置")]
public sealed class MySqlSwitchPolicyTests
{
    [Fact]
    public void 未达到持续窗口时继续使用SQLite()
    {
        MySqlSwitchAssessment result = MySqlSwitchPolicy.Assess(new MySqlSwitchMetrics
        {
            PeakConcurrentPlayers = 499,
            ConsecutiveOnlineDays = 30,
            DatabaseBytes = MySqlSwitchPolicy.DatabaseBytesThreshold,
            ConsecutiveDatabaseSizeDays = MySqlSwitchPolicy.RequiredDatabaseSizeDays - 1,
            SaveCommitP95Milliseconds = MySqlSwitchPolicy.SaveCommitP95MillisecondsThreshold,
            ConsecutiveSlowSaveDays = MySqlSwitchPolicy.RequiredSlowSaveDays - 1,
            SaveFailuresPerHour = MySqlSwitchPolicy.SaveFailuresPerHourThreshold,
            ConsecutiveFailureHours = MySqlSwitchPolicy.RequiredFailureHours - 1,
        });

        Assert.Equal(MySqlSwitchDecision.MaintainSqlite, result.Decision);
        Assert.Empty(result.TriggeredReasons);
    }

    [Theory]
    [InlineData("online")]
    [InlineData("size")]
    [InlineData("latency")]
    [InlineData("failure")]
    public void 任一持续门槛达到时进入迁移规划(string trigger)
    {
        var metrics = new MySqlSwitchMetrics
        {
            PeakConcurrentPlayers = trigger == "online" ? MySqlSwitchPolicy.PeakConcurrentPlayersThreshold : 0,
            ConsecutiveOnlineDays = trigger == "online" ? MySqlSwitchPolicy.RequiredOnlineDays : 0,
            DatabaseBytes = trigger == "size" ? MySqlSwitchPolicy.DatabaseBytesThreshold : 0,
            ConsecutiveDatabaseSizeDays = trigger == "size" ? MySqlSwitchPolicy.RequiredDatabaseSizeDays : 0,
            SaveCommitP95Milliseconds = trigger == "latency" ? MySqlSwitchPolicy.SaveCommitP95MillisecondsThreshold : 0,
            ConsecutiveSlowSaveDays = trigger == "latency" ? MySqlSwitchPolicy.RequiredSlowSaveDays : 0,
            SaveFailuresPerHour = trigger == "failure" ? MySqlSwitchPolicy.SaveFailuresPerHourThreshold : 0,
            ConsecutiveFailureHours = trigger == "failure" ? MySqlSwitchPolicy.RequiredFailureHours : 0,
        };

        MySqlSwitchAssessment result = MySqlSwitchPolicy.Assess(metrics);

        Assert.Equal(MySqlSwitchDecision.PlanMySqlMigration, result.Decision);
        Assert.Single(result.TriggeredReasons);
    }

    [Fact]
    public void 未触发门槛时拒绝创建迁移备份()
    {
        Assert.Throws<InvalidOperationException>(() =>
            MySqlSwitchPolicy.CreateRequiredPreMigrationBackup(
                new MySqlSwitchMetrics(),
                null!));
    }

    [Fact]
    public void 未授权时正式MySQL选择失败关闭()
    {
        string oldProvider = Settings.DatabaseProvider;
        string oldSqlitePath = Settings.SqlitePath;
        string root = Path.Combine(Path.GetTempPath(), "lyocrystal-db06-unauthorized-" + Guid.NewGuid().ToString("N"));
        try
        {
            Settings.DatabaseProvider = "MySql";
            Settings.SqlitePath = Path.Combine(root, "source.db");
            InvalidOperationException error = Assert.Throws<InvalidOperationException>(
                ServerPersistenceFactory.CreateFromSettings);
            Assert.Contains("必须继续使用 SQLite", error.Message);
        }
        finally
        {
            Settings.DatabaseProvider = oldProvider;
            Settings.SqlitePath = oldSqlitePath;
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public void 可用第二卷时生成授权并允许正式MySQL选择()
    {
        string secondRoot = FindDifferentVolumeRoot();
        if (string.IsNullOrEmpty(secondRoot)) return;
        using var fixture = BackupFixture.Create(secondRoot);
        var metrics = new MySqlSwitchMetrics
        {
            PeakConcurrentPlayers = MySqlSwitchPolicy.PeakConcurrentPlayersThreshold,
            ConsecutiveOnlineDays = MySqlSwitchPolicy.RequiredOnlineDays,
        };

        SqliteBackupStatus status = MySqlSwitchPolicy.CreateRequiredPreMigrationBackup(
            metrics,
            fixture.Service,
            fixture.AuthorizationPath,
            fixture.SourcePath);
        string oldProvider = Settings.DatabaseProvider;
        string oldSqlitePath = Settings.SqlitePath;
        try
        {
            Settings.DatabaseProvider = "MySql";
            Settings.SqlitePath = fixture.SourcePath;
            IServerPersistence persistence = ServerPersistenceFactory.CreateFromSettings();
            Assert.Equal(DatabaseProviderKind.MySql, persistence.Provider);
        }
        finally
        {
            Settings.DatabaseProvider = oldProvider;
            Settings.SqlitePath = oldSqlitePath;
        }

        Assert.Equal(SqliteBackupState.Succeeded, status.State);
        Assert.StartsWith(MySqlSwitchPolicy.MigrationBackupTriggerPrefix, status.Trigger);
        Assert.True(File.Exists(status.LastLocalPath));
        Assert.True(File.Exists(status.LastOffsitePath));
        Assert.NotEqual(Path.GetPathRoot(status.LastLocalPath), Path.GetPathRoot(status.LastOffsitePath));
        Assert.Equal(41L, ReadProof(status.LastLocalPath));
        Assert.Equal(41L, ReadProof(status.LastOffsitePath));
    }

    [Fact]
    public void 缺少异地副本时迁移门禁失败关闭()
    {
        using var fixture = BackupFixture.Create(null);
        var metrics = new MySqlSwitchMetrics
        {
            PeakConcurrentPlayers = MySqlSwitchPolicy.PeakConcurrentPlayersThreshold,
            ConsecutiveOnlineDays = MySqlSwitchPolicy.RequiredOnlineDays,
        };

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            MySqlSwitchPolicy.CreateRequiredPreMigrationBackup(
                metrics,
                fixture.Service,
                fixture.AuthorizationPath,
                fixture.SourcePath));

        Assert.Contains("异地备份副本不存在", error.Message);
    }

    [Fact]
    public void 同卷兄弟目录不能冒充异地副本()
    {
        using var fixture = BackupFixture.Create(Path.GetTempPath());
        var metrics = new MySqlSwitchMetrics
        {
            DatabaseBytes = MySqlSwitchPolicy.DatabaseBytesThreshold,
            ConsecutiveDatabaseSizeDays = MySqlSwitchPolicy.RequiredDatabaseSizeDays,
        };

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            MySqlSwitchPolicy.CreateRequiredPreMigrationBackup(
                metrics,
                fixture.Service,
                fixture.AuthorizationPath,
                fixture.SourcePath));

        Assert.Contains("不同的存储卷", error.Message);
        Assert.False(File.Exists(fixture.AuthorizationPath));
    }

    [Fact]
    public void 授权后的备份被篡改时正式选择失败关闭()
    {
        string secondRoot = FindDifferentVolumeRoot();
        if (string.IsNullOrEmpty(secondRoot)) return;
        using var fixture = BackupFixture.Create(secondRoot);
        var metrics = new MySqlSwitchMetrics
        {
            SaveFailuresPerHour = MySqlSwitchPolicy.SaveFailuresPerHourThreshold,
            ConsecutiveFailureHours = MySqlSwitchPolicy.RequiredFailureHours,
        };
        SqliteBackupStatus status = MySqlSwitchPolicy.CreateRequiredPreMigrationBackup(
            metrics,
            fixture.Service,
            fixture.AuthorizationPath,
            fixture.SourcePath);
        File.AppendAllText(status.LastLocalPath, "tampered");

        Assert.ThrowsAny<Exception>(() =>
            MySqlSwitchPolicy.EnsureProviderSelectionAuthorized(fixture.AuthorizationPath, fixture.SourcePath));
    }

    [Fact]
    public void 其他SQLite源库不能为当前主库生成授权()
    {
        using var fixture = BackupFixture.Create(null);
        var metrics = new MySqlSwitchMetrics
        {
            PeakConcurrentPlayers = MySqlSwitchPolicy.PeakConcurrentPlayersThreshold,
            ConsecutiveOnlineDays = MySqlSwitchPolicy.RequiredOnlineDays,
        };
        string otherSource = Path.Combine(fixture.Root, "other.db");

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            MySqlSwitchPolicy.CreateRequiredPreMigrationBackup(
                metrics,
                fixture.Service,
                fixture.AuthorizationPath,
                otherSource));

        Assert.Contains("源库", error.Message);
        Assert.False(File.Exists(fixture.AuthorizationPath));
    }

    [Fact]
    public void 普通JSON不能伪造DPAPI授权()
    {
        string root = Path.Combine(Path.GetTempPath(), "lyocrystal-db06-forged-" + Guid.NewGuid().ToString("N"));
        string source = Path.Combine(root, "server.db");
        string authorization = source + ".mysql-switch-authorization.dpapi";
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(authorization, "{\"FormatVersion\":1,\"Metrics\":{\"PeakConcurrentPlayers\":9999}}");

            Assert.ThrowsAny<Exception>(() =>
                MySqlSwitchPolicy.EnsureProviderSelectionAuthorized(authorization, source));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static long ReadProof(string path)
    {
        using var connection = new SqliteConnection($"Data Source={path};Mode=ReadOnly;Pooling=False");
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT Value FROM MigrationProof WHERE Id = 1;";
        return (long)command.ExecuteScalar()!;
    }

    private sealed class BackupFixture : IDisposable
    {
        private readonly string _root;
        private readonly string _offsiteDirectory;
        internal SqliteBackupService Service { get; }
        internal string Root => _root;
        internal string SourcePath { get; }
        internal string AuthorizationPath { get; }

        private BackupFixture(string root, string sourcePath, string offsiteDirectory, SqliteBackupService service)
        {
            _root = root;
            SourcePath = sourcePath;
            _offsiteDirectory = offsiteDirectory;
            Service = service;
            AuthorizationPath = sourcePath + ".mysql-switch-authorization.dpapi";
        }

        internal static BackupFixture Create(string offsiteRoot)
        {
            string root = Path.Combine(Path.GetTempPath(), "lyocrystal-db06-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string source = Path.Combine(root, "source.db");
            using (var connection = new SqliteConnection($"Data Source={source};Pooling=False"))
            {
                connection.Open();
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = "CREATE TABLE MigrationProof(Id INTEGER PRIMARY KEY, Value INTEGER NOT NULL); INSERT INTO MigrationProof VALUES(1, 41);";
                command.ExecuteNonQuery();
            }

            string offsiteDirectory = string.IsNullOrWhiteSpace(offsiteRoot)
                ? string.Empty
                : Path.Combine(offsiteRoot, "LyoCrystalDb06Tests", Guid.NewGuid().ToString("N"));
            var options = new SqliteBackupOptions
            {
                SourcePath = source,
                BackupDirectory = Path.Combine(root, "local"),
                OffsiteDirectory = offsiteDirectory,
            };
            return new BackupFixture(root, source, offsiteDirectory, new SqliteBackupService(options));
        }

        public void Dispose()
        {
            Service.Dispose();
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(_root, recursive: true); } catch { }
            try { if (Directory.Exists(_offsiteDirectory)) Directory.Delete(_offsiteDirectory, recursive: true); } catch { }
        }
    }

    private static string FindDifferentVolumeRoot()
    {
        string primaryRoot = Path.GetPathRoot(Path.GetTempPath()) ?? string.Empty;
        return DriveInfo.GetDrives()
            .Where(drive => drive.IsReady && drive.DriveType != DriveType.CDRom)
            .Select(drive => drive.RootDirectory.FullName)
            .FirstOrDefault(root => !string.Equals(root, primaryRoot, StringComparison.OrdinalIgnoreCase))
            ?? string.Empty;
    }
}

[CollectionDefinition("数据库Provider设置", DisableParallelization = true)]
public sealed class DatabaseProviderSettingsCollection
{
}
