namespace Server.Scripting
{
    public static class LingFengScriptReferenceResolver
    {
        public static bool IsExternalCallbackLabel(string rawLabel)
        {
            string label = (rawLabel ?? string.Empty).Trim().TrimStart('[').TrimEnd(']');
            return label.StartsWith("@_@", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsKnownExternalInclude(string rawTarget) =>
            string.Equals((rawTarget ?? string.Empty).Trim().Trim('[', ']'),
                "Constant.Ini", StringComparison.OrdinalIgnoreCase);

        public static bool TryResolveUniquePage(
            ITextFileProvider provider, string rawLabel, out string targetKey)
        {
            targetKey = string.Empty;
            if (provider == null || string.IsNullOrWhiteSpace(rawLabel)) return false;
            string label = rawLabel.Trim();
            if (!label.StartsWith("[@", StringComparison.Ordinal))
                label = label.StartsWith("@", StringComparison.Ordinal)
                    ? "[" + label + "]"
                    : "[@" + label + "]";

            string match = null;
            foreach (TextFileDefinition definition in provider.GetAll())
            {
                if (definition.Key.Equals("SystemScripts/AutoRunRobot", StringComparison.OrdinalIgnoreCase) ||
                    !definition.Lines.Any(line =>
                        string.Equals(line?.Trim(), label, StringComparison.OrdinalIgnoreCase)))
                    continue;
                if (match != null && !match.Equals(definition.Key, StringComparison.OrdinalIgnoreCase))
                    return false;
                match = definition.Key;
            }
            if (match == null) return false;
            targetKey = match;
            return true;
        }

        public static bool TryResolveCandidateTextKey(string rawTarget, out string targetKey)
        {
            targetKey = string.Empty;
            string target = (rawTarget ?? string.Empty).Trim().Trim('"').Replace('\\', '/');
            if (target.Length == 0 || Path.IsPathRooted(target) ||
                !target.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                return false;

            string combined = target.StartsWith("../", StringComparison.Ordinal)
                ? "Market_Def/" + target
                : target;
            var segments = new List<string>();
            foreach (string rawSegment in combined.Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                string segment = rawSegment.Trim();
                if (segment.Length == 0 || segment == ".") continue;
                if (segment == "..")
                {
                    if (segments.Count == 0) return false;
                    segments.RemoveAt(segments.Count - 1);
                    continue;
                }
                if (segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return false;
                segments.Add(segment);
            }
            if (segments.Count == 0) return false;

            string relative = string.Join('/', segments);
            if (relative.StartsWith("Market_Def/", StringComparison.OrdinalIgnoreCase))
                relative = "NPCs/" + relative.Substring("Market_Def/".Length);
            else if (relative.StartsWith("Npc_def/", StringComparison.OrdinalIgnoreCase))
                relative = "NpcDefs/" + relative.Substring("Npc_def/".Length);
            else if (!relative.StartsWith("QuestDiary/", StringComparison.OrdinalIgnoreCase) &&
                     !relative.StartsWith("DeFines/", StringComparison.OrdinalIgnoreCase))
                return false;

            return LogicKey.TryNormalize(relative, out targetKey);
        }

        public static bool TryResolveCallKey(string rawTarget, out string targetKey)
        {
            targetKey = string.Empty;
            string target = (rawTarget ?? string.Empty).Trim().Trim('[', ']').Replace('\\', '/');
            if (target.Length == 0 || target.StartsWith("//", StringComparison.Ordinal)) return false;

            bool rootRelative = target[0] == '/';
            if (rootRelative) target = target.TrimStart('/');
            if (target.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) ||
                target.EndsWith(".ini", StringComparison.OrdinalIgnoreCase))
                target = target.Substring(0, target.Length - 4);
            if (target.Length == 0) return false;

            if (rootRelative && !target.StartsWith("QuestDiary/", StringComparison.OrdinalIgnoreCase))
                target = "QuestDiary/" + target;
            else if (!target.Contains('/'))
                target = "NPCs/" + target;

            return LogicKey.TryNormalize(target, out targetKey);
        }
    }
}
