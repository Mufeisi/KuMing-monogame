using System.Text;

namespace Server.Scripting.Ai
{
    public sealed class ScriptDocumentationContextBuilder
    {
        private sealed class QueryToken
        {
            public QueryToken(string value, int weight)
            {
                Value = value;
                Weight = weight;
            }

            public string Value { get; }
            public int Weight { get; }
        }

        private sealed class MarkdownSection
        {
            public int StartLine { get; init; }
            public int EndLineExclusive { get; init; }
            public int Level { get; init; }
            public string Heading { get; init; } = string.Empty;

            public string DisplayTitle => string.IsNullOrWhiteSpace(Heading) ? $"L{Level}@{StartLine + 1}" : Heading;
        }

        private static readonly string[] DefaultDocumentPaths =
        {
            Path.Combine("Docs", "Scripting", "ScriptManual.md"),
            Path.Combine("Docs", "Scripting", "KeySpec.md"),
        };

        public IReadOnlyList<DocumentationSnippet> BuildMvpContext(AiScriptOptions options)
        {
            return BuildMvpContext(options, new ScriptGenerationRequest());
        }

        public IReadOnlyList<DocumentationSnippet> BuildMvpContext(AiScriptOptions options, ScriptGenerationRequest request)
        {
            options ??= new AiScriptOptions();
            request ??= new ScriptGenerationRequest();

            var snippets = new List<DocumentationSnippet>();
            var projectRoot = TryFindProjectRoot();
            if (string.IsNullOrWhiteSpace(projectRoot))
                return snippets;

            var remaining = Math.Max(1024, options.DocumentationMaxCharacters > 0 ? options.DocumentationMaxCharacters : 12000);
            var tokens = BuildQueryTokens(request);

            for (var index = 0; index < DefaultDocumentPaths.Length; index++)
            {
                var relativePath = DefaultDocumentPaths[index];
                if (remaining <= 0)
                    break;

                var fullPath = Path.Combine(projectRoot, relativePath);
                if (!File.Exists(fullPath))
                    continue;

                var text = File.ReadAllText(fullPath, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(text))
                    continue;

                var source = relativePath.Replace('\\', '/');
                var docsRemaining = DefaultDocumentPaths.Length - index;
                var docBudget = Math.Min(remaining, Math.Max(1024, remaining / Math.Max(1, docsRemaining)));
                if (tokens.Count == 0)
                {
                    var trimmed = TrimToLength(text, docBudget);
                    if (string.IsNullOrWhiteSpace(trimmed))
                        continue;

                    snippets.Add(new DocumentationSnippet
                    {
                        Source = source,
                        Content = trimmed,
                    });

                    remaining -= trimmed.Length;
                    continue;
                }

                var extractedSnippets = ExtractRelevantSnippets(source, text, tokens, docBudget);
                if (extractedSnippets.Count == 0)
                {
                    // 如果按 Key 检索没有命中，则退回到“前缀注入”，避免无文档上下文。
                    var trimmed = TrimToLength(text, docBudget);
                    if (string.IsNullOrWhiteSpace(trimmed))
                        continue;

                    snippets.Add(new DocumentationSnippet
                    {
                        Source = source + "（未命中 Key，降级为前缀注入）",
                        Content = trimmed,
                    });

                    remaining -= trimmed.Length;
                    continue;
                }

                foreach (var snippet in extractedSnippets)
                {
                    if (remaining <= 0)
                        break;

                    var trimmed = TrimToLength(snippet.Content, remaining);
                    if (string.IsNullOrWhiteSpace(trimmed))
                        continue;

                    snippets.Add(new DocumentationSnippet
                    {
                        Source = snippet.Source,
                        Content = trimmed,
                    });

                    remaining -= trimmed.Length;
                }
            }

            return snippets;
        }

        private static IReadOnlyList<QueryToken> BuildQueryTokens(ScriptGenerationRequest request)
        {
            request ??= new ScriptGenerationRequest();

            var tokenWeights = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            void AddToken(string value, int weight)
            {
                if (string.IsNullOrWhiteSpace(value))
                    return;

                var normalized = value.Trim();
                if (normalized.Length < 2)
                    return;

                if (tokenWeights.TryGetValue(normalized, out var existing) && existing >= weight)
                    return;

                tokenWeights[normalized] = weight;
            }

            var key = (request.TargetKey ?? string.Empty)
                .Trim()
                .Replace('\\', '/')
                .TrimStart('/')
                .ToLowerInvariant();

            if (!string.IsNullOrWhiteSpace(key))
            {
                AddToken(key, 80);

                var segments = key.Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (segments.Length > 0) AddToken(segments[0], 30);
                if (segments.Length > 1) AddToken(segments[^1], 25);

                foreach (var segment in segments)
                {
                    if (segment.Length < 2) continue;
                    if (segment == segments[0] || segment == segments[^1]) continue;
                    AddToken(segment, 15);
                }
            }

            switch (request.Kind)
            {
                case ScriptGenerationKind.DropTable:
                    AddToken("drop", 25);
                    AddToken("drops", 25);
                    AddToken("掉落", 25);
                    break;
                case ScriptGenerationKind.Quest:
                    AddToken("quest", 25);
                    AddToken("任务", 25);
                    break;
                case ScriptGenerationKind.Dialog:
                    AddToken("npc", 25);
                    AddToken("dialog", 25);
                    AddToken("对话", 25);
                    break;
                case ScriptGenerationKind.Route:
                    AddToken("route", 20);
                    AddToken("路线", 20);
                    break;
                case ScriptGenerationKind.ValueTable:
                    AddToken("table", 20);
                    AddToken("值表", 20);
                    break;
                case ScriptGenerationKind.NameList:
                    AddToken("list", 20);
                    AddToken("名单", 20);
                    break;
                case ScriptGenerationKind.TextFile:
                    AddToken("txt", 20);
                    AddToken("文本", 20);
                    break;
            }

            return tokenWeights
                .OrderByDescending(kvp => kvp.Value)
                .Take(12)
                .Select(kvp => new QueryToken(kvp.Key, kvp.Value))
                .ToArray();
        }

        private static IReadOnlyList<DocumentationSnippet> ExtractRelevantSnippets(string source, string text, IReadOnlyList<QueryToken> tokens, int budget)
        {
            if (string.IsNullOrWhiteSpace(text) || tokens.Count == 0 || budget <= 0)
                return Array.Empty<DocumentationSnippet>();

            var normalized = text.Replace("\r\n", "\n");
            var lines = normalized.Split('\n');
            var sections = BuildMarkdownSections(lines);
            if (sections.Count == 0)
                return Array.Empty<DocumentationSnippet>();

            var scored = new List<(MarkdownSection Section, int Score)>();
            foreach (var section in sections)
            {
                var score = ScoreSection(lines, section, tokens);
                if (score > 0)
                    scored.Add((section, score));
            }

            if (scored.Count == 0)
                return Array.Empty<DocumentationSnippet>();

            scored.Sort((a, b) =>
            {
                var byScore = b.Score.CompareTo(a.Score);
                return byScore != 0 ? byScore : a.Section.StartLine.CompareTo(b.Section.StartLine);
            });

            var results = new List<DocumentationSnippet>();
            var remaining = budget;
            foreach (var (section, _) in scored.Take(6))
            {
                if (remaining <= 0)
                    break;

                var perSectionBudget = Math.Min(remaining, 4000);
                var content = JoinLines(lines, section.StartLine, section.EndLineExclusive).TrimEnd();
                if (string.IsNullOrWhiteSpace(content))
                    continue;

                if (content.Length > perSectionBudget)
                {
                    content = content[..perSectionBudget].TrimEnd() + "\n...(已截断)";
                }

                results.Add(new DocumentationSnippet
                {
                    Source = $"{source}#{section.DisplayTitle}",
                    Content = content,
                });

                remaining -= content.Length;
            }

            return results;
        }

        private static int ScoreSection(string[] lines, MarkdownSection section, IReadOnlyList<QueryToken> tokens)
        {
            var score = 0;
            var headingLine = section.StartLine >= 0 && section.StartLine < lines.Length ? lines[section.StartLine] : string.Empty;

            foreach (var token in tokens)
            {
                if (string.IsNullOrWhiteSpace(token.Value))
                    continue;

                var hitInHeading = ContainsIgnoreCase(headingLine, token.Value);
                if (hitInHeading)
                    score += token.Weight * 2;

                var hits = CountHits(lines, section.StartLine, section.EndLineExclusive, token.Value, maxHits: 6);
                if (hits <= 0)
                    continue;

                score += token.Weight;
                score += Math.Min(2, hits - 1) * (token.Weight / 2);
            }

            return score;
        }

        private static int CountHits(string[] lines, int startLine, int endLineExclusive, string token, int maxHits)
        {
            if (lines.Length == 0 || string.IsNullOrWhiteSpace(token) || maxHits <= 0)
                return 0;

            var start = Math.Max(0, startLine);
            var end = Math.Min(lines.Length, endLineExclusive);
            var count = 0;

            for (var i = start; i < end; i++)
            {
                var line = lines[i];
                if (ContainsIgnoreCase(line, token))
                {
                    count++;
                    if (count >= maxHits)
                        return count;
                }
            }

            return count;
        }

        private static bool ContainsIgnoreCase(string text, string token)
        {
            return !string.IsNullOrEmpty(text) &&
                   !string.IsNullOrWhiteSpace(token) &&
                   text.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static List<MarkdownSection> BuildMarkdownSections(string[] lines)
        {
            var headings = new List<(int LineIndex, int Level, string HeadingText)>();
            for (var i = 0; i < lines.Length; i++)
            {
                if (TryParseHeading(lines[i], out var level, out var headingText))
                    headings.Add((i, level, headingText));
            }

            if (headings.Count == 0)
            {
                return new List<MarkdownSection>
                {
                    new MarkdownSection { StartLine = 0, EndLineExclusive = lines.Length, Level = 1, Heading = string.Empty },
                };
            }

            var sections = new List<MarkdownSection>(headings.Count);
            for (var i = 0; i < headings.Count; i++)
            {
                var start = headings[i].LineIndex;
                var level = headings[i].Level;
                var end = lines.Length;

                for (var j = i + 1; j < headings.Count; j++)
                {
                    if (headings[j].Level <= level)
                    {
                        end = headings[j].LineIndex;
                        break;
                    }
                }

                sections.Add(new MarkdownSection
                {
                    StartLine = start,
                    EndLineExclusive = end,
                    Level = level,
                    Heading = headings[i].HeadingText,
                });
            }

            return sections;
        }

        private static bool TryParseHeading(string line, out int level, out string headingText)
        {
            level = 0;
            headingText = string.Empty;
            if (string.IsNullOrEmpty(line))
                return false;

            var count = 0;
            while (count < line.Length && count < 6 && line[count] == '#')
                count++;

            if (count == 0 || count > 6)
                return false;

            if (count >= line.Length || line[count] != ' ')
                return false;

            level = count;
            headingText = line[(count + 1)..].Trim();
            return true;
        }

        private static string JoinLines(string[] lines, int startLine, int endLineExclusive)
        {
            var start = Math.Max(0, startLine);
            var end = Math.Min(lines.Length, endLineExclusive);
            if (start >= end)
                return string.Empty;

            return string.Join("\n", lines.Skip(start).Take(end - start));
        }

        private static string TrimToLength(string text, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(text) || maxLength <= 0)
                return string.Empty;

            if (text.Length <= maxLength)
                return text;

            return text[..maxLength];
        }

        private static string TryFindProjectRoot()
        {
            var current = new DirectoryInfo(AppContext.BaseDirectory);
            while (current != null)
            {
                var docsPath = Path.Combine(current.FullName, "Docs", "Scripting");
                if (Directory.Exists(docsPath))
                    return current.FullName;

                current = current.Parent;
            }

            return string.Empty;
        }
    }
}
