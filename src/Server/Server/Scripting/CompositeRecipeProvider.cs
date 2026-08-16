namespace Server.Scripting
{
    internal sealed class CompositeRecipeProvider : IRecipeProvider
    {
        private readonly IReadOnlyDictionary<string, RecipeDefinition> _definitions;
        private readonly RecipeDefinition[] _all;

        internal CompositeRecipeProvider(
            IRecipeProvider csharp,
            IRecipeProvider txt,
            bool csharpRuntimeActive,
            bool fallbackToTxt,
            TextFileSourcePriority priority)
        {
            _definitions = Merge(
                csharp?.GetAll(), txt?.GetAll(), definition => definition.Key,
                csharpRuntimeActive, fallbackToTxt, priority);
            _all = _definitions.Values.OrderBy(value => value.Key, StringComparer.Ordinal).ToArray();
        }

        public IReadOnlyCollection<RecipeDefinition> GetAll() => _all;

        public RecipeDefinition GetByKey(string key)
        {
            if (!LogicKey.TryNormalize(key, out string normalized)) return null;
            return _definitions.TryGetValue(normalized, out RecipeDefinition value) ? value : null;
        }

        internal static IReadOnlyDictionary<string, T> Merge<T>(
            IEnumerable<T> csharp,
            IEnumerable<T> txt,
            Func<T, string> keySelector,
            bool csharpRuntimeActive,
            bool fallbackToTxt,
            TextFileSourcePriority priority) where T : class
        {
            var result = new Dictionary<string, T>(StringComparer.Ordinal);
            if (!csharpRuntimeActive)
            {
                AddAll(result, txt, keySelector, overwrite: false);
                return result;
            }
            if (priority == TextFileSourcePriority.TxtFirst)
            {
                AddAll(result, csharp, keySelector, overwrite: false);
                AddAll(result, txt, keySelector, overwrite: true);
                return result;
            }
            if (fallbackToTxt) AddAll(result, txt, keySelector, overwrite: false);
            AddAll(result, csharp, keySelector, overwrite: true);
            return result;
        }

        private static void AddAll<T>(
            IDictionary<string, T> target,
            IEnumerable<T> source,
            Func<T, string> keySelector,
            bool overwrite) where T : class
        {
            foreach (T value in source ?? Array.Empty<T>())
            {
                if (value == null) continue;
                string key = keySelector(value);
                if (overwrite) target[key] = value;
                else target.TryAdd(key, value);
            }
        }
    }
}
