using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using Dapper;
using Server;

namespace Server.Persistence.Sql
{
    public enum SqlCommandKind
    {
        Execute = 1,
        Query = 2,
        Scalar = 3,
    }

    public sealed class SqlCommandStatsSnapshot
    {
        public long TotalCommands { get; init; }
        public long TotalDurationMs { get; init; }
        public long SlowCommands { get; init; }
        public long SlowDurationMs { get; init; }
        public long ErrorCommands { get; init; }
    }

    public sealed class SqlCommandStats
    {
        private long _totalCommands;
        private long _totalDurationMs;
        private long _slowCommands;
        private long _slowDurationMs;
        private long _errorCommands;

        internal void Record(long durationMs, bool isSlow, bool isError)
        {
            Interlocked.Increment(ref _totalCommands);
            Interlocked.Add(ref _totalDurationMs, durationMs);

            if (isSlow)
            {
                Interlocked.Increment(ref _slowCommands);
                Interlocked.Add(ref _slowDurationMs, durationMs);
            }

            if (isError)
            {
                Interlocked.Increment(ref _errorCommands);
            }
        }

        public SqlCommandStatsSnapshot Snapshot()
        {
            return new SqlCommandStatsSnapshot
            {
                TotalCommands = Interlocked.Read(ref _totalCommands),
                TotalDurationMs = Interlocked.Read(ref _totalDurationMs),
                SlowCommands = Interlocked.Read(ref _slowCommands),
                SlowDurationMs = Interlocked.Read(ref _slowDurationMs),
                ErrorCommands = Interlocked.Read(ref _errorCommands),
            };
        }
    }

    public static class SqlCommandDiagnostics
    {
        public static int SlowQueryThresholdMs { get; set; } = 200;

        public static bool LogAllCommands { get; set; } = false;

        public static SqlCommandStats Stats { get; } = new SqlCommandStats();

        internal static void Record(
            DatabaseProviderKind provider,
            SqlCommandKind kind,
            string sql,
            object param,
            long durationMs,
            Exception exception = null)
        {
            var slowThreshold = SlowQueryThresholdMs <= 0 ? 200 : SlowQueryThresholdMs;
            var isSlow = durationMs >= slowThreshold;
            var isError = exception != null;

            Stats.Record(durationMs, isSlow, isError);

            if (!LogAllCommands && !isSlow && !isError)
                return;

            var sqlText = NormalizeSql(sql, maxChars: 600);
            var paramNames = GetParameterNames(param);

            if (isError)
            {
                MessageQueue.Instance.Enqueue(
                    $"[SQL:{provider}] 错误 {kind} ({durationMs}ms) Param=[{string.Join(", ", paramNames)}] Sql={sqlText} Exception={exception.GetType().Name}: {exception.Message}");
                return;
            }

            if (isSlow)
            {
                MessageQueue.Instance.EnqueueDebugging(
                    $"[SQL:{provider}] 慢查询 {kind} ({durationMs}ms) Param=[{string.Join(", ", paramNames)}] Sql={sqlText}");
                return;
            }

            MessageQueue.Instance.EnqueueDebugging(
                $"[SQL:{provider}] {kind} ({durationMs}ms) Param=[{string.Join(", ", paramNames)}] Sql={sqlText}");
        }

        internal static long MeasureMs(Stopwatch sw)
        {
            if (sw == null) return 0;
            return sw.ElapsedMilliseconds;
        }

        private static string NormalizeSql(string sql, int maxChars)
        {
            if (string.IsNullOrWhiteSpace(sql))
                return string.Empty;

            sql = sql.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');

            while (sql.Contains("  "))
                sql = sql.Replace("  ", " ");

            if (maxChars > 0 && sql.Length > maxChars)
                return sql.Substring(0, maxChars) + "...";

            return sql;
        }

        private static string[] GetParameterNames(object param)
        {
            if (param == null)
                return Array.Empty<string>();

            try
            {
                if (param is DynamicParameters dynamicParameters)
                {
                    return dynamicParameters.ParameterNames?
                        .Where(n => !string.IsNullOrWhiteSpace(n))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                        .Take(30)
                        .ToArray()
                        ?? Array.Empty<string>();
                }

                if (param is IEnumerable<KeyValuePair<string, object>> kvps)
                {
                    return kvps
                        .Select(k => k.Key)
                        .Where(n => !string.IsNullOrWhiteSpace(n))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                        .Take(30)
                        .ToArray();
                }

                var type = param.GetType();
                var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
                    .Select(p => p.Name);

                return props
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                    .Take(30)
                    .ToArray();
            }
            catch
            {
                return Array.Empty<string>();
            }
        }
    }
}

