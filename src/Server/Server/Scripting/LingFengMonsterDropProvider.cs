using System.Globalization;
using System.Text.RegularExpressions;
using Server.MirDatabase;
using Server.MirEnvir;
using Server.MirObjects;
using Server.Scripting.Variables;

namespace Server.Scripting
{
    public sealed class LingFengMonsterDropProvider : IDropTableProvider
    {
        private const int MaximumReferenceDepth = 16;
        private const int MaximumEntriesPerTable = 100_000;
        private readonly CSharpDropTableProvider _inner;
        private readonly IReadOnlyDictionary<string, DropTableDefinition> _definitions;
        private readonly Func<string, ItemInfo> _itemResolver;

        private LingFengMonsterDropProvider(
            IReadOnlyDictionary<string, DropTableDefinition> definitions,
            Func<string, ItemInfo> itemResolver)
        {
            _definitions = definitions;
            _itemResolver = itemResolver;
            _inner = new CSharpDropTableProvider(definitions, itemResolver, skipMissingItems: false);
        }

        public IReadOnlyList<DropInfo> Get(string key) => _inner.Get(key);

        internal IReadOnlyList<string> ValidateDependencies()
        {
            var errors = new List<string>();
            foreach ((string key, DropTableDefinition table) in _definitions)
                if (!CSharpDropTableProvider.TryBuildStrict(table, _itemResolver, out string error))
                    errors.Add($"LFENV11-DROP-DEPENDENCY：{key} {error}");
            return errors.AsReadOnly();
        }

        internal IEnumerable<LingFengDependencyRequirement> GetDependencyRequirements()
        {
            foreach ((string key, DropTableDefinition table) in _definitions)
                foreach (string itemName in EnumerateItems(table.Drops))
                    yield return new LingFengDependencyRequirement(
                        LingFengDependencyKind.ItemName, itemName, LingFengDependencyLevel.E1, key);
        }

        private static IEnumerable<string> EnumerateItems(IEnumerable<DropEntryDefinition> entries)
        {
            foreach (DropEntryDefinition entry in entries ?? Array.Empty<DropEntryDefinition>())
            {
                if (!string.IsNullOrWhiteSpace(entry.ItemName)) yield return entry.ItemName;
                if (entry.Group != null)
                    foreach (string nested in EnumerateItems(entry.Group.Drops)) yield return nested;
            }
        }

        public static bool TryCreate(
            IEnumerable<TextFileDefinition> dropFiles,
            IReadOnlyDictionary<string, TextFileDefinition> scriptFiles,
            Func<string, ItemInfo> itemResolver,
            out LingFengMonsterDropProvider provider,
            out IReadOnlyList<string> errors)
        {
            return TryCreate(dropFiles, scriptFiles, itemResolver,
                EvaluateComparison,
                LingFengTxtSystemHookAdapter.TryDispatchDropConditionCallback,
                out provider, out errors);
        }

        internal static bool TryCreate(
            IEnumerable<TextFileDefinition> dropFiles,
            IReadOnlyDictionary<string, TextFileDefinition> scriptFiles,
            Func<string, ItemInfo> itemResolver,
            Func<PlayerObject, string, string, string, bool> comparisonEvaluator,
            Func<PlayerObject, string, bool> callbackExecutor,
            out LingFengMonsterDropProvider provider,
            out IReadOnlyList<string> errors)
        {
            var failures = new List<string>();
            var parsed = new Dictionary<string, DropTableDefinition>(StringComparer.Ordinal);
            var scripts = scriptFiles ?? new Dictionary<string, TextFileDefinition>(StringComparer.Ordinal);
            foreach (TextFileDefinition source in (dropFiles ?? Array.Empty<TextFileDefinition>())
                         .OrderBy(definition => definition.Key, StringComparer.Ordinal))
            {
                if (source == null) continue;
                var table = new DropTableDefinition(source.Key);
                var activeReferences = new HashSet<string>(StringComparer.Ordinal);
                ParseRange(source, 0, source.Lines.Count, table.Drops, scripts,
                    comparisonEvaluator, callbackExecutor,
                    activeReferences, 0, failures);
                if (table.Drops.Count > MaximumEntriesPerTable)
                    failures.Add($"LFENV11-DROP-010：掉落表超过 {MaximumEntriesPerTable} 项上限（{source.Key}）。");
                if (!parsed.TryAdd(table.Key, table))
                    failures.Add($"LFENV11-DROP-011：重复掉落逻辑 Key {table.Key}。");
            }

            errors = failures.AsReadOnly();
            provider = failures.Count == 0
                ? new LingFengMonsterDropProvider(parsed, itemResolver ?? throw new ArgumentNullException(nameof(itemResolver)))
                : null;
            return failures.Count == 0;
        }

        private static void ParseRange(
            TextFileDefinition source,
            int start,
            int end,
            ICollection<DropEntryDefinition> destination,
            IReadOnlyDictionary<string, TextFileDefinition> scripts,
            Func<PlayerObject, string, string, string, bool> comparisonEvaluator,
            Func<PlayerObject, string, bool> callbackExecutor,
            ISet<string> activeReferences,
            int depth,
            ICollection<string> errors)
        {
            if (depth > MaximumReferenceDepth)
            {
                errors.Add($"LFENV11-DROP-008：掉落引用深度超过 {MaximumReferenceDepth} 层（{source.Key}）。");
                return;
            }

            for (int index = start; index < end; index++)
            {
                string line = StripComment(source.Lines[index]);
                if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal)) continue;
                if (line.StartsWith("[@", StringComparison.Ordinal) && line.EndsWith(']')) continue;
                if (line.Equals("#ACT", StringComparison.OrdinalIgnoreCase)) continue;
                if (line is "{" or "}") continue;

                if (line.StartsWith("#CALL", StringComparison.OrdinalIgnoreCase))
                {
                    ParseCall(source, index, line, destination, scripts, comparisonEvaluator, callbackExecutor,
                        activeReferences, depth, errors);
                    continue;
                }

                if (line.StartsWith("#CHILD", StringComparison.OrdinalIgnoreCase))
                {
                    if (!TxtScriptTokenizer.TryTokenize(line, out string[] tokens, out string tokenError) || tokens.Length < 3)
                    {
                        errors.Add($"LFENV11-DROP-003：CHILD 格式无效：{tokenError}（{source.GetSourceLocation(index)}）。");
                        continue;
                    }
                    if (!TryChance(tokens[1], out int chance) ||
                        !(tokens[2].Equals("RANDOM", StringComparison.OrdinalIgnoreCase) ||
                          tokens[2].Equals("FIRST", StringComparison.OrdinalIgnoreCase)))
                    {
                        errors.Add($"LFENV11-DROP-003：CHILD 必须为 #CHILD 1/N RANDOM|FIRST（{source.GetSourceLocation(index)}）。");
                        continue;
                    }
                    Func<DropAttemptContext, bool> condition = null;
                    string conditionError = tokens.Length > 4 ? "参数过多" : string.Empty;
                    if (tokens.Length > 4 ||
                        tokens.Length == 4 && !TryBuildCondition(tokens[3], scripts,
                            comparisonEvaluator, callbackExecutor,
                            out condition, out conditionError))
                    {
                        errors.Add($"LFENV11-DROP-004：CHILD 条件无效：{conditionError}（{source.GetSourceLocation(index)}）。");
                        continue;
                    }
                    int open = NextContentLine(source, index + 1, end);
                    if (open >= end || StripComment(source.Lines[open]) != "(")
                    {
                        errors.Add($"LFENV11-DROP-005：CHILD 后必须紧跟括号组（{source.GetSourceLocation(index)}）。");
                        continue;
                    }
                    int close = FindClosingParenthesis(source, open, end);
                    if (close < 0)
                    {
                        errors.Add($"LFENV11-DROP-006：CHILD 括号组未闭合（{source.GetSourceLocation(index)}）。");
                        continue;
                    }
                    var group = new DropGroupDefinition
                    {
                        Random = tokens[2].Equals("RANDOM", StringComparison.OrdinalIgnoreCase),
                        First = tokens[2].Equals("FIRST", StringComparison.OrdinalIgnoreCase)
                    };
                    ParseRange(source, open + 1, close, group.Drops, scripts,
                        comparisonEvaluator, callbackExecutor,
                        activeReferences, depth + 1, errors);
                    if (group.Drops.Count == 0)
                        errors.Add($"LFENV11-DROP-007：CHILD 括号组不能为空（{source.GetSourceLocation(index)}）。");
                    else
                    {
                        DropEntryDefinition groupEntry = DropEntryDefinition.GroupDrop(chance, group);
                        groupEntry.Condition = condition;
                        destination.Add(groupEntry);
                    }
                    index = close;
                    continue;
                }

                if (line is "(" or ")")
                {
                    errors.Add($"LFENV11-DROP-006：存在未配对的括号（{source.GetSourceLocation(index)}）。");
                    continue;
                }

                if (!TryParseDropLine(line, out DropEntryDefinition entry, out string lineError))
                {
                    errors.Add($"LFENV11-DROP-002：{lineError}（{source.GetSourceLocation(index)}）。");
                    continue;
                }
                destination.Add(entry);
                if (destination.Count > MaximumEntriesPerTable) return;
            }
        }

        private static void ParseCall(
            TextFileDefinition source,
            int lineIndex,
            string line,
            ICollection<DropEntryDefinition> destination,
            IReadOnlyDictionary<string, TextFileDefinition> scripts,
            Func<PlayerObject, string, string, string, bool> comparisonEvaluator,
            Func<PlayerObject, string, bool> callbackExecutor,
            ISet<string> activeReferences,
            int depth,
            ICollection<string> errors)
        {
            if (!TxtScriptTokenizer.TryTokenize(line, out string[] tokens, out string tokenError) || tokens.Length != 3 ||
                !TryResolveCallKey(source, tokens[1], out string key))
            {
                errors.Add($"LFENV11-DROP-012：CALL 格式或路径无效：{tokenError}（{source.GetSourceLocation(lineIndex)}）。");
                return;
            }
            string label = NormalizeLabel(tokens[2]);
            if (label == null || !scripts.TryGetValue(key, out TextFileDefinition target) ||
                !TryFindPage(target, label, out int start, out int end))
            {
                errors.Add($"LFENV11-DROP-013：CALL 目标不存在 {key} {tokens[2]}（{source.GetSourceLocation(lineIndex)}）。");
                return;
            }
            string reference = key + "|" + label.ToUpperInvariant();
            if (!activeReferences.Add(reference))
            {
                errors.Add($"LFENV11-DROP-009：CALL 形成循环 {key} {label}（{source.GetSourceLocation(lineIndex)}）。");
                return;
            }
            try
            {
                ParseRange(target, start, end, destination, scripts,
                    comparisonEvaluator, callbackExecutor,
                    activeReferences, depth + 1, errors);
            }
            finally
            {
                activeReferences.Remove(reference);
            }
        }

        private static bool TryParseDropLine(
            string line,
            out DropEntryDefinition entry,
            out string error)
        {
            entry = null;
            error = string.Empty;
            if (!TxtScriptTokenizer.TryTokenize(line, out string[] tokens, out string tokenError) || tokens.Length < 2 ||
                !TryChance(tokens[0], out int chance))
            {
                error = string.IsNullOrWhiteSpace(tokenError) ? "格式必须为 1/N 目标 [数量|Q]" : tokenError;
                return false;
            }
            if (tokens[1].Equals("Gold", StringComparison.OrdinalIgnoreCase) || tokens[1] == "金币")
            {
                if (tokens.Length != 3 || !uint.TryParse(tokens[2], NumberStyles.None, CultureInfo.InvariantCulture, out uint gold) || gold == 0)
                {
                    error = "金币行必须为 1/N 金币 正整数";
                    return false;
                }
                entry = DropEntryDefinition.GoldDrop(chance, gold);
                return true;
            }

            ushort count = 1;
            bool quest = false;
            for (int index = 2; index < tokens.Length; index++)
            {
                if (tokens[index].Equals("Q", StringComparison.OrdinalIgnoreCase))
                    quest = true;
                else if (!ushort.TryParse(tokens[index], NumberStyles.None, CultureInfo.InvariantCulture, out count) || count == 0)
                {
                    error = $"无法识别物品掉落参数 {tokens[index]}";
                    return false;
                }
            }
            entry = DropEntryDefinition.Item(chance, tokens[1], count, quest);
            return true;
        }

        private static bool TryBuildCondition(
            string raw,
            IReadOnlyDictionary<string, TextFileDefinition> scripts,
            Func<PlayerObject, string, string, string, bool> comparisonEvaluator,
            Func<PlayerObject, string, bool> callbackExecutor,
            out Func<DropAttemptContext, bool> condition,
            out string error)
        {
            condition = null;
            error = string.Empty;
            string value = (raw ?? string.Empty).Trim();
            if (!value.StartsWith('[') || !value.EndsWith(']'))
            {
                error = "必须为 [表达式,7,@QFunction标签]";
                return false;
            }
            string[] parts = value[1..^1].Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length != 3 || parts[1] != "7" || NormalizeLabel(parts[2]) is not string label)
            {
                error = "仅支持 [表达式,7,@QFunction标签]";
                return false;
            }
            if (!scripts.TryGetValue("systemscripts/qfunction-0", out TextFileDefinition qFunction) ||
                !TryFindPage(qFunction, label, out _, out _))
            {
                error = $"QFunction 条件回调不存在 {parts[2]}";
                return false;
            }
            var comparisons = new List<(string Reference, string Operator, string Operand)>();
            foreach (string expression in parts[0].Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                Match match = Regex.Match(expression, @"^([A-Za-z]+(?:\$[A-Za-z0-9_]+|[0-9]+))\s*(>=|<=|<>|!=|==|=|>|<)\s*(.+)$",
                    RegexOptions.CultureInvariant);
                if (!match.Success)
                {
                    error = $"无法解析比较表达式 {expression}";
                    return false;
                }
                comparisons.Add((match.Groups[1].Value, match.Groups[2].Value, match.Groups[3].Value));
            }
            if (comparisons.Count == 0)
            {
                error = "比较表达式不能为空";
                return false;
            }
            condition = context =>
            {
                if (context?.Player == null) return false;
                return Envir.Main.InvokeOnMainThread(() =>
                {
                    foreach ((string reference, string comparison, string operand) in comparisons)
                        if (comparisonEvaluator == null ||
                            !comparisonEvaluator(context.Player, reference, comparison, operand)) return false;
                    return callbackExecutor != null && callbackExecutor(context.Player, label);
                });
            };
            return true;
        }

        private static bool EvaluateComparison(
            PlayerObject player,
            string reference,
            string comparison,
            string operand)
        {
            ScriptVariableContext variableContext = ScriptVariableContext.ForPlayer(player, player.CurrentMap);
            ScriptVariableCheckResult result = Envir.Main.CSharpScripts.VariableCommands.Check(
                variableContext, reference, comparison, operand);
            return result.Success && result.Matched;
        }

        private static bool TryChance(string value, out int chance)
        {
            chance = 0;
            return value != null && value.StartsWith("1/", StringComparison.Ordinal) &&
                   int.TryParse(value.AsSpan(2), NumberStyles.None, CultureInfo.InvariantCulture, out chance) && chance > 0;
        }

        private static string StripComment(string line)
        {
            string value = (line ?? string.Empty).Trim();
            return value.StartsWith(';') ? string.Empty : value;
        }

        private static int NextContentLine(TextFileDefinition source, int start, int end)
        {
            for (int index = start; index < end; index++)
                if (StripComment(source.Lines[index]).Length > 0) return index;
            return end;
        }

        private static int FindClosingParenthesis(TextFileDefinition source, int open, int end)
        {
            int depth = 0;
            for (int index = open; index < end; index++)
            {
                string line = StripComment(source.Lines[index]);
                if (line == "(") depth++;
                else if (line == ")" && --depth == 0) return index;
            }
            return -1;
        }

        private static bool TryResolveCallKey(TextFileDefinition source, string raw, out string key)
        {
            key = null;
            string path = (raw ?? string.Empty).Trim().Trim('[', ']').Replace('\\', '/');
            if (path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)) path = path[..^4];
            bool rooted = path.StartsWith('/');
            path = path.TrimStart('/');
            if (path.StartsWith("QuestDiary/", StringComparison.OrdinalIgnoreCase)) path = path[11..];
            else if (!rooted && source?.Key?.StartsWith("questdiary/", StringComparison.Ordinal) == true)
            {
                string sourcePath = source.Key[11..];
                int separator = sourcePath.LastIndexOf('/');
                path = (separator < 0 ? string.Empty : sourcePath[..(separator + 1)]) + path;
            }

            var segments = new List<string>();
            foreach (string segment in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                if (segment == ".") continue;
                if (segment == "..")
                {
                    if (segments.Count == 0)
                    {
                        key = null;
                        return false;
                    }
                    segments.RemoveAt(segments.Count - 1);
                    continue;
                }
                segments.Add(segment);
            }
            return segments.Count > 0 && LogicKey.TryNormalize("QuestDiary/" + string.Join('/', segments), out key);
        }

        private static string NormalizeLabel(string raw)
        {
            string label = (raw ?? string.Empty).Trim();
            if (label.StartsWith("[@", StringComparison.Ordinal) && label.EndsWith(']')) return label;
            return label.StartsWith('@') && label.Length > 1 ? "[" + label + "]" : null;
        }

        private static bool TryFindPage(
            TextFileDefinition source,
            string label,
            out int start,
            out int end)
        {
            start = end = -1;
            for (int index = 0; index < source.Lines.Count; index++)
            {
                string line = source.Lines[index].Trim();
                if (start < 0)
                {
                    if (line.Equals(label, StringComparison.OrdinalIgnoreCase)) start = index + 1;
                    continue;
                }
                if (line.StartsWith("[@", StringComparison.Ordinal) && line.EndsWith(']'))
                {
                    end = index;
                    return true;
                }
            }
            if (start < 0) return false;
            end = source.Lines.Count;
            return true;
        }
    }
}
