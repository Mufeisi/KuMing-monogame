using System;
using System.Data;
using Microsoft.Data.Sqlite;
using MySqlConnector;

namespace Server.Persistence.Sql
{
    public sealed class SqlConnectionFactory
    {
        private readonly SqlDatabaseOptions _options;

        public SqlConnectionFactory(SqlDatabaseOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public IDbConnection CreateOpenConnection(DatabaseProviderKind provider)
        {
            return provider switch
            {
                DatabaseProviderKind.Sqlite => CreateOpenSqliteConnection(_options.SqlitePath),
                DatabaseProviderKind.MySql => CreateOpenMySqlConnection(_options),
                _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, "不支持的数据库 Provider。"),
            };
        }

        private static IDbConnection CreateOpenSqliteConnection(string sqlitePath)
        {
            if (string.IsNullOrWhiteSpace(sqlitePath))
                throw new ArgumentException("SqlitePath 不能为空。", nameof(sqlitePath));

            var fullPath = Path.GetFullPath(sqlitePath);
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            var csb = new SqliteConnectionStringBuilder
            {
                DataSource = fullPath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Shared,
            };

            var connection = new SqliteConnection(csb.ToString());
            connection.Open();
            return connection;
        }

        private static IDbConnection CreateOpenMySqlConnection(SqlDatabaseOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));

            if (string.IsNullOrWhiteSpace(options.MySqlConnectionString))
                throw new ArgumentException("MySqlConnectionString 不能为空。", nameof(options.MySqlConnectionString));

            var connectionString = ApplyMySqlConnectionStringOverrides(options.MySqlConnectionString, options);

            var connection = new MySqlConnection(connectionString);
            try
            {
                connection.Open();
                return connection;
            }
            catch (MySqlException ex) when (IsUnknownDatabase(ex))
            {
                try { connection.Dispose(); } catch { /* ignore */ }

                EnsureMySqlDatabaseExists(connectionString);

                var retry = new MySqlConnection(connectionString);
                retry.Open();
                return retry;
            }
            catch
            {
                try { connection.Dispose(); } catch { /* ignore */ }
                throw;
            }
        }

        private static string ApplyMySqlConnectionStringOverrides(string baseConnectionString, SqlDatabaseOptions options)
        {
            if (string.IsNullOrWhiteSpace(baseConnectionString))
                return baseConnectionString;

            if (options == null)
                return baseConnectionString;

            var hasOverride =
                options.MySqlPooling != -1 ||
                options.MySqlMinPoolSize > 0 ||
                options.MySqlMaxPoolSize > 0 ||
                options.MySqlConnectionTimeoutSeconds > 0 ||
                options.MySqlKeepAliveSeconds > 0 ||
                options.MySqlConnectionIdleTimeoutSeconds > 0 ||
                options.MySqlConnectionLifeTimeSeconds > 0;

            if (!hasOverride)
                return baseConnectionString;

            MySqlConnectionStringBuilder csb;
            try
            {
                csb = new MySqlConnectionStringBuilder(baseConnectionString);
            }
            catch (Exception ex)
            {
                throw new ArgumentException("MySqlConnectionString 解析失败。", nameof(SqlDatabaseOptions.MySqlConnectionString), ex);
            }

            // Pooling：-1=不覆盖（使用连接串/驱动默认），0=禁用，1=启用
            if (options.MySqlPooling == 0) csb.Pooling = false;
            else if (options.MySqlPooling == 1) csb.Pooling = true;

            var minPoolSize = options.MySqlMinPoolSize;
            var maxPoolSize = options.MySqlMaxPoolSize;

            if (minPoolSize > 0 && maxPoolSize > 0 && minPoolSize > maxPoolSize)
                maxPoolSize = minPoolSize;

            if (minPoolSize > 0) csb.MinimumPoolSize = (uint)minPoolSize;
            if (maxPoolSize > 0) csb.MaximumPoolSize = (uint)maxPoolSize;

            if (options.MySqlConnectionTimeoutSeconds > 0) csb.ConnectionTimeout = (uint)options.MySqlConnectionTimeoutSeconds;
            if (options.MySqlKeepAliveSeconds > 0) csb.Keepalive = (uint)options.MySqlKeepAliveSeconds;
            if (options.MySqlConnectionIdleTimeoutSeconds > 0) csb.ConnectionIdleTimeout = (uint)options.MySqlConnectionIdleTimeoutSeconds;
            if (options.MySqlConnectionLifeTimeSeconds > 0) csb.ConnectionLifeTime = (uint)options.MySqlConnectionLifeTimeSeconds;

            return csb.ConnectionString;
        }

        private static bool IsUnknownDatabase(MySqlException ex)
        {
            // MySQL error: ER_BAD_DB_ERROR (1049): Unknown database 'xxx'
            return ex != null && ex.Number == 1049;
        }

        private static void EnsureMySqlDatabaseExists(string connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentException("MySqlConnectionString 不能为空。", nameof(connectionString));

            MySqlConnectionStringBuilder csb;
            try
            {
                csb = new MySqlConnectionStringBuilder(connectionString);
            }
            catch (Exception ex)
            {
                throw new ArgumentException("MySqlConnectionString 解析失败。", nameof(connectionString), ex);
            }

            var database = (csb.Database ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(database))
                throw new InvalidOperationException("连接字符串未指定 Database，无法自动创建数据库。");

            // 连接到 server 级别（不指定 Database）创建目标库，再回连。
            var serverCsb = new MySqlConnectionStringBuilder(connectionString)
            {
                Database = string.Empty,
                Pooling = false,
            };

            try
            {
                using var serverConn = new MySqlConnection(serverCsb.ConnectionString);
                serverConn.Open();

                using var cmd = serverConn.CreateCommand();
                cmd.CommandText =
                    "CREATE DATABASE IF NOT EXISTS " +
                    QuoteMySqlIdentifier(database) +
                    " DEFAULT CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci";
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"无法自动创建 MySQL 数据库：{database}。请手动创建该数据库，或为该账号授予 CREATE 权限。", ex);
            }
        }

        private static string QuoteMySqlIdentifier(string identifier)
        {
            if (identifier == null) return "``";
            return "`" + identifier.Replace("`", "``") + "`";
        }
    }
}
