namespace Server.Scripting
{
    public sealed class NpcShopRegistry
    {
        private readonly Dictionary<string, NpcShopDefinition> _definitions =
            new Dictionary<string, NpcShopDefinition>(StringComparer.Ordinal);

        public int Count => _definitions.Count;

        public IReadOnlyDictionary<string, NpcShopDefinition> Definitions => _definitions;

        public void Register(NpcShopDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));

            var normalizedKey = definition.Key;
            if (string.IsNullOrWhiteSpace(normalizedKey))
                throw new ArgumentException("NpcShopDefinition.Key 不能为空。", nameof(definition));

            if (_definitions.ContainsKey(normalizedKey))
                throw new InvalidOperationException($"重复的 NPC 商店定义 Key：{normalizedKey}");

            _definitions.Add(normalizedKey, definition);
        }

        public bool TryGetByNpcFileName(string npcFileName, out NpcShopDefinition definition)
        {
            definition = null;

            if (string.IsNullOrWhiteSpace(npcFileName)) return false;

            foreach (var candidate in NpcFileNameAliases.Enumerate(npcFileName))
            {
                var key = $"NPCs/{candidate}";
                if (!LogicKey.TryNormalize(key, out var normalizedKey))
                    continue;

                if (_definitions.TryGetValue(normalizedKey, out definition))
                    return true;
            }

            definition = null;
            return false;
        }
    }
}
