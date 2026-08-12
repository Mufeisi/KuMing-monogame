using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;

namespace Server.Persistence.Sql;

[JsonConverter(typeof(JsonStringEnumConverter))]
internal enum SqliteBackupState
{
    Idle,
    Running,
    Succeeded,
    Failed,
}

internal sealed class SqliteBackupOptions
{
    internal string SourcePath { get; init; }
    internal string BackupDirectory { get; init; }
    internal string OffsiteDirectory { get; init; }
    internal int RetentionCount { get; init; } = 48;
    internal TimeSpan Interval { get; init; } = TimeSpan.FromHours(1);

    internal static SqliteBackupOptions FromSettings() => new SqliteBackupOptions
    {
        SourcePath = Settings.SqlitePath,
        BackupDirectory = Settings.SqliteBackupDirectory,
        OffsiteDirectory = Settings.SqliteBackupOffsiteDirectory,
        RetentionCount = Settings.SqliteBackupRetentionCount,
        Interval = TimeSpan.FromMinutes(Settings.SqliteBackupIntervalMinutes),
    };

    internal void Validate(bool requireOffsite)
    {
        if (string.IsNullOrWhiteSpace(SourcePath))
            throw new InvalidOperationException("SQLite 备份源路径未配置");
        if (string.IsNullOrWhiteSpace(BackupDirectory))
            throw new InvalidOperationException("SQLite 本地备份目录未配置");
        if (RetentionCount < 1 || RetentionCount > 10000)
            throw new InvalidOperationException("SQLite 备份保留数量必须在 1～10000 之间");
        if (Interval < TimeSpan.FromMinutes(1) || Interval > TimeSpan.FromDays(7))
            throw new InvalidOperationException("SQLite 自动备份间隔必须在 1 分钟～7 天之间");
        if (requireOffsite && string.IsNullOrWhiteSpace(OffsiteDirectory))
            throw new InvalidOperationException("正式服 SQLite 备份必须配置异地副本目录");

        string source = Path.GetFullPath(SourcePath);
        string local = Path.GetFullPath(BackupDirectory);
        if (File.Exists(local))
            throw new InvalidOperationException("SQLite 本地备份目录不能是现有文件");
        if (IsFileSystemRoot(local))
            throw new InvalidOperationException("SQLite 本地备份目录不得是文件系统根目录");
        if (string.Equals(source, local, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("SQLite 备份源文件与本地备份目录不能相同");

        if (!string.IsNullOrWhiteSpace(OffsiteDirectory))
        {
            string offsite = Path.GetFullPath(OffsiteDirectory);
            if (File.Exists(offsite))
                throw new InvalidOperationException("SQLite 异地副本目录不能是现有文件");
            if (IsFileSystemRoot(offsite))
                throw new InvalidOperationException("SQLite 异地副本目录不得是文件系统根目录");
            if (IsSameOrNested(local, offsite) || IsSameOrNested(offsite, local))
                throw new InvalidOperationException("SQLite 本地备份目录与异地副本目录不能相同或互相嵌套");
            if (requireOffsite)
                ValidateOffsiteSeparation(local, offsite);
        }
    }

    internal static void ValidateOffsiteSeparation(string localPath, string offsitePath)
    {
        if (string.IsNullOrWhiteSpace(localPath) || string.IsNullOrWhiteSpace(offsitePath))
            throw new InvalidOperationException("SQLite 本地与异地副本路径必须完整提供");
        string local = Path.GetFullPath(localPath);
        string offsite = Path.GetFullPath(offsitePath);
        if (IsSameOrNested(local, offsite) || IsSameOrNested(offsite, local))
            throw new InvalidOperationException("SQLite 本地备份与异地副本不能相同或互相嵌套");
        if (!IsUncPath(offsitePath) &&
            string.Equals(Path.GetPathRoot(local), Path.GetPathRoot(offsite), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("正式服 SQLite 异地副本必须使用 UNC 路径或与本地备份不同的存储卷");
    }

    private static bool IsUncPath(string path) =>
        !string.IsNullOrWhiteSpace(path) &&
        (path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal));

    private static bool IsSameOrNested(string parent, string candidate)
    {
        string normalizedParent = Path.TrimEndingDirectorySeparator(parent) + Path.DirectorySeparatorChar;
        string normalizedCandidate = Path.TrimEndingDirectorySeparator(candidate) + Path.DirectorySeparatorChar;
        return normalizedCandidate.StartsWith(normalizedParent, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsFileSystemRoot(string path) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(path),
            Path.TrimEndingDirectorySeparator(Path.GetPathRoot(path) ?? string.Empty),
            StringComparison.OrdinalIgnoreCase);
}

internal sealed class SqliteBackupStatus
{
    public SqliteBackupState State { get; init; }
    public string Trigger { get; init; } = string.Empty;
    public DateTimeOffset? LastAttemptUtc { get; init; }
    public DateTimeOffset? LastSuccessUtc { get; init; }
    public long LastDurationMilliseconds { get; init; }
    public string LastLocalPath { get; init; } = string.Empty;
    public string LastOffsitePath { get; init; } = string.Empty;
    public string IntegrityResult { get; init; } = string.Empty;
    public string LastError { get; init; } = string.Empty;
}

/// <summary>
/// SQLite 在线备份深模块：在线一致性复制、副本完整性检查、原子发布、保留清理和状态持久化。
/// </summary>
internal sealed class SqliteBackupService : IDisposable
{
    private const string BackupPattern = "lyocrystal-sqlite-*.db";
    private const string StatusFileName = "backup-status.json";
    private readonly object _gate = new object();
    private readonly ManualResetEventSlim _idle = new ManualResetEventSlim(initialState: true);
    private readonly SqliteBackupOptions _options;
    private readonly Action<FileInfo> _deleteBackup;
    private readonly Action<string> _deleteProbe;
    private Timer _timer;
    private bool _running;
    private bool _disposed;
    private long _backupSequence;
    private SqliteBackupStatus _status;
    internal string SourcePath => Path.GetFullPath(_options.SourcePath);

    internal SqliteBackupService(
        SqliteBackupOptions options,
        Action<FileInfo> deleteBackup = null,
        Action<string> deleteProbe = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate(requireOffsite: false);
        _deleteBackup = deleteBackup ?? (file => file.Delete());
        _deleteProbe = deleteProbe ?? File.Delete;
        EnsureDirectoryWritable(Path.GetFullPath(_options.BackupDirectory), "本地备份目录");
        if (!string.IsNullOrWhiteSpace(_options.OffsiteDirectory))
            EnsureDirectoryWritable(Path.GetFullPath(_options.OffsiteDirectory), "异地副本目录");
        _status = LoadStatus() ?? new SqliteBackupStatus { State = SqliteBackupState.Idle };
        PersistStatus(_status, throwOnFailure: true);
    }

    internal SqliteBackupStatus GetStatus()
    {
        lock (_gate)
            return CopyStatus(_status);
    }

    internal void StartAutomatic()
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_timer != null) return;
            _timer = new Timer(_ =>
            {
                try
                {
                    TryQueueBackup("automatic");
                }
                catch (ObjectDisposedException)
                {
                }
            }, null, _options.Interval, _options.Interval);

            // 首份备份是 StartAutomatic 的启动语义，不能依赖线程池何时调度零延迟 Timer。
            TryQueueBackup("automatic");
        }
    }

    internal bool WaitForIdle(TimeSpan timeout) => _idle.Wait(timeout);

    internal bool TryQueueBackup(string trigger)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_running) return false;
            _running = true;
            _idle.Reset();
            _status = BeginStatus(trigger);
            PersistStatus(_status);
        }

        ThreadPool.QueueUserWorkItem(_ => ExecuteBackup());
        return true;
    }

    internal SqliteBackupStatus RunNow(string trigger = "test")
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_running) throw new InvalidOperationException("SQLite 备份正在执行");
            _running = true;
            _idle.Reset();
            _status = BeginStatus(trigger);
            PersistStatus(_status);
        }

        ExecuteBackup();
        return GetStatus();
    }

    private SqliteBackupStatus BeginStatus(string trigger) => new SqliteBackupStatus
    {
        State = SqliteBackupState.Running,
        Trigger = string.IsNullOrWhiteSpace(trigger) ? "unknown" : trigger.Trim(),
        LastAttemptUtc = DateTimeOffset.UtcNow,
        LastSuccessUtc = _status.LastSuccessUtc,
        LastLocalPath = _status.LastLocalPath,
        LastOffsitePath = _status.LastOffsitePath,
    };

    private void ExecuteBackup()
    {
        var stopwatch = Stopwatch.StartNew();
        string localPath = string.Empty;
        string offsitePath = string.Empty;
        try
        {
            (localPath, offsitePath) = CreateValidatedBackup();
            stopwatch.Stop();
            Complete(new SqliteBackupStatus
            {
                State = SqliteBackupState.Succeeded,
                Trigger = GetStatus().Trigger,
                LastAttemptUtc = GetStatus().LastAttemptUtc,
                LastSuccessUtc = DateTimeOffset.UtcNow,
                LastDurationMilliseconds = stopwatch.ElapsedMilliseconds,
                LastLocalPath = localPath,
                LastOffsitePath = offsitePath,
                IntegrityResult = "ok",
            });
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            SqliteBackupStatus current = GetStatus();
            Complete(new SqliteBackupStatus
            {
                State = SqliteBackupState.Failed,
                Trigger = current.Trigger,
                LastAttemptUtc = current.LastAttemptUtc,
                LastSuccessUtc = current.LastSuccessUtc,
                LastDurationMilliseconds = stopwatch.ElapsedMilliseconds,
                LastLocalPath = string.IsNullOrEmpty(localPath) ? current.LastLocalPath : localPath,
                LastOffsitePath = string.IsNullOrEmpty(offsitePath) ? current.LastOffsitePath : offsitePath,
                IntegrityResult = "failed",
                LastError = ex.GetType().Name + ": " + ex.Message,
            });
            MessageQueue.Instance.Enqueue($"[SQLite备份] 失败：{ex}");
        }
    }

    private (string LocalPath, string OffsitePath) CreateValidatedBackup()
    {
        string sourcePath = Path.GetFullPath(_options.SourcePath);
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("SQLite 备份源文件不存在", sourcePath);

        string localDirectory = Path.GetFullPath(_options.BackupDirectory);
        Directory.CreateDirectory(localDirectory);
        long sequence = Interlocked.Increment(ref _backupSequence);
        string fileName = $"lyocrystal-sqlite-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss-fff}-{sequence:D6}-{Guid.NewGuid():N}.db";
        string partialPath = Path.Combine(localDirectory, fileName + ".partial");
        string localPath = Path.Combine(localDirectory, fileName);

        try
        {
            BackupDatabase(sourcePath, partialPath);
            ValidateIntegrity(partialPath);
            File.Move(partialPath, localPath);
            RecordRunningPaths(localPath, string.Empty);
        }
        finally
        {
            TryDelete(partialPath);
        }

        ApplyRetention(localDirectory);

        string offsitePath = string.Empty;
        if (!string.IsNullOrWhiteSpace(_options.OffsiteDirectory))
        {
            string offsiteDirectory = Path.GetFullPath(_options.OffsiteDirectory);
            Directory.CreateDirectory(offsiteDirectory);
            string offsitePartial = Path.Combine(offsiteDirectory, fileName + ".partial");
            offsitePath = Path.Combine(offsiteDirectory, fileName);
            try
            {
                File.Copy(localPath, offsitePartial, overwrite: false);
                ValidateIntegrity(offsitePartial);
                File.Move(offsitePartial, offsitePath);
                RecordRunningPaths(localPath, offsitePath);
            }
            finally
            {
                TryDelete(offsitePartial);
            }
            ApplyRetention(offsiteDirectory);
        }

        return (localPath, offsitePath);
    }

    private static void BackupDatabase(string sourcePath, string destinationPath)
    {
        var sourceBuilder = new SqliteConnectionStringBuilder
        {
            DataSource = sourcePath,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
        };
        var destinationBuilder = new SqliteConnectionStringBuilder
        {
            DataSource = destinationPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
        };

        using var source = new SqliteConnection(sourceBuilder.ConnectionString);
        using var destination = new SqliteConnection(destinationBuilder.ConnectionString);
        source.Open();
        destination.Open();
        source.BackupDatabase(destination);
    }

    internal static void ValidateIntegrity(string databasePath)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(databasePath),
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
        };
        using var connection = new SqliteConnection(builder.ConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA integrity_check;";
        using var reader = command.ExecuteReader();
        var errors = new List<string>();
        while (reader.Read())
        {
            string result = reader.GetString(0);
            if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
                errors.Add(result);
        }
        if (errors.Count > 0)
            throw new InvalidDataException("SQLite 备份副本完整性检查失败：" + string.Join("；", errors));
    }

    private void ApplyRetention(string directory)
    {
        FileInfo[] backups = new DirectoryInfo(directory)
            .GetFiles(BackupPattern, SearchOption.TopDirectoryOnly)
            .OrderByDescending(file => file.Name, StringComparer.Ordinal)
            .ToArray();
        for (int i = _options.RetentionCount; i < backups.Length; i++)
            _deleteBackup(backups[i]);
    }

    private void Complete(SqliteBackupStatus status)
    {
        lock (_gate)
        {
            _status = status;
            PersistStatus(status);
            _running = false;
            _idle.Set();
        }
    }

    private void RecordRunningPaths(string localPath, string offsitePath)
    {
        lock (_gate)
        {
            if (!_running) return;
            _status = new SqliteBackupStatus
            {
                State = _status.State,
                Trigger = _status.Trigger,
                LastAttemptUtc = _status.LastAttemptUtc,
                LastSuccessUtc = _status.LastSuccessUtc,
                LastLocalPath = localPath,
                LastOffsitePath = offsitePath,
            };
            PersistStatus(_status);
        }
    }

    private SqliteBackupStatus LoadStatus()
    {
        try
        {
            string path = Path.Combine(Path.GetFullPath(_options.BackupDirectory), StatusFileName);
            SqliteBackupStatus status = File.Exists(path)
                ? JsonSerializer.Deserialize<SqliteBackupStatus>(File.ReadAllText(path))
                : null;
            if (File.Exists(path) && status == null)
                throw new InvalidDataException("备份状态文件内容为空");
            if (status?.State != SqliteBackupState.Running) return status;
            return new SqliteBackupStatus
            {
                State = SqliteBackupState.Failed,
                Trigger = status.Trigger,
                LastAttemptUtc = status.LastAttemptUtc,
                LastSuccessUtc = status.LastSuccessUtc,
                LastDurationMilliseconds = status.LastDurationMilliseconds,
                LastLocalPath = status.LastLocalPath,
                LastOffsitePath = status.LastOffsitePath,
                IntegrityResult = "failed",
                LastError = "Interrupted: 上次备份在进程退出前未完成",
            };
        }
        catch (Exception ex)
        {
            return new SqliteBackupStatus
            {
                State = SqliteBackupState.Failed,
                IntegrityResult = "unknown",
                LastError = "StatusCorrupted: 备份状态文件读取失败：" + ex.GetType().Name + ": " + ex.Message,
            };
        }
    }

    private void PersistStatus(SqliteBackupStatus status, bool throwOnFailure = false)
    {
        try
        {
            string directory = Path.GetFullPath(_options.BackupDirectory);
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, StatusFileName);
            string temporaryPath = path + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(status));
            File.Move(temporaryPath, path, overwrite: true);
        }
        catch (Exception ex)
        {
            if (throwOnFailure)
                throw new IOException("SQLite 备份状态文件无法写入", ex);
            MessageQueue.Instance.Enqueue($"[SQLite备份] 状态文件写入失败：{ex.Message}");
        }
    }

    private void EnsureDirectoryWritable(string directory, string displayName)
    {
        if (File.Exists(directory))
            throw new InvalidOperationException($"{displayName}不能是现有文件");
        Directory.CreateDirectory(directory);
        string probe = Path.Combine(directory, ".lyocrystal-backup-write-probe-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var stream = new FileStream(
                       probe,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 1,
                       FileOptions.WriteThrough))
            {
                stream.WriteByte(1);
                stream.Flush(flushToDisk: true);
            }
            _deleteProbe(probe);
            if (File.Exists(probe))
                throw new IOException("探针文件删除后仍然存在");
        }
        catch (Exception ex)
        {
            TryDelete(probe);
            throw new IOException($"{displayName}不可写或不可删除", ex);
        }
    }

    private static SqliteBackupStatus CopyStatus(SqliteBackupStatus status) => new SqliteBackupStatus
    {
        State = status.State,
        Trigger = status.Trigger,
        LastAttemptUtc = status.LastAttemptUtc,
        LastSuccessUtc = status.LastSuccessUtc,
        LastDurationMilliseconds = status.LastDurationMilliseconds,
        LastLocalPath = status.LastLocalPath,
        LastOffsitePath = status.LastOffsitePath,
        IntegrityResult = status.IntegrityResult,
        LastError = status.LastError,
    };

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

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(SqliteBackupService));
    }

    public void Dispose()
    {
        bool waitForBackup;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _timer?.Dispose();
            _timer = null;
            waitForBackup = _running;
        }
        if (waitForBackup) _idle.Wait();
    }
}
