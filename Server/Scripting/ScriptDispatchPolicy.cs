namespace Server.Scripting
{
    public static class ScriptDispatchPolicy
    {
        private static readonly object Gate = new object();

        private static string _preferKeyPrefixesSource = string.Empty;
        private static IReadOnlyList<string> _preferKeyPrefixes = Array.Empty<string>();

        private static string _disabledKeysSource = string.Empty;
        private static HashSet<string> _disabledKeys = new HashSet<string>(StringComparer.Ordinal);

        public static bool ShouldTryCSharp(string logicKey)
        {
            if (string.IsNullOrWhiteSpace(logicKey))
                return true;

            EnsureCache();

            if (!LogicKey.TryNormalize(logicKey, out var normalizedKey))
                return true;

            if (_disabledKeys.Contains(normalizedKey))
                return false;

            var prefixes = _preferKeyPrefixes;
            if (prefixes.Count == 0)
                return true;

            for (var i = 0; i < prefixes.Count; i++)
            {
                if (IsPrefixMatch(prefixes[i], normalizedKey))
                    return true;
            }

            return false;
        }

        private static void EnsureCache()
        {
            var preferSource = Settings.CSharpScriptsPreferKeyPrefixes ?? string.Empty;
            var disabledSource = Settings.CSharpScriptsDisabledKeys ?? string.Empty;

            if (string.Equals(preferSource, _preferKeyPrefixesSource, StringComparison.Ordinal) &&
                string.Equals(disabledSource, _disabledKeysSource, StringComparison.Ordinal))
            {
                return;
            }

            lock (Gate)
            {
                preferSource = Settings.CSharpScriptsPreferKeyPrefixes ?? string.Empty;
                disabledSource = Settings.CSharpScriptsDisabledKeys ?? string.Empty;

                if (string.Equals(preferSource, _preferKeyPrefixesSource, StringComparison.Ordinal) &&
                    string.Equals(disabledSource, _disabledKeysSource, StringComparison.Ordinal))
                {
                    return;
                }

                _preferKeyPrefixesSource = preferSource;
                _disabledKeysSource = disabledSource;

                _preferKeyPrefixes = ParsePrefixes(preferSource);
                _disabledKeys = ParseDisabledKeys(disabledSource);
            }
        }

        private static IReadOnlyList<string> ParsePrefixes(string source)
        {
            var tokens = SplitTokens(source);
            if (tokens.Count == 0) return Array.Empty<string>();

            var list = new List<string>(tokens.Count);

            for (var i = 0; i < tokens.Count; i++)
            {
                var prefix = NormalizePrefix(tokens[i]);
                if (prefix.Length == 0) continue;
                list.Add(prefix);
            }

            return list;
        }

        private static HashSet<string> ParseDisabledKeys(string source)
        {
            var tokens = SplitTokens(source);
            if (tokens.Count == 0) return new HashSet<string>(StringComparer.Ordinal);

            var set = new HashSet<string>(StringComparer.Ordinal);

            for (var i = 0; i < tokens.Count; i++)
            {
                if (!LogicKey.TryNormalize(tokens[i], out var normalizedKey)) continue;
                set.Add(normalizedKey);
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

        private static string NormalizePrefix(string raw)
        {
            var p = raw.Trim();
            if (p.Length == 0) return string.Empty;

            p = p.Replace('\\', '/');

            while (p.Contains("//"))
            {
                p = p.Replace("//", "/");
            }

            if (p.StartsWith("./", StringComparison.Ordinal))
            {
                p = p.Substring(2);
            }

            p = p.TrimStart('/');

            if (p.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            {
                p = p.Substring(0, p.Length - 4);
            }
            else if (p.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                p = p.Substring(0, p.Length - 3);
            }

            if (p.EndsWith("/**", StringComparison.Ordinal))
            {
                p = p.Substring(0, p.Length - 3);
            }
            else if (p.EndsWith("/*", StringComparison.Ordinal))
            {
                p = p.Substring(0, p.Length - 2);
            }
            else if (p.EndsWith("**", StringComparison.Ordinal))
            {
                p = p.Substring(0, p.Length - 2);
            }
            else if (p.EndsWith("*", StringComparison.Ordinal))
            {
                p = p.Substring(0, p.Length - 1);
            }

            p = p.TrimEnd('/');

            if (p.Length == 0) return string.Empty;

            return p.ToLowerInvariant();
        }

        private static bool IsPrefixMatch(string normalizedPrefix, string normalizedKey)
        {
            if (normalizedPrefix.Length == 0) return true;

            if (string.Equals(normalizedKey, normalizedPrefix, StringComparison.Ordinal))
                return true;

            if (normalizedKey.Length > normalizedPrefix.Length &&
                normalizedKey.StartsWith(normalizedPrefix, StringComparison.Ordinal) &&
                normalizedKey[normalizedPrefix.Length] == '/')
            {
                return true;
            }

            return false;
        }
    }
}

