using System.Text.RegularExpressions;

namespace Server.Persistence.Sql
{
    public static class AuthoritySchemaMigrator
    {
        private static readonly Regex TargetTablePattern = new(
            "(?:CREATE\\s+(?:UNIQUE\\s+)?INDEX\\s+\\S+\\s+ON|CREATE\\s+TABLE\\s+IF\\s+NOT\\s+EXISTS|ALTER\\s+TABLE)\\s+[`\"\\[]?(?<table>[a-zA-Z0-9_]+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly HashSet<string> IdentityTables = new(StringComparer.OrdinalIgnoreCase)
        {
            "accounts",
        };

        private static readonly HashSet<string> WorldTables = new(StringComparer.OrdinalIgnoreCase)
        {
            "map_infos", "map_safe_zones", "map_respawns", "map_movements", "map_mine_zones",
            "item_infos", "item_info_stats", "monster_infos", "monster_info_stats", "npc_infos",
            "npc_collect_quests", "npc_finish_quests", "quest_infos", "magic_infos", "gameshop_items",
            "dragon_info", "dragon_exps", "conquests", "conquest_extra_maps", "conquest_guards",
            "conquest_gates", "conquest_walls", "conquest_sieges", "conquest_flags",
            "conquest_control_points", "respawn_timer_state", "respawn_tick_options",
        };

        private static readonly HashSet<string> SharedMetadataTables = new(StringComparer.OrdinalIgnoreCase)
        {
            "server_meta", "next_ids",
        };

        private static readonly HashSet<string> RemovedLegacyTables = new(StringComparer.OrdinalIgnoreCase)
        {
            "legacy_files", "legacy_blobs",
            "character_buffs",
        };

        public static IReadOnlyList<SchemaMigration> Create(DatabaseAuthority authority)
        {
            var migrations = new List<SchemaMigration>
            {
                new(
                    1,
                    $"{authority} manifest",
                    [
                        "CREATE TABLE IF NOT EXISTS database_manifest (" +
                        "authority VARCHAR(32) NOT NULL PRIMARY KEY, " +
                        "layout_version INTEGER NOT NULL, " +
                        "migration_id VARCHAR(128) NOT NULL, " +
                        "generation BIGINT NOT NULL, " +
                        "source_checksum VARCHAR(64) NOT NULL, " +
                        "completed INTEGER NOT NULL, " +
                        "updated_utc_ms BIGINT NOT NULL" +
                        ")",
                    ]),
            };

            if (authority == DatabaseAuthority.Identity)
            {
                migrations.Add(CreateIdentityMigration());
                return migrations;
            }

            var source = SchemaMigrator.CreateDefaultMigrations();
            foreach (var migration in source)
            {
                var statements = migration.Statements
                    .Where(statement => BelongsTo(statement, authority))
                    .ToArray();

                if (statements.Length == 0)
                    continue;

                migrations.Add(new SchemaMigration(
                    migration.Version + 1,
                    $"{authority}: {migration.Description}",
                    statements));
            }

            if (authority == DatabaseAuthority.Character)
            {
                migrations.Add(CreateCharacterOwnershipMigration());
                migrations.Add(CreateCharacterRuntimeDetailMigration());
                migrations.Add(CreateCharacterSnapshotGenerationMigration());
            }
            else if (authority == DatabaseAuthority.World)
            {
                migrations.Add(CreateWorldMetadataRepairMigration());
            }

            return migrations;
        }

        public static void MarkComplete(
            SqlSession session,
            DatabaseAuthority authority,
            string migrationId,
            long generation,
            string sourceChecksum)
        {
            var sql = session.Dialect.BuildUpsert(
                "database_manifest",
                ["authority", "layout_version", "migration_id", "generation", "source_checksum", "completed", "updated_utc_ms"],
                ["authority"],
                ["layout_version", "migration_id", "generation", "source_checksum", "completed", "updated_utc_ms"]);

            session.Execute(sql, new
            {
                authority = authority.ToString().ToLowerInvariant(),
                layout_version = 1,
                migration_id = migrationId ?? string.Empty,
                generation,
                source_checksum = sourceChecksum ?? string.Empty,
                completed = 1,
                updated_utc_ms = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            });
        }

        private static SchemaMigration CreateIdentityMigration()
        {
            return new SchemaMigration(
                2,
                "Identity accounts",
                [
                    "CREATE TABLE IF NOT EXISTS accounts (" +
                    "account_id BIGINT NOT NULL PRIMARY KEY, " +
                    "account_name VARCHAR(32) NOT NULL, " +
                    "password_hash TEXT NOT NULL, " +
                    "password_salt BLOB NOT NULL, " +
                    "require_password_change INTEGER NOT NULL, " +
                    "user_name TEXT NOT NULL, " +
                    "birth_utc_ms BIGINT NOT NULL, " +
                    "secret_question TEXT NOT NULL, " +
                    "secret_answer TEXT NOT NULL, " +
                    "email_address TEXT NOT NULL, " +
                    "creation_ip VARCHAR(45) NOT NULL, " +
                    "creation_utc_ms BIGINT NOT NULL, " +
                    "banned INTEGER NOT NULL, " +
                    "ban_reason TEXT NOT NULL, " +
                    "expiry_utc_ms BIGINT NOT NULL, " +
                    "last_ip VARCHAR(45) NOT NULL, " +
                    "last_utc_ms BIGINT NOT NULL, " +
                    "wrong_password_count INTEGER NOT NULL DEFAULT 0, " +
                    "admin_account INTEGER NOT NULL, " +
                    "updated_utc_ms BIGINT NOT NULL" +
                    ")",
                    "CREATE UNIQUE INDEX accounts_uq_account_name ON accounts(account_name)",
                    "CREATE INDEX accounts_ix_last_utc_ms ON accounts(last_utc_ms)",
                    "CREATE INDEX accounts_ix_expiry_utc_ms ON accounts(expiry_utc_ms)",
                ]);
        }

        private static SchemaMigration CreateCharacterOwnershipMigration()
        {
            return new SchemaMigration(
                19,
                "Character ownership, lifecycle and relational runtime domains",
                [
                    "ALTER TABLE characters ADD COLUMN lifecycle_state VARCHAR(24) NOT NULL DEFAULT 'active'",
                    "ALTER TABLE characters ADD COLUMN archived_utc_ms BIGINT NOT NULL DEFAULT 0",
                    "CREATE TABLE IF NOT EXISTS account_wallets (account_id BIGINT NOT NULL PRIMARY KEY, gold BIGINT NOT NULL, credit BIGINT NOT NULL, updated_utc_ms BIGINT NOT NULL)",
                    "CREATE TABLE IF NOT EXISTS item_locations (item_id BIGINT NOT NULL PRIMARY KEY, location_kind VARCHAR(32) NOT NULL, owner_id BIGINT NOT NULL, container_kind INTEGER NOT NULL, slot_index INTEGER NOT NULL, parent_item_id BIGINT NULL, updated_utc_ms BIGINT NOT NULL, FOREIGN KEY(item_id) REFERENCES item_instances(item_id) ON DELETE CASCADE, FOREIGN KEY(parent_item_id) REFERENCES item_instances(item_id) ON DELETE RESTRICT)",
                    "CREATE INDEX item_locations_ix_owner ON item_locations(location_kind, owner_id)",
                    "CREATE TABLE IF NOT EXISTS guilds (guild_id BIGINT NOT NULL PRIMARY KEY, guild_name VARCHAR(64) NOT NULL, leader_character_id BIGINT NOT NULL, gold BIGINT NOT NULL, level INTEGER NOT NULL, experience BIGINT NOT NULL, updated_utc_ms BIGINT NOT NULL)",
                    "CREATE UNIQUE INDEX guilds_uq_name ON guilds(guild_name)",
                    "CREATE TABLE IF NOT EXISTS guild_ranks (guild_id BIGINT NOT NULL, rank_index INTEGER NOT NULL, rank_name VARCHAR(64) NOT NULL, permissions BIGINT NOT NULL, updated_utc_ms BIGINT NOT NULL, PRIMARY KEY(guild_id, rank_index), FOREIGN KEY(guild_id) REFERENCES guilds(guild_id) ON DELETE CASCADE)",
                    "CREATE TABLE IF NOT EXISTS guild_members (guild_id BIGINT NOT NULL, character_id BIGINT NOT NULL, rank_index INTEGER NOT NULL, joined_utc_ms BIGINT NOT NULL, updated_utc_ms BIGINT NOT NULL, PRIMARY KEY(guild_id, character_id), FOREIGN KEY(guild_id, rank_index) REFERENCES guild_ranks(guild_id, rank_index) ON DELETE CASCADE, FOREIGN KEY(character_id) REFERENCES characters(character_id) ON DELETE RESTRICT)",
                    "CREATE TABLE IF NOT EXISTS guild_notices (guild_id BIGINT NOT NULL, notice_index INTEGER NOT NULL, notice_text TEXT NOT NULL, updated_utc_ms BIGINT NOT NULL, PRIMARY KEY(guild_id, notice_index), FOREIGN KEY(guild_id) REFERENCES guilds(guild_id) ON DELETE CASCADE)",
                    "CREATE TABLE IF NOT EXISTS guild_buffs (guild_id BIGINT NOT NULL, buff_type INTEGER NOT NULL, buff_level INTEGER NOT NULL, expiry_utc_ms BIGINT NOT NULL, updated_utc_ms BIGINT NOT NULL, PRIMARY KEY(guild_id, buff_type), FOREIGN KEY(guild_id) REFERENCES guilds(guild_id) ON DELETE CASCADE)",
                    "CREATE TABLE IF NOT EXISTS guild_storage_slots (guild_id BIGINT NOT NULL, slot_index INTEGER NOT NULL, item_id BIGINT NOT NULL UNIQUE, updated_utc_ms BIGINT NOT NULL, PRIMARY KEY(guild_id, slot_index), FOREIGN KEY(guild_id) REFERENCES guilds(guild_id) ON DELETE CASCADE, FOREIGN KEY(item_id) REFERENCES item_instances(item_id) ON DELETE RESTRICT)",
                    "CREATE TABLE IF NOT EXISTS npc_buybacks (buyback_id BIGINT NOT NULL PRIMARY KEY, npc_id BIGINT NOT NULL, character_id BIGINT NOT NULL, item_id BIGINT NOT NULL UNIQUE, price BIGINT NOT NULL, expires_utc_ms BIGINT NOT NULL, updated_utc_ms BIGINT NOT NULL, FOREIGN KEY(character_id) REFERENCES characters(character_id) ON DELETE CASCADE, FOREIGN KEY(item_id) REFERENCES item_instances(item_id) ON DELETE RESTRICT)",
                    "CREATE TABLE IF NOT EXISTS npc_used_goods (used_good_id BIGINT NOT NULL PRIMARY KEY, npc_id BIGINT NOT NULL, item_id BIGINT NOT NULL UNIQUE, price BIGINT NOT NULL, available_utc_ms BIGINT NOT NULL, updated_utc_ms BIGINT NOT NULL, FOREIGN KEY(item_id) REFERENCES item_instances(item_id) ON DELETE RESTRICT)",
                    "CREATE TABLE IF NOT EXISTS conquest_runtime (conquest_id BIGINT NOT NULL PRIMARY KEY, owner_guild_id BIGINT NOT NULL, attacker_guild_id BIGINT NOT NULL, treasury BIGINT NOT NULL, tax_rate INTEGER NOT NULL, updated_utc_ms BIGINT NOT NULL)",
                    "CREATE TABLE IF NOT EXISTS conquest_facilities (conquest_id BIGINT NOT NULL, facility_kind VARCHAR(32) NOT NULL, facility_index INTEGER NOT NULL, current_hp BIGINT NOT NULL, max_hp BIGINT NOT NULL, updated_utc_ms BIGINT NOT NULL, PRIMARY KEY(conquest_id, facility_kind, facility_index), FOREIGN KEY(conquest_id) REFERENCES conquest_runtime(conquest_id) ON DELETE CASCADE)",
                    "CREATE TABLE IF NOT EXISTS character_backups (backup_id VARCHAR(64) NOT NULL PRIMARY KEY, character_id BIGINT NOT NULL, character_name VARCHAR(32) NOT NULL, format_version INTEGER NOT NULL, canonical_json TEXT NOT NULL, sha256 VARCHAR(64) NOT NULL, created_utc_ms BIGINT NOT NULL, FOREIGN KEY(character_id) REFERENCES characters(character_id) ON DELETE CASCADE)",
                    "CREATE INDEX character_backups_ix_character ON character_backups(character_id, created_utc_ms)",
                    "CREATE TABLE IF NOT EXISTS character_buffs (character_id BIGINT NOT NULL, list_index INTEGER NOT NULL, buff_type INTEGER NOT NULL, object_id BIGINT NOT NULL, expire_time BIGINT NOT NULL, last_time BIGINT NOT NULL, next_time BIGINT NOT NULL, flag_for_removal INTEGER NOT NULL, paused INTEGER NOT NULL, updated_utc_ms BIGINT NOT NULL, PRIMARY KEY(character_id, list_index), FOREIGN KEY(character_id) REFERENCES characters(character_id) ON DELETE CASCADE)",
                    "CREATE INDEX character_buffs_ix_buff_type ON character_buffs(buff_type)",
                    "CREATE TABLE IF NOT EXISTS character_buff_stats (character_id BIGINT NOT NULL, list_index INTEGER NOT NULL, stat_id INTEGER NOT NULL, stat_value BIGINT NOT NULL, PRIMARY KEY(character_id, list_index, stat_id), FOREIGN KEY(character_id, list_index) REFERENCES character_buffs(character_id, list_index) ON DELETE CASCADE)",
                    "CREATE TABLE IF NOT EXISTS character_buff_values (character_id BIGINT NOT NULL, list_index INTEGER NOT NULL, value_index INTEGER NOT NULL, value_type VARCHAR(16) NOT NULL, integer_value BIGINT NULL, real_value DOUBLE NULL, text_value TEXT NULL, PRIMARY KEY(character_id, list_index, value_index), FOREIGN KEY(character_id, list_index) REFERENCES character_buffs(character_id, list_index) ON DELETE CASCADE)",
                    "CREATE TABLE IF NOT EXISTS character_buff_data (character_id BIGINT NOT NULL, list_index INTEGER NOT NULL, data_key VARCHAR(64) NOT NULL, data_type VARCHAR(16) NOT NULL, integer_value BIGINT NULL, real_value DOUBLE NULL, text_value TEXT NULL, PRIMARY KEY(character_id, list_index, data_key), FOREIGN KEY(character_id, list_index) REFERENCES character_buffs(character_id, list_index) ON DELETE CASCADE)",
                ]);
        }

        private static SchemaMigration CreateCharacterRuntimeDetailMigration()
        {
            return new SchemaMigration(
                20,
                "Guild and NPC runtime round-trip fields",
                [
                    "ALTER TABLE guilds ADD COLUMN spare_points INTEGER NOT NULL DEFAULT 0",
                    "ALTER TABLE guilds ADD COLUMN votes INTEGER NOT NULL DEFAULT 0",
                    "ALTER TABLE guilds ADD COLUMN last_vote_attempt_utc_ms BIGINT NOT NULL DEFAULT 0",
                    "ALTER TABLE guilds ADD COLUMN voting INTEGER NOT NULL DEFAULT 0",
                    "ALTER TABLE guilds ADD COLUMN flag_image INTEGER NOT NULL DEFAULT 1000",
                    "ALTER TABLE guilds ADD COLUMN flag_colour_argb INTEGER NOT NULL DEFAULT -1",
                    "ALTER TABLE guild_members ADD COLUMN last_login_utc_ms BIGINT NOT NULL DEFAULT 0",
                    "ALTER TABLE guild_members ADD COLUMN has_voted INTEGER NOT NULL DEFAULT 0",
                    "ALTER TABLE guild_members ADD COLUMN online INTEGER NOT NULL DEFAULT 0",
                    "ALTER TABLE guild_buffs ADD COLUMN active INTEGER NOT NULL DEFAULT 0",
                    "ALTER TABLE guild_buffs ADD COLUMN active_time_remaining INTEGER NOT NULL DEFAULT 0",
                    "ALTER TABLE guild_storage_slots ADD COLUMN user_character_id BIGINT NOT NULL DEFAULT 0",
                ]);
        }

        private static SchemaMigration CreateCharacterSnapshotGenerationMigration()
        {
            return new SchemaMigration(
                21,
                "Character Runtime snapshot lineage fields",
                [
                    "ALTER TABLE guilds ADD COLUMN snapshot_generation BIGINT NOT NULL DEFAULT 0",
                    "ALTER TABLE guilds ADD COLUMN snapshot_active INTEGER NOT NULL DEFAULT 1",
                    "ALTER TABLE guild_ranks ADD COLUMN snapshot_generation BIGINT NOT NULL DEFAULT 0",
                    "ALTER TABLE guild_ranks ADD COLUMN snapshot_active INTEGER NOT NULL DEFAULT 1",
                    "ALTER TABLE guild_members ADD COLUMN snapshot_generation BIGINT NOT NULL DEFAULT 0",
                    "ALTER TABLE guild_members ADD COLUMN snapshot_active INTEGER NOT NULL DEFAULT 1",
                    "ALTER TABLE guild_notices ADD COLUMN snapshot_generation BIGINT NOT NULL DEFAULT 0",
                    "ALTER TABLE guild_notices ADD COLUMN snapshot_active INTEGER NOT NULL DEFAULT 1",
                    "ALTER TABLE guild_buffs ADD COLUMN snapshot_generation BIGINT NOT NULL DEFAULT 0",
                    "ALTER TABLE guild_buffs ADD COLUMN snapshot_active INTEGER NOT NULL DEFAULT 1",
                    "ALTER TABLE guild_storage_slots ADD COLUMN snapshot_generation BIGINT NOT NULL DEFAULT 0",
                    "ALTER TABLE guild_storage_slots ADD COLUMN snapshot_active INTEGER NOT NULL DEFAULT 1",
                    "ALTER TABLE npc_buybacks ADD COLUMN snapshot_generation BIGINT NOT NULL DEFAULT 0",
                    "ALTER TABLE npc_buybacks ADD COLUMN snapshot_active INTEGER NOT NULL DEFAULT 1",
                    "ALTER TABLE npc_used_goods ADD COLUMN snapshot_generation BIGINT NOT NULL DEFAULT 0",
                    "ALTER TABLE npc_used_goods ADD COLUMN snapshot_active INTEGER NOT NULL DEFAULT 1",
                    "ALTER TABLE conquest_runtime ADD COLUMN snapshot_generation BIGINT NOT NULL DEFAULT 0",
                    "ALTER TABLE conquest_runtime ADD COLUMN snapshot_active INTEGER NOT NULL DEFAULT 1",
                    "ALTER TABLE conquest_facilities ADD COLUMN snapshot_generation BIGINT NOT NULL DEFAULT 0",
                    "ALTER TABLE conquest_facilities ADD COLUMN snapshot_active INTEGER NOT NULL DEFAULT 1",
                    "ALTER TABLE character_buffs ADD COLUMN snapshot_generation BIGINT NOT NULL DEFAULT 0",
                    "ALTER TABLE character_buffs ADD COLUMN snapshot_active INTEGER NOT NULL DEFAULT 1",
                ]);
        }

        private static SchemaMigration CreateWorldMetadataRepairMigration()
        {
            return new SchemaMigration(
                22,
                "World completion metadata repair",
                [
                    "CREATE TABLE IF NOT EXISTS server_meta (meta_key VARCHAR(128) NOT NULL PRIMARY KEY, meta_value TEXT NOT NULL, updated_utc_ms BIGINT NOT NULL)",
                    "CREATE TABLE IF NOT EXISTS next_ids (name VARCHAR(128) NOT NULL PRIMARY KEY, next_value BIGINT NOT NULL, updated_utc_ms BIGINT NOT NULL)",
                ]);
        }

        private static bool BelongsTo(string statement, DatabaseAuthority authority)
        {
            var match = TargetTablePattern.Match(statement ?? string.Empty);
            if (!match.Success)
                return false;

            var table = match.Groups["table"].Value;
            if (RemovedLegacyTables.Contains(table))
                return false;

            if (SharedMetadataTables.Contains(table))
                return authority is DatabaseAuthority.Character or DatabaseAuthority.World;

            if (authority == DatabaseAuthority.World)
                return WorldTables.Contains(table);

            if (authority != DatabaseAuthority.Character)
                return IdentityTables.Contains(table);

            return !IdentityTables.Contains(table) && !WorldTables.Contains(table);
        }
    }
}
