namespace Server.Scripting
{
    /// <summary>
    /// C# 版本“文本文件”定义（按 Key 标识），用于替代 Envir 下零散的 *.txt（公告/黑名单/配置/表等）。
    /// 说明：
    /// - Key 规则见：Docs/Scripting/KeySpec.md（例如 Notice.txt -> notice；SystemScripts/00Default/Login.txt -> systemscripts/00default/login）
    /// - 本定义仅提供“行文本”快照；解析与业务含义由引擎侧处理。
    /// </summary>
    public sealed class TextFileDefinition
    {
        public string Key { get; }

        private readonly List<string> _lines = new List<string>();

        public IReadOnlyList<string> Lines => _lines;

        public TextFileDefinition(string key)
        {
            Key = LogicKey.NormalizeOrThrow(key);
        }

        public TextFileDefinition AddLine(string line)
        {
            _lines.Add(line ?? string.Empty);
            return this;
        }

        public TextFileDefinition AddLines(IEnumerable<string> lines)
        {
            if (lines == null) return this;

            foreach (var line in lines)
            {
                _lines.Add(line ?? string.Empty);
            }

            return this;
        }

        public TextFileDefinition SetLines(IEnumerable<string> lines)
        {
            _lines.Clear();
            return AddLines(lines);
        }
    }
}

