using Server.MirEnvir;
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
                var player = new PlayerObject { NPCObjectID = 100 };
                var context = ScriptVariableContext.ForConversation(player, player.NPCObjectID);
                var page = new NPCPage("[@VARIABLESMOKE]");
                var actions = new NPCSegment(
                    page, new List<string>(), new List<string>(), new List<string>(),
                    new List<string>(), new List<string>());
                actions.ParseAct(actions.ActList, "MOV P0 7");
                actions.ParseAct(actions.ActList, "DIV P0 2");
                actions.ParseAct(actions.ActList, "MOV P.Rate 12.5");
                actions.ParseAct(actions.ActList, "INC P.Rate 0.25");
                if (!actions.Check(player))
                    return Failure(4, "VARIABLE_SMOKE_TXT_ACTION_FAILED");

                var display = new NPCSegment(
                    page,
                    new List<string> { "整数<$STR(P0)> 小数<$FORMAT(P.Rate,2)>" },
                    new List<string>(), new List<string>(), new List<string>(), new List<string>());
                display.ParseCheck("CHECK P.Rate >= 12.75");
                if (!display.Check(player) ||
                    commands.Format(context, "P0").Text != "3" ||
                    commands.Format(context, "P.Rate", 2).Text != "12.75" ||
                    !player.NPCSpeech.Any(line => line.Contains("整数3 小数12.75", StringComparison.Ordinal)))
                    return Failure(4, "VARIABLE_SMOKE_COMMAND_FAILED");

                if (!envir.CSharpScripts.VariableModule
                        .Reset(context, ScriptVariableSelector.Conversation()).Success ||
                    commands.Format(context, "P.Rate").Text != "1.5")
                    return Failure(5, "VARIABLE_SMOKE_RESET_FAILED");

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

                return new VariableProcessSmokeResult(
                    true,
                    0,
                    $"VARIABLE_SMOKE_OK;VERSION={rejectedVersion};INTEGER=3;DECIMAL=12.75;" +
                    "RESET=1.5;BONUS=0.5;CONFLICT_REJECTED=True");
            }
            catch (Exception ex)
            {
                return Failure(1, $"VARIABLE_SMOKE_EXCEPTION={ex.GetType().Name}:{ex.Message}");
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
                        {{bonus}}
                    }
                }
                """);
        }
    }
}
