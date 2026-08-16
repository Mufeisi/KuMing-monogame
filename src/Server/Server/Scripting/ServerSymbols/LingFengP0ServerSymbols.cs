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
        private static readonly ServerSymbolCatalog Catalog = CreateCatalog();
        private static readonly IScriptTextRenderer Renderer = new ScriptTextRenderer(new ServerSymbolResolver(Catalog));

        internal static IReadOnlyList<string> CanonicalNames => Catalog.Definitions.Select(x => x.CanonicalName).ToArray();

        internal static ScriptTextRenderResult Render(PlayerObject player, string text)
        {
            if (player == null) throw new ArgumentNullException(nameof(player));
            string source = text ?? string.Empty;
            try
            {
                return Renderer.Render(CreateContext(player), source);
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

        internal static ServerSymbolContext CreateContext(PlayerObject player)
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

            long gold = account?.Gold ?? 0;
            long credit = account?.Credit ?? 0;
            AddInteger(bindings, "GOLDCOUNT", gold);
            AddInteger(bindings, "GAMEGOLD", gold);
            AddCompatibilityInteger(bindings, "GAMEPOINT", 0);
            AddCompatibilityInteger(bindings, "GAMEDIAMOND", 0);
            AddCompatibilityInteger(bindings, "GAMEGIRD", 0);
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

            return new ServerSymbolContext(
                ServerSymbolContextKind.Player | ServerSymbolContextKind.Map |
                ServerSymbolContextKind.Guild | ServerSymbolContextKind.Server,
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

            if (!ServerSymbolCatalog.TryCreate(definitions, out ServerSymbolCatalog catalog, out string diagnostic))
                throw new InvalidOperationException(diagnostic);
            return catalog;
        }

        private static void AddDefinitions(
            ICollection<ServerSymbolDefinition> target,
            ServerSymbolValueType type,
            ServerSymbolContextKind context,
            IEnumerable<string> names)
        {
            foreach (string name in names) target.Add(Definition(name, type, context));
        }

        private static ServerSymbolDefinition Definition(
            string name,
            ServerSymbolValueType type,
            ServerSymbolContextKind context,
            IEnumerable<string> aliases = null) =>
            new ServerSymbolDefinition(
                name, aliases ?? Array.Empty<string>(), string.Empty, type, context,
                ServerSymbolNoContextBehavior.StructuredFailure,
                ServerSymbolSecurityClassification.Public, ServerSymbolAccessPolicy.Allowed,
                "翎风 P0 只读显示常量", "PlayerObject/Envir 只读快照",
                "B", new[] { "NPC", "命令参数", "系统触发", "ScriptApi" }, "执行时",
                new[] { "LFENV05-P0" }, Documentation, 1, new DateOnly(2026, 8, 16));

        private static long StatValue(PlayerObject player, Stat stat) => player.Stats?[stat] ?? 0;

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
