using System.Collections.ObjectModel;

namespace Server.Scripting
{
    internal sealed class LingFengRuleListContentProvider
    {
        private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _lists;

        private LingFengRuleListContentProvider(
            IDictionary<string, IReadOnlyList<string>> lists)
        {
            _lists = new ReadOnlyDictionary<string, IReadOnlyList<string>>(
                new Dictionary<string, IReadOnlyList<string>>(lists, StringComparer.Ordinal));
        }

        internal IReadOnlyDictionary<string, IReadOnlyList<string>> Lists => _lists;

        internal static bool TryCreate(
            IEnumerable<TextFileDefinition> sources,
            out LingFengRuleListContentProvider provider,
            out IReadOnlyList<string> errors)
        {
            var diagnostics = new List<string>();
            var lists = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
            foreach (TextFileDefinition source in sources ?? Array.Empty<TextFileDefinition>())
            {
                if (source == null) continue;
                var values = new List<string>();
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (string raw in source.Lines)
                {
                    string value = Clean(raw);
                    if (value.Length == 0 || !seen.Add(value)) continue;
                    values.Add(value);
                }
                if (!lists.TryAdd(source.Key, Array.AsReadOnly(values.ToArray())))
                    diagnostics.Add($"LFENV13-RULE-DUPLICATE：重复名单逻辑 Key：{source.Key}");
            }
            if (diagnostics.Count > 0)
            {
                provider = null;
                errors = diagnostics.AsReadOnly();
                return false;
            }
            provider = new LingFengRuleListContentProvider(lists);
            errors = Array.Empty<string>();
            return true;
        }

        internal INameListProvider BuildProvider()
        {
            var definitions = new Dictionary<string, NameListDefinition>(StringComparer.Ordinal);
            foreach ((string key, IReadOnlyList<string> values) in _lists)
            {
                string runtimeKey = key.StartsWith("rulelists/", StringComparison.Ordinal)
                    ? "NameLists/" + key["rulelists/".Length..]
                    : key;
                var definition = new NameListDefinition(runtimeKey);
                foreach (string value in values) definition.Add(value);
                definitions.Add(definition.Key, definition);
            }
            return new CSharpNameListProvider(definitions);
        }

        private static string Clean(string raw)
        {
            string line = (raw ?? string.Empty).Trim();
            return line.StartsWith(';') || line.StartsWith("//", StringComparison.Ordinal)
                ? string.Empty
                : line;
        }
    }
}
