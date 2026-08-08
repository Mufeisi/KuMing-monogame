using System;
using System.Data;
using System.Diagnostics;
using System.Threading;
using Server;
using Shared.Diagnostics;

namespace Server.Persistence.Sql
{
    public sealed class SqlDomainTransactionResult
    {
        public SqlSaveDomain Domain { get; }
        public bool Success { get; }
        public int Attempts { get; }
        public long DurationMs { get; }
        public Exception Exception { get; }

        internal SqlDomainTransactionResult(SqlSaveDomain domain, bool success, int attempts, long durationMs, Exception exception)
        {
            Domain = domain;
            Success = success;
            Attempts = attempts;
            DurationMs = durationMs;
            Exception = exception;
        }
    }

    public sealed class SqlDomainTransactionRunner
    {
        private readonly DatabaseProviderKind _provider;
        private readonly SqlDatabaseOptions _databaseOptions;
        private readonly SqlSessionOptions _sessionOptions;

        public int MaxAttempts { get; }
        public IsolationLevel? IsolationLevel { get; }
        public bool ContinueOnError { get; }

        public SqlDomainTransactionRunner(
            DatabaseProviderKind provider,
            SqlDatabaseOptions databaseOptions,
            SqlSessionOptions sessionOptions = null,
            int maxAttempts = 3,
            IsolationLevel? isolationLevel = null,
            bool continueOnError = true)
        {
            if (provider == DatabaseProviderKind.Legacy)
                throw new ArgumentException("Legacy Provider 不支持 SQL 事务执行。", nameof(provider));

            _provider = provider;
            _databaseOptions = databaseOptions ?? throw new ArgumentNullException(nameof(databaseOptions));
            _sessionOptions = sessionOptions ?? new SqlSessionOptions
            {
                // 事务级重试优先：避免在事务内对单条命令重复执行造成“部分副作用”。
                ConnectMaxRetries = 3,
                CommandMaxRetries = 1,
                BaseRetryDelayMs = 200,
            };

            MaxAttempts = maxAttempts < 1 ? 1 : maxAttempts;
            IsolationLevel = isolationLevel;
            ContinueOnError = continueOnError;
        }

        public SqlDomainTransactionResult Run(SqlSaveDomain domain, Action<SqlSession> work)
        {
            return RunCore(domain, work, recordFullSaveDuration: true);
        }

        private SqlDomainTransactionResult RunCore(
            SqlSaveDomain domain,
            Action<SqlSession> work,
            bool recordFullSaveDuration)
        {
            if (work == null) throw new ArgumentNullException(nameof(work));

            var swTotal = Stopwatch.StartNew();
            Exception lastError = null;

            for (var attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                long transactionStart = 0;
                try
                {
                    using var session = SqlSession.Open(_provider, _databaseOptions, _sessionOptions);
                    transactionStart = Stopwatch.GetTimestamp();
                    session.RunInTransaction(work, isolationLevel: IsolationLevel);
                    PerformanceMetrics.RecordDuration(
                        PerformanceMetricKind.SaveTransactionCommit,
                        Stopwatch.GetTimestamp() - transactionStart);

                    swTotal.Stop();
                    if (recordFullSaveDuration)
                        PerformanceMetrics.RecordDuration(PerformanceMetricKind.Save, swTotal.ElapsedTicks);
                    MessageQueue.Instance.EnqueueDebugging($"[SQL:{_provider}] {domain} 保存事务成功（attempt={attempt}, {swTotal.ElapsedMilliseconds}ms）");
                    SqlSaveResilience.ReportSuccess(_provider, domain);
                    return new SqlDomainTransactionResult(domain, success: true, attempts: attempt, durationMs: swTotal.ElapsedMilliseconds, exception: null);
                }
                catch (Exception ex) when (attempt < MaxAttempts && SqlTransientDetector.IsTransient(_provider, ex))
                {
                    lastError = ex;
                    PerformanceMetrics.Increment(PerformanceMetricKind.SaveFailure);
                    if (transactionStart > 0)
                        PerformanceMetrics.RecordDuration(
                            PerformanceMetricKind.SaveTransactionCommit,
                            Stopwatch.GetTimestamp() - transactionStart);

                    var delayMs = Math.Min(5000, (_sessionOptions.BaseRetryDelayMs <= 0 ? 200 : _sessionOptions.BaseRetryDelayMs) * attempt);
                    MessageQueue.Instance.EnqueueDebugging($"[SQL:{_provider}] {domain} 保存事务失败（可重试，attempt={attempt}/{MaxAttempts}，{delayMs}ms 后重试）：{ex.GetType().Name}: {ex.Message}");
                    Thread.Sleep(delayMs);
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    PerformanceMetrics.Increment(PerformanceMetricKind.SaveFailure);
                    if (transactionStart > 0)
                        PerformanceMetrics.RecordDuration(
                            PerformanceMetricKind.SaveTransactionCommit,
                            Stopwatch.GetTimestamp() - transactionStart);
                    swTotal.Stop();
                    if (recordFullSaveDuration)
                        PerformanceMetrics.RecordDuration(PerformanceMetricKind.Save, swTotal.ElapsedTicks);
                    MessageQueue.Instance.Enqueue($"[SQL:{_provider}] {domain} 保存事务失败（attempt={attempt}/{MaxAttempts}，{swTotal.ElapsedMilliseconds}ms）：{ex}");
                    SqlSaveResilience.ReportFailure(
                        _provider,
                        domain,
                        ex,
                        operation: "SqlDomainTransactionRunner.Run",
                        transient: SqlTransientDetector.IsTransient(_provider, ex),
                        attempts: attempt,
                        durationMs: swTotal.ElapsedMilliseconds);

                    if (!ContinueOnError)
                        throw;

                    return new SqlDomainTransactionResult(domain, success: false, attempts: attempt, durationMs: swTotal.ElapsedMilliseconds, exception: ex);
                }
            }

            swTotal.Stop();
            if (recordFullSaveDuration)
                PerformanceMetrics.RecordDuration(PerformanceMetricKind.Save, swTotal.ElapsedTicks);
            MessageQueue.Instance.Enqueue($"[SQL:{_provider}] {domain} 保存事务连续失败（{MaxAttempts} 次，{swTotal.ElapsedMilliseconds}ms）：{lastError}");
            if (lastError != null)
            {
                SqlSaveResilience.ReportFailure(
                    _provider,
                    domain,
                    lastError,
                    operation: "SqlDomainTransactionRunner.Run",
                    transient: SqlTransientDetector.IsTransient(_provider, lastError),
                    attempts: MaxAttempts,
                    durationMs: swTotal.ElapsedMilliseconds);
            }
            return new SqlDomainTransactionResult(domain, success: false, attempts: MaxAttempts, durationMs: swTotal.ElapsedMilliseconds, exception: lastError);
        }

        /// <summary>
        /// 两阶段保存：先在“快照阶段”构建纯数据快照（不依赖后续可变的内存状态），再在事务内提交。
        /// </summary>
        public SqlDomainTransactionResult RunWithSnapshot<TSnapshot>(
            SqlSaveDomain domain,
            Func<TSnapshot> snapshotFactory,
            Action<SqlSession, TSnapshot> work)
        {
            if (snapshotFactory == null) throw new ArgumentNullException(nameof(snapshotFactory));
            if (work == null) throw new ArgumentNullException(nameof(work));

            var saveStart = Stopwatch.GetTimestamp();
            TSnapshot snapshot;
            var snapshotStart = Stopwatch.GetTimestamp();
            try
            {
                snapshot = snapshotFactory();
                PerformanceMetrics.RecordDuration(
                    PerformanceMetricKind.SaveSnapshotCapture,
                    Stopwatch.GetTimestamp() - snapshotStart);
            }
            catch (Exception ex)
            {
                PerformanceMetrics.RecordDuration(
                    PerformanceMetricKind.SaveSnapshotCapture,
                    Stopwatch.GetTimestamp() - snapshotStart);
                PerformanceMetrics.Increment(PerformanceMetricKind.SaveFailure);
                PerformanceMetrics.RecordDuration(
                    PerformanceMetricKind.Save,
                    Stopwatch.GetTimestamp() - saveStart);
                MessageQueue.Instance.Enqueue($"[SQL:{_provider}] {domain} 快照构建失败：{ex}");
                SqlSaveResilience.ReportFailure(
                    _provider,
                    domain,
                    ex,
                    operation: "SqlDomainTransactionRunner.Snapshot",
                    transient: SqlTransientDetector.IsTransient(_provider, ex),
                    attempts: 0,
                    durationMs: 0);
                if (!ContinueOnError) throw;

                return new SqlDomainTransactionResult(domain, success: false, attempts: 0, durationMs: 0, exception: ex);
            }

            try
            {
                return RunCore(domain, session => work(session, snapshot), recordFullSaveDuration: false);
            }
            finally
            {
                // Save 是完整调用耗时，覆盖快照、事务、重试和失败；快照/事务指标仍单独保留。
                PerformanceMetrics.RecordDuration(
                    PerformanceMetricKind.Save,
                    Stopwatch.GetTimestamp() - saveStart);
            }
        }
    }
}
