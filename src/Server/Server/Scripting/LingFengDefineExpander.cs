using System.Text.RegularExpressions;

namespace Server.Scripting;

internal static class LingFengDefineExpander
{
    private const int MaximumDepth = 16;
    private const int MaximumExpandedLineLength = 64 * 1024;
    private static readonly Regex DefineRegex = new(
        @"^\s*#DEFINE\s+(?<name>\$\([^\)\r\n]+\))\s+(?<value>.*?)(?:\s+;.*)?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex ReferenceRegex = new(
        @"\$\([^\)\r\n]+\)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static void Expand(IDictionary<string, TextFileDefinition> definitions)
    {
        if (definitions == null || definitions.Count == 0) return;
        var raw = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (TextFileDefinition definition in definitions.Values)
        {
            for (int index = 0; index < definition.Lines.Count; index++)
            {
                Match match = DefineRegex.Match(definition.Lines[index]);
                if (!match.Success) continue;
                string name = match.Groups["name"].Value;
                string value = match.Groups["value"].Value.Trim();
                if (value.Length == 0)
                    throw new InvalidDataException(
                        $"LFENV16-DEFINE-001：宏 {name} 的值不能为空（{definition.GetSourceLocation(index)}）。");
                if (raw.TryGetValue(name, out string existing) &&
                    !string.Equals(existing, value, StringComparison.Ordinal))
                    throw new InvalidDataException(
                        $"LFENV16-DEFINE-002：宏 {name} 存在冲突定义（{definition.GetSourceLocation(index)}）。");
                raw[name] = value;
            }
        }
        if (raw.Count == 0) return;

        var resolved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in raw.Keys)
            Resolve(name, raw, resolved, new HashSet<string>(StringComparer.OrdinalIgnoreCase), 0);

        foreach ((string key, TextFileDefinition source) in definitions.ToArray())
        {
            var expanded = new TextFileDefinition(
                source.Key, source.SourcePath, source.SourceEncoding, source.SourceNewLine);
            for (int index = 0; index < source.Lines.Count; index++)
            {
                string line = source.Lines[index];
                string output = DefineRegex.IsMatch(line)
                    ? "; " + line.Trim()
                    : ReplaceKnown(line, resolved);
                if (output.Length > MaximumExpandedLineLength)
                    throw new InvalidDataException(
                        $"LFENV16-DEFINE-004：宏展开后单行超过 {MaximumExpandedLineLength} 字符（{source.GetSourceLocation(index)}）。");
                expanded.AddLine(output, source.GetSourceLineNumber(index));
            }
            definitions[key] = expanded;
        }
    }

    private static string Resolve(
        string name,
        IReadOnlyDictionary<string, string> raw,
        IDictionary<string, string> resolved,
        ISet<string> active,
        int depth)
    {
        if (resolved.TryGetValue(name, out string value)) return value;
        if (depth >= MaximumDepth || !active.Add(name))
            throw new InvalidDataException($"LFENV16-DEFINE-003：宏 {name} 存在循环或展开深度超过 {MaximumDepth}。");
        string expanded = ReferenceRegex.Replace(raw[name], match =>
            raw.ContainsKey(match.Value)
                ? Resolve(match.Value, raw, resolved, active, depth + 1)
                : match.Value);
        active.Remove(name);
        if (expanded.Length > MaximumExpandedLineLength)
            throw new InvalidDataException(
                $"LFENV16-DEFINE-004：宏 {name} 展开结果超过 {MaximumExpandedLineLength} 字符。");
        resolved[name] = expanded;
        return expanded;
    }

    private static string ReplaceKnown(
        string line,
        IReadOnlyDictionary<string, string> resolved) =>
        ReferenceRegex.Replace(line ?? string.Empty,
            match => resolved.TryGetValue(match.Value, out string value) ? value : match.Value);
}
