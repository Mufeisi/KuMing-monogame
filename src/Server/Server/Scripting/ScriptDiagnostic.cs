namespace Server.Scripting
{
    public sealed class ScriptDiagnostic
    {
        public string Id { get; }
        public string Severity { get; }
        public string Message { get; }
        public string FilePath { get; }
        public int Line { get; }
        public int Column { get; }

        public ScriptDiagnostic(string id, string severity, string message, string filePath, int line, int column)
        {
            Id = id ?? string.Empty;
            Severity = severity ?? string.Empty;
            Message = message ?? string.Empty;
            FilePath = filePath ?? string.Empty;
            Line = line;
            Column = column;
        }

        public override string ToString()
        {
            if (!string.IsNullOrWhiteSpace(FilePath) && Line > 0 && Column > 0)
                return $"{Severity} {Id} {FilePath}:{Line}:{Column} {Message}";

            if (!string.IsNullOrWhiteSpace(FilePath))
                return $"{Severity} {Id} {FilePath} {Message}";

            return $"{Severity} {Id} {Message}";
        }
    }
}

