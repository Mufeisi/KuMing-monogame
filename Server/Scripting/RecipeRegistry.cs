namespace Server.Scripting
{
    public sealed class RecipeRegistry
    {
        private readonly Dictionary<string, RecipeDefinition> _definitions =
            new Dictionary<string, RecipeDefinition>(StringComparer.Ordinal);

        public int Count => _definitions.Count;

        public IReadOnlyDictionary<string, RecipeDefinition> Definitions => _definitions;

        public void Register(string recipeKey, RecipeDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));

            if (!LogicKey.TryNormalize(recipeKey, out var normalizedKey))
                throw new ArgumentException("recipeKey 无效。", nameof(recipeKey));

            if (!string.Equals(definition.Key, normalizedKey, StringComparison.Ordinal))
                throw new ArgumentException($"RecipeDefinition.Key 与 recipeKey 不一致：expected={normalizedKey} actual={definition.Key}", nameof(definition));

            if (_definitions.ContainsKey(normalizedKey))
                throw new InvalidOperationException($"重复的配方定义 Key：{normalizedKey}");

            _definitions.Add(normalizedKey, definition);
        }

        public void Register(RecipeDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            Register(definition.Key, definition);
        }

        public bool TryGet(string key, out RecipeDefinition definition)
        {
            definition = null;

            if (!LogicKey.TryNormalize(key, out var normalizedKey))
                return false;

            return _definitions.TryGetValue(normalizedKey, out definition);
        }
    }
}

