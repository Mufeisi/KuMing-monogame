namespace Server.Scripting.Debug
{
    public readonly struct ScriptDebugLocation : IEquatable<ScriptDebugLocation>
    {
        public ScriptDebugLocation(string filePath, int line, int column)
        {
            FilePath = filePath ?? string.Empty;
            Line = line;
            Column = column;
        }

        public string FilePath { get; }

        public int Line { get; }

        public int Column { get; }

        public bool IsValid => !string.IsNullOrWhiteSpace(FilePath) && Line > 0;

        public ScriptDebugBreakpoint ToBreakpoint() => new ScriptDebugBreakpoint(FilePath, Line);

        public bool Equals(ScriptDebugLocation other) =>
            Line == other.Line &&
            Column == other.Column &&
            string.Equals(FilePath, other.FilePath, StringComparison.OrdinalIgnoreCase);

        public override bool Equals(object obj) =>
            obj is ScriptDebugLocation other && Equals(other);

        public override int GetHashCode() =>
            HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(FilePath ?? string.Empty), Line, Column);

        public override string ToString() =>
            Column > 0 ? $"{FilePath}:{Line}:{Column}" : $"{FilePath}:{Line}";
    }
}

