using System.Globalization;
using Server.MirDatabase;
using Server.MirEnvir;
using Server.MirObjects;

namespace Server.Scripting.ServerSymbols
{
    internal static class LingFengP0ServerSymbols
    {
        private const string Documentation = "翎风服务器常量与整服Envir直接运行实施规格.md";
        private static readonly string[] FashionNames =
        {
            "FASHIONDRESS", "FASHIONWEAPON", "FASHIONHELMET", "FASHIONNECKLACE",
            "FASHIONRINGL", "FASHIONRINGR", "FASHIONRING_L", "FASHIONRING_R",
            "FASHIONARMRINGL", "FASHIONARMRINGR", "FASHIONBELT", "FASHIONBOOTS",
            "FASHIONCHARM", "FASHIONRIGHTHAND"
        };
        private static readonly string[] TeamNames =
            Enumerable.Range(0, 10).Select(index => $"TEAM{index}").ToArray();
        private static readonly string[] ScriptParameterNames =
            Enumerable.Range(1, 9).Select(index => $"SCRIPTPARAM{index}").ToArray();
        private static readonly ServerSymbolCatalog Catalog = CreateCatalog();
        private static readonly IScriptTextRenderer Renderer = new ScriptTextRenderer(new ServerSymbolResolver(Catalog));

        internal static IReadOnlyList<string> CanonicalNames => Catalog.Definitions
            .Where(definition => definition.TestIds.Contains("LFENV05-P0", StringComparer.Ordinal))
            .Select(definition => definition.CanonicalName)
            .ToArray();
        internal static IReadOnlyList<string> P1CanonicalNames => Catalog.Definitions
            .Where(definition => definition.TestIds.Contains("LFENV06-P1", StringComparer.Ordinal))
            .Select(definition => definition.CanonicalName)
            .ToArray();
        internal static IReadOnlyList<string> P2CanonicalNames => Catalog.Definitions
            .Where(definition => definition.TestIds.Contains("LFENV07-P2", StringComparer.Ordinal))
            .Select(definition => definition.CanonicalName)
            .ToArray();
        internal static IReadOnlyList<string> P3CanonicalNames => Catalog.Definitions
            .Where(definition => definition.TestIds.Contains("LFENV08-P3", StringComparer.Ordinal))
            .Select(definition => definition.CanonicalName)
            .ToArray();

        internal static ScriptTextRenderResult Render(
            PlayerObject player,
            string text,
            uint? invocationNpcObjectId = null)
        {
            if (player == null) throw new ArgumentNullException(nameof(player));
            string source = text ?? string.Empty;
            try
            {
                return Renderer.Render(CreateContext(player, invocationNpcObjectId), source);
            }
            catch
            {
                return new ScriptTextRenderResult(
                    ScriptTextRenderStatus.CompletedWithDiagnostics,
                    source,
                    0,
                    new[]
                    {
                        new ScriptTextDiagnostic(
                            ScriptTextDiagnosticCode.SymbolResolutionFailed,
                            ServerSymbolStatus.Faulted,
                            string.Empty,
                            0,
                            source.Length,
                            "服务器常量上下文快照失败。")
                    });
            }
        }

        internal static ServerSymbolContext CreateContext(
            PlayerObject player,
            uint? invocationNpcObjectId = null)
        {
            if (player == null) throw new ArgumentNullException(nameof(player));

            DateTime now = Envir.Main.Now;
            CharacterInfo info = player.Info;
            AccountInfo account = player.Account;
            Map map = player.CurrentMap;
            var bindings = new List<ServerSymbolBinding>();

            AddString(bindings, "USERNAME", player.Name);
            AddString(bindings, "USERALLNAME", player.Name);
            AddInteger(bindings, "LEVEL", player.Level);
            AddString(bindings, "JOB", player.Class.ToString());
            AddString(bindings, "GENDER", player.Gender.ToString());
            AddInteger(bindings, "HP", player.HP);
            AddInteger(bindings, "MAXHP", StatValue(player, Stat.HP));
            AddInteger(bindings, "MP", player.MP);
            AddInteger(bindings, "MAXMP", StatValue(player, Stat.MP));
            AddInteger(bindings, "EXP", player.Experience);
            AddInteger(bindings, "MAXEXP", player.MaxExperience);
            AddInteger(bindings, "PKPOINT", player.PKPoints);

            AddInteger(bindings, "AC", StatValue(player, Stat.MinAC));
            AddInteger(bindings, "MAXAC", StatValue(player, Stat.MaxAC));
            AddInteger(bindings, "MAC", StatValue(player, Stat.MinMAC));
            AddInteger(bindings, "MAXMAC", StatValue(player, Stat.MaxMAC));
            AddInteger(bindings, "DC", StatValue(player, Stat.MinDC));
            AddInteger(bindings, "MAXDC", StatValue(player, Stat.MaxDC));
            AddInteger(bindings, "MC", StatValue(player, Stat.MinMC));
            AddInteger(bindings, "MAXMC", StatValue(player, Stat.MaxMC));
            AddInteger(bindings, "SC", StatValue(player, Stat.MinSC));
            AddInteger(bindings, "MAXSC", StatValue(player, Stat.MaxSC));
            AddInteger(bindings, "HIT", StatValue(player, Stat.Accuracy));
            AddInteger(bindings, "SPD", StatValue(player, Stat.Agility));
            AddInteger(bindings, "LUCK", StatValue(player, Stat.Luck));

            AddString(bindings, "MAP", map?.Info?.FileName);
            AddString(bindings, "MAPTITLE", map?.Info?.Title);
            AddInteger(bindings, "X", player.CurrentLocation.X);
            AddInteger(bindings, "Y", player.CurrentLocation.Y);
            AddString(bindings, "FBMAP", map?.Info?.FileName);
            AddString(bindings, "FBMAPNAME", map?.Info?.Title);

            AddString(bindings, "GUILDNAME", player.MyGuild?.Name ?? "未入行会");
            AddString(bindings, "RANKNAME", player.MyGuildRank?.Name);
            AddInteger(bindings, "GUILDMEMBERCOUNT", player.MyGuild?.Ranks?.Sum(rank => rank.Members?.Count ?? 0) ?? 0);
            if (player.GroupMembers != null)
            {
                int teamCount = Math.Min(TeamNames.Length, player.GroupMembers.Count);
                for (int index = 0; index < teamCount; index++)
                    AddString(bindings, TeamNames[index], player.GroupMembers[index]?.Name);
            }
            AddInteger(bindings, "GROUPMEMBERCOUNT", player.GroupMembers?.Count ?? 0);
            AddInteger(bindings, "RECALLREMAININGTIME", RecallRemainingSeconds(player));
            AddInteger(bindings, "NPCREBORNCOUNT", player.GetLingFengNpcRebornRemaining());
            AddInteger(bindings, "TRIGGERNPCREBORNCOUNT", player.GetLingFengNpcRebornTriggered());
            AddInteger(bindings, "KILLMONEXPRATE", EffectiveWholeMultiplier(player, Stat.经验增长数率));
            AddInteger(bindings, "KILLMONBURSTRATE",
                Math.Max(0, player.GetLingFengDropRatePercent()) / 100);
            AddInteger(bindings, "KILLMONEXPRATETIME", Math.Max(
                BuffRemainingSeconds(player, BuffType.获取经验提升),
                player.GetLingFengExperienceRateRemainingSeconds()));
            AddInteger(bindings, "KILLMONBURSTRATETIME",
                player.GetLingFengDropRateRemainingSeconds());
            AddInteger(bindings, "POWERRATE",
                Math.Max(0, player.GetLingFengPowerRatePercent(targetIsHuman: true)) / 100);
            AddInteger(bindings, "POWERRATETIME",
                player.GetLingFengPowerRateRemainingSeconds(targetIsHuman: true));
            AddInteger(bindings, "ATTACKMONPOWERRATE",
                Math.Max(0, player.GetLingFengPowerRatePercent(targetIsHuman: false)) / 100);
            AddInteger(bindings, "ATTACKMONPOWERRATETIME",
                player.GetLingFengPowerRateRemainingSeconds(targetIsHuman: false));
            IReadOnlyList<string> scriptParameters = LingFengTxtTriggerContext.Current?.ScriptParameters;
            if (scriptParameters != null)
            {
                int parameterCount = Math.Min(ScriptParameterNames.Length, scriptParameters.Count);
                ServerSymbolContextKind parameterContext =
                    ServerSymbolContextKind.Player | ServerSymbolContextKind.TriggerResult;
                for (int index = 0; index < parameterCount; index++)
                    AddString(bindings, ScriptParameterNames[index], scriptParameters[index], parameterContext);
            }

            long gold = account?.Gold ?? 0;
            long credit = account?.Credit ?? 0;
            AddInteger(bindings, "GOLDCOUNT", gold);
            AddInteger(bindings, "GAMEGOLD", gold);
            AddInteger(bindings, "GAMEPOINT", info?.LingFengProgress.GamePoint ?? 0);
            AddInteger(bindings, "GAMEDIAMOND", info?.LingFengProgress.GameDiamond ?? 0);
            AddInteger(bindings, "GAMEGIRD", info?.LingFengProgress.GameGird ?? 0);
            AddCompatibilityInteger(bindings, "JADE", 0);
            AddCompatibilityInteger(bindings, "GAMEGLORY", 0);
            AddInteger(bindings, "CREDITPOINT", credit);

            AddString(bindings, "DATE", now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), ServerSymbolContextKind.Server);
            AddString(bindings, "TIME", now.ToString("HH:mm:ss", CultureInfo.InvariantCulture), ServerSymbolContextKind.Server);
            AddString(bindings, "DATETIME", now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture), ServerSymbolContextKind.Server);
            AddInteger(bindings, "YEAR", now.Year, ServerSymbolContextKind.Server);
            AddInteger(bindings, "MONTH", now.Month, ServerSymbolContextKind.Server);
            AddInteger(bindings, "DAY", now.Day, ServerSymbolContextKind.Server);
            AddInteger(bindings, "HOUR", now.Hour, ServerSymbolContextKind.Server);
            AddInteger(bindings, "MINUTE", now.Minute, ServerSymbolContextKind.Server);
            AddInteger(bindings, "SECOND", now.Second, ServerSymbolContextKind.Server);
            AddString(bindings, "SERVERNAME", GameLanguage.GameName, ServerSymbolContextKind.Server);
            AddInteger(bindings, "USERCOUNT", Envir.Main.PlayerCount, ServerSymbolContextKind.Server);
            AddInteger(bindings, "ONUSERCOUNT", Envir.Main.PlayerCount, ServerSymbolContextKind.Server);
            AddCompatibilityInteger(bindings, "DUMMYCOUNT", 0, ServerSymbolContextKind.Server);

            AddEquipment(bindings, info, "DRESS", EquipmentSlot.Armour);
            AddEquipment(bindings, info, "WEAPON", EquipmentSlot.Weapon);
            AddEquipment(bindings, info, "HELMET", EquipmentSlot.Helmet);
            AddEquipment(bindings, info, "NECKLACE", EquipmentSlot.Necklace);
            AddEquipment(bindings, info, "RING_L", EquipmentSlot.RingL);
            AddEquipment(bindings, info, "RING_R", EquipmentSlot.RingR);
            AddEquipment(bindings, info, "ARMRING_L", EquipmentSlot.BraceletL);
            AddEquipment(bindings, info, "ARMRING_R", EquipmentSlot.BraceletR);
            AddEquipment(bindings, info, "BELT", EquipmentSlot.Belt);
            AddEquipment(bindings, info, "BOOTS", EquipmentSlot.Boots);
            AddEquipment(bindings, info, "BUJUK", EquipmentSlot.Amulet);
            AddEquipment(bindings, info, "CHARM", EquipmentSlot.Stone);
            AddCompatibilityString(bindings, "SHIELD", "空");
            foreach (string fashion in FashionNames) AddCompatibilityString(bindings, fashion, "空");

            ServerSymbolContextKind availableContexts =
                ServerSymbolContextKind.Player | ServerSymbolContextKind.Map |
                ServerSymbolContextKind.Guild | ServerSymbolContextKind.Server;
            if (scriptParameters != null)
                availableContexts |= ServerSymbolContextKind.TriggerResult;
            if (LingFengTxtTriggerContext.Current?.Payload is LingFengMonsterKillEvent killEvent)
            {
                ServerSymbolContextKind eventContext =
                    ServerSymbolContextKind.Monster | ServerSymbolContextKind.TriggerResult;
                AddString(bindings, "KILLMONNAME", killEvent.MonsterName, eventContext);
                AddInteger(bindings, "KILLMONX", killEvent.X, eventContext);
                AddInteger(bindings, "KILLMONY", killEvent.Y, eventContext);
                AddInteger(bindings, "GETEXP", killEvent.Experience, eventContext);
                availableContexts |= eventContext;
            }
            else if (LingFengTxtTriggerContext.Current?.Payload is LingFengItemTriggerEvent itemEvent)
            {
                ServerSymbolContextKind eventContext =
                    ServerSymbolContextKind.Item | ServerSymbolContextKind.TriggerResult;
                AddString(bindings, "CURITEMNAME", itemEvent.ItemName, eventContext);
                if (itemEvent.Kind is LingFengItemTriggerKind.Pickup or LingFengItemTriggerKind.Drop)
                    AddString(bindings, "PICKDROPITEMNAME", itemEvent.ItemName, eventContext);
                else
                    AddString(bindings, "USEITEMNAME", itemEvent.ItemName, eventContext);
                if (itemEvent.Position.HasValue)
                    AddInteger(bindings, "CURITEMPOS", itemEvent.Position.Value, eventContext);
                availableContexts |= eventContext;
            }
            else if (LingFengTxtTriggerContext.Current?.Payload is LingFengDamageEvent damageEvent)
            {
                ServerSymbolContextKind eventContext =
                    ServerSymbolContextKind.Attacker | ServerSymbolContextKind.Target |
                    ServerSymbolContextKind.TriggerResult;
                AddString(bindings, "KILLER", damageEvent.AttackerName, eventContext);
                AddString(bindings, "CURRRTARGETNAME", damageEvent.CurrentTargetName, eventContext);
                AddInteger(bindings, "DAMAGEVALUE", damageEvent.DamageValue, eventContext);
                AddString(bindings, "CURRRUSEMAGICID", damageEvent.MagicId, eventContext);
                if (damageEvent.Perspective == PlayerDamagePerspective.Outgoing)
                    AddInteger(bindings, "PKPOWER", damageEvent.AppliedDamage, eventContext);
                else
                    AddInteger(bindings, "STRUCKHP", damageEvent.AppliedDamage, eventContext);
                if (damageEvent.Perspective == PlayerDamagePerspective.Outgoing && damageEvent.TargetIsMonster)
                {
                    ServerSymbolContextKind monsterTargetContext =
                        ServerSymbolContextKind.Monster | ServerSymbolContextKind.Target |
                        ServerSymbolContextKind.TriggerResult;
                    AddString(bindings, "ATTACKMONSTER_NAME", damageEvent.TargetName, monsterTargetContext);
                    AddString(bindings, "ATTACKMONSTER_NAMEEX", damageEvent.CurrentTargetName, monsterTargetContext);
                    AddString(bindings, "ATTACKMONSTER_X", damageEvent.TargetX.ToString(CultureInfo.InvariantCulture), monsterTargetContext);
                    AddString(bindings, "ATTACKMONSTER_XEX", damageEvent.TargetX.ToString(CultureInfo.InvariantCulture), monsterTargetContext);
                    AddString(bindings, "ATTACKMONSTER_Y", damageEvent.TargetY.ToString(CultureInfo.InvariantCulture), monsterTargetContext);
                    AddString(bindings, "ATTACKMONSTER_YEX", damageEvent.TargetY.ToString(CultureInfo.InvariantCulture), monsterTargetContext);
                    AddInteger(bindings, "ATTACKMONSTER_HP", damageEvent.TargetHp, monsterTargetContext);
                    AddString(bindings, "ATTACKMONSTER_HPEX", damageEvent.TargetHp.ToString(CultureInfo.InvariantCulture), monsterTargetContext);
                    AddInteger(bindings, "ATTACKMONSTER_MAXHP", damageEvent.TargetMaxHp, monsterTargetContext);
                    AddInteger(bindings, "ATTACKMONSTER_MAXHPEX", damageEvent.TargetMaxHp, monsterTargetContext);
                    availableContexts |= ServerSymbolContextKind.Monster;
                }
                availableContexts |= eventContext;
            }

            availableContexts |= HeroSymbolAdapter.AppendBindings(player, bindings);
            availableContexts |= PetSymbolAdapter.AppendBindings(player, bindings);
            availableContexts |= GuildConquestSymbolAdapter.AppendBindings(
                player, bindings, invocationNpcObjectId);

            return new ServerSymbolContext(
                availableContexts,
                bindings.ToArray());
        }

        private static ServerSymbolCatalog CreateCatalog()
        {
            var definitions = new List<ServerSymbolDefinition>();
            AddDefinitions(definitions, ServerSymbolValueType.String, ServerSymbolContextKind.Player,
                new[] { "USERNAME", "USERALLNAME", "GENDER", "FBMAP", "FBMAPNAME", "GUILDNAME", "RANKNAME" });
            definitions.Add(Definition("JOB", ServerSymbolValueType.String, ServerSymbolContextKind.Player, new[] { "CLASS" }));
            definitions.Add(Definition("MAP", ServerSymbolValueType.String, ServerSymbolContextKind.Player));
            definitions.Add(Definition("MAPTITLE", ServerSymbolValueType.String, ServerSymbolContextKind.Player, new[] { "MAPNAME" }));

            AddDefinitions(definitions, ServerSymbolValueType.Integer, ServerSymbolContextKind.Player,
                new[] { "LEVEL", "HP", "MAXHP", "MP", "MAXMP", "EXP", "MAXEXP", "PKPOINT",
                    "AC", "MAXAC", "MAC", "MAXMAC", "DC", "MAXDC", "MC", "MAXMC", "SC", "MAXSC", "HIT", "SPD", "LUCK",
                    "GUILDMEMBERCOUNT", "GAMEGOLD", "GAMEPOINT", "GAMEDIAMOND", "GAMEGIRD", "JADE", "GAMEGLORY" });
            definitions.Add(Definition("X", ServerSymbolValueType.Integer, ServerSymbolContextKind.Player, new[] { "X_COORD" }));
            definitions.Add(Definition("Y", ServerSymbolValueType.Integer, ServerSymbolContextKind.Player, new[] { "Y_COORD" }));
            definitions.Add(Definition("GOLDCOUNT", ServerSymbolValueType.Integer, ServerSymbolContextKind.Player, new[] { "GOLD" }));
            definitions.Add(Definition("CREDITPOINT", ServerSymbolValueType.Integer, ServerSymbolContextKind.Player, new[] { "CREDIT" }));
            AddDefinitions(definitions, ServerSymbolValueType.String, ServerSymbolContextKind.Player,
                TeamNames, "LFENV06-P1");
            AddDefinitions(definitions, ServerSymbolValueType.Integer, ServerSymbolContextKind.Player,
                new[] { "GROUPMEMBERCOUNT", "RECALLREMAININGTIME", "KILLMONEXPRATE", "KILLMONBURSTRATE",
                    "KILLMONEXPRATETIME", "KILLMONBURSTRATETIME", "POWERRATE", "POWERRATETIME",
                    "ATTACKMONPOWERRATE", "ATTACKMONPOWERRATETIME", "NPCREBORNCOUNT",
                    "TRIGGERNPCREBORNCOUNT" }, "LFENV06-P1");
            AddDefinitions(definitions, ServerSymbolValueType.String,
                ServerSymbolContextKind.Player | ServerSymbolContextKind.TriggerResult,
                ScriptParameterNames, "LFENV06-P1");

            AddDefinitions(definitions, ServerSymbolValueType.String, ServerSymbolContextKind.Server,
                new[] { "DATE", "TIME", "DATETIME", "SERVERNAME" });
            AddDefinitions(definitions, ServerSymbolValueType.Integer, ServerSymbolContextKind.Server,
                new[] { "YEAR", "MONTH", "DAY", "HOUR", "MINUTE", "SECOND", "USERCOUNT", "ONUSERCOUNT", "DUMMYCOUNT" });

            AddDefinitions(definitions, ServerSymbolValueType.String, ServerSymbolContextKind.Player,
                new[] { "WEAPON", "HELMET", "NECKLACE", "RING_L", "RING_R", "BELT", "BOOTS", "SHIELD" }.Concat(FashionNames));
            definitions.Add(Definition("ARMRING_L", ServerSymbolValueType.String, ServerSymbolContextKind.Player, new[] { "BRACELET_L" }));
            definitions.Add(Definition("ARMRING_R", ServerSymbolValueType.String, ServerSymbolContextKind.Player, new[] { "BRACELET_R" }));
            definitions.Add(Definition("BUJUK", ServerSymbolValueType.String, ServerSymbolContextKind.Player, new[] { "AMULET" }));
            definitions.Add(Definition("CHARM", ServerSymbolValueType.String, ServerSymbolContextKind.Player, new[] { "STONE" }));
            definitions.Add(Definition("DRESS", ServerSymbolValueType.String, ServerSymbolContextKind.Player, new[] { "ARMOUR" }));

            ServerSymbolContextKind killContext =
                ServerSymbolContextKind.Monster | ServerSymbolContextKind.TriggerResult;
            definitions.Add(Definition("KILLMONNAME", ServerSymbolValueType.String, killContext,
                new[] { "KILLMONNAMEEX" }, "LFENV06-P1"));
            AddDefinitions(definitions, ServerSymbolValueType.Integer, killContext,
                new[] { "KILLMONX", "KILLMONY", "GETEXP" }, "LFENV06-P1");

            ServerSymbolContextKind itemContext =
                ServerSymbolContextKind.Item | ServerSymbolContextKind.TriggerResult;
            AddDefinitions(definitions, ServerSymbolValueType.String, itemContext,
                new[] { "PICKDROPITEMNAME", "CURITEMNAME", "USEITEMNAME" }, "LFENV06-P1");
            definitions.Add(Definition("CURITEMPOS", ServerSymbolValueType.Integer, itemContext,
                testId: "LFENV06-P1"));

            ServerSymbolContextKind damageContext =
                ServerSymbolContextKind.Attacker | ServerSymbolContextKind.Target |
                ServerSymbolContextKind.TriggerResult;
            definitions.Add(Definition("KILLER", ServerSymbolValueType.String, damageContext,
                testId: "LFENV06-P1"));
            definitions.Add(Definition("CURRRTARGETNAME", ServerSymbolValueType.String, damageContext,
                new[] { "CURRRTARGETFULLNAME" }, "LFENV06-P1"));
            AddDefinitions(definitions, ServerSymbolValueType.Integer, damageContext,
                new[] { "DAMAGEVALUE", "PKPOWER", "STRUCKHP" }, "LFENV06-P1");
            definitions.Add(Definition("CURRRUSEMAGICID", ServerSymbolValueType.String,
                damageContext,
                testId: "LFENV06-P1"));

            ServerSymbolContextKind monsterTargetContext =
                ServerSymbolContextKind.Monster | ServerSymbolContextKind.Target |
                ServerSymbolContextKind.TriggerResult;
            AddDefinitions(definitions, ServerSymbolValueType.String, monsterTargetContext,
                new[] { "ATTACKMONSTER_NAME", "ATTACKMONSTER_NAMEEX", "ATTACKMONSTER_X", "ATTACKMONSTER_XEX",
                    "ATTACKMONSTER_Y", "ATTACKMONSTER_YEX", "ATTACKMONSTER_HPEX" }, "LFENV06-P1");
            AddDefinitions(definitions, ServerSymbolValueType.Integer, monsterTargetContext,
                new[] { "ATTACKMONSTER_HP", "ATTACKMONSTER_MAXHP", "ATTACKMONSTER_MAXHPEX" }, "LFENV06-P1");

            HeroSymbolAdapter.AppendDefinitions(definitions);
            PetSymbolAdapter.AppendDefinitions(definitions);
            GuildConquestSymbolAdapter.AppendDefinitions(definitions);

            if (!ServerSymbolCatalog.TryCreate(definitions, out ServerSymbolCatalog catalog, out string diagnostic))
                throw new InvalidOperationException(diagnostic);
            return catalog;
        }

        private static void AddDefinitions(
            ICollection<ServerSymbolDefinition> target,
            ServerSymbolValueType type,
            ServerSymbolContextKind context,
            IEnumerable<string> names,
            string testId = "LFENV05-P0")
        {
            foreach (string name in names) target.Add(Definition(name, type, context, testId: testId));
        }

        private static ServerSymbolDefinition Definition(
            string name,
            ServerSymbolValueType type,
            ServerSymbolContextKind context,
            IEnumerable<string> aliases = null,
            string testId = "LFENV05-P0") =>
            new ServerSymbolDefinition(
                name, aliases ?? Array.Empty<string>(), string.Empty, type, context,
                ServerSymbolNoContextBehavior.StructuredFailure,
                ServerSymbolSecurityClassification.Public, ServerSymbolAccessPolicy.Allowed,
                "翎风 P0 只读显示常量", "PlayerObject/Envir 只读快照",
                "B", new[] { "NPC", "命令参数", "系统触发", "ScriptApi" }, "执行时",
                new[] { testId }, Documentation, 1, new DateOnly(2026, 8, 16));

        private static long StatValue(PlayerObject player, Stat stat) => player.Stats?[stat] ?? 0;

        private static long EffectiveWholeMultiplier(PlayerObject player, Stat stat) =>
            1 + Math.Max(0, StatValue(player, stat)) / 100;

        private static long BuffRemainingSeconds(PlayerObject player, BuffType type)
        {
            long remaining = player.Buffs?
                .Where(buff => buff != null && !buff.FlagForRemoval && buff.Info?.Type == type)
                .Select(buff => Math.Max(0, buff.ExpireTime))
                .DefaultIfEmpty(0)
                .Max() ?? 0;
            return (remaining + 999) / 1000;
        }

        private static long RecallRemainingSeconds(PlayerObject player)
        {
            long now = Envir.Main.Time;
            long remaining = player.ActionList?
                .Where(action => action != null && !action.FlaggedToRemove &&
                                 action.Type == DelayedType.NPC && action.Params?.Length == 5 && action.Time > now)
                .Select(action => action.Time - now)
                .DefaultIfEmpty(0)
                .Min() ?? 0;
            return (remaining + 999) / 1000;
        }

        private static void AddEquipment(
            ICollection<ServerSymbolBinding> bindings,
            CharacterInfo info,
            string name,
            EquipmentSlot slot)
        {
            int index = (int)slot;
            string value = info?.Equipment != null && index < info.Equipment.Length && info.Equipment[index] != null
                ? info.Equipment[index].FriendlyName
                : "空";
            AddString(bindings, name, value);
        }

        private static void AddString(
            ICollection<ServerSymbolBinding> bindings,
            string name,
            string value,
            ServerSymbolContextKind context = ServerSymbolContextKind.Player) =>
            bindings.Add(ServerSymbolBinding.Value(context, name, ServerSymbolValue.FromString(value ?? string.Empty)));

        private static void AddInteger(
            ICollection<ServerSymbolBinding> bindings,
            string name,
            long value,
            ServerSymbolContextKind context = ServerSymbolContextKind.Player) =>
            bindings.Add(ServerSymbolBinding.Value(context, name, ServerSymbolValue.FromInteger(value)));

        private static void AddCompatibilityString(
            ICollection<ServerSymbolBinding> bindings,
            string name,
            string value,
            ServerSymbolContextKind context = ServerSymbolContextKind.Player) =>
            bindings.Add(ServerSymbolBinding.CompatibilityValue(
                context,
                name,
                ServerSymbolValue.FromString(value ?? string.Empty),
                "当前 LyoCrystal 数据模型没有该独立字段，使用只读兼容显示值。"));

        private static void AddCompatibilityInteger(
            ICollection<ServerSymbolBinding> bindings,
            string name,
            long value,
            ServerSymbolContextKind context = ServerSymbolContextKind.Player) =>
            bindings.Add(ServerSymbolBinding.CompatibilityValue(
                context,
                name,
                ServerSymbolValue.FromInteger(value),
                "当前 LyoCrystal 数据模型没有该独立字段，使用只读兼容显示值。"));
    }
}
