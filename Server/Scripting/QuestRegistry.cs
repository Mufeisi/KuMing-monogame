namespace Server.Scripting
{
    public sealed class QuestRegistry
    {
        private readonly Dictionary<string, QuestDefinition> _definitions =
            new Dictionary<string, QuestDefinition>(StringComparer.Ordinal);

        public int Count => _definitions.Count;

        public IReadOnlyDictionary<string, QuestDefinition> Definitions => _definitions;

        public void Register(QuestDefinition quest)
        {
            if (quest == null) throw new ArgumentNullException(nameof(quest));

            var normalizedKey = quest.Key;
            if (string.IsNullOrWhiteSpace(normalizedKey))
                throw new ArgumentException("QuestDefinition.Key 不能为空。", nameof(quest));

            if (_definitions.ContainsKey(normalizedKey))
                throw new InvalidOperationException($"重复的任务定义 Key：{normalizedKey}");

            _definitions.Add(normalizedKey, quest);
        }

        public bool TryGet(string key, out QuestDefinition quest)
        {
            quest = null;

            if (!LogicKey.TryNormalize(key, out var normalizedKey))
                return false;

            return _definitions.TryGetValue(normalizedKey, out quest);
        }
    }
}

