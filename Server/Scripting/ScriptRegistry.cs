namespace Server.Scripting
{
    public sealed class ScriptRegistry
    {
        internal readonly struct ScriptModuleCatalogEntry
        {
            public Type ModuleType { get; }
            public bool AutoRegister { get; }

            public ScriptModuleCatalogEntry(Type moduleType, bool autoRegister)
            {
                ModuleType = moduleType ?? throw new ArgumentNullException(nameof(moduleType));
                AutoRegister = autoRegister;
            }
        }

        private readonly Dictionary<string, Delegate> _handlers = new Dictionary<string, Delegate>(StringComparer.Ordinal);
        private readonly HashSet<string> _customCommands = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public QuestRegistry Quests { get; } = new QuestRegistry();
        public DropRegistry Drops { get; } = new DropRegistry();
        public RecipeRegistry Recipes { get; } = new RecipeRegistry();
        public ValueRegistry Values { get; } = new ValueRegistry();
        public NameListRegistry NameLists { get; } = new NameListRegistry();
        public RouteRegistry Routes { get; } = new RouteRegistry();
        public RootConfigRegistry RootConfigs { get; } = new RootConfigRegistry();
        public TextFileRegistry TextFiles { get; } = new TextFileRegistry();
        public NpcShopRegistry NpcShops { get; } = new NpcShopRegistry();
        public PlayerRegionRegistry Regions { get; } = new PlayerRegionRegistry();
        public ActiveMapCoordRegistry ActiveMapCoords { get; } = new ActiveMapCoordRegistry();

        private IReadOnlyDictionary<string, ScriptModuleCatalogEntry> _moduleCatalog = new Dictionary<string, ScriptModuleCatalogEntry>(StringComparer.Ordinal);
        private readonly HashSet<string> _importedModuleKeys = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _importingModuleKeys = new HashSet<string>(StringComparer.Ordinal);

        public IReadOnlyDictionary<string, Delegate> Handlers => _handlers;

        public int Count => _handlers.Count;

        public IReadOnlyCollection<string> CustomCommands => _customCommands;

        public void RegisterNpcShop(NpcShopDefinition definition) => NpcShops.Register(definition);

        internal void SetModuleCatalog(IReadOnlyDictionary<string, ScriptModuleCatalogEntry> catalog)
        {
            _moduleCatalog = catalog ?? new Dictionary<string, ScriptModuleCatalogEntry>(StringComparer.Ordinal);
            _importedModuleKeys.Clear();
            _importingModuleKeys.Clear();
        }

        /// <summary>
        /// 组合导入其它脚本模块（用于替代 legacy 的 #INSERT/#INCLUDE 文本拼接）。
        /// 说明：
        /// - moduleKey 会按 <see cref="LogicKey"/> 归一化后匹配 <see cref="ScriptModuleAttribute.Key"/>
        /// - 仅允许导入标记为 AutoRegister=false 的模块（避免重复注册）
        /// </summary>
        public void Import(string moduleKey)
        {
            if (!LogicKey.TryNormalize(moduleKey, out var normalizedKey))
                throw new ArgumentException("moduleKey 无效。", nameof(moduleKey));

            var catalog = _moduleCatalog;
            if (catalog == null || catalog.Count == 0)
                throw new InvalidOperationException("模块目录未初始化；Import 仅可在脚本加载/注册阶段使用。");

            if (!catalog.TryGetValue(normalizedKey, out var entry))
                throw new InvalidOperationException($"找不到脚本模块：{normalizedKey}");

            if (entry.AutoRegister)
                throw new InvalidOperationException($"脚本模块 {normalizedKey} 为 AutoRegister=true，不能 Import；请将模块标记为 [ScriptModule(\"{normalizedKey}\", false)]。");

            ImportInternal(normalizedKey, entry.ModuleType);
        }

        /// <summary>
        /// 按类型组合导入脚本模块（不依赖 Key）。
        /// </summary>
        public void Import<TModule>() where TModule : IScriptModule, new()
        {
            Import(new TModule());
        }

        /// <summary>
        /// 组合导入脚本模块实例（允许导入“子模块/工具模块”）。
        /// </summary>
        public void Import(IScriptModule module)
        {
            if (module == null) throw new ArgumentNullException(nameof(module));
            module.Register(this);
        }

        private void ImportInternal(string normalizedKey, Type moduleType)
        {
            if (_importedModuleKeys.Contains(normalizedKey))
                return;

            if (!_importingModuleKeys.Add(normalizedKey))
                throw new InvalidOperationException($"检测到脚本模块循环 Import：{normalizedKey}");

            try
            {
                var instance = (IScriptModule)Activator.CreateInstance(moduleType);
                instance.Register(this);
                _importedModuleKeys.Add(normalizedKey);
            }
            finally
            {
                _importingModuleKeys.Remove(normalizedKey);
            }
        }

        public void RegisterOnPlayerLogin(OnPlayerLoginHook handler) => Register(ScriptHookKeys.OnPlayerLogin, handler);
        public void RegisterOnPlayerLevelUp(OnPlayerLevelUpHook handler) => Register(ScriptHookKeys.OnPlayerLevelUp, handler);
        public void RegisterOnPlayerDie(OnPlayerDieHook handler) => Register(ScriptHookKeys.OnPlayerDie, handler);
        public void RegisterOnPlayerUseItem(OnPlayerUseItemHook handler) => Register(ScriptHookKeys.OnPlayerUseItem, handler);

        public void RegisterOnPlayerUseItem(int itemShape, OnPlayerUseItemHook handler)
        {
            if (itemShape <= 0)
                throw new ArgumentOutOfRangeException(nameof(itemShape), "itemShape 必须大于 0。");

            Register(ScriptHookKeys.OnPlayerUseItemShape(itemShape), handler);
        }

        public void RegisterOnPlayerMapEnter(OnPlayerMapEnterHook handler) => Register(ScriptHookKeys.OnPlayerMapEnter, handler);
        public void RegisterOnPlayerMapCoord(OnPlayerMapCoordHook handler) => Register(ScriptHookKeys.OnPlayerMapCoord, handler);

        /// <summary>
        /// 注册“坐标触发点”（legacy DefaultNPC 的 [@_MAPCOORD(...)]）。
        /// 说明：该列表用于地图 <see cref="Server.MirDatabase.MapInfo.ActiveCoords"/>，从而触发 <see cref="DefaultNPCType.MapCoord"/>。
        /// </summary>
        public void RegisterActiveMapCoord(string mapFileName, int x, int y) =>
            ActiveMapCoords.Register(mapFileName, x, y);

        public void RegisterOnPlayerMapLeaveBefore(OnPlayerMapLeaveBeforeHook handler) => Register(ScriptHookKeys.OnPlayerMapLeaveBefore, handler);

        public void RegisterOnPlayerMapLeaveBefore(string mapFileName, OnPlayerMapLeaveBeforeHook handler)
        {
            if (string.IsNullOrWhiteSpace(mapFileName))
                throw new ArgumentException("mapFileName 不能为空。", nameof(mapFileName));

            Register(ScriptHookKeys.OnPlayerMapLeaveBeforeMap(mapFileName), handler);
        }

        public void RegisterOnPlayerMapEnterAfter(OnPlayerMapEnterAfterHook handler) => Register(ScriptHookKeys.OnPlayerMapEnterAfter, handler);

        public void RegisterOnPlayerMapEnterAfter(string mapFileName, OnPlayerMapEnterAfterHook handler)
        {
            if (string.IsNullOrWhiteSpace(mapFileName))
                throw new ArgumentException("mapFileName 不能为空。", nameof(mapFileName));

            Register(ScriptHookKeys.OnPlayerMapEnterAfterMap(mapFileName), handler);
        }

        public void RegisterOnPlayerRegionEnter(OnPlayerRegionEnterHook handler) => Register(ScriptHookKeys.OnPlayerRegionEnter, handler);

        public void RegisterOnPlayerRegionEnter(string mapFileName, string regionKey, OnPlayerRegionEnterHook handler)
        {
            if (string.IsNullOrWhiteSpace(mapFileName))
                throw new ArgumentException("mapFileName 不能为空。", nameof(mapFileName));
            if (string.IsNullOrWhiteSpace(regionKey))
                throw new ArgumentException("regionKey 不能为空。", nameof(regionKey));

            Register(ScriptHookKeys.OnPlayerRegionEnterKey(mapFileName, regionKey), handler);
        }

        public void RegisterOnPlayerRegionLeave(OnPlayerRegionLeaveHook handler) => Register(ScriptHookKeys.OnPlayerRegionLeave, handler);

        public void RegisterOnPlayerRegionLeave(string mapFileName, string regionKey, OnPlayerRegionLeaveHook handler)
        {
            if (string.IsNullOrWhiteSpace(mapFileName))
                throw new ArgumentException("mapFileName 不能为空。", nameof(mapFileName));
            if (string.IsNullOrWhiteSpace(regionKey))
                throw new ArgumentException("regionKey 不能为空。", nameof(regionKey));

            Register(ScriptHookKeys.OnPlayerRegionLeaveKey(mapFileName, regionKey), handler);
        }

        public void RegisterOnActivityProgressBefore(OnActivityProgressBeforeHook handler) => Register(ScriptHookKeys.OnActivityProgressBefore, handler);

        public void RegisterOnActivityProgressBefore(ActivitySourceType sourceType, string activityKey, OnActivityProgressBeforeHook handler)
        {
            if (string.IsNullOrWhiteSpace(activityKey))
                throw new ArgumentException("activityKey 不能为空。", nameof(activityKey));

            Register(ScriptHookKeys.OnActivityProgressBeforeKey(sourceType, activityKey), handler);
        }

        public void RegisterOnActivityProgressAfter(OnActivityProgressAfterHook handler) => Register(ScriptHookKeys.OnActivityProgressAfter, handler);

        public void RegisterOnActivityProgressAfter(ActivitySourceType sourceType, string activityKey, OnActivityProgressAfterHook handler)
        {
            if (string.IsNullOrWhiteSpace(activityKey))
                throw new ArgumentException("activityKey 不能为空。", nameof(activityKey));

            Register(ScriptHookKeys.OnActivityProgressAfterKey(sourceType, activityKey), handler);
        }

        public void RegisterOnActivityResultBefore(OnActivityResultBeforeHook handler) => Register(ScriptHookKeys.OnActivityResultBefore, handler);

        public void RegisterOnActivityResultBefore(ActivitySourceType sourceType, string activityKey, OnActivityResultBeforeHook handler)
        {
            if (string.IsNullOrWhiteSpace(activityKey))
                throw new ArgumentException("activityKey 不能为空。", nameof(activityKey));

            Register(ScriptHookKeys.OnActivityResultBeforeKey(sourceType, activityKey), handler);
        }

        public void RegisterOnActivityResultAfter(OnActivityResultAfterHook handler) => Register(ScriptHookKeys.OnActivityResultAfter, handler);

        public void RegisterOnActivityResultAfter(ActivitySourceType sourceType, string activityKey, OnActivityResultAfterHook handler)
        {
            if (string.IsNullOrWhiteSpace(activityKey))
                throw new ArgumentException("activityKey 不能为空。", nameof(activityKey));

            Register(ScriptHookKeys.OnActivityResultAfterKey(sourceType, activityKey), handler);
        }

        public void RegisterOnActivityRewardBefore(OnActivityRewardBeforeHook handler) => Register(ScriptHookKeys.OnActivityRewardBefore, handler);

        public void RegisterOnActivityRewardBefore(ActivitySourceType sourceType, string activityKey, OnActivityRewardBeforeHook handler)
        {
            if (string.IsNullOrWhiteSpace(activityKey))
                throw new ArgumentException("activityKey 不能为空。", nameof(activityKey));

            Register(ScriptHookKeys.OnActivityRewardBeforeKey(sourceType, activityKey), handler);
        }

        public void RegisterOnActivityRewardAfter(OnActivityRewardAfterHook handler) => Register(ScriptHookKeys.OnActivityRewardAfter, handler);

        public void RegisterOnActivityRewardAfter(ActivitySourceType sourceType, string activityKey, OnActivityRewardAfterHook handler)
        {
            if (string.IsNullOrWhiteSpace(activityKey))
                throw new ArgumentException("activityKey 不能为空。", nameof(activityKey));

            Register(ScriptHookKeys.OnActivityRewardAfterKey(sourceType, activityKey), handler);
        }

        public void RegisterOnShopPriceBefore(OnShopPriceBeforeHook handler) => Register(ScriptHookKeys.OnShopPriceBefore, handler);

        public void RegisterOnShopPriceBefore(ShopPriceOperation operation, OnShopPriceBeforeHook handler) =>
            Register(ScriptHookKeys.OnShopPriceBeforeOperation(operation), handler);

        public void RegisterOnShopPriceBefore(ShopPriceOperation operation, int npcIndex, OnShopPriceBeforeHook handler)
        {
            if (npcIndex <= 0)
                throw new ArgumentOutOfRangeException(nameof(npcIndex), "npcIndex 必须大于 0。");

            Register(ScriptHookKeys.OnShopPriceBeforeNpc(operation, npcIndex), handler);
        }

        public void RegisterOnShopPriceAfter(OnShopPriceAfterHook handler) => Register(ScriptHookKeys.OnShopPriceAfter, handler);

        public void RegisterOnShopPriceAfter(ShopPriceOperation operation, OnShopPriceAfterHook handler) =>
            Register(ScriptHookKeys.OnShopPriceAfterOperation(operation), handler);

        public void RegisterOnShopPriceAfter(ShopPriceOperation operation, int npcIndex, OnShopPriceAfterHook handler)
        {
            if (npcIndex <= 0)
                throw new ArgumentOutOfRangeException(nameof(npcIndex), "npcIndex 必须大于 0。");

            Register(ScriptHookKeys.OnShopPriceAfterNpc(operation, npcIndex), handler);
        }

        public void RegisterOnMarketFeeBefore(OnMarketFeeBeforeHook handler) => Register(ScriptHookKeys.OnMarketFeeBefore, handler);

        public void RegisterOnMarketFeeBefore(MarketFeeOperation operation, OnMarketFeeBeforeHook handler) =>
            Register(ScriptHookKeys.OnMarketFeeBeforeOperation(operation), handler);

        public void RegisterOnMarketFeeBefore(MarketFeeOperation operation, int npcIndex, OnMarketFeeBeforeHook handler)
        {
            if (npcIndex <= 0)
                throw new ArgumentOutOfRangeException(nameof(npcIndex), "npcIndex 必须大于 0。");

            Register(ScriptHookKeys.OnMarketFeeBeforeNpc(operation, npcIndex), handler);
        }

        public void RegisterOnMarketFeeAfter(OnMarketFeeAfterHook handler) => Register(ScriptHookKeys.OnMarketFeeAfter, handler);

        public void RegisterOnMarketFeeAfter(MarketFeeOperation operation, OnMarketFeeAfterHook handler) =>
            Register(ScriptHookKeys.OnMarketFeeAfterOperation(operation), handler);

        public void RegisterOnMarketFeeAfter(MarketFeeOperation operation, int npcIndex, OnMarketFeeAfterHook handler)
        {
            if (npcIndex <= 0)
                throw new ArgumentOutOfRangeException(nameof(npcIndex), "npcIndex 必须大于 0。");

            Register(ScriptHookKeys.OnMarketFeeAfterNpc(operation, npcIndex), handler);
        }

        public void RegisterOnMailCostBefore(OnMailCostBeforeHook handler) => Register(ScriptHookKeys.OnMailCostBefore, handler);

        public void RegisterOnMailCostBefore(MailCostOperation operation, OnMailCostBeforeHook handler) =>
            Register(ScriptHookKeys.OnMailCostBeforeOperation(operation), handler);

        public void RegisterOnMailCostAfter(OnMailCostAfterHook handler) => Register(ScriptHookKeys.OnMailCostAfter, handler);

        public void RegisterOnMailCostAfter(MailCostOperation operation, OnMailCostAfterHook handler) =>
            Register(ScriptHookKeys.OnMailCostAfterOperation(operation), handler);

        public void RegisterOnEconomyRateBefore(OnEconomyRateBeforeHook handler) => Register(ScriptHookKeys.OnEconomyRateBefore, handler);

        public void RegisterOnEconomyRateBefore(EconomyRateType type, OnEconomyRateBeforeHook handler) =>
            Register(ScriptHookKeys.OnEconomyRateBeforeType(type), handler);

        public void RegisterOnEconomyRateAfter(OnEconomyRateAfterHook handler) => Register(ScriptHookKeys.OnEconomyRateAfter, handler);

        public void RegisterOnEconomyRateAfter(EconomyRateType type, OnEconomyRateAfterHook handler) =>
            Register(ScriptHookKeys.OnEconomyRateAfterType(type), handler);

        public void RegisterOnPlayerTrigger(OnPlayerTriggerHook handler) => Register(ScriptHookKeys.OnPlayerTrigger, handler);
        public void RegisterOnPlayerChatCommand(OnPlayerChatCommandHook handler) => Register(ScriptHookKeys.OnPlayerChatCommand, handler);

        public void RegisterOnPlayerChatCommand(string command, OnPlayerChatCommandHook handler)
        {
            if (string.IsNullOrWhiteSpace(command))
                throw new ArgumentException("command 不能为空。", nameof(command));

            Register(ScriptHookKeys.OnPlayerChatCommandName(command), handler);
        }

        public void RegisterOnPlayerMagicCastBefore(OnPlayerMagicCastBeforeHook handler) => Register(ScriptHookKeys.OnPlayerMagicCastBefore, handler);

        public void RegisterOnPlayerMagicCastBefore(Spell spell, OnPlayerMagicCastBeforeHook handler) =>
            Register(ScriptHookKeys.OnPlayerMagicCastBeforeSpell(spell), handler);

        public void RegisterOnPlayerMagicCastAfter(OnPlayerMagicCastAfterHook handler) => Register(ScriptHookKeys.OnPlayerMagicCastAfter, handler);

        public void RegisterOnPlayerMagicCastAfter(Spell spell, OnPlayerMagicCastAfterHook handler) =>
            Register(ScriptHookKeys.OnPlayerMagicCastAfterSpell(spell), handler);

        public void RegisterOnPlayerDamageBefore(OnPlayerDamageBeforeHook handler) => Register(ScriptHookKeys.OnPlayerDamageBefore, handler);

        public void RegisterOnPlayerDamageBefore(PlayerDamagePerspective perspective, OnPlayerDamageBeforeHook handler)
        {
            switch (perspective)
            {
                case PlayerDamagePerspective.Outgoing:
                    Register(ScriptHookKeys.OnPlayerDamageBeforeOut, handler);
                    break;
                case PlayerDamagePerspective.Incoming:
                    Register(ScriptHookKeys.OnPlayerDamageBeforeIn, handler);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(perspective));
            }
        }

        public void RegisterOnPlayerDamageAfter(OnPlayerDamageAfterHook handler) => Register(ScriptHookKeys.OnPlayerDamageAfter, handler);

        public void RegisterOnPlayerDamageAfter(PlayerDamagePerspective perspective, OnPlayerDamageAfterHook handler)
        {
            switch (perspective)
            {
                case PlayerDamagePerspective.Outgoing:
                    Register(ScriptHookKeys.OnPlayerDamageAfterOut, handler);
                    break;
                case PlayerDamagePerspective.Incoming:
                    Register(ScriptHookKeys.OnPlayerDamageAfterIn, handler);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(perspective));
            }
        }

        public void RegisterOnPlayerDeathPenaltyBefore(OnPlayerDeathPenaltyBeforeHook handler) => Register(ScriptHookKeys.OnPlayerDeathPenaltyBefore, handler);

        public void RegisterOnPlayerDeathPenaltyAfter(OnPlayerDeathPenaltyAfterHook handler) => Register(ScriptHookKeys.OnPlayerDeathPenaltyAfter, handler);

        public void RegisterOnPlayerItemPickupCheck(OnPlayerItemPickupCheckHook handler) => Register(ScriptHookKeys.OnPlayerItemPickupCheck, handler);

        public void RegisterOnPlayerItemPickupCheck(int itemIndex, OnPlayerItemPickupCheckHook handler)
        {
            if (itemIndex <= 0)
                throw new ArgumentOutOfRangeException(nameof(itemIndex), "itemIndex 必须大于 0。");

            Register(ScriptHookKeys.OnPlayerItemPickupCheckIndex(itemIndex), handler);
        }

        public void RegisterOnPlayerItemUseCheck(OnPlayerItemUseCheckHook handler) => Register(ScriptHookKeys.OnPlayerItemUseCheck, handler);

        public void RegisterOnPlayerItemUseCheck(int itemIndex, OnPlayerItemUseCheckHook handler)
        {
            if (itemIndex <= 0)
                throw new ArgumentOutOfRangeException(nameof(itemIndex), "itemIndex 必须大于 0。");

            Register(ScriptHookKeys.OnPlayerItemUseCheckIndex(itemIndex), handler);
        }
        public void RegisterOnPlayerCustomCommand(OnPlayerCustomCommandHook handler) => Register(ScriptHookKeys.OnPlayerCustomCommand, handler);
        public void RegisterOnPlayerAcceptQuest(OnPlayerAcceptQuestHook handler) => Register(ScriptHookKeys.OnPlayerAcceptQuest, handler);
        public void RegisterOnPlayerFinishQuest(OnPlayerFinishQuestHook handler) => Register(ScriptHookKeys.OnPlayerFinishQuest, handler);
        public void RegisterOnPlayerDaily(OnPlayerDailyHook handler) => Register(ScriptHookKeys.OnPlayerDaily, handler);
        public void RegisterOnClientEvent(OnClientEventHook handler) => Register(ScriptHookKeys.OnClientEvent, handler);

        public void RegisterOnPlayerTimerExpired(OnPlayerTimerExpiredHook handler) => Register(ScriptHookKeys.OnPlayerTimerExpired, handler);

        public void RegisterOnPlayerTimerExpired(string timerKey, OnPlayerTimerExpiredHook handler)
        {
            if (string.IsNullOrWhiteSpace(timerKey))
                throw new ArgumentException("timerKey 不能为空。", nameof(timerKey));

            Register(ScriptHookKeys.OnPlayerTimerExpiredKey(timerKey), handler);
        }
        public void RegisterOnMonsterSpawn(OnMonsterSpawnHook handler) => Register(ScriptHookKeys.OnMonsterSpawn, handler);
        public void RegisterOnMonsterDie(OnMonsterDieHook handler) => Register(ScriptHookKeys.OnMonsterDie, handler);
        public void RegisterOnMonsterDropBefore(OnMonsterDropBeforeHook handler) => Register(ScriptHookKeys.OnMonsterDropBefore, handler);
        public void RegisterOnMonsterDropAfter(OnMonsterDropAfterHook handler) => Register(ScriptHookKeys.OnMonsterDropAfter, handler);
        public void RegisterOnMonsterRespawnBefore(OnMonsterRespawnBeforeHook handler) => Register(ScriptHookKeys.OnMonsterRespawnBefore, handler);
        public void RegisterOnMonsterRespawnAfter(OnMonsterRespawnAfterHook handler) => Register(ScriptHookKeys.OnMonsterRespawnAfter, handler);

        public void RegisterOnMonsterAiTargetBefore(OnMonsterAiTargetBeforeHook handler) => Register(ScriptHookKeys.OnMonsterAiTargetBefore, handler);
        public void RegisterOnMonsterAiTargetAfter(OnMonsterAiTargetAfterHook handler) => Register(ScriptHookKeys.OnMonsterAiTargetAfter, handler);
        public void RegisterOnMonsterAiSkillBefore(OnMonsterAiSkillBeforeHook handler) => Register(ScriptHookKeys.OnMonsterAiSkillBefore, handler);
        public void RegisterOnMonsterAiSkillAfter(OnMonsterAiSkillAfterHook handler) => Register(ScriptHookKeys.OnMonsterAiSkillAfter, handler);
        public void RegisterOnMonsterAiMoveBefore(OnMonsterAiMoveBeforeHook handler) => Register(ScriptHookKeys.OnMonsterAiMoveBefore, handler);
        public void RegisterOnMonsterAiMoveAfter(OnMonsterAiMoveAfterHook handler) => Register(ScriptHookKeys.OnMonsterAiMoveAfter, handler);

        public void RegisterOnMonsterSpawn(int monsterIndex, OnMonsterSpawnHook handler)
        {
            if (monsterIndex <= 0)
                throw new ArgumentOutOfRangeException(nameof(monsterIndex), "monsterIndex 必须大于 0。");

            Register(ScriptHookKeys.OnMonsterSpawnIndex(monsterIndex), handler);
        }

        public void RegisterOnMonsterDie(int monsterIndex, OnMonsterDieHook handler)
        {
            if (monsterIndex <= 0)
                throw new ArgumentOutOfRangeException(nameof(monsterIndex), "monsterIndex 必须大于 0。");

            Register(ScriptHookKeys.OnMonsterDieIndex(monsterIndex), handler);
        }

        public void RegisterOnMonsterDropBefore(int monsterIndex, OnMonsterDropBeforeHook handler)
        {
            if (monsterIndex <= 0)
                throw new ArgumentOutOfRangeException(nameof(monsterIndex), "monsterIndex 必须大于 0。");

            Register(ScriptHookKeys.OnMonsterDropBeforeIndex(monsterIndex), handler);
        }

        public void RegisterOnMonsterDropAfter(int monsterIndex, OnMonsterDropAfterHook handler)
        {
            if (monsterIndex <= 0)
                throw new ArgumentOutOfRangeException(nameof(monsterIndex), "monsterIndex 必须大于 0。");

            Register(ScriptHookKeys.OnMonsterDropAfterIndex(monsterIndex), handler);
        }

        public void RegisterOnMonsterRespawnBefore(int monsterIndex, OnMonsterRespawnBeforeHook handler)
        {
            if (monsterIndex <= 0)
                throw new ArgumentOutOfRangeException(nameof(monsterIndex), "monsterIndex 必须大于 0。");

            Register(ScriptHookKeys.OnMonsterRespawnBeforeIndex(monsterIndex), handler);
        }

        public void RegisterOnMonsterRespawnAfter(int monsterIndex, OnMonsterRespawnAfterHook handler)
        {
            if (monsterIndex <= 0)
                throw new ArgumentOutOfRangeException(nameof(monsterIndex), "monsterIndex 必须大于 0。");

            Register(ScriptHookKeys.OnMonsterRespawnAfterIndex(monsterIndex), handler);
        }

        public void RegisterOnMonsterAiTargetBefore(int monsterIndex, OnMonsterAiTargetBeforeHook handler)
        {
            if (monsterIndex <= 0)
                throw new ArgumentOutOfRangeException(nameof(monsterIndex), "monsterIndex 必须大于 0。");

            Register(ScriptHookKeys.OnMonsterAiTargetBeforeIndex(monsterIndex), handler);
        }

        public void RegisterOnMonsterAiTargetAfter(int monsterIndex, OnMonsterAiTargetAfterHook handler)
        {
            if (monsterIndex <= 0)
                throw new ArgumentOutOfRangeException(nameof(monsterIndex), "monsterIndex 必须大于 0。");

            Register(ScriptHookKeys.OnMonsterAiTargetAfterIndex(monsterIndex), handler);
        }

        public void RegisterOnMonsterAiSkillBefore(int monsterIndex, OnMonsterAiSkillBeforeHook handler)
        {
            if (monsterIndex <= 0)
                throw new ArgumentOutOfRangeException(nameof(monsterIndex), "monsterIndex 必须大于 0。");

            Register(ScriptHookKeys.OnMonsterAiSkillBeforeIndex(monsterIndex), handler);
        }

        public void RegisterOnMonsterAiSkillAfter(int monsterIndex, OnMonsterAiSkillAfterHook handler)
        {
            if (monsterIndex <= 0)
                throw new ArgumentOutOfRangeException(nameof(monsterIndex), "monsterIndex 必须大于 0。");

            Register(ScriptHookKeys.OnMonsterAiSkillAfterIndex(monsterIndex), handler);
        }

        public void RegisterOnMonsterAiMoveBefore(int monsterIndex, OnMonsterAiMoveBeforeHook handler)
        {
            if (monsterIndex <= 0)
                throw new ArgumentOutOfRangeException(nameof(monsterIndex), "monsterIndex 必须大于 0。");

            Register(ScriptHookKeys.OnMonsterAiMoveBeforeIndex(monsterIndex), handler);
        }

        public void RegisterOnMonsterAiMoveAfter(int monsterIndex, OnMonsterAiMoveAfterHook handler)
        {
            if (monsterIndex <= 0)
                throw new ArgumentOutOfRangeException(nameof(monsterIndex), "monsterIndex 必须大于 0。");

            Register(ScriptHookKeys.OnMonsterAiMoveAfterIndex(monsterIndex), handler);
        }

        public void RegisterCustomCommand(string command)
        {
            if (string.IsNullOrWhiteSpace(command))
                throw new ArgumentException("命令不能为空。", nameof(command));

            _customCommands.Add(command.Trim());
        }

        public bool IsCustomCommand(string command)
        {
            if (string.IsNullOrWhiteSpace(command)) return false;
            return _customCommands.Contains(command.Trim());
        }

        public void Register(string key, Delegate handler)
        {
            if (!LogicKey.TryNormalize(key, out var normalizedKey))
                throw new ArgumentException("Key 无效。", nameof(key));

            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            if (_handlers.ContainsKey(normalizedKey))
                throw new InvalidOperationException($"重复的脚本处理器 Key：{normalizedKey}");

            _handlers.Add(normalizedKey, handler);
        }

        public bool TryGet<TDelegate>(string key, out TDelegate handler) where TDelegate : class
        {
            handler = null;

            if (!LogicKey.TryNormalize(key, out var normalizedKey)) return false;
            if (!_handlers.TryGetValue(normalizedKey, out var del)) return false;

            handler = del as TDelegate;
            return handler != null;
        }
    }
}
