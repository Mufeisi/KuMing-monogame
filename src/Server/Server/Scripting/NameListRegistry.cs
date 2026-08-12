namespace Server.Scripting
{
    public sealed class NameListRegistry
    {
        private readonly Dictionary<string, NameListDefinition> _definitions =
            new Dictionary<string, NameListDefinition>(StringComparer.Ordinal);

        public int Count => _definitions.Count;

        public IReadOnlyDictionary<string, NameListDefinition> Definitions => _definitions;

        public void Register(string listKey, NameListDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));

            if (!LogicKey.TryNormalize(listKey, out var normalizedKey))
                throw new ArgumentException("listKey 无效。", nameof(listKey));

            if (!string.Equals(definition.Key, normalizedKey, StringComparison.Ordinal))
                throw new ArgumentException($"NameListDefinition.Key 与 listKey 不一致：expected={normalizedKey} actual={definition.Key}", nameof(definition));

            if (_definitions.ContainsKey(normalizedKey))
                throw new InvalidOperationException($"重复的名单 Key：{normalizedKey}");

            _definitions.Add(normalizedKey, definition);
        }

        public void Register(NameListDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            Register(definition.Key, definition);
        }

        public bool TryGet(string key, out NameListDefinition definition)
        {
            definition = null;

            if (!LogicKey.TryNormalize(key, out var normalizedKey))
                return false;

            return _definitions.TryGetValue(normalizedKey, out definition);
        }
    }
}

