using System.Globalization;
using Server.MirEnvir;
using Server.MirObjects;

namespace Server.Scripting.ServerSymbols
{
    internal static class GuildConquestSymbolAdapter
    {
        private const string Documentation = "翎风服务器常量与整服Envir直接运行实施规格.md";

        internal static ServerSymbolContextKind AppendBindings(
            PlayerObject player,
            ICollection<ServerSymbolBinding> bindings,
            uint? invocationNpcObjectId = null)
        {
            GuildObject guild = player.MyGuild;
            if (guild != null)
            {
                string[] masters = guild.Ranks?
                    .Where(rank => rank != null)
                    .OrderBy(rank => rank.Index)
                    .FirstOrDefault(rank => rank.Index == 0)?
                    .Members?
                    .Select(member => member?.Name)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Take(2)
                    .ToArray() ?? Array.Empty<string>();
                AddString(bindings, "GUILDMASTER1", masters.ElementAtOrDefault(0) ?? string.Empty,
                    ServerSymbolContextKind.Guild);
                AddString(bindings, "GUILDMASTER2", masters.ElementAtOrDefault(1) ?? string.Empty,
                    ServerSymbolContextKind.Guild);

            }

            ConquestObject invocationConquest = invocationNpcObjectId.HasValue
                ? NPCObject.Get(invocationNpcObjectId.Value)?.Conq
                : NPCObject.Get(player.NPCObjectID)?.Conq;
            ConquestObject conquest = invocationConquest ??
                                      guild?.Conquest ??
                                      Envir.Main.Conquests?
                                          .Where(item => item?.Info != null)
                                          .OrderBy(item => item.Info.Index)
                                          .FirstOrDefault();
            if (conquest?.Info != null)
            {
                AddString(bindings, "CASTLENAME", conquest.Info.Name, ServerSymbolContextKind.Server);
                AddString(bindings, "OWNERGUILD", conquest.Guild?.Name ?? string.Empty,
                    ServerSymbolContextKind.Server);
                AddInteger(bindings, "CASTLEGOLD", conquest.GuildInfo?.GoldStorage ?? 0,
                    ServerSymbolContextKind.Server);
                if (conquest.WarStartTime != default)
                {
                    AddCompatibilityString(bindings, "CASTLEWARDATE",
                        conquest.WarStartTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                        ServerSymbolContextKind.Server,
                        "当前模型只保留本进程最近一次战争开始时间，未持久化下一次申请日期或上次占领日期。");
                }
            }

            AddInteger(bindings, "GUILDWARFEE", Settings.Guild_WarCost, ServerSymbolContextKind.Server);
            AddString(bindings, "REQUESTBUILDGUILDITEM", FormatCreationCosts(), ServerSymbolContextKind.Server);
            AddCompatibilityString(bindings, "LISTOFWAR", FormatConquestApplications(),
                ServerSymbolContextKind.Server,
                "当前模型可列出已申请或进行中的城堡名称，但没有翎风原生列表排版与逐次申请日期。");

            return guild == null ? ServerSymbolContextKind.Server :
                ServerSymbolContextKind.Guild | ServerSymbolContextKind.Server;
        }

        internal static void AppendDefinitions(ICollection<ServerSymbolDefinition> definitions)
        {
            AddDefinitions(definitions, ServerSymbolValueType.String, ServerSymbolContextKind.Guild,
                new[] { "GUILDMASTER1", "GUILDMASTER2" });
            AddDefinitions(definitions, ServerSymbolValueType.String, ServerSymbolContextKind.Server,
                new[] { "CASTLENAME", "OWNERGUILD" });
            AddDefinitions(definitions, ServerSymbolValueType.String, ServerSymbolContextKind.Server,
                new[] { "CASTLEWARDATE" }, "C");
            AddDefinitions(definitions, ServerSymbolValueType.Integer, ServerSymbolContextKind.Server,
                new[] { "CASTLEGOLD" });
            AddDefinitions(definitions, ServerSymbolValueType.String, ServerSymbolContextKind.Server,
                new[] { "REQUESTBUILDGUILDITEM" });
            AddDefinitions(definitions, ServerSymbolValueType.String, ServerSymbolContextKind.Server,
                new[] { "LISTOFWAR" }, "C");
            AddDefinitions(definitions, ServerSymbolValueType.Integer, ServerSymbolContextKind.Server,
                new[] { "GUILDWARFEE" });
        }

        private static string FormatCreationCosts()
        {
            string[] costs = Settings.Guild_CreationCostList?
                .Where(cost => cost != null && !string.IsNullOrWhiteSpace(cost.ItemName) && cost.Amount > 0)
                .Select(cost => $"{cost.ItemName}*{cost.Amount.ToString(CultureInfo.InvariantCulture)}")
                .ToArray() ?? Array.Empty<string>();
            return costs.Length == 0 ? "无" : string.Join(",", costs);
        }

        private static string FormatConquestApplications()
        {
            string[] names = Envir.Main.Conquests?
                .Where(conquest => conquest?.Info != null &&
                                   (conquest.WarIsOn || (conquest.GuildInfo?.AttackerID ?? 0) > 0))
                .OrderBy(conquest => conquest.Info.Index)
                .Select(conquest => conquest.Info.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToArray() ?? Array.Empty<string>();
            return string.Join(",", names);
        }

        private static void AddDefinitions(
            ICollection<ServerSymbolDefinition> target,
            ServerSymbolValueType type,
            ServerSymbolContextKind context,
            IEnumerable<string> names,
            string compatibilityStatus = "B")
        {
            foreach (string name in names)
            {
                target.Add(new ServerSymbolDefinition(
                    name, Array.Empty<string>(), string.Empty, type, context,
                    ServerSymbolNoContextBehavior.StructuredFailure,
                    ServerSymbolSecurityClassification.Public, ServerSymbolAccessPolicy.Allowed,
                    "翎风 P3 行会攻城只读显示常量", "GuildObject/ConquestObject/公开服务器配置只读快照",
                    compatibilityStatus,
                    new[] { "NPC", "命令参数", "系统触发", "ScriptApi" }, "执行时",
                    new[] { "LFENV08-P3" }, Documentation, 0, new DateOnly(2026, 8, 16)));
            }
        }

        private static void AddString(
            ICollection<ServerSymbolBinding> bindings,
            string name,
            string value,
            ServerSymbolContextKind context) =>
            bindings.Add(ServerSymbolBinding.Value(
                context, name, ServerSymbolValue.FromString(value ?? string.Empty)));

        private static void AddInteger(
            ICollection<ServerSymbolBinding> bindings,
            string name,
            long value,
            ServerSymbolContextKind context) =>
            bindings.Add(ServerSymbolBinding.Value(context, name, ServerSymbolValue.FromInteger(value)));

        private static void AddCompatibilityString(
            ICollection<ServerSymbolBinding> bindings,
            string name,
            string value,
            ServerSymbolContextKind context,
            string diagnostic) =>
            bindings.Add(ServerSymbolBinding.CompatibilityValue(
                context, name, ServerSymbolValue.FromString(value ?? string.Empty), diagnostic));
    }
}
