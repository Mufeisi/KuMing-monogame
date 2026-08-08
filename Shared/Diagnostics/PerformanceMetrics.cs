using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Numerics;
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
        private static string _configuredOutputPath;
        private static int _processExitHookRegistered;

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
                if (!enabled)
                    _configuredOutputPath = null;
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

        /// <summary>
        /// 生产进程的最小启用入口：设置 LYOCRYSTAL_PERF00_ENABLED=true，
        /// 可选 LYOCRYSTAL_PERF00_SCENARIO 与 LYOCRYSTAL_PERF00_OUTPUT。
        /// 进程退出时自动冻结并导出；正常代码路径不创建额外压测工具。
        /// </summary>
        public static bool TryConfigureFromEnvironment(out string reason)
        {
            reason = string.Empty;
            try
            {
                var enabledText = Environment.GetEnvironmentVariable("LYOCRYSTAL_PERF00_ENABLED");
                if (!IsTruthy(enabledText))
                {
                    reason = "未设置 LYOCRYSTAL_PERF00_ENABLED=true。";
                    return false;
                }

                var scenario = Environment.GetEnvironmentVariable("LYOCRYSTAL_PERF00_SCENARIO") ?? string.Empty;
                var output = Environment.GetEnvironmentVariable("LYOCRYSTAL_PERF00_OUTPUT");
                if (string.IsNullOrWhiteSpace(output))
                {
                    output = Path.Combine(AppContext.BaseDirectory, "perf00-session.json");
                }

                Configure(enabled: true, scenario: scenario);
                _configuredOutputPath = output;
                if (Interlocked.Exchange(ref _processExitHookRegistered, 1) == 0)
                    AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
                return true;
            }
            catch (Exception ex)
            {
                reason = ex.Message;
                return false;
            }
        }

        /// <summary>停止环境变量启用的会话并导出 JSON；可由宿主的正常关闭路径显式调用。</summary>
        public static bool TryStopAndWriteConfiguredSnapshot(out PerformanceSnapshot snapshot, out string error)
        {
            var output = _configuredOutputPath;
            if (string.IsNullOrWhiteSpace(output))
            {
                snapshot = PerformanceSnapshot.Empty();
                error = "未配置 LYOCRYSTAL_PERF00_OUTPUT，当前会话没有可导出的生产路径。";
                return false;
            }

            return TryFreezeAndWriteSnapshot(output, out snapshot, out error);
        }

        private static bool IsTruthy(string value)
        {
            return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "on", StringComparison.OrdinalIgnoreCase);
        }

        private static void OnProcessExit(object sender, EventArgs args)
        {
            try
            {
                TryStopAndWriteConfiguredSnapshot(out _, out _);
            }
            catch
            {
                // 进程退出阶段不能再向业务路径抛出异常。
            }
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
        private const int ActiveState = 1;
        private const int FrozenState = 2;
        private const int DisabledState = 0;

        private readonly PerformanceMetricAccumulator[] _accumulators = CreateAccumulators();
        private long _state;
        private long _writers;
        private long _lastGcPauseTicks = -1;
        private long _lastGcGen0 = -1;
        private long _lastGcGen1 = -1;
        private long _lastGcGen2 = -1;

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
                // CollectionCount 是进程累计值；会话内只记录从上次采样开始的增量，
                // 防止长驻进程把会话开始前的 GC 误计入本次基线。
                var gen0 = GC.CollectionCount(0);
                var gen1 = GC.CollectionCount(1);
                var gen2 = GC.CollectionCount(2);
                var previousGen0 = Interlocked.Exchange(ref _lastGcGen0, gen0);
                var previousGen1 = Interlocked.Exchange(ref _lastGcGen1, gen1);
                var previousGen2 = Interlocked.Exchange(ref _lastGcGen2, gen2);
                var gen0Delta = CollectionDelta(previousGen0, gen0);
                var gen1Delta = CollectionDelta(previousGen1, gen1);
                var gen2Delta = CollectionDelta(previousGen2, gen2);
                GetAccumulator(PerformanceMetricKind.Gc)?.RecordValue(gen0Delta + gen1Delta + gen2Delta);
                GetAccumulator(PerformanceMetricKind.GcGen0)?.RecordValue(gen0Delta);
                GetAccumulator(PerformanceMetricKind.GcGen1)?.RecordValue(gen1Delta);
                GetAccumulator(PerformanceMetricKind.GcGen2)?.RecordValue(gen2Delta);
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

        private static long CollectionDelta(long previous, long current)
        {
            if (previous < 0 || current < previous) return 0;
            return current - previous;
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
                result[i] = new PerformanceMetricAccumulator();
            return result;
        }
    }

    internal sealed class PerformanceMetricAccumulator
    {
        // 固定对数直方图覆盖整个会话，不会在 4096 个样本后悄悄退化为“最近样本”。
        // 直方图只保存 128 个计数，写入是一次 Interlocked.Increment，适合热路径。
        private const int HistogramBucketCount = 128;
        private readonly long[] _durationHistogram = new long[HistogramBucketCount];
        private readonly long[] _valueHistogram = new long[HistogramBucketCount];
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

        public void RecordDuration(long elapsedStopwatchTicks)
        {
            if (elapsedStopwatchTicks < 0) elapsedStopwatchTicks = 0;

            Interlocked.Increment(ref _samples);
            Interlocked.Increment(ref _durationSampleCount);
            Interlocked.Increment(ref _durationHistogram[HistogramBucket(elapsedStopwatchTicks)]);
            Interlocked.Add(ref _totalDurationTicks, elapsedStopwatchTicks);
            UpdateMaximum(ref _maxDurationTicks, elapsedStopwatchTicks);
            Interlocked.Exchange(ref _lastUtcTicks, DateTime.UtcNow.Ticks);
        }

        public void RecordValue(long value)
        {
            Interlocked.Increment(ref _samples);
            Interlocked.Increment(ref _valueSampleCount);
            Interlocked.Increment(ref _valueHistogram[HistogramBucket(value)]);
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
            var durationPercentiles = ComputePercentiles(_durationHistogram, durationSamples);
            var valuePercentiles = ComputePercentiles(_valueHistogram, valueSamples);
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
                PercentileMethod = samples > 0 ? "log2-histogram" : null,
                PercentileSampleCount = durationSamples > 0 ? durationSamples : valueSamples,
                DurationPercentileMethod = durationSamples > 0 ? "log2-histogram" : null,
                DurationPercentileSampleCount = durationSamples > 0 ? durationSamples : (long?)null,
                ValuePercentileMethod = valueSamples > 0 ? "log2-histogram" : null,
                ValuePercentileSampleCount = valueSamples > 0 ? valueSamples : (long?)null,
                LastUpdatedAtUtc = ToUtcDateTime(Interlocked.Read(ref _lastUtcTicks)),
            };
        }

        private static int HistogramBucket(long value)
        {
            ulong magnitude;
            if (value < 0)
                magnitude = (ulong)(-(value + 1)) + 1UL;
            else
                magnitude = (ulong)value;

            var magnitudeBucket = magnitude == 0
                ? 0
                : Math.Min(63, 63 - BitOperations.LeadingZeroCount(magnitude));
            return value < 0 ? 64 + magnitudeBucket : magnitudeBucket;
        }

        private static (long? P95, long? P99) ComputePercentiles(long[] histogram, long sampleCount)
        {
            if (sampleCount <= 0) return (null, null);

            return (HistogramPercentile(histogram, sampleCount, 0.95D),
                HistogramPercentile(histogram, sampleCount, 0.99D));
        }

        private static long HistogramPercentile(long[] histogram, long sampleCount, double percentile)
        {
            var rank = Math.Max(1L, (long)Math.Ceiling(percentile * sampleCount));
            var seen = 0L;

            // 负数按数值从小到大遍历：更大的绝对值更小。
            for (var index = HistogramBucketCount - 1; index >= 64; index--)
            {
                seen += Interlocked.Read(ref histogram[index]);
                if (seen >= rank) return BucketRepresentative(index);
            }

            for (var index = 0; index < 64; index++)
            {
                seen += Interlocked.Read(ref histogram[index]);
                if (seen >= rank) return BucketRepresentative(index);
            }

            return 0;
        }

        private static long BucketRepresentative(int index)
        {
            var magnitudeBucket = index >= 64 ? index - 64 : index;
            ulong magnitude = magnitudeBucket >= 63 ? 1UL << 63 : 1UL << magnitudeBucket;
            if (index >= 64)
            {
                return magnitude >= (1UL << 63) ? long.MinValue : -(long)magnitude;
            }

            return magnitude >= (1UL << 63) ? long.MaxValue : (long)magnitude;
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
        GcGen0,
        GcGen1,
        GcGen2,
        GcPause,
        Memory,
        GpuMemory,
        GpuMemoryBudget,
        Save,
        SaveSnapshotCapture,
        SaveTransactionCommit,
        SaveFailure,
        NetworkQueue,
        NetworkInQueue,
        NetworkOutQueue,
        NetworkQueueHighWater,
        NetworkInQueueHighWater,
        NetworkOutQueueHighWater,
        Connections,
        ActiveConnections,
        Disconnects,
        MobileSpriteBatchBegin,
        MobileSpriteBatchStateChange,
    }

    public enum PerformanceSessionState
    {
        Disabled = 0,
        Active = 1,
        Frozen = 2,
    }

    /// <summary>
    /// 网络队列的低开销深度/高水位计数器。入队、出队路径各只做一次原子加减，
    /// 采样方可在队列已排空后仍取得本采样窗口内的峰值。
    /// </summary>
    public sealed class PerformanceQueueTracker
    {
        private int _depth;
        private int _highWater;

        public int Depth => Math.Max(0, Volatile.Read(ref _depth));
        public int HighWater => Math.Max(Depth, Volatile.Read(ref _highWater));

        public void Enqueue()
        {
            var depth = Interlocked.Increment(ref _depth);
            while (true)
            {
                var highWater = Volatile.Read(ref _highWater);
                if (depth <= highWater) return;
                if (Interlocked.CompareExchange(ref _highWater, depth, highWater) == highWater) return;
            }
        }

        public void Dequeue()
        {
            while (true)
            {
                var depth = Volatile.Read(ref _depth);
                if (depth <= 0) return;
                if (Interlocked.CompareExchange(ref _depth, depth - 1, depth) == depth) return;
            }
        }

        /// <summary>读取连接/会话生命周期内高水位；不重置，避免并发入队在采样瞬间丢峰值。</summary>
        public int CaptureHighWater()
        {
            return HighWater;
        }
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
        /// <summary>百分位算法；当前为覆盖全会话样本的固定对数直方图，结果是近似值。</summary>
        public string PercentileMethod { get; set; }
        public long? PercentileSampleCount { get; set; }
        public string DurationPercentileMethod { get; set; }
        public long? DurationPercentileSampleCount { get; set; }
        public string ValuePercentileMethod { get; set; }
        public long? ValuePercentileSampleCount { get; set; }
        public DateTime? LastUpdatedAtUtc { get; set; }
    }
}
