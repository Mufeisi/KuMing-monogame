namespace Server.Persistence.Sql
{
    public sealed class SchemaMigration
    {
        public int Version { get; }
        public string Description { get; }
        public IReadOnlyList<string> Statements { get; }

        public SchemaMigration(int version, string description, IReadOnlyList<string> statements)
        {
            if (version <= 0) throw new ArgumentOutOfRangeException(nameof(version));
            if (string.IsNullOrWhiteSpace(description)) throw new ArgumentException("描述不能为空。", nameof(description));
            if (statements == null || statements.Count == 0) throw new ArgumentException("Statements 不能为空。", nameof(statements));

            Version = version;
            Description = description;
            Statements = statements;
        }
    }
}

