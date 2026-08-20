using Server.MirDatabase;
using Server.MirObjects;
using System.Collections.Generic;

namespace Server.Scripting
{
    public static class ScriptHookKeys
    {
        public const string OnPlayerLogin = "hooks/player/login";
        public const string OnPlayerLevelUp = "hooks/player/levelup";
        public const string OnPlayerDie = "hooks/player/die";
        public const string OnPlayerUseItem = "hooks/player/useitem";

        public static string OnPlayerUseItemShape(int itemShape) => $"hooks/player/useitem/{itemShape}";

        public const string OnPlayerMapEnter = "hooks/player/mapenter";
        public const string OnPlayerMapCoord = "hooks/player/mapcoord";

        public const string OnPlayerMapLeaveBefore = "hooks/player/map/leave/before";
        public static string OnPlayerMapLeaveBeforeMap(string mapFileName) => $"hooks/player/map/leave/before/{mapFileName}";
        public const string OnPlayerMapEnterAfter = "hooks/player/map/enter/after";
        public static string OnPlayerMapEnterAfterMap(string mapFileName) => $"hooks/player/map/enter/after/{mapFileName}";

        public const string OnPlayerRegionEnter = "hooks/player/region/enter";
        public const string OnPlayerRegionLeave = "hooks/player/region/leave";
        public static string OnPlayerRegionEnterKey(string mapFileName, string regionKey) => $"hooks/player/region/enter/{mapFileName}/{regionKey}";
        public static string OnPlayerRegionLeaveKey(string mapFileName, string regionKey) => $"hooks/player/region/leave/{mapFileName}/{regionKey}";

        public const string OnActivityProgressBefore = "hooks/activity/progress/before";
        public const string OnActivityProgressAfter = "hooks/activity/progress/after";
        public const string OnActivityResultBefore = "hooks/activity/result/before";
        public const string OnActivityResultAfter = "hooks/activity/result/after";
        public const string OnActivityRewardBefore = "hooks/activity/reward/before";
        public const string OnActivityRewardAfter = "hooks/activity/reward/after";

        public const string OnShopPriceBefore = "hooks/economy/shop/price/before";
        public const string OnShopPriceAfter = "hooks/economy/shop/price/after";
        public const string OnMarketFeeBefore = "hooks/economy/market/fee/before";
        public const string OnMarketFeeAfter = "hooks/economy/market/fee/after";
        public const string OnMailCostBefore = "hooks/economy/mail/cost/before";
        public const string OnMailCostAfter = "hooks/economy/mail/cost/after";
        public const string OnEconomyRateBefore = "hooks/economy/rate/before";
        public const string OnEconomyRateAfter = "hooks/economy/rate/after";
        public const string OnPlayerTrigger = "hooks/player/trigger";
        public const string OnPlayerChatCommand = "hooks/player/chatcommand";
        public static string OnPlayerChatCommandName(string command) => $"hooks/player/chatcommand/{command}";
        public const string OnPlayerMagicCastBefore = "hooks/player/magic/before";
        public const string OnPlayerMagicCastAfter = "hooks/player/magic/after";
        public static string OnPlayerMagicCastBeforeSpell(Spell spell) => $"hooks/player/magic/before/{spell}";
        public static string OnPlayerMagicCastAfterSpell(Spell spell) => $"hooks/player/magic/after/{spell}";
        public const string OnPlayerDamageBefore = "hooks/player/damage/before";
        public const string OnPlayerDamageAfter = "hooks/player/damage/after";
        public const string OnPlayerDamageBeforeIn = "hooks/player/damage/before/in";
        public const string OnPlayerDamageBeforeOut = "hooks/player/damage/before/out";
        public const string OnPlayerDamageAfterIn = "hooks/player/damage/after/in";
        public const string OnPlayerDamageAfterOut = "hooks/player/damage/after/out";
        public const string OnPlayerDeathPenaltyBefore = "hooks/player/deathpenalty/before";
        public const string OnPlayerDeathPenaltyAfter = "hooks/player/deathpenalty/after";
        public const string OnPlayerItemPickupCheck = "hooks/player/item/pickup/check";
        public static string OnPlayerItemPickupCheckIndex(int itemIndex) => $"hooks/player/item/pickup/check/{itemIndex}";
        public const string OnPlayerItemUseCheck = "hooks/player/item/use/check";
        public static string OnPlayerItemUseCheckIndex(int itemIndex) => $"hooks/player/item/use/check/{itemIndex}";
        public const string OnPlayerCustomCommand = "hooks/player/customcommand";
        public const string OnPlayerAcceptQuest = "hooks/player/acceptquest";
        public const string OnPlayerFinishQuest = "hooks/player/finishquest";
        public const string OnPlayerDaily = "hooks/player/daily";
        public const string OnClientEvent = "hooks/client/event";
        public const string OnPlayerTimerExpired = "hooks/player/timer/expired";
        public static string OnPlayerTimerExpiredKey(string timerKey) => $"hooks/player/timer/expired/{timerKey}";

        public const string OnMonsterSpawn = "hooks/monster/spawn";
        public const string OnMonsterDie = "hooks/monster/die";
        public const string OnMonsterDropBefore = "hooks/monster/drop/before";
        public const string OnMonsterDropAfter = "hooks/monster/drop/after";
        public const string OnMonsterRespawnBefore = "hooks/monster/respawn/before";
        public const string OnMonsterRespawnAfter = "hooks/monster/respawn/after";

        public const string OnMonsterAiTargetBefore = "hooks/monster/ai/target/before";
        public const string OnMonsterAiTargetAfter = "hooks/monster/ai/target/after";
        public const string OnMonsterAiSkillBefore = "hooks/monster/ai/skill/before";
        public const string OnMonsterAiSkillAfter = "hooks/monster/ai/skill/after";
        public const string OnMonsterAiMoveBefore = "hooks/monster/ai/move/before";
        public const string OnMonsterAiMoveAfter = "hooks/monster/ai/move/after";

        public static string OnMonsterSpawnIndex(int monsterIndex) => $"hooks/monster/spawn/{monsterIndex}";
        public static string OnMonsterDieIndex(int monsterIndex) => $"hooks/monster/die/{monsterIndex}";
        public static string OnMonsterDropBeforeIndex(int monsterIndex) => $"hooks/monster/drop/before/{monsterIndex}";
        public static string OnMonsterDropAfterIndex(int monsterIndex) => $"hooks/monster/drop/after/{monsterIndex}";
        public static string OnMonsterRespawnBeforeIndex(int monsterIndex) => $"hooks/monster/respawn/before/{monsterIndex}";
        public static string OnMonsterRespawnAfterIndex(int monsterIndex) => $"hooks/monster/respawn/after/{monsterIndex}";

        public static string OnMonsterAiTargetBeforeIndex(int monsterIndex) => $"hooks/monster/ai/target/before/{monsterIndex}";
        public static string OnMonsterAiTargetAfterIndex(int monsterIndex) => $"hooks/monster/ai/target/after/{monsterIndex}";
        public static string OnMonsterAiSkillBeforeIndex(int monsterIndex) => $"hooks/monster/ai/skill/before/{monsterIndex}";
        public static string OnMonsterAiSkillAfterIndex(int monsterIndex) => $"hooks/monster/ai/skill/after/{monsterIndex}";
        public static string OnMonsterAiMoveBeforeIndex(int monsterIndex) => $"hooks/monster/ai/move/before/{monsterIndex}";
        public static string OnMonsterAiMoveAfterIndex(int monsterIndex) => $"hooks/monster/ai/move/after/{monsterIndex}";

        public static string OnActivityProgressBeforeKey(ActivitySourceType sourceType, string activityKey) => $"{OnActivityProgressBefore}/{GetActivitySourceSegment(sourceType)}/{activityKey}";
        public static string OnActivityProgressAfterKey(ActivitySourceType sourceType, string activityKey) => $"{OnActivityProgressAfter}/{GetActivitySourceSegment(sourceType)}/{activityKey}";
        public static string OnActivityResultBeforeKey(ActivitySourceType sourceType, string activityKey) => $"{OnActivityResultBefore}/{GetActivitySourceSegment(sourceType)}/{activityKey}";
        public static string OnActivityResultAfterKey(ActivitySourceType sourceType, string activityKey) => $"{OnActivityResultAfter}/{GetActivitySourceSegment(sourceType)}/{activityKey}";
        public static string OnActivityRewardBeforeKey(ActivitySourceType sourceType, string activityKey) => $"{OnActivityRewardBefore}/{GetActivitySourceSegment(sourceType)}/{activityKey}";
        public static string OnActivityRewardAfterKey(ActivitySourceType sourceType, string activityKey) => $"{OnActivityRewardAfter}/{GetActivitySourceSegment(sourceType)}/{activityKey}";

        public static string OnShopPriceBeforeOperation(ShopPriceOperation operation) => $"{OnShopPriceBefore}/{GetShopPriceOperationSegment(operation)}";
        public static string OnShopPriceAfterOperation(ShopPriceOperation operation) => $"{OnShopPriceAfter}/{GetShopPriceOperationSegment(operation)}";
        public static string OnShopPriceBeforeNpc(ShopPriceOperation operation, int npcIndex) => $"{OnShopPriceBeforeOperation(operation)}/{npcIndex}";
        public static string OnShopPriceAfterNpc(ShopPriceOperation operation, int npcIndex) => $"{OnShopPriceAfterOperation(operation)}/{npcIndex}";

        public static string OnMarketFeeBeforeOperation(MarketFeeOperation operation) => $"{OnMarketFeeBefore}/{GetMarketFeeOperationSegment(operation)}";
        public static string OnMarketFeeAfterOperation(MarketFeeOperation operation) => $"{OnMarketFeeAfter}/{GetMarketFeeOperationSegment(operation)}";
        public static string OnMarketFeeBeforeNpc(MarketFeeOperation operation, int npcIndex) => $"{OnMarketFeeBeforeOperation(operation)}/{npcIndex}";
        public static string OnMarketFeeAfterNpc(MarketFeeOperation operation, int npcIndex) => $"{OnMarketFeeAfterOperation(operation)}/{npcIndex}";

        public static string OnMailCostBeforeOperation(MailCostOperation operation) => $"{OnMailCostBefore}/{GetMailCostOperationSegment(operation)}";
        public static string OnMailCostAfterOperation(MailCostOperation operation) => $"{OnMailCostAfter}/{GetMailCostOperationSegment(operation)}";

        public static string OnEconomyRateBeforeType(EconomyRateType type) => $"{OnEconomyRateBefore}/{GetEconomyRateTypeSegment(type)}";
        public static string OnEconomyRateAfterType(EconomyRateType type) => $"{OnEconomyRateAfter}/{GetEconomyRateTypeSegment(type)}";

        private static string GetActivitySourceSegment(ActivitySourceType sourceType)
        {
            switch (sourceType)
            {
                case ActivitySourceType.Conquest:
                    return "conquest";
                case ActivitySourceType.Dragon:
                    return "dragon";
                default:
                    return "unknown";
            }
        }

        private static string GetShopPriceOperationSegment(ShopPriceOperation operation)
        {
            switch (operation)
            {
                case ShopPriceOperation.Buy:
                    return "buy";
                case ShopPriceOperation.Sell:
                    return "sell";
                default:
                    return "unknown";
            }
        }

        private static string GetMarketFeeOperationSegment(MarketFeeOperation operation)
        {
            switch (operation)
            {
                case MarketFeeOperation.Listing:
                    return "listing";
                case MarketFeeOperation.Commission:
                    return "commission";
                default:
                    return "unknown";
            }
        }

        private static string GetMailCostOperationSegment(MailCostOperation operation)
        {
            switch (operation)
            {
                case MailCostOperation.Preview:
                    return "preview";
                case MailCostOperation.Send:
                    return "send";
                default:
                    return "unknown";
            }
        }

        private static string GetEconomyRateTypeSegment(EconomyRateType type)
        {
            switch (type)
            {
                case EconomyRateType.Experience:
                    return "exp";
                case EconomyRateType.Drop:
                    return "drop";
                default:
                    return "unknown";
            }
        }

        /// <summary>
        /// NPC 页面对话脚本：Key = NPCs/&lt;npcFileName&gt;/&lt;pageKey&gt;
        /// - npcFileName：对应 Envir/NPCs 下的文件名（可含子目录，去掉 .txt）
        /// - pageKey：例如 [@MAIN]、[@BUY]、[@@INPUT]、[@SOME(1,2)] 等
        /// </summary>
        public static string OnNpcPage(string npcFileName, string pageKey) => $"NPCs/{npcFileName}/{pageKey}";
    }

    public delegate bool OnPlayerLoginHook(ScriptContext context, PlayerObject player);
    public delegate bool OnPlayerLevelUpHook(ScriptContext context, PlayerObject player);
    public delegate bool OnPlayerDieHook(ScriptContext context, PlayerObject player);
    public delegate bool OnPlayerUseItemHook(ScriptContext context, PlayerObject player, int itemShape);
    public delegate bool OnPlayerMapEnterHook(ScriptContext context, PlayerObject player, string mapFileName);
    public delegate bool OnPlayerMapCoordHook(ScriptContext context, PlayerObject player, string mapFileName, int x, int y);
    public delegate void OnPlayerMapLeaveBeforeHook(ScriptContext context, PlayerObject player, PlayerMapLeaveRequest request);
    public delegate void OnPlayerMapEnterAfterHook(ScriptContext context, PlayerObject player, PlayerMapEnterResult result);
    public delegate void OnPlayerRegionEnterHook(ScriptContext context, PlayerObject player, PlayerRegionEvent e);
    public delegate void OnPlayerRegionLeaveHook(ScriptContext context, PlayerObject player, PlayerRegionEvent e);
    public delegate void OnActivityProgressBeforeHook(ScriptContext context, ActivityProgressRequest request);
    public delegate void OnActivityProgressAfterHook(ScriptContext context, ActivityProgressResult result);
    public delegate void OnActivityResultBeforeHook(ScriptContext context, ActivityResultRequest request);
    public delegate void OnActivityResultAfterHook(ScriptContext context, ActivityResult result);
    public delegate void OnActivityRewardBeforeHook(ScriptContext context, ActivityRewardRequest request);
    public delegate void OnActivityRewardAfterHook(ScriptContext context, ActivityRewardResult result);
    public delegate void OnShopPriceBeforeHook(ScriptContext context, ShopPriceRequest request);
    public delegate void OnShopPriceAfterHook(ScriptContext context, ShopPriceResult result);
    public delegate void OnMarketFeeBeforeHook(ScriptContext context, MarketFeeRequest request);
    public delegate void OnMarketFeeAfterHook(ScriptContext context, MarketFeeResult result);
    public delegate void OnMailCostBeforeHook(ScriptContext context, MailCostRequest request);
    public delegate void OnMailCostAfterHook(ScriptContext context, MailCostResult result);
    public delegate void OnEconomyRateBeforeHook(ScriptContext context, EconomyRateRequest request);
    public delegate void OnEconomyRateAfterHook(ScriptContext context, EconomyRateResult result);
    public delegate bool OnPlayerTriggerHook(ScriptContext context, PlayerObject player, string triggerKey);
    public delegate bool OnPlayerChatCommandHook(ScriptContext context, PlayerObject player, string commandLine, string command, IReadOnlyList<string> args);
    public delegate void OnPlayerMagicCastBeforeHook(ScriptContext context, PlayerObject player, PlayerMagicCastRequest request);
    public delegate void OnPlayerMagicCastAfterHook(ScriptContext context, PlayerObject player, PlayerMagicCastResult result);
    public delegate void OnPlayerDamageBeforeHook(ScriptContext context, PlayerObject player, PlayerDamageRequest request);
    public delegate void OnPlayerDamageAfterHook(ScriptContext context, PlayerObject player, PlayerDamageResult result);
    public delegate void OnPlayerDeathPenaltyBeforeHook(ScriptContext context, PlayerObject player, PlayerDeathPenaltyRequest request);
    public delegate void OnPlayerDeathPenaltyAfterHook(ScriptContext context, PlayerObject player, PlayerDeathPenaltyResult result);
    public delegate void OnPlayerItemPickupCheckHook(ScriptContext context, PlayerObject player, PlayerItemPickupCheckRequest request);
    public delegate void OnPlayerItemUseCheckHook(ScriptContext context, PlayerObject player, PlayerItemUseCheckRequest request);
    public delegate bool OnPlayerCustomCommandHook(ScriptContext context, PlayerObject player, string command);
    public delegate bool OnPlayerAcceptQuestHook(ScriptContext context, PlayerObject player, int questIndex);
    public delegate bool OnPlayerFinishQuestHook(ScriptContext context, PlayerObject player, int questIndex);
    public delegate bool OnPlayerDailyHook(ScriptContext context, PlayerObject player);
    public delegate bool OnClientEventHook(ScriptContext context, PlayerObject player, object payload);
    public delegate bool OnPlayerTimerExpiredHook(ScriptContext context, PlayerObject player, string timerKey, byte timerType);

    public delegate bool OnMonsterSpawnHook(ScriptContext context, MonsterObject monster);
    public delegate bool OnMonsterDieHook(ScriptContext context, MonsterObject monster);
    public delegate void OnMonsterDropBeforeHook(ScriptContext context, MonsterObject monster, MonsterDropRequest request);
    public delegate void OnMonsterDropAfterHook(ScriptContext context, MonsterObject monster, MonsterDropResult result);
    public delegate void OnMonsterRespawnBeforeHook(ScriptContext context, MonsterInfo monster, MonsterRespawnRequest request);
    public delegate void OnMonsterRespawnAfterHook(ScriptContext context, MonsterInfo monster, MonsterRespawnResult result);

    public delegate void OnMonsterAiTargetBeforeHook(ScriptContext context, MonsterObject monster, MonsterAiTargetSelectRequest request);
    public delegate void OnMonsterAiTargetAfterHook(ScriptContext context, MonsterObject monster, MonsterAiTargetSelectResult result);
    public delegate void OnMonsterAiSkillBeforeHook(ScriptContext context, MonsterObject monster, MonsterAiSkillSelectRequest request);
    public delegate void OnMonsterAiSkillAfterHook(ScriptContext context, MonsterObject monster, MonsterAiSkillSelectResult result);
    public delegate void OnMonsterAiMoveBeforeHook(ScriptContext context, MonsterObject monster, MonsterAiMoveRequest request);
    public delegate void OnMonsterAiMoveAfterHook(ScriptContext context, MonsterObject monster, MonsterAiMoveResult result);

    public delegate bool OnNpcPageHook(ScriptContext context, PlayerObject player, NpcPageCall call, NpcDialog dialog);
    public delegate bool OnNpcInputHook(ScriptContext context, PlayerObject player, NpcPageCall call, string input, NpcDialog dialog);
}
