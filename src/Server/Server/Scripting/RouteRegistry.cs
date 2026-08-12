namespace Server.Scripting
{
    public sealed class RouteRegistry
    {
        private readonly Dictionary<string, RouteDefinition> _definitions =
            new Dictionary<string, RouteDefinition>(StringComparer.Ordinal);

        public int Count => _definitions.Count;

        public IReadOnlyDictionary<string, RouteDefinition> Definitions => _definitions;

        public void Register(string routeKey, RouteDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));

            if (!LogicKey.TryNormalize(routeKey, out var normalizedKey))
                throw new ArgumentException("routeKey 无效。", nameof(routeKey));

            if (!string.Equals(definition.Key, normalizedKey, StringComparison.Ordinal))
                throw new ArgumentException($"RouteDefinition.Key 与 routeKey 不一致：expected={normalizedKey} actual={definition.Key}", nameof(definition));

            if (_definitions.ContainsKey(normalizedKey))
                throw new InvalidOperationException($"重复的路线 Key：{normalizedKey}");

            _definitions.Add(normalizedKey, definition);
        }

        public void Register(RouteDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            Register(definition.Key, definition);
        }

        public bool TryGet(string key, out RouteDefinition definition)
        {
            definition = null;

            if (!LogicKey.TryNormalize(key, out var normalizedKey))
                return false;

            return _definitions.TryGetValue(normalizedKey, out definition);
        }
    }
}

