namespace Server.Scripting
{
    internal static class NpcTxtSkipPolicy
    {
        private static readonly object Gate = new object();

        private static string _source = string.Empty;
        private static HashSet<string> _normalizedNpcFiles = new HashSet<string>(StringComparer.Ordinal);

        public static bool IsNpcFileInAllowlist(string npcFileName)
        {
            if (string.IsNullOrWhiteSpace(npcFileName))
                return false;

            EnsureCache();

            if (!TryNormalizeNpcFileName(npcFileName, out var normalized))
                return false;

            return _normalizedNpcFiles.Contains(normalized);
        }

        private static void EnsureCache()
        {
            var source = Settings.CSharpScriptsSkipTxtNpcLoadNpcFiles ?? string.Empty;

            if (string.Equals(source, _source, StringComparison.Ordinal))
                return;

            lock (Gate)
            {
                source = Settings.CSharpScriptsSkipTxtNpcLoadNpcFiles ?? string.Empty;

                if (string.Equals(source, _source, StringComparison.Ordinal))
                    return;

                _source = source;
                _normalizedNpcFiles = ParseNpcFiles(source);
            }
        }

        private static HashSet<string> ParseNpcFiles(string source)
        {
            var tokens = SplitTokens(source);
            if (tokens.Count == 0) return new HashSet<string>(StringComparer.Ordinal);

            var set = new HashSet<string>(StringComparer.Ordinal);

            for (var i = 0; i < tokens.Count; i++)
            {
                if (!TryNormalizeNpcFileName(tokens[i], out var normalized))
                    continue;

                set.Add(normalized);
            }

            return set;
        }

        private static List<string> SplitTokens(string source)
        {
            if (string.IsNullOrWhiteSpace(source)) return new List<string>(0);

            var parts = source.Split(new[] { ';', ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            var list = new List<string>(parts.Length);

            for (var i = 0; i < parts.Length; i++)
            {
                var token = parts[i].Trim();
                if (token.Length == 0) continue;
                list.Add(token);
            }

            return list;
        }

        internal static bool TryNormalizeNpcFileName(string raw, out string normalized)
        {
            normalized = string.Empty;

            if (string.IsNullOrWhiteSpace(raw))
                return false;

            var s = raw.Trim();

            s = s.Replace('\\', '/');

            while (s.Contains("//"))
            {
                s = s.Replace("//", "/");
            }

            if (s.StartsWith("./", StringComparison.Ordinal))
            {
                s = s.Substring(2);
            }

            s = s.TrimStart('/');

            if (s.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            {
                s = s.Substring(0, s.Length - 4);
            }

            s = s.TrimEnd('/');

            if (s.Length == 0)
                return false;

            var idx = s.LastIndexOf("/NPCs/", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                s = s.Substring(idx + "/NPCs/".Length);
            }
            else if (s.StartsWith("NPCs/", StringComparison.OrdinalIgnoreCase))
            {
                s = s.Substring("NPCs/".Length);
            }

            s = s.TrimStart('/');
            s = s.TrimEnd('/');

            if (s.Length == 0)
                return false;

            normalized = s.ToLowerInvariant();
            return true;
        }
    }
}

