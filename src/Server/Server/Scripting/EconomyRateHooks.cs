using System;
using Server.MirEnvir;
using Server.MirObjects;

namespace Server.Scripting
{
    public enum EconomyRateType : byte
    {
        Experience = 1,
        Drop = 2,
    }

    public sealed class EconomyRateRequest
    {
        public EconomyRateRequest(
            EconomyRateType type,
            PlayerObject player,
            string source,
            uint amount,
            uint targetLevel,
            float baseRate,
            float rate)
        {
            Type = type;
            Player = player;
            Source = source ?? string.Empty;
            Amount = amount;
            TargetLevel = targetLevel;
            BaseRate = baseRate;
            Rate = rate;
        }

        public EconomyRateType Type { get; }

        public PlayerObject Player { get; }

        /// <summary>
        /// 来源标识（例如：kill/dragon/fishing/npcdrop/strongbox 等；仅用于脚本自行判别）。
        /// </summary>
        public string Source { get; }

        /// <summary>
        /// 原始数值（经验：怪物基础经验；掉率：一般为 0）。
        /// </summary>
        public uint Amount { get; }

        /// <summary>
        /// 目标等级（经验：击杀目标等级；掉率：一般为 0）。
        /// </summary>
        public uint TargetLevel { get; }

        public float BaseRate { get; }

        public float Rate { get; set; }

        public string FailMessage { get; set; } = string.Empty;

        public ScriptHookDecision Decision { get; set; } = ScriptHookDecision.Continue;
    }

    public sealed class EconomyRateResult
    {
        public EconomyRateResult(
            EconomyRateType type,
            PlayerObject player,
            string source,
            uint amount,
            uint targetLevel,
            float baseRate,
            float rate,
            bool executedLegacy,
            ScriptHookDecision decision)
        {
            Type = type;
            Player = player;
            Source = source ?? string.Empty;
            Amount = amount;
            TargetLevel = targetLevel;
            BaseRate = baseRate;
            Rate = rate;
            ExecutedLegacy = executedLegacy;
            Decision = decision;
        }

        public EconomyRateType Type { get; }

        public PlayerObject Player { get; }

        public string Source { get; }

        public uint Amount { get; }

        public uint TargetLevel { get; }

        public float BaseRate { get; }

        public float Rate { get; }

        public bool ExecutedLegacy { get; }

        public ScriptHookDecision Decision { get; }
    }

    public static class EconomyRateHooks
    {
        public static EconomyRateRequest ResolveExperienceRate(PlayerObject player, uint amount, uint targetLevel, string source = "kill")
        {
            return Resolve(player, EconomyRateType.Experience, Settings.ExpRate, source, amount, targetLevel);
        }

        public static EconomyRateRequest ResolveDropRate(PlayerObject player, string source = "monster")
        {
            return Resolve(player, EconomyRateType.Drop, Settings.DropRate, source, 0, 0);
        }

        private static EconomyRateRequest Resolve(
            PlayerObject player,
            EconomyRateType type,
            float baseRate,
            string source,
            uint amount,
            uint targetLevel)
        {
            var request = new EconomyRateRequest(type, player, source, amount, targetLevel, baseRate, baseRate);

            var scriptsRuntimeActive = Settings.CSharpScriptsEnabled && Envir.Main.CSharpScripts.Enabled;
            var allowBefore = scriptsRuntimeActive &&
                              ScriptDispatchPolicy.ShouldTryCSharp(ScriptHookKeys.OnEconomyRateBefore) &&
                              ScriptDispatchPolicy.ShouldTryCSharp(ScriptHookKeys.OnEconomyRateBeforeType(type));
            var allowAfter = scriptsRuntimeActive &&
                             ScriptDispatchPolicy.ShouldTryCSharp(ScriptHookKeys.OnEconomyRateAfter) &&
                             ScriptDispatchPolicy.ShouldTryCSharp(ScriptHookKeys.OnEconomyRateAfterType(type));

            if (allowBefore)
            {
                Envir.Main.CSharpScripts.TryHandleEconomyRateBefore(request);
            }

            if (request.Rate < 0)
                request.Rate = 0;

            if (allowAfter)
            {
                var result = new EconomyRateResult(
                    type,
                    player,
                    source,
                    amount,
                    targetLevel,
                    request.BaseRate,
                    request.Rate,
                    request.Decision == ScriptHookDecision.Continue,
                    request.Decision);

                Envir.Main.CSharpScripts.TryHandleEconomyRateAfter(result);
            }

            return request;
        }
    }
}

