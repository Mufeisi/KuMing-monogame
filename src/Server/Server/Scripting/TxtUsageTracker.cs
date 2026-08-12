using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace Server.Scripting
{
    public static class TxtUsageTracker
    {
        private sealed class Entry
        {
            public readonly string Key;
            public readonly string RelativePath;

            public long ReadCount;
            public long DispatchCount;

            public long LastReadTicksUtc;
            public long LastDispatchTicksUtc;

            public readonly Dictionary<string, long> ReadSources = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, long> DispatchSources = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            public readonly object Gate = new object();

            public Entry(string key, string relativePath)
            {
                Key = key;
                RelativePath = relativePath;
            }
        }

        public sealed class Snapshot
        {
            public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;
            public string EnvirRoot { get; set; } = string.Empty;
            public List<EntrySnapshot> Entries { get; set; } = new List<EntrySnapshot>();
        }

        public sealed class EntrySnapshot
        {
            public string Key { get; set; } = string.Empty;
            public string RelativePath { get; set; } = string.Empty;

            public long ReadCount { get; set; }
            public long DispatchCount { get; set; }

            public DateTime? LastReadAtUtc { get; set; }
            public DateTime? LastDispatchAtUtc { get; set; }

            public List<SourceSnapshot> ReadSources { get; set; } = new List<SourceSnapshot>();
            public List<SourceSnapshot> DispatchSources { get; set; } = new List<SourceSnapshot>();
        }

        public sealed class SourceSnapshot
        {
            public string Source { get; set; } = string.Empty;
            public long Count { get; set; }
        }

        private static readonly ConcurrentDictionary<string, Entry> Entries = new ConcurrentDictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

        private static Timer? _autoDumpTimer;
        private static int _autoDumpStarted;
        private static int _dumpInProgress;
        private static int _dirty;

        public static void RecordRead(string path, string source)
        {
            if (!Settings.TxtScriptsUsageTraceEnabled) return;

            EnsureAutoDumpTimer();
            if (!TryGetRelativeEnvirPath(path, out var relativePath)) return;

            Record(relativePath, source, isDispatch: false);
        }

        public static void RecordDispatch(string path, string source)
        {
            if (!Settings.TxtScriptsUsageTraceEnabled) return;

            EnsureAutoDumpTimer();
            if (!TryGetRelativeEnvirPath(path, out var relativePath)) return;

            Record(relativePath, source, isDispatch: true);
        }

        public static void RecordReadKey(string keyOrRelativePath, string source)
        {
            if (!Settings.TxtScriptsUsageTraceEnabled) return;

            EnsureAutoDumpTimer();

            var relative = (keyOrRelativePath ?? string.Empty).Trim();
            if (relative.Length == 0) return;

            relative = relative.Replace('\\', '/').TrimStart('/');

            Record(relative, source, isDispatch: false);
        }

        public static void RecordDispatchKey(string keyOrRelativePath, string source)
        {
            if (!Settings.TxtScriptsUsageTraceEnabled) return;

            EnsureAutoDumpTimer();

            var relative = (keyOrRelativePath ?? string.Empty).Trim();
            if (relative.Length == 0) return;

            relative = relative.Replace('\\', '/').TrimStart('/');

            Record(relative, source, isDispatch: true);
        }

        public static void Clear()
        {
            Entries.Clear();
            Interlocked.Exchange(ref _dirty, 0);
        }

        public static Snapshot CreateSnapshot()
        {
            var snapshot = new Snapshot
            {
                GeneratedAtUtc = DateTime.UtcNow,
                EnvirRoot = Settings.EnvirPath.Replace('\\', '/')
            };

            var items = Entries.Values.ToArray();

            var maxSourcesPerKey = Settings.TxtScriptsUsageTraceMaxSourcesPerKey;
            if (maxSourcesPerKey < 0) maxSourcesPerKey = 0;
            if (maxSourcesPerKey > 50) maxSourcesPerKey = 50;

            snapshot.Entries = new List<EntrySnapshot>(items.Length);

            for (var i = 0; i < items.Length; i++)
            {
                var entry = items[i];

                var readCount = Interlocked.Read(ref entry.ReadCount);
                var dispatchCount = Interlocked.Read(ref entry.DispatchCount);

                var lastReadTicks = Interlocked.Read(ref entry.LastReadTicksUtc);
                var lastDispatchTicks = Interlocked.Read(ref entry.LastDispatchTicksUtc);

                var entrySnapshot = new EntrySnapshot
                {
                    Key = entry.Key,
                    RelativePath = entry.RelativePath,
                    ReadCount = readCount,
                    DispatchCount = dispatchCount,
                    LastReadAtUtc = lastReadTicks > 0 ? new DateTime(lastReadTicks, DateTimeKind.Utc) : null,
                    LastDispatchAtUtc = lastDispatchTicks > 0 ? new DateTime(lastDispatchTicks, DateTimeKind.Utc) : null,
                    ReadSources = new List<SourceSnapshot>(),
                    DispatchSources = new List<SourceSnapshot>()
                };

                if (maxSourcesPerKey > 0)
                {
                    lock (entry.Gate)
                    {
                        foreach (var kv in entry.ReadSources.OrderByDescending(kv => kv.Value).Take(maxSourcesPerKey))
                        {
                            entrySnapshot.ReadSources.Add(new SourceSnapshot { Source = kv.Key, Count = kv.Value });
                        }

                        foreach (var kv in entry.DispatchSources.OrderByDescending(kv => kv.Value).Take(maxSourcesPerKey))
                        {
                            entrySnapshot.DispatchSources.Add(new SourceSnapshot { Source = kv.Key, Count = kv.Value });
                        }
                    }
                }

                snapshot.Entries.Add(entrySnapshot);
            }

            snapshot.Entries.Sort((a, b) => string.CompareOrdinal(a.RelativePath, b.RelativePath));

            return snapshot;
        }

        public static bool DumpLatest(out string filePath, out string error)
        {
            filePath = string.Empty;
            error = string.Empty;

            try
            {
                var dir = Path.Combine(".", "Logs", "TxtUsage");
                Directory.CreateDirectory(dir);

                filePath = Path.Combine(dir, "usage-latest.json");
                var tempFilePath = filePath + ".tmp";

                var snapshot = CreateSnapshot();

                var jsonOptions = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };

                var json = JsonSerializer.Serialize(snapshot, jsonOptions);

                File.WriteAllText(tempFilePath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                File.Move(tempFilePath, filePath, overwrite: true);

                Interlocked.Exchange(ref _dirty, 0);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static void Record(string relativePath, string source, bool isDispatch)
        {
            if (string.IsNullOrWhiteSpace(relativePath)) return;

            var normalizedKey =
                LogicKey.TryNormalize(relativePath, out var normalized)
                    ? normalized
                    : relativePath.Replace('\\', '/') + "(InvalidKey)";

            var entry = Entries.GetOrAdd(normalizedKey, _ => new Entry(normalizedKey, relativePath.Replace('\\', '/')));

            if (isDispatch)
            {
                Interlocked.Increment(ref entry.DispatchCount);
                Interlocked.Exchange(ref entry.LastDispatchTicksUtc, DateTime.UtcNow.Ticks);
                AddSource(entry, isDispatch: true, source);
            }
            else
            {
                Interlocked.Increment(ref entry.ReadCount);
                Interlocked.Exchange(ref entry.LastReadTicksUtc, DateTime.UtcNow.Ticks);
                AddSource(entry, isDispatch: false, source);
            }

            Volatile.Write(ref _dirty, 1);
        }

        private static void AddSource(Entry entry, bool isDispatch, string source)
        {
            if (entry == null) return;
            if (string.IsNullOrWhiteSpace(source)) return;

            var maxSourcesPerKey = Settings.TxtScriptsUsageTraceMaxSourcesPerKey;
            if (maxSourcesPerKey <= 0) return;
            if (maxSourcesPerKey > 50) maxSourcesPerKey = 50;

            lock (entry.Gate)
            {
                var sources = isDispatch ? entry.DispatchSources : entry.ReadSources;

                if (sources.TryGetValue(source, out var count))
                {
                    sources[source] = count + 1;
                    return;
                }

                if (sources.Count >= maxSourcesPerKey) return;

                sources[source] = 1;
            }
        }

        private static void EnsureAutoDumpTimer()
        {
            if (!Settings.TxtScriptsUsageTraceEnabled) return;

            var seconds = Settings.TxtScriptsUsageTraceAutoDumpSeconds;
            if (seconds <= 0) return;

            if (Interlocked.CompareExchange(ref _autoDumpStarted, 1, 0) != 0) return;

            if (seconds < 5) seconds = 5;
            if (seconds > 3600) seconds = 3600;

            _autoDumpTimer = new Timer(_ => TryAutoDump(), null, TimeSpan.FromSeconds(seconds), TimeSpan.FromSeconds(seconds));
        }

        private static void TryAutoDump()
        {
            if (!Settings.TxtScriptsUsageTraceEnabled) return;
            if (Volatile.Read(ref _dirty) == 0) return;

            if (Interlocked.CompareExchange(ref _dumpInProgress, 1, 0) != 0) return;

            try
            {
                DumpLatest(out _, out _);
            }
            finally
            {
                Interlocked.Exchange(ref _dumpInProgress, 0);
            }
        }

        private static bool TryGetRelativeEnvirPath(string path, out string relativePath)
        {
            relativePath = string.Empty;

            if (string.IsNullOrWhiteSpace(path)) return false;

            try
            {
                var fullPath = Path.GetFullPath(path);

                var envirRoot = Path.GetFullPath(Settings.EnvirPath);
                var envirRootWithSep = envirRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

                if (!fullPath.StartsWith(envirRootWithSep, StringComparison.OrdinalIgnoreCase))
                    return false;

                relativePath = fullPath.Substring(envirRootWithSep.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                relativePath = relativePath.Replace('\\', '/');

                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
