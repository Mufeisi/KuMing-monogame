namespace Server.Scripting
{
    public sealed class TextFileRegistry
    {
        private readonly Dictionary<string, TextFileDefinition> _definitions =
            new Dictionary<string, TextFileDefinition>(StringComparer.Ordinal);

        public int Count => _definitions.Count;

        public IReadOnlyDictionary<string, TextFileDefinition> Definitions => _definitions;

        public void Register(string key, TextFileDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));

            if (!LogicKey.TryNormalize(key, out var normalizedKey))
                throw new ArgumentException("key 无效。", nameof(key));

            if (!string.Equals(definition.Key, normalizedKey, StringComparison.Ordinal))
                throw new ArgumentException($"TextFileDefinition.Key 与 key 不一致：expected={normalizedKey} actual={definition.Key}", nameof(definition));

            if (_definitions.ContainsKey(normalizedKey))
                throw new InvalidOperationException($"重复的文本 Key：{normalizedKey}");

            _definitions.Add(normalizedKey, definition);
        }

        public void Register(TextFileDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            Register(definition.Key, definition);
        }

        public bool TryGet(string key, out TextFileDefinition definition)
        {
            definition = null;

            if (!LogicKey.TryNormalize(key, out var normalizedKey))
                return false;

            return _definitions.TryGetValue(normalizedKey, out definition);
        }
    }
}

