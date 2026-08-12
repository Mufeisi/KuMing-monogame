using System.Collections.Concurrent;
using Microsoft.Data.Sqlite;
using Server.Persistence;
using Server.Persistence.Sql;
using Xunit;

namespace Base05.Tests;

public sealed class SqliteSingleWriterTests
{
    [Fact]
    public void Sqlite连接启用Wal和五秒忙等待()
    {
        string databasePath = Path.Combine(Path.GetTempPath(), $"base05-db01-pragma-{Guid.NewGuid():N}.db");
        try
        {
            using (SqlSession session = SqlSession.Open(
                       DatabaseProviderKind.Sqlite,
                       new SqlDatabaseOptions { SqlitePath = databasePath }))
            {
                using var journal = session.Connection.CreateCommand();
                journal.CommandText = "PRAGMA journal_mode;";
                Assert.Equal("wal", Convert.ToString(journal.ExecuteScalar()), ignoreCase: true);

                using var timeout = session.Connection.CreateCommand();
                timeout.CommandText = "PRAGMA busy_timeout;";
                Assert.Equal(SqlSession.SqliteBusyTimeoutMilliseconds, Convert.ToInt32(timeout.ExecuteScalar()));

                using var synchronous = session.Connection.CreateCommand();
                synchronous.CommandText = "PRAGMA synchronous;";
                Assert.Equal(2, Convert.ToInt32(synchronous.ExecuteScalar()));

                var builder = new SqliteConnectionStringBuilder(session.Connection.ConnectionString);
                Assert.Equal(SqliteCacheMode.Private, builder.Cache);
                Assert.Equal(5, builder.DefaultTimeout);
            }
        }
        finally
        {
            DeleteSqliteFiles(databasePath);
        }
    }

    [Fact]
    public void 单写线程串行提交并把同域待处理请求合并为最新一次()
    {
        var writer = new SqliteSingleWriter();
        using var firstStarted = new ManualResetEventSlim(false);
        using var releaseFirst = new ManualResetEventSlim(false);
        var executedValues = new ConcurrentQueue<int>();
        var threadIds = new ConcurrentBag<int>();
        int callerThreadId = Environment.CurrentManagedThreadId;
        int active = 0;
        int maximumActive = 0;

        void Execute(int value, bool block = false)
        {
            int current = Interlocked.Increment(ref active);
            UpdateMaximum(ref maximumActive, current);
            threadIds.Add(Environment.CurrentManagedThreadId);
            if (block)
            {
                firstStarted.Set();
                Assert.True(releaseFirst.Wait(TimeSpan.FromSeconds(5)));
            }
            executedValues.Enqueue(value);
            Interlocked.Decrement(ref active);
        }

        writer.Enqueue(SqlSaveDomain.Accounts, 1, Commit(() => Execute(1, block: true)));
        Assert.True(firstStarted.Wait(TimeSpan.FromSeconds(5)));

        for (int value = 2; value <= 101; value++)
        {
            int captured = value;
            writer.Enqueue(SqlSaveDomain.Guilds, captured, Commit(() => Execute(captured)));
        }
        writer.Enqueue(SqlSaveDomain.Goods, 102, Commit(() => Execute(200)));

        releaseFirst.Set();
        writer.Drain();

        Assert.Equal(1, maximumActive);
        Assert.Equal(new[] { 1, 101, 200 }, executedValues.ToArray());
        Assert.Equal(102, writer.EnqueuedCount);
        Assert.Equal(99, writer.MergedCount);
        Assert.Equal(3, writer.CompletedCount);
        Assert.Equal(3, writer.CommittedCount);
        Assert.NotEqual(callerThreadId, writer.WorkerThreadId);
        Assert.Single(threadIds.Distinct());
    }

    [Fact]
    public async Task Drain等待正在执行和排队的最后一次提交()
    {
        var writer = new SqliteSingleWriter();
        using var started = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);
        int committed = 0;

        writer.Enqueue(SqlSaveDomain.Accounts, 1, Commit(() =>
        {
            started.Set();
            Assert.True(release.Wait(TimeSpan.FromSeconds(5)));
            Interlocked.Increment(ref committed);
        }));
        writer.Enqueue(SqlSaveDomain.Conquests, 2, Commit(() => Interlocked.Increment(ref committed)));
        Assert.True(started.Wait(TimeSpan.FromSeconds(5)));

        Task drain = Task.Run(writer.Drain);
        await Task.Delay(100);
        Assert.False(drain.IsCompleted);
        release.Set();
        await drain.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, committed);
    }

    [Fact]
    public async Task Wal模式下单写事务进行时并发读取不死锁()
    {
        string databasePath = Path.Combine(Path.GetTempPath(), $"base05-db01-wal-{Guid.NewGuid():N}.db");
        var options = new SqlDatabaseOptions { SqlitePath = databasePath };
        var writer = new SqliteSingleWriter();
        using var writeStarted = new ManualResetEventSlim(false);
        using var releaseWrite = new ManualResetEventSlim(false);

        try
        {
            using (SqlSession setup = SqlSession.Open(DatabaseProviderKind.Sqlite, options))
            {
                using var command = setup.Connection.CreateCommand();
                command.CommandText = "CREATE TABLE db01_probe (id INTEGER PRIMARY KEY, value INTEGER NOT NULL); INSERT INTO db01_probe(value) VALUES (1);";
                command.ExecuteNonQuery();
            }

            writer.Enqueue(SqlSaveDomain.Accounts, 1, Commit(() =>
            {
                using SqlSession session = SqlSession.Open(DatabaseProviderKind.Sqlite, options);
                session.BeginTransaction();
                using var command = session.Connection.CreateCommand();
                command.Transaction = session.Transaction;
                command.CommandText = "INSERT INTO db01_probe(value) VALUES (2);";
                command.ExecuteNonQuery();
                writeStarted.Set();
                Assert.True(releaseWrite.Wait(TimeSpan.FromSeconds(5)));
                session.Commit();
            }));
            Assert.True(writeStarted.Wait(TimeSpan.FromSeconds(5)));

            Task[] readers = Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
            {
                for (int iteration = 0; iteration < 20; iteration++)
                {
                    using SqlSession session = SqlSession.Open(DatabaseProviderKind.Sqlite, options);
                    using var command = session.Connection.CreateCommand();
                    command.CommandText = "SELECT COUNT(*) FROM db01_probe;";
                    Assert.Equal(1L, Convert.ToInt64(command.ExecuteScalar()));
                }
            })).ToArray();

            await Task.WhenAll(readers).WaitAsync(TimeSpan.FromSeconds(10));
            releaseWrite.Set();
            writer.Drain();

            using SqlSession verify = SqlSession.Open(DatabaseProviderKind.Sqlite, options);
            using var verifyCommand = verify.Connection.CreateCommand();
            verifyCommand.CommandText = "SELECT COUNT(*) FROM db01_probe;";
            Assert.Equal(2L, Convert.ToInt64(verifyCommand.ExecuteScalar()));
        }
        finally
        {
            releaseWrite.Set();
            writer.Drain();
            DeleteSqliteFiles(databasePath);
        }
    }

    [Fact]
    public void 单写线程拒绝迟到旧代且只在事务成功时推进成功代次()
    {
        var writer = new SqliteSingleWriter();
        var committed = new ConcurrentQueue<long>();

        Assert.True(writer.Enqueue(SqlSaveDomain.Accounts, 20, () =>
        {
            committed.Enqueue(20);
            return true;
        }));
        writer.Drain();
        Assert.Equal(20, writer.GetLastCommittedGeneration(SqlSaveDomain.Accounts));

        Assert.False(writer.Enqueue(SqlSaveDomain.Accounts, 19, () =>
        {
            committed.Enqueue(19);
            return true;
        }));
        Assert.True(writer.Enqueue(SqlSaveDomain.Accounts, 21, () => false));
        writer.Drain();

        Assert.Equal(new long[] { 20 }, committed.ToArray());
        Assert.Equal(20, writer.GetLastCommittedGeneration(SqlSaveDomain.Accounts));
        Assert.Equal(1, writer.StaleRejectedCount);
        Assert.Equal(1, writer.CommittedCount);
    }

    [Fact]
    public void 增量归档写入不参与同域合并()
    {
        var writer = new SqliteSingleWriter();
        var committed = new ConcurrentQueue<long>();

        Assert.True(writer.Enqueue(SqlSaveDomain.Archive, 1, () =>
        {
            committed.Enqueue(1);
            return true;
        }, coalescePending: false));
        Assert.True(writer.Enqueue(SqlSaveDomain.Archive, 2, () =>
        {
            committed.Enqueue(2);
            return true;
        }, coalescePending: false));
        writer.Drain();

        Assert.Equal(new long[] { 1, 2 }, committed.ToArray());
        Assert.Equal(0, writer.MergedCount);
        Assert.Equal(2, writer.GetLastCommittedGeneration(SqlSaveDomain.Archive));
    }

    private static Func<bool> Commit(Action action)
    {
        return () =>
        {
            action();
            return true;
        };
    }

    private static void UpdateMaximum(ref int target, int value)
    {
        int current;
        while (value > (current = Volatile.Read(ref target)) &&
               Interlocked.CompareExchange(ref target, value, current) != current)
        {
        }
    }

    private static void DeleteSqliteFiles(string databasePath)
    {
        SqliteConnection.ClearAllPools();
        foreach (string suffix in new[] { string.Empty, "-wal", "-shm" })
        {
            string path = databasePath + suffix;
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
