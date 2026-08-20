using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Server.Persistence.Sql
{
    public sealed class SqliteWorldOnlyMigrationRequest
    {
        public string SourcePath { get; init; } = string.Empty;
        public string TargetDirectory { get; init; } = string.Empty;
        public string MigrationId { get; init; } = string.Empty;
        public string AuthorizedSourceSha256 { get; init; } = string.Empty;
        public bool WorldOnlyResetPlayers { get; init; }
    }

    public sealed class SqliteTableVerification
    {
        public string Table { get; init; } = string.Empty;
        public long SourceRows { get; init; }
        public long TargetRows { get; init; }
        public string SourceChecksum { get; init; } = string.Empty;
        public string TargetChecksum { get; init; } = string.Empty;
    }

    public sealed class SqliteLayoutMigrationResult
    {
        public string SourceSha256 { get; init; } = string.Empty;
        public string GenerationDirectory { get; init; } = string.Empty;
        public IReadOnlyList<SqliteTableVerification> Tables { get; init; } = Array.Empty<SqliteTableVerification>();
    }

    public sealed class SqliteLayoutMigrator
    {
        public SqliteLayoutMigrationResult Migrate(SqliteWorldOnlyMigrationRequest request)
        {
            ValidateRequest(request);

            var sourcePath = Path.GetFullPath(request.SourcePath);
            var targetRoot = Path.GetFullPath(request.TargetDirectory);
            var sourceSha = ComputeFileSha256(sourcePath);
            if (!sourceSha.Equals(request.AuthorizedSourceSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"源库 SHA-256 与授权不一致。actual={sourceSha}");

            AssertIntegrity(sourcePath);
            Directory.CreateDirectory(targetRoot);

            var backups = Path.Combine(targetRoot, "backups");
            Directory.CreateDirectory(backups);
            var backupPath = Path.Combine(backups, $"{request.MigrationId}-{sourceSha}.db");
            if (!File.Exists(backupPath))
                File.Copy(sourcePath, backupPath, overwrite: false);
            File.SetAttributes(backupPath, File.GetAttributes(backupPath) | FileAttributes.ReadOnly);

            var generationName = Path.Combine("layouts", SanitizeMigrationId(request.MigrationId));
            var generationRoot = Path.Combine(targetRoot, generationName);
            Directory.CreateDirectory(generationRoot);

            var identityPath = Path.Combine(generationRoot, "identity.db");
            var characterPath = Path.Combine(generationRoot, "characters.db");
            var worldPath = Path.Combine(generationRoot, "world.db");

            CreateAuthorityDatabase(identityPath, DatabaseAuthority.Identity, request.MigrationId, sourceSha);
            CreateAuthorityDatabase(characterPath, DatabaseAuthority.Character, request.MigrationId, sourceSha);
            CreateAuthorityDatabase(worldPath, DatabaseAuthority.World, request.MigrationId, sourceSha);

            var verifications = CopyAndVerifyWorld(sourcePath, worldPath);
            AssertEmpty(identityPath, "accounts");
            AssertEmpty(characterPath, "characters");
            AssertIntegrity(identityPath);
            AssertIntegrity(characterPath);
            AssertIntegrity(worldPath);
            AssertForeignKeys(identityPath);
            AssertForeignKeys(characterPath);
            AssertForeignKeys(worldPath);

            MarkManifest(identityPath, DatabaseAuthority.Identity, request.MigrationId, sourceSha);
            MarkManifest(characterPath, DatabaseAuthority.Character, request.MigrationId, sourceSha);
            MarkManifest(worldPath, DatabaseAuthority.World, request.MigrationId, sourceSha);

            var activation = new SqliteActivationManifest
            {
                MigrationId = request.MigrationId,
                SourceSha256 = sourceSha,
                GenerationDirectory = generationName,
                Completed = true,
                ActivatedUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            };

            var activationPath = Path.Combine(targetRoot, SqliteDatabaseLayout.ActivationFileName);
            var activationTemp = activationPath + ".new";
            File.WriteAllText(activationTemp, JsonSerializer.Serialize(activation, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(activationTemp, activationPath, overwrite: true);

            return new SqliteLayoutMigrationResult
            {
                SourceSha256 = sourceSha,
                GenerationDirectory = generationRoot,
                Tables = verifications,
            };
        }

        public static string ComputeFileSha256(string path)
        {
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }

        private static void ValidateRequest(SqliteWorldOnlyMigrationRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (!request.WorldOnlyResetPlayers)
                throw new InvalidOperationException("发现玩家域时默认拒绝清空；本迁移必须显式授权 WorldOnlyResetPlayers。");
            if (!File.Exists(request.SourcePath))
                throw new FileNotFoundException("找不到 SQLite 源库。", request.SourcePath);
            if (string.IsNullOrWhiteSpace(request.TargetDirectory))
                throw new ArgumentException("TargetDirectory 不能为空。", nameof(request));
            if (string.IsNullOrWhiteSpace(request.MigrationId))
                throw new ArgumentException("MigrationId 不能为空。", nameof(request));
            if (request.AuthorizedSourceSha256?.Length != 64)
                throw new ArgumentException("AuthorizedSourceSha256 必须是 64 位 SHA-256。", nameof(request));
        }

        private static void CreateAuthorityDatabase(string path, DatabaseAuthority authority, string migrationId, string sourceSha)
        {
            var options = new SqlDatabaseOptions { SqlitePath = path, Authority = authority };
            using var session = SqlSession.Open(DatabaseProviderKind.Sqlite, options);
            var migrator = new SchemaMigrator(AuthoritySchemaMigrator.Create(authority));
            migrator.ApplyPendingMigrations(session.Connection, session.Dialect, "DbMigrator", migrationId);
        }

        private static IReadOnlyList<SqliteTableVerification> CopyAndVerifyWorld(string sourcePath, string worldPath)
        {
            using var connection = Open(worldPath);
            using (var attach = connection.CreateCommand())
            {
                attach.CommandText = "ATTACH DATABASE $source AS source";
                attach.Parameters.AddWithValue("$source", sourcePath);
                attach.ExecuteNonQuery();
            }

            var tables = GetUserTables(connection, "main")
                .Where(table => table is not "schema_version" and not "database_manifest")
                .OrderBy(table => table, StringComparer.Ordinal)
                .ToArray();

            using (var tx = connection.BeginTransaction())
            {
                foreach (var table in tables)
                {
                    if (!TableExists(connection, "source", table, tx))
                        throw new InvalidOperationException($"源库缺少 World 表：{table}");

                    using var clear = connection.CreateCommand();
                    clear.Transaction = tx;
                    clear.CommandText = $"DELETE FROM {Quote(table)}";
                    clear.ExecuteNonQuery();

                    using var copy = connection.CreateCommand();
                    copy.Transaction = tx;
                    copy.CommandText = $"INSERT INTO {Quote(table)} SELECT * FROM source.{Quote(table)}{WorldRowFilter(table)}";
                    copy.ExecuteNonQuery();
                }
                tx.Commit();
            }

            var results = new List<SqliteTableVerification>(tables.Length);
            foreach (var table in tables)
            {
                var sourceRows = Count(connection, "source", table, WorldRowFilter(table));
                var targetRows = Count(connection, "main", table);
                var sourceChecksum = ComputeTableChecksum(connection, "source", table, WorldRowFilter(table));
                var targetChecksum = ComputeTableChecksum(connection, "main", table);
                if (sourceRows != targetRows || !sourceChecksum.Equals(targetChecksum, StringComparison.Ordinal))
                    throw new InvalidOperationException($"World 表校验失败：{table}");

                results.Add(new SqliteTableVerification
                {
                    Table = table,
                    SourceRows = sourceRows,
                    TargetRows = targetRows,
                    SourceChecksum = sourceChecksum,
                    TargetChecksum = targetChecksum,
                });
            }
            return results;
        }

        private static SqliteConnection Open(string path)
        {
            var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadWrite,
                Cache = SqliteCacheMode.Private,
                Pooling = false,
            }.ToString());
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=10000;";
            command.ExecuteNonQuery();
            return connection;
        }

        private static string[] GetUserTables(SqliteConnection connection, string schema)
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT name FROM {Quote(schema)}.sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name";
            using var reader = command.ExecuteReader();
            var result = new List<string>();
            while (reader.Read()) result.Add(reader.GetString(0));
            return result.ToArray();
        }

        private static bool TableExists(SqliteConnection connection, string schema, string table, SqliteTransaction tx)
        {
            using var command = connection.CreateCommand();
            command.Transaction = tx;
            command.CommandText = $"SELECT COUNT(*) FROM {Quote(schema)}.sqlite_master WHERE type='table' AND name=$table";
            command.Parameters.AddWithValue("$table", table);
            return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture) == 1;
        }

        private static long Count(SqliteConnection connection, string schema, string table, string filter = "")
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT COUNT(*) FROM {Quote(schema)}.{Quote(table)}{filter}";
            return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        }

        private static string ComputeTableChecksum(SqliteConnection connection, string schema, string table, string filter = "")
        {
            var columns = GetColumns(connection, schema, table);
            var order = columns.Where(column => column.PrimaryKey > 0).OrderBy(column => column.PrimaryKey).Select(column => Quote(column.Name)).ToArray();
            if (order.Length == 0) order = columns.Select(column => Quote(column.Name)).ToArray();

            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT * FROM {Quote(schema)}.{Quote(table)}{filter} ORDER BY {string.Join(",", order)}";
            using var reader = command.ExecuteReader();
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            while (reader.Read())
            {
                for (var index = 0; index < reader.FieldCount; index++)
                {
                    var bytes = CanonicalBytes(reader.GetValue(index));
                    hash.AppendData(BitConverter.GetBytes(bytes.Length));
                    hash.AppendData(bytes);
                }
            }
            return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        }

        private static (string Name, int PrimaryKey)[] GetColumns(SqliteConnection connection, string schema, string table)
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA {Quote(schema)}.table_info({Quote(table)})";
            using var reader = command.ExecuteReader();
            var result = new List<(string, int)>();
            while (reader.Read()) result.Add((reader.GetString(1), reader.GetInt32(5)));
            return result.ToArray();
        }

        private static byte[] CanonicalBytes(object value)
        {
            if (value is null or DBNull) return [0];
            if (value is byte[] bytes) return bytes;
            return Encoding.UTF8.GetBytes(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);
        }

        private static void AssertEmpty(string path, string table)
        {
            using var connection = Open(path);
            if (Count(connection, "main", table) != 0)
                throw new InvalidOperationException($"重置迁移要求 {table} 为空。");
        }

        private static void AssertIntegrity(string path)
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadOnly,
                Cache = SqliteCacheMode.Private,
                Pooling = false,
            }.ToString());
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA integrity_check";
            if (!string.Equals(Convert.ToString(command.ExecuteScalar()), "ok", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"SQLite integrity_check 失败：{path}");
        }

        private static void AssertForeignKeys(string path)
        {
            using var connection = Open(path);
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA foreign_key_check";
            using var reader = command.ExecuteReader();
            if (reader.Read()) throw new InvalidOperationException($"SQLite foreign_key_check 失败：{path}");
        }

        private static void MarkManifest(string path, DatabaseAuthority authority, string migrationId, string sourceSha)
        {
            var options = new SqlDatabaseOptions { SqlitePath = path, Authority = authority };
            using var session = SqlSession.Open(DatabaseProviderKind.Sqlite, options);
            session.RunInTransaction(s => AuthoritySchemaMigrator.MarkComplete(s, authority, migrationId, 0, sourceSha));
        }

        private static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"") + "\"";

        private static string WorldRowFilter(string table)
        {
            if (table.Equals("server_meta", StringComparison.OrdinalIgnoreCase))
                return $" WHERE meta_key='{SqlWorldRelationsStore.MetaKeyWorldRelationsEpochUtcMs}'";
            if (!table.Equals("next_ids", StringComparison.OrdinalIgnoreCase))
                return string.Empty;

            var keys = string.Join(",", SqlWorldRelationsStore.WorldNextIdKeys.Select(key => $"'{key.Replace("'", "''")}'"));
            return $" WHERE name IN ({keys})";
        }

        private static string SanitizeMigrationId(string migrationId)
        {
            var invalid = Path.GetInvalidFileNameChars();
            return new string(migrationId.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
        }
    }
}
