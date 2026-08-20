namespace Server.Scripting
{
    public sealed class CSharpRecipeProvider : IRecipeProvider
    {
        private readonly IReadOnlyDictionary<string, RecipeDefinition> _definitions;
        private readonly RecipeDefinition[] _all;

        public CSharpRecipeProvider(IReadOnlyDictionary<string, RecipeDefinition> definitions)
        {
            _definitions = definitions ?? new Dictionary<string, RecipeDefinition>(StringComparer.Ordinal);

            var list = new List<RecipeDefinition>();
            foreach (var kv in _definitions)
            {
                if (kv.Value != null) list.Add(kv.Value);
            }

            _all = list.ToArray();
        }

        public IReadOnlyCollection<RecipeDefinition> GetAll() => _all;

        public RecipeDefinition GetByKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return null;
            if (!LogicKey.TryNormalize(key, out var normalizedKey)) return null;

            return _definitions.TryGetValue(normalizedKey, out var definition) ? definition : null;
        }
    }
}

