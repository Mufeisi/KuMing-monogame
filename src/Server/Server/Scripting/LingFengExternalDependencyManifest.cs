using System.Collections.ObjectModel;

namespace Server.Scripting
{
    public enum LingFengDependencyLevel
    {
        None = -1,
        E1,
        E2
    }

    public enum LingFengDependencyKind
    {
        ItemName,
        ItemIndex,
        Monster,
        Map,
        ClientContract,
        DomainAdapter
    }

    public sealed record LingFengDependencyRequirement(
        LingFengDependencyKind Kind,
        string Key,
        LingFengDependencyLevel Level,
        string SourceKey);

    public sealed record LingFengDependencyProbe(
        Func<string, bool> ItemByName,
        Func<int, bool> ItemByIndex,
        Func<string, bool> Monster,
        Func<string, bool> Map,
        Func<string, bool> ClientContract,
        Func<string, bool> DomainAdapter);

    public sealed class LingFengDependencyReport
    {
        internal LingFengDependencyReport(
            IReadOnlyList<LingFengDependencyRequirement> satisfied,
            IReadOnlyList<LingFengDependencyRequirement> missing)
        {
            Satisfied = satisfied;
            Missing = missing;
        }

        public IReadOnlyList<LingFengDependencyRequirement> Satisfied { get; }
        public IReadOnlyList<LingFengDependencyRequirement> Missing { get; }
        public bool Success => Missing.Count == 0;
    }

    public sealed class LingFengExternalDependencyManifest
    {
        private readonly LingFengDependencyRequirement[] _requirements;

        internal LingFengExternalDependencyManifest(IEnumerable<LingFengDependencyRequirement> requirements)
        {
            _requirements = (requirements ?? Array.Empty<LingFengDependencyRequirement>())
                .Where(value => value != null && !string.IsNullOrWhiteSpace(value.Key))
                .DistinctBy(value => $"{value.Kind}|{value.Key}|{value.SourceKey}", StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value.Level)
                .ThenBy(value => value.Kind)
                .ThenBy(value => value.Key, StringComparer.OrdinalIgnoreCase)
                .ThenBy(value => value.SourceKey, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            Requirements = new ReadOnlyCollection<LingFengDependencyRequirement>(_requirements);
        }

        public IReadOnlyList<LingFengDependencyRequirement> Requirements { get; }

        public LingFengDependencyReport Evaluate(
            LingFengDependencyLevel level,
            LingFengDependencyProbe probe)
        {
            if (probe == null) throw new ArgumentNullException(nameof(probe));
            var satisfied = new List<LingFengDependencyRequirement>();
            var missing = new List<LingFengDependencyRequirement>();
            var probeResults = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (LingFengDependencyRequirement requirement in _requirements)
            {
                if (requirement.Level > level) continue;
                string probeKey = $"{requirement.Kind}\0{requirement.Key}";
                if (!probeResults.TryGetValue(probeKey, out bool exists))
                {
                    exists = requirement.Kind switch
                    {
                        LingFengDependencyKind.ItemName => probe.ItemByName?.Invoke(requirement.Key) == true,
                        LingFengDependencyKind.ItemIndex => int.TryParse(requirement.Key, out int itemIndex) &&
                                                            probe.ItemByIndex?.Invoke(itemIndex) == true,
                        LingFengDependencyKind.Monster => probe.Monster?.Invoke(requirement.Key) == true,
                        LingFengDependencyKind.Map => probe.Map?.Invoke(requirement.Key) == true,
                        LingFengDependencyKind.ClientContract => probe.ClientContract?.Invoke(requirement.Key) == true,
                        LingFengDependencyKind.DomainAdapter => probe.DomainAdapter?.Invoke(requirement.Key) == true,
                        _ => false
                    };
                    probeResults[probeKey] = exists;
                }
                (exists ? satisfied : missing).Add(requirement);
            }
            return new LingFengDependencyReport(
                new ReadOnlyCollection<LingFengDependencyRequirement>(satisfied),
                new ReadOnlyCollection<LingFengDependencyRequirement>(missing));
        }
    }

    internal static class LingFengScriptDependencyExtractor
    {
        private sealed record Argument(int Index, LingFengDependencyKind Kind);

        private static readonly IReadOnlyDictionary<string, Argument[]> CheckCommands =
            new Dictionary<string, Argument[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["CHECKITEM"] = new[] { new Argument(1, LingFengDependencyKind.ItemName) },
                ["CHECKHEROITEM"] = new[] { new Argument(1, LingFengDependencyKind.ItemName) },
                ["HASBAGSPACE"] = new[] { new Argument(1, LingFengDependencyKind.ItemName) },
                ["CHECKMAP"] = new[] { new Argument(1, LingFengDependencyKind.Map) },
                ["CHECKMAPNAME"] = new[] { new Argument(1, LingFengDependencyKind.Map) },
                ["ISONMAP"] = new[] { new Argument(1, LingFengDependencyKind.Map) },
                ["CHECKMON"] = new[] { new Argument(1, LingFengDependencyKind.Map) },
                ["CHECKEXACTMON"] = new[]
                {
                    new Argument(1, LingFengDependencyKind.Map),
                    new Argument(4, LingFengDependencyKind.Monster)
                },
                ["CHECKPET"] = new[] { new Argument(1, LingFengDependencyKind.Monster) }
            };

        private static readonly IReadOnlyDictionary<string, Argument[]> ActionCommands =
            new Dictionary<string, Argument[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["GIVE"] = new[] { new Argument(1, LingFengDependencyKind.ItemName) },
                ["GIVEITEM"] = new[] { new Argument(1, LingFengDependencyKind.ItemName) },
                ["TAKE"] = new[] { new Argument(1, LingFengDependencyKind.ItemName) },
                ["TAKEITEM"] = new[] { new Argument(1, LingFengDependencyKind.ItemName) },
                ["ADDMAILITEM"] = new[] { new Argument(1, LingFengDependencyKind.ItemName) },
                ["GIVEPET"] = new[] { new Argument(1, LingFengDependencyKind.Monster) },
                ["REMOVEPET"] = new[] { new Argument(1, LingFengDependencyKind.Monster) },
                ["MONGEN"] = new[] { new Argument(1, LingFengDependencyKind.Monster) },
                ["MONCLEAR"] = new[]
                {
                    new Argument(1, LingFengDependencyKind.Map),
                    new Argument(3, LingFengDependencyKind.Monster)
                },
                ["MOVE"] = new[] { new Argument(1, LingFengDependencyKind.Map) },
                ["TELEPORT"] = new[] { new Argument(1, LingFengDependencyKind.Map) },
                ["INSTANCEMOVE"] = new[] { new Argument(1, LingFengDependencyKind.Map) },
                ["GROUPTELEPORT"] = new[] { new Argument(1, LingFengDependencyKind.Map) },
                ["PARAM1"] = new[] { new Argument(1, LingFengDependencyKind.Map) }
            };

        internal static IEnumerable<LingFengDependencyRequirement> Extract(
            IEnumerable<TextFileDefinition> definitions)
        {
            foreach (TextFileDefinition definition in definitions ?? Array.Empty<TextFileDefinition>())
            {
                string section = string.Empty;
                for (int index = 0; index < definition.Lines.Count; index++)
                {
                    string line = definition.Lines[index].TrimStart();
                    if (!TxtScriptTokenizer.TryTokenize(line, out string[] tokens, out _) || tokens.Length == 0)
                        continue;
                    string command = tokens[0].TrimStart('#');
                    if (tokens[0].StartsWith("#", StringComparison.Ordinal))
                    {
                        if (command.Equals("IF", StringComparison.OrdinalIgnoreCase)) section = "IF";
                        else if (command.Equals("ACT", StringComparison.OrdinalIgnoreCase) ||
                                 command.Equals("ELSEACT", StringComparison.OrdinalIgnoreCase)) section = "ACT";
                        else if (command.Equals("SAY", StringComparison.OrdinalIgnoreCase) ||
                                 command.Equals("ELSESAY", StringComparison.OrdinalIgnoreCase)) section = "SAY";
                        continue;
                    }
                    IReadOnlyDictionary<string, Argument[]> commands =
                        section == "IF" ? CheckCommands : section == "ACT" ? ActionCommands : null;
                    if (commands == null || !commands.TryGetValue(command, out Argument[] arguments))
                        continue;
                    foreach (Argument argument in arguments)
                    {
                        if (tokens.Length <= argument.Index) continue;
                        string key = tokens[argument.Index].Trim();
                        if (!IsStaticLiteral(key))
                        {
                            yield return new LingFengDependencyRequirement(
                                LingFengDependencyKind.DomainAdapter,
                                $"ScriptDynamic/{definition.Key}:{index + 1}:arg{argument.Index}",
                                LingFengDependencyLevel.E2,
                                $"{definition.Key}:{index + 1}");
                            continue;
                        }
                        yield return new LingFengDependencyRequirement(
                            argument.Kind, key, LingFengDependencyLevel.E1,
                            $"{definition.Key}:{index + 1}");
                    }
                }
            }
        }

        private static bool IsStaticLiteral(string value) =>
            !string.IsNullOrWhiteSpace(value) && value != "*" &&
            value.IndexOfAny(new[] { '<', '>', '$', '%', '[', ']', '{', '}' }) < 0;
    }
}
