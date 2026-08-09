using Microsoft.Data.Sqlite;
using Server.MirDatabase;
using Server.MirEnvir;
using Server.Persistence;
using Server.Persistence.Sql;
using Shared.Diagnostics;
using Xunit;

namespace Base05.Tests;

[Collection("PerformanceMetrics")]
public sealed class Db02SaveGenerationTests
{
    [Fact]
    public void 账户快照交接后内存变化不影响后台提交且成功代次单调前进()
    {
        string databasePath = Path.Combine(Path.GetTempPath(), $"base05-db02-generation-{Guid.NewGuid():N}.db");
        var options = new SqlDatabaseOptions { SqlitePath = databasePath };
        var persistence = new SqlServerPersistence(DatabaseProviderKind.Sqlite, options);
        var source = new Envir();
        var account = new AccountInfo
        {
            Index = 501,
            AccountID = "db02-account",
            UserName = "DB02",
            Gold = 100,
        };
        source.AccountList.Add(account);
        int captureThreadId = Environment.CurrentManagedThreadId;

        try
        {
            PerformanceMetrics.Configure(enabled: true, scenario: "db02-save-generation");

            persistence.SaveAccounts(source);
            long firstGeneration = persistence.LastIssuedSaveGeneration;
            account.Gold = 999;
            ((IPendingSaveCoordinator)persistence).DrainPendingSaves();

            Assert.True(firstGeneration > 0);
            Assert.Equal(firstGeneration, persistence.GetLastCommittedGeneration(SqlSaveDomain.Accounts));
            Assert.Equal(captureThreadId, persistence.LastSnapshotCaptureThreadId);
            Assert.NotEqual(captureThreadId, persistence.SqliteWriterThreadId);
            Assert.Equal(100u, LoadSingleAccount(persistence).Gold);

            persistence.SaveAccounts(source);
            long secondGeneration = persistence.LastIssuedSaveGeneration;
            ((IPendingSaveCoordinator)persistence).DrainPendingSaves();

            Assert.True(secondGeneration > firstGeneration);
            Assert.Equal(secondGeneration, persistence.GetLastCommittedGeneration(SqlSaveDomain.Accounts));
            Assert.Equal(999u, LoadSingleAccount(persistence).Gold);

            var metrics = PerformanceMetrics.CreateSnapshot().Metrics;
            Assert.Equal(2, metrics.Single(item => item.Name == nameof(PerformanceMetricKind.SaveSnapshotCapture)).Samples);
            Assert.Equal(2, metrics.Single(item => item.Name == nameof(PerformanceMetricKind.SaveTransactionCommit)).Samples);
        }
        finally
        {
            PerformanceMetrics.Configure(enabled: false);
            DeleteSqliteFiles(databasePath);
        }
    }

    private static AccountInfo LoadSingleAccount(SqlServerPersistence persistence)
    {
        var restored = new Envir();
        persistence.LoadAccounts(restored);
        return Assert.Single(restored.AccountList);
    }

    private static void DeleteSqliteFiles(string databasePath)
    {
        SqliteConnection.ClearAllPools();
        foreach (string suffix in new[] { string.Empty, "-wal", "-shm" })
        {
            string path = databasePath + suffix;
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
