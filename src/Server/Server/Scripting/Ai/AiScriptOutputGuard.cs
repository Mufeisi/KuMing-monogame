using System;
using System.Collections.Generic;
using System.IO;

namespace Server.Scripting.Ai
{
    public static class AiScriptOutputGuard
    {
        private static readonly string[] AllowedUsingPrefixes =
        {
            "System",
            "Server",
            "Shared",
        };

        private static readonly string[] DisallowedUsingPrefixes =
        {
            "System.IO",
            "System.Net",
            "System.Reflection",
            "System.Runtime.InteropServices",
            "Microsoft.Win32",
            "System.Diagnostics",
        };

        private static readonly string[] DangerousTokens =
        {
            "System.IO.File",
            "System.IO.Directory",
            "System.IO.FileInfo",
            "System.IO.DirectoryInfo",
            "System.IO.FileStream",
            "System.IO.StreamReader",
            "System.IO.StreamWriter",
            "System.Net.Http.HttpClient",
            "System.Net.WebClient",
            "System.Net.Sockets",
            "System.Diagnostics.Process",
            "System.Diagnostics.ProcessStartInfo",
            "System.Reflection",
            "System.Runtime.InteropServices",
            "DllImport",
            "Microsoft.Win32.Registry",
            "Environment.Exit",
            "Environment.FailFast",
        };

        public static IReadOnlyList<string> Check(string? code)
        {
            if (string.IsNullOrWhiteSpace(code))
                return Array.Empty<string>();

            var issues = new List<string>();

            foreach (var usingNamespace in ExtractUsingNamespaces(code))
            {
                if (!IsUsingAllowed(usingNamespace))
                    issues.Add($"不允许的 using: {usingNamespace}");
            }

            foreach (var token in DangerousTokens)
            {
                if (code.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                    issues.Add($"检测到潜在危险 API: {token}");
            }

            if (issues.Count == 0)
                return Array.Empty<string>();

            var uniqueIssues = new List<string>(issues.Count);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var issue in issues)
            {
                if (seen.Add(issue))
                    uniqueIssues.Add(issue);
            }

            return uniqueIssues.ToArray();
        }

        private static IEnumerable<string> ExtractUsingNamespaces(string code)
        {
            using var reader = new StringReader(code);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                var trimmed = line.Trim();
                if (!trimmed.StartsWith("using ", StringComparison.Ordinal) || !trimmed.EndsWith(";", StringComparison.Ordinal))
                    continue;

                var statement = trimmed.Substring("using ".Length, trimmed.Length - "using ".Length - 1).Trim();
                if (statement.StartsWith("static ", StringComparison.Ordinal))
                    statement = statement.Substring("static ".Length).Trim();

                if (statement.Contains("="))
                {
                    var parts = statement.Split(new[] { '=' }, 2);
                    if (parts.Length == 2)
                        statement = parts[1].Trim();
                }

                if (!string.IsNullOrWhiteSpace(statement))
                    yield return statement;
            }
        }

        private static bool IsUsingAllowed(string usingNamespace)
        {
            foreach (var disallowedPrefix in DisallowedUsingPrefixes)
            {
                if (usingNamespace.StartsWith(disallowedPrefix, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            foreach (var allowedPrefix in AllowedUsingPrefixes)
            {
                if (usingNamespace.StartsWith(allowedPrefix, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
    }
}

