using System.Collections.ObjectModel;

namespace Server.Scripting.ServerSymbols
{
    internal sealed class ServerSymbolDefinition
    {
        public ServerSymbolDefinition(
            string canonicalName,
            IEnumerable<string> aliases,
            string parameterForm,
            ServerSymbolValueType valueType,
            ServerSymbolContextKind requiredContext,
            ServerSymbolNoContextBehavior noContextBehavior,
            ServerSymbolSecurityClassification securityClassification,
            ServerSymbolAccessPolicy accessPolicy,
            string lingFengSemantics,
            string dataSource,
            string compatibilityLevel,
            IEnumerable<string> entryPoints,
            string timing,
            IEnumerable<string> testIds,
            string documentation,
            long usageCount,
            DateOnly lastReviewed)
        {
            if (!ServerSymbolReference.TryNormalizeName(canonicalName, out string normalizedName))
                throw new ArgumentException("服务器常量规范名称无效。", nameof(canonicalName));

            CanonicalName = normalizedName;
            Aliases = CopyNormalizedNames(aliases, nameof(aliases));
            ParameterForm = parameterForm?.Trim() ?? string.Empty;
            ParameterCount = CountParameters(ParameterForm);
            ValueType = valueType;
            RequiredContext = requiredContext;
            NoContextBehavior = noContextBehavior;
            SecurityClassification = securityClassification;
            AccessPolicy = accessPolicy;
            LingFengSemantics = lingFengSemantics?.Trim() ?? string.Empty;
            DataSource = dataSource?.Trim() ?? string.Empty;
            CompatibilityLevel = compatibilityLevel?.Trim() ?? string.Empty;
            EntryPoints = CopyStrings(entryPoints);
            Timing = timing?.Trim() ?? string.Empty;
            TestIds = CopyStrings(testIds);
            Documentation = documentation?.Trim() ?? string.Empty;
            UsageCount = Math.Max(0, usageCount);
            LastReviewed = lastReviewed;
        }

        public string CanonicalName { get; }
        public IReadOnlyList<string> Aliases { get; }
        public string ParameterForm { get; }
        public int ParameterCount { get; }
        public ServerSymbolValueType ValueType { get; }
        public ServerSymbolContextKind RequiredContext { get; }
        public ServerSymbolNoContextBehavior NoContextBehavior { get; }
        public ServerSymbolSecurityClassification SecurityClassification { get; }
        public ServerSymbolAccessPolicy AccessPolicy { get; }
        public string LingFengSemantics { get; }
        public string DataSource { get; }
        public string CompatibilityLevel { get; }
        public IReadOnlyList<string> EntryPoints { get; }
        public string Timing { get; }
        public IReadOnlyList<string> TestIds { get; }
        public string Documentation { get; }
        public long UsageCount { get; }
        public DateOnly LastReviewed { get; }

        private static IReadOnlyList<string> CopyNormalizedNames(IEnumerable<string> names, string argumentName)
        {
            var result = new List<string>();
            foreach (string name in names ?? Array.Empty<string>())
            {
                if (!ServerSymbolReference.TryNormalizeName(name, out string normalized))
                    throw new ArgumentException("服务器常量别名无效。", argumentName);
                result.Add(normalized);
            }
            return new ReadOnlyCollection<string>(result);
        }

        private static IReadOnlyList<string> CopyStrings(IEnumerable<string> values) =>
            new ReadOnlyCollection<string>((values ?? Array.Empty<string>())
                .Select(value => value?.Trim() ?? string.Empty)
                .ToList());

        private static int CountParameters(string parameterForm)
        {
            if (string.IsNullOrWhiteSpace(parameterForm)) return 0;
            if (parameterForm.Contains('[', StringComparison.Ordinal))
            {
                int openBracket = parameterForm.IndexOf('[');
                int closeBracket = parameterForm.IndexOf(']', openBracket + 1);
                if (openBracket <= 0 || closeBracket <= openBracket + 1 ||
                    parameterForm.IndexOf('[', openBracket + 1) >= 0 ||
                    parameterForm.IndexOf(']', closeBracket + 1) >= 0)
                    throw new ArgumentException("服务器常量索引参数形式无效。", nameof(parameterForm));
                return 1;
            }
            int open = parameterForm.IndexOf('(');
            if (open <= 0 || !parameterForm.EndsWith(")", StringComparison.Ordinal))
                throw new ArgumentException("服务器常量参数形式无效。", nameof(parameterForm));
            string inner = parameterForm.Substring(open + 1, parameterForm.Length - open - 2);
            if (string.IsNullOrWhiteSpace(inner)) return 0;
            return inner.Split(',').Length;
        }
    }

    internal sealed class ServerSymbolCatalog
    {
        private readonly IReadOnlyDictionary<string, ServerSymbolDefinition> _byName;

        private ServerSymbolCatalog(
            IDictionary<string, ServerSymbolDefinition> byName,
            IList<ServerSymbolDefinition> definitions)
        {
            _byName = new ReadOnlyDictionary<string, ServerSymbolDefinition>(
                new Dictionary<string, ServerSymbolDefinition>(byName, StringComparer.Ordinal));
            Definitions = new ReadOnlyCollection<ServerSymbolDefinition>(definitions.ToList());
        }

        public IReadOnlyList<ServerSymbolDefinition> Definitions { get; }

        public static bool TryCreate(
            IEnumerable<ServerSymbolDefinition> definitions,
            out ServerSymbolCatalog catalog,
            out string diagnostic)
        {
            var byName = new Dictionary<string, ServerSymbolDefinition>(StringComparer.Ordinal);
            var snapshot = new List<ServerSymbolDefinition>();

            foreach (ServerSymbolDefinition definition in definitions ?? Array.Empty<ServerSymbolDefinition>())
            {
                if (definition == null)
                {
                    catalog = null;
                    diagnostic = "服务器常量目录包含空定义。";
                    return false;
                }

                if (!TryValidateMetadata(definition, out diagnostic))
                {
                    catalog = null;
                    return false;
                }

                foreach (string name in new[] { definition.CanonicalName }.Concat(definition.Aliases))
                {
                    if (byName.TryGetValue(name, out ServerSymbolDefinition existing))
                    {
                        catalog = null;
                        diagnostic = $"服务器常量名称或别名冲突：{name}（{existing.CanonicalName} / {definition.CanonicalName}）。";
                        return false;
                    }
                    byName.Add(name, definition);
                }
                snapshot.Add(definition);
            }

            catalog = new ServerSymbolCatalog(byName, snapshot);
            diagnostic = string.Empty;
            return true;
        }

        private static bool TryValidateMetadata(ServerSymbolDefinition definition, out string diagnostic)
        {
            if ((definition.SecurityClassification & ServerSymbolSecurityClassification.Credential) != 0 &&
                definition.AccessPolicy != ServerSymbolAccessPolicy.Denied)
            {
                diagnostic = $"凭据类服务器常量必须拒绝解析：{definition.CanonicalName}。";
                return false;
            }

            if (string.IsNullOrWhiteSpace(definition.LingFengSemantics) ||
                string.IsNullOrWhiteSpace(definition.DataSource) ||
                string.IsNullOrWhiteSpace(definition.CompatibilityLevel) ||
                definition.EntryPoints.Count == 0 || definition.EntryPoints.Any(string.IsNullOrWhiteSpace) ||
                string.IsNullOrWhiteSpace(definition.Timing) ||
                definition.TestIds.Count == 0 || definition.TestIds.Any(string.IsNullOrWhiteSpace) ||
                string.IsNullOrWhiteSpace(definition.Documentation) ||
                definition.LastReviewed == default)
            {
                diagnostic = $"服务器常量目录元数据不完整：{definition.CanonicalName}。";
                return false;
            }

            diagnostic = string.Empty;
            return true;
        }

        public bool TryGet(string name, out ServerSymbolDefinition definition)
        {
            definition = null;
            return ServerSymbolReference.TryNormalizeName(name, out string normalized) &&
                   _byName.TryGetValue(normalized, out definition);
        }
    }
}
