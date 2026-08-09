using System.Diagnostics;

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
/// SQLite 离线恢复接缝：校验副本、同目录原子替换，并保留被替换数据库及强停 sidecar 作为回滚工件。
/// </summary>
public static class SqliteRestoreService
{
    public static SqliteRestoreResult Restore(string backupPath, string targetPath)
    {
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

        SqliteBackupService.ValidateIntegrity(backup);

        string targetDirectory = Path.GetDirectoryName(target)
            ?? throw new InvalidOperationException("SQLite 目标数据库目录无效");
        if (File.Exists(targetDirectory))
            throw new InvalidOperationException("SQLite 目标数据库目录不能是现有文件");
        Directory.CreateDirectory(targetDirectory);

        string operationId = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff") + "-" + Guid.NewGuid().ToString("N")[..8];
        string partial = Path.Combine(targetDirectory, "." + Path.GetFileName(target) + ".restore-" + operationId + ".partial");
        string rollback = File.Exists(target) ? target + ".pre-restore-" + operationId : string.Empty;
        string wal = target + "-wal";
        string shm = target + "-shm";
        string rollbackWal = File.Exists(wal) ? wal + ".pre-restore-" + operationId : string.Empty;
        string rollbackShm = File.Exists(shm) ? shm + ".pre-restore-" + operationId : string.Empty;
        bool targetPublished = false;
        bool walMoved = false;
        bool shmMoved = false;

        try
        {
            CopyAndFlush(backup, partial);
            SqliteBackupService.ValidateIntegrity(partial);
            EnsureTargetOffline(target);

            if (!string.IsNullOrEmpty(rollbackWal))
            {
                File.Move(wal, rollbackWal);
                walMoved = true;
            }
            if (!string.IsNullOrEmpty(rollbackShm))
            {
                File.Move(shm, rollbackShm);
                shmMoved = true;
            }

            if (!string.IsNullOrEmpty(rollback))
                File.Replace(partial, target, rollback, ignoreMetadataErrors: true);
            else
                File.Move(partial, target);
            targetPublished = true;

            SqliteBackupService.ValidateIntegrity(target);
            stopwatch.Stop();
            return new SqliteRestoreResult
            {
                BackupPath = backup,
                TargetPath = target,
                RollbackPath = rollback,
                RollbackWalPath = rollbackWal,
                RollbackShmPath = rollbackShm,
                DurationMilliseconds = stopwatch.ElapsedMilliseconds,
            };
        }
        catch
        {
            if (targetPublished)
            {
                if (!string.IsNullOrEmpty(rollback) && File.Exists(rollback))
                    File.Replace(rollback, target, destinationBackupFileName: null, ignoreMetadataErrors: true);
                else
                    TryDelete(target);
            }
            if (shmMoved && File.Exists(rollbackShm) && !File.Exists(shm))
                File.Move(rollbackShm, shm);
            if (walMoved && File.Exists(rollbackWal) && !File.Exists(wal))
                File.Move(rollbackWal, wal);
            throw;
        }
        finally
        {
            TryDelete(partial);
        }
    }

    private static void CopyAndFlush(string source, string destination)
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

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
        }
    }
}
