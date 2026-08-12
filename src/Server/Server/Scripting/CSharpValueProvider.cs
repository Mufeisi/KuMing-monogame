namespace Server.Scripting
{
    public sealed class CSharpValueProvider : IValueProvider
    {
        private readonly IReadOnlyDictionary<string, ValueTableDefinition> _tables;
        private readonly ValueTableDefinition[] _all;

        public CSharpValueProvider(IReadOnlyDictionary<string, ValueTableDefinition> tables)
        {
            _tables = tables ?? new Dictionary<string, ValueTableDefinition>(StringComparer.Ordinal);

            var list = new List<ValueTableDefinition>();
            foreach (var kv in _tables)
            {
                if (kv.Value != null) list.Add(kv.Value);
            }

            _all = list.ToArray();
        }

        public IReadOnlyCollection<ValueTableDefinition> GetAll() => _all;

        public bool TryGet(string tableKey, string section, string key, out string value)
        {
            value = string.Empty;

            if (string.IsNullOrWhiteSpace(tableKey))
                return false;

            if (!LogicKey.TryNormalize(tableKey, out var normalizedKey))
                return false;

            if (!_tables.TryGetValue(normalizedKey, out var table) || table == null)
                return false;

            return table.TryGet(section, key, out value);
        }
    }
}

