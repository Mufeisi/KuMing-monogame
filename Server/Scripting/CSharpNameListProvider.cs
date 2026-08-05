namespace Server.Scripting
{
    public sealed class CSharpNameListProvider : INameListProvider
    {
        private readonly IReadOnlyDictionary<string, NameListDefinition> _definitions;
        private readonly NameListDefinition[] _all;

        public CSharpNameListProvider(IReadOnlyDictionary<string, NameListDefinition> definitions)
        {
            _definitions = definitions ?? new Dictionary<string, NameListDefinition>(StringComparer.Ordinal);

            var list = new List<NameListDefinition>();
            foreach (var kv in _definitions)
            {
                if (kv.Value != null) list.Add(kv.Value);
            }

            _all = list.ToArray();
        }

        public IReadOnlyCollection<NameListDefinition> GetAll() => _all;

        public NameListDefinition GetByKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return null;
            if (!LogicKey.TryNormalize(key, out var normalizedKey)) return null;

            return _definitions.TryGetValue(normalizedKey, out var definition) ? definition : null;
        }
    }
}

