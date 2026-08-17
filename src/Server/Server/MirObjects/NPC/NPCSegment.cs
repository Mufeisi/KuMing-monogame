using System.Drawing;
﻿using Server.MirDatabase;
using Server.MirEnvir;
using System.Globalization;
using System.Text.RegularExpressions;
using Server.Scripting.Variables;
using Server.Scripting;
using Server.Scripting.ServerSymbols;
using S = ServerPackets;
using Timer = Server.MirEnvir.Timer;

namespace Server.MirObjects
{
    public class NPCSegment
    {
        protected static Envir Envir
        {
            get { return Envir.Main; }
        }

        protected static MessageQueue MessageQueue
        {
            get { return MessageQueue.Instance; }
        }

        private static bool IsLingFengCompatibility =>
            Settings.TxtScriptsCompatibilityVersion?.StartsWith("LFM2-", StringComparison.OrdinalIgnoreCase) == true;

        private const int MaximumLingFengWhileIterations = 10_000;
        internal const int MaximumLingFengStringBlankLength = 1_024;

        private static bool ShouldAllowLegacyTxtExecution()
        {
            var scriptsRuntimeActive = Settings.CSharpScriptsEnabled && Envir.CSharpScripts.Enabled;
            return !scriptsRuntimeActive || Settings.CSharpScriptsFallbackToTxt;
        }

        private static bool TryParseRuntimeVariableReference(
            string text,
            out ScriptVariableReference reference)
        {
            string source = (text ?? string.Empty).Trim();
            int selector = source.LastIndexOf('[');
            if (selector >= 0 && source.EndsWith("]", StringComparison.Ordinal))
                source = source.Substring(0, selector);
            return ScriptVariableReferenceParser.TryParse(source, out reference) &&
                   (reference.Scope == ScriptVariableScope.P ||
                    reference.Scope == ScriptVariableScope.D ||
                    reference.Scope == ScriptVariableScope.M ||
                    reference.Scope == ScriptVariableScope.N ||
                    reference.Scope == ScriptVariableScope.S ||
                    reference.Scope == ScriptVariableScope.I ||
                    reference.Scope == ScriptVariableScope.U ||
                    reference.Scope == ScriptVariableScope.T ||
                    reference.Scope == ScriptVariableScope.J ||
                    reference.Scope == ScriptVariableScope.Z ||
                    reference.Scope == ScriptVariableScope.G ||
                    reference.Scope == ScriptVariableScope.A ||
                    reference.Scope == ScriptVariableScope.Human ||
                    reference.Scope == ScriptVariableScope.Guild ||
                    reference.Scope == ScriptVariableScope.Global ||
                    reference.Scope == ScriptVariableScope.L ||
                    reference.Scope == ScriptVariableScope.Dict);
        }

        private bool TryFormatScriptVariable(PlayerObject player, string expression, out string text)
        {
            text = string.Empty;
            if (player == null || player.NPCObjectID == 0 || string.IsNullOrWhiteSpace(expression))
                return false;

            Match currentTarget = Regex.Match(
                expression, @"^C\.(STR|HUMAN|GUILD)\(([^,()]+)\)$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (currentTarget.Success)
            {
                PlayerObject target = player.ScriptVariableCurrentTarget;
                if (target == null || Envir.GetPlayer(target.Name) != target ||
                    target.CurrentMap != player.CurrentMap ||
                    !Functions.InRange(player.CurrentLocation, target.CurrentLocation, 20))
                    return false;
                string targetReference = currentTarget.Groups[1].Value.ToUpperInvariant() switch
                {
                    "HUMAN" => "HUMAN." + currentTarget.Groups[2].Value.Trim(),
                    "GUILD" => "GUILD." + currentTarget.Groups[2].Value.Trim(),
                    _ => currentTarget.Groups[2].Value.Trim()
                };
                if (!TryParseRuntimeVariableReference(targetReference, out _)) return false;
                ScriptVariableTextResult targetResult = Envir.CSharpScripts.VariableCommands.Format(
                    ScriptVariableContext.ForPlayer(target, target.CurrentMap), targetReference);
                if (!targetResult.Success)
                {
                    MessageQueue.Enqueue(
                        $"[Variables][TXT] C. 读取失败：{targetResult.ErrorCode} {targetResult.Diagnostic}，页码：{Key}");
                    return false;
                }
                text = targetResult.Text;
                return true;
            }

            Match str = Regex.Match(
                expression, @"^STR\(([^,()]+)\)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            Match format = Regex.Match(
                expression, @"^FORMAT\(([^,()]+),(\d+)\)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            string referenceText;
            int? digits;
            if (str.Success)
            {
                referenceText = str.Groups[1].Value.Trim();
                digits = null;
            }
            else if (format.Success && int.TryParse(
                         format.Groups[2].Value, NumberStyles.None,
                         CultureInfo.InvariantCulture, out var parsedDigits))
            {
                referenceText = format.Groups[1].Value.Trim();
                digits = parsedDigits;
            }
            else
            {
                return false;
            }

            if (!TryParseRuntimeVariableReference(referenceText, out _)) return false;
            var context = ScriptVariableContext.ForConversation(player, player.NPCObjectID, player.CurrentMap);
            ScriptVariableTextResult result = Envir.CSharpScripts.VariableCommands.Format(
                context, referenceText, digits);
            if (!result.Success)
            {
                MessageQueue.Enqueue(
                    $"[Variables][TXT] 显示失败：{result.ErrorCode} {result.Diagnostic}，页码：{Key}");
                return false;
            }

            text = result.Text;
            return true;
        }

        public NPCPage Page;

        public readonly string Key;
        public readonly string SourceKey;
        public bool MatchAnyCheck;
        public int RequiredCheckMatches;
        public List<NPCChecks> CheckList = new List<NPCChecks>();
        public List<NPCActions> ActList = new List<NPCActions>(), ElseActList = new List<NPCActions>();
        public List<string> Say, ElseSay, Buttons, ElseButtons, GotoButtons;

        public string Param1;
        public int Param1Instance, Param2, Param3;

        public List<string> Args = new List<string>();

        public NPCSegment(NPCPage page, List<string> say, List<string> buttons, List<string> elseSay, List<string> elseButtons, List<string> gotoButtons, string sourceKey = null)
        {
            Page = page;
            SourceKey = string.IsNullOrWhiteSpace(sourceKey)
                ? $"{page?.Key ?? "[UNKNOWN]"}|{Guid.NewGuid():N}"
                : sourceKey;

            Say = say;
            Buttons = buttons;

            ElseSay = elseSay;
            ElseButtons = elseButtons;

            GotoButtons = gotoButtons;
        }

        public string[] ParseArguments(string[] words)
        {
            Regex r = new Regex(@"\%ARG\((\d+)\)");

            for (int i = 0; i < words.Length; i++)
            {
                foreach (Match m in r.Matches(words[i].ToUpper()))
                {
                    if (!m.Success) continue;

                    int sequence = Convert.ToInt32(m.Groups[1].Value);

                    if (Page.Args.Count >= (sequence + 1)) words[i] = words[i].Replace(m.Groups[0].Value, Page.Args[sequence]);
                }
            }

            return words;
        }

        public void AddVariable(MapObject player, string key, string value)
        {
            if (player is PlayerObject playerObject &&
                TryParseRuntimeVariableReference(key, out var reference) &&
                reference.Scope == ScriptVariableScope.A)
            {
                var context = ScriptVariableContext.ForConversation(
                    playerObject, playerObject.NPCObjectID, playerObject.CurrentMap);
                ScriptVariableMutationResult result = Envir.CSharpScripts.VariableCommands.Mutate(
                    context, key, "MOV", value);
                if (!result.Success)
                    MessageQueue.Enqueue(
                        $"[Variables][TXT] LOADVALUE 写入 A 变量失败：{result.ErrorCode} {result.Diagnostic}，页码：{Key}");
                return;
            }

            Regex regex = new Regex(@"^[A-Za-z][0-9]+$", RegexOptions.CultureInvariant);

            if (!regex.Match(key).Success) return;

            for (int i = 0; i < player.NPCVar.Count; i++)
            {
                if (!String.Equals(player.NPCVar[i].Key, key, StringComparison.CurrentCultureIgnoreCase)) continue;
                player.NPCVar[i] = new KeyValuePair<string, string>(player.NPCVar[i].Key, value);
                return;
            }

            player.NPCVar.Add(new KeyValuePair<string, string>(key, value));
        }

        public string FindVariable(MapObject player, string key)
        {
            bool isVariableValue = (key ?? string.Empty).StartsWith("%", StringComparison.Ordinal);
            string runtimeKey = isVariableValue ? key.Substring(1) : string.Empty;
            if (isVariableValue && player is PlayerObject playerObject &&
                TryParseRuntimeVariableReference(runtimeKey, out var reference) &&
                reference.Scope == ScriptVariableScope.A)
            {
                var context = ScriptVariableContext.ForConversation(
                    playerObject, playerObject.NPCObjectID, playerObject.CurrentMap);
                ScriptVariableTextResult result = Envir.CSharpScripts.VariableCommands.Format(context, runtimeKey);
                if (result.Success) return result.Text;
                MessageQueue.Enqueue(
                    $"[Variables][TXT] 读取 A 变量失败：{result.ErrorCode} {result.Diagnostic}，页码：{Key}");
                return key;
            }

            Regex regex = new Regex(@"^\%[A-Za-z][0-9]+$", RegexOptions.CultureInvariant);

            if (!regex.Match(key).Success) return key;

            string tempKey = key.Substring(1);

            foreach (KeyValuePair<string, string> t in player.NPCVar)
            {
                if (String.Equals(t.Key, tempKey, StringComparison.CurrentCultureIgnoreCase)) return t.Value;
            }

            return key;
        }

        public void ParseCheck(string line)
        {
            if (!TxtScriptTokenizer.TryTokenize(line, out string[] parts, out string tokenError))
                throw new InvalidDataException($"检测命令参数无效：{tokenError} 原文={line}");

            parts = ParseArguments(parts);

            if (parts.Length == 0) return;

            bool negated = false;
            if (IsLingFengCompatibility &&
                (parts[0].Equals("NOT", StringComparison.OrdinalIgnoreCase) || parts[0] == "!"))
            {
                if (parts.Length < 2)
                    throw new InvalidDataException("NOT/! 后必须提供完整检测命令。");
                negated = true;
                parts = parts.Skip(1).ToArray();
            }
            else if (IsLingFengCompatibility && parts[0].StartsWith('!') && parts[0].Length > 1)
            {
                negated = true;
                parts[0] = parts[0].Substring(1);
            }

            string tempString, tempString2;

            var regexFlag = new Regex(@"\[(.*?)\]");
            var regexQuote = new Regex("\"([^\"]*)\"");

            Match quoteMatch;
            int originalCheckCount = CheckList.Count;

            switch (parts[0].ToUpper())
            {
                case "M.EQUAL" when IsLingFengCompatibility:
                case "M.LARGE" when IsLingFengCompatibility:
                case "M.SMALL" when IsLingFengCompatibility:
                    if (parts.Length != 3 ||
                        !TryParseRuntimeVariableReference(parts[1], out _))
                        throw new InvalidDataException(
                            $"{parts[0].ToUpperInvariant()} 需要目标变量和比较值。" );
                    CheckList.Add(new NPCChecks(
                        CheckType.LingFengTargetVariable,
                        parts[1],
                        parts[0].EndsWith("EQUAL", StringComparison.OrdinalIgnoreCase) ? "=" :
                        parts[0].EndsWith("LARGE", StringComparison.OrdinalIgnoreCase) ? ">" : "<",
                        parts[2]));
                    break;

                case "EQUAL" when IsLingFengCompatibility:
                case "LARGE" when IsLingFengCompatibility:
                case "SMALL" when IsLingFengCompatibility:
                    if (parts.Length != 3)
                        throw new InvalidDataException($"{parts[0].ToUpperInvariant()} 需要两个比较参数。");
                    CheckList.Add(new NPCChecks(
                        CheckType.LingFengCompare,
                        parts[0].ToUpperInvariant(), parts[1], parts[2]));
                    break;

                case "CHECKCONTAINSTEXT" when IsLingFengCompatibility:
                    if (parts.Length != 3)
                        throw new InvalidDataException("CHECKCONTAINSTEXT 需要待检文本和包含文本两个参数。");
                    CheckList.Add(new NPCChecks(CheckType.LingFengContainsText, parts[1], parts[2]));
                    break;

                case "CHECKMAGICNAME" when IsLingFengCompatibility:
                    if (parts.Length != 2)
                        throw new InvalidDataException("CHECKMAGICNAME 需要一个技能名称。");
                    CheckList.Add(new NPCChecks(CheckType.LingFengMagicName, parts[1]));
                    break;

                case "CHECKSKILL" when IsLingFengCompatibility:
                    if (parts.Length is < 4 or > 5 ||
                        parts[2] is not (">" or "<" or "=") ||
                        (parts.Length == 5 && parts[4] is not ("0" or "1")))
                        throw new InvalidDataException(
                            "CHECKSKILL 需要技能名称、>/< /=、等级和可选的普通0或强化1标志。");
                    CheckList.Add(new NPCChecks(
                        CheckType.LingFengSkillLevel, parts[1], parts[2], parts[3],
                        parts.Length == 5 ? parts[4] : "0"));
                    break;

                case "CHECKBAGSIZE" when IsLingFengCompatibility:
                    if (parts.Length != 2 ||
                        !int.TryParse(parts[1], NumberStyles.None,
                            CultureInfo.InvariantCulture, out int requiredBagSlots) ||
                        requiredBagSlots < 0)
                        throw new InvalidDataException("CHECKBAGSIZE 需要非负空格数量。");
                    CheckList.Add(new NPCChecks(CheckType.LingFengBagSize, parts[1]));
                    break;

                case "CHECKBAGGAGE" when IsLingFengCompatibility:
                    if (parts.Length != 1)
                        throw new InvalidDataException("CHECKBAGGAGE 不接受参数。");
                    CheckList.Add(new NPCChecks(CheckType.LingFengBagSize, "1"));
                    break;

                case "CHECKHAVEHERO" when IsLingFengCompatibility:
                    if (parts.Length != 1)
                        throw new InvalidDataException("CHECKHAVEHERO 不接受参数。");
                    CheckList.Add(new NPCChecks(CheckType.LingFengHaveHero));
                    break;

                case "CHECKHEROONLINE" when IsLingFengCompatibility:
                    if (parts.Length != 1)
                        throw new InvalidDataException("CHECKHEROONLINE 不接受参数。");
                    CheckList.Add(new NPCChecks(CheckType.LingFengHeroOnline));
                    break;

                case "CHECKTEXTLIST" when IsLingFengCompatibility:
                case "CHECKCACHETEXTLIST" when IsLingFengCompatibility:
                    if (parts.Length is < 3 or > 6 ||
                        (parts.Length >= 5 && parts[4] is not ("0" or "1")) ||
                        (parts.Length == 6 && parts[5] is not ("0" or "1")))
                        throw new InvalidDataException($"{parts[0].ToUpperInvariant()} 参数格式无效。");
                    CheckList.Add(new NPCChecks(
                        CheckType.LingFengTextList,
                        parts[1], parts[2], parts.Length >= 4 ? parts[3] : string.Empty,
                        parts.Length >= 5 ? parts[4] : "0", parts.Length >= 6 ? parts[5] : "0"));
                    break;

                case "GETSTRINGPOSEX" when IsLingFengCompatibility:
                    if (parts.Length is not (6 or 7) ||
                        !TryNormalizeWritableDestination(parts[3], out string positionDestination) ||
                        !TryNormalizeWritableDestination(parts[4], out string lineDestination) ||
                        parts[5] is not ("0" or "1") ||
                        (parts.Length == 7 && parts[6] is not ("0" or "1")))
                        throw new InvalidDataException("GETSTRINGPOSEX 参数格式无效。");
                    CheckList.Add(new NPCChecks(
                        CheckType.LingFengStringPosition,
                        parts[1], parts[2], positionDestination, lineDestination,
                        parts[5], parts.Length == 7 ? parts[6] : "0"));
                    break;

                case "CHECKKILLBYHUM" when IsLingFengCompatibility:
                case "KILLBYHUM" when IsLingFengCompatibility:
                    if (parts.Length != 1)
                        throw new InvalidDataException($"{parts[0]} 不接受参数。");
                    CheckList.Add(new NPCChecks(CheckType.LingFengKilledByHuman));
                    break;

                case "CHECKATTACKMODE" when IsLingFengCompatibility:
                    if (parts.Length != 2 || parts[1] is not ("0" or "1"))
                        throw new InvalidDataException("CHECKATTACKMODE 当前仅支持命格使用的 0（全体）与 1（和平）。");
                    CheckList.Add(new NPCChecks(CheckType.LingFengAttackMode, parts[1]));
                    break;

                case "CHECKONLINE" when IsLingFengCompatibility:
                    if (parts.Length != 2)
                        throw new InvalidDataException("CHECKONLINE 需要人物名称。");
                    CheckList.Add(new NPCChecks(CheckType.LingFengOnline, parts[1]));
                    break;

                case "CHECKSTRINGLENGTH" when IsLingFengCompatibility:
                    if (parts.Length != 4 || parts[2] is not ("<" or ">" or "=" or "==" or "<=" or ">=") ||
                        !int.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture,
                            out int expectedLength) || expectedLength < 0)
                        throw new InvalidDataException("CHECKSTRINGLENGTH 参数格式无效。");
                    CheckList.Add(new NPCChecks(
                        CheckType.LingFengStringLength, parts[1], parts[2], parts[3]));
                    break;

                case "L.CHECKJOB" when IsLingFengCompatibility:
                    if (parts.Length != 2 || !Enum.TryParse<MirClass>(parts[1], true, out _))
                        throw new InvalidDataException("L.CHECKJOB 需要有效职业名称。");
                    CheckList.Add(new NPCChecks(CheckType.LingFengLastActorClass, parts[1]));
                    break;

                case "L.CHECKLEVELEX" when IsLingFengCompatibility:
                    if (parts.Length != 3 || parts[1] is not ("<" or ">" or "=" or "==" or "<=" or ">=") ||
                        !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture,
                            out int lastActorLevel) || lastActorLevel < 0)
                        throw new InvalidDataException("L.CHECKLEVELEX 参数格式无效。");
                    CheckList.Add(new NPCChecks(
                        CheckType.LingFengLastActorLevel, parts[1], parts[2]));
                    break;

                case "M.CHECKLEVELEX" when IsLingFengCompatibility:
                    if (parts.Length != 3 || parts[1] is not ("<" or ">" or "=" or "==" or "<=" or ">=") ||
                        !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture,
                            out int targetLevel) || targetLevel < 0)
                        throw new InvalidDataException("M.CHECKLEVELEX 参数格式无效。");
                    CheckList.Add(new NPCChecks(
                        CheckType.LingFengTargetLevel, parts[1], parts[2]));
                    break;

                case "M.CHECKHPPER" when IsLingFengCompatibility:
                    if (parts.Length != 3 || parts[1] is not ("<" or ">" or "=" or "==" or "<=" or ">=") ||
                        !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture,
                            out int targetHpPercent) || targetHpPercent is < 0 or > 100)
                        throw new InvalidDataException("M.CHECKHPPER 参数格式无效。");
                    CheckList.Add(new NPCChecks(
                        CheckType.LingFengTargetResourcePercent, parts[1], parts[2]));
                    break;

                case "INSAFEZONE" when IsLingFengCompatibility:
                    if (parts.Length != 1)
                        throw new InvalidDataException("INSAFEZONE 不接受参数。");
                    CheckList.Add(new NPCChecks(CheckType.LingFengInSafeZone));
                    break;

                case "CHECKRANGEMONCOUNT" when IsLingFengCompatibility:
                    if (parts.Length != 7 ||
                        !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out int rangeX) ||
                        !int.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out int rangeY) ||
                        !int.TryParse(parts[4], NumberStyles.None, CultureInfo.InvariantCulture, out int rangeRadius) ||
                        rangeX < 0 || rangeY < 0 || rangeRadius < 0 ||
                        parts[5] is not ("<" or ">" or "=" or "==" or "<=" or ">=") ||
                        !int.TryParse(parts[6], NumberStyles.None, CultureInfo.InvariantCulture, out int rangeCount) ||
                        rangeCount < 0)
                        throw new InvalidDataException("CHECKRANGEMONCOUNT 参数格式无效。");
                    CheckList.Add(new NPCChecks(
                        CheckType.LingFengRangeMonsterCount, parts.Skip(1).ToArray()));
                    break;

                case "CHECKRANGEHUMCOUNT" when IsLingFengCompatibility:
                    if (parts.Length != 7 ||
                        !int.TryParse(parts[2], NumberStyles.None,
                            CultureInfo.InvariantCulture, out int humanRangeX) || humanRangeX < 0 ||
                        !int.TryParse(parts[3], NumberStyles.None,
                            CultureInfo.InvariantCulture, out int humanRangeY) || humanRangeY < 0 ||
                        !int.TryParse(parts[4], NumberStyles.None,
                            CultureInfo.InvariantCulture, out int humanRange) || humanRange < 0 ||
                        parts[5] is not ("<" or ">" or "=" or "==" or "<=" or ">=") ||
                        !int.TryParse(parts[6], NumberStyles.None,
                            CultureInfo.InvariantCulture, out int expectedHumans) || expectedHumans < 0)
                        throw new InvalidDataException("CHECKRANGEHUMCOUNT 参数格式无效。" );
                    CheckList.Add(new NPCChecks(
                        CheckType.LingFengRangeHumanCount, parts.Skip(1).ToArray()));
                    break;

                case "CHECKMAPSAMEMONCOUNT" when IsLingFengCompatibility:
                    if (parts.Length is not (5 or 6) ||
                        parts[3] is not ("<" or ">" or "=" or "==" or "<=" or ">=") ||
                        !int.TryParse(parts[4], NumberStyles.None, CultureInfo.InvariantCulture, out int sameCount) ||
                        sameCount < 0 || (parts.Length == 6 && parts[5] is not ("0" or "1")))
                        throw new InvalidDataException("CHECKMAPSAMEMONCOUNT 参数格式无效。");
                    CheckList.Add(new NPCChecks(
                        CheckType.LingFengMapSameMonsterCount,
                        parts[1], parts[2], parts[3], parts[4], parts.Length == 6 ? parts[5] : "0"));
                    break;

                case "CHECKMAPMONCOUNT" when IsLingFengCompatibility:
                    if (parts.Length is not (4 or 5) ||
                        parts[2] is not ("<" or ">" or "=" or "==" or "<=" or ">=") ||
                        !int.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out int mapMonsterCount) ||
                        mapMonsterCount < 0 || (parts.Length == 5 && parts[4] is not ("0" or "1")))
                        throw new InvalidDataException("CHECKMAPMONCOUNT 参数格式无效。");
                    CheckList.Add(new NPCChecks(
                        CheckType.LingFengMapMonsterCount,
                        parts[1], parts[2], parts[3], parts.Length == 5 ? parts[4] : "0"));
                    break;

                case "CHECKMAPHUMANCOUNT" when IsLingFengCompatibility:
                    if (parts.Length != 4 ||
                        parts[2] is not ("<" or ">" or "=" or "==" or "<=" or ">=") ||
                        !int.TryParse(parts[3], NumberStyles.None,
                            CultureInfo.InvariantCulture, out int mapHumanCount) || mapHumanCount < 0)
                        throw new InvalidDataException("CHECKMAPHUMANCOUNT 参数格式无效。" );
                    CheckList.Add(new NPCChecks(
                        CheckType.LingFengMapHumanCount, parts[1], parts[2], parts[3]));
                    break;

                case "CHECKMONMAP" when IsLingFengCompatibility:
                    if (parts.Length is not (3 or 4) ||
                        !int.TryParse(parts[2], NumberStyles.None,
                            CultureInfo.InvariantCulture, out int minimumMonsterCount) ||
                        minimumMonsterCount < 0 ||
                        parts.Length == 4 && parts[3] is not ("0" or "1"))
                        throw new InvalidDataException("CHECKMONMAP 参数格式无效。" );
                    CheckList.Add(new NPCChecks(
                        CheckType.LingFengMapMonsterMinimum, parts[1], parts[2],
                        parts.Length == 4 ? parts[3] : "0"));
                    break;

                case "CHECKMARRY" when IsLingFengCompatibility:
                case "P.CHECKMARRY" when IsLingFengCompatibility:
                    if (parts.Length != 1)
                        throw new InvalidDataException($"{parts[0].ToUpperInvariant()} 不接受参数。");
                    CheckList.Add(new NPCChecks(
                        parts[0].StartsWith("P.", StringComparison.OrdinalIgnoreCase)
                            ? CheckType.LingFengTargetMarried
                            : CheckType.LingFengMarried));
                    break;

                case "CHECKPOSEMARRY" when IsLingFengCompatibility:
                    if (parts.Length != 1)
                        throw new InvalidDataException("CHECKPOSEMARRY 不接受参数。" );
                    CheckList.Add(new NPCChecks(CheckType.LingFengPoseMarried));
                    break;

                case "P.GENDER" when IsLingFengCompatibility:
                    if (parts.Length > 2 ||
                        (parts.Length == 2 && parts[1] is not ("MAN" or "Man" or "man" or "WOMAN" or "Woman" or "woman")))
                        throw new InvalidDataException("P.GENDER 只允许可选的 MAN/WOMAN 参数。");
                    CheckList.Add(new NPCChecks(
                        CheckType.LingFengTargetGender,
                        parts.Length == 1 ? "Female" : parts[1]));
                    break;

                case "CHECKPOSEGENDER" when IsLingFengCompatibility:
                    if (parts.Length != 2 ||
                        !parts[1].Equals("MAN", StringComparison.OrdinalIgnoreCase) &&
                        !parts[1].Equals("男", StringComparison.OrdinalIgnoreCase) &&
                        !parts[1].Equals("WOMAN", StringComparison.OrdinalIgnoreCase) &&
                        !parts[1].Equals("女", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException(
                            "CHECKPOSEGENDER 只允许 MAN/男/WOMAN/女参数。" );
                    CheckList.Add(new NPCChecks(CheckType.LingFengPoseGender, parts[1]));
                    break;

                case "CHECKCURRTARGETRACE" when IsLingFengCompatibility:
                    if (parts.Length != 3 || parts[1] != "=" ||
                        parts[2] is not ("0" or "1" or "151"))
                        throw new InvalidDataException(
                            "CHECKCURRTARGETRACE 当前仅支持命格使用的人物、英雄与宝宝类型。");
                    CheckList.Add(new NPCChecks(CheckType.LingFengCurrentTargetRace, parts[2]));
                    break;

                case "CHECKCURRTARGETSLAVE" when IsLingFengCompatibility:
                    if (parts.Length != 1)
                        throw new InvalidDataException("CHECKCURRTARGETSLAVE 不接受参数。");
                    CheckList.Add(new NPCChecks(CheckType.LingFengCurrentTargetSlave));
                    break;

                case "CHECKGAMEGOLD" when IsLingFengCompatibility:
                    if (parts.Length != 3 || parts[1] is not ("?" or "<" or ">" or "=" or "==" or "<=" or ">=") ||
                        !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture,
                            out int expectedGameGold) || expectedGameGold < 0)
                        throw new InvalidDataException("CHECKGAMEGOLD 参数格式无效。");
                    CheckList.Add(new NPCChecks(CheckType.LingFengGameGold,
                        parts[1] == "?" ? ">=" : parts[1], parts[2]));
                    break;

                case "CHECKGAMEPOINT" when IsLingFengCompatibility:
                    if (parts.Length != 3 ||
                        parts[1] is not ("?" or "<" or ">" or "=" or "==" or "<=" or ">="))
                        throw new InvalidDataException("CHECKGAMEPOINT 参数格式无效。");
                    CheckList.Add(new NPCChecks(CheckType.LingFengGamePoint,
                        parts[1] == "?" ? ">=" : parts[1], parts[2]));
                    break;

                case "CHECKGAMEDIAMOND" when IsLingFengCompatibility:
                    if (parts.Length != 3 ||
                        parts[1] is not ("?" or "<" or ">" or "=" or "==" or "<=" or ">="))
                        throw new InvalidDataException("CHECKGAMEDIAMOND 参数格式无效。");
                    CheckList.Add(new NPCChecks(CheckType.LingFengGameDiamond,
                        parts[1] == "?" ? ">=" : parts[1], parts[2]));
                    break;

                case "CHECKGAMEGIRD" when IsLingFengCompatibility:
                    if (parts.Length != 3 || parts[1] is not ("?" or "<" or ">" or "=" or "==" or "<=" or ">="))
                        throw new InvalidDataException("CHECKGAMEGIRD 参数格式无效。");
                    CheckList.Add(new NPCChecks(CheckType.LingFengGameGird,
                        parts[1] == "?" ? ">=" : parts[1], parts[2]));
                    break;

                case "CHECKUSEITEM" when IsLingFengCompatibility:
                case "H.CHECKUSEITEM" when IsLingFengCompatibility:
                    if (parts.Length != 2 ||
                        !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture,
                            out int useItemPosition) ||
                        !TryMapLingFengEquipmentPosition(useItemPosition, out _))
                        throw new InvalidDataException(
                            $"{parts[0]} 当前仅支持已映射的 0 至 13 装备位置。");
                    CheckList.Add(new NPCChecks(
                        CheckType.LingFengUseItem,
                        parts[0].StartsWith("H.", StringComparison.OrdinalIgnoreCase) ? "1" : "0",
                        parts[1]));
                    break;

                case "CHECKSTORAGEOPEN" when IsLingFengCompatibility:
                    if (parts.Length != 2 ||
                        !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture,
                            out int storagePage) || storagePage is < 2 or > 4)
                        throw new InvalidDataException("CHECKSTORAGEOPEN 仅接受仓库页 2 至 4。" );
                    CheckList.Add(new NPCChecks(CheckType.LingFengStorageOpen, parts[1]));
                    break;

                case "CHECKCUSTOMITEMVALUE" when IsLingFengCompatibility:
                case "H.CHECKCUSTOMITEMVALUE" when IsLingFengCompatibility:
                    if (parts.Length is not (5 or 6) ||
                        parts[3] is not ("<" or ">" or "=" or "==" or "<=" or ">=") ||
                        (parts.Length == 6 && parts[5] is not ("0" or "1" or "2")))
                        throw new InvalidDataException(
                            "CHECKCUSTOMITEMVALUE 需要装备位置、属性位置、比较符、值和可选值位置。" );
                    CheckList.Add(new NPCChecks(
                        CheckType.LingFengCustomItemValue,
                        parts[0].StartsWith("H.", StringComparison.OrdinalIgnoreCase) ? "1" : "0",
                        parts[1], parts[2], parts[3], parts[4],
                        parts.Length == 6 ? parts[5] : "0"));
                    break;

                case "CHECKITEMADDVALUE" when IsLingFengCompatibility:
                case "H.CHECKITEMADDVALUE" when IsLingFengCompatibility:
                    if (parts.Length != 5 ||
                        parts[3] is not ("<" or ">" or "=" or "==" or "<=" or ">=") ||
                        !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture,
                            out int checkAddedAttribute) || checkAddedAttribute is < 0 or > 14)
                        throw new InvalidDataException(
                            "CHECKITEMADDVALUE 需要装备位置、属性序号、比较符和值。" );
                    CheckList.Add(new NPCChecks(
                        CheckType.LingFengItemAddedValue,
                        parts[0].StartsWith("H.", StringComparison.OrdinalIgnoreCase) ? "1" : "0",
                        parts[1], parts[2], parts[3], parts[4]));
                    break;

                case "CHECKITEMNAMECOLOR" when IsLingFengCompatibility:
                case "H.CHECKITEMNAMECOLOR" when IsLingFengCompatibility:
                    if (parts.Length != 3 ||
                        !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture,
                            out int nameColourPosition) || nameColourPosition is < 0 or > 13 ||
                        !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture,
                            out int expectedNameColour) || expectedNameColour is < 0 or > 255)
                        throw new InvalidDataException(
                            "CHECKITEMNAMECOLOR 需要 0-13 的装备位置和 0-255 的颜色编号。" );
                    CheckList.Add(new NPCChecks(
                        CheckType.LingFengItemNameColour,
                        parts[0].StartsWith("H.", StringComparison.OrdinalIgnoreCase) ? "1" : "0",
                        parts[1], parts[2]));
                    break;

                case "CHECKUPGRADECOUNT" when IsLingFengCompatibility:
                case "H.CHECKUPGRADECOUNT" when IsLingFengCompatibility:
                    if (parts.Length != 4 ||
                        !int.TryParse(parts[1], NumberStyles.None,
                            CultureInfo.InvariantCulture, out int upgradePosition) ||
                        upgradePosition is < 0 or > 13 ||
                        parts[2] is not ("<" or ">" or "=" or "==" or "<=" or ">=") ||
                        !byte.TryParse(parts[3], NumberStyles.None,
                            CultureInfo.InvariantCulture, out _))
                        throw new InvalidDataException(
                            "CHECKUPGRADECOUNT 需要 0-13 的装备位置、比较符和 0-255 的星数。" );
                    CheckList.Add(new NPCChecks(
                        CheckType.LingFengItemUpgradeCount,
                        parts[0].StartsWith("H.", StringComparison.OrdinalIgnoreCase) ? "1" : "0",
                        parts[1], parts[2], parts[3]));
                    break;

                case "CHECKCUSTOMITEMPROGRESSBARVALUE" when IsLingFengCompatibility:
                case "H.CHECKCUSTOMITEMPROGRESSBARVALUE" when IsLingFengCompatibility:
                    if (parts.Length is not (4 or 6) ||
                        !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture,
                            out int checkProgressIndex) ||
                        checkProgressIndex is < 0 or >= UserItem.LingFengCustomProgressBarLimit ||
                        !int.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture,
                            out int checkProgressValueKind) || checkProgressValueKind is < 0 or > 2 ||
                        (parts.Length == 6 &&
                         parts[4] is not ("<" or ">" or "=" or "==" or "<=" or ">=")))
                        throw new InvalidDataException(
                            "CHECKCUSTOMITEMPROGRESSBARVALUE 需要装备位置、进度条序号、值类型及可选比较符和值。" );
                    CheckList.Add(new NPCChecks(
                        CheckType.LingFengCustomItemProgressBarValue,
                        parts[0].StartsWith("H.", StringComparison.OrdinalIgnoreCase) ? "1" : "0",
                        parts[1], parts[2], parts[3],
                        parts.Length == 6 ? parts[4] : string.Empty,
                        parts.Length == 6 ? parts[5] : string.Empty));
                    break;

                case "CHECKITEMW" when IsLingFengCompatibility:
                    if (parts.Length is < 2 or > 3 ||
                        (parts.Length == 3 &&
                         (!ushort.TryParse(parts[2], NumberStyles.None,
                             CultureInfo.InvariantCulture, out ushort equippedCount) ||
                          equippedCount == 0)))
                        throw new InvalidDataException(
                            "CHECKITEMW 需要装备名称和可选的正整数数量。");
                    CheckList.Add(new NPCChecks(
                        CheckType.LingFengEquippedItem, parts[1],
                        parts.Length == 3 ? parts[2] : "1"));
                    break;

                case "CHECKREPAIRALLGOLD" when IsLingFengCompatibility:
                    if (parts.Length != 2 ||
                        !TryNormalizeWritableDestination(parts[1], out string repairCostDestination))
                        throw new InvalidDataException(
                            "CHECKREPAIRALLGOLD 需要一个可写数值变量用于返回特修总价。");
                    CheckList.Add(new NPCChecks(
                        CheckType.LingFengRepairAllGold, repairCostDestination));
                    break;

                case "P.CHECKSHIELDSTATEOPEN" when IsLingFengCompatibility:
                    if (parts.Length != 2 || parts[1] != "1")
                        throw new InvalidDataException(
                            "P.CHECKSHIELDSTATEOPEN 当前仅支持检测目标魔法盾。");
                    CheckList.Add(new NPCChecks(CheckType.LingFengTargetShieldOpen));
                    break;

                case "CHECKBATTLESTATUS" when IsLingFengCompatibility:
                    if (parts.Length != 1)
                        throw new InvalidDataException("CHECKBATTLESTATUS 不接受参数。");
                    CheckList.Add(new NPCChecks(CheckType.LingFengBattleStatus));
                    break;

                case "CHECKUNDERWAR" when IsLingFengCompatibility:
                    if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[1]))
                        throw new InvalidDataException("CHECKUNDERWAR 需要城堡名称。");
                    CheckList.Add(new NPCChecks(CheckType.LingFengCastleUnderWar, parts[1]));
                    break;

                case "CHECKPOSEDIR" when IsLingFengCompatibility:
                    if (parts.Length is < 1 or > 2 ||
                        parts.Length == 2 && parts[1] is not ("1" or "2"))
                        throw new InvalidDataException(
                            "CHECKPOSEDIR 只允许可选的1同性交互或2异性交互。");
                    CheckList.Add(new NPCChecks(CheckType.LingFengPoseDirection,
                        parts.Length == 2 ? parts[1] : "0"));
                    break;

                case "CHECKPOSELEVEL" when IsLingFengCompatibility:
                    if (parts.Length != 3 || parts[1] is not ("=" or ">" or "<") ||
                        !ushort.TryParse(parts[2], NumberStyles.None,
                            CultureInfo.InvariantCulture, out ushort poseLevel) || poseLevel == 0)
                        throw new InvalidDataException(
                            "CHECKPOSELEVEL 需要比较符和1至65535等级。");
                    CheckList.Add(new NPCChecks(CheckType.LingFengPoseLevel,
                        parts[1], parts[2]));
                    break;

                case "ISCASTLEGUILD" when IsLingFengCompatibility:
                    if (parts.Length != 1)
                        throw new InvalidDataException("ISCASTLEGUILD 不接受参数。");
                    CheckList.Add(new NPCChecks(CheckType.LingFengCastleGuild));
                    break;

                case "ISCASTLEMASTER" when IsLingFengCompatibility:
                    if (parts.Length != 1)
                        throw new InvalidDataException("ISCASTLEMASTER 不接受参数。");
                    CheckList.Add(new NPCChecks(CheckType.LingFengCastleMaster));
                    break;

                case "HAVEMASTER" when IsLingFengCompatibility:
                case "CHECKMASTER" when IsLingFengCompatibility:
                    if (parts.Length != 1)
                        throw new InvalidDataException("HAVEMASTER/CHECKMASTER 不接受参数。");
                    CheckList.Add(new NPCChecks(CheckType.LingFengHaveMentor));
                    break;

                case "CHECKISMASTER" when IsLingFengCompatibility:
                    if (parts.Length != 1)
                        throw new InvalidDataException("CHECKISMASTER 不接受参数。");
                    CheckList.Add(new NPCChecks(CheckType.LingFengIsMentor));
                    break;

                case "CHECKPOSEMASTER" when IsLingFengCompatibility:
                    if (parts.Length != 1)
                        throw new InvalidDataException("CHECKPOSEMASTER 不接受参数。");
                    CheckList.Add(new NPCChecks(CheckType.LingFengPoseMentor));
                    break;

                case "M.ISCASTLEGUILD" when IsLingFengCompatibility:
                    if (parts.Length != 1)
                        throw new InvalidDataException("M.ISCASTLEGUILD 不接受参数。");
                    CheckList.Add(new NPCChecks(CheckType.LingFengTargetCastleGuild));
                    break;

                case "CHECKSTATEVALUE" when IsLingFengCompatibility:
                case "M.CHECKSTATEVALUE" when IsLingFengCompatibility:
                    if (parts.Length != 2 || parts[1] is not ("0" or "1" or "2" or "3"))
                        throw new InvalidDataException(
                            $"{parts[0].ToUpperInvariant()} 当前仅支持命格使用的绿毒、红毒、麻痹、冰冻状态。");
                    CheckList.Add(new NPCChecks(
                        parts[0].StartsWith("M.", StringComparison.OrdinalIgnoreCase)
                            ? CheckType.LingFengTargetStateValue
                            : CheckType.LingFengStateValue,
                        parts[1]));
                    break;

                case "CHECKSCRIPTPARAM" when IsLingFengCompatibility:
                    if (parts.Length != 2)
                        throw new InvalidDataException("CHECKSCRIPTPARAM 需要一个逗号分隔的参数组。");
                    string[] expectedParameters = parts[1].Split(',', StringSplitOptions.None);
                    if (expectedParameters.Length == 0 || expectedParameters.Any(string.IsNullOrEmpty))
                        throw new InvalidDataException("CHECKSCRIPTPARAM 不允许空参数。");
                    CheckList.Add(new NPCChecks(CheckType.LingFengScriptParameters, expectedParameters));
                    break;

                case "CHECKRENEWLEVEL" when IsLingFengCompatibility:
                case "H.CHECKRENEWLEVEL" when IsLingFengCompatibility:
                    if (parts.Length != 3 || parts[1] is not ("=" or ">" or "<") ||
                        !byte.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out _))
                        throw new InvalidDataException(
                            $"{parts[0].ToUpperInvariant()} 需要操作符(=、>、<)和0至255的转生等级。");
                    CheckList.Add(new NPCChecks(
                        CheckType.LingFengRenewLevel,
                        parts[0].StartsWith("H.", StringComparison.OrdinalIgnoreCase) ? "H" : "SELF",
                        parts[1], parts[2]));
                    break;

                case "CHECKFENGHAO" when IsLingFengCompatibility:
                case "H.CHECKFENGHAO" when IsLingFengCompatibility:
                    if (parts.Length < 2)
                        throw new InvalidDataException($"{parts[0].ToUpperInvariant()} 需要称号名称。");
                    CheckList.Add(new NPCChecks(
                        CheckType.LingFengFengHao,
                        parts[0].StartsWith("H.", StringComparison.OrdinalIgnoreCase) ? "H" : "SELF",
                        string.Join(" ", parts.Skip(1))));
                    break;

                case "CHECKACTIVEFENGHAO" when IsLingFengCompatibility:
                case "H.CHECKACTIVEFENGHAO" when IsLingFengCompatibility:
                    if (parts.Length < 2)
                        throw new InvalidDataException(
                            $"{parts[0].ToUpperInvariant()} 需要称号名称。");
                    CheckList.Add(new NPCChecks(
                        CheckType.LingFengActiveFengHao,
                        parts[0].StartsWith("H.", StringComparison.OrdinalIgnoreCase) ? "H" : "SELF",
                        string.Join(" ", parts.Skip(1))));
                    break;

                case "CHECKSLAVECOUNT" when IsLingFengCompatibility:
                    if (parts.Length is < 3 or > 5 || parts[1] is not ("=" or ">" or "<") ||
                        !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture,
                            out int requiredSlaveCount) || requiredSlaveCount < 0 ||
                        parts.Length == 5 && parts[4] is not ("0" or "1"))
                        throw new InvalidDataException(
                            "CHECKSLAVECOUNT 需要比较符、数量和可选宝宝名称、是否检查名称数字。");
                    CheckList.Add(new NPCChecks(CheckType.LingFengSlaveCount,
                        parts[1] == "=" ? "==" : parts[1], parts[2],
                        parts.Length >= 4 ? parts[3] : string.Empty,
                        parts.Length == 5 ? parts[4] : "1"));
                    break;

                case "CHECKMIRRORMAP" when IsLingFengCompatibility:
                    if (parts.Length != 2)
                        throw new InvalidDataException("CHECKMIRRORMAP 需要镜像地图编号。");
                    CheckList.Add(new NPCChecks(CheckType.LingFengMirrorMap, parts[1]));
                    break;

                case "CANMOVEECTYPE" when IsLingFengCompatibility:
                    if (parts.Length != 2)
                        throw new InvalidDataException("CANMOVEECTYPE 需要副本名称。");
                    CheckList.Add(new NPCChecks(CheckType.LingFengCanMoveEctype, parts[1]));
                    break;

                case "FINDMONPOINT" when IsLingFengCompatibility:
                    if (parts.Length != 5)
                        throw new InvalidDataException("FINDMONPOINT 需要地图、怪物名称、X变量、Y变量四个参数。");
                    if (!IsWritableScriptVariable(parts[3]) || !IsWritableScriptVariable(parts[4]))
                        throw new InvalidDataException("FINDMONPOINT 的坐标结果必须写入有效脚本变量。");
                    CheckList.Add(new NPCChecks(
                        CheckType.LingFengFindMonsterPoint, parts[1], parts[2], parts[3], parts[4]));
                    break;

                case "LEVEL":
                case "CHECKLEVEL":
                case "CHECKLEVELEX" when IsLingFengCompatibility:
                    if (parts.Length < 3) return;

                    CheckList.Add(new NPCChecks(CheckType.Level, parts[1], parts[2]));
                    break;

                case "CHECKHPPER" when IsLingFengCompatibility:
                case "CHECKMPPER" when IsLingFengCompatibility:
                    if (parts.Length is not (3 or 4))
                        throw new InvalidDataException($"{parts[0].ToUpperInvariant()} 需要操作符、比例值和可选比例类型。");
                    CheckList.Add(new NPCChecks(
                        CheckType.LingFengResourcePercent,
                        parts[0].Equals("CHECKHPPER", StringComparison.OrdinalIgnoreCase) ? "HP" : "MP",
                        parts[1], parts[2], parts.Length == 4 ? parts[3] : "0"));
                    break;

                case "CHECKEXP":
                case "CHECKHP":
                case "CHECKMP":
                    if (parts.Length is not (3 or 5)) return;
                    CheckType numericType = parts[0].Equals("CHECKEXP", StringComparison.OrdinalIgnoreCase)
                        ? CheckType.CheckExperience
                        : parts[0].Equals("CHECKHP", StringComparison.OrdinalIgnoreCase)
                            ? CheckType.CheckHP
                            : CheckType.CheckMP;
                    CheckList.Add(new NPCChecks(numericType, parts.Skip(1).ToArray()));
                    break;

                case "CHECKGOLD":
                    if (parts.Length < 3) return;

                    CheckList.Add(new NPCChecks(CheckType.CheckGold, parts[1], parts[2]));
                    break;
                case "CHECKGUILDGOLD":
                    if (parts.Length < 3) return;

                    CheckList.Add(new NPCChecks(CheckType.CheckGuildGold, parts[1], parts[2]));
                    break;
                case "CHECKCREDIT":
                case "CHECKCREDITPOINT" when IsLingFengCompatibility:
                    if (parts.Length < 3) return;

                    CheckList.Add(new NPCChecks(CheckType.CheckCredit, parts[1], parts[2]));
                    break;
                case "CHECKBINDMONEY" when IsLingFengCompatibility:
                    if (parts.Length != 3)
                        throw new InvalidDataException(
                            "CHECKBINDMONEY 需要货币名称和非负检测数量。");
                    CheckList.Add(new NPCChecks(
                        CheckType.LingFengBindMoney, parts[1], parts[2]));
                    break;
                case "CHECKITEMSTATE" when IsLingFengCompatibility:
                    if (parts.Length != 3 ||
                        !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture,
                            out int checkedItemState) || checkedItemState is < 0 or > 7)
                        throw new InvalidDataException(
                            "CHECKITEMSTATE 需要装备位置和 0 到 7 的状态项目。");
                    CheckList.Add(new NPCChecks(
                        CheckType.LingFengItemState, "STATE", parts[1], parts[2]));
                    break;
                case "CHECKITEMBIND" when IsLingFengCompatibility:
                    if (parts.Length != 2)
                        throw new InvalidDataException("CHECKITEMBIND 需要装备位置。");
                    CheckList.Add(new NPCChecks(
                        CheckType.LingFengItemState, "BIND", parts[1], "0"));
                    break;
                case "CHECKITEM":
                    if (parts.Length < 2) return;

                    if (IsLingFengCompatibility)
                    {
                        string requestedCount = parts.Length < 3 ? "1" : parts[2];
                        string partial = parts.Length < 4 ? "0" : parts[3];
                        string renamed = parts.Length < 5 ? "0" : parts[4];
                        CheckList.Add(new NPCChecks(
                            CheckType.CheckItemLingFeng, parts[1], requestedCount, partial, renamed));
                        break;
                    }

                    tempString = parts.Length < 3 ? "1" : parts[2];
                    tempString2 = parts.Length > 3 ? parts[3] : "";

                    CheckList.Add(new NPCChecks(CheckType.CheckItem, parts[1], tempString, tempString2));
                    break;

                case "CHECKGENDER":
                    if (parts.Length < 2) return;

                    CheckList.Add(new NPCChecks(CheckType.CheckGender, parts[1]));
                    break;
                case "GENDER" when IsLingFengCompatibility:
                    if (parts.Length > 2)
                        throw new InvalidDataException("GENDER 只允许可选的 MAN/WOMAN 参数。");
                    CheckList.Add(new NPCChecks(
                        CheckType.CheckGender, parts.Length == 1 ? "Female" : parts[1]));
                    break;

                case "CHECKCLASS":
                case "CHECKJOB":
                    if (parts.Length < 2) return;

                    CheckList.Add(new NPCChecks(CheckType.CheckClass, parts[1]));
                    break;

                case "DAYOFWEEK":
                    if (parts.Length < 2) return;
                    CheckList.Add(new NPCChecks(CheckType.CheckDay, parts[1]));
                    break;

                case "HOUR":
                    if (parts.Length < 2) return;

                    CheckList.Add(new NPCChecks(CheckType.CheckHour, parts[1]));
                    break;

                case "MIN":
                    if (parts.Length < 2) return;

                    CheckList.Add(new NPCChecks(CheckType.CheckMinute, parts[1]));
                    break;

                //cant use stored var
                case "CHECKNAMELIST":
                    if (parts.Length < 2) return;

                    quoteMatch = regexQuote.Match(line);

                    string listPath = parts[1];

                    if (quoteMatch.Success)
                        listPath = quoteMatch.Groups[1].Captures[0].Value;

                    var fileName = Path.Combine(Settings.NameListPath, listPath);

                    string sDirectory = Path.GetDirectoryName(fileName);
                    Directory.CreateDirectory(sDirectory);

                    if (File.Exists(fileName) || Envir.IsCSharpNameListDefined(listPath))
                        CheckList.Add(new NPCChecks(CheckType.CheckNameList, fileName));
                    break;

                case "CHECKACCOUNTLIST" when IsLingFengCompatibility:
                    if (parts.Length != 2)
                        throw new InvalidDataException("CHECKACCOUNTLIST 需要且只允许一个名单路径。");
                    CheckList.Add(new NPCChecks(CheckType.LingFengAccountList, parts[1]));
                    break;

                case "CHECKNAMEDATETIMELIST" when IsLingFengCompatibility:
                    if (parts.Length != 7 || parts[2] is not ("0" or "1"))
                        throw new InvalidDataException(
                            "CHECKNAMEDATETIMELIST 需要名单路径、过期清理标志、到期时间和剩余日时分四个变量。");
                    string expiryDestination = NormalizeWritableDestination(parts[3]);
                    string daysDestination = NormalizeWritableDestination(parts[4]);
                    string hoursDestination = NormalizeWritableDestination(parts[5]);
                    string minutesDestination = NormalizeWritableDestination(parts[6]);
                    if (string.IsNullOrEmpty(expiryDestination) || string.IsNullOrEmpty(daysDestination) ||
                        string.IsNullOrEmpty(hoursDestination) || string.IsNullOrEmpty(minutesDestination))
                        throw new InvalidDataException("CHECKNAMEDATETIMELIST 输出变量无效。");
                    CheckList.Add(new NPCChecks(
                        CheckType.LingFengNameDateTimeList, parts[1], parts[2], expiryDestination,
                        daysDestination, hoursDestination, minutesDestination));
                    break;

                //cant use stored var
                case "CHECKGUILDNAMELIST":
                    if (parts.Length < 2) return;

                    quoteMatch = regexQuote.Match(line);

                    listPath = parts[1];

                    if (quoteMatch.Success)
                        listPath = quoteMatch.Groups[1].Captures[0].Value;

                    fileName = Path.Combine(Settings.NameListPath, listPath);

                    sDirectory = Path.GetDirectoryName(fileName);
                    Directory.CreateDirectory(sDirectory);

                    if (File.Exists(fileName) || Envir.IsCSharpNameListDefined(listPath))
                        CheckList.Add(new NPCChecks(CheckType.CheckGuildNameList, fileName));
                    break;
                case "ISADMIN":
                    CheckList.Add(new NPCChecks(CheckType.IsAdmin));
                    break;

                case "CHECKPKPOINT":
                case "CHECKPKPOINTEX":
                    if (parts.Length < 3) return;

                    CheckList.Add(new NPCChecks(CheckType.CheckPkPoint, parts[1], parts[2]));
                    break;

                case "CHECKRANGE":
                    if (parts.Length < 4) return;

                    CheckList.Add(new NPCChecks(CheckType.CheckRange, parts[1], parts[2], parts[3]));
                    break;

                case "CHECKMAP":
                case "CHECKMAPNAME" when IsLingFengCompatibility:
                case "ISONMAP" when IsLingFengCompatibility:
                    if (parts.Length < 2) return;

                    CheckList.Add(new NPCChecks(CheckType.CheckMap, parts[1]));
                    break;

                //cant use stored var
                case "CHECK":
                    if (parts.Length < 3) return;
                    if (TryParseRuntimeVariableReference(parts[1], out _))
                    {
                        if (parts.Length < 4) return;
                        CheckList.Add(new NPCChecks(CheckType.Variable, parts[1], parts[2], parts[3]));
                        break;
                    }
                    var match = regexFlag.Match(parts[1]);
                    if (match.Success)
                    {
                        string flagIndex = match.Groups[1].Captures[0].Value;
                        CheckList.Add(new NPCChecks(CheckType.Check, flagIndex, parts[2]));
                    }
                    break;

                case "CHECKHUM":
                    if (parts.Length < 4) return;

                    tempString = parts.Length < 5 ? "1" : parts[4];
                    CheckList.Add(new NPCChecks(CheckType.CheckHum, parts[1], parts[2], parts[3], tempString));
                    break;

                case "CHECKMON":
                    if (parts.Length < 4) return;

                    tempString = parts.Length < 5 ? "1" : parts[4];
                    CheckList.Add(new NPCChecks(CheckType.CheckMon, parts[1], parts[2], parts[3], tempString));
                    break;

                case "CHECKEXACTMON":
                    if (parts.Length < 5) return;

                    tempString = parts.Length < 6 ? "1" : parts[5];
                    CheckList.Add(new NPCChecks(CheckType.CheckExactMon, parts[1], parts[2], parts[3], parts[4], tempString));
                    break;

                case "RANDOM":
                    if (parts.Length < 2) return;

                    CheckList.Add(new NPCChecks(CheckType.Random, parts[1]));
                    break;
                case "RANDOMEX" when IsLingFengCompatibility:
                    if (parts.Length != 3)
                        throw new InvalidDataException("RANDOMEX 需要子值和母值两个参数。");
                    CheckList.Add(new NPCChecks(CheckType.LingFengRandomRatio, parts[1], parts[2]));
                    break;
                case "CHANCE":
                    if (parts.Length < 2 || !TryParseRuntimeVariableReference(parts[1], out _)) return;
                    CheckList.Add(new NPCChecks(
                        CheckType.VariableChance, parts[1], parts.Length > 2 ? parts[2] : "PERCENT"));
                    break;
                case "CHECKVARINLIST":
                case "CHECKLISTALLDIGIT":
                case "CHECKINDICT":
                case "CHECKDICTALLDIGIT":
                    if (parts.Length < 2 || !TryParseRuntimeVariableReference(parts[1], out _)) return;
                    CheckList.Add(new NPCChecks(
                        CheckType.VariableComposite,
                        new[] { parts[0].ToUpperInvariant() }.Concat(parts.Skip(1)).ToArray()));
                    break;

                case "GROUPLEADER":
                    CheckList.Add(new NPCChecks(CheckType.Groupleader));
                    break;

                case "GROUPCOUNT":
                case "CHECKGROUPMEMBERCOUNT" when IsLingFengCompatibility:
                    if (parts.Length < 3) return;

                    CheckList.Add(new NPCChecks(CheckType.GroupCount, parts[1], parts[2]));
                    break;

                case "GROUPCHECKNEARBY":
                    CheckList.Add(new NPCChecks(CheckType.GroupCheckNearby));
                    break;

                case "PETCOUNT":
                    if (parts.Length < 3) return;

                    CheckList.Add(new NPCChecks(CheckType.PetCount, parts[1], parts[2]));
                    break;

                case "PETLEVEL":
                    if (parts.Length < 3) return;

                    CheckList.Add(new NPCChecks(CheckType.PetLevel, parts[1], parts[2]));
                    break;

                case "HEROLEVEL":
                    if (parts.Length < 3) return;
                    CheckList.Add(new NPCChecks(CheckType.HeroLevel, parts[1], parts[2]));
                    break;

                case "CHECKHEROCLASS":
                    if (parts.Length < 2) return;
                    CheckList.Add(new NPCChecks(CheckType.CheckHeroClass, parts[1]));
                    break;

                case "CHECKHEROGENDER":
                    if (parts.Length < 2) return;
                    CheckList.Add(new NPCChecks(CheckType.CheckHeroGender, parts[1]));
                    break;

                case "CHECKHEROITEM":
                    if (parts.Length < 2) return;
                    tempString = parts.Length < 3 ? "1" : parts[2];
                    tempString2 = parts.Length > 3 ? parts[3] : "";
                    CheckList.Add(new NPCChecks(CheckType.CheckHeroItem, parts[1], tempString, tempString2));
                    break;

                case "CHECKCALC":
                    if (parts.Length < 4) return;
                    CheckList.Add(new NPCChecks(CheckType.CheckCalc, parts[1], parts[2], parts[3]));
                    break;

                case "INGUILD":
                    string guildName = string.Empty;

                    if (parts.Length > 1) guildName = parts[1];

                    CheckList.Add(new NPCChecks(CheckType.InGuild, guildName));
                    break;

                case "CHECKQUEST":
                    if (parts.Length < 3) return;

                    CheckList.Add(new NPCChecks(CheckType.CheckQuest, parts[1], parts[2]));
                    break;
                case "ISQUESTACTIVE" when IsLingFengCompatibility:
                    if (parts.Length != 2 ||
                        !Server.Scripting.LingFengSocialCommandExecutor.TryParseQuestIndex(parts[1], out _, out _)) return;
                    CheckList.Add(new NPCChecks(CheckType.CheckQuest, parts[1], "ACTIVE"));
                    break;
                case "ISQUESTCOMPLETED" when IsLingFengCompatibility:
                    if (parts.Length != 2 ||
                        !Server.Scripting.LingFengSocialCommandExecutor.TryParseQuestIndex(parts[1], out _, out _)) return;
                    CheckList.Add(new NPCChecks(CheckType.CheckQuest, parts[1], "COMPLETE"));
                    break;
                case "CHECKRELATIONSHIP":
                    CheckList.Add(new NPCChecks(CheckType.CheckRelationship));
                    break;
                case "CHECKWEDDINGRING":
                    CheckList.Add(new NPCChecks(CheckType.CheckWeddingRing));
                    break;

                case "CHECKPET":
                    if (parts.Length < 2) return;

                    CheckList.Add(new NPCChecks(CheckType.CheckPet, parts[1]));
                    break;

                case "HASBAGSPACE":
                    if (parts.Length < 3) return;

                    CheckList.Add(new NPCChecks(CheckType.HasBagSpace, parts[1], parts[2]));
                    break;
                case "ISNEWHUMAN":
                    CheckList.Add(new NPCChecks(CheckType.IsNewHuman));
                    break;
                case "CHECKCONQUEST":
                    if (parts.Length < 2) return;

                    CheckList.Add(new NPCChecks(CheckType.CheckConquest, parts[1]));
                    break;
                case "AFFORDGUARD":
                    if (parts.Length < 3) return;

                    CheckList.Add(new NPCChecks(CheckType.AffordGuard, parts[1], parts[2]));
                    break;
                case "AFFORDGATE":
                    if (parts.Length < 3) return;

                    CheckList.Add(new NPCChecks(CheckType.AffordGate, parts[1], parts[2]));
                    break;
                case "AFFORDWALL":
                    if (parts.Length < 3) return;

                    CheckList.Add(new NPCChecks(CheckType.AffordWall, parts[1], parts[2]));
                    break;
                case "AFFORDSIEGE":
                    if (parts.Length < 3) return;

                    CheckList.Add(new NPCChecks(CheckType.AffordSiege, parts[1], parts[2]));
                    break;
                case "CHECKPERMISSION":
                    if (parts.Length < 2) return;

                    CheckList.Add(new NPCChecks(CheckType.CheckPermission, parts[1]));
                    break;
                case "CONQUESTAVAILABLE":
                    if (parts.Length < 2) return;

                    CheckList.Add(new NPCChecks(CheckType.ConquestAvailable, parts[1]));
                    break;
                case "CONQUESTOWNER":
                    if (parts.Length < 2) return;

                    CheckList.Add(new NPCChecks(CheckType.ConquestOwner, parts[1]));
                    break;
                case "CHECKTIMER":
                    if (parts.Length < 4) return;

                    CheckList.Add(new NPCChecks(CheckType.CheckTimer, parts[1], parts[2], parts[3]));
                    break;
                case "CHECKMYSHOP" when IsLingFengCompatibility:
                    if (parts.Length != 1)
                        throw new InvalidDataException("CHECKMYSHOP 不接受参数。" );
                    CheckList.Add(new NPCChecks(
                        CheckType.LingFengDeferredCompatibilityCheck, "CHECKMYSHOP"));
                    break;
                case "CHECKSHOPNAME" when IsLingFengCompatibility:
                    if (parts.Length != 2)
                        throw new InvalidDataException("CHECKSHOPNAME 需要商店名称。" );
                    CheckList.Add(new NPCChecks(
                        CheckType.LingFengDeferredCompatibilityCheck,
                        "CHECKSHOPNAME", parts[1]));
                    break;
                case "CHECKBOXITEMCOUNT" when IsLingFengCompatibility:
                    if (parts.Length != 2)
                        throw new InvalidDataException("CHECKBOXITEMCOUNT 需要一个物品框编号。" );
                    CheckList.Add(new NPCChecks(
                        CheckType.LingFengDeferredCompatibilityCheck,
                        "CHECKBOXITEMCOUNT", parts[1]));
                    break;
                case "CHECKBUFF":
                    if (parts.Length < 2) return;

                    CheckList.Add(new NPCChecks(CheckType.CheckBuff, parts[1]));
                    break;
                case "CHECKTRANSFORM":
                    if (parts.Length < 2) return;

                    CheckList.Add(new NPCChecks(CheckType.CheckTransform, parts[1]));
                    break;
                case "ISGUILDLEADER":
                    CheckList.Add(new NPCChecks(CheckType.IsGuildLeader));
                    break;
            }

            if (negated && CheckList.Count == originalCheckCount + 1)
                CheckList[^1].Negated = true;

        }
        public void ParseAct(List<NPCActions> acts, string line)
        {
            if (!TxtScriptTokenizer.TryTokenize(line, out string[] parts, out string tokenError))
                throw new InvalidDataException($"动作命令参数无效：{tokenError} 原文={line}");

            parts = ParseArguments(parts);

            if (parts.Length == 0) return;

            string fileName;
            var regexQuote = new Regex("\"([^\"]*)\"");
            var regexFlag = new Regex(@"\[(.*?)\]");

            Match quoteMatch = null;

            switch (parts[0].ToUpper())
            {
                case "MOVE":
                case "TELEPORT" when IsLingFengCompatibility:
                    if (parts.Length < 2) return;

                    string tempx = parts.Length > 3 ? parts[2] : "0";
                    string tempy = parts.Length > 3 ? parts[3] : "0";

                    acts.Add(new NPCActions(ActionType.Move, parts[1], tempx, tempy));
                    break;

                case "MAP" when IsLingFengCompatibility:
                case "MAPMOVE" when IsLingFengCompatibility:
                    if (parts.Length is not (2 or 4 or 5))
                        throw new InvalidDataException(
                            $"{parts[0].ToUpperInvariant()} 需要参数：地图，或地图 X Y [范围]。");

                    acts.Add(new NPCActions(ActionType.LingFengMapMove, parts.Skip(1).ToArray()));
                    break;

                case "ADDMIRRORMAP" when IsLingFengCompatibility:
                    if (parts.Length != 10 || !IsWritableScriptVariable(parts[7]))
                        throw new InvalidDataException(
                            "ADDMIRRORMAP 需要源地图、镜像编号、标题、秒数、返回地图、小地图、结果变量、显示模式、返回坐标。");
                    acts.Add(new NPCActions(
                        ActionType.LingFengAddMirrorMap, parts.Skip(1).ToArray()));
                    break;

                case "DELMIRRORMAP" when IsLingFengCompatibility:
                    if (parts.Length != 2)
                        throw new InvalidDataException("DELMIRRORMAP 需要镜像地图编号。");
                    acts.Add(new NPCActions(ActionType.LingFengDeleteMirrorMap, parts[1]));
                    break;

                case "SETMIRRORMAPTIME" when IsLingFengCompatibility:
                    if (parts.Length is < 3 or > 4 ||
                        parts.Length == 4 && parts[3] is not ("0" or "1"))
                        throw new InvalidDataException(
                            "SETMIRRORMAPTIME 需要镜像编号、秒数和可选重新计时标志 0/1。");
                    acts.Add(new NPCActions(ActionType.LingFengSetMirrorMapTime,
                        parts[1], parts[2], parts.Length == 4 ? parts[3] : "0"));
                    break;

                case "GETMIRRORMAPTIME" when IsLingFengCompatibility:
                    if (parts.Length is < 3 or > 4 ||
                        !IsWritableScriptVariable(parts[2]) ||
                        parts.Length == 4 && !IsWritableScriptVariable(parts[3]))
                        throw new InvalidDataException(
                            "GETMIRRORMAPTIME 需要镜像编号、总时间变量和可选剩余时间变量。");
                    acts.Add(new NPCActions(ActionType.LingFengGetMirrorMapTime,
                        parts[1], parts[2], parts.Length == 4 ? parts[3] : string.Empty));
                    break;

                case "CREATEECTYPE" when IsLingFengCompatibility:
                    if (parts.Length != 3)
                        throw new InvalidDataException("CREATEECTYPE 需要副本名称和有效分钟数。");
                    acts.Add(new NPCActions(
                        ActionType.LingFengCreateEctype, parts[1], parts[2]));
                    break;

                case "MOVEECTYPE" when IsLingFengCompatibility:
                    if (parts.Length != 4)
                        throw new InvalidDataException("MOVEECTYPE 需要副本名称、X、Y。");
                    acts.Add(new NPCActions(
                        ActionType.LingFengMoveEctype, parts[1], parts[2], parts[3]));
                    break;

                case "MOBECTYPEMON" when IsLingFengCompatibility:
                    if (parts.Length is not (7 or 8))
                        throw new InvalidDataException(
                            "MOBECTYPEMON 需要副本选择、X、Y、怪物、数量、范围和可选名称颜色。");
                    acts.Add(new NPCActions(
                        ActionType.LingFengSpawnEctypeMonster, parts.Skip(1).ToArray()));
                    break;

                case "ADDTEXTLIST" when IsLingFengCompatibility:
                    if (parts.Length < 3)
                        throw new InvalidDataException(
                            "ADDTEXTLIST 需要文件位置、写入内容和可选绝对路径标志 0/1。");
                    bool hasAbsoluteFlag = parts.Length >= 4 && parts[^1] is "0" or "1";
                    string textListValue = string.Join(" ", parts.Skip(2).Take(
                        parts.Length - 2 - (hasAbsoluteFlag ? 1 : 0)));
                    if (textListValue.Length == 0)
                        throw new InvalidDataException("ADDTEXTLIST 写入内容不能为空。");
                    acts.Add(new NPCActions(ActionType.LingFengAddTextList,
                        parts[1], textListValue, hasAbsoluteFlag ? parts[^1] : "0"));
                    break;

                case "ADDTEXTLISTEX" when IsLingFengCompatibility:
                    if (parts.Length < 3)
                        throw new InvalidDataException("ADDTEXTLISTEX 需要文件位置、写入内容和可选行号、绝对路径标志。");
                    int tail = parts.Length;
                    string absoluteFlag = "0";
                    if (tail >= 5 && parts[^1] is "0" or "1" &&
                        int.TryParse(parts[^2], out int explicitLine) && explicitLine is >= 0 and <= 65_535)
                    {
                        absoluteFlag = parts[^1];
                        tail -= 2;
                    }
                    else if (int.TryParse(parts[^1], out explicitLine) && explicitLine is >= 0 and <= 65_535)
                        tail--;
                    else explicitLine = 0;
                    string textListLine = string.Join(" ", parts.Skip(2).Take(tail - 2));
                    if (textListLine.Length == 0)
                        throw new InvalidDataException("ADDTEXTLISTEX 写入内容不能为空。");
                    acts.Add(new NPCActions(ActionType.LingFengSetTextListLine,
                        parts[1], textListLine, explicitLine.ToString(CultureInfo.InvariantCulture), absoluteFlag));
                    break;

                case "INSTANCEMOVE":
                    if (parts.Length < 5) return;

                    acts.Add(new NPCActions(ActionType.InstanceMove, parts[1], parts[2], parts[3], parts[4]));
                    break;

                case "GOLDCOUNT":
                    if (parts.Length != 3) return;
                    acts.Add(new NPCActions(ActionType.ChangeGold, parts[1], parts[2]));
                    break;

                case "CHANGEDAMAGEVALUE" when IsLingFengCompatibility:
                    if (parts.Length != 4)
                        throw new InvalidDataException("CHANGEDAMAGEVALUE 需要参数：字段(0伤害/1防御) 运算符 数值。");
                    acts.Add(new NPCActions(ActionType.ChangeDamageValue, parts[1], parts[2], parts[3]));
                    break;

                case "GIVEGOLD":
                    if (parts.Length < 2) return;

                    acts.Add(new NPCActions(ActionType.GiveGold, parts[1]));
                    break;

                case "TAKEGOLD":
                    if (parts.Length < 2) return;

                    acts.Add(new NPCActions(ActionType.TakeGold, parts[1]));
                    break;

                case "<$CURRRTARGETNAME>.TAKE" when IsLingFengCompatibility:
                    if (parts.Length != 3 ||
                        !parts[1].Equals("金币", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException(
                            "<$CURRRTARGETNAME>.TAKE 当前仅支持金币和扣除数量。");
                    acts.Add(new NPCActions(
                        ActionType.LingFengTargetTakeGold, parts[2]));
                    break;

                case "<$CURRRTARGETNAME>.GOTO" when IsLingFengCompatibility:
                    if (parts.Length != 2 || !parts[1].StartsWith("@", StringComparison.Ordinal))
                        throw new InvalidDataException(
                            "<$CURRRTARGETNAME>.GOTO 需要目标页标签。");
                    acts.Add(new NPCActions(ActionType.LingFengTargetGoto, parts[1]));
                    break;

                case "<$CURRRTARGETNAME>.DELAYGOTO" when IsLingFengCompatibility:
                    if (parts.Length != 3 || !parts[2].StartsWith("@", StringComparison.Ordinal))
                        throw new InvalidDataException(
                            "<$CURRRTARGETNAME>.DELAYGOTO 需要毫秒数和目标页标签。");
                    acts.Add(new NPCActions(
                        ActionType.LingFengTargetDelayGoto, parts[1], parts[2]));
                    break;

                case "DECBINDMONEY" when IsLingFengCompatibility:
                    if (parts.Length != 3)
                        throw new InvalidDataException(
                            "DECBINDMONEY 需要货币名称和非负扣除数量。");
                    acts.Add(new NPCActions(
                        ActionType.LingFengDecBindMoney, parts[1], parts[2]));
                    break;

                case "CHANGEPKPOINT":
                    if (parts.Length != 3) return;
                    acts.Add(new NPCActions(ActionType.ChangePkPoint, parts[1], parts[2]));
                    break;
                case "<$KILLER>.CHANGEPKPOINT" when IsLingFengCompatibility:
                    if (parts.Length != 3 || parts[1] is not ("+" or "-" or "="))
                        throw new InvalidDataException("<$KILLER>.CHANGEPKPOINT 参数格式无效。");
                    acts.Add(new NPCActions(
                        ActionType.LingFengChangePkPointTarget, parts[1], parts[2]));
                    break;
                case "<$KILLER>.GAMEGOLD" when IsLingFengCompatibility:
                case "<$KILLER>.GAMEGIRD" when IsLingFengCompatibility:
                    if (parts.Length != 3 || parts[1] is not ("+" or "-" or "="))
                        throw new InvalidDataException(
                            $"{parts[0].ToUpperInvariant()} 参数格式无效。");
                    acts.Add(new NPCActions(
                        ActionType.LingFengKillerCurrencyAdjust,
                        parts[0].EndsWith("GAMEGOLD", StringComparison.OrdinalIgnoreCase)
                            ? "GAMEGOLD" : "GAMEGIRD",
                        parts[1], parts[2]));
                    break;
                case "GIVEGUILDGOLD":
                    if (parts.Length < 2) return;

                    acts.Add(new NPCActions(ActionType.GiveGuildGold, parts[1]));
                    break;
                case "TAKEGUILDGOLD":
                    if (parts.Length < 2) return;

                    acts.Add(new NPCActions(ActionType.TakeGuildGold, parts[1]));
                    break;
                case "GIVECREDIT":
                    if (parts.Length < 2) return;

                    acts.Add(new NPCActions(ActionType.GiveCredit, parts[1]));
                    break;
                case "TAKECREDIT":
                    if (parts.Length < 2) return;

                    acts.Add(new NPCActions(ActionType.TakeCredit, parts[1]));
                    break;

                case "CREDITPOINT" when IsLingFengCompatibility:
                    if (parts.Length != 3 || parts[1] is not ("+" or "-" or "="))
                        throw new InvalidDataException(
                            "CREDITPOINT 需要参数：操作符(+|-|=) 点数。");
                    acts.Add(new NPCActions(
                        ActionType.LingFengCreditPoint, parts[1], parts[2]));
                    break;

                case "GIVEPEARLS":
                    if (parts.Length < 2) return;

                    acts.Add(new NPCActions(ActionType.GivePearls, parts[1]));
                    break;

                case "TAKEPEARLS":
                    if (parts.Length < 2) return;

                    acts.Add(new NPCActions(ActionType.TakePearls, parts[1]));
                    break;

                case "GIVEITEM":
                    if (parts.Length < 2) return;

                    string count = parts.Length < 3 ? string.Empty : parts[2];
                    acts.Add(new NPCActions(ActionType.GiveItem, parts[1], count));
                    break;

                case "GIVESTATEITEM" when IsLingFengCompatibility:
                    if (parts.Length is < 9 or > 10)
                        throw new InvalidDataException(
                            "GIVESTATEITEM 需要参数：物品名 禁扔 禁交易 禁存 禁修 禁售 禁爆 丢弃消失 [数量]。");
                    acts.Add(new NPCActions(ActionType.LingFengGiveStateItem,
                        parts.Skip(1).Concat(parts.Length == 9 ? new[] { "1" } : Array.Empty<string>()).ToArray()));
                    break;

                case "SETITEMSTATE" when IsLingFengCompatibility:
                    if (parts.Length == 3 && parts[2] is "0" or "1")
                    {
                        acts.Add(new NPCActions(
                            ActionType.LingFengSetItemState, "BIND", parts[1], "0", parts[2]));
                        break;
                    }
                    if (parts.Length != 4 ||
                        !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture,
                            out int itemStateIndex) || itemStateIndex is < 0 or > 7 ||
                        parts[3] is not ("0" or "1"))
                        throw new InvalidDataException(
                            "SETITEMSTATE 需要装备位置、0 到 7 的状态项目和 0/1 状态；三参数写法按旧人物绑定语义处理。");
                    acts.Add(new NPCActions(
                        ActionType.LingFengSetItemState, "STATE", parts[1], parts[2], parts[3]));
                    break;

                case "LINKGIVEITEM" when IsLingFengCompatibility:
                    if (parts.Length != 1)
                        throw new InvalidDataException("LINKGIVEITEM 不接受参数。");
                    acts.Add(new NPCActions(ActionType.LingFengLinkGiveItem));
                    break;

                case "LINKPICKUPITEM" when IsLingFengCompatibility:
                    if (parts.Length != 1)
                        throw new InvalidDataException("LINKPICKUPITEM 不接受参数。");
                    acts.Add(new NPCActions(ActionType.LingFengLinkPickupItem));
                    break;

                case "CLEARLINKITEM" when IsLingFengCompatibility:
                    if (parts.Length != 1)
                        throw new InvalidDataException("CLEARLINKITEM 不接受参数。");
                    acts.Add(new NPCActions(ActionType.LingFengClearLinkItem));
                    break;

                case "SETSKILLPOWER" when IsLingFengCompatibility:
                    if (parts.Length != 11 || parts[2] is not ("=" or "+" or "-"))
                        throw new InvalidDataException(
                            "SETSKILLPOWER 需要参数：技能ID 操作符 六项修正 持续秒数 是否保存。");
                    acts.Add(new NPCActions(ActionType.LingFengSetSkillPower,
                        parts.Skip(1).ToArray()));
                    break;

                case "ADDHUMNEWVALUE" when IsLingFengCompatibility:
                    if (parts.Length is < 4 or > 6 ||
                        parts[2] is not ("=" or "+" or "-") ||
                        (parts.Length == 6 && parts[5] != "0"))
                        throw new InvalidDataException(
                            "ADDHUMNEWVALUE 需要参数：属性 操作符 值 [持续秒数] [酷明扩展0]。");
                    acts.Add(new NPCActions(ActionType.LingFengAddHumNewValue,
                        parts[1], parts[2], parts[3],
                        parts.Length >= 5 ? parts[4] : "0"));
                    break;

                case "SETONTIMER" when IsLingFengCompatibility:
                    if (parts.Length is < 3 or > 4)
                        throw new InvalidDataException(
                            "SETONTIMER 需要参数：定时器编号 间隔秒数 [执行次数]。");
                    acts.Add(new NPCActions(ActionType.LingFengSetOnTimer,
                        parts[1], parts[2], parts.Length == 4 ? parts[3] : "0"));
                    break;

                case "SETOFFTIMER" when IsLingFengCompatibility:
                    if (parts.Length != 2)
                        throw new InvalidDataException(
                            "SETOFFTIMER 需要参数：定时器编号。");
                    acts.Add(new NPCActions(ActionType.LingFengSetOffTimer, parts[1]));
                    break;

                case "LOOPGOTO" when IsLingFengCompatibility:
                    if (parts.Length is < 2 or > 3)
                        throw new InvalidDataException(
                            "LOOPGOTO 需要参数：页面标签 [执行次数]。");
                    acts.Add(new NPCActions(ActionType.LingFengLoopGoto,
                        parts[1], parts.Length == 3 ? parts[2] : "0"));
                    break;

                case "ENDLOOP" when IsLingFengCompatibility:
                    if (parts.Length != 1)
                        throw new InvalidDataException("ENDLOOP 不接受参数。");
                    acts.Add(new NPCActions(ActionType.LingFengEndLoop));
                    break;

                case "WHILE" when IsLingFengCompatibility:
                    if (parts.Length != 4 ||
                        parts[2] is not ("=" or "==" or "!=" or "<>" or ">" or ">=" or "<" or "<="))
                        throw new InvalidDataException(
                            "WHILE 需要参数：变量或值 比较符号(=|==|!=|<>|>|>=|<|<=) 变量或值。");
                    acts.Add(new NPCActions(ActionType.LingFengWhile,
                        parts[1], parts[2], parts[3]));
                    break;

                case "ENDWHILE" when IsLingFengCompatibility:
                    if (parts.Length != 1)
                        throw new InvalidDataException("ENDWHILE 不接受参数。");
                    acts.Add(new NPCActions(ActionType.LingFengEndWhile));
                    break;

                case "GIVE" when IsLingFengCompatibility:
                    if (parts.Length < 2) return;
                    if (parts.Length > 3)
                        throw new InvalidDataException("LFM2 扩展 GIVE 极品属性参数尚无等价物品模型；请改用 GIVEITEM 或移除扩展参数。");
                    count = parts.Length < 3 ? string.Empty : parts[2];
                    acts.Add(new NPCActions(ActionType.GiveItem, parts[1], count));
                    break;

                case "TAKEITEM":
                    if (parts.Length < 3) return;

                    count = parts.Length < 3 ? string.Empty : parts[2];
                    string dura = parts.Length > 3 ? parts[3] : "";

                    acts.Add(new NPCActions(ActionType.TakeItem, parts[1], count, dura));
                    break;

                case "TAKE" when IsLingFengCompatibility:
                    if (parts.Length < 2 || parts.Length > 7) return;
                    acts.Add(new NPCActions(
                        ActionType.TakeItemLingFeng,
                        parts[1],
                        parts.Length > 2 ? parts[2] : "1",
                        parts.Length > 3 ? parts[3] : "0",
                        parts.Length > 4 ? parts[4] : "0",
                        parts.Length > 5 ? parts[5] : "1",
                        parts.Length > 6 ? parts[6] : "0"));
                    break;

                case "TAKEW" when IsLingFengCompatibility:
                    if (parts.Length is < 2 or > 3)
                        throw new InvalidDataException("TAKEW 需要物品名称及可选数量。");
                    acts.Add(new NPCActions(
                        ActionType.LingFengTakeWornItem,
                        parts[1], parts.Length == 3 ? parts[2] : "1"));
                    break;

                case "TAKEBAGITEM" when IsLingFengCompatibility:
                    if (parts.Length is < 9 or > 14)
                        throw new InvalidDataException(
                            "TAKEBAGITEM 需要物品列表、数量、元宝、金币、泡点、经验、结果变量、聚灵珠选项及最多五个筛选参数。");
                    acts.Add(new NPCActions(
                        ActionType.LingFengTakeBagItem, parts.Skip(1).ToArray()));
                    break;

                case "TAKEBAGITEMEX" when IsLingFengCompatibility:
                    if (parts.Length is < 9 or > 14)
                        throw new InvalidDataException(
                            "TAKEBAGITEMEX 需要物品IDX范围、数量、元宝、金币、泡点、经验、结果变量、聚灵珠选项及最多五个筛选参数。");
                    acts.Add(new NPCActions(
                        ActionType.LingFengTakeBagItemByIndex, parts.Skip(1).ToArray()));
                    break;

                case "CHANGEMAPDESC" when IsLingFengCompatibility:
                    if (parts.Length < 3 ||
                        !int.TryParse(parts[^1], NumberStyles.None, CultureInfo.InvariantCulture,
                            out int saveMapDescription) || saveMapDescription is not (0 or 1))
                        throw new InvalidDataException(
                            "CHANGEMAPDESC 需要新地图显示名和是否保存参数。");
                    string mapDescription = string.Join(" ", parts.Skip(1).Take(parts.Length - 2));
                    if (string.IsNullOrWhiteSpace(mapDescription) || mapDescription.Length > 128)
                        throw new InvalidDataException(
                            "CHANGEMAPDESC 的显示名必须为 1 到 128 个字符。");
                    acts.Add(new NPCActions(
                        ActionType.LingFengChangeMapDescription,
                        mapDescription, parts[^1]));
                    break;

                case "GIVEEXP":
                    if (parts.Length < 2) return;

                    acts.Add(new NPCActions(ActionType.GiveExp, parts[1]));
                    break;

                case "CHANGEEXP" when IsLingFengCompatibility:
                    if (parts.Length != 3 || parts[1] != "+")
                        throw new InvalidDataException("CHANGEEXP 当前仅支持命格使用的增加经验格式。");
                    acts.Add(new NPCActions(ActionType.GiveExp, parts[2]));
                    break;

                case "CHANGENAMECOLOR" when IsLingFengCompatibility:
                    if (parts.Length != 2)
                        throw new InvalidDataException(
                            "CHANGENAMECOLOR 需要一个 0 至 255 的颜色代码。");
                    acts.Add(new NPCActions(ActionType.LingFengChangeNameColour, parts[1]));
                    break;

                case "GAMEGOLD" when IsLingFengCompatibility:
                    if (parts.Length != 3 || parts[1] is not ("+" or "-" or "="))
                        throw new InvalidDataException("GAMEGOLD 参数格式无效。");
                    acts.Add(new NPCActions(
                        ActionType.LingFengGameGoldAdjust, parts[1], parts[2]));
                    break;

                case "GAMEPOINT" when IsLingFengCompatibility:
                    if (parts.Length != 3 || parts[1] is not ("+" or "-" or "="))
                        throw new InvalidDataException("GAMEPOINT 参数格式无效。");
                    acts.Add(new NPCActions(
                        ActionType.LingFengGamePointAdjust, parts[1], parts[2]));
                    break;

                case "GAMEDIAMOND" when IsLingFengCompatibility:
                    if (parts.Length != 3 || parts[1] is not ("+" or "-" or "="))
                        throw new InvalidDataException("GAMEDIAMOND 参数格式无效。");
                    acts.Add(new NPCActions(
                        ActionType.LingFengGameDiamondAdjust, parts[1], parts[2]));
                    break;

                case "GAMEGIRD" when IsLingFengCompatibility:
                    if (parts.Length != 3 || parts[1] is not ("+" or "-" or "="))
                        throw new InvalidDataException("GAMEGIRD 参数格式无效。");
                    acts.Add(new NPCActions(
                        ActionType.LingFengGameGirdAdjust, parts[1], parts[2]));
                    break;

                case "GIVEPET":
                    if (parts.Length < 2) return;

                    string petcount = parts.Length > 2 ? parts[2] : "1";
                    string petlevel = parts.Length > 3 ? parts[3] : "0";

                    acts.Add(new NPCActions(ActionType.GivePet, parts[1], petcount, petlevel));
                    break;
                case "REMOVEPET":
                    if (parts.Length < 2) return;

                    acts.Add(new NPCActions(ActionType.RemovePet, parts[1]));
                    break;
                case "CLEARPETS":
                    acts.Add(new NPCActions(ActionType.ClearPets));
                    break;

                case "GOTO":
                    if (parts.Length < 2) return;

                    acts.Add(new NPCActions(ActionType.Goto, parts[1]));
                    break;

                case "GOTOLABEL":
                    if (parts.Length < 3) return;

                    acts.Add(new NPCActions(ActionType.GotoLabel, parts.Skip(1).ToArray()));
                    break;

                case "CALL":
                    if (parts.Length < 2) return;
                    string listPath = parts[1].Trim('[', ']');
                    if (!LingFengScriptReferenceResolver.TryResolveCallKey(listPath, out string callKey)) return;
                    if (Envir.TextFileProvider == null || Envir.TextFileProvider.GetByKey(callKey) == null)
                    {
                        if (Settings.TxtScriptsLogLoads)
                            MessageQueue.Enqueue($"[TxtScripts] CALL 目标脚本缺失：{callKey}");

                        return;
                    }

                    var script = NPCScript.GetOrAdd(0, callKey, NPCScriptType.Called);

                    Page.ScriptCalls.Add(script.ScriptID);

                    acts.Add(parts.Length > 2
                        ? new NPCActions(ActionType.Call, script.ScriptID.ToString(), parts[2])
                        : new NPCActions(ActionType.Call, script.ScriptID.ToString()));
                    break;

                case "BREAK":
                    acts.Add(new NPCActions(ActionType.Break));
                    break;

                //cant use stored var
                case "ADDNAMELIST":
                    if (parts.Length < 2) return;

                    quoteMatch = regexQuote.Match(line);

                    listPath = parts[1];

                    if (quoteMatch.Success)
                        listPath = quoteMatch.Groups[1].Captures[0].Value;

                    fileName = Path.Combine(Settings.NameListPath, listPath);

                    string sDirectory = Path.GetDirectoryName(fileName);
                    Directory.CreateDirectory(sDirectory);

                    if (!File.Exists(fileName))
                        File.Create(fileName).Close();

                    acts.Add(new NPCActions(ActionType.AddNameList, fileName));
                    break;

                case "ADDACCOUNTLIST" when IsLingFengCompatibility:
                    if (parts.Length != 2)
                        throw new InvalidDataException("ADDACCOUNTLIST 需要且只允许一个名单路径。");
                    acts.Add(new NPCActions(ActionType.LingFengAddAccountList, parts[1]));
                    break;

                case "DELACCOUNTLIST" when IsLingFengCompatibility:
                    if (parts.Length != 2)
                        throw new InvalidDataException("DELACCOUNTLIST 需要且只允许一个名单路径。");
                    acts.Add(new NPCActions(ActionType.LingFengDelAccountList, parts[1]));
                    break;

                case "ADDNAMEDATETIMELIST" when IsLingFengCompatibility:
                    if (parts.Length != 5)
                        throw new InvalidDataException(
                            "ADDNAMEDATETIMELIST 需要名单路径、增加天数、小时数和分钟数。");
                    acts.Add(new NPCActions(
                        ActionType.LingFengAddNameDateTimeList,
                        parts[1], parts[2], parts[3], parts[4]));
                    break;

                //cant use stored var
                case "ADDGUILDNAMELIST":
                    if (parts.Length < 2) return;

                    quoteMatch = regexQuote.Match(line);

                    listPath = parts[1];

                    if (quoteMatch.Success)
                        listPath = quoteMatch.Groups[1].Captures[0].Value;

                    fileName = Path.Combine(Settings.NameListPath, listPath);

                    sDirectory = Path.GetDirectoryName(fileName);
                    Directory.CreateDirectory(sDirectory);

                    if (!File.Exists(fileName))
                        File.Create(fileName).Close();

                    acts.Add(new NPCActions(ActionType.AddGuildNameList, fileName));
                    break;
                //cant use stored var
                case "DELNAMELIST":
                    if (parts.Length < 2) return;

                    quoteMatch = regexQuote.Match(line);

                    listPath = parts[1];

                    if (quoteMatch.Success)
                        listPath = quoteMatch.Groups[1].Captures[0].Value;

                    fileName = Path.Combine(Settings.NameListPath, listPath);

                    sDirectory = Path.GetDirectoryName(fileName);
                    Directory.CreateDirectory(sDirectory);

                    if (File.Exists(fileName))
                        acts.Add(new NPCActions(ActionType.DelNameList, fileName));
                    break;

                //cant use stored var
                case "DELGUILDNAMELIST":
                    if (parts.Length < 2) return;

                    quoteMatch = regexQuote.Match(line);

                    listPath = parts[1];

                    if (quoteMatch.Success)
                        listPath = quoteMatch.Groups[1].Captures[0].Value;

                    fileName = Path.Combine(Settings.NameListPath, listPath);

                    sDirectory = Path.GetDirectoryName(fileName);
                    Directory.CreateDirectory(sDirectory);

                    if (File.Exists(fileName))
                        acts.Add(new NPCActions(ActionType.DelGuildNameList, fileName));
                    break;
                //cant use stored var
                case "CLEARNAMELIST":
                    if (parts.Length < 2) return;

                    quoteMatch = regexQuote.Match(line);

                    listPath = parts[1];

                    if (quoteMatch.Success)
                        listPath = quoteMatch.Groups[1].Captures[0].Value;

                    fileName = Path.Combine(Settings.NameListPath, listPath);

                    sDirectory = Path.GetDirectoryName(fileName);
                    Directory.CreateDirectory(sDirectory);

                    if (File.Exists(fileName))
                        acts.Add(new NPCActions(ActionType.ClearNameList, fileName));
                    break;
                //cant use stored var
                case "CLEARGUILDNAMELIST":
                    if (parts.Length < 2) return;

                    quoteMatch = regexQuote.Match(line);

                    listPath = parts[1];

                    if (quoteMatch.Success)
                        listPath = quoteMatch.Groups[1].Captures[0].Value;

                    fileName = Path.Combine(Settings.NameListPath, listPath);

                    sDirectory = Path.GetDirectoryName(fileName);
                    Directory.CreateDirectory(sDirectory);

                    if (File.Exists(fileName))
                        acts.Add(new NPCActions(ActionType.ClearGuildNameList, fileName));
                    break;

                case "GIVEHP":
                    if (parts.Length < 2) return;

                    acts.Add(new NPCActions(ActionType.GiveHP, parts[1]));
                    break;

                case "GIVEMP":
                    if (parts.Length < 2) return;
                    acts.Add(new NPCActions(ActionType.GiveMP, parts[1]));
                    break;

                case "CHANGELEVEL":
                    if (parts.Length < 2) return;

                    acts.Add(new NPCActions(ActionType.ChangeLevel, parts[1]));
                    break;

                case "SETPKPOINT":
                    if (parts.Length < 2) return;

                    acts.Add(new NPCActions(ActionType.SetPkPoint, parts[1]));
                    break;

                case "REDUCEPKPOINT":
                    if (parts.Length < 2) return;

                    acts.Add(new NPCActions(ActionType.ReducePkPoint, parts[1]));
                    break;

                case "INCREASEPKPOINT":
                    if (parts.Length < 2) return;

                    acts.Add(new NPCActions(ActionType.IncreasePkPoint, parts[1]));
                    break;

                case "CHANGEGENDER":
                    acts.Add(new NPCActions(ActionType.ChangeGender));
                    break;

                case "CHANGECLASS":
                    if (parts.Length < 2) return;

                    acts.Add(new NPCActions(ActionType.ChangeClass, parts[1]));
                    break;

                case "CHANGEJOB" when IsLingFengCompatibility:
                    if (parts.Length != 2 ||
                        !Enum.TryParse(parts[1], true, out MirClass changeJob) ||
                        changeJob is not (MirClass.Warrior or MirClass.Wizard or MirClass.Taoist))
                        throw new InvalidDataException(
                            "CHANGEJOB 仅接受 Warrior、Wizard 或 Taoist。" );
                    acts.Add(new NPCActions(ActionType.ChangeClass, parts[1]));
                    break;

                case "CHANGEHAIR":
                case "HAIRSTYLE" when IsLingFengCompatibility:
                    if (parts.Length < 2)
                    {
                        acts.Add(new NPCActions(ActionType.ChangeHair));
                    }
                    else
                    {
                        acts.Add(new NPCActions(ActionType.ChangeHair, parts[1]));
                    }
                    break;

                case "LOCALMESSAGE":
                    var match = regexQuote.Match(line);
                    if (match.Success)
                    {
                        var message = match.Groups[1].Captures[0].Value;

                        var last = parts.Count() - 1;
                        acts.Add(new NPCActions(ActionType.LocalMessage, message, parts[last]));
                    }
                    break;

                case "GLOBALMESSAGE":
                    match = regexQuote.Match(line);
                    if (match.Success)
                    {
                        var message = match.Groups[1].Captures[0].Value;

                        var last = parts.Count() - 1;
                        acts.Add(new NPCActions(ActionType.GlobalMessage, message, parts[last]));
                    }
                    break;

                case "GIVESKILL":
                    if (parts.Length < 3) return;

                    string spelllevel = parts.Length > 2 ? parts[2] : "0";
                    acts.Add(new NPCActions(ActionType.GiveSkill, parts[1], spelllevel));
                    break;

                case "ADDSKILL" when IsLingFengCompatibility:
                    if (parts.Length is < 2 or > 3 ||
                        (parts.Length == 3 &&
                         (!byte.TryParse(parts[2], NumberStyles.None,
                              CultureInfo.InvariantCulture, out byte addSkillLevel) ||
                          addSkillLevel > 3)))
                        throw new InvalidDataException(
                            "ADDSKILL 需要技能名称和可选的 0 到 3 级初始等级。");
                    acts.Add(new NPCActions(
                        ActionType.LingFengAddSkill,
                        parts[1], parts.Length == 3 ? parts[2] : "0"));
                    break;

                case "SKILLLEVEL" when IsLingFengCompatibility:
                    if (parts.Length is < 4 or > 5 ||
                        parts[2] is not ("+" or "-" or "=") ||
                        (parts.Length == 5 && parts[4] is not ("0" or "1")))
                        throw new InvalidDataException(
                            "SKILLLEVEL 需要技能名称、+/-/=、等级和可选的普通0或强化1标志。");
                    acts.Add(new NPCActions(
                        ActionType.LingFengSkillLevel, parts[1], parts[2], parts[3],
                        parts.Length == 5 ? parts[4] : "0"));
                    break;

                case "DELSKILL" when IsLingFengCompatibility:
                    if (parts.Length != 2)
                        throw new InvalidDataException("DELSKILL 需要且只允许一个技能名称。");
                    acts.Add(new NPCActions(ActionType.LingFengDeleteSkill, parts[1]));
                    break;

                case "CLEARSKILL" when IsLingFengCompatibility:
                    if (parts.Length != 1)
                        throw new InvalidDataException("CLEARSKILL 不接受参数。" );
                    acts.Add(new NPCActions(ActionType.LingFengClearSkills));
                    break;

                case "SETHUMATTACKMODE" when IsLingFengCompatibility:
                    if (parts.Length is not (3 or 4) ||
                        !byte.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture,
                            out byte attackModeValue) ||
                        !Enum.IsDefined(typeof(AttackMode), (AttackMode)attackModeValue) ||
                        !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture,
                            out int attackModeSeconds) ||
                        attackModeSeconds is < 1 or > 604800)
                        throw new InvalidDataException(
                            "SETHUMATTACKMODE 需要现有攻击模式、1至604800秒和可选地图或*。" );
                    acts.Add(new NPCActions(ActionType.LingFengSetAttackMode,
                        parts[1], parts[2], parts.Length == 4 ? parts[3] : string.Empty));
                    break;

                case "RENEWLEVEL" when IsLingFengCompatibility:
                    if (parts.Length != 4 ||
                        !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture,
                            out int renewTimes) || renewTimes is < 1 or > byte.MaxValue ||
                        !ushort.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture,
                            out _) ||
                        !int.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture,
                            out int renewPoints) || renewPoints is < 1 or > 20000)
                        throw new InvalidDataException(
                            "RENEWLEVEL 需要1至255转次数、0至65535转后等级和1至20000分配点数。" );
                    acts.Add(new NPCActions(ActionType.LingFengRenewLevel,
                        parts[1], parts[2], parts[3]));
                    break;

                case "KILLSLAVE" when IsLingFengCompatibility:
                    if (parts.Length is not (2 or 3) || parts[1] is not ("0" or "1") ||
                        parts.Length == 3 && string.IsNullOrWhiteSpace(parts[2]))
                        throw new InvalidDataException(
                            "KILLSLAVE 需要清尸标志0或1，以及可选宝宝名称或*。" );
                    acts.Add(new NPCActions(ActionType.LingFengKillSlaves,
                        parts[1], parts.Length == 3 ? parts[2] : "*"));
                    break;

                case "RECALLSELF" when IsLingFengCompatibility:
                    if (parts.Length is not (4 or 9) ||
                        !int.TryParse(parts[1], NumberStyles.None,
                            CultureInfo.InvariantCulture, out int cloneSeconds) ||
                        cloneSeconds < 0 ||
                        !int.TryParse(parts[2], NumberStyles.None,
                            CultureInfo.InvariantCulture, out int cloneCount) ||
                        cloneCount is < 1 or > 100 ||
                        !int.TryParse(parts[3], NumberStyles.None,
                            CultureInfo.InvariantCulture, out int clonePercent) ||
                        clonePercent is < 1 or > 1000000 ||
                        parts.Length == 9 && (!byte.TryParse(parts[4], out _) ||
                            !ushort.TryParse(parts[5], out _) ||
                            !ushort.TryParse(parts[6], out _) ||
                            !int.TryParse(parts[7], out _) ||
                            !int.TryParse(parts[8], out _)))
                        throw new InvalidDataException(
                            "RECALLSELF 需要非负秒数、1至100数量、属性百分比，以及可选颜色/衣服/武器/X/Y。 ");
                    acts.Add(new NPCActions(ActionType.LingFengRecallSelf,
                        parts.Skip(1).Concat(Enumerable.Repeat("0", 5))
                            .Take(8).ToArray()));
                    break;

                case "SETSLAVEATTACKHUMPOWERRATE" when IsLingFengCompatibility:
                    if (parts.Length != 3 ||
                        !int.TryParse(parts[2], NumberStyles.None,
                            CultureInfo.InvariantCulture, out int slaveHumanRate) ||
                        slaveHumanRate is < 0 or > 1000000)
                        throw new InvalidDataException(
                            "SETSLAVEATTACKHUMPOWERRATE 需要宝宝名称和0至1000000倍率。 ");
                    acts.Add(new NPCActions(
                        ActionType.LingFengSetSlaveAttackHumanPowerRate,
                        parts[1], parts[2]));
                    break;

                case "KILLMONEXPRATE" when IsLingFengCompatibility:
                    if (parts.Length is < 3 or > 5 ||
                        !int.TryParse(parts[1], NumberStyles.None,
                            CultureInfo.InvariantCulture, out int experienceRate) ||
                        experienceRate is < 100 or > 1_000_000 ||
                        !int.TryParse(parts[2], NumberStyles.None,
                            CultureInfo.InvariantCulture, out int experienceRateSeconds) ||
                        experienceRateSeconds < 0 ||
                        parts.Length >= 4 && parts[3] is not ("0" or "1") ||
                        parts.Length == 5 && parts[4] is not ("0" or "1"))
                        throw new InvalidDataException(
                            "KILLMONEXPRATE 需要100至1000000倍率、非负秒数、可选保存和静默标志0或1。");
                    acts.Add(new NPCActions(ActionType.LingFengKillMonsterExperienceRate,
                        parts[1], parts[2], parts.Length >= 4 ? parts[3] : "0",
                        parts.Length == 5 ? parts[4] : "0"));
                    break;

                case "POWERRATE" when IsLingFengCompatibility:
                    if (parts.Length is < 3 or > 6 ||
                        parts.Length >= 4 && parts[3] is not ("0" or "1") ||
                        parts.Length >= 5 && parts[4] is not ("0" or "1") ||
                        parts.Length == 6 && parts[5] is not ("0" or "1" or "2"))
                        throw new InvalidDataException(
                            "POWERRATE 需要倍率、时长、可选保存/静默标志0或1及目标类型0至2。");
                    acts.Add(new NPCActions(ActionType.LingFengPowerRate,
                        parts[1], parts[2], parts.Length >= 4 ? parts[3] : "0",
                        parts.Length >= 5 ? parts[4] : "0",
                        parts.Length == 6 ? parts[5] : "0"));
                    break;

                case "SETBLASTHITRATE" when IsLingFengCompatibility:
                    if (parts.Length is not (2 or 3))
                        throw new InvalidDataException(
                            "SETBLASTHITRATE 需要暴击威力百分比和可选有效秒数。");
                    acts.Add(new NPCActions(ActionType.LingFengBlastHitRate,
                        parts[1], parts.Length == 3 ? parts[2] : "0"));
                    break;

                case "KILLMONBURSTRATE" when IsLingFengCompatibility:
                    if (parts.Length is < 2 or > 5 ||
                        parts.Length >= 4 && parts[3] is not ("0" or "1") ||
                        parts.Length == 5 && parts[4] is not ("0" or "1"))
                        throw new InvalidDataException(
                            "KILLMONBURSTRATE 需要倍率、可选时长、保存和静默标志0或1。");
                    acts.Add(new NPCActions(ActionType.LingFengKillMonsterDropRate,
                        parts[1], parts.Length >= 3 ? parts[2] : "0",
                        parts.Length >= 4 ? parts[3] : "0",
                        parts.Length == 5 ? parts[4] : "0"));
                    break;

                case "SETREBORN" when IsLingFengCompatibility:
                    if (parts.Length != 3)
                        throw new InvalidDataException(
                            "SETREBORN 需要复活次数和持续有效秒数。 ");
                    acts.Add(new NPCActions(
                        ActionType.LingFengSetNpcReborn, parts[1], parts[2]));
                    break;

                case "GETMAPMONCOUNT" when IsLingFengCompatibility:
                    if (parts.Length != 4 || parts[2] is not ("0" or "1") ||
                        !TryNormalizeWritableDestination(parts[3], out string mapCountDestination))
                        throw new InvalidDataException(
                            "GETMAPMONCOUNT 需要地图、排除宝宝标志0或1和结果变量。");
                    acts.Add(new NPCActions(ActionType.LingFengGetMapMonsterCount,
                        parts[1], parts[2], mapCountDestination));
                    break;

                case "CLEARSKILLCD" when IsLingFengCompatibility:
                    if (parts.Length != 2)
                        throw new InvalidDataException("CLEARSKILLCD 需要且只允许一个技能名称。");
                    acts.Add(new NPCActions(ActionType.LingFengClearSkillCooldown, parts[1]));
                    break;

                case "KILLCALLMOB" when IsLingFengCompatibility:
                    if (parts.Length is < 2 or > 3 ||
                        (parts.Length == 3 &&
                         (!int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture,
                              out int killCalledCount) || killCalledCount <= 0)))
                        throw new InvalidDataException(
                            "KILLCALLMOB 需要宝宝名称和可选的正整数数量。");
                    acts.Add(new NPCActions(ActionType.LingFengKillCalledMonster,
                        parts[1], parts.Length == 3 ? parts[2] : int.MaxValue.ToString(CultureInfo.InvariantCulture)));
                    break;

                case "RECALLMOB" when IsLingFengCompatibility:
                    if (parts.Length is < 2 or > 8)
                        throw new InvalidDataException(
                            "RECALLMOB 需要怪物名称，以及可选的等级、叛变分钟、颜色类型、颜色值、所属技能和数量。");
                    string[] recallParameters = { parts[1], "0", "0", "0", "0", "0", "1" };
                    for (int recallIndex = 2; recallIndex < parts.Length; recallIndex++)
                        recallParameters[recallIndex - 1] = parts[recallIndex];
                    acts.Add(new NPCActions(ActionType.LingFengRecallMob, recallParameters));
                    break;

                case "SETCUSTOMITEMABIL" when IsLingFengCompatibility:
                case "H.SETCUSTOMITEMABIL" when IsLingFengCompatibility:
                    if (parts.Length != 5)
                        throw new InvalidDataException(
                            "SETCUSTOMITEMABIL 需要装备位置、属性位置、属性类型和属性值。");
                    acts.Add(new NPCActions(ActionType.LingFengSetCustomItemAbility,
                        parts[0].StartsWith("H.", StringComparison.OrdinalIgnoreCase) ? "1" : "0",
                        parts[1], parts[2], parts[3], parts[4]));
                    break;

                case "GETCUSTOMITEMABIL" when IsLingFengCompatibility:
                case "H.GETCUSTOMITEMABIL" when IsLingFengCompatibility:
                    if (parts.Length != 5 ||
                        !TryNormalizeWritableDestination(parts[4], out string customAbilityDestination))
                        throw new InvalidDataException(
                            "GETCUSTOMITEMABIL 需要装备位置、属性位置、属性类型和结果变量。");
                    acts.Add(new NPCActions(ActionType.LingFengGetCustomItemAbility,
                        parts[0].StartsWith("H.", StringComparison.OrdinalIgnoreCase) ? "1" : "0",
                        parts[1], parts[2], parts[3], customAbilityDestination));
                    break;

                case "SETCUSTOMITEMVALUE" when IsLingFengCompatibility:
                case "H.SETCUSTOMITEMVALUE" when IsLingFengCompatibility:
                    if (parts.Length != 5 || parts[3] is not ("+" or "-" or "="))
                        throw new InvalidDataException(
                            "SETCUSTOMITEMVALUE 需要装备位置、属性位置、+/-/= 和属性值。");
                    acts.Add(new NPCActions(ActionType.LingFengSetCustomItemValue,
                        parts[0].StartsWith("H.", StringComparison.OrdinalIgnoreCase) ? "1" : "0",
                        parts[1], parts[2], parts[3], parts[4], "0", "0"));
                    break;

                case "SETCUSTOMITEMVALUEEX" when IsLingFengCompatibility:
                case "H.SETCUSTOMITEMVALUEEX" when IsLingFengCompatibility:
                    if (parts.Length != 7 || parts[3] is not ("+" or "-" or "="))
                        throw new InvalidDataException(
                            "SETCUSTOMITEMVALUEEX 需要装备位置、属性位置、+/-/= 和三个属性值。");
                    acts.Add(new NPCActions(ActionType.LingFengSetCustomItemValue,
                        parts[0].StartsWith("H.", StringComparison.OrdinalIgnoreCase) ? "1" : "0",
                        parts[1], parts[2], parts[3], parts[4], parts[5], parts[6]));
                    break;

                case "GETCUSTOMITEMVALUE" when IsLingFengCompatibility:
                case "H.GETCUSTOMITEMVALUE" when IsLingFengCompatibility:
                    if (parts.Length is < 4 or > 6 ||
                        !TryNormalizeWritableDestination(parts[3], out string customValueDestination) ||
                        (parts.Length >= 5 &&
                         !TryNormalizeWritableDestination(parts[4], out _)) ||
                        (parts.Length == 6 && parts[5] is not ("0" or "1" or "2")))
                        throw new InvalidDataException(
                            "GETCUSTOMITEMVALUE 需要装备位置、属性位置、值变量、可选模式变量和值位置。" );
                    acts.Add(new NPCActions(
                        ActionType.LingFengGetCustomItemValue,
                        parts[0].StartsWith("H.", StringComparison.OrdinalIgnoreCase) ? "1" : "0",
                        parts[1], parts[2], customValueDestination,
                        parts.Length >= 5 ? NormalizeWritableDestination(parts[4]) : string.Empty,
                        parts.Length == 6 ? parts[5] : "0"));
                    break;

                case "GETCUSTOMITEMVALUEEX" when IsLingFengCompatibility:
                case "H.GETCUSTOMITEMVALUEEX" when IsLingFengCompatibility:
                    if (parts.Length is not (7 or 8) ||
                        parts.Skip(3).Any(part => !TryNormalizeWritableDestination(part, out _)))
                        throw new InvalidDataException(
                            "GETCUSTOMITEMVALUEEX 需要装备位置、属性位置、模式变量、可选显示行变量和值1至值3变量。" );
                    acts.Add(new NPCActions(
                        ActionType.LingFengGetCustomItemValue,
                        parts[0].StartsWith("H.", StringComparison.OrdinalIgnoreCase) ? "1" : "0",
                        parts[1], parts[2], "EX",
                        string.Join("\u001F", parts.Skip(3).Select(NormalizeWritableDestination))));
                    break;

                case "GETALLCUSTOMITEMVALUE" when IsLingFengCompatibility:
                case "H.GETALLCUSTOMITEMVALUE" when IsLingFengCompatibility:
                    if (parts.Length is not (4 or 6) ||
                        !TryNormalizeWritableDestination(parts[2], out _) ||
                        !TryNormalizeWritableDestination(parts[3], out _) ||
                        (parts.Length == 6 &&
                         (parts[4] is not ("0" or "1" or "2") ||
                          !TryNormalizeWritableDestination(parts[5], out _))))
                        throw new InvalidDataException(
                            "GETALLCUSTOMITEMVALUE 需要绑定类型、点数变量、单件百分比变量及可选值位置和全身百分比变量。" );
                    acts.Add(new NPCActions(
                        ActionType.LingFengGetAllCustomItemValue,
                        parts[0].StartsWith("H.", StringComparison.OrdinalIgnoreCase) ? "1" : "0",
                        parts[1], NormalizeWritableDestination(parts[2]),
                        NormalizeWritableDestination(parts[3]),
                        parts.Length >= 5 ? parts[4] : "0",
                        parts.Length == 6 ? NormalizeWritableDestination(parts[5]) : string.Empty));
                    break;

                case "SETITEMADDBYTE" when IsLingFengCompatibility:
                case "SETITEMADDINT" when IsLingFengCompatibility:
                case "SETITEMADDTEXT" when IsLingFengCompatibility:
                    if (parts.Length != 4)
                        throw new InvalidDataException("SETITEMADD* 需要装备位置、标记序号和值。" );
                    acts.Add(new NPCActions(
                        ActionType.LingFengItemMark, "SET",
                        parts[0].EndsWith("BYTE", StringComparison.OrdinalIgnoreCase) ? "BYTE" :
                        parts[0].EndsWith("INT", StringComparison.OrdinalIgnoreCase) ? "INT" : "TEXT",
                        parts[1], parts[2], parts[3]));
                    break;

                case "GETITEMADDBYTE" when IsLingFengCompatibility:
                case "GETITEMADDINT" when IsLingFengCompatibility:
                case "GETITEMADDTEXT" when IsLingFengCompatibility:
                    if (parts.Length != 4 ||
                        !TryNormalizeWritableDestination(parts[3], out string itemMarkDestination))
                        throw new InvalidDataException("GETITEMADD* 需要装备位置、标记序号和结果变量。" );
                    acts.Add(new NPCActions(
                        ActionType.LingFengItemMark, "GET",
                        parts[0].EndsWith("BYTE", StringComparison.OrdinalIgnoreCase) ? "BYTE" :
                        parts[0].EndsWith("INT", StringComparison.OrdinalIgnoreCase) ? "INT" : "TEXT",
                        parts[1], parts[2], itemMarkDestination));
                    break;

                case "CHANGEITEMADDVALUE" when IsLingFengCompatibility:
                case "H.CHANGEITEMADDVALUE" when IsLingFengCompatibility:
                    if (parts.Length != 5 ||
                        parts[3] is not ("+" or "-" or "=") ||
                        !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture,
                            out int addedAttribute) || addedAttribute is < 0 or > 14)
                        throw new InvalidDataException(
                            "CHANGEITEMADDVALUE 需要装备位置、属性序号、运算符和值。" );
                    acts.Add(new NPCActions(
                        ActionType.LingFengChangeItemAddedValue,
                        parts[0].StartsWith("H.", StringComparison.OrdinalIgnoreCase) ? "1" : "0",
                        parts[1], parts[2], parts[3], parts[4]));
                    break;

                case "CHANGEITEMNAMECOLOR" when IsLingFengCompatibility:
                case "H.CHANGEITEMNAMECOLOR" when IsLingFengCompatibility:
                    if (parts.Length != 3 ||
                        !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture,
                            out int changeNameColourPosition) || changeNameColourPosition is < 0 or > 13 ||
                        !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture,
                            out int itemNameColour) || itemNameColour is < 0 or > 255)
                        throw new InvalidDataException(
                            "CHANGEITEMNAMECOLOR 需要 0-13 的装备位置和 0-255 的颜色编号。" );
                    acts.Add(new NPCActions(
                        ActionType.LingFengChangeItemNameColour,
                        parts[0].StartsWith("H.", StringComparison.OrdinalIgnoreCase) ? "1" : "0",
                        parts[1], parts[2]));
                    break;

                case "CHANGEITEMUPGRADECOUNT" when IsLingFengCompatibility:
                case "H.CHANGEITEMUPGRADECOUNT" when IsLingFengCompatibility:
                    if (parts.Length != 4 ||
                        !int.TryParse(parts[1], NumberStyles.Integer,
                            CultureInfo.InvariantCulture, out int changeUpgradePosition) ||
                        changeUpgradePosition is < -1 or > 13 ||
                        (parts[0].StartsWith("H.", StringComparison.OrdinalIgnoreCase) &&
                         changeUpgradePosition == -1) ||
                        parts[2] is not ("+" or "-" or "="))
                        throw new InvalidDataException(
                            "CHANGEITEMUPGRADECOUNT 当前支持人物 -1/0-13 或英雄 0-13 装备位置、运算符和值。" );
                    acts.Add(new NPCActions(
                        ActionType.LingFengChangeItemUpgradeCount,
                        parts[0].StartsWith("H.", StringComparison.OrdinalIgnoreCase) ? "1" : "0",
                        parts[1], parts[2], parts[3]));
                    break;

                case "SETITEMLOOKS" when IsLingFengCompatibility:
                case "H.SETITEMLOOKS" when IsLingFengCompatibility:
                case "SETITEMSHAPE" when IsLingFengCompatibility:
                case "H.SETITEMSHAPE" when IsLingFengCompatibility:
                    if (parts.Length != 4 || parts[2] is not ("+" or "-" or "="))
                        throw new InvalidDataException(
                            $"{parts[0].ToUpperInvariant()} 需要位置、操作符和数值。" );
                    acts.Add(new NPCActions(
                        ActionType.LingFengChangeItemVisual,
                        parts[0].StartsWith("H.", StringComparison.OrdinalIgnoreCase) ? "1" : "0",
                        parts[0].EndsWith("SHAPE", StringComparison.OrdinalIgnoreCase) ? "SHAPE" : "LOOKS",
                        parts[1], parts[2], parts[3]));
                    break;

                case "SETCUSTOMITEMPROGRESSBAR" when IsLingFengCompatibility:
                case "H.SETCUSTOMITEMPROGRESSBAR" when IsLingFengCompatibility:
                    if (parts.Length < 5 ||
                        !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture,
                            out int progressIndex) ||
                        progressIndex is < 0 or >= UserItem.LingFengCustomProgressBarLimit ||
                        !int.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture,
                            out int progressField) || progressField is < 0 or > 4)
                        throw new InvalidDataException(
                            "SETCUSTOMITEMPROGRESSBAR 需要装备位置、进度条序号、字段和值。" );
                    acts.Add(new NPCActions(
                        ActionType.LingFengSetCustomItemProgressBar,
                        parts[0].StartsWith("H.", StringComparison.OrdinalIgnoreCase) ? "1" : "0",
                        parts[1], parts[2], parts[3], string.Join(" ", parts.Skip(4))));
                    break;

                case "SETCUSTOMITEMPROGRESSBARVALUE" when IsLingFengCompatibility:
                case "H.SETCUSTOMITEMPROGRESSBARVALUE" when IsLingFengCompatibility:
                    if (parts.Length != 6 || parts[4] is not ("+" or "-" or "=") ||
                        !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture,
                            out int progressValueIndex) ||
                        progressValueIndex is < 0 or >= UserItem.LingFengCustomProgressBarLimit ||
                        !int.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture,
                            out int progressValueKind) || progressValueKind is < 0 or > 2)
                        throw new InvalidDataException(
                            "SETCUSTOMITEMPROGRESSBARVALUE 需要装备位置、进度条序号、值类型、运算符和值。" );
                    acts.Add(new NPCActions(
                        ActionType.LingFengChangeCustomItemProgressBarValue,
                        parts[0].StartsWith("H.", StringComparison.OrdinalIgnoreCase) ? "1" : "0",
                        parts[1], parts[2], parts[3], parts[4], parts[5]));
                    break;

                case "GETCUSTOMITEMPROGRESSBARVALUE" when IsLingFengCompatibility:
                case "H.GETCUSTOMITEMPROGRESSBARVALUE" when IsLingFengCompatibility:
                    if (parts.Length != 5 ||
                        !TryNormalizeWritableDestination(parts[4], out string progressDestination))
                        throw new InvalidDataException(
                            "GETCUSTOMITEMPROGRESSBARVALUE 需要装备位置、进度条序号、值类型和结果变量。" );
                    acts.Add(new NPCActions(
                        ActionType.LingFengGetCustomItemProgressBarValue,
                        parts[0].StartsWith("H.", StringComparison.OrdinalIgnoreCase) ? "1" : "0",
                        parts[1], parts[2], parts[3], progressDestination));
                    break;

                case "SETCUSTOMITEMTEXT" when IsLingFengCompatibility:
                case "H.SETCUSTOMITEMTEXT" when IsLingFengCompatibility:
                    if (parts.Length < 3)
                        throw new InvalidDataException("SETCUSTOMITEMTEXT 需要装备位置和文本。" );
                    acts.Add(new NPCActions(
                        ActionType.LingFengSetCustomItemText,
                        parts[0].StartsWith("H.", StringComparison.OrdinalIgnoreCase) ? "1" : "0",
                        parts[1], "TEXT", string.Join(" ", parts.Skip(2))));
                    break;

                case "SETCUSTOMITEMTEXTCOLOR" when IsLingFengCompatibility:
                case "H.SETCUSTOMITEMTEXTCOLOR" when IsLingFengCompatibility:
                    if (parts.Length != 3)
                        throw new InvalidDataException("SETCUSTOMITEMTEXTCOLOR 需要装备位置和颜色。" );
                    acts.Add(new NPCActions(
                        ActionType.LingFengSetCustomItemText,
                        parts[0].StartsWith("H.", StringComparison.OrdinalIgnoreCase) ? "1" : "0",
                        parts[1], "COLOR", parts[2]));
                    break;

                case "SETITEMEFFECT" when IsLingFengCompatibility:
                case "H.SETITEMEFFECT" when IsLingFengCompatibility:
                    if (parts.Length is not (3 or 4) ||
                        (parts.Length == 4 &&
                         (!int.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture,
                              out int itemEffectPosition) || itemEffectPosition is < 0 or > 2)))
                        throw new InvalidDataException(
                            "SETITEMEFFECT 需要装备位置、特效编号和可选特效位置。" );
                    acts.Add(new NPCActions(
                        ActionType.LingFengSetItemEffect,
                        parts[0].StartsWith("H.", StringComparison.OrdinalIgnoreCase) ? "1" : "0",
                        parts[1], parts[2], parts.Length == 4 ? parts[3] : "0"));
                    break;

                case "SETNEWITEMVALUE" when IsLingFengCompatibility:
                case "H.SETNEWITEMVALUE" when IsLingFengCompatibility:
                    if (parts.Length != 5 || parts[3] is not ("+" or "-" or "="))
                        throw new InvalidDataException(
                            "SETNEWITEMVALUE 需要物品位置、属性类型、+/-/= 和属性值。" );
                    acts.Add(new NPCActions(
                        ActionType.LingFengSetNewItemValue,
                        parts[0].StartsWith("H.", StringComparison.OrdinalIgnoreCase) ? "1" : "0",
                        parts[1], parts[2], parts[3], parts[4]));
                    break;

                case "SETNEWITEMVALUEEX" when IsLingFengCompatibility:
                    if (parts.Length != 6 ||
                        !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture,
                            out int temporaryItemPosition) || temporaryItemPosition is < 0 or > 12 ||
                        !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture,
                            out int temporaryAttribute) || temporaryAttribute is < 0 or > 26 ||
                        parts[3] is not ("+" or "-" or "=") ||
                        !int.TryParse(parts[4], NumberStyles.None, CultureInfo.InvariantCulture,
                            out int temporaryValue) || temporaryValue < 0 ||
                        !int.TryParse(parts[5], NumberStyles.None, CultureInfo.InvariantCulture,
                            out int temporarySeconds) || temporarySeconds < 0)
                        throw new InvalidDataException(
                            "SETNEWITEMVALUEEX 需要 0-12 装备位置、0-26 属性、+/-/=、非负值和非负秒数。" );
                    acts.Add(new NPCActions(
                        ActionType.LingFengSetTemporaryNewItemValue,
                        parts[1], parts[2], parts[3], parts[4], parts[5]));
                    break;

                case "LOCKUPDATEITEM" when IsLingFengCompatibility:
                case "H.LOCKUPDATEITEM" when IsLingFengCompatibility:
                    if (parts.Length != 2)
                        throw new InvalidDataException("LOCKUPDATEITEM 需要且只允许一个装备位置。");
                    // 属性命令本身不发包；UPDATEITEM 是唯一刷新边界，因此锁定命令无需额外状态。
                    break;

                case "UPDATEITEM" when IsLingFengCompatibility:
                case "H.UPDATEITEM" when IsLingFengCompatibility:
                    if (parts.Length != 2)
                        throw new InvalidDataException("UPDATEITEM 需要且只允许一个装备位置。");
                    acts.Add(new NPCActions(ActionType.LingFengUpdateItem,
                        parts[0].StartsWith("H.", StringComparison.OrdinalIgnoreCase) ? "1" : "0",
                        parts[1]));
                    break;

                case "REMOVESKILL":
                    if (parts.Length < 2) return;

                    acts.Add(new NPCActions(ActionType.RemoveSkill, parts[1]));
                    break;

                //cant use stored var
                case "SET":
                    if (parts.Length < 3) return;
                    match = regexFlag.Match(parts[1]);
                    if (match.Success)
                    {
                        string flagIndex = match.Groups[1].Captures[0].Value;
                        acts.Add(new NPCActions(ActionType.Set, flagIndex, parts[2]));
                    }
                    break;

                case "PARAM1":
                    if (parts.Length < 2) return;

                    string instanceId = parts.Length < 3 ? "1" : parts[2];
                    acts.Add(new NPCActions(ActionType.Param1, parts[1], instanceId));
                    break;

                case "PARAM2":
                    if (parts.Length < 2) return;

                    acts.Add(new NPCActions(ActionType.Param2, parts[1]));
                    break;

                case "PARAM3":
                    if (parts.Length < 2) return;

                    acts.Add(new NPCActions(ActionType.Param3, parts[1]));
                    break;

                case "MONGEN":
                    if (parts.Length < 2) return;

                    count = parts.Length < 3 ? "1" : parts[2];
                    acts.Add(new NPCActions(ActionType.Mongen, parts[1], count));
                    break;

                case "MONGENEX" when IsLingFengCompatibility:
                    if (parts.Length is not (7 or 8))
                        throw new InvalidDataException(
                            "MONGENEX 需要参数：地图 X Y 怪物 范围 数量 [名字颜色]。");

                    acts.Add(new NPCActions(
                        ActionType.LingFengMongenEx, parts.Skip(1).ToArray()));
                    break;

                case "TIMERECALL":
                    if (parts.Length < 2) return;

                    string page = parts.Length > 2 ? parts[2] : "";

                    acts.Add(new NPCActions(ActionType.TimeRecall, parts[1], page));
                    break;

                case "TIMERECALLGROUP":
                    if (parts.Length < 2) return;

                    page = parts.Length > 2 ? parts[2] : "";

                    acts.Add(new NPCActions(ActionType.TimeRecallGroup, parts[1], page));
                    break;

                case "BREAKTIMERECALL":
                    acts.Add(new NPCActions(ActionType.BreakTimeRecall));
                    break;

                case "DELAYGOTO":
                    if (parts.Length < 3) return;

                    acts.Add(new NPCActions(ActionType.DelayGoto, parts[1], parts[2]));
                    break;

                case "MONCLEAR":
                    if (parts.Length < 2) return;

                    instanceId = parts.Length < 3 ? "1" : parts[2];

                    string mobName = parts.Length < 4 ? "" : parts[3];

                    acts.Add(new NPCActions(ActionType.MonClear, parts[1], instanceId, mobName));
                    break;

                case "CLEARMAPMON" when IsLingFengCompatibility:
                    if (parts.Length != 2)
                        throw new InvalidDataException("CLEARMAPMON 需要一个地图名称。");
                    acts.Add(new NPCActions(ActionType.MonClear, parts[1], "1", ""));
                    break;

                case "GROUPRECALL":
                    acts.Add(new NPCActions(ActionType.GroupRecall));
                    break;

                case "GROUPTELEPORT":
                    if (parts.Length < 2) return;
                    string x;
                    string y;

                    if (parts.Length == 4)
                    {
                        instanceId = "1";
                        x = parts[2];
                        y = parts[3];
                    }
                    else
                    {
                        instanceId = parts.Length < 3 ? "1" : parts[2];
                        x = parts.Length < 4 ? "0" : parts[3];
                        y = parts.Length < 5 ? "0" : parts[4];
                    }

                    acts.Add(new NPCActions(ActionType.GroupTeleport, parts[1], instanceId, x, y));
                    break;

                case "EQUAL" when IsLingFengCompatibility:
                    if (parts.Length < 3) return;
                    match = Regex.Match(
                        parts[1], @"^[A-Z][0-9]+$", RegexOptions.IgnoreCase);
                    if (match.Success)
                        acts.Add(new NPCActions(ActionType.Mov, parts[1], parts[2]));
                    else if (TryParseRuntimeVariableReference(parts[1], out _))
                        acts.Add(new NPCActions(
                            ActionType.VariableMutate, parts[1], "MOV", parts[2]));
                    break;

                case "MOV":
                    if (parts.Length < 3) return;
                    match = Regex.Match(parts[1], @"^[A-Z][0-9]+$", RegexOptions.IgnoreCase);

                    string valueToStore = parts[2];

                    if (TryParseRuntimeVariableReference(parts[1], out _))
                    {
                        string conversion = parts[2].ToUpperInvariant();
                        if (parts.Length >= 4 &&
                            (conversion == "ROUND" || conversion == "FLOOR" ||
                             conversion == "CEIL" || conversion == "TRUNC" ||
                             conversion == "PARSEDECIMAL") &&
                            TryParseRuntimeVariableReference(parts[3], out _))
                        {
                            acts.Add(new NPCActions(
                                ActionType.VariableConvert, parts[1], conversion, parts[3]));
                        }
                        else
                        {
                            acts.Add(new NPCActions(ActionType.VariableMutate, parts[1], "MOV", valueToStore));
                        }
                    }
                    else if (match.Success)
                        acts.Add(new NPCActions(ActionType.Mov, parts[1], valueToStore));

                    break;

                case "INITVAR":
                    if (parts.Length != 2 || !TryParseRuntimeVariableReference(parts[1], out _)) return;
                    acts.Add(new NPCActions(ActionType.VariableInitialize, parts[1]));
                    break;

                case "INC":
                case "DEC":
                case "MUL":
                case "DIV":
                    if (parts.Length < 3 || !TryParseRuntimeVariableReference(parts[1], out _)) return;
                    valueToStore = parts[2];
                    acts.Add(new NPCActions(
                        ActionType.VariableMutate, parts[1], parts[0].ToUpperInvariant(), valueToStore));
                    break;

                case "CALC":
                    if (parts.Length < 4) return;

                    if (TryParseRuntimeVariableReference(parts[1], out _))
                    {
                        acts.Add(new NPCActions(
                            ActionType.VariableMutate, parts[1], parts[2], parts[3]));
                        break;
                    }

                    match = Regex.Match(parts[1], @"^[A-Z][0-9]+$", RegexOptions.IgnoreCase);

                    valueToStore = parts[3];

                    if (match.Success)
                        acts.Add(new NPCActions(ActionType.Calc, "%" + parts[1], parts[2], valueToStore, parts[1].Insert(1, "-")));

                    break;

                case "M.MOV" when IsLingFengCompatibility:
                case "M.INC" when IsLingFengCompatibility:
                case "M.DEC" when IsLingFengCompatibility:
                case "M.MUL" when IsLingFengCompatibility:
                case "M.DIV" when IsLingFengCompatibility:
                    if (parts.Length != 3 ||
                        !TryParseRuntimeVariableReference(parts[1], out _))
                        throw new InvalidDataException(
                            $"{parts[0].ToUpperInvariant()} 需要目标变量和值。" );
                    acts.Add(new NPCActions(
                        ActionType.LingFengTargetVariableMutate,
                        parts[1], parts[0].Substring(2).ToUpperInvariant(), parts[2]));
                    break;

                case "GIVEFENGHAO" when IsLingFengCompatibility:
                case "H.GIVEFENGHAO" when IsLingFengCompatibility:
                    if (parts.Length < 2)
                        throw new InvalidDataException($"{parts[0].ToUpperInvariant()} 需要称号名称。");
                    bool activateFengHao = parts.Length > 2 && parts[^1] == "1";
                    int titleEnd = activateFengHao ? parts.Length - 1 : parts.Length;
                    string fengHaoTitle = string.Join(" ", parts.Skip(1).Take(titleEnd - 1));
                    if (string.IsNullOrWhiteSpace(fengHaoTitle))
                        throw new InvalidDataException($"{parts[0].ToUpperInvariant()} 的称号名称不能为空。");
                    acts.Add(new NPCActions(
                        ActionType.LingFengGiveFengHao,
                        parts[0].StartsWith("H.", StringComparison.OrdinalIgnoreCase) ? "H" : "SELF",
                        fengHaoTitle, activateFengHao ? "1" : "0"));
                    break;

                case "RECYCFENGHAO" when IsLingFengCompatibility:
                case "H.RECYCFENGHAO" when IsLingFengCompatibility:
                    if (parts.Length < 2)
                        throw new InvalidDataException(
                            $"{parts[0].ToUpperInvariant()} 需要称号名称。");
                    acts.Add(new NPCActions(
                        ActionType.LingFengRevokeFengHao,
                        parts[0].StartsWith("H.", StringComparison.OrdinalIgnoreCase)
                            ? "H"
                            : "SELF",
                        string.Join(" ", parts.Skip(1))));
                    break;

                case "SETCLIENTBUFF" when IsLingFengCompatibility:
                case "M.SETCLIENTBUFF" when IsLingFengCompatibility:
                case "L.SETCLIENTBUFF" when IsLingFengCompatibility:
                    if (parts.Length < 8 ||
                        !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture,
                            out int buffIconPackage) || buffIconPackage < 0 ||
                        !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture,
                            out int buffIconIndex) || buffIconIndex < 0 ||
                        !byte.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture,
                            out byte buffSlot) || buffSlot > 6 ||
                        !int.TryParse(parts[4], NumberStyles.None, CultureInfo.InvariantCulture,
                            out int buffSeconds) || buffSeconds < 0 ||
                        parts[5] != "0" || parts[6] != "0")
                        throw new InvalidDataException(
                            $"{parts[0].ToUpperInvariant()} 当前支持图标包、图标、槽位(0-6)、秒数、0、0和说明文本。");
                    string buffDescription = string.Join(" ", parts.Skip(7));
                    if (string.IsNullOrWhiteSpace(buffDescription) || buffDescription.Length > 256)
                        throw new InvalidDataException(
                            $"{parts[0].ToUpperInvariant()} 的说明文本不能为空或超过256字符。");
                    string buffTarget = parts[0].StartsWith("M.", StringComparison.OrdinalIgnoreCase)
                        ? "M"
                        : parts[0].StartsWith("L.", StringComparison.OrdinalIgnoreCase) ? "L" : "SELF";
                    acts.Add(new NPCActions(
                        ActionType.LingFengSetClientBuff, buffTarget,
                        parts[1], parts[2], parts[3], parts[4], buffDescription));
                    break;

                case "CLOSECLIENTBUFF" when IsLingFengCompatibility:
                    if (parts.Length != 2 ||
                        !int.TryParse(parts[1], NumberStyles.None,
                            CultureInfo.InvariantCulture, out int closeClientBuffId) ||
                        closeClientBuffId is < 1 or > 10000)
                        throw new InvalidDataException(
                            "CLOSECLIENTBUFF 需要 1 到 10000 的客户端 Buff 编号。");
                    acts.Add(new NPCActions(
                        ActionType.LingFengCloseClientBuff, parts[1]));
                    break;

                case "SETSNDACASKET" when IsLingFengCompatibility:
                    if (parts.Length != 2 || parts[1] is not ("0" or "1"))
                        throw new InvalidDataException(
                            "SETSNDACASKET 需要状态 0（灰色）或 1（彩色）。");
                    acts.Add(new NPCActions(
                        ActionType.LingFengSetJewelryCasket, parts[1]));
                    break;

                case "ACTIVATIONCASKET" when IsLingFengCompatibility:
                    if (parts.Length != 1)
                        throw new InvalidDataException("ACTIVATIONCASKET 不接受参数。");
                    acts.Add(new NPCActions(ActionType.LingFengActivateJewelryCasket));
                    break;

                case "UNALLOWITEMINTOBOX" when IsLingFengCompatibility:
                    if (parts.Length != 1)
                        throw new InvalidDataException("UNALLOWITEMINTOBOX 不接受参数。");
                    acts.Add(new NPCActions(ActionType.LingFengRejectBoxItem));
                    break;

                case "RETURNBOXITEM" when IsLingFengCompatibility:
                    if (parts.Length != 2 ||
                        !int.TryParse(parts[1], NumberStyles.None,
                            CultureInfo.InvariantCulture, out int returnBoxIndex) ||
                        returnBoxIndex is < 0 or > 31)
                        throw new InvalidDataException(
                            "RETURNBOXITEM 需要 0 到 31 的自定义 OK 框编号。");
                    acts.Add(new NPCActions(
                        ActionType.LingFengReturnBoxItem, parts[1]));
                    break;

                case "SETUPGRADEITEM" when IsLingFengCompatibility:
                    if (parts.Length != 2 ||
                        !(int.TryParse(parts[1], NumberStyles.None,
                              CultureInfo.InvariantCulture, out int upgradeItemPosition) &&
                          upgradeItemPosition is >= 0 and <= 13) &&
                        !(parts[1].StartsWith("BOXITEM", StringComparison.OrdinalIgnoreCase) &&
                          int.TryParse(parts[1].AsSpan(7), NumberStyles.None,
                              CultureInfo.InvariantCulture, out int upgradeBoxIndex) &&
                          upgradeBoxIndex is >= 0 and <= 31))
                        throw new InvalidDataException(
                            "SETUPGRADEITEM 需要 0-13 装备位置或 BoxItem0-31。" );
                    acts.Add(new NPCActions(
                        ActionType.LingFengSetUpgradeItemContext, parts[1]));
                    break;

                case "OPENITEMBOXEX" when IsLingFengCompatibility:
                    if (parts.Length < 4 ||
                        !int.TryParse(parts[1], NumberStyles.None,
                            CultureInfo.InvariantCulture, out int legacyBoxId) ||
                        legacyBoxId is < 1 or > 1000 || parts[2] is not ("0" or "1") ||
                        string.IsNullOrWhiteSpace(string.Join(" ", parts.Skip(3))))
                        throw new InvalidDataException(
                            "OPENITEMBOXEX 需要 1-1000 触发编号、0/1 回收标志和提示文本。" );
                    acts.Add(new NPCActions(
                        ActionType.LingFengOpenItemBoxEx,
                        parts[1], parts[2], string.Join(" ", parts.Skip(3))));
                    break;

                case "CHANGEITEMNAME" when IsLingFengCompatibility:
                    if (parts.Length < 3)
                        throw new InvalidDataException(
                            "CHANGEITEMNAME 需要物品位置和新名称。" );
                    string changedItemName = string.Join(" ", parts.Skip(2));
                    if (string.IsNullOrWhiteSpace(changedItemName) || changedItemName.Length > 60)
                        throw new InvalidDataException(
                            "CHANGEITEMNAME 的新名称不能为空或超过60字符。" );
                    acts.Add(new NPCActions(
                        ActionType.LingFengChangeItemName, parts[1], changedItemName));
                    break;

                case "SETBODYCOLOR" when IsLingFengCompatibility:
                    if (parts.Length is not (3 or 4))
                        throw new InvalidDataException(
                            "SETBODYCOLOR 需要颜色、秒数和可选类型。" );
                    acts.Add(new NPCActions(
                        ActionType.LingFengSetBodyColor,
                        parts[1], parts[2], parts.Length == 4 ? parts[3] : "0"));
                    break;

                case "EXTBAGPAGECOUNT" when IsLingFengCompatibility:
                case "EXTBAGOPENITEMCOUNT" when IsLingFengCompatibility:
                case "SETBIGSTORAGECOUNT" when IsLingFengCompatibility:
                    if (parts.Length != 3 || parts[1] is not ("+" or "-" or "="))
                        throw new InvalidDataException(
                            $"{parts[0].ToUpperInvariant()} 需要 +、- 或 = 以及数量。" );
                    acts.Add(new NPCActions(
                        ActionType.LingFengDeferredCompatibilityCommand,
                        new[] { parts[0].ToUpperInvariant() }.Concat(parts.Skip(1)).ToArray()));
                    break;

                case "SENDMOVEHINTMSG" when IsLingFengCompatibility:
                    if (parts.Length is < 4 or > 8)
                        throw new InvalidDataException(
                            "SENDMOVEHINTMSG 需要消息、前景色、背景色及可选坐标、时长或模式参数。" );
                    acts.Add(new NPCActions(
                        ActionType.LingFengDeferredCompatibilityCommand,
                        new[] { "SENDMOVEHINTMSG" }.Concat(parts.Skip(1)).ToArray()));
                    break;

                case "OPENAUTOPICKITEM" when IsLingFengCompatibility:
                    if (parts.Length != 8)
                        throw new InvalidDataException(
                            "OPENAUTOPICKITEM 需要类型、秒数、范围、全捡、捡丢弃、捡爆出和落地等待七个参数。" );
                    acts.Add(new NPCActions(
                        ActionType.LingFengDeferredCompatibilityCommand,
                        new[] { "OPENAUTOPICKITEM" }.Concat(parts.Skip(1)).ToArray()));
                    break;

                case "CLOSEAUTOPICKITEM" when IsLingFengCompatibility:
                case "BREAKADDSELLPLAYER" when IsLingFengCompatibility:
                case "STOPTAKEON" when IsLingFengCompatibility:
                    if (parts.Length != 1)
                        throw new InvalidDataException(
                            $"{parts[0].ToUpperInvariant()} 不接受参数。" );
                    acts.Add(new NPCActions(
                        ActionType.LingFengDeferredCompatibilityCommand,
                        parts[0].ToUpperInvariant()));
                    break;

                case "OPENBIGDIALOGBOX" when IsLingFengCompatibility:
                    if (parts.Length != 10)
                        throw new InvalidDataException(
                            "OPENBIGDIALOGBOX 需要资源包、图片、移动、位置、偏移、关闭按钮及其坐标九个参数。" );
                    acts.Add(new NPCActions(
                        ActionType.LingFengDeferredCompatibilityCommand,
                        new[] { "OPENBIGDIALOGBOX" }.Concat(parts.Skip(1)).ToArray()));
                    break;

                case "OPENITEMBOX" when IsLingFengCompatibility:
                    if (parts.Length != 2)
                        throw new InvalidDataException("OPENITEMBOX 需要一个物品或怪物名称。" );
                    acts.Add(new NPCActions(
                        ActionType.LingFengDeferredCompatibilityCommand,
                        "OPENITEMBOX", parts[1]));
                    break;

                case "SETITEMFROM" when IsLingFengCompatibility:
                    if (parts.Length is not (4 or 5))
                        throw new InvalidDataException(
                            "SETITEMFROM 需要物品位置、来源类型、值及时间类型的可选第二值。" );
                    acts.Add(new NPCActions(
                        ActionType.LingFengDeferredCompatibilityCommand,
                        new[] { "SETITEMFROM" }.Concat(parts.Skip(1)).ToArray()));
                    break;

                case "HCALL" when IsLingFengCompatibility:
                case "ADDATTACKSABUKALL" when IsLingFengCompatibility:
                case "AUTOTAKEONITEM" when IsLingFengCompatibility:
                case "CHANGEHUMNAME" when IsLingFengCompatibility:
                case "CREATEMYSHOP" when IsLingFengCompatibility:
                case "OPENGODBLESS" when IsLingFengCompatibility:
                case "PLAYSOUNDEXT" when IsLingFengCompatibility:
                case "SETOFFLINEPLAY" when IsLingFengCompatibility:
                case "SETRANKLEVELNAME" when IsLingFengCompatibility:
                case "SHOWGODBLESS" when IsLingFengCompatibility:
                case "STARTAUTOPLAYGAME" when IsLingFengCompatibility:
                case "STOPAUTOPLAYGAME" when IsLingFengCompatibility:
                case "STOPBUYUSER" when IsLingFengCompatibility:
                case "STOPTAKEOFF" when IsLingFengCompatibility:
                case "SUPERMOVEMSG" when IsLingFengCompatibility:
                case "TAKEPOSW" when IsLingFengCompatibility:
                    string deferredCommand = parts[0].ToUpperInvariant();
                    bool deferredSyntaxValid = deferredCommand switch
                    {
                        "HCALL" => parts.Length == 3 && parts[2].StartsWith("@", StringComparison.Ordinal),
                        "ADDATTACKSABUKALL" or "CHANGEHUMNAME" or "CREATEMYSHOP" or
                            "OPENGODBLESS" or "SETOFFLINEPLAY" or "SHOWGODBLESS" or
                            "TAKEPOSW" => parts.Length == 2,
                        "AUTOTAKEONITEM" => parts.Length == 3,
                        "PLAYSOUNDEXT" => parts.Length == 4,
                        "SETRANKLEVELNAME" => parts.Length >= 2,
                        "STARTAUTOPLAYGAME" or "STOPAUTOPLAYGAME" or "STOPBUYUSER" or
                            "STOPTAKEOFF" => parts.Length == 1,
                        "SUPERMOVEMSG" => parts.Length >= 8,
                        _ => false
                    };
                    if (!deferredSyntaxValid)
                        throw new InvalidDataException(
                            $"{deferredCommand} 参数格式与翎风正文不匹配。" );
                    acts.Add(new NPCActions(
                        ActionType.LingFengDeferredCompatibilityCommand,
                        new[] { deferredCommand }.Concat(parts.Skip(1)).ToArray()));
                    break;

                case "SETARRBUFF" when IsLingFengCompatibility:
                case "<$CURRRTARGETNAME>.SETARRBUFF" when IsLingFengCompatibility:
                    if (parts.Length < 10)
                        throw new InvalidDataException(
                            "SETARRBUFF 至少需要分组、按钮、资源包、图片、时间、闪烁时间、闪烁图片、帧数和备注九个参数。");
                    acts.Add(new NPCActions(
                        ActionType.LingFengSetArrBuff,
                        parts[0].StartsWith("<$CURRRTARGETNAME>",
                                StringComparison.OrdinalIgnoreCase)
                            ? new[] { "TARGET" }.Concat(parts.Skip(1)).ToArray()
                            : parts.Skip(1).ToArray()));
                    break;

                case "ADDBUTTON" when IsLingFengCompatibility:
                    if (parts.Length < 10)
                        throw new InvalidDataException(
                            "ADDBUTTON 至少需要资源包、按钮序号、三张图片、坐标、挂载位置和标题九个参数。" );
                    acts.Add(new NPCActions(
                        ActionType.LingFengAddButton, parts.Skip(1).ToArray()));
                    break;

                case "CLOSEARRBUFF" when IsLingFengCompatibility:
                    if (parts.Length != 2)
                        throw new InvalidDataException(
                            "CLOSEARRBUFF 需要一个按钮序号参数。");
                    acts.Add(new NPCActions(
                        ActionType.LingFengCloseArrBuff, parts[1]));
                    break;

                case "SCATTERMONITEMS" when IsLingFengCompatibility:
                    if (parts.Length is not (2 or 5))
                        throw new InvalidDataException(
                            "SCATTERMONITEMS 需要怪物名称，或怪物名称、地图、X、Y。");
                    acts.Add(new NPCActions(
                        ActionType.LingFengScatterMonsterItems, parts.Skip(1).ToArray()));
                    break;

                case "MONDROPITEMSEX" when IsLingFengCompatibility:
                    if (parts.Length != 5)
                        throw new InvalidDataException(
                            "MONDROPITEMSEX 需要怪物名称、物品名称、数量和掉落中心模式。");
                    acts.Add(new NPCActions(
                        ActionType.LingFengForceMonsterDropItems, parts.Skip(1).ToArray()));
                    break;

                case "ADDARRBUTTON" when IsLingFengCompatibility:
                    if (parts.Length < 10)
                        throw new InvalidDataException(
                            "ADDARRBUTTON 需要分组、触发、资源包、三态图片、位置、标题和说明九个参数。");
                    acts.Add(new NPCActions(
                        ActionType.LingFengAddArrButton, parts.Skip(1).ToArray()));
                    break;

                case "DELARRBUTTON" when IsLingFengCompatibility:
                    if (parts.Length != 2)
                        throw new InvalidDataException("DELARRBUTTON 需要一个按钮编号。");
                    acts.Add(new NPCActions(
                        ActionType.LingFengDeleteArrButton, parts[1]));
                    break;

                case "DELBOXITEM" when IsLingFengCompatibility:
                    if (parts.Length is not (2 or 3))
                        throw new InvalidDataException(
                            "DELBOXITEM 需要OK框编号和可选叠加数量。");
                    acts.Add(new NPCActions(
                        ActionType.LingFengDeleteBoxItem, parts.Skip(1).ToArray()));
                    break;

                case "OPENSTORAGEVIEW" when IsLingFengCompatibility:
                    if (parts.Length is not (2 or 4) || parts[1] is not ("0" or "1"))
                        throw new InvalidDataException(
                            "OPENSTORAGEVIEW 需要模式(0普通/1无限)和可选X、Y坐标。");
                    acts.Add(new NPCActions(
                        ActionType.LingFengOpenStorageView, parts.Skip(1).ToArray()));
                    break;

                case "OPENSTORATGE" when IsLingFengCompatibility:
                    if (parts.Length != 3 ||
                        !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture,
                            out int openStoragePage) || openStoragePage is < 2 or > 4 ||
                        parts[2] != "1")
                        throw new InvalidDataException(
                            "OPENSTORATGE 当前仅支持永久开启第 2 至 4 页，末参数必须为 1。" );
                    acts.Add(new NPCActions(ActionType.LingFengOpenStoragePage, parts[1]));
                    break;

                case "PLAYEFFECT" when IsLingFengCompatibility:
                case "M.PLAYEFFECT" when IsLingFengCompatibility:
                case "PET.PLAYEFFECT" when IsLingFengCompatibility:
                    if (parts.Length is not (7 or 10))
                        throw new InvalidDataException(
                            $"{parts[0].ToUpperInvariant()} 需要资源包、起图、张数、次数、速度、图层和可选X、Y、普通播放参数。");
                    string playEffectTarget = parts[0].StartsWith("M.", StringComparison.OrdinalIgnoreCase)
                        ? "M"
                        : parts[0].StartsWith("PET.", StringComparison.OrdinalIgnoreCase) ? "PET" : "SELF";
                    acts.Add(new NPCActions(ActionType.LingFengPlayEffect,
                        new[] { playEffectTarget }.Concat(parts.Skip(1)).ToArray()));
                    break;

                case "SETICON" when IsLingFengCompatibility:
                    if (parts.Length is not (3 or 10 or 11))
                        throw new InvalidDataException(
                            "SETICON 需要位置与清除值，或位置、资源包、图片、坐标、帧数、效果、层级、速度和可选可见范围。");
                    acts.Add(new NPCActions(
                        ActionType.LingFengSetIcon, parts.Skip(1).ToArray()));
                    break;

                case "CHANGESLAVEABILITY" when IsLingFengCompatibility:
                    if (parts.Length is not (3 or 4))
                        throw new InvalidDataException(
                            "CHANGESLAVEABILITY 需要属性编号、绝对值和可选宝宝名称。");
                    acts.Add(new NPCActions(
                        ActionType.LingFengChangeSlaveAbility,
                        parts[1], parts[2], parts.Length == 4 ? parts[3] : string.Empty));
                    break;

                case "RECALCSLAVEABILITY" when IsLingFengCompatibility:
                    if (parts.Length is < 1 or > 2)
                        throw new InvalidDataException(
                            "RECALCSLAVEABILITY 只接受可选宝宝名称。");
                    acts.Add(new NPCActions(
                        ActionType.LingFengRecalcSlaveAbility,
                        parts.Length == 2 ? parts[1] : string.Empty));
                    break;

                case "SCREENEFFECT" when IsLingFengCompatibility:
                case "STOPSCREENEFFECT" when IsLingFengCompatibility:
                    if (parts.Length != 11 || parts.Skip(1).Any(value =>
                            !int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)))
                        throw new InvalidDataException(
                            $"{parts[0].ToUpperInvariant()} 需要十个整数参数。");
                    int[] screenEffectValues = parts.Skip(1)
                        .Select(value => int.Parse(value, CultureInfo.InvariantCulture)).ToArray();
                    if (screenEffectValues[2] < 0 || screenEffectValues[3] < 0 ||
                        screenEffectValues[4] < 0 || screenEffectValues[6] < 0)
                        throw new InvalidDataException(
                            $"{parts[0].ToUpperInvariant()} 的资源包、起始图、帧数和帧间隔不能为负数。");
                    acts.Add(new NPCActions(
                        ActionType.LingFengScreenEffect,
                        parts[0].Equals("STOPSCREENEFFECT", StringComparison.OrdinalIgnoreCase) ? "1" : "0",
                        parts[1], parts[2], parts[3], parts[4], parts[5], parts[6], parts[7], parts[8],
                        parts[9], parts[10]));
                    break;

                case "MAPEFFECT" when IsLingFengCompatibility:
                    if (parts.Length is < 11 or > 13)
                        throw new InvalidDataException(
                            "MAPEFFECT 需要地图、坐标、资源、帧、次数、速度、效果、亮度及可选编号和图层。" );
                    acts.Add(new NPCActions(ActionType.LingFengMapEffect, parts.Skip(1).ToArray()));
                    break;

                case "ADDDLGEX" when IsLingFengCompatibility:
                    if (parts.Length != 10 || parts[4] is not ("0" or "1") ||
                        parts[9] is not ("0" or "1"))
                        throw new InvalidDataException(
                            "ADDDLGEX 需要编号、资源包、图片、可移动、坐标、偏移、挂载位置、外部文本和路径模式。");
                    acts.Add(new NPCActions(
                        ActionType.LingFengDialog, "ADD", parts[1], parts[2], parts[3], parts[4],
                        parts[5], parts[6], parts[7], parts[8], parts[9]));
                    break;

                case "DELDLG" when IsLingFengCompatibility:
                    if (parts.Length is < 2 or > 3 || (parts.Length == 3 && parts[2] != "0"))
                        throw new InvalidDataException("DELDLG 需要对话框编号；不允许跨用户删除。");
                    acts.Add(new NPCActions(ActionType.LingFengDialog, "REMOVE", parts[1]));
                    break;

                case "OPENMERCHANTBIGDLG" when IsLingFengCompatibility:
                    if (parts.Length is < 3 or > 11)
                        throw new InvalidDataException(
                            "OPENMERCHANTBIGDLG 需要资源包、图片，以及可选的移动、位置、坐标、关闭按钮和延续参数。");
                    acts.Add(new NPCActions(ActionType.LingFengDialog,
                        new[] { "NPC_STYLE" }.Concat(parts.Skip(1)).ToArray()));
                    break;

                case "GETDBITEMFIELDVALUE" when IsLingFengCompatibility:
                    if (parts.Length != 4 ||
                        !TryNormalizeWritableDestination(parts[3], out string itemFieldDestination))
                        throw new InvalidDataException(
                            "GETDBITEMFIELDVALUE 需要物品名称、字段名和结果变量。");
                    acts.Add(new NPCActions(
                        ActionType.LingFengGetDbItemField,
                        parts[1], parts[2].ToUpperInvariant(), itemFieldDestination));
                    break;

                case "GETDBIDXITEMFIELDVALUE" when IsLingFengCompatibility:
                    if (parts.Length != 4 ||
                        !parts[2].Equals("NAME", StringComparison.OrdinalIgnoreCase) ||
                        !TryNormalizeWritableDestination(parts[3],
                            out string indexedItemFieldDestination))
                        throw new InvalidDataException(
                            "GETDBIDXITEMFIELDVALUE 当前支持按物品IDX读取NAME字段。 ");
                    acts.Add(new NPCActions(
                        ActionType.LingFengGetDbItemFieldByIndex,
                        parts[1], "NAME", indexedItemFieldDestination));
                    break;

                case "GETBAGINFO" when IsLingFengCompatibility:
                    if (parts.Length is < 3 or > 4 ||
                        !parts[1].Equals("ITEMCOUNT", StringComparison.OrdinalIgnoreCase) &&
                        !parts[1].Equals("ITEMMAKEINDEX", StringComparison.OrdinalIgnoreCase) &&
                        !parts[1].Equals("ITEMIDX", StringComparison.OrdinalIgnoreCase) &&
                        !parts[1].Equals("ITEMNAME", StringComparison.OrdinalIgnoreCase) ||
                        !TryNormalizeWritableDestination(parts[2], out string bagInfoDestination) ||
                        parts[1].Equals("ITEMCOUNT", StringComparison.OrdinalIgnoreCase) ==
                        bagInfoDestination.StartsWith("L$", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException(
                            "GETBAGINFO 需要信息类型、匹配类型的结果变量，以及可选的 StdMode 列表。" );
                    if (parts.Length == 4 && !TryParseLingFengItemTypes(parts[3], out _))
                        throw new InvalidDataException("GETBAGINFO 的 StdMode 列表无效。" );
                    acts.Add(new NPCActions(
                        ActionType.LingFengGetBagInfo,
                        parts[1].ToUpperInvariant(), bagInfoDestination,
                        parts.Length == 4 ? parts[3] : string.Empty));
                    break;

                case "GETITEMFIELDVALUE" when IsLingFengCompatibility:
                    if (parts.Length != 4 ||
                        !TryNormalizeWritableDestination(parts[3], out string instanceFieldDestination))
                        throw new InvalidDataException(
                            "GETITEMFIELDVALUE 需要物品位置、字段名和结果变量。" );
                    acts.Add(new NPCActions(
                        ActionType.LingFengGetItemField,
                        parts[1], parts[2].ToUpperInvariant(), instanceFieldDestination));
                    break;

                case "GETBAGITEMCOUNT" when IsLingFengCompatibility:
                    if (parts.Length is not (3 or 5) ||
                        !TryNormalizeWritableDestination(parts[2], out string bagCountDestination) ||
                        (parts.Length == 5 &&
                         (parts[3] is not ("0" or "1") || parts[4] is not ("0" or "1"))))
                        throw new InvalidDataException(
                            "GETBAGITEMCOUNT 需要物品名称、结果变量，可选排除OK框和满持久标志。");
                    acts.Add(new NPCActions(
                        ActionType.LingFengGetBagItemCount, parts[1], bagCountDestination,
                        parts.Length == 5 ? parts[3] : "0",
                        parts.Length == 5 ? parts[4] : "0"));
                    break;

                case "GETITEMCOUNT" when IsLingFengCompatibility:
                    if (parts.Length != 4 || parts[1] != "0" ||
                        !TryNormalizeWritableDestination(parts[3], out string itemCountDestination))
                        throw new InvalidDataException(
                            "GETITEMCOUNT 当前支持酷明使用的背包位置 0、物品名称和结果变量。");
                    acts.Add(new NPCActions(
                        ActionType.LingFengGetBagItemCount,
                        parts[2], itemCountDestination, "0", "0"));
                    break;

                case "CLOSE" when IsLingFengCompatibility:
                case "CLOSEBIGDIALOGBOX" when IsLingFengCompatibility:
                case "CLOSEMERCHANTBIGDLG" when IsLingFengCompatibility:
                    if (parts.Length != 1)
                        throw new InvalidDataException(
                            $"{parts[0].ToUpperInvariant()} 不接受参数。");
                    acts.Add(new NPCActions(ActionType.LingFengCloseNpc));
                    break;

                case "RECLAIMITEM" when IsLingFengCompatibility:
                    if (parts.Length != 1)
                        throw new InvalidDataException("RECLAIMITEM 不接受参数。");
                    acts.Add(new NPCActions(ActionType.LingFengReclaimItem));
                    break;

                case "CALCPERCENT" when IsLingFengCompatibility:
                    if (parts.Length != 4)
                        throw new InvalidDataException("CALCPERCENT 需要数值、百分比和结果变量三个参数。");
                    if (!Regex.IsMatch(parts[3], @"^[A-Za-z][0-9]+$", RegexOptions.CultureInvariant) &&
                        !TryParseRuntimeVariableReference(parts[3], out _))
                        throw new InvalidDataException("CALCPERCENT 的结果参数必须是有效脚本变量。");
                    acts.Add(new NPCActions(ActionType.LingFengCalcPercent, parts[1], parts[2], parts[3]));
                    break;

                case "SENDMSG" when IsLingFengCompatibility:
                    if (parts.Length < 3 || !byte.TryParse(parts[1], NumberStyles.None,
                            CultureInfo.InvariantCulture, out byte messageMode) ||
                        messageMode is not (0 or 1 or 2 or 3 or 5 or 6 or 7))
                        throw new InvalidDataException("SENDMSG 仅支持 0、1、2、3、5、6、7 消息模式。");
                    acts.Add(new NPCActions(
                        ActionType.LingFengSendMessage, parts[1], string.Join(" ", parts.Skip(2))));
                    break;

                case "FILTERGLOBALMSG" when IsLingFengCompatibility:
                    if (parts.Length != 3 ||
                        !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture,
                            out int filterCategory) || filterCategory is < 1 or > 4 ||
                        parts[2] is not ("0" or "1"))
                        throw new InvalidDataException(
                            "FILTERGLOBALMSG 需要 1 到 4 的消息类型和 0/1 过滤开关。");
                    acts.Add(new NPCActions(
                        ActionType.LingFengFilterGlobalMessage, parts[1], parts[2]));
                    break;

                case "SENDCENTERMSG" when IsLingFengCompatibility:
                case "M.SENDCENTERMSG" when IsLingFengCompatibility:
                    if (parts.Length < 6 ||
                        !byte.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out _) ||
                        !byte.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out _) ||
                        !byte.TryParse(parts[^2], NumberStyles.None, CultureInfo.InvariantCulture, out _) ||
                        !byte.TryParse(parts[^1], NumberStyles.None, CultureInfo.InvariantCulture, out _))
                        throw new InvalidDataException(
                            "SENDCENTERMSG 需要前景色、背景色、文本、显示模式和持续时间。");
                    string centerMessage = string.Join(" ", parts.Skip(3).Take(parts.Length - 5));
                    if (string.IsNullOrWhiteSpace(centerMessage))
                        throw new InvalidDataException("SENDCENTERMSG 的文本不能为空。");
                    acts.Add(new NPCActions(
                        parts[0].StartsWith("M.", StringComparison.OrdinalIgnoreCase)
                            ? ActionType.LingFengSendCurrentTargetMessage
                            : ActionType.LingFengSendCenterAudienceMessage,
                        parts[^2], centerMessage));
                    break;

                case "SENDNEWLINEMSG" when IsLingFengCompatibility:
                    if (parts.Length < 9 ||
                        !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture,
                            out int lineRecipientMode) || lineRecipientMode is < 0 or > 7 ||
                        !byte.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out _) ||
                        !byte.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out _) ||
                        !int.TryParse(parts[4], NumberStyles.None, CultureInfo.InvariantCulture,
                            out int lineFontSize) || lineFontSize is < 1 or > 72 ||
                        !int.TryParse(parts[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out _) ||
                        !int.TryParse(parts[6], NumberStyles.None, CultureInfo.InvariantCulture,
                            out int lineDuration) || lineDuration <= 0 ||
                        !int.TryParse(parts[7], NumberStyles.None, CultureInfo.InvariantCulture,
                            out int lineDrawMode) || lineDrawMode is < 0 or > 2)
                        throw new InvalidDataException(
                            "SENDNEWLINEMSG 需要受众、前景色、背景色、字号、Y坐标、时长、绘制方式和文本。");
                    int lineMessageEnd = parts.Length;
                    int lineRange = 0;
                    if (parts.Length >= 11 &&
                        int.TryParse(parts[^2], NumberStyles.None, CultureInfo.InvariantCulture,
                            out int parsedLineRange) && parsedLineRange >= 0 &&
                        int.TryParse(parts[^1], NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                    {
                        lineRange = parsedLineRange;
                        lineMessageEnd -= 2;
                    }
                    else if (parts.Length >= 10 &&
                        int.TryParse(parts[^1], NumberStyles.None, CultureInfo.InvariantCulture,
                            out parsedLineRange) && parsedLineRange >= 0)
                    {
                        lineRange = parsedLineRange;
                        lineMessageEnd--;
                    }
                    string lineMessage = string.Join(" ", parts.Skip(8).Take(lineMessageEnd - 8));
                    if (string.IsNullOrWhiteSpace(lineMessage) || lineMessage.Length > 1024)
                        throw new InvalidDataException("SENDNEWLINEMSG 的文本必须为 1 到 1024 个字符。");
                    acts.Add(new NPCActions(
                        ActionType.LingFengSendAudienceMessage, parts[1], lineMessage,
                        lineRange.ToString(CultureInfo.InvariantCulture)));
                    break;

                case "SENDDELAYMSG" when IsLingFengCompatibility:
                    if (parts.Length < 6 ||
                        !int.TryParse(parts[^4], NumberStyles.None, CultureInfo.InvariantCulture,
                            out int delayedSeconds) || delayedSeconds <= 0 ||
                        !byte.TryParse(parts[^3], NumberStyles.None, CultureInfo.InvariantCulture, out _) ||
                        parts[^2] is not ("0" or "1") ||
                        !parts[^1].StartsWith("@", StringComparison.Ordinal) ||
                        parts[^1].Length is < 2 or > 128)
                        throw new InvalidDataException(
                            "SENDDELAYMSG 需要文本、正数秒数、字体颜色、换图取消标志和跳转标签。");
                    string delayedMessage = string.Join(
                        " ", parts.Skip(1).Take(parts.Length - 5));
                    if (string.IsNullOrWhiteSpace(delayedMessage) || delayedMessage.Length > 1024)
                        throw new InvalidDataException(
                            "SENDDELAYMSG 的文本必须为 1 到 1024 个字符。");
                    acts.Add(new NPCActions(
                        ActionType.LingFengSendDelayedMessage, delayedMessage,
                        delayedSeconds.ToString(CultureInfo.InvariantCulture),
                        parts[^3], parts[^2], parts[^1]));
                    break;

                case "SENDMOVEMSG" when IsLingFengCompatibility:
                    if (parts.Length < 10)
                        throw new InvalidDataException(
                            "SENDMOVEMSG 需要范围模式、颜色、Y坐标、行数、文本、字号、速度和范围。" );
                    string movingMessage = string.Join(" ", parts.Skip(6).Take(parts.Length - 9));
                    if (string.IsNullOrWhiteSpace(movingMessage))
                        throw new InvalidDataException("SENDMOVEMSG 的文本不能为空。" );
                    acts.Add(new NPCActions(
                        ActionType.LingFengSendMoveMessage,
                        parts[1], parts[2], parts[3], parts[4], parts[5], movingMessage,
                        parts[^3], parts[^2], parts[^1]));
                    break;

                case "GUILDNOTICEMSG" when IsLingFengCompatibility:
                    if (parts.Length < 4 ||
                        !byte.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture,
                            out _) ||
                        !byte.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture,
                            out _))
                        throw new InvalidDataException(
                            "GUILDNOTICEMSG 需要前景色、背景色、文本和可选范围。" );
                    string noticeScope = IsLingFengNoticeScope(parts[^1])
                        ? parts[^1].ToUpperInvariant()
                        : "ALL";
                    int noticeTextCount = noticeScope == "ALL" &&
                                          !IsLingFengNoticeScope(parts[^1])
                        ? parts.Length - 3
                        : parts.Length - 4;
                    string noticeText = string.Join(" ", parts.Skip(3).Take(noticeTextCount));
                    if (string.IsNullOrWhiteSpace(noticeText))
                        throw new InvalidDataException("GUILDNOTICEMSG 的文本不能为空。" );
                    acts.Add(new NPCActions(
                        ActionType.LingFengGuildNoticeMessage,
                        parts[1], parts[2], noticeScope, noticeText));
                    break;

                case "MESSAGEBOX" when IsLingFengCompatibility:
                    if (parts.Length < 2)
                        throw new InvalidDataException("MESSAGEBOX 需要显示内容。");
                    acts.Add(new NPCActions(
                        ActionType.LingFengMessageBox, string.Join(" ", parts.Skip(1))));
                    break;

                case "ADDHPPER" when IsLingFengCompatibility:
                case "ADDMPPER" when IsLingFengCompatibility:
                    if (parts.Length is not (3 or 4))
                        throw new InvalidDataException($"{parts[0].ToUpperInvariant()} 需要操作符、比例值和可选比例类型。");
                    acts.Add(new NPCActions(
                        ActionType.LingFengAdjustResourcePercent,
                        parts[0].Equals("ADDHPPER", StringComparison.OrdinalIgnoreCase) ? "HP" : "MP",
                        parts[1], parts[2], parts.Length == 4 ? parts[3] : "0"));
                    break;

                case "CHANGEHUMABILITY" when IsLingFengCompatibility:
                case "M.CHANGEHUMABILITY" when IsLingFengCompatibility:
                case "<$CURRRTARGETNAME>.CHANGEHUMABILITY" when IsLingFengCompatibility:
                case "CHANGEHUMABILITYPERCENTAGE" when IsLingFengCompatibility:
                case "M.CHANGEHUMABILITYPERCENTAGE" when IsLingFengCompatibility:
                case "L.CHANGEHUMABILITYPERCENTAGE" when IsLingFengCompatibility:
                    if (parts.Length is < 4 or > 6 || parts[2] is not ("+" or "-" or "="))
                        throw new InvalidDataException($"{parts[0].ToUpperInvariant()} 参数格式无效。");
                    string abilityTarget = parts[0].StartsWith("M.", StringComparison.OrdinalIgnoreCase) ||
                                           parts[0].StartsWith("<$CURRRTARGETNAME>.", StringComparison.OrdinalIgnoreCase)
                        ? "M"
                        : parts[0].StartsWith("L.", StringComparison.OrdinalIgnoreCase) ? "L" : "SELF";
                    acts.Add(new NPCActions(
                        ActionType.LingFengChangeAbility,
                        parts[1], parts[2], parts[3],
                        parts.Length >= 5 ? parts[4] : "0",
                        parts[0].EndsWith("CHANGEHUMABILITYPERCENTAGE", StringComparison.OrdinalIgnoreCase) ||
                        (parts.Length == 6 && parts[5] == "1") ? "1" : "0",
                        abilityTarget));
                    break;

                case "CHANGEMONABILITY" when IsLingFengCompatibility:
                    if (parts.Length is not (7 or 10) ||
                        parts[4] is not ("+" or "-" or "=") ||
                        parts[6] is not ("0" or "1"))
                        throw new InvalidDataException(
                            "CHANGEMONABILITY 需要地图、怪物、属性、操作符、值、值类型及可选 X Y 范围。");
                    acts.Add(new NPCActions(
                        ActionType.LingFengChangeMapMonsterAbility,
                        parts.Skip(1).ToArray()));
                    break;

                case "RECALCMONABILITY" when IsLingFengCompatibility:
                    if (parts.Length is not (3 or 6))
                        throw new InvalidDataException(
                            "RECALCMONABILITY 需要地图、怪物及可选 X Y 范围。");
                    acts.Add(new NPCActions(
                        ActionType.LingFengRecalcMapMonsterAbility,
                        parts.Skip(1).ToArray()));
                    break;

                case "CLEARITEMMAP" when IsLingFengCompatibility:
                    if (parts.Length is not (2 or 5 or 6))
                        throw new InvalidDataException(
                            "CLEARITEMMAP 需要地图及可选 X Y 范围 [物品名称]。");
                    acts.Add(new NPCActions(
                        ActionType.LingFengClearMapItems, parts.Skip(1).ToArray()));
                    break;

                case "CSVOPENCACHE" when IsLingFengCompatibility:
                    if (parts.Length != 2)
                        throw new InvalidDataException("CSVOPENCACHE 需要一个 CSV 文件路径。");
                    acts.Add(new NPCActions(ActionType.LingFengCsvOpenCache, parts[1]));
                    break;

                case "READCONFIGFILEITEM" when IsLingFengCompatibility:
                case "READCACHECONFIGFILEITEM" when IsLingFengCompatibility:
                    if (parts.Length != 5 ||
                        !TryNormalizeWritableDestination(parts[4], out string configDestination))
                        throw new InvalidDataException(
                            $"{parts[0].ToUpperInvariant()} 需要 INI 路径、节名、键名和结果变量。");
                    acts.Add(new NPCActions(
                        ActionType.LingFengReadConfigFileItem,
                        parts[1], parts[2], parts[3], configDestination));
                    break;

                case "WRITECACHECONFIGFILEITEM" when IsLingFengCompatibility:
                    if (parts.Length != 5 ||
                        string.IsNullOrWhiteSpace(parts[1]) ||
                        string.IsNullOrWhiteSpace(parts[2]) ||
                        string.IsNullOrWhiteSpace(parts[3]))
                        throw new InvalidDataException(
                            "WRITECACHECONFIGFILEITEM 需要 INI 路径、节名、键名和值。");
                    acts.Add(new NPCActions(
                        ActionType.LingFengWriteCachedConfigFileItem,
                        parts[1], parts[2], parts[3], parts[4]));
                    break;

                case "GETLISTSTRING" when IsLingFengCompatibility:
                    if (parts.Length is < 4 or > 6 ||
                        !int.TryParse(parts[2], NumberStyles.None,
                            CultureInfo.InvariantCulture, out int listLineIndex) ||
                        listLineIndex < 0 ||
                        !TryNormalizeWritableDestination(parts[3], out string listDestination) ||
                        (parts.Length >= 5 &&
                         !TryNormalizeWritableDestination(parts[4], out _)) ||
                        (parts.Length == 6 && parts[5] != "0"))
                        throw new InvalidDataException(
                            "GETLISTSTRING 需要候选 TXT 路径、从 0 开始的行号、结果变量1、[结果变量2]、[相对路径标志0]。");
                    acts.Add(new NPCActions(
                        ActionType.LingFengGetListString,
                        parts[1], parts[2], listDestination,
                        parts.Length >= 5 ? NormalizeWritableDestination(parts[4]) : string.Empty));
                    break;

                case "GETSTRINGPOS" when IsLingFengCompatibility:
                    if (parts.Length is not (3 or 4) ||
                        (parts.Length == 4 && parts[3] is not ("0" or "1")))
                        throw new InvalidDataException(
                            "GETSTRINGPOS 需要路径、字符串和可选绝对路径标志0或1。");
                    acts.Add(new NPCActions(
                        ActionType.LingFengGetStringPosition,
                        parts[1], parts[2], parts.Length == 4 ? parts[3] : "0"));
                    break;

                case "GETSTRINGPOSEX" when IsLingFengCompatibility:
                    if (parts.Length is not (6 or 7) ||
                        !TryNormalizeWritableDestination(parts[3], out string extendedPositionDestination) ||
                        !TryNormalizeWritableDestination(parts[4], out string extendedLineDestination) ||
                        parts[5] is not ("0" or "1") ||
                        parts.Length == 7 && parts[6] is not ("0" or "1"))
                        throw new InvalidDataException("GETSTRINGPOSEX 参数格式无效。" );
                    acts.Add(new NPCActions(
                        ActionType.LingFengGetStringPositionEx,
                        parts[1], parts[2], extendedPositionDestination, extendedLineDestination,
                        parts[5], parts.Length == 7 ? parts[6] : "0"));
                    break;

                case "CSVFINDTEXTROW" when IsLingFengCompatibility:
                    if (parts.Length != 7 ||
                        !Regex.IsMatch(parts[3], @"^\d+~\d+$", RegexOptions.CultureInvariant) ||
                        !int.TryParse(parts[4], NumberStyles.None, CultureInfo.InvariantCulture, out int csvColumn) ||
                        csvColumn < 0 || parts[5] is not ("0" or "1") ||
                        !IsWritableScriptVariable(parts[6]))
                        throw new InvalidDataException("CSVFINDTEXTROW 参数格式无效。");
                    acts.Add(new NPCActions(
                        ActionType.LingFengCsvFindTextRow,
                        parts[1], parts[2], parts[3], parts[4], parts[5], parts[6]));
                    break;

                case "GETRANDOMLINETEXT" when IsLingFengCompatibility:
                    if (parts.Length is < 3 or > 5 ||
                        !TryNormalizeWritableDestination(parts[2], out string randomLineDestination) ||
                        (parts.Length >= 4 && !int.TryParse(parts[3], NumberStyles.Integer,
                            CultureInfo.InvariantCulture, out _)) ||
                        (parts.Length == 5 && parts[4] is not ("0" or "1")))
                        throw new InvalidDataException("GETRANDOMLINETEXT 参数格式无效。");
                    acts.Add(new NPCActions(
                        ActionType.LingFengGetRandomLineText, parts[1], randomLineDestination,
                        parts.Length >= 4 ? parts[3] : "0", parts.Length == 5 ? parts[4] : "0"));
                    break;

                case "EXTRACTSTRING" when IsLingFengCompatibility:
                    if (parts.Length < 4)
                        throw new InvalidDataException("EXTRACTSTRING 需要分隔符、文本和至少一个结果变量。");
                    var extractParameters = new List<string> { parts[1], parts[2] };
                    for (int destinationIndex = 3; destinationIndex < parts.Length; destinationIndex++)
                    {
                        if (!TryNormalizeWritableDestination(parts[destinationIndex], out string destination))
                            throw new InvalidDataException("EXTRACTSTRING 的结果参数必须是有效脚本变量。");
                        extractParameters.Add(destination);
                    }
                    acts.Add(new NPCActions(ActionType.LingFengExtractString, extractParameters.ToArray()));
                    break;

                case "EXTRACTSTRINGEX" when IsLingFengCompatibility:
                    if (parts.Length is < 4 or > 5 ||
                        !TryNormalizeWritableDestination(parts[3], out string extractBase) ||
                        (parts.Length == 5 &&
                         !TryNormalizeWritableDestination(parts[4], out _)))
                        throw new InvalidDataException(
                            "EXTRACTSTRINGEX 需要分隔符、文本、自动编号起始变量和可选数量变量。");
                    acts.Add(new NPCActions(
                        ActionType.LingFengExtractStringEx,
                        parts[1], parts[2], extractBase,
                        parts.Length == 5 ? NormalizeWritableDestination(parts[4]) : string.Empty));
                    break;

                case "TEXTSPLIT" when IsLingFengCompatibility:
                    if (parts.Length != 5 ||
                        !TryNormalizeWritableDestination(parts[3], out string splitBase) ||
                        !TryNormalizeWritableDestination(parts[4], out string splitCount))
                        throw new InvalidDataException(
                            "TEXTSPLIT 需要分隔符、文本、自动编号起始变量和数量变量。");
                    acts.Add(new NPCActions(
                        ActionType.LingFengExtractStringEx,
                        parts[1], parts[2], splitBase, splitCount));
                    break;

                case "TEXTLENGTH" when IsLingFengCompatibility:
                    if (parts.Length != 3 ||
                        !TryNormalizeWritableDestination(parts[2], out string lengthDestination))
                        throw new InvalidDataException("TEXTLENGTH 需要文本和结果变量。");
                    acts.Add(new NPCActions(
                        ActionType.LingFengTextLength, parts[1], lengthDestination));
                    break;

                case "SETSTRINGBLANK" when IsLingFengCompatibility:
                    if (parts.Length != 4 ||
                        !TryNormalizeWritableDestination(parts[1], out string blankDestination) ||
                        !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture,
                        out int blankLength) || blankLength is < 1 or > MaximumLingFengStringBlankLength ||
                        parts[3] is not ("0" or "1"))
                        throw new InvalidDataException(
                            $"SETSTRINGBLANK 需要变量、1 到 {MaximumLingFengStringBlankLength} 的目标字节长度、前补0或后补1。");
                    acts.Add(new NPCActions(
                        ActionType.LingFengSetStringBlank,
                        blankDestination, parts[2], parts[3]));
                    break;

                case "TEXTREPLACE" when IsLingFengCompatibility:
                    if (parts.Length != 7 || parts[2].Length == 0 ||
                        !TryNormalizeWritableDestination(parts[4], out string replaceDestination) ||
                        parts[5] is not ("0" or "1") || parts[6] is not ("0" or "1"))
                        throw new InvalidDataException(
                            "TEXTREPLACE 需要原文、待替换文本、新文本、结果变量、大小写和单次标志。" );
                    acts.Add(new NPCActions(
                        ActionType.LingFengTextReplace,
                        parts[1], parts[2], parts[3], replaceDestination, parts[5], parts[6]));
                    break;

                case "UNIXTOSTR" when IsLingFengCompatibility:
                    if (parts.Length != 4 ||
                        !TryNormalizeWritableDestination(parts[2], out string unixDestination) ||
                        parts[3] is not ("0" or "1"))
                        throw new InvalidDataException(
                            "UNIXTOSTR 需要 Unix 秒时间戳、结果变量和日期分隔格式。" );
                    acts.Add(new NPCActions(
                        ActionType.LingFengUnixToString, parts[1], unixDestination, parts[3]));
                    break;

                case "RANDOMSPLIT" when IsLingFengCompatibility:
                    if (parts.Length is not (4 or 6) || parts[2] is not ("0" or "1" or "2") ||
                        !TryNormalizeWritableDestination(parts[3], out string randomDestination) ||
                        (parts.Length == 6 && (parts[4] is not ("0" or "1" or "2") ||
                            !TryNormalizeWritableDestination(parts[5], out _))))
                        throw new InvalidDataException("RANDOMSPLIT 参数格式无效。");
                    acts.Add(new NPCActions(
                        ActionType.LingFengRandomSplit, parts[1], parts[2], randomDestination,
                        parts.Length == 6 ? parts[4] : string.Empty,
                        parts.Length == 6 ? NormalizeWritableDestination(parts[5]) : string.Empty));
                    break;

                case "MOVR" when IsLingFengCompatibility:
                    if (parts.Length is not (3 or 4) ||
                        !TryNormalizeWritableDestination(parts[1], out string randomVariableDestination) ||
                        !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture,
                            out int randomMinimumOrMaximum) || randomMinimumOrMaximum < 0 ||
                        (parts.Length == 4 &&
                         (!int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture,
                             out int randomMaximum) || randomMaximum < randomMinimumOrMaximum ||
                          randomMaximum == int.MaxValue)))
                        throw new InvalidDataException("MOVR 参数格式无效。");
                    acts.Add(new NPCActions(
                        ActionType.LingFengRandomVariable,
                        randomVariableDestination,
                        parts[2], parts.Length == 4 ? parts[3] : string.Empty));
                    break;

                case "HUMANHP" when IsLingFengCompatibility:
                case "HUMANMP" when IsLingFengCompatibility:
                case "L.HUMANHP" when IsLingFengCompatibility:
                case "<$KILLER>.HUMANHP" when IsLingFengCompatibility:
                    if (parts.Length != 5 || parts[1] is not ("+" or "-" or "=") ||
                        parts[3] != "0" || parts[4] != "1")
                        throw new InvalidDataException(
                            $"{parts[0].ToUpperInvariant()} 当前仅支持命格使用的即时单次固定值格式。");
                    acts.Add(new NPCActions(
                        ActionType.LingFengAdjustResource,
                        parts[0].EndsWith("HUMANHP", StringComparison.OrdinalIgnoreCase) ? "HP" : "MP",
                        parts[1], parts[2],
                        parts[0].Equals("L.HUMANHP", StringComparison.OrdinalIgnoreCase) ||
                        parts[0].Equals("<$KILLER>.HUMANHP", StringComparison.OrdinalIgnoreCase)
                            ? "L"
                            : "SELF"));
                    break;

                case "M.HUMANHP" when IsLingFengCompatibility:
                    if (parts.Length != 9 || parts[1] is not ("+" or "-" or "=") ||
                        parts[5] is not ("0" or "1") || parts[7] is not ("0" or "1" or "2" or "3") ||
                        parts[8] is not ("0" or "1"))
                        throw new InvalidDataException(
                            "M.HUMANHP 当前支持操作符、数值、延时、次数、漂血、自定义图片、单位与护身参数的完整格式。");
                    acts.Add(new NPCActions(
                        ActionType.LingFengTimedTargetHp,
                        parts[1], parts[2], parts[3], parts[4], parts[5], parts[6], parts[7], parts[8]));
                    break;

                case "CHANGESTATE" when IsLingFengCompatibility:
                case "M.CHANGESTATE" when IsLingFengCompatibility:
                case "L.CHANGESTATE" when IsLingFengCompatibility:
                    if (parts.Length is < 3 or > 5 ||
                        !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture,
                            out int stateCode) || stateCode is not (1 or 2 or 3 or 4 or 5 or 13) ||
                        !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture,
                            out int stateDuration) || stateDuration < 0)
                        throw new InvalidDataException($"{parts[0].ToUpperInvariant()} 参数格式无效。");
                    string stateTarget = parts[0].StartsWith("M.", StringComparison.OrdinalIgnoreCase)
                        ? "M"
                        : parts[0].StartsWith("L.", StringComparison.OrdinalIgnoreCase) ? "L" : "SELF";
                    if ((stateTarget == "SELF" && (stateDuration != 0 || stateCode is not (1 or 2 or 4 or 5))) ||
                        (stateTarget == "L" && (stateCode != 1 || stateDuration <= 0)) ||
                        (stateTarget == "M" && stateDuration <= 0) ||
                        (stateCode == 13 && parts.Length != 5))
                        throw new InvalidDataException(
                            $"{parts[0].ToUpperInvariant()} 当前仅支持酷明命格使用的状态组合。");
                    acts.Add(new NPCActions(
                        ActionType.LingFengChangeState,
                        stateTarget, parts[1], parts[2],
                        parts.Length >= 4 ? parts[3] : "0",
                        parts.Length == 5 ? parts[4] : "1"));
                    break;

                case "M.MAKEPOSION" when IsLingFengCompatibility:
                case "L.MAKEPOSION" when IsLingFengCompatibility:
                    if (parts.Length is < 4 or > 6 ||
                        parts[1] is not ("0" or "1" or "5") ||
                        (parts.Length >= 5 && parts[4] is not ("0" or "1")) ||
                        (parts.Length == 6 && parts[5] is not ("0" or "1")))
                        throw new InvalidDataException("M.MAKEPOSION 参数格式无效。");
                    acts.Add(new NPCActions(
                        ActionType.LingFengMakePoison,
                        parts[0].StartsWith("L.", StringComparison.OrdinalIgnoreCase) ? "L" : "M",
                        parts[1], parts[2], parts[3],
                        parts.Length >= 5 ? parts[4] : "1",
                        parts.Length == 6 ? parts[5] : "0"));
                    break;

                case "M.GETOBJECTABILITYEX" when IsLingFengCompatibility:
                case "L.GETOBJECTABILITYEX" when IsLingFengCompatibility:
                    if (parts.Length != 3 ||
                        !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture,
                            out int targetAbilityType) || targetAbilityType is < 0 or > 15 ||
                        !TryNormalizeWritableDestination(parts[2], out string targetAbilityDestination))
                        throw new InvalidDataException("M.GETOBJECTABILITYEX 参数格式无效。");
                    acts.Add(new NPCActions(
                        ActionType.LingFengGetTargetAbility,
                        parts[0].StartsWith("L.", StringComparison.OrdinalIgnoreCase) ? "L" : "M",
                        parts[1], targetAbilityDestination));
                    break;

                case "GETDBMONSTERFIELDVALUE" when IsLingFengCompatibility:
                    if (parts.Length != 4 ||
                        !parts[2].Equals("RACE", StringComparison.OrdinalIgnoreCase) ||
                        !TryNormalizeWritableDestination(parts[3], out string monsterFieldDestination))
                        throw new InvalidDataException(
                            "GETDBMONSTERFIELDVALUE 当前支持酷明命格使用的 Race 字段。");
                    acts.Add(new NPCActions(
                        ActionType.LingFengGetMonsterField,
                        parts[1], "RACE", monsterFieldDestination));
                    break;

                case "REPAIRALL" when IsLingFengCompatibility:
                case "ACTREPAIRALL" when IsLingFengCompatibility:
                    if (parts.Length != 1)
                        throw new InvalidDataException($"{parts[0].ToUpperInvariant()} 不接受参数。");
                    acts.Add(new NPCActions(ActionType.LingFengRepairAll));
                    break;

                case "GETPLAYINFO" when IsLingFengCompatibility:
                    if (parts.Length != 3 ||
                        !parts[1].Equals("HAIR", StringComparison.OrdinalIgnoreCase) ||
                        !TryNormalizeWritableDestination(parts[2], out string playInfoDestination))
                        throw new InvalidDataException(
                            "GETPLAYINFO 当前支持酷明命格使用的 Hair 字段。");
                    acts.Add(new NPCActions(
                        ActionType.LingFengGetPlayInfo, "HAIR", playInfoDestination));
                    break;

                case "GMEXECUTE" when IsLingFengCompatibility:
                    if (parts.Length != 3 ||
                        !parts[1].Equals("探测", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException(
                            "GMEXECUTE 仅允许酷明命格使用的‘探测 人物名’。");
                    acts.Add(new NPCActions(ActionType.LingFengProbePlayer, parts[2]));
                    break;

                case "HIDEMODEEX" when IsLingFengCompatibility:
                    if (parts.Length != 3 ||
                        !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture,
                            out int hideSeconds) || hideSeconds <= 0 ||
                        parts[2] is not ("0" or "1"))
                        throw new InvalidDataException(
                            "HIDEMODEEX 需要参数：持续秒数 半透明(0/1)。");
                    acts.Add(new NPCActions(ActionType.LingFengHideModeEx, parts[1], parts[2]));
                    break;

                case "CHANGEMODE" when IsLingFengCompatibility:
                    if (parts.Length != 3 || parts[1] != "3" || parts[2] != "0")
                        throw new InvalidDataException(
                            "CHANGEMODE 仅允许酷明命格使用的‘3 0’关闭隐身。");
                    acts.Add(new NPCActions(ActionType.LingFengChangeMode));
                    break;

                case "CHANGEMODEEX" when IsLingFengCompatibility:
                    if (parts.Length is < 3 or > 4 ||
                        parts[1] is not ("1" or "2") ||
                        !int.TryParse(parts[2], NumberStyles.None,
                            CultureInfo.InvariantCulture, out int modeDuration) ||
                        modeDuration is < 0 or > 65535)
                        throw new InvalidDataException(
                            "CHANGEMODEEX 当前支持模式1无敌和模式2隐身，时间0至65535秒。 ");
                    acts.Add(new NPCActions(ActionType.LingFengChangeModeEx,
                        parts[1], parts[2]));
                    break;

                case "CHANGESPEED" when IsLingFengCompatibility:
                case "M.CHANGESPEED" when IsLingFengCompatibility:
                case "FS.CHANGESPEED" when IsLingFengCompatibility:
                    if (parts.Length is < 3 or > 4 ||
                        !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture,
                            out int speedType) || speedType is < 1 or > 3 ||
                        !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture,
                            out int speedValue) || speedValue is < -100 or > 100 ||
                        (parts.Length == 4 &&
                         (!int.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture,
                              out int speedDuration) || speedDuration < 0)))
                        throw new InvalidDataException(
                            "CHANGESPEED 需要参数：类型(1移动/2攻击/3魔法) 速度(-100..100) [秒]。");
                    acts.Add(new NPCActions(
                        ActionType.LingFengChangeSpeed, parts[1], parts[2],
                        parts.Length == 4 ? parts[3] : "0",
                        parts[0].StartsWith("M.", StringComparison.OrdinalIgnoreCase)
                            ? "M"
                            : parts[0].StartsWith("FS.", StringComparison.OrdinalIgnoreCase)
                                ? "FS"
                                : "SELF"));
                    break;

                case "CLEARDELAYGOTO" when IsLingFengCompatibility:
                    if (parts.Length > 2 ||
                        (parts.Length == 2 && parts[1] != "1"))
                        throw new InvalidDataException(
                            "CLEARDELAYGOTO 不带参数时清除 DELAYGOTO，参数1清除 SENDCENTERMSG 倒计时。");
                    acts.Add(new NPCActions(ActionType.LingFengClearDelayGoto,
                        parts.Length == 2 ? "1" : "0"));
                    break;

                case "SETSUCKDAMAGE" when IsLingFengCompatibility:
                    if (parts.Length != 5 || parts[1] is not ("+" or "-" or "=") ||
                        !int.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture,
                            out int suckPermille) || suckPermille is < 1 or > 1000 ||
                        !int.TryParse(parts[4], NumberStyles.None, CultureInfo.InvariantCulture,
                            out int suckChance) || suckChance is < 1 or > 100)
                        throw new InvalidDataException(
                            "SETSUCKDAMAGE 需要参数：运算符 总吸收值 吸收千分比 成功率。");
                    acts.Add(new NPCActions(
                        ActionType.LingFengSetSuckDamage,
                        parts[1], parts[2], parts[3], parts[4]));
                    break;

                case "RANGEHARM" when IsLingFengCompatibility:
                case "L.RANGEHARM" when IsLingFengCompatibility:
                    bool actorRangeHarm = parts[0].StartsWith(
                        "L.", StringComparison.OrdinalIgnoreCase);
                    if (!actorRangeHarm &&
                        (parts.Length != 9 || parts[5] != "0" || parts[6] != "0" ||
                         parts[7] != "1" || parts[8] is not ("0" or "1" or "2")))
                        throw new InvalidDataException(
                            "RANGEHARM 当前仅支持酷明命格的无附加状态范围伤害组合。");
                    if (actorRangeHarm && parts.Length is not (9 or 15))
                        throw new InvalidDataException(
                            "L.RANGEHARM 需要八项伤害参数及可选的六项客户端特效参数。");
                    var rangeHarmParameters = parts.Skip(1).ToList();
                    while (rangeHarmParameters.Count < 14)
                        rangeHarmParameters.Add("0");
                    rangeHarmParameters.Add(actorRangeHarm ? "L" : "SELF");
                    acts.Add(new NPCActions(
                        ActionType.LingFengRangeHarm, rangeHarmParameters.ToArray()));
                    break;

                case "RELEASEMAGIC" when IsLingFengCompatibility:
                case "RELEASEMAGICEX" when IsLingFengCompatibility:
                    if (parts.Length != 7 || parts[2] != "0" || parts[3] != "3" ||
                        parts[4] != "1" || parts[5] != "1" || parts[6] != "0")
                        throw new InvalidDataException(
                            $"{parts[0].ToUpperInvariant()} 当前仅支持酷明命格的当前技能三级无递归组合。");
                    acts.Add(new NPCActions(
                        ActionType.LingFengReleaseMagic,
                        parts[1],
                        parts[0].Equals("RELEASEMAGIC", StringComparison.OrdinalIgnoreCase) ? "1" : "0"));
                    break;

                case "FORMULATION":
                case "<$CURRRTARGETNAME>.FORMULATION" when IsLingFengCompatibility:
                    if (parts.Length < 3 || !TryParseRuntimeVariableReference(parts[^1], out _)) return;
                    acts.Add(new NPCActions(
                        parts[0].StartsWith("<$CURRRTARGETNAME>.", StringComparison.OrdinalIgnoreCase)
                            ? ActionType.LingFengTargetFormulation
                            : ActionType.VariableFormulation,
                        string.Join(" ", parts.Skip(1).Take(parts.Length - 2)),
                        parts[^1]));
                    break;

                case "SETCURRTARGET":
                    acts.Add(new NPCActions(
                        ActionType.VariableSetCurrentTarget, parts.Length > 1 ? parts[1] : string.Empty));
                    break;

                case "SETHUMVAR":
                    if (parts.Length < 4 || !TryParseRuntimeVariableReference(parts[2], out _)) return;
                    acts.Add(new NPCActions(ActionType.VariableSetHuman, parts[1], parts[2], parts[3]));
                    break;

                case "GETHUMVAR":
                    if (parts.Length < 4 || !TryParseRuntimeVariableReference(parts[2], out _) ||
                        !TryParseRuntimeVariableReference(parts[3], out _)) return;
                    acts.Add(new NPCActions(ActionType.VariableGetHuman, parts[1], parts[2], parts[3]));
                    break;

                case "ADDTOLIST":
                case "INSERTTOLIST":
                case "REPLACELISTBYINDEX":
                case "REMOVELISTBYINDEX":
                case "REMOVELISTBYCONTENT":
                case "REVERSELIST":
                case "SORTLIST":
                case "EXTRACTLIST":
                case "GETLISTVARINDEX":
                case "GETLISTVARCOUNT":
                case "GETLISTMAXVAR":
                case "GETLISTMINVAR":
                case "GETDICTKEYCOUNT":
                case "GETDICTITEMS":
                case "GETDICTMAXVALUE":
                case "GETDICTMINVALUE":
                    if (parts.Length < 2 || !TryParseRuntimeVariableReference(parts[1], out _)) return;
                    acts.Add(new NPCActions(
                        ActionType.VariableComposite,
                        new[] { parts[0].ToUpperInvariant() }.Concat(parts.Skip(1)).ToArray()));
                    break;

                case "GIVEBUFF":
                    if (parts.Length < 4) return;

                    string visible = parts.Length > 3 ? parts[3] : "";
                    string infinite = parts.Length > 4 ? parts[4] : "";
                    string stackable = parts.Length > 5 ? parts[5] : "";

                    var additionalParams = new List<string>();
                    for (int i = 6; i < parts.Length; i++)
                    {
                        additionalParams.Add(parts[i]);
                    }

                    var allParams = new List<string> { parts[1], parts[2], visible, infinite, stackable };
                    allParams.AddRange(additionalParams);

                    acts.Add(new NPCActions(ActionType.GiveBuff, allParams.ToArray()));
                    break;

                case "REMOVEBUFF":
                    if (parts.Length < 2) return;

                    acts.Add(new NPCActions(ActionType.RemoveBuff, parts[1]));
                    break;

                case "ADDTOGUILD":
                    if (parts.Length < 2) return;
                    acts.Add(new NPCActions(ActionType.AddToGuild, parts[1]));
                    break;

                case "REMOVEFROMGUILD":
                    if (parts.Length < 2) return;
                    acts.Add(new NPCActions(ActionType.RemoveFromGuild, parts[1]));
                    break;
                case "TRYREMOVEFROMGUILD" when IsLingFengCompatibility:
                    if (parts.Length != 1) return;
                    acts.Add(new NPCActions(ActionType.RemoveFromGuild));
                    break;

                case "REFRESHEFFECTS":
                    acts.Add(new NPCActions(ActionType.RefreshEffects));
                    break;

                case "CANGAINEXP":
                    if (parts.Length < 2) return;
                    acts.Add(new NPCActions(ActionType.CanGainExp, parts[1]));
                    break;

                case "COMPOSEMAIL":
                    match = regexQuote.Match(line);
                    if (match.Success)
                    {
                        var message = match.Groups[1].Captures[0].Value;

                        var last = parts.Count() - 1;
                        acts.Add(new NPCActions(ActionType.ComposeMail, message, parts[last]));
                    }
                    break;

                case "ADDMAILGOLD":
                    if (parts.Length < 2) return;
                    acts.Add(new NPCActions(ActionType.AddMailGold, parts[1]));
                    break;

                case "ADDMAILITEM":
                    if (parts.Length < 3) return;
                    acts.Add(new NPCActions(ActionType.AddMailItem, parts[1], parts[2]));
                    break;

                case "SENDMAIL":
                    acts.Add(new NPCActions(ActionType.SendMail));
                    break;

                case "GROUPGOTO":
                    if (parts.Length < 2) return;
                    acts.Add(new NPCActions(ActionType.GroupGoto, parts[1]));
                    break;

                case "ENTERMAP":
                    acts.Add(new NPCActions(ActionType.EnterMap));
                    break;
                case "MAKEWEDDINGRING":
                    acts.Add(new NPCActions(ActionType.MakeWeddingRing));
                    break;
                case "FORCEDIVORCE":
                    acts.Add(new NPCActions(ActionType.ForceDivorce));
                    break;
                case "MARRY" when IsLingFengCompatibility:
                    if (parts.Length > 3 ||
                        (parts.Length >= 2 && !parts[1].Equals(
                            "REQUESTMARRY", StringComparison.OrdinalIgnoreCase)) &&
                        (parts.Length >= 2 && !parts[1].Equals(
                            "RESPONSEMARRY", StringComparison.OrdinalIgnoreCase)) ||
                        (parts.Length == 3 &&
                         !parts[2].Equals("OK", StringComparison.OrdinalIgnoreCase) &&
                         !parts[2].Equals("FAIL", StringComparison.OrdinalIgnoreCase)))
                        throw new InvalidDataException(
                            "MARRY 仅支持无参数、REQUESTMARRY 或 RESPONSEMARRY OK|FAIL。");
                    acts.Add(new NPCActions(
                        ActionType.LingFengMarriage, parts.Skip(1).ToArray()));
                    break;
                case "UNMARRY" when IsLingFengCompatibility:
                    if (parts.Length > 3 ||
                        (parts.Length >= 2 &&
                         !parts[1].Equals("REQUESTUNMARRY", StringComparison.OrdinalIgnoreCase) &&
                         !parts[1].Equals("RESPONSEUNMARRY", StringComparison.OrdinalIgnoreCase)) ||
                        (parts.Length == 3 &&
                         !parts[2].Equals("FORCE", StringComparison.OrdinalIgnoreCase)))
                        throw new InvalidDataException(
                            "UNMARRY 仅支持无参数、REQUESTUNMARRY [FORCE] 或 RESPONSEUNMARRY。");
                    acts.Add(new NPCActions(
                        ActionType.LingFengDivorce, parts.Skip(1).ToArray()));
                    break;
                case "MASTER" when IsLingFengCompatibility:
                    if (parts.Length > 3 ||
                        (parts.Length >= 2 &&
                         !parts[1].Equals("REQUESTMASTER", StringComparison.OrdinalIgnoreCase) &&
                         !parts[1].Equals("RESPONSEMASTER", StringComparison.OrdinalIgnoreCase)) ||
                        (parts.Length == 3 &&
                         !parts[2].Equals("OK", StringComparison.OrdinalIgnoreCase) &&
                         !parts[2].Equals("FAIL", StringComparison.OrdinalIgnoreCase)))
                        throw new InvalidDataException(
                            "MASTER 仅支持无参数、REQUESTMASTER 或 RESPONSEMASTER OK|FAIL。");
                    acts.Add(new NPCActions(
                        ActionType.LingFengMentorship, parts.Skip(1).ToArray()));
                    break;
                case "UNMASTER" when IsLingFengCompatibility:
                    if (parts.Length > 3 ||
                        (parts.Length >= 2 &&
                         !parts[1].Equals("REQUESTUNMASTER", StringComparison.OrdinalIgnoreCase) &&
                         !parts[1].Equals("RESPONSEUNMASTER", StringComparison.OrdinalIgnoreCase)) ||
                        (parts.Length == 3 &&
                         !parts[2].Equals("FORCE", StringComparison.OrdinalIgnoreCase)))
                        throw new InvalidDataException(
                            "UNMASTER 仅支持无参数、REQUESTUNMASTER [FORCE] 或 RESPONSEUNMASTER。");
                    acts.Add(new NPCActions(
                        ActionType.LingFengEndMentorship, parts.Skip(1).ToArray()));
                    break;

                case "LOADVALUE":
                    if (parts.Length < 5) return;

                    quoteMatch = regexQuote.Match(line);

                    if (quoteMatch.Success)
                    {
                        fileName = Path.Combine(Settings.ValuePath, quoteMatch.Groups[1].Captures[0].Value);

                        string group = parts[parts.Length - 2];
                        string key = parts[parts.Length - 1];

                        sDirectory = Path.GetDirectoryName(fileName);
                        Directory.CreateDirectory(sDirectory);

                        acts.Add(new NPCActions(ActionType.LoadValue, parts[1], fileName, group, key));
                    }
                    break;

                case "SAVEVALUE":
                    if (parts.Length < 5) return;

                    MatchCollection matchCol = regexQuote.Matches(line);

                    if (matchCol.Count > 0 && matchCol[0].Success)
                    {
                        fileName = Path.Combine(Settings.ValuePath, matchCol[0].Groups[1].Captures[0].Value);

                        string value = parts[parts.Length - 1];

                        if (matchCol.Count > 1 && matchCol[1].Success)
                            value = matchCol[1].Groups[1].Captures[0].Value;

                        string[] newParts = line.Replace(value, string.Empty).Replace("\"", "").Trim().Split(' ');

                        string group = newParts[newParts.Length - 2];
                        string key = newParts[newParts.Length - 1];

                        sDirectory = Path.GetDirectoryName(fileName);
                        Directory.CreateDirectory(sDirectory);

                        if (!File.Exists(fileName))
                            File.Create(fileName).Close();

                        acts.Add(new NPCActions(ActionType.SaveValue, fileName, group, key, value));
                    }
                    break;
                case "CONQUESTGUARD":
                    if (parts.Length < 3) return;
                    acts.Add(new NPCActions(ActionType.ConquestGuard, parts[1], parts[2]));
                    break;
                case "CONQUESTGATE":
                    if (parts.Length < 3) return;
                    acts.Add(new NPCActions(ActionType.ConquestGate, parts[1], parts[2]));
                    break;
                case "CONQUESTWALL":
                    if (parts.Length < 3) return;
                    acts.Add(new NPCActions(ActionType.ConquestWall, parts[1], parts[2]));
                    break;
                case "TAKECONQUESTGOLD":
                    if (parts.Length < 2) return;
                    acts.Add(new NPCActions(ActionType.TakeConquestGold, parts[1]));
                    break;
                case "SETCONQUESTRATE":
                    if (parts.Length < 3) return;
                    acts.Add(new NPCActions(ActionType.SetConquestRate, parts[1], parts[2]));
                    break;
                case "STARTCONQUEST":
                    if (parts.Length < 2) return;
                    acts.Add(new NPCActions(ActionType.StartConquest, parts[1]));
                    break;
                case "SCHEDULECONQUEST":
                    if (parts.Length < 2) return;
                    acts.Add(new NPCActions(ActionType.ScheduleConquest, parts[1]));
                    break;
                case "OPENGATE":
                    if (parts.Length < 3) return;
                    acts.Add(new NPCActions(ActionType.OpenGate, parts[1], parts[2]));
                    break;
                case "CLOSEGATE":
                    if (parts.Length < 3) return;
                    acts.Add(new NPCActions(ActionType.CloseGate, parts[1], parts[2]));
                    break;
                case "OPENBROWSER":
                case "OPENWEBSITE" when IsLingFengCompatibility:
                    if (parts.Length < 2) return;
                    acts.Add(new NPCActions(ActionType.OpenBrowser, parts[1]));
                    break;
                case "GETRANDOMTEXT":
                    if (parts.Length < 3) return;
                    match = Regex.Match(parts[2], @"^[A-Z][0-9]+$", RegexOptions.IgnoreCase);
                    if (match.Success)
                        acts.Add(new NPCActions(ActionType.GetRandomText, parts[1], parts[2]));
                    break;
                case "PLAYSOUND":
                    if (parts.Length < 2) return;
                    acts.Add(new NPCActions(ActionType.PlaySound, parts[1]));
                    break;
                case "SETTIMER":
                    {
                        if (parts.Length < 4) return;

                        string global = parts.Length < 5 ? "" : parts[4];

                        acts.Add(new NPCActions(ActionType.SetTimer, parts[1], parts[2], parts[3], global));
                    }
                    break;
                case "EXPIRETIMER":
                    {
                        if (parts.Length < 2) return;

                        acts.Add(new NPCActions(ActionType.ExpireTimer, parts[1]));
                    }
                    break;

                case "UNEQUIPITEM":
                    var type = "";

                    if (parts.Length >= 2)
                    {
                        type = parts[1];
                    }

                    acts.Add(new NPCActions(ActionType.UnequipItem, type));
                    break;
                case "ROLLDIE":
                    if (parts.Length < 3) return;

                    acts.Add(new NPCActions(ActionType.RollDie, parts[1], parts[2]));
                    break;
                case "ROLLYUT":
                    if (parts.Length < 3) return;

                    acts.Add(new NPCActions(ActionType.RollYut, parts[1], parts[2]));
                    break;

                case "DROP":
                    if (parts.Length < 2) return;

                    quoteMatch = regexQuote.Match(line);

                    listPath = parts[1];

                    if (quoteMatch.Success)
                        listPath = quoteMatch.Groups[1].Captures[0].Value;

                    fileName = Path.Combine(Settings.DropPath, listPath);

                    acts.Add(new NPCActions(ActionType.Drop, fileName));
                    break;

                case "REVIVEHERO":
                    acts.Add(new NPCActions(ActionType.ReviveHero));
                    break;

                case "SEALHERO":
                    acts.Add(new NPCActions(ActionType.SealHero));
                    break;

                case "DELETEHERO":
                    acts.Add(new NPCActions(ActionType.DeleteHero));
                    break;

                case "CONQUESTREPAIRALL":
                    if (parts.Length < 2) return;

                    acts.Add(new NPCActions(ActionType.ConquestRepairAll, parts[1]));
                    break;
                case "GIVEGUILDEXP":
                    if (parts.Length < 2) return;

                    acts.Add(new NPCActions(ActionType.GiveGuildExp, parts[1]));
                    break;
            }
        }

        public List<string> ParseSay(
            PlayerObject player,
            List<string> speech,
            uint? invocationNpcObjectId = null)
        {
            for (var i = 0; i < speech.Count; i++)
            {
                if (IsLingFengCompatibility && speech[i].Contains("<$", StringComparison.Ordinal))
                {
                    ScriptTextRenderResult rendered = LingFengP0ServerSymbols.Render(
                        player, speech[i], invocationNpcObjectId);
                    ReportServerSymbolDiagnostics(rendered);
                    speech[i] = rendered.Text;
                    if (!CanUseLegacyServerSymbolFallback(rendered))
                        continue;
                }

                var parts = speech[i].Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

                if (parts.Length == 0) continue;

                foreach (var part in parts)
                {
                    speech[i] = speech[i].Replace(part, ReplaceLegacyValue(player, part));
                }
            }
            return speech;
        }

        public string ReplaceValue(
            PlayerObject player,
            string param,
            uint? invocationNpcObjectId = null)
        {
            if (IsLingFengCompatibility && param.Contains("<$", StringComparison.Ordinal))
            {
                Match variablePlaceholder = Regex.Match(param, @"\<\$(.*)\>");
                if (variablePlaceholder.Success &&
                    TryFormatScriptVariable(
                        player, variablePlaceholder.Groups[1].Value, out string variableText))
                    return param.Replace(
                        variablePlaceholder.Value, variableText, StringComparison.Ordinal);
            }

            if (IsLingFengCompatibility && param.Contains("<$", StringComparison.Ordinal))
            {
                ScriptTextRenderResult rendered = LingFengP0ServerSymbols.Render(
                    player, param, invocationNpcObjectId);
                ReportServerSymbolDiagnostics(rendered);
                if (!string.Equals(rendered.Text, param, StringComparison.Ordinal))
                    param = rendered.Text;
                if (!CanUseLegacyServerSymbolFallback(rendered))
                    return param;
            }

            return ReplaceLegacyValue(player, param);
        }

        private static void ReportServerSymbolDiagnostics(ScriptTextRenderResult rendered)
        {
            foreach (ScriptTextDiagnostic diagnostic in rendered.Diagnostics)
            {
                if (diagnostic.SymbolStatus == ServerSymbolStatus.CompatibilitySubstitute)
                    continue;
                MessageQueue.EnqueueDebugging(
                    $"[翎风服务器常量] {diagnostic.SymbolStatus}: {diagnostic.CanonicalName} {diagnostic.Message}");
            }
        }

        private static bool CanUseLegacyServerSymbolFallback(ScriptTextRenderResult rendered)
        {
            if (rendered.Status is ScriptTextRenderStatus.Unchanged or ScriptTextRenderStatus.Rendered)
                return true;

            return rendered.Status == ScriptTextRenderStatus.CompletedWithDiagnostics &&
                   rendered.Diagnostics.All(diagnostic =>
                       diagnostic.SymbolStatus is ServerSymbolStatus.Unsupported or
                           ServerSymbolStatus.CompatibilitySubstitute);
        }

        private string ReplaceLegacyValue(PlayerObject player, string param)
        {
            var regex = new Regex(@"\<\$(.*)\>");
            var varRegex = new Regex(@"(.*?)\(([A-Z][0-9]+)\)");
            var oneValRegex = new Regex(@"(.*?)\(((.*?))\)");
            var twoValRegex = new Regex(@"(.*?)\(((.*?),(.*?))\)");
            ConquestObject Conquest;
            ConquestGuildArcherInfo 弓箭;
            ConquestGuildGateInfo Gate;
            ConquestGuildWallInfo Wall;
            ConquestGuildSiegeInfo Siege;

            var match = regex.Match(param);

            if (!match.Success) return param;

            if (TryFormatScriptVariable(player, match.Groups[1].Value, out var variableText))
                return param.Replace(match.Value, variableText);

            string innerMatch = match.Groups[1].Captures[0].Value.ToUpper();

            Match varMatch = varRegex.Match(innerMatch);
            Match oneValMatch = oneValRegex.Match(innerMatch);
            Match twoValMatch = twoValRegex.Match(innerMatch);

            if (varRegex.Match(innerMatch).Success)
                innerMatch = innerMatch.Replace(varMatch.Groups[2].Captures[0].Value.ToUpper(), "");
            else if (twoValRegex.Match(innerMatch).Success)
                innerMatch = innerMatch.Replace(twoValMatch.Groups[2].Captures[0].Value.ToUpper(), "");
            else if (oneValRegex.Match(innerMatch).Success)
                innerMatch = innerMatch.Replace(oneValMatch.Groups[2].Captures[0].Value.ToUpper(), "");

            string newValue = string.Empty;

            switch (innerMatch)
            {
                case "MONSTERCOUNT()":
                    Map map = Envir.GetMapByNameAndInstance(oneValMatch.Groups[2].Captures[0].Value.ToUpper());
                    newValue = map == null ? "N/A" : map.MonsterCount.ToString();
                    break;
                case "CONQUESTGUARD()":
                    var val1 = FindVariable(player, "%" + twoValMatch.Groups[3].Captures[0].Value.ToUpper());
                    var val2 = FindVariable(player, "%" + twoValMatch.Groups[4].Captures[0].Value.ToUpper());

                    int intVal1, intVal2;

                    if (int.TryParse(val1.Replace("%", ""), out intVal1) && int.TryParse(val2.Replace("%", ""), out intVal2))
                    {

                        Conquest = Envir.Conquests.FirstOrDefault(x => x.Info.Index == intVal1);
                        if (Conquest == null) return "未设置";

                        弓箭 = Conquest.ArcherList.FirstOrDefault(x => x.Index == intVal2);
                        if (弓箭 == null) return "未设置";

                        if (弓箭.Info.Name == "" || 弓箭.Info.Name == null)
                            newValue = "Conquest Guard";
                        else
                            newValue = 弓箭.Info.Name;

                        if (弓箭.GetRepairCost() == 0)
                            newValue += " - [ 正常状态 ]";
                        else
                            newValue += " - [ " + 弓箭.GetRepairCost().ToString("#,##0") + " 金币雇佣 ]";
                    }
                    break;
                case "CONQUESTGATE()":
                    val1 = FindVariable(player, "%" + twoValMatch.Groups[3].Captures[0].Value.ToUpper());
                    val2 = FindVariable(player, "%" + twoValMatch.Groups[4].Captures[0].Value.ToUpper());

                    if (int.TryParse(val1.Replace("%", ""), out intVal1) && int.TryParse(val2.Replace("%", ""), out intVal2))
                    {
                        Conquest = Envir.Conquests.FirstOrDefault(x => x.Info.Index == intVal1);
                        if (Conquest == null) return "未设置";

                        Gate = Conquest.GateList.FirstOrDefault(x => x.Index == intVal2);
                        if (Gate == null) return "未设置";

                        if (Gate.Info.Name == "" || Gate.Info.Name == null)
                            newValue = "Conquest Gate";
                        else
                            newValue = Gate.Info.Name;

                        if (Gate.GetRepairCost() == 0)
                            newValue += " - [ 无需维修 ]";
                        else
                            newValue += " - [ " + Gate.GetRepairCost().ToString("#,##0") + " 金币维修 ]";
                    }
                    break;
                case "CONQUESTWALL()":
                    val1 = FindVariable(player, "%" + twoValMatch.Groups[3].Captures[0].Value.ToUpper());
                    val2 = FindVariable(player, "%" + twoValMatch.Groups[4].Captures[0].Value.ToUpper());

                    if (int.TryParse(val1.Replace("%", ""), out intVal1) && int.TryParse(val2.Replace("%", ""), out intVal2))
                    {
                        Conquest = Envir.Conquests.FirstOrDefault(x => x.Info.Index == intVal1);
                        if (Conquest == null) return "未设置";

                        Wall = Conquest.WallList.FirstOrDefault(x => x.Index == intVal2);
                        if (Wall == null) return "未设置";

                        if (Wall.Info.Name == "" || Wall.Info.Name == null)
                            newValue = "Conquest Wall";
                        else
                            newValue = Wall.Info.Name;

                        if (Wall.GetRepairCost() == 0)
                            newValue += " - [ 无需维修 ]";
                        else
                            newValue += " - [ " + Wall.GetRepairCost().ToString("#,##0") + " 金币维修 ]";
                    }
                    break;
                case "CONQUESTSIEGE()":
                    val1 = FindVariable(player, "%" + twoValMatch.Groups[3].Captures[0].Value.ToUpper());
                    val2 = FindVariable(player, "%" + twoValMatch.Groups[4].Captures[0].Value.ToUpper());

                    if (int.TryParse(val1.Replace("%", ""), out intVal1) && int.TryParse(val2.Replace("%", ""), out intVal2))
                    {
                        Conquest = Envir.Conquests.FirstOrDefault(x => x.Info.Index == intVal1);
                        if (Conquest == null) return "未设置";

                        Siege = Conquest.SiegeList.FirstOrDefault(x => x.Index == intVal2);
                        if (Siege == null) return "未设置";

                        if (Siege.Info.Name == "" || Siege.Info.Name == null)
                            newValue = "Conquest Siege";
                        else
                            newValue = Siege.Info.Name;

                        if (Siege.GetRepairCost() == 0)
                            newValue += " - [ 正常状态 ]";
                        else
                            newValue += " - [ " + Siege.GetRepairCost().ToString("#,##0") + " 金币 ]";
                    }
                    break;
                case "CONQUESTOWNER()":
                    val1 = FindVariable(player, "%" + oneValMatch.Groups[2].Captures[0].Value.ToUpper());

                    if (int.TryParse(val1.Replace("%", ""), out intVal1))
                    {
                        Conquest = Envir.Conquests.FirstOrDefault(x => x.Info.Index == intVal1);
                        if (Conquest == null) return string.Empty;
                        if (Conquest.Guild == null) return "虚位以待";

                        newValue = Conquest.Guild.Name;
                    }
                    break;
                case "CONQUESTGOLD()":
                    val1 = FindVariable(player, "%" + oneValMatch.Groups[2].Captures[0].Value.ToUpper());

                    if (int.TryParse(val1.Replace("%", ""), out intVal1))
                    {
                        Conquest = Envir.Conquests.FirstOrDefault(x => x.Info.Index == intVal1);
                        if (Conquest == null) return string.Empty;

                        newValue = Conquest.GuildInfo.GoldStorage.ToString();
                    }
                    break;
                case "CONQUESTRATE()":
                    val1 = FindVariable(player, "%" + oneValMatch.Groups[2].Captures[0].Value.ToUpper());

                    if (int.TryParse(val1.Replace("%", ""), out intVal1))
                    {
                        Conquest = Envir.Conquests.FirstOrDefault(x => x.Info.Index == intVal1);
                        if (Conquest == null) return string.Empty;

                        newValue = Conquest.GuildInfo.NPCRate.ToString() + "%";
                    }
                    break;
                case "CONQUESTSCHEDULE()":
                    val1 = FindVariable(player, "%" + oneValMatch.Groups[2].Captures[0].Value.ToUpper());

                    if (int.TryParse(val1.Replace("%", ""), out intVal1))
                    {
                        Conquest = Envir.Conquests.FirstOrDefault(x => x.Info.Index == intVal1);
                        if (Conquest == null) return "Conquest Not Found";
                        if (Conquest.GuildInfo.AttackerID == -1) return "No War Scheduled";

                        if (Envir.Guilds.FirstOrDefault(x => x.Guildindex == Conquest.GuildInfo.AttackerID) == null)
                        {
                            newValue = "No War Scheduled";
                        }
                        else
                        {
                            newValue = (Envir.Guilds.FirstOrDefault(x => x.Guildindex == Conquest.GuildInfo.AttackerID).Name);
                        }
                    }
                    break;
                case "OUTPUT()":
                    newValue = FindVariable(player, "%" + varMatch.Groups[2].Captures[0].Value.ToUpper());
                    break;
                case "NPCNAME":
                    for (int i = 0; i < player.CurrentMap.NPCs.Count; i++)
                    {
                        NPCObject ob = player.CurrentMap.NPCs[i];
                        if (ob.ObjectID != player.NPCObjectID) continue;
                        newValue = ob.Name.Replace("_", " ");
                    }
                    break;
                case "USERNAME":
                    newValue = player.Name;
                    break;
                case "LEVEL":
                    newValue = player.Level.ToString(CultureInfo.InvariantCulture);
                    break;
                case "CLASS":
                    newValue = player.Class.ToString();
                    break;
                case "MAP":
                    newValue = player.CurrentMap.Info.FileName;
                    break;
                case "MAPNAME":
                    newValue = player.CurrentMap.Info.Title;
                    break;
                case "X_COORD":
                    newValue = player.CurrentLocation.X.ToString();
                    break;
                case "Y_COORD":
                    newValue = player.CurrentLocation.Y.ToString();
                    break;
                case "HP":
                    newValue = player.HP.ToString(CultureInfo.InvariantCulture);
                    break;
                case "MAXHP":
                    newValue = player.Stats[Stat.HP].ToString(CultureInfo.InvariantCulture);
                    break;
                case "MP":
                    newValue = player.MP.ToString(CultureInfo.InvariantCulture);
                    break;
                case "MAXMP":
                    newValue = player.Stats[Stat.MP].ToString(CultureInfo.InvariantCulture);
                    break;
                case "GAMEGOLD":
                    newValue = player.Account.Gold.ToString(CultureInfo.InvariantCulture);
                    break;
                case "CREDIT":
                    newValue = player.Account.Credit.ToString(CultureInfo.InvariantCulture);
                    break;
                case "ARMOUR":
                    newValue = player.Info.Equipment[(int)EquipmentSlot.盔甲] != null ?
                        player.Info.Equipment[(int)EquipmentSlot.盔甲].FriendlyName : "空";
                    break;
                case "WEAPON":
                    newValue = player.Info.Equipment[(int)EquipmentSlot.武器] != null ?
                        player.Info.Equipment[(int)EquipmentSlot.武器].FriendlyName : "空";
                    break;
                case "RING_L":
                    newValue = player.Info.Equipment[(int)EquipmentSlot.左戒指] != null ?
                        player.Info.Equipment[(int)EquipmentSlot.左戒指].FriendlyName : "空";
                    break;
                case "RING_R":
                    newValue = player.Info.Equipment[(int)EquipmentSlot.右戒指] != null ?
                        player.Info.Equipment[(int)EquipmentSlot.右戒指].FriendlyName : "空";
                    break;
                case "BRACELET_L":
                    newValue = player.Info.Equipment[(int)EquipmentSlot.左手镯] != null ?
                        player.Info.Equipment[(int)EquipmentSlot.左手镯].FriendlyName : "空";
                    break;
                case "BRACELET_R":
                    newValue = player.Info.Equipment[(int)EquipmentSlot.右手镯] != null ?
                        player.Info.Equipment[(int)EquipmentSlot.右手镯].FriendlyName : "空";
                    break;
                case "NECKLACE":
                    newValue = player.Info.Equipment[(int)EquipmentSlot.项链] != null ?
                        player.Info.Equipment[(int)EquipmentSlot.项链].FriendlyName : "空";
                    break;
                case "BELT":
                    newValue = player.Info.Equipment[(int)EquipmentSlot.腰带] != null ?
                        player.Info.Equipment[(int)EquipmentSlot.腰带].FriendlyName : "空";
                    break;
                case "BOOTS":
                    newValue = player.Info.Equipment[(int)EquipmentSlot.靴子] != null ?
                        player.Info.Equipment[(int)EquipmentSlot.靴子].FriendlyName : "空";
                    break;
                case "HELMET":
                    newValue = player.Info.Equipment[(int)EquipmentSlot.头盔] != null ?
                        player.Info.Equipment[(int)EquipmentSlot.头盔].FriendlyName : "空";
                    break;
                case "AMULET":
                    newValue = player.Info.Equipment[(int)EquipmentSlot.护身符] != null ?
                        player.Info.Equipment[(int)EquipmentSlot.护身符].FriendlyName : "空";
                    break;
                case "STONE":
                    newValue = player.Info.Equipment[(int)EquipmentSlot.守护石] != null ?
                        player.Info.Equipment[(int)EquipmentSlot.守护石].FriendlyName : "空";
                    break;
                case "TORCH":
                    newValue = player.Info.Equipment[(int)EquipmentSlot.照明物] != null ?
                        player.Info.Equipment[(int)EquipmentSlot.照明物].FriendlyName : "空";
                    break;

                case "DATE":
                    newValue = Envir.Now.ToShortDateString();
                    break;
                case "USERCOUNT":
                    newValue = Envir.PlayerCount.ToString(CultureInfo.InvariantCulture);
                    break;
                case "PKPOINT":
                    newValue = player.PKPoints.ToString();
                    break;
                case "GUILDWARTIME":
                    newValue = Settings.Guild_WarTime.ToString();
                    break;
                case "GUILDWARFEE":
                    newValue = Settings.Guild_WarCost.ToString();
                    break;
                case "PARCELAMOUNT":
                    newValue = player.GetMailAwaitingCollectionAmount().ToString();
                    break;
                case "GUILDNAME":
                    if (player.MyGuild == null) return "未入行会";
                    else
                        newValue = player.MyGuild.Name; //newValue = player.MyGuild.Name + " Guild";
                    break;
                case "ROLLRESULT":
                    newValue = player.NPCData.TryGetValue("NPCRollResult", out object _rollResult) ? _rollResult.ToString() : "Not Rolled";
                    break;
                case "MOUNTLOYALTY":
                    if (!player.Mount.HasMount)
                    {
                        newValue = "No Mount";
                    }
                    else
                    {
                        newValue = $"{player.Info.Equipment[(int)EquipmentSlot.坐骑].CurrentDura} ({player.Info.Equipment[(int)EquipmentSlot.坐骑].MaxDura}";
                    }
                    break;
                case "MOUNT":
                    if (player.Mount.HasMount)
                    {
                        newValue = player.Info.Equipment[(int)EquipmentSlot.坐骑].FriendlyName;
                    }
                    else
                    {
                        newValue = "No Mount";
                    }
                    break;
                default:
                    newValue = string.Empty;
                    break;
            }

            if (string.IsNullOrEmpty(newValue)) return param;

            return param.Replace(match.Value, newValue);
        }
        public string ReplaceValue(MonsterObject Monster, string param)
        {
            var regex = new Regex(@"\<\$(.*)\>");
            var varRegex = new Regex(@"(.*?)\(([A-Z][0-9]+)\)");

            var match = regex.Match(param);

            if (!match.Success) return param;

            string innerMatch = match.Groups[1].Captures[0].Value.ToUpper();

            Match varMatch = varRegex.Match(innerMatch);

            if (varRegex.Match(innerMatch).Success)
                innerMatch = innerMatch.Replace(varMatch.Groups[2].Captures[0].Value.ToUpper(), "");

            string newValue = string.Empty;

            switch (innerMatch)
            {
                case "OUTPUT()":
                    newValue = FindVariable(Monster, "%" + varMatch.Groups[2].Captures[0].Value.ToUpper());
                    break;
                case "USERNAME":
                    newValue = Monster.Name;
                    break;
                case "LEVEL":
                    newValue = Monster.Level.ToString(CultureInfo.InvariantCulture);
                    break;
                case "MAP":
                    newValue = Monster.CurrentMap.Info.FileName;
                    break;
                case "MAPNAME":
                    newValue = Monster.CurrentMap.Info.Title;
                    break;
                case "X_COORD":
                    newValue = Monster.CurrentLocation.X.ToString();
                    break;
                case "Y_COORD":
                    newValue = Monster.CurrentLocation.Y.ToString();
                    break;
                case "HP":
                    newValue = Monster.HP.ToString(CultureInfo.InvariantCulture);
                    break;
                case "MAXHP":
                    newValue = Monster.Stats[Stat.HP].ToString(CultureInfo.InvariantCulture);
                    break;
                case "DATE":
                    newValue = Envir.Now.ToShortDateString();
                    break;
                case "USERCOUNT":
                    newValue = Envir.PlayerCount.ToString(CultureInfo.InvariantCulture);
                    break;
                case "GUILDWARTIME":
                    newValue = Settings.Guild_WarTime.ToString();
                    break;
                case "GUILDWARFEE":
                    newValue = Settings.Guild_WarCost.ToString();
                    break;
                default:
                    newValue = string.Empty;
                    break;
            }

            if (string.IsNullOrEmpty(newValue)) return param;

            return param.Replace(match.Value, newValue);
        }

        public bool Check()
        {
            if (!ShouldAllowLegacyTxtExecution())
            {
                if (Settings.TxtScriptsLogDispatch)
                    MessageQueue.Enqueue($"[TxtScripts] CSharpScriptsFallbackToTxt=false，阻止 legacy NPCSegment.Check：page={Page?.Key} segment={Key}");

                return false;
            }

            var failed = false;

            for (int i = 0; i < CheckList.Count; i++)
            {
                NPCChecks check = CheckList[i];
                List<string> param = check.Params.ToList();

                uint tempUint;
                int tempInt;
                int tempInt2;
                Map map;
                switch (check.Type)
                {
                    case CheckType.LingFengRenewLevel:
                    case CheckType.LingFengFengHao:
                    case CheckType.LingFengActiveFengHao:
                    case CheckType.LingFengSlaveCount:
                    case CheckType.LingFengMirrorMap:
                    case CheckType.LingFengEquippedItem:
                    case CheckType.LingFengRepairAllGold:
                    case CheckType.LingFengDeferredCompatibilityCheck:
                        failed = true;
                        break;
                    case CheckType.CheckDay:
                        var day = Envir.Now.DayOfWeek.ToString().ToUpper();
                        var dayToCheck = param[0].ToUpper();

                        failed = day != dayToCheck;
                        break;

                    case CheckType.CheckHour:
                        if (!uint.TryParse(param[0], out tempUint))
                        {
                            failed = true;
                            break;
                        }

                        var hour = Envir.Now.Hour;
                        var hourToCheck = tempUint;

                        failed = hour != hourToCheck;
                        break;

                    case CheckType.CheckMinute:
                        if (!uint.TryParse(param[0], out tempUint))
                        {
                            failed = true;
                            break;
                        }

                        var minute = Envir.Now.Minute;
                        var minuteToCheck = tempUint;

                        failed = minute != minuteToCheck;
                        break;

                    case CheckType.CheckHum:
                        if (!int.TryParse(param[1], out tempInt) || !int.TryParse(param[3], out tempInt2))
                        {
                            failed = true;
                            break;
                        }

                        map = Envir.GetMapByNameAndInstance(param[2], tempInt2);
                        if (map == null)
                        {
                            failed = true;
                            break;
                        }

                        failed = !Compare(param[0], map.Players.Count(), tempInt);

                        break;

                    case CheckType.CheckMon:
                        if (!int.TryParse(param[1], out tempInt) || !int.TryParse(param[3], out tempInt2))
                        {
                            failed = true;
                            break;
                        }

                        map = Envir.GetMapByNameAndInstance(param[2], tempInt2);
                        if (map == null)
                        {
                            failed = true;
                            break;
                        }

                        failed = !Compare(param[0], map.MonsterCount, tempInt);

                        break;

                    case CheckType.CheckExactMon:
                        if (Envir.GetMonsterInfo(param[0]) == null)
                        {
                            failed = true;
                            break;
                        }

                        if (!int.TryParse(param[2], out tempInt) || !int.TryParse(param[4], out tempInt2))
                        {
                            failed = true;
                            break;
                        }

                        map = Envir.GetMapByNameAndInstance(param[3], tempInt2);
                        if (map == null)
                        {
                            failed = true;
                            break;
                        }

                        failed = (!Compare(param[1], Envir.Objects.Count((
                            d => d.CurrentMap == map &&
                                d.Race == ObjectType.Monster &&
                                string.Equals(d.Name.Replace(" ", ""), param[0], StringComparison.OrdinalIgnoreCase) &&
                                !d.Dead)), tempInt));

                        break;

                    case CheckType.Random:
                        if (!int.TryParse(param[0], out tempInt))
                        {
                            failed = true;
                            break;
                        }

                        failed = 0 != Envir.Random.Next(0, tempInt);
                        break;
                    case CheckType.CheckCalc:
                        int left;
                        int right;

                        if (!int.TryParse(param[0], out left) || !int.TryParse(param[2], out right))
                        {
                            failed = true;
                            break;
                        }

                        try
                        {
                            failed = !Compare(param[1], left, right);
                        }
                        catch (ArgumentException)
                        {
                            MessageQueue.Enqueue(string.Format("NPC命令CHECKCALC中错误使用 {0} 操作符, 页码: {1} ", param[1], Key));
                            return true;
                        }
                        break;
                }

                if (!failed) continue;

                Failed();
                return false;
            }

            Success();
            return true;

        }
        public bool Check(MonsterObject monster)
        {
            if (!ShouldAllowLegacyTxtExecution())
            {
                if (Settings.TxtScriptsLogDispatch)
                    MessageQueue.Enqueue($"[TxtScripts] CSharpScriptsFallbackToTxt=false，阻止 legacy NPCSegment.Check(monster)：page={Page?.Key} segment={Key}");

                return false;
            }

            var failed = false;

            for (int i = 0; i < CheckList.Count; i++)
            {
                NPCChecks check = CheckList[i];
                List<string> param = check.Params.Select(t => FindVariable(monster, t)).ToList();

                for (int j = 0; j < param.Count; j++)
                {
                    var parts = param[j].Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

                    if (parts.Length == 0) continue;

                    foreach (var part in parts)
                    {
                        param[j] = param[j].Replace(part, ReplaceValue(monster, part));
                    }
                }

                uint tempUint;
                int tempInt;
                int tempInt2;
                Map map;

                switch (check.Type)
                {
                    case CheckType.LingFengRenewLevel:
                    case CheckType.LingFengFengHao:
                    case CheckType.LingFengActiveFengHao:
                    case CheckType.LingFengSlaveCount:
                    case CheckType.LingFengMirrorMap:
                        failed = true;
                        break;
                    case CheckType.Level:
                        {
                            if (!ushort.TryParse(param[1], out ushort level))
                            {
                                failed = true;
                                break;
                            }

                            try
                            {
                                failed = !Compare(param[0], monster.Level, level);
                            }
                            catch (ArgumentException)
                            {
                                MessageQueue.Enqueue(string.Format("以怪物为对象的NPC命令LEVEL中错误使用 {0} 操作符, 页码: {1}", param[0], Key));
                                return true;
                            }
                        }
                        break;
                    case CheckType.CheckDay:
                        var day = Envir.Now.DayOfWeek.ToString().ToUpper();
                        var dayToCheck = param[0].ToUpper();

                        failed = day != dayToCheck;
                        break;

                    case CheckType.CheckHour:
                        if (!uint.TryParse(param[0], out tempUint))
                        {
                            failed = true;
                            break;
                        }

                        var hour = Envir.Now.Hour;
                        var hourToCheck = tempUint;

                        failed = hour != hourToCheck;
                        break;

                    case CheckType.CheckMinute:
                        if (!uint.TryParse(param[0], out tempUint))
                        {
                            failed = true;
                            break;
                        }

                        var minute = Envir.Now.Minute;
                        var minuteToCheck = tempUint;

                        failed = minute != minuteToCheck;
                        break;

                    case CheckType.CheckRange:
                        int x, y, range;
                        if (!int.TryParse(param[0], out x) || !int.TryParse(param[1], out y) || !int.TryParse(param[2], out range))
                        {
                            failed = true;
                            break;
                        }

                        var target = new Point { X = x, Y = y };

                        failed = !Functions.InRange(monster.CurrentLocation, target, range);
                        break;

                    case CheckType.CheckMap:
                        map = Envir.GetMapByNameAndInstance(param[0]);

                        failed = monster.CurrentMap != map;
                        break;
                    case CheckType.CheckHum:
                        if (!int.TryParse(param[1], out tempInt) || !int.TryParse(param[3], out tempInt2))
                        {
                            failed = true;
                            break;
                        }

                        map = Envir.GetMapByNameAndInstance(param[2], tempInt2);
                        if (map == null)
                        {
                            failed = true;
                            break;
                        }

                        failed = !Compare(param[0], map.Players.Count(), tempInt);

                        break;

                    case CheckType.CheckMon:
                        if (!int.TryParse(param[1], out tempInt) || !int.TryParse(param[3], out tempInt2))
                        {
                            failed = true;
                            break;
                        }

                        map = Envir.GetMapByNameAndInstance(param[2], tempInt2);
                        if (map == null)
                        {
                            failed = true;
                            break;
                        }

                        failed = !Compare(param[0], map.MonsterCount, tempInt);

                        break;

                    case CheckType.CheckExactMon:
                        if (Envir.GetMonsterInfo(param[0]) == null)
                        {
                            failed = true;
                            break;
                        }

                        if (!int.TryParse(param[2], out tempInt) || !int.TryParse(param[4], out tempInt2))
                        {
                            failed = true;
                            break;
                        }

                        map = Envir.GetMapByNameAndInstance(param[3], tempInt2);
                        if (map == null)
                        {
                            failed = true;
                            break;
                        }

                        failed = (!Compare(param[1], Envir.Objects.Count((
                            d => d.CurrentMap == map &&
                                d.Race == ObjectType.Monster &&
                                string.Equals(d.Name.Replace(" ", ""), param[0], StringComparison.OrdinalIgnoreCase) &&
                                !d.Dead)), tempInt));

                        break;

                    case CheckType.Random:
                        if (!int.TryParse(param[0], out tempInt))
                        {
                            failed = true;
                            break;
                        }

                        failed = 0 != Envir.Random.Next(0, tempInt);
                        break;
                    case CheckType.CheckCalc:
                        int left;
                        int right;

                        if (!int.TryParse(param[0], out left) || !int.TryParse(param[2], out right))
                        {
                            failed = true;
                            break;
                        }

                        try
                        {
                            failed = !Compare(param[1], left, right);
                        }
                        catch (ArgumentException)
                        {
                            MessageQueue.Enqueue(string.Format("以怪物为对象的NPC命令CHECKCALC中错误使用 {0} 操作符,页码: {1} ", param[1], Key));
                            return true;
                        }
                        break;
                }

                if (!failed) continue;

                Failed(monster);
                return false;
            }

            Success(monster);
            return true;

        }
        public bool Check(PlayerObject player)
        {
            if (!ShouldAllowLegacyTxtExecution())
            {
                if (Settings.TxtScriptsLogDispatch)
                    MessageQueue.Enqueue($"[TxtScripts] CSharpScriptsFallbackToTxt=false，阻止 legacy NPCSegment.Check(player)：page={Page?.Key} segment={Key}");

                return false;
            }

            var failed = false;
            int matchedChecks = 0;
            int requiredMatches = RequiredCheckMatches > 0
                ? RequiredCheckMatches
                : MatchAnyCheck ? 1 : 0;

            for (int i = 0; i < CheckList.Count; i++)
            {
                if (requiredMatches > 0) failed = false;
                NPCChecks check = CheckList[i];
                List<string> param = check.Params.Select(t => FindVariable(player, t)).ToList();

                for (int j = 0; j < param.Count; j++)
                {
                    var parts = param[j].Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

                    if (parts.Length == 0) continue;

                    foreach (var part in parts)
                    {
                        param[j] = param[j].Replace(part, ReplaceValue(player, part));
                    }
                }

                uint tempUint;
                int tempInt;
                int tempInt2;

                switch (check.Type)
                {
                    case CheckType.LingFengRenewLevel:
                        {
                            CharacterInfo progressInfo = GetLingFengProgressInfo(player, param[0]);
                            if (progressInfo == null ||
                                !byte.TryParse(param[2], NumberStyles.None,
                                    CultureInfo.InvariantCulture, out byte requiredRenewLevel))
                            {
                                failed = true;
                                break;
                            }

                            try
                            {
                                failed = !Compare(
                                    param[1], progressInfo.LingFengProgress.RenewLevel,
                                    requiredRenewLevel);
                            }
                            catch (ArgumentException)
                            {
                                MessageQueue.Enqueue(
                                    $"[TxtScripts] CHECKRENEWLEVEL 操作符无效，页码：{Key}");
                                return true;
                            }
                        }
                        break;

                    case CheckType.LingFengFengHao:
                        CharacterInfo titleInfo = GetLingFengProgressInfo(player, param[0]);
                        failed = titleInfo == null ||
                                 !titleInfo.LingFengProgress.HasTitle(param[1]);
                        break;

                    case CheckType.LingFengActiveFengHao:
                        CharacterInfo activeTitleInfo = GetLingFengProgressInfo(player, param[0]);
                        failed = activeTitleInfo == null ||
                                 !string.Equals(activeTitleInfo.LingFengProgress.ActiveTitle,
                                     param[1], StringComparison.OrdinalIgnoreCase);
                        break;

                    case CheckType.LingFengSlaveCount:
                        if (!int.TryParse(param[1], NumberStyles.None,
                                CultureInfo.InvariantCulture, out int requiredSlaveCount) ||
                            requiredSlaveCount < 0 || param[3] is not ("0" or "1"))
                        {
                            failed = true;
                            break;
                        }

                        string requiredSlaveName = param[2];
                        bool includeNameDigits = param[3] == "1";
                        int slaveCount = player.Pets.Count(pet => pet != null && !pet.Dead &&
                            (requiredSlaveName.Length == 0 || string.Equals(
                                includeNameDigits ? pet.Info.Name :
                                    TrimLingFengMonsterNumericSuffix(pet.Info.Name),
                                includeNameDigits ? requiredSlaveName : TrimLingFengMonsterNumericSuffix(requiredSlaveName),
                                StringComparison.OrdinalIgnoreCase)));
                        failed = !Compare(param[0], slaveCount, requiredSlaveCount);
                        break;

                    case CheckType.LingFengMirrorMap:
                        failed = !Envir.IsLingFengMirrorMap(param[0]);
                        break;

                    case CheckType.LingFengCanMoveEctype:
                        failed = !Envir.CanMoveLingFengEctype(player, param[0]);
                        break;

                    case CheckType.LingFengEquippedItem:
                        if (!ushort.TryParse(param[1], NumberStyles.None,
                                CultureInfo.InvariantCulture, out ushort requiredEquipped))
                        {
                            failed = true;
                            break;
                        }

                        int equipped = player.Info.Equipment.Count(item =>
                            item != null && string.Equals(
                                item.Info.FriendlyName, param[0],
                                StringComparison.OrdinalIgnoreCase));
                        failed = equipped < requiredEquipped;
                        break;

                    case CheckType.LingFengRepairAllGold:
                        ulong repairCost = player.LingFengRepairAllEquipmentCost();
                        AddVariable(player, param[0],
                            repairCost.ToString(CultureInfo.InvariantCulture));
                        failed = player.Account == null || player.Account.Gold < repairCost;
                        break;

                    case CheckType.LingFengDeferredCompatibilityCheck:
                        // E1 只保留检测语法；缺少领域会话时必须失败关闭，不能伪造店铺状态。
                        failed = true;
                        break;

                    case CheckType.Level:
                        {
                            if (!ushort.TryParse(param[1], out ushort level))
                            {
                                failed = true;
                                break;
                            }

                            try
                            {
                                failed = !Compare(param[0], player.Level, level);
                            }
                            catch (ArgumentException)
                            {
                                MessageQueue.Enqueue(string.Format("以玩家为对象的NPC命令LEVEL中错误使用 {0} 操作符, 页码: {1}", param[0], Key));
                                return true;
                            }
                        }
                        break;

                    case CheckType.CheckGold:
                        if (!uint.TryParse(param[1], out tempUint))
                        {
                            failed = true;
                            break;
                        }

                        if (!LingFengNumericCommandExecutor.TryCheck(
                                player.Account.Gold, new[] { param[0], param[1] },
                                out bool goldMatched, out string goldDiagnostic))
                        {
                            MessageQueue.Enqueue($"[TxtScripts] CHECKGOLD 失败：{goldDiagnostic}，页码：{Key}");
                            return true;
                        }
                        failed = !goldMatched;
                        break;
                    case CheckType.CheckGuildGold:
                        if (!uint.TryParse(param[1], out tempUint))
                        {
                            failed = true;
                            break;
                        }

                        try
                        {
                            failed = !Compare(param[0], player.MyGuild.Gold, tempUint);
                        }
                        catch (ArgumentException)
                        {
                            MessageQueue.Enqueue(string.Format("以玩家为对象的NPC命令CHECKGUILDGOLD中错误使用 {0} 操作符, 页码: {1}", param[0], Key));
                            return true;
                        }
                        break;
                    case CheckType.CheckCredit:
                        if (!uint.TryParse(param[1], out tempUint))
                        {
                            failed = true;
                            break;
                        }

                        try
                        {
                            failed = !Compare(param[0], player.Account.Credit, tempUint);
                        }
                        catch (ArgumentException)
                        {
                            MessageQueue.Enqueue(string.Format("以玩家为对象的NPC命令CHECKCREDIT中错误使用 {0} 操作符, 页码: {1}", param[0], Key));
                            return true;
                        }
                        break;

                    case CheckType.CheckItem:
                        ushort count;
                        ushort dura;

                        if (!ushort.TryParse(param[1], out count))
                        {
                            failed = true;
                            break;
                        }

                        bool checkDura = ushort.TryParse(param[2], out dura);

                        var info = Envir.GetItemInfo(param[0]);

                        foreach (var item in player.Info.Inventory.Where(item => item != null && item.Info == info))
                        {
                            if (checkDura)
                                if (item.CurrentDura < (dura * 1000)) continue;

                            if (count > item.Count)
                            {
                                count -= item.Count;
                                continue;
                            }

                            if (count > item.Count) continue;
                            count = 0;
                            break;
                        }
                        if (count > 0)
                            failed = true;
                        break;

                    case CheckType.CheckItemLingFeng:
                        {
                            if (!ushort.TryParse(param[1], out ushort required) || required == 0 ||
                                !int.TryParse(param[2], out int partialMode) || partialMode is not (0 or 1) ||
                                !int.TryParse(param[3], out int renamedMode) || renamedMode is not (0 or 1))
                            {
                                MessageQueue.Enqueue($"[TxtScripts] CHECKITEM 扩展参数无效，页码：{Key}");
                                return true;
                            }
                            int available = player.Info.Inventory
                                .Where(item => item != null && LingFengItemCommandExecutor.NameMatches(
                                    item.Info.FriendlyName, param[0], partialMode == 1))
                                .Sum(item => item.Count);
                            failed = available < required;
                        }
                        break;

                    case CheckType.CheckGender:
                        MirGender gender;

                        string genderName = param[0].Equals("MAN", StringComparison.OrdinalIgnoreCase)
                            ? "Male"
                            : param[0].Equals("WOMAN", StringComparison.OrdinalIgnoreCase)
                                ? "Female"
                                : param[0];
                        if (!MirGender.TryParse(genderName, false, out gender))
                        {
                            failed = true;
                            break;
                        }

                        failed = player.Gender != gender;
                        break;

                    case CheckType.CheckClass:
                        MirClass mirClass;

                        if (!MirClass.TryParse(param[0], true, out mirClass))
                        {
                            failed = true;
                            break;
                        }

                        failed = player.Class != mirClass;
                        break;

                    case CheckType.CheckDay:
                        var day = Envir.Now.DayOfWeek.ToString().ToUpper();
                        var dayToCheck = param[0].ToUpper();

                        failed = day != dayToCheck;
                        break;

                    case CheckType.CheckHour:
                        if (!uint.TryParse(param[0], out tempUint))
                        {
                            failed = true;
                            break;
                        }

                        var hour = Envir.Now.Hour;
                        var hourToCheck = tempUint;

                        failed = hour != hourToCheck;
                        break;

                    case CheckType.CheckMinute:
                        if (!uint.TryParse(param[0], out tempUint))
                        {
                            failed = true;
                            break;
                        }

                        var minute = Envir.Now.Minute;
                        var minuteToCheck = tempUint;

                        failed = minute != minuteToCheck;
                        break;

                    case CheckType.CheckNameList:
                        failed = !Envir.NameListContainsFromFilePath(param[0], player.Name);
                        break;

                    case CheckType.LingFengBindMoney:
                        if (!TryResolveLingFengBoundMoney(
                                player, param[0], param[1], out uint bindAmount,
                                out uint bindBalance, out string bindDiagnostic))
                        {
                            MessageQueue.Enqueue(
                                $"[TxtScripts] CHECKBINDMONEY 失败：{bindDiagnostic}，页码：{Key}");
                            failed = true;
                            break;
                        }
                        failed = bindBalance < bindAmount;
                        break;

                    case CheckType.LingFengAccountList:
                        // 无法解析账号或安全逻辑路径时按“已存在”处理，阻止限次奖励放行。
                        failed = !LingFengAccountListContainsFailClosed(player, param[0]);
                        break;

                    case CheckType.LingFengNameDateTimeList:
                        {
                            failed = true;
                            AddVariable(player, param[2], string.Empty);
                            AddVariable(player, param[3], "0");
                            AddVariable(player, param[4], "0");
                            AddVariable(player, param[5], "0");
                            if (!Server.Scripting.LingFengScriptReferenceResolver.TryResolveCandidateTextKey(
                                    param[0], out string membershipKey) ||
                                param[1] is not ("0" or "1") ||
                                !player.Info.LingFengProgress.TryGetTimedMembership(
                                    membershipKey, Envir.Now, param[1] == "1",
                                    out DateTime expiry, out TimeSpan remaining))
                                break;
                            long totalMinutes = Math.Max(0L, (long)remaining.TotalMinutes);
                            AddVariable(player, param[2],
                                expiry.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
                            AddVariable(player, param[3],
                                (totalMinutes / 1440).ToString(CultureInfo.InvariantCulture));
                            AddVariable(player, param[4],
                                (totalMinutes % 1440 / 60).ToString(CultureInfo.InvariantCulture));
                            AddVariable(player, param[5],
                                (totalMinutes % 60).ToString(CultureInfo.InvariantCulture));
                            failed = false;
                        }
                        break;

                    case CheckType.CheckGuildNameList:
                        if (player.MyGuild == null)
                        {
                            failed = true;
                            break;
                        }

                        failed = !Envir.NameListContainsFromFilePath(param[0], player.MyGuild.Name);
                        break;

                    case CheckType.IsAdmin:
                        failed = !player.IsGM;
                        break;

                    case CheckType.CheckPkPoint:
                        if (!int.TryParse(param[1], out tempInt))
                        {
                            failed = true;
                            break;
                        }

                        try
                        {
                            failed = !Compare(param[0], player.PKPoints, tempInt);
                        }
                        catch (ArgumentException)
                        {
                            MessageQueue.Enqueue(string.Format("以玩家为对象的NPC命令CHECKPOINT中错误使用 {0} 操作符, 页码: {1}", param[0], Key));
                            return true;
                        }
                        break;

                    case CheckType.CheckRange:
                        int x, y, range;
                        if (!int.TryParse(param[0], out x) || !int.TryParse(param[1], out y) || !int.TryParse(param[2], out range))
                        {
                            failed = true;
                            break;
                        }

                        var target = new Point { X = x, Y = y };

                        failed = !Functions.InRange(player.CurrentLocation, target, range);
                        break;

                    case CheckType.CheckMap:
                        Map map = Envir.GetMapByNameAndInstance(param[0]);

                        failed = player.CurrentMap != map;
                        break;

                    case CheckType.Check:
                        uint onCheck;

                        if (!uint.TryParse(param[0], out tempUint) || !uint.TryParse(param[1], out onCheck) || tempUint > Globals.FlagIndexCount)
                        {
                            failed = true;
                            break;
                        }

                        bool tempBool = Convert.ToBoolean(onCheck);

                        bool flag = player.Info.Flags[tempUint];

                        failed = flag != tempBool;
                        break;

                    case CheckType.CheckHum:
                        if (!int.TryParse(param[1], out tempInt) || !int.TryParse(param[3], out tempInt2))
                        {
                            failed = true;
                            break;
                        }

                        map = Envir.GetMapByNameAndInstance(param[2], tempInt2);
                        if (map == null)
                        {
                            failed = true;
                            break;
                        }

                        failed = !Compare(param[0], map.Players.Count(), tempInt);

                        break;

                    case CheckType.CheckMon:
                        if (!int.TryParse(param[1], out tempInt) || !int.TryParse(param[3], out tempInt2))
                        {
                            failed = true;
                            break;
                        }

                        map = Envir.GetMapByNameAndInstance(param[2], tempInt2);
                        if (map == null)
                        {
                            failed = true;
                            break;
                        }

                        int actualMonsterCount = map.MonsterCount - player.Pets.Count();

                        failed = !Compare(param[0], actualMonsterCount, tempInt);

                        break;

                    case CheckType.CheckExactMon:
                        if (Envir.GetMonsterInfo(param[0]) == null)
                        {
                            failed = true;
                            break;
                        }

                        if (!int.TryParse(param[2], out tempInt) || !int.TryParse(param[4], out tempInt2))
                        {
                            failed = true;
                            break;
                        }

                        map = Envir.GetMapByNameAndInstance(param[3], tempInt2);
                        if (map == null)
                        {
                            failed = true;
                            break;
                        }

                        failed = (!Compare(param[1], Envir.Objects.Count((
                            d => d.CurrentMap == map &&
                                d.Race == ObjectType.Monster &&
                                string.Equals(d.Name.Replace(" ", ""), param[0], StringComparison.OrdinalIgnoreCase) &&
                                !d.Dead)), tempInt));

                        break;

                    case CheckType.Random:
                        if (!int.TryParse(param[0], out tempInt))
                        {
                            failed = true;
                            break;
                        }

                        failed = 0 != Envir.Random.Next(0, tempInt);
                        break;

                    case CheckType.Groupleader:
                        failed = (player.GroupMembers == null || player.GroupMembers[0] != player);
                        break;

                    case CheckType.GroupCount:
                        if (!int.TryParse(param[1], out tempInt))
                        {
                            failed = true;
                            break;
                        }

                        failed = (player.GroupMembers == null || !Compare(param[0], player.GroupMembers.Count, tempInt));
                        break;
                    case CheckType.GroupCheckNearby:
                        target = new Point(-1, -1);
                        for (int j = 0; j < player.CurrentMap.NPCs.Count; j++)
                        {
                            NPCObject ob = player.CurrentMap.NPCs[j];
                            if (ob.ObjectID != player.NPCObjectID) continue;
                            target = ob.CurrentLocation;
                            break;
                        }
                        if (target.X == -1)
                        {
                            failed = true;
                            break;
                        }
                        if (player.GroupMembers == null)
                            failed = true;
                        else
                        {
                            for (int j = 0; j < player.GroupMembers.Count; j++)
                            {
                                if (player.GroupMembers[j] == null) continue;
                                failed |= !Functions.InRange(player.GroupMembers[j].CurrentLocation, target, 9);
                                if (failed) break;
                            }
                        }
                        break;

                    case CheckType.PetCount:
                        if (!int.TryParse(param[1], out tempInt))
                        {
                            failed = true;
                            break;
                        }

                        failed = !Compare(param[0], player.Pets.Count(), tempInt);
                        break;

                    case CheckType.PetLevel:
                        if (!int.TryParse(param[1], out tempInt))
                        {
                            failed = true;
                            break;
                        }

                        for (int p = 0; p < player.Pets.Count(); p++)
                        {
                            failed = !Compare(param[0], player.Pets[p].PetLevel, tempInt);
                        }
                        break;

                    case CheckType.CheckCalc:
                        int left;
                        int right;

                        try
                        {
                            if (!int.TryParse(param[0], out left) || !int.TryParse(param[2], out right))
                            {
                                failed = !Compare(param[1], param[0], param[2]);
                            }
                            else
                            {
                                failed = !Compare(param[1], left, right);
                            }
                        }
                        catch (ArgumentException)
                        {
                            MessageQueue.Enqueue(string.Format("以玩家为对象的NPC命令CHECKCALC中错误使用 {0} 操作符, 页码: {1} ", param[1], Key));
                            return true;
                        }
                        break;

                    case CheckType.CheckExperience:
                    case CheckType.CheckHP:
                    case CheckType.CheckMP:
                        {
                            long current = check.Type switch
                            {
                                CheckType.CheckExperience => player.Experience,
                                CheckType.CheckHP => player.HP,
                                _ => player.MP
                            };
                            if (!LingFengNumericCommandExecutor.TryCheck(
                                    current, param, out bool matched, out string diagnostic))
                            {
                                MessageQueue.Enqueue($"[TxtScripts] 数值检测参数错误：{diagnostic}，页码：{Key}");
                                return true;
                            }
                            failed = !matched;
                        }
                        break;
                    case CheckType.Variable:
                        {
                            if (player.NPCObjectID == 0)
                            {
                                failed = true;
                                break;
                            }

                            var context = ScriptVariableContext.ForConversation(player, player.NPCObjectID, player.CurrentMap);
                            ScriptVariableCheckResult result = Envir.CSharpScripts.VariableCommands.Check(
                                context, param[0], param[1], param[2]);
                            failed = !result.Success || !result.Matched;
                            if (!result.Success)
                                MessageQueue.Enqueue($"[Variables][TXT] CHECK 失败：{result.ErrorCode} {result.Diagnostic}，页码：{Key}");
                        }
                        break;
                    case CheckType.VariableChance:
                        {
                            if (player.NPCObjectID == 0)
                            {
                                failed = true;
                                break;
                            }
                            if (!Enum.TryParse(param[1], true, out ScriptProbabilityUnit unit))
                            {
                                failed = true;
                                MessageQueue.Enqueue($"[Variables][TXT] CHANCE 概率单位无效：{param[1]}，页码：{Key}");
                                break;
                            }
                            var context = ScriptVariableContext.ForConversation(player, player.NPCObjectID, player.CurrentMap);
                            ScriptVariableCheckResult result = Envir.CSharpScripts.VariableCommands.Chance(
                                context, param[0], unit, Envir.Random.Next);
                            failed = !result.Success || !result.Matched;
                            if (!result.Success)
                                MessageQueue.Enqueue($"[Variables][TXT] CHANCE 失败：{result.ErrorCode} {result.Diagnostic}，页码：{Key}");
                        }
                        break;
                    case CheckType.VariableComposite:
                        {
                            if (player.NPCObjectID == 0)
                            {
                                failed = true;
                                break;
                            }
                            var context = ScriptVariableContext.ForConversation(player, player.NPCObjectID, player.CurrentMap);
                            ScriptCompositeResult result = EvaluateCompositeCheck(context, param);
                            failed = !result.Success || !result.Matched;
                            if (!result.Success)
                                MessageQueue.Enqueue($"[Variables][TXT] {param[0]} 失败：{result.ErrorCode} {result.Diagnostic}，页码：{Key}");
                        }
                        break;
                    case CheckType.LingFengCompare:
                        {
                            string command = param[0];
                            string leftValue = param[1];
                            string rightValue = param[2];
                            bool matched;
                            if (command.Equals("EQUAL", StringComparison.OrdinalIgnoreCase))
                            {
                                matched = string.Equals(leftValue, rightValue, StringComparison.OrdinalIgnoreCase);
                            }
                            else if (decimal.TryParse(leftValue, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal leftNumber) &&
                                     decimal.TryParse(rightValue, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal rightNumber))
                            {
                                matched = command.Equals("LARGE", StringComparison.OrdinalIgnoreCase)
                                    ? leftNumber > rightNumber
                                    : leftNumber < rightNumber;
                            }
                            else
                            {
                                MessageQueue.Enqueue($"[TxtScripts] {command} 需要有效数值，页码：{Key}");
                                Failed(player);
                                return false;
                            }

                            failed = !matched;
                        }
                        break;
                    case CheckType.LingFengTargetVariable:
                        {
                            if (!TryGetLingFengCurrentTarget(
                                    player, out MapObject variableTarget))
                            {
                                failed = true;
                                break;
                            }
                            ScriptVariableCheckResult result =
                                Envir.CSharpScripts.VariableCommands.Check(
                                    ScriptVariableContext.ForPlayer(
                                        variableTarget, variableTarget.CurrentMap),
                                    param[0], param[1], param[2]);
                            failed = !result.Success || !result.Matched;
                            if (!result.Success)
                                MessageQueue.Enqueue(
                                    $"[Variables][TXT] M. 检测失败：{result.ErrorCode} {result.Diagnostic}，页码：{Key}");
                        }
                        break;
                    case CheckType.LingFengContainsText:
                        failed = !param[0].Contains(param[1], StringComparison.Ordinal);
                        break;
                    case CheckType.LingFengMagicName:
                        failed = !TryResolveLingFengMagic(param[0], out MagicInfo magicNameInfo) ||
                                 !player.Info.Magics.Any(magic => magic.Spell == magicNameInfo.Spell);
                        break;
                    case CheckType.LingFengSkillLevel:
                        {
                            failed = true;
                            if (!TryResolveLingFengMagic(param[0], out MagicInfo skillInfo)) break;
                            UserMagic learnedMagic = player.Info.Magics.FirstOrDefault(
                                magic => magic.Spell == skillInfo.Spell);
                            if (learnedMagic == null) break;
                            int currentSkillLevel = param[3] == "1"
                                ? player.Info.LingFengProgress.GetEnhancedSkillLevel(skillInfo.Spell)
                                : learnedMagic.Level;
                            failed = !LingFengNumericCommandExecutor.TryCheck(
                                currentSkillLevel, param.Skip(1).Take(2).ToList(), out bool skillMatched, out _) ||
                                     !skillMatched;
                        }
                        break;
                    case CheckType.LingFengBagSize:
                        failed = !int.TryParse(param[0], NumberStyles.None,
                                     CultureInfo.InvariantCulture, out int requiredSlots) ||
                                 player.Info?.Inventory == null ||
                                 player.Info.Inventory.Count(item => item == null) < requiredSlots;
                        break;
                    case CheckType.LingFengHaveHero:
                        failed = player.Info?.Heroes == null ||
                                 !player.Info.Heroes.Any(hero => hero != null && !hero.Deleted);
                        break;
                    case CheckType.LingFengHeroOnline:
                        failed = player.Hero == null;
                        break;
                    case CheckType.LingFengTextList:
                        {
                            failed = true;
                            if (param[3] == "1") break;
                            StringComparison comparison = param[4] == "1"
                                ? StringComparison.Ordinal
                                : StringComparison.OrdinalIgnoreCase;
                            IEnumerable<string> sourceLines =
                                TryGetCandidateTextDefinition(param[0], out TextFileDefinition definition)
                                    ? definition.Lines
                                    : Array.Empty<string>();
                            sourceLines = sourceLines.Concat(
                                Envir.GetLingFengRuntimeTextListValues(param[0]));
                            foreach (string sourceLine in sourceLines)
                            {
                                string line = (sourceLine ?? string.Empty).Trim();
                                if (line.Length == 0 || line.StartsWith(";", StringComparison.Ordinal))
                                    continue;
                                if (param[2].Length == 0)
                                {
                                    if (line.Contains(param[1], comparison))
                                    {
                                        failed = false;
                                        break;
                                    }
                                    continue;
                                }
                                string[] columns = line.Split(
                                    (char[])null, StringSplitOptions.RemoveEmptyEntries);
                                if (columns.Length >= 2 &&
                                    string.Equals(columns[0], param[1], comparison) &&
                                    string.Equals(columns[1], param[2], comparison))
                                {
                                    failed = false;
                                    break;
                                }
                            }
                        }
                        break;
                    case CheckType.LingFengStringPosition:
                        {
                            failed = true;
                            if (param[4] == "1" ||
                                !TryGetCandidateTextDefinition(param[0], out TextFileDefinition definition))
                                break;
                            for (int lineIndex = 0; lineIndex < definition.Lines.Count; lineIndex++)
                            {
                                string line = definition.Lines[lineIndex] ?? string.Empty;
                                bool matched = param[5] == "1"
                                    ? string.Equals(line, param[1], StringComparison.OrdinalIgnoreCase)
                                    : line.Contains(param[1], StringComparison.OrdinalIgnoreCase);
                                if (!matched) continue;
                                failed = !TryStoreScriptValue(player, param[2], lineIndex) ||
                                         !TryStoreScriptTextValue(player, param[3], line);
                                break;
                            }
                        }
                        break;
                    case CheckType.LingFengKilledByHuman:
                        failed = !IsHumanOwnedActor(player.LastHitter);
                        break;
                    case CheckType.LingFengAttackMode:
                        failed = !int.TryParse(param[0], NumberStyles.None,
                            CultureInfo.InvariantCulture, out int lingFengAttackMode) ||
                            !MatchesLingFengAttackMode(player.AMode, lingFengAttackMode);
                        break;
                    case CheckType.LingFengOnline:
                        failed = Envir.GetPlayer(param[0]) == null;
                        break;
                    case CheckType.LingFengStringLength:
                        {
                            int textLength = param[0].Sum(value => value <= 0x7F ? 1 : 2);
                            failed = !int.TryParse(param[2], NumberStyles.None,
                                         CultureInfo.InvariantCulture, out int expectedLength) ||
                                     !LingFengNumericCommandExecutor.TryCheck(
                                         textLength, new[] { param[1], expectedLength.ToString(CultureInfo.InvariantCulture) },
                                         out bool matched, out _) || !matched;
                        }
                        break;
                    case CheckType.LingFengLastActorClass:
                        failed = !TryGetLingFengLastActorPlayer(out PlayerObject lastClassActor) ||
                                 !Enum.TryParse(param[0], true, out MirClass expectedClass) ||
                                 lastClassActor.Class != expectedClass;
                        break;
                    case CheckType.LingFengLastActorLevel:
                        failed = !TryGetLingFengLastActorPlayer(out PlayerObject lastLevelActor) ||
                                 !LingFengNumericCommandExecutor.TryCheck(
                                     lastLevelActor.Level, param, out bool levelMatched, out _) ||
                                 !levelMatched;
                        break;
                    case CheckType.LingFengTargetLevel:
                        failed = !TryGetLingFengCurrentTargetMonster(player, out MonsterObject levelTarget) ||
                                 !LingFengNumericCommandExecutor.TryCheck(
                                     levelTarget.Level, param, out bool targetLevelMatched, out _) ||
                                 !targetLevelMatched;
                        break;
                    case CheckType.LingFengTargetResourcePercent:
                        {
                            failed = true;
                            if (!TryGetLingFengCurrentTargetMonster(player, out MonsterObject hpTarget) ||
                                hpTarget.MaxHealth <= 0)
                                break;
                            long percent = (long)hpTarget.Health * 100 / hpTarget.MaxHealth;
                            failed = !LingFengNumericCommandExecutor.TryCheck(
                                percent, param, out bool targetHpMatched, out _) || !targetHpMatched;
                        }
                        break;
                    case CheckType.LingFengInSafeZone:
                        failed = !player.InSafeZone;
                        break;
                    case CheckType.LingFengRangeMonsterCount:
                        {
                            failed = true;
                            Map countMap = Envir.GetMapByNameAndInstance(param[0]);
                            if (countMap == null ||
                                !int.TryParse(param[1], NumberStyles.None, CultureInfo.InvariantCulture, out int countX) ||
                                !int.TryParse(param[2], NumberStyles.None, CultureInfo.InvariantCulture, out int countY) ||
                                !int.TryParse(param[3], NumberStyles.None, CultureInfo.InvariantCulture, out int countRange))
                                break;
                            int monsterCount = Envir.Objects.OfType<MonsterObject>().Count(monster =>
                                !monster.Dead && monster.CurrentMap == countMap &&
                                Functions.InRange(monster.CurrentLocation, new Point(countX, countY), countRange));
                            failed = !LingFengNumericCommandExecutor.TryCheck(
                                monsterCount, param.Skip(4).ToList(), out bool rangeCountMatched, out _) ||
                                     !rangeCountMatched;
                        }
                        break;
                    case CheckType.LingFengRangeHumanCount:
                        {
                            failed = true;
                            Map countMap = param[0].Equals("SELF", StringComparison.OrdinalIgnoreCase)
                                ? player.CurrentMap
                                : Envir.GetMapByNameAndInstance(param[0]);
                            if (countMap == null ||
                                !int.TryParse(param[1], NumberStyles.None,
                                    CultureInfo.InvariantCulture, out int countX) ||
                                !int.TryParse(param[2], NumberStyles.None,
                                    CultureInfo.InvariantCulture, out int countY) ||
                                !int.TryParse(param[3], NumberStyles.None,
                                    CultureInfo.InvariantCulture, out int countRange))
                                break;
                            if (countX == 0 && countY == 0 && player.CurrentMap == countMap)
                            {
                                countX = player.CurrentLocation.X;
                                countY = player.CurrentLocation.Y;
                            }
                            int humanCount = countMap.Players.Count(candidate =>
                                candidate != null && !candidate.Dead &&
                                Functions.InRange(candidate.CurrentLocation,
                                    new Point(countX, countY), countRange));
                            failed = !LingFengNumericCommandExecutor.TryCheck(
                                         humanCount, param.Skip(4).ToList(),
                                         out bool humanRangeMatched, out _) ||
                                     !humanRangeMatched;
                        }
                        break;
                    case CheckType.LingFengMapSameMonsterCount:
                        {
                            failed = true;
                            Map countMap = Envir.GetMapByNameAndInstance(param[0]);
                            if (countMap == null) break;
                            bool ignoreNumericSuffix = param[4] == "1";
                            string expectedName = ignoreNumericSuffix
                                ? TrimLingFengMonsterNumericSuffix(param[1])
                                : param[1];
                            int monsterCount = Envir.Objects.OfType<MonsterObject>().Count(monster =>
                                !monster.Dead && monster.CurrentMap == countMap &&
                                string.Equals(
                                    ignoreNumericSuffix
                                        ? TrimLingFengMonsterNumericSuffix(monster.Info.Name)
                                        : monster.Info.Name,
                                    expectedName,
                                    StringComparison.OrdinalIgnoreCase));
                            failed = !LingFengNumericCommandExecutor.TryCheck(
                                monsterCount, param.Skip(2).Take(2).ToList(),
                                out bool sameCountMatched, out _) || !sameCountMatched;
                        }
                        break;
                    case CheckType.LingFengMapMonsterCount:
                        {
                            failed = true;
                            if (!TryCountLingFengMapMonsters(
                                    param[0], param[3] == "1", out int monsterCount))
                                break;
                            failed = !LingFengNumericCommandExecutor.TryCheck(
                                monsterCount, param.Skip(1).Take(2).ToList(),
                                out bool mapCountMatched, out _) || !mapCountMatched;
                        }
                        break;
                    case CheckType.LingFengMapHumanCount:
                        {
                            Map humanMap = Envir.GetMapByNameAndInstance(param[0]);
                            failed = humanMap == null ||
                                     !LingFengNumericCommandExecutor.TryCheck(
                                         humanMap.Players.Count(candidate => candidate != null && !candidate.Dead),
                                         param.Skip(1).Take(2).ToList(),
                                         out bool humanCountMatched, out _) ||
                                     !humanCountMatched;
                        }
                        break;
                    case CheckType.LingFengMapMonsterMinimum:
                        failed = !TryCountLingFengMapMonsters(
                                     param[0], param[2] == "1", out int minimumActual) ||
                                 !int.TryParse(param[1], NumberStyles.None,
                                     CultureInfo.InvariantCulture, out int minimumExpected) ||
                                 minimumActual < minimumExpected;
                        break;
                    case CheckType.LingFengStateValue:
                        failed = !HasLingFengState(player, param[0]);
                        break;
                    case CheckType.LingFengTargetStateValue:
                        failed = !TryGetLingFengCurrentTargetMonster(player, out MonsterObject stateTarget) ||
                                 !HasLingFengState(stateTarget, param[0]);
                        break;
                    case CheckType.LingFengMarried:
                        failed = player.Info?.Married == 0;
                        break;
                    case CheckType.LingFengTargetMarried:
                        failed = !TryGetLingFengCurrentTargetPlayer(player, out PlayerObject marriedTarget) ||
                                 marriedTarget.Info?.Married == 0;
                        break;
                    case CheckType.LingFengTargetGender:
                        {
                            string targetGenderName = param[0].Equals("MAN", StringComparison.OrdinalIgnoreCase)
                                ? "Male"
                                : param[0].Equals("WOMAN", StringComparison.OrdinalIgnoreCase)
                                    ? "Female"
                                    : param[0];
                            failed = !TryGetLingFengCurrentTargetPlayer(player, out PlayerObject genderTarget) ||
                                     !MirGender.TryParse(targetGenderName, false, out MirGender targetGender) ||
                                     genderTarget.Gender != targetGender;
                        }
                        break;
                    case CheckType.LingFengPoseMarried:
                        failed = !TryGetLingFengFacingPlayer(player, out PlayerObject marriedPose) ||
                                 marriedPose.Info?.Married == 0;
                        break;
                    case CheckType.LingFengPoseGender:
                        {
                            bool wantsMale = param[0].Equals("MAN", StringComparison.OrdinalIgnoreCase) ||
                                             param[0].Equals("男", StringComparison.OrdinalIgnoreCase);
                            failed = !TryGetLingFengFacingPlayer(player, out PlayerObject genderPose) ||
                                     genderPose.Gender != (wantsMale ? MirGender.Male : MirGender.Female);
                        }
                        break;
                    case CheckType.LingFengCurrentTargetRace:
                        failed = LingFengTxtTriggerContext.Current?.Payload is not LingFengDamageEvent raceDamage ||
                                 !MatchesLingFengTargetRace(raceDamage.ActorKind, param[0]);
                        break;
                    case CheckType.LingFengCurrentTargetSlave:
                        failed = LingFengTxtTriggerContext.Current?.Payload is not LingFengDamageEvent slaveDamage ||
                                 slaveDamage.ActorKind != LingFengCombatActorKind.Pet;
                        break;
                    case CheckType.LingFengGameGold:
                        failed = !LingFengNumericCommandExecutor.TryCheck(
                            player.Info.PearlCount, param, out bool gameGoldMatched, out _) ||
                            !gameGoldMatched;
                        break;
                    case CheckType.LingFengGamePoint:
                        failed = !LingFengNumericCommandExecutor.TryCheck(
                            player.Info.LingFengProgress.GamePoint, param,
                            out bool gamePointMatched, out _) || !gamePointMatched;
                        break;
                    case CheckType.LingFengGameDiamond:
                        failed = !LingFengNumericCommandExecutor.TryCheck(
                            player.Info.LingFengProgress.GameDiamond, param,
                            out bool gameDiamondMatched, out _) || !gameDiamondMatched;
                        break;
                    case CheckType.LingFengGameGird:
                        failed = !LingFengNumericCommandExecutor.TryCheck(
                            player.Info.LingFengProgress.GameGird, param,
                            out bool gameGirdMatched, out _) || !gameGirdMatched;
                        break;
                    case CheckType.LingFengUseItem:
                        {
                            failed = !TryGetLingFengEquipmentItem(
                                player, param[0] == "1", param[1], out _, out _);
                        }
                        break;
                    case CheckType.LingFengStorageOpen:
                        failed = player.Account == null ||
                                 !int.TryParse(param[0], NumberStyles.None,
                                     CultureInfo.InvariantCulture, out int storagePage) ||
                                 !player.Account.IsLingFengStorageOpen(storagePage);
                        break;
                    case CheckType.LingFengItemState:
                        {
                            if (!TryGetLingFengEquipmentItem(
                                    player, false, param[1], out _, out UserItem stateItem))
                            {
                                failed = true;
                                break;
                            }
                            if (param[0] == "BIND")
                            {
                                failed = stateItem.SoulBoundId == -1;
                                break;
                            }
                            failed = !int.TryParse(param[2], NumberStyles.None,
                                         CultureInfo.InvariantCulture, out int stateIndex) ||
                                     !stateItem.HasLingFengItemState(stateIndex);
                        }
                        break;
                    case CheckType.LingFengCustomItemValue:
                        {
                            if (!TryGetLingFengEquipmentItem(player, param[0] == "1", param[1],
                                    out _, out UserItem customItem) ||
                                !int.TryParse(param[2], NumberStyles.None, CultureInfo.InvariantCulture,
                                    out int attributeIndex) ||
                                attributeIndex < 0 ||
                                attributeIndex >= UserItem.LingFengCustomAttributeLimit ||
                                !int.TryParse(param[4], NumberStyles.Integer, CultureInfo.InvariantCulture,
                                    out int expectedValue) ||
                                !int.TryParse(param[5], NumberStyles.None, CultureInfo.InvariantCulture,
                                    out int valueIndex))
                            {
                                failed = true;
                                break;
                            }
                            int actualValue = GetLingFengCustomValue(
                                customItem.GetLingFengCustomAttribute(attributeIndex), valueIndex);
                            failed = !CompareLingFengInteger(actualValue, param[3], expectedValue);
                        }
                        break;
                    case CheckType.LingFengItemAddedValue:
                        {
                            if (!TryGetLingFengEquipmentItem(player, param[0] == "1", param[1],
                                    out _, out UserItem addedItem) ||
                                !int.TryParse(param[1], NumberStyles.Integer, CultureInfo.InvariantCulture,
                                    out int addedPosition) ||
                                !int.TryParse(param[2], NumberStyles.None, CultureInfo.InvariantCulture,
                                    out int addedAttribute) ||
                                !int.TryParse(param[4], NumberStyles.Integer, CultureInfo.InvariantCulture,
                                    out int expectedAddedValue) ||
                                !TryGetLingFengItemAddedValue(
                                    addedItem, addedPosition, addedAttribute, out int actualAddedValue))
                            {
                                failed = true;
                                break;
                            }
                            failed = !CompareLingFengInteger(
                                actualAddedValue, param[3], expectedAddedValue);
                        }
                        break;
                    case CheckType.LingFengItemNameColour:
                        {
                            failed = !TryGetLingFengEquipmentItem(
                                         player, param[0] == "1", param[1], out _, out UserItem colourItem) ||
                                     !byte.TryParse(param[2], NumberStyles.None,
                                         CultureInfo.InvariantCulture, out byte expectedColour) ||
                                     colourItem.LingFengNameColour != expectedColour;
                        }
                        break;
                    case CheckType.LingFengItemUpgradeCount:
                        {
                            failed = !TryGetLingFengEquipmentItem(
                                         player, param[0] == "1", param[1], out _,
                                         out UserItem upgradeItem) ||
                                     !byte.TryParse(param[3], NumberStyles.None,
                                         CultureInfo.InvariantCulture, out byte expectedUpgradeCount) ||
                                     !CompareLingFengInteger(
                                         upgradeItem.LingFengUpgradeCount,
                                         param[2], expectedUpgradeCount);
                        }
                        break;
                    case CheckType.LingFengCustomItemProgressBarValue:
                        {
                            if (!TryGetLingFengEquipmentItem(player, param[0] == "1", param[1],
                                    out _, out UserItem progressItem) ||
                                !int.TryParse(param[2], NumberStyles.None, CultureInfo.InvariantCulture,
                                    out int progressIndex) ||
                                !int.TryParse(param[3], NumberStyles.None, CultureInfo.InvariantCulture,
                                    out int progressValueKind) ||
                                !progressItem.TryGetLingFengCustomProgressBarValue(
                                    progressIndex, progressValueKind, out int progressValue))
                            {
                                failed = true;
                                break;
                            }
                            if (string.IsNullOrEmpty(param[4]))
                            {
                                failed = progressValueKind != 2 || progressValue >= 100;
                                break;
                            }
                            failed = !int.TryParse(param[5], NumberStyles.Integer,
                                         CultureInfo.InvariantCulture, out int expectedProgress) ||
                                     !CompareLingFengInteger(
                                         progressValue, param[4], expectedProgress);
                        }
                        break;
                    case CheckType.LingFengTargetShieldOpen:
                        failed = !TryGetLingFengCurrentTargetPlayer(player, out PlayerObject shieldTarget) ||
                                 !shieldTarget.HasBuff(BuffType.MagicShield);
                        break;
                    case CheckType.LingFengBattleStatus:
                        failed = !Envir.Conquests.Any(conquest => conquest.WarIsOn);
                        break;
                    case CheckType.LingFengCastleUnderWar:
                        failed = !Envir.Conquests.Any(conquest =>
                            conquest.WarIsOn &&
                            string.Equals(conquest.Info?.Name, param[0],
                                StringComparison.OrdinalIgnoreCase));
                        break;
                    case CheckType.LingFengPoseDirection:
                        failed = !TryGetLingFengFacingPlayer(player, out PlayerObject posePlayer) ||
                                 posePlayer.Front != player.CurrentLocation ||
                                 param[0] == "1" && posePlayer.Info.Gender != player.Info.Gender ||
                                 param[0] == "2" && posePlayer.Info.Gender == player.Info.Gender;
                        break;
                    case CheckType.LingFengPoseLevel:
                        failed = !TryGetLingFengFacingPlayer(player, out PlayerObject levelPlayer) ||
                                 !int.TryParse(param[1], NumberStyles.None,
                                     CultureInfo.InvariantCulture, out int poseLevel) ||
                                 !Compare(param[0], levelPlayer.Level, poseLevel);
                        break;
                    case CheckType.LingFengCastleGuild:
                        failed = player.MyGuild == null ||
                                 !Envir.Conquests.Any(conquest => conquest.Guild == player.MyGuild);
                        break;
                    case CheckType.LingFengCastleMaster:
                        failed = player.MyGuild == null || player.MyGuildRank == null ||
                                 player.MyGuild.Ranks.Count == 0 ||
                                 player.MyGuild.Ranks[0] != player.MyGuildRank ||
                                 !Envir.Conquests.Any(conquest => conquest.Guild == player.MyGuild);
                        break;
                    case CheckType.LingFengHaveMentor:
                        failed = player.Info.Mentor == 0;
                        break;
                    case CheckType.LingFengIsMentor:
                        failed = !player.Info.IsMentor;
                        break;
                    case CheckType.LingFengPoseMentor:
                        failed = !TryGetLingFengFacingPlayer(player, out PlayerObject mentorPose) ||
                                 mentorPose.Info.Mentor == 0;
                        break;
                    case CheckType.LingFengTargetCastleGuild:
                        failed = !TryGetLingFengCurrentTargetPlayer(player, out PlayerObject castleTarget) ||
                                 castleTarget.MyGuild == null ||
                                 !Envir.Conquests.Any(conquest => conquest.Guild == castleTarget.MyGuild);
                        break;
                    case CheckType.LingFengScriptParameters:
                        {
                            IReadOnlyList<string> actual =
                                LingFengTxtTriggerContext.Current?.ScriptParameters ?? Array.Empty<string>();
                            failed = actual.Count != param.Count;
                            for (int parameterIndex = 0; !failed && parameterIndex < param.Count; parameterIndex++)
                                failed = !string.Equals(
                                    actual[parameterIndex], param[parameterIndex], StringComparison.Ordinal);
                        }
                        break;
                    case CheckType.LingFengFindMonsterPoint:
                        {
                            Map targetMap = Envir.GetMapByNameAndInstance(param[0]);
                            MonsterObject nearest = targetMap == null
                                ? null
                                : Envir.Objects
                                    .OfType<MonsterObject>()
                                    .Where(monster => !monster.Dead && monster.CurrentMap == targetMap &&
                                        string.Equals(monster.Name, param[1], StringComparison.OrdinalIgnoreCase))
                                    .OrderBy(monster => DistanceSquared(player.CurrentLocation, monster.CurrentLocation))
                                    .ThenBy(monster => monster.ObjectID)
                                    .FirstOrDefault();
                            failed = nearest == null ||
                                     !TryStoreScriptValue(player, param[2], nearest.CurrentLocation.X) ||
                                     !TryStoreScriptValue(player, param[3], nearest.CurrentLocation.Y);
                        }
                        break;
                    case CheckType.LingFengRandomRatio:
                        if (!int.TryParse(param[0], NumberStyles.Integer, CultureInfo.InvariantCulture,
                                out int numerator) ||
                            !int.TryParse(param[1], NumberStyles.Integer, CultureInfo.InvariantCulture,
                                out int denominator) ||
                            denominator <= 0 || numerator < 0 || numerator > denominator)
                        {
                            failed = true;
                            break;
                        }
                        failed = Envir.Random.Next(denominator) >= numerator;
                        break;
                    case CheckType.LingFengResourcePercent:
                        {
                            int current = param[0] == "HP" ? player.HP : player.MP;
                            int maximum = param[0] == "HP" ? player.Stats[Stat.HP] : player.Stats[Stat.MP];
                            if (!TryGetPercentScale(param[3], out int scale) || maximum <= 0)
                            {
                                failed = true;
                                break;
                            }
                            long ratio = (long)current * scale / maximum;
                            failed = !LingFengNumericCommandExecutor.TryCheck(
                                ratio, new[] { param[1], param[2] }, out bool matched, out _) || !matched;
                        }
                        break;
                    case CheckType.InGuild:
                        if (param[0].Length > 0)
                        {
                            failed = player.MyGuild == null || player.MyGuild.Name != param[0];
                            break;
                        }

                        failed = player.MyGuild == null;
                        break;

                    case CheckType.CheckQuest:
                        if (!int.TryParse(param[0], out tempInt))
                        {
                            failed = true;
                            break;
                        }

                        string tempString = param[1].ToUpper();

                        if (tempString == "ACTIVE")
                        {
                            failed = !player.CurrentQuests.Any(e => e.Index == tempInt);
                        }
                        else //COMPLETE
                        {
                            failed = !player.CompletedQuests.Contains(tempInt);
                        }
                        break;
                    case CheckType.CheckRelationship:
                        if (player.Info.Married == 0)
                        {
                            failed = true;
                        }
                        break;
                    case CheckType.CheckWeddingRing:
                        failed = !player.CheckMakeWeddingRing();
                        break;
                    case CheckType.CheckPet:

                        bool petMatch = false;
                        for (int c = player.Pets.Count - 1; c >= 0; c--)
                        {
                            if (string.Compare(player.Pets[c].Info.Name, param[0], true) != 0) continue;

                            petMatch = true;
                        }

                        failed = !petMatch;
                        break;

                    case CheckType.HasBagSpace:
                        if (!int.TryParse(param[1], out tempInt))
                        {
                            failed = true;
                            break;
                        }

                        int slotCount = 0;

                        for (int k = 0; k < player.Info.Inventory.Length; k++)
                            if (player.Info.Inventory[k] == null) slotCount++;

                        failed = !Compare(param[0], slotCount, tempInt);
                        break;
                    case CheckType.IsNewHuman:
                        failed = player.Info.AccountInfo.Characters.Count > 1;
                        break;

                    case CheckType.CheckConquest:
                        if (!int.TryParse(param[0], out tempInt))
                        {
                            failed = true;
                            break;
                        }

                        try
                        {
                            ConquestObject Conquest = Envir.Conquests.FirstOrDefault(z => z.Info.Index == tempInt);
                            if (Conquest == null)
                            {
                                failed = true;
                                break;
                            }
                            failed = Conquest.WarIsOn;
                        }
                        catch (ArgumentException)
                        {
                            MessageQueue.Enqueue(string.Format("以玩家为对象的NPC命令CHECKCONQUEST中错误使用 {0} 操作符, 页码: {1}", param[0], Key));
                            return true;
                        }
                        break;
                    case CheckType.AffordGuard:
                        if (!int.TryParse(param[0], out tempInt) || !int.TryParse(param[1], out tempInt2))
                        {
                            failed = true;
                            break;
                        }

                        try
                        {
                            ConquestObject Conquest = Envir.Conquests.FirstOrDefault(z => z.Info.Index == tempInt);
                            if (Conquest == null)
                            {
                                failed = true;
                                break;
                            }

                            ConquestGuildArcherInfo 弓箭 = Conquest.ArcherList.FirstOrDefault(g => g.Info.Index == tempInt2);
                            if (弓箭 == null || 弓箭.GetRepairCost() == 0)
                            {
                                failed = true;
                                break;
                            }
                            if (player.MyGuild != null)
                                failed = (player.MyGuild.Gold < 弓箭.GetRepairCost());
                            else
                                failed = true;
                        }
                        catch (ArgumentException)
                        {
                            MessageQueue.Enqueue(string.Format("以玩家为对象的NPC命令AFFORDGUARD中错误使用 {0} 操作符, 页码: {1}", param[0], Key));
                            return true;
                        }
                        break;
                    case CheckType.AffordGate:
                        if (!int.TryParse(param[0], out tempInt) || !int.TryParse(param[1], out tempInt2))
                        {
                            failed = true;
                            break;
                        }

                        try
                        {
                            ConquestObject Conquest = Envir.Conquests.FirstOrDefault(z => z.Info.Index == tempInt);
                            if (Conquest == null)
                            {
                                failed = true;
                                break;
                            }

                            ConquestGuildGateInfo Gate = Conquest.GateList.FirstOrDefault(f => f.Info.Index == tempInt2);
                            if (Gate == null || Gate.GetRepairCost() == 0)
                            {
                                failed = true;
                                break;
                            }
                            if (player.MyGuild != null)
                                failed = (player.MyGuild.Gold < Gate.GetRepairCost());
                            else
                                failed = true;
                        }
                        catch (ArgumentException)
                        {
                            MessageQueue.Enqueue(string.Format("以玩家为对象的NPC命令AFFORDGATE中错误使用 {0} 操作符, 页码: {1}", param[0], Key));
                            return true;
                        }
                        break;
                    case CheckType.AffordWall:
                        if (!int.TryParse(param[0], out tempInt) || !int.TryParse(param[1], out tempInt2))
                        {
                            failed = true;
                            break;
                        }

                        try
                        {
                            ConquestObject Conquest = Envir.Conquests.FirstOrDefault(z => z.Info.Index == tempInt);
                            if (Conquest == null)
                            {
                                failed = true;
                                break;
                            }

                            ConquestGuildWallInfo Wall = Conquest.WallList.FirstOrDefault(h => h.Info.Index == tempInt2);
                            if (Wall == null || Wall.GetRepairCost() == 0)
                            {
                                failed = true;
                                break;
                            }
                            if (player.MyGuild != null)
                                failed = (player.MyGuild.Gold < Wall.GetRepairCost());
                            else
                                failed = true;
                        }
                        catch (ArgumentException)
                        {
                            MessageQueue.Enqueue(string.Format("以玩家为对象的NPC命令AFFORDWALL中错误使用 {0} 操作符, 页码: {1}", param[0], Key));
                            return true;
                        }
                        break;
                    case CheckType.AffordSiege:
                        if (!int.TryParse(param[0], out tempInt) || !int.TryParse(param[1], out tempInt2))
                        {
                            failed = true;
                            break;
                        }

                        try
                        {
                            ConquestObject Conquest = Envir.Conquests.FirstOrDefault(z => z.Info.Index == tempInt);
                            if (Conquest == null)
                            {
                                failed = true;
                                break;
                            }

                            ConquestGuildGateInfo Gate = Conquest.GateList.FirstOrDefault(f => f.Info.Index == tempInt2);
                            if (Gate == null || Gate.GetRepairCost() == 0)
                            {
                                failed = true;
                                break;
                            }
                            if (player.MyGuild != null)
                                failed = (player.MyGuild.Gold < Gate.GetRepairCost());
                            else
                                failed = true;
                        }
                        catch (ArgumentException)
                        {
                            MessageQueue.Enqueue(string.Format("以玩家为对象的NPC命令AFFORDSIEGE中错误使用 {0} 操作符, 页码: {1}", param[0], Key));
                            return true;
                        }
                        break;
                    case CheckType.CheckPermission:
                        GuildRankOptions guildPermissions;
                        if (!Enum.TryParse(param[0], true, out guildPermissions))
                        {
                            failed = true;
                            break;
                        }

                        if (player.MyGuild == null)
                        {
                            failed = true;
                            break;
                        }

                        failed = !(player.MyGuildRank.Options.HasFlag(guildPermissions));

                        break;
                    case CheckType.ConquestAvailable:
                        if (!int.TryParse(param[0], out tempInt))
                        {
                            failed = true;
                            break;
                        }

                        try
                        {
                            ConquestObject Conquest = Envir.Conquests.FirstOrDefault(z => z.Info.Index == tempInt);
                            if (Conquest == null)
                            {
                                failed = true;
                                break;
                            }

                            if (player.MyGuild != null)
                                failed = (Conquest.GuildInfo.AttackerID != -1);
                            else
                                failed = true;
                        }
                        catch (ArgumentException)
                        {
                            MessageQueue.Enqueue(string.Format("以玩家为对象的NPC命令CONQUESTAVAILABLE中错误使用 {0} 操作符, 页码: {1}", param[0], Key));
                            return true;
                        }
                        break;
                    case CheckType.ConquestOwner:
                        if (!int.TryParse(param[0], out tempInt))
                        {
                            failed = true;
                            break;
                        }

                        try
                        {
                            ConquestObject Conquest = Envir.Conquests.FirstOrDefault(z => z.Info.Index == tempInt);
                            if (Conquest == null)
                            {
                                failed = true;
                                break;
                            }

                            if (player.MyGuild != null && player.MyGuild.Guildindex == Conquest.GuildInfo.Owner)
                                failed = false;
                            else
                                failed = true;
                        }
                        catch (ArgumentException)
                        {
                            MessageQueue.Enqueue(string.Format("以玩家为对象的NPC命令CONQUESTOWNER错误使用 {0} 操作符, 页码: {1}", param[0], Key));
                            return true;
                        }
                        break;
                    case CheckType.CheckTimer:
                        {
                            if (!long.TryParse(param[1], out long time))
                            {
                                failed = true;
                                break;
                            }

                            try
                            {
                                var globalTimerKey = "_-" + param[0];

                                Timer timer;

                                if (Envir.Timers.ContainsKey(globalTimerKey))
                                {
                                    timer = Envir.Timers[globalTimerKey];
                                }
                                else
                                {
                                    timer = player.GetTimer(param[0]);
                                }

                                long remainingTime = 0;

                                if (timer != null)
                                {
                                    remainingTime = (timer.RelativeTime - Envir.Time) / 1000;
                                    break;
                                }

                                failed = !Compare(param[0], remainingTime, time);
                            }
                            catch (ArgumentException)
                            {
                                MessageQueue.Enqueue(string.Format("以玩家为对象的NPC命令CHECKTIMER中错误使用 {0} 操作符, 页码: {1}", param[0], Key));
                                return true;
                            }
                        }
                        break;
                    case CheckType.HeroLevel:
                        if (!int.TryParse(param[1], out tempInt))
                        {
                            failed = true;
                            break;
                        }
                        if (player.CurrentHero == null)
                        {
                            failed = true;
                        }
                        else
                        {
                            failed = !Compare(param[0], player.CurrentHero.Level, tempInt);
                        }
                        break;
                    case CheckType.CheckHeroClass:
                        MirClass heroClass;
                        if (!MirClass.TryParse(param[0], true, out heroClass))
                        {
                            failed = true;
                            break;
                        }
                        if (player.CurrentHero == null)
                        {
                            failed = true;
                        }
                        else
                        {
                            failed = player.CurrentHero.Class != heroClass;
                        }
                        break;
                    case CheckType.CheckHeroGender:
                        MirGender heroGender;
                        if (!MirGender.TryParse(param[0], false, out heroGender))
                        {
                            failed = true;
                            break;
                        }
                        if (player.CurrentHero == null)
                        {
                            failed = true;
                        }
                        else
                        {
                            failed = player.CurrentHero.Gender != heroGender;
                        }
                        break;
                    case CheckType.CheckHeroItem:
                        ushort heroItemCount;
                        ushort heroItemDura;
                        if (!ushort.TryParse(param[1], out heroItemCount))
                        {
                            failed = true;
                            break;
                        }
                        bool heroCheckDura = ushort.TryParse(param[2], out heroItemDura);
                        var heroItemInfo = Envir.GetItemInfo(param[0]);
                        if (player.CurrentHero == null || player.CurrentHero.Inventory == null)
                        {
                            failed = true;
                            break;
                        }
                        foreach (var item in player.CurrentHero.Inventory.Where(item => item != null && item.Info == heroItemInfo))
                        {
                            if (heroCheckDura)
                                if (item.CurrentDura < (heroItemDura * 1000)) continue;
                            if (heroItemCount > item.Count)
                            {
                                heroItemCount -= item.Count;
                                continue;
                            }
                            heroItemCount = 0;
                            break;
                        }
                        if (heroItemCount > 0)
                            failed = true;
                        break;
                    case CheckType.CheckBuff:
                        {
                            if (!Enum.TryParse(param[0], true, out BuffType buffType))
                            {
                                failed = true;
                                break;
                            }

                            failed = !player.HasBuff(buffType);
                        }
                        break;
                    case CheckType.CheckTransform:
                        {
                            if (!short.TryParse(param[0], out short transformType))
                            {
                                failed = true;
                                break;
                            }
                            failed = player.TransformType != transformType;
                        }
                        break;

                    case CheckType.IsGuildLeader:
                        failed = player.MyGuild == null || player.MyGuild.Ranks.Count == 0 || player.MyGuild.Ranks[0] != player.MyGuildRank;
                        break;

                }

                if (check.Negated) failed = !failed;
                if (requiredMatches > 0)
                {
                    if (failed) continue;
                    if (++matchedChecks >= requiredMatches)
                    {
                        Success(player);
                        return true;
                    }
                    continue;
                }
                if (!failed) continue;

                Failed(player);
                return false;
            }

            if (requiredMatches > 0 && matchedChecks < requiredMatches)
            {
                Failed(player);
                return false;
            }

            Success(player);
            return true;

        }

        private void Act(IList<NPCActions> acts)
        {
            var metricsEnabled = Settings.ScriptsRuntimeMetricsEnabled;

            for (var i = 0; i < acts.Count; i++)
            {
                string tempString = string.Empty;
                int tempInt;
                byte tempByte;
                Packet p;

                MonsterInfo monInfo;

                NPCActions act = acts[i];
                var start = metricsEnabled ? Server.Scripting.ScriptRuntimeMetrics.GetTimestamp() : 0;
                List<string> param = act.Params.ToList();
                Map map;
                ChatType chatType;

                try
                {
                    switch (act.Type)
                    {
                        case ActionType.ClearNameList:
                            tempString = param[0];
                            File.WriteAllLines(tempString, new string[] { });
                            break;

                        case ActionType.GlobalMessage:
                            if (!Enum.TryParse(param[1], true, out chatType)) return;

                            p = new S.Chat { Message = param[0], Type = chatType };
                            Envir.Broadcast(p);
                            break;

                        case ActionType.LingFengGuildNoticeMessage:
                            if (!TryDispatchLingFengGuildNotice(
                                    null, param[0], param[1], param[2], param[3]))
                                MessageQueue.Enqueue(
                                    $"[TxtScripts] GUILDNOTICEMSG 全局执行失败，页码：{Key}");
                            break;

                        case ActionType.Break:
                            Page.BreakFromSegments = true;
                            break;

                        case ActionType.Param1:
                            if (!int.TryParse(param[1], out tempInt)) return;

                            Param1 = param[0];
                            Param1Instance = tempInt;
                            break;

                        case ActionType.Param2:
                            if (!int.TryParse(param[0], out tempInt)) return;

                            Param2 = tempInt;
                            break;

                        case ActionType.Param3:
                            if (!int.TryParse(param[0], out tempInt)) return;

                            Param3 = tempInt;
                            break;

                        case ActionType.Mongen:
                            if (Param1 == null || Param2 == 0 || Param3 == 0) return;
                            if (!byte.TryParse(param[1], out tempByte)) return;

                            map = Envir.GetMapByNameAndInstance(Param1, Param1Instance);
                            if (map == null) return;

                            monInfo = Envir.GetMonsterInfo(param[0]);
                            if (monInfo == null) return;

                            for (int j = 0; j < tempByte; j++)
                            {
                                MonsterObject monster = MonsterObject.GetMonster(monInfo);
                                if (monster == null) return;
                                monster.Direction = 0;
                                monster.ActionTime = Envir.Time + 1000;
                                monster.Spawn(map, new Point(Param2, Param3));
                            }
                            break;

                        case ActionType.MonClear:
                            if (!int.TryParse(param[1], out tempInt)) return;

                            map = Envir.GetMapByNameAndInstance(param[0], tempInt);
                            if (map == null) return;

                            foreach (var cell in map.Cells)
                            {
                                if (cell == null || cell.Objects == null) continue;

                                for (int j = 0; j < cell.Objects.Count(); j++)
                                {
                                    MapObject ob = cell.Objects[j];

                                    if (ob.Race != ObjectType.Monster) continue;
                                    if (ob.Dead) continue;
                                    ob.Die();
                                }
                            }
                            break;
                    }
                }
                finally
                {
                    if (metricsEnabled)
                    {
                        var elapsed = Server.Scripting.ScriptRuntimeMetrics.GetTimestamp() - start;
                        Server.Scripting.ScriptRuntimeMetrics.RecordLegacyNpcAction(act.Type, elapsed);
                    }
                }
            }
        }
        private void Act(IList<NPCActions> acts, PlayerObject player)
        {
            MailInfo mailInfo = null;
            var metricsEnabled = Settings.ScriptsRuntimeMetricsEnabled;
            int whileIterations = 0;
            int? lastGivenInventoryIndex = null;
            int? linkedInventoryIndex = null;
            bool suppressOuterItemContext = false;

            for (var i = 0; i < acts.Count; i++)
            {
                NPCActions act = acts[i];
                List<string> param = act.Params.Select(t => FindVariable(player, t)).ToList();

                for (int j = 0; j < param.Count; j++)
                {
                    var parts = param[j].Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

                    if (parts.Length == 0) continue;

                    foreach (var part in parts)
                    {
                        PlayerObject renderPlayer = player;
                        if (act.Type == ActionType.LingFengTargetFormulation &&
                            TryGetLingFengCurrentTargetPlayer(player, out PlayerObject formulationTarget))
                            renderPlayer = formulationTarget;
                        param[j] = param[j].Replace(part, ReplaceValue(renderPlayer, part));
                    }

                    if (player.NPCData.TryGetValue("NPCInputStr", out object _npcInputStr))
                    {
                        param[j] = param[j].Replace("%INPUTSTR", (string)_npcInputStr);
                    }
                }

                if (linkedInventoryIndex is int linkedIndex &&
                    (linkedIndex < 0 || linkedIndex >= player.Info.Inventory.Length ||
                     player.Info.Inventory[linkedIndex] == null))
                    linkedInventoryIndex = null;
                UserItem linkedItem = linkedInventoryIndex is int activeIndex
                    ? player.Info.Inventory[activeIndex]
                    : null;
                object linkedItemEvent = linkedItem != null
                    ? new Server.Scripting.LingFengItemTriggerEvent(
                        Server.Scripting.LingFengItemTriggerKind.Use,
                        linkedItem.Info.FriendlyName, linkedInventoryIndex, 0)
                    : suppressOuterItemContext
                        ? new Server.Scripting.LingFengItemTriggerEvent(
                            Server.Scripting.LingFengItemTriggerKind.Use,
                            string.Empty, null, 0)
                        : null;
                using IDisposable linkedItemScope = linkedItemEvent == null
                    ? null
                    : Server.Scripting.LingFengTxtTriggerContext.Push(linkedItemEvent);

                switch (act.Type)
                {
                    case ActionType.LingFengWhile:
                        if (!TryEvaluateLingFengWhile(
                                player, param[0], param[1], param[2], out bool whileMatched))
                        {
                            MessageQueue.Enqueue(
                                $"[TxtScripts] WHILE 条件无效，已终止当前动作段，页码：{Key}");
                            return;
                        }
                        if (!whileMatched)
                        {
                            int end = FindMatchingLingFengWhileBoundary(
                                acts, i, ActionType.LingFengWhile, ActionType.LingFengEndWhile);
                            if (end < 0)
                            {
                                MessageQueue.Enqueue(
                                    $"[TxtScripts] WHILE 缺少 ENDWHILE，已终止当前动作段，页码：{Key}");
                                return;
                            }
                            i = end;
                        }
                        break;

                    case ActionType.LingFengAddHumNewValue:
                        if (!int.TryParse(param[0], NumberStyles.None,
                                CultureInfo.InvariantCulture, out int newValueIndex) ||
                            !int.TryParse(param[2], NumberStyles.Integer,
                                CultureInfo.InvariantCulture, out int newValue) ||
                            !int.TryParse(param[3], NumberStyles.None,
                                CultureInfo.InvariantCulture, out int newValueDuration) ||
                            !player.TryChangeLingFengNewValue(
                                $"{SourceKey}|{(ReferenceEquals(acts, ActList) ? "ACT" : "ELSEACT")}|{i}",
                                newValueIndex, param[1], newValue, newValueDuration))
                            MessageQueue.Enqueue(
                                $"[TxtScripts] ADDHUMNEWVALUE 参数无效，页码：{Key}");
                        break;

                    case ActionType.LingFengSetOnTimer:
                        if (!int.TryParse(param[0], NumberStyles.None,
                                CultureInfo.InvariantCulture, out int timerIndex) ||
                            !int.TryParse(param[1], NumberStyles.None,
                                CultureInfo.InvariantCulture, out int timerSeconds) ||
                            !int.TryParse(param[2], NumberStyles.None,
                                CultureInfo.InvariantCulture, out int timerExecutions) ||
                            !player.TrySetLingFengPersonalTimer(
                                timerIndex, timerSeconds, timerExecutions))
                            MessageQueue.Enqueue(
                                $"[TxtScripts] SETONTIMER 参数无效，页码：{Key}");
                        break;

                    case ActionType.LingFengSetOffTimer:
                        if (!int.TryParse(param[0], NumberStyles.None,
                                CultureInfo.InvariantCulture, out int offTimerIndex) ||
                            !player.TryStopLingFengPersonalTimer(offTimerIndex))
                            MessageQueue.Enqueue(
                                $"[TxtScripts] SETOFFTIMER 参数无效，页码：{Key}");
                        break;

                    case ActionType.LingFengLoopGoto:
                        if (!int.TryParse(param[1], NumberStyles.Integer,
                                CultureInfo.InvariantCulture, out int loopCount) ||
                            !NPCScript.TryGet(player.NPCScriptID, out NPCScript loopScript) ||
                            !loopScript.CallLingFengLoop(player, param[0], loopCount))
                            MessageQueue.Enqueue(
                                $"[TxtScripts] LOOPGOTO 执行失败，页码：{Key}");
                        break;

                    case ActionType.LingFengEndLoop:
                        if (!NPCScript.TryBreakLingFengLoop())
                            MessageQueue.Enqueue(
                                $"[TxtScripts] ENDLOOP 不在 LOOPGOTO 执行上下文中，页码：{Key}");
                        return;

                    case ActionType.LingFengEndWhile:
                        if (++whileIterations > MaximumLingFengWhileIterations)
                        {
                            MessageQueue.Enqueue(
                                $"[TxtScripts][TXT-RUNTIME-001] WHILE 超过 {MaximumLingFengWhileIterations} 次预算，已终止当前动作段，页码：{Key}");
                            return;
                        }
                        int start = FindMatchingLingFengWhileBoundary(
                            acts, i, ActionType.LingFengEndWhile, ActionType.LingFengWhile);
                        if (start < 0)
                        {
                            MessageQueue.Enqueue(
                                $"[TxtScripts] ENDWHILE 缺少 WHILE，已终止当前动作段，页码：{Key}");
                            return;
                        }
                        i = start - 1;
                        break;

                    case ActionType.Move:
                        {
                            if (!Server.Scripting.LingFengWorldCommandExecutor.TryPlanTeleport(
                                    param[0], param[1], param[2], out var teleport, out _)) return;

                            Map map = Envir.GetMapByNameAndInstance(teleport.MapName);
                            if (map == null) return;

                            var coords = new Point(teleport.X, teleport.Y);

                            if (!teleport.Random) player.Teleport(map, coords);
                            else player.TeleportRandom(200, 0, map);

                            if (Server.Scripting.ScriptTrace.IsEnabled(player))
                            {
                                var mode = (coords.X > 0 && coords.Y > 0) ? $"{param[0]}(0) {coords.X},{coords.Y}" : $"{param[0]}(0) RANDOM";
                                Server.Scripting.ScriptTrace.Record(player, $"[TXT] TELEPORT {mode}");
                            }
                        }
                        break;

                    case ActionType.InstanceMove:
                        {
                            if (!int.TryParse(param[1], out int instanceId)) return;
                            if (!int.TryParse(param[2], out int x)) return;
                            if (!int.TryParse(param[3], out int y)) return;

                            var map = Envir.GetMapByNameAndInstance(param[0], instanceId);
                            if (map == null) return;
                            player.Teleport(map, new Point(x, y));

                            if (Server.Scripting.ScriptTrace.IsEnabled(player))
                            {
                                Server.Scripting.ScriptTrace.Record(player, $"[TXT] TELEPORT {param[0]}({instanceId}) {x},{y}");
                            }
                        }
                        break;

                    case ActionType.ChangeGold:
                        {
                            if (!LingFengNumericCommandExecutor.TryAdjust(
                                    player.Account.Gold, param[0], param[1], 0, 2_100_000_000,
                                    false, out long adjusted, out string diagnostic))
                            {
                                MessageQueue.Enqueue($"[TxtScripts] GOLDCOUNT 失败：{diagnostic}，页码：{Key}");
                                break;
                            }

                            uint nextGold = (uint)adjusted;
                            if (nextGold > player.Account.Gold)
                                player.GainGold(nextGold - player.Account.Gold);
                            else if (nextGold < player.Account.Gold)
                            {
                                uint lost = player.Account.Gold - nextGold;
                                player.Account.Gold = nextGold;
                                player.Enqueue(new S.LoseGold { Gold = lost });
                            }
                        }
                        break;

                    case ActionType.ChangeDamageValue:
                        {
                            LingFengTxtTriggerContext context = LingFengTxtTriggerContext.Current;
                            if (context == null || !context.TryChangeDamageValue(param[0], param[1], param[2]))
                                MessageQueue.Enqueue($"[TxtScripts] CHANGEDAMAGEVALUE 仅允许在伤害前置触发中使用，页码：{Key}");
                        }
                        break;

                    case ActionType.GiveGold:
                        {
                            if (!uint.TryParse(param[0], out uint gold)) return;
                            gold = LingFengNumericCommandExecutor.PlanGoldGain(player.Account.Gold, gold);
                            player.GainGold(gold);
                        }
                        break;

                    case ActionType.TakeGold:
                        {
                            if (!uint.TryParse(param[0], out uint gold)) return;
                            gold = LingFengNumericCommandExecutor.PlanGoldTake(player.Account.Gold, gold);
                            player.Account.Gold -= gold;
                            player.Enqueue(new S.LoseGold { Gold = gold });
                        }
                        break;
                    case ActionType.GiveGuildGold:
                        {
                            if (!uint.TryParse(param[0], out uint gold)) return;

                            if (gold + player.MyGuild.Gold >= uint.MaxValue)
                                gold = uint.MaxValue - player.MyGuild.Gold;

                            player.MyGuild.Gold += gold;
                            player.MyGuild.SendServerPacket(new S.GuildStorageGoldChange() { Type = 3, Amount = gold });
                        }
                        break;
                    case ActionType.TakeGuildGold:
                        {
                            if (!uint.TryParse(param[0], out uint gold)) return;

                            if (gold >= player.MyGuild.Gold) gold = player.MyGuild.Gold;

                            player.MyGuild.Gold -= gold;
                            player.MyGuild.SendServerPacket(new S.GuildStorageGoldChange() { Type = 2, Amount = gold });
                        }
                        break;
                    case ActionType.GiveCredit:
                        {
                            if (!uint.TryParse(param[0], out uint credit)) return;

                            if (credit + player.Account.Credit >= uint.MaxValue)
                                credit = uint.MaxValue - player.Account.Credit;

                            player.GainCredit(credit);
                        }
                        break;

                    case ActionType.TakeCredit:
                        {
                            if (!uint.TryParse(param[0], out uint credit)) return;

                            if (credit >= player.Account.Credit) credit = player.Account.Credit;

                            player.Account.Credit -= credit;
                            player.Enqueue(new S.LoseCredit { Credit = credit });
                        }
                        break;

                    case ActionType.GivePearls:
                        {
                            if (!uint.TryParse(param[0], out uint pearls)) return;

                            if (pearls + player.Info.PearlCount >= int.MaxValue)
                                pearls = (uint)(int.MaxValue - player.Info.PearlCount);

                            player.IntelligentCreatureGainPearls((int)pearls);
                        }
                        break;

                    case ActionType.TakePearls:
                        {
                            if (!uint.TryParse(param[0], out uint pearls)) return;

                            if (pearls >= player.Info.PearlCount) pearls = (uint)player.Info.PearlCount;

                            player.IntelligentCreatureLosePearls((int)pearls);
                        }
                        break;

                    case ActionType.GiveItem:
                        {
                            if (param.Count < 2 || !ushort.TryParse(param[1], out ushort count)) count = 1;
                            var requested = count;
                            var given = 0;

                            var info = Envir.GetItemInfo(param[0]);

                            if (info == null)
                            {
                                MessageQueue.Enqueue(string.Format("无法获取物品信息: {0}, 页码: {1}", param[0], Key));
                                break;
                            }

                            while (count > 0)
                            {
                                UserItem item = Envir.CreateFreshItem(info);

                                if (item == null)
                                {
                                    MessageQueue.Enqueue(string.Format("无法创建用户物品: {0}, 页码: {1}", param[0], Key));
                                    return;
                                }

                                if (item.Info.StackSize > count)
                                {
                                    item.Count = count;
                                    count = 0;
                                }
                                else
                                {
                                    count -= item.Info.StackSize;
                                    item.Count = item.Info.StackSize;
                                }

                                if (player.CanGainItem(item))
                                {
                                    player.GainItem(item);
                                    given += item.Count;
                                }
                            }

                            if (Server.Scripting.ScriptTrace.IsEnabled(player))
                            {
                                Server.Scripting.ScriptTrace.Record(player, $"[TXT] GIVEITEM {param[0]} x{requested} -> {given}");
                            }
                        }
                        break;

                    case ActionType.TakeItem:
                        {
                            if (param.Count < 2 || !ushort.TryParse(param[1], out ushort count)) count = 1;
                            var requested = count;
                            var info = Envir.GetItemInfo(param[0]);

                            ushort dura;
                            bool checkDura = ushort.TryParse(param[2], out dura);

                            if (info == null)
                            {
                                MessageQueue.Enqueue(string.Format("TAKEITEM命令未能获取物品信息: {0}, 页码: {1}", param[0], Key));
                                break;
                            }

                            for (int j = 0; j < player.Info.Inventory.Length; j++)
                            {
                                UserItem item = player.Info.Inventory[j];
                                if (item == null) continue;
                                if (item.Info != info) continue;

                                if (checkDura)
                                {
                                    if (item.CurrentDura < (dura * 1000)) continue;
                                }

                                if (count > item.Count)
                                {
                                    player.Enqueue(new S.DeleteItem { UniqueID = item.UniqueID, Count = item.Count });
                                    player.Info.Inventory[j] = null;

                                    count -= item.Count;
                                    continue;
                                }

                                player.Enqueue(new S.DeleteItem { UniqueID = item.UniqueID, Count = count });
                                if (count == item.Count)
                                    player.Info.Inventory[j] = null;
                                else
                                    item.Count -= count;
                                count = 0;
                                break;
                            }
                            player.RefreshStats();

                            if (Server.Scripting.ScriptTrace.IsEnabled(player))
                            {
                                var taken = (int)requested - count;
                                Server.Scripting.ScriptTrace.Record(player, $"[TXT] TAKEITEM {param[0]} x{requested} -> {taken} (ok={count <= 0})");
                            }
                        }
                        break;

                    case ActionType.GiveExp:
                        {
                            uint tempUint;
                            if (!uint.TryParse(param[0], out tempUint)) return;
                            player.GainExp(tempUint);
                        }
                        break;

                    case ActionType.GivePet:
                        {
                            if (!Server.Scripting.LingFengWorldCommandExecutor.TryPlanPet(
                                    param[0], param[1], param[2], out var petPlan, out _)) return;

                            var monInfo = Envir.GetMonsterInfo(petPlan.MonsterName);
                            if (monInfo == null) return;

                            for (int j = 0; j < petPlan.Count; j++)
                            {
                                MonsterObject monster = MonsterObject.GetMonster(monInfo);
                                if (monster == null) return;
                                monster.PetLevel = petPlan.Level;
                                monster.Master = player;
                                monster.MaxPetLevel = 7;
                                monster.Direction = player.Direction;
                                monster.ActionTime = Envir.Time + 1000;
                                monster.Spawn(player.CurrentMap, player.CurrentLocation);
                                player.Pets.Add(monster);
                            }
                        }
                        break;

                    case ActionType.RemovePet:
                        {
                            for (int c = player.Pets.Count - 1; c >= 0; c--)
                            {
                                if (string.Compare(player.Pets[c].Info.Name, param[0], true) != 0) continue;

                                player.Pets[c].Die();
                            }
                        }
                        break;

                    case ActionType.ClearPets:
                        {
                            for (int c = player.Pets.Count - 1; c >= 0; c--)
                            {
                                player.Pets[c].DieNextTurn = true;
                            }
                        }
                        break;

                    case ActionType.AddNameList:
                        {
                            Envir.AddNameToNameListFromFilePath(param[0], player.Name);
                        }
                        break;


                    case ActionType.AddGuildNameList:
                        {
                            if (player.MyGuild == null) break;
                            Envir.AddNameToNameListFromFilePath(param[0], player.MyGuild.Name);
                        }
                        break;
                    case ActionType.DelNameList:
                        {
                            Envir.RemoveNameFromNameListFromFilePath(param[0], player.Name);
                        }
                        break;

                    case ActionType.DelGuildNameList:
                        {
                            if (player.MyGuild == null) break;
                            Envir.RemoveNameFromNameListFromFilePath(param[0], player.MyGuild.Name);
                        }
                        break;
                    case ActionType.ClearNameList:
                        {
                            Envir.ClearNameListFromFilePath(param[0]);
                        }
                        break;
                    case ActionType.ClearGuildNameList:
                        {
                            if (player.MyGuild == null) break;
                            Envir.ClearNameListFromFilePath(param[0]);
                        }
                        break;

                    case ActionType.GiveHP:
                        {
                            if (!int.TryParse(param[0], out int tempInt)) return;
                            player.ChangeHP(tempInt);
                        }
                        break;

                    case ActionType.GiveMP:
                        {
                            if (!int.TryParse(param[0], out int tempInt)) return;
                            player.ChangeMP(tempInt);
                        }
                        break;

                    case ActionType.ChangeLevel:
                        {
                            if (!ushort.TryParse(param[0], out ushort tempuShort)) return;
                            tempuShort = Math.Min(ushort.MaxValue, tempuShort);

                            player.Level = tempuShort;
                            player.Experience = 0;
                            player.LevelUp();
                        }
                        break;

                    case ActionType.ChangePkPoint:
                        {
                            if (!LingFengNumericCommandExecutor.TryAdjust(
                                    player.PKPoints, param[0], param[1], 0, int.MaxValue,
                                    true, out long adjusted, out string diagnostic))
                            {
                                MessageQueue.Enqueue($"[TxtScripts] CHANGEPKPOINT 失败：{diagnostic}，页码：{Key}");
                                break;
                            }
                            player.PKPoints = (int)adjusted;
                            if (Server.Scripting.ScriptTrace.IsEnabled(player))
                                Server.Scripting.ScriptTrace.Record(player, $"[TXT] PKPOINT {param[0]} {param[1]} -> {player.PKPoints}");
                        }
                        break;

                    case ActionType.TakeItemLingFeng:
                        {
                            if (!ushort.TryParse(param[1], out ushort required) || required == 0 ||
                                !int.TryParse(param[2], out int renamedMode) || renamedMode is not (0 or 1) ||
                                !int.TryParse(param[3], out int partialMode) || partialMode is not (0 or 1) ||
                                !int.TryParse(param[4], out int excludeCustomOk) || excludeCustomOk is not (0 or 1) ||
                                !int.TryParse(param[5], out int durabilityMode) || durabilityMode is not (0 or -1 or -2))
                            {
                                MessageQueue.Enqueue($"[TxtScripts] TAKE 扩展参数无效，页码：{Key}");
                                break;
                            }
                            if (excludeCustomOk == 0)
                            {
                                MessageQueue.Enqueue($"[TxtScripts] TAKE 参数5=0要求包含自定义OK框，但当前没有等价物品容器，页码：{Key}");
                                break;
                            }

                            var matches = player.Info.Inventory
                                .Select((item, index) => (item, index))
                                .Where(entry => entry.item != null &&
                                    LingFengItemCommandExecutor.NameMatches(
                                        entry.item.Info.FriendlyName, param[0], partialMode == 1) &&
                                    LingFengItemCommandExecutor.DurabilityMatches(
                                        entry.item.CurrentDura, entry.item.MaxDura, durabilityMode))
                                .ToArray();
                            int available = matches.Sum(entry => entry.item.Count);
                            if (available < required)
                            {
                                MessageQueue.Enqueue($"[TxtScripts] TAKE 匹配物品不足：需要 {required}，实际 {available}，页码：{Key}");
                                break;
                            }

                            int remaining = required;
                            foreach (var entry in matches)
                            {
                                ushort removed = (ushort)Math.Min(remaining, entry.item.Count);
                                player.Enqueue(new S.DeleteItem { UniqueID = entry.item.UniqueID, Count = removed });
                                if (removed == entry.item.Count)
                                    player.Info.Inventory[entry.index] = null;
                                else
                                    entry.item.Count -= removed;
                                remaining -= removed;
                                if (remaining == 0) break;
                            }
                            player.RefreshStats();
                            if (Server.Scripting.ScriptTrace.IsEnabled(player))
                                Server.Scripting.ScriptTrace.Record(player, $"[TXT] TAKE {param[0]} x{required} -> {required}");
                        }
                        break;

                    case ActionType.LingFengTargetTakeGold:
                        if (!TryGetLingFengCurrentTargetPlayer(
                                player, out PlayerObject goldTarget) ||
                            !uint.TryParse(param[0], NumberStyles.None,
                                CultureInfo.InvariantCulture, out uint targetGold) ||
                            goldTarget.Account == null || goldTarget.Account.Gold < targetGold)
                        {
                            MessageQueue.Enqueue(
                                $"[TxtScripts] <$CURRRTARGETNAME>.TAKE 金币失败，页码：{Key}");
                            break;
                        }
                        goldTarget.Account.Gold -= targetGold;
                        goldTarget.Enqueue(new S.LoseGold { Gold = targetGold });
                        break;

                    case ActionType.LingFengTargetGoto:
                    case ActionType.LingFengTargetDelayGoto:
                        {
                            int delayMilliseconds = 0;
                            int pageIndex = 0;
                            if (act.Type == ActionType.LingFengTargetDelayGoto)
                            {
                                pageIndex = 1;
                                if (!int.TryParse(param[0], NumberStyles.None,
                                        CultureInfo.InvariantCulture, out delayMilliseconds))
                                    break;
                            }
                            int scriptId = NPCScript.CurrentSystemScriptId ?? player.NPCScriptID;
                            if (!TryGetLingFengCurrentTargetPlayer(
                                    player, out PlayerObject pageTarget) ||
                                !pageTarget.TryScheduleLingFengTargetPage(
                                    scriptId, param[pageIndex], delayMilliseconds))
                                MessageQueue.Enqueue(
                                    $"[TxtScripts] 当前目标跳转调度失败，页码：{Key}");
                        }
                        break;

                    case ActionType.LingFengClearDelayGoto:
                        {
                            DelayedType delayedType = param[0] == "1"
                                ? DelayedType.LingFengDelayedMessage
                                : DelayedType.NPC;
                            foreach (DelayedAction pending in player.ActionList
                                         .Where(action => action.Type == delayedType))
                                pending.FlaggedToRemove = true;
                        }
                        break;

                    case ActionType.LingFengDecBindMoney:
                        if (!TryResolveLingFengBoundMoney(
                                player, param[0], param[1], out uint bindAmount,
                                out uint bindBalance, out string bindDiagnostic) ||
                            bindBalance < bindAmount)
                        {
                            MessageQueue.Enqueue(
                                $"[TxtScripts] DECBINDMONEY 失败：{bindDiagnostic}，页码：{Key}");
                            break;
                        }
                        player.Account.Gold -= bindAmount;
                        player.Enqueue(new S.LoseGold { Gold = bindAmount });
                        break;

                    case ActionType.LingFengTakeWornItem:
                        {
                            if (!int.TryParse(param[1], NumberStyles.None,
                                    CultureInfo.InvariantCulture, out int wornCount) ||
                                wornCount <= 0 ||
                                !player.TryTakeLingFengWornItem(param[0], wornCount))
                            {
                                MessageQueue.Enqueue(
                                    $"[TxtScripts] TAKEW 匹配装备不足或参数无效：{param[0]} x{param[1]}，页码：{Key}");
                                break;
                            }
                            if (Server.Scripting.ScriptTrace.IsEnabled(player))
                                Server.Scripting.ScriptTrace.Record(
                                    player, $"[TXT] TAKEW {param[0]} x{wornCount} -> {wornCount}");
                        }
                        break;

                    case ActionType.LingFengAddAccountList:
                        if (!TryAddLingFengAccountList(player, param[0]))
                        {
                            MessageQueue.Enqueue(
                                $"[TxtScripts] ADDACCOUNTLIST 写入失败，已终止当前动作段，页码：{Key}");
                            return;
                        }
                        break;

                    case ActionType.LingFengDelAccountList:
                        if (!TryRemoveLingFengAccountList(player, param[0]))
                        {
                            MessageQueue.Enqueue(
                                $"[TxtScripts] DELACCOUNTLIST 写入失败，已终止当前动作段，页码：{Key}");
                            return;
                        }
                        break;

                    case ActionType.LingFengAddNameDateTimeList:
                        if (!Server.Scripting.LingFengScriptReferenceResolver.TryResolveCandidateTextKey(
                                param[0], out string membershipKey) ||
                            !int.TryParse(param[1], NumberStyles.None, CultureInfo.InvariantCulture,
                                out int membershipDays) ||
                            !int.TryParse(param[2], NumberStyles.None, CultureInfo.InvariantCulture,
                                out int membershipHours) ||
                            !int.TryParse(param[3], NumberStyles.None, CultureInfo.InvariantCulture,
                                out int membershipMinutes) ||
                            !player.Info.LingFengProgress.AddTimedMembership(
                                membershipKey, Envir.Now,
                                membershipDays, membershipHours, membershipMinutes))
                        {
                            MessageQueue.Enqueue(
                                $"[TxtScripts] ADDNAMEDATETIMELIST 失败，已终止当前动作段，页码：{Key}");
                            return;
                        }
                        break;

                    case ActionType.LingFengTakeBagItem:
                        {
                            if (param.Count is < 8 or > 13 ||
                                !int.TryParse(param[1], NumberStyles.None, CultureInfo.InvariantCulture,
                                    out int maximum) || maximum <= 0 ||
                                !uint.TryParse(param[2], NumberStyles.None, CultureInfo.InvariantCulture,
                                    out uint gameGoldEach) ||
                                !uint.TryParse(param[3], NumberStyles.None, CultureInfo.InvariantCulture,
                                    out uint goldEach) ||
                                !uint.TryParse(param[4], NumberStyles.None, CultureInfo.InvariantCulture,
                                    out uint gamePointEach) ||
                                !uint.TryParse(param[5], NumberStyles.None, CultureInfo.InvariantCulture,
                                    out uint experienceEach) ||
                                !int.TryParse(param[7], NumberStyles.None, CultureInfo.InvariantCulture,
                                    out int pearlExperience) || pearlExperience is not (0 or 1))
                            {
                                MessageQueue.Enqueue($"[TxtScripts] TAKEBAGITEM 基础参数无效，页码：{Key}");
                                break;
                            }
                            if (gameGoldEach != 0 || gamePointEach != 0 ||
                                (pearlExperience == 1 && experienceEach != 0))
                            {
                                MessageQueue.Enqueue(
                                    $"[TxtScripts] TAKEBAGITEM 元宝、泡点或聚灵珠经验需要独立账本适配器，未回收物品，页码：{Key}");
                                break;
                            }
                            if (param.Count > 8 &&
                                (!int.TryParse(param[8], NumberStyles.None, CultureInfo.InvariantCulture,
                                     out int suppressPrompt) || suppressPrompt is not (0 or 1)))
                            {
                                MessageQueue.Enqueue($"[TxtScripts] TAKEBAGITEM 提示参数无效，页码：{Key}");
                                break;
                            }

                            HashSet<byte> colours = null;
                            if (param.Count > 9 && !string.IsNullOrWhiteSpace(param[9]) && param[9] != "*")
                            {
                                colours = new HashSet<byte>();
                                foreach (string value in param[9].Split('|', StringSplitOptions.RemoveEmptyEntries))
                                {
                                    if (!byte.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture,
                                            out byte colour))
                                    {
                                        colours = null;
                                        break;
                                    }
                                    colours.Add(colour);
                                }
                                if (colours == null || colours.Count == 0)
                                {
                                    MessageQueue.Enqueue($"[TxtScripts] TAKEBAGITEM 颜色筛选无效，页码：{Key}");
                                    break;
                                }
                            }
                            if (param.Count > 10 && param[10] != "0" ||
                                param.Count > 11 && param[11] != "0" ||
                                param.Count > 12 && param[12] != "0")
                            {
                                MessageQueue.Enqueue(
                                    $"[TxtScripts] TAKEBAGITEM 极品、改名或等级筛选尚无完整实例模型，未回收物品，页码：{Key}");
                                break;
                            }

                            var names = new HashSet<string>(
                                param[0].Split('|', StringSplitOptions.RemoveEmptyEntries |
                                                     StringSplitOptions.TrimEntries),
                                StringComparer.OrdinalIgnoreCase);
                            if (names.Count == 0)
                            {
                                MessageQueue.Enqueue($"[TxtScripts] TAKEBAGITEM 物品列表为空，页码：{Key}");
                                break;
                            }
                            var matches = player.Info.Inventory
                                .Select((item, index) => (item, index))
                                .Where(entry => entry.item != null &&
                                    (names.Contains(entry.item.Info.Name) ||
                                     names.Contains(entry.item.Info.FriendlyName)) &&
                                    (colours == null || colours.Contains(entry.item.LingFengNameColour)))
                                .ToArray();
                            int available = matches.Sum(entry => entry.item.Count);
                            int planned = Math.Min(maximum, available);
                            if ((ulong)goldEach * (ulong)planned > uint.MaxValue ||
                                (ulong)experienceEach * (ulong)planned > uint.MaxValue)
                            {
                                MessageQueue.Enqueue(
                                    $"[TxtScripts] TAKEBAGITEM 奖励乘积溢出，未回收物品，页码：{Key}");
                                break;
                            }

                            int remaining = planned;
                            foreach (var entry in matches)
                            {
                                ushort removed = (ushort)Math.Min(remaining, entry.item.Count);
                                player.Enqueue(new S.DeleteItem { UniqueID = entry.item.UniqueID, Count = removed });
                                if (removed == entry.item.Count)
                                    player.Info.Inventory[entry.index] = null;
                                else
                                    entry.item.Count -= removed;
                                remaining -= removed;
                                if (remaining == 0) break;
                            }
                            AddVariable(player, param[6], planned.ToString(CultureInfo.InvariantCulture));
                            uint goldReward = checked(goldEach * (uint)planned);
                            if (goldReward > 0)
                                player.GainGold(LingFengNumericCommandExecutor.PlanGoldGain(
                                    player.Account.Gold, goldReward));
                            uint experienceReward = checked(experienceEach * (uint)planned);
                            if (experienceReward > 0) player.GainExp(experienceReward);
                            player.RefreshStats();
                            if (Server.Scripting.ScriptTrace.IsEnabled(player))
                                Server.Scripting.ScriptTrace.Record(
                                    player, $"[TXT] TAKEBAGITEM {param[0]} -> {planned}");
                        }
                        break;

                    case ActionType.LingFengTakeBagItemByIndex:
                        {
                            if (param.Count is < 8 or > 13 ||
                                !TryParseLingFengIndexRanges(param[0], out HashSet<int> itemIndexes) ||
                                !int.TryParse(param[1], NumberStyles.None, CultureInfo.InvariantCulture,
                                    out int maximum) || maximum <= 0 ||
                                !uint.TryParse(param[2], NumberStyles.None, CultureInfo.InvariantCulture,
                                    out uint gameGoldEach) ||
                                !uint.TryParse(param[3], NumberStyles.None, CultureInfo.InvariantCulture,
                                    out uint goldEach) ||
                                !uint.TryParse(param[4], NumberStyles.None, CultureInfo.InvariantCulture,
                                    out uint gamePointEach) ||
                                !uint.TryParse(param[5], NumberStyles.None, CultureInfo.InvariantCulture,
                                    out uint experienceEach) ||
                                !int.TryParse(param[7], NumberStyles.None, CultureInfo.InvariantCulture,
                                    out int pearlExperience) || pearlExperience is not (0 or 1))
                            {
                                MessageQueue.Enqueue(
                                    $"[TxtScripts] TAKEBAGITEMEX 基础参数无效，页码：{Key}");
                                break;
                            }
                            if (gameGoldEach != 0 || goldEach != 0 || gamePointEach != 0 ||
                                experienceEach != 0 || pearlExperience != 0)
                            {
                                MessageQueue.Enqueue(
                                    $"[TxtScripts] TAKEBAGITEMEX 奖励账本组合尚未适配，未回收物品，页码：{Key}");
                                break;
                            }
                            if (param.Count > 8 &&
                                (!int.TryParse(param[8], NumberStyles.None, CultureInfo.InvariantCulture,
                                     out int suppressPrompt) || suppressPrompt is not (0 or 1)))
                            {
                                MessageQueue.Enqueue(
                                    $"[TxtScripts] TAKEBAGITEMEX 提示参数无效，页码：{Key}");
                                break;
                            }
                            HashSet<byte> colours = null;
                            if (param.Count > 9 && !string.IsNullOrWhiteSpace(param[9]) && param[9] != "*")
                            {
                                colours = new HashSet<byte>();
                                foreach (string value in param[9].Split(
                                             '|', StringSplitOptions.RemoveEmptyEntries))
                                {
                                    if (!byte.TryParse(value, NumberStyles.None,
                                            CultureInfo.InvariantCulture, out byte colour))
                                    {
                                        colours = null;
                                        break;
                                    }
                                    colours.Add(colour);
                                }
                                if (colours == null || colours.Count == 0)
                                {
                                    MessageQueue.Enqueue(
                                        $"[TxtScripts] TAKEBAGITEMEX 颜色筛选无效，页码：{Key}");
                                    break;
                                }
                            }
                            if (param.Skip(10).Any(value => value != "0"))
                            {
                                MessageQueue.Enqueue(
                                    $"[TxtScripts] TAKEBAGITEMEX 极品、改名或等级筛选尚无完整实例模型，未回收物品，页码：{Key}");
                                break;
                            }

                            var matches = player.Info.Inventory
                                .Select((item, index) => (item, index))
                                .Where(entry => entry.item?.Info != null &&
                                    itemIndexes.Contains(entry.item.Info.Index) &&
                                    (colours == null || colours.Contains(entry.item.LingFengNameColour)))
                                .ToArray();
                            int planned = Math.Min(maximum,
                                matches.Sum(entry => (int)entry.item.Count));
                            int remaining = planned;
                            foreach (var entry in matches)
                            {
                                ushort removed = (ushort)Math.Min(remaining, entry.item.Count);
                                player.Enqueue(new S.DeleteItem
                                    { UniqueID = entry.item.UniqueID, Count = removed });
                                if (removed == entry.item.Count)
                                    player.Info.Inventory[entry.index] = null;
                                else
                                    entry.item.Count -= removed;
                                remaining -= removed;
                                if (remaining == 0) break;
                            }
                            AddVariable(player, param[6], planned.ToString(CultureInfo.InvariantCulture));
                            if (Server.Scripting.ScriptTrace.IsEnabled(player))
                                Server.Scripting.ScriptTrace.Record(
                                    player, $"[TXT] TAKEBAGITEMEX {param[0]} -> {planned}");
                        }
                        break;

                    case ActionType.LingFengChangeMapDescription:
                        {
                            if (player.CurrentMap?.Info == null || param[1] != "0")
                            {
                                MessageQueue.Enqueue(
                                    $"[TxtScripts] CHANGEMAPDESC 持久保存尚未适配，未修改地图，页码：{Key}");
                                break;
                            }
                            player.CurrentMap.Info.LingFengRuntimeTitle = param[0];
                            foreach (PlayerObject recipient in Envir.Players
                                         .Where(target => target.CurrentMap == player.CurrentMap)
                                         .Concat(new[] { player })
                                         .Distinct())
                                recipient.RefreshLingFengCurrentMapInformation();
                        }
                        break;

                    case ActionType.SetPkPoint:
                        {
                            if (!int.TryParse(param[0], out int tempInt)) return;
                            player.PKPoints = tempInt;

                            if (Server.Scripting.ScriptTrace.IsEnabled(player))
                            {
                                Server.Scripting.ScriptTrace.Record(player, $"[TXT] PKPOINT = {tempInt}");
                            }
                        }
                        break;

                    case ActionType.ReducePkPoint:
                        {
                            if (!int.TryParse(param[0], out int tempInt)) return;

                            player.PKPoints -= tempInt;
                            if (player.PKPoints < 0) player.PKPoints = 0;

                            if (Server.Scripting.ScriptTrace.IsEnabled(player))
                            {
                                Server.Scripting.ScriptTrace.Record(player, $"[TXT] PKPOINT -{tempInt} -> {player.PKPoints}");
                            }
                        }
                        break;

                    case ActionType.IncreasePkPoint:
                        {
                            if (!int.TryParse(param[0], out int tempInt)) return;
                            player.PKPoints += tempInt;

                            if (Server.Scripting.ScriptTrace.IsEnabled(player))
                            {
                                Server.Scripting.ScriptTrace.Record(player, $"[TXT] PKPOINT +{tempInt} -> {player.PKPoints}");
                            }
                        }
                        break;

                    case ActionType.ChangeGender:
                        {
                            switch (player.Info.Gender)
                            {
                                case MirGender.男性:
                                    player.Info.Gender = MirGender.女性;
                                    break;
                                case MirGender.女性:
                                    player.Info.Gender = MirGender.男性;
                                    break;
                            }

                            if (Server.Scripting.ScriptTrace.IsEnabled(player))
                            {
                                Server.Scripting.ScriptTrace.Record(player, $"[TXT] GENDER -> {player.Info.Gender}");
                            }
                        }
                        break;

                    case ActionType.ChangeHair:
                        {
                            if (param.Count < 1)
                            {
                                player.Info.Hair = (byte)Envir.Random.Next(0, 9);
                            }
                            else
                            {
                                byte.TryParse(param[0], out byte tempByte);

                                if (tempByte >= 0 && tempByte <= 9)
                                {
                                    player.Info.Hair = tempByte;
                                }
                            }
                        }
                        break;

                    case ActionType.ChangeClass:
                        {
                            if (!Enum.TryParse(param[0], true, out MirClass mirClass)) return;

                            switch (mirClass)
                            {
                                case MirClass.战士:
                                    player.Info.Class = MirClass.战士;
                                    break;
                                case MirClass.道士:
                                    player.Info.Class = MirClass.道士;
                                    break;
                                case MirClass.法师:
                                    player.Info.Class = MirClass.法师;
                                    break;
                                case MirClass.刺客:
                                    player.Info.Class = MirClass.刺客;
                                    break;
                                case MirClass.弓箭:
                                    player.Info.Class = MirClass.弓箭;
                                    break;
                            }
                        }
                        break;

                    case ActionType.LocalMessage:
                        {
                            ChatType chatType;
                            if (!Enum.TryParse(param[1], true, out chatType)) return;
                            player.ReceiveChat(param[0], chatType);

                            if (Server.Scripting.ScriptTrace.IsEnabled(player))
                            {
                                Server.Scripting.ScriptTrace.Record(player, $"[TXT] MESSAGE({chatType}) {param[0]}");
                            }
                        }
                        break;

                    case ActionType.GlobalMessage:
                        {
                            if (!Enum.TryParse(param[1], true, out ChatType chatType)) return;

                            var p = new S.Chat { Message = param[0], Type = chatType };
                            Envir.Broadcast(p);
                        }
                        break;

                    case ActionType.GiveSkill:
                        {
                            byte spellLevel = 0;

                            Spell skill;
                            if (!Enum.TryParse(param[0], true, out skill)) return;

                            if (player.Info.Magics.Any(e => e.Spell == skill)) break;

                            if (param.Count > 1)
                                spellLevel = byte.TryParse(param[1], out spellLevel) ? Math.Min((byte)3, spellLevel) : (byte)0;

                            var magic = new UserMagic(skill) { Level = spellLevel };

                            if (magic.Info == null) return;

                            player.Info.Magics.Add(magic);
                            player.SendMagicInfo(magic);
                        }
                        break;

                    case ActionType.RemoveSkill:
                        {
                            if (!Enum.TryParse(param[0], true, out Spell skill)) return;

                            if (!player.Info.Magics.Any(e => e.Spell == skill)) break;

                            for (var j = player.Info.Magics.Count - 1; j >= 0; j--)
                            {
                                if (player.Info.Magics[j].Spell != skill) continue;

                                player.Info.Magics.RemoveAt(j);
                                player.Enqueue(new S.RemoveMagic { PlaceId = j });
                            }
                        }
                        break;

                    case ActionType.Goto:
                        {
                            DelayedAction action = new DelayedAction(DelayedType.NPC, -1, player.NPCObjectID, player.NPCScriptID, "[" + param[0] + "]");
                            player.ActionList.Add(action);
                        }
                        break;

                    case ActionType.GotoLabel:
                        {
                            if (NPCScript.BlockSystemNavigation(nameof(ActionType.GotoLabel))) break;
                            if (!int.TryParse(param[0], out int mode) || mode < 0 || mode > 8) return;
                            string label = param[1].Trim('[', ']');
                            if (!label.StartsWith('@')) label = "@" + label;

                            int range = -1;
                            if (mode <= 7 && param.Count > 2 && !int.TryParse(param[2], out range)) return;
                            IEnumerable<PlayerObject> candidates;
                            switch (mode)
                            {
                                case 0:
                                case 4:
                                    candidates = player.GroupMembers ?? new List<PlayerObject> { player };
                                    break;
                                case 1:
                                case 5:
                                    candidates = player.MyGuild?.GetOnlinePlayers() ?? new List<PlayerObject> { player };
                                    break;
                                case 2:
                                case 3:
                                case 6:
                                case 7:
                                    candidates = player.CurrentMap?.Players ?? new List<PlayerObject>();
                                    break;
                                case 8:
                                    if (param.Count < 6 ||
                                        !int.TryParse(param[2], out int x) ||
                                        !int.TryParse(param[3], out int y) ||
                                        !int.TryParse(param[4], out range) ||
                                        !int.TryParse(param[5], out int exclude)) return;
                                    Point center = new Point(x, y);
                                    candidates = (player.CurrentMap?.Players ?? new List<PlayerObject>())
                                        .Where(target => Functions.InRange(target.CurrentLocation, center, Math.Max(0, range)) &&
                                                         (exclude == 0 || target != player));
                                    break;
                                default:
                                    return;
                            }

                            bool transferVariable = false;
                            ScriptVariableValue transferredValue = default;
                            string receiveReference = string.Empty;
                            if (mode == 8 && param.Count > 6)
                            {
                                string sourceReference = param[6];
                                receiveReference = param.Count > 7 ? param[7] : string.Empty;
                                if (string.IsNullOrWhiteSpace(sourceReference) !=
                                    string.IsNullOrWhiteSpace(receiveReference))
                                {
                                    MessageQueue.Enqueue($"[Variables][TXT] GOTOLABEL 8 变量传递失败：传递变量和接收变量必须同时为空或同时填写，页码：{Key}");
                                    return;
                                }
                                if (!string.IsNullOrWhiteSpace(sourceReference))
                                {
                                    if (!ScriptVariableReferenceParser.TryParse(sourceReference, out var source))
                                    {
                                        MessageQueue.Enqueue($"[Variables][TXT] GOTOLABEL 8 变量传递失败：传递变量引用无效，页码：{Key}");
                                        return;
                                    }
                                    ScriptVariableReadResult read = Envir.CSharpScripts.VariableModule.Read(
                                        ScriptVariableContext.ForConversation(
                                            player, player.NPCObjectID, player.CurrentMap), source);
                                    if (!read.Success)
                                    {
                                        MessageQueue.Enqueue($"[Variables][TXT] GOTOLABEL 8 变量传递失败：{read.ErrorCode} {read.Diagnostic}，页码：{Key}");
                                        return;
                                    }
                                    if (!ScriptVariableReferenceParser.TryParse(receiveReference, out _))
                                    {
                                        MessageQueue.Enqueue($"[Variables][TXT] GOTOLABEL 8 变量传递失败：接收变量引用无效，页码：{Key}");
                                        return;
                                    }
                                    transferredValue = read.Value;
                                    transferVariable = true;
                                }
                            }

                            bool excludeSelf = mode >= 4 && mode <= 7;
                            foreach (PlayerObject target in candidates.Distinct().ToArray())
                            {
                                if (target == null || excludeSelf && target == player) continue;
                                if (range >= 0 && mode <= 7 &&
                                    (target.CurrentMap != player.CurrentMap ||
                                     !Functions.InRange(target.CurrentLocation, player.CurrentLocation, range))) continue;

                                if (transferVariable)
                                {
                                    ScriptVariableMutationResult transfer = Envir.CSharpScripts.VariableCommands.Mutate(
                                        ScriptVariableContext.ForConversation(
                                            target, player.NPCObjectID, target.CurrentMap),
                                        receiveReference, "MOV", transferredValue.Format());
                                    if (!transfer.Success)
                                    {
                                        MessageQueue.Enqueue($"[Variables][TXT] GOTOLABEL 8 变量传递失败：{transfer.ErrorCode} {transfer.Diagnostic}，目标：{target.Name}，页码：{Key}");
                                        continue;
                                    }
                                }

                                target.ActionList.Add(new DelayedAction(
                                    DelayedType.NPC, -1, player.NPCObjectID, player.NPCScriptID, $"[{label}]"));
                            }
                        }
                        break;

                    case ActionType.Call:
                        {
                            if (NPCScript.BlockSystemNavigation(nameof(ActionType.Call))) break;
                            if (!int.TryParse(param[0], out int scriptID)) return;

                            string targetPage = param.Count > 1 ? param[1] : "[@MAIN]";
                            if (!targetPage.StartsWith("[@", StringComparison.Ordinal))
                                targetPage = $"[{targetPage}]";
                            var action = new DelayedAction(DelayedType.NPC, -1, player.NPCObjectID, scriptID, targetPage);
                            player.ActionList.Add(action);
                        }
                        break;

                    case ActionType.Break:
                        {
                            Page.BreakFromSegments = true;
                        }
                        break;

                    case ActionType.Set:
                        {
                            int flagIndex;
                            uint onCheck;
                            if (!int.TryParse(param[0], out flagIndex)) return;
                            if (!uint.TryParse(param[1], out onCheck)) return;

                            if (flagIndex < 0 || flagIndex >= Globals.FlagIndexCount) return;
                            var flagIsOn = Convert.ToBoolean(onCheck);

                            player.Info.Flags[flagIndex] = flagIsOn;

                            for (int f = player.CurrentMap.NPCs.Count - 1; f >= 0; f--)
                            {
                                if (Functions.InRange(player.CurrentMap.NPCs[f].CurrentLocation, player.CurrentLocation, Globals.DataRange))
                                    player.CurrentMap.NPCs[f].CheckVisible(player);
                            }

                            if (flagIsOn) player.CheckNeedQuestFlag(flagIndex);
                        }
                        break;

                    case ActionType.Param1:
                        {
                            if (!int.TryParse(param[1], out int tempInt)) return;

                            Param1 = param[0];
                            Param1Instance = tempInt;
                        }
                        break;

                    case ActionType.Param2:
                        {
                            if (!int.TryParse(param[0], out int tempInt)) return;

                            Param2 = tempInt;
                        }
                        break;

                    case ActionType.Param3:
                        {
                            if (!int.TryParse(param[0], out int tempInt)) return;

                            Param3 = tempInt;
                        }
                        break;

                    case ActionType.Mongen:
                        {
                            if (Param1 == null || Param2 == 0 || Param3 == 0) return;
                            if (!byte.TryParse(param[1], out byte tempByte)) return;

                            Map map = Envir.GetMapByNameAndInstance(Param1, Param1Instance);
                            if (map == null) return;

                            var monInfo = Envir.GetMonsterInfo(param[0]);
                            if (monInfo == null) return;

                            for (int j = 0; j < tempByte; j++)
                            {
                                MonsterObject monster = MonsterObject.GetMonster(monInfo);
                                if (monster == null) return;
                                monster.Direction = 0;
                                monster.ActionTime = Envir.Time + 1000;
                                monster.Spawn(map, new Point(Param2, Param3));
                            }
                        }
                        break;

                    case ActionType.TimeRecall:
                        {
                            if (NPCScript.BlockSystemNavigation(nameof(ActionType.TimeRecall))) break;
                            var tempString = "";
                            if (!long.TryParse(param[0], out long tempLong)) return;

                            if (param[1].Length > 0) tempString = "[" + param[1] + "]";

                            Map tempMap = player.CurrentMap;
                            Point tempPoint = player.CurrentLocation;

                            var action = new DelayedAction(DelayedType.NPC, Envir.Time + (tempLong * 1000), player.NPCObjectID, player.NPCScriptID, tempString, tempMap, tempPoint);
                            player.ActionList.Add(action);
                        }
                        break;

                    case ActionType.TimeRecallGroup:
                        {
                            if (NPCScript.BlockSystemNavigation(nameof(ActionType.TimeRecallGroup))) break;
                            var tempString = "";
                            if (player.GroupMembers == null) return;
                            if (!long.TryParse(param[0], out long tempLong)) return;
                            if (param[1].Length > 0) tempString = "[" + param[1] + "]";

                            for (int j = 0; j < player.GroupMembers.Count(); j++)
                            {
                                var groupMember = player.GroupMembers[j];

                                var action = new DelayedAction(DelayedType.NPC, Envir.Time + (tempLong * 1000), player.NPCObjectID, player.NPCScriptID, tempString, player.CurrentMap, player.CurrentLocation);
                                groupMember.ActionList.Add(action);
                            }
                        }
                        break;

                    case ActionType.BreakTimeRecall:
                        {
                            if (NPCScript.BlockSystemNavigation(nameof(ActionType.BreakTimeRecall))) break;
                            foreach (DelayedAction ac in player.ActionList.Where(u => u.Type == DelayedType.NPC))
                            {
                                ac.FlaggedToRemove = true;
                            }
                        }
                        break;

                    case ActionType.DelayGoto:
                        {
                            if (NPCScript.BlockSystemNavigation(nameof(ActionType.DelayGoto))) break;
                            if (!long.TryParse(param[0], out long tempLong)) return;

                            var action = new DelayedAction(DelayedType.NPC, Envir.Time + (tempLong * 1000), player.NPCObjectID, player.NPCScriptID, "[" + param[1] + "]");
                            player.ActionList.Add(action);
                        }
                        break;

                    case ActionType.MonClear:
                        {
                            if (!int.TryParse(param[1], out int tempInt)) return;

                            var map = Envir.GetMapByNameAndInstance(param[0], tempInt);
                            if (map == null) return;

                            foreach (var cell in map.Cells)
                            {
                                if (cell == null || cell.Objects == null) continue;

                                for (int j = 0; j < cell.Objects.Count(); j++)
                                {
                                    MapObject ob = cell.Objects[j];

                                    if (ob.Race != ObjectType.Monster) continue;
                                    if (ob is MonsterObject ownedMonster &&
                                        IsHumanOwnedActor(ownedMonster)) continue;
                                    if (ob.Dead) continue;

                                    if (!string.IsNullOrEmpty(param[2]) && string.Compare(param[2], ((MonsterObject)ob).Info.Name, true) != 0)
                                        continue;

                                    ob.Die();
                                }
                            }
                        }
                        break;

                    case ActionType.GroupRecall:
                        {
                            if (player.GroupMembers == null) return;

                            for (int j = 0; j < player.GroupMembers.Count(); j++)
                            {
                                player.GroupMembers[j].Teleport(player.CurrentMap, player.CurrentLocation);
                            }
                        }
                        break;

                    case ActionType.GroupTeleport:
                        {
                            if (player.GroupMembers == null) return;
                            if (!int.TryParse(param[1], out int tempInt)) return;
                            if (!int.TryParse(param[2], out int x)) return;
                            if (!int.TryParse(param[3], out int y)) return;

                            var map = Envir.GetMapByNameAndInstance(param[0], tempInt);
                            if (map == null) return;

                            for (int j = 0; j < player.GroupMembers.Count(); j++)
                            {
                                if (x == 0 || y == 0)
                                {
                                    player.GroupMembers[j].TeleportRandom(200, 0, map);
                                }
                                else
                                {
                                    player.GroupMembers[j].Teleport(map, new Point(x, y));
                                }
                            }
                        }
                        break;

                    case ActionType.Mov:
                        {
                            string value = param[0];
                            AddVariable(player, value, param[1]);
                        }
                        break;

                    case ActionType.Calc:
                        {
                            int left;
                            int right;

                            bool resultLeft = int.TryParse(param[0], out left);
                            bool resultRight = int.TryParse(param[2], out right);

                            if (resultLeft && resultRight)
                            {
                                try
                                {
                                    int result = Calculate(param[1], left, right);
                                    AddVariable(player, param[3].Replace("-", ""), result.ToString());
                                }
                                catch (ArgumentException)
                                {
                                    MessageQueue.Enqueue(string.Format("以列表的玩家为对象的NPC命令CALC中错误使用 {0} 操作符, 页码: {1}", param[1], Key));
                                }
                            }
                            else
                            {
                                AddVariable(player, param[3].Replace("-", ""), param[0] + param[2]);
                            }
                        }
                        break;

                    case ActionType.LingFengGiveStateItem:
                        {
                            if (param.Count != 9 ||
                                !TryParseLingFengStateItemFlags(param.Skip(1).Take(7), out BindMode flags) ||
                                !ushort.TryParse(param[8], out ushort count) || count == 0)
                                return;

                            ushort requested = count;
                            int given = 0;
                            ItemInfo info = Envir.GetItemInfo(param[0]);
                            if (info == null)
                            {
                                MessageQueue.Enqueue($"无法获取物品信息: {param[0]}, 页码: {Key}");
                                break;
                            }

                            while (count > 0)
                            {
                                UserItem item = Envir.CreateFreshItem(info);
                                if (item == null || !item.TrySetLingFengBindingFlags(flags)) return;
                                if (item.Info.StackSize > count)
                                {
                                    item.Count = count;
                                    count = 0;
                                }
                                else
                                {
                                    count -= item.Info.StackSize;
                                    item.Count = item.Info.StackSize;
                                }
                                if (!player.CanGainItem(item)) continue;
                                player.GainItem(item);
                                int inventoryIndex = Array.FindIndex(
                                    player.Info.Inventory,
                                    candidate => ReferenceEquals(candidate, item) ||
                                                 candidate?.UniqueID == item.UniqueID);
                                if (inventoryIndex >= 0) lastGivenInventoryIndex = inventoryIndex;
                                given += item.Count;
                            }

                            if (Server.Scripting.ScriptTrace.IsEnabled(player))
                                Server.Scripting.ScriptTrace.Record(player,
                                    $"[TXT] GIVESTATEITEM {param[0]} x{requested} -> {given}, flags={(short)flags}");
                        }
                        break;

                    case ActionType.LingFengSetItemState:
                        {
                            if (!TryGetLingFengEquipmentItem(
                                    player, false, param[1], out _, out UserItem stateItem))
                                break;
                            bool enabled = param[3] == "1";
                            if (param[0] == "BIND")
                                stateItem.SoulBoundId = enabled ? player.Info.Index : -1;
                            else if (!int.TryParse(param[2], NumberStyles.None,
                                         CultureInfo.InvariantCulture, out int stateIndex) ||
                                     !stateItem.TrySetLingFengItemState(stateIndex, enabled))
                                break;
                            player.Enqueue(new S.RefreshItem { Item = stateItem });
                        }
                        break;

                    case ActionType.LingFengLinkGiveItem:
                        if (lastGivenInventoryIndex is int givenIndex &&
                            givenIndex >= 0 && givenIndex < player.Info.Inventory.Length &&
                            player.Info.Inventory[givenIndex] != null)
                        {
                            linkedInventoryIndex = givenIndex;
                            suppressOuterItemContext = false;
                        }
                        else
                            MessageQueue.Enqueue(
                                $"[TxtScripts] LINKGIVEITEM 没有可绑定的刚发放物品，页码：{Key}");
                        break;

                    case ActionType.LingFengLinkPickupItem:
                        if (Server.Scripting.LingFengTxtTriggerContext.Current?.Payload is
                                Server.Scripting.LingFengItemTriggerEvent
                                {
                                    Kind: Server.Scripting.LingFengItemTriggerKind.Pickup,
                                    Position: int pickupIndex
                                } pickupEvent &&
                            pickupIndex >= 0 && pickupIndex < player.Info.Inventory.Length &&
                            player.Info.Inventory[pickupIndex] is UserItem pickupItem &&
                            pickupItem.Info.FriendlyName.Equals(
                                pickupEvent.ItemName, StringComparison.OrdinalIgnoreCase))
                        {
                            linkedInventoryIndex = pickupIndex;
                            suppressOuterItemContext = false;
                        }
                        else
                            MessageQueue.Enqueue(
                                $"[TxtScripts] LINKPICKUPITEM 没有可绑定的精确拾取物品，页码：{Key}");
                        break;

                    case ActionType.LingFengClearLinkItem:
                        linkedInventoryIndex = null;
                        lastGivenInventoryIndex = null;
                        suppressOuterItemContext = true;
                        break;

                    case ActionType.LingFengSetSkillPower:
                        {
                            if (param.Count != 10 || !int.TryParse(param[0], out int skillId) ||
                                skillId <= 0 || param[1] is not ("=" or "+" or "-") ||
                                !int.TryParse(param[8], out int duration) ||
                                !int.TryParse(param[9], out int save) || save is < 0 or > 1)
                                return;
                            var values = new int[6];
                            for (int index = 0; index < values.Length; index++)
                            {
                                if (!int.TryParse(param[index + 2], out values[index]) ||
                                    values[index] is < short.MinValue or > short.MaxValue)
                                    return;
                            }
                            if (!player.TryChangeLingFengSkillPower(
                                    skillId, param[1], values, duration, save == 1))
                                return;
                            if (Server.Scripting.ScriptTrace.IsEnabled(player))
                                Server.Scripting.ScriptTrace.Record(player,
                                    $"[TXT] SETSKILLPOWER {skillId} {param[1]} " +
                                    $"{string.Join(' ', values)} {duration} {save}");
                        }
                        break;

                    case ActionType.LingFengCreditPoint:
                        {
                            uint previous = player.Account.Credit;
                            if (!LingFengNumericCommandExecutor.TryAdjust(
                                    previous, param[0], param[1], uint.MinValue,
                                    uint.MaxValue, true, out long adjusted,
                                    out string diagnostic))
                            {
                                MessageQueue.Enqueue(
                                    $"[TxtScripts] CREDITPOINT 失败：{diagnostic}，页码：{Key}");
                                break;
                            }

                            uint next = (uint)adjusted;
                            if (next > previous)
                                player.GainCredit(next - previous);
                            else if (next < previous)
                            {
                                uint lost = previous - next;
                                player.Account.Credit = next;
                                player.Enqueue(new S.LoseCredit { Credit = lost });
                            }
                        }
                        break;

                    case ActionType.LingFengAddSkill:
                        {
                            byte magicLevel = 0;
                            if (!TryResolveLingFengMagic(param[0], out MagicInfo magicInfo) ||
                                !byte.TryParse(param[1], NumberStyles.None,
                                    CultureInfo.InvariantCulture, out magicLevel) ||
                                magicLevel > 3)
                            {
                                MessageQueue.Enqueue(
                                    $"[TxtScripts] ADDSKILL 技能或等级无效，页码：{Key}");
                                break;
                            }

                            if (player.Info.Magics.Any(existing => existing.Spell == magicInfo.Spell))
                                break;

                            var magic = new UserMagic(magicInfo.Spell) { Level = magicLevel };
                            if (magic.Info == null)
                            {
                                MessageQueue.Enqueue(
                                    $"[TxtScripts] ADDSKILL 技能定义不存在，页码：{Key}");
                                break;
                            }
                            player.Info.Magics.Add(magic);
                            player.SendMagicInfo(magic);
                        }
                        break;

                    case ActionType.LingFengSkillLevel:
                        {
                            int operand = 0;
                            if (!TryResolveLingFengMagic(param[0], out MagicInfo magicInfo) ||
                                !int.TryParse(param[2], NumberStyles.Integer,
                                    CultureInfo.InvariantCulture, out operand) || operand < 0)
                            {
                                MessageQueue.Enqueue(
                                    $"[TxtScripts] SKILLLEVEL 技能或等级无效，页码：{Key}");
                                break;
                            }

                            UserMagic magic = player.Info.Magics.FirstOrDefault(
                                existing => existing.Spell == magicInfo.Spell);
                            if (magic == null) break;

                            if (param[3] == "1")
                            {
                                int currentEnhanced =
                                    player.Info.LingFengProgress.GetEnhancedSkillLevel(magic.Spell);
                                if (!LingFengNumericCommandExecutor.TryAdjust(
                                        currentEnhanced, param[1], param[2], 0, byte.MaxValue,
                                        true, out long enhancedLevel, out _))
                                    break;
                                player.Info.LingFengProgress.SetEnhancedSkillLevel(
                                    magic.Spell, (int)enhancedLevel);
                                break;
                            }

                            int level = param[1] switch
                            {
                                "+" => magic.Level + operand,
                                "-" => magic.Level - operand,
                                "=" => operand,
                                _ => magic.Level
                            };
                            byte updatedLevel = (byte)Math.Clamp(level, 0, 3);
                            if (updatedLevel == magic.Level) break;
                            magic.Level = updatedLevel;
                            magic.Experience = 0;
                            player.Enqueue(new S.MagicLeveled
                            {
                                ObjectID = player.ObjectID,
                                Spell = magic.Spell,
                                Level = magic.Level,
                                Experience = magic.Experience
                            });
                        }
                        break;

                    case ActionType.LingFengDeleteSkill:
                        {
                            if (!TryResolveLingFengMagic(param[0], out MagicInfo magicInfo))
                            {
                                MessageQueue.Enqueue(
                                    $"[TxtScripts] DELSKILL 技能不存在，页码：{Key}");
                                break;
                            }

                            for (int magicIndex = player.Info.Magics.Count - 1; magicIndex >= 0; magicIndex--)
                            {
                                if (player.Info.Magics[magicIndex].Spell != magicInfo.Spell) continue;
                                player.Info.Magics.RemoveAt(magicIndex);
                                player.Enqueue(new S.RemoveMagic { PlaceId = magicIndex });
                            }
                        }
                        break;

                    case ActionType.LingFengClearSkills:
                        {
                            for (int magicIndex = player.Info.Magics.Count - 1;
                                 magicIndex >= 0; magicIndex--)
                            {
                                player.Info.Magics.RemoveAt(magicIndex);
                                player.Enqueue(new S.RemoveMagic { PlaceId = magicIndex });
                            }
                        }
                        break;

                    case ActionType.LingFengSetAttackMode:
                        {
                            if (!byte.TryParse(param[0], NumberStyles.None,
                                    CultureInfo.InvariantCulture, out byte modeValue) ||
                                !int.TryParse(param[1], NumberStyles.None,
                                    CultureInfo.InvariantCulture, out int durationSeconds) ||
                                !player.TryApplyLingFengForcedAttackMode(
                                    (AttackMode)modeValue, durationSeconds, param[2]))
                                MessageQueue.Enqueue(
                                    $"[TxtScripts] SETHUMATTACKMODE 参数或地图无效，页码：{Key}");
                        }
                        break;

                    case ActionType.LingFengRenewLevel:
                        {
                            if (!int.TryParse(param[0], NumberStyles.None,
                                    CultureInfo.InvariantCulture, out int renewTimes) ||
                                !ushort.TryParse(param[1], NumberStyles.None,
                                    CultureInfo.InvariantCulture, out ushort targetLevel) ||
                                !int.TryParse(param[2], NumberStyles.None,
                                    CultureInfo.InvariantCulture, out int renewPoints) ||
                                player.Info.LingFengProgress.RenewLevel > byte.MaxValue - renewTimes ||
                                player.Info.LingFengProgress.RenewPoints > int.MaxValue - renewPoints)
                            {
                                MessageQueue.Enqueue(
                                    $"[TxtScripts] RENEWLEVEL 数值越界，页码：{Key}");
                                break;
                            }

                            player.Info.LingFengProgress.SetRenewLevel(
                                player.Info.LingFengProgress.RenewLevel + renewTimes);
                            player.Info.LingFengProgress.TryAddRenewPoints(renewPoints);
                            if (targetLevel > 0)
                            {
                                player.Level = targetLevel;
                                player.Experience = 0;
                                player.LevelUp();
                            }
                        }
                        break;

                    case ActionType.LingFengKillSlaves:
                        {
                            bool clearCorpse = param[0] == "1";
                            string expectedName = param[1];
                            MonsterObject[] pets = player.Pets
                                .Where(pet => pet != null &&
                                    (expectedName == "*" ||
                                     string.Equals(pet.Name, expectedName,
                                         StringComparison.OrdinalIgnoreCase) ||
                                     string.Equals(pet.Info?.Name, expectedName,
                                         StringComparison.OrdinalIgnoreCase)))
                                .ToArray();
                            foreach (MonsterObject pet in pets)
                            {
                                if (!pet.Dead) pet.Die();
                                if (!clearCorpse) continue;
                                pet.DeadTime = Envir.Time;
                                if (pet.CurrentMap != null && pet.Node != null)
                                    pet.Process();
                                else
                                    pet.Master = null;
                                player.Pets.Remove(pet);
                            }
                        }
                        break;

                    case ActionType.LingFengRecallSelf:
                        {
                            MonsterInfo cloneInfo = Envir.GetMonsterInfo(Settings.CloneName);
                            if (cloneInfo == null || player.CurrentMap == null ||
                                !int.TryParse(param[0], NumberStyles.None,
                                    CultureInfo.InvariantCulture, out int cloneSeconds) ||
                                !int.TryParse(param[1], NumberStyles.None,
                                    CultureInfo.InvariantCulture, out int cloneCount) ||
                                !int.TryParse(param[2], NumberStyles.None,
                                    CultureInfo.InvariantCulture, out int clonePercent) ||
                                !int.TryParse(param[3], NumberStyles.None,
                                    CultureInfo.InvariantCulture, out int bodyColor) ||
                                !int.TryParse(param[4], NumberStyles.None,
                                    CultureInfo.InvariantCulture, out int armourLook) ||
                                !int.TryParse(param[5], NumberStyles.None,
                                    CultureInfo.InvariantCulture, out int weaponLook) ||
                                !int.TryParse(param[6], NumberStyles.Integer,
                                    CultureInfo.InvariantCulture, out int cloneX) ||
                                !int.TryParse(param[7], NumberStyles.Integer,
                                    CultureInfo.InvariantCulture, out int cloneY))
                                break;
                            if (bodyColor != 0 || armourLook != 0 || weaponLook != 0)
                            {
                                MessageQueue.Enqueue(
                                    $"[TxtScripts] RECALLSELF 当前客户端不支持非零分身外观，页码：{Key}");
                                break;
                            }
                            Point location = cloneX == 0 && cloneY == 0
                                ? player.CurrentLocation
                                : new Point(cloneX, cloneY);
                            if (!player.CurrentMap.ValidPoint(location)) break;
                            long expireTime = cloneSeconds == 0
                                ? 0
                                : Envir.Time + Math.Min(
                                    (long)cloneSeconds,
                                    (long.MaxValue - Envir.Time) / Settings.Second) *
                                  Settings.Second;
                            for (int cloneIndex = 0; cloneIndex < cloneCount; cloneIndex++)
                            {
                                MonsterObject clone = MonsterObject.GetMonster(cloneInfo);
                                if (clone == null) break;
                                clone.Master = player;
                                clone.Direction = player.Direction;
                                clone.ActionTime = Envir.Time + 1000;
                                clone.ConfigureLingFengSelfClone(
                                    player.Stats, clonePercent, expireTime);
                                if (!clone.Spawn(player.CurrentMap, location))
                                {
                                    clone.Master = null;
                                    break;
                                }
                                player.Pets.Add(clone);
                            }
                        }
                        break;

                    case ActionType.LingFengSetSlaveAttackHumanPowerRate:
                        {
                            if (!int.TryParse(param[1], NumberStyles.None,
                                    CultureInfo.InvariantCulture, out int attackHumanRate))
                                break;
                            string expectedName = param[0];
                            MonsterObject[] pets = player.Pets
                                .Where(pet => pet != null && !pet.Dead &&
                                    (string.Equals(pet.Name, expectedName,
                                         StringComparison.OrdinalIgnoreCase) ||
                                     string.Equals(pet.Info?.Name, expectedName,
                                         StringComparison.OrdinalIgnoreCase) ||
                                     string.Equals(pet.Info?.GameName, expectedName,
                                         StringComparison.OrdinalIgnoreCase) ||
                                     (string.Equals(player.Name, expectedName,
                                          StringComparison.OrdinalIgnoreCase) &&
                                      pet.Info != null &&
                                      (string.Equals(pet.Info.Name, Settings.CloneName,
                                           StringComparison.OrdinalIgnoreCase) ||
                                       string.Equals(pet.Info.Name, Settings.AssassinCloneName,
                                           StringComparison.OrdinalIgnoreCase)))))
                                .ToArray();
                            foreach (MonsterObject pet in pets)
                                pet.SetLingFengAttackHumanPowerRate(attackHumanRate);
                            if (pets.Length == 0)
                                MessageQueue.Enqueue(
                                    $"[TxtScripts] SETSLAVEATTACKHUMPOWERRATE 未找到宝宝 {expectedName}，页码：{Key}");
                        }
                        break;

                    case ActionType.LingFengKillMonsterExperienceRate:
                        {
                            if (!int.TryParse(param[0], NumberStyles.None,
                                    CultureInfo.InvariantCulture, out int rate) ||
                                !int.TryParse(param[1], NumberStyles.None,
                                    CultureInfo.InvariantCulture, out int durationSeconds) ||
                                !player.TrySetLingFengExperienceRate(
                                    $"{SourceKey}|{(ReferenceEquals(acts, ActList) ? "ACT" : "ELSEACT")}|{i}",
                                    rate, durationSeconds, param[2] == "1"))
                            {
                                MessageQueue.Enqueue(
                                    $"[TxtScripts] KILLMONEXPRATE 参数或持久状态无效，页码：{Key}");
                                break;
                            }
                            if (param[3] == "0")
                                player.ReceiveChat(
                                    $"杀怪经验倍率已设为 {rate / 100M:0.##} 倍，" +
                                    (durationSeconds == 0 ? "永久有效。" : $"有效 {durationSeconds} 秒。"),
                                    ChatType.System);
                        }
                        break;

                    case ActionType.LingFengPowerRate:
                        {
                            if (!int.TryParse(param[0], NumberStyles.None,
                                    CultureInfo.InvariantCulture, out int rate) ||
                                !int.TryParse(param[1], NumberStyles.None,
                                    CultureInfo.InvariantCulture, out int durationSeconds) ||
                                !int.TryParse(param[4], NumberStyles.None,
                                    CultureInfo.InvariantCulture, out int targetType) ||
                                !player.TrySetLingFengPowerRate(
                                    $"{SourceKey}|{(ReferenceEquals(acts, ActList) ? "ACT" : "ELSEACT")}|{i}",
                                    rate, durationSeconds, param[2] == "1", targetType))
                            {
                                MessageQueue.Enqueue(
                                    $"[TxtScripts] POWERRATE 参数或持久状态无效，页码：{Key}");
                                break;
                            }
                            if (param[3] == "0")
                                player.ReceiveChat(
                                    $"攻击倍率已设为 {rate / 100M:0.##} 倍，" +
                                    (durationSeconds == 0 ? "永久有效。" : $"有效 {durationSeconds} 秒。"),
                                    ChatType.System);
                        }
                        break;

                    case ActionType.LingFengBlastHitRate:
                        if (!int.TryParse(param[0], NumberStyles.None,
                                CultureInfo.InvariantCulture, out int blastRate) ||
                            !int.TryParse(param[1], NumberStyles.None,
                                CultureInfo.InvariantCulture, out int blastSeconds) ||
                            !player.TrySetLingFengBlastHitRate(
                                $"{SourceKey}|{(ReferenceEquals(acts, ActList) ? "ACT" : "ELSEACT")}|{i}",
                                blastRate, blastSeconds))
                            MessageQueue.Enqueue(
                                $"[TxtScripts] SETBLASTHITRATE 参数无效，页码：{Key}");
                        break;

                    case ActionType.LingFengKillMonsterDropRate:
                        {
                            if (!int.TryParse(param[0], NumberStyles.None,
                                    CultureInfo.InvariantCulture, out int rate) ||
                                !int.TryParse(param[1], NumberStyles.None,
                                    CultureInfo.InvariantCulture, out int durationSeconds) ||
                                !player.TrySetLingFengDropRate(
                                    $"{SourceKey}|{(ReferenceEquals(acts, ActList) ? "ACT" : "ELSEACT")}|{i}",
                                    rate, durationSeconds, param[2] == "1"))
                            {
                                MessageQueue.Enqueue(
                                    $"[TxtScripts] KILLMONBURSTRATE 参数或持久状态无效，页码：{Key}");
                                break;
                            }
                            if (param[3] == "0")
                                player.ReceiveChat(
                                    $"杀怪爆率已设为 {rate / 100M:0.##} 倍，" +
                                    (durationSeconds == 0 ? "永久有效。" : $"有效 {durationSeconds} 秒。"),
                                    ChatType.System);
                        }
                        break;

                    case ActionType.LingFengSetNpcReborn:
                        if (!int.TryParse(param[0], NumberStyles.None,
                                CultureInfo.InvariantCulture, out int rebornCount) ||
                            !int.TryParse(param[1], NumberStyles.None,
                                CultureInfo.InvariantCulture, out int rebornSeconds) ||
                            !player.TrySetLingFengNpcReborn(rebornCount, rebornSeconds))
                        {
                            MessageQueue.Enqueue(
                                $"[TxtScripts] SETREBORN 参数无效，页码：{Key}");
                            break;
                        }
                        break;

                    case ActionType.LingFengClearSkillCooldown:
                        {
                            if (!TryResolveLingFengMagic(param[0], out MagicInfo magicInfo))
                            {
                                MessageQueue.Enqueue(
                                    $"[TxtScripts] CLEARSKILLCD 技能不存在，页码：{Key}");
                                break;
                            }

                            UserMagic magic = player.Info.Magics.FirstOrDefault(
                                existing => existing.Spell == magicInfo.Spell);
                            if (magic == null) break;
                            magic.CastTime = Envir.Time - magic.GetDelay();
                            player.Enqueue(new S.MagicCooldownCleared
                            {
                                ObjectID = player.ObjectID,
                                Spell = magic.Spell
                            });
                        }
                        break;

                    case ActionType.LingFengKillCalledMonster:
                        {
                            if (!int.TryParse(param[1], NumberStyles.None,
                                    CultureInfo.InvariantCulture, out int remaining) || remaining <= 0)
                                break;
                            foreach (MonsterObject pet in player.Pets.ToArray())
                            {
                                if (remaining == 0) break;
                                if (pet == null || pet.Dead ||
                                    !string.Equals(pet.Info?.Name, param[0],
                                        StringComparison.OrdinalIgnoreCase))
                                    continue;
                                pet.Die();
                                remaining--;
                            }
                        }
                        break;

                    case ActionType.LingFengRecallMob:
                        {
                            MonsterInfo monsterInfo = Envir.GetMonsterInfo(param[0]);
                            if (monsterInfo == null || player.CurrentMap == null ||
                                !byte.TryParse(param[1], NumberStyles.None,
                                    CultureInfo.InvariantCulture, out byte petLevel) || petLevel > 7 ||
                                !long.TryParse(param[2], NumberStyles.None,
                                    CultureInfo.InvariantCulture, out long tameMinutes) || tameMinutes < 0 ||
                                !int.TryParse(param[3], NumberStyles.None,
                                    CultureInfo.InvariantCulture, out int colorType) || colorType is < 0 or > 1 ||
                                !int.TryParse(param[4], NumberStyles.None,
                                    CultureInfo.InvariantCulture, out int colorIndex) || colorIndex is < 0 or > 255 ||
                                !int.TryParse(param[5], NumberStyles.None,
                                    CultureInfo.InvariantCulture, out int affiliatedSkill) || affiliatedSkill is < 0 or > 5 ||
                                !int.TryParse(param[6], NumberStyles.None,
                                    CultureInfo.InvariantCulture, out int summonCount) || summonCount is < 1 or > 100)
                                break;

                            Color fixedColor = Color.Empty;
                            if (colorType == 0 &&
                                !LingFengLegacyPalette.TryGetColor(colorIndex, out fixedColor))
                                break;

                            long tameTime = 0;
                            if (tameMinutes > 0)
                            {
                                long maximumMinutes = (long.MaxValue - Envir.Time) / Settings.Minute;
                                tameTime = Envir.Time + Math.Min(tameMinutes, maximumMinutes) * Settings.Minute;
                            }

                            for (int summonIndex = 0; summonIndex < summonCount; summonIndex++)
                            {
                                MonsterObject monster = MonsterObject.GetMonster(monsterInfo);
                                if (monster == null) break;
                                monster.PetLevel = petLevel;
                                monster.Master = player;
                                monster.MaxPetLevel = 7;
                                monster.Direction = player.Direction;
                                monster.ActionTime = Envir.Time + 1000;
                                monster.TameTime = tameTime;
                                if (colorType == 0) monster.NameColour = fixedColor;
                                if (!monster.Spawn(player.CurrentMap, player.CurrentLocation)) break;
                                player.Pets.Add(monster);
                                if (colorType == 1) monster.RefreshNameColour();
                            }
                        }
                        break;

                    case ActionType.LingFengSetCustomItemAbility:
                        {
                            if (!TryGetLingFengEquipmentItem(player, param[0] == "1", param[1],
                                    out HumanObject itemOwner, out UserItem customItem) ||
                                !int.TryParse(param[2], NumberStyles.None, CultureInfo.InvariantCulture,
                                    out int attributeIndex) ||
                                !int.TryParse(param[3], NumberStyles.None, CultureInfo.InvariantCulture,
                                    out int attributeField) ||
                                !int.TryParse(param[4], NumberStyles.Integer, CultureInfo.InvariantCulture,
                                    out int attributeValue) ||
                                !customItem.TrySetLingFengCustomAbility(
                                    attributeIndex, attributeField, attributeValue))
                                break;
                            itemOwner.RefreshStats();
                        }
                        break;

                    case ActionType.LingFengGetCustomItemAbility:
                        {
                            if (!TryGetLingFengEquipmentItem(player, param[0] == "1", param[1],
                                    out _, out UserItem customItem) ||
                                !int.TryParse(param[2], NumberStyles.None, CultureInfo.InvariantCulture,
                                    out int attributeIndex) ||
                                attributeIndex < 0 ||
                                attributeIndex >= UserItem.LingFengCustomAttributeLimit ||
                                !int.TryParse(param[3], NumberStyles.None, CultureInfo.InvariantCulture,
                                    out int attributeField) ||
                                attributeField is < 0 or > 4)
                                break;
                            LingFengCustomItemAttribute attribute =
                                customItem.GetLingFengCustomAttribute(attributeIndex);
                            int value = attributeField switch
                            {
                                0 => attribute.Colour,
                                1 => attribute.Binding,
                                2 => attribute.DisplayOrder,
                                3 => attribute.Mode,
                                4 => attribute.Module,
                                _ => 0
                            };
                            TryStoreScriptValue(player, param[4], value);
                        }
                        break;

                    case ActionType.LingFengSetCustomItemValue:
                        {
                            if (!TryGetLingFengEquipmentItem(player, param[0] == "1", param[1],
                                    out HumanObject itemOwner, out UserItem customItem) ||
                                !int.TryParse(param[2], NumberStyles.None, CultureInfo.InvariantCulture,
                                    out int attributeIndex) ||
                                !int.TryParse(param[4], NumberStyles.Integer, CultureInfo.InvariantCulture,
                                    out int attributeValue1) ||
                                !int.TryParse(param[5], NumberStyles.Integer, CultureInfo.InvariantCulture,
                                    out int attributeValue2) ||
                                !int.TryParse(param[6], NumberStyles.Integer, CultureInfo.InvariantCulture,
                                    out int attributeValue3) ||
                                !customItem.TryChangeLingFengCustomValues(attributeIndex, param[3],
                                    attributeValue1, attributeValue2, attributeValue3))
                                break;
                            itemOwner.RefreshStats();
                        }
                        break;

                    case ActionType.LingFengGetCustomItemValue:
                        {
                            if (!TryGetLingFengEquipmentItem(player, param[0] == "1", param[1],
                                    out _, out UserItem customItem) ||
                                !int.TryParse(param[2], NumberStyles.None, CultureInfo.InvariantCulture,
                                    out int attributeIndex) ||
                                attributeIndex < 0 ||
                                attributeIndex >= UserItem.LingFengCustomAttributeLimit)
                                break;
                            LingFengCustomItemAttribute attribute =
                                customItem.GetLingFengCustomAttribute(attributeIndex);
                            if (param[3] == "EX")
                            {
                                string[] destinations = param[4].Split('\u001F');
                                int[] values = destinations.Length == 4
                                    ? new[] { (int)attribute.Mode, attribute.Value1, attribute.Value2, attribute.Value3 }
                                    : new[] { (int)attribute.Mode, (int)attribute.DisplayOrder,
                                        attribute.Value1, attribute.Value2, attribute.Value3 };
                                for (int index = 0; index < destinations.Length && index < values.Length; index++)
                                    TryStoreScriptValue(player, destinations[index], values[index]);
                                break;
                            }
                            if (!int.TryParse(param[5], NumberStyles.None, CultureInfo.InvariantCulture,
                                    out int valueIndex))
                                break;
                            TryStoreScriptValue(player, param[3], GetLingFengCustomValue(attribute, valueIndex));
                            if (!string.IsNullOrEmpty(param[4]))
                                TryStoreScriptValue(player, param[4], attribute.Mode);
                        }
                        break;

                    case ActionType.LingFengGetAllCustomItemValue:
                        {
                            HumanObject itemOwner = param[0] == "1" ? player.Hero : player;
                            if (itemOwner?.Info?.Equipment == null ||
                                !int.TryParse(param[1], NumberStyles.None, CultureInfo.InvariantCulture,
                                    out int binding) ||
                                binding is < 1 or > 60 ||
                                !int.TryParse(param[4], NumberStyles.None, CultureInfo.InvariantCulture,
                                    out int valueIndex))
                                break;
                            long direct = 0;
                            long singlePercentage = 0;
                            long wholeBodyPercentage = 0;
                            foreach (UserItem equippedItem in itemOwner.Info.Equipment)
                            {
                                if (equippedItem == null) continue;
                                for (int attributeIndex = 0;
                                     attributeIndex < UserItem.LingFengCustomAttributeLimit;
                                     attributeIndex++)
                                {
                                    LingFengCustomItemAttribute attribute =
                                        equippedItem.GetLingFengCustomAttribute(attributeIndex);
                                    if (attribute.Binding != binding) continue;
                                    int value = GetLingFengCustomValue(attribute, valueIndex);
                                    switch (attribute.Mode)
                                    {
                                        case 0: direct += value; break;
                                        case 1: singlePercentage += value; break;
                                        case 2: wholeBodyPercentage += value; break;
                                    }
                                }
                            }
                            TryStoreScriptValue(player, param[2], Math.Clamp(direct, int.MinValue, int.MaxValue));
                            TryStoreScriptValue(player, param[3],
                                Math.Clamp(singlePercentage, int.MinValue, int.MaxValue));
                            if (!string.IsNullOrEmpty(param[5]))
                                TryStoreScriptValue(player, param[5],
                                    Math.Clamp(wholeBodyPercentage, int.MinValue, int.MaxValue));
                        }
                        break;

                    case ActionType.LingFengItemMark:
                        {
                            if (!TryGetLingFengEquipmentItem(player, false, param[2], out HumanObject itemOwner,
                                    out UserItem markedItem) ||
                                !int.TryParse(param[3], NumberStyles.None, CultureInfo.InvariantCulture,
                                    out int markIndex))
                                break;
                            if (param[0] == "SET")
                            {
                                bool changed = param[1] switch
                                {
                                    "BYTE" when int.TryParse(param[4], NumberStyles.Integer,
                                        CultureInfo.InvariantCulture, out int byteValue) =>
                                        markedItem.TrySetLingFengByteMark(markIndex, byteValue),
                                    "INT" when int.TryParse(param[4], NumberStyles.Integer,
                                        CultureInfo.InvariantCulture, out int intValue) =>
                                        markedItem.TrySetLingFengIntMark(markIndex, intValue),
                                    "TEXT" => markedItem.TrySetLingFengTextMark(markIndex, param[4]),
                                    _ => false
                                };
                                if (changed) itemOwner.RefreshStats();
                                break;
                            }
                            switch (param[1])
                            {
                                case "BYTE" when markedItem.TryGetLingFengByteMark(markIndex, out byte byteValue):
                                    TryStoreScriptValue(player, param[4], byteValue);
                                    break;
                                case "INT" when markedItem.TryGetLingFengIntMark(markIndex, out int intValue):
                                    TryStoreScriptValue(player, param[4], intValue);
                                    break;
                                case "TEXT" when markedItem.TryGetLingFengTextMark(markIndex, out string textValue):
                                    TryStoreScriptTextValue(player, param[4], textValue);
                                    break;
                            }
                        }
                        break;

                    case ActionType.LingFengChangeItemAddedValue:
                        {
                            if (!TryGetLingFengEquipmentItem(player, param[0] == "1", param[1],
                                    out HumanObject itemOwner, out UserItem addedItem) ||
                                !int.TryParse(param[1], NumberStyles.Integer, CultureInfo.InvariantCulture,
                                    out int addedPosition) ||
                                !int.TryParse(param[2], NumberStyles.None, CultureInfo.InvariantCulture,
                                    out int addedAttribute) ||
                                !int.TryParse(param[4], NumberStyles.Integer, CultureInfo.InvariantCulture,
                                    out int operand) ||
                                !TryChangeLingFengItemAddedValue(
                                    addedItem, addedPosition, addedAttribute, param[3], operand))
                                break;
                            itemOwner.RefreshStats();
                        }
                        break;

                    case ActionType.LingFengSetCustomItemProgressBar:
                        {
                            if (!TryGetLingFengEquipmentItem(player, param[0] == "1", param[1],
                                    out HumanObject itemOwner, out UserItem progressItem) ||
                                !int.TryParse(param[2], NumberStyles.None, CultureInfo.InvariantCulture,
                                    out int progressIndex) ||
                                !int.TryParse(param[3], NumberStyles.None, CultureInfo.InvariantCulture,
                                    out int progressField) ||
                                !progressItem.TrySetLingFengCustomProgressBar(
                                    progressIndex, progressField, param[4]))
                                break;
                            itemOwner.RefreshStats();
                        }
                        break;

                    case ActionType.LingFengChangeCustomItemProgressBarValue:
                        {
                            if (!TryGetLingFengEquipmentItem(player, param[0] == "1", param[1],
                                    out HumanObject itemOwner, out UserItem progressItem) ||
                                !int.TryParse(param[2], NumberStyles.None, CultureInfo.InvariantCulture,
                                    out int progressIndex) ||
                                !int.TryParse(param[3], NumberStyles.None, CultureInfo.InvariantCulture,
                                    out int progressValueKind) ||
                                !int.TryParse(param[5], NumberStyles.Integer, CultureInfo.InvariantCulture,
                                    out int progressOperand) ||
                                !progressItem.TryChangeLingFengCustomProgressBarValue(
                                    progressIndex, progressValueKind, param[4], progressOperand))
                                break;
                            itemOwner.RefreshStats();
                        }
                        break;

                    case ActionType.LingFengGetCustomItemProgressBarValue:
                        {
                            if (!TryGetLingFengEquipmentItem(player, param[0] == "1", param[1],
                                    out _, out UserItem progressItem) ||
                                !int.TryParse(param[2], NumberStyles.None, CultureInfo.InvariantCulture,
                                    out int progressIndex) ||
                                !int.TryParse(param[3], NumberStyles.None, CultureInfo.InvariantCulture,
                                    out int progressValueKind) ||
                                !progressItem.TryGetLingFengCustomProgressBarValue(
                                    progressIndex, progressValueKind, out int progressValue))
                                break;
                            TryStoreScriptValue(player, param[4], progressValue);
                        }
                        break;

                    case ActionType.LingFengSetCustomItemText:
                        {
                            if (!TryGetLingFengEquipmentItem(player, param[0] == "1", param[1],
                                    out HumanObject itemOwner, out UserItem textItem))
                                break;
                            bool changed = param[2] == "TEXT"
                                ? textItem.TrySetLingFengCustomText(param[3])
                                : int.TryParse(param[3], NumberStyles.None,
                                      CultureInfo.InvariantCulture, out int textColour) &&
                                  textItem.TrySetLingFengCustomTextColour(textColour);
                            if (changed) itemOwner.RefreshStats();
                        }
                        break;

                    case ActionType.LingFengSetItemEffect:
                        {
                            if (!TryGetLingFengEquipmentItem(player, param[0] == "1", param[1],
                                    out HumanObject itemOwner, out UserItem effectItem) ||
                                !int.TryParse(param[2], NumberStyles.None, CultureInfo.InvariantCulture,
                                    out int lingFengItemEffect) ||
                                !int.TryParse(param[3], NumberStyles.None, CultureInfo.InvariantCulture,
                                    out int effectPosition) ||
                                !effectItem.TrySetLingFengItemEffect(
                                    effectPosition, lingFengItemEffect))
                                break;
                            itemOwner.RefreshStats();
                            player.Enqueue(new S.RefreshItem { Item = effectItem });
                        }
                        break;

                    case ActionType.LingFengChangeItemNameColour:
                        {
                            if (!TryGetLingFengEquipmentItem(player, param[0] == "1", param[1],
                                    out _, out UserItem colourItem) ||
                                !int.TryParse(param[2], NumberStyles.None,
                                    CultureInfo.InvariantCulture, out int nameColour) ||
                                !colourItem.TrySetLingFengNameColour(nameColour))
                                break;
                            player.Enqueue(new S.RefreshItem { Item = colourItem });
                        }
                        break;

                    case ActionType.LingFengChangeItemUpgradeCount:
                        {
                            if (!TryGetLingFengEquipmentItem(
                                    player, param[0] == "1", param[1],
                                    out _, out UserItem upgradeItem) ||
                                !int.TryParse(param[3], NumberStyles.None,
                                    CultureInfo.InvariantCulture, out int upgradeValue) ||
                                !upgradeItem.TryChangeLingFengUpgradeCount(
                                    param[2], upgradeValue))
                            {
                                MessageQueue.Enqueue(
                                    $"[TxtScripts] CHANGEITEMUPGRADECOUNT 调整失败，页码：{Key}");
                                break;
                            }
                            player.Enqueue(new S.RefreshItem { Item = upgradeItem });
                        }
                        break;

                    case ActionType.LingFengChangeItemVisual:
                        {
                            if (!TryGetLingFengVisualItem(
                                    player, param[0] == "1", param[2],
                                    out HumanObject visualOwner, out UserItem visualItem) ||
                                !int.TryParse(param[4], NumberStyles.Integer,
                                    CultureInfo.InvariantCulture, out int visualValue))
                                break;

                            bool changed = param[1] == "LOOKS"
                                ? visualItem.TryChangeLingFengLooks(param[3], visualValue)
                                : (visualItem.Info.Type is ItemType.武器 or ItemType.盔甲) &&
                                  visualItem.TryChangeLingFengShape(param[3], visualValue);
                            if (!changed) break;
                            visualOwner.RefreshStats();
                            player.Enqueue(new S.RefreshItem { Item = visualItem });
                        }
                        break;

                    case ActionType.LingFengSetNewItemValue:
                        {
                            if (!TryGetLingFengEquipmentItem(player, param[0] == "1", param[1],
                                    out HumanObject itemOwner, out UserItem valueItem) ||
                                !int.TryParse(param[2], NumberStyles.None, CultureInfo.InvariantCulture,
                                    out int valueType) ||
                                !int.TryParse(param[4], NumberStyles.None, CultureInfo.InvariantCulture,
                                    out int newItemValue) ||
                                !valueItem.TryChangeLingFengNewItemValue(
                                    valueType, param[3], newItemValue))
                                break;
                            itemOwner.RefreshStats();
                        }
                        break;

                    case ActionType.LingFengSetTemporaryNewItemValue:
                        {
                            if (!TryGetLingFengEquipmentItem(player, false, param[0],
                                    out _, out _) ||
                                !int.TryParse(param[1], NumberStyles.None,
                                    CultureInfo.InvariantCulture, out int temporaryAttribute) ||
                                !int.TryParse(param[3], NumberStyles.None,
                                    CultureInfo.InvariantCulture, out int temporaryValue) ||
                                !int.TryParse(param[4], NumberStyles.None,
                                    CultureInfo.InvariantCulture, out int temporarySeconds) ||
                                !player.TryChangeLingFengNewValue(
                                    $"{SourceKey}|TEMPITEMVALUE|" +
                                    $"{(ReferenceEquals(acts, ActList) ? "ACT" : "ELSEACT")}|{i}",
                                    temporaryAttribute, param[2], temporaryValue,
                                    temporarySeconds))
                            {
                                MessageQueue.Enqueue(
                                    $"[TxtScripts] SETNEWITEMVALUEEX 参数或装备无效，页码：{Key}");
                            }
                        }
                        break;

                    case ActionType.LingFengUpdateItem:
                        {
                            if (!TryGetLingFengEquipmentItem(player, param[0] == "1", param[1],
                                    out _, out UserItem customItem))
                                break;
                            player.Enqueue(new S.RefreshItem { Item = customItem });
                        }
                        break;

                    case ActionType.LingFengMapMove:
                        {
                            Map map = Envir.GetMapByNameAndInstance(param[0]);
                            if (map == null) return;

                            if (param.Count == 1)
                            {
                                player.TeleportRandom(200, 0, map);
                                break;
                            }

                            if (!int.TryParse(param[1], out int centerX) ||
                                !int.TryParse(param[2], out int centerY) ||
                                centerX < 0 || centerY < 0)
                                return;

                            int radius = 0;
                            if (param.Count == 4 &&
                                (!int.TryParse(param[3], out radius) || radius < 0))
                                return;

                            var center = new Point(centerX, centerY);
                            if (radius == 0)
                            {
                                player.Teleport(map, center);
                                break;
                            }

                            Point destination = Point.Empty;
                            bool found = false;
                            for (int attempt = 0; attempt < 200; attempt++)
                            {
                                var candidate = new Point(
                                    centerX + Envir.Random.Next(-radius, radius + 1),
                                    centerY + Envir.Random.Next(-radius, radius + 1));
                                if (!map.ValidPoint(candidate)) continue;

                                destination = candidate;
                                found = true;
                                break;
                            }

                            if (!found && map.ValidPoint(center))
                            {
                                destination = center;
                                found = true;
                            }

                            if (found) player.Teleport(map, destination);
                        }
                        break;

                    case ActionType.LingFengAddMirrorMap:
                        {
                            ushort miniMap = 0;
                            Point returnLocation = Point.Empty;
                            bool valid = int.TryParse(param[3], NumberStyles.None,
                                             CultureInfo.InvariantCulture, out int durationSeconds) &&
                                         durationSeconds > 0 &&
                                         ushort.TryParse(param[5], NumberStyles.None,
                                             CultureInfo.InvariantCulture, out miniMap) &&
                                         param[7] is "0" or "1" &&
                                         TryParseLingFengCommaPoint(param[8], out returnLocation);
                            bool created = valid && Envir.TryCreateLingFengMirrorMap(
                                param[0], param[1], param[2], durationSeconds,
                                param[4], miniMap, returnLocation, out _);
                            if (TryStoreScriptValue(player, param[6], created ? 1 : 0))
                            {
                                if (!created) return;
                                break;
                            }

                            if (created) Envir.TryDeleteLingFengMirrorMap(param[1]);
                            return;
                        }

                    case ActionType.LingFengDeleteMirrorMap:
                        if (!Envir.TryDeleteLingFengMirrorMap(param[0])) return;
                        break;

                    case ActionType.LingFengSetMirrorMapTime:
                        if (!int.TryParse(param[1], NumberStyles.None,
                                CultureInfo.InvariantCulture, out int mirrorDuration) ||
                            !Envir.TrySetLingFengMirrorMapTime(
                                param[0], mirrorDuration, param[2] == "1"))
                            return;
                        break;

                    case ActionType.LingFengGetMirrorMapTime:
                        if (!Envir.TryGetLingFengMirrorMapStatus(
                                param[0], out LingFengMirrorMapStatus mirrorStatus) ||
                            !TryStoreScriptValue(player, param[1], mirrorStatus.TotalSeconds) ||
                            param[2].Length > 0 && !TryStoreScriptValue(
                                player, param[2], mirrorStatus.RemainingSeconds))
                            return;
                        break;

                    case ActionType.LingFengCreateEctype:
                        {
                            LingFengEctypeCreateResult result =
                                int.TryParse(param[1], NumberStyles.None,
                                    CultureInfo.InvariantCulture, out int durationMinutes)
                                    ? Envir.TryCreateLingFengEctype(
                                        player, param[0], durationMinutes)
                                    : LingFengEctypeCreateResult.InvalidDuration;
                            ScheduleLingFengEctypeCallback(player, result switch
                            {
                                LingFengEctypeCreateResult.Created => "@CreateEctype_OK",
                                LingFengEctypeCreateResult.DefinitionMissing => "@CreateEctype_NoExists",
                                LingFengEctypeCreateResult.GroupLeaderRequired => "@CreateEctype_Fail_GroupMaster",
                                LingFengEctypeCreateResult.GuildLeaderRequired => "@CreateEctype_Fail_GuildMaster",
                                LingFengEctypeCreateResult.ExistingForMember => "@CreateEctype_IN",
                                LingFengEctypeCreateResult.ExistingForOwner => "@CreateEctype_IN_Time",
                                _ => "@CreateEctype_Fail"
                            });
                        }
                        break;

                    case ActionType.LingFengMoveEctype:
                        {
                            LingFengEctypeMoveResult result =
                                int.TryParse(param[1], NumberStyles.None,
                                    CultureInfo.InvariantCulture, out int ectypeX) &&
                                int.TryParse(param[2], NumberStyles.None,
                                    CultureInfo.InvariantCulture, out int ectypeY)
                                    ? Envir.TryMoveLingFengEctype(
                                        player, param[0], new Point(ectypeX, ectypeY))
                                    : LingFengEctypeMoveResult.InvalidLocation;
                            ScheduleLingFengEctypeCallback(player, result switch
                            {
                                LingFengEctypeMoveResult.Success => "@MoveEctype_OK",
                                LingFengEctypeMoveResult.EntryWindowExpired => "@MoveEctype_Fail_Time",
                                _ => "@MoveEctype_Fail"
                            });
                        }
                        break;

                    case ActionType.LingFengSpawnEctypeMonster:
                        {
                            Color? ectypeNameColour = null;
                            int ectypeMonsterX = 0;
                            int ectypeMonsterY = 0;
                            int ectypeMonsterCount = 0;
                            int ectypeMonsterRange = 0;
                            bool valid = int.TryParse(param[1], NumberStyles.None,
                                             CultureInfo.InvariantCulture, out ectypeMonsterX) &&
                                         int.TryParse(param[2], NumberStyles.None,
                                             CultureInfo.InvariantCulture, out ectypeMonsterY) &&
                                         int.TryParse(param[4], NumberStyles.None,
                                             CultureInfo.InvariantCulture, out ectypeMonsterCount) &&
                                         int.TryParse(param[5], NumberStyles.None,
                                             CultureInfo.InvariantCulture, out ectypeMonsterRange);
                            if (valid && param.Count == 7)
                            {
                                Color colour = default;
                                valid = int.TryParse(param[6], NumberStyles.None,
                                            CultureInfo.InvariantCulture, out int ectypeColourIndex) &&
                                        LingFengLegacyPalette.TryGetColor(
                                            ectypeColourIndex, out colour);
                                if (valid) ectypeNameColour = colour;
                            }
                            if (!valid || !Envir.TrySpawnLingFengEctypeMonsters(
                                    player, param[0],
                                    new Point(ectypeMonsterX, ectypeMonsterY),
                                    param[3], ectypeMonsterCount, ectypeMonsterRange,
                                    ectypeNameColour))
                                MessageQueue.Enqueue(
                                    $"[TxtScripts] MOBECTYPEMON 副本、坐标、怪物或范围无效，页码：{Key}");
                        }
                        break;

                    case ActionType.LingFengAddTextList:
                        if (param[2] == "1" ||
                            !Envir.TryAddLingFengRuntimeTextListValue(param[0], param[1]))
                            return;
                        break;

                    case ActionType.LingFengSetTextListLine:
                        if (!int.TryParse(param[2], NumberStyles.None, CultureInfo.InvariantCulture,
                                out int textListLineNumber) ||
                            !Envir.TrySetLingFengRuntimeTextListLine(
                                param[0], param[1], textListLineNumber)) return;
                        break;

                    case ActionType.LingFengMongenEx:
                        {
                            Map map = Envir.GetMapByNameAndInstance(param[0]);
                            MonsterInfo monsterInfo = Envir.GetMonsterInfo(param[3]);
                            if (map == null || monsterInfo == null ||
                                !int.TryParse(param[1], NumberStyles.Integer,
                                    CultureInfo.InvariantCulture, out int centerX) ||
                                !int.TryParse(param[2], NumberStyles.Integer,
                                    CultureInfo.InvariantCulture, out int centerY) ||
                                !int.TryParse(param[4], NumberStyles.None,
                                    CultureInfo.InvariantCulture, out int range) ||
                                !int.TryParse(param[5], NumberStyles.None,
                                    CultureInfo.InvariantCulture, out int monsterCount) ||
                                centerX <= 0 || centerY <= 0 || range <= 0 ||
                                monsterCount <= 0 || monsterCount > byte.MaxValue)
                                return;

                            Color? nameColor = null;
                            if (param.Count == 7)
                            {
                                if (!int.TryParse(param[6], NumberStyles.None,
                                        CultureInfo.InvariantCulture, out int colorIndex) ||
                                    !LingFengLegacyPalette.TryGetColor(colorIndex, out Color color))
                                    return;
                                nameColor = color;
                            }

                            for (int spawnIndex = 0; spawnIndex < monsterCount; spawnIndex++)
                            {
                                Point location = Point.Empty;
                                bool found = false;
                                for (int attempt = 0; attempt < 32; attempt++)
                                {
                                    var candidate = new Point(
                                        Envir.Random.Next(centerX - range, centerX + range + 1),
                                        Envir.Random.Next(centerY - range, centerY + range + 1));
                                    if (!map.ValidPoint(candidate)) continue;
                                    location = candidate;
                                    found = true;
                                    break;
                                }

                                if (!found) return;
                                MonsterObject monster = MonsterObject.GetMonster(monsterInfo);
                                if (monster == null) return;
                                if (nameColor.HasValue) monster.NameColour = nameColor.Value;
                                monster.Direction = 0;
                                monster.ActionTime = Envir.Time + 1000;
                                if (!monster.Spawn(map, location)) return;
                            }
                        }
                        break;

                    case ActionType.LingFengChangePkPointTarget:
                        {
                            string targetPkDiagnostic = "缺少当前击杀者上下文";
                            if (!TryGetLingFengLastActorPlayer(out PlayerObject pkTarget) ||
                                !LingFengNumericCommandExecutor.TryAdjust(
                                    pkTarget.PKPoints, param[0], param[1], 0, int.MaxValue,
                                    true, out long adjustedPk, out targetPkDiagnostic))
                            {
                                MessageQueue.Enqueue(
                                    $"[TxtScripts] <$KILLER>.CHANGEPKPOINT 失败：{targetPkDiagnostic}，页码：{Key}");
                                break;
                            }
                            pkTarget.PKPoints = (int)adjustedPk;
                        }
                        break;

                    case ActionType.LingFengKillerCurrencyAdjust:
                        {
                            if (!TryGetLingFengLastActorPlayer(out PlayerObject currencyTarget))
                            {
                                MessageQueue.Enqueue(
                                    $"[TxtScripts] <$KILLER>.{param[0]} 缺少当前击杀者上下文，页码：{Key}");
                                break;
                            }
                            int current = param[0] == "GAMEGOLD"
                                ? currencyTarget.Info.PearlCount
                                : currencyTarget.Info.LingFengProgress.GameGird;
                            if (!LingFengNumericCommandExecutor.TryAdjust(
                                    current, param[1], param[2], 0, int.MaxValue,
                                    false, out long adjustedCurrency, out string currencyDiagnostic))
                            {
                                MessageQueue.Enqueue(
                                    $"[TxtScripts] <$KILLER>.{param[0]} 失败：{currencyDiagnostic}，页码：{Key}");
                                break;
                            }
                            if (param[0] == "GAMEGOLD")
                            {
                                int delta = (int)adjustedCurrency - currencyTarget.Info.PearlCount;
                                if (delta >= 0) currencyTarget.IntelligentCreatureGainPearls(delta);
                                else currencyTarget.IntelligentCreatureLosePearls(-delta);
                            }
                            else
                            {
                                currencyTarget.Info.LingFengProgress.SetGameGird(
                                    (int)adjustedCurrency);
                            }
                        }
                        break;

                    case ActionType.LingFengCalcPercent:
                        {
                            if (!LingFengNumericCommandExecutor.TryCalculatePercent(
                                    param[0], param[1], out long result, out string diagnostic))
                            {
                                MessageQueue.Enqueue($"[TxtScripts] CALCPERCENT 失败：{diagnostic}，页码：{Key}");
                                break;
                            }

                            string destination = param[2];
                            bool legacyLocal = Regex.IsMatch(
                                destination, @"^[A-Za-z][0-9]+$", RegexOptions.CultureInvariant);
                            if (!legacyLocal && TryParseRuntimeVariableReference(destination, out _))
                            {
                                if (player.NPCObjectID == 0)
                                {
                                    MessageQueue.Enqueue($"[Variables][TXT] CALCPERCENT 缺少 NPC 作用域，页码：{Key}");
                                    break;
                                }
                                var context = ScriptVariableContext.ForConversation(
                                    player, player.NPCObjectID, player.CurrentMap);
                                ScriptVariableMutationResult mutation = Envir.CSharpScripts.VariableCommands.Mutate(
                                    context, destination, "MOV", result.ToString(CultureInfo.InvariantCulture));
                                if (!mutation.Success)
                                    MessageQueue.Enqueue($"[Variables][TXT] CALCPERCENT 写入失败：{mutation.ErrorCode} {mutation.Diagnostic}，页码：{Key}");
                            }
                            else
                            {
                                AddVariable(player, destination, result.ToString(CultureInfo.InvariantCulture));
                            }
                        }
                        break;

                    case ActionType.LingFengSendMessage:
                        {
                            if (!byte.TryParse(param[0], NumberStyles.None, CultureInfo.InvariantCulture,
                                    out byte mode))
                                break;
                            ChatType type = mode switch
                            {
                                0 => ChatType.Normal,
                                1 => ChatType.Shout,
                                2 => ChatType.System,
                                3 => ChatType.Hint,
                                5 => ChatType.System,
                                6 => ChatType.Shout2,
                                7 => ChatType.LineMessage,
                                _ => ChatType.Normal
                            };
                            if (mode <= 3)
                            {
                                foreach (PlayerObject recipient in Envir.Players
                                             .Concat(new[] { player }).Distinct())
                                    if (!recipient.Info.LingFengProgress.IsGlobalMessageFiltered(3))
                                        recipient.ReceiveChat(param[1], type);
                            }
                            else
                            {
                                player.ReceiveChat(param[1], type);
                            }
                        }
                        break;

                    case ActionType.LingFengFilterGlobalMessage:
                        if (int.TryParse(param[0], NumberStyles.None, CultureInfo.InvariantCulture,
                                out int filterCategory) && filterCategory is >= 1 and <= 4 &&
                            param[1] is "0" or "1")
                            player.Info.LingFengProgress.SetGlobalMessageFilter(
                                filterCategory, param[1] == "1");
                        break;

                    case ActionType.LingFengSendCurrentTargetMessage:
                        {
                            if (!TryGetLingFengCurrentTargetPlayer(
                                    player, out PlayerObject messageTarget) ||
                                !byte.TryParse(param[0], NumberStyles.None,
                                    CultureInfo.InvariantCulture, out _))
                            {
                                MessageQueue.Enqueue(
                                    $"[TxtScripts] M.SENDCENTERMSG 缺少当前人物目标，页码：{Key}");
                                break;
                            }
                            messageTarget.ReceiveChat(param[1], ChatType.Announcement);
                        }
                        break;

                    case ActionType.LingFengSendCenterAudienceMessage:
                        {
                            if (!int.TryParse(param[0], NumberStyles.None, CultureInfo.InvariantCulture,
                                    out int centerMode) ||
                                !TrySendLingFengCenterAudienceMessage(player, centerMode, param[1]))
                                MessageQueue.Enqueue(
                                    $"[TxtScripts] SENDCENTERMSG 受众模式无效，页码：{Key}");
                        }
                        break;

                    case ActionType.LingFengSendAudienceMessage:
                        {
                            if (!int.TryParse(param[0], NumberStyles.None, CultureInfo.InvariantCulture,
                                    out int recipientMode) ||
                                !int.TryParse(param[2], NumberStyles.None, CultureInfo.InvariantCulture,
                                    out int range) ||
                                !TrySendLingFengAudienceMessage(
                                    player, recipientMode, param[1], range))
                                MessageQueue.Enqueue(
                                    $"[TxtScripts] SENDNEWLINEMSG 受众模式无效，页码：{Key}");
                        }
                        break;

                    case ActionType.LingFengSendDelayedMessage:
                        {
                            if (!int.TryParse(param[1], NumberStyles.None,
                                    CultureInfo.InvariantCulture, out int delayedSeconds) ||
                                delayedSeconds <= 0 ||
                                !byte.TryParse(param[2], NumberStyles.None,
                                    CultureInfo.InvariantCulture, out _) ||
                                param[3] is not ("0" or "1") ||
                                !param[4].StartsWith("@", StringComparison.Ordinal))
                                break;

                            player.ReceiveChat(param[0], ChatType.Announcement);
                            long delay = Math.Min(
                                long.MaxValue - Envir.Time,
                                (long)delayedSeconds * Settings.Second);
                            player.ActionList.Add(new DelayedAction(
                                DelayedType.LingFengDelayedMessage,
                                Envir.Time + delay,
                                player.NPCObjectID,
                                NPCScript.CurrentSystemScriptId ?? player.NPCScriptID,
                                $"[{param[4]}]",
                                player.CurrentMap,
                                param[3] == "1"));
                        }
                        break;

                    case ActionType.LingFengSendMoveMessage:
                        {
                            if (!int.TryParse(param[0], NumberStyles.None, CultureInfo.InvariantCulture,
                                    out int recipientMode) || recipientMode is < 0 or > 7 ||
                                !byte.TryParse(param[1], NumberStyles.None, CultureInfo.InvariantCulture,
                                    out _) ||
                                !byte.TryParse(param[2], NumberStyles.None, CultureInfo.InvariantCulture,
                                    out _) ||
                                !int.TryParse(param[3], NumberStyles.Integer, CultureInfo.InvariantCulture,
                                    out _) ||
                                !int.TryParse(param[4], NumberStyles.None, CultureInfo.InvariantCulture,
                                    out int lineCount) || lineCount <= 0 || lineCount > 20 ||
                                !int.TryParse(param[6], NumberStyles.None, CultureInfo.InvariantCulture,
                                    out int fontSize) || fontSize is < 0 or > 20 ||
                                !int.TryParse(param[7], NumberStyles.None, CultureInfo.InvariantCulture,
                                    out int speed) || speed < 0 ||
                                !int.TryParse(param[8], NumberStyles.None, CultureInfo.InvariantCulture,
                                    out int range) || range < 0 || param[5].Length > 1024)
                                break;

                            TrySendLingFengAudienceMessage(
                                player, recipientMode, param[5], range);
                        }
                        break;

                    case ActionType.LingFengGuildNoticeMessage:
                        if (!TryDispatchLingFengGuildNotice(
                                player, param[0], param[1], param[2], param[3]))
                            MessageQueue.Enqueue(
                                $"[TxtScripts] GUILDNOTICEMSG 执行失败，页码：{Key}");
                        break;

                    case ActionType.LingFengMessageBox:
                        player.NPCSpeech ??= new List<string>();
                        player.NPCSpeech.Clear();
                        player.NPCSpeech.Add(param[0]);
                        break;

                    case ActionType.LingFengAdjustResourcePercent:
                        {
                            if (!decimal.TryParse(param[2], NumberStyles.Number, CultureInfo.InvariantCulture,
                                    out decimal proportion) || proportion < 0 ||
                                !TryGetPercentScale(param[3], out int scale))
                                break;
                            int current = param[0] == "HP" ? player.HP : player.MP;
                            int maximum = param[0] == "HP" ? player.Stats[Stat.HP] : player.Stats[Stat.MP];
                            decimal amountValue = decimal.Truncate(maximum * proportion / scale);
                            if (amountValue > int.MaxValue) break;
                            int amount = decimal.ToInt32(amountValue);
                            int delta = param[1] switch
                            {
                                "+" => amount,
                                "-" => -amount,
                                "=" => amount - current,
                                _ => 0
                            };
                            if (param[0] == "HP") player.ChangeHP(delta);
                            else player.ChangeMP(delta);
                        }
                        break;

                    case ActionType.LingFengChangeAbility:
                        {
                            if (!int.TryParse(param[0], NumberStyles.Integer, CultureInfo.InvariantCulture,
                                    out int abilityIndex) ||
                                !int.TryParse(param[2], NumberStyles.Integer, CultureInfo.InvariantCulture,
                                    out int value) ||
                                !int.TryParse(param[3], NumberStyles.Integer, CultureInfo.InvariantCulture,
                                    out int durationSeconds) ||
                                !TryChangeLingFengTargetAbility(
                                    player, param[5],
                                    $"{SourceKey}|{(ReferenceEquals(acts, ActList) ? "ACT" : "ELSEACT")}|{i}",
                                    abilityIndex, param[1], value, durationSeconds, param[4] == "1"))
                            {
                                MessageQueue.Enqueue($"[TxtScripts] CHANGEHUMABILITY 参数无效，页码：{Key}");
                            }
                        }
                        break;

                    case ActionType.LingFengChangeMapMonsterAbility:
                        {
                            if (!TryChangeLingFengMapMonsterAbility(
                                    player, param,
                                    $"{SourceKey}|MAPABILITY|" +
                                    $"{(ReferenceEquals(acts, ActList) ? "ACT" : "ELSEACT")}|{i}"))
                                MessageQueue.Enqueue(
                                    $"[TxtScripts] CHANGEMONABILITY 参数或目标无效，页码：{Key}");
                        }
                        break;

                    case ActionType.LingFengRecalcMapMonsterAbility:
                        {
                            if (!TryRecalcLingFengMapMonsterAbility(player, param, out int refreshed))
                            {
                                MessageQueue.Enqueue(
                                    $"[TxtScripts] RECALCMONABILITY 参数或地图无效，页码：{Key}");
                                break;
                            }
                            if (Server.Scripting.ScriptTrace.IsEnabled(player))
                                Server.Scripting.ScriptTrace.Record(
                                    player, $"[TXT] RECALCMONABILITY {param[0]} {param[1]} -> {refreshed}");
                        }
                        break;

                    case ActionType.LingFengClearMapItems:
                        {
                            if (!TryClearLingFengMapItems(player, param, out int cleared))
                            {
                                MessageQueue.Enqueue(
                                    $"[TxtScripts] CLEARITEMMAP 参数或地图无效，页码：{Key}");
                                break;
                            }
                            if (Server.Scripting.ScriptTrace.IsEnabled(player))
                                Server.Scripting.ScriptTrace.Record(
                                    player, $"[TXT] CLEARITEMMAP {param[0]} -> {cleared}");
                        }
                        break;

                    case ActionType.LingFengChangeNameColour:
                        {
                            if (!int.TryParse(param[0], NumberStyles.None,
                                    CultureInfo.InvariantCulture, out int colour) ||
                                colour is < byte.MinValue or > byte.MaxValue)
                            {
                                MessageQueue.Enqueue(
                                    $"[TxtScripts] CHANGENAMECOLOR 颜色代码无效，页码：{Key}");
                                break;
                            }
                            player.Info.LingFengProgress.SetNameColour(colour);
                            player.RefreshNameColour();
                        }
                        break;

                    case ActionType.LingFengCsvOpenCache:
                        if (Envir.PhysicalCsvContentProvider?.Contains(param[0]) != true)
                            MessageQueue.Enqueue($"[TxtScripts] CSVOPENCACHE 未找到候选 CSV：{param[0]}，页码：{Key}");
                        break;

                    case ActionType.LingFengReadConfigFileItem:
                        if (Envir.PhysicalTextDataProvider == null ||
                            !Envir.PhysicalTextDataProvider.TryReadConfigValue(
                                param[0], param[1], param[2], out string configValue) ||
                            !TryStoreScriptTextValue(player, param[3], configValue))
                            MessageQueue.Enqueue(
                                $"[TxtScripts] READCONFIGFILEITEM 执行失败，页码：{Key}");
                        break;

                    case ActionType.LingFengWriteCachedConfigFileItem:
                        if (Envir.PhysicalTextDataProvider == null ||
                            !Envir.PhysicalTextDataProvider.TryWriteCachedConfigValue(
                                param[0], param[1], param[2], param[3]))
                            MessageQueue.Enqueue(
                                $"[TxtScripts] WRITECACHECONFIGFILEITEM 执行失败，页码：{Key}");
                        break;

                    case ActionType.LingFengGetListString:
                        {
                            if (!int.TryParse(param[1], NumberStyles.None,
                                    CultureInfo.InvariantCulture, out int lineIndex) ||
                                !TryGetCandidateTextDefinition(
                                    param[0], out TextFileDefinition listDefinition) ||
                                lineIndex < 0 || lineIndex >= listDefinition.Lines.Count)
                            {
                                MessageQueue.Enqueue(
                                    $"[TxtScripts] GETLISTSTRING 文件或行号无效，页码：{Key}");
                                break;
                            }
                            string line = listDefinition.Lines[lineIndex] ?? string.Empty;

                            if (param[3].Length == 0)
                            {
                                if (!TryStoreScriptTextValue(player, param[2], line))
                                    MessageQueue.Enqueue(
                                        $"[TxtScripts] GETLISTSTRING 结果写入失败，页码：{Key}");
                                break;
                            }

                            int separator = line.IndexOf(':');
                            if (separator < 0 ||
                                !TryStoreScriptTextValue(player, param[2], line[..separator]) ||
                                !TryStoreScriptTextValue(player, param[3], line[(separator + 1)..]))
                                MessageQueue.Enqueue(
                                    $"[TxtScripts] GETLISTSTRING 双变量行必须为 文本:数值，页码：{Key}");
                        }
                        break;

                    case ActionType.LingFengGetStringPosition:
                        {
                            int foundIndex = 9_999_999;
                            if (param[2] == "0" &&
                                TryGetCandidateTextDefinition(
                                    param[0], out TextFileDefinition definition))
                            {
                                for (int lineIndex = 0;
                                     lineIndex < definition.Lines.Count; lineIndex++)
                                {
                                    if (!string.Equals(
                                            (definition.Lines[lineIndex] ?? string.Empty).Trim(),
                                            param[1], StringComparison.OrdinalIgnoreCase))
                                        continue;
                                    foundIndex = lineIndex;
                                    break;
                                }
                            }

                            if (!TryStoreScriptValue(player, "N0", foundIndex))
                                MessageQueue.Enqueue(
                                    $"[TxtScripts] GETSTRINGPOS 结果写入失败，页码：{Key}");
                        }
                        break;

                    case ActionType.LingFengGetStringPositionEx:
                        {
                            int foundIndex = 9_999_999;
                            string foundLine = string.Empty;
                            if (param[4] == "0" &&
                                TryGetCandidateTextDefinition(
                                    param[0], out TextFileDefinition definition))
                            {
                                StringComparison comparison = StringComparison.OrdinalIgnoreCase;
                                for (int lineIndex = 0; lineIndex < definition.Lines.Count; lineIndex++)
                                {
                                    string candidate = definition.Lines[lineIndex] ?? string.Empty;
                                    bool matched = param[5] == "1"
                                        ? string.Equals(candidate, param[1], comparison)
                                        : candidate.Contains(param[1], comparison);
                                    if (!matched) continue;
                                    foundIndex = lineIndex;
                                    foundLine = candidate;
                                    break;
                                }
                            }
                            if (!TryStoreScriptValue(player, param[2], foundIndex) ||
                                !TryStoreScriptTextValue(player, param[3], foundLine))
                                MessageQueue.Enqueue(
                                    $"[TxtScripts] GETSTRINGPOSEX 结果写入失败，页码：{Key}");
                        }
                        break;

                    case ActionType.LingFengCsvFindTextRow:
                        {
                            string[] range = param[2].Split('~');
                            if (range.Length != 2 ||
                                !int.TryParse(range[0], NumberStyles.None, CultureInfo.InvariantCulture,
                                    out int startRow) ||
                                !int.TryParse(range[1], NumberStyles.None, CultureInfo.InvariantCulture,
                                    out int endRow) ||
                                !int.TryParse(param[3], NumberStyles.None, CultureInfo.InvariantCulture,
                                    out int column) ||
                                Envir.PhysicalCsvContentProvider == null ||
                                !Envir.PhysicalCsvContentProvider.TryFindTextRow(
                                    param[0], param[1], startRow, endRow, column,
                                    param[4] == "1", out int foundRow) ||
                                !TryStoreScriptValue(player, param[5], foundRow))
                            {
                                MessageQueue.Enqueue($"[TxtScripts] CSVFINDTEXTROW 执行失败，页码：{Key}");
                            }
                        }
                        break;

                    case ActionType.LingFengGetRandomLineText:
                        {
                            if (param[3] == "1" ||
                                !int.TryParse(param[2], NumberStyles.Integer, CultureInfo.InvariantCulture,
                                    out int requestedLine) ||
                                !TryGetCandidateTextDefinition(param[0], out TextFileDefinition definition))
                            {
                                MessageQueue.Enqueue($"[TxtScripts] GETRANDOMLINETEXT 文件或参数无效，页码：{Key}");
                                break;
                            }
                            IReadOnlyList<string> lines = definition.Lines;
                            if (lines.Count == 0) break;
                            int lineIndex;
                            if (requestedLine == 0)
                                lineIndex = Envir.Random.Next(lines.Count);
                            else if (requestedLine > 0)
                                lineIndex = requestedLine - 1;
                            else
                                lineIndex = lines.Count + requestedLine;
                            if (lineIndex < 0 || lineIndex >= lines.Count ||
                                !TryStoreScriptTextValue(player, param[1], lines[lineIndex] ?? string.Empty))
                                MessageQueue.Enqueue($"[TxtScripts] GETRANDOMLINETEXT 结果写入失败，页码：{Key}");
                        }
                        break;

                    case ActionType.LingFengExtractString:
                        {
                            string[] values = param[1].Split(
                                new[] { param[0] }, StringSplitOptions.None);
                            for (int destinationIndex = 2; destinationIndex < param.Count; destinationIndex++)
                            {
                                string value = destinationIndex - 2 < values.Length
                                    ? values[destinationIndex - 2]
                                    : string.Empty;
                                if (!TryStoreScriptTextValue(player, param[destinationIndex], value))
                                {
                                    MessageQueue.Enqueue($"[TxtScripts] EXTRACTSTRING 结果写入失败，页码：{Key}");
                                    break;
                                }
                            }
                        }
                        break;

                    case ActionType.LingFengExtractStringEx:
                        {
                            string[] values = param[1].Split(
                                new[] { param[0] }, StringSplitOptions.None);
                            bool stored = true;
                            for (int valueIndex = 0; valueIndex < values.Length; valueIndex++)
                            {
                                if (!TryGetIncrementedVariable(param[2], valueIndex, out string destination) ||
                                    !TryStoreScriptTextValue(player, destination, values[valueIndex]))
                                {
                                    stored = false;
                                    break;
                                }
                            }
                            if (stored && param[3].Length > 0)
                                stored = TryStoreScriptValue(player, param[3], values.Length);
                            if (!stored)
                                MessageQueue.Enqueue(
                                    $"[TxtScripts] EXTRACTSTRINGEX 结果写入失败，页码：{Key}");
                        }
                        break;

                    case ActionType.LingFengCloseNpc:
                        player.Enqueue(new S.LingFengDialog { Remove = true, DialogId = 0 });
                        player.NPCSpeech ??= new List<string>();
                        player.NPCSpeech.Clear();
                        player.EndNpcConversation(player.NPCObjectID);
                        break;

                    case ActionType.LingFengReclaimItem:
                        // 当前运行时尚无 OPENITEMBOXEX 物品托管会话。物品从未离开背包时，
                        // 退回命令必须保持幂等，不能凭脚本变量复制或删除物品。
                        if (Server.Scripting.ScriptTrace.IsEnabled(player))
                            Server.Scripting.ScriptTrace.Record(
                                player, "[TXT] RECLAIMITEM -> 当前无物品托管会话");
                        break;

                    case ActionType.LingFengTextLength:
                        if (!TryStoreScriptValue(player, param[1], GetLingFengTextLength(param[0])))
                            MessageQueue.Enqueue(
                                $"[TxtScripts] TEXTLENGTH 结果写入失败，页码：{Key}");
                        break;

                    case ActionType.LingFengSetStringBlank:
                        {
                            string current = Regex.IsMatch(
                                param[0], @"^[A-Za-z][0-9]+$", RegexOptions.CultureInvariant)
                                ? FindVariable(player, "%" + param[0])
                                : ReplaceValue(player, $"<$STR({param[0]})>");
                            if (!int.TryParse(param[1], NumberStyles.None,
                                    CultureInfo.InvariantCulture, out int targetLength))
                                break;
                            int spaces = Math.Max(0, targetLength - GetLingFengTextLength(current));
                            string updated = param[2] == "0"
                                ? new string(' ', spaces) + current
                                : current + new string(' ', spaces);
                            if (!TryStoreScriptTextValue(player, param[0], updated))
                                MessageQueue.Enqueue(
                                    $"[TxtScripts] SETSTRINGBLANK 结果写入失败，页码：{Key}");
                        }
                        break;

                    case ActionType.LingFengTextReplace:
                        {
                            StringComparison comparison = param[4] == "1"
                                ? StringComparison.Ordinal
                                : StringComparison.OrdinalIgnoreCase;
                            string replaced;
                            if (param[5] == "1")
                            {
                                int index = param[0].IndexOf(param[1], comparison);
                                replaced = index < 0
                                    ? param[0]
                                    : param[0].Remove(index, param[1].Length)
                                        .Insert(index, param[2]);
                            }
                            else
                            {
                                replaced = param[0].Replace(param[1], param[2], comparison);
                            }
                            if (!TryStoreScriptTextValue(player, param[3], replaced))
                                MessageQueue.Enqueue(
                                    $"[TxtScripts] TEXTREPLACE 结果写入失败，页码：{Key}");
                        }
                        break;

                    case ActionType.LingFengUnixToString:
                        if (!long.TryParse(param[0], NumberStyles.Integer,
                                CultureInfo.InvariantCulture, out long unixSeconds))
                            break;
                        try
                        {
                            string format = param[2] == "1"
                                ? "yyyy/MM/dd HH:mm:ss"
                                : "yyyy-MM-dd HH:mm:ss";
                            string timestampText = DateTimeOffset.FromUnixTimeSeconds(unixSeconds)
                                .UtcDateTime.ToString(format, CultureInfo.InvariantCulture);
                            if (!TryStoreScriptTextValue(player, param[1], timestampText))
                                MessageQueue.Enqueue(
                                    $"[TxtScripts] UNIXTOSTR 结果写入失败，页码：{Key}");
                        }
                        catch (ArgumentOutOfRangeException)
                        {
                            MessageQueue.Enqueue(
                                $"[TxtScripts] UNIXTOSTR 时间戳超出范围，页码：{Key}");
                        }
                        break;

                    case ActionType.LingFengRandomSplit:
                        if (!TryExecuteRandomSplit(player, param))
                            MessageQueue.Enqueue($"[TxtScripts] RANDOMSPLIT 参数或结果写入无效，页码：{Key}");
                        break;

                    case ActionType.LingFengRandomVariable:
                        {
                            if (!int.TryParse(param[1], NumberStyles.None, CultureInfo.InvariantCulture,
                                    out int firstBound))
                                break;
                            int randomValue;
                            if (string.IsNullOrEmpty(param[2]))
                                randomValue = firstBound == 0 ? 0 : Envir.Random.Next(firstBound);
                            else if (!int.TryParse(param[2], NumberStyles.None,
                                         CultureInfo.InvariantCulture, out int secondBound) ||
                                     secondBound < firstBound)
                                break;
                            else
                                randomValue = firstBound == secondBound
                                    ? firstBound
                                    : Envir.Random.Next(firstBound, secondBound + 1);
                            if (!TryStoreScriptValue(player, param[0], randomValue))
                                MessageQueue.Enqueue($"[TxtScripts] MOVR 结果写入失败，页码：{Key}");
                        }
                        break;

                    case ActionType.LingFengGameGoldAdjust:
                        if (!LingFengNumericCommandExecutor.TryAdjust(
                                player.Info.PearlCount, param[0], param[1], 0, int.MaxValue,
                                false, out long adjustedGameGold, out _))
                        {
                            MessageQueue.Enqueue($"[TxtScripts] GAMEGOLD 调整失败，页码：{Key}");
                            break;
                        }
                        int gameGoldDelta = (int)adjustedGameGold - player.Info.PearlCount;
                        if (gameGoldDelta >= 0)
                            player.IntelligentCreatureGainPearls(gameGoldDelta);
                        else
                            player.IntelligentCreatureLosePearls(-gameGoldDelta);
                        break;

                    case ActionType.LingFengGamePointAdjust:
                        if (!LingFengNumericCommandExecutor.TryAdjust(
                                player.Info.LingFengProgress.GamePoint, param[0], param[1],
                                0, int.MaxValue, false, out long adjustedGamePoint, out _))
                        {
                            MessageQueue.Enqueue($"[TxtScripts] GAMEPOINT 调整失败，页码：{Key}");
                            break;
                        }
                        player.Info.LingFengProgress.SetGamePoint((int)adjustedGamePoint);
                        break;

                    case ActionType.LingFengGameDiamondAdjust:
                        if (!LingFengNumericCommandExecutor.TryAdjust(
                                player.Info.LingFengProgress.GameDiamond, param[0], param[1],
                                0, int.MaxValue, false, out long adjustedGameDiamond, out _))
                        {
                            MessageQueue.Enqueue($"[TxtScripts] GAMEDIAMOND 调整失败，页码：{Key}");
                            break;
                        }
                        player.Info.LingFengProgress.SetGameDiamond((int)adjustedGameDiamond);
                        break;

                    case ActionType.LingFengGameGirdAdjust:
                        if (!LingFengNumericCommandExecutor.TryAdjust(
                                player.Info.LingFengProgress.GameGird, param[0], param[1],
                                0, int.MaxValue, false, out long adjustedGameGird, out _))
                        {
                            MessageQueue.Enqueue($"[TxtScripts] GAMEGIRD 调整失败，页码：{Key}");
                            break;
                        }
                        player.Info.LingFengProgress.SetGameGird((int)adjustedGameGird);
                        break;

                    case ActionType.LingFengAdjustResource:
                        {
                            MapObject resourceTarget = param[3] switch
                            {
                                "SELF" => player,
                                "L" when TryGetLingFengLastActorPlayer(out PlayerObject resourceActor) => resourceActor,
                                _ => null
                            };
                            int current = resourceTarget switch
                            {
                                HumanObject human when param[0] == "HP" => human.HP,
                                HumanObject human => human.MP,
                                MonsterObject monster when param[0] == "HP" => monster.HP,
                                _ => -1
                            };
                            int maximum = resourceTarget switch
                            {
                                HumanObject human when param[0] == "HP" => human.Stats[Stat.HP],
                                HumanObject human => human.Stats[Stat.MP],
                                MonsterObject monster when param[0] == "HP" => monster.Stats[Stat.HP],
                                _ => -1
                            };
                            if (maximum < 0 || !LingFengNumericCommandExecutor.TryAdjust(
                                    current, param[1], param[2], 0, maximum, true,
                                    out long adjusted, out _))
                            {
                                MessageQueue.Enqueue($"[TxtScripts] {param[0]} 固定值调整失败，页码：{Key}");
                                break;
                            }
                            int delta = checked((int)adjusted - current);
                            if (resourceTarget is HumanObject targetHuman)
                            {
                                if (param[0] == "HP") targetHuman.ChangeHP(delta);
                                else targetHuman.ChangeMP(delta);
                            }
                            else if (resourceTarget is MonsterObject targetMonster && param[0] == "HP")
                                targetMonster.ChangeHP(delta);
                        }
                        break;

                    case ActionType.LingFengChangeState:
                        {
                            MapObject stateTarget = param[0] switch
                            {
                                "SELF" => player,
                                "M" when TryGetLingFengCurrentTargetMonster(player, out MonsterObject targetMonster) => targetMonster,
                                "L" when TryGetLingFengLastActorPlayer(out PlayerObject lastActor) => lastActor,
                                _ => null
                            };
                            if (stateTarget == null ||
                                !int.TryParse(param[1], NumberStyles.None, CultureInfo.InvariantCulture,
                                    out int stateCode) ||
                                !int.TryParse(param[2], NumberStyles.None, CultureInfo.InvariantCulture,
                                    out int duration) ||
                                !int.TryParse(param[3], NumberStyles.Integer, CultureInfo.InvariantCulture,
                                    out int stateValue) ||
                                !int.TryParse(param[4], NumberStyles.None, CultureInfo.InvariantCulture,
                                    out int tickSeconds) ||
                                !TryApplyLingFengState(player, stateTarget, stateCode, duration,
                                    stateValue, tickSeconds))
                                MessageQueue.Enqueue($"[TxtScripts] CHANGESTATE 执行失败，页码：{Key}");
                        }
                        break;

                    case ActionType.LingFengMakePoison:
                        {
                            MapObject poisonTarget = param[0] switch
                            {
                                "M" when TryGetLingFengCurrentTargetMonster(
                                    player, out MonsterObject targetMonster) => targetMonster,
                                "L" when TryGetLingFengLastActor(out MapObject lastActor) => lastActor,
                                _ => null
                            };
                            if (poisonTarget == null ||
                                !int.TryParse(param[1], NumberStyles.None, CultureInfo.InvariantCulture,
                                    out int poisonCode) ||
                                !int.TryParse(param[2], NumberStyles.None, CultureInfo.InvariantCulture,
                                    out int poisonDuration) || poisonDuration <= 0 ||
                                !int.TryParse(param[3], NumberStyles.None, CultureInfo.InvariantCulture,
                                    out int poisonPower) || poisonPower < 0 ||
                                param[4] is not ("0" or "1") || param[5] is not ("0" or "1") ||
                                !TryApplyLingFengPoison(player, poisonTarget, poisonCode,
                                    poisonDuration, poisonPower, param[4] == "1", param[5] == "1"))
                                MessageQueue.Enqueue($"[TxtScripts] {param[0]}.MAKEPOSION 执行失败，页码：{Key}");
                        }
                        break;

                    case ActionType.LingFengGetTargetAbility:
                        {
                            MapObject abilityTarget = param[0] switch
                            {
                                "M" when TryGetLingFengCurrentTargetMonster(
                                    player, out MonsterObject targetMonster) => targetMonster,
                                "M" when TryGetLingFengCurrentTargetPlayer(
                                    player, out PlayerObject targetPlayer) => targetPlayer,
                                "L" when TryGetLingFengLastActor(out MapObject lastActor) => lastActor,
                                _ => null
                            };
                            if (abilityTarget == null ||
                                !int.TryParse(param[1], NumberStyles.None, CultureInfo.InvariantCulture,
                                    out int abilityType) ||
                                !TryGetLingFengObjectAbility(abilityTarget, abilityType, out int abilityValue) ||
                                !TryStoreScriptValue(player, param[2], abilityValue))
                                MessageQueue.Enqueue(
                                    $"[TxtScripts] {param[0]}.GETOBJECTABILITYEX 执行失败，页码：{Key}");
                        }
                        break;

                    case ActionType.LingFengTimedTargetHp:
                        {
                            if (!TryGetLingFengCurrentTargetMonster(player, out MonsterObject hpTarget) ||
                                !int.TryParse(param[1], NumberStyles.None, CultureInfo.InvariantCulture,
                                    out int hpValue) || hpValue < 0 ||
                                !int.TryParse(param[2], NumberStyles.None, CultureInfo.InvariantCulture,
                                    out int hpDelay) || hpDelay < 0 ||
                                !int.TryParse(param[3], NumberStyles.None, CultureInfo.InvariantCulture,
                                    out int hpCount) || hpCount <= 0 ||
                                !int.TryParse(param[6], NumberStyles.None, CultureInfo.InvariantCulture,
                                    out int hpUnit) ||
                                !hpTarget.TryScheduleLingFengHumanHp(
                                    player, param[0], hpValue, hpDelay, hpCount, hpUnit))
                                MessageQueue.Enqueue($"[TxtScripts] M.HUMANHP 执行失败，页码：{Key}");
                        }
                        break;

                    case ActionType.LingFengGetMonsterField:
                        {
                            MonsterInfo fieldMonster = Envir.MonsterInfoList.FirstOrDefault(info =>
                                string.Equals(info.Name, param[0], StringComparison.OrdinalIgnoreCase));
                            if (fieldMonster == null || param[1] != "RACE" ||
                                !TryStoreScriptValue(player, param[2], fieldMonster.AI))
                                MessageQueue.Enqueue(
                                    $"[TxtScripts] GETDBMONSTERFIELDVALUE 执行失败，页码：{Key}");
                        }
                        break;

                    case ActionType.LingFengRepairAll:
                        player.LingFengRepairAllEquipment();
                        break;

                    case ActionType.LingFengGetPlayInfo:
                        if (param[0] != "HAIR" ||
                            !TryStoreScriptValue(player, param[1], player.Hair))
                            MessageQueue.Enqueue($"[TxtScripts] GETPLAYINFO 执行失败，页码：{Key}");
                        break;

                    case ActionType.LingFengProbePlayer:
                        player.LingFengProbePlayer(param[0]);
                        break;

                    case ActionType.LingFengHideModeEx:
                        if (!int.TryParse(param[0], NumberStyles.None, CultureInfo.InvariantCulture,
                                out int hideDuration) ||
                            !player.ApplyLingFengHideMode(hideDuration, param[1] == "1"))
                            MessageQueue.Enqueue($"[TxtScripts] HIDEMODEEX 执行失败，页码：{Key}");
                        break;

                    case ActionType.LingFengChangeMode:
                        player.ClearLingFengHideMode();
                        break;

                    case ActionType.LingFengChangeModeEx:
                        bool modeApplied = int.TryParse(param[1], NumberStyles.None,
                            CultureInfo.InvariantCulture, out int modeDuration);
                        if (modeApplied && param[0] == "1")
                            modeApplied = player.ApplyLingFengInvincibleMode(modeDuration);
                        else if (modeApplied && modeDuration == 0)
                            player.ClearLingFengHideMode();
                        else if (modeApplied)
                            modeApplied = player.ApplyLingFengHideMode(
                                modeDuration, semiTransparent: false);
                        if (!modeApplied)
                            MessageQueue.Enqueue(
                                $"[TxtScripts] CHANGEMODEEX 执行失败，页码：{Key}");
                        break;

                    case ActionType.LingFengChangeSpeed:
                        if (!int.TryParse(param[0], NumberStyles.None, CultureInfo.InvariantCulture,
                                out int changeSpeedType) ||
                            !int.TryParse(param[1], NumberStyles.Integer, CultureInfo.InvariantCulture,
                                out int changeSpeedValue) ||
                            !int.TryParse(param[2], NumberStyles.None, CultureInfo.InvariantCulture,
                                out int changeSpeedDuration) ||
                            !TryChangeLingFengTargetSpeed(
                                player, param[3],
                                $"{SourceKey}|{(ReferenceEquals(acts, ActList) ? "ACT" : "ELSEACT")}|{i}|{param[3]}",
                                changeSpeedType, changeSpeedValue, changeSpeedDuration))
                            MessageQueue.Enqueue($"[TxtScripts] CHANGESPEED 执行失败，页码：{Key}");
                        break;

                    case ActionType.LingFengSetSuckDamage:
                        if (!long.TryParse(param[1], NumberStyles.Integer, CultureInfo.InvariantCulture,
                                out long suckAmount) ||
                            !int.TryParse(param[2], NumberStyles.None, CultureInfo.InvariantCulture,
                                out int suckRatio) ||
                            !int.TryParse(param[3], NumberStyles.None, CultureInfo.InvariantCulture,
                                out int suckSuccess) ||
                            !player.TrySetLingFengSuckDamage(
                                param[0], suckAmount, suckRatio, suckSuccess))
                            MessageQueue.Enqueue($"[TxtScripts] SETSUCKDAMAGE 执行失败，页码：{Key}");
                        break;

                    case ActionType.LingFengRangeHarm:
                        string rangeLibraryName = string.Empty;
                        if (!int.TryParse(param[0], NumberStyles.Integer, CultureInfo.InvariantCulture,
                                out int rangeX) ||
                            !int.TryParse(param[1], NumberStyles.Integer, CultureInfo.InvariantCulture,
                                out int rangeY) ||
                            !int.TryParse(param[2], NumberStyles.None, CultureInfo.InvariantCulture,
                                out int rangeSize) || rangeSize < 0 ||
                            !int.TryParse(param[3], NumberStyles.None, CultureInfo.InvariantCulture,
                                out int rangeDamage) || rangeDamage < 0 ||
                            !int.TryParse(param[4], NumberStyles.None, CultureInfo.InvariantCulture,
                                out int rangeEffect) || rangeEffect is not (0 or 8) ||
                            !int.TryParse(param[5], NumberStyles.None, CultureInfo.InvariantCulture,
                                out int rangeEffectValue) || rangeEffectValue < 0 ||
                            rangeEffect == 8 && rangeEffectValue == 0 ||
                            !int.TryParse(param[6], NumberStyles.None, CultureInfo.InvariantCulture,
                                out int rangeCheckResistance) || rangeCheckResistance is < 0 or > 1 ||
                            !int.TryParse(param[7], NumberStyles.None, CultureInfo.InvariantCulture,
                                out int rangeTarget) || rangeTarget is < 0 or > 2 ||
                            !int.TryParse(param[8], NumberStyles.None, CultureInfo.InvariantCulture,
                                out int rangeLibraryIndex) || rangeLibraryIndex < 0 ||
                            !int.TryParse(param[9], NumberStyles.None, CultureInfo.InvariantCulture,
                                out int rangeStartIndex) || rangeStartIndex < 0 ||
                            !int.TryParse(param[10], NumberStyles.None, CultureInfo.InvariantCulture,
                                out int rangeFrameCount) || rangeFrameCount < 0 ||
                            !int.TryParse(param[11], NumberStyles.None, CultureInfo.InvariantCulture,
                                out int rangeFrameDelay) || rangeFrameDelay < 0 ||
                            !int.TryParse(param[12], NumberStyles.None, CultureInfo.InvariantCulture,
                                out int rangeTransparent) || rangeTransparent is < 0 or > 1 ||
                            !int.TryParse(param[13], NumberStyles.None, CultureInfo.InvariantCulture,
                                out int rangePhysical) || rangePhysical is < 0 or > 1 ||
                            rangeLibraryIndex == 0 &&
                                (rangeStartIndex != 0 || rangeFrameCount != 0 || rangeFrameDelay != 0) ||
                            rangeLibraryIndex > 0 &&
                                (rangeFrameCount is < 1 or > 1000 ||
                                 rangeFrameDelay is < 1 or > 60000) ||
                            rangeLibraryIndex > 0 &&
                                !TryResolveLingFengEffectLibrary(
                                    rangeLibraryIndex, out rangeLibraryName) ||
                            !TryResolveLingFengRangeActor(player, param[14], out MapObject rangeActor))
                            MessageQueue.Enqueue($"[TxtScripts] RANGEHARM 执行失败，页码：{Key}");
                        else
                        {
                            if (player.LingFengRangeHarm(
                                    rangeActor, rangeX, rangeY, rangeSize, rangeDamage,
                                    rangeEffect, rangeEffectValue, rangeCheckResistance == 1,
                                    rangeTarget, rangeLibraryName, rangeStartIndex,
                                    rangeFrameCount, rangeFrameDelay, rangeTransparent == 1,
                                    rangePhysical == 1) < 0)
                                MessageQueue.Enqueue(
                                    $"[TxtScripts] RANGEHARM 执行失败，页码：{Key}");
                        }
                        break;

                    case ActionType.LingFengReleaseMagic:
                        {
                            MapObject magicTarget = null;
                            PlayerObject magicPlayer = null;
                            if (!TryGetLingFengCurrentTargetMonster(player, out MonsterObject magicMonster))
                                TryGetLingFengCurrentTargetPlayer(player, out magicPlayer);
                            else
                                magicTarget = magicMonster;
                            magicTarget ??= magicPlayer;
                            if (!int.TryParse(param[0], NumberStyles.None, CultureInfo.InvariantCulture,
                                    out int releaseSpell) ||
                                !player.TryReleaseLingFengMagic(
                                    releaseSpell, 3, magicTarget, param[1] == "1", false))
                                MessageQueue.Enqueue($"[TxtScripts] RELEASEMAGIC 执行失败，页码：{Key}");
                        }
                        break;

                    case ActionType.LingFengGiveFengHao:
                        CharacterInfo fengHaoInfo = GetLingFengProgressInfo(player, param[0]);
                        if (fengHaoInfo == null ||
                            !fengHaoInfo.LingFengProgress.GrantTitle(param[1], param[2] == "1"))
                            MessageQueue.Enqueue($"[TxtScripts] GIVEFENGHAO 执行失败，页码：{Key}");
                        break;

                    case ActionType.LingFengRevokeFengHao:
                        CharacterInfo revokeFengHaoInfo =
                            GetLingFengProgressInfo(player, param[0]);
                        if (revokeFengHaoInfo == null ||
                            !revokeFengHaoInfo.LingFengProgress.RevokeTitle(param[1]))
                            MessageQueue.Enqueue(
                                $"[TxtScripts] RECYCFENGHAO 执行失败，页码：{Key}");
                        break;

                    case ActionType.LingFengSetClientBuff:
                        MapObject clientBuffTarget = param[0] switch
                        {
                            "M" when TryGetLingFengCurrentTargetMonster(
                                player, out MonsterObject buffMonster) => buffMonster,
                            "L" when TryGetLingFengLastActorPlayer(
                                out PlayerObject buffActor) => buffActor,
                            "SELF" => player,
                            _ => null
                        };
                        if (!int.TryParse(param[1], NumberStyles.None, CultureInfo.InvariantCulture,
                                out int clientBuffPackage) ||
                            !int.TryParse(param[2], NumberStyles.None, CultureInfo.InvariantCulture,
                                out int clientBuffIcon) ||
                            !byte.TryParse(param[3], NumberStyles.None, CultureInfo.InvariantCulture,
                                out byte clientBuffSlot) ||
                            !int.TryParse(param[4], NumberStyles.None, CultureInfo.InvariantCulture,
                                out int clientBuffSeconds) ||
                            !SendLingFengClientBuff(
                                player, clientBuffTarget, clientBuffPackage, clientBuffIcon,
                                clientBuffSlot, clientBuffSeconds, param[5]))
                            MessageQueue.Enqueue($"[TxtScripts] SETCLIENTBUFF 执行失败，页码：{Key}");
                        break;

                    case ActionType.LingFengCloseClientBuff:
                        // 翎风编号可超过旧 RemoveBuff 的 byte 型 BuffType（酷明使用 310）。
                        // E1 保留展开后的完整编号且不误删服务端 Buff；宽编号客户端协议留 E2 闭环。
                        if (Server.Scripting.ScriptTrace.IsEnabled(player))
                            Server.Scripting.ScriptTrace.Record(
                                player, $"[TXT] CLOSECLIENTBUFF {param[0]} -> 等待宽编号客户端契约");
                        break;

                    case ActionType.LingFengSetJewelryCasket:
                    case ActionType.LingFengActivateJewelryCasket:
                        // 首饰盒激活/彩灰状态依赖专用人物界面与 30..35 装备槽协议。
                        // E1 只保留命令，不凭普通装备栏伪造首饰盒状态。
                        if (Server.Scripting.ScriptTrace.IsEnabled(player))
                            Server.Scripting.ScriptTrace.Record(
                                player, $"[TXT] {act.Type} -> 等待首饰盒客户端与领域契约");
                        break;

                    case ActionType.LingFengSetUpgradeItemContext:
                        // 该命令为翎风自定义 OK 框建立后续物品操作上下文。
                        // E1 保留位置，但没有受托管框会话时不缓存不可信物品引用。
                        if (Server.Scripting.ScriptTrace.IsEnabled(player))
                            Server.Scripting.ScriptTrace.Record(
                                player, $"[TXT] SETUPGRADEITEM {param[0]} -> 等待自定义OK框会话契约");
                        break;

                    case ActionType.LingFengOpenItemBoxEx:
                        // 旧拆解框会把物品名/持久写 S0/N0 并触发 @GetBoxItemX；
                        // 缺少完整客户端交互事务时仅保留参数，不伪造放入或回收结果。
                        if (Server.Scripting.ScriptTrace.IsEnabled(player))
                            Server.Scripting.ScriptTrace.Record(
                                player, $"[TXT] OPENITEMBOXEX {param[0]} -> 等待旧物品框客户端契约");
                        break;

                    case ActionType.LingFengChangeItemName:
                        // 当前 UserItem 没有实例级自定义名称的持久字段及协议字段。
                        // E1 保留展开后的名称，不修改共享 ItemInfo，避免给同模板全部物品改名。
                        if (Server.Scripting.ScriptTrace.IsEnabled(player))
                            Server.Scripting.ScriptTrace.Record(
                                player, $"[TXT] CHANGEITEMNAME {param[0]} -> 等待实例名称领域契约");
                        break;

                    case ActionType.LingFengSetBodyColor:
                        // 人体染色不是名称颜色；旧 ColourChanged 包只携带 NameColour。
                        // E1 不错误复用名称颜色，专用人体染色协议由 E2 门禁负责。
                        if (Server.Scripting.ScriptTrace.IsEnabled(player))
                            Server.Scripting.ScriptTrace.Record(
                                player, "[TXT] SETBODYCOLOR -> 等待人体染色客户端契约");
                        break;

                    case ActionType.LingFengDeferredCompatibilityCommand:
                        // 这些命令已完成 E1 语法与参数保留，但依赖尚未接入的客户端界面
                        // 或外部事务上下文。显式记录等待项，不写入替代状态来伪造成功。
                        if (Server.Scripting.ScriptTrace.IsEnabled(player))
                            Server.Scripting.ScriptTrace.Record(
                                player, $"[TXT] {param[0]} -> 等待 E2 客户端或领域契约");
                        break;

                    case ActionType.LingFengRejectBoxItem:
                    case ActionType.LingFengReturnBoxItem:
                        // 没有本次自定义 OK 框托管会话时，无法证明框内物品身份。
                        // 终止动作段，避免未拒绝/未退回物品却继续升级或发奖。
                        if (Server.Scripting.ScriptTrace.IsEnabled(player))
                            Server.Scripting.ScriptTrace.Record(
                                player, $"[TXT] {act.Type} -> 缺少自定义OK框托管契约，动作段失败关闭");
                        return;

                    case ActionType.LingFengSetArrBuff:
                        // 自动排列 Buff 按钮依赖翎风专用客户端布局与点击/到时回调协议。
                        // E1 仅保证命令可解析且不会错误修改服务端 Buff；E2 由客户端契约门禁阻断。
                        if (Server.Scripting.ScriptTrace.IsEnabled(player))
                            Server.Scripting.ScriptTrace.Record(
                                player,
                                $"[TXT] {(param[0] == "TARGET" ? "<$CURRRTARGETNAME>." : string.Empty)}SETARRBUFF -> 等待客户端自动排列 Buff 契约");
                        break;

                    case ActionType.LingFengAddButton:
                        // 自定义按钮需要翎风客户端绘制、点击回调和资源包契约。
                        // E1 保留完整动态参数，不把按钮伪造成 NPC 文本或服务端状态。
                        if (Server.Scripting.ScriptTrace.IsEnabled(player))
                            Server.Scripting.ScriptTrace.Record(
                                player, "[TXT] ADDBUTTON -> 等待客户端自定义按钮契约");
                        break;

                    case ActionType.LingFengCloseArrBuff:
                        // 与 SETARRBUFF 共用翎风专用客户端布局及点击/到时回调协议。
                        // E1 保留运行时展开后的按钮序号且不错误清除服务端 Buff。
                        if (Server.Scripting.ScriptTrace.IsEnabled(player))
                            Server.Scripting.ScriptTrace.Record(
                                player, $"[TXT] CLOSEARRBUFF {param[0]} -> 等待客户端自动排列 Buff 契约");
                        break;

                    case ActionType.LingFengScatterMonsterItems:
                        MonsterInfo scatterInfo = Envir.GetMonsterInfo(param[0]);
                        Map scatterMap = player.CurrentMap;
                        Point scatterLocation = player.CurrentLocation;
                        if (param.Count == 4)
                        {
                            scatterMap = Envir.GetMapByNameAndInstance(param[1]);
                            if (!int.TryParse(param[2], NumberStyles.Integer, CultureInfo.InvariantCulture,
                                    out int scatterX) ||
                                !int.TryParse(param[3], NumberStyles.Integer, CultureInfo.InvariantCulture,
                                    out int scatterY))
                            {
                                scatterMap = null;
                            }
                            else
                            {
                                scatterLocation = new Point(scatterX, scatterY);
                            }
                        }
                        if (scatterInfo == null || scatterMap == null ||
                            !scatterMap.ValidPoint(scatterLocation))
                        {
                            MessageQueue.Enqueue(
                                $"[TxtScripts] SCATTERMONITEMS 执行失败，页码：{Key}");
                            break;
                        }
                        MonsterObject scatterSource = MonsterObject.GetMonster(scatterInfo);
                        if (scatterSource == null ||
                            !scatterSource.ScatterLingFengDrops(player, scatterMap, scatterLocation))
                            MessageQueue.Enqueue(
                                $"[TxtScripts] SCATTERMONITEMS 执行失败，页码：{Key}");
                        break;

                    case ActionType.LingFengForceMonsterDropItems:
                        if (!TryForceLingFengMonsterDropItems(player, param))
                            MessageQueue.Enqueue(
                                $"[TxtScripts] MONDROPITEMSEX 执行失败，页码：{Key}");
                        break;

                    case ActionType.LingFengAddArrButton:
                    case ActionType.LingFengDeleteArrButton:
                        // 自动排列按钮依赖专用客户端布局、资源及点击回调协议。
                        // E1 完整保留参数但不伪造一个无法点击的普通服务端按钮。
                        if (Server.Scripting.ScriptTrace.IsEnabled(player))
                            Server.Scripting.ScriptTrace.Record(
                                player, $"[TXT] {acts[i].Type} -> 等待客户端自动排列按钮契约");
                        break;

                    case ActionType.LingFengDeleteBoxItem:
                        // 当前客户端/服务器尚无自定义 OK 框托管会话，无法证明框内物品身份。
                        // 必须终止整个动作段，防止未扣除材料却继续升级或发奖。
                        if (Server.Scripting.ScriptTrace.IsEnabled(player))
                            Server.Scripting.ScriptTrace.Record(
                                player, "[TXT] DELBOXITEM -> 缺少自定义OK框托管契约，动作段失败关闭");
                        return;

                    case ActionType.LingFengOpenStorageView:
                        if (param[0] != "0")
                        {
                            if (Server.Scripting.ScriptTrace.IsEnabled(player))
                                Server.Scripting.ScriptTrace.Record(
                                    player, "[TXT] OPENSTORAGEVIEW 1 -> 缺少无限仓库持久层，动作段失败关闭");
                            return;
                        }
                        player.SendStorage();
                        player.Enqueue(new S.NPCStorage());
                        break;

                    case ActionType.LingFengPlayEffect:
                        MapObject playEffectTarget = param[0] switch
                        {
                            "SELF" => player,
                            "M" when TryGetLingFengCurrentTargetMonster(player,
                                out MonsterObject effectMonster) => effectMonster,
                            "PET" => player.Pets.FirstOrDefault(value => value != null && !value.Dead) ??
                                     player.Hero?.Pets?.FirstOrDefault(value => value != null && !value.Dead),
                            _ => null
                        };
                        if (!TryDispatchLingFengObjectEffect(playEffectTarget, param.Skip(1).ToArray()))
                            MessageQueue.Enqueue($"[TxtScripts] PLAYEFFECT 执行失败，页码：{Key}");
                        break;

                    case ActionType.LingFengSetIcon:
                        // 顶戴图标依赖翎风客户端资源、角色附着渲染和同步协议。
                        // E1 只保留完整参数且不伪装成服务端 Buff；E2 由客户端契约门禁阻断。
                        if (Server.Scripting.ScriptTrace.IsEnabled(player))
                            Server.Scripting.ScriptTrace.Record(
                                player, "[TXT] SETICON -> 等待客户端顶戴图标契约");
                        break;

                    case ActionType.LingFengChangeSlaveAbility:
                    case ActionType.LingFengRecalcSlaveAbility:
                        // 原命令是一组“暂存绝对属性 -> 统一重算”的宝宝批处理事务。
                        // 在专用领域适配器完成前不套用单属性临时层，避免产生半组属性或计时串扰。
                        if (Server.Scripting.ScriptTrace.IsEnabled(player))
                            Server.Scripting.ScriptTrace.Record(
                                player, $"[TXT] {acts[i].Type} -> 等待宝宝属性批处理适配器");
                        break;

                    case ActionType.LingFengScreenEffect:
                        if (param.Count != 11 || param.Skip(1).Any(value =>
                                !int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)))
                        {
                            MessageQueue.Enqueue($"[TxtScripts] SCREENEFFECT 执行失败，页码：{Key}");
                            break;
                        }
                        int[] effect = param.Skip(1)
                            .Select(value => int.Parse(value, CultureInfo.InvariantCulture)).ToArray();
                        player.Enqueue(new S.LingFengScreenEffect
                        {
                            Stop = param[0] == "1",
                            X = effect[0],
                            Y = effect[1],
                            IconPackage = effect[2],
                            StartIndex = effect[3],
                            FrameCount = effect[4],
                            LoopCount = effect[5],
                            FrameDelay = effect[6],
                            BlendMode = effect[7],
                            Reserved = effect[8],
                            Layer = effect[9]
                        });
                        break;

                    case ActionType.LingFengMapEffect:
                        if (!TryDispatchLingFengMapEffect(param))
                            MessageQueue.Enqueue($"[TxtScripts] MAPEFFECT 执行失败，页码：{Key}");
                        break;

                    case ActionType.LingFengDialog:
                        if (param.Count > 0 && param[0] == "NPC_STYLE")
                        {
                            if (!TryCreateLingFengNpcDialogPacket(param, out S.LingFengDialog style))
                                MessageQueue.Enqueue(
                                    $"[TxtScripts] OPENMERCHANTBIGDLG 参数或资源无效，页码：{Key}");
                            else
                                player.Enqueue(style);
                            break;
                        }
                        if (param.Count < 2 || !int.TryParse(param[1], NumberStyles.None,
                                CultureInfo.InvariantCulture, out int dialogId) || dialogId is < 1 or > 50)
                        {
                            MessageQueue.Enqueue($"[TxtScripts] ADDDLGEX/DELDLG 编号无效，页码：{Key}");
                            break;
                        }
                        if (param[0] == "REMOVE")
                        {
                            player.Enqueue(new S.LingFengDialog { Remove = true, DialogId = dialogId });
                            break;
                        }
                        if (param.Count != 10 ||
                            !int.TryParse(param[2], NumberStyles.None, CultureInfo.InvariantCulture,
                                out int dialogPackage) || dialogPackage < 0 ||
                            !int.TryParse(param[3], NumberStyles.None, CultureInfo.InvariantCulture,
                                out int dialogImage) || dialogImage < 0 ||
                            param[4] is not ("0" or "1") ||
                            !TryParseLingFengPair(param[5], out int dialogX, out int dialogY) ||
                            !TryParseLingFengPair(param[6], out int dialogOffsetX, out int dialogOffsetY) ||
                            !int.TryParse(param[7], NumberStyles.None, CultureInfo.InvariantCulture,
                                out int dialogPosition) || dialogPosition is < 0 or > 50 ||
                            string.IsNullOrWhiteSpace(param[8]) || param[8].Length > 260 ||
                            param[9] is not ("0" or "1"))
                        {
                            MessageQueue.Enqueue($"[TxtScripts] ADDDLGEX 参数无效，页码：{Key}");
                            break;
                        }
                        player.Enqueue(new S.LingFengDialog
                        {
                            DialogId = dialogId,
                            IconPackage = dialogPackage,
                            ImageIndex = dialogImage,
                            Movable = param[4] == "1",
                            X = dialogX,
                            Y = dialogY,
                            OffsetX = dialogOffsetX,
                            OffsetY = dialogOffsetY,
                            Position = dialogPosition,
                            ExternalTextFile = param[8],
                            AbsolutePath = param[9] == "1"
                        });
                        break;

                    case ActionType.LingFengGetDbItemField:
                        ItemInfo dbItem = Envir.ItemInfoList.FirstOrDefault(info =>
                            info.Name.Equals(param[0], StringComparison.OrdinalIgnoreCase));
                        if (dbItem == null ||
                            !TryGetLingFengItemField(dbItem, param[1], out long itemFieldValue) ||
                            !TryStoreScriptValue(player, param[2], itemFieldValue))
                            MessageQueue.Enqueue(
                                $"[TxtScripts] GETDBITEMFIELDVALUE 物品或字段无效，页码：{Key}");
                        break;

                    case ActionType.LingFengGetDbItemFieldByIndex:
                        if (!int.TryParse(param[0], NumberStyles.None,
                                CultureInfo.InvariantCulture, out int indexedItemId) ||
                            Envir.GetItemInfo(indexedItemId) is not ItemInfo indexedItem ||
                            !TryStoreScriptTextValue(
                                player, param[2], indexedItem.FriendlyName))
                            MessageQueue.Enqueue(
                                $"[TxtScripts] GETDBIDXITEMFIELDVALUE 物品或字段无效，页码：{Key}");
                        break;

                    case ActionType.LingFengGetBagInfo:
                        {
                            if (!TryParseLingFengItemTypes(param[2], out HashSet<ItemType> itemTypes))
                            {
                                MessageQueue.Enqueue(
                                    $"[TxtScripts] GETBAGINFO 的 StdMode 列表无效，页码：{Key}");
                                break;
                            }

                            UserItem[] bagItems = player.Info.Inventory
                                .Where(item => item?.Info != null &&
                                    (itemTypes.Count == 0 || itemTypes.Contains(item.Info.Type)))
                                .ToArray();
                            if (param[0] == "ITEMCOUNT")
                            {
                                if (!TryStoreScriptValue(player, param[1], bagItems.Length))
                                    MessageQueue.Enqueue(
                                        $"[TxtScripts] GETBAGINFO 数量写入失败，页码：{Key}");
                                break;
                            }

                            IEnumerable<string> values = param[0] switch
                            {
                                "ITEMMAKEINDEX" => bagItems.Select(item =>
                                    item.UniqueID.ToString(CultureInfo.InvariantCulture)),
                                "ITEMIDX" => bagItems.Select(item =>
                                    item.ItemIndex.ToString(CultureInfo.InvariantCulture)),
                                "ITEMNAME" => bagItems.Select(item => item.Info.FriendlyName),
                                _ => Array.Empty<string>()
                            };
                            if (!TryStoreScriptListValue(player, param[1], values))
                                MessageQueue.Enqueue(
                                    $"[TxtScripts] GETBAGINFO 列表写入失败，页码：{Key}");
                        }
                        break;

                    case ActionType.LingFengGetItemField:
                        if (!TryGetLingFengItemAtPosition(player, param[0], out UserItem instanceItem,
                                out int instancePosition) ||
                            !TryGetLingFengItemInstanceField(
                                instanceItem, instancePosition, param[1], out string instanceFieldValue) ||
                            !TryStoreScriptTextValue(player, param[2], instanceFieldValue))
                            MessageQueue.Enqueue(
                                $"[TxtScripts] GETITEMFIELDVALUE 物品或字段无效，页码：{Key}");
                        break;

                    case ActionType.LingFengGetBagItemCount:
                        bool fullDurabilityOnly = param[3] == "1";
                        long bagItemCount = player.Info.Inventory
                            .Where(item => item != null &&
                                item.Info.Name.Equals(param[0], StringComparison.OrdinalIgnoreCase) &&
                                (!fullDurabilityOnly || item.CurrentDura == item.MaxDura))
                            .Sum(item => (long)item.Count);
                        if (!TryStoreScriptValue(player, param[1], bagItemCount))
                            MessageQueue.Enqueue(
                                $"[TxtScripts] GETBAGITEMCOUNT 结果写入失败，页码：{Key}");
                        break;

                    case ActionType.LingFengGetMapMonsterCount:
                        if (!TryCountLingFengMapMonsters(
                                param[0], param[1] == "1", out int mapMonsterCount) ||
                            !TryStoreScriptValue(player, param[2], mapMonsterCount))
                            MessageQueue.Enqueue(
                                $"[TxtScripts] GETMAPMONCOUNT 地图或结果变量无效，页码：{Key}");
                        break;

                    case ActionType.VariableMutate:
                        {
                            if (player.NPCObjectID == 0) return;
                            var context = ScriptVariableContext.ForConversation(player, player.NPCObjectID, player.CurrentMap);
                            ScriptVariableMutationResult result = Envir.CSharpScripts.VariableCommands.Mutate(
                                context, param[0], param[1], param[2]);
                            if (!result.Success)
                                MessageQueue.Enqueue($"[Variables][TXT] {param[1]} 失败：{result.ErrorCode} {result.Diagnostic}，页码：{Key}");
                        }
                        break;

                    case ActionType.LingFengOpenStoragePage:
                        {
                            if (player.Account == null ||
                                !int.TryParse(param[0], NumberStyles.None,
                                    CultureInfo.InvariantCulture, out int storagePage) ||
                                !player.Account.TryOpenLingFengStorage(storagePage))
                            {
                                MessageQueue.Enqueue(
                                    $"[TxtScripts] OPENSTORATGE 仓库页无效，页码：{Key}");
                                break;
                            }
                            player.Enqueue(new S.ResizeStorage
                            {
                                Size = player.Account.Storage.Length,
                                HasExpandedStorage = true,
                                ExpiryTime = player.Account.ExpandedStorageExpiryDate
                            });
                        }
                        break;

                    case ActionType.LingFengTargetVariableMutate:
                        {
                            if (!TryGetLingFengCurrentTarget(
                                    player, out MapObject variableTarget))
                                break;
                            ScriptVariableMutationResult result =
                                Envir.CSharpScripts.VariableCommands.Mutate(
                                    ScriptVariableContext.ForPlayer(
                                        variableTarget, variableTarget.CurrentMap),
                                    param[0], param[1], param[2]);
                            if (!result.Success)
                                MessageQueue.Enqueue(
                                    $"[Variables][TXT] M.{param[1]} 失败：{result.ErrorCode} {result.Diagnostic}，页码：{Key}");
                        }
                        break;

                    case ActionType.VariableInitialize:
                        {
                            if (player.NPCObjectID == 0) return;
                            var context = ScriptVariableContext.ForConversation(player, player.NPCObjectID, player.CurrentMap);
                            ScriptVariableMutationResult result = Envir.CSharpScripts.VariableCommands.Initialize(
                                context, param[0]);
                            if (!result.Success)
                                MessageQueue.Enqueue($"[Variables][TXT] INITVAR 失败：{result.ErrorCode} {result.Diagnostic}，页码：{Key}");
                        }
                        break;

                    case ActionType.VariableConvert:
                        {
                            if (player.NPCObjectID == 0) return;
                            var context = ScriptVariableContext.ForConversation(player, player.NPCObjectID, player.CurrentMap);
                            ScriptVariableMutationResult result = Envir.CSharpScripts.VariableCommands.Convert(
                                context, param[0], param[1], param[2]);
                            if (!result.Success)
                                MessageQueue.Enqueue($"[Variables][TXT] {param[1]} 失败：{result.ErrorCode} {result.Diagnostic}，页码：{Key}");
                        }
                        break;

                    case ActionType.VariableFormulation:
                        {
                            if (player.NPCObjectID == 0) return;
                            var context = ScriptVariableContext.ForConversation(player, player.NPCObjectID, player.CurrentMap);
                            ScriptVariableMutationResult result = Envir.CSharpScripts.VariableCommands.Formulate(
                                context, param[0], param[1], Envir.Random.Next);
                            if (!result.Success)
                                MessageQueue.Enqueue($"[Variables][TXT] FORMULATION 失败：{result.ErrorCode} {result.Diagnostic}，页码：{Key}");
                        }
                        break;

                    case ActionType.VariableComposite:
                        {
                            if (player.NPCObjectID == 0) return;
                            var context = ScriptVariableContext.ForConversation(player, player.NPCObjectID, player.CurrentMap);
                            ScriptVariableMutationResult result = ExecuteCompositeAction(context, param);
                            if (!result.Success)
                                MessageQueue.Enqueue($"[Variables][TXT] {param[0]} 失败：{result.ErrorCode} {result.Diagnostic}，页码：{Key}");
                        }
                        break;

                    case ActionType.VariableSetCurrentTarget:
                        {
                            if (string.IsNullOrWhiteSpace(param[0]))
                            {
                                player.ScriptVariableCurrentTarget = null;
                                break;
                            }
                            PlayerObject target = Envir.GetPlayer(ResolveOwnVariableOperand(player, param[0]));
                            player.ScriptVariableCurrentTarget = target != null &&
                                target.CurrentMap == player.CurrentMap &&
                                Functions.InRange(player.CurrentLocation, target.CurrentLocation, 20)
                                    ? target
                                    : null;
                            if (player.ScriptVariableCurrentTarget == null)
                                MessageQueue.Enqueue($"[Variables][TXT] SETCURRTARGET 失败：目标离线、不在同图或距离超过 20 格，页码：{Key}");
                        }
                        break;

                    case ActionType.VariableSetHuman:
                        {
                            string targetName = ResolveOwnVariableOperand(player, param[0]);
                            PlayerObject target = Envir.GetPlayer(targetName);
                            if (target == null)
                            {
                                MessageQueue.Enqueue($"[Variables][TXT] SETHUMVAR 失败：TargetOffline 目标不在线，页码：{Key}");
                                break;
                            }
                            ScriptVariableMutationResult result = Envir.CSharpScripts.VariableCommands.Mutate(
                                ScriptVariableContext.ForPlayer(target, target.CurrentMap),
                                param[1], "MOV", ResolveOwnVariableOperand(player, param[2]));
                            if (!result.Success)
                                MessageQueue.Enqueue($"[Variables][TXT] SETHUMVAR 失败：{result.ErrorCode} {result.Diagnostic}，页码：{Key}");
                        }
                        break;

                    case ActionType.VariableGetHuman:
                        {
                            string targetName = ResolveOwnVariableOperand(player, param[0]);
                            PlayerObject target = Envir.GetPlayer(targetName);
                            if (target == null)
                            {
                                MessageQueue.Enqueue($"[Variables][TXT] GETHUMVAR 失败：TargetOffline 目标不在线，页码：{Key}");
                                break;
                            }
                            if (!ScriptVariableReferenceParser.TryParse(param[1], out var sourceReference))
                            {
                                MessageQueue.Enqueue($"[Variables][TXT] GETHUMVAR 失败：UnknownReference，页码：{Key}");
                                break;
                            }
                            ScriptVariableReadResult source = Envir.CSharpScripts.VariableModule.Read(
                                ScriptVariableContext.ForPlayer(target, target.CurrentMap), sourceReference);
                            if (!source.Success)
                            {
                                MessageQueue.Enqueue($"[Variables][TXT] GETHUMVAR 失败：{source.ErrorCode} {source.Diagnostic}，页码：{Key}");
                                break;
                            }
                            ScriptVariableMutationResult result = Envir.CSharpScripts.VariableCommands.Mutate(
                                ScriptVariableContext.ForConversation(player, player.NPCObjectID, player.CurrentMap),
                                param[2], "MOV", source.Value.Format());
                            if (!result.Success)
                                MessageQueue.Enqueue($"[Variables][TXT] GETHUMVAR 失败：{result.ErrorCode} {result.Diagnostic}，页码：{Key}");
                        }
                        break;

                    case ActionType.GiveBuff:
                        {
                            if (!Enum.IsDefined(typeof(BuffType), param[0]))
                            {
                                return;
                            }

                            int.TryParse(param[1], out int duration);
                            bool.TryParse(param[2], out bool infinite);
                            bool.TryParse(param[3], out bool visible);
                            bool.TryParse(param[4], out bool stackable);

                            IReadOnlyList<string> extraStatPairs = Array.Empty<string>();

                            if (param.Count > 5)
                            {
                                var list = new List<string>(param.Count - 5);
                                for (int j = 5; j < param.Count; j++)
                                {
                                    list.Add(param[j]);
                                }

                                extraStatPairs = list;
                            }

                            if (!Envir.TryBuildBuffStatsFromSetBuffs(param[0], extraStatPairs, out var buffStats))
                                break;

                            {
                                player.AddBuff((BuffType)(byte)Enum.Parse(typeof(BuffType), param[0], true), player, Settings.Second * duration, buffStats, visible);

                                if (Server.Scripting.ScriptTrace.IsEnabled(player))
                                {
                                    Server.Scripting.ScriptTrace.Record(player, $"[TXT] ADDBUFF {param[0]} {duration}s");
                                }
                            }
                        }
                        break;

                    case ActionType.RemoveBuff:
                        {
                            if (!Enum.IsDefined(typeof(BuffType), param[0])) return;

                            BuffType bType = (BuffType)(byte)Enum.Parse(typeof(BuffType), param[0]);

                            player.RemoveBuff(bType);

                            if (Server.Scripting.ScriptTrace.IsEnabled(player))
                            {
                                Server.Scripting.ScriptTrace.Record(player, $"[TXT] REMOVEBUFF {bType}");
                            }
                        }
                        break;

                    case ActionType.AddToGuild:
                        {
                            if (player.MyGuild != null) return;

                            GuildObject guild = Envir.GetGuild(param[0]);

                            if (guild == null) return;

                            player.PendingGuildInvite = guild;
                            player.GuildInvite(true);
                        }
                        break;

                    case ActionType.RemoveFromGuild:
                        {
                            if (player.MyGuild == null) return;

                            if (player.MyGuildRank == null) return;

                            if (player.MyGuild.Name == Settings.NewbieGuild) player.RemoveBuff(BuffType.新人特效);
                            if (player.HasBuff(BuffType.公会特效)) player.RemoveBuff(BuffType.公会特效);

                            player.MyGuild.DeleteMember(player, player.Name);
                        }
                        break;

                    case ActionType.RefreshEffects:
                        {
                            player.SetLevelEffects();
                            var p = new S.ObjectLevelEffects { ObjectID = player.ObjectID, LevelEffects = player.LevelEffects };
                            player.Enqueue(p);
                            player.Broadcast(p);
                        }
                        break;

                    case ActionType.CanGainExp:
                        {
                            bool.TryParse(param[0], out bool tempBool);
                            player.CanGainExp = tempBool;
                        }
                        break;

                    case ActionType.ComposeMail:
                        {
                            mailInfo = new MailInfo(player.Info.Index, false)
                            {
                                Sender = param[1],
                                Message = param[0]
                            };
                        }
                        break;
                    case ActionType.AddMailGold:
                        {
                            if (mailInfo == null) return;

                            uint.TryParse(param[0], out uint tempUint);

                            mailInfo.Gold += tempUint;
                        }
                        break;

                    case ActionType.AddMailItem:
                        {
                            if (mailInfo == null) return;
                            if (mailInfo.Items.Count > 5) return;

                            if (param.Count < 2 || !ushort.TryParse(param[1], out ushort count)) count = 1;

                            var info = Envir.GetItemInfo(param[0]);

                            if (info == null)
                            {
                                MessageQueue.Enqueue(string.Format("使用ADDMAILITEM命令无法获取物品信息: {0}, 页码: {1}", param[0], Key));
                                break;
                            }

                            while (count > 0 && mailInfo.Items.Count < 5)
                            {
                                UserItem item = Envir.CreateFreshItem(info);

                                if (item == null)
                                {
                                    MessageQueue.Enqueue(string.Format("使用ADDMAILITEM命令无法创建用户物品: {0}, 页码: {1}", param[0], Key));
                                    return;
                                }

                                if (item.Info.StackSize > count)
                                {
                                    item.Count = count;
                                    count = 0;
                                }
                                else
                                {
                                    count -= item.Info.StackSize;
                                    item.Count = item.Info.StackSize;
                                }

                                mailInfo.Items.Add(item);
                            }
                        }
                        break;

                    case ActionType.SendMail:
                        {
                            if (mailInfo == null) return;

                            mailInfo.Send();
                        }
                        break;

                    case ActionType.GroupGoto:
                        {
                            if (NPCScript.BlockSystemNavigation(nameof(ActionType.GroupGoto))) break;
                            if (player.GroupMembers == null) return;

                            for (int j = 0; j < player.GroupMembers.Count(); j++)
                            {
                                var action = new DelayedAction(DelayedType.NPC, Envir.Time, player.NPCObjectID, player.NPCScriptID, "[" + param[0] + "]");
                                player.GroupMembers[j].ActionList.Add(action);
                            }
                        }
                        break;

                    case ActionType.EnterMap:
                        {
                            if (!player.NPCData.TryGetValue("NPCMoveMap", out object _npcMoveMap) || !player.NPCData.TryGetValue("NPCMoveCoord", out object _npcMoveCoord)) return;

                            player.Teleport((Map)_npcMoveMap, (Point)_npcMoveCoord, false);

                            player.NPCData.Remove("NPCMoveMap");
                            player.NPCData.Remove("NPCMoveCoord");
                        }
                        break;

                    case ActionType.MakeWeddingRing:
                        {
                            player.MakeWeddingRing();
                        }
                        break;

                    case ActionType.ForceDivorce:
                        {
                            player.NPCDivorce();
                        }
                        break;

                    case ActionType.LingFengTargetFormulation:
                        {
                            if (player.NPCObjectID == 0) return;
                            if (!TryGetLingFengCurrentTargetPlayer(
                                    player, out PlayerObject formulationTarget))
                            {
                                MessageQueue.Enqueue(
                                    $"[Variables][TXT] 当前目标 FORMULATION 缺少有效人物目标，页码：{Key}");
                                break;
                            }
                            string expression = ReplaceValue(
                                formulationTarget, FindVariable(player, act.Params[0]));
                            string destination = FindVariable(player, act.Params[1]);
                            var context = ScriptVariableContext.ForConversation(
                                player, player.NPCObjectID, player.CurrentMap);
                            ScriptVariableMutationResult result =
                                Envir.CSharpScripts.VariableCommands.Formulate(
                                    context, expression, destination, Envir.Random.Next,
                                    truncateIntegerResult: true);
                            if (!result.Success)
                                MessageQueue.Enqueue(
                                    $"[Variables][TXT] 当前目标 FORMULATION 失败：{result.ErrorCode} {result.Diagnostic}，页码：{Key}");
                        }
                        break;

                    case ActionType.LingFengMarriage:
                        if (param.Count == 0 ||
                            param[0].Equals("REQUESTMARRY", StringComparison.OrdinalIgnoreCase))
                        {
                            player.MarriageRequest();
                        }
                        else if (param[0].Equals(
                                     "RESPONSEMARRY", StringComparison.OrdinalIgnoreCase))
                        {
                            bool accept = param.Count >= 2 &&
                                          param[1].Equals("OK", StringComparison.OrdinalIgnoreCase);
                            player.MarriageReply(accept);
                        }
                        break;

                    case ActionType.LingFengDivorce:
                        if (param.Count == 0)
                        {
                            player.DivorceRequest();
                        }
                        else if (param[0].Equals(
                                     "REQUESTUNMARRY", StringComparison.OrdinalIgnoreCase))
                        {
                            if (param.Count >= 2 &&
                                param[1].Equals("FORCE", StringComparison.OrdinalIgnoreCase))
                                player.NPCDivorce();
                            else
                                player.DivorceRequest();
                        }
                        else if (param[0].Equals(
                                     "RESPONSEUNMARRY", StringComparison.OrdinalIgnoreCase))
                        {
                            player.DivorceReply(true);
                        }
                        break;

                    case ActionType.LingFengMentorship:
                        if (param.Count == 0 ||
                            param[0].Equals("REQUESTMASTER", StringComparison.OrdinalIgnoreCase))
                        {
                            if (TryGetLingFengFacingPlayer(player, out PlayerObject mentor))
                                player.AddMentor(mentor.Name);
                            else
                                MessageQueue.Enqueue(
                                    $"[TxtScripts] MASTER 正前方没有有效师傅对象，页码：{Key}");
                        }
                        else if (param[0].Equals(
                                     "RESPONSEMASTER", StringComparison.OrdinalIgnoreCase))
                        {
                            bool accept = param.Count >= 2 &&
                                          param[1].Equals("OK", StringComparison.OrdinalIgnoreCase);
                            player.MentorReply(accept);
                        }
                        break;

                    case ActionType.LingFengEndMentorship:
                        bool forceMentorBreak = param.Count >= 2 &&
                                                param[0].Equals(
                                                    "REQUESTUNMASTER",
                                                    StringComparison.OrdinalIgnoreCase) &&
                                                param[1].Equals(
                                                    "FORCE", StringComparison.OrdinalIgnoreCase);
                        player.MentorBreak(forceMentorBreak);
                        break;

                    case ActionType.LoadValue:
                        {
                            string val = param[0];
                            string filePath = param[1];
                            string header = param[2];
                            string key = param[3];

                            System.Diagnostics.Stopwatch sw = null;
                            if (Settings.TxtScriptsLogLoads)
                                sw = System.Diagnostics.Stopwatch.StartNew();

                            string loadedString = Envir.LoadValueFromFilePath(filePath, header, key, "");

                            if (sw != null)
                            {
                                sw.Stop();
                                MessageQueue.Enqueue($"[TxtScripts] 读取 {filePath}（{sw.ElapsedMilliseconds}ms）来源=Values:LoadValue");
                            }

                            if (loadedString == "") break;
                            AddVariable(player, val, loadedString);
                        }
                        break;

                    case ActionType.SaveValue:
                        {
                            string filePath = param[0];
                            string header = param[1];
                            string key = param[2];
                            string val = param[3];

                            Envir.SaveValueFromFilePath(filePath, header, key, val);
                        }
                        break;
                    case ActionType.ConquestGuard:
                        {
                            if (!int.TryParse(param[0], out int tempInt)) return;
                            var conquest = Envir.Conquests.FirstOrDefault(z => z.Info.Index == tempInt);
                            if (conquest == null) return;

                            if (!int.TryParse(param[1], out tempInt)) return;
                            ConquestGuildArcherInfo conquestArcher = conquest.ArcherList.FirstOrDefault(z => z.Index == tempInt);
                            if (conquestArcher == null) return;

                            if (conquestArcher.ArcherMonster != null)
                                if (!conquestArcher.ArcherMonster.Dead) return;

                            if (player.IsGM)
                            {
                                conquestArcher.Spawn(true);
                            }
                            else
                            {
                                if (player.MyGuild == null || player.MyGuild.Gold < conquestArcher.GetRepairCost()) return;

                                player.MyGuild.Gold -= conquestArcher.GetRepairCost();
                                player.MyGuild.SendServerPacket(new S.GuildStorageGoldChange() { Type = 2, Amount = conquestArcher.GetRepairCost() });

                                conquestArcher.Spawn(true);
                            }
                        }
                        break;
                    case ActionType.ConquestGate:
                        {
                            if (!int.TryParse(param[0], out int tempInt)) return;
                            var conquest = Envir.Conquests.FirstOrDefault(z => z.Info.Index == tempInt);
                            if (conquest == null) return;

                            if (!int.TryParse(param[1], out tempInt)) return;
                            ConquestGuildGateInfo conquestGate = conquest.GateList.FirstOrDefault(z => z.Index == tempInt);
                            if (conquestGate == null) return;

                            if (player.IsGM)
                            {
                                conquestGate.Repair();
                            }
                            else
                            {
                                if (player.MyGuild == null || player.MyGuild.Gold < conquestGate.GetRepairCost()) return;

                                player.MyGuild.Gold -= (uint)conquestGate.GetRepairCost();
                                player.MyGuild.SendServerPacket(new S.GuildStorageGoldChange() { Type = 2, Amount = (uint)conquestGate.GetRepairCost() });

                                conquestGate.Repair();
                            }
                        }
                        break;
                    case ActionType.ConquestWall:
                        {
                            if (!int.TryParse(param[0], out int tempInt)) return;
                            var conquest = Envir.Conquests.FirstOrDefault(z => z.Info.Index == tempInt);
                            if (conquest == null) return;

                            if (!int.TryParse(param[1], out tempInt)) return;
                            ConquestGuildWallInfo conquestWall = conquest.WallList.FirstOrDefault(z => z.Index == tempInt);

                            if (conquestWall == null) return;

                            if (player.IsGM)
                            {
                                conquestWall.Repair();
                            }
                            else
                            {
                                if (player.MyGuild == null || player.MyGuild.Gold < conquestWall.GetRepairCost()) return;

                                player.MyGuild.Gold -= (uint)conquestWall.GetRepairCost();
                                player.MyGuild.SendServerPacket(new S.GuildStorageGoldChange() { Type = 2, Amount = (uint)conquestWall.GetRepairCost() });

                                conquestWall.Repair();
                            }
                        }
                        break;
                    case ActionType.ConquestSiege:
                        {
                            if (!int.TryParse(param[0], out int tempInt)) return;
                            var conquest = Envir.Conquests.FirstOrDefault(z => z.Info.Index == tempInt);
                            if (conquest == null) return;

                            if (!int.TryParse(param[1], out tempInt)) return;
                            ConquestGuildSiegeInfo conquestSiege = conquest.SiegeList.FirstOrDefault(z => z.Index == tempInt);
                            if (conquestSiege == null) return;

                            if (conquestSiege.Gate != null)
                            {
                                if (!conquestSiege.Gate.Dead) return;
                            }

                            if (player.IsGM)
                            {
                                conquestSiege.Repair();
                            }
                            else
                            {
                                if (player.MyGuild == null || player.MyGuild.Gold < conquestSiege.GetRepairCost()) return;

                                player.MyGuild.Gold -= (uint)conquestSiege.GetRepairCost();
                                player.MyGuild.SendServerPacket(new S.GuildStorageGoldChange() { Type = 2, Amount = (uint)conquestSiege.GetRepairCost() });

                                conquestSiege.Repair();
                            }
                        }
                        break;
                    case ActionType.TakeConquestGold:
                        {
                            if (!int.TryParse(param[0], out int tempInt)) return;
                            var conquest = Envir.Conquests.FirstOrDefault(z => z.Info.Index == tempInt);
                            if (conquest == null) return;

                            if (player.MyGuild != null && player.MyGuild.Guildindex == conquest.GuildInfo.Owner)
                            {
                                player.MyGuild.Gold += conquest.GuildInfo.GoldStorage;
                                player.MyGuild.SendServerPacket(new S.GuildStorageGoldChange() { Type = 3, Amount = conquest.GuildInfo.GoldStorage });
                                conquest.GuildInfo.GoldStorage = 0;
                            }
                        }
                        break;
                    case ActionType.SetConquestRate:
                        {
                            if (!int.TryParse(param[0], out int tempInt)) return;
                            var conquest = Envir.Conquests.FirstOrDefault(z => z.Info.Index == tempInt);
                            if (conquest == null) return;

                            if (!byte.TryParse(param[1], out byte tempByte)) return;
                            if (player.MyGuild != null && player.MyGuild.Guildindex == conquest.GuildInfo.Owner)
                            {
                                conquest.GuildInfo.NPCRate = tempByte;
                            }
                        }
                        break;
                    case ActionType.StartConquest:
                        {
                            if (!int.TryParse(param[0], out int tempInt)) return;
                            var conquest = Envir.Conquests.FirstOrDefault(z => z.Info.Index == tempInt);
                            if (conquest == null) return;

                            if (!conquest.WarIsOn)
                            {
                                conquest.StartType = ConquestType.强制启动;
                                conquest.StartWar(conquest.GameType);

                                MessageQueue.Enqueue(string.Format("{0} 开始攻城战", conquest.Info.Name));

                            }
                            else
                            {
                                conquest.WarIsOn = false;

                                MessageQueue.Enqueue(string.Format("{0} 攻城结束", conquest.Info.Name));
                            }

                            foreach (var pl in Envir.Players)
                            {
                                if (conquest.WarIsOn)
                                {
                                    pl.ReceiveChat($"{conquest.Info.Name} 开始攻城战", ChatType.System);
                                }
                                else
                                {
                                    pl.ReceiveChat($"{conquest.Info.Name} 攻城战结束", ChatType.System);
                                }

                                pl.BroadcastInfo();
                            }

                        }
                        break;
                    case ActionType.ScheduleConquest:
                        {
                            if (!int.TryParse(param[0], out int tempInt)) return;
                            var conquest = Envir.Conquests.FirstOrDefault(z => z.Info.Index == tempInt);
                            if (conquest == null) return;

                            if (player.MyGuild != null && player.MyGuild.Guildindex != conquest.GuildInfo.Owner && !conquest.WarIsOn)
                            {
                                conquest.GuildInfo.AttackerID = player.MyGuild.Guildindex;
                            }
                        }
                        break;
                    case ActionType.OpenGate:
                        {
                            if (!int.TryParse(param[0], out int tempInt)) return;
                            var Conquest = Envir.Conquests.FirstOrDefault(z => z.Info.Index == tempInt);
                            if (Conquest == null) return;

                            if (!int.TryParse(param[1], out tempInt)) return;
                            ConquestGuildGateInfo OpenGate = Conquest.GateList.FirstOrDefault(z => z.Index == tempInt);
                            if (OpenGate == null) return;
                            if (OpenGate.Gate == null) return;
                            OpenGate.Gate.OpenDoor();
                        }
                        break;
                    case ActionType.CloseGate:
                        {
                            if (!int.TryParse(param[0], out int tempInt)) return;
                            var conquest = Envir.Conquests.FirstOrDefault(z => z.Info.Index == tempInt);
                            if (conquest == null) return;

                            if (!int.TryParse(param[1], out tempInt)) return;
                            ConquestGuildGateInfo CloseGate = conquest.GateList.FirstOrDefault(z => z.Index == tempInt);
                            if (CloseGate == null) return;
                            if (CloseGate.Gate == null) return;
                            CloseGate.Gate.CloseDoor();
                        }
                        break;
                    case ActionType.OpenBrowser:
                        {
                            bool killSwitchEnabled = Envir.KillSwitches?.IsEnabled(
                                Server.Operations.KillSwitchFeature.HighRiskOperations) == true;
                            if (!Server.Scripting.LingFengHighRiskCommandPolicy.CanOpenBrowser(
                                    param[0], Settings.TxtScriptsHighRiskCapabilitiesEnabled,
                                    Settings.TxtScriptsAllowedHttpsHosts, killSwitchEnabled,
                                    out Uri safeUri, out _)) return;
                            player.Enqueue(new S.OpenBrowser { Url = safeUri.AbsoluteUri });
                        }
                        break;
                    case ActionType.GetRandomText:
                        {
                            var key = $"NPCs/{param[0]}";
                            var definition = Envir.TextFileProvider?.GetByKey(key);

                            if (definition == null)
                            {
                                MessageQueue.Enqueue(string.Format("随机文本定义:{0} 不存在", key));
                                break;
                            }

                            var lines = definition.Lines;
                            if (lines == null || lines.Count == 0)
                            {
                                MessageQueue.Enqueue(string.Format("随机文本定义:{0} 为空", key));
                                break;
                            }

                            int index = Envir.Random.Next(0, lines.Count);
                            AddVariable(player, param[1], lines[index] ?? string.Empty);
                        }
                        break;
                    case ActionType.PlaySound:
                        {
                            if (!int.TryParse(param[0], out int soundID)) return;
                            player.Enqueue(new S.PlaySound { Sound = soundID });
                        }
                        break;

                    case ActionType.SetTimer:
                        {
                            if (!int.TryParse(param[1], out int seconds) || !byte.TryParse(param[2], out byte type)) return;

                            bool.TryParse(param[3], out bool global);

                            if (seconds < 0) seconds = 0;

                            if (global)
                            {
                                var timerKey = "_-" + param[0];

                                Envir.Timers[timerKey] = new Timer(timerKey, seconds, type);
                            }
                            else
                            {
                                player.SetTimer(param[0], seconds, type);
                            }
                        }
                        break;
                    case ActionType.ExpireTimer:
                        {
                            var globalTimerKey = "_-" + param[0];

                            if (Envir.Timers.ContainsKey(globalTimerKey))
                            {
                                Envir.Timers.Remove(globalTimerKey);
                            }

                            player.ExpireTimer(param[0]);
                        }
                        break;
                    case ActionType.UnequipItem:
                        {
                            var slot = param[0];

                            for (int e = 0; e < player.Info.Equipment.Length; e++)
                            {
                                var item = player.Info.Equipment[e];

                                if (item == null) continue;

                                var slotName = (EquipmentSlot)e;

                                if (!string.IsNullOrEmpty(slot) && slot.ToLower() != slotName.ToString().ToLower()) continue;

                                if (!player.CanRemoveItem(MirGridType.Inventory, item) || item.Cursed || item.WeddingRing != -1) continue;

                                for (int k = 0; k < player.Info.Inventory.Length; k++)
                                {
                                    var freeSlot = player.Info.Inventory[k];

                                    if (freeSlot != null) continue;

                                    player.Info.Equipment[e] = null;
                                    player.Info.Inventory[k] = item;

                                    player.Report.ItemMoved(item, MirGridType.Equipment, MirGridType.Inventory, e, k);

                                    break;
                                }
                            }

                            S.UserSlotsRefresh packet = new S.UserSlotsRefresh
                            {
                                Inventory = new UserItem[player.Info.Inventory.Length],
                                Equipment = new UserItem[player.Info.Equipment.Length],
                            };

                            player.Info.Inventory.CopyTo(packet.Inventory, 0);
                            player.Info.Equipment.CopyTo(packet.Equipment, 0);

                            player.Enqueue(packet);

                            player.RefreshStats();
                        }
                        break;
                    case ActionType.RollDie:
                        {
                            bool.TryParse(param[1], out bool autoRoll);

                            var result = Envir.Random.Next(1, 7);

                            S.Roll p = new S.Roll { Type = 0, Page = param[0], AutoRoll = autoRoll, Result = result };

                            player.NPCData["NPCRollResult"] = result;
                            player.Enqueue(p);
                        }
                        break;
                    case ActionType.RollYut:
                        {
                            bool.TryParse(param[1], out bool autoRoll);

                            var result = Envir.Random.Next(1, 7);

                            S.Roll p = new S.Roll { Type = 1, Page = param[0], AutoRoll = autoRoll, Result = result };

                            player.NPCData["NPCRollResult"] = result;
                            player.Enqueue(p);
                        }
                        break;
                    case ActionType.Drop:
                        {
                            var path = param[0];
                            var drops = new List<DropInfo>();
                            DropInfo.Load(drops, "NPC", path);

                            var dropTableKey = string.Empty;
                            if (Server.Scripting.DropTableKeyResolver.TryResolve(path, out var resolvedDropTableKey))
                                dropTableKey = resolvedDropTableKey;

                            var dropContext = new Server.Scripting.DropAttemptContext("npcdrop", player, null, dropTableKey);

                            var dropRateRequest = Server.Scripting.EconomyRateHooks.ResolveDropRate(player, "npcdrop");

                            if (dropRateRequest.Decision == Server.Scripting.ScriptHookDecision.Cancel &&
                                !string.IsNullOrEmpty(dropRateRequest.FailMessage))
                            {
                                player.ReceiveChat(dropRateRequest.FailMessage, ChatType.System);
                            }

                            var effectiveDropRate = dropRateRequest.Decision == Server.Scripting.ScriptHookDecision.Continue
                                ? dropRateRequest.Rate
                                : 0F;

                            foreach (var drop in drops)
                            {
                                var reward = drop.AttemptDrop(player?.Stats[Stat.物品掉落数率] ?? 0, player?.Stats[Stat.金币收益数率] ?? 0, effectiveDropRate, dropContext);

                                if (reward != null)
                                {
                                    if (reward.Gold > 0)
                                    {
                                        player.GainGold(reward.Gold);
                                    }

                                    foreach (var dropItem in reward.Items)
                                    {
                                        UserItem item = Envir.CreateDropItem(dropItem);

                                        if (item == null) continue;

                                        if (player != null && player.Race == ObjectType.Player)
                                        {
                                            PlayerObject ob = (PlayerObject)player;

                                            if (ob.CheckGroupQuestItem(item))
                                            {
                                                continue;
                                            }
                                        }

                                        if (drop.QuestRequired) continue;

                                         if (player.CanGainItem(item))
                                         {
                                             player.GainItem(item);
                                             Server.Scripting.RareDropAnnouncements.Notify(item, dropContext);
                                         }
                                     }
                                 }
                             }
                        }
                        break;
                    case ActionType.ReviveHero:
                        player.ReviveHero();
                        break;
                    case ActionType.SealHero:
                        player.SealHero();
                        break;
                    case ActionType.DeleteHero:
                        player.DeleteHero();
                        break;
                    case ActionType.ConquestRepairAll:
                        {
                            if (!player.IsGM)
                            {
                                player.ReceiveChat($"非游戏管理员，该命令无效", ChatType.System);
                                MessageQueue.Enqueue($"非管理员玩家: {player.Name} 调用了 @CONQUESTREPAIRALL 命令");
                                return;
                            }

                            if (!int.TryParse(param[0], out int tempInt)) return;
                            var conquest = Envir.Conquests.FirstOrDefault(z => z.Info.Index == tempInt);
                            if (conquest == null) return;

                            MessageQueue.Enqueue($"游戏管理员:{player.Name} 在账户目录为: {player.Info.AccountInfo.Index} 上调用了 @CONQUESTREPAIRALL 命令");
                            MessageQueue.Enqueue($"攻城战: {conquest.Info.Name}");

                            if (conquest.Guild != null)
                            {
                                MessageQueue.Enqueue($"城堡拥有者: {conquest.Guild.Name}");
                            }
                            else
                            {
                                MessageQueue.Enqueue($"城堡当前没有拥有者");
                            }

                            int _fixed = 0;
                            foreach (ConquestGuildArcherInfo archer in conquest.ArcherList)
                            {
                                if (archer.ArcherMonster != null &&
                                    archer.ArcherMonster.Dead)
                                {
                                    archer.Spawn(true);
                                    _fixed++;
                                }
                            }
                            player.ReceiveChat($"恢复弓箭手: {_fixed}/{conquest.ArcherList.Count}", ChatType.System);
                            MessageQueue.Enqueue($"恢复弓箭手: {_fixed}/{conquest.ArcherList.Count}");

                            _fixed = 0;
                            foreach (ConquestGuildGateInfo conquestGate in conquest.GateList)
                            {
                                if (conquestGate != null)
                                {
                                    conquestGate.Repair();
                                    _fixed++;
                                }
                            }
                            player.ReceiveChat($"恢复卫士: {_fixed}/{conquest.GateList.Count}", ChatType.System);
                            MessageQueue.Enqueue($"恢复卫士: {_fixed}/{conquest.GateList.Count}");

                            _fixed = 0;
                            foreach (ConquestGuildWallInfo conquestWall in conquest.WallList)
                            {
                                if (conquestWall != null)
                                {
                                    conquestWall.Repair();
                                    _fixed++;
                                }
                            }
                            player.ReceiveChat($"修复城墙: {_fixed}/{conquest.WallList.Count}", ChatType.System);
                            MessageQueue.Enqueue($"修复城墙: {_fixed}/{conquest.WallList.Count}");

                            _fixed = 0;
                            foreach (ConquestGuildSiegeInfo conquestSiege in conquest.SiegeList)
                            {
                                if (conquestSiege != null)
                                {
                                    conquestSiege.Repair();
                                    _fixed++;
                                }
                            }
                            player.ReceiveChat($"Sieges repaired: {_fixed}/{conquest.SiegeList.Count}", ChatType.System);
                            MessageQueue.Enqueue($"Sieges repaired: {_fixed}/{conquest.SiegeList.Count}");

                            break;
                        }

                    case ActionType.GiveGuildExp:
                        {
                            if (!Server.Scripting.LingFengSocialCommandExecutor.TryPlanGuildExperience(
                                    player.MyGuild != null, param[0], out uint amount, out _)) return;
                            player.MyGuild.GainExp(amount);
                        }
                        break;

                }
            }
        }
        private void Act(IList<NPCActions> acts, MonsterObject monster)
        {
            for (var i = 0; i < acts.Count; i++)
            {
                NPCActions act = acts[i];
                List<string> param = act.Params.Select(t => FindVariable(monster, t)).ToList();

                for (int j = 0; j < param.Count; j++)
                {
                    var parts = param[j].Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

                    if (parts.Length == 0) continue;

                    foreach (var part in parts)
                    {
                        param[j] = param[j].Replace(part, ReplaceValue(monster, part));
                    }
                }

                switch (act.Type)
                {
                    case ActionType.GiveHP:
                        {
                            if (!int.TryParse(param[0], out int tempInt)) return;
                            monster.ChangeHP(tempInt);
                        }
                        break;
                    case ActionType.GlobalMessage:
                        {
                            if (!Enum.TryParse(param[1], true, out ChatType chatType)) return;

                            var p = new S.Chat { Message = param[0], Type = chatType };
                            Envir.Broadcast(p);
                        }
                        break;

                    /* //mobs have no real "delayed" npc code so not added this yet
                                        case ActionType.Goto:
                                            DelayedAction action = new DelayedAction(DelayedType.NPC, -1, player.NPCID, "[" + param[0] + "]");
                                            player.ActionList.Add(action);
                                            break;
                    */
                    case ActionType.Break:
                        {
                            Page.BreakFromSegments = true;
                        }
                        break;

                    case ActionType.Param1:
                        {
                            if (!int.TryParse(param[1], out int tempInt)) return;

                            Param1 = param[0];
                            Param1Instance = tempInt;
                        }
                        break;

                    case ActionType.Param2:
                        {
                            if (!int.TryParse(param[0], out int tempInt)) return;

                            Param2 = tempInt;
                        }
                        break;

                    case ActionType.Param3:
                        {
                            if (!int.TryParse(param[0], out int tempInt)) return;

                            Param3 = tempInt;
                        }
                        break;

                    case ActionType.Mongen:
                        {
                            if (Param1 == null || Param2 == 0 || Param3 == 0) return;
                            if (!byte.TryParse(param[1], out byte tempByte)) return;

                            var map = Envir.GetMapByNameAndInstance(Param1, Param1Instance);
                            if (map == null) return;

                            var monInfo = Envir.GetMonsterInfo(param[0]);
                            if (monInfo == null) return;

                            for (int j = 0; j < tempByte; j++)
                            {
                                MonsterObject mob = MonsterObject.GetMonster(monInfo);
                                if (mob == null) return;
                                mob.Direction = 0;
                                mob.ActionTime = Envir.Time + 1000;
                                mob.Spawn(map, new Point(Param2, Param3));
                            }
                        }
                        break;
                    case ActionType.MonClear:
                        {
                            if (!int.TryParse(param[1], out int tempInt)) return;

                            var map = Envir.GetMapByNameAndInstance(param[0], tempInt);
                            if (map == null) return;

                            foreach (var cell in map.Cells)
                            {
                                if (cell == null || cell.Objects == null) continue;

                                for (int j = 0; j < cell.Objects.Count(); j++)
                                {
                                    MapObject ob = cell.Objects[j];

                                    if (ob.Race != ObjectType.Monster) continue;
                                    if (ob.Dead) continue;
                                    ob.Die();
                                }
                            }
                        }
                        break;

                    case ActionType.Mov:
                        {
                            string value = param[0];
                            AddVariable(monster, value, param[1]);
                        }
                        break;

                    case ActionType.Calc:
                        {
                            int left;
                            int right;

                            bool resultLeft = int.TryParse(param[0], out left);
                            bool resultRight = int.TryParse(param[2], out right);

                            if (resultLeft && resultRight)
                            {
                                try
                                {
                                    int result = Calculate(param[1], left, right);
                                    AddVariable(monster, param[3].Replace("-", ""), result.ToString());
                                }
                                catch (ArgumentException)
                                {
                                    MessageQueue.Enqueue(string.Format("以列表的怪物为对象的NPC命令CALC中错误使用 {0} 操作符: {0}, 页码: {1}", param[1], Key));
                                }
                            }
                            else
                            {
                                AddVariable(monster, param[3].Replace("-", ""), param[0] + param[2]);
                            }
                        }
                        break;

                    case ActionType.GiveBuff:
                        {
                            if (!Enum.IsDefined(typeof(BuffType), param[0])) return;

                            int.TryParse(param[1], out int tempInt);
                            bool.TryParse(param[2], out bool infinite);
                            bool.TryParse(param[3], out bool visible);
                            bool.TryParse(param[4], out bool stackable);

                            monster.AddBuff((BuffType)(byte)Enum.Parse(typeof(BuffType), param[0], true), monster, Settings.Second * tempInt, new Stats(), visible);
                        }
                        break;

                    case ActionType.RemoveBuff:
                        {
                            if (!Enum.IsDefined(typeof(BuffType), param[0])) return;

                            BuffType bType = (BuffType)(byte)Enum.Parse(typeof(BuffType), param[0]);

                            monster.RemoveBuff(bType);
                        }
                        break;

                    case ActionType.LoadValue:
                        {
                            string val = param[0];
                            string filePath = param[1];
                            string header = param[2];
                            string key = param[3];

                            var reader = new InIReader(filePath);
                            string loadedString = reader.ReadString(header, key, "");

                            if (loadedString == "") break;
                            AddVariable(monster, val, loadedString);
                        }
                        break;

                    case ActionType.SaveValue:
                        {
                            string filePath = param[0];
                            string header = param[1];
                            string key = param[2];
                            string val = param[3];

                            var reader = new InIReader(filePath);
                            reader.Write(header, key, val);
                        }
                        break;
                }
            }
        }

        private void Success(PlayerObject player)
        {
            Act(ActList, player);

            var parseSay = new List<String>(Say);
            parseSay = ParseSay(player, parseSay);

            player.NPCSpeech.AddRange(parseSay);
        }

        private void Failed(PlayerObject player)
        {
            Act(ElseActList, player);

            var parseElseSay = new List<String>(ElseSay);
            parseElseSay = ParseSay(player, parseElseSay);

            player.NPCSpeech.AddRange(parseElseSay);
        }

        private void Success(MonsterObject Monster)
        {
            Act(ActList, Monster);
        }

        private void Failed(MonsterObject Monster)
        {
            Act(ElseActList, Monster);
        }

        private void Success()
        {
            Act(ActList);
        }

        private void Failed()
        {
            Act(ElseActList);
        }

        private static bool TryNormalizeWritableDestination(string value, out string destination)
        {
            destination = (value ?? string.Empty).Trim();
            Match wrapped = Regex.Match(
                destination, @"^<\$STR\((.+)\)>$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (wrapped.Success) destination = wrapped.Groups[1].Value.Trim();
            return IsWritableScriptVariable(destination);
        }

        private static string NormalizeWritableDestination(string value) =>
            TryNormalizeWritableDestination(value, out string destination) ? destination : string.Empty;

        private static bool TryGetCandidateTextDefinition(
            string sourcePath,
            out TextFileDefinition definition)
        {
            definition = null;
            if (LingFengScriptReferenceResolver.TryResolveCandidateTextKey(
                    sourcePath, out string key) &&
                Envir.TextFileProvider != null &&
                (definition = Envir.TextFileProvider.GetByKey(key)) != null)
                return true;
            return Envir.PhysicalTextDataProvider?.TryGet(sourcePath, out definition) == true;
        }

        private static bool TryResolveLingFengMagic(string nameOrId, out MagicInfo magicInfo)
        {
            magicInfo = null;
            if (string.IsNullOrWhiteSpace(nameOrId)) return false;

            List<MagicInfo> matches = Envir.MagicInfoList
                .Where(info => info != null &&
                    (string.Equals(info.Name, nameOrId, StringComparison.OrdinalIgnoreCase) ||
                     info.Spell.ToString().Equals(nameOrId, StringComparison.OrdinalIgnoreCase) ||
                     ((ushort)info.Spell).ToString(CultureInfo.InvariantCulture)
                         .Equals(nameOrId, StringComparison.Ordinal)))
                .Take(2)
                .ToList();
            if (matches.Count != 1) return false;
            magicInfo = matches[0];
            return magicInfo.Spell != Spell.None;
        }

        private static bool TryGetIncrementedVariable(
            string baseDestination, int offset, out string destination)
        {
            destination = string.Empty;
            if (offset < 0 || string.IsNullOrWhiteSpace(baseDestination)) return false;
            Match numbered = Regex.Match(
                baseDestination, @"^([A-Za-z])(\d+)$", RegexOptions.CultureInvariant);
            if (numbered.Success &&
                int.TryParse(numbered.Groups[2].Value, NumberStyles.None,
                    CultureInfo.InvariantCulture, out int start) &&
                start <= int.MaxValue - offset)
            {
                destination = numbered.Groups[1].Value +
                              (start + offset).ToString(CultureInfo.InvariantCulture);
                return IsWritableScriptVariable(destination);
            }

            destination = baseDestination + (offset + 1).ToString(CultureInfo.InvariantCulture);
            return IsWritableScriptVariable(destination);
        }

        private static int GetLingFengTextLength(string text) =>
            (text ?? string.Empty).Sum(character => character <= 0x7F ? 1 : 2);

        private bool TryExecuteRandomSplit(PlayerObject player, IReadOnlyList<string> param)
        {
            if (param.Count != 5 || !int.TryParse(param[1], NumberStyles.None,
                    CultureInfo.InvariantCulture, out int resultMode) || resultMode is < 0 or > 2)
                return false;

            var entries = new List<(string Text, long Weight, string Source)>();
            long totalWeight = 0;
            foreach (string source in param[0].Split('|', StringSplitOptions.RemoveEmptyEntries))
            {
                int separator = source.LastIndexOf('#');
                if (separator <= 0 || separator == source.Length - 1 ||
                    !long.TryParse(source.Substring(separator + 1), NumberStyles.None,
                        CultureInfo.InvariantCulture, out long weight) || weight <= 0)
                    return false;
                try
                {
                    totalWeight = checked(totalWeight + weight);
                }
                catch (OverflowException)
                {
                    return false;
                }
                entries.Add((source.Substring(0, separator), weight, source));
            }
            if (entries.Count == 0 || totalWeight <= 0 || totalWeight > int.MaxValue) return false;

            long ticket = Envir.Random.Next((int)totalWeight);
            int selectedIndex = 0;
            for (; selectedIndex < entries.Count; selectedIndex++)
            {
                if (ticket < entries[selectedIndex].Weight) break;
                ticket -= entries[selectedIndex].Weight;
            }
            if (selectedIndex >= entries.Count) return false;

            (string Text, long Weight, string Source) selected = entries[selectedIndex];
            string result = FormatRandomSplitValue(selected, resultMode);
            if (!TryStoreScriptTextValue(player, param[2], result)) return false;
            if (param[3].Length == 0 || param[4].Length == 0) return true;
            if (!int.TryParse(param[3], NumberStyles.None, CultureInfo.InvariantCulture,
                    out int remainingMode) || remainingMode is < 0 or > 2)
                return false;
            string remaining = string.Join('|', entries
                .Where((_, index) => index != selectedIndex)
                .Select(entry => FormatRandomSplitValue(entry, remainingMode)));
            return TryStoreScriptTextValue(player, param[4], remaining);
        }

        private static string FormatRandomSplitValue(
            (string Text, long Weight, string Source) entry,
            int mode) => mode switch
        {
            0 => entry.Text,
            1 => entry.Weight.ToString(CultureInfo.InvariantCulture),
            _ => entry.Source
        };

        private static bool IsHumanOwnedActor(MapObject actor) => actor switch
        {
            PlayerObject => true,
            HeroObject => true,
            MonsterObject monster when monster.Master is PlayerObject => true,
            MonsterObject monster when monster.Master is HeroObject => true,
            _ => false
        };

        private static bool MatchesLingFengAttackMode(AttackMode actual, int expected) => expected switch
        {
            0 => actual == AttackMode.All,
            1 => actual == AttackMode.Peace,
            _ => false
        };

        private static CharacterInfo GetLingFengProgressInfo(PlayerObject player, string target) =>
            target == "H" ? player.Hero?.Info : player.Info;

        private static bool SendLingFengClientBuff(
            PlayerObject executor,
            MapObject target,
            int iconPackage,
            int iconIndex,
            byte slot,
            int seconds,
            string description)
        {
            if (executor == null || target == null || slot > 6 || iconPackage < 0 ||
                iconIndex < 0 || seconds < 0 || string.IsNullOrWhiteSpace(description) ||
                description.Length > 256)
                return false;

            long duration = Math.Min(long.MaxValue, (long)seconds * Settings.Second);
            var packet = new S.AddBuff
            {
                Buff = new ClientBuff
                {
                    Type = (BuffType)(240 + slot),
                    Visible = target != executor,
                    ObjectID = target.ObjectID,
                    ExpireTime = duration,
                    Infinite = seconds == 0,
                    Stats = new Stats(),
                    Values = Array.Empty<int>(),
                    IsLingFengScript = true,
                    LingFengIconPackage = iconPackage,
                    LingFengIconIndex = iconIndex,
                    LingFengSlot = slot,
                    LingFengDescription = description
                }
            };

            (target as PlayerObject ?? executor).Enqueue(packet);
            return true;
        }

        private static bool MatchesLingFengTargetRace(
            LingFengCombatActorKind actual,
            string expected) => expected switch
        {
            "0" => actual == LingFengCombatActorKind.Player,
            "1" => actual == LingFengCombatActorKind.Hero,
            "151" => actual == LingFengCombatActorKind.Pet,
            _ => false
        };

        private static bool TryGetLingFengLastActorPlayer(out PlayerObject actor)
        {
            actor = null;
            if (!TryGetLingFengLastActor(out MapObject source)) return false;
            actor = source as PlayerObject;
            return actor != null;
        }

        private static bool TryGetLingFengLastActor(out MapObject actor)
        {
            actor = null;
            if (LingFengTxtTriggerContext.Current?.Payload is not LingFengDamageEvent damage)
                return false;
            if (damage.ActorObjectId != 0)
                actor = Envir.Objects.FirstOrDefault(value =>
                    value.ObjectID == damage.ActorObjectId);
            if (actor == null && damage.ActorKind == LingFengCombatActorKind.Player &&
                !string.IsNullOrWhiteSpace(damage.AttackerName))
                actor = Envir.GetPlayer(damage.AttackerName);
            return actor != null;
        }

        private static bool TryResolveLingFengRangeActor(
            PlayerObject player, string targetKind, out MapObject actor)
        {
            actor = player;
            if (targetKind == "SELF") return player != null && !player.Dead && player.Node != null;
            if (targetKind != "L" || player?.CurrentMap == null ||
                LingFengTxtTriggerContext.Current?.Payload is not LingFengDamageEvent damage)
                return false;

            if (damage.ActorObjectId != 0)
            {
                actor = Envir.Objects.Concat<MapObject>(Envir.Players)
                    .FirstOrDefault(value => value != null &&
                        value.ObjectID == damage.ActorObjectId && !value.Dead &&
                        value.CurrentMap == player.CurrentMap);
                if (actor != null) return true;
            }

            if (damage.Perspective == PlayerDamagePerspective.Incoming &&
                damage.CurrentTargetObjectId != 0)
            {
                actor = Envir.Objects.Concat<MapObject>(Envir.Players)
                    .FirstOrDefault(value => value != null &&
                        value.ObjectID == damage.CurrentTargetObjectId && !value.Dead &&
                        value.CurrentMap == player.CurrentMap);
                if (actor != null) return true;
            }

            actor = Envir.GetPlayer(damage.AttackerName);
            if (actor != null && !actor.Dead && actor.CurrentMap == player.CurrentMap) return true;

            actor = Envir.Objects.OfType<MonsterObject>()
                .Where(value => !value.Dead && value.CurrentMap == player.CurrentMap &&
                    string.Equals(value.Name, damage.AttackerName,
                        StringComparison.OrdinalIgnoreCase))
                .OrderBy(value => value.ObjectID)
                .FirstOrDefault();
            return actor != null;
        }

        private static bool TryGetLingFengCurrentTargetMonster(
            PlayerObject player,
            out MonsterObject monster)
        {
            bool found = TryGetLingFengCurrentTarget(player, out MapObject target);
            monster = target as MonsterObject;
            return found && monster != null;
        }

        private static bool TryGetLingFengCurrentTargetPlayer(
            PlayerObject player,
            out PlayerObject target)
        {
            bool found = TryGetLingFengCurrentTarget(player, out MapObject currentTarget);
            target = currentTarget as PlayerObject;
            return found && target != null;
        }

        private static bool TryGetLingFengCurrentTarget(
            PlayerObject player,
            out MapObject target)
        {
            target = null;
            if (player == null ||
                LingFengTxtTriggerContext.Current?.Payload is not LingFengDamageEvent damage)
                return false;

            if (damage.CurrentTargetObjectId != 0)
            {
                target = Envir.Objects.Concat<MapObject>(Envir.Players).FirstOrDefault(value =>
                    value != null && value.ObjectID == damage.CurrentTargetObjectId &&
                    !value.Dead && value.CurrentMap == player.CurrentMap);
                if (target != null) return true;
            }

            string currentName = damage.Perspective == PlayerDamagePerspective.Incoming
                ? damage.AttackerName
                : damage.CurrentTargetName;
            if (string.IsNullOrWhiteSpace(currentName)) return false;

            PlayerObject targetPlayer = Envir.GetPlayer(currentName);
            if (targetPlayer != null && !targetPlayer.Dead &&
                targetPlayer.CurrentMap == player.CurrentMap)
            {
                target = targetPlayer;
                return true;
            }

            IEnumerable<MonsterObject> monsters = Envir.Objects
                .OfType<MonsterObject>()
                .Where(value => !value.Dead && value.CurrentMap == player.CurrentMap &&
                    string.Equals(value.Name, currentName, StringComparison.OrdinalIgnoreCase));
            if (damage.Perspective == PlayerDamagePerspective.Outgoing && damage.TargetIsMonster)
                monsters = monsters.Where(value =>
                    value.CurrentLocation.X == damage.TargetX &&
                    value.CurrentLocation.Y == damage.TargetY);
            target = monsters.OrderBy(value => value.ObjectID).FirstOrDefault();
            return target != null;
        }

        private static string TrimLingFengMonsterNumericSuffix(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;
            int length = name.Length;
            while (length > 0 && char.IsDigit(name[length - 1])) length--;
            return name[..length];
        }

        private static bool TryChangeLingFengTargetAbility(
            PlayerObject player,
            string targetKind,
            string sourceKey,
            int abilityIndex,
            string operation,
            int value,
            int durationSeconds,
            bool percentage)
        {
            MapObject target = targetKind switch
            {
                "SELF" => player,
                "L" when TryGetLingFengLastActorPlayer(out PlayerObject lastActor) => lastActor,
                "M" when TryGetLingFengCurrentTargetMonster(player, out MonsterObject monster) => monster,
                "M" when TryGetLingFengCurrentTargetPlayer(player, out PlayerObject targetPlayer) => targetPlayer,
                _ => null
            };
            return target switch
            {
                HumanObject human => human.TryChangeLingFengAbility(
                    sourceKey, abilityIndex, operation, value, durationSeconds, percentage),
                MonsterObject monster => monster.TryChangeLingFengAbility(
                    sourceKey, abilityIndex, operation, value, durationSeconds, percentage),
                _ => false
            };
        }

        private static void ScheduleLingFengEctypeCallback(
            PlayerObject player, string label)
        {
            if (player == null || player.NPCObjectID == 0 || player.NPCScriptID <= 0 ||
                string.IsNullOrWhiteSpace(label) ||
                !NPCScript.TryGet(player.NPCScriptID, out NPCScript script))
                return;
            string pageKey = $"[{label.Trim().Trim('[', ']')}]".ToUpperInvariant();
            if (!script.NPCPages.Any(page =>
                    string.Equals(page.Key, pageKey, StringComparison.OrdinalIgnoreCase)))
                return;
            player.ActionList.Add(new DelayedAction(
                DelayedType.NPC, -1, player.NPCObjectID, player.NPCScriptID, pageKey));
        }

        private static bool TryChangeLingFengMapMonsterAbility(
            PlayerObject player, IReadOnlyList<string> values, string sourceKey)
        {
            if (player == null || values == null || values.Count is not (6 or 9) ||
                !int.TryParse(values[2], NumberStyles.None, CultureInfo.InvariantCulture,
                    out int lingFengIndex) ||
                values[3] is not ("+" or "-" or "=") ||
                !int.TryParse(values[4], NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out int value) || values[5] != "0")
                return false;

            Map map = values[0].Equals("SELF", StringComparison.OrdinalIgnoreCase)
                ? player.CurrentMap
                : Envir.GetMapByNameAndInstance(values[0]);
            if (map == null) return false;

            Point center = Point.Empty;
            int range = 0;
            bool useRange = values.Count == 9;
            if (useRange &&
                (!int.TryParse(values[6], NumberStyles.None, CultureInfo.InvariantCulture,
                     out int x) ||
                 !int.TryParse(values[7], NumberStyles.None, CultureInfo.InvariantCulture,
                     out int y) ||
                 !int.TryParse(values[8], NumberStyles.None, CultureInfo.InvariantCulture,
                     out range) || x < 0 || y < 0 || range < 0))
                return false;
            if (useRange)
                center = new Point(
                    int.Parse(values[6], CultureInfo.InvariantCulture),
                    int.Parse(values[7], CultureInfo.InvariantCulture));

            int translatedIndex = lingFengIndex switch
            {
                1 => 11,
                3 => 12,
                >= 4 and <= 13 => lingFengIndex - 3,
                _ => -1
            };
            if (lingFengIndex is not 0 && translatedIndex < 0) return false;

            foreach (MonsterObject monster in Envir.Objects.OfType<MonsterObject>()
                         .Where(candidate => !candidate.Dead && candidate.CurrentMap == map &&
                             (values[1] == "*" || candidate.Info.Name.Equals(
                                 values[1], StringComparison.OrdinalIgnoreCase)))
                         .Where(candidate => !useRange ||
                             Functions.InRange(candidate.CurrentLocation, center, range))
                         .ToArray())
            {
                if (lingFengIndex == 0)
                {
                    long next = values[3] switch
                    {
                        "+" => (long)monster.HP + value,
                        "-" => (long)monster.HP - value,
                        "=" => value,
                        _ => monster.HP
                    };
                    int targetHp = (int)Math.Clamp(next, 0L, monster.Stats[Stat.HP]);
                    monster.ChangeHP(targetHp - monster.HP);
                    continue;
                }

                if (!monster.TryChangeLingFengAbility(
                        sourceKey, translatedIndex, values[3], value, 0, false))
                    return false;
            }
            return true;
        }

        private static bool TryRecalcLingFengMapMonsterAbility(
            PlayerObject player, IReadOnlyList<string> values, out int refreshed)
        {
            refreshed = 0;
            if (player == null || values == null || values.Count is not (2 or 5))
                return false;
            Map map = values[0].Equals("SELF", StringComparison.OrdinalIgnoreCase)
                ? player.CurrentMap
                : Envir.GetMapByNameAndInstance(values[0]);
            if (map == null) return false;

            Point center = Point.Empty;
            int range = 0;
            bool useRange = values.Count == 5;
            if (useRange &&
                (!int.TryParse(values[2], NumberStyles.None, CultureInfo.InvariantCulture,
                     out int x) ||
                 !int.TryParse(values[3], NumberStyles.None, CultureInfo.InvariantCulture,
                     out int y) ||
                 !int.TryParse(values[4], NumberStyles.None, CultureInfo.InvariantCulture,
                     out range) || x < 0 || y < 0 || range < 0))
                return false;
            if (useRange)
                center = new Point(
                    int.Parse(values[2], CultureInfo.InvariantCulture),
                    int.Parse(values[3], CultureInfo.InvariantCulture));

            MonsterObject[] targets = Envir.Objects.OfType<MonsterObject>()
                .Where(candidate => !candidate.Dead && candidate.CurrentMap == map &&
                    (values[1] == "*" || candidate.Info.Name.Equals(
                        values[1], StringComparison.OrdinalIgnoreCase)))
                .Where(candidate => !useRange ||
                    Functions.InRange(candidate.CurrentLocation, center, range))
                .OrderBy(candidate => candidate.ObjectID)
                .Take(2048)
                .ToArray();
            foreach (MonsterObject target in targets)
            {
                target.RefreshAll();
                refreshed++;
            }
            return true;
        }

        private static bool TryForceLingFengMonsterDropItems(
            PlayerObject player, IReadOnlyList<string> values)
        {
            if (player == null || values == null || values.Count != 4 ||
                !int.TryParse(values[2], NumberStyles.None, CultureInfo.InvariantCulture,
                    out int count) || count is < 1 or > 1000 ||
                !int.TryParse(values[3], NumberStyles.None, CultureInfo.InvariantCulture,
                    out int centerMode) || centerMode is < 0 or > 1)
                return false;
            MonsterInfo monsterInfo = Envir.GetMonsterInfo(values[0]);
            ItemInfo itemInfo = Envir.GetItemInfo(values[1]);
            Map map = player.CurrentMap;
            Point location = player.CurrentLocation;
            if (monsterInfo == null || itemInfo == null || map == null)
                return false;

            MonsterObject source = null;
            if (centerMode == 1 &&
                Server.Scripting.LingFengTxtTriggerContext.Current?.Payload is
                    Server.Scripting.LingFengMonsterKillEvent killEvent &&
                map.ValidPoint(new Point(killEvent.X, killEvent.Y)))
            {
                location = new Point(killEvent.X, killEvent.Y);
                source = Envir.Objects.OfType<MonsterObject>()
                    .Where(candidate => candidate.CurrentMap == map &&
                        candidate.CurrentLocation == location &&
                        candidate.Info.Name.Equals(values[0], StringComparison.OrdinalIgnoreCase))
                    .OrderBy(candidate => candidate.ObjectID)
                    .FirstOrDefault();
            }

            source ??= MonsterObject.GetMonster(monsterInfo);
            if (source == null) return false;
            if (source.CurrentMap == null)
            {
                source.CurrentMap = map;
                source.CurrentLocation = location;
                source.EXPOwner = player;
                source.LastHitter = player;
            }
            return source.ForceLingFengDropItems(player, itemInfo, count);
        }

        private static bool TryClearLingFengMapItems(
            PlayerObject player, IReadOnlyList<string> values, out int cleared)
        {
            cleared = 0;
            if (player == null || values == null || values.Count is not (1 or 4 or 5))
                return false;
            Map map = values[0].Equals("SELF", StringComparison.OrdinalIgnoreCase)
                ? player.CurrentMap
                : Envir.GetMapByNameAndInstance(values[0]);
            if (map == null) return false;

            Point center = Point.Empty;
            int range = 0;
            bool useRange = values.Count >= 4;
            if (useRange &&
                (!int.TryParse(values[1], NumberStyles.None, CultureInfo.InvariantCulture,
                     out int x) ||
                 !int.TryParse(values[2], NumberStyles.None, CultureInfo.InvariantCulture,
                     out int y) ||
                 !int.TryParse(values[3], NumberStyles.None, CultureInfo.InvariantCulture,
                     out range) || x < 0 || y < 0 || range < 0))
                return false;
            if (useRange)
                center = new Point(
                    int.Parse(values[1], CultureInfo.InvariantCulture),
                    int.Parse(values[2], CultureInfo.InvariantCulture));
            string itemName = values.Count == 5 ? values[4] : "*";

            ItemObject[] items = Envir.Objects.OfType<ItemObject>()
                .Where(item => item.CurrentMap == map &&
                    (!useRange || Functions.InRange(item.CurrentLocation, center, range)) &&
                    (itemName == "*" ||
                     (item.Item != null && item.Item.Info.FriendlyName.Equals(
                         itemName, StringComparison.OrdinalIgnoreCase)) ||
                     (item.Item == null && item.Gold > 0 &&
                      itemName.Equals("金币", StringComparison.OrdinalIgnoreCase))))
                .ToArray();
            foreach (ItemObject item in items)
            {
                map.RemoveObject(item);
                item.Despawn();
                cleared++;
            }
            return true;
        }

        private static bool TryGetLingFengFacingPlayer(
            PlayerObject player, out PlayerObject facingPlayer)
        {
            facingPlayer = null;
            if (player?.CurrentMap == null) return false;
            Point front = player.Front;
            if (front.X < 0 || front.Y < 0 ||
                front.X >= player.CurrentMap.Width || front.Y >= player.CurrentMap.Height)
                return false;
            Cell cell = player.CurrentMap.GetCell(front);
            facingPlayer = cell?.Objects?
                .OfType<PlayerObject>()
                .FirstOrDefault(candidate => !ReferenceEquals(candidate, player) && !candidate.Dead);
            return facingPlayer != null;
        }

        private static bool TryCountLingFengMapMonsters(
            string mapName, bool excludePets, out int count)
        {
            count = 0;
            Map map = Envir.GetMapByNameAndInstance(mapName);
            if (map == null) return false;
            count = Envir.Objects.OfType<MonsterObject>().Count(monster =>
                !monster.Dead && monster.CurrentMap == map &&
                (!excludePets || !IsHumanOwnedActor(monster)));
            return true;
        }

        private static bool TryChangeLingFengTargetSpeed(
            PlayerObject player, string targetKind, string sourceKey,
            int speedType, int value, int durationSeconds)
        {
            return targetKind switch
            {
                "SELF" => player.TryChangeLingFengSpeed(
                    sourceKey, speedType, value, durationSeconds),
                "M" when TryGetLingFengCurrentTargetMonster(
                    player, out MonsterObject monster) => monster.TryChangeLingFengSpeed(
                    sourceKey, speedType, value, durationSeconds),
                "M" when TryGetLingFengCurrentTargetPlayer(
                    player, out PlayerObject targetPlayer) => targetPlayer.TryChangeLingFengSpeed(
                    sourceKey, speedType, value, durationSeconds),
                "FS" => TryChangeLingFengCloneSpeed(
                    player, sourceKey, speedType, value, durationSeconds),
                _ => false
            };
        }

        private static bool TryChangeLingFengCloneSpeed(
            PlayerObject player, string sourceKey,
            int speedType, int value, int durationSeconds)
        {
            MonsterObject[] clones = player.Pets
                .Where(pet => pet != null && !pet.Dead && pet.Master == player &&
                    (pet.LingFengIsSelfClone ||
                     string.Equals(pet.Info?.Name, Settings.CloneName,
                         StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(pet.Info?.Name, Settings.AssassinCloneName,
                         StringComparison.OrdinalIgnoreCase)))
                .Distinct()
                .ToArray();
            foreach (MonsterObject clone in clones)
            {
                if (!clone.TryChangeLingFengSpeed(
                        sourceKey, speedType, value, durationSeconds))
                    return false;
            }
            return true;
        }

        private static bool TryParseLingFengStateItemFlags(
            IEnumerable<string> values, out BindMode flags)
        {
            flags = BindMode.None;
            BindMode[] mappings =
            {
                BindMode.DontDrop,
                BindMode.DontTrade,
                BindMode.DontStore,
                BindMode.DontRepair,
                BindMode.DontSell,
                BindMode.DontDeathdrop,
                BindMode.DestroyOnDrop
            };
            string[] entries = values.ToArray();
            if (entries.Length != mappings.Length) return false;
            for (int index = 0; index < entries.Length; index++)
            {
                if (!int.TryParse(entries[index], out int state) || state is < 0 or > 1)
                    return false;
                if (state == 1) flags |= mappings[index];
            }
            return true;
        }

        private static bool HasLingFengState(MapObject target, string state) => state switch
        {
            "0" => target.CurrentPoison.HasFlag(PoisonType.Green),
            "1" => target.CurrentPoison.HasFlag(PoisonType.Red),
            "2" => target.CurrentPoison.HasFlag(PoisonType.Paralysis) ||
                   target.CurrentPoison.HasFlag(PoisonType.LRParalysis),
            "3" => target.CurrentPoison.HasFlag(PoisonType.Frozen),
            _ => false
        };

        private static bool TryApplyLingFengState(
            PlayerObject caster,
            MapObject target,
            int stateCode,
            int durationSeconds,
            int value,
            int tickSeconds)
        {
            PoisonType type = stateCode switch
            {
                1 => PoisonType.Paralysis,
                2 => PoisonType.Frozen,
                3 => PoisonType.Slow,
                4 => PoisonType.Red,
                5 or 13 => PoisonType.Green,
                _ => PoisonType.None
            };
            if (type == PoisonType.None) return false;
            if (durationSeconds == 0)
            {
                target.PoisonList.RemoveAll(poison => poison.PType == type);
                target.CurrentPoison &= ~type;
                if (target is HumanObject human)
                    human.Enqueue(new S.Poisoned { Poison = target.CurrentPoison });
                target.Broadcast(new S.ObjectPoisoned
                {
                    ObjectID = target.ObjectID,
                    Poison = target.CurrentPoison
                });
                return true;
            }

            int intervalSeconds = stateCode == 13 ? tickSeconds : 1;
            if (intervalSeconds <= 0 || value < 0) return false;
            int durationTicks = stateCode == 13
                ? Math.Max(1, (durationSeconds + intervalSeconds - 1) / intervalSeconds)
                : durationSeconds;
            int tickMilliseconds;
            try
            {
                tickMilliseconds = checked(intervalSeconds * Settings.Second);
            }
            catch (OverflowException)
            {
                return false;
            }
            target.ApplyPoison(new Poison
            {
                PType = type,
                Duration = durationTicks,
                TickSpeed = tickMilliseconds,
                Value = stateCode == 13 ? value : 0
            }, caster, NoResist: stateCode == 13 || value == 0);
            return true;
        }

        private static bool TryApplyLingFengPoison(
            PlayerObject caster,
            MapObject target,
            int poisonCode,
            int durationSeconds,
            int power,
            bool calculateDefence,
            bool powerPermille)
        {
            PoisonType type = poisonCode switch
            {
                0 => PoisonType.Green,
                1 => PoisonType.Red,
                5 => PoisonType.Paralysis,
                _ => PoisonType.None
            };
            if (type == PoisonType.None || target is not (HumanObject or MonsterObject)) return false;

            int appliedPower = power;
            if (powerPermille && type == PoisonType.Green)
                appliedPower = (int)Math.Clamp(
                    (long)target.Stats[Stat.HP] * power / 1000, 0L, int.MaxValue);

            target.ApplyPoison(new Poison
            {
                Owner = caster,
                PType = type,
                Duration = durationSeconds,
                TickSpeed = Settings.Second,
                Value = type == PoisonType.Paralysis ? 0 : appliedPower,
                LingFengPowerDefined = type == PoisonType.Red,
                LingFengPowerPermille = type == PoisonType.Red && powerPermille
            }, caster, NoResist: !calculateDefence, ignoreDefence: !calculateDefence);
            return true;
        }

        private static bool TryGetLingFengObjectAbility(
            MapObject target,
            int type,
            out int value)
        {
            value = type switch
            {
                0 when target is HumanObject human => human.HP,
                0 when target is MonsterObject monster => monster.HP,
                1 => target.Stats[Stat.HP],
                2 when target is HumanObject human => human.MP,
                3 when target is HumanObject human => human.Stats[Stat.MP],
                4 => target.Stats[Stat.MinAC],
                5 => target.Stats[Stat.MaxAC],
                6 => target.Stats[Stat.MinMAC],
                7 => target.Stats[Stat.MaxMAC],
                8 => target.Stats[Stat.MinDC],
                9 => target.Stats[Stat.MaxDC],
                10 => target.Stats[Stat.MinMC],
                11 => target.Stats[Stat.MaxMC],
                12 => target.Stats[Stat.MinSC],
                13 => target.Stats[Stat.MaxSC],
                14 => target.AttackSpeed,
                15 when target is MonsterObject monster => monster.MoveSpeed,
                _ => 0
            };
            return type is >= 0 and <= 15 &&
                   (type is not (2 or 3) || target is HumanObject) &&
                   (type != 15 || target is MonsterObject);
        }

        private static bool IsWritableScriptVariable(string destination) =>
            Regex.IsMatch(destination ?? string.Empty, @"^[A-Za-z][0-9]+$", RegexOptions.CultureInvariant) ||
            TryParseRuntimeVariableReference(destination, out _);

        private bool TryStoreScriptValue(PlayerObject player, string destination, long value)
        {
            return TryStoreScriptTextValue(
                player, destination, value.ToString(CultureInfo.InvariantCulture));
        }

        private bool TryStoreScriptTextValue(PlayerObject player, string destination, string text)
        {
            if (Regex.IsMatch(destination ?? string.Empty, @"^[A-Za-z][0-9]+$", RegexOptions.CultureInvariant))
            {
                AddVariable(player, destination, text);
                return true;
            }

            if (!TryParseRuntimeVariableReference(destination, out _) || player.NPCObjectID == 0)
                return false;
            ScriptVariableMutationResult mutation = Envir.CSharpScripts.VariableCommands.Mutate(
                ScriptVariableContext.ForConversation(player, player.NPCObjectID, player.CurrentMap),
                destination, "MOV", text);
            if (mutation.Success) return true;
            MessageQueue.Enqueue(
                $"[Variables][TXT] 坐标写入失败：{mutation.ErrorCode} {mutation.Diagnostic}，页码：{Key}");
            return false;
        }

        private bool TryStoreScriptListValue(
            PlayerObject player, string destination, IEnumerable<string> values)
        {
            if (player?.NPCObjectID == 0 ||
                !ScriptVariableReferenceParser.TryParse(destination, out ScriptVariableReference reference) ||
                reference.Scope != ScriptVariableScope.L)
                return false;
            ScriptVariableMutationResult mutation = Envir.CSharpScripts.VariableModule.Mutate(
                ScriptVariableContext.ForConversation(
                    player, player.NPCObjectID, player.CurrentMap),
                ScriptVariableMutation.Set(reference, ScriptVariableValue.FromList(values)));
            return mutation.Success;
        }

        private static bool TryParseLingFengItemTypes(
            string text, out HashSet<ItemType> itemTypes)
        {
            itemTypes = new HashSet<ItemType>();
            if (string.IsNullOrWhiteSpace(text)) return true;
            foreach (string value in text.Split('|', StringSplitOptions.RemoveEmptyEntries))
            {
                if (!byte.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture,
                        out byte rawType) || !Enum.IsDefined(typeof(ItemType), rawType))
                    return false;
                itemTypes.Add((ItemType)rawType);
            }
            return itemTypes.Count > 0;
        }

        private static bool TryParseLingFengIndexRanges(
            string text, out HashSet<int> indexes)
        {
            indexes = new HashSet<int>();
            if (string.IsNullOrWhiteSpace(text)) return false;
            foreach (string part in text.Split(
                         '|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                string[] bounds = part.Split('-', StringSplitOptions.TrimEntries);
                if (bounds.Length is < 1 or > 2 ||
                    !int.TryParse(bounds[0], NumberStyles.None, CultureInfo.InvariantCulture,
                        out int start) || start < 0)
                    return false;
                int end = start;
                if (bounds.Length == 2 &&
                    (!int.TryParse(bounds[1], NumberStyles.None, CultureInfo.InvariantCulture,
                         out end) || end < start))
                    return false;
                if ((long)end - start + 1 > 10000 || indexes.Count + (long)end - start + 1 > 10000)
                    return false;
                for (long index = start; index <= end; index++) indexes.Add((int)index);
            }
            return indexes.Count > 0;
        }

        private static bool TrySendLingFengCenterAudienceMessage(
            PlayerObject player, int mode, string message)
        {
            IEnumerable<PlayerObject> recipients = mode switch
            {
                0 => new[] { player },
                1 => Envir.Players.Concat(new[] { player }),
                2 => (player.MyGuild?.GetOnlinePlayers() ?? Enumerable.Empty<PlayerObject>())
                    .Concat(new[] { player }),
                4 => Envir.Players
                    .Where(target => target.CurrentMap == player.CurrentMap)
                    .Concat(new[] { player }),
                _ => Enumerable.Empty<PlayerObject>()
            };
            if (mode is not (0 or 1 or 2 or 4)) return false;
            foreach (PlayerObject recipient in recipients.Distinct())
            {
                if (mode == 1 && recipient.Info.LingFengProgress.IsGlobalMessageFiltered(1))
                    continue;
                recipient.ReceiveChat(message, ChatType.Announcement);
            }
            return true;
        }

        private static bool TrySendLingFengAudienceMessage(
            PlayerObject player, int mode, string message, int range)
        {
            if (mode is < 0 or > 7 || range < 0) return false;
            IEnumerable<PlayerObject> recipients = mode switch
            {
                0 => Envir.Players.Concat(new[] { player }),
                1 => new[] { player },
                2 or 5 => (player.GroupMembers ?? Enumerable.Empty<PlayerObject>())
                    .Concat(mode == 2 ? new[] { player } : Enumerable.Empty<PlayerObject>()),
                3 or 6 => (player.MyGuild?.GetOnlinePlayers() ?? Enumerable.Empty<PlayerObject>())
                    .Concat(mode == 3 ? new[] { player } : Enumerable.Empty<PlayerObject>()),
                4 or 7 => Envir.Players
                    .Where(target => target.CurrentMap == player.CurrentMap)
                    .Concat(mode == 4 ? new[] { player } : Enumerable.Empty<PlayerObject>()),
                _ => Enumerable.Empty<PlayerObject>()
            };
            long rangeSquared = (long)range * range;
            foreach (PlayerObject recipient in recipients.Distinct())
            {
                if (recipient == null ||
                    (mode is 5 or 6 or 7) && ReferenceEquals(recipient, player))
                    continue;
                if (range > 0 && (recipient.CurrentMap != player.CurrentMap ||
                    DistanceSquared(recipient.CurrentLocation, player.CurrentLocation) > rangeSquared))
                    continue;
                recipient.ReceiveChat(message, ChatType.Announcement);
            }
            return true;
        }

        private static long DistanceSquared(Point left, Point right)
        {
            long x = (long)left.X - right.X;
            long y = (long)left.Y - right.Y;
            return x * x + y * y;
        }

        private static bool TryResolveLingFengBoundMoney(
            PlayerObject player, string currencyName, string amountText,
            out uint amount, out uint balance, out string diagnostic)
        {
            amount = 0;
            balance = 0;
            diagnostic = string.Empty;
            if (player?.Account == null)
            {
                diagnostic = "缺少人物账户上下文。";
                return false;
            }
            if (!uint.TryParse(amountText, NumberStyles.None,
                    CultureInfo.InvariantCulture, out amount))
            {
                diagnostic = "数量必须是 0 到 4294967295 的整数。";
                return false;
            }
            if (!string.Equals(currencyName, "铜钱", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(currencyName, "金币", StringComparison.OrdinalIgnoreCase))
            {
                diagnostic = $"当前没有货币“{currencyName}”的绑定分组配置。";
                return false;
            }
            balance = player.Account.Gold;
            return true;
        }

        private bool TryEvaluateLingFengWhile(
            PlayerObject player, string left, string comparison, string right, out bool matched)
        {
            matched = false;
            if (player == null || player.NPCObjectID == 0) return false;
            var context = ScriptVariableContext.ForConversation(
                player, player.NPCObjectID, player.CurrentMap);
            if (TryParseRuntimeVariableReference(left, out _))
            {
                ScriptVariableCheckResult result = Envir.CSharpScripts.VariableCommands.Check(
                    context, left, comparison, right);
                matched = result.Success && result.Matched;
                return result.Success;
            }

            if (TryParseRuntimeVariableReference(right, out _))
            {
                ScriptVariableTextResult result = Envir.CSharpScripts.VariableCommands.Format(context, right);
                if (!result.Success) return false;
                right = result.Text;
            }

            int order;
            if (decimal.TryParse(left, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal leftNumber) &&
                decimal.TryParse(right, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal rightNumber))
                order = leftNumber.CompareTo(rightNumber);
            else
                order = string.Compare(left, right, StringComparison.Ordinal);

            matched = comparison switch
            {
                "=" or "==" => order == 0,
                "!=" or "<>" => order != 0,
                ">" => order > 0,
                ">=" => order >= 0,
                "<" => order < 0,
                "<=" => order <= 0,
                _ => false
            };
            return comparison is "=" or "==" or "!=" or "<>" or ">" or ">=" or "<" or "<=";
        }

        private static int FindMatchingLingFengWhileBoundary(
            IList<NPCActions> acts, int origin, ActionType opening, ActionType closing)
        {
            int direction = opening == ActionType.LingFengWhile ? 1 : -1;
            int depth = 0;
            for (int index = origin + direction;
                 index >= 0 && index < acts.Count;
                 index += direction)
            {
                ActionType type = acts[index].Type;
                if (type == opening)
                    depth++;
                else if (type == closing && depth-- == 0)
                    return index;
            }
            return -1;
        }

        private static bool IsLingFengNoticeScope(string value) =>
            value?.ToUpperInvariant() is
                "SELF" or "GROUP" or "GUILD" or "MAP" or "NATION" or "NATIONAL" or "ALL";

        private static bool TryDispatchLingFengGuildNotice(
            PlayerObject player, string foreground, string background,
            string scope, string message)
        {
            if (!byte.TryParse(foreground, NumberStyles.None, CultureInfo.InvariantCulture,
                    out _) ||
                !byte.TryParse(background, NumberStyles.None, CultureInfo.InvariantCulture,
                    out _) ||
                string.IsNullOrWhiteSpace(message) || message.Length > 2048)
                return false;

            IEnumerable<PlayerObject> recipients = scope?.ToUpperInvariant() switch
            {
                "SELF" when player != null => new[] { player },
                "GROUP" when player != null =>
                    player.GroupMembers ?? Enumerable.Empty<PlayerObject>(),
                "GUILD" when player != null =>
                    player.MyGuild?.GetOnlinePlayers() ?? Enumerable.Empty<PlayerObject>(),
                "MAP" when player != null =>
                    Envir.Players.Where(target => target.CurrentMap == player.CurrentMap),
                "NATION" or "NATIONAL" or "ALL" => Envir.Players,
                _ => Enumerable.Empty<PlayerObject>()
            };
            foreach (PlayerObject recipient in recipients.Where(value => value != null).Distinct())
                recipient.ReceiveChat(message, ChatType.Announcement);
            return scope?.ToUpperInvariant() is
                "NATION" or "NATIONAL" or "ALL" or "SELF" or "GROUP" or "GUILD" or "MAP";
        }

        private static bool TryGetPercentScale(string type, out int scale)
        {
            scale = type switch
            {
                "0" or "" => 100,
                "1" => 1_000,
                "2" => 10_000,
                _ => 0
            };
            return scale != 0;
        }

        public static bool Compare<T>(string op, T left, T right) where T : IComparable<T>
        {
            switch (op)
            {
                case "<": return left.CompareTo(right) < 0;
                case ">": return left.CompareTo(right) > 0;
                case "<=": return left.CompareTo(right) <= 0;
                case ">=": return left.CompareTo(right) >= 0;
                case "==": return left.Equals(right);
                case "!=": return !left.Equals(right);
                default: throw new ArgumentException("无效的-比较运算符: {0}", op);
            }
        }

        private static ScriptVariableMutationResult ExecuteCompositeAction(
            in ScriptVariableContext context, IReadOnlyList<string> param)
        {
            ScriptVariableCommands commands = Envir.CSharpScripts.VariableCommands;
            ScriptCompositeVariableCommands composite = commands.Composites;
            string command = param.Count == 0 ? string.Empty : param[0].ToUpperInvariant();
            try
            {
                switch (command)
                {
                    case "ADDTOLIST" when param.Count >= 3:
                        return commands.Mutate(context, param[1], "INC", param[2]);
                    case "INSERTTOLIST" when param.Count >= 4 && TryInteger(param[3], out int insertIndex):
                        return composite.InsertList(context, param[1], param[2], insertIndex);
                    case "REPLACELISTBYINDEX" when param.Count >= 4:
                        return commands.Mutate(context, $"{param[1]}[{param[3]}]", "MOV", param[2]);
                    case "REMOVELISTBYINDEX" when param.Count >= 3 && TryInteger(param[2], out int removeIndex):
                        return composite.RemoveListByIndex(context, param[1], removeIndex);
                    case "REMOVELISTBYCONTENT" when param.Count >= 3:
                        return composite.RemoveListByContent(
                            context, param[1], param[2], param.Count < 4 || param[3] == "1");
                    case "REVERSELIST" when param.Count >= 3:
                        return composite.ReverseList(context, param[1], param[2]);
                    case "SORTLIST" when param.Count >= 3:
                        return composite.SortList(
                            context, param[1], param[2],
                            param.Count > 3 && param[3] == "1",
                            param.Count <= 4 || param[4] != "1");
                    case "EXTRACTLIST" when param.Count >= 5 &&
                        TryInteger(param[3], out int start) && TryInteger(param[4], out int end) &&
                        (param.Count <= 5 || TryInteger(param[5], out _)):
                        return composite.SliceList(
                            context, param[1], param[2], start, end,
                            param.Count > 5 ? int.Parse(param[5], CultureInfo.InvariantCulture) : 1);
                    case "GETLISTVARINDEX" when param.Count >= 4:
                        return StoreCompositeNumber(commands, context,
                            composite.FindListIndex(context, param[1], param[2]), param[3]);
                    case "GETLISTVARCOUNT" when param.Count >= 3:
                    case "GETDICTKEYCOUNT" when param.Count >= 3:
                        return StoreCompositeNumber(commands, context, composite.Count(context, param[1]), param[2]);
                    case "GETLISTMAXVAR" when param.Count >= 3:
                    case "GETLISTMINVAR" when param.Count >= 3:
                        return StoreCompositeValue(commands, context,
                            composite.NumericExtremum(context, param[1], command == "GETLISTMAXVAR"), param[2]);
                    case "GETDICTITEMS" when param.Count >= 4:
                        return composite.DictionaryItems(context, param[1], param[3], param[2] == "1");
                    case "GETDICTMAXVALUE" when param.Count >= 4:
                    case "GETDICTMINVALUE" when param.Count >= 4:
                        {
                            ScriptCompositeResult extremum = composite.NumericExtremum(
                                context, param[1], command == "GETDICTMAXVALUE");
                            if (!extremum.Success) return CompositeFailure(extremum);
                            ScriptVariableMutationResult key = commands.Mutate(
                                context, param[2], "MOV", extremum.Diagnostic);
                            return key.Success
                                ? commands.Mutate(context, param[3], "MOV", extremum.Value.Format())
                                : key;
                        }
                    default:
                        return new ScriptVariableMutationResult(false, ScriptVariableErrorCode.InvalidExpression,
                            default, default, "复合变量命令参数不足或格式无效。");
                }
            }
            catch (FormatException)
            {
                return new ScriptVariableMutationResult(false, ScriptVariableErrorCode.InvalidExpression,
                    default, default, "复合变量命令包含无效整数参数。");
            }
        }

        private static ScriptCompositeResult EvaluateCompositeCheck(
            in ScriptVariableContext context, IReadOnlyList<string> param)
        {
            ScriptCompositeVariableCommands composite = Envir.CSharpScripts.VariableCommands.Composites;
            if (param.Count < 2)
                return new ScriptCompositeResult(false, ScriptVariableErrorCode.InvalidExpression,
                    default, 0, false, "复合变量检查参数不足。");
            return param[0].ToUpperInvariant() switch
            {
                "CHECKVARINLIST" when param.Count >= 3 => composite.Contains(context, param[1], param[2]),
                "CHECKLISTALLDIGIT" => composite.AllNumeric(context, param[1]),
                "CHECKINDICT" when param.Count >= 3 => composite.Contains(
                    context, param[1], param[2], param.Count > 3 && param[3] == "1"),
                "CHECKDICTALLDIGIT" => composite.AllNumeric(context, param[1]),
                _ => new ScriptCompositeResult(false, ScriptVariableErrorCode.InvalidExpression,
                    default, 0, false, "复合变量检查命令无效。")
            };
        }

        private static ScriptVariableMutationResult StoreCompositeNumber(
            ScriptVariableCommands commands,
            in ScriptVariableContext context,
            ScriptCompositeResult result,
            string destination) => result.Success
                ? commands.Mutate(context, destination, "MOV", result.Number.ToString(CultureInfo.InvariantCulture))
                : CompositeFailure(result);

        private static ScriptVariableMutationResult StoreCompositeValue(
            ScriptVariableCommands commands,
            in ScriptVariableContext context,
            ScriptCompositeResult result,
            string destination) => result.Success
                ? commands.Mutate(context, destination, "MOV", result.Value.Format())
                : CompositeFailure(result);

        private static ScriptVariableMutationResult CompositeFailure(ScriptCompositeResult result) =>
            new ScriptVariableMutationResult(false, result.ErrorCode, default, default, result.Diagnostic);

        private static bool TryParseLingFengPair(string text, out int x, out int y)
        {
            x = 0;
            y = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;
            int separator = text.IndexOf(':');
            return separator > 0 && separator == text.LastIndexOf(':') &&
                   int.TryParse(text.AsSpan(0, separator), NumberStyles.Integer,
                       CultureInfo.InvariantCulture, out x) &&
                   int.TryParse(text.AsSpan(separator + 1), NumberStyles.Integer,
                       CultureInfo.InvariantCulture, out y);
        }

        private static bool TryParseLingFengCommaPoint(string text, out Point point)
        {
            point = Point.Empty;
            if (string.IsNullOrWhiteSpace(text)) return false;
            int separator = text.IndexOf(',');
            if (separator <= 0 || separator != text.LastIndexOf(',') ||
                !int.TryParse(text.AsSpan(0, separator), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out int x) ||
                !int.TryParse(text.AsSpan(separator + 1), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out int y) ||
                x < 0 || y < 0)
                return false;
            point = new Point(x, y);
            return true;
        }

        private static bool TryGetLingFengAccountListPath(string sourcePath, out string listPath)
        {
            listPath = string.Empty;
            if (!Server.Scripting.LingFengScriptReferenceResolver.TryResolveCandidateTextKey(
                    sourcePath, out string key))
                return false;
            listPath = $"LingFengAccountLists/{key}";
            return true;
        }

        private static bool LingFengAccountListContainsFailClosed(
            PlayerObject player, string sourcePath)
        {
            string accountId = player?.Account?.AccountID;
            return string.IsNullOrWhiteSpace(accountId) ||
                   !TryGetLingFengAccountListPath(sourcePath, out string listPath) ||
                   Envir.Main.NameListContains(listPath, accountId);
        }

        private static bool TryAddLingFengAccountList(PlayerObject player, string sourcePath)
        {
            string accountId = player?.Account?.AccountID;
            return !string.IsNullOrWhiteSpace(accountId) &&
                   TryGetLingFengAccountListPath(sourcePath, out string listPath) &&
                   Envir.Main.AddNameToNameList(listPath, accountId);
        }

        private static bool TryRemoveLingFengAccountList(PlayerObject player, string sourcePath)
        {
            string accountId = player?.Account?.AccountID;
            return !string.IsNullOrWhiteSpace(accountId) &&
                   TryGetLingFengAccountListPath(sourcePath, out string listPath) &&
                   Envir.Main.RemoveNameFromNameList(listPath, accountId);
        }

        private static bool TryGetLingFengItemAtPosition(
            PlayerObject player, string position, out UserItem item, out int lingFengPosition)
        {
            item = null;
            lingFengPosition = -1;
            if (player?.Info == null) return false;
            if (!int.TryParse(position, NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out lingFengPosition))
                return false;
            if (lingFengPosition != -1)
                return TryGetLingFengEquipmentItem(
                    player, false, position, out _, out item);

            return TryGetLingFengLinkedItem(player, out item);
        }

        private static bool TryGetLingFengLinkedItem(PlayerObject player, out UserItem item)
        {
            item = null;
            if (player?.Info == null ||
                LingFengTxtTriggerContext.Current?.Payload is not LingFengItemTriggerEvent trigger)
                return false;
            if (trigger.Position is int inventoryPosition && inventoryPosition >= 0 &&
                inventoryPosition < player.Info.Inventory.Length &&
                player.Info.Inventory[inventoryPosition] is UserItem positioned)
            {
                item = positioned;
                return true;
            }

            item = player.Info.Inventory
                .Concat(player.Info.Equipment)
                .Where(value => value != null &&
                    value.Info.Name.Equals(trigger.ItemName, StringComparison.OrdinalIgnoreCase))
                .OrderBy(value => value.UniqueID)
                .FirstOrDefault();
            return item != null;
        }

        private static bool TryGetLingFengEquipmentItem(
            PlayerObject player, bool heroTarget, string position,
            out HumanObject owner, out UserItem item)
        {
            owner = heroTarget ? player?.Hero : player;
            item = null;
            if (owner?.Info?.Equipment == null ||
                !int.TryParse(position, NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out int lingFengPosition))
                return false;
            if (!heroTarget && lingFengPosition == -1)
                return TryGetLingFengLinkedItem(player, out item);
            if (
                !TryMapLingFengEquipmentPosition(lingFengPosition, out int equipmentIndex) ||
                equipmentIndex >= owner.Info.Equipment.Length)
                return false;
            item = owner.Info.Equipment[equipmentIndex];
            return item != null;
        }

        private static bool TryGetLingFengVisualItem(
            PlayerObject player, bool heroTarget, string position,
            out HumanObject owner, out UserItem item)
        {
            if (heroTarget)
                return TryGetLingFengEquipmentItem(player, true, position, out owner, out item);

            owner = player;
            if (!TryGetLingFengItemAtPosition(player, position, out item, out _)) return false;
            if (player?.Hero?.Info?.Equipment?.Contains(item) == true) owner = player.Hero;
            return true;
        }

        private bool TryDispatchLingFengMapEffect(IReadOnlyList<string> values)
        {
            if (values == null || values.Count is < 10 or > 12 ||
                !int.TryParse(values[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int x) ||
                !int.TryParse(values[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int y) ||
                !int.TryParse(values[3], NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out int libraryIndex) ||
                !int.TryParse(values[4], NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out int startIndex) ||
                !int.TryParse(values[5], NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out int frameCount) ||
                !int.TryParse(values[6], NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out int repeatCount) ||
                !int.TryParse(values[7], NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out int frameDelay) ||
                !int.TryParse(values[8], NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out int blendMode) ||
                !int.TryParse(values[9], NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out int light) ||
                values.Count >= 11 && !int.TryParse(values[10], NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out _) ||
                values.Count >= 12 && !int.TryParse(values[11], NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out _))
                return false;

            int effectId = values.Count >= 11
                ? int.Parse(values[10], CultureInfo.InvariantCulture)
                : -1;
            int layer = values.Count >= 12
                ? int.Parse(values[11], CultureInfo.InvariantCulture)
                : 0;
            Map map = Envir.GetMapByNameAndInstance(values[0]);
            if (map == null || x < 0 || y < 0 ||
                map.Width > 0 && x >= map.Width || map.Height > 0 && y >= map.Height ||
                libraryIndex < 0 || startIndex < 0 || frameCount is < 1 or > 1000 ||
                repeatCount is < -1 or > 100000 || frameDelay is < 1 or > 60000 ||
                blendMode is < 0 or > 1 || light is < 0 or > 5 ||
                effectId < -1 || layer is < 0 or > 2 ||
                !TryResolveLingFengEffectLibrary(libraryIndex, out string libraryName))
                return false;

            map.Broadcast(new S.LingFengMapEffect
            {
                Location = new Point(x, y),
                LibraryName = libraryName,
                StartIndex = startIndex,
                FrameCount = frameCount,
                RepeatCount = repeatCount,
                FrameDelay = frameDelay,
                Blend = blendMode == 1,
                Light = (byte)light,
                EffectId = effectId,
                Layer = (byte)layer
            }, new Point(x, y));
            return true;
        }

        private static bool TryDispatchLingFengObjectEffect(
            MapObject target, IReadOnlyList<string> values)
        {
            if (target?.CurrentMap == null || values == null || values.Count is not (6 or 9) ||
                !int.TryParse(values[0], NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out int libraryIndex) ||
                !int.TryParse(values[1], NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out int startIndex) ||
                !int.TryParse(values[2], NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out int frameCount) ||
                !int.TryParse(values[3], NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out int repeatCount) ||
                !int.TryParse(values[4], NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out int frameDelay) ||
                !int.TryParse(values[5], NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out int layer))
                return false;

            int offsetX = 0;
            int offsetY = 0;
            int normalDraw = 0;
            if (values.Count == 9 &&
                (!TryParseOptionalEffectOffset(values[6], out offsetX) ||
                 !TryParseOptionalEffectOffset(values[7], out offsetY) ||
                 !int.TryParse(values[8], NumberStyles.Integer, CultureInfo.InvariantCulture,
                     out normalDraw)))
                return false;

            if (libraryIndex < 0 || startIndex < 0 || frameCount is < 1 or > 1000 ||
                repeatCount is < -1 or > 100000 || frameDelay is < 1 or > 60000 ||
                layer is < 0 or > 2 || Math.Abs(offsetX) > 4096 || Math.Abs(offsetY) > 4096 ||
                !TryResolveLingFengEffectLibrary(libraryIndex, out string libraryName))
                return false;

            target.CurrentMap.Broadcast(new S.LingFengMapEffect
            {
                Location = target.CurrentLocation,
                AnchorObjectId = target.ObjectID,
                PixelOffset = new Point(offsetX, offsetY),
                LibraryName = libraryName,
                StartIndex = startIndex,
                FrameCount = frameCount,
                RepeatCount = repeatCount,
                FrameDelay = frameDelay,
                Blend = normalDraw == 0,
                Light = 0,
                EffectId = -1,
                Layer = (byte)layer
            }, target.CurrentLocation);
            return true;
        }

        private static bool TryParseOptionalEffectOffset(string value, out int result)
        {
            if (value == "*")
            {
                result = 0;
                return true;
            }
            return int.TryParse(value, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out result);
        }

        private static bool TryResolveLingFengEffectLibrary(int index, out string libraryName)
        {
            libraryName = string.Empty;
            if (Envir.PhysicalTextDataProvider == null ||
                !Envir.PhysicalTextDataProvider.TryGet(
                    "EffectImageList.txt", out TextFileDefinition definition) ||
                index < 0 || index >= definition.Lines.Count)
                return false;

            string file = (definition.Lines[index] ?? string.Empty).Trim().Trim('"');
            if (file.Length is < 1 or > 80 || file.IndexOfAny(['/', '\\']) >= 0 ||
                !string.Equals(Path.GetFileName(file), file, StringComparison.Ordinal))
                return false;
            libraryName = Path.GetFileNameWithoutExtension(file);
            return libraryName.Length is > 0 and <= 64;
        }

        private static bool TryCreateLingFengNpcDialogPacket(
            IReadOnlyList<string> param,
            out S.LingFengDialog packet)
        {
            packet = null;
            if (param == null || param.Count is < 3 or > 11 ||
                !int.TryParse(param[1], NumberStyles.None, CultureInfo.InvariantCulture,
                    out int libraryIndex) || libraryIndex < 0 ||
                !int.TryParse(param[2], NumberStyles.None, CultureInfo.InvariantCulture,
                    out int imageIndex) || imageIndex < 0 ||
                !TryResolveLingFengEffectLibrary(libraryIndex, out string libraryName))
                return false;

            string movable = param.Count > 3 ? param[3] : "0";
            string positionValue = param.Count > 4 ? param[4] : "4";
            string xValue = param.Count > 5 ? param[5] : "0";
            string yValue = param.Count > 6 ? param[6] : "0";
            string closeValue = param.Count > 7 ? param[7] : "1";
            string closeXValue = param.Count > 8 ? param[8] : "0";
            string closeYValue = param.Count > 9 ? param[9] : "0";
            string continueValue = param.Count > 10 ? param[10] : "0";
            if (movable is not ("0" or "1") || closeValue is not ("0" or "1") ||
                continueValue is not ("0" or "1") ||
                !int.TryParse(positionValue, NumberStyles.None, CultureInfo.InvariantCulture,
                    out int position) || position is < 0 or > 4 ||
                !int.TryParse(xValue, NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out int x) ||
                !int.TryParse(yValue, NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out int y) ||
                !int.TryParse(closeXValue, NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out int closeX) ||
                !int.TryParse(closeYValue, NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out int closeY))
                return false;

            packet = new S.LingFengDialog
            {
                DialogId = 0,
                IconPackage = libraryIndex,
                ImageIndex = imageIndex,
                Movable = movable == "1",
                X = x,
                Y = y,
                Position = position,
                NpcStyle = true,
                LibraryName = libraryName,
                ShowCloseButton = closeValue == "1",
                CloseButtonX = closeX,
                CloseButtonY = closeY,
                ContinueNpcStyle = continueValue == "1"
            };
            return true;
        }

        private static bool TryGetLingFengItemInstanceField(
            UserItem item, int position, string field, out string value)
        {
            value = string.Empty;
            if (item?.Info == null || string.IsNullOrWhiteSpace(field)) return false;
            field = field.ToUpperInvariant();
            if (field.StartsWith("UELEMENT", StringComparison.Ordinal) &&
                int.TryParse(field.AsSpan("UELEMENT".Length), NumberStyles.None,
                    CultureInfo.InvariantCulture, out int elementIndex) &&
                item.TryGetLingFengNewItemValue(elementIndex, out int elementValue))
            {
                value = elementValue.ToString(CultureInfo.InvariantCulture);
                return true;
            }
            if (field.StartsWith("VALUE", StringComparison.Ordinal) &&
                int.TryParse(field.AsSpan("VALUE".Length), NumberStyles.None,
                    CultureInfo.InvariantCulture, out int addedIndex) &&
                TryGetLingFengItemAddedValue(item, position, addedIndex, out int addedValue))
            {
                value = addedValue.ToString(CultureInfo.InvariantCulture);
                return true;
            }

            long numeric = field switch
            {
                "MAKEINDEX" => item.UniqueID <= long.MaxValue ? (long)item.UniqueID : long.MinValue,
                "IDX" => item.ItemIndex,
                "DURA" => item.CurrentDura,
                "DURAMAX" => item.MaxDura,
                "UPGRADECOUNT" => item.RefineAdded,
                "STDMODE" => (byte)item.Info.Type,
                "SHAPE" => item.LingFengShape ?? item.Info.Shape,
                "LOOKS" => item.Image,
                "COLOR" => (byte)item.Info.Grade,
                "HP" => item.Info.Stats[Stat.HP] + item.AddedStats[Stat.HP],
                "MP" => item.Info.Stats[Stat.MP] + item.AddedStats[Stat.MP],
                "LAC" => item.Info.Stats[Stat.MinAC],
                "HAC" => item.Info.Stats[Stat.MaxAC] + item.AddedStats[Stat.MaxAC],
                "LMAC" => item.Info.Stats[Stat.MinMAC],
                "HMAC" => item.Info.Stats[Stat.MaxMAC] + item.AddedStats[Stat.MaxMAC],
                "LDC" => item.Info.Stats[Stat.MinDC],
                "HDC" => item.Info.Stats[Stat.MaxDC] + item.AddedStats[Stat.MaxDC],
                "LMC" => item.Info.Stats[Stat.MinMC],
                "HMC" => item.Info.Stats[Stat.MaxMC] + item.AddedStats[Stat.MaxMC],
                "LSC" => item.Info.Stats[Stat.MinSC],
                "HSC" => item.Info.Stats[Stat.MaxSC] + item.AddedStats[Stat.MaxSC],
                _ => long.MinValue
            };
            if (numeric != long.MinValue)
            {
                value = numeric.ToString(CultureInfo.InvariantCulture);
                return true;
            }
            if (field is "NAME" or "NAME_G")
            {
                value = item.Info.Name;
                return true;
            }
            return false;
        }

        private static bool TryMapLingFengEquipmentPosition(int position, out int equipmentIndex)
        {
            equipmentIndex = position switch
            {
                0 => (int)EquipmentSlot.盔甲,
                1 => (int)EquipmentSlot.武器,
                2 => (int)EquipmentSlot.照明物,
                3 => (int)EquipmentSlot.项链,
                4 => (int)EquipmentSlot.头盔,
                5 => (int)EquipmentSlot.右手镯,
                6 => (int)EquipmentSlot.左手镯,
                7 => (int)EquipmentSlot.右戒指,
                8 => (int)EquipmentSlot.左戒指,
                9 => (int)EquipmentSlot.护身符,
                10 => (int)EquipmentSlot.腰带,
                11 => (int)EquipmentSlot.靴子,
                12 => (int)EquipmentSlot.守护石,
                13 => (int)EquipmentSlot.坐骑,
                _ => -1
            };
            return equipmentIndex >= 0;
        }

        private static int GetLingFengCustomValue(
            LingFengCustomItemAttribute attribute, int valueIndex) => valueIndex switch
        {
            0 => attribute.Value1,
            1 => attribute.Value2,
            2 => attribute.Value3,
            _ => 0
        };

        private static bool CompareLingFengInteger(int actual, string operation, int expected) =>
            operation switch
            {
                "<" => actual < expected,
                ">" => actual > expected,
                "=" or "==" => actual == expected,
                "<=" => actual <= expected,
                ">=" => actual >= expected,
                _ => false
            };

        private static bool TryGetLingFengItemAddedValue(
            UserItem item, int position, int attributeIndex, out int value)
        {
            value = 0;
            if (attributeIndex == 14)
            {
                value = item.MaxDura;
                return true;
            }
            if (!TryMapLingFengItemAddedStat(position, attributeIndex, out Stat stat, out int sign))
                return false;
            value = item.AddedStats[stat] * sign;
            return true;
        }

        private static bool TryChangeLingFengItemAddedValue(
            UserItem item, int position, int attributeIndex, string operation, int operand)
        {
            if (!TryGetLingFengItemAddedValue(item, position, attributeIndex, out int current))
                return false;
            long next = operation switch
            {
                "+" => (long)current + operand,
                "-" => (long)current - operand,
                "=" => operand,
                _ => long.MinValue
            };
            if (next is < int.MinValue or > int.MaxValue) return false;
            if (attributeIndex == 14)
            {
                if (next is < 0 or > ushort.MaxValue) return false;
                item.MaxDura = (ushort)next;
                if (item.CurrentDura > item.MaxDura) item.CurrentDura = item.MaxDura;
                return true;
            }
            if (!TryMapLingFengItemAddedStat(position, attributeIndex, out Stat stat, out int sign))
                return false;
            long stored = next * sign;
            if (stored is < int.MinValue or > int.MaxValue) return false;
            item.AddedStats[stat] = (int)stored;
            return true;
        }

        private static bool TryMapLingFengItemAddedStat(
            int position, int attributeIndex, out Stat stat, out int sign)
        {
            sign = 1;
            if (position == 1)
            {
                stat = attributeIndex switch
                {
                    0 => Stat.MaxDC,
                    1 => Stat.MaxMC,
                    2 => Stat.MaxSC,
                    3 => Stat.Luck,
                    4 => Stat.Luck,
                    5 => Stat.Accuracy,
                    6 => Stat.AttackSpeed,
                    7 => Stat.Strong,
                    _ => default
                };
                if (attributeIndex == 4) sign = -1;
                return attributeIndex is >= 0 and <= 7;
            }
            stat = attributeIndex switch
            {
                0 => Stat.MaxAC,
                1 => Stat.MaxMAC,
                2 => Stat.MaxDC,
                3 => Stat.MaxMC,
                4 => Stat.MaxSC,
                _ => default
            };
            return attributeIndex is >= 0 and <= 4;
        }

        private static bool TryGetLingFengItemField(ItemInfo item, string field, out long value)
        {
            value = field switch
            {
                "IDX" => item.Index,
                "STDMODE" => (byte)item.Type,
                "SHAPE" => item.Shape,
                "LOOKS" => item.Image,
                "COLOR" => (byte)item.Grade,
                "DC2" => item.Stats[Stat.MaxDC],
                "MC2" => item.Stats[Stat.MaxMC],
                "SC2" => item.Stats[Stat.MaxSC],
                _ => 0
            };
            return field is "IDX" or "STDMODE" or "SHAPE" or "LOOKS" or "COLOR" or
                "DC2" or "MC2" or "SC2";
        }

        private static bool TryInteger(string text, out int value) =>
            int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);

        private static string ResolveOwnVariableOperand(PlayerObject player, string text)
        {
            if (player == null || !ScriptVariableReferenceParser.TryParse(text, out _)) return text;
            ScriptVariableTextResult result = Envir.CSharpScripts.VariableCommands.Format(
                ScriptVariableContext.ForConversation(player, player.NPCObjectID, player.CurrentMap), text);
            return result.Success ? result.Text : text;
        }

        public static int Calculate(string op, int left, int right)
        {
            switch (op)
            {
                case "+": return left + right;
                case "-": return left - right;
                case "*": return left * right;
                case "/": return left / right;
                default: throw new ArgumentException("无效的-和运算符: {0}", op);
            }
        }
    }
}
