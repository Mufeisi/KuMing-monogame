using System;
using System.Collections.Generic;

namespace Server.Scripting
{
    /// <summary>
    /// txt 回落策略（按 Key/前缀关闭回落，用于逐步退役 legacy txt）。
    /// </summary>
    public static class TxtFallbackPolicy
    {
        private static readonly object Gate = new object();

        private static bool _enabledCache;
        private static string _prefixesSource = string.Empty;
        private static IReadOnlyList<string> _prefixes = Array.Empty<string>();

        private static string _keysSource = string.Empty;
        private static HashSet<string> _keys = new HashSet<string>(StringComparer.Ordinal);

        public static bool ShouldFallbackToTxt(string logicKey)
        {
            // 全局开关优先：关闭后任何 Key 均不允许回落。
            if (!Settings.CSharpScriptsFallbackToTxt)
                return false;

            if (!Settings.CSharpScriptsNoTxtFallbackEnabled)
                return true;

            if (string.IsNullOrWhiteSpace(logicKey))
                return true;

            EnsureCache();

            if (!LogicKey.TryNormalize(logicKey, out var normalizedKey))
                return true;

            if (_keys.Contains(normalizedKey))
                return false;

            var prefixes = _prefixes;
            for (var i = 0; i < prefixes.Count; i++)
            {
                if (IsPrefixMatch(prefixes[i], normalizedKey))
                    return false;
            }

            return true;
        }

        private static void EnsureCache()
        {
            var enabled = Settings.CSharpScriptsNoTxtFallbackEnabled;
            var prefixSource = Settings.CSharpScriptsNoTxtFallbackKeyPrefixes ?? string.Empty;
            var keysSource = Settings.CSharpScriptsNoTxtFallbackKeys ?? string.Empty;

            if (enabled == _enabledCache &&
                string.Equals(prefixSource, _prefixesSource, StringComparison.Ordinal) &&
                string.Equals(keysSource, _keysSource, StringComparison.Ordinal))
            {
                return;
            }

            lock (Gate)
            {
                enabled = Settings.CSharpScriptsNoTxtFallbackEnabled;
                prefixSource = Settings.CSharpScriptsNoTxtFallbackKeyPrefixes ?? string.Empty;
                keysSource = Settings.CSharpScriptsNoTxtFallbackKeys ?? string.Empty;

                if (enabled == _enabledCache &&
                    string.Equals(prefixSource, _prefixesSource, StringComparison.Ordinal) &&
                    string.Equals(keysSource, _keysSource, StringComparison.Ordinal))
                {
                    return;
                }

                _enabledCache = enabled;
                _prefixesSource = prefixSource;
                _keysSource = keysSource;

                _prefixes = ParsePrefixes(prefixSource);
                _keys = ParseKeys(keysSource);
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

        private static HashSet<string> ParseKeys(string source)
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
