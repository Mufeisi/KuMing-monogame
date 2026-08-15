using System.Text.RegularExpressions;

namespace Server.Scripting.Variables
{
    internal static class ScriptVariableTextDeclarationParser
    {
        private static readonly Regex Declaration = new Regex(
            @"^\s*VAR\s+(?<kind>INTEGER|DECIMAL|STRING)\s+(?<scope>P|D|M|N|I|U|T|G|A|J|Z|HUMAN|GUILD|GLOBAL|CALL)\s+(?<key>\S+?)(?:\s+DEFAULT(?:\s+(?<default>.*))?)?\s*$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex Candidate = new Regex(
            @"^\s*VAR(?:\s|$)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static void Register(TextFileRegistry textFiles, ScriptVariableRegistry variables)
        {
            if (textFiles == null) throw new ArgumentNullException(nameof(textFiles));
            if (variables == null) throw new ArgumentNullException(nameof(variables));

            foreach (var pair in textFiles.Definitions.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                IReadOnlyList<string> lines = pair.Value.Lines;
                for (int index = 0; index < lines.Count; index++)
                {
                    string line = lines[index] ?? string.Empty;
                    if (!Candidate.IsMatch(line)) continue;

                    Match match = Declaration.Match(line);
                    if (!match.Success)
                        throw new ArgumentException(
                            $"TXT 变量声明语法无效：{pair.Key}:{index + 1}。" +
                            "格式应为 VAR <Integer|Decimal|String> <作用域> <名称> DEFAULT <默认值>。");

                    if (!Enum.TryParse(match.Groups["kind"].Value, true, out ScriptVariableKind kind) ||
                        !Enum.TryParse(match.Groups["scope"].Value, true, out ScriptVariableScope scope))
                        throw new ArgumentException($"TXT 变量声明类型或作用域无效：{pair.Key}:{index + 1}。");

                    string defaultValue = match.Groups["default"].Success
                        ? match.Groups["default"].Value.Trim()
                        : kind == ScriptVariableKind.String ? string.Empty : "0";
                    if (kind == ScriptVariableKind.String && defaultValue.Length >= 2 &&
                        defaultValue[0] == '"' && defaultValue[defaultValue.Length - 1] == '"')
                        defaultValue = defaultValue.Substring(1, defaultValue.Length - 2);

                    variables.Register(new ScriptVariableDeclaration(
                        scope,
                        match.Groups["key"].Value,
                        kind,
                        defaultValue,
                        pair.Key,
                        index + 1));
                }
            }
        }
    }
}
