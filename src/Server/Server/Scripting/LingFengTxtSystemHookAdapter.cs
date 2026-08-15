using System;
using System.Collections.Generic;
using Server.MirEnvir;
using Server.MirObjects;

namespace Server.Scripting
{
    public readonly record struct LingFengTxtHookTarget(string ScriptKey, string Label);

    public static class LingFengTxtSystemHookAdapter
    {
        [ThreadStatic]
        private static HashSet<string> _activeTargets;

        private static readonly IReadOnlyDictionary<string, LingFengTxtHookTarget> Targets =
            new Dictionary<string, LingFengTxtHookTarget>(StringComparer.Ordinal)
            {
                [ScriptHookKeys.OnPlayerLogin] = new("SystemScripts/QManage", "[@LOGIN]"),
                [ScriptHookKeys.OnPlayerLevelUp] = new("SystemScripts/QFunction-0", "[@PLAYLEVELUP]")
            };

        public static bool TryResolve(string hookKey, out LingFengTxtHookTarget target) =>
            Targets.TryGetValue(hookKey ?? string.Empty, out target);

        public static bool IsCompatibilityEnabled(ITextFileProvider provider) =>
            provider != null &&
            Settings.TxtScriptsEnabled &&
            Settings.TxtScriptsCompatibilityVersion.StartsWith("LFM2-", StringComparison.OrdinalIgnoreCase);

        public static bool TryDispatchAfterCSharp(
            bool cSharpHandled,
            ITextFileProvider provider,
            string hookKey,
            Func<LingFengTxtHookTarget, bool> execute,
            bool cSharpEligible = false)
        {
            if (cSharpHandled || provider == null || execute == null) return cSharpHandled;
            if (!IsCompatibilityEnabled(provider))
                return false;
            if (!TryResolve(hookKey, out LingFengTxtHookTarget target)) return false;
            if (!CanDispatchTxt(cSharpEligible, target.ScriptKey)) return false;

            TextFileDefinition definition = provider.GetByKey(target.ScriptKey);
            if (definition == null || !ContainsLabel(definition, target.Label)) return false;
            return execute(target);
        }

        public static bool TryDispatchPlayerDamageBefore(
            bool cSharpInvoked,
            ITextFileProvider provider,
            PlayerObject player,
            PlayerDamageRequest request,
            bool cSharpEligible = false)
        {
            string label = request?.Perspective == PlayerDamagePerspective.Outgoing
                ? "[@ATTACKDAMAGE]"
                : "[@STRUCKDAMAGE]";
            return TryDispatchTarget(cSharpInvoked, provider, player, request,
                cSharpEligible,
                new LingFengTxtHookTarget("SystemScripts/QFunction-0", label));
        }

        public static bool TryDispatchPlayerDamageAfter(
            bool cSharpInvoked,
            ITextFileProvider provider,
            PlayerObject player,
            PlayerDamageResult result,
            bool cSharpEligible = false)
        {
            string label = result?.Perspective == PlayerDamagePerspective.Outgoing
                ? "[@ATTACK]"
                : "[@STRUCK]";
            return TryDispatchTarget(cSharpInvoked, provider, player, result,
                cSharpEligible,
                new LingFengTxtHookTarget("SystemScripts/QFunction-0", label));
        }

        public static bool TryDispatchPlayerItemPickupAfter(
            bool cSharpInvoked,
            ITextFileProvider provider,
            PlayerObject player,
            PlayerItemPickupResult result,
            bool cSharpEligible = false) =>
            TryDispatchTarget(cSharpInvoked, provider, player, result, cSharpEligible,
                new LingFengTxtHookTarget("SystemScripts/QFunction-0", "[@PICKUPITEMEX]"));

        public static bool TryDispatchMonsterDie(
            bool cSharpInvoked,
            ITextFileProvider provider,
            MonsterObject monster,
            bool cSharpEligible = false)
        {
            PlayerObject player = ResolvePlayer(monster?.EXPOwner);
            return TryDispatchTarget(cSharpInvoked, provider, player, monster,
                cSharpEligible,
                new LingFengTxtHookTarget("SystemScripts/QFunction-0", "[@KILLMON]"));
        }

        public static bool TryDispatchMonsterDropAfter(
            bool cSharpInvoked,
            ITextFileProvider provider,
            MonsterDropResult result,
            bool cSharpEligible = false)
        {
            if (result == null || result.DroppedGold == 0 && result.DroppedItems.Count == 0)
                return cSharpInvoked;
            PlayerObject player = ResolvePlayer(result?.ExpOwner) ?? ResolvePlayer(result?.DropOwner);
            return TryDispatchTarget(cSharpInvoked, provider, player, result,
                cSharpEligible,
                new LingFengTxtHookTarget("SystemScripts/QFunction-0", "[@M2DROPITEM]"));
        }

        private static bool TryDispatchTarget(
            bool cSharpInvoked,
            ITextFileProvider provider,
            PlayerObject player,
            object payload,
            bool cSharpEligible,
            LingFengTxtHookTarget target)
        {
            if (cSharpInvoked || player == null || !IsCompatibilityEnabled(provider)) return cSharpInvoked;
            if (!CanDispatchTxt(cSharpEligible, target.ScriptKey)) return false;
            TextFileDefinition definition = provider.GetByKey(target.ScriptKey);
            if (definition == null || !ContainsLabel(definition, target.Label)) return false;

            string activeKey = target.ScriptKey + "|" + target.Label;
            try
            {
                return Envir.Main.InvokeOnMainThread(() =>
                {
                    _activeTargets ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    if (!_activeTargets.Add(activeKey))
                    {
                        MessageQueue.Instance.Enqueue($"[TxtScripts] 特殊触发重入已阻止：{activeKey}");
                        return false;
                    }

                    try
                    {
                        using (LingFengTxtTriggerContext.Push(payload))
                        {
                            NPCScript script = NPCScript.GetOrAdd(0, target.ScriptKey, NPCScriptType.Called);
                            return script.CallSystem(player, target.Label);
                        }
                    }
                    finally
                    {
                        _activeTargets.Remove(activeKey);
                    }
                });
            }
            catch (Exception ex)
            {
                MessageQueue.Instance.Enqueue($"[TxtScripts] 特殊触发执行失败：{activeKey} {ex}");
                return false;
            }
        }

        private static PlayerObject ResolvePlayer(MapObject mapObject) => mapObject switch
        {
            PlayerObject player => player,
            HeroObject hero => hero.Owner,
            _ => null
        };

        public static bool TryDispatchAfterCSharp(
            bool cSharpHandled,
            ITextFileProvider provider,
            string hookKey,
            PlayerObject player,
            bool cSharpEligible = false)
        {
            if (player == null) return cSharpHandled;
            if (cSharpHandled || !IsCompatibilityEnabled(provider)) return cSharpHandled;
            if (!TryResolve(hookKey, out LingFengTxtHookTarget target)) return false;
            return TryDispatchTarget(false, provider, player, player, cSharpEligible, target);
        }

        private static bool CanDispatchTxt(bool cSharpEligible, string scriptKey) =>
            !cSharpEligible || TxtFallbackPolicy.ShouldFallbackToTxt(scriptKey);

        private static bool ContainsLabel(TextFileDefinition definition, string label)
        {
            foreach (string line in definition.Lines)
            {
                if (line.Trim().Equals(label, StringComparison.OrdinalIgnoreCase)) return true;
            }

            return false;
        }
    }
}
