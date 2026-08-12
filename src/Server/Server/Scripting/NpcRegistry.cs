namespace Server.Scripting
{
    public sealed class NpcRegistry
    {
        private readonly ScriptRegistry _registry;

        public NpcRegistry(ScriptRegistry registry)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        public NpcFileRegistry For(string npcFileName) => new NpcFileRegistry(this, npcFileName);

        /// <summary>
        /// 注册某个 NPC 脚本文件（Envir/NPCs 下的 FileName）某个页面（PageKey）的处理器。
        /// </summary>
        public void RegisterPage(string npcFileName, string pageKey, OnNpcPageHook handler)
        {
            if (string.IsNullOrWhiteSpace(npcFileName))
                throw new ArgumentException("npcFileName 不能为空。", nameof(npcFileName));

            if (string.IsNullOrWhiteSpace(pageKey))
                throw new ArgumentException("pageKey 不能为空。", nameof(pageKey));

            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            var hookKey = ScriptHookKeys.OnNpcPage(npcFileName, pageKey);

            if (_registry.TryGet<OnNpcPageHook>(hookKey, out var existing))
            {
                if (Delegate.Equals(existing, handler))
                    return;

                throw new InvalidOperationException($"重复的 NPC 页面处理器 Key：{LogicKey.NormalizeOrThrow(hookKey)}");
            }

            _registry.Register(hookKey, handler);
        }

        /// <summary>
        /// 注册输入框回调页（legacy 的 [@@] 机制）。
        /// 说明：
        /// - inputPageKey 支持：[@@KEY] / @@KEY / [@KEY] / @KEY / KEY（均会归一化为 [@@KEY]）
        /// - 回调时可从 <see cref="NpcPageCall.Input"/> 取到玩家输入内容
        /// </summary>
        public void RequestInput(string npcFileName, string inputPageKey, OnNpcInputHook callback)
        {
            if (callback == null) throw new ArgumentNullException(nameof(callback));

            var pageKey = NormalizeInputPageKey(inputPageKey);

            RegisterPage(npcFileName, pageKey, (context, player, call, dialog) =>
            {
                var input = call?.Input ?? string.Empty;
                return callback(context, player, call, input, dialog);
            });
        }

        private static string NormalizeInputPageKey(string inputKeyOrPageKey)
        {
            if (string.IsNullOrWhiteSpace(inputKeyOrPageKey))
                throw new ArgumentException("inputPageKey 不能为空。", nameof(inputKeyOrPageKey));

            var s = inputKeyOrPageKey.Trim();

            if (s.StartsWith("[@@", StringComparison.OrdinalIgnoreCase) && s.EndsWith("]", StringComparison.Ordinal))
                return s;

            // [@X] -> [@@X]
            if (s.StartsWith("[@", StringComparison.OrdinalIgnoreCase) && s.EndsWith("]", StringComparison.Ordinal))
                return "[@@" + s.Substring(2);

            // @@X -> [@@X]
            if (s.StartsWith("@@", StringComparison.OrdinalIgnoreCase))
                return $"[{s}]";

            // @X -> [@@X]
            if (s.StartsWith("@", StringComparison.Ordinal))
                return $"[@@{s.Substring(1)}]";

            // X -> [@@X]
            return $"[@@{s}]";
        }
    }
}
