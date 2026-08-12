namespace Server.Scripting
{
    public sealed class ValueRegistry
    {
        private readonly Dictionary<string, ValueTableDefinition> _definitions =
            new Dictionary<string, ValueTableDefinition>(StringComparer.Ordinal);

        public int Count => _definitions.Count;

        public IReadOnlyDictionary<string, ValueTableDefinition> Definitions => _definitions;

        public void Register(string tableKey, ValueTableDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));

            if (!LogicKey.TryNormalize(tableKey, out var normalizedKey))
                throw new ArgumentException("tableKey 无效。", nameof(tableKey));

            if (!string.Equals(definition.Key, normalizedKey, StringComparison.Ordinal))
                throw new ArgumentException($"ValueTableDefinition.Key 与 tableKey 不一致：expected={normalizedKey} actual={definition.Key}", nameof(definition));

            if (_definitions.ContainsKey(normalizedKey))
                throw new InvalidOperationException($"重复的数值表 Key：{normalizedKey}");

            _definitions.Add(normalizedKey, definition);
        }

        public void Register(ValueTableDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            Register(definition.Key, definition);
        }

        public bool TryGet(string key, out ValueTableDefinition definition)
        {
            definition = null;

            if (!LogicKey.TryNormalize(key, out var normalizedKey))
                return false;

            return _definitions.TryGetValue(normalizedKey, out definition);
        }
    }
}

