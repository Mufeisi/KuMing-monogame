using Server.MirObjects;
using System.Globalization;

namespace Server.Scripting.ServerSymbols
{
    internal static class PetSymbolAdapter
    {
        private const string Documentation = "翎风服务器常量与整服Envir直接运行实施规格.md";

        internal static ServerSymbolContextKind AppendBindings(
            PlayerObject player,
            ICollection<ServerSymbolBinding> bindings)
        {
            IEnumerable<MonsterObject> playerPets = player.Pets ?? Enumerable.Empty<MonsterObject>();
            IEnumerable<MonsterObject> heroPets = player.Hero?.Pets ?? Enumerable.Empty<MonsterObject>();
            MonsterObject[] activePets = playerPets
                .Concat(heroPets)
                .Where(pet => pet != null && !pet.Dead && pet.Node != null)
                .Distinct()
                .ToArray();

            AddInteger(bindings, "SLAVECOUNT", activePets.Length, ServerSymbolContextKind.Player);
            MonsterObject pet = activePets.FirstOrDefault();
            ServerSymbolContextKind available = ServerSymbolContextKind.None;
            if (pet != null)
            {
                available = ServerSymbolContextKind.Pet;
                AddString(bindings, "SLAVEX", pet.CurrentLocation.X.ToString(CultureInfo.InvariantCulture));
                AddString(bindings, "SLAVEY", pet.CurrentLocation.Y.ToString(CultureInfo.InvariantCulture));
                AddInteger(bindings, "PET.X", pet.CurrentLocation.X);
                AddInteger(bindings, "PET.Y", pet.CurrentLocation.Y);
                AddInteger(bindings, "PET.HP", pet.HP);
                AddInteger(bindings, "PET.MAXHP", pet.Stats?[Stat.HP] ?? 0);

                MapObject target = pet.Target;
                if (target != null && !target.Dead && target.Node != null)
                {
                    ServerSymbolContextKind context = ServerSymbolContextKind.Pet | ServerSymbolContextKind.Target;
                    AddString(bindings, "SLAVETARGETX", target.CurrentLocation.X.ToString(CultureInfo.InvariantCulture), context);
                    AddString(bindings, "SLAVETARGETY", target.CurrentLocation.Y.ToString(CultureInfo.InvariantCulture), context);
                    string fullName = target is MonsterObject monster ? monster.Info?.Name : target.Name;
                    AddString(bindings, "PET.CURTARGETFULLNAME", fullName, context);
                    AddString(bindings, "PET.CURTARGETNAME", RemoveNumericSuffix(target.Name), context);
                    AddInteger(bindings, "PET.CURTARGETHP", target.Health, context);
                    AddInteger(bindings, "PET.CURTARGETMAXHP", target.MaxHealth, context);
                    AddString(bindings, "PET.CURTARGETX", target.CurrentLocation.X.ToString(CultureInfo.InvariantCulture), context);
                    AddString(bindings, "PET.CURTARGETY", target.CurrentLocation.Y.ToString(CultureInfo.InvariantCulture), context);
                    available |= ServerSymbolContextKind.Target;
                }
            }

            if (LingFengTxtTriggerContext.Current?.Payload is LingFengMonsterKillEvent kill &&
                kill.ActorKind == LingFengCombatActorKind.Pet)
            {
                ServerSymbolContextKind context = ServerSymbolContextKind.Pet |
                                                  ServerSymbolContextKind.Monster |
                                                  ServerSymbolContextKind.TriggerResult;
                AddString(bindings, "PET.KILLMONNAME", kill.MonsterName, context);
                available |= ServerSymbolContextKind.Pet | ServerSymbolContextKind.Monster |
                             ServerSymbolContextKind.TriggerResult;
            }

            return available;
        }

        internal static void AppendDefinitions(ICollection<ServerSymbolDefinition> definitions)
        {
            definitions.Add(Definition("SLAVECOUNT", ServerSymbolValueType.Integer, ServerSymbolContextKind.Player));
            AddDefinitions(definitions, ServerSymbolValueType.String, ServerSymbolContextKind.Pet,
                new[] { "SLAVEX", "SLAVEY" });
            AddDefinitions(definitions, ServerSymbolValueType.Integer, ServerSymbolContextKind.Pet,
                new[] { "PET.X", "PET.Y", "PET.HP", "PET.MAXHP" });
            ServerSymbolContextKind target = ServerSymbolContextKind.Pet | ServerSymbolContextKind.Target;
            AddDefinitions(definitions, ServerSymbolValueType.String, target,
                new[] { "SLAVETARGETX", "SLAVETARGETY", "PET.CURTARGETFULLNAME", "PET.CURTARGETNAME", "PET.CURTARGETX", "PET.CURTARGETY" });
            AddDefinitions(definitions, ServerSymbolValueType.Integer, target,
                new[] { "PET.CURTARGETHP", "PET.CURTARGETMAXHP" });
            definitions.Add(Definition("PET.KILLMONNAME", ServerSymbolValueType.String,
                ServerSymbolContextKind.Pet | ServerSymbolContextKind.Monster |
                ServerSymbolContextKind.TriggerResult));
        }

        private static string RemoveNumericSuffix(string name)
        {
            string value = name ?? string.Empty;
            int length = value.Length;
            while (length > 0 && char.IsDigit(value[length - 1])) length--;
            return value.Substring(0, length);
        }

        private static void AddDefinitions(ICollection<ServerSymbolDefinition> target, ServerSymbolValueType type,
            ServerSymbolContextKind context, IEnumerable<string> names)
        {
            foreach (string name in names) target.Add(Definition(name, type, context));
        }

        private static ServerSymbolDefinition Definition(string name, ServerSymbolValueType type,
            ServerSymbolContextKind context) => new(
            name, Array.Empty<string>(), string.Empty, type, context,
            ServerSymbolNoContextBehavior.StructuredFailure,
            ServerSymbolSecurityClassification.Public, ServerSymbolAccessPolicy.Allowed,
            "翎风 P2 宝宝只读显示常量", "PlayerObject.Pets 稳定存活对象快照", "B",
            new[] { "NPC", "命令参数", "系统触发", "ScriptApi" }, "执行时",
            new[] { "LFENV07-P2" }, Documentation, 0, new DateOnly(2026, 8, 16));

        private static void AddString(ICollection<ServerSymbolBinding> bindings, string name, string value,
            ServerSymbolContextKind context = ServerSymbolContextKind.Pet) =>
            bindings.Add(ServerSymbolBinding.Value(context, name,
                ServerSymbolValue.FromString(value ?? string.Empty)));

        private static void AddInteger(ICollection<ServerSymbolBinding> bindings, string name, long value,
            ServerSymbolContextKind context = ServerSymbolContextKind.Pet) =>
            bindings.Add(ServerSymbolBinding.Value(context, name, ServerSymbolValue.FromInteger(value)));
    }
}
