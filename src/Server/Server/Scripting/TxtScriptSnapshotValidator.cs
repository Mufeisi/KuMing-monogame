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
            "IF", "OR", "ACT", "SAY", "ELSEACT", "ELSESAY", "INCLUDE", "INSERT", "CALL", "DEFINE"
        };
        private static readonly HashSet<string> SupportedCheckCommands = new(StringComparer.OrdinalIgnoreCase)
        {
            "AFFORDGATE", "AFFORDGUARD", "AFFORDSIEGE", "AFFORDWALL", "CHANCE", "CHECK",
            "CHECKBUFF", "CHECKCALC", "CHECKCLASS", "CHECKCONQUEST", "CHECKCREDIT", "CHECKCREDITPOINT",
            "CHECKDICTALLDIGIT", "CHECKEXACTMON", "CHECKEXP", "CHECKGENDER", "CHECKGOLD",
            "CHECKGUILDGOLD", "CHECKGUILDNAMELIST", "CHECKHEROCLASS", "CHECKHEROGENDER",
            "CHECKHEROITEM", "CHECKHP", "CHECKHUM", "CHECKINDICT", "CHECKITEM", "CHECKJOB",
            "CHECKLEVEL", "CHECKLEVELEX", "CHECKLISTALLDIGIT", "CHECKMAP", "CHECKMAPNAME", "ISONMAP", "CHECKMON", "CHECKMP", "CHECKMPPER", "CHECKHPPER", "CHECKNAMELIST", "CHECKACCOUNTLIST", "CHECKNAMEDATETIMELIST", "CHECKSCRIPTPARAM", "CHECKUPGRADECOUNT", "H.CHECKUPGRADECOUNT", "GENDER",
            "CHECKPERMISSION", "CHECKPET", "CHECKPKPOINT", "CHECKPKPOINTEX", "CHECKQUEST",
            "CHECKRANGE", "CHECKRELATIONSHIP", "CHECKTIMER", "CHECKTRANSFORM", "CHECKVARINLIST",
            "CHECKWEDDINGRING", "CONQUESTAVAILABLE", "CONQUESTOWNER", "DAYOFWEEK",
            "FINDMONPOINT", "GROUPCHECKNEARBY", "GROUPCOUNT", "CHECKGROUPMEMBERCOUNT", "GROUPLEADER", "HASBAGSPACE", "HEROLEVEL", "HOUR",
            "INGUILD", "ISADMIN", "ISGUILDLEADER", "ISNEWHUMAN", "ISQUESTACTIVE", "ISQUESTCOMPLETED", "HAVEMASTER", "LEVEL", "MIN", "PETCOUNT",
            "PETLEVEL", "RANDOM", "RANDOMEX", "EQUAL", "LARGE", "SMALL", "CHECKCONTAINSTEXT", "CHECKTEXTLIST", "CHECKCACHETEXTLIST", "GETSTRINGPOSEX",
            "CHECKKILLBYHUM", "KILLBYHUM", "CHECKATTACKMODE", "CHECKONLINE", "CHECKSTRINGLENGTH",
            "L.CHECKJOB", "L.CHECKLEVELEX", "M.CHECKLEVELEX", "M.CHECKHPPER", "INSAFEZONE",
            "CHECKRANGEMONCOUNT", "CHECKRANGEHUMCOUNT", "CHECKMAPSAMEMONCOUNT", "CHECKMAPMONCOUNT", "CHECKMAPHUMANCOUNT", "CHECKMONMAP",
            "CHECKSTATEVALUE", "M.CHECKSTATEVALUE",
            "CHECKMARRY", "P.CHECKMARRY", "CHECKPOSEMARRY", "P.GENDER", "CHECKPOSEGENDER", "CHECKCURRTARGETRACE",
            "CHECKCURRTARGETSLAVE", "CHECKGAMEGOLD", "CHECKGAMEPOINT", "CHECKBINDMONEY", "CHECKUSEITEM", "H.CHECKUSEITEM", "CHECKSTORAGEOPEN", "CHECKITEMW", "CHECKREPAIRALLGOLD",
            "P.CHECKSHIELDSTATEOPEN", "CHECKBATTLESTATUS", "CHECKUNDERWAR", "CHECKPOSEDIR", "CHECKPOSELEVEL", "CHECKISMASTER", "CHECKMASTER", "CHECKPOSEMASTER", "ISCASTLEGUILD", "ISCASTLEMASTER", "M.ISCASTLEGUILD", "NOT", "!",
            "CHECKRENEWLEVEL", "H.CHECKRENEWLEVEL", "CHECKFENGHAO", "H.CHECKFENGHAO",
            "CHECKACTIVEFENGHAO", "H.CHECKACTIVEFENGHAO",
            "CHECKSLAVECOUNT", "CHECKMIRRORMAP", "CANMOVEECTYPE",
            "CHECKMAGICNAME", "CHECKSKILL", "CHECKBAGSIZE", "CHECKBAGGAGE",
            "CHECKHAVEHERO", "CHECKHEROONLINE",
            "CHECKCUSTOMITEMVALUE", "H.CHECKCUSTOMITEMVALUE", "CHECKITEMSTATE", "CHECKITEMBIND",
            "CHECKITEMADDVALUE", "H.CHECKITEMADDVALUE",
            "CHECKITEMNAMECOLOR", "H.CHECKITEMNAMECOLOR",
            "CHECKCUSTOMITEMPROGRESSBARVALUE", "H.CHECKCUSTOMITEMPROGRESSBARVALUE",
            "CHECKGAMEDIAMOND", "CHECKGAMEGIRD",
            "M.EQUAL", "M.LARGE", "M.SMALL", "CHECKMYSHOP", "CHECKSHOPNAME", "CHECKBOXITEMCOUNT"
        };
        private static readonly HashSet<string> SupportedActionCommands = new(StringComparer.OrdinalIgnoreCase)
        {
            "ADDGUILDNAMELIST", "ADDMAILGOLD", "ADDMAILITEM", "ADDNAMELIST", "ADDACCOUNTLIST", "DELACCOUNTLIST", "ADDNAMEDATETIMELIST", "ADDTOGUILD",
            "ADDHPPER", "ADDMPPER", "ADDHUMNEWVALUE", "ADDTOLIST", "BREAK", "BREAKTIMERECALL", "CALC", "CALCPERCENT", "CALL", "CANGAINEXP", "CHANGECLASS", "CHANGEJOB",
            "CHANGEHUMABILITY", "M.CHANGEHUMABILITY", "<$CURRRTARGETNAME>.CHANGEHUMABILITY", "CHANGEHUMABILITYPERCENTAGE", "CHANGESLAVEABILITY", "RECALCSLAVEABILITY", "M.CHANGEHUMABILITYPERCENTAGE",
            "L.CHANGEHUMABILITYPERCENTAGE", "CHANGESTATE", "M.CHANGESTATE", "L.CHANGESTATE",
            "M.MAKEPOSION", "L.MAKEPOSION",
            "M.GETOBJECTABILITYEX", "L.GETOBJECTABILITYEX",
            "GETDBMONSTERFIELDVALUE",
            "REPAIRALL", "ACTREPAIRALL",
            "GETPLAYINFO",
            "GMEXECUTE",
            "HIDEMODEEX",
            "CHANGEMODE",
            "CHANGESPEED", "M.CHANGESPEED", "FS.CHANGESPEED", "CLEARDELAYGOTO", "GUILDNOTICEMSG", "SENDMOVEHINTMSG",
            "SETSUCKDAMAGE", "SETONTIMER", "SETOFFTIMER", "SETREBORN", "LOOPGOTO", "ENDLOOP",
            "RANGEHARM", "L.RANGEHARM",
            "RELEASEMAGIC",
            "RELEASEMAGICEX",
            "GIVEFENGHAO", "H.GIVEFENGHAO", "RECYCFENGHAO", "H.RECYCFENGHAO",
            "SETCLIENTBUFF", "M.SETCLIENTBUFF", "L.SETCLIENTBUFF", "CLOSECLIENTBUFF",
            "SETSNDACASKET", "ACTIVATIONCASKET", "UNALLOWITEMINTOBOX", "RETURNBOXITEM",
            "SCREENEFFECT", "STOPSCREENEFFECT",
            "MAPEFFECT",
            "ADDDLGEX", "DELDLG",
            "GETDBITEMFIELDVALUE", "GETBAGITEMCOUNT",
            "CSVOPENCACHE", "CSVFINDTEXTROW", "READCONFIGFILEITEM", "READCACHECONFIGFILEITEM", "WRITECACHECONFIGFILEITEM", "GETRANDOMLINETEXT", "EXTRACTSTRING", "RANDOMSPLIT", "MOVR",
            "HUMANHP", "HUMANMP", "L.HUMANHP", "<$KILLER>.HUMANHP", "CHANGEEXP", "GAMEGOLD", "GAMEPOINT",
            "GAMEDIAMOND", "GAMEGIRD",
            "M.HUMANHP",
            "<$KILLER>.GAMEGOLD", "<$KILLER>.GAMEGIRD",
            "<$KILLER>.CHANGEPKPOINT",
            "CHANGEGENDER", "CHANGEHAIR", "CHANGELEVEL", "CHANGENAMECOLOR", "CHANGEPKPOINT", "CHANGEMONABILITY", "RECALCMONABILITY", "CLEARGUILDNAMELIST",
            "CLEARNAMELIST", "CLEARPETS", "CLEARITEMMAP", "CLEARMAPMON", "CLOSE", "CLOSEBIGDIALOGBOX", "CLOSEMERCHANTBIGDLG", "CLOSEGATE", "COMPOSEMAIL", "CONQUESTGATE",
            "CONQUESTGUARD", "CONQUESTREPAIRALL", "CONQUESTWALL", "DEC", "DELAYGOTO", "DELETEHERO", "CHANGEMAPDESC",
            "DELGUILDNAMELIST", "DELNAMELIST", "DECBINDMONEY", "DIV", "DROP", "ENTERMAP", "EXPIRETIMER",
            "EXTRACTLIST", "FORCEDIVORCE", "MARRY", "UNMARRY", "MASTER", "UNMASTER", "FORMULATION", "<$CURRRTARGETNAME>.FORMULATION", "<$CURRRTARGETNAME>.TAKE", "<$CURRRTARGETNAME>.GOTO", "<$CURRRTARGETNAME>.DELAYGOTO", "EQUAL", "GETDICTITEMS", "GETDICTKEYCOUNT",
            "GETDICTMAXVALUE", "GETDICTMINVALUE", "GETHUMVAR", "GETLISTMAXVAR", "GETLISTMINVAR", "GETSTRINGPOS",
            "GETLISTVARCOUNT", "GETLISTVARINDEX", "GETLISTSTRING", "GETRANDOMTEXT", "GETITEMCOUNT", "GETITEMFIELDVALUE", "GETSTRINGPOSEX", "GIVE", "GIVEBUFF", "GIVECREDIT",
            "GIVEEXP", "GIVEGOLD", "GIVEGUILDEXP", "GIVEGUILDGOLD", "GIVEHP", "GIVEITEM", "GIVESTATEITEM", "SETITEMSTATE", "LINKGIVEITEM", "LINKPICKUPITEM", "CLEARLINKITEM", "GIVEMP", "ADDSKILL", "SETSKILLPOWER",
            "GIVEPEARLS", "GIVEPET", "GIVESKILL", "GLOBALMESSAGE", "GOLDCOUNT", "CREDITPOINT", "GOTO", "GOTOLABEL",
            "GROUPGOTO", "GROUPRECALL", "GROUPTELEPORT", "HAIRSTYLE", "INC", "INCREASEPKPOINT", "INITVAR", "CHANGEMODEEX",
            "INSERTTOLIST", "INSTANCEMOVE", "LOADVALUE", "LOCALMESSAGE", "MAKEWEDDINGRING", "MESSAGEBOX", "MONCLEAR",
            "MONGEN", "MONGENEX", "MONCLEAR", "MOV", "MOVE", "TELEPORT", "MAP", "MAPMOVE", "MUL", "OPENBROWSER", "OPENWEBSITE", "OPENMERCHANTBIGDLG", "OPENGATE", "PARAM1", "PARAM2", "PARAM3",
            "PLAYSOUND", "REDUCEPKPOINT", "REFRESHEFFECTS", "REMOVEBUFF", "REMOVEFROMGUILD", "TRYREMOVEFROMGUILD",
            "REMOVELISTBYCONTENT", "REMOVELISTBYINDEX", "REMOVEPET", "REMOVESKILL",
            "REPLACELISTBYINDEX", "REVERSELIST", "REVIVEHERO", "ROLLDIE", "ROLLYUT", "SAVEVALUE", "CHANGEDAMAGEVALUE",
            "SCHEDULECONQUEST", "SEALHERO", "SENDMAIL", "SENDMSG", "SENDCENTERMSG", "M.SENDCENTERMSG", "SENDDELAYMSG", "SENDMOVEMSG", "SENDNEWLINEMSG", "FILTERGLOBALMSG", "SET", "SETCONQUESTRATE", "SETCURRTARGET", "SKILLLEVEL",
            "DELSKILL", "CLEARSKILL", "SETHUMATTACKMODE", "RENEWLEVEL", "KILLSLAVE", "RECALLSELF", "SETSLAVEATTACKHUMPOWERRATE", "KILLMONEXPRATE", "POWERRATE", "SETBLASTHITRATE", "KILLMONBURSTRATE", "CLEARSKILLCD", "KILLCALLMOB", "RECALLMOB", "RECLAIMITEM",
            "SETCUSTOMITEMABIL", "H.SETCUSTOMITEMABIL", "GETCUSTOMITEMABIL", "H.GETCUSTOMITEMABIL",
            "SETARRBUFF", "<$CURRRTARGETNAME>.SETARRBUFF", "CLOSEARRBUFF", "ADDARRBUTTON", "DELARRBUTTON", "ADDBUTTON", "DELBOXITEM",
            "SETUPGRADEITEM", "OPENITEMBOXEX",
            "CHANGEITEMNAME", "SETBODYCOLOR",
            "EXTBAGPAGECOUNT", "EXTBAGOPENITEMCOUNT", "SETBIGSTORAGECOUNT",
            "OPENAUTOPICKITEM", "CLOSEAUTOPICKITEM", "OPENBIGDIALOGBOX", "OPENITEMBOX",
            "BREAKADDSELLPLAYER", "STOPTAKEON", "SETITEMFROM",
            "HCALL", "ADDATTACKSABUKALL", "AUTOTAKEONITEM", "CHANGEHUMNAME",
            "CREATEMYSHOP", "OPENGODBLESS", "PLAYSOUNDEXT", "SETOFFLINEPLAY",
            "SETRANKLEVELNAME", "SHOWGODBLESS", "STARTAUTOPLAYGAME", "STOPAUTOPLAYGAME",
            "STOPBUYUSER", "STOPTAKEOFF", "SUPERMOVEMSG", "TAKEPOSW",
            "SCATTERMONITEMS", "MONDROPITEMSEX", "OPENSTORAGEVIEW", "OPENSTORATGE", "GETMAPMONCOUNT", "GETDBIDXITEMFIELDVALUE", "GETBAGINFO", "SETICON",
            "PLAYEFFECT", "M.PLAYEFFECT", "PET.PLAYEFFECT",
            "ADDMIRRORMAP", "DELMIRRORMAP", "SETMIRRORMAPTIME", "GETMIRRORMAPTIME",
            "CREATEECTYPE", "MOVEECTYPE", "MOBECTYPEMON",
            "ADDTEXTLIST", "ADDTEXTLISTEX",
            "SETCUSTOMITEMVALUE", "H.SETCUSTOMITEMVALUE",
            "SETCUSTOMITEMVALUEEX", "H.SETCUSTOMITEMVALUEEX",
            "GETCUSTOMITEMVALUE", "H.GETCUSTOMITEMVALUE",
            "GETCUSTOMITEMVALUEEX", "H.GETCUSTOMITEMVALUEEX",
            "GETALLCUSTOMITEMVALUE", "H.GETALLCUSTOMITEMVALUE",
            "SETITEMADDBYTE", "SETITEMADDINT", "SETITEMADDTEXT",
            "GETITEMADDBYTE", "GETITEMADDINT", "GETITEMADDTEXT",
            "CHANGEITEMADDVALUE", "H.CHANGEITEMADDVALUE",
            "CHANGEITEMUPGRADECOUNT", "H.CHANGEITEMUPGRADECOUNT",
            "CHANGEITEMNAMECOLOR", "H.CHANGEITEMNAMECOLOR",
            "SETITEMLOOKS", "H.SETITEMLOOKS", "SETITEMSHAPE", "H.SETITEMSHAPE",
            "SETCUSTOMITEMPROGRESSBAR", "H.SETCUSTOMITEMPROGRESSBAR",
            "SETCUSTOMITEMPROGRESSBARVALUE", "H.SETCUSTOMITEMPROGRESSBARVALUE",
            "GETCUSTOMITEMPROGRESSBARVALUE", "H.GETCUSTOMITEMPROGRESSBARVALUE",
            "SETCUSTOMITEMTEXT", "H.SETCUSTOMITEMTEXT",
            "SETCUSTOMITEMTEXTCOLOR", "H.SETCUSTOMITEMTEXTCOLOR",
            "SETITEMEFFECT", "H.SETITEMEFFECT",
            "SETNEWITEMVALUE", "H.SETNEWITEMVALUE", "SETNEWITEMVALUEEX",
            "LOCKUPDATEITEM", "H.LOCKUPDATEITEM", "UPDATEITEM", "H.UPDATEITEM",
            "SETHUMVAR", "SETPKPOINT", "SETTIMER", "SORTLIST", "STARTCONQUEST", "TAKE",
            "TAKECONQUESTGOLD", "TAKECREDIT", "TAKEGOLD", "TAKEGUILDGOLD", "TAKEITEM", "TAKEBAGITEM", "TAKEBAGITEMEX", "TAKEW", "TAKEPEARLS",
            "TIMERECALL", "TIMERECALLGROUP", "UNEQUIPITEM", "VAR", "EXTRACTSTRINGEX", "WHILE", "ENDWHILE",
            "TEXTSPLIT", "TEXTLENGTH", "SETSTRINGBLANK", "TEXTREPLACE", "UNIXTOSTR",
            "M.MOV", "M.INC", "M.DEC", "M.MUL", "M.DIV"
        };
        private static readonly HashSet<string> KnownUnsupportedSystemTriggerLabels = new(StringComparer.OrdinalIgnoreCase)
        {
            "[@KILLSLAVE]", "[@GROUPKILLMON]",
            "[@PICKUPITEM]", "[@DROPITEM]",
            "[@HUMDROPITEM]", "[@ITEMEXPIRED]"
        };
        private static readonly HashSet<string> KnownLegacyFirstPageDuplicates = new(StringComparer.OrdinalIgnoreCase)
        {
            "npcdefs/武馆教头-0137|[@WATEUNMASTER]"
        };

        public static IReadOnlyList<string> Validate(ITextFileProvider provider)
        {
            if (provider == null) return new[] { "TXT-SNAPSHOT-001：候选 Provider 不能为空。" };

            var errors = new List<string>();
            var pagesByKey = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            var includeGraph = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            IReadOnlySet<string> dropScriptKeys = provider is PhysicalTextFileProvider physical &&
                                                   physical.MonsterDropProvider is LingFengMonsterDropProvider drops
                ? drops.ConsumedScriptKeys
                : new HashSet<string>(StringComparer.Ordinal);
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
                    {
                        if (!dropScriptKeys.Contains(definition.Key))
                            errors.Add($"TXT-SNAPSHOT-006：未知段落指令 #{directive.Groups[1].Value.ToUpperInvariant()}（{definition.GetSourceLocation(index)}）。");
                    }

                    Match page = PageRegex.Match(definition.Lines[index]);
                    if (!page.Success) continue;
                    string label = page.Groups[1].Value.ToUpperInvariant();
                    if (!pages.Add(label) && !KnownLegacyFirstPageDuplicates.Contains(
                            definition.Key + "|" + label))
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
                    if (!LingFengRobotScheduleProvider.TryResolvePages(
                            robotSchedules, robotPages, out _,
                            out IReadOnlyList<string> pageErrors))
                        errors.AddRange(pageErrors);
                }
            }

            foreach (TextFileDefinition definition in provider.GetAll())
            {
                if (definition.Key.Equals("systemscripts/autorunrobot", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (dropScriptKeys.Contains(definition.Key)) continue;
                string activeSection = string.Empty;
                var whileLines = new Stack<int>();
                void FlushUnclosedWhile()
                {
                    while (whileLines.Count > 0)
                    {
                        int openingLine = whileLines.Pop();
                        errors.Add($"TXT-SNAPSHOT-018：WHILE 缺少同一动作段内的 ENDWHILE（{definition.GetSourceLocation(openingLine)}）。");
                    }
                }
                for (int index = 0; index < definition.Lines.Count; index++)
                {
                    if (SectionRegex.IsMatch(definition.Lines[index]))
                    {
                        FlushUnclosedWhile();
                        activeSection = string.Empty;
                        continue;
                    }
                    if (!TxtScriptTokenizer.TryTokenize(
                            definition.Lines[index].TrimStart(), out string[] tokens, out _) || tokens.Length == 0)
                        continue;

                    string command = tokens[0].TrimStart('#');
                    if (tokens[0].StartsWith('#'))
                    {
                        if (Regex.IsMatch(command, @"^IF\([1-9][0-9]*\)$",
                                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                            command = "IF";
                        bool startsSection = command.Equals("IF", StringComparison.OrdinalIgnoreCase) ||
                            command.Equals("OR", StringComparison.OrdinalIgnoreCase) ||
                            command.Equals("ACT", StringComparison.OrdinalIgnoreCase) ||
                            command.Equals("ELSEACT", StringComparison.OrdinalIgnoreCase) ||
                            command.Equals("SAY", StringComparison.OrdinalIgnoreCase) ||
                            command.Equals("ELSESAY", StringComparison.OrdinalIgnoreCase);
                        if (startsSection && activeSection == "ACT")
                            FlushUnclosedWhile();
                        if (command.Equals("IF", StringComparison.OrdinalIgnoreCase) ||
                            command.Equals("OR", StringComparison.OrdinalIgnoreCase)) activeSection = "IF";
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
                        if (activeSection == "ACT" && command.Equals("WHILE", StringComparison.OrdinalIgnoreCase))
                            whileLines.Push(index);
                        else if (activeSection == "ACT" && command.Equals("ENDWHILE", StringComparison.OrdinalIgnoreCase))
                        {
                            if (whileLines.Count == 0)
                                errors.Add($"TXT-SNAPSHOT-018：ENDWHILE 缺少同一动作段内的 WHILE（{definition.GetSourceLocation(index)}）。");
                            else
                                whileLines.Pop();
                        }
                    }

                    if (TryGetLocalTargetLabel(command, tokens, out string localLabel))
                    {
                        if (IsDynamicLabel(localLabel)) continue;
                        string normalizedLocalLabel = NormalizeLabel(localLabel);
                        if (!pagesByKey[definition.Key].Contains(normalizedLocalLabel) &&
                            !LingFengScriptReferenceResolver.TryResolveUniquePage(
                                provider, normalizedLocalLabel, out _) &&
                            !LingFengScriptReferenceResolver.IsExternalCallbackLabel(
                                normalizedLocalLabel))
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
                        if (include && LingFengScriptReferenceResolver.IsKnownExternalInclude(rawTarget))
                            continue;
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
                FlushUnclosedWhile();
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
                tokens = tokens.Skip(1).ToArray();
            }
            else if (section.Equals("IF", StringComparison.Ordinal) &&
                     command.StartsWith('!') && command.Length > 1)
            {
                command = command.Substring(1);
                string[] normalized = tokens.ToArray();
                normalized[0] = command;
                tokens = normalized;
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

            if (section.Equals("ACT", StringComparison.Ordinal) &&
                command.Equals("WHILE", StringComparison.OrdinalIgnoreCase) &&
                (tokens.Count != 4 || tokens[2] is not ("=" or "==" or "!=" or "<>" or ">" or ">=" or "<" or "<=")))
                errors.Add($"TXT-SNAPSHOT-018：WHILE 参数格式无效（{sourceLocation}）。");

            if (section.Equals("ACT", StringComparison.Ordinal) &&
                command.Equals("ADDHUMNEWVALUE", StringComparison.OrdinalIgnoreCase) &&
                (tokens.Count is < 4 or > 6 ||
                 tokens[2] is not ("=" or "+" or "-") ||
                 (tokens.Count == 6 && tokens[5] != "0")))
                errors.Add($"TXT-SNAPSHOT-013：ADDHUMNEWVALUE 参数格式无效（{sourceLocation}）。");

            if (section.Equals("ACT", StringComparison.Ordinal) &&
                command.Equals("SETONTIMER", StringComparison.OrdinalIgnoreCase) &&
                tokens.Count is < 3 or > 4)
                errors.Add($"TXT-SNAPSHOT-013：SETONTIMER 参数格式无效（{sourceLocation}）。");

            if (section.Equals("ACT", StringComparison.Ordinal) &&
                command.Equals("SETOFFTIMER", StringComparison.OrdinalIgnoreCase) &&
                tokens.Count != 2)
                errors.Add($"TXT-SNAPSHOT-013：SETOFFTIMER 参数格式无效（{sourceLocation}）。");

            if (section.Equals("ACT", StringComparison.Ordinal) &&
                command.Equals("LOOPGOTO", StringComparison.OrdinalIgnoreCase) &&
                tokens.Count is < 2 or > 3)
                errors.Add($"TXT-SNAPSHOT-018：LOOPGOTO 参数格式无效（{sourceLocation}）。");

            if (section.Equals("ACT", StringComparison.Ordinal) &&
                command.Equals("ENDLOOP", StringComparison.OrdinalIgnoreCase) &&
                tokens.Count != 1)
                errors.Add($"TXT-SNAPSHOT-018：ENDLOOP 不接受参数（{sourceLocation}）。");

            if (section.Equals("IF", StringComparison.Ordinal) &&
                command.Equals("CHECKGAMEGIRD", StringComparison.OrdinalIgnoreCase) &&
                (tokens.Count != 3 || tokens[1] is not ("?" or "<" or ">" or "=" or "==" or "<=" or ">=")))
                errors.Add($"TXT-SNAPSHOT-015：CHECKGAMEGIRD 参数格式无效（{sourceLocation}）。");

            if (section.Equals("ACT", StringComparison.Ordinal) &&
                command.Equals("GAMEGIRD", StringComparison.OrdinalIgnoreCase) &&
                (tokens.Count != 3 || tokens[1] is not ("+" or "-" or "=")))
                errors.Add($"TXT-SNAPSHOT-015：GAMEGIRD 参数格式无效（{sourceLocation}）。");

            if (section.Equals("ACT", StringComparison.Ordinal) &&
                command.Equals("ENDWHILE", StringComparison.OrdinalIgnoreCase) && tokens.Count != 1)
                errors.Add($"TXT-SNAPSHOT-018：ENDWHILE 不接受参数（{sourceLocation}）。");

            if (section.Equals("ACT", StringComparison.Ordinal) &&
                command.Equals("RECLAIMITEM", StringComparison.OrdinalIgnoreCase) && tokens.Count != 1)
                errors.Add($"TXT-SNAPSHOT-015：RECLAIMITEM 不接受参数（{sourceLocation}）。");

            if (section.Equals("IF", StringComparison.Ordinal) &&
                command.Equals("CHECKMAGICNAME", StringComparison.OrdinalIgnoreCase) && tokens.Count != 2)
                errors.Add($"TXT-SNAPSHOT-015：CHECKMAGICNAME 必须且只能包含一个技能名称（{sourceLocation}）。");

            if (section.Equals("IF", StringComparison.Ordinal) &&
                command.Equals("CHECKSKILL", StringComparison.OrdinalIgnoreCase) &&
                (tokens.Count is < 4 or > 5 ||
                 tokens[2] is not (">" or "<" or "=") ||
                 (tokens.Count == 5 && tokens[4] is not ("0" or "1"))))
                errors.Add($"TXT-SNAPSHOT-013：CHECKSKILL 参数格式无效（{sourceLocation}）。");

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
                command.Equals("ADDSKILL", StringComparison.OrdinalIgnoreCase) &&
                (tokens.Count is < 2 or > 3 ||
                 (tokens.Count == 3 &&
                  (!byte.TryParse(tokens[2], out byte skillLevel) || skillLevel > 3))))
                errors.Add($"TXT-SNAPSHOT-015：ADDSKILL 初始等级必须为 0 到 3（{sourceLocation}）。");

            if (section.Equals("ACT", StringComparison.Ordinal) &&
                command.Equals("GETITEMCOUNT", StringComparison.OrdinalIgnoreCase) &&
                (tokens.Count != 4 || tokens[1] != "0"))
                errors.Add($"TXT-SNAPSHOT-013：GETITEMCOUNT 当前仅支持背包位置 0（{sourceLocation}）。");

            if (section.Equals("ACT", StringComparison.Ordinal) &&
                command.Equals("EXTRACTSTRINGEX", StringComparison.OrdinalIgnoreCase) &&
                tokens.Count is < 4 or > 5)
                errors.Add($"TXT-SNAPSHOT-015：EXTRACTSTRINGEX 参数数量无效（{sourceLocation}）。");

            if (section.Equals("ACT", StringComparison.Ordinal) &&
                command.Equals("TEXTSPLIT", StringComparison.OrdinalIgnoreCase) && tokens.Count != 5)
                errors.Add($"TXT-SNAPSHOT-015：TEXTSPLIT 参数数量无效（{sourceLocation}）。");

            if (section.Equals("ACT", StringComparison.Ordinal) &&
                command.Equals("TEXTLENGTH", StringComparison.OrdinalIgnoreCase) && tokens.Count != 3)
                errors.Add($"TXT-SNAPSHOT-015：TEXTLENGTH 参数数量无效（{sourceLocation}）。");

            if (section.Equals("ACT", StringComparison.Ordinal) &&
                command.Equals("SETSTRINGBLANK", StringComparison.OrdinalIgnoreCase) &&
                (tokens.Count != 4 ||
                 !int.TryParse(tokens.ElementAtOrDefault(2), out int blankLength) ||
                 blankLength is < 1 or > Server.MirObjects.NPCSegment.MaximumLingFengStringBlankLength ||
                 tokens.ElementAtOrDefault(3) is not ("0" or "1")))
                errors.Add($"TXT-SNAPSHOT-015：SETSTRINGBLANK 目标长度或补齐方向无效（{sourceLocation}）。");

            if (section.Equals("ACT", StringComparison.Ordinal) &&
                command.Equals("SKILLLEVEL", StringComparison.OrdinalIgnoreCase) &&
                (tokens.Count is < 4 or > 5 ||
                 tokens[2] is not ("+" or "-" or "=") ||
                 (tokens.Count == 5 && tokens[4] is not ("0" or "1"))))
                errors.Add($"TXT-SNAPSHOT-013：SKILLLEVEL 参数格式无效（{sourceLocation}）。");

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
                "GOTO" or "GROUPGOTO" or "LOOPGOTO" => 1,
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
            int arguments = value.IndexOf('(');
            if (arguments > 0 && value.EndsWith(")", StringComparison.Ordinal))
                value = value[..arguments];
            if (!value.StartsWith('@')) value = "@" + value;
            return $"[{value}]".ToUpperInvariant();
        }

        private static bool IsDynamicLabel(string label) =>
            !string.IsNullOrWhiteSpace(label) &&
            label.Contains("<$", StringComparison.Ordinal) &&
            label.Contains('>');

        private static bool TryResolveCallKey(string rawTarget, out string targetKey)
        {
            return LingFengScriptReferenceResolver.TryResolveCallKey(rawTarget, out targetKey);
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
