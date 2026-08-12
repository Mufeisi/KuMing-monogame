namespace Server.Scripting
{
    public sealed class NpcFileRegistry
    {
        private readonly NpcRegistry _registry;
        private readonly string _npcFileName;

        internal NpcFileRegistry(NpcRegistry registry, string npcFileName)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));

            if (string.IsNullOrWhiteSpace(npcFileName))
                throw new ArgumentException("npcFileName 不能为空。", nameof(npcFileName));

            _npcFileName = npcFileName;
        }

        public void RegisterPage(string pageKey, OnNpcPageHook handler) =>
            _registry.RegisterPage(_npcFileName, pageKey, handler);

        public void Page(string pageKey, OnNpcPageHook handler) =>
            RegisterPage(pageKey, handler);

        public void RequestInput(string inputPageKey, OnNpcInputHook callback) =>
            _registry.RequestInput(_npcFileName, inputPageKey, callback);
    }
}

