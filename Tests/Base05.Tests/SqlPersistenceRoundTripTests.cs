using Server;
using Server.MirDatabase;
using Server.MirEnvir;
using Server.Persistence;
using Server.Persistence.Sql;
using Server.Utils;
using Shared.Diagnostics;
using Server.Scripting.Variables;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Base05.Tests;

[Collection("PerformanceMetrics")]
public sealed class SqlPersistenceRoundTripTests
{
    [Fact]
    public void PrivateVariableSchemaAndUpsertAreDefinedForSqliteAndMySql()
    {
        SchemaMigration migration = Assert.Single(
            SchemaMigrator.CreateDefaultMigrations(), item => item.Version == 18);
        string createTable = Assert.Single(
            migration.Statements,
            statement => statement.Contains(
                "CREATE TABLE IF NOT EXISTS character_script_variables", StringComparison.Ordinal));
        Assert.Contains("PRIMARY KEY(character_id, variable_namespace, variable_key)", createTable);
        Assert.Contains("decimal_text", createTable);

        string[] columns =
        [
            "character_id", "variable_namespace", "variable_key", "value_kind",
            "integer_value", "decimal_text", "text_value", "reset_policy",
            "reset_period_id", "updated_utc_ms"
        ];
        string[] keys = ["character_id", "variable_namespace", "variable_key"];
        string[] updates = columns.Except(keys).ToArray();
        string sqlite = SqlDialectFactory.Create(DatabaseProviderKind.Sqlite)
            .BuildUpsert("character_script_variables", columns, keys, updates);
        string mysql = SqlDialectFactory.Create(DatabaseProviderKind.MySql)
            .BuildUpsert("character_script_variables", columns, keys, updates);

        Assert.Contains("ON CONFLICT", sqlite, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ON DUPLICATE KEY UPDATE", mysql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ServerVariableSchemaAndUpsertAreDefinedForSqliteAndMySql()
    {
        SchemaMigration migration = Assert.Single(
            SchemaMigrator.CreateDefaultMigrations(), item => item.Version == 19);
        string createTable = Assert.Single(
            migration.Statements,
            statement => statement.Contains(
                "CREATE TABLE IF NOT EXISTS server_script_variables", StringComparison.Ordinal));
        Assert.Contains("PRIMARY KEY(variable_namespace, variable_key)", createTable);

        string[] columns =
        [
            "variable_namespace", "variable_key", "value_kind", "integer_value",
            "decimal_text", "text_value", "updated_utc_ms"
        ];
        string[] keys = ["variable_namespace", "variable_key"];
        string[] updates = columns.Except(keys).ToArray();
        Assert.Contains("ON CONFLICT", SqlDialectFactory.Create(DatabaseProviderKind.Sqlite)
            .BuildUpsert("server_script_variables", columns, keys, updates), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ON DUPLICATE KEY UPDATE", SqlDialectFactory.Create(DatabaseProviderKind.MySql)
            .BuildUpsert("server_script_variables", columns, keys, updates), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Var05SchemaDefinesGuildVariablesAndRankingIndex()
    {
        SchemaMigration migration = Assert.Single(
            SchemaMigrator.CreateDefaultMigrations(), item => item.Version == 20);
        Assert.Contains(migration.Statements, statement =>
            statement.Contains("CREATE TABLE IF NOT EXISTS guild_script_variables", StringComparison.Ordinal));
        Assert.Contains(migration.Statements, statement =>
            statement.Contains("guild_script_variables_ix_rank", StringComparison.Ordinal));
        Assert.Contains(migration.Statements, statement =>
            statement.Contains("character_script_variables_ix_rank", StringComparison.Ordinal));
        Assert.Contains(migration.Statements, statement =>
            statement.Contains("server_script_variables_ix_rank", StringComparison.Ordinal));
        string[] columns =
        [
            "guild_id", "variable_namespace", "variable_key", "value_kind",
            "integer_value", "decimal_text", "updated_utc_ms"
        ];
        string[] keys = ["guild_id", "variable_namespace", "variable_key"];
        string[] updates = columns.Except(keys).ToArray();
        Assert.Contains("ON CONFLICT", SqlDialectFactory.Create(DatabaseProviderKind.Sqlite)
            .BuildUpsert("guild_script_variables", columns, keys, updates), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ON DUPLICATE KEY UPDATE", SqlDialectFactory.Create(DatabaseProviderKind.MySql)
            .BuildUpsert("guild_script_variables", columns, keys, updates), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LingFengCustomItemAttributeSchemaIsAppendOnly()
    {
        SchemaMigration migration = Assert.Single(
            SchemaMigrator.CreateDefaultMigrations(), item => item.Version == 21);
        Assert.Equal(
            "ALTER TABLE item_instances ADD COLUMN lingfeng_custom_attributes TEXT NOT NULL DEFAULT ''",
            Assert.Single(migration.Statements));
    }

    [Fact]
    public void Sqlite_round_trips_server_persistent_variables()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"base05-global-vars-{Guid.NewGuid():N}.db");
        var persistence = new SqlServerPersistence(
            DatabaseProviderKind.Sqlite,
            new SqlDatabaseOptions { SqlitePath = databasePath });
        try
        {
            var source = new Envir();
            source.ScriptVariables.Set(
                ScriptVariableScope.G, "EVENTRATE", ScriptVariableValue.FromDecimal(3.125m));
            source.ScriptVariables.Set(
                ScriptVariableScope.A, "#0", ScriptVariableValue.FromString("跨服重启公告"));
            source.ScriptVariables.Set(
                ScriptVariableScope.Global, "score", ScriptVariableValue.FromInteger(456));
            persistence.SaveScriptVariables(source);
            ((IPendingSaveCoordinator)persistence).DrainPendingSaves();

            var restored = new Envir();
            persistence.LoadScriptVariables(restored);
            Assert.True(restored.ScriptVariables.TryGet(
                ScriptVariableScope.G, "EventRate", out var rate));
            Assert.Equal(3.125m, rate.Decimal);
            Assert.True(restored.ScriptVariables.TryGet(
                ScriptVariableScope.A, "#0", out var text));
            Assert.Equal("跨服重启公告", text.Text);
            Assert.True(restored.ScriptVariables.TryGet(
                ScriptVariableScope.Global, "score", out var globalScore));
            Assert.Equal(456L, globalScore.Integer);

            source.ScriptVariables.Clear(ScriptVariableScope.G);
            source.ScriptVariables.Clear(ScriptVariableScope.A);
            source.ScriptVariables.Clear(ScriptVariableScope.Global);
            persistence.SaveScriptVariables(source);
            ((IPendingSaveCoordinator)persistence).DrainPendingSaves();

            var cleared = new Envir();
            persistence.LoadScriptVariables(cleared);
            Assert.Equal(0, cleared.ScriptVariables.Count);
        }
        finally
        {
            TryDelete(databasePath);
            TryDelete(databasePath + "-wal");
            TryDelete(databasePath + "-shm");
        }
    }

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
            Assert.True(inventoryItem.TrySetLingFengCustomAbility(5, 0, 249));
            Assert.True(inventoryItem.TrySetLingFengCustomAbility(5, 1, 3));
            Assert.True(inventoryItem.TryChangeLingFengCustomValues(5, "=", 12, 34, 56));
            Assert.True(inventoryItem.TrySetLingFengByteMark(2, 255));
            Assert.True(inventoryItem.TrySetLingFengIntMark(3, 123456));
            Assert.True(inventoryItem.TrySetLingFengTextMark(1, "命格标记"));
            Assert.True(inventoryItem.TrySetLingFengCustomText("命格持久化"));
            Assert.True(inventoryItem.TrySetLingFengCustomProgressBar(0, 0, "1"));
            Assert.True(inventoryItem.TrySetLingFengCustomProgressBar(0, 1, "命格%r%："));
            Assert.True(inventoryItem.TryChangeLingFengCustomProgressBarValue(0, 0, "=", 1000));
            Assert.True(inventoryItem.TryChangeLingFengCustomProgressBarValue(0, 1, "=", 600));
            Assert.True(inventoryItem.TrySetLingFengItemEffect(1, 218));
            Assert.True(inventoryItem.TryChangeLingFengNewItemValue(25, "=", 777));
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
            character.ScriptVariables.Set(
                ScriptVariableScope.U, "droprate", ScriptVariableValue.FromDecimal(12.75m));
            character.ScriptVariables.Set(
                ScriptVariableScope.T, "#0", ScriptVariableValue.FromString("永久称号"));
            character.ScriptVariables.EnsureDailyPeriod(20260815);
            character.ScriptVariables.Set(
                ScriptVariableScope.J, "#0", ScriptVariableValue.FromInteger(7));
            character.ScriptVariables.Set(
                ScriptVariableScope.Z, "#0", ScriptVariableValue.FromString("今日阶段"));
            character.ScriptVariables.Set(
                ScriptVariableScope.Human, "lifetime", ScriptVariableValue.FromDecimal(8.5m));

            persistence.SaveAccounts(source);
            ((IPendingSaveCoordinator)persistence).DrainPendingSaves();

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
            LingFengCustomItemAttribute restoredCustom =
                restoredInventoryItem.GetLingFengCustomAttribute(5);
            Assert.Equal(249, restoredCustom.Colour);
            Assert.Equal(3, restoredCustom.Binding);
            Assert.Equal(12, restoredCustom.Value1);
            Assert.Equal(34, restoredCustom.Value2);
            Assert.Equal(56, restoredCustom.Value3);
            Assert.True(restoredInventoryItem.TryGetLingFengByteMark(2, out byte restoredByteMark));
            Assert.Equal((byte)255, restoredByteMark);
            Assert.True(restoredInventoryItem.TryGetLingFengIntMark(3, out int restoredIntMark));
            Assert.Equal(123456, restoredIntMark);
            Assert.True(restoredInventoryItem.TryGetLingFengTextMark(1, out string restoredTextMark));
            Assert.Equal("命格标记", restoredTextMark);
            Assert.True(restoredInventoryItem.TryGetLingFengCustomProgressBarValue(
                0, 0, out int restoredMaximum));
            Assert.Equal(1000, restoredMaximum);
            Assert.True(restoredInventoryItem.TryGetLingFengCustomProgressBarValue(
                0, 1, out int restoredCurrent));
            Assert.Equal(600, restoredCurrent);
            Assert.Equal((ushort)218, restoredInventoryItem.GetLingFengItemEffect(1));
            Assert.True(restoredInventoryItem.TryGetLingFengNewItemValue(
                25, out int restoredNewItemValue));
            Assert.Equal(777, restoredNewItemValue);
            Assert.Contains("命格持久化",
                restoredInventoryItem.GetLingFengCustomAttributeDisplayLines());
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
            Assert.True(restoredCharacter.ScriptVariables.TryGet(
                ScriptVariableScope.U, "droprate", out var restoredRate));
            Assert.Equal(12.75m, restoredRate.Decimal);
            Assert.True(restoredCharacter.ScriptVariables.TryGet(
                ScriptVariableScope.T, "#0", out var restoredTitle));
            Assert.Equal("永久称号", restoredTitle.Text);
            Assert.Equal(20260815, restoredCharacter.ScriptVariables.DailyResetPeriodId);
            Assert.True(restoredCharacter.ScriptVariables.TryGet(
                ScriptVariableScope.J, "#0", out var restoredDaily));
            Assert.Equal(7L, restoredDaily.Integer);
            Assert.True(restoredCharacter.ScriptVariables.TryGet(
                ScriptVariableScope.Z, "#0", out var restoredDailyText));
            Assert.Equal("今日阶段", restoredDailyText.Text);
            Assert.True(restoredCharacter.ScriptVariables.TryGet(
                ScriptVariableScope.Human, "lifetime", out var restoredLifetime));
            Assert.Equal(8.5m, restoredLifetime.Decimal);
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
    public void Sqlite_persists_guild_custom_variables_in_relational_table()
    {
        string databasePath = Path.Combine(Path.GetTempPath(), $"base05-guild-vars-{Guid.NewGuid():N}.db");
        var persistence = new SqlServerPersistence(
            DatabaseProviderKind.Sqlite,
            new SqlDatabaseOptions { SqlitePath = databasePath });
        try
        {
            var envir = new Envir();
            var guild = new GuildInfo { GuildIndex = 77, Name = "变量行会" };
            guild.ScriptVariables.Set(
                ScriptVariableScope.Guild, "score", ScriptVariableValue.FromInteger(123));
            envir.GuildList.Add(guild);

            persistence.SaveGuilds(envir, forced: true);
            ((IPendingSaveCoordinator)persistence).DrainPendingSaves();

            ScriptVariableRankEntry rank = Assert.Single(
                persistence.QueryIntegerVariableRanking(ScriptVariableScope.Guild, "score", 10));
            Assert.Equal(77L, rank.OwnerId);
            Assert.Equal(123L, rank.Value);

            using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT integer_value FROM guild_script_variables " +
                "WHERE guild_id = 77 AND variable_namespace = 'Guild' AND variable_key = 'SCORE'";
            Assert.Equal(123L, Convert.ToInt64(command.ExecuteScalar()));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            TryDelete(databasePath);
            TryDelete(databasePath + "-wal");
            TryDelete(databasePath + "-shm");
        }
    }

    [Fact]
    public void Sqlite_archive_round_trip_preserves_private_persistent_variables()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"base05-archive-{Guid.NewGuid():N}.db");
        var persistence = new SqlServerPersistence(
            DatabaseProviderKind.Sqlite,
            new SqlDatabaseOptions { SqlitePath = databasePath });
        try
        {
            var envir = new Envir();
            var character = new CharacterInfo
            {
                Index = 901,
                Name = "变量归档测试",
                CreationIP = "127.0.0.1",
                Heroes = new HeroInfo[1],
            };
            character.ScriptVariables.Set(
                ScriptVariableScope.U, "chance", ScriptVariableValue.FromDecimal(33.33333333m));
            character.ScriptVariables.Set(
                ScriptVariableScope.T, "#2", ScriptVariableValue.FromString("可恢复文本"));

            persistence.SaveArchivedCharacter(envir, character);
            ((IPendingSaveCoordinator)persistence).DrainPendingSaves();
            CharacterInfo restored = persistence.GetArchivedCharacter(envir, character.Name);

            Assert.NotNull(restored);
            Assert.True(restored.ScriptVariables.TryGet(
                ScriptVariableScope.U, "chance", out var chance));
            Assert.Equal(33.33333333m, chance.Decimal);
            Assert.True(restored.ScriptVariables.TryGet(
                ScriptVariableScope.T, "#2", out var text));
            Assert.Equal("可恢复文本", text.Text);
        }
        finally
        {
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
