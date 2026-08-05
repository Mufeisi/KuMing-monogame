using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Server;

namespace Server.Persistence.Sql
{
    internal sealed class SqlSaveDomainFailureState
    {
        public int ConsecutiveFailures;
        public DateTime LastFailureUtc;
        public DateTime LastSuccessUtc;
        public DateTime CooldownUntilUtc;
        public string LastError;
        public DatabaseProviderKind LastProvider;
        public bool CircuitOpenLogged;
    }

    /// <summary>
    /// 保存容灾（失败计数/降级跳过/落盘日志/停服保护）。
    /// 说明：
    /// - 不依赖 SQL 具体实现，可用于 Legacy 文件保存与 SQL 保存共同统计。
    /// - 仅做“监控与降级决策”，是否跳过/是否允许停服由上层调用方决定。
    /// </summary>
    public static class SqlSaveResilience
    {
        private static readonly ConcurrentDictionary<SqlSaveDomain, SqlSaveDomainFailureState> States =
            new ConcurrentDictionary<SqlSaveDomain, SqlSaveDomainFailureState>();

        private static readonly object FileGate = new object();
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        public static int FailuresBeforeCooldown
        {
            get
            {
                return Settings.SaveFailureMaxConsecutive < 1 ? 1 : Settings.SaveFailureMaxConsecutive;
            }
        }

        public static TimeSpan CooldownDuration
        {
            get
            {
                var seconds = Settings.SaveFailureCooldownSeconds;
                if (seconds < 0) seconds = 0;
                return TimeSpan.FromSeconds(seconds);
            }
        }

        public static bool ShouldSkipAutoSave(SqlSaveDomain domain, out int remainingSeconds)
        {
            remainingSeconds = 0;

            var state = States.GetOrAdd(domain, _ => new SqlSaveDomainFailureState());
            var now = DateTime.UtcNow;

            lock (state)
            {
                if (state.CooldownUntilUtc <= now)
                    return false;

                var remain = state.CooldownUntilUtc - now;
                remainingSeconds = (int)Math.Ceiling(remain.TotalSeconds);
                if (remainingSeconds < 0) remainingSeconds = 0;
                return true;
            }
        }

        public static void ReportSuccess(DatabaseProviderKind provider, SqlSaveDomain domain)
        {
            var state = States.GetOrAdd(domain, _ => new SqlSaveDomainFailureState());
            lock (state)
            {
                state.ConsecutiveFailures = 0;
                state.LastSuccessUtc = DateTime.UtcNow;
                state.CooldownUntilUtc = DateTime.MinValue;
                state.LastError = null;
                state.LastProvider = provider;
                state.CircuitOpenLogged = false;
            }
        }

        public static void ReportFailure(
            DatabaseProviderKind provider,
            SqlSaveDomain domain,
            Exception ex,
            string operation,
            bool transient = false,
            int attempts = 1,
            long durationMs = 0)
        {
            if (ex == null) return;

            var now = DateTime.UtcNow;
            var threshold = FailuresBeforeCooldown;
            var cooldown = CooldownDuration;

            int consecutive;
            DateTime cooldownUntil;
            string lastError;

            var state = States.GetOrAdd(domain, _ => new SqlSaveDomainFailureState());
            lock (state)
            {
                state.ConsecutiveFailures++;
                state.LastFailureUtc = now;
                state.LastProvider = provider;

                lastError = $"{ex.GetType().Name}: {ex.Message}";
                state.LastError = lastError;

                consecutive = state.ConsecutiveFailures;

                if (cooldown > TimeSpan.Zero && consecutive >= threshold)
                {
                    state.CooldownUntilUtc = now.Add(cooldown);
                }

                cooldownUntil = state.CooldownUntilUtc;
            }

            var transientText = transient ? "Transient" : "NonTransient";
            var msg =
                $"[SAVE:{provider}] {domain} 保存失败（{transientText}，attempts={attempts}，{durationMs}ms，连续失败={consecutive}）" +
                (string.IsNullOrWhiteSpace(operation) ? string.Empty : $" op={operation}") +
                $"：{lastError}";

            MessageQueue.Instance.Enqueue(msg);
            TryAppendFailureToFile(provider, domain, ex, operation, transient, attempts, durationMs, consecutive, cooldownUntil);

            if (cooldown > TimeSpan.Zero && consecutive == threshold)
            {
                MessageQueue.Instance.Enqueue(
                    $"[SAVE:{provider}] {domain} 连续失败达到阈值（{threshold}），进入降级：{Math.Max(0, (int)cooldown.TotalSeconds)}s 内跳过自动保存。");
            }
        }

        public static bool ShouldBlockShutdown(out string reason)
        {
            reason = null;

            if (!Settings.BlockShutdownOnSaveFailures)
                return false;

            var threshold = Settings.BlockShutdownOnSaveFailuresThreshold < 1 ? 1 : Settings.BlockShutdownOnSaveFailuresThreshold;

            var offenders = new List<(SqlSaveDomain Domain, int Failures, DatabaseProviderKind Provider, string LastError)>();

            foreach (var kvp in States)
            {
                var domain = kvp.Key;
                var state = kvp.Value;

                int failures;
                DatabaseProviderKind provider;
                string lastError;

                lock (state)
                {
                    failures = state.ConsecutiveFailures;
                    provider = state.LastProvider;
                    lastError = state.LastError;
                }

                if (failures >= threshold)
                    offenders.Add((domain, failures, provider, lastError));
            }

            if (offenders.Count == 0)
                return false;

            offenders.Sort((a, b) => b.Failures.CompareTo(a.Failures));

            var parts = offenders
                .Take(6)
                .Select(o => $"{o.Domain}({o.Provider}, 连续失败={o.Failures})")
                .ToArray();

            reason =
                $"检测到连续保存失败（>= {threshold}）：" +
                $"{string.Join(", ", parts)}。" +
                "继续关服可能导致数据丢失（未落盘），建议先排查存储问题或等待保存恢复后再停服。";

            return true;
        }

        private static void TryAppendFailureToFile(
            DatabaseProviderKind provider,
            SqlSaveDomain domain,
            Exception ex,
            string operation,
            bool transient,
            int attempts,
            long durationMs,
            int consecutiveFailures,
            DateTime cooldownUntilUtc)
        {
            if (!Settings.SaveFailureLogToFile) return;

            try
            {
                var dir = string.IsNullOrWhiteSpace(Settings.SaveFailureLogDir)
                    ? Path.Combine(".", "Logs", "Persistence")
                    : Settings.SaveFailureLogDir.Trim();

                Directory.CreateDirectory(dir);

                var filePath = Path.Combine(dir, $"save-failures-{DateTime.Now:yyyyMMdd}.log");

                var cooldownRemainingSeconds = 0;
                if (cooldownUntilUtc > DateTime.UtcNow)
                    cooldownRemainingSeconds = (int)Math.Ceiling((cooldownUntilUtc - DateTime.UtcNow).TotalSeconds);

                var line =
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\t" +
                    $"provider={provider}\t" +
                    $"domain={domain}\t" +
                    $"op={operation}\t" +
                    $"transient={(transient ? 1 : 0)}\t" +
                    $"attempts={attempts}\t" +
                    $"durationMs={durationMs}\t" +
                    $"consecutiveFailures={consecutiveFailures}\t" +
                    $"cooldownRemainingSeconds={cooldownRemainingSeconds}\t" +
                    $"exception={ex.GetType().Name}\t" +
                    $"message={ex.Message}\t" +
                    $"detail={Normalize(ex.ToString(), maxChars: 1200)}";

                lock (FileGate)
                {
                    File.AppendAllText(filePath, line + Environment.NewLine, Utf8NoBom);
                }
            }
            catch (Exception logEx)
            {
                // 避免日志写入失败造成二次故障：只输出到内存队列（且尽量只输出一次）。
                MessageQueue.Instance.Enqueue($"[SAVE] 落盘日志写入失败：{logEx.GetType().Name}: {logEx.Message}");
            }
        }

        private static string Normalize(string text, int maxChars)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            text = text.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');

            while (text.Contains("  "))
                text = text.Replace("  ", " ");

            if (maxChars > 0 && text.Length > maxChars)
                return text.Substring(0, maxChars) + "...";

            return text;
        }
    }
}

