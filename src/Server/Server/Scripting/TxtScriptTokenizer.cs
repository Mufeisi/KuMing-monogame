using System.Text;

namespace Server.Scripting
{
    public static class TxtScriptTokenizer
    {
        /// <summary>
        /// 翎风旧脚本偶尔在物理行首残留 DOS EOF（0x1A）或编辑器控制前缀。
        /// 只清除首个可见字符之前的 C0 控制字符；空格和制表符仍按原样保留。
        /// </summary>
        public static string NormalizePhysicalLine(string line)
        {
            string source = line ?? string.Empty;
            int index = 0;
            while (index < source.Length && source[index] < ' ' && source[index] != '\t')
                index++;

            return index == 0 ? source : source.Substring(index);
        }

        public static bool TryTokenize(string line, out string[] tokens, out string error)
        {
            var result = new List<string>();
            var current = new StringBuilder();
            bool quoted = false;
            bool tokenStarted = false;
            string source = NormalizePhysicalLine(line);
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

                // 分号在参数边界开始时表示翎风行尾注释；引号内或参数内部的分号仍按正文保留。
                if (!quoted && value == ';' && !tokenStarted)
                    break;

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
