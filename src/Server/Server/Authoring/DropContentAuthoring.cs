using System.Globalization;
using System.Text;
using Server.Diagnostics;
using Server.MirDatabase;
using Server.Scripting;

namespace Server.Authoring;

public sealed record DropContentDiff(int LineNumber, string Before, string After);

public sealed record DropAnalysisRow(
    string Path,
    string Target,
    string Probability,
    double ExpectedAmount,
    bool Conditional,
    string Note = "");

public sealed record DropSimulationRow(string Target, double AverageAmount);

public sealed record DropSimulationResult(int Attempts, IReadOnlyList<DropSimulationRow> Rows);

public sealed record DropAnalysisSnapshot(string TableKey, IReadOnlyList<DropAnalysisRow> Rows);

public sealed record DropAnalysisDiff(string Path, string Target, double BeforeExpected, double AfterExpected);

public sealed record DropContentCommitResult(bool Success, string Error)
{
    public static DropContentCommitResult Completed { get; } = new(true, string.Empty);
}

/// <summary>掉落文本编辑会话。磁盘内容只在显式提交回调中更新。</summary>
public sealed class DropContentEditingSession
{
    private string _baseline;

    public string DraftText { get; private set; }
    public bool IsDirty => !string.Equals(_baseline, DraftText, StringComparison.Ordinal);

    public DropContentEditingSession(string sourceText)
    {
        _baseline = Normalize(sourceText);
        DraftText = _baseline;
    }

    public void SetDraft(string text) => DraftText = Normalize(text);

    public void Reload(string sourceText)
    {
        _baseline = Normalize(sourceText);
        DraftText = _baseline;
    }

    public IReadOnlyList<DropContentDiff> BuildDiff()
    {
        string[] before = Lines(_baseline);
        string[] after = Lines(DraftText);
        var result = new List<DropContentDiff>();
        for (var index = 0; index < Math.Max(before.Length, after.Length); index++)
        {
            string oldLine = index < before.Length ? before[index] : string.Empty;
            string newLine = index < after.Length ? after[index] : string.Empty;
            if (!string.Equals(oldLine, newLine, StringComparison.Ordinal))
                result.Add(new DropContentDiff(index + 1, oldLine, newLine));
        }
        return result;
    }

    public IReadOnlyList<ProjectPreflightDiagnostic> Validate(
        string tableKey,
        Func<string, bool> itemExists,
        out DropTableDefinition table)
    {
        var diagnostics = new List<ProjectPreflightDiagnostic>();
        table = DropTextDefinitionParser.Parse(tableKey, DraftText, diagnostics);
        if (table is not null)
            diagnostics.AddRange(ProjectSemanticPreflight.ValidateDropContent(table, itemExists).Diagnostics);
        return diagnostics;
    }

    public DropContentCommitResult TryCommit(Action<string> persist)
    {
        ArgumentNullException.ThrowIfNull(persist);
        try
        {
            persist(DraftText);
            _baseline = DraftText;
            return DropContentCommitResult.Completed;
        }
        catch (Exception ex)
        {
            return new DropContentCommitResult(false, ex.Message);
        }
    }

    public DropContentCommitResult TryCommitFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("掉落文件路径不能为空。", nameof(path));
        return TryCommit(text => PersistAtomically(path, text));
    }

    private static string Normalize(string value) => (value ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
    private static string[] Lines(string value) => value.Length == 0 ? [] : value.Split('\n');

    private static void PersistAtomically(string path, string text)
    {
        string fullPath = Path.GetFullPath(path);
        string directory = Path.GetDirectoryName(fullPath) ?? throw new IOException("无法解析掉落文件目录。");
        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporary, text.Replace("\n", Environment.NewLine));
            File.Move(temporary, fullPath, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}

/// <summary>只解析现有 DropBuilder 可编辑的 TXT 子集，不参与运行时加载。</summary>
public static class DropTextDefinitionParser
{
    public static DropTableDefinition Parse(
        string tableKey,
        string text,
        ICollection<ProjectPreflightDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        DropTableDefinition table;
        try
        {
            table = new DropTableDefinition(tableKey);
        }
        catch (Exception ex)
        {
            diagnostics.Add(ParseError(tableKey, 0, ex.Message));
            return null;
        }

        string[] lines = (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            string line = lines[index].Trim();
            if (line.Length == 0 || line.StartsWith(';')) continue;
            string[] parts = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || !TryChance(parts[0], out int chance))
            {
                diagnostics.Add(ParseError(table.Key, index + 1, "格式必须为 1/N 目标 [数量或 Q]"));
                continue;
            }

            if (string.Equals(parts[1], "Gold", StringComparison.OrdinalIgnoreCase))
            {
                if (parts.Length < 3 || !uint.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out uint gold) || gold == 0)
                {
                    diagnostics.Add(ParseError(table.Key, index + 1, "金币数量必须大于 0"));
                    continue;
                }
                table.Drops.Add(DropEntryDefinition.GoldDrop(chance, gold));
                continue;
            }

            ushort count = 1;
            bool quest = false;
            for (var partIndex = 2; partIndex < parts.Length; partIndex++)
            {
                if (string.Equals(parts[partIndex], "Q", StringComparison.OrdinalIgnoreCase)) quest = true;
                else if (!ushort.TryParse(parts[partIndex], NumberStyles.None, CultureInfo.InvariantCulture, out count) || count == 0)
                {
                    diagnostics.Add(ParseError(table.Key, index + 1, $"无法识别参数：{parts[partIndex]}"));
                    count = 0;
                    break;
                }
            }
            if (count > 0) table.Drops.Add(DropEntryDefinition.Item(chance, parts[1], count, quest));
        }
        return table;
    }

    private static bool TryChance(string value, out int chance)
    {
        chance = 0;
        return value.StartsWith("1/", StringComparison.Ordinal) &&
               int.TryParse(value.AsSpan(2), NumberStyles.None, CultureInfo.InvariantCulture, out chance) && chance > 0;
    }

    private static ProjectPreflightDiagnostic ParseError(string key, int line, string message) =>
        new("CONTENT04-DROP-001", ProjectPreflightSeverity.Error, $"{key}:line[{line}]", message);
}

/// <summary>复用 C# 掉落定义的只读分析器，不改变运行时概率实现。</summary>
public static class DropContentAnalyzer
{
    public static DropAnalysisSnapshot Capture(DropTableDefinition table) =>
        new(table.Key, Expand(table).ToArray());

    public static IReadOnlyList<DropAnalysisDiff> Compare(
        DropAnalysisSnapshot before,
        DropTableDefinition after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        var original = before.Rows.ToDictionary(value => (value.Path, value.Target));
        var current = Expand(after).ToDictionary(value => (value.Path, value.Target));
        return original.Keys.Union(current.Keys)
            .OrderBy(value => value.Path, StringComparer.Ordinal)
            .ThenBy(value => value.Target, StringComparer.Ordinal)
            .Select(key => new DropAnalysisDiff(
                key.Path,
                key.Target,
                original.TryGetValue(key, out DropAnalysisRow oldValue) ? oldValue.ExpectedAmount : 0,
                current.TryGetValue(key, out DropAnalysisRow newValue) ? newValue.ExpectedAmount : 0))
            .Where(value =>
                double.IsNaN(value.BeforeExpected) != double.IsNaN(value.AfterExpected) ||
                (!double.IsNaN(value.BeforeExpected) && Math.Abs(value.BeforeExpected - value.AfterExpected) > 0.0000001))
            .ToArray();
    }

    public static IReadOnlyList<DropAnalysisRow> Expand(DropTableDefinition table)
    {
        ArgumentNullException.ThrowIfNull(table);
        var rows = new List<DropAnalysisRow>();
        for (var index = 0; index < table.Drops.Count; index++)
            ExpandEntry(table.Drops[index], $"Drops[{index}]", 1D, false, rows);
        return rows;
    }

    public static DropSimulationResult Simulate(
        DropTableDefinition table,
        int attempts,
        int seed = 20260813,
        DropAttemptContext context = null)
    {
        ArgumentNullException.ThrowIfNull(table);
        if (attempts <= 0) throw new ArgumentOutOfRangeException(nameof(attempts));
        var items = new Dictionary<string, ItemInfo>(StringComparer.OrdinalIgnoreCase);
        ItemInfo ResolveItem(string name)
        {
            if (!items.TryGetValue(name, out ItemInfo item))
            {
                item = new ItemInfo { Name = name };
                items.Add(name, item);
            }
            return item;
        }
        if (!CSharpDropTableProvider.TryBuildForAuthoring(table, ResolveItem, out IReadOnlyList<DropInfo> drops, out string error))
            throw new InvalidOperationException(error);
        if (drops.Any(HasCondition) && context is null)
            throw new InvalidOperationException("掉落定义包含 Condition；模拟必须提供 DropAttemptContext。当前仅显示结构概率。");

        var totals = new Dictionary<string, double>(StringComparer.Ordinal);
        var random = new Random(seed);
        for (var attempt = 0; attempt < attempts; attempt++)
            foreach (DropInfo drop in drops)
            {
                DropRewardInfo reward = drop.AttemptDropWithRandom(
                    random.Next,
                    random.Next,
                    dropRate: 1F,
                    context: context);
                if (reward is null) continue;
                if (reward.Gold > 0) Add(totals, "Gold", reward.Gold);
                foreach (ItemInfo item in reward.Items ?? [])
                    if (item is not null) Add(totals, item.Name, 1);
            }
        return new DropSimulationResult(attempts, totals
            .OrderBy(value => value.Key, StringComparer.Ordinal)
            .Select(value => new DropSimulationRow(value.Key, value.Value / attempts))
            .ToArray());
    }

    public static string FormatAnalysis(DropTableDefinition table, int simulationAttempts = 10000)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"定义：{table.Key}");
        builder.AppendLine("概率展开 / 理论期望");
        foreach (DropAnalysisRow row in Expand(table))
            builder.AppendLine($"{row.Path}  {row.Target}  {row.Probability}  期望={(double.IsNaN(row.ExpectedAmount) ? "不可计算" : row.ExpectedAmount.ToString("0.######", CultureInfo.InvariantCulture))}{(row.Conditional ? "  条件项" : string.Empty)}{(row.Note.Length > 0 ? $"  {row.Note}" : string.Empty)}");
        builder.AppendLine();
        builder.AppendLine($"固定种子模拟（{simulationAttempts:N0} 次）");
        if (ContainsCondition(table))
            builder.AppendLine("包含 Condition：缺少运行时上下文，跳过数值模拟。");
        else
            foreach (DropSimulationRow row in Simulate(table, simulationAttempts).Rows)
                builder.AppendLine($"{row.Target}  平均={row.AverageAmount:0.######}");
        return builder.ToString().TrimEnd();
    }

    private static void ExpandEntry(
        DropEntryDefinition entry,
        string path,
        double parentProbability,
        bool parentConditional,
        ICollection<DropAnalysisRow> rows)
    {
        if (entry is null) return;
        double probability = entry.Chance > 0 ? parentProbability / entry.Chance : 0;
        bool conditional = parentConditional || entry.Condition is not null;
        string probabilityText = conditional ? "不可计算" : FormatProbability(probability);
        if (!string.IsNullOrWhiteSpace(entry.ItemName))
            rows.Add(new(path, entry.ItemName, probabilityText, conditional ? double.NaN : probability * entry.Count, conditional));
        else if (entry.Gold > 0)
            rows.Add(new(path, "Gold", probabilityText, conditional ? double.NaN : probability * AverageGold(entry.Gold), conditional));
        else if (entry.Group is not null)
        {
            if (entry.Group.Random)
            {
                ExpandRandomGroup(entry, path, probability, conditional, rows);
                return;
            }
            double remaining = 1D;
            for (var index = 0; index < entry.Group.Drops.Count; index++)
            {
                DropEntryDefinition child = entry.Group.Drops[index];
                double childParent = probability * (entry.Group.First ? remaining : 1D);
                ExpandEntry(child, $"{path}.Group[{index}]", childParent, conditional, rows);
                if (entry.Group.First && child?.Chance > 0) remaining *= 1D - (1D / child.Chance);
            }
        }
    }

    private static void ExpandRandomGroup(
        DropEntryDefinition entry,
        string path,
        double groupProbability,
        bool parentConditional,
        ICollection<DropAnalysisRow> rows)
    {
        DropEntryDefinition[] children = entry.Group.Drops.Where(value => value is not null).ToArray();
        bool exact = children.All(value =>
            value.Condition is null && value.Chance == 1 && value.Group is null &&
            !string.IsNullOrWhiteSpace(value.ItemName) && value.Count == 1);
        if (!exact)
        {
            for (var index = 0; index < children.Length; index++)
            {
                DropEntryDefinition child = children[index];
                double rawProbability = child.Chance > 0 ? groupProbability / child.Chance : 0;
                string target = !string.IsNullOrWhiteSpace(child.ItemName) ? child.ItemName : child.Gold > 0 ? "Gold" : "Group";
                double amount = !string.IsNullOrWhiteSpace(child.ItemName)
                    ? rawProbability * child.Count
                    : child.Gold > 0 ? rawProbability * AverageGold(child.Gold) : 0;
                bool conditional = parentConditional || ContainsCondition(child);
                rows.Add(new($"{path}.Group[{index}]", target, conditional ? "不可计算" : FormatProbability(rawProbability),
                    conditional ? double.NaN : amount, conditional, "随机组最终产出率以固定种子模拟为准"));
            }
            return;
        }

        int totalWeight = children.Sum(value => Math.Max(1, value.Weight));
        for (var index = 0; index < children.Length; index++)
        {
            DropEntryDefinition child = children[index];
            double probability = totalWeight > 0
                ? groupProbability * Math.Max(1, child.Weight) / totalWeight
                : 0;
            string target = !string.IsNullOrWhiteSpace(child.ItemName) ? child.ItemName : "Gold";
            double amount = !string.IsNullOrWhiteSpace(child.ItemName)
                ? probability * child.Count
                : probability * AverageGold(child.Gold);
            rows.Add(new($"{path}.Group[{index}]", target,
                parentConditional ? "不可计算" : FormatProbability(probability),
                parentConditional ? double.NaN : amount,
                parentConditional,
                "随机组权重精确展开"));
        }
    }

    private static string FormatProbability(double value) => $"{value:P6}";
    private static double AverageGold(uint gold)
    {
        int lower = (int)(gold / 2);
        int upper = (int)(gold + gold / 2);
        return upper > lower ? (lower + upper - 1) / 2D : lower;
    }
    private static void Add(IDictionary<string, double> totals, string key, double amount) =>
        totals[key] = totals.TryGetValue(key, out double current) ? current + amount : amount;

    private static bool ContainsCondition(DropTableDefinition table) =>
        table.Drops.Any(ContainsCondition);

    private static bool ContainsCondition(DropEntryDefinition entry) =>
        entry is not null && (entry.Condition is not null || entry.Group?.Drops.Any(ContainsCondition) == true);

    private static bool HasCondition(DropInfo drop) =>
        drop is not null && (drop.Condition is not null || drop.GroupedDrop?.Any(HasCondition) == true);
}
