namespace Server.Scripting
{
    public enum TextFileSourcePriority
    {
        CSharpFirst,
        TxtFirst
    }

    public sealed record TextFileSourceConflict(
        string Key,
        TextFileSourcePriority Priority,
        string SelectedSource,
        string ShadowedSource);

    public sealed class CompositeTextFileProvider : ITextFileProvider
    {
        private readonly IReadOnlyDictionary<string, TextFileDefinition> _definitions;
        private readonly TextFileDefinition[] _all;

        public CompositeTextFileProvider(
            ITextFileProvider csharpProvider,
            ITextFileProvider txtProvider,
            TextFileSourcePriority priority)
        {
            if (!Enum.IsDefined(priority)) throw new ArgumentOutOfRangeException(nameof(priority));

            Dictionary<string, TextFileDefinition> csharp = Index(csharpProvider, "C#");
            Dictionary<string, TextFileDefinition> txt = Index(txtProvider, "TXT");
            var merged = new Dictionary<string, TextFileDefinition>(StringComparer.Ordinal);
            var conflicts = new List<TextFileSourceConflict>();
            foreach (string key in csharp.Keys.Concat(txt.Keys).Distinct(StringComparer.Ordinal)
                         .OrderBy(value => value, StringComparer.Ordinal))
            {
                bool hasCSharp = csharp.TryGetValue(key, out TextFileDefinition csharpDefinition);
                bool hasTxt = txt.TryGetValue(key, out TextFileDefinition txtDefinition);
                if (hasCSharp && hasTxt)
                {
                    bool csharpFirst = priority == TextFileSourcePriority.CSharpFirst;
                    TextFileDefinition selected = csharpFirst ? csharpDefinition : txtDefinition;
                    TextFileDefinition shadowed = csharpFirst ? txtDefinition : csharpDefinition;
                    merged.Add(key, selected);
                    conflicts.Add(new TextFileSourceConflict(
                        key, priority,
                        Describe(selected, csharpFirst ? "C#" : "TXT"),
                        Describe(shadowed, csharpFirst ? "TXT" : "C#")));
                }
                else
                {
                    merged.Add(key, hasCSharp ? csharpDefinition : txtDefinition);
                }
            }

            _definitions = merged;
            _all = merged.Values.ToArray();
            Conflicts = conflicts;
        }

        public IReadOnlyList<TextFileSourceConflict> Conflicts { get; }

        public IReadOnlyCollection<TextFileDefinition> GetAll() => _all;

        public TextFileDefinition GetByKey(string key)
        {
            if (!LogicKey.TryNormalize(key, out string normalizedKey)) return null;
            return _definitions.TryGetValue(normalizedKey, out TextFileDefinition definition) ? definition : null;
        }

        private static Dictionary<string, TextFileDefinition> Index(ITextFileProvider provider, string sourceName)
        {
            var result = new Dictionary<string, TextFileDefinition>(StringComparer.Ordinal);
            if (provider == null) return result;
            foreach (TextFileDefinition definition in provider.GetAll())
            {
                if (definition == null) continue;
                if (!result.TryAdd(definition.Key, definition))
                    throw new InvalidDataException($"{sourceName} 文本来源包含重复逻辑 Key：{definition.Key}");
            }
            return result;
        }

        private static string Describe(TextFileDefinition definition, string sourceName) =>
            string.IsNullOrWhiteSpace(definition.SourcePath)
                ? $"{sourceName}:{definition.Key}"
                : $"{sourceName}:{definition.SourcePath}";
    }
}
