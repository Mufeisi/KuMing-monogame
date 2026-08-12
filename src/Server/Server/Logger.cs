using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Server
{
    public enum LogType
    {
        Server,
        Chat,
        Debug,
        Player,
        Spawn
    }

    public enum LogLevel
    {
        Debug,
        Info,
        Warn,
        Error,
        Fatal
    }

    /// <summary>
    /// The server logging seam.  It intentionally keeps the small API used by
    /// the legacy server while the implementation remains independent of the
    /// log4net configuration used by the old WinForms host.
    /// </summary>
    public interface ILog
    {
        bool IsDebugEnabled { get; }
        bool IsInfoEnabled { get; }
        bool IsWarnEnabled { get; }
        bool IsErrorEnabled { get; }
        bool IsFatalEnabled { get; }

        void Debug(object message);
        void Debug(object message, Exception exception);
        void Info(object message);
        void Info(object message, Exception exception);
        void Warn(object message);
        void Warn(object message, Exception exception);
        void Error(object message);
        void Error(object message, Exception exception);
        void Error(Exception exception);
        void Fatal(object message);
        void Fatal(object message, Exception exception);
        void Fatal(Exception exception);
    }

    public sealed class LoggerOptions
    {
        public string Directory { get; set; } = @".\Logs";
        public int MaxFileSizeMB { get; set; } = 10;
        public int RetentionDays { get; set; } = 14;

        internal LoggerOptions CloneAndNormalize()
        {
            return new LoggerOptions
            {
                Directory = string.IsNullOrWhiteSpace(Directory) ? @".\Logs" : Directory.Trim(),
                MaxFileSizeMB = Math.Max(1, MaxFileSizeMB),
                RetentionDays = Math.Max(1, RetentionDays)
            };
        }
    }

    /// <summary>
    /// Bounded asynchronous logger. Debug/Info are non-blocking and may be
    /// dropped under pressure; Error/Fatal always take the synchronous
    /// emergency path before attempting the normal queue.
    /// </summary>
    public static class Logger
    {
        private const int QueueCapacity = 4096;
        private const int NormalLockTimeoutMilliseconds = 250;
        private const int EmergencyLockTimeoutMilliseconds = 250;

        private static readonly object Sync = new object();
        private static readonly object CompletionSync = new object();
        private static readonly HashSet<long> CompletedOutOfOrder = new HashSet<long>();
        private static readonly BlockingCollection<LogEntry> Queue =
            new BlockingCollection<LogEntry>(new ConcurrentQueue<LogEntry>(), QueueCapacity);
        private static readonly Dictionary<LogType, ILog> Facades = new Dictionary<LogType, ILog>();
        private static readonly Thread Worker;
        private static readonly FileLogSink Sink;
        private static int ShutdownStarted;
        private static long DroppedDebug;
        private static long DroppedInfo;
        private static long NextSequence;
        private static long ContiguousCompletedSequence;

        static Logger()
        {
            Sink = new FileLogSink(new LoggerOptions());
            Worker = new Thread(ProcessQueue)
            {
                IsBackground = true,
                Name = "Server.Logger"
            };
            Worker.Start();

            AppDomain.CurrentDomain.ProcessExit += (_, __) => Shutdown(TimeSpan.FromSeconds(2));
        }

        public static long DroppedDebugCount => Interlocked.Read(ref DroppedDebug);
        public static long DroppedInfoCount => Interlocked.Read(ref DroppedInfo);
        public static int PendingCount => Queue.Count;

        public static ILog GetLogger(LogType type = LogType.Server)
        {
            lock (Sync)
            {
                ILog logger;
                if (!Facades.TryGetValue(type, out logger))
                {
                    logger = new LoggerFacade(type);
                    Facades[type] = logger;
                }

                return logger;
            }
        }

        public static void Configure(LoggerOptions options)
        {
            if (options == null)
                return;

            lock (Sync)
                Sink.Configure(options);
        }

        public static void Flush(TimeSpan? timeout = null)
        {
            var limit = timeout ?? TimeSpan.FromSeconds(2);
            var deadline = DateTime.UtcNow + limit;
            var targetSequence = Volatile.Read(ref NextSequence);

            WaitForCompletion(targetSequence, deadline);

            var remaining = deadline - DateTime.UtcNow;
            if (remaining > TimeSpan.Zero)
                Sink.Flush(remaining);
        }

        public static void Shutdown(TimeSpan? timeout = null)
        {
            var limit = timeout ?? TimeSpan.FromSeconds(2);
            var deadline = DateTime.UtcNow + limit;
            if (Interlocked.Exchange(ref ShutdownStarted, 1) == 0)
            {
                Queue.CompleteAdding();

                if (Thread.CurrentThread != Worker)
                {
                    var remaining = deadline - DateTime.UtcNow;
                    if (remaining > TimeSpan.Zero)
                        Worker.Join(remaining);
                }

                var remainingAfterJoin = deadline - DateTime.UtcNow;
                if (remainingAfterJoin > TimeSpan.Zero)
                    Sink.Flush(remainingAfterJoin);
            }
            else if (Thread.CurrentThread != Worker && Worker.IsAlive)
            {
                Worker.Join(limit);
            }
        }

        private static void ProcessQueue()
        {
            try
            {
                foreach (var entry in Queue.GetConsumingEnumerable())
                {
                    try
                    {
                        Sink.Write(entry);
                    }
                    finally
                    {
                        MarkCompleted(entry.Sequence);
                    }
                }
            }
            catch (Exception ex)
            {
                // The emergency sink is deliberately independent of this
                // worker. It is the last line of defense if the queue fails.
                try { Queue.CompleteAdding(); } catch { }
                Sink.WriteEmergency(new LogEntry(0, DateTime.UtcNow, LogType.Server, LogLevel.Fatal,
                    "Logger worker stopped unexpectedly", ex));

                while (Queue.TryTake(out var pending))
                {
                    Sink.WriteEmergency(pending);
                    MarkCompleted(pending.Sequence);
                }
            }
        }

        private static void Write(LogType type, LogLevel level, object message, Exception exception)
        {
            var entry = new LogEntry(Interlocked.Increment(ref NextSequence), DateTime.UtcNow,
                type, level, ToMessage(message), exception);

            if (level == LogLevel.Error || level == LogLevel.Fatal)
                Sink.WriteEmergency(entry);

            if (level == LogLevel.Debug || level == LogLevel.Info)
            {
                if (!TryEnqueue(entry))
                {
                    IncrementDrop(level);
                    MarkCompleted(entry.Sequence);
                }

                return;
            }

            if (TryEnqueue(entry))
                return;

            // Warnings and errors are not silently discarded. If shutdown or
            // saturation closes the queue, write the normal record inline.
            if (level != LogLevel.Error && level != LogLevel.Fatal)
            {
                Sink.Write(entry);
                MarkCompleted(entry.Sequence);
            }
            else
            {
                // The emergency record was already written synchronously.
                MarkCompleted(entry.Sequence);
            }
        }

        private static bool TryEnqueue(LogEntry entry)
        {
            if (Queue.IsAddingCompleted)
                return false;

            try
            {
                return Queue.TryAdd(entry);
            }
            catch (InvalidOperationException)
            {
                // CompleteAdding may race with a producer during shutdown.
                return false;
            }
        }

        private static void IncrementDrop(LogLevel level)
        {
            if (level == LogLevel.Debug)
                Interlocked.Increment(ref DroppedDebug);
            else if (level == LogLevel.Info)
                Interlocked.Increment(ref DroppedInfo);
        }

        private static void WaitForCompletion(long targetSequence, DateTime deadline)
        {
            if (targetSequence <= 0)
                return;

            lock (CompletionSync)
            {
                while (ContiguousCompletedSequence < targetSequence)
                {
                    var remaining = deadline - DateTime.UtcNow;
                    if (remaining <= TimeSpan.Zero)
                        return;

                    Monitor.Wait(CompletionSync, remaining);
                }
            }
        }

        private static void MarkCompleted(long sequence)
        {
            if (sequence <= 0)
                return;

            lock (CompletionSync)
            {
                if (sequence <= ContiguousCompletedSequence)
                    return;

                CompletedOutOfOrder.Add(sequence);
                while (CompletedOutOfOrder.Remove(ContiguousCompletedSequence + 1))
                    ContiguousCompletedSequence++;

                Monitor.PulseAll(CompletionSync);
            }
        }

        private static string ToMessage(object message)
        {
            if (message == null)
                return string.Empty;

            try
            {
                return Convert.ToString(message, CultureInfo.InvariantCulture) ?? string.Empty;
            }
            catch (Exception ex)
            {
                return "[message conversion failed: " + ex.GetType().Name + "]";
            }
        }

        private sealed class LoggerFacade : ILog
        {
            private readonly LogType Type;

            public LoggerFacade(LogType type)
            {
                Type = type;
            }

            public bool IsDebugEnabled => Volatile.Read(ref ShutdownStarted) == 0;
            public bool IsInfoEnabled => Volatile.Read(ref ShutdownStarted) == 0;
            public bool IsWarnEnabled => true;
            public bool IsErrorEnabled => true;
            public bool IsFatalEnabled => true;

            public void Debug(object message) => Write(Type, LogLevel.Debug, message, null);
            public void Debug(object message, Exception exception) => Write(Type, LogLevel.Debug, message, exception);
            public void Info(object message) => Write(Type, LogLevel.Info, message, null);
            public void Info(object message, Exception exception) => Write(Type, LogLevel.Info, message, exception);
            public void Warn(object message) => Write(Type, LogLevel.Warn, message, null);
            public void Warn(object message, Exception exception) => Write(Type, LogLevel.Warn, message, exception);
            public void Error(object message) => Write(Type, LogLevel.Error, message, null);
            public void Error(object message, Exception exception) => Write(Type, LogLevel.Error, message, exception);
            public void Error(Exception exception) => Write(Type, LogLevel.Error, exception == null ? string.Empty : exception.Message, exception);
            public void Fatal(object message) => Write(Type, LogLevel.Fatal, message, null);
            public void Fatal(object message, Exception exception) => Write(Type, LogLevel.Fatal, message, exception);
            public void Fatal(Exception exception) => Write(Type, LogLevel.Fatal, exception == null ? string.Empty : exception.Message, exception);
        }

        private readonly struct LogEntry
        {
            public readonly long Sequence;
            public readonly DateTime TimestampUtc;
            public readonly LogType Type;
            public readonly LogLevel Level;
            public readonly string Message;
            public readonly Exception Exception;

            public LogEntry(long sequence, DateTime timestampUtc, LogType type, LogLevel level, string message, Exception exception)
            {
                Sequence = sequence;
                TimestampUtc = timestampUtc;
                Type = type;
                Level = level;
                Message = message;
                Exception = exception;
            }
        }

        private sealed class FileLogSink
        {
            private static readonly Regex SensitiveKey = new Regex(
                @"(?<prefix>[""']?(?:\b(?:password|passwd|pwd|token|access[_-]?token|refresh[_-]?token|secret|authorization|api[_-]?key|bearer|username|user|account|email|phone|player(?:\s*name)?|role(?:\s*(?:id|name))?|character(?:\s*(?:id|name))?|session(?:[_-]?id)?)\b|玩家(?:名|名称)?|角色(?:名|名称)?|账号|帐号|账户(?:目录为)?|用户名|用户|人物(?:名|名称)?|游戏管理员|会话(?:编号|ID|标识)?)['""]?\s*[:=：]\s*)(?<value>""[^""]*""|'[^']*'|[^\s,;，；}\]]+)",
                RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            private static readonly Regex ChineseLabelWithSpace = new Regex(
                @"(?<prefix>(?:玩家|角色|账号|帐号|账户|用户|人物)\s+)(?<value>[^\s,，。:：;；]+)",
                RegexOptions.Compiled | RegexOptions.CultureInvariant);
            private static readonly Regex BareSessionId = new Regex(
                @"(?<!\d)\d+(?=\s*断开连接\s*客户端版本错误|,\s*[^\s,]+客户端版本匹配|,\s*[^\s,]+正在(?:创建新账户|更改密码|登录)|,\s*[^\s,]+已登录服务器)",
                RegexOptions.Compiled | RegexOptions.CultureInvariant);
            private static readonly Regex BearerToken = new Regex(
                @"\bBearer\s+[A-Za-z0-9._~+/=-]+",
                RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            private static readonly Regex Email = new Regex(
                @"\b[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}\b",
                RegexOptions.Compiled | RegexOptions.CultureInvariant);
            private static readonly Regex Ipv4Address = new Regex(
                @"(?<![\d.])(?:\d{1,3}\.){3}\d{1,3}(?![\d.])",
                RegexOptions.Compiled | RegexOptions.CultureInvariant);
            private static readonly Regex Ipv6Candidate = new Regex(
                @"(?<![0-9A-Za-z:])(?:[0-9A-Fa-f]{0,4}:){2,8}[0-9A-Fa-f:.]{0,19}(?:%[0-9A-Za-z_.-]+)?(?![0-9A-Za-z:])",
                RegexOptions.Compiled | RegexOptions.CultureInvariant);
            private static readonly Regex OwnedLogFile = new Regex(
                @"^(?:Server|Chat|Debug|Player|Spawn)(?:-emergency)?-\d{8}(?:\.\d+)?\.log$",
                RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            private readonly object SinkSync = new object();
            private readonly object EmergencySync = new object();
            private readonly object FallbackSync = new object();
            private readonly object RetentionSync = new object();
            private LoggerOptions Options;
            private DateTime LastRetentionUtc = DateTime.MinValue;

            public FileLogSink(LoggerOptions options)
            {
                Options = options.CloneAndNormalize();
            }

            public void Configure(LoggerOptions options)
            {
                lock (SinkSync)
                    Options = options.CloneAndNormalize();
            }

            public bool Flush(TimeSpan timeout)
            {
                if (!Monitor.TryEnter(SinkSync, timeout < TimeSpan.Zero ? TimeSpan.Zero : timeout))
                    return false;

                try
                {
                    // Files are opened and closed per record. Taking the
                    // normal sink lock waits for an active writer without
                    // allowing Shutdown to block indefinitely.
                    return true;
                }
                finally
                {
                    Monitor.Exit(SinkSync);
                }
            }

            public void Write(LogEntry entry)
            {
                var line = Format(entry);
                if (!Monitor.TryEnter(SinkSync, TimeSpan.FromMilliseconds(NormalLockTimeoutMilliseconds)))
                {
                    WriteEmergency(entry, line, new TimeoutException("normal logger sink lock timeout"));
                    return;
                }

                try
                {
                    WriteLocked(entry.Type, entry.TimestampUtc, line, false, SnapshotOptions());
                }
                catch (Exception sinkException)
                {
                    WriteEmergency(entry, line, sinkException);
                }
                finally
                {
                    Monitor.Exit(SinkSync);
                }
            }

            public bool WriteEmergency(LogEntry entry)
            {
                return WriteEmergency(entry, Format(entry), null);
            }

            private bool WriteEmergency(LogEntry entry, string line, Exception writeFailure)
            {
                if (!Monitor.TryEnter(EmergencySync, TimeSpan.FromMilliseconds(EmergencyLockTimeoutMilliseconds)))
                {
                    return WriteFallback(line, writeFailure ?? new TimeoutException("emergency logger sink lock timeout"));
                }

                try
                {
                    try
                    {
                        WriteEmergencyLocked(entry.Type, entry.TimestampUtc, line, SnapshotOptions());
                        return true;
                    }
                    catch (Exception sinkException)
                    {
                        return WriteFallback(line, sinkException);
                    }
                }
                finally
                {
                    Monitor.Exit(EmergencySync);
                }
            }

            private string Format(LogEntry entry)
            {
                var message = FilterPii(entry.Message ?? string.Empty);

                var builder = new StringBuilder();
                builder.Append(entry.TimestampUtc.ToLocalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz", CultureInfo.InvariantCulture));
                builder.Append(" [").Append(entry.Level).Append("] [").Append(entry.Type).Append("] ");
                builder.Append(message);

                if (entry.Exception != null)
                {
                    builder.AppendLine();
                    string exceptionText;
                    try { exceptionText = entry.Exception.ToString(); }
                    catch (Exception exceptionError) { exceptionText = exceptionError.GetType().Name; }
                    builder.Append(FilterPii(exceptionText));
                }

                return builder.ToString();
            }

            private void WriteLocked(LogType type, DateTime timestampUtc, string line, bool emergency, LoggerOptions options)
            {
                var root = GetRootPath(options);
                EnsureDirectory(root);
                var date = timestampUtc.ToLocalTime().ToString("yyyyMMdd", CultureInfo.InvariantCulture);
                var category = type.ToString();
                var directory = Path.Combine(root, category);
                EnsureDirectory(directory);

                var prefix = category + (emergency ? "-emergency-" : "-") + date;
                var path = Path.Combine(directory, prefix + ".log");
                var bytes = Encoding.UTF8.GetBytes(line + Environment.NewLine);
                RotateIfNeeded(path, bytes.Length, directory, prefix, options);
                AppendBytes(path, bytes, false);

                CleanupRetention(root, options);
            }

            private void WriteEmergencyLocked(LogType type, DateTime timestampUtc, string line, LoggerOptions options)
            {
                var root = GetRootPath(options);
                EnsureDirectory(root);
                var date = timestampUtc.ToLocalTime().ToString("yyyyMMdd", CultureInfo.InvariantCulture);
                var category = type.ToString();
                var directory = Path.Combine(root, category);
                EnsureDirectory(directory);

                var prefix = category + "-emergency-" + date;
                var path = Path.Combine(directory, prefix + ".log");
                var bytes = Encoding.UTF8.GetBytes(line + Environment.NewLine);
                RotateIfNeeded(path, bytes.Length, directory, prefix, options);
                // Emergency writes use a separate path, lock and durable
                // FileStream so a blocked normal sink cannot delay them.
                AppendBytes(path, bytes, true);

                CleanupRetention(root, options);
            }

            private void RotateIfNeeded(string path, int incomingBytes, string directory, string prefix, LoggerOptions options)
            {
                var maxBytes = (long)options.MaxFileSizeMB * 1024L * 1024L;
                if (maxBytes <= 0 || !File.Exists(path))
                    return;

                if (IsReparsePoint(path))
                    throw new IOException("refusing to rotate a reparse-point log file");

                var length = new FileInfo(path).Length;
                if (length + incomingBytes <= maxBytes)
                    return;

                for (var index = 1; index < 10000; index++)
                {
                    var rotated = Path.Combine(directory, prefix + "." + index + ".log");
                    if (File.Exists(rotated))
                    {
                        if (IsReparsePoint(rotated))
                            throw new IOException("refusing to overwrite a reparse-point rotated log file");
                        continue;
                    }

                    File.Move(path, rotated);
                    return;
                }
            }

            private void CleanupRetention(string root, LoggerOptions options)
            {
                if (!Monitor.TryEnter(RetentionSync, TimeSpan.FromMilliseconds(25)))
                    return;

                try
                {
                    var now = DateTime.UtcNow;
                    if (now - LastRetentionUtc < TimeSpan.FromMinutes(1))
                        return;

                    if (!Directory.Exists(root) || IsReparsePoint(root))
                        return;

                    var cutoff = now - TimeSpan.FromDays(options.RetentionDays);
                    foreach (var type in Enum.GetValues<LogType>())
                    {
                        var directory = Path.Combine(root, type.ToString());
                        if (!Directory.Exists(directory) || IsReparsePoint(directory))
                            continue;

                        string[] files;
                        try { files = Directory.GetFiles(directory, "*.log", SearchOption.TopDirectoryOnly); }
                        catch { continue; }

                        foreach (var file in files)
                        {
                            if (IsReparsePoint(file) || !OwnedLogFile.IsMatch(Path.GetFileName(file)))
                                continue;

                            try
                            {
                                if (File.GetLastWriteTimeUtc(file) < cutoff)
                                    File.Delete(file);
                            }
                            catch
                            {
                                // Retention is best effort and must never
                                // interrupt a live server because another
                                // process has a file open.
                            }
                        }
                    }

                    LastRetentionUtc = now;
                }
                finally
                {
                    Monitor.Exit(RetentionSync);
                }
            }

            private static bool IsReparsePoint(string path)
            {
                try { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; }
                catch { return true; }
            }

            private static void EnsureDirectory(string path)
            {
                Directory.CreateDirectory(path);
                if (IsReparsePoint(path))
                    throw new IOException("refusing to write through a reparse-point log directory");
            }

            private static void AppendBytes(string path, byte[] bytes, bool durable)
            {
                using (var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite, 4096, FileOptions.SequentialScan))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(durable);
                }
            }

            private LoggerOptions SnapshotOptions()
            {
                return Volatile.Read(ref Options);
            }

            private static string GetRootPath(LoggerOptions options)
            {
                try
                {
                    return Path.GetFullPath(options.Directory);
                }
                catch
                {
                    return Path.GetFullPath(@".\Logs");
                }
            }

            private bool WriteFallback(string line, Exception sinkException)
            {
                if (!Monitor.TryEnter(FallbackSync, TimeSpan.FromMilliseconds(EmergencyLockTimeoutMilliseconds)))
                {
                    try { Console.Error.WriteLine(line); } catch { }
                    return false;
                }

                try
                {
                    var root = GetRootPath(SnapshotOptions());
                    EnsureDirectory(root);
                    var fallback = Path.Combine(root, "Logger-fallback.log");
                    var text = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) + " " + line + Environment.NewLine +
                               "SinkError: " + FilterPii(sinkException == null ? string.Empty : sinkException.ToString()) + Environment.NewLine;
                    AppendBytes(fallback, Encoding.UTF8.GetBytes(text), true);
                    return true;
                }
                catch
                {
                    try { Console.Error.WriteLine(line); } catch { }
                    return false;
                }
                finally
                {
                    Monitor.Exit(FallbackSync);
                }
            }

            private static string FilterPii(string value)
            {
                value = SensitiveKey.Replace(value, "${prefix}***");
                value = ChineseLabelWithSpace.Replace(value, "${prefix}***");
                value = BareSessionId.Replace(value, "<redacted-session>");
                value = BearerToken.Replace(value, "Bearer ***");
                value = Email.Replace(value, "<redacted-email>");
                value = Ipv4Address.Replace(value, "<redacted-ip>");
                return Ipv6Candidate.Replace(value, match =>
                {
                    var candidate = match.Value;
                    var zoneIndex = candidate.IndexOf('%');
                    var address = zoneIndex >= 0 ? candidate.Substring(0, zoneIndex) : candidate;
                    return IPAddress.TryParse(address, out var parsed) && parsed.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
                        ? "<redacted-ip>"
                        : candidate;
                });
            }
        }
    }
}
