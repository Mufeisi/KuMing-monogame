namespace Server.Scripting
{
    public enum LingFengEnvirFileOwner
    {
        Unassigned,
        Script,
        DomainConfiguration,
        RuntimeData,
        ClientContract,
        BackupOrArchive,
        Documentation,
        ExecutableArtifact
    }

    public sealed record LingFengEnvirFileClassification(
        LingFengEnvirFileOwner Owner,
        string RuleId,
        bool MayPublishAsScript,
        string LogicKey = null);

    public static class LingFengEnvirFileClassifier
    {
        private static readonly HashSet<string> RuntimeDirectories = new(StringComparer.OrdinalIgnoreCase)
        {
            "UserData", "Market_Saved", "Market_Storage", "Market_SellOff"
        };

        private static readonly HashSet<string> ClientContractDirectories = new(StringComparer.OrdinalIgnoreCase)
        {
            "MonIcons", "NpcIcons"
        };

        private static readonly HashSet<string> RuntimeExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".prc", ".sav", ".dat", ".sell", ".gold", ".db", ".915031"
        };

        private static readonly HashSet<string> BackupExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".bak", ".zip", ".rar", ".7z", ".wn", ".bf"
        };

        private static readonly HashSet<string> DocumentationExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".htm", ".html", ".xlsx", ".xls", ".doc", ".docx", ".pdf"
        };

        private static readonly HashSet<string> ExecutableExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".bat", ".cmd", ".ps1", ".exe", ".dll", ".com", ".msi"
        };

        private static readonly HashSet<string> DomainConfigurationExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".txt", ".ini", ".csv", ".upg"
        };

        public static LingFengEnvirFileClassification Classify(string relativePath)
        {
            if (!TryNormalizeRelativePath(relativePath, out string normalized))
                return Result(LingFengEnvirFileOwner.Unassigned, "LFENV09-INVALID-PATH");

            string[] segments = normalized.Split('/');
            string topDirectory = segments.Length > 1 ? segments[0] : string.Empty;
            string fileName = segments[^1];
            string extension = Path.GetExtension(fileName);

            string fileStem = Path.GetFileNameWithoutExtension(fileName);
            if (fileName.StartsWith("~$", StringComparison.OrdinalIgnoreCase) ||
                fileStem.Equals("新建文本文档", StringComparison.OrdinalIgnoreCase) ||
                segments.Take(segments.Length - 1).Any(segment =>
                    segment.Contains("备份", StringComparison.OrdinalIgnoreCase) ||
                    segment.Contains("backup", StringComparison.OrdinalIgnoreCase)) ||
                fileStem.Contains(" - 副本", StringComparison.OrdinalIgnoreCase) ||
                fileStem.EndsWith(" - 备份", StringComparison.OrdinalIgnoreCase) ||
                fileStem.EndsWith("-备份", StringComparison.OrdinalIgnoreCase) ||
                BackupExtensions.Contains(extension))
                return Result(LingFengEnvirFileOwner.BackupOrArchive, "LFENV09-BACKUP");

            if (RuntimeDirectories.Contains(topDirectory) ||
                segments.Any(RuntimeDirectories.Contains) ||
                ContainsPath(segments, "GuildBase", "Guilds") ||
                RuntimeExtensions.Contains(extension))
                return Result(LingFengEnvirFileOwner.RuntimeData, "LFENV09-RUNTIME");

            if (fileName.Equals(".txt", StringComparison.OrdinalIgnoreCase))
                return Result(LingFengEnvirFileOwner.RuntimeData, "LFENV09-RUNTIME-RECORD");

            if (ExecutableExtensions.Contains(extension))
                return Result(LingFengEnvirFileOwner.ExecutableArtifact, "LFENV09-EXECUTABLE");

            if (DocumentationExtensions.Contains(extension))
                return Result(LingFengEnvirFileOwner.Documentation, "LFENV09-DOCUMENTATION");

            if (ClientContractDirectories.Contains(topDirectory))
                return Result(LingFengEnvirFileOwner.ClientContract, "LFENV09-CLIENT-CONTRACT");

            bool scriptExtension = extension.Equals(".txt", StringComparison.OrdinalIgnoreCase) ||
                                   extension.Equals(".ini", StringComparison.OrdinalIgnoreCase) &&
                                   (topDirectory.Equals("QuestDiary", StringComparison.OrdinalIgnoreCase) ||
                                    topDirectory.Equals("DeFines", StringComparison.OrdinalIgnoreCase));
            if (scriptExtension &&
                TryMapScriptLogicKey(normalized, out string logicKey))
                return new LingFengEnvirFileClassification(
                    LingFengEnvirFileOwner.Script, "LFENV09-SCRIPT", true, logicKey);

            if (extension.Equals(".txt", StringComparison.OrdinalIgnoreCase) &&
                IsScriptNamespace(topDirectory))
                return Result(LingFengEnvirFileOwner.Unassigned, "LFENV09-INVALID-SCRIPT-KEY");

            if (DomainConfigurationExtensions.Contains(extension))
                return Result(LingFengEnvirFileOwner.DomainConfiguration, "LFENV09-DOMAIN-CONFIG");

            return Result(LingFengEnvirFileOwner.Unassigned, "LFENV09-UNASSIGNED");
        }

        private static bool TryMapScriptLogicKey(string normalized, out string logicKey)
        {
            logicKey = null;
            if (normalized.Equals("QFunction-0.txt", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("Market_Def/QFunction-0.txt", StringComparison.OrdinalIgnoreCase))
            {
                logicKey = LogicKey.NormalizeOrThrow("SystemScripts/QFunction-0");
                return true;
            }
            if (normalized.Equals("Robot_def/AUTORUNROBOT.txt", StringComparison.OrdinalIgnoreCase))
            {
                logicKey = LogicKey.NormalizeOrThrow("SystemScripts/AutoRunRobot");
                return true;
            }
            int separator = normalized.IndexOf('/');
            if (separator <= 0) return false;
            string directory = normalized[..separator];
            string nestedPath = normalized[(separator + 1)..];
            string mappedPath;
            if (directory.Equals("Market_Def", StringComparison.OrdinalIgnoreCase))
                mappedPath = $"NPCs/{nestedPath}";
            else if (directory.Equals("Npc_def", StringComparison.OrdinalIgnoreCase))
                mappedPath = $"NpcDefs/{nestedPath}";
            else if (directory.Equals("QuestDiary", StringComparison.OrdinalIgnoreCase))
                mappedPath = $"QuestDiary/{Path.ChangeExtension(nestedPath, null)}";
            else if (directory.Equals("DeFines", StringComparison.OrdinalIgnoreCase))
                mappedPath = $"Defines/{Path.ChangeExtension(nestedPath, null)}";
            else if (directory.Equals("MapQuest_def", StringComparison.OrdinalIgnoreCase) &&
                     nestedPath.Equals("QManage.txt", StringComparison.OrdinalIgnoreCase))
                mappedPath = "SystemScripts/QManage";
            else if (directory.Equals("Robot_def", StringComparison.OrdinalIgnoreCase) &&
                     nestedPath.Equals("ROBOTMANAGE.txt", StringComparison.OrdinalIgnoreCase))
                mappedPath = "SystemScripts/RobotManage";
            else
                return false;

            if (!LogicKey.TryNormalize(mappedPath, out string normalizedKey)) return false;
            logicKey = normalizedKey;
            return true;
        }

        private static bool IsScriptNamespace(string topDirectory) =>
            topDirectory.Equals("Market_Def", StringComparison.OrdinalIgnoreCase) ||
            topDirectory.Equals("Npc_def", StringComparison.OrdinalIgnoreCase) ||
            topDirectory.Equals("QuestDiary", StringComparison.OrdinalIgnoreCase) ||
            topDirectory.Equals("DeFines", StringComparison.OrdinalIgnoreCase);

        private static bool ContainsPath(IReadOnlyList<string> segments, string parent, string child)
        {
            for (int index = 0; index + 1 < segments.Count; index++)
                if (segments[index].Equals(parent, StringComparison.OrdinalIgnoreCase) &&
                    segments[index + 1].Equals(child, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        private static bool TryNormalizeRelativePath(string relativePath, out string normalized)
        {
            normalized = (relativePath ?? string.Empty).Replace('\\', '/').Trim();
            if (normalized.Length == 0 || normalized.StartsWith('/') || Path.IsPathRooted(normalized))
                return false;
            string[] segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0 || segments.Any(segment => segment is "." or "..")) return false;
            normalized = string.Join('/', segments);
            return true;
        }

        private static LingFengEnvirFileClassification Result(
            LingFengEnvirFileOwner owner,
            string ruleId) => new(owner, ruleId, false);
    }
}
