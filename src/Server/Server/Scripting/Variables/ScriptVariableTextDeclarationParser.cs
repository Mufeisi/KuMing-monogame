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
                RegisterDefinition(pair.Value, variables);
        }

        internal static ScriptVariableDeclarationSnapshot CreateSnapshot(ITextFileProvider provider)
        {
            var variables = new ScriptVariableRegistry();
            if (provider != null)
            {
                foreach (TextFileDefinition definition in provider.GetAll()
                             .OrderBy(item => item.Key, StringComparer.Ordinal))
                    RegisterDefinition(definition, variables);
            }
            variables.Seal();
            return variables.CreateSnapshot();
        }

        private static void RegisterDefinition(
            TextFileDefinition definition,
            ScriptVariableRegistry variables)
        {
            IReadOnlyList<string> lines = definition.Lines;
            for (int index = 0; index < lines.Count; index++)
            {
                string line = lines[index] ?? string.Empty;
                if (!Candidate.IsMatch(line)) continue;
                int sourceLine = definition.GetSourceLineNumber(index);

                Match match = Declaration.Match(line);
                if (!match.Success)
                    throw new ArgumentException(
                        $"TXT 变量声明语法无效：{definition.Key}:{sourceLine}。" +
                        "格式应为 VAR <Integer|Decimal|String> <作用域> <名称> DEFAULT <默认值>。");

                if (!Enum.TryParse(match.Groups["kind"].Value, true, out ScriptVariableKind kind) ||
                    !Enum.TryParse(match.Groups["scope"].Value, true, out ScriptVariableScope scope))
                    throw new ArgumentException($"TXT 变量声明类型或作用域无效：{definition.Key}:{sourceLine}。");

                string defaultValue = match.Groups["default"].Success
                    ? match.Groups["default"].Value.Trim()
                    : kind == ScriptVariableKind.String ? string.Empty : "0";
                if (kind == ScriptVariableKind.String && defaultValue.Length >= 2 &&
                    defaultValue[0] == '"' && defaultValue[defaultValue.Length - 1] == '"')
                    defaultValue = defaultValue.Substring(1, defaultValue.Length - 2);

                try
                {
                    variables.Register(new ScriptVariableDeclaration(
                        scope,
                        match.Groups["key"].Value,
                        kind,
                        defaultValue,
                        definition.Key,
                        sourceLine));
                }
                catch (ArgumentException error)
                {
                    throw new ArgumentException(
                        $"TXT 变量声明无效：{definition.Key}:{sourceLine}：{error.Message}", error);
                }
                catch (InvalidOperationException error)
                {
                    throw new InvalidOperationException(
                        $"TXT 变量声明无效：{definition.Key}:{sourceLine}：{error.Message}", error);
                }
            }
        }
    }
}
