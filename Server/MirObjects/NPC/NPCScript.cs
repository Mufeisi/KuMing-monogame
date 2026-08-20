using System.Drawing;
﻿using Server.MirDatabase;
using Server.MirEnvir;
using Server.Scripting;
using System.Text.RegularExpressions;
using S = ServerPackets;

namespace Server.MirObjects
{
    public class NPCScript
    {
        protected static Envir Envir
        {
            get { return Envir.Main; }
        }

        protected static MessageQueue MessageQueue
        {
            get { return MessageQueue.Instance; }
        }

        public static NPCScript Get(int index)
        {
            return Envir.Scripts[index];
        }

        public static NPCScript GetOrAdd(uint loadedObjectID, string fileName, NPCScriptType type)
        {
            var script = Envir.Scripts.SingleOrDefault(x => x.Value.FileName.Equals(fileName, StringComparison.OrdinalIgnoreCase) && x.Value.LoadedObjectID == loadedObjectID).Value;

            if (script != null)
            {
                return script;
            }

            return new NPCScript(loadedObjectID, fileName, type);
        }

        public readonly int ScriptID;
        public readonly uint LoadedObjectID;
        public readonly NPCScriptType Type;
        public readonly string FileName;

        public const string
            MainKey = "[@MAIN]",
            BuyKey = "[@BUY]",
            SellKey = "[@SELL]",
            BuySellKey = "[@BUYSELL]",
            RepairKey = "[@REPAIR]",
            SRepairKey = "[@SREPAIR]",
            RefineKey = "[@REFINE]",
            RefineCheckKey = "[@REFINECHECK]",
            RefineCollectKey = "[@REFINECOLLECT]",
            ReplaceWedRingKey = "[@REPLACEWEDDINGRING]",
            BuyBackKey = "[@BUYBACK]",
            StorageKey = "[@STORAGE]",
            ConsignKey = "[@CONSIGN]",
            MarketKey = "[@MARKET]",
            CraftKey = "[@CRAFT]",

            GuildCreateKey = "[@CREATEGUILD]",
            RequestWarKey = "[@REQUESTWAR]",
            SendParcelKey = "[@SENDPARCEL]",
            CollectParcelKey = "[@COLLECTPARCEL]",
            AwakeningKey = "[@AWAKENING]",
            DisassembleKey = "[@DISASSEMBLE]",
            DowngradeKey = "[@DOWNGRADE]",
            ResetKey = "[@RESET]",
            PearlBuyKey = "[@PEARLBUY]",
            BuyUsedKey = "[@BUYUSED]",
            BuyNewKey = "[@BUYNEW]",
            BuySellNewKey = "[@BUYSELLNEW]",
            HeroCreateKey = "[@CREATEHERO]",
            HeroManageKey = "[@MANAGEHERO]",

            TradeKey = "[TRADE]",
            RecipeKey = "[RECIPE]",
            TypeKey = "[TYPES]",
            UsedTypeKey = "[USEDTYPES]",
            QuestKey = "[QUESTS]",
            SpeechKey = "[SPEECH]";


        public List<ItemType> Types = new List<ItemType>();
        public List<ItemType> UsedTypes = new List<ItemType>();
        public List<UserItem> Goods = new List<UserItem>();
        public List<RecipeInfo> CraftGoods = new List<RecipeInfo>();

        public List<NPCPage> NPCSections = new List<NPCPage>();
        public List<NPCPage> NPCPages = new List<NPCPage>();

        private NPCScript(uint loadedObjectID, string fileName, NPCScriptType type)
        {
            ScriptID = ++Envir.ScriptIndex;

            LoadedObjectID = loadedObjectID;
            FileName = fileName;
            Type = type;

            Load();

            Envir.Scripts.Add(ScriptID, this);
        }

        public void Load()
        {
            LoadInfo();
            LoadGoods();
        }

        private NPCObject GetCallingNpc(PlayerObject player)
        {
            if (player == null) return null;
            return Envir.NPCs.SingleOrDefault(x => x.ObjectID == player.NPCObjectID);
        }

        private float GetLegacyBuyRate(PlayerObject player, NPCObject callingNPC, bool baseRate = false)
        {
            if (callingNPC == null)
            {
                return 1F;
            }

            if (callingNPC.Conq == null || baseRate)
            {
                return callingNPC.Info.Rate / 100F;
            }

            if (player.MyGuild != null && player.MyGuild.Guildindex == callingNPC.Conq.GuildInfo.Owner)
            {
                return callingNPC.Info.Rate / 100F;
            }
            else
            {
                return (((callingNPC.Info.Rate / 100F) * callingNPC.Conq.GuildInfo.NPCRate) + callingNPC.Info.Rate) / 100F;
            }
        }

        private float GetLegacySellRate()
        {
            return 0.5F;
        }

        private ShopPriceRequest ResolveShopPrice(
            PlayerObject player,
            ShopPriceOperation operation,
            ShopPriceCurrency currency,
            UserItem item,
            ushort count,
            bool previewOnly,
            bool isUsedGoods,
            bool isBuyBack,
            NPCObject callingNPC = null)
        {
            if (player == null) return null;

            callingNPC ??= GetCallingNpc(player);
            if (callingNPC == null) return null;

            float baseRate;
            float rate;
            int conquestTaxRatePercent = 0;
            bool depositTaxToConquest = false;

            switch (operation)
            {
                case ShopPriceOperation.Buy:
                    baseRate = GetLegacyBuyRate(player, callingNPC, true);
                    rate = GetLegacyBuyRate(player, callingNPC);
                    if (callingNPC.Conq != null &&
                        (player.MyGuild == null || player.MyGuild.Guildindex != callingNPC.Conq.GuildInfo.Owner))
                    {
                        conquestTaxRatePercent = callingNPC.Conq.GuildInfo.NPCRate;
                        depositTaxToConquest = currency == ShopPriceCurrency.Gold && conquestTaxRatePercent > 0;
                    }
                    break;
                case ShopPriceOperation.Sell:
                    baseRate = GetLegacySellRate();
                    rate = baseRate;
                    break;
                default:
                    return null;
            }

            var request = new ShopPriceRequest(
                player,
                callingNPC,
                operation,
                currency,
                item,
                count,
                previewOnly,
                isUsedGoods,
                isBuyBack,
                baseRate,
                rate,
                conquestTaxRatePercent,
                depositTaxToConquest);

            var scriptsRuntimeActive = Settings.CSharpScriptsEnabled && Envir.CSharpScripts.Enabled;
            var allowBefore = scriptsRuntimeActive &&
                              ScriptDispatchPolicy.ShouldTryCSharp(ScriptHookKeys.OnShopPriceBefore) &&
                              ScriptDispatchPolicy.ShouldTryCSharp(ScriptHookKeys.OnShopPriceBeforeOperation(operation));
            var allowAfter = scriptsRuntimeActive &&
                             ScriptDispatchPolicy.ShouldTryCSharp(ScriptHookKeys.OnShopPriceAfter) &&
                             ScriptDispatchPolicy.ShouldTryCSharp(ScriptHookKeys.OnShopPriceAfterOperation(operation));

            if (allowBefore)
            {
                Envir.CSharpScripts.TryHandleShopPriceBefore(request);
            }

            if (request.Rate < 0)
                request.Rate = 0;

            if (allowAfter)
            {
                var result = new ShopPriceResult(
                    player,
                    callingNPC,
                    operation,
                    currency,
                    item,
                    count,
                    previewOnly,
                    isUsedGoods,
                    isBuyBack,
                    request.BaseRate,
                    request.Rate,
                    request.ConquestTaxRatePercent,
                    request.DepositTaxToConquest,
                    request.Decision == ScriptHookDecision.Continue,
                    request.Decision);

                Envir.CSharpScripts.TryHandleShopPriceAfter(result);
            }

            return request;
        }

        public float PriceRate(PlayerObject player, bool baseRate = false)
        {
            return GetLegacyBuyRate(player, GetCallingNpc(player), baseRate);
        }

        public float GetBuyRate(PlayerObject player, ShopPriceCurrency currency = ShopPriceCurrency.Gold)
        {
            var request = ResolveShopPrice(player, ShopPriceOperation.Buy, currency, null, 0, previewOnly: true, isUsedGoods: false, isBuyBack: false);
            return request?.Rate ?? PriceRate(player);
        }

        public float GetSellRate(PlayerObject player)
        {
            var request = ResolveShopPrice(player, ShopPriceOperation.Sell, ShopPriceCurrency.Gold, null, 0, previewOnly: true, isUsedGoods: false, isBuyBack: false);
            return request?.Rate ?? GetLegacySellRate();
        }

        public ShopPriceRequest GetBuyPriceRequest(PlayerObject player, UserItem item, ushort count, bool isUsedGoods, bool isBuyBack, ShopPriceCurrency currency = ShopPriceCurrency.Gold)
        {
            return ResolveShopPrice(player, ShopPriceOperation.Buy, currency, item, count, previewOnly: false, isUsedGoods: isUsedGoods, isBuyBack: isBuyBack);
        }

        public ShopPriceRequest GetSellPriceRequest(PlayerObject player, UserItem item, ushort count)
        {
            return ResolveShopPrice(player, ShopPriceOperation.Sell, ShopPriceCurrency.Gold, item, count, previewOnly: false, isUsedGoods: false, isBuyBack: false);
        }


        public void LoadInfo()
        {
            ClearInfo();

            // 可选优化：当 NPC 交互页已完全迁移为 C# 时，可跳过加载/解析 txt。
            // 注意：目前仅对 Normal（交互式 NPC）生效，避免影响 AutoPlayer/AutoMonster 的 legacy 扫描逻辑。
            var csharpScriptsEnabled = Settings.CSharpScriptsEnabled;

            // 最终目标：抛弃 Envir/NPCs/*.txt。
            // 说明：在启用 C# 脚本的前提下，脚本运行时可能尚未启动（例如编辑环境先 LoadDB、或服务尚未 Start）。
            // 此时不再尝试读取磁盘 txt（也不输出“找不到脚本”刷屏），等脚本系统就绪后由 C# 页面对话接管。
            if (csharpScriptsEnabled && !Envir.CSharpScripts.Enabled)
            {
                if (Settings.TxtScriptsLogLoads)
                    MessageQueue.Enqueue($"[Scripts][Load] NPC 脚本运行时未就绪，已跳过加载：{FileName}");

                return;
            }

            var scriptsRuntimeActive = csharpScriptsEnabled && Envir.CSharpScripts.Enabled;

            var allowSkipTxt = false;

            if (scriptsRuntimeActive && Settings.CSharpScriptsSkipTxtNpcLoad && Type == NPCScriptType.Normal)
            {
                // 当配置了白名单时，仅对白名单内的 NPC 文件跳过 txt（避免“全量开关”误伤未迁移的 NPC）。
                // 白名单为空则表示对所有 Normal NPC 生效（保持向后兼容：旧配置 SkipTxtNpcLoad=true 即“全量跳过”）。
                var allowlistSource = Settings.CSharpScriptsSkipTxtNpcLoadNpcFiles;

                if (string.IsNullOrWhiteSpace(allowlistSource) || Server.Scripting.NpcTxtSkipPolicy.IsNpcFileInAllowlist(FileName))
                {
                    allowSkipTxt = true;
                }
                else
                {
                    if (Settings.TxtScriptsLogLoads)
                        MessageQueue.Enqueue($"[TxtScripts] SkipTxtNpcLoad=true 但 {FileName}.txt 不在 SkipTxtNpcLoadNpcFiles 白名单中，仍加载该 NPC 脚本。");
                }
            }

            // C# 结构化 NPC 商店定义（不依赖 TextFileDefinition/raw txt）。
            // 当存在定义时，直接加载 Goods/Types/Quests 并跳过 legacy txt 解析。
            if (scriptsRuntimeActive && Type == NPCScriptType.Normal)
            {
                if (TryApplyCSharpShopDefinition())
                {
                    if (Settings.TxtScriptsLogLoads)
                        MessageQueue.Enqueue($"[Scripts] 加载 NPC 商店: {FileName} <- C# NpcShops（v{Envir.CSharpScripts.Version}）");

                    return;
                }
            }

            var npcFileKey = $"NPCs/{FileName}";

            // legacy txt 解析器护栏：当全局关闭 txt 回落时，不应再解析/执行 legacy DSL。
            if (scriptsRuntimeActive && !Settings.CSharpScriptsFallbackToTxt)
            {
                if (Settings.TxtScriptsLogLoads)
                    MessageQueue.Enqueue($"[TxtScripts] CSharpScriptsFallbackToTxt=false，跳过 legacy NPC 脚本解析：{FileName}.txt（key={npcFileKey}）");

                return;
            }

            if (allowSkipTxt)
            {
                // SkipTxtNpcLoad 仅在“不会回落到 txt”时才安全：
                // - 全局关闭回落：CSharpScriptsFallbackToTxt=false
                // - 或者：对该 NPC 的 NpcPage KeyPrefix（NPCs/<npcFileName>/...）通过 NoTxtFallback 策略禁用回落
                //   （即 TxtFallbackPolicy.ShouldFallbackToTxt(...) 返回 false）
                var probeKey = Server.Scripting.ScriptHookKeys.OnNpcPage(FileName, "__probe__");
                var allowFallbackToTxt = Server.Scripting.TxtFallbackPolicy.ShouldFallbackToTxt(probeKey);

                if (allowFallbackToTxt)
                {
                    if (Settings.TxtScriptsLogLoads)
                        MessageQueue.Enqueue($"[TxtScripts] SkipTxtNpcLoad=true 但仍允许回落TXT，仍加载 NPC 脚本: {FileName}.txt");
                }
                else
                {
                    if (Settings.TxtScriptsLogLoads)
                        MessageQueue.Enqueue($"[TxtScripts] 跳过加载 NPC 脚本: {FileName}.txt（SkipTxtNpcLoad=true, 禁止回落TXT）");

                    return;
                }
            }

            // 支持从 C# TextFiles 提供 NPC 脚本源文本（Key = NPCs/<FileName>），用于逐步退役 Envir/NPCs/*.txt。
            // 说明：这里仅替换“脚本源文本”的读取来源，不影响“页级别 Hook（C#）优先 / legacy txt 回落”的执行策略。
            // 注意：若你希望仍走磁盘 txt，请通过 PreferKeyPrefixes/DisabledKeys 策略控制 ShouldTryCSharp 返回 false。
            List<string> lines = null;

            if (scriptsRuntimeActive)
            {
                var allowCSharp = Server.Scripting.ScriptDispatchPolicy.ShouldTryCSharp(npcFileKey);

                if (allowCSharp && Envir.TextFileProvider != null)
                {
                    var definition = Envir.TextFileProvider.GetByKey(npcFileKey);
                    if (definition != null)
                    {
                        lines = new List<string>(definition.Lines);

                        if (Settings.TxtScriptsLogLoads)
                            MessageQueue.Enqueue($"[TxtScripts] 加载 NPC 脚本: {FileName}.txt <- C# TextFiles（key={npcFileKey}，v{Envir.CSharpScripts.Version}）");
                    }
                    else if (!Server.Scripting.TxtFallbackPolicy.ShouldFallbackToTxt(npcFileKey))
                    {
                        if (Settings.TxtScriptsLogLoads)
                            MessageQueue.Enqueue($"[TxtScripts] NPC 脚本 C# 定义缺失且禁止回落TXT：{FileName}.txt（key={npcFileKey}）");

                        return;
                    }
                }
                else if (!Server.Scripting.TxtFallbackPolicy.ShouldFallbackToTxt(npcFileKey))
                {
                    if (Settings.TxtScriptsLogLoads)
                        MessageQueue.Enqueue($"[TxtScripts] NPC 脚本禁止回落TXT且跳过 C#：{FileName}.txt（key={npcFileKey}）");

                    return;
                }
            }

            if (lines == null)
            {
                if (Settings.TxtScriptsLogLoads)
                    MessageQueue.Enqueue($"[TxtScripts] 找不到 NPC 脚本源：{FileName}.txt（key={npcFileKey}，无磁盘回落）");
                else
                    MessageQueue.Enqueue(string.Format("找不到脚本: {0}", FileName));

                return;
            }

            if (Settings.TxtScriptsEnableInsertInclude)
            {
                lines = ParseInsert(lines);
                lines = ParseInclude(lines);
            }
            else
            {
                var removed = lines.RemoveAll(str =>
                    !string.IsNullOrWhiteSpace(str)
                    && (str.TrimStart().StartsWith("#INSERT", StringComparison.OrdinalIgnoreCase)
                        || str.TrimStart().StartsWith("#INCLUDE", StringComparison.OrdinalIgnoreCase)));

                if (removed > 0 && Settings.TxtScriptsLogLoads)
                {
                    MessageQueue.Enqueue($"[TxtScripts] 已忽略 {FileName}.txt 中 {removed} 行 #INSERT/#INCLUDE（TxtScriptsEnableInsertInclude=false）");
                }
            }

            switch (Type)
            {
                case NPCScriptType.Normal:
                default:
                    ParseScript(lines);
                    break;
                case NPCScriptType.AutoPlayer:
                case NPCScriptType.AutoMonster:
                case NPCScriptType.Robot:
                    ParseDefault(lines);
                    break;
            }
        }

        private bool TryApplyCSharpShopDefinition()
        {
            try
            {
                var registry = Envir.CSharpScripts?.CurrentRegistry;
                var shopRegistry = registry?.NpcShops;

                if (shopRegistry == null) return false;

                if (!shopRegistry.TryGetByNpcFileName(FileName, out var definition) || definition == null)
                    return false;

                return TryApplyShopDefinition(definition);
            }
            catch (Exception ex)
            {
                MessageQueue.Enqueue($"[Scripts] 应用 C# NPC 商店定义失败：{FileName} err={ex}");
                return false;
            }
        }

        internal bool TryApplyShopDefinition(Server.Scripting.NpcShopDefinition definition)
        {
            if (definition == null) return false;

            // 只覆盖“商店数据”，不主动清理 NPCPages（对话页由 C# INpcScriptModule 接管）。
            Goods = new List<UserItem>();
            Types = new List<ItemType>();
            UsedTypes = new List<ItemType>();
            CraftGoods = new List<RecipeInfo>();

            if (definition.Types != null)
            {
                for (var i = 0; i < definition.Types.Count; i++)
                    Types.Add(definition.Types[i]);
            }

            if (definition.UsedTypes != null)
            {
                for (var i = 0; i < definition.UsedTypes.Count; i++)
                    UsedTypes.Add(definition.UsedTypes[i]);
            }

            var missingGoods = new List<string>();
            var missingRecipes = new List<string>();
            var missingRecipeMaterials = new List<string>();

            uint uid = 0;
            if (definition.Goods != null)
            {
                for (var i = 0; i < definition.Goods.Count; i++)
                {
                    var good = definition.Goods[i];
                    var itemName = good.ItemName;

                    var info = Envir.GetItemInfo(itemName);
                    if (info == null)
                    {
                        missingGoods.Add(itemName);
                        continue;
                    }

                    var goods = Envir.CreateShopItem(info, ++uid);
                    if (goods == null)
                        continue;

                    goods.Count = good.Count;
                    Goods.Add(goods);
                }
            }

            if (definition.CraftRecipeOutputItemNames != null)
            {
                for (var i = 0; i < definition.CraftRecipeOutputItemNames.Count; i++)
                {
                    var itemName = definition.CraftRecipeOutputItemNames[i];
                    if (string.IsNullOrWhiteSpace(itemName))
                        continue;

                    var recipe = Envir.GetRecipeInfoByOutputItemName(itemName);

                    if (recipe == null)
                    {
                        missingRecipes.Add(itemName);
                        continue;
                    }

                    if (recipe.Ingredients.Count == 0)
                    {
                        missingRecipeMaterials.Add(itemName);
                        continue;
                    }

                    CraftGoods.Add(recipe);
                }
            }

            LogShopDefinitionIssues("未找到物品", missingGoods);
            LogShopDefinitionIssues("缺少配方", missingRecipes);
            LogShopDefinitionIssues("缺少材料", missingRecipeMaterials);

            if (definition.QuestIndices != null && definition.QuestIndices.Count > 0)
            {
                var loadedNPC = NPCObject.Get(LoadedObjectID);

                if (loadedNPC != null)
                {
                    for (var i = 0; i < definition.QuestIndices.Count; i++)
                    {
                        var index = definition.QuestIndices[i];
                        if (index == 0) continue;

                        var quest = Envir.GetQuestInfo(Math.Abs(index));
                        if (quest == null) return false;

                        if (index > 0)
                            quest.NpcIndex = LoadedObjectID;
                        else
                            quest.FinishNpcIndex = LoadedObjectID;

                        if (loadedNPC.Quests.All(x => x != quest))
                            loadedNPC.Quests.Add(quest);
                    }
                }
            }

            return true;
        }

        private void LogShopDefinitionIssues(string issueType, List<string> names)
        {
            if (names == null || names.Count == 0)
                return;

            if (FileName.StartsWith("游戏管理/", StringComparison.OrdinalIgnoreCase))
                return;

            var distinctNames = names
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (distinctNames.Count == 0)
                return;

            var preview = string.Join("、", distinctNames.Take(8));
            if (distinctNames.Count > 8)
                preview += $" 等 {distinctNames.Count} 项";

            MessageQueue.Enqueue($"[Scripts] NPC 商店定义 {issueType}: {FileName} -> {preview}");
        }

        public void ClearInfo()
        {
            Goods = new List<UserItem>();
            Types = new List<ItemType>();
            UsedTypes = new List<ItemType>();
            NPCPages = new List<NPCPage>();
            CraftGoods = new List<RecipeInfo>();

            if (Type == NPCScriptType.AutoPlayer)
            {
                Envir.CustomCommands.Clear();
            }
        }
        public void LoadGoods()
        {
            var loadedNPC = NPCObject.Get(LoadedObjectID);

            if (loadedNPC != null)
            {
                loadedNPC.UsedGoods.Clear();

                string path = Path.Combine(Settings.GoodsPath, loadedNPC.Info.Index.ToString() + ".msd");

                if (!File.Exists(path)) return;

                using (FileStream stream = File.OpenRead(path))
                {
                    using (BinaryReader reader = new BinaryReader(stream))
                    {
                        int version = reader.ReadInt32();
                        int count = version;
                        int customversion = Envir.LoadCustomVersion;
                        if (version == 9999)//the only real way to tell if the file was made before or after version code got added: assuming nobody had a config option to save more then 10000 sold items
                        {
                            version = reader.ReadInt32();
                            customversion = reader.ReadInt32();
                            count = reader.ReadInt32();
                        }
                        else
                            version = Envir.LoadVersion;

                        for (int k = 0; k < count; k++)
                        {
                            UserItem item = new UserItem(reader, version, customversion);
                            if (Envir.BindItem(item))
                                loadedNPC.UsedGoods.Add(item);
                        }
                    }
                }
            }
        }

        private void ParseDefault(List<string> lines)
        {
            if (Settings.CSharpScriptsEnabled && Envir.CSharpScripts.Enabled && !Settings.CSharpScriptsFallbackToTxt)
            {
                if (Settings.TxtScriptsLogLoads)
                    MessageQueue.Enqueue($"[TxtScripts] CSharpScriptsFallbackToTxt=false，阻止 legacy ParseDefault：{FileName}.txt");

                return;
            }

            for (int i = 0; i < lines.Count; i++)
            {
                if (!lines[i].ToUpper().StartsWith("[@_")) continue;

                if (Type == NPCScriptType.AutoPlayer)
                {
                    if (lines[i].ToUpper().Contains("MAPCOORD"))
                    {
                        Regex regex = new Regex(@"\((.*?),([0-9]{1,3}),([0-9]{1,3})\)");
                        Match match = regex.Match(lines[i]);

                        if (!match.Success) continue;

                        Map map = Envir.MapList.FirstOrDefault(m => m.Info.FileName == match.Groups[1].Value);

                        if (map == null) continue;

                        Point point = new Point(Convert.ToInt16(match.Groups[2].Value), Convert.ToInt16(match.Groups[3].Value));

                        if (!map.Info.ActiveCoords.Contains(point))
                        {
                            map.Info.ActiveCoords.Add(point);
                        }
                    }

                    if (lines[i].ToUpper().Contains("CUSTOMCOMMAND"))
                    {
                        Regex regex = new Regex(@"\((.*?)\)");
                        Match match = regex.Match(lines[i]);

                        if (!match.Success) continue;

                        Envir.CustomCommands.Add(match.Groups[1].Value);
                    }
                }
                else if (Type == NPCScriptType.AutoMonster)
                {
                    MonsterInfo MobInfo;
                    if (lines[i].ToUpper().Contains("SPAWN"))
                    {
                        Regex regex = new Regex(@"\((.*?)\)");
                        Match match = regex.Match(lines[i]);

                        if (!match.Success) continue;
                        MobInfo = Envir.GetMonsterInfo(Convert.ToInt16(match.Groups[1].Value));
                        if (MobInfo == null) continue;
                        MobInfo.HasSpawnScript = true;
                    }
                    if (lines[i].ToUpper().Contains("DIE"))
                    {
                        Regex regex = new Regex(@"\((.*?)\)");
                        Match match = regex.Match(lines[i]);

                        if (!match.Success) continue;
                        MobInfo = Envir.GetMonsterInfo(Convert.ToInt16(match.Groups[1].Value));
                        if (MobInfo == null) continue;
                        MobInfo.HasDieScript = true;
                    }
                }
                else if (Type == NPCScriptType.Robot)
                {
                    if (lines[i].ToUpper().Contains("TIME"))
                    {
                        Robot.AddRobot(lines[i].ToUpper());
                    }
                }

                NPCPages.AddRange(ParsePages(lines, lines[i]));
            }
        }

        private void ParseScript(IList<string> lines)
        {
            if (Settings.CSharpScriptsEnabled && Envir.CSharpScripts.Enabled && !Settings.CSharpScriptsFallbackToTxt)
            {
                if (Settings.TxtScriptsLogLoads)
                    MessageQueue.Enqueue($"[TxtScripts] CSharpScriptsFallbackToTxt=false，阻止 legacy ParseScript：{FileName}.txt");

                return;
            }

            NPCPages.AddRange(ParsePages(lines));

            ParseGoods(lines);
            ParseTypes(lines);
            ParseQuests(lines);
            ParseCrafting(lines);
            ParseSpeech(lines);
        }

        private List<string> ParseInsert(List<string> lines)
        {
            List<string> newLines = new List<string>();

            for (int i = 0; i < lines.Count; i++)
            {
                if (!lines[i].ToUpper().StartsWith("#INSERT")) continue;

                string[] split = lines[i].Split(' ');

                if (split.Length < 2) continue;

                string path = Path.Combine(Settings.EnvirPath, split[1].Substring(1, split[1].Length - 2));

                if (!TryReadAllLinesWithTrace(path, $"#INSERT:{FileName}", out newLines))
                {
                    MessageQueue.Enqueue(string.Format("INSERT:未找到要调用的文件或脚本 {0}", path));
                    newLines = new List<string>();
                }

                lines.AddRange(newLines);
            }

            lines.RemoveAll(str => str.ToUpper().StartsWith("#INSERT"));

            return lines;
        }

        private List<string> ParseInclude(List<string> lines)
        {
            for (int i = 0; i < lines.Count; i++)
            {
                if (!lines[i].ToUpper().StartsWith("#INCLUDE")) continue;

                string[] split = lines[i].Split(' ');

                string path = Path.Combine(Settings.EnvirPath, split[1].Substring(1, split[1].Length - 2));
                string page = ("[" + split[2] + "]").ToUpper();

                bool start = false, finish = false;

                var parsedLines = new List<string>();

                if (!TryReadAllLinesWithTrace(path, $"#INCLUDE:{FileName}", out var extLines))
                {
                    MessageQueue.Enqueue(string.Format("INCLUDE:未找到要调用的脚本或文件 {0}", path));
                    continue;
                }

                for (int j = 0; j < extLines.Count; j++)
                {
                    if (!extLines[j].ToUpper().StartsWith(page)) continue;

                    for (int x = j + 1; x < extLines.Count; x++)
                    {
                        if (extLines[x].Trim() == ("{"))
                        {
                            start = true;
                            continue;
                        }

                        if (extLines[x].Trim() == ("}"))
                        {
                            finish = true;
                            break;
                        }

                        parsedLines.Add(extLines[x]);
                    }
                }

                if (start && finish)
                {
                    lines.InsertRange(i + 1, parsedLines);
                    parsedLines.Clear();
                }
            }

            lines.RemoveAll(str => str.ToUpper().StartsWith("#INCLUDE"));

            return lines;
        }

        private static string TryGetLogicKeyForTxtPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            try
            {
                var fullPath = Path.GetFullPath(path);

                var envirRoot = Path.GetFullPath(Settings.EnvirPath);
                var envirRootWithSep = envirRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

                if (!fullPath.StartsWith(envirRootWithSep, StringComparison.OrdinalIgnoreCase))
                    return null;

                var relative = fullPath.Substring(envirRootWithSep.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                relative = relative.Replace('\\', '/');

                return LogicKey.TryNormalize(relative, out var key) ? key : null;
            }
            catch
            {
                return null;
            }
        }

        private static bool TryReadAllLinesWithTrace(string path, string source, out List<string> lines)
        {
            lines = new List<string>();

            var key = TryGetLogicKeyForTxtPath(path);
            var scriptsRuntimeActive = Settings.CSharpScriptsEnabled && Envir.CSharpScripts.Enabled;

            if (!string.IsNullOrWhiteSpace(key))
            {
                var allowCSharp = scriptsRuntimeActive && ScriptDispatchPolicy.ShouldTryCSharp(key);

                if (allowCSharp && Envir.TextFileProvider != null)
                {
                    var definition = Envir.TextFileProvider.GetByKey(key);
                    if (definition != null)
                    {
                        lines = definition.Lines != null ? definition.Lines.ToList() : new List<string>();

                        if (Settings.TxtScriptsLogLoads)
                        {
                            MessageQueue.Enqueue($"[TxtScripts] C# TextFiles 命中 key={key} lines={lines.Count} 来源={source}");
                        }

                        return true;
                    }

                    if (scriptsRuntimeActive && !Server.Scripting.TxtFallbackPolicy.ShouldFallbackToTxt(key))
                    {
                        if (Settings.TxtScriptsLogLoads)
                        {
                            MessageQueue.Enqueue($"[TxtScripts] 禁止回落TXT且未命中 C# TextFiles: key={key} 来源={source}");
                        }

                        return true;
                    }
                }
                else if (scriptsRuntimeActive && !Server.Scripting.TxtFallbackPolicy.ShouldFallbackToTxt(key))
                {
                    if (Settings.TxtScriptsLogLoads)
                    {
                        MessageQueue.Enqueue($"[TxtScripts] 禁止回落TXT且策略跳过 C# TextFiles: key={key} 来源={source}");
                    }

                    return true;
                }
            }

            // 已移除磁盘 txt 回落：只允许从 C# TextFiles 提供源文本。
            return false;
        }


        private List<NPCPage> ParsePages(IList<string> lines, string key = MainKey)
        {
            List<NPCPage> pages = new List<NPCPage>();
            List<string> buttons = new List<string>();

            NPCPage page = ParsePage(lines, key);
            pages.Add(page);

            buttons.AddRange(page.Buttons);

            for (int i = 0; i < buttons.Count; i++)
            {
                string section = buttons[i];

                bool match = pages.Any(t => t.Key.ToUpper() == section.ToUpper());

                if (match) continue;

                page = ParsePage(lines, section);
                buttons.AddRange(page.Buttons);

                pages.Add(page);
            }

            return pages;
        }

        private NPCPage ParsePage(IList<string> scriptLines, string sectionName)
        {
            bool nextPage = false, nextSection = false;

            List<string> lines = scriptLines.Where(x => !string.IsNullOrEmpty(x)).ToList();

            NPCPage Page = new NPCPage(sectionName);

            //Cleans arguments out of search page name
            string tempSectionName = Page.ArgumentParse(sectionName);

            //parse all individual pages in a script, defined by sectionName
            for (int i = 0; i < lines.Count; i++)
            {
                string line = lines[i];

                if (line.StartsWith(";")) continue;

                if (!lines[i].ToUpper().StartsWith(tempSectionName.ToUpper())) continue;

                List<string> segmentLines = new List<string>();

                nextPage = false;

                //Found a page, now process that page and split it into segments
                for (int j = i + 1; j < lines.Count; j++)
                {
                    string nextLine = lines[j];

                    if (j < lines.Count - 1)
                        nextLine = lines[j + 1];
                    else
                        nextLine = "";

                    if (nextLine.StartsWith("[") && nextLine.EndsWith("]"))
                    {
                        nextPage = true;
                    }

                    else if (nextLine.StartsWith("#IF"))
                    {
                        nextSection = true;
                    }

                    if (nextSection || nextPage)
                    {
                        segmentLines.Add(lines[j]);

                        //end of segment, so need to parse it and put into the segment list within the page
                        if (segmentLines.Count > 0)
                        {
                            NPCSegment segment = ParseSegment(Page, segmentLines);

                            List<string> currentButtons = new List<string>();
                            currentButtons.AddRange(segment.Buttons);
                            currentButtons.AddRange(segment.ElseButtons);
                            currentButtons.AddRange(segment.GotoButtons);

                            Page.Buttons.AddRange(currentButtons);
                            Page.SegmentList.Add(segment);
                            segmentLines.Clear();

                            nextSection = false;
                        }

                        if (nextPage) break;

                        continue;
                    }

                    segmentLines.Add(lines[j]);
                }

                //bottom of script reached, add all lines found to new segment
                if (segmentLines.Count > 0)
                {
                    NPCSegment segment = ParseSegment(Page, segmentLines);

                    List<string> currentButtons = new List<string>();
                    currentButtons.AddRange(segment.Buttons);
                    currentButtons.AddRange(segment.ElseButtons);
                    currentButtons.AddRange(segment.GotoButtons);

                    Page.Buttons.AddRange(currentButtons);
                    Page.SegmentList.Add(segment);
                    segmentLines.Clear();
                }

                return Page;
            }

            return Page;
        }

        private NPCSegment ParseSegment(NPCPage page, IEnumerable<string> scriptLines)
        {
            List<string>
                checks = new List<string>(),
                acts = new List<string>(),
                say = new List<string>(),
                buttons = new List<string>(),
                elseSay = new List<string>(),
                elseActs = new List<string>(),
                elseButtons = new List<string>(),
                gotoButtons = new List<string>();

            List<string> lines = scriptLines.ToList();
            List<string> currentSay = say, currentButtons = buttons;

            Regex regex = new Regex(@"<.*?/(\@.*?)>");

            for (int i = 0; i < lines.Count; i++)
            {
                if (string.IsNullOrEmpty(lines[i])) continue;

                if (lines[i].StartsWith(";")) continue;

                if (lines[i].StartsWith("#"))
                {
                    string[] action = lines[i].Remove(0, 1).ToUpper().Trim().Split(' ');
                    switch (action[0])
                    {
                        case "IF":
                            currentSay = checks;
                            currentButtons = null;
                            continue;
                        case "SAY":
                            currentSay = say;
                            currentButtons = buttons;
                            continue;
                        case "ACT":
                            currentSay = acts;
                            currentButtons = gotoButtons;
                            continue;
                        case "ELSESAY":
                            currentSay = elseSay;
                            currentButtons = elseButtons;
                            continue;
                        case "ELSEACT":
                            currentSay = elseActs;
                            currentButtons = gotoButtons;
                            continue;
                        default:
                            throw new NotImplementedException();
                    }
                }

                if (lines[i].StartsWith("[") && lines[i].EndsWith("]")) break;

                if (currentButtons != null)
                {
                    Match match = regex.Match(lines[i]);

                    while (match.Success)
                    {
                        string argu = match.Groups[1].Captures[0].Value;
                        argu = argu.Split('/')[0];

                        currentButtons.Add(string.Format("[{0}]", argu));
                        match = match.NextMatch();
                    }

                    //Check if line has a goto command
                    var parts = lines[i].Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

                    if (parts.Count() > 1)
                        switch (parts[0].ToUpper())
                        {
                            case "GOTO":
                            case "GROUPGOTO":
                                gotoButtons.Add(string.Format("[{0}]", parts[1].ToUpper()));
                                break;
                            case "TIMERECALL":
                            case "DELAYGOTO":
                            case "TIMERECALLGROUP":
                                if (parts.Length > 2)
                                    gotoButtons.Add(string.Format("[{0}]", parts[2].ToUpper()));
                                break;
                            case "ROLLDIE":
                            case "ROLLYUT":
                                buttons.Add(string.Format("[{0}]", parts[1].ToUpper()));
                                break;
                        }
                }

                currentSay.Add(lines[i].TrimEnd());
            }

            NPCSegment segment = new NPCSegment(page, say, buttons, elseSay, elseButtons, gotoButtons);

            for (int i = 0; i < checks.Count; i++)
                segment.ParseCheck(checks[i]);

            for (int i = 0; i < acts.Count; i++)
                segment.ParseAct(segment.ActList, acts[i]);

            for (int i = 0; i < elseActs.Count; i++)
                segment.ParseAct(segment.ElseActList, elseActs[i]);

            currentButtons = new List<string>();
            currentButtons.AddRange(buttons);
            currentButtons.AddRange(elseButtons);
            currentButtons.AddRange(gotoButtons);

            return segment;
        }

        private void ParseTypes(IList<string> lines)
        {
            for (int i = 0; i < lines.Count; i++)
            {
                if (!lines[i].ToUpper().StartsWith(TypeKey)) continue;

                while (++i < lines.Count)
                {
                    if (String.IsNullOrEmpty(lines[i])) continue;

                    if (!int.TryParse(lines[i], out int index)) break;
                    Types.Add((ItemType)index);
                }
            }

            for (int i = 0; i < lines.Count; i++)
            {
                if (!lines[i].ToUpper().StartsWith(UsedTypeKey)) continue;

                while (++i < lines.Count)
                {
                    if (String.IsNullOrEmpty(lines[i])) continue;

                    if (!int.TryParse(lines[i], out int index)) break;
                    UsedTypes.Add((ItemType)index);
                }
            }
        }
        private void ParseGoods(IList<string> lines)
        {
            for (int i = 0; i < lines.Count; i++)
            {
                if (!lines[i].ToUpper().StartsWith(TradeKey)) continue;

                while (++i < lines.Count)
                {
                    if (lines[i].StartsWith("[")) return;
                    if (String.IsNullOrEmpty(lines[i])) continue;

                    var data = lines[i].Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

                    ItemInfo info = Envir.GetItemInfo(data[0]);
                    if (info == null)
                        continue;

                    UserItem goods = Envir.CreateShopItem(info, (uint)i);

                    if (goods == null || Goods.Contains(goods))
                    {
                        MessageQueue.Enqueue(string.Format("{1} 中未找到 {0} ", lines[i], FileName));
                        continue;
                    }

                    ushort count = 1;
                    if (data.Length == 2)
                        ushort.TryParse(data[1], out count);

                    goods.Count = count;

                    Goods.Add(goods);
                }
            }
        }
        private void ParseQuests(IList<string> lines)
        {
            for (int i = 0; i < lines.Count; i++)
            {
                if (!lines[i].ToUpper().StartsWith(QuestKey)) continue;

                var loadedNPC = NPCObject.Get(LoadedObjectID);

                if (loadedNPC == null)
                {
                    return;
                }

                while (++i < lines.Count)
                {
                    if (lines[i].StartsWith("[")) return;
                    if (String.IsNullOrEmpty(lines[i])) continue;

                    int.TryParse(lines[i], out int index);

                    if (index == 0) continue;

                    QuestInfo info = Envir.GetQuestInfo(Math.Abs(index));

                    if (info == null) return;

                    if (index > 0)
                        info.NpcIndex = LoadedObjectID;
                    else
                        info.FinishNpcIndex = LoadedObjectID;

                    if (loadedNPC.Quests.All(x => x != info))
                        loadedNPC.Quests.Add(info);

                }
            }
        }


        private void ParseSpeech(IList<string> lines)
        {
            var loadedNPC = NPCObject.Get(LoadedObjectID);

            if (loadedNPC == null)
            {
                return;
            }

            for (int i = 0; i < lines.Count; i++)
            {
                if (!lines[i].ToUpper().StartsWith(SpeechKey)) continue;

                while (++i < lines.Count)
                {
                    if (String.IsNullOrEmpty(lines[i])) continue;

                    var parts = lines[i].Split(' ');

                    if (parts.Length < 2 || !int.TryParse(parts[0], out int weight)) return;

                    loadedNPC.Speech.Add(new NPCSpeech { Weight = weight, Message = lines[i].Substring(parts[0].Length + 1) });
                }
            }
        }
        private void ParseCrafting(IList<string> lines)
        {
            for (int i = 0; i < lines.Count; i++)
            {
                if (!lines[i].ToUpper().StartsWith(RecipeKey)) continue;

                while (++i < lines.Count)
                {
                    if (lines[i].StartsWith("[")) return;
                    if (String.IsNullOrEmpty(lines[i])) continue;

                    var data = lines[i].Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

                    ItemInfo info = Envir.GetItemInfo(data[0]);
                    if (info == null)
                        continue;

                    RecipeInfo recipe = Envir.RecipeInfoList.SingleOrDefault(x => x.MatchItem(info.Index));

                    if (recipe == null)
                    {
                        MessageQueue.Enqueue(string.Format("缺少配方: {0}, 文件名: {1}", lines[i], FileName));
                        continue;
                    }

                    if (recipe.Ingredients.Count == 0)
                    {
                        MessageQueue.Enqueue(string.Format("缺少材料: {0}, 文件名: {1}", lines[i], FileName));
                        continue;
                    }

                    CraftGoods.Add(recipe);
                }
            }
        }

        public void Call(MonsterObject monster, string key)
        {
            key = key.ToUpper();

            for (int i = 0; i < NPCPages.Count; i++)
            {
                NPCPage page = NPCPages[i];
                if (!String.Equals(page.Key, key, StringComparison.CurrentCultureIgnoreCase)) continue;

                foreach (NPCSegment segment in page.SegmentList)
                {
                    if (page.BreakFromSegments)
                    {
                        page.BreakFromSegments = false;
                        break;
                    }

                    ProcessSegment(monster, page, segment);
                }
            }
        }
        public void Call(string key)
        {
            key = key.ToUpper();

            for (int i = 0; i < NPCPages.Count; i++)
            {
                NPCPage page = NPCPages[i];
                if (!String.Equals(page.Key, key, StringComparison.CurrentCultureIgnoreCase)) continue;

                foreach (NPCSegment segment in page.SegmentList)
                {
                    if (page.BreakFromSegments)
                    {
                        page.BreakFromSegments = false;
                        break;
                    }

                    ProcessSegment(page, segment);
                }
            }
        }
        public void Call(PlayerObject player, uint objectID, string key)
        {
            key = key.ToUpper();

            if (!player.NPCDelayed)
            {
                if (key != MainKey)
                {
                    if (player.NPCObjectID != objectID) return;

                    bool found = false;

                    foreach (NPCSegment segment in player.NPCPage.SegmentList)
                    {
                        if (!player.NPCSuccess.TryGetValue(segment, out bool result)) break; //no result for segment ?

                        if ((result ? segment.Buttons : segment.ElseButtons).Any(s => s.ToUpper() == key))
                        {
                            found = true;
                        }
                    }

                    if (!found)
                    {
                        MessageQueue.Enqueue(string.Format("玩家: {0} 执行NPC脚本命令: '{1}' 被阻止 ", player.Name, key));
                        return;
                    }
                }
            }
            else
            {
                player.NPCDelayed = false;
            }

            if (key.StartsWith("[@@") && !player.NPCData.TryGetValue("NPCInputStr", out object _npcInputStr))
            {
                //send off packet to request input
                player.Enqueue(new S.NPCRequestInput { NPCID = player.NPCObjectID, PageName = key });
                return;
            }

            var scriptsRuntimeActive = Settings.CSharpScriptsEnabled && Envir.CSharpScripts.Enabled;

            if (scriptsRuntimeActive)
            {
                // 对齐 NPCPage.ArgumentParse：Default NPC page 不使用参数占位机制
                var definitionKey = key;

                if (!key.StartsWith("[@_", StringComparison.OrdinalIgnoreCase))
                {
                    var r = new Regex(@"\((.*)\)");
                    var m = r.Match(key);
                    if (m.Success)
                    {
                        definitionKey = Regex.Replace(key, r.ToString(), "()");
                    }
                }

                var policyKey = Server.Scripting.ScriptHookKeys.OnNpcPage(FileName, definitionKey);
                var allowCSharp = Server.Scripting.ScriptDispatchPolicy.ShouldTryCSharp(policyKey);

                if (!allowCSharp)
                {
                    if (Settings.TxtScriptsLogDispatch)
                        MessageQueue.Enqueue($"[Scripts][Dispatch] NpcPage {FileName} {key} -> 跳过C#（policy key={policyKey}）");

                    if (!Server.Scripting.TxtFallbackPolicy.ShouldFallbackToTxt(policyKey))
                    {
                        if (Settings.TxtScriptsLogDispatch)
                            MessageQueue.Enqueue($"[Scripts][Dispatch] NpcPage {FileName} {key} -> 阻止回落TXT（policy key={policyKey}，跳过C#）");

                        player.NPCData.Remove("NPCInputStr");
                        return;
                    }
                }
                else
                {
                    var handled = Envir.CSharpScripts.TryHandleNpcPage(player, FileName, objectID, ScriptID, key, out _, out var dialog);

                    if (Settings.TxtScriptsLogDispatch)
                    {
                        if (handled)
                            MessageQueue.Enqueue($"[Scripts][Dispatch] NpcPage {FileName} {key} -> C#（v{Envir.CSharpScripts.Version}，handlers={Envir.CSharpScripts.LastRegisteredHandlerCount}）");
                        else if (!Server.Scripting.TxtFallbackPolicy.ShouldFallbackToTxt(policyKey))
                            MessageQueue.Enqueue($"[Scripts][Dispatch] NpcPage {FileName} {key} -> 阻止回落TXT（v{Envir.CSharpScripts.Version}，handlers={Envir.CSharpScripts.LastRegisteredHandlerCount}）");
                    }

                    if (handled)
                    {
                        // Say 文本：沿用旧 txt 的 ReplaceValue 机制（保持 {}/颜色/占位符 等语法兼容）
                        var speech = new List<string>(dialog.Lines);

                        if (speech.Count > 0)
                        {
                            var parserPage = new NPCPage(key);
                            var parserSegment = new NPCSegment(parserPage, new List<string>(), new List<string>(), new List<string>(), new List<string>(), new List<string>());
                            speech = parserSegment.ParseSay(player, speech);
                        }

                        player.NPCSpeech = speech;

                        // 生成最小页面结构，用于后续按钮 Key 校验（C# 页面对话也遵守“只能点本页给出的按钮”）
                        var page = new NPCPage(key);
                        var allowedButtons = dialog.AllowedPageKeys.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
                        var segment = new NPCSegment(page, new List<string>(), allowedButtons, new List<string>(), new List<string>(), new List<string>());

                        page.SegmentList.Add(segment);
                        page.Buttons.AddRange(allowedButtons);

                        player.NPCObjectID = objectID;
                        player.NPCScriptID = ScriptID;
                        player.NPCPage = page;

                        player.NPCSuccess.Clear();
                        player.NPCSuccess.Add(segment, true);

                        if (Settings.TxtScriptsLogDispatch &&
                            (player.NPCSpeech == null || player.NPCSpeech.Count == 0) &&
                            allowedButtons.Count == 0)
                        {
                            MessageQueue.Enqueue($"[Scripts][NpcDialog] C# NPCResponse 为空：Player={player.Name} NPCFile={FileName} Key={key} ObjectID={objectID}");
                        }

                        player.Enqueue(new S.NPCResponse { Page = player.NPCSpeech });

                        // 服务端 GOTO：对齐 txt 的 ActionType.Goto（投递到 CallNPCNextPage 处理）
                        if (!string.IsNullOrWhiteSpace(dialog.RedirectToPageKey))
                        {
                            var action = new DelayedAction(DelayedType.NPC, -1, objectID, ScriptID, dialog.RedirectToPageKey);
                            player.ActionList.Add(action);
                        }

                        player.NPCData.Remove("NPCInputStr");
                        return;
                    }

                    if (!Server.Scripting.TxtFallbackPolicy.ShouldFallbackToTxt(policyKey))
                    {
                        player.NPCData.Remove("NPCInputStr");
                        return;
                    }
                }
            }

            if (Settings.TxtScriptsUsageTraceEnabled)
            {
                var txtKey = $"NPCs/{FileName}";
                Server.Scripting.TxtUsageTracker.RecordDispatchKey(txtKey, $"NpcPage:{FileName} {key}");
            }

            bool legacyPageMatched = false;
            for (int i = 0; i < NPCPages.Count; i++)
            {
                NPCPage page = NPCPages[i];
                if (!String.Equals(page.Key, key, StringComparison.CurrentCultureIgnoreCase)) continue;
                legacyPageMatched = true;

                var metricsEnabled = Settings.ScriptsRuntimeMetricsEnabled;
                var start = metricsEnabled ? Server.Scripting.ScriptRuntimeMetrics.GetTimestamp() : 0;

                try
                {
                    player.NPCSpeech = new List<string>();
                    player.NPCSuccess.Clear();

                    foreach (NPCSegment segment in page.SegmentList)
                    {
                        if (page.BreakFromSegments)
                        {
                            page.BreakFromSegments = false;
                            break;
                        }

                        ProcessSegment(player, page, segment, objectID);
                    }

                    Response(player, page);
                }
                finally
                {
                    if (metricsEnabled)
                    {
                        var elapsed = Server.Scripting.ScriptRuntimeMetrics.GetTimestamp() - start;
                        Server.Scripting.ScriptRuntimeMetrics.RecordLegacyNpcPage(FileName, key, elapsed);
                    }
                }
            }

            if (!legacyPageMatched && Settings.TxtScriptsLogDispatch)
            {
                var csharpState = scriptsRuntimeActive ? $"C#=on(v{Envir.CSharpScripts.Version}, handlers={Envir.CSharpScripts.LastRegisteredHandlerCount})" : "C#=off";
                MessageQueue.Enqueue($"[Scripts][NpcDialog] 未找到NPC页：Player={player.Name} NPCFile={FileName} Key={key} ObjectID={objectID} {csharpState}（请检查 Envir/NPCs/{FileName}.txt 是否存在该页）");
            }

            // 兼容：当 NPC 页未命中时，也要给客户端一个可见响应，避免“点 NPC 无任何反馈”。
            // 说明：当前工程已移除磁盘 txt 回落，并且可能存在 npcFileName 与脚本注册名不一致（如是否带 "-"）的问题。
            //      这里提供一个最小兜底对话，便于玩家/GM 发现问题，同时不影响已有脚本逻辑。
            if (!legacyPageMatched)
            {
                try
                {
                    player.NPCSpeech ??= new List<string>();

                    if (player.NPCSpeech.Count == 0)
                    {
                        player.NPCSpeech.Add("该 NPC 暂无可用对话脚本。");

                        // 调试信息仅在开启调度日志时附加，避免向普通玩家暴露过多细节。
                        if (Settings.TxtScriptsLogDispatch)
                        {
                            player.NPCSpeech.Add($"NPCFile={FileName}");
                            player.NPCSpeech.Add($"Key={key}");
                            player.NPCSpeech.Add(scriptsRuntimeActive
                                ? $"C#=on(v{Envir.CSharpScripts.Version}, handlers={Envir.CSharpScripts.LastRegisteredHandlerCount})"
                                : "C#=off");
                        }
                    }

                    player.Enqueue(new S.NPCResponse { Page = player.NPCSpeech });
                }
                catch
                {
                }
            }

            player.NPCData.Remove("NPCInputStr");
        }

        private void Response(PlayerObject player, NPCPage page)
        {
            if (Settings.TxtScriptsLogDispatch && (player.NPCSpeech == null || player.NPCSpeech.Count == 0))
            {
                MessageQueue.Enqueue($"[Scripts][NpcDialog] TXT NPCResponse 为空：Player={player.Name} NPCFile={FileName} Key={page?.Key} ObjectID={player.NPCObjectID}");
            }

            player.Enqueue(new S.NPCResponse { Page = player.NPCSpeech });

            ProcessSpecial(player, page);
        }

        private void ProcessSegment(PlayerObject player, NPCPage page, NPCSegment segment, uint objectID)
        {
            player.NPCObjectID = objectID;
            player.NPCScriptID = ScriptID;
            player.NPCSuccess.Add(segment, segment.Check(player));
            player.NPCPage = page;
        }

        private void ProcessSegment(MonsterObject monster, NPCPage page, NPCSegment segment)
        {
            segment.Check(monster);
        }
        private void ProcessSegment(NPCPage page, NPCSegment segment)
        {
            segment.Check();
        }

        private void ProcessSpecial(PlayerObject player, NPCPage page)
        {
            List<UserItem> allGoods = new List<UserItem>();

            var key = page.Key.ToUpper();

            switch (key)
            {
                case BuyKey:
                case BuySellKey:
                    var sentGoods = new List<UserItem>(Goods);

                    for (int i = 0; i < Goods.Count; i++)
                        player.CheckItem(Goods[i]);

                    if (Settings.GoodsOn)
                    {
                        var callingNPC = NPCObject.Get(player.NPCObjectID);

                        if (callingNPC != null)
                        {
                            for (int i = 0; i < callingNPC.UsedGoods.Count; i++)
                                player.CheckItem(callingNPC.UsedGoods[i]);
                        }

                        sentGoods.AddRange(callingNPC.UsedGoods);
                    }

                    player.SendNPCGoods(sentGoods, GetBuyRate(player), PanelType.Buy, Settings.GoodsHideAddedStats);

                    if (key == BuySellKey)
                    {
                        player.Enqueue(new S.NPCSell { Rate = GetSellRate(player) });
                    }
                    break;
                case BuyNewKey:
                case BuySellNewKey:
                    sentGoods = new List<UserItem>(Goods);

                    for (int i = 0; i < Goods.Count; i++)
                        player.CheckItem(Goods[i]);

                    player.SendNPCGoods(sentGoods, GetBuyRate(player), PanelType.Buy, Settings.GoodsHideAddedStats);

                    if (key == BuySellNewKey)
                    {
                        player.Enqueue(new S.NPCSell { Rate = GetSellRate(player) });
                    }
                    break;
                case SellKey:
                    player.Enqueue(new S.NPCSell { Rate = GetSellRate(player) });
                    break;
                case RepairKey:
                    player.Enqueue(new S.NPCRepair { Rate = PriceRate(player) });
                    break;
                case SRepairKey:
                    player.Enqueue(new S.NPCSRepair { Rate = PriceRate(player) });
                    break;
                case CraftKey:
                    for (int i = 0; i < CraftGoods.Count; i++)
                        player.CheckItemInfo(CraftGoods[i].Item.Info);

                    player.SendNPCGoods((from x in CraftGoods where x.CanCraft(player) select x.Item).ToList(), PriceRate(player), PanelType.Craft);
                    break;
                case RefineKey:
                    if (player.Info.CurrentRefine != null)
                    {
                        player.ReceiveChat("精炼正在进行中...", ChatType.System);
                        player.Enqueue(new S.NPCRefine { Rate = (Settings.RefineCost), Refining = true });
                        break;
                    }
                    else
                        player.Enqueue(new S.NPCRefine { Rate = (Settings.RefineCost), Refining = false });
                    break;
                case RefineCheckKey:
                    player.Enqueue(new S.NPCCheckRefine());
                    break;
                case RefineCollectKey:
                    player.CollectRefine();
                    break;
                case ReplaceWedRingKey:
                    player.Enqueue(new S.NPCReplaceWedRing { Rate = Settings.ReplaceWedRingCost });
                    break;
                case StorageKey:
                    player.SendStorage();
                    player.Enqueue(new S.NPCStorage());
                    break;
                case BuyBackKey:
                    {
                        if (Settings.GoodsOn)
                        {
                            var callingNPC = NPCObject.Get(player.NPCObjectID);

                            if (callingNPC != null)
                            {
                                if (!callingNPC.BuyBack.ContainsKey(player.Name)) callingNPC.BuyBack[player.Name] = new List<UserItem>();

                                for (int i = 0; i < callingNPC.BuyBack[player.Name].Count; i++)
                                {
                                    player.CheckItem(callingNPC.BuyBack[player.Name][i]);
                                }

                                player.SendNPCGoods(callingNPC.BuyBack[player.Name], GetBuyRate(player), PanelType.Buy);
                            }
                        }
                    }
                    break;
                case BuyUsedKey:
                    {
                        if (Settings.GoodsOn)
                        {
                            var callingNPC = NPCObject.Get(player.NPCObjectID);

                            if (callingNPC != null)
                            {
                                for (int i = 0; i < callingNPC.UsedGoods.Count; i++)
                                    player.CheckItem(callingNPC.UsedGoods[i]);

                                player.SendNPCGoods(callingNPC.UsedGoods, GetBuyRate(player), PanelType.BuySub, Settings.GoodsHideAddedStats);
                            }
                        }
                    }
                    break;
                case ConsignKey:
                    player.Enqueue(new S.NPCConsign());
                    break;
                case MarketKey:
                    player.UserMatch = false;
                    player.GetMarket(string.Empty, ItemType.杂物);
                    break;
                case GuildCreateKey:
                    if (player.Info.Level < Settings.Guild_RequiredLevel)
                    {
                        player.ReceiveChat(String.Format("创建行会需要 {0} 级", Settings.Guild_RequiredLevel), ChatType.System);
                    }
                    else if (player.MyGuild == null)
                    {
                        player.CanCreateGuild = true;
                        player.Enqueue(new S.GuildNameRequest());
                    }
                    else
                        player.ReceiveChat("你已经是公会成员", ChatType.System);
                    break;
                case RequestWarKey:
                    if (player.MyGuild != null)
                    {
                        if (player.MyGuildRank != player.MyGuild.Ranks[0])
                        {
                            player.ReceiveChat("必须由会长发起行会战", ChatType.System);
                            return;
                        }
                        player.Enqueue(new S.GuildRequestWar());
                    }
                    else
                    {
                        player.ReceiveChat(GameLanguage.NotInGuild, ChatType.System);
                    }
                    break;
                case SendParcelKey:
                    player.Enqueue(new S.MailSendRequest());
                    break;
                case CollectParcelKey:

                    sbyte result = 0;

                    if (player.GetMailAwaitingCollectionAmount() < 1)
                    {
                        result = -1;
                    }
                    else
                    {
                        foreach (var mail in player.Info.Mail)
                        {
                            if (mail.Parcel) mail.Collected = true;
                        }
                    }
                    player.Enqueue(new S.ParcelCollected { Result = result });
                    player.GetMail();
                    break;
                case AwakeningKey:
                    player.Enqueue(new S.NPCAwakening());
                    break;
                case DisassembleKey:
                    player.Enqueue(new S.NPCDisassemble());
                    break;
                case DowngradeKey:
                    player.Enqueue(new S.NPCDowngrade());
                    break;
                case ResetKey:
                    player.Enqueue(new S.NPCReset());
                    break;
                case PearlBuyKey:
                    for (int i = 0; i < Goods.Count; i++)
                        player.CheckItem(Goods[i]);

                    player.Enqueue(new S.NPCPearlGoods { List = Goods, Rate = GetBuyRate(player, ShopPriceCurrency.Pearl), Type = PanelType.Buy });
                    break;
                case HeroCreateKey:
                    if (player.Info.Level < Settings.Hero_RequiredLevel)
                    {
                        player.ReceiveChat(String.Format("召唤英雄需要角色达到 {0} 级", Settings.Hero_RequiredLevel), ChatType.System);
                        break;
                    }
                    player.CanCreateHero = true;
                    player.Enqueue(new S.HeroCreateRequest()
                    {
                        CanCreateClass = Settings.Hero_CanCreateClass
                    });
                    break;
                case HeroManageKey:
                    player.ManageHeroes();
                    break;
            }
        }

        public void Buy(PlayerObject player, ulong index, ushort count)
        {
            UserItem goods = null;

            for (int i = 0; i < Goods.Count; i++)
            {
                if (Goods[i].UniqueID != index) continue;
                goods = Goods[i];
                break;
            }

            bool isUsed = false;
            bool isBuyBack = false;

            var callingNPC = NPCObject.Get(player.NPCObjectID);

            if (callingNPC != null)
            {
                if (goods == null)
                {
                    for (int i = 0; i < callingNPC.UsedGoods.Count; i++)
                    {
                        if (callingNPC.UsedGoods[i].UniqueID != index) continue;
                        goods = callingNPC.UsedGoods[i];
                        isUsed = true;
                        break;
                    }
                }

                if (goods == null)
                {
                    if (!callingNPC.BuyBack.ContainsKey(player.Name)) callingNPC.BuyBack[player.Name] = new List<UserItem>();
                    for (int i = 0; i < callingNPC.BuyBack[player.Name].Count; i++)
                    {
                        if (callingNPC.BuyBack[player.Name][i].UniqueID != index) continue;
                        goods = callingNPC.BuyBack[player.Name][i];
                        isBuyBack = true;
                        break;
                    }
                }
            }

            if (goods == null || count == 0 || count > goods.Info.StackSize) return;

            if ((isBuyBack || isUsed) && count > goods.Count)
                count = goods.Count;
            else
                goods.Count = count;

            var priceRequest = ResolveShopPrice(
                player,
                ShopPriceOperation.Buy,
                player.NPCPage.Key.ToUpper() == PearlBuyKey ? ShopPriceCurrency.Pearl : ShopPriceCurrency.Gold,
                goods,
                count,
                previewOnly: false,
                isUsedGoods: isUsed,
                isBuyBack: isBuyBack,
                callingNPC);

            if (priceRequest == null) return;

            if (priceRequest.Decision == ScriptHookDecision.Cancel)
            {
                if (!string.IsNullOrEmpty(priceRequest.FailMessage))
                    player.ReceiveChat(priceRequest.FailMessage, ChatType.System);

                return;
            }

            if (priceRequest.Decision == ScriptHookDecision.Handled)
                return;

            uint cost = priceRequest.TotalPrice;

            if (player.NPCPage.Key.ToUpper() == PearlBuyKey)//pearl currency
            {
                if (cost > player.Info.PearlCount) return;
            }
            else if (cost > player.Account.Gold) return;

            UserItem item = (isBuyBack || isUsed) ? goods : Envir.CreateFreshItem(goods.Info);
            item.Count = goods.Count;

            if (!player.CanGainItem(item)) return;

            if (player.NPCPage.Key.ToUpper() == PearlBuyKey)
            {
                player.IntelligentCreatureLosePearls((int)cost);
            }
            else
            {
                player.Account.Gold -= cost;
                player.Enqueue(new S.LoseGold { Gold = cost });

                if (callingNPC != null && callingNPC.Conq != null && priceRequest.DepositTaxToConquest && priceRequest.TaxAmount > 0)
                {
                    callingNPC.Conq.GuildInfo.GoldStorage += priceRequest.TaxAmount;
                }
            }

            player.GainItem(item);

            if (isUsed)
            {
                callingNPC.UsedGoods.Remove(goods); //If used or buyback will destroy whole stack instead of reducing to remaining quantity

                List<UserItem> newGoodsList = new List<UserItem>();
                newGoodsList.AddRange(Goods);
                newGoodsList.AddRange(callingNPC.UsedGoods);

                callingNPC.NeedSave = true;

                player.SendNPCGoods(newGoodsList, GetBuyRate(player), player.NPCPage.Key.ToUpper() == BuyUsedKey ? PanelType.BuySub : PanelType.Buy, Settings.GoodsHideAddedStats);
            }

            if (isBuyBack)
            {
                callingNPC.BuyBack[player.Name].Remove(goods); //If used or buyback will destroy whole stack instead of reducing to remaining quantity
                player.SendNPCGoods(callingNPC.BuyBack[player.Name], GetBuyRate(player), PanelType.Buy);
            }
        }
        public void Sell(PlayerObject player, UserItem item)
        {
            /* Handle Item Sale */
        }
        public void Craft(PlayerObject player, ulong index, ushort count, int[] slots)
        {
            S.CraftItem p = new S.CraftItem();

            RecipeInfo recipe = null;

            for (int i = 0; i < CraftGoods.Count; i++)
            {
                if (CraftGoods[i].Item.UniqueID != index) continue;
                recipe = CraftGoods[i];
                break;
            }

            if (recipe?.Item == null)
            {
                player.Enqueue(p);
                return;
            }

            UserItem goods = recipe.Item;

            if (goods == null || count == 0 || count > goods.Info.StackSize)
            {
                player.Enqueue(p);
                return;
            }

            if (player.Account.Gold < (recipe.Gold * count))
            {
                player.Enqueue(p);
                return;
            }

            bool hasItems = true;

            List<int> usedSlots = new List<int>();

            //Check Tools
            foreach (var tool in recipe.Tools)
            {
                for (int i = 0; i < slots.Length; i++)
                {
                    int slot = slots[i];

                    if (usedSlots.Contains(slot)) continue;

                    if (slot < 0 || slot > player.Info.Inventory.Length) continue;

                    UserItem item = player.Info.Inventory[slot];

                    if (item == null || item.Info != tool.Info) continue;

                    usedSlots.Add(slot);

                    if ((uint)Math.Floor(item.CurrentDura / 1000M) < count)
                    {
                        hasItems = false;
                        break;
                    }
                }

                if (!hasItems)
                {
                    break;
                }
            }

            //Check Ingredients
            foreach (var ingredient in recipe.Ingredients)
            {
                if (ingredient.Count * count > ingredient.Info.StackSize)
                {
                    player.Enqueue(p);
                    return;
                }

                ushort amount = (ushort)(ingredient.Count * count);

                for (int i = 0; i < slots.Length; i++)
                {
                    int slot = slots[i];

                    if (usedSlots.Contains(slot)) continue;

                    if (slot < 0 || slot > player.Info.Inventory.Length) continue;

                    UserItem item = player.Info.Inventory[slot];

                    if (item == null || item.Info != ingredient.Info) continue;

                    usedSlots.Add(slot);

                    if (ingredient.CurrentDura < ingredient.MaxDura && ingredient.CurrentDura > item.CurrentDura)
                    {
                        hasItems = false;
                        break;
                    }

                    if (amount > item.Count)
                    {
                        hasItems = false;
                        break;
                    }

                    amount = 0;
                    break;
                }

                if (amount > 0)
                {
                    hasItems = false;
                    break;
                }
            }

            if (!hasItems || usedSlots.Count != (recipe.Tools.Count + recipe.Ingredients.Count))
            {
                player.Enqueue(p);
                return;
            }

            if (count > (goods.Info.StackSize / goods.Count) || count < 1)
            {
                player.Enqueue(p);
                return;
            }

            UserItem craftedItem = Envir.CreateFreshItem(goods.Info);
            craftedItem.Count = (ushort)(goods.Count * count);

            if (!player.CanGainItem(craftedItem))
            {
                player.Enqueue(p);
                return;
            }

            List<int> usedSlots2 = new List<int>();

            //Use Tool Durability
            foreach (var tool in recipe.Tools)
            {
                for (int i = 0; i < slots.Length; i++)
                {
                    int slot = slots[i];

                    if (usedSlots2.Contains(slot)) continue;

                    if (slot < 0 || slot > player.Info.Inventory.Length) continue;

                    UserItem item = player.Info.Inventory[slot];

                    if (item == null || item.Info != tool.Info) continue;

                    usedSlots2.Add(slot);

                    player.DamageItem(item, (int)(count * 1000), true);

                    break;
                }
            }

            //Take Ingredients
            foreach (var ingredient in recipe.Ingredients)
            {
                ushort amount = (ushort)(ingredient.Count * count);

                for (int i = 0; i < slots.Length; i++)
                {
                    int slot = slots[i];

                    if (usedSlots2.Contains(slot)) continue;

                    if (slot < 0 || slot > player.Info.Inventory.Length) continue;

                    UserItem item = player.Info.Inventory[slot];

                    if (item == null || item.Info != ingredient.Info) continue;

                    usedSlots2.Add(slot);

                    if (item.Count > amount)
                    {
                        player.Enqueue(new S.DeleteItem { UniqueID = item.UniqueID, Count = amount });
                        player.Info.Inventory[slot].Count -= amount;
                        break;
                    }
                    else
                    {
                        player.Enqueue(new S.DeleteItem { UniqueID = item.UniqueID, Count = item.Count });
                        amount -= item.Count;
                        player.Info.Inventory[slot] = null;
                    }

                    break;
                }
            }

            //Take Gold
            player.Account.Gold -= (recipe.Gold * count);
            player.Enqueue(new S.LoseGold { Gold = (recipe.Gold * count) });

            if (Envir.Random.Next(100) >= recipe.Chance + player.Stats[Stat.大师概率数率])
            {
                player.ReceiveChat("制作失败", ChatType.System);
            }
            else
            {
                //Give Item
                player.GainItem(craftedItem);
            }

            p.Success = true;
            player.Enqueue(p);
        }
    }

    public enum NPCScriptType
    {
        Normal,
        Called,
        AutoPlayer,
        AutoMonster,
        Robot
    }
}
