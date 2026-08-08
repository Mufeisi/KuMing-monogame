using Server;
using Server.MirDatabase;
using Server.MirEnvir;
using Server.Persistence;
using Server.Persistence.Sql;
using Server.Utils;
using Shared.Diagnostics;
using Xunit;

namespace Base05.Tests;

[Collection("PerformanceMetrics")]
public sealed class SqlPersistenceRoundTripTests
{
    [Fact]
    public void Sqlite_round_trips_account_character_inventory_storage_and_mail()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"base05-{Guid.NewGuid():N}.db");
        var options = new SqlDatabaseOptions { SqlitePath = databasePath };
        var persistence = new SqlServerPersistence(DatabaseProviderKind.Sqlite, options);

        try
        {
            PerformanceMetrics.Configure(enabled: true, scenario: "sql-roundtrip");
            var source = new Envir();
            var account = new AccountInfo
            {
                Index = 101,
                AccountID = "base05-account",
                UserName = "Base05",
                Gold = 1234,
            };
            var legacySalt = Crypto.GenerateSalt();
            var legacyHash = Crypto.HashPassword("roundtrip-secret", legacySalt);
            account.SetPasswordHashAndSalt(legacyHash, legacySalt);
            Assert.False(source.HasPendingAutoSave);
            Assert.Equal(PasswordVerificationResult.Invalid,
                source.VerifyAccountPassword(account, "wrong-roundtrip-secret"));
            Assert.False(source.HasPendingAutoSave);
            Assert.Equal(legacyHash, account.Password);
            Assert.Equal(PasswordVerificationResult.ValidNeedsUpgrade,
                source.VerifyAccountPassword(account, "roundtrip-secret"));
            Assert.True(source.HasPendingAutoSave);
            var character = new CharacterInfo
            {
                Index = 202,
                Name = "base05-character",
                Level = 12,
                AccountInfo = account,
            };
            account.Characters.Add(character);
            source.AccountList.Add(account);
            source.CharacterList.Add(character);

            var itemInfo = new ItemInfo { Index = 303, Name = "base05-item", StackSize = 99 };
            source.ItemInfoList.Add(itemInfo);
            var inventoryItem = new UserItem(itemInfo) { UniqueID = 404, Count = 3, CurrentDura = 7, MaxDura = 9 };
            var storageItem = new UserItem(itemInfo) { UniqueID = 405, Count = 2, CurrentDura = 5, MaxDura = 9 };
            character.Inventory[0] = inventoryItem;
            account.Storage[0] = storageItem;

            var mailItem = new UserItem(itemInfo) { UniqueID = 406, Count = 1, CurrentDura = 4, MaxDura = 9 };
            var mail = new MailInfo
            {
                MailID = 407,
                Sender = "base05-sender",
                RecipientIndex = character.Index,
                Message = "base05-mail",
                Gold = 88,
                DateSent = new DateTime(2026, 8, 5, 12, 0, 0, DateTimeKind.Utc),
                CanReply = true,
            };
            mail.Items.Add(mailItem);
            character.Mail.Add(mail);

            persistence.SaveAccounts(source);

            var saveMetric = PerformanceMetrics.CreateSnapshot().Metrics
                .Single(item => item.Name == nameof(PerformanceMetricKind.Save));
            Assert.True(saveMetric.Samples > 0);
            Assert.True(saveMetric.P95Milliseconds.HasValue);
            var snapshotMetric = PerformanceMetrics.CreateSnapshot().Metrics
                .Single(item => item.Name == nameof(PerformanceMetricKind.SaveSnapshotCapture));
            Assert.True(snapshotMetric.Samples > 0);
            Assert.True(snapshotMetric.P95Milliseconds.HasValue);
            var transactionMetric = PerformanceMetrics.CreateSnapshot().Metrics
                .Single(item => item.Name == nameof(PerformanceMetricKind.SaveTransactionCommit));
            Assert.True(transactionMetric.Samples > 0);
            Assert.True(transactionMetric.P95Milliseconds.HasValue);

            var restored = new Envir();
            restored.ItemInfoList.Add(new ItemInfo { Index = 303, Name = "base05-item", StackSize = 99 });
            persistence.LoadAccounts(restored);

            var restoredAccount = Assert.Single(restored.AccountList);
            var restoredCharacter = Assert.Single(restored.CharacterList);
            Assert.Equal("base05-account", restoredAccount.AccountID);
            Assert.StartsWith("$argon2id$v=19$", restoredAccount.Password, StringComparison.Ordinal);
            Assert.Empty(restoredAccount.Salt);
            Assert.Equal(PasswordVerificationResult.Valid, restoredAccount.VerifyPassword("roundtrip-secret"));
            Assert.Equal(1234u, restoredAccount.Gold);
            Assert.Equal(101, restoredAccount.Index);
            Assert.Equal("base05-character", restoredCharacter.Name);
            Assert.Equal(202, restoredCharacter.Index);
            Assert.Same(restoredAccount, restoredCharacter.AccountInfo);
            Assert.Same(restoredCharacter, Assert.Single(restoredAccount.Characters));

            var restoredInventoryItem = Assert.IsType<UserItem>(restoredCharacter.Inventory[0]);
            Assert.Equal(404ul, restoredInventoryItem.UniqueID);
            Assert.Equal(303, restoredInventoryItem.ItemIndex);
            Assert.Equal(303, restoredInventoryItem.Info.Index);
            Assert.Equal(3, restoredInventoryItem.Count);
            Assert.Null(restoredCharacter.Inventory[1]);

            var restoredStorageItem = Assert.IsType<UserItem>(restoredAccount.Storage[0]);
            Assert.Equal(405ul, restoredStorageItem.UniqueID);
            Assert.Equal(303, restoredStorageItem.ItemIndex);
            Assert.Equal(303, restoredStorageItem.Info.Index);
            Assert.Equal(2, restoredStorageItem.Count);
            Assert.Null(restoredAccount.Storage[1]);

            var restoredMail = Assert.Single(restoredCharacter.Mail);
            Assert.Equal(407ul, restoredMail.MailID);
            Assert.Equal(202, restoredMail.RecipientIndex);
            Assert.Equal("base05-mail", restoredMail.Message);
            Assert.Equal(88u, restoredMail.Gold);
            var restoredMailItem = Assert.Single(restoredMail.Items);
            Assert.Equal(406ul, restoredMailItem.UniqueID);
            Assert.Equal(303, restoredMailItem.ItemIndex);
            Assert.Equal(303, restoredMailItem.Info.Index);
        }
        finally
        {
            PerformanceMetrics.Configure(enabled: false);
            TryDelete(databasePath);
            TryDelete(databasePath + "-wal");
            TryDelete(databasePath + "-shm");
        }
    }

    [Fact]
    public void Sql_save_failure_records_full_call_and_failure_segment()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"base05-save-failure-{Guid.NewGuid():N}.db");
        try
        {
            PerformanceMetrics.Configure(enabled: true, scenario: "sql-save-failure");
            var runner = new SqlDomainTransactionRunner(
                DatabaseProviderKind.Sqlite,
                new SqlDatabaseOptions { SqlitePath = databasePath },
                maxAttempts: 2,
                continueOnError: true);

            var result = runner.RunWithSnapshot<int>(
                SqlSaveDomain.Accounts,
                () => throw new InvalidOperationException("测试快照失败"),
                (session, snapshot) => { });

            Assert.False(result.Success);
            var metrics = PerformanceMetrics.CreateSnapshot().Metrics;
            var save = metrics.Single(item => item.Name == nameof(PerformanceMetricKind.Save));
            var snapshot = metrics.Single(item => item.Name == nameof(PerformanceMetricKind.SaveSnapshotCapture));
            var failure = metrics.Single(item => item.Name == nameof(PerformanceMetricKind.SaveFailure));
            Assert.Equal(1, save.Samples);
            Assert.Equal(1, snapshot.Samples);
            Assert.True(failure.TotalValue >= 1);
            Assert.True(save.P95Milliseconds.HasValue);
        }
        finally
        {
            PerformanceMetrics.Configure(enabled: false);
            TryDelete(databasePath);
        }
    }

    [Fact]
    public void Sql_retry_success_counts_attempt_failure_without_final_failure()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"base05-save-retry-success-{Guid.NewGuid():N}.db");
        try
        {
            PerformanceMetrics.Configure(enabled: true, scenario: "sql-retry-success");
            var runner = new SqlDomainTransactionRunner(
                DatabaseProviderKind.Sqlite,
                new SqlDatabaseOptions { SqlitePath = databasePath },
                new SqlSessionOptions { BaseRetryDelayMs = 1 },
                maxAttempts: 2,
                continueOnError: true);
            var invocation = 0;

            var result = runner.Run(SqlSaveDomain.Accounts, _ =>
            {
                if (Interlocked.Increment(ref invocation) == 1)
                    throw new IOException("测试瞬时失败");
            });

            Assert.True(result.Success);
            var metrics = PerformanceMetrics.CreateSnapshot().Metrics;
            Assert.Equal(1L, metrics.Single(item => item.Name == nameof(PerformanceMetricKind.SaveAttemptFailure)).TotalValue ?? 0L);
            Assert.Equal(0L, metrics.Single(item => item.Name == nameof(PerformanceMetricKind.SaveFailure)).TotalValue ?? 0L);
        }
        finally
        {
            PerformanceMetrics.Configure(enabled: false);
            TryDelete(databasePath);
        }
    }

    [Fact]
    public void Sql_retry_exhaustion_counts_one_final_failure_after_attempts()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"base05-save-retry-failure-{Guid.NewGuid():N}.db");
        try
        {
            PerformanceMetrics.Configure(enabled: true, scenario: "sql-retry-exhaustion");
            var runner = new SqlDomainTransactionRunner(
                DatabaseProviderKind.Sqlite,
                new SqlDatabaseOptions { SqlitePath = databasePath },
                new SqlSessionOptions { BaseRetryDelayMs = 1 },
                maxAttempts: 2,
                continueOnError: true);

            var result = runner.Run(SqlSaveDomain.Accounts, _ =>
                throw new IOException("测试持续瞬时失败"));

            Assert.False(result.Success);
            var metrics = PerformanceMetrics.CreateSnapshot().Metrics;
            Assert.Equal(1L, metrics.Single(item => item.Name == nameof(PerformanceMetricKind.SaveAttemptFailure)).TotalValue ?? 0L);
            Assert.Equal(1L, metrics.Single(item => item.Name == nameof(PerformanceMetricKind.SaveFailure)).TotalValue ?? 0L);
        }
        finally
        {
            PerformanceMetrics.Configure(enabled: false);
            TryDelete(databasePath);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // Best effort cleanup; the test database is isolated under the temp directory.
        }
    }
}
