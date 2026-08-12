namespace Server.Scripting.Debug
{
    public readonly struct ScriptDebugBreakpoint : IEquatable<ScriptDebugBreakpoint>
    {
        public ScriptDebugBreakpoint(string filePath, int line)
        {
            FilePath = filePath ?? string.Empty;
            Line = line;
        }

        public string FilePath { get; }

        public int Line { get; }

        public bool IsValid => !string.IsNullOrWhiteSpace(FilePath) && Line > 0;

        public bool Equals(ScriptDebugBreakpoint other) =>
            Line == other.Line && string.Equals(FilePath, other.FilePath, StringComparison.OrdinalIgnoreCase);

        public override bool Equals(object obj) =>
            obj is ScriptDebugBreakpoint other && Equals(other);

        public override int GetHashCode() =>
            HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(FilePath ?? string.Empty), Line);

        public override string ToString() =>
            $"{FilePath}:{Line}";
    }
}

