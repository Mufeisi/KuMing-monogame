using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

namespace Shared.Diagnostics
{
    /// <summary>
    /// PERF-00 低开销指标接缝。默认关闭；所有写入都绑定到一个采样会话。
    /// 会话冻结后，正在进行的写入会排空，旧 Scope 不会写入后续会话。
    /// </summary>
    public static class PerformanceMetrics
    {
        private static readonly object SessionGate = new object();
        private static readonly ConcurrentDictionary<string, object> ExportGates =
            new ConcurrentDictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        private static long _sessionSequence;
        private static PerformanceMetricsSession _current;

        static PerformanceMetrics()
        {
            _current = CreateSession(string.Empty, PerformanceSessionState.Disabled);
        }

        public static bool Enabled => CurrentSession?.IsActive == true;

        public static string SessionId => CurrentSession?.SessionId ?? string.Empty;

        public static string Scenario => CurrentSession?.Scenario ?? string.Empty;

        public static long GetTimestamp() => Stopwatch.GetTimestamp();

        /// <summary>开始一个新的采样会话；旧会话先冻结并排空写入。</summary>
        public static PerformanceMetricsSession StartSession(string scenario = null)
        {
            lock (SessionGate)
            {
                _current?.Freeze();
                _current = CreateSession(scenario, PerformanceSessionState.Active);
                return _current;
            }
        }

        /// <summary>冻结当前会话并等待写入排空。冻结后的快照仍可导出。</summary>
        public static bool FreezeSession()
        {
            var session = CurrentSession;
            return session != null && session.Freeze();
        }

        /// <summary>冻结当前会话并返回稳定快照；后续 StartSession 会创建新代次。</summary>
        public static PerformanceSnapshot StopSession()
        {
            var session = CurrentSession;
            if (session == null) return PerformanceSnapshot.Empty();

            session.Freeze();
            return session.CreateSnapshot();
        }

        /// <summary>兼容既有调用：每次 Configure 都会创建隔离的新会话。</summary>
        public static void Configure(bool enabled, string scenario = null)
        {
            lock (SessionGate)
            {
                _current?.Freeze();
                _current = CreateSession(
                    scenario,
                    enabled ? PerformanceSessionState.Active : PerformanceSessionState.Disabled);
            }
        }

        /// <summary>在当前状态下重置为新会话，避免跨会话混合。</summary>
        public static void Reset(string scenario = null)
        {
            lock (SessionGate)
            {
                var state = _current?.IsActive == true
                    ? PerformanceSessionState.Active
                    : PerformanceSessionState.Disabled;
                _current?.Freeze();
                _current = CreateSession(scenario, state);
            }
        }

        public static void RecordDuration(PerformanceMetricKind kind, long elapsedStopwatchTicks)
        {
            CurrentSession?.RecordDuration(kind, elapsedStopwatchTicks);
        }

        public static void RecordValue(PerformanceMetricKind kind, long value)
        {
            CurrentSession?.RecordValue(kind, value);
        }

        public static void Increment(PerformanceMetricKind kind, long delta = 1)
        {
            CurrentSession?.RecordValue(kind, delta);
        }

        public static void SetGauge(PerformanceMetricKind kind, long value)
        {
            CurrentSession?.RecordValue(kind, value);
        }

        public static void MarkUnavailable(PerformanceMetricKind kind, string reason)
        {
            CurrentSession?.MarkUnavailable(kind, reason);
        }

        /// <summary>
        /// 记录托管堆、GC 次数和运行时累计暂停时间增量。
        /// GC 次数与 GC 暂停是两个独立指标，不能互相冒充。
        /// </summary>
        public static void SampleRuntime()
        {
            CurrentSession?.SampleRuntime();
        }

        public static Scope Begin(PerformanceMetricKind kind)
        {
            var session = CurrentSession;
            return session == null || !session.IsActive
                ? default
                : new Scope(session, kind, GetTimestamp());
        }

        public static PerformanceSnapshot CreateSnapshot()
        {
            return CurrentSession?.CreateSnapshot() ?? PerformanceSnapshot.Empty();
        }

        public static bool TryWriteSnapshot(string filePath, out string error)
        {
            return TryWriteSnapshot(filePath, freezeSession: false, out _, out error);
        }

        public static bool TryFreezeAndWriteSnapshot(
            string filePath,
            out PerformanceSnapshot snapshot,
            out string error)
        {
            return TryWriteSnapshot(filePath, freezeSession: true, out snapshot, out error);
        }

        public static bool TryWriteSnapshot(
            string filePath,
            bool freezeSession,
            out PerformanceSnapshot snapshot,
            out string error)
        {
            snapshot = PerformanceSnapshot.Empty();
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(filePath))
            {
                error = "指标快照路径为空。";
                return false;
            }

            var session = CurrentSession;
            if (session == null)
            {
                error = "指标会话不存在。";
                return false;
            }

            if (freezeSession) session.Freeze();
            snapshot = session.CreateSnapshot();

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(filePath);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }

            var exportGate = ExportGates.GetOrAdd(fullPath, _ => new object());
            lock (exportGate)
            {
                string tempPath = null;
                try
                {
                    var directory = Path.GetDirectoryName(fullPath);
                    if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

                    // 同一路径并发导出使用唯一临时文件，避免互相覆盖或留下半截 JSON。
                    tempPath = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    var options = new JsonSerializerOptions
                    {
                        WriteIndented = true,
                        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                    };
                    var json = JsonSerializer.Serialize(snapshot, options);
                    File.WriteAllText(
                        tempPath,
                        json,
                        new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                    File.Move(tempPath, fullPath, overwrite: true);
                    tempPath = null;
                    return true;
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                    return false;
                }
                finally
                {
                    if (!string.IsNullOrWhiteSpace(tempPath))
                    {
                        try { File.Delete(tempPath); } catch { }
                    }
                }
            }
        }

        private static PerformanceMetricsSession CurrentSession => Volatile.Read(ref _current);

        private static PerformanceMetricsSession CreateSession(string scenario, PerformanceSessionState state)
        {
            var sequence = Interlocked.Increment(ref _sessionSequence);
            return new PerformanceMetricsSession(
                $"perf-{sequence}-{Guid.NewGuid():N}",
                scenario?.Trim() ?? string.Empty,
                state);
        }

        public readonly struct Scope : IDisposable
        {
            private readonly PerformanceMetricsSession _session;
            private readonly PerformanceMetricKind _kind;
            private readonly long _startTimestamp;

            internal Scope(PerformanceMetricsSession session, PerformanceMetricKind kind, long startTimestamp)
            {
                _session = session;
                _kind = kind;
                _startTimestamp = startTimestamp;
            }

            public void Dispose()
            {
                if (_session == null || _startTimestamp <= 0) return;
                _session.RecordDuration(_kind, GetTimestamp() - _startTimestamp);
            }
        }
    }

    public sealed class PerformanceMetricsSession
    {
        private const int DurationReservoirCapacity = 4096;
        private const int ValueReservoirCapacity = 4096;
        private const int ActiveState = 1;
        private const int FrozenState = 2;
        private const int DisabledState = 0;

        private readonly PerformanceMetricAccumulator[] _accumulators = CreateAccumulators();
        private long _state;
        private long _writers;
        private long _lastGcPauseTicks = -1;

        internal PerformanceMetricsSession(string sessionId, string scenario, PerformanceSessionState state)
        {
            SessionId = sessionId;
            Scenario = scenario;
            StartedAtUtc = DateTime.UtcNow;
            _state = (long)state;
        }

        public string SessionId { get; }
        public string Scenario { get; }
        public DateTime StartedAtUtc { get; }
        public bool IsActive => Volatile.Read(ref _state) == ActiveState;
        public PerformanceSessionState State => (PerformanceSessionState)Volatile.Read(ref _state);

        public bool Freeze()
        {
            var previous = Interlocked.CompareExchange(ref _state, FrozenState, ActiveState);
            if (previous != ActiveState && previous != FrozenState) return false;

            var spinner = new SpinWait();
            while (Volatile.Read(ref _writers) != 0) spinner.SpinOnce();
            return previous == ActiveState;
        }

        internal void RecordDuration(PerformanceMetricKind kind, long elapsedStopwatchTicks)
        {
            if (!TryEnterWrite(kind, out var accumulator)) return;
            try { accumulator.RecordDuration(elapsedStopwatchTicks); }
            finally { Interlocked.Decrement(ref _writers); }
        }

        internal void RecordValue(PerformanceMetricKind kind, long value)
        {
            if (!TryEnterWrite(kind, out var accumulator)) return;
            try { accumulator.RecordValue(value); }
            finally { Interlocked.Decrement(ref _writers); }
        }

        internal void MarkUnavailable(PerformanceMetricKind kind, string reason)
        {
            if (!TryEnterWrite(kind, out var accumulator)) return;
            try { accumulator.MarkUnavailable(reason); }
            finally { Interlocked.Decrement(ref _writers); }
        }

        internal void SampleRuntime()
        {
            if (!TryEnterWrite(PerformanceMetricKind.Memory, out var accumulator)) return;
            try
            {
                GetAccumulator(PerformanceMetricKind.Gc)?.RecordValue(
                    GC.CollectionCount(0) + GC.CollectionCount(1) + GC.CollectionCount(2));
                accumulator.RecordValue(GC.GetTotalMemory(forceFullCollection: false));

                try
                {
                    // GC.GetTotalPauseDuration 返回的是 TimeSpan ticks（每 tick=100ns），
                    // 而耗时累加器统一使用 Stopwatch ticks。先换算，避免导出的毫秒值
                    // 因时间基准不同而缩小或放大。
                    var pauseTimeSpanTicks = GC.GetTotalPauseDuration().Ticks;
                    var previousPauseTimeSpanTicks = Interlocked.Exchange(
                        ref _lastGcPauseTicks,
                        pauseTimeSpanTicks);
                    if (previousPauseTimeSpanTicks >= 0 && pauseTimeSpanTicks >= previousPauseTimeSpanTicks)
                    {
                        var deltaTimeSpanTicks = pauseTimeSpanTicks - previousPauseTimeSpanTicks;
                        var deltaStopwatchTicks = (long)Math.Round(
                            deltaTimeSpanTicks * (double)Stopwatch.Frequency / TimeSpan.TicksPerSecond,
                            MidpointRounding.AwayFromZero);
                        GetAccumulator(PerformanceMetricKind.GcPause)?.RecordDuration(deltaStopwatchTicks);
                    }
                }
                catch (Exception ex)
                {
                    GetAccumulator(PerformanceMetricKind.GcPause)?.MarkUnavailable(
                        "当前运行时无法读取 GC.GetTotalPauseDuration：" + ex.GetType().Name);
                }
            }
            finally
            {
                Interlocked.Decrement(ref _writers);
            }
        }

        internal PerformanceSnapshot CreateSnapshot()
        {
            var snapshot = new PerformanceSnapshot
            {
                GeneratedAtUtc = DateTime.UtcNow,
                SessionStartedAtUtc = StartedAtUtc,
                SessionId = SessionId,
                Scenario = Scenario,
                State = State.ToString(),
                Enabled = State != PerformanceSessionState.Disabled,
                StopwatchFrequency = Stopwatch.Frequency,
                Metrics = new List<PerformanceMetricSnapshot>(_accumulators.Length),
            };

            for (var i = 0; i < _accumulators.Length; i++)
                snapshot.Metrics.Add(_accumulators[i].CreateSnapshot((PerformanceMetricKind)i));

            return snapshot;
        }

        private bool TryEnterWrite(PerformanceMetricKind kind, out PerformanceMetricAccumulator accumulator)
        {
            accumulator = null;
            if (Volatile.Read(ref _state) != ActiveState) return false;

            Interlocked.Increment(ref _writers);
            if (Volatile.Read(ref _state) != ActiveState)
            {
                Interlocked.Decrement(ref _writers);
                return false;
            }

            accumulator = GetAccumulator(kind);
            if (accumulator == null)
            {
                Interlocked.Decrement(ref _writers);
                return false;
            }
            return true;
        }

        private PerformanceMetricAccumulator GetAccumulator(PerformanceMetricKind kind)
        {
            var index = (int)kind;
            return (uint)index < (uint)_accumulators.Length ? _accumulators[index] : null;
        }

        private static PerformanceMetricAccumulator[] CreateAccumulators()
        {
            var values = (PerformanceMetricKind[])Enum.GetValues(typeof(PerformanceMetricKind));
            var result = new PerformanceMetricAccumulator[values.Length];
            for (var i = 0; i < result.Length; i++)
                result[i] = new PerformanceMetricAccumulator(DurationReservoirCapacity, ValueReservoirCapacity);
            return result;
        }
    }

    internal sealed class PerformanceMetricAccumulator
    {
        private readonly long[] _durationSamples;
        private readonly long[] _valueSamples;
        private readonly object _reasonGate = new object();
        private string _unavailableReason;
        private long _durationSampleCount;
        private long _valueSampleCount;
        private long _samples;
        private long _totalDurationTicks;
        private long _maxDurationTicks;
        private long _totalValue;
        private long _maxValue = long.MinValue;
        private long _lastValue;
        private long _lastUtcTicks;

        public PerformanceMetricAccumulator(int durationReservoirCapacity, int valueReservoirCapacity)
        {
            _durationSamples = new long[durationReservoirCapacity];
            _valueSamples = new long[valueReservoirCapacity];
        }

        public void RecordDuration(long elapsedStopwatchTicks)
        {
            if (elapsedStopwatchTicks < 0) elapsedStopwatchTicks = 0;

            Interlocked.Increment(ref _samples);
            Interlocked.Increment(ref _durationSampleCount);
            AddReservoirSample(_durationSamples, _durationSampleCount, elapsedStopwatchTicks);
            Interlocked.Add(ref _totalDurationTicks, elapsedStopwatchTicks);
            UpdateMaximum(ref _maxDurationTicks, elapsedStopwatchTicks);
            Interlocked.Exchange(ref _lastUtcTicks, DateTime.UtcNow.Ticks);
        }

        public void RecordValue(long value)
        {
            Interlocked.Increment(ref _samples);
            Interlocked.Increment(ref _valueSampleCount);
            AddReservoirSample(_valueSamples, _valueSampleCount, value);
            Interlocked.Add(ref _totalValue, value);
            UpdateMaximum(ref _maxValue, value);
            Interlocked.Exchange(ref _lastValue, value);
            Interlocked.Exchange(ref _lastUtcTicks, DateTime.UtcNow.Ticks);
        }

        public void MarkUnavailable(string reason)
        {
            lock (_reasonGate)
                _unavailableReason = string.IsNullOrWhiteSpace(reason) ? "未提供" : reason;
        }

        public PerformanceMetricSnapshot CreateSnapshot(PerformanceMetricKind kind)
        {
            var samples = Interlocked.Read(ref _samples);
            var durationSamples = Interlocked.Read(ref _durationSampleCount);
            var valueSamples = Interlocked.Read(ref _valueSampleCount);
            var frequency = Stopwatch.Frequency <= 0 ? 1 : Stopwatch.Frequency;
            var durationPercentiles = ComputePercentiles(_durationSamples, durationSamples);
            var valuePercentiles = ComputePercentiles(_valueSamples, valueSamples);
            string unavailableReason;
            lock (_reasonGate) unavailableReason = _unavailableReason;

            return new PerformanceMetricSnapshot
            {
                Name = kind.ToString(),
                Samples = samples,
                Available = samples > 0,
                UnavailableReason = samples > 0 ? null : unavailableReason,
                TotalMilliseconds = durationSamples > 0
                    ? Interlocked.Read(ref _totalDurationTicks) * 1000D / frequency
                    : null,
                AverageMilliseconds = durationSamples > 0
                    ? Interlocked.Read(ref _totalDurationTicks) * 1000D / frequency / durationSamples
                    : null,
                MaxMilliseconds = durationSamples > 0
                    ? Interlocked.Read(ref _maxDurationTicks) * 1000D / frequency
                    : null,
                P95Milliseconds = durationPercentiles.P95.HasValue
                    ? durationPercentiles.P95.Value * 1000D / frequency
                    : null,
                P99Milliseconds = durationPercentiles.P99.HasValue
                    ? durationPercentiles.P99.Value * 1000D / frequency
                    : null,
                TotalValue = valueSamples > 0 ? Interlocked.Read(ref _totalValue) : null,
                AverageValue = valueSamples > 0
                    ? Interlocked.Read(ref _totalValue) / (double)valueSamples
                    : null,
                MaxValue = valueSamples > 0 ? Interlocked.Read(ref _maxValue) : null,
                LastValue = valueSamples > 0 ? Interlocked.Read(ref _lastValue) : null,
                P95Value = valuePercentiles.P95,
                P99Value = valuePercentiles.P99,
                LastUpdatedAtUtc = ToUtcDateTime(Interlocked.Read(ref _lastUtcTicks)),
            };
        }

        private static void AddReservoirSample(long[] reservoir, long sequence, long value)
        {
            var index = (int)((sequence - 1) % reservoir.Length);
            Volatile.Write(ref reservoir[index], value);
        }

        private static (long? P95, long? P99) ComputePercentiles(long[] reservoir, long sampleCount)
        {
            if (sampleCount <= 0) return (null, null);

            var count = (int)Math.Min(sampleCount, reservoir.Length);
            var values = new long[count];
            for (var i = 0; i < count; i++) values[i] = Volatile.Read(ref reservoir[i]);
            Array.Sort(values);

            return (Percentile(values, 0.95D), Percentile(values, 0.99D));
        }

        private static long Percentile(long[] values, double percentile)
        {
            if (values.Length == 1) return values[0];
            var index = (int)Math.Ceiling(percentile * values.Length) - 1;
            index = Math.Clamp(index, 0, values.Length - 1);
            return values[index];
        }

        private static void UpdateMaximum(ref long target, long value)
        {
            while (true)
            {
                var current = Interlocked.Read(ref target);
                if (value <= current) return;
                if (Interlocked.CompareExchange(ref target, value, current) == current) return;
            }
        }

        private static DateTime? ToUtcDateTime(long ticks)
        {
            return ticks > 0 ? new DateTime(ticks, DateTimeKind.Utc) : null;
        }
    }

    public enum PerformanceMetricKind
    {
        Cpu,
        Update,
        Draw,
        DrawCall,
        TextureSwitch,
        TextureCreate,
        Gc,
        GcPause,
        Memory,
        GpuMemory,
        Save,
        SaveSnapshotCapture,
        SaveTransactionCommit,
        SaveFailure,
        NetworkQueue,
        NetworkInQueue,
        NetworkOutQueue,
        Connections,
        ActiveConnections,
        Disconnects,
    }

    public enum PerformanceSessionState
    {
        Disabled = 0,
        Active = 1,
        Frozen = 2,
    }

    public sealed class PerformanceSnapshot
    {
        public DateTime GeneratedAtUtc { get; set; }
        public DateTime? SessionStartedAtUtc { get; set; }
        public string SessionId { get; set; } = string.Empty;
        public string Scenario { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
        public bool Enabled { get; set; }
        public long StopwatchFrequency { get; set; }
        public List<PerformanceMetricSnapshot> Metrics { get; set; } = new List<PerformanceMetricSnapshot>();

        internal static PerformanceSnapshot Empty()
        {
            return new PerformanceSnapshot
            {
                GeneratedAtUtc = DateTime.UtcNow,
                State = PerformanceSessionState.Disabled.ToString(),
                Metrics = new List<PerformanceMetricSnapshot>(),
            };
        }
    }

    public sealed class PerformanceMetricSnapshot
    {
        public string Name { get; set; } = string.Empty;
        public long Samples { get; set; }
        public bool Available { get; set; }
        public string UnavailableReason { get; set; }
        public double? TotalMilliseconds { get; set; }
        public double? AverageMilliseconds { get; set; }
        public double? MaxMilliseconds { get; set; }
        public double? P95Milliseconds { get; set; }
        public double? P99Milliseconds { get; set; }
        public long? TotalValue { get; set; }
        public double? AverageValue { get; set; }
        public long? MaxValue { get; set; }
        public long? LastValue { get; set; }
        public long? P95Value { get; set; }
        public long? P99Value { get; set; }
        public DateTime? LastUpdatedAtUtc { get; set; }
    }
}
