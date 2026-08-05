using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading;
using Server.MirObjects;

namespace Server.Scripting
{
    public static class ScriptRuntimeMetrics
    {
        private sealed class Entry
        {
            public readonly string Key;
            public long Count;
            public long TotalStopwatchTicks;
            public long LastTicksUtc;

            public Entry(string key)
            {
                Key = key ?? string.Empty;
            }
        }

        public sealed class Snapshot
        {
            public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;
            public double StopwatchFrequency { get; set; } = Stopwatch.Frequency;
            public List<EntrySnapshot> Entries { get; set; } = new List<EntrySnapshot>();
        }

        public sealed class EntrySnapshot
        {
            public string Key { get; set; } = string.Empty;
            public long Count { get; set; }
            public double TotalMilliseconds { get; set; }
            public double AverageMilliseconds { get; set; }
            public DateTime? LastAtUtc { get; set; }
        }

        private static readonly ConcurrentDictionary<string, Entry> Entries =
            new ConcurrentDictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

        private static Timer _autoDumpTimer;
        private static int _autoDumpStarted;
        private static int _dumpInProgress;
        private static int _dirty;

        public static long GetTimestamp() => Stopwatch.GetTimestamp();

        public static void RecordCSharpHandler(string hookKey, long elapsedStopwatchTicks)
        {
            if (!Settings.ScriptsRuntimeMetricsEnabled) return;
            if (elapsedStopwatchTicks < 0) elapsedStopwatchTicks = 0;

            EnsureAutoDumpTimer();

            var normalized = NormalizeLogicKeyOrFallback(hookKey);
            RecordCore("csharp/" + normalized, elapsedStopwatchTicks);
        }

        public static void RecordLegacyNpcPage(string npcFileName, string pageKey, long elapsedStopwatchTicks)
        {
            if (!Settings.ScriptsRuntimeMetricsEnabled) return;
            if (elapsedStopwatchTicks < 0) elapsedStopwatchTicks = 0;

            EnsureAutoDumpTimer();

            var raw = $"NPCs/{npcFileName}/{pageKey}";
            var normalized = NormalizeLogicKeyOrFallback(raw);
            RecordCore("legacy/npc-page/" + normalized, elapsedStopwatchTicks);
        }

        public static void RecordLegacyNpcAction(ActionType actionType, long elapsedStopwatchTicks)
        {
            if (!Settings.ScriptsRuntimeMetricsEnabled) return;
            if (elapsedStopwatchTicks < 0) elapsedStopwatchTicks = 0;

            EnsureAutoDumpTimer();

            RecordCore("legacy/npc-act/" + actionType, elapsedStopwatchTicks);
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
                StopwatchFrequency = Stopwatch.Frequency,
                Entries = new List<EntrySnapshot>(),
            };

            var items = Entries.Values.ToArray();
            snapshot.Entries = new List<EntrySnapshot>(items.Length);

            var freq = Stopwatch.Frequency;
            if (freq <= 0) freq = 1;

            for (var i = 0; i < items.Length; i++)
            {
                var e = items[i];
                if (e == null) continue;

                var count = Interlocked.Read(ref e.Count);
                var ticks = Interlocked.Read(ref e.TotalStopwatchTicks);
                var lastTicks = Interlocked.Read(ref e.LastTicksUtc);

                var totalMs = ticks * 1000.0 / freq;
                var avgMs = count > 0 ? (totalMs / count) : 0;

                snapshot.Entries.Add(new EntrySnapshot
                {
                    Key = e.Key,
                    Count = count,
                    TotalMilliseconds = totalMs,
                    AverageMilliseconds = avgMs,
                    LastAtUtc = lastTicks > 0 ? new DateTime(lastTicks, DateTimeKind.Utc) : null
                });
            }

            snapshot.Entries.Sort((a, b) => b.TotalMilliseconds.CompareTo(a.TotalMilliseconds));
            return snapshot;
        }

        public static bool DumpLatest(out string filePath, out int entryCount, out string error)
        {
            filePath = string.Empty;
            entryCount = 0;
            error = string.Empty;

            try
            {
                var dir = Path.Combine(".", "Logs", "Scripts");
                Directory.CreateDirectory(dir);

                filePath = Path.Combine(dir, "runtime-metrics-latest.json");
                var tempFilePath = filePath + ".tmp";

                var snapshot = CreateSnapshot();
                entryCount = snapshot.Entries.Count;

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

        private static void RecordCore(string key, long elapsedStopwatchTicks)
        {
            if (string.IsNullOrWhiteSpace(key)) return;

            var normalizedKey = key.Trim();

            var maxKeys = Settings.ScriptsRuntimeMetricsMaxKeys;
            if (maxKeys < 0) maxKeys = 0;
            if (maxKeys > 100000) maxKeys = 100000;

            if (maxKeys > 0 && Entries.Count >= maxKeys && !Entries.ContainsKey(normalizedKey))
            {
                normalizedKey = "overflow";
            }

            var entry = Entries.GetOrAdd(normalizedKey, k => new Entry(k));

            Interlocked.Increment(ref entry.Count);
            Interlocked.Add(ref entry.TotalStopwatchTicks, elapsedStopwatchTicks);
            Interlocked.Exchange(ref entry.LastTicksUtc, DateTime.UtcNow.Ticks);

            Volatile.Write(ref _dirty, 1);
        }

        private static string NormalizeLogicKeyOrFallback(string key)
        {
            if (LogicKey.TryNormalize(key, out var normalized))
                return normalized;

            return (key ?? string.Empty).Replace('\\', '/').TrimStart('/').Trim();
        }

        private static void EnsureAutoDumpTimer()
        {
            if (!Settings.ScriptsRuntimeMetricsEnabled) return;

            var seconds = Settings.ScriptsRuntimeMetricsAutoDumpSeconds;
            if (seconds <= 0) return;

            if (seconds < 5) seconds = 5;
            if (seconds > 3600) seconds = 3600;

            if (Interlocked.CompareExchange(ref _autoDumpStarted, 1, 0) != 0)
                return;

            try
            {
                _autoDumpTimer = new Timer(_state =>
                {
                    try
                    {
                        if (Volatile.Read(ref _dirty) == 0) return;
                        if (Interlocked.CompareExchange(ref _dumpInProgress, 1, 0) != 0) return;

                        DumpLatest(out _, out _, out _);
                    }
                    finally
                    {
                        Interlocked.Exchange(ref _dumpInProgress, 0);
                    }
                }, null, dueTime: seconds * 1000, period: seconds * 1000);
            }
            catch
            {
                // ignore
            }
        }
    }
}
