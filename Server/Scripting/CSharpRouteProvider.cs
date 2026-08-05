namespace Server.Scripting
{
    public sealed class CSharpRouteProvider : IRouteProvider
    {
        private readonly IReadOnlyDictionary<string, RouteDefinition> _definitions;
        private readonly RouteDefinition[] _all;

        public CSharpRouteProvider(IReadOnlyDictionary<string, RouteDefinition> definitions)
        {
            _definitions = definitions ?? new Dictionary<string, RouteDefinition>(StringComparer.Ordinal);

            var list = new List<RouteDefinition>();
            foreach (var kv in _definitions)
            {
                if (kv.Value != null) list.Add(kv.Value);
            }

            _all = list.ToArray();
        }

        public IReadOnlyCollection<RouteDefinition> GetAll() => _all;

        public RouteDefinition GetByKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return null;
            if (!LogicKey.TryNormalize(key, out var normalizedKey)) return null;

            return _definitions.TryGetValue(normalizedKey, out var definition) ? definition : null;
        }
    }
}

