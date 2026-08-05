using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Text;
using System.Threading;
using Dapper;

namespace Server.Persistence.Sql
{
    public sealed class SqlSession : IDisposable
    {
        private readonly int _commandTimeoutSeconds;
        private readonly int _maxRetries;
        private readonly int _baseRetryDelayMs;

        public DatabaseProviderKind Provider { get; }
        public ISqlDialect Dialect { get; }
        public IDbConnection Connection { get; }
        public IDbTransaction Transaction { get; private set; }

        public int CommandTimeoutSeconds => _commandTimeoutSeconds;

        private SqlSession(
            DatabaseProviderKind provider,
            ISqlDialect dialect,
            IDbConnection connection,
            int commandTimeoutSeconds,
            int maxRetries,
            int baseRetryDelayMs)
        {
            Provider = provider;
            Dialect = dialect ?? throw new ArgumentNullException(nameof(dialect));
            Connection = connection ?? throw new ArgumentNullException(nameof(connection));
            _commandTimeoutSeconds = commandTimeoutSeconds <= 0 ? 30 : commandTimeoutSeconds;
            _maxRetries = maxRetries < 1 ? 1 : maxRetries;
            _baseRetryDelayMs = baseRetryDelayMs < 0 ? 0 : baseRetryDelayMs;
        }

        public static SqlSession Open(
            DatabaseProviderKind provider,
            SqlDatabaseOptions options,
            int maxRetries = 1,
            int baseRetryDelayMs = 200)
        {
            return Open(provider, options, new SqlSessionOptions
            {
                ConnectMaxRetries = maxRetries,
                CommandMaxRetries = maxRetries,
                BaseRetryDelayMs = baseRetryDelayMs,
            });
        }

        public static SqlSession Open(
            DatabaseProviderKind provider,
            SqlDatabaseOptions options,
            SqlSessionOptions sessionOptions)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (provider == DatabaseProviderKind.Legacy)
                throw new ArgumentException("Legacy Provider 不支持打开 SQL Session。", nameof(provider));

            // 统一开启下划线字段映射：便于 snake_case 列名映射到 PascalCase 属性（例如 map_id -> MapId）。
            // 说明：该设置为 Dapper 全局开关，重复设置为 true 是幂等的。
            DefaultTypeMap.MatchNamesWithUnderscores = true;

            sessionOptions ??= new SqlSessionOptions();

            var connectMaxRetries = sessionOptions.ConnectMaxRetries < 1 ? 1 : sessionOptions.ConnectMaxRetries;
            var commandMaxRetries = sessionOptions.CommandMaxRetries < 1 ? 1 : sessionOptions.CommandMaxRetries;
            var baseRetryDelayMs = sessionOptions.BaseRetryDelayMs < 0 ? 0 : sessionOptions.BaseRetryDelayMs;

            var dialect = SqlDialectFactory.Create(provider);
            var connection = OpenConnectionWithRetry(provider, options, connectMaxRetries, baseRetryDelayMs);

            var session = new SqlSession(provider, dialect, connection, options.CommandTimeoutSeconds, commandMaxRetries, baseRetryDelayMs);
            session.InitializeConnection();
            return session;
        }

        private static IDbConnection OpenConnectionWithRetry(
            DatabaseProviderKind provider,
            SqlDatabaseOptions options,
            int maxRetries,
            int baseRetryDelayMs)
        {
            var factory = new SqlConnectionFactory(options);
            var attempt = 0;

            while (true)
            {
                attempt++;
                try
                {
                    return factory.CreateOpenConnection(provider);
                }
                catch (Exception ex) when (attempt < maxRetries && SqlTransientDetector.IsTransient(provider, ex))
                {
                    SleepBackoff(attempt, baseRetryDelayMs);
                }
            }
        }

        private void InitializeConnection()
        {
            if (Provider == DatabaseProviderKind.Sqlite)
            {
                using var cmd = Connection.CreateCommand();
                cmd.CommandText = "PRAGMA foreign_keys = ON;";
                cmd.CommandTimeout = _commandTimeoutSeconds;
                cmd.ExecuteNonQuery();
            }
        }

        public void BeginTransaction(IsolationLevel? isolationLevel = null)
        {
            if (Transaction != null)
                throw new InvalidOperationException("事务已开始，无法重复开启。");

            Transaction = isolationLevel.HasValue
                ? Connection.BeginTransaction(isolationLevel.Value)
                : Connection.BeginTransaction();
        }

        public void Commit()
        {
            if (Transaction == null)
                throw new InvalidOperationException("未开启事务，无法提交。");

            Transaction.Commit();
            Transaction.Dispose();
            Transaction = null;
        }

        public void Rollback()
        {
            if (Transaction == null) return;

            try { Transaction.Rollback(); } catch { /* ignore */ }
            Transaction.Dispose();
            Transaction = null;
        }

        public void RunInTransaction(Action<SqlSession> action, IsolationLevel? isolationLevel = null)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));

            BeginTransaction(isolationLevel);

            try
            {
                action(this);
                Commit();
            }
            catch
            {
                Rollback();
                throw;
            }
        }

        public string QuoteIdentifier(SqlIdentifier identifier)
        {
            return Dialect.QuoteIdentifier(identifier.Value);
        }

        public int Execute(string sql, object param = null)
        {
            if (string.IsNullOrWhiteSpace(sql)) throw new ArgumentException("SQL 不能为空。", nameof(sql));

            return ExecuteWithRetry(SqlCommandKind.Execute, sql, param, () =>
                Connection.Execute(sql, param, transaction: Transaction, commandTimeout: _commandTimeoutSeconds));
        }

        public T ExecuteScalar<T>(string sql, object param = null)
        {
            if (string.IsNullOrWhiteSpace(sql)) throw new ArgumentException("SQL 不能为空。", nameof(sql));

            return ExecuteWithRetry(SqlCommandKind.Scalar, sql, param, () =>
                Connection.ExecuteScalar<T>(sql, param, transaction: Transaction, commandTimeout: _commandTimeoutSeconds));
        }

        public IReadOnlyList<T> Query<T>(string sql, object param = null)
        {
            if (string.IsNullOrWhiteSpace(sql)) throw new ArgumentException("SQL 不能为空。", nameof(sql));

            return ExecuteWithRetry(SqlCommandKind.Query, sql, param, () =>
                Connection.Query<T>(sql, param, transaction: Transaction, buffered: true, commandTimeout: _commandTimeoutSeconds).AsList());
        }

        public int Execute(FormattableString sql)
        {
            if (sql == null) throw new ArgumentNullException(nameof(sql));

            var (sqlText, parameters) = BuildInterpolatedSql(sql);
            return Execute(sqlText, parameters);
        }

        public T ExecuteScalar<T>(FormattableString sql)
        {
            if (sql == null) throw new ArgumentNullException(nameof(sql));

            var (sqlText, parameters) = BuildInterpolatedSql(sql);
            return ExecuteScalar<T>(sqlText, parameters);
        }

        public IReadOnlyList<T> Query<T>(FormattableString sql)
        {
            if (sql == null) throw new ArgumentNullException(nameof(sql));

            var (sqlText, parameters) = BuildInterpolatedSql(sql);
            return Query<T>(sqlText, parameters);
        }

        private static (string Sql, DynamicParameters Parameters) BuildInterpolatedSql(FormattableString formattable)
        {
            var format = formattable.Format ?? string.Empty;
            var args = formattable.GetArguments();
            var added = new bool[args.Length];

            var parameters = new DynamicParameters();
            var sb = new StringBuilder(format.Length + args.Length * 4);

            for (var i = 0; i < format.Length; i++)
            {
                var ch = format[i];

                if (ch == '{')
                {
                    if (i + 1 < format.Length && format[i + 1] == '{')
                    {
                        sb.Append('{');
                        i++;
                        continue;
                    }

                    i++;
                    if (i >= format.Length)
                        throw new FormatException("SQL 插值格式不正确：缺少 '}'。");

                    var index = 0;
                    var hasIndex = false;
                    while (i < format.Length && char.IsDigit(format[i]))
                    {
                        hasIndex = true;
                        index = (index * 10) + (format[i] - '0');
                        i++;
                    }

                    if (!hasIndex)
                        throw new FormatException("SQL 插值格式不正确：占位符缺少索引。");
                    if (index < 0 || index >= args.Length)
                        throw new FormatException($"SQL 插值格式不正确：索引 {index} 超出参数范围。");

                    if (i < format.Length && (format[i] == ',' || format[i] == ':'))
                        throw new NotSupportedException("SQL 插值不支持对齐/格式化，请去掉格式说明符（例如 {0} 而不是 {0:N0}）。");

                    if (i >= format.Length || format[i] != '}')
                        throw new FormatException("SQL 插值格式不正确：缺少 '}'。");

                    sb.Append("@p").Append(index);

                    if (!added[index])
                    {
                        parameters.Add("p" + index, args[index]);
                        added[index] = true;
                    }

                    continue;
                }

                if (ch == '}')
                {
                    if (i + 1 < format.Length && format[i + 1] == '}')
                    {
                        sb.Append('}');
                        i++;
                        continue;
                    }

                    throw new FormatException("SQL 插值格式不正确：出现未转义的 '}'。");
                }

                sb.Append(ch);
            }

            return (sb.ToString(), parameters);
        }

        private T ExecuteWithRetry<T>(SqlCommandKind kind, string sql, object param, Func<T> action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));

            var attempt = 0;

            while (true)
            {
                attempt++;

                var sw = Stopwatch.StartNew();
                try
                {
                    var result = action();
                    sw.Stop();
                    SqlCommandDiagnostics.Record(Provider, kind, sql, param, sw.ElapsedMilliseconds);
                    return result;
                }
                catch (Exception ex) when (attempt < _maxRetries && SqlTransientDetector.IsTransient(Provider, ex))
                {
                    sw.Stop();
                    SleepBackoff(attempt, _baseRetryDelayMs);
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    SqlCommandDiagnostics.Record(Provider, kind, sql, param, sw.ElapsedMilliseconds, ex);
                    throw;
                }
            }
        }

        private static void SleepBackoff(int attempt, int baseRetryDelayMs)
        {
            if (baseRetryDelayMs <= 0) return;

            var delay = Math.Min(5000, baseRetryDelayMs * attempt);
            Thread.Sleep(delay);
        }

        public void Dispose()
        {
            Rollback();
            Connection.Dispose();
        }
    }
}
