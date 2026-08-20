namespace Server.Scripting
{
    public sealed class CSharpTextFileProvider : ITextFileProvider
    {
        private readonly IReadOnlyDictionary<string, TextFileDefinition> _definitions;
        private readonly TextFileDefinition[] _all;

        public CSharpTextFileProvider(IReadOnlyDictionary<string, TextFileDefinition> definitions)
        {
            _definitions = definitions ?? new Dictionary<string, TextFileDefinition>(StringComparer.Ordinal);

            var list = new List<TextFileDefinition>();
            foreach (var kv in _definitions)
            {
                if (kv.Value != null) list.Add(kv.Value);
            }

            _all = list.ToArray();
        }

        public IReadOnlyCollection<TextFileDefinition> GetAll() => _all;

        public TextFileDefinition GetByKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return null;
            if (!LogicKey.TryNormalize(key, out var normalizedKey)) return null;

            return _definitions.TryGetValue(normalizedKey, out var definition) ? definition : null;
        }
    }
}

