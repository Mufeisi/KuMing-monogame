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

        public string SourcePath { get; }

        public string SourceEncoding { get; }

        public string SourceNewLine { get; }

        private readonly List<string> _lines = new List<string>();
        private readonly List<int> _sourceLineNumbers = new List<int>();

        public IReadOnlyList<string> Lines => _lines;

        public TextFileDefinition(string key)
            : this(key, string.Empty, string.Empty, "NONE")
        {
        }

        public TextFileDefinition(string key, string sourcePath, string sourceEncoding, string sourceNewLine)
        {
            Key = LogicKey.NormalizeOrThrow(key);
            SourcePath = sourcePath ?? string.Empty;
            SourceEncoding = sourceEncoding ?? string.Empty;
            SourceNewLine = sourceNewLine ?? "NONE";
        }

        public TextFileDefinition AddLine(string line)
        {
            _lines.Add(line ?? string.Empty);
            _sourceLineNumbers.Add(_sourceLineNumbers.Count + 1);
            return this;
        }

        public TextFileDefinition AddLine(string line, int sourceLineNumber)
        {
            if (sourceLineNumber <= 0) throw new ArgumentOutOfRangeException(nameof(sourceLineNumber));
            _lines.Add(line ?? string.Empty);
            _sourceLineNumbers.Add(sourceLineNumber);
            return this;
        }

        public TextFileDefinition AddLines(IEnumerable<string> lines)
        {
            if (lines == null) return this;

            foreach (var line in lines)
            {
                AddLine(line);
            }

            return this;
        }

        public TextFileDefinition SetLines(IEnumerable<string> lines)
        {
            _lines.Clear();
            _sourceLineNumbers.Clear();
            return AddLines(lines);
        }

        public int GetSourceLineNumber(int lineIndex)
        {
            if (lineIndex < 0 || lineIndex >= _lines.Count)
                throw new ArgumentOutOfRangeException(nameof(lineIndex));

            return _sourceLineNumbers[lineIndex];
        }

        public string GetSourceLocation(int lineIndex)
        {
            string source = string.IsNullOrWhiteSpace(SourcePath) ? Key : SourcePath;
            return $"{source}:{GetSourceLineNumber(lineIndex)}";
        }
    }
}

