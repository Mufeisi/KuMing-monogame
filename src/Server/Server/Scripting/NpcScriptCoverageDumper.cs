using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Server.MirEnvir;

namespace Server.Scripting
{
    internal static class NpcScriptCoverageDumper
    {
        internal sealed class NpcCoveragePageEntry
        {
            public string PageKey { get; set; } = string.Empty;
            public string CoverageStatus { get; set; } = string.Empty;
            public bool HasCSharpHandler { get; set; }
            public bool PolicyAllowsCSharp { get; set; }
        }

        internal sealed class NpcCoverageItem
        {
            public string NpcFileName { get; set; } = string.Empty;
            public string StartPageKey { get; set; } = string.Empty;

            public int LegacyReachablePageCount { get; set; }
            public int CSharpRegisteredPageCount { get; set; }

            public int MissingCSharpPageCount { get; set; }
            public int PolicyDisallowedPageCount { get; set; }

            public List<NpcCoveragePageEntry> Pages { get; set; } = new();
            public List<string> LegacyNonDialogSections { get; set; } = new();
            public List<string> Diagnostics { get; set; } = new();
        }

        internal sealed class NpcCoverageSnapshot
        {
            public DateTime GeneratedAtUtc { get; set; }
            public string StartPageKey { get; set; } = string.Empty;
            public string CSharpRuntimeState { get; set; } = string.Empty;
            public List<NpcCoverageItem> Items { get; set; } = new();
        }

        internal sealed class AllNpcCoverageSummary
        {
            public int NpcFileCount { get; set; }
            public int LegacyReachablePageCount { get; set; }
            public int MissingCSharpPageCount { get; set; }
            public int PolicyDisallowedPageCount { get; set; }
            public int NpcWithLegacyNonDialogSectionsCount { get; set; }
            public int NpcWithAnyMissingPagesCount { get; set; }
            public int NpcWithAnyPolicyDisallowedPagesCount { get; set; }
        }

        internal sealed class AllNpcCoverageItem
        {
            public string NpcFileName { get; set; } = string.Empty;
            public string StartPageKey { get; set; } = string.Empty;
            public int LegacyReachablePageCount { get; set; }
            public int MissingCSharpPageCount { get; set; }
            public int PolicyDisallowedPageCount { get; set; }
            public List<string> LegacyNonDialogSections { get; set; } = new();
        }

        internal sealed class AllNpcCoveragePageIssue
        {
            public string NpcFileName { get; set; } = string.Empty;
            public string PageKey { get; set; } = string.Empty;
            public string Issue { get; set; } = string.Empty;
        }

        internal sealed class AllNpcCoverageSnapshot
        {
            public DateTime GeneratedAtUtc { get; set; }
            public string StartPageKey { get; set; } = string.Empty;
            public string CSharpRuntimeState { get; set; } = string.Empty;
            public AllNpcCoverageSummary Summary { get; set; } = new();
            public List<AllNpcCoverageItem> Npcs { get; set; } = new();
            public List<AllNpcCoveragePageIssue> PageIssues { get; set; } = new();
        }

        private static string GetCSharpRuntimeState()
        {
            var scriptsRuntimeActive = Settings.CSharpScriptsEnabled && Envir.Main?.CSharpScripts?.Enabled == true;
            return scriptsRuntimeActive
                ? $"v{Envir.Main.CSharpScripts.Version}, handlers={Envir.Main.CSharpScripts.LastRegisteredHandlerCount}"
                : $"不可用: {Envir.Main?.CSharpScripts?.LastError}";
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

        private static bool IsBuiltinClientPageKey(string pageKey)
        {
            var key = NormalizePageKey(pageKey);
            return string.Equals(key, "[@EXIT]", StringComparison.OrdinalIgnoreCase);
        }

        private static string SanitizeFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "empty";

            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(value.Length);

            for (var i = 0; i < value.Length; i++)
            {
                var ch = value[i];

                if (ch == '/' || ch == '\\')
                {
                    sb.Append('_');
                    continue;
                }

                if (Array.IndexOf(invalid, ch) >= 0)
                {
                    sb.Append('_');
                    continue;
                }

                sb.Append(ch);
            }

            var s = sb.ToString().Trim();
            if (s.Length == 0) s = "empty";

            // 避免文件名过长（Windows 上路径长度更敏感）
            if (s.Length > 80) s = s.Substring(0, 80);

            return s;
        }

        private static string GetShortSha1(string text)
        {
            var bytes = Encoding.UTF8.GetBytes(text ?? string.Empty);
            var hashBytes = SHA1.HashData(bytes);
            return Convert.ToHexString(hashBytes).ToLowerInvariant().Substring(0, 8);
        }

        private static void WriteUtf8Atomic(string path, string content)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var temp = path + ".tmp";
            File.WriteAllText(temp, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temp, path, overwrite: true);
        }

        private static NpcCoverageItem CreateItem(NpcScriptCoverageReport report)
        {
            report ??= new NpcScriptCoverageReport(string.Empty, string.Empty, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());

            var reachable = report.ReachablePageKeys ?? Array.Empty<string>();
            var missingSet = new HashSet<string>(report.MissingCSharpPageKeys ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            var policySet = new HashSet<string>(report.PolicyDisallowedPageKeys ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);

            var pages = new List<NpcCoveragePageEntry>(reachable.Count);

            var legacyReachableCount = 0;
            var csharpRegisteredCount = 0;

            for (var i = 0; i < reachable.Count; i++)
            {
                var pageKey = reachable[i] ?? string.Empty;
                var isBuiltin = IsBuiltinClientPageKey(pageKey);

                var isMissing = !isBuiltin && missingSet.Contains(pageKey);
                var isPolicyDisallowed = !isBuiltin && policySet.Contains(pageKey);

                var policyAllows = !isPolicyDisallowed;
                var hasCSharp = !isMissing;

                var status =
                    isBuiltin ? "builtin_client" :
                    (isPolicyDisallowed && isMissing) ? "policy_disallowed_and_missing_handler" :
                    isPolicyDisallowed ? "policy_disallowed" :
                    isMissing ? "missing_handler" :
                    "covered";

                if (!isBuiltin)
                {
                    legacyReachableCount++;
                    if (hasCSharp) csharpRegisteredCount++;
                }

                pages.Add(new NpcCoveragePageEntry
                {
                    PageKey = NormalizePageKey(pageKey),
                    CoverageStatus = status,
                    HasCSharpHandler = hasCSharp,
                    PolicyAllowsCSharp = policyAllows
                });
            }

            return new NpcCoverageItem
            {
                NpcFileName = report.NpcFileName ?? string.Empty,
                StartPageKey = report.StartPageKey ?? string.Empty,
                LegacyReachablePageCount = legacyReachableCount,
                CSharpRegisteredPageCount = csharpRegisteredCount,
                MissingCSharpPageCount = report.MissingCSharpPageKeys?.Count ?? 0,
                PolicyDisallowedPageCount = report.PolicyDisallowedPageKeys?.Count ?? 0,
                Pages = pages,
                LegacyNonDialogSections = report.LegacyNonDialogSections?.ToList() ?? new List<string>(0),
                Diagnostics = report.Diagnostics?.ToList() ?? new List<string>(0),
            };
        }

        private static string RenderMarkdown(NpcCoverageItem item)
        {
            item ??= new NpcCoverageItem();

            var sb = new StringBuilder();
            sb.AppendLine("# NPC 脚本迁移覆盖率报表（latest）");
            sb.AppendLine();
            sb.AppendLine($"- NpcFileName: `{item.NpcFileName}`");
            sb.AppendLine($"- StartPageKey: `{item.StartPageKey}`");
            sb.AppendLine($"- legacy 可达页数: `{item.LegacyReachablePageCount}`");
            sb.AppendLine($"- 已注册 C# handler 页数: `{item.CSharpRegisteredPageCount}`");
            sb.AppendLine($"- 缺少 C# handler 页数: `{item.MissingCSharpPageCount}`");
            sb.AppendLine($"- 被 ScriptDispatchPolicy 禁用页数: `{item.PolicyDisallowedPageCount}`");

            if (item.LegacyNonDialogSections.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("## legacy 非对话 section");
                for (var i = 0; i < item.LegacyNonDialogSections.Count; i++)
                {
                    sb.AppendLine($"- `{item.LegacyNonDialogSections[i]}`");
                }
            }

            if (item.Diagnostics.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("## Diagnostics");
                for (var i = 0; i < item.Diagnostics.Count; i++)
                {
                    sb.AppendLine($"- {item.Diagnostics[i]}");
                }
            }

            var missing = item.Pages.Where(p => string.Equals(p.CoverageStatus, "missing_handler", StringComparison.OrdinalIgnoreCase) ||
                                                string.Equals(p.CoverageStatus, "policy_disallowed_and_missing_handler", StringComparison.OrdinalIgnoreCase))
                                    .Select(p => p.PageKey)
                                    .Distinct(StringComparer.OrdinalIgnoreCase)
                                    .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                                    .ToList();

            if (missing.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("## 缺少 C# handler 的页面");
                for (var i = 0; i < missing.Count; i++)
                {
                    sb.AppendLine($"- `{missing[i]}`");
                }
            }

            var policy = item.Pages.Where(p => string.Equals(p.CoverageStatus, "policy_disallowed", StringComparison.OrdinalIgnoreCase) ||
                                               string.Equals(p.CoverageStatus, "policy_disallowed_and_missing_handler", StringComparison.OrdinalIgnoreCase))
                                   .Select(p => p.PageKey)
                                   .Distinct(StringComparer.OrdinalIgnoreCase)
                                   .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                                   .ToList();

            if (policy.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("## 被 ScriptDispatchPolicy 禁用的页面");
                for (var i = 0; i < policy.Count; i++)
                {
                    sb.AppendLine($"- `{policy[i]}`");
                }
            }

            return sb.ToString();
        }

        public static bool DumpNpcLatest(NpcScriptCoverageReport report, out string jsonFilePath, out string mdFilePath, out string error)
        {
            jsonFilePath = string.Empty;
            mdFilePath = string.Empty;
            error = string.Empty;

            try
            {
                var dir = Path.Combine(".", "Logs", "NpcCoverage");
                Directory.CreateDirectory(dir);

                var npcFileName = report?.NpcFileName ?? string.Empty;
                var safe = SanitizeFileName(npcFileName);
                var hash = GetShortSha1(npcFileName);

                jsonFilePath = Path.Combine(dir, $"npc-coverage-{safe}-{hash}-latest.json");
                mdFilePath = Path.Combine(dir, $"npc-coverage-{safe}-{hash}-latest.md");

                var item = CreateItem(report);

                var jsonOptions = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };

                var json = JsonSerializer.Serialize(item, jsonOptions);
                WriteUtf8Atomic(jsonFilePath, json);

                var md = RenderMarkdown(item);
                WriteUtf8Atomic(mdFilePath, md);

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public static bool DumpAllNpcLatest(string startPageKey, ScriptRegistry registry, out string jsonFilePath, out string mdFilePath, out string error)
        {
            jsonFilePath = string.Empty;
            mdFilePath = string.Empty;
            error = string.Empty;

            try
            {
                var npcFiles = NpcScriptCoverage.ListNpcFileNames(out var listError);
                if (!string.IsNullOrWhiteSpace(listError))
                {
                    error = listError;
                    return false;
                }

                var snapshot = new AllNpcCoverageSnapshot
                {
                    GeneratedAtUtc = DateTime.UtcNow,
                    StartPageKey = startPageKey ?? string.Empty,
                    CSharpRuntimeState = GetCSharpRuntimeState(),
                    Summary = new AllNpcCoverageSummary(),
                    Npcs = new List<AllNpcCoverageItem>(npcFiles.Count),
                    PageIssues = new List<AllNpcCoveragePageIssue>()
                };

                var legacyReachableTotal = 0;
                var missingTotal = 0;
                var policyTotal = 0;
                var npcWithLegacySections = 0;
                var npcWithMissing = 0;
                var npcWithPolicy = 0;

                for (var i = 0; i < npcFiles.Count; i++)
                {
                    var npcFile = npcFiles[i];
                    var report = NpcScriptCoverage.CheckNpcFile(npcFile, startPageKey, registry);
                    var item = CreateItem(report);

                    legacyReachableTotal += item.LegacyReachablePageCount;
                    missingTotal += item.MissingCSharpPageCount;
                    policyTotal += item.PolicyDisallowedPageCount;

                    if (item.LegacyNonDialogSections.Count > 0) npcWithLegacySections++;
                    if (item.MissingCSharpPageCount > 0) npcWithMissing++;
                    if (item.PolicyDisallowedPageCount > 0) npcWithPolicy++;

                    snapshot.Npcs.Add(new AllNpcCoverageItem
                    {
                        NpcFileName = item.NpcFileName,
                        StartPageKey = item.StartPageKey,
                        LegacyReachablePageCount = item.LegacyReachablePageCount,
                        MissingCSharpPageCount = item.MissingCSharpPageCount,
                        PolicyDisallowedPageCount = item.PolicyDisallowedPageCount,
                        LegacyNonDialogSections = item.LegacyNonDialogSections
                    });

                    foreach (var page in report.MissingCSharpPageKeys ?? Array.Empty<string>())
                    {
                        snapshot.PageIssues.Add(new AllNpcCoveragePageIssue
                        {
                            NpcFileName = item.NpcFileName,
                            PageKey = NormalizePageKey(page),
                            Issue = "missing_handler"
                        });
                    }

                    foreach (var page in report.PolicyDisallowedPageKeys ?? Array.Empty<string>())
                    {
                        snapshot.PageIssues.Add(new AllNpcCoveragePageIssue
                        {
                            NpcFileName = item.NpcFileName,
                            PageKey = NormalizePageKey(page),
                            Issue = "policy_disallowed"
                        });
                    }
                }

                snapshot.Summary = new AllNpcCoverageSummary
                {
                    NpcFileCount = npcFiles.Count,
                    LegacyReachablePageCount = legacyReachableTotal,
                    MissingCSharpPageCount = missingTotal,
                    PolicyDisallowedPageCount = policyTotal,
                    NpcWithLegacyNonDialogSectionsCount = npcWithLegacySections,
                    NpcWithAnyMissingPagesCount = npcWithMissing,
                    NpcWithAnyPolicyDisallowedPagesCount = npcWithPolicy,
                };

                snapshot.Npcs.Sort((a, b) => string.Compare(a.NpcFileName, b.NpcFileName, StringComparison.OrdinalIgnoreCase));
                snapshot.PageIssues.Sort((a, b) =>
                {
                    var c = string.Compare(a.NpcFileName, b.NpcFileName, StringComparison.OrdinalIgnoreCase);
                    if (c != 0) return c;
                    c = string.Compare(a.Issue, b.Issue, StringComparison.OrdinalIgnoreCase);
                    if (c != 0) return c;
                    return string.Compare(a.PageKey, b.PageKey, StringComparison.OrdinalIgnoreCase);
                });

                var dir = Path.Combine(".", "Logs", "NpcCoverage");
                Directory.CreateDirectory(dir);

                jsonFilePath = Path.Combine(dir, "npc-coverage-all-latest.json");
                mdFilePath = Path.Combine(dir, "npc-coverage-all-latest.md");

                var jsonOptions = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };

                var json = JsonSerializer.Serialize(snapshot, jsonOptions);
                WriteUtf8Atomic(jsonFilePath, json);

                var md = RenderMarkdownAll(snapshot);
                WriteUtf8Atomic(mdFilePath, md);

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static string RenderMarkdownAll(AllNpcCoverageSnapshot snapshot)
        {
            snapshot ??= new AllNpcCoverageSnapshot();

            var sb = new StringBuilder();
            sb.AppendLine("# NPC 脚本迁移覆盖率总表（latest）");
            sb.AppendLine();
            sb.AppendLine($"- GeneratedAtUtc: `{snapshot.GeneratedAtUtc:O}`");
            sb.AppendLine($"- StartPageKey: `{snapshot.StartPageKey}`");
            sb.AppendLine($"- CSharpRuntimeState: `{snapshot.CSharpRuntimeState}`");
            sb.AppendLine();

            var s = snapshot.Summary ?? new AllNpcCoverageSummary();
            sb.AppendLine("## Summary");
            sb.AppendLine($"- files: `{s.NpcFileCount}`");
            sb.AppendLine($"- legacy 可达页数（合计）: `{s.LegacyReachablePageCount}`");
            sb.AppendLine($"- 缺少 C# handler 页数（合计）: `{s.MissingCSharpPageCount}`");
            sb.AppendLine($"- 被 ScriptDispatchPolicy 禁用页数（合计）: `{s.PolicyDisallowedPageCount}`");
            sb.AppendLine($"- 含 legacy 非对话 section 的 NPC 数: `{s.NpcWithLegacyNonDialogSectionsCount}`");
            sb.AppendLine($"- 存在缺页的 NPC 数: `{s.NpcWithAnyMissingPagesCount}`");
            sb.AppendLine($"- 存在 policy 禁用页的 NPC 数: `{s.NpcWithAnyPolicyDisallowedPagesCount}`");

            if (snapshot.PageIssues != null && snapshot.PageIssues.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("## PageIssues");
                sb.AppendLine();
                sb.AppendLine("| NPC | PageKey | Issue |");
                sb.AppendLine("|---|---|---|");

                for (var i = 0; i < snapshot.PageIssues.Count; i++)
                {
                    var e = snapshot.PageIssues[i];
                    if (e == null) continue;
                    sb.AppendLine($"| `{e.NpcFileName}` | `{e.PageKey}` | `{e.Issue}` |");
                }
            }

            return sb.ToString();
        }
    }
}

