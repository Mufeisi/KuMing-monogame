using System.Collections.ObjectModel;

namespace Server.Scripting.ServerSymbols
{
    internal sealed class ServerSymbolBinding
    {
        private readonly Func<ServerSymbolReference, ServerSymbolValue> _resolve;

        private ServerSymbolBinding(
            ServerSymbolContextKind contextKind,
            string canonicalName,
            Func<ServerSymbolReference, ServerSymbolValue> resolve)
        {
            if (!ServerSymbolReference.TryNormalizeName(canonicalName, out string normalizedName))
                throw new ArgumentException("服务器常量绑定名称无效。", nameof(canonicalName));

            ContextKind = contextKind;
            CanonicalName = normalizedName;
            _resolve = resolve ?? throw new ArgumentNullException(nameof(resolve));
        }

        public ServerSymbolContextKind ContextKind { get; }
        public string CanonicalName { get; }

        public static ServerSymbolBinding Value(
            ServerSymbolContextKind contextKind,
            string canonicalName,
            ServerSymbolValue value) =>
            new ServerSymbolBinding(contextKind, canonicalName, _ => value);

        public static ServerSymbolBinding Dynamic(
            ServerSymbolContextKind contextKind,
            string canonicalName,
            Func<ServerSymbolReference, ServerSymbolValue> resolve) =>
            new ServerSymbolBinding(contextKind, canonicalName, resolve);

        internal ServerSymbolValue Resolve(ServerSymbolReference reference) => _resolve(reference);
    }

    public sealed class ServerSymbolContext
    {
        private readonly IReadOnlyDictionary<string, ServerSymbolBinding> _bindings;

        internal ServerSymbolContext(
            ServerSymbolContextKind availableContexts,
            params ServerSymbolBinding[] bindings)
        {
            var snapshot = new Dictionary<string, ServerSymbolBinding>(StringComparer.Ordinal);
            foreach (ServerSymbolBinding binding in bindings ?? Array.Empty<ServerSymbolBinding>())
            {
                if (binding == null) throw new ArgumentException("服务器常量上下文包含空绑定。", nameof(bindings));
                if ((availableContexts & binding.ContextKind) != binding.ContextKind)
                    throw new ArgumentException($"绑定 {binding.CanonicalName} 的上下文未声明可用。", nameof(bindings));
                if (!snapshot.TryAdd(binding.CanonicalName, binding))
                    throw new ArgumentException($"服务器常量上下文绑定重复：{binding.CanonicalName}。", nameof(bindings));
            }

            AvailableContexts = availableContexts;
            _bindings = new ReadOnlyDictionary<string, ServerSymbolBinding>(snapshot);
        }

        public static ServerSymbolContext Empty { get; } = new ServerSymbolContext(ServerSymbolContextKind.None);
        public ServerSymbolContextKind AvailableContexts { get; }

        internal bool TryGetBinding(string canonicalName, out ServerSymbolBinding binding) =>
            _bindings.TryGetValue(canonicalName, out binding);
    }

    internal sealed class ServerSymbolResolver : IServerSymbolResolver
    {
        private readonly ServerSymbolCatalog _catalog;

        public ServerSymbolResolver(ServerSymbolCatalog catalog) =>
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));

        public ServerSymbolResult Resolve(ServerSymbolContext context, ServerSymbolReference reference)
        {
            if (context == null || reference == null || !reference.IsValid)
                return ServerSymbolResult.Fail(
                    ServerSymbolStatus.InvalidReference,
                    string.Empty,
                    "服务器常量引用语法无效。");

            if (!_catalog.TryGet(reference.NormalizedName, out ServerSymbolDefinition definition))
                return ServerSymbolResult.Fail(
                    ServerSymbolStatus.Unsupported,
                    reference.NormalizedName,
                    "服务器常量尚未登记支持。");

            if (reference.Arguments.Count != definition.ParameterCount)
                return ServerSymbolResult.Fail(
                    ServerSymbolStatus.InvalidReference,
                    definition.CanonicalName,
                    "服务器常量参数数量无效。");

            if (definition.AccessPolicy == ServerSymbolAccessPolicy.Denied)
                return ServerSymbolResult.Fail(
                    ServerSymbolStatus.SensitiveDenied,
                    definition.CanonicalName,
                    "服务器常量因安全策略拒绝解析。");

            if ((context.AvailableContexts & definition.RequiredContext) != definition.RequiredContext)
                return ServerSymbolResult.Fail(
                    ServerSymbolStatus.ContextUnavailable,
                    definition.CanonicalName,
                    "当前事件缺少服务器常量所需上下文。");

            if (!context.TryGetBinding(definition.CanonicalName, out ServerSymbolBinding binding))
                return ServerSymbolResult.Fail(
                    ServerSymbolStatus.DependencyMissing,
                    definition.CanonicalName,
                    "服务器常量所需数据尚未提供。");

            if ((binding.ContextKind & definition.RequiredContext) != definition.RequiredContext)
                return ServerSymbolResult.Fail(
                    ServerSymbolStatus.Faulted,
                    definition.CanonicalName,
                    "服务器常量 Adapter 上下文契约不匹配。");

            try
            {
                ServerSymbolValue value = binding.Resolve(reference.WithCanonicalName(definition.CanonicalName));
                if (value.Type != definition.ValueType)
                    return ServerSymbolResult.Fail(
                        ServerSymbolStatus.Faulted,
                        definition.CanonicalName,
                        "服务器常量 Adapter 返回了不兼容的数据类型。");

                return ServerSymbolResult.Resolved(definition.CanonicalName, value);
            }
            catch
            {
                return ServerSymbolResult.Fail(
                    ServerSymbolStatus.Faulted,
                    definition.CanonicalName,
                    "服务器常量 Adapter 解析失败。");
            }
        }
    }
}
