namespace Server.Scripting
{
    internal sealed class CompositeNameListProvider : INameListProvider
    {
        private readonly IReadOnlyDictionary<string, NameListDefinition> _definitions;
        private readonly NameListDefinition[] _all;

        internal CompositeNameListProvider(
            INameListProvider csharp,
            INameListProvider txt,
            bool csharpRuntimeActive,
            bool fallbackToTxt,
            TextFileSourcePriority priority)
        {
            _definitions = CompositeRecipeProvider.Merge(
                csharp?.GetAll(), txt?.GetAll(), definition => definition.Key,
                csharpRuntimeActive, fallbackToTxt, priority);
            _all = _definitions.Values.OrderBy(value => value.Key, StringComparer.Ordinal).ToArray();
        }

        public IReadOnlyCollection<NameListDefinition> GetAll() => _all;

        public NameListDefinition GetByKey(string key)
        {
            if (!LogicKey.TryNormalize(key, out string normalized)) return null;
            return _definitions.TryGetValue(normalized, out NameListDefinition value) ? value : null;
        }
    }
}
