using System;
using System.Collections.Generic;
using System.Drawing;
using Server.MirDatabase;
using Server.MirEnvir;

namespace Server.Persistence.Sql
{
    internal static class SqlWorldRelationsLoader
    {
        private sealed class NextIdRow
        {
            public string Name { get; set; }
            public long NextValue { get; set; }
        }

        public static SqlWorldRelationsSnapshot LoadAll(SqlSession session)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));

            var epoch = SqlWorldRelationsStore.TryGetRelationsEpochUtcMs(session);
            if (epoch <= 0) return null;

            var snapshot = new SqlWorldRelationsSnapshot { EpochUtcMs = epoch };

            var nextIds = session.Query<NextIdRow>(
                "SELECT name, next_value FROM next_ids WHERE name IN @Names",
                new { Names = SqlWorldRelationsStore.WorldNextIdKeys });

            for (var i = 0; i < nextIds.Count; i++)
            {
                var row = nextIds[i];
                if (row == null) continue;
                if (string.IsNullOrWhiteSpace(row.Name)) continue;
                snapshot.NextIds[row.Name.Trim()] = row.NextValue;
            }

            snapshot.MapInfos.AddRange(session.Query<WorldMapInfoRow>("SELECT * FROM map_infos ORDER BY map_id"));
            snapshot.MapSafeZones.AddRange(session.Query<WorldMapSafeZoneRow>("SELECT * FROM map_safe_zones ORDER BY map_id, slot_index"));
            snapshot.MapRespawns.AddRange(session.Query<WorldMapRespawnRow>("SELECT * FROM map_respawns ORDER BY respawn_index"));
            snapshot.MapMovements.AddRange(session.Query<WorldMapMovementRow>("SELECT * FROM map_movements ORDER BY map_id, slot_index"));
            snapshot.MapMineZones.AddRange(session.Query<WorldMapMineZoneRow>("SELECT * FROM map_mine_zones ORDER BY map_id, slot_index"));

            snapshot.ItemInfos.AddRange(session.Query<WorldItemInfoRow>("SELECT * FROM item_infos ORDER BY item_id"));
            snapshot.ItemInfoStats.AddRange(session.Query<WorldItemInfoStatRow>("SELECT * FROM item_info_stats ORDER BY item_id, stat"));

            snapshot.MonsterInfos.AddRange(session.Query<WorldMonsterInfoRow>("SELECT * FROM monster_infos ORDER BY monster_id"));
            snapshot.MonsterInfoStats.AddRange(session.Query<WorldMonsterInfoStatRow>("SELECT * FROM monster_info_stats ORDER BY monster_id, stat"));

            snapshot.NpcInfos.AddRange(session.Query<WorldNpcInfoRow>("SELECT * FROM npc_infos ORDER BY npc_id"));
            snapshot.NpcCollectQuests.AddRange(session.Query<WorldNpcQuestRow>("SELECT * FROM npc_collect_quests ORDER BY npc_id, slot_index"));
            snapshot.NpcFinishQuests.AddRange(session.Query<WorldNpcQuestRow>("SELECT * FROM npc_finish_quests ORDER BY npc_id, slot_index"));

            snapshot.QuestInfos.AddRange(session.Query<WorldQuestInfoRow>("SELECT * FROM quest_infos ORDER BY quest_id"));
            snapshot.MagicInfos.AddRange(session.Query<WorldMagicInfoRow>("SELECT * FROM magic_infos ORDER BY spell"));
            snapshot.GameShopItems.AddRange(session.Query<WorldGameShopItemRow>("SELECT * FROM gameshop_items ORDER BY gameshop_item_id"));

            var dragonRows = session.Query<WorldDragonInfoRow>("SELECT * FROM dragon_info ORDER BY dragon_id");
            if (dragonRows.Count > 0) snapshot.DragonInfo = dragonRows[0];
            snapshot.DragonExps.AddRange(session.Query<WorldDragonExpRow>("SELECT * FROM dragon_exps ORDER BY dragon_id, level"));

            snapshot.Conquests.AddRange(session.Query<WorldConquestRow>("SELECT * FROM conquests ORDER BY conquest_id"));
            snapshot.ConquestExtraMaps.AddRange(session.Query<WorldConquestExtraMapRow>("SELECT * FROM conquest_extra_maps ORDER BY conquest_id, slot_index"));
            snapshot.ConquestGuards.AddRange(session.Query<WorldConquestGuardRow>("SELECT * FROM conquest_guards ORDER BY conquest_id, guard_id"));
            snapshot.ConquestGates.AddRange(session.Query<WorldConquestGateRow>("SELECT * FROM conquest_gates ORDER BY conquest_id, gate_id"));
            snapshot.ConquestWalls.AddRange(session.Query<WorldConquestWallRow>("SELECT * FROM conquest_walls ORDER BY conquest_id, wall_id"));
            snapshot.ConquestSieges.AddRange(session.Query<WorldConquestSiegeRow>("SELECT * FROM conquest_sieges ORDER BY conquest_id, siege_id"));
            snapshot.ConquestFlags.AddRange(session.Query<WorldConquestFlagRow>("SELECT * FROM conquest_flags ORDER BY conquest_id, flag_id"));
            snapshot.ConquestControlPoints.AddRange(session.Query<WorldConquestControlPointRow>("SELECT * FROM conquest_control_points ORDER BY conquest_id, control_point_id"));

            var respawnTimerRows = session.Query<WorldRespawnTimerStateRow>("SELECT * FROM respawn_timer_state ORDER BY timer_id");
            if (respawnTimerRows.Count > 0) snapshot.RespawnTimerState = respawnTimerRows[0];
            snapshot.RespawnTickOptions.AddRange(session.Query<WorldRespawnTickOptionRow>("SELECT * FROM respawn_tick_options ORDER BY timer_id, slot_index"));

            return snapshot;
        }

        public static void RestoreToEnvir(Envir envir, SqlWorldRelationsSnapshot snapshot)
        {
            if (envir == null) throw new ArgumentNullException(nameof(envir));
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));

            lock (Envir.LoadLock)
            {
                Envir.LoadVersion = Envir.Version;
                Envir.LoadCustomVersion = Envir.CustomVersion;

                envir.MapIndex = GetNextId(snapshot, SqlWorldRelationsStore.NextIdWorldMapIndex, envir.MapIndex);
                envir.ItemIndex = GetNextId(snapshot, SqlWorldRelationsStore.NextIdWorldItemIndex, envir.ItemIndex);
                envir.MonsterIndex = GetNextId(snapshot, SqlWorldRelationsStore.NextIdWorldMonsterIndex, envir.MonsterIndex);
                envir.NPCIndex = GetNextId(snapshot, SqlWorldRelationsStore.NextIdWorldNpcIndex, envir.NPCIndex);
                envir.QuestIndex = GetNextId(snapshot, SqlWorldRelationsStore.NextIdWorldQuestIndex, envir.QuestIndex);
                envir.GameshopIndex = GetNextId(snapshot, SqlWorldRelationsStore.NextIdWorldGameshopIndex, envir.GameshopIndex);
                envir.ConquestIndex = GetNextId(snapshot, SqlWorldRelationsStore.NextIdWorldConquestIndex, envir.ConquestIndex);
                envir.RespawnIndex = GetNextId(snapshot, SqlWorldRelationsStore.NextIdWorldRespawnIndex, envir.RespawnIndex);

                envir.MapInfoList.Clear();
                envir.ItemInfoList.Clear();
                envir.MonsterInfoList.Clear();
                envir.NPCInfoList.Clear();
                envir.QuestInfoList.Clear();
                envir.MagicInfoList.Clear();
                envir.GameShopList.Clear();
                envir.ConquestInfoList.Clear();

                var mapsById = new Dictionary<long, MapInfo>(snapshot.MapInfos.Count);
                for (var i = 0; i < snapshot.MapInfos.Count; i++)
                {
                    var row = snapshot.MapInfos[i];
                    if (row == null) continue;

                    var map = new MapInfo
                    {
                        Index = ClampToInt(row.MapId),
                        FileName = row.FileName ?? string.Empty,
                        Title = row.Title ?? string.Empty,
                        MiniMap = (ushort)ClampToInt(row.MiniMap),
                        BigMap = (ushort)ClampToInt(row.BigMap),
                        Music = (ushort)ClampToInt(row.Music),
                        Light = (LightSetting)row.Light,
                        MapDarkLight = (byte)ClampToInt(row.MapDarkLight),
                        MineIndex = (byte)ClampToInt(row.MineIndex),
                        NoTeleport = row.NoTeleport != 0,
                        NoReconnect = row.NoReconnect != 0,
                        NoReconnectMap = row.NoReconnectMap ?? string.Empty,
                        NoRandom = row.NoRandom != 0,
                        NoEscape = row.NoEscape != 0,
                        NoRecall = row.NoRecall != 0,
                        NoDrug = row.NoDrug != 0,
                        NoPosition = row.NoPosition != 0,
                        NoThrowItem = row.NoThrowItem != 0,
                        NoDropPlayer = row.NoDropPlayer != 0,
                        NoDropMonster = row.NoDropMonster != 0,
                        NoNames = row.NoNames != 0,
                        Fight = row.Fight != 0,
                        Fire = row.Fire != 0,
                        FireDamage = row.FireDamage,
                        Lightning = row.Lightning != 0,
                        LightningDamage = row.LightningDamage,
                        NoMount = row.NoMount != 0,
                        NeedBridle = row.NeedBridle != 0,
                        NoFight = row.NoFight != 0,
                        NoTownTeleport = row.NoTownTeleport != 0,
                        NoReincarnation = row.NoReincarnation != 0,
                        WeatherParticles = (WeatherSetting)row.WeatherParticles,
                    };

                    envir.MapInfoList.Add(map);
                    mapsById[row.MapId] = map;
                }

                for (var i = 0; i < snapshot.MapSafeZones.Count; i++)
                {
                    var row = snapshot.MapSafeZones[i];
                    if (row == null) continue;
                    if (!mapsById.TryGetValue(row.MapId, out var map)) continue;

                    map.SafeZones.Add(new SafeZoneInfo
                    {
                        Info = map,
                        Location = new Point(row.X, row.Y),
                        Size = (ushort)ClampToInt(row.ZoneSize),
                        StartPoint = row.StartPoint != 0,
                    });
                }

                for (var i = 0; i < snapshot.MapRespawns.Count; i++)
                {
                    var row = snapshot.MapRespawns[i];
                    if (row == null) continue;
                    if (!mapsById.TryGetValue(row.MapId, out var map)) continue;

                    map.Respawns.Add(new RespawnInfo
                    {
                        RespawnIndex = row.RespawnIndex,
                        MonsterIndex = row.MonsterIndex,
                        Location = new Point(row.X, row.Y),
                        Count = (ushort)ClampToInt(row.SpawnCount),
                        Spread = (ushort)ClampToInt(row.Spread),
                        Delay = (ushort)ClampToInt(row.Delay),
                        RandomDelay = (ushort)ClampToInt(row.RandomDelay),
                        Direction = (byte)ClampToInt(row.Direction),
                        RoutePath = row.RoutePath ?? string.Empty,
                        SaveRespawnTime = row.SaveRespawnTime != 0,
                        RespawnTicks = (ushort)ClampToInt(row.RespawnTicks),
                    });
                }

                for (var i = 0; i < snapshot.MapMovements.Count; i++)
                {
                    var row = snapshot.MapMovements[i];
                    if (row == null) continue;
                    if (!mapsById.TryGetValue(row.MapId, out var map)) continue;

                    map.Movements.Add(new MovementInfo
                    {
                        MapIndex = row.DestinationMapId,
                        Source = new Point(row.SrcX, row.SrcY),
                        Destination = new Point(row.DstX, row.DstY),
                        NeedHole = row.NeedHole != 0,
                        NeedMove = row.NeedMove != 0,
                        ConquestIndex = row.ConquestIndex,
                        ShowOnBigMap = row.ShowOnBigMap != 0,
                        Icon = row.Icon,
                    });
                }

                for (var i = 0; i < snapshot.MapMineZones.Count; i++)
                {
                    var row = snapshot.MapMineZones[i];
                    if (row == null) continue;
                    if (!mapsById.TryGetValue(row.MapId, out var map)) continue;

                    map.MineZones.Add(new MineZone
                    {
                        Location = new Point(row.X, row.Y),
                        Size = (ushort)ClampToInt(row.ZoneSize),
                        Mine = (byte)ClampToInt(row.Mine),
                    });
                }

                var itemById = new Dictionary<long, ItemInfo>(snapshot.ItemInfos.Count);
                for (var i = 0; i < snapshot.ItemInfos.Count; i++)
                {
                    var row = snapshot.ItemInfos[i];
                    if (row == null) continue;

                    var item = new ItemInfo
                    {
                        Index = ClampToInt(row.ItemId),
                        Name = row.Name ?? string.Empty,
                        Type = (ItemType)row.ItemType,
                        Grade = (ItemGrade)row.Grade,
                        RequiredType = (RequiredType)row.RequiredType,
                        RequiredClass = (RequiredClass)row.RequiredClass,
                        RequiredGender = (RequiredGender)row.RequiredGender,
                        Set = (ItemSet)row.ItemSet,
                        Shape = (short)ClampToInt(row.Shape),
                        Weight = (byte)ClampToInt(row.Weight),
                        Light = (byte)ClampToInt(row.Light),
                        RequiredAmount = (byte)ClampToInt(row.RequiredAmount),
                        Image = (ushort)ClampToInt(row.Image),
                        Durability = (ushort)ClampToInt(row.Durability),
                        Price = ClampToUInt(row.Price),
                        StackSize = (ushort)ClampToInt(row.StackSize),
                        StartItem = row.StartItem != 0,
                        Effect = (byte)ClampToInt(row.Effect),
                        NeedIdentify = row.NeedIdentify != 0,
                        ShowGroupPickup = row.ShowGroupPickup != 0,
                        GlobalDropNotify = row.GlobalDropNotify != 0,
                        ClassBased = row.ClassBased != 0,
                        LevelBased = row.LevelBased != 0,
                        CanMine = row.CanMine != 0,
                        Bind = (BindMode)row.Bind,
                        Unique = (SpecialItemMode)row.UniqueMode,
                        RandomStatsId = (byte)ClampToInt(row.RandomStatsId),
                        CanFastRun = row.CanFastRun != 0,
                        CanAwakening = row.CanAwakening != 0,
                        Slots = (byte)ClampToInt(row.Slots),
                        ToolTip = row.ToolTip ?? string.Empty,
                    };

                    if (item.RandomStatsId < Settings.RandomItemStatsList.Count)
                        item.RandomStats = Settings.RandomItemStatsList[item.RandomStatsId];

                    envir.ItemInfoList.Add(item);
                    itemById[row.ItemId] = item;
                }

                for (var i = 0; i < snapshot.ItemInfoStats.Count; i++)
                {
                    var row = snapshot.ItemInfoStats[i];
                    if (row == null) continue;
                    if (!itemById.TryGetValue(row.ItemId, out var item)) continue;
                    item.Stats[(Stat)row.Stat] = row.StatValue;
                }

                var monsterById = new Dictionary<long, MonsterInfo>(snapshot.MonsterInfos.Count);
                for (var i = 0; i < snapshot.MonsterInfos.Count; i++)
                {
                    var row = snapshot.MonsterInfos[i];
                    if (row == null) continue;

                    var monster = new MonsterInfo
                    {
                        Index = ClampToInt(row.MonsterId),
                        Name = row.Name ?? string.Empty,
                        Image = (Monster)row.Image,
                        AI = (ushort)ClampToInt(row.AI),
                        Effect = (byte)ClampToInt(row.Effect),
                        Level = (ushort)ClampToInt(row.Level),
                        ViewRange = (byte)ClampToInt(row.ViewRange),
                        CoolEye = (byte)ClampToInt(row.CoolEye),
                        Light = (byte)ClampToInt(row.Light),
                        AttackSpeed = (ushort)ClampToInt(row.AttackSpeed),
                        MoveSpeed = (ushort)ClampToInt(row.MoveSpeed),
                        Experience = ClampToUInt(row.Experience),
                        CanTame = row.CanTame != 0,
                        CanPush = row.CanPush != 0,
                        AutoRev = row.AutoRev != 0,
                        Undead = row.Undead != 0,
                        DropPath = row.DropPath ?? string.Empty,
                    };

                    envir.MonsterInfoList.Add(monster);
                    monsterById[row.MonsterId] = monster;
                }

                for (var i = 0; i < snapshot.MonsterInfoStats.Count; i++)
                {
                    var row = snapshot.MonsterInfoStats[i];
                    if (row == null) continue;
                    if (!monsterById.TryGetValue(row.MonsterId, out var monster)) continue;
                    monster.Stats[(Stat)row.Stat] = row.StatValue;
                }

                var npcById = new Dictionary<long, NPCInfo>(snapshot.NpcInfos.Count);
                for (var i = 0; i < snapshot.NpcInfos.Count; i++)
                {
                    var row = snapshot.NpcInfos[i];
                    if (row == null) continue;

                    var npc = new NPCInfo
                    {
                        Index = ClampToInt(row.NpcId),
                        MapIndex = ClampToInt(row.MapId),
                        FileName = row.FileName ?? string.Empty,
                        Name = row.Name ?? string.Empty,
                        Location = new Point(row.X, row.Y),
                        Rate = (ushort)ClampToInt(row.Rate),
                        Image = (ushort)ClampToInt(row.Image),
                        TimeVisible = row.TimeVisible != 0,
                        HourStart = (byte)ClampToInt(row.HourStart),
                        MinuteStart = (byte)ClampToInt(row.MinuteStart),
                        HourEnd = (byte)ClampToInt(row.HourEnd),
                        MinuteEnd = (byte)ClampToInt(row.MinuteEnd),
                        MinLev = (short)ClampToInt(row.MinLev),
                        MaxLev = (short)ClampToInt(row.MaxLev),
                        DayofWeek = row.DayOfWeek ?? string.Empty,
                        ClassRequired = row.ClassRequired ?? string.Empty,
                        Conquest = row.Conquest,
                        FlagNeeded = row.FlagNeeded,
                        ShowOnBigMap = row.ShowOnBigMap != 0,
                        BigMapIcon = row.BigMapIcon,
                        CanTeleportTo = row.CanTeleportTo != 0,
                        ConquestVisible = row.ConquestVisible != 0,
                    };

                    envir.NPCInfoList.Add(npc);
                    npcById[row.NpcId] = npc;
                }

                for (var i = 0; i < snapshot.NpcCollectQuests.Count; i++)
                {
                    var row = snapshot.NpcCollectQuests[i];
                    if (row == null) continue;
                    if (!npcById.TryGetValue(row.NpcId, out var npc)) continue;
                    npc.CollectQuestIndexes.Add(ClampToInt(row.QuestId));
                }

                for (var i = 0; i < snapshot.NpcFinishQuests.Count; i++)
                {
                    var row = snapshot.NpcFinishQuests[i];
                    if (row == null) continue;
                    if (!npcById.TryGetValue(row.NpcId, out var npc)) continue;
                    npc.FinishQuestIndexes.Add(ClampToInt(row.QuestId));
                }

                for (var i = 0; i < snapshot.QuestInfos.Count; i++)
                {
                    var row = snapshot.QuestInfos[i];
                    if (row == null) continue;

                    var quest = new QuestInfo
                    {
                        Index = ClampToInt(row.QuestId),
                        Name = row.Name ?? string.Empty,
                        Group = row.QuestGroup ?? string.Empty,
                        FileName = row.FileName ?? string.Empty,
                        RequiredMinLevel = row.RequiredMinLevel,
                        RequiredMaxLevel = row.RequiredMaxLevel == 0 ? ushort.MaxValue : row.RequiredMaxLevel,
                        RequiredQuest = row.RequiredQuest,
                        RequiredClass = (RequiredClass)row.RequiredClass,
                        Type = (QuestType)row.QuestType,
                        GotoMessage = row.GotoMessage ?? string.Empty,
                        KillMessage = row.KillMessage ?? string.Empty,
                        ItemMessage = row.ItemMessage ?? string.Empty,
                        FlagMessage = row.FlagMessage ?? string.Empty,
                        TimeLimitInSeconds = row.TimeLimitSeconds,
                    };

                    quest.LoadInfo();
                    envir.QuestInfoList.Add(quest);
                }

                envir.DragonInfo = new DragonInfo();
                if (snapshot.DragonInfo != null)
                {
                    var row = snapshot.DragonInfo;
                    envir.DragonInfo.Enabled = row.Enabled != 0;
                    envir.DragonInfo.MapFileName = row.MapFileName ?? string.Empty;
                    envir.DragonInfo.MonsterName = row.MonsterName ?? string.Empty;
                    envir.DragonInfo.BodyName = row.BodyName ?? string.Empty;
                    envir.DragonInfo.Location = new Point(row.LocationX, row.LocationY);
                    envir.DragonInfo.DropAreaTop = new Point(row.DropAreaTopX, row.DropAreaTopY);
                    envir.DragonInfo.DropAreaBottom = new Point(row.DropAreaBottomX, row.DropAreaBottomY);
                }

                for (var i = 0; i < snapshot.DragonExps.Count; i++)
                {
                    var row = snapshot.DragonExps[i];
                    if (row == null) continue;
                    var level = row.Level;
                    if (level < 1) continue;
                    if (level > envir.DragonInfo.Exps.Length) continue;
                    envir.DragonInfo.Exps[level - 1] = row.Exp;
                }

                var magicSpells = new HashSet<Spell>();
                for (var i = 0; i < snapshot.MagicInfos.Count; i++)
                {
                    var row = snapshot.MagicInfos[i];
                    if (row == null) continue;

                    var spell = (Spell)row.Spell;
                    var magic = new MagicInfo
                    {
                        Name = row.Name ?? string.Empty,
                        Spell = spell,
                        BaseCost = (byte)ClampToInt(row.BaseCost),
                        LevelCost = (byte)ClampToInt(row.LevelCost),
                        Icon = (byte)ClampToInt(row.Icon),
                        Level1 = (byte)ClampToInt(row.Level1),
                        Level2 = (byte)ClampToInt(row.Level2),
                        Level3 = (byte)ClampToInt(row.Level3),
                        Need1 = (ushort)ClampToInt(row.Need1),
                        Need2 = (ushort)ClampToInt(row.Need2),
                        Need3 = (ushort)ClampToInt(row.Need3),
                        DelayBase = ClampToUInt(row.DelayBase),
                        DelayReduction = ClampToUInt(row.DelayReduction),
                        PowerBase = (ushort)ClampToInt(row.PowerBase),
                        PowerBonus = (ushort)ClampToInt(row.PowerBonus),
                        MPowerBase = (ushort)ClampToInt(row.MPowerBase),
                        MPowerBonus = (ushort)ClampToInt(row.MPowerBonus),
                        Range = (byte)ClampToInt(row.MagicRange),
                        MultiplierBase = (float)row.MultiplierBase,
                        MultiplierBonus = (float)row.MultiplierBonus,
                    };

                    if (magicSpells.Add(spell))
                        envir.MagicInfoList.Add(magic);
                }

                for (var i = 0; i < snapshot.GameShopItems.Count; i++)
                {
                    var row = snapshot.GameShopItems[i];
                    if (row == null) continue;

                    var item = new GameShopItem
                    {
                        ItemIndex = ClampToInt(row.ItemId),
                        GIndex = ClampToInt(row.GameshopItemId),
                        GoldPrice = ClampToUInt(row.GoldPrice),
                        CreditPrice = ClampToUInt(row.CreditPrice),
                        Count = (ushort)ClampToInt(row.Count),
                        Class = row.ClassMask ?? string.Empty,
                        Category = row.Category ?? string.Empty,
                        Stock = row.Stock,
                        iStock = row.IStock != 0,
                        Deal = row.Deal != 0,
                        TopItem = row.TopItem != 0,
                        Date = DateTime.FromBinary(row.DateBinary),
                        CanBuyGold = row.CanBuyGold != 0,
                        CanBuyCredit = row.CanBuyCredit != 0,
                    };

                    if (Envir.Main.BindGameShop(item))
                        envir.GameShopList.Add(item);
                }

                var conquestsById = new Dictionary<long, ConquestInfo>(snapshot.Conquests.Count);
                for (var i = 0; i < snapshot.Conquests.Count; i++)
                {
                    var row = snapshot.Conquests[i];
                    if (row == null) continue;

                    var conquest = new ConquestInfo
                    {
                        Index = ClampToInt(row.ConquestId),
                        FullMap = row.FullMap != 0,
                        Location = new Point(row.LocationX, row.LocationY),
                        Size = (ushort)ClampToInt(row.Size),
                        Name = row.Name ?? string.Empty,
                        MapIndex = ClampToInt(row.MapId),
                        PalaceIndex = ClampToInt(row.PalaceId),
                        GuardIndex = row.GuardIndex,
                        GateIndex = row.GateIndex,
                        WallIndex = row.WallIndex,
                        SiegeIndex = row.SiegeIndex,
                        FlagIndex = row.FlagIndex,
                        StartHour = (byte)ClampToInt(row.StartHour),
                        WarLength = row.WarLength,
                        Type = (ConquestType)row.ConquestType,
                        Game = (ConquestGame)row.ConquestGame,
                        Monday = row.Monday != 0,
                        Tuesday = row.Tuesday != 0,
                        Wednesday = row.Wednesday != 0,
                        Thursday = row.Thursday != 0,
                        Friday = row.Friday != 0,
                        Saturday = row.Saturday != 0,
                        Sunday = row.Sunday != 0,
                        KingLocation = new Point(row.KingLocationX, row.KingLocationY),
                        KingSize = (ushort)ClampToInt(row.KingSize),
                        ControlPointIndex = row.ControlPointIndex,
                    };

                    envir.ConquestInfoList.Add(conquest);
                    conquestsById[row.ConquestId] = conquest;
                }

                for (var i = 0; i < snapshot.ConquestExtraMaps.Count; i++)
                {
                    var row = snapshot.ConquestExtraMaps[i];
                    if (row == null) continue;
                    if (!conquestsById.TryGetValue(row.ConquestId, out var conquest)) continue;
                    conquest.ExtraMaps.Add(ClampToInt(row.MapId));
                }

                for (var i = 0; i < snapshot.ConquestGuards.Count; i++)
                {
                    var row = snapshot.ConquestGuards[i];
                    if (row == null) continue;
                    if (!conquestsById.TryGetValue(row.ConquestId, out var conquest)) continue;
                    conquest.ConquestGuards.Add(new ConquestArcherInfo
                    {
                        Index = row.GuardId,
                        Location = new Point(row.LocationX, row.LocationY),
                        MobIndex = row.MobIndex,
                        Name = row.Name ?? string.Empty,
                        RepairCost = ClampToUInt(row.RepairCost),
                    });
                }

                for (var i = 0; i < snapshot.ConquestGates.Count; i++)
                {
                    var row = snapshot.ConquestGates[i];
                    if (row == null) continue;
                    if (!conquestsById.TryGetValue(row.ConquestId, out var conquest)) continue;
                    conquest.ConquestGates.Add(new ConquestGateInfo
                    {
                        Index = row.GateId,
                        Location = new Point(row.LocationX, row.LocationY),
                        MobIndex = row.MobIndex,
                        Name = row.Name ?? string.Empty,
                        RepairCost = ClampToInt(row.RepairCost),
                    });
                }

                for (var i = 0; i < snapshot.ConquestWalls.Count; i++)
                {
                    var row = snapshot.ConquestWalls[i];
                    if (row == null) continue;
                    if (!conquestsById.TryGetValue(row.ConquestId, out var conquest)) continue;
                    conquest.ConquestWalls.Add(new ConquestWallInfo
                    {
                        Index = row.WallId,
                        Location = new Point(row.LocationX, row.LocationY),
                        MobIndex = row.MobIndex,
                        Name = row.Name ?? string.Empty,
                        RepairCost = ClampToInt(row.RepairCost),
                    });
                }

                for (var i = 0; i < snapshot.ConquestSieges.Count; i++)
                {
                    var row = snapshot.ConquestSieges[i];
                    if (row == null) continue;
                    if (!conquestsById.TryGetValue(row.ConquestId, out var conquest)) continue;
                    conquest.ConquestSieges.Add(new ConquestSiegeInfo
                    {
                        Index = row.SiegeId,
                        Location = new Point(row.LocationX, row.LocationY),
                        MobIndex = row.MobIndex,
                        Name = row.Name ?? string.Empty,
                        RepairCost = ClampToInt(row.RepairCost),
                    });
                }

                for (var i = 0; i < snapshot.ConquestFlags.Count; i++)
                {
                    var row = snapshot.ConquestFlags[i];
                    if (row == null) continue;
                    if (!conquestsById.TryGetValue(row.ConquestId, out var conquest)) continue;
                    conquest.ConquestFlags.Add(new ConquestFlagInfo
                    {
                        Index = row.FlagId,
                        Location = new Point(row.LocationX, row.LocationY),
                        Name = row.Name ?? string.Empty,
                        FileName = row.FileName ?? string.Empty,
                    });
                }

                for (var i = 0; i < snapshot.ConquestControlPoints.Count; i++)
                {
                    var row = snapshot.ConquestControlPoints[i];
                    if (row == null) continue;
                    if (!conquestsById.TryGetValue(row.ConquestId, out var conquest)) continue;
                    conquest.ControlPoints.Add(new ConquestFlagInfo
                    {
                        Index = row.ControlPointId,
                        Location = new Point(row.LocationX, row.LocationY),
                        Name = row.Name ?? string.Empty,
                        FileName = row.FileName ?? string.Empty,
                    });
                }

                envir.RespawnTick = new RespawnTimer();
                if (snapshot.RespawnTimerState != null)
                {
                    envir.RespawnTick.BaseSpawnRate = (byte)ClampToInt(snapshot.RespawnTimerState.BaseSpawnRate);
                    envir.RespawnTick.CurrentTickcounter = unchecked((ulong)snapshot.RespawnTimerState.CurrentTickCounter);
                    envir.RespawnTick.LastTick = envir.Time;
                    envir.RespawnTick.CurrentDelay = (long)Math.Round(envir.RespawnTick.BaseSpawnRate * 60000D);

                    envir.RespawnTick.Respawn.Clear();
                    for (var i = 0; i < snapshot.RespawnTickOptions.Count; i++)
                    {
                        var row = snapshot.RespawnTickOptions[i];
                        if (row == null) continue;
                        if (row.TimerId != snapshot.RespawnTimerState.TimerId) continue;
                        envir.RespawnTick.Respawn.Add(new RespawnTickOption { UserCount = row.UserCount, DelayLoss = row.DelayLoss });
                    }
                }

                Settings.LinkGuildCreationItems(envir.ItemInfoList);
            }
        }

        private static int GetNextId(SqlWorldRelationsSnapshot snapshot, string key, int fallback)
        {
            if (snapshot == null) return fallback;
            if (string.IsNullOrWhiteSpace(key)) return fallback;
            return snapshot.NextIds.TryGetValue(key, out var value) ? ClampToInt(value) : fallback;
        }

        private static int ClampToInt(long value)
        {
            if (value < int.MinValue) return int.MinValue;
            if (value > int.MaxValue) return int.MaxValue;
            return (int)value;
        }

        private static uint ClampToUInt(long value)
        {
            if (value < 0) return 0;
            if (value > uint.MaxValue) return uint.MaxValue;
            return (uint)value;
        }
    }
}
