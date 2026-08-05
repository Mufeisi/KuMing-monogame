using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using Dapper;
using MySqlConnector;

namespace Server.Persistence.Sql
{
    public interface ISchemaMigrator
    {
        void ApplyPendingMigrations(IDbConnection connection, ISqlDialect dialect, string appVersion, string appCommit);
    }

    public sealed class SchemaMigrator : ISchemaMigrator
    {
        private readonly IReadOnlyList<SchemaMigration> _migrations;
        private readonly int _commandTimeoutSeconds;

        public SchemaMigrator(IReadOnlyList<SchemaMigration> migrations, int commandTimeoutSeconds = 30)
        {
            _migrations = migrations ?? throw new ArgumentNullException(nameof(migrations));
            _commandTimeoutSeconds = commandTimeoutSeconds <= 0 ? 30 : commandTimeoutSeconds;
        }

        public void ApplyPendingMigrations(IDbConnection connection, ISqlDialect dialect, string appVersion, string appCommit)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));
            if (dialect == null) throw new ArgumentNullException(nameof(dialect));

            EnsureVersionTableExists(connection, dialect);

            var currentVersion = GetCurrentVersion(connection, dialect.Provider);

            if (_migrations.Count == 0) return;

            using var tx = connection.BeginTransaction();
            try
            {
                for (var i = 0; i < _migrations.Count; i++)
                {
                    var migration = _migrations[i];
                    if (migration.Version <= currentVersion) continue;

                    ApplyMigration(connection, tx, migration, dialect.Provider);
                    RecordMigration(connection, tx, migration, dialect.Provider, appVersion, appCommit);
                }

                tx.Commit();
            }
            catch
            {
                try { tx.Rollback(); } catch { /* ignore */ }
                throw;
            }
        }

        private void EnsureVersionTableExists(IDbConnection connection, ISqlDialect dialect)
        {
            // 说明：尽量使用两种方言都支持的 DDL 语法，避免引入额外适配成本。
            var sql =
                "CREATE TABLE IF NOT EXISTS " + dialect.QuoteIdentifier("schema_version") + " (" +
                dialect.QuoteIdentifier("version") + " INTEGER NOT NULL PRIMARY KEY, " +
                dialect.QuoteIdentifier("description") + " TEXT NOT NULL, " +
                dialect.QuoteIdentifier("applied_utc_ms") + " BIGINT NOT NULL, " +
                dialect.QuoteIdentifier("app_version") + " TEXT NULL, " +
                dialect.QuoteIdentifier("app_commit") + " TEXT NULL" +
                ")";

            ExecuteWithDiagnostics(connection, dialect.Provider, sql, param: null, transaction: null);
        }

        private int GetCurrentVersion(IDbConnection connection, DatabaseProviderKind provider)
        {
            var sql = "SELECT COALESCE(MAX(version), 0) FROM schema_version";
            return ExecuteScalarWithDiagnostics<int>(connection, provider, sql, param: null, transaction: null);
        }

        private void ApplyMigration(IDbConnection connection, IDbTransaction tx, SchemaMigration migration, DatabaseProviderKind provider)
        {
            for (var i = 0; i < migration.Statements.Count; i++)
            {
                var statement = migration.Statements[i];
                if (string.IsNullOrWhiteSpace(statement)) continue;
                ExecuteWithDiagnostics(connection, provider, statement, param: null, transaction: tx);
            }
        }

        private void RecordMigration(IDbConnection connection, IDbTransaction tx, SchemaMigration migration, DatabaseProviderKind provider, string appVersion, string appCommit)
        {
            ExecuteWithDiagnostics(
                connection,
                provider,
                "INSERT INTO schema_version (version, description, applied_utc_ms, app_version, app_commit) VALUES (@Version, @Description, @AppliedUtcMs, @AppVersion, @AppCommit)",
                new
                {
                    Version = migration.Version,
                    Description = migration.Description,
                    AppliedUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    AppVersion = string.IsNullOrWhiteSpace(appVersion) ? null : appVersion.Trim(),
                    AppCommit = string.IsNullOrWhiteSpace(appCommit) ? null : appCommit.Trim(),
                },
                transaction: tx);
        }

        private void ExecuteWithDiagnostics(IDbConnection connection, DatabaseProviderKind provider, string sql, object param, IDbTransaction transaction)
        {
            sql = NormalizeCreateTableSql(provider, sql);

            var sw = Stopwatch.StartNew();
            try
            {
                connection.Execute(sql, param, transaction: transaction, commandTimeout: _commandTimeoutSeconds);
                sw.Stop();
                SqlCommandDiagnostics.Record(provider, SqlCommandKind.Execute, sql, param, sw.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                sw.Stop();

                if (provider == DatabaseProviderKind.MySql && IsIgnorableMySqlSchemaException(sql, ex))
                {
                    SqlCommandDiagnostics.Record(provider, SqlCommandKind.Execute, sql, param, sw.ElapsedMilliseconds);
                    return;
                }

                SqlCommandDiagnostics.Record(provider, SqlCommandKind.Execute, sql, param, sw.ElapsedMilliseconds, ex);
                throw;
            }
        }

        private static bool IsIgnorableMySqlSchemaException(string sql, Exception ex)
        {
            if (string.IsNullOrWhiteSpace(sql)) return false;

            var trimmed = sql.TrimStart();
            if (ex is not MySqlException mySqlException)
                return false;

            // 1061: Duplicate key name（重复创建索引，常见于 MySQL DDL 非事务导致部分成功）。
            if (trimmed.StartsWith("CREATE INDEX", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("CREATE UNIQUE INDEX", StringComparison.OrdinalIgnoreCase))
            {
                return mySqlException.Number == 1061;
            }

            // 1060: Duplicate column name（重复加列，常见于部分迁移语句已成功执行）。
            if (trimmed.StartsWith("ALTER TABLE", StringComparison.OrdinalIgnoreCase) &&
                trimmed.IndexOf(" ADD COLUMN ", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return mySqlException.Number == 1060;
            }

            return false;
        }

        private static string NormalizeCreateTableSql(DatabaseProviderKind provider, string sql)
        {
            if (provider != DatabaseProviderKind.MySql) return sql;
            if (string.IsNullOrWhiteSpace(sql)) return sql;

            var trimmed = sql.Trim();
            if (!trimmed.StartsWith("CREATE TABLE", StringComparison.OrdinalIgnoreCase))
                return sql;

            // 避免重复追加（以及保留未来可能的手写 ENGINE/CHARSET）。
            if (trimmed.IndexOf("ENGINE=", StringComparison.OrdinalIgnoreCase) >= 0)
                return sql;

            return trimmed + " ENGINE=InnoDB DEFAULT CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci";
        }

        private T ExecuteScalarWithDiagnostics<T>(IDbConnection connection, DatabaseProviderKind provider, string sql, object param, IDbTransaction transaction)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                var result = connection.ExecuteScalar<T>(sql, param, transaction: transaction, commandTimeout: _commandTimeoutSeconds);
                sw.Stop();
                SqlCommandDiagnostics.Record(provider, SqlCommandKind.Scalar, sql, param, sw.ElapsedMilliseconds);
                return result;
            }
            catch (Exception ex)
            {
                sw.Stop();
                SqlCommandDiagnostics.Record(provider, SqlCommandKind.Scalar, sql, param, sw.ElapsedMilliseconds, ex);
                throw;
            }
        }

        public static IReadOnlyList<SchemaMigration> CreateDefaultMigrations()
        {
            return new[]
            {
                new SchemaMigration(
                    version: 1,
                    description: "基础表：server_meta / next_ids",
                    statements:
                    [
                        "CREATE TABLE IF NOT EXISTS server_meta (" +
                        "meta_key VARCHAR(128) NOT NULL PRIMARY KEY, " +
                        "meta_value TEXT NOT NULL, " +
                        "updated_utc_ms BIGINT NOT NULL" +
                        ")",
                        "CREATE TABLE IF NOT EXISTS next_ids (" +
                        "name VARCHAR(128) NOT NULL PRIMARY KEY, " +
                        "next_value BIGINT NOT NULL, " +
                        "updated_utc_ms BIGINT NOT NULL" +
                        ")",
                    ]),
                new SchemaMigration(
                    version: 2,
                    description: "Legacy 快照表：legacy_blobs",
                    statements:
                    [
                        "CREATE TABLE IF NOT EXISTS legacy_blobs (" +
                        "domain VARCHAR(64) NOT NULL PRIMARY KEY, " +
                        "payload LONGBLOB NOT NULL, " +
                        "updated_utc_ms BIGINT NOT NULL" +
                        ")",
                    ]),
                new SchemaMigration(
                    version: 3,
                    description: "账号主表：accounts（仅登录/封禁/安全字段）",
                    statements:
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
                        "admin_account INTEGER NOT NULL, " +
                        "updated_utc_ms BIGINT NOT NULL" +
                        ")",
                        "CREATE UNIQUE INDEX accounts_uq_account_name ON accounts(account_name)",
                    ]),
                new SchemaMigration(
                    version: 4,
                    description: "角色主表：characters（核心字段）",
                    statements:
                    [
                        "CREATE TABLE IF NOT EXISTS characters (" +
                        "character_id BIGINT NOT NULL PRIMARY KEY, " +
                        "account_id BIGINT NOT NULL, " +
                        "character_name VARCHAR(32) NOT NULL, " +
                        "level INTEGER NOT NULL, " +
                        "class INTEGER NOT NULL, " +
                        "gender INTEGER NOT NULL, " +
                        "hair INTEGER NOT NULL, " +
                        "guild_id BIGINT NOT NULL, " +
                        "creation_ip VARCHAR(45) NOT NULL, " +
                        "creation_utc_ms BIGINT NOT NULL, " +
                        "banned INTEGER NOT NULL, " +
                        "ban_reason TEXT NOT NULL, " +
                        "expiry_utc_ms BIGINT NOT NULL, " +
                        "chat_banned INTEGER NOT NULL, " +
                        "chat_ban_expiry_utc_ms BIGINT NOT NULL, " +
                        "last_ip VARCHAR(45) NOT NULL, " +
                        "last_logout_utc_ms BIGINT NOT NULL, " +
                        "last_login_utc_ms BIGINT NOT NULL, " +
                        "deleted INTEGER NOT NULL, " +
                        "delete_utc_ms BIGINT NOT NULL, " +
                        "married_character_id BIGINT NOT NULL, " +
                        "married_utc_ms BIGINT NOT NULL, " +
                        "mentor_character_id BIGINT NOT NULL, " +
                        "mentor_utc_ms BIGINT NOT NULL, " +
                        "is_mentor INTEGER NOT NULL, " +
                        "mentor_exp BIGINT NOT NULL, " +
                        "current_map_id INTEGER NOT NULL, " +
                        "current_x INTEGER NOT NULL, " +
                        "current_y INTEGER NOT NULL, " +
                        "direction INTEGER NOT NULL, " +
                        "bind_map_id INTEGER NOT NULL, " +
                        "bind_x INTEGER NOT NULL, " +
                        "bind_y INTEGER NOT NULL, " +
                        "hp INTEGER NOT NULL, " +
                        "mp INTEGER NOT NULL, " +
                        "experience BIGINT NOT NULL, " +
                        "attack_mode INTEGER NOT NULL, " +
                        "pet_mode INTEGER NOT NULL, " +
                        "allow_group INTEGER NOT NULL, " +
                        "allow_trade INTEGER NOT NULL, " +
                        "allow_observe INTEGER NOT NULL, " +
                        "pk_points INTEGER NOT NULL, " +
                        "new_day INTEGER NOT NULL, " +
                        "updated_utc_ms BIGINT NOT NULL" +
                        ")",
                        "CREATE UNIQUE INDEX characters_uq_character_name ON characters(character_name)",
                        "CREATE INDEX characters_ix_account_id ON characters(account_id)",
                    ]),
                new SchemaMigration(
                    version: 5,
                    description: "物品实例：item_instances + 子表（stats/awake/slots）",
                    statements:
                    [
                        "CREATE TABLE IF NOT EXISTS item_instances (" +
                        "item_id BIGINT NOT NULL PRIMARY KEY, " +
                        "item_index INTEGER NOT NULL, " +
                        "current_dura INTEGER NOT NULL, " +
                        "max_dura INTEGER NOT NULL, " +
                        "stack_count INTEGER NOT NULL, " +
                        "gem_count INTEGER NOT NULL, " +
                        "soul_bound_id INTEGER NOT NULL, " +
                        "identified INTEGER NOT NULL, " +
                        "cursed INTEGER NOT NULL, " +
                        "slot_count INTEGER NOT NULL, " +
                        "awake_type INTEGER NOT NULL, " +
                        "refined_value INTEGER NOT NULL, " +
                        "refine_added INTEGER NOT NULL, " +
                        "refine_success_chance INTEGER NOT NULL, " +
                        "wedding_ring INTEGER NOT NULL, " +
                        "expire_utc_ms BIGINT NOT NULL, " +
                        "rental_owner_name TEXT NOT NULL, " +
                        "rental_binding_flags INTEGER NOT NULL, " +
                        "rental_expiry_utc_ms BIGINT NOT NULL, " +
                        "rental_locked INTEGER NOT NULL, " +
                        "is_shop_item INTEGER NOT NULL, " +
                        "sealed_expiry_utc_ms BIGINT NOT NULL, " +
                        "sealed_next_seal_utc_ms BIGINT NOT NULL, " +
                        "gm_made INTEGER NOT NULL, " +
                        "updated_utc_ms BIGINT NOT NULL" +
                        ")",
                        "CREATE INDEX item_instances_ix_item_index ON item_instances(item_index)",
                        "CREATE TABLE IF NOT EXISTS item_added_stats (" +
                        "item_id BIGINT NOT NULL, " +
                        "stat_id INTEGER NOT NULL, " +
                        "stat_value INTEGER NOT NULL, " +
                        "PRIMARY KEY(item_id, stat_id)" +
                        ")",
                        "CREATE INDEX item_added_stats_ix_item_id ON item_added_stats(item_id)",
                        "CREATE TABLE IF NOT EXISTS item_awake_levels (" +
                        "item_id BIGINT NOT NULL, " +
                        "level_index INTEGER NOT NULL, " +
                        "level_value INTEGER NOT NULL, " +
                        "PRIMARY KEY(item_id, level_index)" +
                        ")",
                        "CREATE INDEX item_awake_levels_ix_item_id ON item_awake_levels(item_id)",
                        "CREATE TABLE IF NOT EXISTS item_slot_links (" +
                        "parent_item_id BIGINT NOT NULL, " +
                        "slot_index INTEGER NOT NULL, " +
                        "child_item_id BIGINT NOT NULL, " +
                        "PRIMARY KEY(parent_item_id, slot_index)" +
                        ")",
                        "CREATE INDEX item_slot_links_ix_child_item_id ON item_slot_links(child_item_id)",
                    ]),
                new SchemaMigration(
                    version: 6,
                    description: "容器槽位：account_storage / character_containers",
                    statements:
                    [
                        "CREATE TABLE IF NOT EXISTS account_storage (" +
                        "account_id BIGINT NOT NULL PRIMARY KEY, " +
                        "slot_count INTEGER NOT NULL, " +
                        "has_expanded_storage INTEGER NOT NULL, " +
                        "expanded_storage_expiry_utc_ms BIGINT NOT NULL, " +
                        "updated_utc_ms BIGINT NOT NULL" +
                        ")",
                        "CREATE TABLE IF NOT EXISTS account_storage_slots (" +
                        "account_id BIGINT NOT NULL, " +
                        "slot_index INTEGER NOT NULL, " +
                        "item_id BIGINT NOT NULL, " +
                        "PRIMARY KEY(account_id, slot_index)" +
                        ")",
                        "CREATE INDEX account_storage_slots_ix_item_id ON account_storage_slots(item_id)",
                        "CREATE TABLE IF NOT EXISTS character_containers (" +
                        "character_id BIGINT NOT NULL, " +
                        "container_kind INTEGER NOT NULL, " +
                        "slot_count INTEGER NOT NULL, " +
                        "updated_utc_ms BIGINT NOT NULL, " +
                        "PRIMARY KEY(character_id, container_kind)" +
                        ")",
                        "CREATE TABLE IF NOT EXISTS character_container_slots (" +
                        "character_id BIGINT NOT NULL, " +
                        "container_kind INTEGER NOT NULL, " +
                        "slot_index INTEGER NOT NULL, " +
                        "item_id BIGINT NOT NULL, " +
                        "PRIMARY KEY(character_id, container_kind, slot_index)" +
                        ")",
                        "CREATE INDEX character_container_slots_ix_item_id ON character_container_slots(item_id)",
                    ]),
                new SchemaMigration(
                    version: 7,
                    description: "Legacy 文件表：legacy_files（用于 Guilds/Archive 等目录落库）",
                    statements:
                    [
                        "CREATE TABLE IF NOT EXISTS legacy_files (" +
                        "domain VARCHAR(64) NOT NULL, " +
                        "relative_path VARCHAR(512) NOT NULL, " +
                        "payload LONGBLOB NOT NULL, " +
                        "updated_utc_ms BIGINT NOT NULL, " +
                        "PRIMARY KEY(domain, relative_path)" +
                        ")",
                    ]),
                new SchemaMigration(
                    version: 8,
                    description: "Accounts 扩展域：拍卖/邮件/商城日志/刷怪保存",
                    statements:
                    [
                        "CREATE TABLE IF NOT EXISTS auctions (" +
                        "auction_id BIGINT NOT NULL PRIMARY KEY, " +
                        "item_id BIGINT NOT NULL, " +
                        "consignment_utc_ms BIGINT NOT NULL, " +
                        "price BIGINT NOT NULL, " +
                        "current_bid BIGINT NOT NULL, " +
                        "seller_character_id BIGINT NOT NULL, " +
                        "current_buyer_character_id BIGINT NOT NULL, " +
                        "expired INTEGER NOT NULL, " +
                        "sold INTEGER NOT NULL, " +
                        "item_type INTEGER NOT NULL, " +
                        "updated_utc_ms BIGINT NOT NULL" +
                        ")",
                        "CREATE INDEX auctions_ix_item_id ON auctions(item_id)",
                        "CREATE INDEX auctions_ix_seller_character_id ON auctions(seller_character_id)",
                        "CREATE INDEX auctions_ix_current_buyer_character_id ON auctions(current_buyer_character_id)",
                        "CREATE TABLE IF NOT EXISTS mails (" +
                        "mail_id BIGINT NOT NULL PRIMARY KEY, " +
                        "sender_name TEXT NOT NULL, " +
                        "recipient_character_id BIGINT NOT NULL, " +
                        "message TEXT NOT NULL, " +
                        "gold BIGINT NOT NULL, " +
                        "date_sent_utc_ms BIGINT NOT NULL, " +
                        "date_opened_utc_ms BIGINT NOT NULL, " +
                        "locked INTEGER NOT NULL, " +
                        "collected INTEGER NOT NULL, " +
                        "can_reply INTEGER NOT NULL, " +
                        "updated_utc_ms BIGINT NOT NULL" +
                        ")",
                        "CREATE INDEX mails_ix_recipient_character_id ON mails(recipient_character_id)",
                        "CREATE TABLE IF NOT EXISTS mail_items (" +
                        "mail_id BIGINT NOT NULL, " +
                        "slot_index INTEGER NOT NULL, " +
                        "item_id BIGINT NOT NULL, " +
                        "PRIMARY KEY(mail_id, slot_index)" +
                        ")",
                        "CREATE INDEX mail_items_ix_item_id ON mail_items(item_id)",
                        "CREATE TABLE IF NOT EXISTS gameshop_log (" +
                        "item_index INTEGER NOT NULL PRIMARY KEY, " +
                        "count INTEGER NOT NULL, " +
                        "updated_utc_ms BIGINT NOT NULL" +
                        ")",
                        "CREATE TABLE IF NOT EXISTS respawn_saves (" +
                         "respawn_index INTEGER NOT NULL PRIMARY KEY, " +
                         "next_spawn_tick BIGINT NOT NULL, " +
                         "spawned INTEGER NOT NULL, " +
                         "updated_utc_ms BIGINT NOT NULL" +
                         ")",
                     ]),
                new SchemaMigration(
                    version: 9,
                    description: "索引优化：常用查询字段",
                    statements:
                    [
                        "CREATE INDEX accounts_ix_last_utc_ms ON accounts(last_utc_ms)",
                        "CREATE INDEX accounts_ix_expiry_utc_ms ON accounts(expiry_utc_ms)",
                        "CREATE INDEX characters_ix_guild_id ON characters(guild_id)",
                        "CREATE INDEX characters_ix_last_login_utc_ms ON characters(last_login_utc_ms)",
                        "CREATE INDEX mails_ix_recipient_collected ON mails(recipient_character_id, collected)",
                        "CREATE INDEX auctions_ix_expired_sold ON auctions(expired, sold)",
                    ]),
                new SchemaMigration(
                    version: 10,
                    description: "写入优化：子表增加 updated_utc_ms（用于 upsert 后清理）",
                    statements:
                    [
                        "ALTER TABLE item_added_stats ADD COLUMN updated_utc_ms BIGINT NOT NULL DEFAULT 0",
                        "ALTER TABLE item_awake_levels ADD COLUMN updated_utc_ms BIGINT NOT NULL DEFAULT 0",
                        "ALTER TABLE item_slot_links ADD COLUMN updated_utc_ms BIGINT NOT NULL DEFAULT 0",
                        "ALTER TABLE account_storage_slots ADD COLUMN updated_utc_ms BIGINT NOT NULL DEFAULT 0",
                        "ALTER TABLE character_container_slots ADD COLUMN updated_utc_ms BIGINT NOT NULL DEFAULT 0",
                        "ALTER TABLE mail_items ADD COLUMN updated_utc_ms BIGINT NOT NULL DEFAULT 0",
                    ]),
                new SchemaMigration(
                    version: 11,
                    description: "World 关系表：Maps/Items/Monsters/NPC/Quests/Magics/Dragon/Conquests/RespawnTimer",
                    statements:
                    [
                        "CREATE TABLE IF NOT EXISTS map_infos (" +
                        "map_id BIGINT NOT NULL PRIMARY KEY, " +
                        "file_name VARCHAR(64) NOT NULL, " +
                        "title TEXT NOT NULL, " +
                        "mini_map INTEGER NOT NULL, " +
                        "big_map INTEGER NOT NULL, " +
                        "music INTEGER NOT NULL, " +
                        "light INTEGER NOT NULL, " +
                        "map_dark_light INTEGER NOT NULL, " +
                        "mine_index INTEGER NOT NULL, " +
                        "no_teleport INTEGER NOT NULL, " +
                        "no_reconnect INTEGER NOT NULL, " +
                        "no_reconnect_map TEXT NOT NULL, " +
                        "no_random INTEGER NOT NULL, " +
                        "no_escape INTEGER NOT NULL, " +
                        "no_recall INTEGER NOT NULL, " +
                        "no_drug INTEGER NOT NULL, " +
                        "no_position INTEGER NOT NULL, " +
                        "no_throw_item INTEGER NOT NULL, " +
                        "no_drop_player INTEGER NOT NULL, " +
                        "no_drop_monster INTEGER NOT NULL, " +
                        "no_names INTEGER NOT NULL, " +
                        "fight INTEGER NOT NULL, " +
                        "fire INTEGER NOT NULL, " +
                        "fire_damage INTEGER NOT NULL, " +
                        "lightning INTEGER NOT NULL, " +
                        "lightning_damage INTEGER NOT NULL, " +
                        "no_mount INTEGER NOT NULL, " +
                        "need_bridle INTEGER NOT NULL, " +
                        "no_fight INTEGER NOT NULL, " +
                        "no_town_teleport INTEGER NOT NULL, " +
                        "no_reincarnation INTEGER NOT NULL, " +
                        "weather_particles INTEGER NOT NULL, " +
                        "updated_utc_ms BIGINT NOT NULL" +
                        ")",
                        "CREATE UNIQUE INDEX map_infos_uq_file_name ON map_infos(file_name)",
                        "CREATE TABLE IF NOT EXISTS map_safe_zones (" +
                        "map_id BIGINT NOT NULL, " +
                        "slot_index INTEGER NOT NULL, " +
                        "x INTEGER NOT NULL, " +
                        "y INTEGER NOT NULL, " +
                        "zone_size INTEGER NOT NULL, " +
                        "start_point INTEGER NOT NULL, " +
                        "updated_utc_ms BIGINT NOT NULL, " +
                        "PRIMARY KEY(map_id, slot_index)" +
                        ")",
                        "CREATE INDEX map_safe_zones_ix_map_id ON map_safe_zones(map_id)",
                        "CREATE TABLE IF NOT EXISTS map_respawns (" +
                        "respawn_index INTEGER NOT NULL PRIMARY KEY, " +
                        "map_id BIGINT NOT NULL, " +
                        "monster_index INTEGER NOT NULL, " +
                        "x INTEGER NOT NULL, " +
                        "y INTEGER NOT NULL, " +
                        "spawn_count INTEGER NOT NULL, " +
                        "spread INTEGER NOT NULL, " +
                        "delay INTEGER NOT NULL, " +
                        "random_delay INTEGER NOT NULL, " +
                        "direction INTEGER NOT NULL, " +
                        "route_path TEXT NOT NULL, " +
                        "save_respawn_time INTEGER NOT NULL, " +
                        "respawn_ticks INTEGER NOT NULL, " +
                        "updated_utc_ms BIGINT NOT NULL" +
                        ")",
                        "CREATE INDEX map_respawns_ix_map_id ON map_respawns(map_id)",
                        "CREATE INDEX map_respawns_ix_monster_index ON map_respawns(monster_index)",
                        "CREATE TABLE IF NOT EXISTS map_movements (" +
                        "map_id BIGINT NOT NULL, " +
                        "slot_index INTEGER NOT NULL, " +
                        "destination_map_id INTEGER NOT NULL, " +
                        "src_x INTEGER NOT NULL, " +
                        "src_y INTEGER NOT NULL, " +
                        "dst_x INTEGER NOT NULL, " +
                        "dst_y INTEGER NOT NULL, " +
                        "need_hole INTEGER NOT NULL, " +
                        "need_move INTEGER NOT NULL, " +
                        "conquest_index INTEGER NOT NULL, " +
                        "show_on_big_map INTEGER NOT NULL, " +
                        "icon INTEGER NOT NULL, " +
                        "updated_utc_ms BIGINT NOT NULL, " +
                        "PRIMARY KEY(map_id, slot_index)" +
                        ")",
                        "CREATE INDEX map_movements_ix_map_id ON map_movements(map_id)",
                        "CREATE TABLE IF NOT EXISTS map_mine_zones (" +
                        "map_id BIGINT NOT NULL, " +
                        "slot_index INTEGER NOT NULL, " +
                        "x INTEGER NOT NULL, " +
                        "y INTEGER NOT NULL, " +
                        "zone_size INTEGER NOT NULL, " +
                        "mine INTEGER NOT NULL, " +
                        "updated_utc_ms BIGINT NOT NULL, " +
                        "PRIMARY KEY(map_id, slot_index)" +
                        ")",
                        "CREATE INDEX map_mine_zones_ix_map_id ON map_mine_zones(map_id)",
                        "CREATE TABLE IF NOT EXISTS item_infos (" +
                        "item_id BIGINT NOT NULL PRIMARY KEY, " +
                        "name TEXT NOT NULL, " +
                        "item_type INTEGER NOT NULL, " +
                        "grade INTEGER NOT NULL, " +
                        "required_type INTEGER NOT NULL, " +
                        "required_class INTEGER NOT NULL, " +
                        "required_gender INTEGER NOT NULL, " +
                        "item_set INTEGER NOT NULL, " +
                        "shape INTEGER NOT NULL, " +
                        "weight INTEGER NOT NULL, " +
                        "light INTEGER NOT NULL, " +
                        "required_amount INTEGER NOT NULL, " +
                        "image INTEGER NOT NULL, " +
                        "durability INTEGER NOT NULL, " +
                        "price BIGINT NOT NULL, " +
                        "stack_size INTEGER NOT NULL, " +
                        "start_item INTEGER NOT NULL, " +
                        "effect INTEGER NOT NULL, " +
                        "need_identify INTEGER NOT NULL, " +
                        "show_group_pickup INTEGER NOT NULL, " +
                        "global_drop_notify INTEGER NOT NULL, " +
                        "class_based INTEGER NOT NULL, " +
                        "level_based INTEGER NOT NULL, " +
                        "can_mine INTEGER NOT NULL, " +
                        "bind INTEGER NOT NULL, " +
                        "unique_mode INTEGER NOT NULL, " +
                        "random_stats_id INTEGER NOT NULL, " +
                        "can_fast_run INTEGER NOT NULL, " +
                        "can_awakening INTEGER NOT NULL, " +
                        "slots INTEGER NOT NULL, " +
                        "tool_tip TEXT NULL, " +
                        "updated_utc_ms BIGINT NOT NULL" +
                        ")",
                        "CREATE TABLE IF NOT EXISTS item_info_stats (" +
                        "item_id BIGINT NOT NULL, " +
                        "stat INTEGER NOT NULL, " +
                        "stat_value INTEGER NOT NULL, " +
                        "updated_utc_ms BIGINT NOT NULL, " +
                        "PRIMARY KEY(item_id, stat)" +
                        ")",
                        "CREATE INDEX item_info_stats_ix_stat ON item_info_stats(stat)",
                        "CREATE TABLE IF NOT EXISTS monster_infos (" +
                        "monster_id BIGINT NOT NULL PRIMARY KEY, " +
                        "name TEXT NOT NULL, " +
                        "image INTEGER NOT NULL, " +
                        "ai INTEGER NOT NULL, " +
                        "effect INTEGER NOT NULL, " +
                        "level INTEGER NOT NULL, " +
                        "view_range INTEGER NOT NULL, " +
                        "cool_eye INTEGER NOT NULL, " +
                        "light INTEGER NOT NULL, " +
                        "attack_speed INTEGER NOT NULL, " +
                        "move_speed INTEGER NOT NULL, " +
                        "experience BIGINT NOT NULL, " +
                        "can_tame INTEGER NOT NULL, " +
                        "can_push INTEGER NOT NULL, " +
                        "auto_rev INTEGER NOT NULL, " +
                        "undead INTEGER NOT NULL, " +
                        "drop_path TEXT NOT NULL, " +
                        "updated_utc_ms BIGINT NOT NULL" +
                        ")",
                        "CREATE TABLE IF NOT EXISTS monster_info_stats (" +
                        "monster_id BIGINT NOT NULL, " +
                        "stat INTEGER NOT NULL, " +
                        "stat_value INTEGER NOT NULL, " +
                        "updated_utc_ms BIGINT NOT NULL, " +
                        "PRIMARY KEY(monster_id, stat)" +
                        ")",
                        "CREATE TABLE IF NOT EXISTS npc_infos (" +
                        "npc_id BIGINT NOT NULL PRIMARY KEY, " +
                        "map_id BIGINT NOT NULL, " +
                        "file_name TEXT NOT NULL, " +
                        "name TEXT NOT NULL, " +
                        "x INTEGER NOT NULL, " +
                        "y INTEGER NOT NULL, " +
                        "rate INTEGER NOT NULL, " +
                        "image INTEGER NOT NULL, " +
                        "time_visible INTEGER NOT NULL, " +
                        "hour_start INTEGER NOT NULL, " +
                        "minute_start INTEGER NOT NULL, " +
                        "hour_end INTEGER NOT NULL, " +
                        "minute_end INTEGER NOT NULL, " +
                        "min_lev INTEGER NOT NULL, " +
                        "max_lev INTEGER NOT NULL, " +
                        "day_of_week TEXT NOT NULL, " +
                        "class_required TEXT NOT NULL, " +
                        "conquest INTEGER NOT NULL, " +
                        "flag_needed INTEGER NOT NULL, " +
                        "show_on_big_map INTEGER NOT NULL, " +
                        "big_map_icon INTEGER NOT NULL, " +
                        "can_teleport_to INTEGER NOT NULL, " +
                        "conquest_visible INTEGER NOT NULL, " +
                        "updated_utc_ms BIGINT NOT NULL" +
                        ")",
                        "CREATE INDEX npc_infos_ix_map_id ON npc_infos(map_id)",
                        "CREATE TABLE IF NOT EXISTS npc_collect_quests (" +
                        "npc_id BIGINT NOT NULL, " +
                        "slot_index INTEGER NOT NULL, " +
                        "quest_id BIGINT NOT NULL, " +
                        "updated_utc_ms BIGINT NOT NULL, " +
                        "PRIMARY KEY(npc_id, slot_index)" +
                        ")",
                        "CREATE INDEX npc_collect_quests_ix_quest_id ON npc_collect_quests(quest_id)",
                        "CREATE TABLE IF NOT EXISTS npc_finish_quests (" +
                        "npc_id BIGINT NOT NULL, " +
                        "slot_index INTEGER NOT NULL, " +
                        "quest_id BIGINT NOT NULL, " +
                        "updated_utc_ms BIGINT NOT NULL, " +
                        "PRIMARY KEY(npc_id, slot_index)" +
                        ")",
                        "CREATE INDEX npc_finish_quests_ix_quest_id ON npc_finish_quests(quest_id)",
                        "CREATE TABLE IF NOT EXISTS quest_infos (" +
                        "quest_id BIGINT NOT NULL PRIMARY KEY, " +
                        "name TEXT NOT NULL, " +
                        "quest_group TEXT NOT NULL, " +
                        "file_name VARCHAR(128) NOT NULL, " +
                        "required_min_level INTEGER NOT NULL, " +
                        "required_max_level INTEGER NOT NULL, " +
                        "required_quest INTEGER NOT NULL, " +
                        "required_class INTEGER NOT NULL, " +
                        "quest_type INTEGER NOT NULL, " +
                        "goto_message TEXT NOT NULL, " +
                        "kill_message TEXT NOT NULL, " +
                        "item_message TEXT NOT NULL, " +
                        "flag_message TEXT NOT NULL, " +
                        "time_limit_seconds INTEGER NOT NULL, " +
                        "updated_utc_ms BIGINT NOT NULL" +
                        ")",
                        "CREATE UNIQUE INDEX quest_infos_uq_file_name ON quest_infos(file_name)",
                        "CREATE TABLE IF NOT EXISTS magic_infos (" +
                        "spell INTEGER NOT NULL PRIMARY KEY, " +
                        "name TEXT NOT NULL, " +
                        "base_cost INTEGER NOT NULL, " +
                        "level_cost INTEGER NOT NULL, " +
                        "icon INTEGER NOT NULL, " +
                        "level1 INTEGER NOT NULL, " +
                        "level2 INTEGER NOT NULL, " +
                        "level3 INTEGER NOT NULL, " +
                        "need1 INTEGER NOT NULL, " +
                        "need2 INTEGER NOT NULL, " +
                        "need3 INTEGER NOT NULL, " +
                        "delay_base BIGINT NOT NULL, " +
                        "delay_reduction BIGINT NOT NULL, " +
                        "power_base INTEGER NOT NULL, " +
                        "power_bonus INTEGER NOT NULL, " +
                        "mpower_base INTEGER NOT NULL, " +
                        "mpower_bonus INTEGER NOT NULL, " +
                        "magic_range INTEGER NOT NULL, " +
                        "multiplier_base DOUBLE NOT NULL, " +
                        "multiplier_bonus DOUBLE NOT NULL, " +
                        "updated_utc_ms BIGINT NOT NULL" +
                        ")",
                        "CREATE TABLE IF NOT EXISTS gameshop_items (" +
                        "gameshop_item_id BIGINT NOT NULL PRIMARY KEY, " +
                        "item_id BIGINT NOT NULL, " +
                        "gold_price BIGINT NOT NULL, " +
                        "credit_price BIGINT NOT NULL, " +
                        "count INTEGER NOT NULL, " +
                        "class_mask TEXT NOT NULL, " +
                        "category TEXT NOT NULL, " +
                        "stock INTEGER NOT NULL, " +
                        "i_stock INTEGER NOT NULL, " +
                        "deal INTEGER NOT NULL, " +
                        "top_item INTEGER NOT NULL, " +
                        "date_binary BIGINT NOT NULL, " +
                        "can_buy_gold INTEGER NOT NULL, " +
                        "can_buy_credit INTEGER NOT NULL, " +
                        "updated_utc_ms BIGINT NOT NULL" +
                        ")",
                        "CREATE INDEX gameshop_items_ix_item_id ON gameshop_items(item_id)",
                        "CREATE TABLE IF NOT EXISTS dragon_info (" +
                        "dragon_id INTEGER NOT NULL PRIMARY KEY, " +
                        "enabled INTEGER NOT NULL, " +
                        "map_file_name VARCHAR(64) NOT NULL, " +
                        "monster_name TEXT NOT NULL, " +
                        "body_name TEXT NOT NULL, " +
                        "location_x INTEGER NOT NULL, " +
                        "location_y INTEGER NOT NULL, " +
                        "drop_area_top_x INTEGER NOT NULL, " +
                        "drop_area_top_y INTEGER NOT NULL, " +
                        "drop_area_bottom_x INTEGER NOT NULL, " +
                        "drop_area_bottom_y INTEGER NOT NULL, " +
                        "updated_utc_ms BIGINT NOT NULL" +
                        ")",
                        "CREATE TABLE IF NOT EXISTS dragon_exps (" +
                        "dragon_id INTEGER NOT NULL, " +
                        "level INTEGER NOT NULL, " +
                        "exp BIGINT NOT NULL, " +
                        "updated_utc_ms BIGINT NOT NULL, " +
                        "PRIMARY KEY(dragon_id, level)" +
                        ")",
                        "CREATE TABLE IF NOT EXISTS conquests (" +
                        "conquest_id BIGINT NOT NULL PRIMARY KEY, " +
                        "full_map INTEGER NOT NULL, " +
                        "location_x INTEGER NOT NULL, " +
                        "location_y INTEGER NOT NULL, " +
                        "size INTEGER NOT NULL, " +
                        "name TEXT NOT NULL, " +
                        "map_id BIGINT NOT NULL, " +
                        "palace_id BIGINT NOT NULL, " +
                        "guard_index INTEGER NOT NULL, " +
                        "gate_index INTEGER NOT NULL, " +
                        "wall_index INTEGER NOT NULL, " +
                        "siege_index INTEGER NOT NULL, " +
                        "flag_index INTEGER NOT NULL, " +
                        "start_hour INTEGER NOT NULL, " +
                        "war_length INTEGER NOT NULL, " +
                        "conquest_type INTEGER NOT NULL, " +
                        "conquest_game INTEGER NOT NULL, " +
                        "monday INTEGER NOT NULL, " +
                        "tuesday INTEGER NOT NULL, " +
                        "wednesday INTEGER NOT NULL, " +
                        "thursday INTEGER NOT NULL, " +
                        "friday INTEGER NOT NULL, " +
                        "saturday INTEGER NOT NULL, " +
                        "sunday INTEGER NOT NULL, " +
                        "king_location_x INTEGER NOT NULL, " +
                        "king_location_y INTEGER NOT NULL, " +
                        "king_size INTEGER NOT NULL, " +
                        "control_point_index INTEGER NOT NULL, " +
                        "updated_utc_ms BIGINT NOT NULL" +
                        ")",
                        "CREATE TABLE IF NOT EXISTS conquest_extra_maps (" +
                        "conquest_id BIGINT NOT NULL, " +
                        "slot_index INTEGER NOT NULL, " +
                        "map_id BIGINT NOT NULL, " +
                        "updated_utc_ms BIGINT NOT NULL, " +
                        "PRIMARY KEY(conquest_id, slot_index)" +
                        ")",
                        "CREATE TABLE IF NOT EXISTS conquest_guards (" +
                        "conquest_id BIGINT NOT NULL, " +
                        "guard_id INTEGER NOT NULL, " +
                        "location_x INTEGER NOT NULL, " +
                        "location_y INTEGER NOT NULL, " +
                        "mob_index INTEGER NOT NULL, " +
                        "name TEXT NOT NULL, " +
                        "repair_cost BIGINT NOT NULL, " +
                        "updated_utc_ms BIGINT NOT NULL, " +
                        "PRIMARY KEY(conquest_id, guard_id)" +
                        ")",
                        "CREATE TABLE IF NOT EXISTS conquest_gates (" +
                        "conquest_id BIGINT NOT NULL, " +
                        "gate_id INTEGER NOT NULL, " +
                        "location_x INTEGER NOT NULL, " +
                        "location_y INTEGER NOT NULL, " +
                        "mob_index INTEGER NOT NULL, " +
                        "name TEXT NOT NULL, " +
                        "repair_cost BIGINT NOT NULL, " +
                        "updated_utc_ms BIGINT NOT NULL, " +
                        "PRIMARY KEY(conquest_id, gate_id)" +
                        ")",
                        "CREATE TABLE IF NOT EXISTS conquest_walls (" +
                        "conquest_id BIGINT NOT NULL, " +
                        "wall_id INTEGER NOT NULL, " +
                        "location_x INTEGER NOT NULL, " +
                        "location_y INTEGER NOT NULL, " +
                        "mob_index INTEGER NOT NULL, " +
                        "name TEXT NOT NULL, " +
                        "repair_cost BIGINT NOT NULL, " +
                        "updated_utc_ms BIGINT NOT NULL, " +
                        "PRIMARY KEY(conquest_id, wall_id)" +
                        ")",
                        "CREATE TABLE IF NOT EXISTS conquest_sieges (" +
                        "conquest_id BIGINT NOT NULL, " +
                        "siege_id INTEGER NOT NULL, " +
                        "location_x INTEGER NOT NULL, " +
                        "location_y INTEGER NOT NULL, " +
                        "mob_index INTEGER NOT NULL, " +
                        "name TEXT NOT NULL, " +
                        "repair_cost BIGINT NOT NULL, " +
                        "updated_utc_ms BIGINT NOT NULL, " +
                        "PRIMARY KEY(conquest_id, siege_id)" +
                        ")",
                        "CREATE TABLE IF NOT EXISTS conquest_flags (" +
                        "conquest_id BIGINT NOT NULL, " +
                        "flag_id INTEGER NOT NULL, " +
                        "location_x INTEGER NOT NULL, " +
                        "location_y INTEGER NOT NULL, " +
                        "name TEXT NOT NULL, " +
                        "file_name TEXT NOT NULL, " +
                        "updated_utc_ms BIGINT NOT NULL, " +
                        "PRIMARY KEY(conquest_id, flag_id)" +
                        ")",
                        "CREATE TABLE IF NOT EXISTS conquest_control_points (" +
                        "conquest_id BIGINT NOT NULL, " +
                        "control_point_id INTEGER NOT NULL, " +
                        "location_x INTEGER NOT NULL, " +
                        "location_y INTEGER NOT NULL, " +
                        "name TEXT NOT NULL, " +
                        "file_name TEXT NOT NULL, " +
                        "updated_utc_ms BIGINT NOT NULL, " +
                        "PRIMARY KEY(conquest_id, control_point_id)" +
                        ")",
                        "CREATE TABLE IF NOT EXISTS respawn_timer_state (" +
                        "timer_id INTEGER NOT NULL PRIMARY KEY, " +
                        "base_spawn_rate INTEGER NOT NULL, " +
                        "current_tick_counter BIGINT NOT NULL, " +
                        "updated_utc_ms BIGINT NOT NULL" +
                        ")",
                        "CREATE TABLE IF NOT EXISTS respawn_tick_options (" +
                        "timer_id INTEGER NOT NULL, " +
                        "slot_index INTEGER NOT NULL, " +
                        "user_count INTEGER NOT NULL, " +
                        "delay_loss DOUBLE NOT NULL, " +
                        "updated_utc_ms BIGINT NOT NULL, " +
                        "PRIMARY KEY(timer_id, slot_index)" +
                        ")",
                    ]),
                new SchemaMigration(
                    version: 12,
                    description: "Accounts 扩展域：角色技能/已完成任务/角色标记/商城购买计数",
                    statements:
                    [
                        "CREATE TABLE IF NOT EXISTS character_magics (" +
                        "character_id BIGINT NOT NULL, " +
                        "spell INTEGER NOT NULL, " +
                        "magic_level INTEGER NOT NULL, " +
                        "magic_key INTEGER NOT NULL, " +
                        "experience INTEGER NOT NULL, " +
                        "is_temp_spell INTEGER NOT NULL, " +
                        "cast_time BIGINT NOT NULL, " +
                        "updated_utc_ms BIGINT NOT NULL, " +
                        "PRIMARY KEY(character_id, spell)" +
                        ")",
                        "CREATE INDEX character_magics_ix_spell ON character_magics(spell)",
                        "CREATE TABLE IF NOT EXISTS character_completed_quests (" +
                        "character_id BIGINT NOT NULL, " +
                        "quest_id BIGINT NOT NULL, " +
                        "updated_utc_ms BIGINT NOT NULL, " +
                        "PRIMARY KEY(character_id, quest_id)" +
                        ")",
                        "CREATE INDEX character_completed_quests_ix_quest_id ON character_completed_quests(quest_id)",
                        "CREATE TABLE IF NOT EXISTS character_flags (" +
                        "character_id BIGINT NOT NULL, " +
                        "flag_index INTEGER NOT NULL, " +
                        "flag_value INTEGER NOT NULL, " +
                        "updated_utc_ms BIGINT NOT NULL, " +
                        "PRIMARY KEY(character_id, flag_index)" +
                        ")",
                        "CREATE INDEX character_flags_ix_flag_index ON character_flags(flag_index)",
                        "CREATE TABLE IF NOT EXISTS character_gameshop_purchases (" +
                        "character_id BIGINT NOT NULL, " +
                        "item_index INTEGER NOT NULL, " +
                        "purchase_count INTEGER NOT NULL, " +
                        "updated_utc_ms BIGINT NOT NULL, " +
                        "PRIMARY KEY(character_id, item_index)" +
                        ")",
                        "CREATE INDEX character_gameshop_purchases_ix_item_index ON character_gameshop_purchases(item_index)",
                    ]),
                new SchemaMigration(
                    version: 13,
                    description: "Accounts 扩展域：进行中的任务与子任务进度",
                    statements:
                    [
                        "CREATE TABLE IF NOT EXISTS character_current_quests (" +
                        "character_id BIGINT NOT NULL, " +
                        "slot_index INTEGER NOT NULL, " +
                        "quest_id BIGINT NOT NULL, " +
                        "start_utc_ms BIGINT NOT NULL, " +
                        "end_utc_ms BIGINT NOT NULL, " +
                        "updated_utc_ms BIGINT NOT NULL, " +
                        "PRIMARY KEY(character_id, slot_index)" +
                        ")",
                        "CREATE UNIQUE INDEX character_current_quests_uq_character_quest ON character_current_quests(character_id, quest_id)",
                        "CREATE TABLE IF NOT EXISTS character_current_quest_kill_tasks (" +
                        "character_id BIGINT NOT NULL, " +
                        "quest_id BIGINT NOT NULL, " +
                        "monster_id INTEGER NOT NULL, " +
                        "task_count INTEGER NOT NULL, " +
                        "updated_utc_ms BIGINT NOT NULL, " +
                        "PRIMARY KEY(character_id, quest_id, monster_id)" +
                        ")",
                        "CREATE TABLE IF NOT EXISTS character_current_quest_item_tasks (" +
                        "character_id BIGINT NOT NULL, " +
                        "quest_id BIGINT NOT NULL, " +
                        "item_id INTEGER NOT NULL, " +
                        "task_count INTEGER NOT NULL, " +
                        "updated_utc_ms BIGINT NOT NULL, " +
                        "PRIMARY KEY(character_id, quest_id, item_id)" +
                        ")",
                        "CREATE TABLE IF NOT EXISTS character_current_quest_flag_tasks (" +
                        "character_id BIGINT NOT NULL, " +
                        "quest_id BIGINT NOT NULL, " +
                        "flag_number INTEGER NOT NULL, " +
                        "flag_state INTEGER NOT NULL, " +
                        "updated_utc_ms BIGINT NOT NULL, " +
                        "PRIMARY KEY(character_id, quest_id, flag_number)" +
                        ")",
                    ]),
                new SchemaMigration(
                    version: 14,
                    description: "Accounts 扩展域：宠物/好友/租赁/灵物/英雄专属字段",
                    statements:
                    [
                        "CREATE TABLE IF NOT EXISTS character_pets (" +
                        "character_id BIGINT NOT NULL, " +
                        "list_index INTEGER NOT NULL, " +
                        "monster_id INTEGER NOT NULL, " +
                        "hp INTEGER NOT NULL, " +
                        "experience BIGINT NOT NULL, " +
                        "pet_level INTEGER NOT NULL, " +
                        "max_pet_level INTEGER NOT NULL, " +
                        "updated_utc_ms BIGINT NOT NULL, " +
                        "PRIMARY KEY(character_id, list_index)" +
                        ")",
                        "CREATE TABLE IF NOT EXISTS character_friends (" +
                        "character_id BIGINT NOT NULL, " +
                        "list_index INTEGER NOT NULL, " +
                        "friend_character_id BIGINT NOT NULL, " +
                        "blocked INTEGER NOT NULL, " +
                        "memo TEXT NOT NULL, " +
                        "updated_utc_ms BIGINT NOT NULL, " +
                        "PRIMARY KEY(character_id, list_index)" +
                        ")",
                        "CREATE TABLE IF NOT EXISTS character_rented_items (" +
                        "character_id BIGINT NOT NULL, " +
                        "list_index INTEGER NOT NULL, " +
                        "item_id BIGINT NOT NULL, " +
                        "item_name TEXT NOT NULL, " +
                        "renting_player_name TEXT NOT NULL, " +
                        "item_return_utc_ms BIGINT NOT NULL, " +
                        "updated_utc_ms BIGINT NOT NULL, " +
                        "PRIMARY KEY(character_id, list_index)" +
                        ")",
                        "CREATE TABLE IF NOT EXISTS character_intelligent_creatures (" +
                        "character_id BIGINT NOT NULL, " +
                        "slot_index INTEGER NOT NULL, " +
                        "pet_type INTEGER NOT NULL, " +
                        "custom_name TEXT NOT NULL, " +
                        "fullness INTEGER NOT NULL, " +
                        "expire_utc_ms BIGINT NOT NULL, " +
                        "blackstone_time BIGINT NOT NULL, " +
                        "pickup_mode INTEGER NOT NULL, " +
                        "filter_pickup_all INTEGER NOT NULL, " +
                        "filter_pickup_gold INTEGER NOT NULL, " +
                        "filter_pickup_weapons INTEGER NOT NULL, " +
                        "filter_pickup_armours INTEGER NOT NULL, " +
                        "filter_pickup_helmets INTEGER NOT NULL, " +
                        "filter_pickup_boots INTEGER NOT NULL, " +
                        "filter_pickup_belts INTEGER NOT NULL, " +
                        "filter_pickup_accessories INTEGER NOT NULL, " +
                        "filter_pickup_others INTEGER NOT NULL, " +
                        "filter_pickup_grade INTEGER NOT NULL, " +
                        "maintain_food_time BIGINT NOT NULL, " +
                        "updated_utc_ms BIGINT NOT NULL, " +
                        "PRIMARY KEY(character_id, slot_index)" +
                        ")",
                        "CREATE TABLE IF NOT EXISTS hero_details (" +
                        "character_id BIGINT NOT NULL PRIMARY KEY, " +
                        "auto_pot INTEGER NOT NULL, " +
                        "grade INTEGER NOT NULL, " +
                        "hp_item_index INTEGER NOT NULL, " +
                        "mp_item_index INTEGER NOT NULL, " +
                        "auto_hp_percent INTEGER NOT NULL, " +
                        "auto_mp_percent INTEGER NOT NULL, " +
                        "seal_count INTEGER NOT NULL, " +
                        "updated_utc_ms BIGINT NOT NULL" +
                        ")",
                    ]),
                new SchemaMigration(
                    version: 15,
                    description: "Accounts 扩展域：账号金币/角色战斗状态/英雄槽位映射",
                    statements:
                    [
                        "ALTER TABLE accounts ADD COLUMN gold BIGINT NOT NULL DEFAULT 0",
                        "ALTER TABLE accounts ADD COLUMN credit BIGINT NOT NULL DEFAULT 0",
                        "ALTER TABLE characters ADD COLUMN thrusting INTEGER NOT NULL DEFAULT 0",
                        "ALTER TABLE characters ADD COLUMN half_moon INTEGER NOT NULL DEFAULT 0",
                        "ALTER TABLE characters ADD COLUMN cross_half_moon INTEGER NOT NULL DEFAULT 0",
                        "ALTER TABLE characters ADD COLUMN double_slash INTEGER NOT NULL DEFAULT 0",
                        "ALTER TABLE characters ADD COLUMN mental_state INTEGER NOT NULL DEFAULT 0",
                        "ALTER TABLE characters ADD COLUMN pearl_count INTEGER NOT NULL DEFAULT 0",
                        "ALTER TABLE characters ADD COLUMN collect_time_remaining_ms BIGINT NOT NULL DEFAULT 0",
                        "ALTER TABLE characters ADD COLUMN maximum_hero_count INTEGER NOT NULL DEFAULT 1",
                        "ALTER TABLE characters ADD COLUMN current_hero_index INTEGER NOT NULL DEFAULT 0",
                        "ALTER TABLE characters ADD COLUMN hero_spawned INTEGER NOT NULL DEFAULT 0",
                        "ALTER TABLE characters ADD COLUMN hero_behaviour INTEGER NOT NULL DEFAULT 0",
                        "CREATE TABLE IF NOT EXISTS character_hero_slots (" +
                        "character_id BIGINT NOT NULL, " +
                        "slot_index INTEGER NOT NULL, " +
                        "hero_character_id BIGINT NOT NULL, " +
                        "updated_utc_ms BIGINT NOT NULL, " +
                        "PRIMARY KEY(character_id, slot_index)" +
                        ")",
                    ]),
                new SchemaMigration(
                    version: 16,
                    description: "Accounts 扩展域：角色 Buffs",
                    statements:
                    [
                        "CREATE TABLE IF NOT EXISTS character_buffs (" +
                        "character_id BIGINT NOT NULL, " +
                        "list_index INTEGER NOT NULL, " +
                        "buff_type INTEGER NOT NULL, " +
                        "payload BLOB NOT NULL, " +
                        "updated_utc_ms BIGINT NOT NULL, " +
                        "PRIMARY KEY(character_id, list_index)" +
                        ")",
                        "CREATE INDEX character_buffs_ix_buff_type ON character_buffs(buff_type)",
                    ]),
                new SchemaMigration(
                    version: 17,
                    description: "Accounts 扩展域：角色类型标记（玩家/英雄）",
                    statements:
                    [
                        "ALTER TABLE characters ADD COLUMN character_kind INTEGER NOT NULL DEFAULT 0",
                    ]),
            };
        }
    }
}
