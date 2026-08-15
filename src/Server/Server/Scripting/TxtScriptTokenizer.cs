using System.Text;

namespace Server.Scripting
{
    public static class TxtScriptTokenizer
    {
        public static bool TryTokenize(string line, out string[] tokens, out string error)
        {
            var result = new List<string>();
            var current = new StringBuilder();
            bool quoted = false;
            bool tokenStarted = false;
            string source = line ?? string.Empty;
            for (int index = 0; index < source.Length; index++)
            {
                char value = source[index];
                if (value == '"')
                {
                    quoted = !quoted;
                    tokenStarted = true;
                    continue;
                }

                // 翎风脚本大量使用 Windows 反斜杠目录路径（包括带空格的引号路径）。
                // 只识别双引号和反斜杠自身的转义，避免把 \t、\n、\r 路径片段改成控制字符。
                if (quoted && value == '\\' && index + 1 < source.Length)
                {
                    char escaped = source[index + 1];
                    char? replacement = escaped switch
                    {
                        '"' => '"',
                        '\\' => '\\',
                        _ => null
                    };
                    if (replacement.HasValue)
                    {
                        current.Append(replacement.Value);
                        tokenStarted = true;
                        index++;
                        continue;
                    }
                }

                if (!quoted && char.IsWhiteSpace(value))
                {
                    if (tokenStarted)
                    {
                        result.Add(current.ToString());
                        current.Clear();
                        tokenStarted = false;
                    }
                    continue;
                }

                current.Append(value);
                tokenStarted = true;
            }

            if (quoted)
            {
                tokens = Array.Empty<string>();
                error = "字符串缺少右侧双引号。";
                return false;
            }
            if (tokenStarted) result.Add(current.ToString());
            tokens = result.ToArray();
            error = string.Empty;
            return true;
        }
    }
}
