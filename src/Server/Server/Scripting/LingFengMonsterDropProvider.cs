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
        private readonly IReadOnlyList<LingFengDependencyRequirement> _externalDependencies;
        internal IReadOnlySet<string> ConsumedScriptKeys { get; }

        private LingFengMonsterDropProvider(
            IReadOnlyDictionary<string, DropTableDefinition> definitions,
            Func<string, ItemInfo> itemResolver,
            IReadOnlySet<string> consumedScriptKeys,
            IReadOnlyList<LingFengDependencyRequirement> externalDependencies)
        {
            _definitions = definitions;
            _itemResolver = itemResolver;
            _inner = new CSharpDropTableProvider(definitions, itemResolver, skipMissingItems: false);
            ConsumedScriptKeys = consumedScriptKeys;
            _externalDependencies = externalDependencies;
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
            foreach (LingFengDependencyRequirement dependency in _externalDependencies)
                yield return dependency;
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
            var consumedScriptKeys = new HashSet<string>(StringComparer.Ordinal);
            var externalDependencies = new List<LingFengDependencyRequirement>();
            var scripts = scriptFiles ?? new Dictionary<string, TextFileDefinition>(StringComparer.Ordinal);
            foreach (TextFileDefinition source in (dropFiles ?? Array.Empty<TextFileDefinition>())
                         .OrderBy(definition => definition.Key, StringComparer.Ordinal))
            {
                if (source == null) continue;
                if (source.Key.StartsWith("questdiary/", StringComparison.Ordinal))
                    consumedScriptKeys.Add(source.Key);
                var table = new DropTableDefinition(source.Key);
                var activeReferences = new HashSet<string>(StringComparer.Ordinal);
                ParseRange(source, 0, source.Lines.Count, table.Drops, scripts,
                    comparisonEvaluator, callbackExecutor,
                    activeReferences, consumedScriptKeys, externalDependencies, false, 0, failures);
                if (table.Drops.Count > MaximumEntriesPerTable)
                    failures.Add($"LFENV11-DROP-010：掉落表超过 {MaximumEntriesPerTable} 项上限（{source.Key}）。");
                if (!parsed.TryAdd(table.Key, table))
                    failures.Add($"LFENV11-DROP-011：重复掉落逻辑 Key {table.Key}。");
            }

            errors = failures.AsReadOnly();
            provider = failures.Count == 0
                ? new LingFengMonsterDropProvider(
                    parsed,
                    itemResolver ?? throw new ArgumentNullException(nameof(itemResolver)),
                    new HashSet<string>(consumedScriptKeys, StringComparer.Ordinal),
                    externalDependencies
                        .DistinctBy(value => (value.Kind, value.Key, value.Level, value.SourceKey))
                        .ToArray())
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
            ISet<string> consumedScriptKeys,
            ICollection<LingFengDependencyRequirement> externalDependencies,
            bool allowImplicitChance,
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
                if (line.StartsWith(")#CHILD", StringComparison.OrdinalIgnoreCase))
                    line = line[1..].TrimStart();
                if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal)) continue;
                if (line.StartsWith("[@", StringComparison.Ordinal) && line.EndsWith(']')) continue;
                if (line.Equals("#ACT", StringComparison.OrdinalIgnoreCase)) continue;
                if (line is "{" or "}") continue;

                if (line.StartsWith("#CALL", StringComparison.OrdinalIgnoreCase))
                {
                    ParseCall(source, index, line, destination, scripts, comparisonEvaluator, callbackExecutor,
                        activeReferences, consumedScriptKeys, externalDependencies, allowImplicitChance, depth, errors);
                    continue;
                }

                if (line.StartsWith("#CHILD", StringComparison.OrdinalIgnoreCase))
                {
                    if (!TxtScriptTokenizer.TryTokenize(line, out string[] tokens, out string tokenError) || tokens.Length < 2)
                    {
                        errors.Add($"LFENV11-DROP-003：CHILD 格式无效：{tokenError}（{source.GetSourceLocation(index)}）。");
                        continue;
                    }
                    if (!TryChance(tokens[1], out int chance))
                    {
                        errors.Add($"LFENV11-DROP-003：CHILD 必须以 #CHILD 1/N 开始（{source.GetSourceLocation(index)}）。");
                        continue;
                    }
                    int argumentIndex = 2;
                    bool random = argumentIndex < tokens.Length &&
                                  tokens[argumentIndex].Equals("RANDOM", StringComparison.OrdinalIgnoreCase);
                    bool first = argumentIndex < tokens.Length &&
                                 tokens[argumentIndex].Equals("FIRST", StringComparison.OrdinalIgnoreCase);
                    if (random || first) argumentIndex++;
                    Func<DropAttemptContext, bool> condition = null;
                    LingFengDependencyRequirement conditionDependency = null;
                    string conditionError = tokens.Length > argumentIndex + 1 ? "参数过多" : string.Empty;
                    if (tokens.Length > argumentIndex + 1 ||
                        tokens.Length == argumentIndex + 1 && !TryBuildCondition(tokens[argumentIndex], scripts,
                            comparisonEvaluator, callbackExecutor,
                            out condition, out conditionError, out conditionDependency))
                    {
                        errors.Add($"LFENV11-DROP-004：CHILD 条件无效：{conditionError}（{source.GetSourceLocation(index)}）。");
                        continue;
                    }
                    if (conditionDependency != null) externalDependencies.Add(conditionDependency);
                    int open = NextContentLine(source, index + 1, end);
                    string openLine = open < end ? StripComment(source.Lines[open]) : string.Empty;
                    if (!openLine.StartsWith('('))
                    {
                        errors.Add($"LFENV11-DROP-005：CHILD 后必须紧跟括号组（{source.GetSourceLocation(index)}）。");
                        continue;
                    }
                    int close = FindClosingParenthesis(source, open, end);
                    if (close < 0) close = end;
                    var group = new DropGroupDefinition
                    {
                        Random = random,
                        First = first
                    };
                    string inline = openLine.Substring(1).Trim();
                    if (inline.Length > 0)
                    {
                        if (inline.StartsWith("#CALL", StringComparison.OrdinalIgnoreCase))
                            ParseCall(source, open, inline, group.Drops, scripts,
                                comparisonEvaluator, callbackExecutor,
                                activeReferences, consumedScriptKeys, externalDependencies, true, depth + 1, errors);
                        else if (TryParseDropLine(inline, out DropEntryDefinition inlineEntry, out string inlineError))
                            group.Drops.Add(inlineEntry);
                        else if (!inline.StartsWith('#') && !inline.StartsWith('['))
                            group.Drops.Add(DropEntryDefinition.Item(1, inline, 1, false));
                        else
                            errors.Add($"LFENV11-DROP-002：{inlineError}（{source.GetSourceLocation(open)}）。");
                    }
                    ParseRange(source, open + 1, close, group.Drops, scripts,
                        comparisonEvaluator, callbackExecutor,
                        activeReferences, consumedScriptKeys, externalDependencies, true, depth + 1, errors);
                    if (group.Drops.Count > 0)
                    {
                        DropEntryDefinition groupEntry = DropEntryDefinition.GroupDrop(chance, group);
                        groupEntry.Condition = condition;
                        destination.Add(groupEntry);
                    }
                    string closeLine = close < end ? StripComment(source.Lines[close]) : string.Empty;
                    index = closeLine.StartsWith(")#CHILD", StringComparison.OrdinalIgnoreCase)
                        ? close - 1
                        : close;
                    continue;
                }

                if (line is "(" or ")")
                {
                    errors.Add($"LFENV11-DROP-006：存在未配对的括号（{source.GetSourceLocation(index)}）。");
                    continue;
                }

                if (!TryParseDropLine(line, out DropEntryDefinition entry, out string lineError))
                {
                    MatchCollection concatenated = Regex.Matches(line, @"1/\d+\s+");
                    if (concatenated.Count > 1 && concatenated[0].Index == 0)
                    {
                        bool parsedAll = true;
                        for (int partIndex = 0; partIndex < concatenated.Count; partIndex++)
                        {
                            int partStart = concatenated[partIndex].Index;
                            int partEnd = partIndex + 1 < concatenated.Count
                                ? concatenated[partIndex + 1].Index
                                : line.Length;
                            if (!TryParseDropLine(line[partStart..partEnd].Trim(),
                                    out DropEntryDefinition concatenatedEntry, out _))
                            {
                                parsedAll = false;
                                break;
                            }
                            destination.Add(concatenatedEntry);
                        }
                        if (parsedAll) continue;
                    }
                    if (allowImplicitChance && !line.StartsWith('#') && !line.StartsWith('['))
                    {
                        destination.Add(DropEntryDefinition.Item(1, line, 1, false));
                        continue;
                    }
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
            ISet<string> consumedScriptKeys,
            ICollection<LingFengDependencyRequirement> externalDependencies,
            bool allowImplicitChance,
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
                externalDependencies.Add(new LingFengDependencyRequirement(
                    LingFengDependencyKind.DomainAdapter,
                    $"LingFeng/ExternalDropPage/{key}/{tokens[2]}",
                    LingFengDependencyLevel.E2,
                    source.Key));
                return;
            }
            string reference = key + "|" + label.ToUpperInvariant();
            consumedScriptKeys.Add(key);
            if (!activeReferences.Add(reference))
            {
                errors.Add($"LFENV11-DROP-009：CALL 形成循环 {key} {label}（{source.GetSourceLocation(lineIndex)}）。");
                return;
            }
            try
            {
                ParseRange(target, start, end, destination, scripts,
                    comparisonEvaluator, callbackExecutor,
                    activeReferences, consumedScriptKeys, externalDependencies,
                    allowImplicitChance, depth + 1, errors);
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
            out string error,
            out LingFengDependencyRequirement missingDependency)
        {
            condition = null;
            error = string.Empty;
            missingDependency = null;
            string value = (raw ?? string.Empty).Trim();
            if (!value.StartsWith('[') || !value.EndsWith(']'))
            {
                error = "必须为 [表达式,继承位掩码,@QFunction标签]";
                return false;
            }
            string[] parts = value[1..^1].Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length is < 1 or > 3 ||
                parts.Length >= 2 &&
                (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture,
                    out int inheritanceMask) || inheritanceMask is < 0 or > 7) ||
                parts.Length == 3 && NormalizeLabel(parts[2]) is null)
            {
                error = "继承位掩码必须为 0 至 7，回调必须为 @QFunction标签";
                return false;
            }
            int variableInheritanceMask = parts.Length >= 2
                ? int.Parse(parts[1], CultureInfo.InvariantCulture)
                : 7;
            string label = parts.Length == 3 ? NormalizeLabel(parts[2]) : null;
            if (label != null &&
                (!scripts.TryGetValue("systemscripts/qfunction-0", out TextFileDefinition qFunction) ||
                 !TryFindPage(qFunction, label, out _, out _)))
            {
                missingDependency = new LingFengDependencyRequirement(
                    LingFengDependencyKind.DomainAdapter,
                    $"LingFeng/DropConditionCallback/{parts[2]}",
                    LingFengDependencyLevel.E2,
                    "systemscripts/qfunction-0");
            }
            string comparisonText = parts[0];
            bool useOr = comparisonText.EndsWith("|OR", StringComparison.OrdinalIgnoreCase);
            if (useOr) comparisonText = comparisonText[..^3].TrimEnd();
            var comparisons = new List<(string Reference, string Operator, string Operand)>();
            foreach (string expression in comparisonText.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
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
                    LingFengCombatActorKind actorKind = context.Monster?.LingFengLastDamageActorKind ??
                                                        LingFengCombatActorKind.Player;
                    byte inheritanceBit = context.Monster?.LingFengLastDamageVariableInheritanceBit ?? 0;
                    bool mayReadPrivateVariables = actorKind switch
                    {
                        LingFengCombatActorKind.Player => true,
                        LingFengCombatActorKind.Hero =>
                            (variableInheritanceMask & (inheritanceBit == 0 ? 1 : inheritanceBit)) != 0,
                        LingFengCombatActorKind.Pet => inheritanceBit == 0
                            ? (variableInheritanceMask & 6) != 0
                            : (variableInheritanceMask & inheritanceBit) != 0,
                        _ => false
                    };
                    bool matched = useOr ? false : true;
                    foreach ((string reference, string comparison, string operand) in comparisons)
                    {
                        bool global = reference.StartsWith('I') || reference.StartsWith('G');
                        bool current = (global || mayReadPrivateVariables) && comparisonEvaluator != null &&
                                       comparisonEvaluator(context.Player, reference, comparison, operand);
                        if (useOr) matched |= current;
                        else matched &= current;
                    }
                    if (!matched) return false;
                    return label == null || callbackExecutor != null && callbackExecutor(context.Player, label);
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
            if (value == null) return false;
            ReadOnlySpan<char> denominator = value.StartsWith("1/", StringComparison.Ordinal)
                ? value.AsSpan(2)
                : value.AsSpan();
            return int.TryParse(denominator, NumberStyles.None, CultureInfo.InvariantCulture, out chance) && chance > 0;
        }

        private static string StripComment(string line)
        {
            string value = (line ?? string.Empty).Trim();
            return value.StartsWith(';') || Regex.IsMatch(value, @"^-{3,}") ||
                   value.Equals("本行文件由工具自动生成", StringComparison.OrdinalIgnoreCase) ||
                   value.StartsWith("?[@", StringComparison.OrdinalIgnoreCase) ||
                   value.Contains('^') && !value.Any(char.IsWhiteSpace)
                ? string.Empty
                : value;
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
                if (line.StartsWith('(')) depth++;
                else if (line.StartsWith(')') && --depth == 0) return index;
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
