using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Server.Persistence.Sql;

internal enum MySqlSwitchDecision
{
    MaintainSqlite,
    PlanMySqlMigration,
}

internal sealed class MySqlSwitchMetrics
{
    public int PeakConcurrentPlayers { get; init; }
    public int ConsecutiveOnlineDays { get; init; }
    public long DatabaseBytes { get; init; }
    public int ConsecutiveDatabaseSizeDays { get; init; }
    public double SaveCommitP95Milliseconds { get; init; }
    public int ConsecutiveSlowSaveDays { get; init; }
    public int SaveFailuresPerHour { get; init; }
    public int ConsecutiveFailureHours { get; init; }
}

internal sealed class MySqlSwitchAssessment
{
    internal MySqlSwitchDecision Decision { get; }
    internal IReadOnlyList<string> TriggeredReasons { get; }

    private MySqlSwitchAssessment(MySqlSwitchDecision decision, IReadOnlyList<string> triggeredReasons)
    {
        Decision = decision;
        TriggeredReasons = triggeredReasons;
    }

    internal static MySqlSwitchAssessment Create(
        MySqlSwitchDecision decision,
        IReadOnlyList<string> triggeredReasons) => new MySqlSwitchAssessment(decision, triggeredReasons);
}

internal sealed class MySqlMigrationAuthorization
{
    public int FormatVersion { get; init; }
    public DateTimeOffset CreatedUtc { get; init; }
    public MySqlSwitchMetrics Metrics { get; init; } = new MySqlSwitchMetrics();
    public string BackupTrigger { get; init; } = string.Empty;
    public string SourcePath { get; init; } = string.Empty;
    public string LocalBackupPath { get; init; } = string.Empty;
    public string OffsiteBackupPath { get; init; } = string.Empty;
    public string LocalSha256 { get; init; } = string.Empty;
    public string OffsiteSha256 { get; init; } = string.Empty;
}

/// <summary>
/// DB-06 的数据库选型门禁。这里只作切换决策、迁移前备份与正式 provider 授权，不执行数据迁移。
/// </summary>
internal static class MySqlSwitchPolicy
{
    private const int AuthorizationFormatVersion = 1;
    private static readonly byte[] AuthorizationEntropy =
        Encoding.UTF8.GetBytes("LyoCrystal.DB06.MySqlMigrationAuthorization.v1");
    internal const int PeakConcurrentPlayersThreshold = 500;
    internal const int RequiredOnlineDays = 7;
    internal const long DatabaseBytesThreshold = 10L * 1024 * 1024 * 1024;
    internal const int RequiredDatabaseSizeDays = 3;
    internal const double SaveCommitP95MillisecondsThreshold = 750D;
    internal const int RequiredSlowSaveDays = 3;
    internal const int SaveFailuresPerHourThreshold = 3;
    internal const int RequiredFailureHours = 3;
    internal const string MigrationBackupTriggerPrefix = "mysql-migration-preflight:";

    internal static string DefaultAuthorizationPath =>
        Path.GetFullPath(Settings.SqlitePath) + ".mysql-switch-authorization.dpapi";

    internal static MySqlSwitchAssessment Assess(MySqlSwitchMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        var reasons = new List<string>();

        if (metrics.PeakConcurrentPlayers >= PeakConcurrentPlayersThreshold &&
            metrics.ConsecutiveOnlineDays >= RequiredOnlineDays)
            reasons.Add($"连续{RequiredOnlineDays}天峰值在线数达到{PeakConcurrentPlayersThreshold}");

        if (metrics.DatabaseBytes >= DatabaseBytesThreshold &&
            metrics.ConsecutiveDatabaseSizeDays >= RequiredDatabaseSizeDays)
            reasons.Add($"连续{RequiredDatabaseSizeDays}天数据库大小达到10 GiB");

        if (metrics.SaveCommitP95Milliseconds >= SaveCommitP95MillisecondsThreshold &&
            metrics.ConsecutiveSlowSaveDays >= RequiredSlowSaveDays)
            reasons.Add($"连续{RequiredSlowSaveDays}天保存提交P95达到{SaveCommitP95MillisecondsThreshold:0}ms");

        if (metrics.SaveFailuresPerHour >= SaveFailuresPerHourThreshold &&
            metrics.ConsecutiveFailureHours >= RequiredFailureHours)
            reasons.Add($"连续{RequiredFailureHours}小时每小时保存失败达到{SaveFailuresPerHourThreshold}次");

        return MySqlSwitchAssessment.Create(
            reasons.Count == 0 ? MySqlSwitchDecision.MaintainSqlite : MySqlSwitchDecision.PlanMySqlMigration,
            reasons.AsReadOnly());
    }

    internal static SqliteBackupStatus CreateRequiredPreMigrationBackup(
        MySqlSwitchMetrics metrics,
        SqliteBackupService backupService,
        string authorizationPath = null,
        string expectedSourcePath = null)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        MySqlSwitchAssessment assessment = Assess(metrics);
        if (assessment.Decision != MySqlSwitchDecision.PlanMySqlMigration)
            throw new InvalidOperationException("MySQL 切换门槛尚未触发，应继续使用 SQLite");
        ArgumentNullException.ThrowIfNull(backupService);
        string sourcePath = Path.GetFullPath(expectedSourcePath ?? Settings.SqlitePath);
        if (!string.Equals(backupService.SourcePath, sourcePath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("迁移前备份源库与当前待切换 SQLite 主库不一致");

        string trigger = MigrationBackupTriggerPrefix + Guid.NewGuid().ToString("N");
        SqliteBackupStatus status = backupService.RunNow(trigger);
        if (status.State != SqliteBackupState.Succeeded ||
            !string.Equals(status.Trigger, trigger, StringComparison.Ordinal) ||
            !string.Equals(status.IntegrityResult, "ok", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("迁移前 SQLite 备份未成功完成");

        ValidatePublishedCopies(status.LastLocalPath, status.LastOffsitePath);
        var authorization = new MySqlMigrationAuthorization
        {
            FormatVersion = AuthorizationFormatVersion,
            CreatedUtc = DateTimeOffset.UtcNow,
            Metrics = CloneMetrics(metrics),
            BackupTrigger = trigger,
            SourcePath = sourcePath,
            LocalBackupPath = Path.GetFullPath(status.LastLocalPath),
            OffsiteBackupPath = Path.GetFullPath(status.LastOffsitePath),
            LocalSha256 = ComputeSha256(status.LastLocalPath),
            OffsiteSha256 = ComputeSha256(status.LastOffsitePath),
        };
        PersistAuthorization(authorizationPath ?? DefaultAuthorizationPath, authorization);
        return status;
    }

    internal static void EnsureProviderSelectionAuthorized(
        string authorizationPath = null,
        string expectedSourcePath = null)
    {
        string path = Path.GetFullPath(authorizationPath ?? DefaultAuthorizationPath);
        if (!File.Exists(path))
            throw new InvalidOperationException("MySQL provider 未经 DB-06 门槛与迁移前备份授权，必须继续使用 SQLite");

        MySqlMigrationAuthorization authorization;
        try
        {
            if (!OperatingSystem.IsWindows())
                throw new PlatformNotSupportedException("MySQL 迁移授权只支持 Windows DPAPI");
            byte[] protectedPayload = File.ReadAllBytes(path);
            byte[] payload = ProtectedData.Unprotect(
                protectedPayload,
                AuthorizationEntropy,
                DataProtectionScope.CurrentUser);
            authorization = JsonSerializer.Deserialize<MySqlMigrationAuthorization>(payload);
        }
        catch (Exception ex)
        {
            throw new InvalidDataException("MySQL 迁移授权记录无法读取", ex);
        }
        string expectedSource = Path.GetFullPath(expectedSourcePath ?? Settings.SqlitePath);
        if (authorization == null ||
            authorization.FormatVersion != AuthorizationFormatVersion ||
            !string.Equals(Path.GetFullPath(authorization.SourcePath), expectedSource, StringComparison.OrdinalIgnoreCase) ||
            Assess(authorization.Metrics).Decision != MySqlSwitchDecision.PlanMySqlMigration ||
            string.IsNullOrWhiteSpace(authorization.BackupTrigger) ||
            !authorization.BackupTrigger.StartsWith(MigrationBackupTriggerPrefix, StringComparison.Ordinal))
            throw new InvalidDataException("MySQL 迁移授权记录未包含有效门槛判定");

        ValidatePublishedCopies(authorization.LocalBackupPath, authorization.OffsiteBackupPath);
        ValidateHash(authorization.LocalBackupPath, authorization.LocalSha256, "本地");
        ValidateHash(authorization.OffsiteBackupPath, authorization.OffsiteSha256, "异地");
    }

    private static void ValidatePublishedCopies(string localPath, string offsitePath)
    {
        ValidatePublishedCopy(localPath, "本地");
        ValidatePublishedCopy(offsitePath, "异地");
        SqliteBackupOptions.ValidateOffsiteSeparation(localPath, offsitePath);
    }

    private static void ValidatePublishedCopy(string path, string displayName)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            throw new InvalidOperationException($"迁移前{displayName}备份副本不存在");
        SqliteBackupService.ValidateIntegrity(path);
    }

    private static void ValidateHash(string path, string expected, string displayName)
    {
        string actual = ComputeSha256(path);
        if (string.IsNullOrWhiteSpace(expected) || !string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"迁移前{displayName}备份副本摘要不匹配");
    }

    private static string ComputeSha256(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static void PersistAuthorization(string path, MySqlMigrationAuthorization authorization)
    {
        string fullPath = Path.GetFullPath(path);
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("MySQL 迁移授权只支持 Windows DPAPI");
        string directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("MySQL 迁移授权目录无效");
        Directory.CreateDirectory(directory);
        string temporaryPath = fullPath + ".tmp";
        byte[] payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(authorization));
        byte[] protectedPayload = ProtectedData.Protect(
            payload,
            AuthorizationEntropy,
            DataProtectionScope.CurrentUser);
        try
        {
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.Create,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 4096,
                       FileOptions.WriteThrough))
            {
                stream.Write(protectedPayload);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); } catch { }
        }
    }

    private static MySqlSwitchMetrics CloneMetrics(MySqlSwitchMetrics metrics) => new MySqlSwitchMetrics
    {
        PeakConcurrentPlayers = metrics.PeakConcurrentPlayers,
        ConsecutiveOnlineDays = metrics.ConsecutiveOnlineDays,
        DatabaseBytes = metrics.DatabaseBytes,
        ConsecutiveDatabaseSizeDays = metrics.ConsecutiveDatabaseSizeDays,
        SaveCommitP95Milliseconds = metrics.SaveCommitP95Milliseconds,
        ConsecutiveSlowSaveDays = metrics.ConsecutiveSlowSaveDays,
        SaveFailuresPerHour = metrics.SaveFailuresPerHour,
        ConsecutiveFailureHours = metrics.ConsecutiveFailureHours,
    };
}
