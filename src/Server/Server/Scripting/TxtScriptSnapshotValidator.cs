using System.Text.RegularExpressions;

namespace Server.Scripting
{
    public static class TxtScriptSnapshotValidator
    {
        private static readonly Regex PageRegex = new(
            @"^\s*(\[@[^\]]+\])\s*$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex SectionRegex = new(
            @"^\s*\[[^\]]+\]\s*$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private const int MaximumReferenceDepth = 16;
        private static readonly Regex DirectiveRegex = new(
            @"^\s*#([A-Za-z]+)\b",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly HashSet<string> SupportedDirectives = new(StringComparer.OrdinalIgnoreCase)
        {
            "IF", "ACT", "SAY", "ELSEACT", "ELSESAY", "INCLUDE", "INSERT"
        };
        private static readonly HashSet<string> SupportedCheckCommands = new(StringComparer.OrdinalIgnoreCase)
        {
            "AFFORDGATE", "AFFORDGUARD", "AFFORDSIEGE", "AFFORDWALL", "CHANCE", "CHECK",
            "CHECKBUFF", "CHECKCALC", "CHECKCLASS", "CHECKCONQUEST", "CHECKCREDIT",
            "CHECKDICTALLDIGIT", "CHECKEXACTMON", "CHECKEXP", "CHECKGENDER", "CHECKGOLD",
            "CHECKGUILDGOLD", "CHECKGUILDNAMELIST", "CHECKHEROCLASS", "CHECKHEROGENDER",
            "CHECKHEROITEM", "CHECKHP", "CHECKHUM", "CHECKINDICT", "CHECKITEM", "CHECKJOB",
            "CHECKLEVEL", "CHECKLISTALLDIGIT", "CHECKMAP", "CHECKMAPNAME", "ISONMAP", "CHECKMON", "CHECKMP", "CHECKNAMELIST",
            "CHECKPERMISSION", "CHECKPET", "CHECKPKPOINT", "CHECKPKPOINTEX", "CHECKQUEST",
            "CHECKRANGE", "CHECKRELATIONSHIP", "CHECKTIMER", "CHECKTRANSFORM", "CHECKVARINLIST",
            "CHECKWEDDINGRING", "CONQUESTAVAILABLE", "CONQUESTOWNER", "DAYOFWEEK",
            "GROUPCHECKNEARBY", "GROUPCOUNT", "GROUPLEADER", "HASBAGSPACE", "HEROLEVEL", "HOUR",
            "INGUILD", "ISADMIN", "ISGUILDLEADER", "ISNEWHUMAN", "ISQUESTACTIVE", "ISQUESTCOMPLETED", "LEVEL", "MIN", "PETCOUNT",
            "PETLEVEL", "RANDOM", "EQUAL", "LARGE", "SMALL", "NOT", "!"
        };
        private static readonly HashSet<string> SupportedActionCommands = new(StringComparer.OrdinalIgnoreCase)
        {
            "ADDGUILDNAMELIST", "ADDMAILGOLD", "ADDMAILITEM", "ADDNAMELIST", "ADDTOGUILD",
            "ADDTOLIST", "BREAK", "BREAKTIMERECALL", "CALC", "CALL", "CANGAINEXP", "CHANGECLASS",
            "CHANGEGENDER", "CHANGEHAIR", "CHANGELEVEL", "CHANGEPKPOINT", "CLEARGUILDNAMELIST",
            "CLEARNAMELIST", "CLEARPETS", "CLOSEGATE", "COMPOSEMAIL", "CONQUESTGATE",
            "CONQUESTGUARD", "CONQUESTREPAIRALL", "CONQUESTWALL", "DEC", "DELAYGOTO", "DELETEHERO",
            "DELGUILDNAMELIST", "DELNAMELIST", "DIV", "DROP", "ENTERMAP", "EXPIRETIMER",
            "EXTRACTLIST", "FORCEDIVORCE", "FORMULATION", "GETDICTITEMS", "GETDICTKEYCOUNT",
            "GETDICTMAXVALUE", "GETDICTMINVALUE", "GETHUMVAR", "GETLISTMAXVAR", "GETLISTMINVAR",
            "GETLISTVARCOUNT", "GETLISTVARINDEX", "GETRANDOMTEXT", "GIVE", "GIVEBUFF", "GIVECREDIT",
            "GIVEEXP", "GIVEGOLD", "GIVEGUILDEXP", "GIVEGUILDGOLD", "GIVEHP", "GIVEITEM", "GIVEMP",
            "GIVEPEARLS", "GIVEPET", "GIVESKILL", "GLOBALMESSAGE", "GOLDCOUNT", "GOTO", "GOTOLABEL",
            "GROUPGOTO", "GROUPRECALL", "GROUPTELEPORT", "INC", "INCREASEPKPOINT", "INITVAR",
            "INSERTTOLIST", "INSTANCEMOVE", "LOADVALUE", "LOCALMESSAGE", "MAKEWEDDINGRING", "MONCLEAR",
            "MONGEN", "MONCLEAR", "MOV", "MOVE", "TELEPORT", "MUL", "OPENBROWSER", "OPENGATE", "PARAM1", "PARAM2", "PARAM3",
            "PLAYSOUND", "REDUCEPKPOINT", "REFRESHEFFECTS", "REMOVEBUFF", "REMOVEFROMGUILD", "TRYREMOVEFROMGUILD",
            "REMOVELISTBYCONTENT", "REMOVELISTBYINDEX", "REMOVEPET", "REMOVESKILL",
            "REPLACELISTBYINDEX", "REVERSELIST", "REVIVEHERO", "ROLLDIE", "ROLLYUT", "SAVEVALUE", "CHANGEDAMAGEVALUE",
            "SCHEDULECONQUEST", "SEALHERO", "SENDMAIL", "SET", "SETCONQUESTRATE", "SETCURRTARGET",
            "SETHUMVAR", "SETPKPOINT", "SETTIMER", "SORTLIST", "STARTCONQUEST", "TAKE",
            "TAKECONQUESTGOLD", "TAKECREDIT", "TAKEGOLD", "TAKEGUILDGOLD", "TAKEITEM", "TAKEPEARLS",
            "TIMERECALL", "TIMERECALLGROUP", "UNEQUIPITEM", "VAR"
        };
        private static readonly HashSet<string> KnownUnsupportedSystemTriggerLabels = new(StringComparer.OrdinalIgnoreCase)
        {
            "[@KILLSLAVE]", "[@GROUPKILLMON]",
            "[@PICKUPITEM]", "[@DROPITEM]",
            "[@HUMDROPITEM]", "[@ITEMEXPIRED]"
        };

        public static IReadOnlyList<string> Validate(ITextFileProvider provider)
        {
            if (provider == null) return new[] { "TXT-SNAPSHOT-001：候选 Provider 不能为空。" };

            var errors = new List<string>();
            var pagesByKey = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            var includeGraph = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            LingFengRobotScheduleSnapshot robotSchedules = null;
            foreach (TextFileDefinition definition in provider.GetAll())
            {
                if (definition.Key.Equals("systemscripts/autorunrobot", StringComparison.OrdinalIgnoreCase))
                {
                    if (!LingFengRobotScheduleProvider.TryCreate(
                            definition, out robotSchedules, out IReadOnlyList<string> scheduleErrors))
                        errors.AddRange(scheduleErrors);
                    pagesByKey[definition.Key] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    continue;
                }
                var pages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int index = 0; index < definition.Lines.Count; index++)
                {
                    if (!TxtScriptTokenizer.TryTokenize(
                            definition.Lines[index], out _, out string tokenError))
                        errors.Add($"TXT-SNAPSHOT-007：{tokenError}（{definition.GetSourceLocation(index)}）。");

                    Match directive = DirectiveRegex.Match(definition.Lines[index]);
                    if (directive.Success && !SupportedDirectives.Contains(directive.Groups[1].Value))
                        errors.Add($"TXT-SNAPSHOT-006：未知段落指令 #{directive.Groups[1].Value.ToUpperInvariant()}（{definition.GetSourceLocation(index)}）。");

                    Match page = PageRegex.Match(definition.Lines[index]);
                    if (!page.Success) continue;
                    string label = page.Groups[1].Value.ToUpperInvariant();
                    if (!pages.Add(label))
                        errors.Add($"TXT-SNAPSHOT-002：重复标签 {label}（{definition.GetSourceLocation(index)}）。");
                    if (Settings.TxtScriptsStrictCompatibility &&
                        Settings.TxtScriptsCompatibilityVersion.StartsWith("LFM2-", StringComparison.OrdinalIgnoreCase) &&
                        definition.Key.Equals("SystemScripts/QFunction-0", StringComparison.OrdinalIgnoreCase) &&
                        KnownUnsupportedSystemTriggerLabels.Contains(label))
                        errors.Add($"TXT-SNAPSHOT-016：触发 {label} 的翎风上下文尚未完整适配，禁止静默发布（{definition.GetSourceLocation(index)}）。");
                }
                pagesByKey[definition.Key] = pages;
            }

            if (robotSchedules != null)
            {
                if (!pagesByKey.TryGetValue(
                        LogicKey.NormalizeOrThrow("SystemScripts/RobotManage"), out HashSet<string> robotPages))
                {
                    errors.Add("LFENV10-ROBOT-008：存在 AutoRunRobot 调度定义，但缺少 SystemScripts/RobotManage 页面脚本。");
                }
                else
                {
                    foreach (LingFengRobotScheduleEntry schedule in robotSchedules.Entries)
                    {
                        if (!robotPages.Contains(schedule.Page))
                            errors.Add($"LFENV10-ROBOT-009：Robot 调度标签不存在 {schedule.Page}（AutoRunRobot:{schedule.SourceLine}）。");
                    }
                }
            }

            foreach (TextFileDefinition definition in provider.GetAll())
            {
                if (definition.Key.Equals("systemscripts/autorunrobot", StringComparison.OrdinalIgnoreCase))
                    continue;
                string activeSection = string.Empty;
                for (int index = 0; index < definition.Lines.Count; index++)
                {
                    if (SectionRegex.IsMatch(definition.Lines[index]))
                    {
                        activeSection = string.Empty;
                        continue;
                    }
                    if (!TxtScriptTokenizer.TryTokenize(
                            definition.Lines[index].TrimStart(), out string[] tokens, out _) || tokens.Length == 0)
                        continue;

                    string command = tokens[0].TrimStart('#');
                    if (tokens[0].StartsWith('#'))
                    {
                        if (command.Equals("IF", StringComparison.OrdinalIgnoreCase)) activeSection = "IF";
                        else if (command.Equals("ACT", StringComparison.OrdinalIgnoreCase) ||
                                 command.Equals("ELSEACT", StringComparison.OrdinalIgnoreCase)) activeSection = "ACT";
                        else if (command.Equals("SAY", StringComparison.OrdinalIgnoreCase) ||
                                 command.Equals("ELSESAY", StringComparison.OrdinalIgnoreCase)) activeSection = "SAY";
                    }
                    else if (Settings.TxtScriptsCompatibilityVersion.StartsWith(
                                 "LFM2-", StringComparison.OrdinalIgnoreCase))
                    {
                        ValidateLingFengCommandShape(
                            activeSection, command, tokens, definition.GetSourceLocation(index), errors);
                    }

                    if (TryGetLocalTargetLabel(command, tokens, out string localLabel))
                    {
                        string normalizedLocalLabel = NormalizeLabel(localLabel);
                        if (!pagesByKey[definition.Key].Contains(normalizedLocalLabel))
                            errors.Add($"TXT-SNAPSHOT-010：跳转标签不存在 {normalizedLocalLabel}（{definition.GetSourceLocation(index)}）。");
                        continue;
                    }

                    bool include = command.Equals("INCLUDE", StringComparison.OrdinalIgnoreCase) ||
                                   command.Equals("INSERT", StringComparison.OrdinalIgnoreCase);
                    bool call = command.Equals("CALL", StringComparison.OrdinalIgnoreCase);
                    if ((!include && !call) || tokens.Length < 2) continue;

                    string rawTarget = tokens[1].Trim('[', ']');
                    bool resolved = call
                        ? TryResolveCallKey(rawTarget, out string targetKey)
                        : TryResolveReferenceKey(definition.Key, rawTarget, out targetKey);
                    if (!resolved)
                    {
                        errors.Add($"TXT-SNAPSHOT-003：引用路径无效 [{rawTarget}]（{definition.GetSourceLocation(index)}）。");
                        continue;
                    }
                    if (!pagesByKey.TryGetValue(targetKey, out HashSet<string> targetPages))
                    {
                        errors.Add($"TXT-SNAPSHOT-004：引用脚本不存在 [{rawTarget}]（{definition.GetSourceLocation(index)}）。");
                        continue;
                    }

                    if (include)
                    {
                        if (!includeGraph.TryGetValue(definition.Key, out HashSet<string> targets))
                            includeGraph[definition.Key] = targets = new HashSet<string>(StringComparer.Ordinal);
                        targets.Add(targetKey);
                    }

                    string label = include && tokens.Length >= 3 ? tokens[2] : string.Empty;
                    if (label.Length == 0) continue;
                    string normalizedLabel = NormalizeLabel(label);
                    if (!targetPages.Contains(normalizedLabel))
                        errors.Add($"TXT-SNAPSHOT-005：引用标签不存在 {normalizedLabel} 于 {targetKey}（{definition.GetSourceLocation(index)}）。");
                }
            }

            ValidateIncludeGraph(includeGraph, errors);

            try
            {
                Variables.ScriptVariableTextDeclarationParser.CreateSnapshot(provider);
            }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException)
            {
                errors.Add($"TXT-SNAPSHOT-011：变量声明无效：{error.Message}");
            }

            return errors;
        }

        private static void ValidateLingFengCommandShape(
            string section,
            string command,
            IReadOnlyList<string> tokens,
            string sourceLocation,
            ICollection<string> errors)
        {
            if (section.Equals("IF", StringComparison.Ordinal) &&
                (command.Equals("NOT", StringComparison.OrdinalIgnoreCase) || command == "!"))
            {
                if (tokens.Count < 2)
                {
                    errors.Add($"TXT-SNAPSHOT-014：NOT/! 后缺少检测命令（{sourceLocation}）。");
                    return;
                }
                command = tokens[1];
            }
            else if (section.Equals("IF", StringComparison.Ordinal) &&
                     command.StartsWith('!') && command.Length > 1)
            {
                command = command.Substring(1);
            }

            if (Settings.TxtScriptsStrictCompatibility &&
                command is not ("{" or "}") &&
                !command.StartsWith(';') && !command.StartsWith("//", StringComparison.Ordinal) &&
                !command.StartsWith("[@", StringComparison.OrdinalIgnoreCase))
            {
                bool supported = section.Equals("IF", StringComparison.Ordinal)
                    ? SupportedCheckCommands.Contains(command)
                    : !section.Equals("ACT", StringComparison.Ordinal) || SupportedActionCommands.Contains(command);
                if (!supported)
                {
                    errors.Add($"TXT-SNAPSHOT-014：未知 {section} 命令 {command.ToUpperInvariant()}（{sourceLocation}）。");
                    return;
                }
            }

            if (section.Equals("ACT", StringComparison.Ordinal) &&
                command.Equals("GIVE", StringComparison.OrdinalIgnoreCase) && tokens.Count > 3)
            {
                errors.Add($"TXT-SNAPSHOT-013：LFM2 扩展 GIVE 极品属性没有稳定等价物品模型；请改用 GIVEITEM 或类型化 C# 物品 API（{sourceLocation}）。");
                return;
            }

            if (section.Equals("IF", StringComparison.Ordinal) &&
                command.Equals("CHECKITEM", StringComparison.OrdinalIgnoreCase) &&
                tokens.Count > 4 && tokens[4] != "0")
                errors.Add($"TXT-SNAPSHOT-013：CHECKITEM 改名装备参数必须为 0；当前物品模型没有改名字段（{sourceLocation}）。");

            if (section.Equals("ACT", StringComparison.Ordinal) &&
                command.Equals("TAKE", StringComparison.OrdinalIgnoreCase) &&
                (tokens.Count < 6 || tokens[3] != "0" || tokens[5] != "1"))
                errors.Add($"TXT-SNAPSHOT-013：TAKE 当前要求改名检测参数为 0 且排除自定义 OK 框参数为 1（{sourceLocation}）。");

            if (section.Equals("IF", StringComparison.Ordinal) &&
                (command.Equals("ISQUESTACTIVE", StringComparison.OrdinalIgnoreCase) ||
                 command.Equals("ISQUESTCOMPLETED", StringComparison.OrdinalIgnoreCase)))
            {
                string questError = "任务状态检测必须且只能包含一个任务编号。";
                if (tokens.Count != 2 ||
                    !LingFengSocialCommandExecutor.TryParseQuestIndex(tokens.ElementAtOrDefault(1), out _, out questError))
                    errors.Add($"TXT-SNAPSHOT-015：{questError}（{sourceLocation}）。");
            }

            if (section.Equals("ACT", StringComparison.Ordinal) &&
                command.Equals("GIVEGUILDEXP", StringComparison.OrdinalIgnoreCase) &&
                (tokens.Count != 2 || !uint.TryParse(tokens.ElementAtOrDefault(1), out uint guildExperience) || guildExperience == 0))
                errors.Add($"TXT-SNAPSHOT-015：行会经验必须是大于零的 uint 整数（{sourceLocation}）。");

            if (section.Equals("ACT", StringComparison.Ordinal) &&
                command.Equals("TRYREMOVEFROMGUILD", StringComparison.OrdinalIgnoreCase) && tokens.Count != 1)
                errors.Add($"TXT-SNAPSHOT-015：TRYREMOVEFROMGUILD 不接受参数（{sourceLocation}）。");

            if (section.Equals("ACT", StringComparison.Ordinal) &&
                command.Equals("OPENBROWSER", StringComparison.OrdinalIgnoreCase))
            {
                string browserError = "OPENBROWSER 必须且只能包含一个 URL。";
                if (tokens.Count != 2 || !LingFengHighRiskCommandPolicy.CanOpenBrowser(
                        tokens.ElementAtOrDefault(1), Settings.TxtScriptsHighRiskCapabilitiesEnabled,
                        Settings.TxtScriptsAllowedHttpsHosts, killSwitchEnabled: true, out _, out browserError))
                    errors.Add($"TXT-SNAPSHOT-017：OPENBROWSER 被安全策略拒绝：{browserError}（{sourceLocation}）。");
            }
        }

        private static bool TryGetLocalTargetLabel(string command, IReadOnlyList<string> tokens, out string label)
        {
            label = string.Empty;
            int index = command.ToUpperInvariant() switch
            {
                "GOTO" or "GROUPGOTO" => 1,
                "DELAYGOTO" or "TIMERECALL" or "TIMERECALLGROUP" => 2,
                "GOTOLABEL" => 2,
                _ => -1
            };
            if (index < 0 || tokens.Count <= index || string.IsNullOrWhiteSpace(tokens[index])) return false;
            label = tokens[index];
            return true;
        }

        private static string NormalizeLabel(string label)
        {
            string value = label.Trim().Trim('[', ']');
            if (!value.StartsWith('@')) value = "@" + value;
            return $"[{value}]".ToUpperInvariant();
        }

        private static bool TryResolveCallKey(string rawTarget, out string targetKey)
        {
            string target = rawTarget.Trim().Replace('\\', '/');
            if (target.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                target = target.Substring(0, target.Length - 4);
            if (!target.Contains('/')) target = "NPCs/" + target;
            return LogicKey.TryNormalize(target, out targetKey);
        }

        private static void ValidateIncludeGraph(
            IReadOnlyDictionary<string, HashSet<string>> graph,
            ICollection<string> errors)
        {
            var visiting = new HashSet<string>(StringComparer.Ordinal);
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var path = new List<string>();
            foreach (string key in graph.Keys.OrderBy(value => value, StringComparer.Ordinal))
                Visit(key, 0);

            void Visit(string key, int depth)
            {
                if (visited.Contains(key)) return;
                if (depth > MaximumReferenceDepth)
                {
                    errors.Add($"TXT-SNAPSHOT-009：INCLUDE/INSERT 引用深度超过 {MaximumReferenceDepth}：{string.Join(" -> ", path.Append(key))}。");
                    return;
                }
                if (!visiting.Add(key))
                {
                    int start = path.FindIndex(value => value.Equals(key, StringComparison.Ordinal));
                    IEnumerable<string> cycle = (start >= 0 ? path.Skip(start) : path).Append(key);
                    errors.Add($"TXT-SNAPSHOT-008：INCLUDE/INSERT 循环引用：{string.Join(" -> ", cycle)}。");
                    return;
                }

                path.Add(key);
                if (graph.TryGetValue(key, out HashSet<string> targets))
                    foreach (string target in targets.OrderBy(value => value, StringComparer.Ordinal))
                        Visit(target, depth + 1);
                path.RemoveAt(path.Count - 1);
                visiting.Remove(key);
                visited.Add(key);
            }
        }

        private static bool TryResolveReferenceKey(string sourceKey, string rawTarget, out string targetKey)
        {
            targetKey = string.Empty;
            string target = rawTarget.Trim().Replace('\\', '/');
            if (target.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                target = target.Substring(0, target.Length - 4);
            if (target.Contains('/') || target.StartsWith("QuestDiary", StringComparison.OrdinalIgnoreCase) ||
                target.StartsWith("Defines", StringComparison.OrdinalIgnoreCase))
                return LogicKey.TryNormalize(target, out targetKey);

            int slash = sourceKey.LastIndexOf('/');
            string relative = slash < 0 ? target : sourceKey.Substring(0, slash + 1) + target;
            return LogicKey.TryNormalize(relative, out targetKey);
        }
    }
}
