namespace Server.Scripting
{
    public sealed class DropRegistry
    {
        private readonly Dictionary<string, DropTableDefinition> _definitions =
            new Dictionary<string, DropTableDefinition>(StringComparer.Ordinal);

        public int Count => _definitions.Count;

        public IReadOnlyDictionary<string, DropTableDefinition> Definitions => _definitions;

        public void Register(string tableKey, DropTableDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));

            if (!LogicKey.TryNormalize(tableKey, out var normalizedKey))
                throw new ArgumentException("tableKey 无效。", nameof(tableKey));

            if (!string.Equals(definition.Key, normalizedKey, StringComparison.Ordinal))
                throw new ArgumentException($"DropTableDefinition.Key 与 tableKey 不一致：expected={normalizedKey} actual={definition.Key}", nameof(definition));

            if (_definitions.ContainsKey(normalizedKey))
                throw new InvalidOperationException($"重复的掉落表 Key：{normalizedKey}");

            _definitions.Add(normalizedKey, definition);
        }

        public void Register(DropTableDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            Register(definition.Key, definition);
        }

        public bool TryGet(string key, out DropTableDefinition definition)
        {
            definition = null;

            if (!LogicKey.TryNormalize(key, out var normalizedKey))
                return false;

            return _definitions.TryGetValue(normalizedKey, out definition);
        }
    }
}

