using System;
using System.Collections.Generic;
using System.Threading;

namespace Server.Persistence.Sql
{
    /// <summary>
    /// SQLite 运行期唯一写入执行器：同一时刻只执行一个事务；同一数据域尚未开始的请求只保留最新一次。
    /// 快照必须由调用方在主线程捕获，本类只接收不再读取游戏可变状态的提交动作。
    /// </summary>
    internal sealed class SqliteSingleWriter
    {
        private sealed class PendingWrite
        {
            public SqlSaveDomain Domain;
            public long Generation;
            public bool Coalescible;
            public Func<bool> Commit;
        }

        private readonly object _gate = new object();
        private readonly Queue<PendingWrite> _order = new Queue<PendingWrite>();
        private readonly Dictionary<SqlSaveDomain, PendingWrite> _pending = new Dictionary<SqlSaveDomain, PendingWrite>();
        private readonly Dictionary<SqlSaveDomain, long> _highestAcceptedGeneration = new Dictionary<SqlSaveDomain, long>();
        private readonly Dictionary<SqlSaveDomain, long> _lastCommittedGeneration = new Dictionary<SqlSaveDomain, long>();
        private Thread _thread;
        private bool _busy;
        private int _workerThreadId;
        private long _enqueuedCount;
        private long _mergedCount;
        private long _completedCount;
        private long _committedCount;
        private long _staleRejectedCount;

        internal int WorkerThreadId => Volatile.Read(ref _workerThreadId);
        internal long EnqueuedCount => Interlocked.Read(ref _enqueuedCount);
        internal long MergedCount => Interlocked.Read(ref _mergedCount);
        internal long CompletedCount => Interlocked.Read(ref _completedCount);
        internal long CommittedCount => Interlocked.Read(ref _committedCount);
        internal long StaleRejectedCount => Interlocked.Read(ref _staleRejectedCount);

        internal bool Enqueue(
            SqlSaveDomain domain,
            long generation,
            Func<bool> commit,
            bool coalescePending = true)
        {
            if (commit == null) throw new ArgumentNullException(nameof(commit));
            if (generation <= 0) throw new ArgumentOutOfRangeException(nameof(generation));

            lock (_gate)
            {
                Interlocked.Increment(ref _enqueuedCount);
                if (_highestAcceptedGeneration.TryGetValue(domain, out long highest) && generation <= highest)
                {
                    Interlocked.Increment(ref _staleRejectedCount);
                    return false;
                }

                _highestAcceptedGeneration[domain] = generation;
                if (coalescePending && _pending.TryGetValue(domain, out PendingWrite existing))
                {
                    existing.Generation = generation;
                    existing.Commit = commit;
                    Interlocked.Increment(ref _mergedCount);
                }
                else
                {
                    var write = new PendingWrite
                    {
                        Domain = domain,
                        Generation = generation,
                        Coalescible = coalescePending,
                        Commit = commit,
                    };
                    if (coalescePending)
                        _pending.Add(domain, write);
                    _order.Enqueue(write);
                }

                EnsureWorkerStarted();
                Monitor.PulseAll(_gate);
                return true;
            }
        }

        internal long GetLastCommittedGeneration(SqlSaveDomain domain)
        {
            lock (_gate)
                return _lastCommittedGeneration.TryGetValue(domain, out long generation) ? generation : 0;
        }

        internal void Drain()
        {
            lock (_gate)
            {
                while (_busy || _order.Count > 0)
                    Monitor.Wait(_gate);
            }
        }

        private void EnsureWorkerStarted()
        {
            if (_thread != null && _thread.IsAlive) return;

            _thread = new Thread(WorkLoop)
            {
                IsBackground = true,
                Name = "LyoCrystal SQLite 单写线程",
            };
            _thread.Start();
        }

        private void WorkLoop()
        {
            Volatile.Write(ref _workerThreadId, Thread.CurrentThread.ManagedThreadId);
            while (true)
            {
                PendingWrite write;
                lock (_gate)
                {
                    while (_order.Count == 0)
                    {
                        if (!Monitor.Wait(_gate, TimeSpan.FromSeconds(30)) && _order.Count == 0)
                        {
                            _thread = null;
                            Volatile.Write(ref _workerThreadId, 0);
                            return;
                        }
                    }

                    write = _order.Dequeue();
                    if (write.Coalescible &&
                        _pending.TryGetValue(write.Domain, out PendingWrite current) &&
                        ReferenceEquals(current, write))
                    {
                        _pending.Remove(write.Domain);
                    }
                    _busy = true;
                }

                try
                {
                    if (write.Commit())
                    {
                        lock (_gate)
                            _lastCommittedGeneration[write.Domain] = write.Generation;
                        Interlocked.Increment(ref _committedCount);
                    }
                }
                catch (Exception ex)
                {
                    MessageQueue.Instance.Enqueue($"[SQL:Sqlite] {write.Domain} 单写线程提交异常：{ex}");
                    SqlSaveResilience.ReportFailure(
                        DatabaseProviderKind.Sqlite,
                        write.Domain,
                        ex,
                        operation: "SqliteSingleWriter.Commit",
                        transient: SqlTransientDetector.IsTransient(DatabaseProviderKind.Sqlite, ex));
                }
                finally
                {
                    Interlocked.Increment(ref _completedCount);
                    lock (_gate)
                    {
                        _busy = false;
                        Monitor.PulseAll(_gate);
                    }
                }
            }
        }
    }
}
