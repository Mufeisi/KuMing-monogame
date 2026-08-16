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
            LingFengDamageEvent payload = Snapshot(request);
            return TryDispatchTarget(cSharpInvoked, provider, player, payload,
                cSharpEligible,
                new LingFengTxtHookTarget("SystemScripts/QFunction-0", label), request);
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
            LingFengDamageEvent payload = Snapshot(result);
            return TryDispatchTarget(cSharpInvoked, provider, player, payload,
                cSharpEligible,
                new LingFengTxtHookTarget("SystemScripts/QFunction-0", label));
        }

        public static bool TryDispatchPlayerItemPickupAfter(
            bool cSharpInvoked,
            ITextFileProvider provider,
            PlayerObject player,
            PlayerItemPickupResult result,
            bool cSharpEligible = false)
        {
            string itemName = result?.Item?.FriendlyName;
            if (string.IsNullOrEmpty(itemName) && result?.Gold > 0) itemName = "金币";
            var payload = new LingFengItemTriggerEvent(
                LingFengItemTriggerKind.Pickup,
                itemName ?? string.Empty,
                null,
                result?.Gold ?? 0);
            return TryDispatchTarget(cSharpInvoked, provider, player, payload, cSharpEligible,
                new LingFengTxtHookTarget("SystemScripts/QFunction-0", "[@PICKUPITEMEX]"));
        }

        public static bool TryDispatchMonsterDie(
            bool cSharpInvoked,
            ITextFileProvider provider,
            MonsterObject monster,
            bool cSharpEligible = false)
        {
            PlayerObject player = monster?.LingFengLastDamageActorKind == LingFengCombatActorKind.Unknown
                ? ResolvePlayer(monster.EXPOwner)
                : Envir.Main.GetPlayer(monster.LingFengLastDamageOwnerName);
            var payload = monster == null
                ? default
                : new LingFengMonsterKillEvent(
                    monster.Info?.Name ?? string.Empty,
                    monster.CurrentLocation.X,
                    monster.CurrentLocation.Y,
                    monster.Experience,
                    monster.LingFengLastDamageActorKind);
            return TryDispatchTarget(cSharpInvoked, provider, player, payload,
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
            string itemName = result.DroppedItems.FirstOrDefault()?.FriendlyName;
            if (string.IsNullOrEmpty(itemName) && result.DroppedGold > 0) itemName = "金币";
            var payload = new LingFengItemTriggerEvent(
                LingFengItemTriggerKind.Drop,
                itemName ?? string.Empty,
                null,
                result.DroppedGold);
            return TryDispatchTarget(cSharpInvoked, provider, player, payload,
                cSharpEligible,
                new LingFengTxtHookTarget("SystemScripts/QFunction-0", "[@M2DROPITEM]"));
        }

        private static bool TryDispatchTarget(
            bool cSharpInvoked,
            ITextFileProvider provider,
            PlayerObject player,
            object payload,
            bool cSharpEligible,
            LingFengTxtHookTarget target,
            PlayerDamageRequest damageRequest = null)
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
                        using (damageRequest == null
                                   ? LingFengTxtTriggerContext.Push(payload)
                                   : LingFengTxtTriggerContext.PushDamage((LingFengDamageEvent)payload, damageRequest))
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

        internal static bool TryDispatchDropConditionCallback(PlayerObject player, string label)
        {
            string normalized = (label ?? string.Empty).Trim();
            if (normalized.Length == 0) return false;
            if (!(normalized.StartsWith("[@", StringComparison.Ordinal) && normalized.EndsWith(']')))
                normalized = $"[@{normalized.TrimStart('@')}]";
            return TryDispatchTarget(false, Envir.Main.TextFileProvider, player, player,
                cSharpEligible: false,
                new LingFengTxtHookTarget("SystemScripts/QFunction-0", normalized));
        }

        private static LingFengDamageEvent Snapshot(PlayerDamageRequest request)
        {
            if (request == null) return default;
            int applied = request.ComputeFinalDamage();
            return new LingFengDamageEvent(
                request.Perspective,
                request.Attacker?.Name ?? string.Empty,
                request.Target?.Name ?? string.Empty,
                request.Target?.Name ?? string.Empty,
                applied,
                applied,
                false,
                request.Target is MonsterObject,
                request.Target?.CurrentLocation.X ?? 0,
                request.Target?.CurrentLocation.Y ?? 0,
                (request.Target as MonsterObject)?.HP ?? 0,
                request.Target?.Stats?[Stat.HP] ?? 0,
                LingFengTxtTriggerContext.Current?.MagicId ?? "0",
                ClassifyDamageSubject(request.Perspective, request.Actor, request.Target));
        }

        private static LingFengDamageEvent Snapshot(PlayerDamageResult result)
        {
            if (result == null) return default;
            return new LingFengDamageEvent(
                result.Perspective,
                result.Attacker?.Name ?? string.Empty,
                result.Target?.Name ?? string.Empty,
                result.Target?.Name ?? string.Empty,
                Math.Max(0, result.Damage - result.Armour),
                result.AppliedDamage,
                true,
                result.Target is MonsterObject,
                result.Target?.CurrentLocation.X ?? 0,
                result.Target?.CurrentLocation.Y ?? 0,
                (result.Target as MonsterObject)?.HP ?? 0,
                result.Target?.Stats?[Stat.HP] ?? 0,
                LingFengTxtTriggerContext.Current?.MagicId ?? "0",
                ClassifyDamageSubject(result.Perspective, result.Actor, result.Target));
        }

        private static LingFengCombatActorKind ClassifyDamageSubject(
            PlayerDamagePerspective perspective,
            MapObject actor,
            MapObject target) =>
            ClassifyActor(perspective == PlayerDamagePerspective.Outgoing ? actor : target);

        private static LingFengCombatActorKind ClassifyActor(MapObject actor) => actor switch
        {
            HeroObject => LingFengCombatActorKind.Hero,
            MonsterObject monster when monster.Master is PlayerObject => LingFengCombatActorKind.Pet,
            MonsterObject monster when monster.Master is HeroObject => LingFengCombatActorKind.Pet,
            PlayerObject => LingFengCombatActorKind.Player,
            _ => LingFengCombatActorKind.Unknown
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
