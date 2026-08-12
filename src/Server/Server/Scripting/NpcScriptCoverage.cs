using System.Text.RegularExpressions;
using Server.MirEnvir;

namespace Server.Scripting
{
    internal sealed class NpcScriptCoverageReport
    {
        public string NpcFileName { get; }
        public string StartPageKey { get; }
        public IReadOnlyList<string> ReachablePageKeys { get; }
        public IReadOnlyList<string> MissingCSharpPageKeys { get; }
        public IReadOnlyList<string> PolicyDisallowedPageKeys { get; }
        public IReadOnlyList<string> LegacyNonDialogSections { get; }
        public IReadOnlyList<string> Diagnostics { get; }

        public bool IsCoverageComplete => MissingCSharpPageKeys.Count == 0 && PolicyDisallowedPageKeys.Count == 0;

        public NpcScriptCoverageReport(
            string npcFileName,
            string startPageKey,
            IReadOnlyList<string> reachablePageKeys,
            IReadOnlyList<string> missingCSharpPageKeys,
            IReadOnlyList<string> policyDisallowedPageKeys,
            IReadOnlyList<string> legacyNonDialogSections,
            IReadOnlyList<string> diagnostics)
        {
            NpcFileName = npcFileName ?? string.Empty;
            StartPageKey = startPageKey ?? string.Empty;
            ReachablePageKeys = reachablePageKeys ?? Array.Empty<string>();
            MissingCSharpPageKeys = missingCSharpPageKeys ?? Array.Empty<string>();
            PolicyDisallowedPageKeys = policyDisallowedPageKeys ?? Array.Empty<string>();
            LegacyNonDialogSections = legacyNonDialogSections ?? Array.Empty<string>();
            Diagnostics = diagnostics ?? Array.Empty<string>();
        }
    }

    internal static class NpcScriptCoverage
    {
        private static readonly Regex ButtonRegex = new Regex(@"<.*?/(\@.*?)>", RegexOptions.Compiled);
        private static readonly Regex ArgsRegex = new Regex(@"\((.*)\)", RegexOptions.Compiled);

        private const string BuiltinExitPage = "[@EXIT]";

        // NPCScript.LoadInfo() 除了对话页，还会解析一部分“非对话 section”。当启用 SkipTxtNpcLoad 时这些内容将不会被加载。
        private static readonly HashSet<string> LegacyNonDialogSectionKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "[TRADE]",
            "[TYPES]",
            "[USEDTYPES]",
            "[QUESTS]",
            "[RECIPE]",
            "[SPEECH]",
        };

        public static NpcScriptCoverageReport CheckNpcFile(string npcFileName, string startPageKey, ScriptRegistry registry)
        {
            var diagnostics = new List<string>();

            npcFileName = NormalizeNpcFileName(npcFileName);

            if (string.IsNullOrWhiteSpace(npcFileName))
            {
                diagnostics.Add("npcFileName 不能为空。");
                return new NpcScriptCoverageReport(string.Empty, string.Empty, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), diagnostics);
            }

            var normalizedStartPageKey = NormalizePageKey(startPageKey);
            if (string.IsNullOrWhiteSpace(normalizedStartPageKey))
            {
                normalizedStartPageKey = "[@MAIN]";
            }

            var provider = Envir.Main?.TextFileProvider;
            if (provider == null)
            {
                var state = Settings.CSharpScriptsEnabled && Envir.Main?.CSharpScripts?.Enabled == true
                    ? $"v{Envir.Main.CSharpScripts.Version}, handlers={Envir.Main.CSharpScripts.LastRegisteredHandlerCount}"
                    : $"不可用: {Envir.Main?.CSharpScripts?.LastError}";

                diagnostics.Add($"TextFileProvider 未就绪（C#={state}）。");
                return new NpcScriptCoverageReport(npcFileName, normalizedStartPageKey, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), diagnostics);
            }

            var npcFileKey = $"NPCs/{npcFileName}";
            var definition = provider.GetByKey(npcFileKey);
            if (definition == null)
            {
                diagnostics.Add($"找不到 NPC 脚本定义：{npcFileKey}");
                return new NpcScriptCoverageReport(npcFileName, normalizedStartPageKey, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), diagnostics);
            }

            var lines = definition.Lines != null ? definition.Lines.ToList() : new List<string>();

            if (Settings.TxtScriptsEnableInsertInclude)
            {
                ExpandInsert(lines, diagnostics);
                ExpandInclude(lines, diagnostics);
            }
            else
            {
                diagnostics.Add("已禁用 #INSERT/#INCLUDE 展开（TxtScriptsEnableInsertInclude=false）。");
            }

            var legacyNonDialogSections = DetectLegacyNonDialogSections(lines);
            if (legacyNonDialogSections.Count > 0)
            {
                diagnostics.Add($"检测到该 NPC 脚本包含非对话 section：{string.Join(", ", legacyNonDialogSections)}。若启用 SkipTxtNpcLoad 将跳过这些内容加载。");
            }

            var reachable = GetReachablePageKeysFromLines(lines, normalizedStartPageKey);
            var reachableList = reachable.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();

            var missingCSharp = new List<string>();
            var policyDisallowed = new List<string>();

            for (var i = 0; i < reachableList.Count; i++)
            {
                var pageKey = reachableList[i];

                if (IsBuiltinClientPageKey(pageKey))
                {
                    continue;
                }

                var definitionKey = GetDefinitionKey(pageKey);

                var policyKey = ScriptHookKeys.OnNpcPage(npcFileName, definitionKey);
                if (!ScriptDispatchPolicy.ShouldTryCSharp(policyKey))
                {
                    policyDisallowed.Add(pageKey);
                }

                if (registry == null)
                {
                    missingCSharp.Add(pageKey);
                    continue;
                }

                if (!HasCSharpHandler(registry, npcFileName, pageKey))
                {
                    missingCSharp.Add(pageKey);
                }
            }

            missingCSharp = missingCSharp.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
            policyDisallowed = policyDisallowed.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();

            return new NpcScriptCoverageReport(
                npcFileName,
                normalizedStartPageKey,
                reachableList,
                missingCSharp,
                policyDisallowed,
                legacyNonDialogSections,
                diagnostics);
        }

        private static List<string> DetectLegacyNonDialogSections(List<string> lines)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (lines == null || lines.Count == 0)
                return new List<string>(0);

            for (var i = 0; i < lines.Count; i++)
            {
                var s = (lines[i] ?? string.Empty).Trim();
                if (s.Length == 0) continue;

                if (s.StartsWith(";", StringComparison.Ordinal)) continue;

                if (!s.StartsWith("[", StringComparison.Ordinal) || !s.EndsWith("]", StringComparison.Ordinal))
                    continue;

                // 对话页 Key：[@MAIN]/[@XXX]（忽略）
                if (s.StartsWith("[@", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (LegacyNonDialogSectionKeys.Contains(s))
                {
                    set.Add(s.ToUpperInvariant());
                }
            }

            return set.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static bool HasCSharpHandler(ScriptRegistry registry, string npcFileName, string pageKey)
        {
            if (registry.TryGet<OnNpcPageHook>(ScriptHookKeys.OnNpcPage(npcFileName, pageKey), out _))
                return true;

            var definitionKey = GetDefinitionKey(pageKey);
            if (!string.Equals(definitionKey, pageKey, StringComparison.OrdinalIgnoreCase))
            {
                if (registry.TryGet<OnNpcPageHook>(ScriptHookKeys.OnNpcPage(npcFileName, definitionKey), out _))
                    return true;
            }

            return false;
        }

        private static bool IsBuiltinClientPageKey(string pageKey)
        {
            var key = NormalizePageKey(pageKey);
            return string.Equals(key, BuiltinExitPage, StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizePageKey(string pageKeyOrLabel)
        {
            if (string.IsNullOrWhiteSpace(pageKeyOrLabel))
                return string.Empty;

            var key = pageKeyOrLabel.Trim();

            if (key.StartsWith("[@", StringComparison.OrdinalIgnoreCase) && key.EndsWith("]", StringComparison.Ordinal))
                return key;

            if (key.StartsWith("@", StringComparison.Ordinal))
                return "[" + key + "]";

            return "[@"+ key + "]";
        }

        private static string GetDefinitionKey(string pageKey)
        {
            var key = NormalizePageKey(pageKey);

            if (key.StartsWith("[@_", StringComparison.OrdinalIgnoreCase))
                return key;

            var match = ArgsRegex.Match(key);
            if (!match.Success)
                return key;

            return ArgsRegex.Replace(key, "()");
        }

        private static string NormalizeNpcFileName(string npcFileName)
        {
            if (npcFileName == null) return string.Empty;

            var s = npcFileName.Trim();
            if (s.Length == 0) return string.Empty;

            s = s.Replace('\\', '/');
            s = s.TrimStart('/');

            if (s.StartsWith("NPCs/", StringComparison.OrdinalIgnoreCase))
            {
                s = s.Substring("NPCs/".Length);
            }

            if (s.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            {
                s = s.Substring(0, s.Length - 4);
            }

            s = s.Trim('/');

            return s;
        }

        private static void ExpandInsert(List<string> lines, List<string> diagnostics)
        {
            if (lines == null) return;

            var provider = Envir.Main?.TextFileProvider;
            if (provider == null)
            {
                diagnostics.Add("INSERT: TextFileProvider 未就绪，无法展开。");
                return;
            }

            for (var i = 0; i < lines.Count; i++)
            {
                var line = lines[i] ?? string.Empty;
                var trimmed = line.TrimStart();

                if (!trimmed.StartsWith("#INSERT", StringComparison.OrdinalIgnoreCase))
                    continue;

                var split = trimmed.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (split.Length < 2) continue;

                var token = split[1];
                if (token.Length < 2 || !token.StartsWith("[", StringComparison.Ordinal) || !token.EndsWith("]", StringComparison.Ordinal))
                    continue;

                var relative = token.Substring(1, token.Length - 2);
                var definition = provider.GetByKey(relative);
                if (definition == null)
                {
                    diagnostics.Add($"INSERT: 未找到要调用的脚本定义 {relative}");
                    continue;
                }

                lines.AddRange(definition.Lines);
            }

            lines.RemoveAll(s => (s ?? string.Empty).TrimStart().StartsWith("#INSERT", StringComparison.OrdinalIgnoreCase));
        }

        private static void ExpandInclude(List<string> lines, List<string> diagnostics)
        {
            if (lines == null) return;

            var provider = Envir.Main?.TextFileProvider;
            if (provider == null)
            {
                diagnostics.Add("INCLUDE: TextFileProvider 未就绪，无法展开。");
                return;
            }

            for (var i = 0; i < lines.Count; i++)
            {
                var line = lines[i] ?? string.Empty;
                var trimmed = line.TrimStart();

                if (!trimmed.StartsWith("#INCLUDE", StringComparison.OrdinalIgnoreCase))
                    continue;

                var split = trimmed.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (split.Length < 3) continue;

                var token = split[1];
                var pageLabel = split[2];

                if (token.Length < 2 || !token.StartsWith("[", StringComparison.Ordinal) || !token.EndsWith("]", StringComparison.Ordinal))
                    continue;

                var relative = token.Substring(1, token.Length - 2);
                var page = ("[" + pageLabel + "]").ToUpperInvariant();

                var definition = provider.GetByKey(relative);
                if (definition == null)
                {
                    diagnostics.Add($"INCLUDE: 未找到要调用的脚本定义 {relative}");
                    continue;
                }

                var extLines = definition.Lines ?? Array.Empty<string>();

                var parsedLines = new List<string>();
                var start = false;
                var finish = false;

                for (var j = 0; j < extLines.Count; j++)
                {
                    var extLine = extLines[j] ?? string.Empty;
                    if (!extLine.ToUpperInvariant().StartsWith(page))
                        continue;

                    for (var x = j + 1; x < extLines.Count; x++)
                    {
                        var extLine2 = extLines[x] ?? string.Empty;
                        var t = extLine2.Trim();

                        if (t == "{")
                        {
                            start = true;
                            continue;
                        }

                        if (t == "}")
                        {
                            finish = true;
                            break;
                        }

                        parsedLines.Add(extLine2);
                    }
                }

                if (start && finish)
                {
                    lines.InsertRange(i + 1, parsedLines);
                    parsedLines.Clear();
                }
                else
                {
                    diagnostics.Add($"INCLUDE: 未找到匹配页或缺少大括号：{relative} {pageLabel}");
                }
            }

            lines.RemoveAll(s => (s ?? string.Empty).TrimStart().StartsWith("#INCLUDE", StringComparison.OrdinalIgnoreCase));
        }

        private static HashSet<string> GetReachablePageKeysFromLines(List<string> lines, string startPageKey)
        {
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var queue = new Queue<string>();

            var start = NormalizePageKey(startPageKey);
            if (string.IsNullOrWhiteSpace(start))
                start = "[@MAIN]";

            visited.Add(start);
            queue.Enqueue(start);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();

                var outgoing = ExtractOutgoingPageKeys(lines, current);

                for (var i = 0; i < outgoing.Count; i++)
                {
                    var next = outgoing[i];
                    if (string.IsNullOrWhiteSpace(next)) continue;

                    if (visited.Add(next))
                        queue.Enqueue(next);
                }
            }

            return visited;
        }

        private static List<string> ExtractOutgoingPageKeys(List<string> lines, string sectionName)
        {
            var results = new List<string>();

            if (lines == null || lines.Count == 0) return results;

            var sectionKey = NormalizePageKey(sectionName);
            if (string.IsNullOrWhiteSpace(sectionKey)) return results;

            var searchKey = GetDefinitionKey(sectionKey);

            for (var i = 0; i < lines.Count; i++)
            {
                var line = lines[i] ?? string.Empty;
                var trimmedLine = line.TrimStart();

                if (trimmedLine.StartsWith(";", StringComparison.Ordinal))
                    continue;

                if (!trimmedLine.StartsWith(searchKey, StringComparison.OrdinalIgnoreCase))
                    continue;

                for (var j = i + 1; j < lines.Count; j++)
                {
                    var bodyLine = lines[j] ?? string.Empty;
                    var t = bodyLine.Trim();

                    if (t.Length == 0) continue;
                    if (t.StartsWith(";", StringComparison.Ordinal)) continue;

                    if (t.StartsWith("[", StringComparison.Ordinal) && t.EndsWith("]", StringComparison.Ordinal))
                        break;

                    var m = ButtonRegex.Match(bodyLine);
                    while (m.Success)
                    {
                        var label = m.Groups[1].Captures[0].Value;
                        label = label.Split('/')[0];

                        var pageKey = NormalizePageKey(label);
                        if (pageKey.Length > 0)
                            results.Add(pageKey);

                        m = m.NextMatch();
                    }

                    var parts = t.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length > 1)
                    {
                        var cmd = parts[0].ToUpperInvariant();

                        switch (cmd)
                        {
                            case "GOTO":
                            case "GROUPGOTO":
                                results.Add(NormalizePageKey(parts[1]));
                                break;

                            case "TIMERECALL":
                            case "DELAYGOTO":
                            case "TIMERECALLGROUP":
                                if (parts.Length > 2)
                                    results.Add(NormalizePageKey(parts[2]));
                                break;

                            case "ROLLDIE":
                            case "ROLLYUT":
                                results.Add(NormalizePageKey(parts[1]));
                                break;
                        }
                    }
                }

                break;
            }

            return results.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static IReadOnlyList<string> ListNpcFileNames(out string error)
        {
            error = string.Empty;

            try
            {
                var provider = Envir.Main?.TextFileProvider;
                if (provider == null)
                {
                    error = "TextFileProvider 未就绪。";
                    return Array.Empty<string>();
                }

                var all = provider.GetAll();
                if (all == null || all.Count == 0) return Array.Empty<string>();

                var list = new List<string>(all.Count);

                foreach (var item in all)
                {
                    var key = item?.Key;
                    if (string.IsNullOrWhiteSpace(key)) continue;

                    if (!key.StartsWith("npcs/", StringComparison.Ordinal))
                        continue;

                    var relative = key.Substring("npcs/".Length);
                    if (string.IsNullOrWhiteSpace(relative)) continue;

                    list.Add(relative);
                }

                return list
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return Array.Empty<string>();
            }
        }
    }
}
