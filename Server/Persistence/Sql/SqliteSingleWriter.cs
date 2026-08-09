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
            public Action Commit;
        }

        private readonly object _gate = new object();
        private readonly Queue<SqlSaveDomain> _order = new Queue<SqlSaveDomain>();
        private readonly Dictionary<SqlSaveDomain, PendingWrite> _pending = new Dictionary<SqlSaveDomain, PendingWrite>();
        private Thread _thread;
        private bool _busy;
        private int _workerThreadId;
        private long _enqueuedCount;
        private long _mergedCount;
        private long _completedCount;

        internal int WorkerThreadId => Volatile.Read(ref _workerThreadId);
        internal long EnqueuedCount => Interlocked.Read(ref _enqueuedCount);
        internal long MergedCount => Interlocked.Read(ref _mergedCount);
        internal long CompletedCount => Interlocked.Read(ref _completedCount);

        internal void Enqueue(SqlSaveDomain domain, Action commit)
        {
            if (commit == null) throw new ArgumentNullException(nameof(commit));

            lock (_gate)
            {
                Interlocked.Increment(ref _enqueuedCount);
                if (_pending.TryGetValue(domain, out PendingWrite existing))
                {
                    existing.Commit = commit;
                    Interlocked.Increment(ref _mergedCount);
                }
                else
                {
                    _pending.Add(domain, new PendingWrite { Domain = domain, Commit = commit });
                    _order.Enqueue(domain);
                }

                EnsureWorkerStarted();
                Monitor.PulseAll(_gate);
            }
        }

        internal void Drain()
        {
            lock (_gate)
            {
                while (_busy || _pending.Count > 0)
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

                    SqlSaveDomain domain = _order.Dequeue();
                    write = _pending[domain];
                    _pending.Remove(domain);
                    _busy = true;
                }

                try
                {
                    write.Commit();
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
