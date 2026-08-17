using System.Globalization;

namespace Server.Scripting
{
    public enum LingFengRobotScheduleKind
    {
        Interval,
        Daily,
        Weekly
    }

    public sealed record LingFengRobotScheduleEntry(
        LingFengRobotScheduleKind Kind,
        string Page,
        int SourceLine,
        TimeSpan Interval,
        TimeOnly TimeOfDay,
        DayOfWeek? DayOfWeek);

    public sealed class LingFengRobotScheduleSnapshot
    {
        public LingFengRobotScheduleSnapshot(IEnumerable<LingFengRobotScheduleEntry> entries)
        {
            Entries = Array.AsReadOnly((entries ?? Array.Empty<LingFengRobotScheduleEntry>()).ToArray());
        }

        public IReadOnlyList<LingFengRobotScheduleEntry> Entries { get; }
    }

    public static class LingFengRobotScheduleProvider
    {
        private static readonly TimeSpan MaximumInterval = TimeSpan.FromDays(365);
        private const int MaximumEntries = 4096;

        public static bool IsKnownExternalPage(string page) =>
            string.Equals((page ?? string.Empty).Trim().TrimStart('[').TrimEnd(']'),
                "@Mir2_沙城奖励Rm",
                StringComparison.OrdinalIgnoreCase);

        public static bool TryResolvePages(
            LingFengRobotScheduleSnapshot snapshot,
            IEnumerable<string> availablePages,
            out LingFengRobotScheduleSnapshot resolved,
            out IReadOnlyList<string> errors)
        {
            var pages = (availablePages ?? Array.Empty<string>())
                .Where(page => !string.IsNullOrWhiteSpace(page))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var failures = new List<string>();
            var entries = new List<LingFengRobotScheduleEntry>();
            foreach (LingFengRobotScheduleEntry entry in
                     snapshot?.Entries ?? Array.Empty<LingFengRobotScheduleEntry>())
            {
                string page = pages.FirstOrDefault(candidate =>
                    candidate.Equals(entry.Page, StringComparison.OrdinalIgnoreCase));
                if (page == null)
                {
                    string semantic = NormalizeLegacyPageSemantic(entry.Page);
                    string[] matches = pages.Where(candidate =>
                            NormalizeLegacyPageSemantic(candidate).Equals(
                                semantic, StringComparison.OrdinalIgnoreCase))
                        .ToArray();
                    if (matches.Length == 1) page = matches[0];
                }
                if (page == null)
                {
                    if (IsKnownExternalPage(entry.Page)) continue;
                    failures.Add($"LFENV10-ROBOT-009：Robot 调度标签不存在 {entry.Page}（AutoRunRobot:{entry.SourceLine}）。");
                    continue;
                }
                entries.Add(entry with { Page = page });
            }
            resolved = new LingFengRobotScheduleSnapshot(entries);
            errors = failures.AsReadOnly();
            return failures.Count == 0;
        }

        private static string NormalizeLegacyPageSemantic(string page)
        {
            string value = (page ?? string.Empty).Trim().TrimStart('[').TrimEnd(']');
            if (value.StartsWith('@')) value = value[1..];
            if (value.StartsWith("Mir2_", StringComparison.OrdinalIgnoreCase))
                value = value[5..];
            value = value.TrimStart('_');
            int digitCount = 0;
            while (digitCount < value.Length && char.IsDigit(value[digitCount])) digitCount++;
            value = value[digitCount..];
            if (value.EndsWith("Rm", StringComparison.OrdinalIgnoreCase))
                value = value[..^2];
            return value.Trim();
        }

        public static bool TryCreate(
            TextFileDefinition definition,
            out LingFengRobotScheduleSnapshot snapshot,
            out IReadOnlyList<string> errors)
        {
            var entries = new List<LingFengRobotScheduleEntry>();
            var failures = new List<string>();
            if (definition == null)
            {
                snapshot = new LingFengRobotScheduleSnapshot(entries);
                errors = new[] { "LFENV10-ROBOT-001：AutoRunRobot 定义不能为空。" };
                return false;
            }

            for (int index = 0; index < definition.Lines.Count; index++)
            {
                string line = definition.Lines[index]?.Trim() ?? string.Empty;
                if (line.Length == 0 || line.StartsWith(';') || line.StartsWith("//", StringComparison.Ordinal))
                    continue;
                if (entries.Count >= MaximumEntries)
                {
                    failures.Add($"LFENV10-ROBOT-007：调度条目超过 {MaximumEntries} 条上限（{definition.GetSourceLocation(index)}）。");
                    break;
                }
                if (!TxtScriptTokenizer.TryTokenize(line, out string[] tokens, out string tokenError))
                {
                    failures.Add($"LFENV10-ROBOT-002：{tokenError}（{definition.GetSourceLocation(index)}）。");
                    continue;
                }
                if (tokens.Length < 5 || !tokens[0].Equals("#AutoRun", StringComparison.OrdinalIgnoreCase) ||
                    !tokens[1].Equals("NPC", StringComparison.OrdinalIgnoreCase))
                {
                    failures.Add($"LFENV10-ROBOT-003：调度格式必须为 #AutoRun NPC <类型> <时点> <标签>（{definition.GetSourceLocation(index)}）。");
                    continue;
                }

                string page = NormalizePage(string.Join(' ', tokens.Skip(4)));
                if (page == null)
                {
                    failures.Add($"LFENV10-ROBOT-004：调度标签无效（{definition.GetSourceLocation(index)}）。");
                    continue;
                }

                if (!TryParseEntry(tokens[2], tokens[3], page, definition.GetSourceLineNumber(index),
                        out LingFengRobotScheduleEntry entry, out string error))
                {
                    failures.Add($"LFENV10-ROBOT-005：{error}（{definition.GetSourceLocation(index)}）。");
                    continue;
                }
                entries.Add(entry);
            }

            snapshot = new LingFengRobotScheduleSnapshot(entries.OrderBy(entry => entry.SourceLine));
            errors = failures.AsReadOnly();
            return failures.Count == 0;
        }

        private static bool TryParseEntry(
            string type,
            string value,
            string page,
            int sourceLine,
            out LingFengRobotScheduleEntry entry,
            out string error)
        {
            entry = null;
            error = string.Empty;
            if (type.Equals("SEC", StringComparison.OrdinalIgnoreCase) ||
                type.Equals("MIN", StringComparison.OrdinalIgnoreCase) ||
                type.Equals("HOUR", StringComparison.OrdinalIgnoreCase))
            {
                if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int amount) || amount <= 0)
                {
                    error = $"{type} 间隔必须为正整数";
                    return false;
                }
                int maximumAmount = type.Equals("SEC", StringComparison.OrdinalIgnoreCase)
                    ? (int)MaximumInterval.TotalSeconds
                    : type.Equals("MIN", StringComparison.OrdinalIgnoreCase)
                        ? (int)MaximumInterval.TotalMinutes
                        : (int)MaximumInterval.TotalHours;
                if (amount > maximumAmount)
                {
                    error = $"{type} 间隔超过 365 天上限";
                    return false;
                }
                double seconds = type.Equals("SEC", StringComparison.OrdinalIgnoreCase) ? amount :
                    type.Equals("MIN", StringComparison.OrdinalIgnoreCase) ? amount * 60D : amount * 3600D;
                TimeSpan interval = TimeSpan.FromSeconds(seconds);
                entry = new LingFengRobotScheduleEntry(
                    LingFengRobotScheduleKind.Interval, page, sourceLine, interval, default, null);
                return true;
            }

            if (type.Equals("RUNONDAY", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryParseTime(value, out TimeOnly time))
                {
                    error = "RUNONDAY 时点必须为 HH:mm 或 HH:mm:ss";
                    return false;
                }
                entry = new LingFengRobotScheduleEntry(
                    LingFengRobotScheduleKind.Daily, page, sourceLine, default, time, null);
                return true;
            }

            if (type.Equals("RUNONWEEK", StringComparison.OrdinalIgnoreCase))
            {
                string[] parts = value.Split(':');
                if (parts.Length is not (3 or 4) ||
                    !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int day) ||
                    day is < 0 or > 6 ||
                    !TryParseTime(string.Join(':', parts.Skip(1)), out TimeOnly time))
                {
                    error = "RUNONWEEK 时点必须为 0..6:HH:mm 或 0..6:HH:mm:ss";
                    return false;
                }
                entry = new LingFengRobotScheduleEntry(
                    LingFengRobotScheduleKind.Weekly, page, sourceLine, default, time, (DayOfWeek)day);
                return true;
            }

            error = $"未知调度类型 {type}";
            return false;
        }

        private static bool TryParseTime(string value, out TimeOnly time) =>
            TimeOnly.TryParseExact(value, new[] { "H:mm", "HH:mm", "H:mm:ss", "HH:mm:ss" },
                CultureInfo.InvariantCulture, DateTimeStyles.None, out time);

        private static string NormalizePage(string value)
        {
            string page = (value ?? string.Empty).Trim();
            if (page.StartsWith("[@", StringComparison.Ordinal) && page.EndsWith(']')) return page;
            if (!page.StartsWith('@') || page.Length < 2 || page.Contains('[') || page.Contains(']')) return null;
            return "[" + page + "]";
        }
    }

    public sealed class LingFengRobotScheduler
    {
        private sealed class RuntimeEntry
        {
            public LingFengRobotScheduleEntry Definition;
            public DateTime NextDue;
        }

        private readonly int _maxExecutionsPerTick;
        private List<RuntimeEntry> _entries = new();
        private DateTime _nextDue = DateTime.MaxValue;
        private bool _processing;

        public LingFengRobotScheduler(int maxExecutionsPerTick = 128)
        {
            if (maxExecutionsPerTick <= 0) throw new ArgumentOutOfRangeException(nameof(maxExecutionsPerTick));
            _maxExecutionsPerTick = Math.Min(maxExecutionsPerTick, 4096);
        }

        public bool IsRunning { get; private set; }
        public long ReentryRejectedCount { get; private set; }
        public long FaultedExecutionCount { get; private set; }
        public long BudgetExceededCount { get; private set; }

        public void Publish(LingFengRobotScheduleSnapshot snapshot, DateTime now)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            var candidate = snapshot.Entries.Select(entry => new RuntimeEntry
            {
                Definition = entry,
                NextDue = CalculateNext(entry, now, includeCurrentInstant: true)
            }).ToList();
            _entries = candidate;
            _nextDue = candidate.Count == 0 ? DateTime.MaxValue : candidate.Min(entry => entry.NextDue);
            IsRunning = true;
        }

        public void Process(
            DateTime now,
            Action<string> execute,
            Action<string, Exception> reportFault = null)
        {
            if (!IsRunning || execute == null) return;
            if (_processing)
            {
                ReentryRejectedCount++;
                return;
            }
            if (_nextDue > now) return;

            _processing = true;
            try
            {
                int executions = 0;
                foreach (RuntimeEntry entry in _entries)
                {
                    if (executions >= _maxExecutionsPerTick) break;
                    if (entry.NextDue > now) continue;
                    entry.NextDue = CalculateNext(entry.Definition, now, includeCurrentInstant: false);
                    try
                    {
                        execute(entry.Definition.Page);
                    }
                    catch (Exception error)
                    {
                        FaultedExecutionCount++;
                        try
                        {
                            reportFault?.Invoke(entry.Definition.Page, error);
                        }
                        catch
                        {
                            // 诊断通道不得破坏主线程调度循环。
                        }
                    }
                    executions++;
                }
                if (executions >= _maxExecutionsPerTick && _entries.Any(entry => entry.NextDue <= now))
                    BudgetExceededCount++;
                _nextDue = _entries.Count == 0 ? DateTime.MaxValue : _entries.Min(entry => entry.NextDue);
            }
            finally
            {
                _processing = false;
            }
        }

        public void Stop()
        {
            IsRunning = false;
            _entries = new List<RuntimeEntry>();
            _nextDue = DateTime.MaxValue;
        }

        private static DateTime CalculateNext(
            LingFengRobotScheduleEntry entry,
            DateTime now,
            bool includeCurrentInstant)
        {
            if (entry.Kind == LingFengRobotScheduleKind.Interval) return now + entry.Interval;
            DateTime fixedNow = now.AddTicks(-(now.Ticks % TimeSpan.TicksPerSecond));
            DateTime candidate = fixedNow.Date + entry.TimeOfDay.ToTimeSpan();
            if (entry.Kind == LingFengRobotScheduleKind.Daily)
                return candidate < fixedNow || (!includeCurrentInstant && candidate == fixedNow)
                    ? candidate.AddDays(1)
                    : candidate;
            int days = ((int)entry.DayOfWeek!.Value - (int)candidate.DayOfWeek + 7) % 7;
            candidate = candidate.AddDays(days);
            return candidate < fixedNow || (!includeCurrentInstant && candidate == fixedNow)
                ? candidate.AddDays(7)
                : candidate;
        }
    }
}
