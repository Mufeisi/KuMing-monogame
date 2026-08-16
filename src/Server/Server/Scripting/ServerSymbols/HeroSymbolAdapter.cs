using Server.MirDatabase;
using Server.MirObjects;
using System.Globalization;

namespace Server.Scripting.ServerSymbols
{
    internal static class HeroSymbolAdapter
    {
        private const string Documentation = "翎风服务器常量与整服Envir直接运行实施规格.md";

        internal static ServerSymbolContextKind AppendBindings(
            PlayerObject player,
            ICollection<ServerSymbolBinding> bindings)
        {
            HeroInfo info = player.CurrentHero;
            if (info == null) return ServerSymbolContextKind.None;

            ServerSymbolContextKind available = ServerSymbolContextKind.Hero;
            AddString(bindings, "HERONAME", info.Name);
            AddString(bindings, "H.USERNAME", info.Name);
            AddInteger(bindings, "H.LEVEL", info.Level);
            AddInteger(bindings, "H.EXP", info.Experience);
            AddString(bindings, "H.JOB", info.Class.ToString());
            AddString(bindings, "H.GENDER", info.Gender.ToString());
            AddEquipment(bindings, info, "H.RIGHTHAND", EquipmentSlot.Weapon);
            AddEquipment(bindings, info, "H.WEAPON", EquipmentSlot.Weapon);
            AddEquipment(bindings, info, "H.HELMET", EquipmentSlot.Helmet);
            AddEquipment(bindings, info, "H.NECKLACE", EquipmentSlot.Necklace);
            AddEquipment(bindings, info, "H.RING_L", EquipmentSlot.RingL);
            AddEquipment(bindings, info, "H.RING_R", EquipmentSlot.RingR);
            AddEquipment(bindings, info, "H.ARMRING_L", EquipmentSlot.BraceletL);
            AddEquipment(bindings, info, "H.ARMRING_R", EquipmentSlot.BraceletR);
            AddEquipment(bindings, info, "H.BELT", EquipmentSlot.Belt);
            AddEquipment(bindings, info, "H.BOOTS", EquipmentSlot.Boots);
            AddEquipment(bindings, info, "H.BUJUK", EquipmentSlot.Amulet);

            HeroObject hero = player.Hero;
            if (hero != null)
            {
                AddInteger(bindings, "H.HP", hero.HP);
                AddInteger(bindings, "H.MAXHP", StatValue(hero, Stat.HP));
                AddInteger(bindings, "H.MP", hero.MP);
                AddInteger(bindings, "H.MAXMP", StatValue(hero, Stat.MP));
                AddInteger(bindings, "H.PKPOINT", hero.PKPoints);
                AddInteger(bindings, "H.MAXAC", StatValue(hero, Stat.MaxAC));
                AddInteger(bindings, "H.MAXMAC", StatValue(hero, Stat.MaxMAC));
                AddInteger(bindings, "H.MAXDC", StatValue(hero, Stat.MaxDC));
                AddInteger(bindings, "H.MAXMC", StatValue(hero, Stat.MaxMC));
                AddInteger(bindings, "H.SC", StatValue(hero, Stat.MinSC));
                AddInteger(bindings, "H.MAXSC", StatValue(hero, Stat.MaxSC));
                AddString(bindings, "H.HIT", StatValue(hero, Stat.Accuracy).ToString(CultureInfo.InvariantCulture));
                AddString(bindings, "H.SPD", StatValue(hero, Stat.Agility).ToString(CultureInfo.InvariantCulture));
                AddString(bindings, "H.LUCK", StatValue(hero, Stat.Luck).ToString(CultureInfo.InvariantCulture));
                AddString(bindings, "H.MAP", hero.CurrentMap?.Info?.FileName);
                AddInteger(bindings, "H.X", hero.CurrentLocation.X);
                AddInteger(bindings, "H.Y", hero.CurrentLocation.Y);
            }

            if (LingFengTxtTriggerContext.Current?.Payload is LingFengMonsterKillEvent kill &&
                kill.ActorKind == LingFengCombatActorKind.Hero)
            {
                ServerSymbolContextKind context = ServerSymbolContextKind.Hero |
                                                  ServerSymbolContextKind.Monster |
                                                  ServerSymbolContextKind.TriggerResult;
                AddString(bindings, "H.KILLMONNAME", kill.MonsterName, context);
                AddInteger(bindings, "H.GETEXP", kill.Experience, context);
                available |= ServerSymbolContextKind.Monster | ServerSymbolContextKind.TriggerResult;
            }
            else if (LingFengTxtTriggerContext.Current?.Payload is LingFengDamageEvent damage &&
                     damage.ActorKind == LingFengCombatActorKind.Hero)
            {
                ServerSymbolContextKind context = ServerSymbolContextKind.Hero |
                                                  ServerSymbolContextKind.Attacker |
                                                  ServerSymbolContextKind.Target |
                                                  ServerSymbolContextKind.TriggerResult;
                AddString(bindings, "H.CURRRTARGETNAME", damage.CurrentTargetName, context);
                AddString(bindings, "H.DAMAGEVALUE", damage.DamageValue.ToString(CultureInfo.InvariantCulture), context);
                AddString(bindings, "H.CURRRUSEMAGICID", damage.MagicId, context);
                AddString(bindings, "H.MAGICID", damage.MagicId, context);
                if (damage.Perspective == PlayerDamagePerspective.Outgoing)
                    AddString(bindings, "H.PKPOWER", damage.AppliedDamage.ToString(CultureInfo.InvariantCulture), context);
                else
                    AddInteger(bindings, "H.STRUCKHP", damage.AppliedDamage, context);

                if (damage.Perspective == PlayerDamagePerspective.Outgoing && damage.TargetIsMonster)
                {
                    AddString(bindings, "H.ATTACKMONSTER_NAME", damage.TargetName, context);
                    AddString(bindings, "H.ATTACKMONSTER_NAMEEX", damage.CurrentTargetName, context);
                    AddString(bindings, "H.ATTACKMONSTER_X", damage.TargetX.ToString(CultureInfo.InvariantCulture), context);
                    AddString(bindings, "H.ATTACKMONSTER_XEX", damage.TargetX.ToString(CultureInfo.InvariantCulture), context);
                    AddString(bindings, "H.ATTACKMONSTER_Y", damage.TargetY.ToString(CultureInfo.InvariantCulture), context);
                    AddString(bindings, "H.ATTACKMONSTER_YEX", damage.TargetY.ToString(CultureInfo.InvariantCulture), context);
                    AddInteger(bindings, "H.ATTACKMONSTER_HP", damage.TargetHp, context);
                    AddString(bindings, "H.ATTACKMONSTER_HPEX", damage.TargetHp.ToString(CultureInfo.InvariantCulture), context);
                    AddInteger(bindings, "H.ATTACKMONSTER_MAXHP", damage.TargetMaxHp, context);
                    AddInteger(bindings, "H.ATTACKMONSTER_MAXHPEX", damage.TargetMaxHp, context);
                }
                available |= ServerSymbolContextKind.Attacker | ServerSymbolContextKind.Target |
                             ServerSymbolContextKind.TriggerResult;
            }

            return available;
        }

        internal static void AppendDefinitions(ICollection<ServerSymbolDefinition> definitions)
        {
            string[] staticStrings = { "HERONAME", "H.USERNAME", "H.JOB", "H.GENDER", "H.RIGHTHAND", "H.WEAPON", "H.HELMET", "H.NECKLACE", "H.RING_L", "H.RING_R", "H.ARMRING_L", "H.ARMRING_R", "H.BELT", "H.BOOTS", "H.BUJUK", "H.MAP", "H.HIT", "H.SPD", "H.LUCK" };
            string[] staticIntegers = { "H.LEVEL", "H.EXP", "H.HP", "H.MAXHP", "H.MP", "H.MAXMP", "H.PKPOINT", "H.MAXAC", "H.MAXMAC", "H.MAXDC", "H.MAXMC", "H.SC", "H.MAXSC", "H.X", "H.Y" };
            AddDefinitions(definitions, ServerSymbolValueType.String, ServerSymbolContextKind.Hero, staticStrings);
            AddDefinitions(definitions, ServerSymbolValueType.Integer, ServerSymbolContextKind.Hero, staticIntegers);

            ServerSymbolContextKind damage = ServerSymbolContextKind.Hero | ServerSymbolContextKind.Attacker |
                                             ServerSymbolContextKind.Target | ServerSymbolContextKind.TriggerResult;
            AddDefinitions(definitions, ServerSymbolValueType.String, damage,
                new[] { "H.CURRRTARGETNAME", "H.CURRRUSEMAGICID", "H.MAGICID", "H.DAMAGEVALUE", "H.PKPOWER", "H.ATTACKMONSTER_NAME", "H.ATTACKMONSTER_NAMEEX", "H.ATTACKMONSTER_X", "H.ATTACKMONSTER_XEX", "H.ATTACKMONSTER_Y", "H.ATTACKMONSTER_YEX", "H.ATTACKMONSTER_HPEX" });
            AddDefinitions(definitions, ServerSymbolValueType.Integer, damage,
                new[] { "H.STRUCKHP", "H.ATTACKMONSTER_HP", "H.ATTACKMONSTER_MAXHP", "H.ATTACKMONSTER_MAXHPEX" });

            ServerSymbolContextKind kill = ServerSymbolContextKind.Hero | ServerSymbolContextKind.Monster |
                                           ServerSymbolContextKind.TriggerResult;
            AddDefinitions(definitions, ServerSymbolValueType.String, kill, new[] { "H.KILLMONNAME" });
            AddDefinitions(definitions, ServerSymbolValueType.Integer, kill, new[] { "H.GETEXP" });
        }

        private static void AddDefinitions(ICollection<ServerSymbolDefinition> target, ServerSymbolValueType type,
            ServerSymbolContextKind context, IEnumerable<string> names)
        {
            foreach (string name in names)
                target.Add(new ServerSymbolDefinition(
                    name, Array.Empty<string>(), string.Empty, type, context,
                    ServerSymbolNoContextBehavior.StructuredFailure,
                    ServerSymbolSecurityClassification.Public, ServerSymbolAccessPolicy.Allowed,
                    "翎风 P2 英雄只读显示常量", "HeroInfo/HeroObject 与不可变战斗事件快照", "B",
                    new[] { "NPC", "命令参数", "系统触发", "ScriptApi" }, "执行时",
                    new[] { "LFENV07-P2" }, Documentation, 0, new DateOnly(2026, 8, 16)));
        }

        private static long StatValue(HeroObject hero, Stat stat) => hero.Stats?[stat] ?? 0;

        private static void AddEquipment(ICollection<ServerSymbolBinding> bindings, CharacterInfo info,
            string name, EquipmentSlot slot)
        {
            int index = (int)slot;
            string value = info.Equipment != null && index < info.Equipment.Length && info.Equipment[index] != null
                ? info.Equipment[index].FriendlyName
                : "空";
            AddString(bindings, name, value);
        }

        private static void AddString(ICollection<ServerSymbolBinding> bindings, string name, string value,
            ServerSymbolContextKind context = ServerSymbolContextKind.Hero) =>
            bindings.Add(ServerSymbolBinding.Value(context, name, ServerSymbolValue.FromString(value ?? string.Empty)));

        private static void AddInteger(ICollection<ServerSymbolBinding> bindings, string name, long value,
            ServerSymbolContextKind context = ServerSymbolContextKind.Hero) =>
            bindings.Add(ServerSymbolBinding.Value(context, name, ServerSymbolValue.FromInteger(value)));
    }
}
