namespace Server.Scripting
{
    public sealed class CSharpQuestProvider : IQuestProvider
    {
        private readonly IReadOnlyDictionary<string, QuestDefinition> _definitions;
        private readonly QuestDefinition[] _all;

        public CSharpQuestProvider(IReadOnlyDictionary<string, QuestDefinition> definitions)
        {
            _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));

            var list = new List<QuestDefinition>();
            foreach (var kv in _definitions)
            {
                if (kv.Value != null) list.Add(kv.Value);
            }

            _all = list.ToArray();
        }

        public IReadOnlyCollection<QuestDefinition> GetAll() => _all;

        public QuestDefinition GetByKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return null;
            if (!LogicKey.TryNormalize(key, out var normalizedKey)) return null;

            return _definitions.TryGetValue(normalizedKey, out var definition) ? definition : null;
        }
    }
}

