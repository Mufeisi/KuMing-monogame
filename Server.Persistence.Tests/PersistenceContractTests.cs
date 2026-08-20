using Microsoft.Data.Sqlite;
using Server;
using Server.MirDatabase;
using Server.MirEnvir;
using Server.Persistence;
using Server.Persistence.Sql;
using Xunit;

namespace Server.Persistence.Tests;

public sealed class PersistenceContractTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "lyo-persistence-tests", Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Legacy")]
    [InlineData("unknown")]
    public void ProviderMustBeExplicitSql(string provider)
    {
        Assert.Throws<InvalidOperationException>(() => ServerPersistenceFactory.ParseProvider(provider));
    }

    [Fact]
    public void IncompleteActivationManifestIsRejected()
    {
        Directory.CreateDirectory(_tempRoot);
        File.WriteAllText(
            Path.Combine(_tempRoot, SqliteDatabaseLayout.ActivationFileName),
            "{\"migrationId\":\"broken\",\"generationDirectory\":\"layouts/broken\",\"completed\":false}");

        Assert.Throws<InvalidOperationException>(() => SqliteDatabaseLayout.Resolve(_tempRoot));
    }

    [Fact]
    public void SourceHashAuthorizationIsRequiredBeforeActivation()
    {
        Directory.CreateDirectory(_tempRoot);
        var source = Path.Combine(AppContext.BaseDirectory, "Fixtures", "server.db");
        var request = new SqliteWorldOnlyMigrationRequest
        {
            SourcePath = source,
            TargetDirectory = _tempRoot,
            MigrationId = "wrong-hash",
            AuthorizedSourceSha256 = new string('0', 64),
            WorldOnlyResetPlayers = true,
        };

        Assert.Throws<InvalidOperationException>(() => new SqliteLayoutMigrator().Migrate(request));
        Assert.False(File.Exists(Path.Combine(_tempRoot, SqliteDatabaseLayout.ActivationFileName)));
    }

    [Fact]
    public void WorldOnlyMigrationCreatesVerifiedThreeAuthorityLayout()
    {
        Directory.CreateDirectory(_tempRoot);
        var source = Path.Combine(AppContext.BaseDirectory, "Fixtures", "server.db");
        var sourceSha = SqliteLayoutMigrator.ComputeFileSha256(source);
        var result = new SqliteLayoutMigrator().Migrate(new SqliteWorldOnlyMigrationRequest
        {
            SourcePath = source,
            TargetDirectory = _tempRoot,
            MigrationId = "contract-world-reset",
            AuthorizedSourceSha256 = sourceSha,
            WorldOnlyResetPlayers = true,
        });

        var layout = SqliteDatabaseLayout.Resolve(_tempRoot);
        Assert.Equal(0, Scalar(layout.IdentityPath, "SELECT COUNT(*) FROM accounts"));
        Assert.Equal(0, Scalar(layout.CharacterPath, "SELECT COUNT(*) FROM characters"));
        Assert.Equal(0, Scalar(layout.CharacterPath, "SELECT COUNT(*) FROM account_wallets"));
        Assert.Equal(0, Scalar(layout.CharacterPath, "SELECT COUNT(*) FROM account_storage"));
        Assert.Equal(0, Scalar(layout.CharacterPath, "SELECT COUNT(*) FROM next_ids"));
        Assert.Equal(0, Scalar(layout.CharacterPath, "SELECT COUNT(*) FROM item_instances"));
        Assert.Equal(0, Scalar(layout.CharacterPath, "SELECT COUNT(*) FROM mails"));
        Assert.Equal(0, Scalar(layout.CharacterPath, "SELECT COUNT(*) FROM auctions"));
        Assert.Equal(0, Scalar(layout.CharacterPath, "SELECT COUNT(*) FROM guilds"));
        Assert.Equal(0, Scalar(layout.CharacterPath, "SELECT COUNT(*) FROM npc_buybacks"));
        Assert.Equal(0, Scalar(layout.CharacterPath, "SELECT COUNT(*) FROM npc_used_goods"));
        Assert.Equal(0, Scalar(layout.CharacterPath, "SELECT COUNT(*) FROM item_locations"));
        Assert.Equal(0, Scalar(layout.CharacterPath, "SELECT COUNT(*) FROM conquest_runtime"));
        Assert.Equal(0, Scalar(layout.CharacterPath, "SELECT COUNT(*) FROM character_buffs"));
        Assert.Equal(8, Scalar(layout.WorldPath, "SELECT COUNT(*) FROM next_ids"));
        Assert.Equal(1, Scalar(layout.WorldPath, "SELECT COUNT(*) FROM server_meta WHERE meta_key='world_relations_epoch_utc_ms'"));
        Assert.Equal(3938, Scalar(layout.WorldPath, "SELECT COUNT(*) FROM item_infos"));
        Assert.Equal(1237, Scalar(layout.WorldPath, "SELECT COUNT(*) FROM monster_infos"));
        Assert.Equal(626, Scalar(layout.WorldPath, "SELECT COUNT(*) FROM map_infos"));
        Assert.Equal(4144, Scalar(layout.WorldPath, "SELECT COUNT(*) FROM map_respawns"));
        Assert.DoesNotContain(result.Tables, table => table.SourceRows != table.TargetRows || table.SourceChecksum != table.TargetChecksum);

        foreach (var path in new[] { layout.IdentityPath, layout.CharacterPath, layout.WorldPath })
        {
            Assert.Equal("ok", ScalarText(path, "PRAGMA integrity_check"));
            Assert.Equal(0, Scalar(path, "SELECT COUNT(*) FROM pragma_foreign_key_check"));
            Assert.Equal(0, Scalar(path, "SELECT COUNT(*) FROM sqlite_master WHERE name IN ('legacy_files','legacy_blobs')"));
        }
    }

    [Fact]
    public void WorldOnlyMigrationCanResumeWithTheSameMigrationId()
    {
        Directory.CreateDirectory(_tempRoot);
        var source = Path.Combine(AppContext.BaseDirectory, "Fixtures", "server.db");
        var sourceSha = SqliteLayoutMigrator.ComputeFileSha256(source);
        var request = new SqliteWorldOnlyMigrationRequest
        {
            SourcePath = source,
            TargetDirectory = _tempRoot,
            MigrationId = "resume-world-reset",
            AuthorizedSourceSha256 = sourceSha,
            WorldOnlyResetPlayers = true,
        };

        var first = new SqliteLayoutMigrator().Migrate(request);
        var second = new SqliteLayoutMigrator().Migrate(request);
        var layout = SqliteDatabaseLayout.Resolve(_tempRoot);

        Assert.Equal(first.GenerationDirectory, second.GenerationDirectory);
        Assert.Equal(
            first.Tables.Select(table => (table.Table, table.TargetRows, table.TargetChecksum)),
            second.Tables.Select(table => (table.Table, table.TargetRows, table.TargetChecksum)));
        Assert.Single(Directory.EnumerateFiles(Path.Combine(_tempRoot, "backups"), "*.db"));
        Assert.Equal(1, Scalar(layout.IdentityPath, "SELECT completed FROM database_manifest WHERE authority='identity'"));
        Assert.Equal(1, Scalar(layout.CharacterPath, "SELECT completed FROM database_manifest WHERE authority='character'"));
        Assert.Equal(1, Scalar(layout.WorldPath, "SELECT completed FROM database_manifest WHERE authority='world'"));
    }

    [Fact]
    public void ItemInstanceCanHaveOnlyOneLocation()
    {
        Directory.CreateDirectory(_tempRoot);
        var path = Path.Combine(_tempRoot, "characters.db");
        var options = new SqlDatabaseOptions { SqlitePath = path, Authority = DatabaseAuthority.Character };
        using (var session = SqlSession.Open(DatabaseProviderKind.Sqlite, options))
        {
            new SchemaMigrator(AuthoritySchemaMigrator.Create(DatabaseAuthority.Character))
                .ApplyPendingMigrations(session.Connection, session.Dialect, "test", "test");
            session.Execute("INSERT INTO item_instances (item_id,item_index,current_dura,max_dura,stack_count,gem_count,soul_bound_id,identified,cursed,slot_count,awake_type,refined_value,refine_added,refine_success_chance,wedding_ring,expire_utc_ms,rental_owner_name,rental_binding_flags,rental_expiry_utc_ms,rental_locked,is_shop_item,sealed_expiry_utc_ms,sealed_next_seal_utc_ms,gm_made,updated_utc_ms) VALUES (1,1,0,0,1,0,0,0,0,0,0,0,0,0,0,0,'',0,0,0,0,0,0,0,1)");
            session.Execute("INSERT INTO item_locations (item_id,location_kind,owner_id,container_kind,slot_index,parent_item_id,updated_utc_ms) VALUES (1,'character',1,1,0,NULL,1)");
            Assert.ThrowsAny<Exception>(() => session.Execute("INSERT INTO item_locations (item_id,location_kind,owner_id,container_kind,slot_index,parent_item_id,updated_utc_ms) VALUES (1,'mail',2,0,0,NULL,2)"));
        }
    }

    [Fact]
    public void SqliteSessionEnforcesDurabilityAndConcurrencyPragmas()
    {
        Directory.CreateDirectory(_tempRoot);
        var path = Path.Combine(_tempRoot, "pragmas.db");
        using var session = SqlSession.Open(DatabaseProviderKind.Sqlite, new SqlDatabaseOptions
        {
            SqlitePath = path,
            Authority = DatabaseAuthority.Character,
        });

        Assert.Equal(1, session.ExecuteScalar<long>("PRAGMA foreign_keys"));
        Assert.Equal("wal", session.ExecuteScalar<string>("PRAGMA journal_mode"));
        Assert.Equal(2, session.ExecuteScalar<long>("PRAGMA synchronous"));
        Assert.Equal(10000, session.ExecuteScalar<long>("PRAGMA busy_timeout"));
    }

    [Fact]
    public void CharacterSchemaContainsGuildAndNpcRoundTripFields()
    {
        Directory.CreateDirectory(_tempRoot);
        var path = Path.Combine(_tempRoot, "runtime-schema.db");
        using var session = OpenCharacterSchema(path);

        var guildColumns = session.Query<string>("SELECT name FROM pragma_table_info('guilds') ORDER BY cid");
        Assert.Contains("spare_points", guildColumns);
        Assert.Contains("votes", guildColumns);
        Assert.Contains("last_vote_attempt_utc_ms", guildColumns);
        Assert.Contains("voting", guildColumns);
        Assert.Contains("flag_image", guildColumns);
        Assert.Contains("flag_colour_argb", guildColumns);

        var memberColumns = session.Query<string>("SELECT name FROM pragma_table_info('guild_members') ORDER BY cid");
        Assert.Contains("last_login_utc_ms", memberColumns);
        Assert.Contains("has_voted", memberColumns);
        Assert.Contains("online", memberColumns);

        var buffColumns = session.Query<string>("SELECT name FROM pragma_table_info('guild_buffs') ORDER BY cid");
        Assert.Contains("active", buffColumns);
        Assert.Contains("active_time_remaining", buffColumns);

        foreach (var table in new[]
                 {
                     "guilds", "guild_ranks", "guild_members", "guild_notices", "guild_buffs", "guild_storage_slots",
                     "npc_buybacks", "npc_used_goods", "conquest_runtime", "conquest_facilities", "character_buffs",
                 })
        {
            var columns = session.Query<string>($"SELECT name FROM pragma_table_info('{table}') ORDER BY cid");
            Assert.Contains("snapshot_generation", columns);
            Assert.Contains("snapshot_active", columns);
        }

        Assert.Equal(2, session.ExecuteScalar<long>("SELECT COUNT(*) FROM pragma_foreign_key_list('character_buff_stats') WHERE \"table\"='character_buffs'"));
        Assert.Equal(2, session.ExecuteScalar<long>("SELECT COUNT(*) FROM pragma_foreign_key_list('guild_members') WHERE \"table\"='guild_ranks'"));
        Assert.Equal(0, session.ExecuteScalar<long>("PRAGMA foreign_key_check"));
    }

    [Fact]
    public void RequiredCharacterReadFailureDoesNotReplaceExistingAccountState()
    {
        Directory.CreateDirectory(_tempRoot);
        var source = Path.Combine(AppContext.BaseDirectory, "Fixtures", "server.db");
        var sourceSha = SqliteLayoutMigrator.ComputeFileSha256(source);
        new SqliteLayoutMigrator().Migrate(new SqliteWorldOnlyMigrationRequest
        {
            SourcePath = source,
            TargetDirectory = _tempRoot,
            MigrationId = "startup-read-failure",
            AuthorizedSourceSha256 = sourceSha,
            WorldOnlyResetPlayers = true,
        });

        var layout = SqliteDatabaseLayout.Resolve(_tempRoot);
        using (var connection = new SqliteConnection($"Data Source={layout.CharacterPath};Mode=ReadWrite;Cache=Private"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "DROP TABLE character_flags";
            command.ExecuteNonQuery();
        }

        var previousDirectory = Settings.SqliteDirectory;
        var previousAutoApply = Settings.AutoApplySchemaOnStartup;
        try
        {
            Settings.SqliteDirectory = _tempRoot;
            Settings.AutoApplySchemaOnStartup = true;
            var envir = new Envir();
            var sentinel = new AccountInfo { Index = 77, AccountID = "sentinel" };
            envir.AccountList.Add(sentinel);
            var persistence = new SqlServerPersistence(DatabaseProviderKind.Sqlite, new TestStatePort(envir));

            var result = persistence.LoadStartup();

            Assert.False(result.Committed);
            Assert.Equal(PersistenceModuleState.Faulted, persistence.State);
            Assert.Same(sentinel, Assert.Single(envir.AccountList));
        }
        finally
        {
            Settings.SqliteDirectory = previousDirectory;
            Settings.AutoApplySchemaOnStartup = previousAutoApply;
        }
    }

    [Fact]
    public void ArchiveRestoreAndBackupOwnershipConflictFollowCharacterContract()
    {
        Directory.CreateDirectory(_tempRoot);
        var source = Path.Combine(AppContext.BaseDirectory, "Fixtures", "server.db");
        var sourceSha = SqliteLayoutMigrator.ComputeFileSha256(source);
        new SqliteLayoutMigrator().Migrate(new SqliteWorldOnlyMigrationRequest
        {
            SourcePath = source,
            TargetDirectory = _tempRoot,
            MigrationId = "character-lifecycle-contract",
            AuthorizedSourceSha256 = sourceSha,
            WorldOnlyResetPlayers = true,
        });

        var previousDirectory = Settings.SqliteDirectory;
        var previousAutoApply = Settings.AutoApplySchemaOnStartup;
        try
        {
            Settings.SqliteDirectory = _tempRoot;
            Settings.AutoApplySchemaOnStartup = true;
            var envir = new Envir();
            var persistence = new SqlServerPersistence(DatabaseProviderKind.Sqlite, new TestStatePort(envir));
            CompleteStartup(persistence);

            var account = new AccountInfo { Index = 101, AccountID = "lifecycle-account" };
            var character = new CharacterInfo
            {
                Index = 201,
                Name = "lifecycle-character",
                AccountInfo = account,
                Heroes = new HeroInfo[1],
            };
            account.Characters.Add(character);
            envir.AccountList.Add(account);
            envir.CharacterList.Add(character);

            Assert.True(persistence.ExecuteIdentity(new PersistIdentitySnapshotCommand()).Committed);
            Assert.True(persistence.Commit(CheckpointKind.CharacterRuntime, CommitReason.Operator).Committed);

            var missing = new CharacterInfo { Index = 999, Name = "missing" };
            Assert.False(persistence.ExecuteCharacter(new ArchiveCharacterCommand(missing)).Committed);
            Assert.Contains(character, envir.CharacterList);

            Assert.True(persistence.ExecuteCharacter(new ArchiveCharacterCommand(character)).Committed);
            var layout = SqliteDatabaseLayout.Resolve(_tempRoot);
            Assert.Equal("archived", ScalarText(layout.CharacterPath, "SELECT lifecycle_state FROM characters WHERE character_id=201"));
            Assert.True(persistence.ExecuteCharacter(new RestoreCharacterCommand(character.Name, account)).Committed);
            Assert.Equal("active", ScalarText(layout.CharacterPath, "SELECT lifecycle_state FROM characters WHERE character_id=201"));
            Assert.Equal(101, Scalar(layout.CharacterPath, "SELECT account_id FROM characters WHERE character_id=201"));

            var item = new UserItem(envir.ItemInfoList.First()) { UniqueID = 301 };
            character.Inventory[0] = item;
            Assert.True(persistence.Commit(CheckpointKind.CharacterRuntime, CommitReason.Operator).Committed);
            var backup = persistence.ExecuteCharacter(new BackupCharacterCommand(character));
            Assert.True(backup.Committed, backup.Diagnostics);

            using (var connection = new SqliteConnection($"Data Source={layout.CharacterPath};Mode=ReadWrite;Cache=Private"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "UPDATE item_locations SET location_kind='mail',owner_id=999 WHERE item_id=301";
                Assert.Equal(1, command.ExecuteNonQuery());
            }

            var load = persistence.ExecuteCharacter(new LoadCharacterBackupCommand(character.Name));
            Assert.False(load.Committed);
            Assert.Equal("backup_item_ownership_conflict", load.ErrorCode);

            using (var connection = new SqliteConnection($"Data Source={layout.CharacterPath};Mode=ReadWrite;Cache=Private"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "UPDATE item_locations SET location_kind='character',owner_id=201 WHERE item_id=301";
                Assert.Equal(1, command.ExecuteNonQuery());
            }

            character.Level = 99;
            var rollback = persistence.ExecuteCharacter(new LoadCharacterBackupCommand(character.Name));
            Assert.True(rollback.Committed, rollback.Diagnostics);
            Assert.Equal(0, rollback.Character.Level);
            Assert.Same(envir.ItemInfoList.First(), rollback.Character.Inventory[0].Info);
        }
        finally
        {
            Settings.SqliteDirectory = previousDirectory;
            Settings.AutoApplySchemaOnStartup = previousAutoApply;
        }
    }

    [Fact]
    public void MissingIdentityAccountIsRejectedBeforeCharacterSnapshotTouchesMemory()
    {
        Directory.CreateDirectory(_tempRoot);
        var source = Path.Combine(AppContext.BaseDirectory, "Fixtures", "server.db");
        var sourceSha = SqliteLayoutMigrator.ComputeFileSha256(source);
        new SqliteLayoutMigrator().Migrate(new SqliteWorldOnlyMigrationRequest
        {
            SourcePath = source,
            TargetDirectory = _tempRoot,
            MigrationId = "missing-identity-reference",
            AuthorizedSourceSha256 = sourceSha,
            WorldOnlyResetPlayers = true,
        });

        var previousDirectory = Settings.SqliteDirectory;
        var previousAutoApply = Settings.AutoApplySchemaOnStartup;
        try
        {
            Settings.SqliteDirectory = _tempRoot;
            Settings.AutoApplySchemaOnStartup = true;
            var sourceEnvir = new Envir();
            var writer = new SqlServerPersistence(DatabaseProviderKind.Sqlite, new TestStatePort(sourceEnvir));
            CompleteStartup(writer);

            var account = new AccountInfo { Index = 401, AccountID = "missing-identity" };
            var character = new CharacterInfo { Index = 402, Name = "orphan-character", AccountInfo = account, Heroes = new HeroInfo[1] };
            account.Characters.Add(character);
            sourceEnvir.AccountList.Add(account);
            sourceEnvir.CharacterList.Add(character);
            Assert.True(writer.ExecuteIdentity(new PersistIdentitySnapshotCommand()).Committed);
            Assert.True(writer.Commit(CheckpointKind.CharacterRuntime, CommitReason.Operator).Committed);

            var layout = SqliteDatabaseLayout.Resolve(_tempRoot);
            using (var connection = new SqliteConnection($"Data Source={layout.IdentityPath};Mode=ReadWrite;Cache=Private"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "DELETE FROM accounts WHERE account_id=401";
                Assert.Equal(1, command.ExecuteNonQuery());
            }

            var targetEnvir = new Envir();
            var sentinel = new AccountInfo { Index = 499, AccountID = "sentinel" };
            targetEnvir.AccountList.Add(sentinel);
            var reader = new SqlServerPersistence(DatabaseProviderKind.Sqlite, new TestStatePort(targetEnvir));

            var result = reader.LoadStartup();

            Assert.False(result.Committed);
            Assert.Contains("引用缺失的 Identity 账号", result.Diagnostics);
            Assert.Equal(PersistenceModuleState.Faulted, reader.State);
            Assert.Same(sentinel, Assert.Single(targetEnvir.AccountList));
        }
        finally
        {
            Settings.SqliteDirectory = previousDirectory;
            Settings.AutoApplySchemaOnStartup = previousAutoApply;
        }
    }

    [Fact]
    public void IdentityAccountsGetMissingCharacterRootsWithoutOverwritingExistingWallet()
    {
        Directory.CreateDirectory(_tempRoot);
        var source = Path.Combine(AppContext.BaseDirectory, "Fixtures", "server.db");
        var sourceSha = SqliteLayoutMigrator.ComputeFileSha256(source);
        new SqliteLayoutMigrator().Migrate(new SqliteWorldOnlyMigrationRequest
        {
            SourcePath = source,
            TargetDirectory = _tempRoot,
            MigrationId = "identity-character-roots",
            AuthorizedSourceSha256 = sourceSha,
            WorldOnlyResetPlayers = true,
        });

        var previousDirectory = Settings.SqliteDirectory;
        var previousAutoApply = Settings.AutoApplySchemaOnStartup;
        try
        {
            Settings.SqliteDirectory = _tempRoot;
            Settings.AutoApplySchemaOnStartup = true;
            var writerEnvir = new Envir();
            var writer = new SqlServerPersistence(DatabaseProviderKind.Sqlite, new TestStatePort(writerEnvir));
            CompleteStartup(writer);
            writerEnvir.AccountList.Add(new AccountInfo { Index = 501, AccountID = "new-roots" });
            writerEnvir.AccountList.Add(new AccountInfo { Index = 502, AccountID = "existing-wallet" });
            Assert.True(writer.ExecuteIdentity(new PersistIdentitySnapshotCommand()).Committed);

            var layout = SqliteDatabaseLayout.Resolve(_tempRoot);
            using (var connection = new SqliteConnection($"Data Source={layout.CharacterPath};Mode=ReadWrite;Cache=Private"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "INSERT INTO account_wallets(account_id,gold,credit,updated_utc_ms) VALUES(502,123,7,1)";
                command.ExecuteNonQuery();
            }

            var reader = new SqlServerPersistence(DatabaseProviderKind.Sqlite, new TestStatePort(new Envir()));
            CompleteStartup(reader);

            Assert.Equal(0, Scalar(layout.CharacterPath, "SELECT gold FROM account_wallets WHERE account_id=501"));
            Assert.Equal(123, Scalar(layout.CharacterPath, "SELECT gold FROM account_wallets WHERE account_id=502"));
            Assert.Equal(7, Scalar(layout.CharacterPath, "SELECT credit FROM account_wallets WHERE account_id=502"));
            Assert.Equal(2, Scalar(layout.CharacterPath, "SELECT COUNT(*) FROM account_storage WHERE account_id IN (501,502)"));
        }
        finally
        {
            Settings.SqliteDirectory = previousDirectory;
            Settings.AutoApplySchemaOnStartup = previousAutoApply;
        }
    }

    [Fact]
    public void CharacterRuntimeTransactionRollsBackAllDomainsOnFailure()
    {
        Directory.CreateDirectory(_tempRoot);
        var path = Path.Combine(_tempRoot, "rollback.db");
        using var session = OpenCharacterSchema(path);

        Assert.Throws<InvalidOperationException>(() => session.RunInTransaction(s =>
        {
            s.Execute("INSERT INTO item_instances (item_id,item_index,current_dura,max_dura,stack_count,gem_count,soul_bound_id,identified,cursed,slot_count,awake_type,refined_value,refine_added,refine_success_chance,wedding_ring,expire_utc_ms,rental_owner_name,rental_binding_flags,rental_expiry_utc_ms,rental_locked,is_shop_item,sealed_expiry_utc_ms,sealed_next_seal_utc_ms,gm_made,updated_utc_ms) VALUES (999,1,0,0,1,0,0,0,0,0,0,0,0,0,0,0,'',0,0,0,0,0,0,0,1)");
            s.Execute("INSERT INTO account_wallets (account_id,gold,credit,updated_utc_ms) VALUES (1,100,5,1)");
            s.Execute("INSERT INTO guilds (guild_id,guild_name,leader_character_id,gold,level,experience,updated_utc_ms) VALUES (1,'rollback',0,50,1,0,1)");
            s.Execute("INSERT INTO npc_used_goods (used_good_id,npc_id,item_id,price,available_utc_ms,updated_utc_ms) VALUES (1,1,999,1,1,1)");
            throw new InvalidOperationException("fault injection");
        }));

        Assert.Equal(0, session.ExecuteScalar<long>("SELECT COUNT(*) FROM account_wallets"));
        Assert.Equal(0, session.ExecuteScalar<long>("SELECT COUNT(*) FROM guilds"));
        Assert.Equal(0, session.ExecuteScalar<long>("SELECT COUNT(*) FROM npc_used_goods"));
        Assert.Equal(0, session.ExecuteScalar<long>("SELECT COUNT(*) FROM item_instances"));
    }

    private static SqlSession OpenCharacterSchema(string path)
    {
        var session = SqlSession.Open(DatabaseProviderKind.Sqlite, new SqlDatabaseOptions
        {
            SqlitePath = path,
            Authority = DatabaseAuthority.Character,
        });
        new SchemaMigrator(AuthoritySchemaMigrator.Create(DatabaseAuthority.Character))
            .ApplyPendingMigrations(session.Connection, session.Dialect, "test", "test");
        return session;
    }

    private static long Scalar(string path, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={path};Mode=ReadOnly;Cache=Private");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private static string ScalarText(string path, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={path};Mode=ReadOnly;Cache=Private");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(command.ExecuteScalar());
    }

    private static void CompleteStartup(IGamePersistence persistence)
    {
        var startup = persistence.LoadStartup();
        Assert.True(startup.Committed, startup.Diagnostics);
        Assert.Equal(PersistenceModuleState.Loading, persistence.State);
        Assert.True(persistence.ExecuteCharacter(new LoadGuildRuntimeCommand()).Committed);
        Assert.True(persistence.ExecuteCharacter(new LoadNpcGoodsRuntimeCommand()).Committed);
        var conquest = persistence.ExecuteCharacter(new LoadConquestRuntimeCommand());
        Assert.True(conquest.Committed, conquest.Diagnostics);
        Assert.Equal(PersistenceModuleState.Ready, persistence.State);
    }

    private sealed class TestStatePort : IServerStatePort
    {
        public Envir Envir { get; }

        public TestStatePort(Envir envir)
        {
            Envir = envir;
        }
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (!Directory.Exists(_tempRoot)) return;

        foreach (var file in Directory.EnumerateFiles(_tempRoot, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(_tempRoot, recursive: true);
    }
}
