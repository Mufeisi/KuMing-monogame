using Server.MirEnvir;
using Server.MirDatabase;
using Server.MirObjects;

namespace Server.Scripting.Variables
{
    public readonly struct VariableProcessSmokeResult
    {
        public VariableProcessSmokeResult(bool success, int exitCode, string message)
        {
            Success = success;
            ExitCode = exitCode;
            Message = message ?? string.Empty;
        }

        public bool Success { get; }
        public int ExitCode { get; }
        public string Message { get; }
    }

    /// <summary>
    /// 供正式服务端宿主和 CI 调用的变量专项进程冒烟入口。
    /// 只暴露最终结果，不公开服务器内部生命周期控制面。
    /// </summary>
    public static class VariableProcessSmoke
    {
        public static VariableProcessSmokeResult Run(Envir envir)
        {
            if (envir == null) throw new ArgumentNullException(nameof(envir));

            string scriptsRoot = Path.Combine(
                Path.GetTempPath(), "LyoCrystalVariableSmoke-" + Guid.NewGuid().ToString("N"));
            string scriptPath = Path.Combine(scriptsRoot, "Variables.cs");
            bool oldEnabled = Settings.CSharpScriptsEnabled;
            string oldPath = Settings.CSharpScriptsPath;
            bool oldHotReload = Settings.CSharpScriptsHotReloadEnabled;
            bool oldPushMode = Settings.CSharpScriptsPushModeEnabled;
            bool oldTxtFallback = Settings.CSharpScriptsFallbackToTxt;
            string cleanupWarning = string.Empty;

            try
            {
                Directory.CreateDirectory(scriptsRoot);
                WriteScript(scriptPath, "Decimal", "1.5", includeBonus: false);
                Settings.CSharpScriptsEnabled = true;
                Settings.CSharpScriptsPath = scriptsRoot;
                Settings.CSharpScriptsHotReloadEnabled = false;
                Settings.CSharpScriptsPushModeEnabled = false;
                Settings.CSharpScriptsFallbackToTxt = true;

                envir.Start(new EnvirStartOptions
                {
                    EnforceProductionSecurity = false,
                    LoadResources = false,
                    BindNetwork = false,
                    StartScripts = true,
                    StartHttp = false,
                    SaveOnStop = false,
                    Multithreaded = false,
                });
                if (!SpinWait.SpinUntil(
                        () => envir.StartState is EnvirStartState.Ready or EnvirStartState.Failed,
                        TimeSpan.FromSeconds(10)) || envir.StartState != EnvirStartState.Ready)
                    return Failure(3, $"VARIABLE_SMOKE_START_FAILED={envir.StartFailure}");

                ScriptVariableCommands commands = envir.CSharpScripts.VariableCommands;
                var player = new PlayerObject
                {
                    NPCObjectID = 100,
                    Info = new CharacterInfo { Name = "变量冒烟角色", Heroes = new HeroInfo[1] }
                };
                var firstMap = new Map(new MapInfo { Index = 1 });
                var secondMap = new Map(new MapInfo { Index = 2 });
                var callFrame = new object();
                var context = ScriptVariableContext.ForConversation(
                    player, player.NPCObjectID, firstMap, callFrame);
                var page = new NPCPage("[@VARIABLESMOKE]");
                var actions = new NPCSegment(
                    page, new List<string>(), new List<string>(), new List<string>(),
                    new List<string>(), new List<string>());
                actions.ParseAct(actions.ActList, "MOV P0 7");
                actions.ParseAct(actions.ActList, "DIV P0 2");
                actions.ParseAct(actions.ActList, "MOV P.Rate 12.5");
                actions.ParseAct(actions.ActList, "INC P.Rate 0.25");
                actions.ParseAct(actions.ActList, "MOV D0 11");
                actions.ParseAct(actions.ActList, "MOV N$Score 33");
                actions.ParseAct(actions.ActList, "MOV S$Label 在线");
                actions.ParseAct(actions.ActList, "MOV I0 44");
                actions.ParseAct(actions.ActList, "MOV U0 55");
                actions.ParseAct(actions.ActList, "MOV T0 永久文本");
                actions.ParseAct(actions.ActList, "MOV U.PersistentRate 6.25");
                actions.ParseAct(actions.ActList, "MOV G0 66");
                actions.ParseAct(actions.ActList, "MOV G.EventRate 2.5");
                actions.ParseAct(actions.ActList, "MOV A0 固定公告");
                actions.ParseAct(actions.ActList, "MOV A.Notice 全服公告");
                actions.ParseAct(actions.ActList, "MOV J0 77");
                actions.ParseAct(actions.ActList, "MOV Z0 今日文本");
                actions.ParseAct(actions.ActList, "MOV HUMAN.Lifetime 8.5");
                actions.ParseAct(actions.ActList, "MOV GLOBAL.Score 99");
                if (!actions.Check(player))
                    return Failure(4, "VARIABLE_SMOKE_TXT_ACTION_FAILED");
                AssertResult(commands.Mutate(context, "M0", "MOV", "22").Success,
                    "VARIABLE_SMOKE_MAP_COMMAND_FAILED");

                var display = new NPCSegment(
                    page,
                    new List<string> { "整数<$STR(P0)> 小数<$FORMAT(P.Rate,2)>" },
                    new List<string>(), new List<string>(), new List<string>(), new List<string>());
                display.ParseCheck("CHECK P.Rate >= 12.75");
                if (!display.Check(player) ||
                    commands.Format(context, "P0").Text != "3" ||
                    commands.Format(context, "P.Rate", 2).Text != "12.75" ||
                    commands.Format(context, "D0").Text != "11" ||
                    commands.Format(context, "M0").Text != "22" ||
                    commands.Format(context, "N$Score").Text != "33" ||
                    commands.Format(context, "S$Label").Text != "在线" ||
                    commands.Format(context, "I0").Text != "44" ||
                    commands.Format(context, "U0").Text != "55" ||
                    commands.Format(context, "T0").Text != "永久文本" ||
                    commands.Format(context, "U.PersistentRate", 2).Text != "6.25" ||
                    commands.Format(context, "G0").Text != "66" ||
                    commands.Format(context, "G.EventRate", 2).Text != "2.50" ||
                    commands.Format(context, "A0").Text != "固定公告" ||
                    commands.Format(context, "A.Notice").Text != "全服公告" ||
                    commands.Format(context, "J0").Text != "77" ||
                    commands.Format(context, "Z0").Text != "今日文本" ||
                    commands.Format(context, "HUMAN.Lifetime", 2).Text != "8.50" ||
                    commands.Format(context, "GLOBAL.Score").Text != "99" ||
                    !player.NPCSpeech.Any(line => line.Contains("整数3 小数12.75", StringComparison.Ordinal)))
                    return Failure(4, "VARIABLE_SMOKE_COMMAND_FAILED");

                var guild = new GuildInfo { GuildIndex = 900, Name = "变量冒烟行会" };
                var guildContext = ScriptVariableContext.ForPlayer(guild);
                AssertResult(commands.Mutate(guildContext, "GUILD.Score", "MOV", "123").Success &&
                             commands.Format(guildContext, "GUILD.Score").Text == "123" && guild.NeedSave,
                    "VARIABLE_SMOKE_GUILD_SCOPE_FAILED");

                long nextDailyPeriod = player.Info.ScriptVariables.DailyResetPeriodId + 1;
                player.Info.ScriptVariables.EnsureDailyPeriod(nextDailyPeriod);
                AssertResult(commands.Format(context, "J0").Text == "0" &&
                             commands.Format(context, "Z0").Text == string.Empty &&
                             commands.Format(context, "HUMAN.Lifetime", 2).Text == "8.50",
                    "VARIABLE_SMOKE_DAILY_RESET_FAILED");

                AssertResult(commands.Mutate(context, "Call.Rate", "MOV", "4.75").Success,
                    "VARIABLE_SMOKE_CALL_FRAME_FAILED");
                var otherFrame = ScriptVariableContext.ForConversation(
                    player, player.NPCObjectID, firstMap, new object());
                AssertResult(commands.Format(otherFrame, "Call.Rate").Text == "4.5",
                    "VARIABLE_SMOKE_CALL_FRAME_ISOLATION_FAILED");

                var nextMapContext = ScriptVariableContext.ForConversation(
                    player, player.NPCObjectID, secondMap, callFrame);
                AssertResult(!envir.CSharpScripts.VariableModule.Read(
                        nextMapContext, ScriptVariableReference.Legacy(ScriptVariableScope.M, 0)).Found &&
                    commands.Format(nextMapContext, "D0").Text == "11" &&
                    commands.Format(nextMapContext, "N$Score").Text == "33",
                    "VARIABLE_SMOKE_MAP_LIFECYCLE_FAILED");

                if (!envir.CSharpScripts.VariableModule
                        .Reset(nextMapContext, ScriptVariableSelector.Conversation()).Success ||
                    commands.Format(nextMapContext, "P.Rate").Text != "1.5")
                    return Failure(5, "VARIABLE_SMOKE_RESET_FAILED");

                player.StopGame(0);
                AssertResult(
                    !envir.CSharpScripts.VariableModule.Read(
                        nextMapContext, ScriptVariableReference.Legacy(ScriptVariableScope.D, 0)).Found &&
                    !envir.CSharpScripts.VariableModule.Read(
                        nextMapContext, ScriptVariableReference.Named(ScriptVariableScope.N, "Score")).Found &&
                    !envir.CSharpScripts.VariableModule.Read(
                        nextMapContext, ScriptVariableReference.Named(ScriptVariableScope.S, "Label")).Found &&
                    commands.Format(nextMapContext, "I0").Text == "44" &&
                    commands.Format(nextMapContext, "U0").Text == "55" &&
                    commands.Format(nextMapContext, "T0").Text == "永久文本" &&
                    commands.Format(nextMapContext, "G0").Text == "66" &&
                    commands.Format(nextMapContext, "A0").Text == "固定公告" &&
                    commands.Format(nextMapContext, "A.Notice").Text == "全服公告",
                    "VARIABLE_SMOKE_LOGOFF_LIFECYCLE_FAILED");

                long compatibleVersion = envir.CSharpScripts.Version;
                WriteScript(scriptPath, "Decimal", "2.0", includeBonus: true);
                envir.CSharpScripts.Reload();
                if (envir.CSharpScripts.Version <= compatibleVersion ||
                    commands.Format(context, "P.Bonus").Text != "0.5")
                    return Failure(6, $"VARIABLE_SMOKE_RELOAD_FAILED={envir.CSharpScripts.LastError}");

                long rejectedVersion = envir.CSharpScripts.Version;
                WriteScript(scriptPath, "Integer", "1", includeBonus: false);
                envir.CSharpScripts.Reload();
                if (envir.CSharpScripts.Version != rejectedVersion ||
                    envir.CSharpScripts.CurrentRegistry.VariableDeclarations
                        .GetRequired(ScriptVariableScope.P, "Rate").Kind != ScriptVariableKind.Decimal)
                    return Failure(7, "VARIABLE_SMOKE_CONFLICT_NOT_REJECTED");

                string serverVariablesPath = Path.Combine(scriptsRoot, "Server.Variables.json");
                envir.ScriptVariables.SaveJson(serverVariablesPath);
                var diskRestored = new ServerScriptVariableStore();
                diskRestored.LoadJson(serverVariablesPath);
                if (!diskRestored.TryGet(ScriptVariableScope.G, "EVENTRATE", out var diskRate) ||
                    diskRate.Format(2) != "2.50" ||
                    !diskRestored.TryGet(ScriptVariableScope.A, "#0", out var diskFixedNotice) ||
                    diskFixedNotice.Text != "固定公告" ||
                    !diskRestored.TryGet(ScriptVariableScope.A, "NOTICE", out var diskNotice) ||
                    diskNotice.Text != "全服公告" ||
                    !diskRestored.TryGet(ScriptVariableScope.Global, "SCORE", out var diskGlobal) ||
                    diskGlobal.Integer != 99)
                    return Failure(8, "VARIABLE_SMOKE_SERVER_DISK_PERSISTENCE_FAILED");

                envir.Stop();
                envir.Start(new EnvirStartOptions
                {
                    EnforceProductionSecurity = false,
                    LoadResources = false,
                    BindNetwork = false,
                    StartScripts = true,
                    StartHttp = false,
                    SaveOnStop = false,
                    Multithreaded = false,
                });
                if (!SpinWait.SpinUntil(
                        () => envir.StartState is EnvirStartState.Ready or EnvirStartState.Failed,
                        TimeSpan.FromSeconds(10)) || envir.StartState != EnvirStartState.Ready ||
                    envir.CSharpScripts.VariableModule.Read(
                        ScriptVariableContext.ForServer(),
                        ScriptVariableReference.Legacy(ScriptVariableScope.I, 0)).Found)
                    return Failure(9, "VARIABLE_SMOKE_SERVER_RESTART_LIFECYCLE_FAILED");

                var restartedContext = ScriptVariableContext.ForPlayer(player);
                if (envir.CSharpScripts.VariableCommands.Format(restartedContext, "U0").Text != "55" ||
                    envir.CSharpScripts.VariableCommands.Format(restartedContext, "T0").Text != "永久文本" ||
                    envir.CSharpScripts.VariableCommands.Format(restartedContext, "U.PersistentRate", 2).Text != "6.25" ||
                    envir.CSharpScripts.VariableCommands.Format(restartedContext, "G0").Text != "66" ||
                    envir.CSharpScripts.VariableCommands.Format(restartedContext, "G.EventRate", 2).Text != "2.50" ||
                    envir.CSharpScripts.VariableCommands.Format(restartedContext, "A0").Text != "固定公告" ||
                    envir.CSharpScripts.VariableCommands.Format(restartedContext, "A.Notice").Text != "全服公告" ||
                    envir.CSharpScripts.VariableCommands.Format(restartedContext, "HUMAN.Lifetime", 2).Text != "8.50" ||
                    envir.CSharpScripts.VariableCommands.Format(restartedContext, "GLOBAL.Score").Text != "99" ||
                    envir.CSharpScripts.VariableCommands.Format(restartedContext, "J0").Text != "0")
                    return Failure(10, "VARIABLE_SMOKE_PERSISTENCE_FAILED");

                return new VariableProcessSmokeResult(
                    true,
                    0,
                    $"VARIABLE_SMOKE_OK;VERSION={rejectedVersion};INTEGER=3;DECIMAL=12.75;" +
                    "RESET=1.5;BONUS=0.5;RUNTIME_SCOPES=True;PRIVATE_PERSISTENCE=True;SERVER_PERSISTENCE=True;SERVER_RESTART_CLEAR=True;" +
                    "DAILY_RESET=True;CUSTOM_PERSISTENT_SCOPES=True;CONFLICT_REJECTED=True");
            }
            catch (Exception ex)
            {
                return Failure(1, $"VARIABLE_SMOKE_EXCEPTION={ex}");
            }
            finally
            {
                if (envir.Running) envir.Stop();
                Settings.CSharpScriptsEnabled = oldEnabled;
                Settings.CSharpScriptsPath = oldPath;
                Settings.CSharpScriptsHotReloadEnabled = oldHotReload;
                Settings.CSharpScriptsPushModeEnabled = oldPushMode;
                Settings.CSharpScriptsFallbackToTxt = oldTxtFallback;
                try
                {
                    if (Directory.Exists(scriptsRoot)) Directory.Delete(scriptsRoot, recursive: true);
                }
                catch (Exception cleanupError)
                {
                    cleanupWarning = cleanupError.Message;
                }

                if (!string.IsNullOrEmpty(cleanupWarning))
                    MessageQueue.Instance.Enqueue($"[Variables][Smoke] 临时目录清理失败：{cleanupWarning}");
            }
        }

        private static VariableProcessSmokeResult Failure(int exitCode, string message) =>
            new VariableProcessSmokeResult(false, exitCode, message);

        private static void AssertResult(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private static void WriteScript(string path, string kind, string defaultValue, bool includeBonus)
        {
            string bonus = includeBonus
                ? "registry.RegisterVariable(ScriptVariableScope.P, \"Bonus\", ScriptVariableKind.Decimal, \"0.5\");"
                : string.Empty;
            File.WriteAllText(path, $$"""
                using Server.Scripting;
                using Server.Scripting.Variables;

                public sealed class VariableSmokeDeclarations : IScriptModule
                {
                    public void Register(ScriptRegistry registry)
                    {
                        registry.RegisterVariable(
                            ScriptVariableScope.P, "Rate", ScriptVariableKind.{{kind}}, "{{defaultValue}}");
                        registry.RegisterVariable(
                            ScriptVariableScope.Call, "Rate", ScriptVariableKind.Decimal, "4.5");
                        registry.RegisterVariable(
                            ScriptVariableScope.U, "PersistentRate", ScriptVariableKind.Decimal, "1.0");
                        registry.RegisterVariable(
                            ScriptVariableScope.G, "EventRate", ScriptVariableKind.Decimal, "1.0");
                        registry.RegisterVariable(
                            ScriptVariableScope.A, "Notice", ScriptVariableKind.String, "未开放");
                        registry.RegisterVariable(
                            ScriptVariableScope.Human, "Lifetime", ScriptVariableKind.Decimal, "0");
                        registry.RegisterVariable(
                            ScriptVariableScope.Guild, "Score", ScriptVariableKind.Integer, "0");
                        registry.RegisterVariable(
                            ScriptVariableScope.Global, "Score", ScriptVariableKind.Integer, "0");
                        {{bonus}}
                    }
                }
                """);
        }
    }
}
