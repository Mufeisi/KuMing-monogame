using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Shared.Release;

public sealed class TransactionalFileDeploymentEntry
{
    public required string SourcePath { get; init; }
    public required string TargetPath { get; init; }
}

public sealed class TransactionalFileDeploymentResult
{
    public int PublishedFileCount { get; init; }
    public bool Verified { get; init; }
}

public static class TransactionalFileDeployment
{
    public const string JournalFileName = "deployment-journal.json";
    public const string JournalFormat = "lyocrystal-file-deployment-v1";
    public const string ProcessLockFileName = "deployment.lock";
    private static readonly UTF8Encoding Utf8NoBom = new(false, true);
    private static readonly object Gate = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true,
    };

    public static TransactionalFileDeploymentResult Apply(
        string transactionRoot,
        IEnumerable<string> allowedTargetRoots,
        IEnumerable<TransactionalFileDeploymentEntry> entries)
    {
        return Apply(transactionRoot, allowedTargetRoots, entries, static () => true);
    }

    public static TransactionalFileDeploymentResult Apply(
        string transactionRoot,
        IEnumerable<string> allowedTargetRoots,
        IEnumerable<TransactionalFileDeploymentEntry> entries,
        Func<bool> verifyAfterPublish)
    {
        lock (Gate)
            return ApplyCore(transactionRoot, allowedTargetRoots, entries, verifyAfterPublish);
    }

    private static TransactionalFileDeploymentResult ApplyCore(
        string transactionRoot,
        IEnumerable<string> allowedTargetRoots,
        IEnumerable<TransactionalFileDeploymentEntry> entries,
        Func<bool> verifyAfterPublish)
    {
        string normalizedTransactionRoot = NormalizeDirectory(transactionRoot, nameof(transactionRoot));
        string[] normalizedAllowedRoots = NormalizeAllowedRoots(allowedTargetRoots);
        List<DeploymentJournalEntry> normalizedEntries = NormalizeEntries(entries, normalizedAllowedRoots);
        if (normalizedEntries.Count == 0)
            throw new ArgumentException("事务发布没有文件。", nameof(entries));

        Directory.CreateDirectory(normalizedTransactionRoot);
        using FileStream processLock = AcquireProcessLock(normalizedTransactionRoot);
        RecoverIncompleteWhileLocked(normalizedTransactionRoot, normalizedAllowedRoots);

        string transactionDirectory = Path.Combine(normalizedTransactionRoot, "txn-" + Guid.NewGuid().ToString("N"));
        string backupDirectory = Path.Combine(transactionDirectory, "backups");
        Directory.CreateDirectory(backupDirectory);
        var journal = new DeploymentJournal
        {
            Format = JournalFormat,
            Status = "Preparing",
            Entries = normalizedEntries,
        };

        try
        {
            for (int i = 0; i < journal.Entries.Count; i++)
            {
                DeploymentJournalEntry entry = journal.Entries[i];
                entry.Existed = File.Exists(entry.TargetPath);
                entry.BackupPath = Path.Combine(backupDirectory, i.ToString("D6") + ".bak");
                if (entry.Existed)
                    CopyAndFlush(entry.TargetPath, entry.BackupPath);
            }

            journal.Status = "Prepared";
            WriteJournal(transactionDirectory, journal);
            journal.Status = "Applying";
            WriteJournal(transactionDirectory, journal);

            for (int i = 0; i < journal.Entries.Count; i++)
                PublishFile(journal.Entries[i].SourcePath, journal.Entries[i].TargetPath);

            bool verified = verifyAfterPublish.Invoke();
            if (!verified)
                throw new InvalidDataException("事务发布后的验证未通过。");

            journal.Status = "Committed";
            WriteJournal(transactionDirectory, journal);
            TryDeleteDirectory(transactionDirectory);
            return new TransactionalFileDeploymentResult
            {
                PublishedFileCount = journal.Entries.Count,
                Verified = true,
            };
        }
        catch (Exception primary)
        {
            if (string.Equals(journal.Status, "Preparing", StringComparison.Ordinal))
            {
                if (Directory.Exists(transactionDirectory)) Directory.Delete(transactionDirectory, recursive: true);
                throw;
            }
            try
            {
                Rollback(transactionDirectory, journal, normalizedAllowedRoots);
            }
            catch (Exception rollback)
            {
                throw new AggregateException("事务发布失败，且上一可运行版本未能完整恢复。", primary, rollback);
            }
            throw;
        }
    }

    public static int RecoverIncomplete(string transactionRoot, IEnumerable<string> allowedTargetRoots)
    {
        lock (Gate)
            return RecoverIncompleteCore(transactionRoot, allowedTargetRoots);
    }

    private static int RecoverIncompleteCore(string transactionRoot, IEnumerable<string> allowedTargetRoots)
    {
        string normalizedTransactionRoot = NormalizeDirectory(transactionRoot, nameof(transactionRoot));
        string[] normalizedAllowedRoots = NormalizeAllowedRoots(allowedTargetRoots);
        if (!Directory.Exists(normalizedTransactionRoot))
            return 0;

        using FileStream processLock = AcquireProcessLock(normalizedTransactionRoot);

        return RecoverIncompleteWhileLocked(normalizedTransactionRoot, normalizedAllowedRoots);
    }

    private static int RecoverIncompleteWhileLocked(string normalizedTransactionRoot, string[] normalizedAllowedRoots)
    {

        int recovered = 0;
        foreach (string directory in Directory.GetDirectories(normalizedTransactionRoot, "txn-*", SearchOption.TopDirectoryOnly))
        {
            string journalPath = Path.Combine(directory, JournalFileName);
            if (!File.Exists(journalPath))
            {
                Directory.Delete(directory, recursive: true);
                continue;
            }

            DeploymentJournal journal = JsonSerializer.Deserialize<DeploymentJournal>(File.ReadAllText(journalPath), JsonOptions)
                ?? throw new InvalidDataException("事务日志为空：" + journalPath);
            ValidateJournal(journal, normalizedAllowedRoots, directory);
            if (string.Equals(journal.Status, "Committed", StringComparison.Ordinal))
            {
                Directory.Delete(directory, recursive: true);
                continue;
            }

            Rollback(directory, journal, normalizedAllowedRoots);
            recovered++;
        }
        return recovered;
    }

    private static FileStream AcquireProcessLock(string transactionRoot)
    {
        Directory.CreateDirectory(transactionRoot);
        string lockPath = Path.Combine(transactionRoot, ProcessLockFileName);
        DateTime deadline = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            try
            {
                return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                System.Threading.Thread.Sleep(50);
            }
        }
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
        catch
        {
            // Committed 是不可逆提交点；残留只由下次 RecoverIncomplete 清理，不能伪报发布失败。
        }
    }

    private static void Rollback(string transactionDirectory, DeploymentJournal journal, IReadOnlyCollection<string> allowedRoots)
    {
        ValidateJournal(journal, allowedRoots, transactionDirectory);
        var failures = new List<Exception>();
        for (int i = journal.Entries.Count - 1; i >= 0; i--)
        {
            DeploymentJournalEntry entry = journal.Entries[i];
            try
            {
                if (entry.Existed)
                {
                    if (!File.Exists(entry.BackupPath))
                        throw new FileNotFoundException("事务备份不存在。", entry.BackupPath);
                    PublishFile(entry.BackupPath, entry.TargetPath);
                }
                else if (File.Exists(entry.TargetPath))
                {
                    File.Delete(entry.TargetPath);
                    if (File.Exists(entry.TargetPath))
                        throw new IOException("回滚后目标文件仍存在：" + entry.TargetPath);
                }
            }
            catch (Exception ex)
            {
                failures.Add(new IOException("回滚文件失败：" + entry.TargetPath, ex));
            }
        }

        if (failures.Count > 0)
            throw new AggregateException(failures);
        Directory.Delete(transactionDirectory, recursive: true);
    }

    private static void PublishFile(string sourcePath, string targetPath)
    {
        string directory = Path.GetDirectoryName(targetPath)
            ?? throw new InvalidDataException("目标文件没有父目录：" + targetPath);
        if (string.IsNullOrWhiteSpace(directory))
            throw new InvalidDataException("目标文件没有父目录：" + targetPath);
        Directory.CreateDirectory(directory);
        string partial = targetPath + ".release-partial-" + Guid.NewGuid().ToString("N");
        try
        {
            CopyAndFlush(sourcePath, partial);
            File.Move(partial, targetPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(partial)) File.Delete(partial);
        }
    }

    private static void CopyAndFlush(string sourcePath, string targetPath)
    {
        string directory = Path.GetDirectoryName(targetPath) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        using FileStream source = new(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using FileStream target = new(targetPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.WriteThrough);
        source.CopyTo(target);
        target.Flush(flushToDisk: true);
    }

    private static void WriteJournal(string transactionDirectory, DeploymentJournal journal)
    {
        string path = Path.Combine(transactionDirectory, JournalFileName);
        string partial = path + ".partial-" + Guid.NewGuid().ToString("N");
        byte[] bytes = Utf8NoBom.GetBytes(JsonSerializer.Serialize(journal, JsonOptions) + "\n");
        try
        {
            using (FileStream stream = new(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(partial, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(partial)) File.Delete(partial);
        }
    }

    private static List<DeploymentJournalEntry> NormalizeEntries(
        IEnumerable<TransactionalFileDeploymentEntry> entries,
        IReadOnlyCollection<string> allowedRoots)
    {
        var result = new List<DeploymentJournalEntry>();
        var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (TransactionalFileDeploymentEntry entry in entries ?? Array.Empty<TransactionalFileDeploymentEntry>())
        {
            string source = Path.GetFullPath(entry?.SourcePath ?? string.Empty);
            string target = Path.GetFullPath(entry?.TargetPath ?? string.Empty);
            if (!File.Exists(source)) throw new FileNotFoundException("事务源文件不存在。", source);
            EnsureAllowedTarget(target, allowedRoots);
            if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("事务源文件不能与目标相同：" + target);
            if (!targets.Add(target)) throw new InvalidDataException("事务包含重复目标：" + target);
            result.Add(new DeploymentJournalEntry { SourcePath = source, TargetPath = target });
        }
        return result.OrderBy(item => item.TargetPath, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string[] NormalizeAllowedRoots(IEnumerable<string> roots)
    {
        string[] normalized = (roots ?? Array.Empty<string>())
            .Where(root => !string.IsNullOrWhiteSpace(root))
            .Select(root => NormalizeDirectory(root, nameof(roots)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (normalized.Length == 0) throw new ArgumentException("至少需要一个允许的目标根目录。", nameof(roots));
        return normalized;
    }

    private static string NormalizeDirectory(string path, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("目录路径为空。", parameterName);
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }

    private static void EnsureAllowedTarget(string target, IEnumerable<string> allowedRoots)
    {
        foreach (string root in allowedRoots)
        {
            if (string.Equals(target, root, StringComparison.OrdinalIgnoreCase) ||
                target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return;
        }
        throw new UnauthorizedAccessException("事务目标越出允许根目录：" + target);
    }

    private static void ValidateJournal(DeploymentJournal journal, IReadOnlyCollection<string> allowedRoots, string transactionDirectory)
    {
        if (!string.Equals(journal.Format, JournalFormat, StringComparison.Ordinal))
            throw new InvalidDataException("事务日志格式无效。");
        if (journal.Status is not ("Prepared" or "Applying" or "Committed"))
            throw new InvalidDataException("事务日志状态无效。");
        if (journal.Entries == null || journal.Entries.Count == 0)
            throw new InvalidDataException("事务日志没有文件项。");
        string normalizedTransactionDirectory = NormalizeDirectory(transactionDirectory, nameof(transactionDirectory));
        var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (DeploymentJournalEntry entry in journal.Entries)
        {
            entry.TargetPath = Path.GetFullPath(entry.TargetPath ?? string.Empty);
            entry.BackupPath = Path.GetFullPath(entry.BackupPath ?? string.Empty);
            EnsureAllowedTarget(entry.TargetPath, allowedRoots);
            if (!targets.Add(entry.TargetPath)) throw new InvalidDataException("事务日志包含重复目标。");
            if (!entry.BackupPath.StartsWith(normalizedTransactionDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("事务备份越出事务目录。");
        }
    }

    private sealed class DeploymentJournal
    {
        public string Format { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public List<DeploymentJournalEntry> Entries { get; set; } = new();
    }

    private sealed class DeploymentJournalEntry
    {
        public string SourcePath { get; set; } = string.Empty;
        public string TargetPath { get; set; } = string.Empty;
        public string BackupPath { get; set; } = string.Empty;
        public bool Existed { get; set; }
    }
}
