using System.Diagnostics;
using System.Runtime.ExceptionServices;
using Microsoft.Data.Sqlite;

namespace Server.Persistence.Sql;

public sealed class SqliteRestoreResult
{
    public string BackupPath { get; init; } = string.Empty;
    public string TargetPath { get; init; } = string.Empty;
    public string RollbackPath { get; init; } = string.Empty;
    public string RollbackWalPath { get; init; } = string.Empty;
    public string RollbackShmPath { get; init; } = string.Empty;
    public long DurationMilliseconds { get; init; }
}

/// <summary>
/// SQLite 离线恢复接缝：先把强停 WAL 安全收敛为单文件旧库，再原子替换主库。
/// 任一进程中断点上的正式目标都保持为可独立打开的旧库或新库。
/// </summary>
public static class SqliteRestoreService
{
    public static SqliteRestoreResult Restore(string backupPath, string targetPath)
        => Restore(backupPath, targetPath, new SqliteRestoreOperations());

    internal static SqliteRestoreResult Restore(
        string backupPath,
        string targetPath,
        SqliteRestoreOperations operations)
    {
        ArgumentNullException.ThrowIfNull(operations);
        var stopwatch = Stopwatch.StartNew();
        if (string.IsNullOrWhiteSpace(backupPath))
            throw new InvalidOperationException("SQLite 恢复副本路径未提供");
        if (string.IsNullOrWhiteSpace(targetPath))
            throw new InvalidOperationException("SQLite 目标数据库路径未提供");
        string backup = Path.GetFullPath(backupPath);
        string target = Path.GetFullPath(targetPath);
        if (!File.Exists(backup))
            throw new FileNotFoundException("SQLite 恢复副本不存在", backup);
        if (string.Equals(backup, target, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("SQLite 恢复副本不能与目标数据库相同");
        if (File.Exists(backup + "-wal") || File.Exists(backup + "-shm"))
            throw new InvalidOperationException("SQLite 恢复副本必须是 DB-03 生成的独立主库文件，不能携带 WAL/SHM");

        operations.ValidateIntegrity(backup);

        string targetDirectory = Path.GetDirectoryName(target)
            ?? throw new InvalidOperationException("SQLite 目标数据库目录无效");
        if (File.Exists(targetDirectory))
            throw new InvalidOperationException("SQLite 目标数据库目录不能是现有文件");
        Directory.CreateDirectory(targetDirectory);

        string operationId = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff") + "-" + Guid.NewGuid().ToString("N")[..8];
        string partial = Path.Combine(targetDirectory, "." + Path.GetFileName(target) + ".restore-" + operationId + ".partial");
        string rollback = File.Exists(target) ? target + ".pre-restore-" + operationId : string.Empty;
        string rollbackPartial = string.IsNullOrEmpty(rollback) ? string.Empty : rollback + ".partial";
        bool targetPublished = false;

        try
        {
            operations.CopyAndFlush(backup, partial);
            operations.ValidateIntegrity(partial);
            EnsureTargetOffline(target);

            if (!string.IsNullOrEmpty(rollback))
            {
                operations.PrepareStandaloneTarget(target);
                operations.ValidateIntegrity(target);
                operations.CopyAndFlush(target, rollback);
                operations.ValidateIntegrity(rollback);
                DeleteStrict(target + "-wal", operations);
                DeleteStrict(target + "-shm", operations);
            }

            operations.BeforePublish();
            if (!string.IsNullOrEmpty(rollback))
                operations.Replace(partial, target, null);
            else
                operations.Move(partial, target);
            targetPublished = true;

            operations.ValidateIntegrity(target);
            stopwatch.Stop();
            return new SqliteRestoreResult
            {
                BackupPath = backup,
                TargetPath = target,
                RollbackPath = rollback,
                DurationMilliseconds = stopwatch.ElapsedMilliseconds,
            };
        }
        catch (Exception restoreFailure)
        {
            var rollbackFailures = new List<Exception>();
            if (targetPublished)
            {
                if (!string.IsNullOrEmpty(rollback) && File.Exists(rollback))
                {
                    TryRollbackStep(
                        "主库",
                        () =>
                        {
                            operations.CopyAndFlush(rollback, rollbackPartial);
                            operations.ValidateIntegrity(rollbackPartial);
                            operations.Replace(rollbackPartial, target, null);
                            operations.ValidateIntegrity(target);
                        },
                        rollbackFailures);
                }
                else
                {
                    TryRollbackStep("空环境目标库", () => DeleteStrict(target, operations), rollbackFailures);
                }
            }
            if (rollbackFailures.Count > 0)
            {
                var failures = new List<Exception> { restoreFailure };
                failures.AddRange(rollbackFailures);
                throw new AggregateException("SQLite 恢复失败且回滚不完整", failures);
            }
            ExceptionDispatchInfo.Capture(restoreFailure).Throw();
            throw;
        }
        finally
        {
            TryDelete(partial);
            TryDelete(rollbackPartial);
        }
    }

    internal static void PrepareStandaloneTarget(string target)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = target,
            Mode = SqliteOpenMode.ReadWrite,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
        };
        using var connection = new SqliteConnection(builder.ConnectionString);
        connection.Open();
        using (SqliteCommand checkpoint = connection.CreateCommand())
        {
            checkpoint.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            using SqliteDataReader reader = checkpoint.ExecuteReader();
            if (!reader.Read() || reader.GetInt32(0) != 0)
                throw new IOException("SQLite WAL checkpoint 未能排空，恢复中止");
        }
        using (SqliteCommand journal = connection.CreateCommand())
        {
            journal.CommandText = "PRAGMA journal_mode=DELETE;";
            string mode = Convert.ToString(journal.ExecuteScalar()) ?? string.Empty;
            if (!string.Equals(mode, "delete", StringComparison.OrdinalIgnoreCase))
                throw new IOException("SQLite 目标库未能切换为独立 DELETE journal 模式");
        }
    }

    internal static void CopyAndFlush(string source, string destination)
    {
        using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var output = new FileStream(
            destination,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            FileOptions.WriteThrough);
        input.CopyTo(output);
        output.Flush(flushToDisk: true);
    }

    private static void EnsureTargetOffline(string target)
    {
        if (!File.Exists(target)) return;
        try
        {
            using var stream = new FileStream(target, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        catch (Exception ex)
        {
            throw new IOException("SQLite 目标数据库正在使用，恢复必须在服务器停止后执行", ex);
        }
    }

    private static void DeleteStrict(string path, SqliteRestoreOperations operations)
    {
        if (!File.Exists(path)) return;
        operations.Delete(path);
        if (File.Exists(path))
            throw new IOException($"SQLite 文件删除后仍然存在：{path}");
    }

    private static void TryRollbackStep(string name, Action action, ICollection<Exception> failures)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            failures.Add(new IOException($"SQLite {name}回滚失败", ex));
        }
    }

    private static void TryDelete(string path)
    {
        try { if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path); } catch { }
    }
}

internal sealed class SqliteRestoreOperations
{
    internal Action<string> ValidateIntegrity { get; init; } = SqliteBackupService.ValidateIntegrity;
    internal Action<string> PrepareStandaloneTarget { get; init; } = SqliteRestoreService.PrepareStandaloneTarget;
    internal Action<string, string> CopyAndFlush { get; init; } = SqliteRestoreService.CopyAndFlush;
    internal Action BeforePublish { get; init; } = () => { };
    internal Action<string, string> Move { get; init; } = (source, destination) => File.Move(source, destination);
    internal Action<string, string, string> Replace { get; init; } =
        (source, destination, backup) => File.Replace(source, destination, backup, ignoreMetadataErrors: true);
    internal Action<string> Delete { get; init; } = File.Delete;
}
