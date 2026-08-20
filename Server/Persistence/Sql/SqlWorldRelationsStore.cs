using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Server.MirDatabase;
using Server.MirEnvir;

namespace Server.Persistence.Sql
{
    internal sealed class WorldMapInfoRow
    {
        public long MapId { get; set; }
        public string FileName { get; set; }
        public string Title { get; set; }
        public int MiniMap { get; set; }
        public int BigMap { get; set; }
        public int Music { get; set; }
        public int Light { get; set; }
        public int MapDarkLight { get; set; }
        public int MineIndex { get; set; }
        public int NoTeleport { get; set; }
        public int NoReconnect { get; set; }
        public string NoReconnectMap { get; set; }
        public int NoRandom { get; set; }
        public int NoEscape { get; set; }
        public int NoRecall { get; set; }
        public int NoDrug { get; set; }
        public int NoPosition { get; set; }
        public int NoThrowItem { get; set; }
        public int NoDropPlayer { get; set; }
        public int NoDropMonster { get; set; }
        public int NoNames { get; set; }
        public int Fight { get; set; }
        public int Fire { get; set; }
        public int FireDamage { get; set; }
        public int Lightning { get; set; }
        public int LightningDamage { get; set; }
        public int NoMount { get; set; }
        public int NeedBridle { get; set; }
        public int NoFight { get; set; }
        public int NoTownTeleport { get; set; }
        public int NoReincarnation { get; set; }
        public int WeatherParticles { get; set; }
        public long UpdatedUtcMs { get; set; }
    }

    internal sealed class WorldMapSafeZoneRow
    {
        public long MapId { get; set; }
        public int SlotIndex { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int ZoneSize { get; set; }
        public int StartPoint { get; set; }
        public long UpdatedUtcMs { get; set; }
    }

    internal sealed class WorldMapRespawnRow
    {
        public int RespawnIndex { get; set; }
        public long MapId { get; set; }
        public int MonsterIndex { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int SpawnCount { get; set; }
        public int Spread { get; set; }
        public int Delay { get; set; }
        public int RandomDelay { get; set; }
        public int Direction { get; set; }
        public string RoutePath { get; set; }
        public int SaveRespawnTime { get; set; }
        public int RespawnTicks { get; set; }
        public long UpdatedUtcMs { get; set; }
    }

    internal sealed class WorldMapMovementRow
    {
        public long MapId { get; set; }
        public int SlotIndex { get; set; }
        public int DestinationMapId { get; set; }
        public int SrcX { get; set; }
        public int SrcY { get; set; }
        public int DstX { get; set; }
        public int DstY { get; set; }
        public int NeedHole { get; set; }
        public int NeedMove { get; set; }
        public int ConquestIndex { get; set; }
        public int ShowOnBigMap { get; set; }
        public int Icon { get; set; }
        public long UpdatedUtcMs { get; set; }
    }

    internal sealed class WorldMapMineZoneRow
    {
        public long MapId { get; set; }
        public int SlotIndex { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int ZoneSize { get; set; }
        public int Mine { get; set; }
        public long UpdatedUtcMs { get; set; }
    }

    internal sealed class WorldItemInfoRow
    {
        public long ItemId { get; set; }
        public string Name { get; set; }
        public int ItemType { get; set; }
        public int Grade { get; set; }
        public int RequiredType { get; set; }
        public int RequiredClass { get; set; }
        public int RequiredGender { get; set; }
        public int ItemSet { get; set; }
        public int Shape { get; set; }
        public int Weight { get; set; }
        public int Light { get; set; }
        public int RequiredAmount { get; set; }
        public int Image { get; set; }
        public int Durability { get; set; }
        public long Price { get; set; }
        public int StackSize { get; set; }
        public int StartItem { get; set; }
        public int Effect { get; set; }
        public int NeedIdentify { get; set; }
        public int ShowGroupPickup { get; set; }
        public int GlobalDropNotify { get; set; }
        public int ClassBased { get; set; }
        public int LevelBased { get; set; }
        public int CanMine { get; set; }
        public int Bind { get; set; }
        public int UniqueMode { get; set; }
        public int RandomStatsId { get; set; }
        public int CanFastRun { get; set; }
        public int CanAwakening { get; set; }
        public int Slots { get; set; }
        public string ToolTip { get; set; }
        public long UpdatedUtcMs { get; set; }
    }

    internal sealed class WorldItemInfoStatRow
    {
        public long ItemId { get; set; }
        public int Stat { get; set; }
        public int StatValue { get; set; }
        public long UpdatedUtcMs { get; set; }
    }

    internal sealed class WorldMonsterInfoRow
    {
        public long MonsterId { get; set; }
        public string Name { get; set; }
        public int Image { get; set; }
        public int AI { get; set; }
        public int Effect { get; set; }
        public int Level { get; set; }
        public int ViewRange { get; set; }
        public int CoolEye { get; set; }
        public int Light { get; set; }
        public int AttackSpeed { get; set; }
        public int MoveSpeed { get; set; }
        public long Experience { get; set; }
        public int CanTame { get; set; }
        public int CanPush { get; set; }
        public int AutoRev { get; set; }
        public int Undead { get; set; }
        public string DropPath { get; set; }
        public long UpdatedUtcMs { get; set; }
    }

    internal sealed class WorldMonsterInfoStatRow
    {
        public long MonsterId { get; set; }
        public int Stat { get; set; }
        public int StatValue { get; set; }
        public long UpdatedUtcMs { get; set; }
    }

    internal sealed class WorldNpcInfoRow
    {
        public long NpcId { get; set; }
        public long MapId { get; set; }
        public string FileName { get; set; }
        public string Name { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int Rate { get; set; }
        public int Image { get; set; }
        public int TimeVisible { get; set; }
        public int HourStart { get; set; }
        public int MinuteStart { get; set; }
        public int HourEnd { get; set; }
        public int MinuteEnd { get; set; }
        public int MinLev { get; set; }
        public int MaxLev { get; set; }
        public string DayOfWeek { get; set; }
        public string ClassRequired { get; set; }
        public int Conquest { get; set; }
        public int FlagNeeded { get; set; }
        public int ShowOnBigMap { get; set; }
        public int BigMapIcon { get; set; }
        public int CanTeleportTo { get; set; }
        public int ConquestVisible { get; set; }
        public long UpdatedUtcMs { get; set; }
    }

    internal sealed class WorldNpcQuestRow
    {
        public long NpcId { get; set; }
        public int SlotIndex { get; set; }
        public long QuestId { get; set; }
        public long UpdatedUtcMs { get; set; }
    }

    internal sealed class WorldQuestInfoRow
    {
        public long QuestId { get; set; }
        public string Name { get; set; }
        public string QuestGroup { get; set; }
        public string FileName { get; set; }
        public int RequiredMinLevel { get; set; }
        public int RequiredMaxLevel { get; set; }
        public int RequiredQuest { get; set; }
        public int RequiredClass { get; set; }
        public int QuestType { get; set; }
        public string GotoMessage { get; set; }
        public string KillMessage { get; set; }
        public string ItemMessage { get; set; }
        public string FlagMessage { get; set; }
        public int TimeLimitSeconds { get; set; }
        public long UpdatedUtcMs { get; set; }
    }

    internal sealed class WorldMagicInfoRow
    {
        public int Spell { get; set; }
        public string Name { get; set; }
        public int BaseCost { get; set; }
        public int LevelCost { get; set; }
        public int Icon { get; set; }
        public int Level1 { get; set; }
        public int Level2 { get; set; }
        public int Level3 { get; set; }
        public int Need1 { get; set; }
        public int Need2 { get; set; }
        public int Need3 { get; set; }
        public long DelayBase { get; set; }
        public long DelayReduction { get; set; }
        public int PowerBase { get; set; }
        public int PowerBonus { get; set; }
        public int MPowerBase { get; set; }
        public int MPowerBonus { get; set; }
        public int MagicRange { get; set; }
        public double MultiplierBase { get; set; }
        public double MultiplierBonus { get; set; }
        public long UpdatedUtcMs { get; set; }
    }

    internal sealed class WorldGameShopItemRow
    {
        public long GameshopItemId { get; set; }
        public long ItemId { get; set; }
        public long GoldPrice { get; set; }
        public long CreditPrice { get; set; }
        public int Count { get; set; }
        public string ClassMask { get; set; }
        public string Category { get; set; }
        public int Stock { get; set; }
        public int IStock { get; set; }
        public int Deal { get; set; }
        public int TopItem { get; set; }
        public long DateBinary { get; set; }
        public int CanBuyGold { get; set; }
        public int CanBuyCredit { get; set; }
        public long UpdatedUtcMs { get; set; }
    }

    internal sealed class WorldDragonInfoRow
    {
        public int DragonId { get; set; }
        public int Enabled { get; set; }
        public string MapFileName { get; set; }
        public string MonsterName { get; set; }
        public string BodyName { get; set; }
        public int LocationX { get; set; }
        public int LocationY { get; set; }
        public int DropAreaTopX { get; set; }
        public int DropAreaTopY { get; set; }
        public int DropAreaBottomX { get; set; }
        public int DropAreaBottomY { get; set; }
        public long UpdatedUtcMs { get; set; }
    }

    internal sealed class WorldDragonExpRow
    {
        public int DragonId { get; set; }
        public int Level { get; set; }
        public long Exp { get; set; }
        public long UpdatedUtcMs { get; set; }
    }

    internal sealed class WorldConquestRow
    {
        public long ConquestId { get; set; }
        public int FullMap { get; set; }
        public int LocationX { get; set; }
        public int LocationY { get; set; }
        public int Size { get; set; }
        public string Name { get; set; }
        public int MapId { get; set; }
        public int PalaceId { get; set; }
        public int GuardIndex { get; set; }
        public int GateIndex { get; set; }
        public int WallIndex { get; set; }
        public int SiegeIndex { get; set; }
        public int FlagIndex { get; set; }
        public int StartHour { get; set; }
        public int WarLength { get; set; }
        public int ConquestType { get; set; }
        public int ConquestGame { get; set; }
        public int Monday { get; set; }
        public int Tuesday { get; set; }
        public int Wednesday { get; set; }
        public int Thursday { get; set; }
        public int Friday { get; set; }
        public int Saturday { get; set; }
        public int Sunday { get; set; }
        public int KingLocationX { get; set; }
        public int KingLocationY { get; set; }
        public int KingSize { get; set; }
        public int ControlPointIndex { get; set; }
        public long UpdatedUtcMs { get; set; }
    }

    internal sealed class WorldConquestExtraMapRow
    {
        public long ConquestId { get; set; }
        public int SlotIndex { get; set; }
        public int MapId { get; set; }
        public long UpdatedUtcMs { get; set; }
    }

    internal sealed class WorldConquestGuardRow
    {
        public long ConquestId { get; set; }
        public int GuardId { get; set; }
        public int LocationX { get; set; }
        public int LocationY { get; set; }
        public int MobIndex { get; set; }
        public string Name { get; set; }
        public long RepairCost { get; set; }
        public long UpdatedUtcMs { get; set; }
    }

    internal sealed class WorldConquestGateRow
    {
        public long ConquestId { get; set; }
        public int GateId { get; set; }
        public int LocationX { get; set; }
        public int LocationY { get; set; }
        public int MobIndex { get; set; }
        public string Name { get; set; }
        public long RepairCost { get; set; }
        public long UpdatedUtcMs { get; set; }
    }

    internal sealed class WorldConquestWallRow
    {
        public long ConquestId { get; set; }
        public int WallId { get; set; }
        public int LocationX { get; set; }
        public int LocationY { get; set; }
        public int MobIndex { get; set; }
        public string Name { get; set; }
        public long RepairCost { get; set; }
        public long UpdatedUtcMs { get; set; }
    }

    internal sealed class WorldConquestSiegeRow
    {
        public long ConquestId { get; set; }
        public int SiegeId { get; set; }
        public int LocationX { get; set; }
        public int LocationY { get; set; }
        public int MobIndex { get; set; }
        public string Name { get; set; }
        public long RepairCost { get; set; }
        public long UpdatedUtcMs { get; set; }
    }

    internal sealed class WorldConquestFlagRow
    {
        public long ConquestId { get; set; }
        public int FlagId { get; set; }
        public int LocationX { get; set; }
        public int LocationY { get; set; }
        public string Name { get; set; }
        public string FileName { get; set; }
        public long UpdatedUtcMs { get; set; }
    }

    internal sealed class WorldConquestControlPointRow
    {
        public long ConquestId { get; set; }
        public int ControlPointId { get; set; }
        public int LocationX { get; set; }
        public int LocationY { get; set; }
        public string Name { get; set; }
        public string FileName { get; set; }
        public long UpdatedUtcMs { get; set; }
    }

    internal sealed class WorldRespawnTimerStateRow
    {
        public int TimerId { get; set; }
        public int BaseSpawnRate { get; set; }
        public long CurrentTickCounter { get; set; }
        public long UpdatedUtcMs { get; set; }
    }

    internal sealed class WorldRespawnTickOptionRow
    {
        public int TimerId { get; set; }
        public int SlotIndex { get; set; }
        public int UserCount { get; set; }
        public double DelayLoss { get; set; }
        public long UpdatedUtcMs { get; set; }
    }

    internal sealed class SqlWorldRelationsSnapshot
    {
        public long EpochUtcMs { get; init; }

        public Dictionary<string, long> NextIds { get; } = new Dictionary<string, long>(StringComparer.Ordinal);

        public List<WorldMapInfoRow> MapInfos { get; } = new List<WorldMapInfoRow>();
        public List<WorldMapSafeZoneRow> MapSafeZones { get; } = new List<WorldMapSafeZoneRow>();
        public List<WorldMapRespawnRow> MapRespawns { get; } = new List<WorldMapRespawnRow>();
        public List<WorldMapMovementRow> MapMovements { get; } = new List<WorldMapMovementRow>();
        public List<WorldMapMineZoneRow> MapMineZones { get; } = new List<WorldMapMineZoneRow>();

        public List<WorldItemInfoRow> ItemInfos { get; } = new List<WorldItemInfoRow>();
        public List<WorldItemInfoStatRow> ItemInfoStats { get; } = new List<WorldItemInfoStatRow>();

        public List<WorldMonsterInfoRow> MonsterInfos { get; } = new List<WorldMonsterInfoRow>();
        public List<WorldMonsterInfoStatRow> MonsterInfoStats { get; } = new List<WorldMonsterInfoStatRow>();

        public List<WorldNpcInfoRow> NpcInfos { get; } = new List<WorldNpcInfoRow>();
        public List<WorldNpcQuestRow> NpcCollectQuests { get; } = new List<WorldNpcQuestRow>();
        public List<WorldNpcQuestRow> NpcFinishQuests { get; } = new List<WorldNpcQuestRow>();

        public List<WorldQuestInfoRow> QuestInfos { get; } = new List<WorldQuestInfoRow>();
        public List<WorldMagicInfoRow> MagicInfos { get; } = new List<WorldMagicInfoRow>();

        public List<WorldGameShopItemRow> GameShopItems { get; } = new List<WorldGameShopItemRow>();

        public WorldDragonInfoRow DragonInfo { get; set; }
        public List<WorldDragonExpRow> DragonExps { get; } = new List<WorldDragonExpRow>();

        public List<WorldConquestRow> Conquests { get; } = new List<WorldConquestRow>();
        public List<WorldConquestExtraMapRow> ConquestExtraMaps { get; } = new List<WorldConquestExtraMapRow>();
        public List<WorldConquestGuardRow> ConquestGuards { get; } = new List<WorldConquestGuardRow>();
        public List<WorldConquestGateRow> ConquestGates { get; } = new List<WorldConquestGateRow>();
        public List<WorldConquestWallRow> ConquestWalls { get; } = new List<WorldConquestWallRow>();
        public List<WorldConquestSiegeRow> ConquestSieges { get; } = new List<WorldConquestSiegeRow>();
        public List<WorldConquestFlagRow> ConquestFlags { get; } = new List<WorldConquestFlagRow>();
        public List<WorldConquestControlPointRow> ConquestControlPoints { get; } = new List<WorldConquestControlPointRow>();

        public WorldRespawnTimerStateRow RespawnTimerState { get; set; }
        public List<WorldRespawnTickOptionRow> RespawnTickOptions { get; } = new List<WorldRespawnTickOptionRow>();
    }

    internal static class SqlWorldRelationsStore
    {
        public const string MetaKeyWorldRelationsEpochUtcMs = "world_relations_epoch_utc_ms";

        public const string NextIdWorldMapIndex = "world_map_index";
        public const string NextIdWorldItemIndex = "world_item_index";
        public const string NextIdWorldMonsterIndex = "world_monster_index";
        public const string NextIdWorldNpcIndex = "world_npc_index";
        public const string NextIdWorldQuestIndex = "world_quest_index";
        public const string NextIdWorldGameshopIndex = "world_gameshop_index";
        public const string NextIdWorldConquestIndex = "world_conquest_index";
        public const string NextIdWorldRespawnIndex = "world_respawn_index";

        public static readonly string[] WorldNextIdKeys =
        [
            NextIdWorldMapIndex,
            NextIdWorldItemIndex,
            NextIdWorldMonsterIndex,
            NextIdWorldNpcIndex,
            NextIdWorldQuestIndex,
            NextIdWorldGameshopIndex,
            NextIdWorldConquestIndex,
            NextIdWorldRespawnIndex,
        ];

        private sealed class ServerMetaValueRow
        {
            public string MetaValue { get; set; }
        }

        private sealed class NextIdRow
        {
            public string Name { get; set; }
            public long NextValue { get; set; }
        }

        private static void UpsertServerMeta(SqlSession session, string key, string value, long updatedUtcMs)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("key 不能为空。", nameof(key));

            if (updatedUtcMs <= 0)
                updatedUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            var sql = session.Dialect.BuildUpsert(
                tableName: "server_meta",
                insertColumns: ["meta_key", "meta_value", "updated_utc_ms"],
                keyColumns: ["meta_key"],
                updateColumns: ["meta_value", "updated_utc_ms"]);

            session.Execute(
                sql,
                new
                {
                    meta_key = key.Trim(),
                    meta_value = value ?? string.Empty,
                    updated_utc_ms = updatedUtcMs,
                });
        }

        private static long TryLoadServerMetaInt64(SqlSession session, string key)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("key 不能为空。", nameof(key));

            var rows = session.Query<ServerMetaValueRow>(
                "SELECT meta_value AS MetaValue FROM server_meta WHERE meta_key=@Key",
                new { Key = key.Trim() });

            if (rows.Count == 0 || rows[0] == null)
                return 0;

            var text = (rows[0].MetaValue ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(text))
                return 0;

            return long.TryParse(text, out var value) ? value : 0;
        }

        private static IReadOnlyDictionary<string, long> LoadNextIds(SqlSession session, IReadOnlyList<string> keys)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (keys == null || keys.Count == 0) return new Dictionary<string, long>(StringComparer.Ordinal);

            var rows = session.Query<NextIdRow>(
                "SELECT name AS Name, next_value AS NextValue FROM next_ids WHERE name IN @Names",
                new { Names = keys });

            if (rows.Count == 0) return new Dictionary<string, long>(StringComparer.Ordinal);

            var result = new Dictionary<string, long>(StringComparer.Ordinal);
            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                if (row == null) continue;
                if (string.IsNullOrWhiteSpace(row.Name)) continue;
                result[row.Name.Trim()] = row.NextValue;
            }

            return result;
        }

        private static void UpsertNextIds(SqlSession session, IReadOnlyDictionary<string, long> values, long nowMs)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (values == null || values.Count == 0) return;

            if (nowMs <= 0)
                nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            var sql = session.Dialect.BuildUpsert(
                tableName: "next_ids",
                insertColumns: ["name", "next_value", "updated_utc_ms"],
                keyColumns: ["name"],
                updateColumns: ["next_value", "updated_utc_ms"]);

            foreach (var pair in values)
            {
                if (string.IsNullOrWhiteSpace(pair.Key)) continue;

                session.Execute(
                    sql,
                    new
                    {
                        name = pair.Key.Trim(),
                        next_value = pair.Value,
                        updated_utc_ms = nowMs,
                    });
            }
        }

        public static bool HasWorldRelations(SqlSession session)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));

            try
            {
                var count = session.ExecuteScalar<long>("SELECT COUNT(1) FROM map_infos");
                return count > 0;
            }
            catch
            {
                return false;
            }
        }

        public static long TryGetRelationsEpochUtcMs(SqlSession session)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            return TryLoadServerMetaInt64(session, MetaKeyWorldRelationsEpochUtcMs);
        }

        public static SqlWorldRelationsSnapshot Capture(Envir envir)
        {
            if (envir == null) throw new ArgumentNullException(nameof(envir));

            var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var snapshot = new SqlWorldRelationsSnapshot { EpochUtcMs = nowMs };

            snapshot.NextIds[NextIdWorldMapIndex] = envir.MapIndex;
            snapshot.NextIds[NextIdWorldItemIndex] = envir.ItemIndex;
            snapshot.NextIds[NextIdWorldMonsterIndex] = envir.MonsterIndex;
            snapshot.NextIds[NextIdWorldNpcIndex] = envir.NPCIndex;
            snapshot.NextIds[NextIdWorldQuestIndex] = envir.QuestIndex;
            snapshot.NextIds[NextIdWorldGameshopIndex] = envir.GameshopIndex;
            snapshot.NextIds[NextIdWorldConquestIndex] = envir.ConquestIndex;
            snapshot.NextIds[NextIdWorldRespawnIndex] = envir.RespawnIndex;

            for (var i = 0; i < envir.MapInfoList.Count; i++)
            {
                var map = envir.MapInfoList[i];
                if (map == null) continue;

                snapshot.MapInfos.Add(new WorldMapInfoRow
                {
                    MapId = map.Index,
                    FileName = map.FileName ?? string.Empty,
                    Title = map.Title ?? string.Empty,
                    MiniMap = map.MiniMap,
                    BigMap = map.BigMap,
                    Music = map.Music,
                    Light = (int)map.Light,
                    MapDarkLight = map.MapDarkLight,
                    MineIndex = map.MineIndex,
                    NoTeleport = map.NoTeleport ? 1 : 0,
                    NoReconnect = map.NoReconnect ? 1 : 0,
                    NoReconnectMap = map.NoReconnectMap ?? string.Empty,
                    NoRandom = map.NoRandom ? 1 : 0,
                    NoEscape = map.NoEscape ? 1 : 0,
                    NoRecall = map.NoRecall ? 1 : 0,
                    NoDrug = map.NoDrug ? 1 : 0,
                    NoPosition = map.NoPosition ? 1 : 0,
                    NoThrowItem = map.NoThrowItem ? 1 : 0,
                    NoDropPlayer = map.NoDropPlayer ? 1 : 0,
                    NoDropMonster = map.NoDropMonster ? 1 : 0,
                    NoNames = map.NoNames ? 1 : 0,
                    Fight = map.Fight ? 1 : 0,
                    Fire = map.Fire ? 1 : 0,
                    FireDamage = map.FireDamage,
                    Lightning = map.Lightning ? 1 : 0,
                    LightningDamage = map.LightningDamage,
                    NoMount = map.NoMount ? 1 : 0,
                    NeedBridle = map.NeedBridle ? 1 : 0,
                    NoFight = map.NoFight ? 1 : 0,
                    NoTownTeleport = map.NoTownTeleport ? 1 : 0,
                    NoReincarnation = map.NoReincarnation ? 1 : 0,
                    WeatherParticles = (int)map.WeatherParticles,
                    UpdatedUtcMs = nowMs,
                });

                for (var j = 0; j < map.SafeZones.Count; j++)
                {
                    var zone = map.SafeZones[j];
                    if (zone == null) continue;
                    snapshot.MapSafeZones.Add(new WorldMapSafeZoneRow
                    {
                        MapId = map.Index,
                        SlotIndex = j,
                        X = zone.Location.X,
                        Y = zone.Location.Y,
                        ZoneSize = zone.Size,
                        StartPoint = zone.StartPoint ? 1 : 0,
                        UpdatedUtcMs = nowMs,
                    });
                }

                for (var j = 0; j < map.Respawns.Count; j++)
                {
                    var respawn = map.Respawns[j];
                    if (respawn == null) continue;

                    snapshot.MapRespawns.Add(new WorldMapRespawnRow
                    {
                        RespawnIndex = respawn.RespawnIndex,
                        MapId = map.Index,
                        MonsterIndex = respawn.MonsterIndex,
                        X = respawn.Location.X,
                        Y = respawn.Location.Y,
                        SpawnCount = respawn.Count,
                        Spread = respawn.Spread,
                        Delay = respawn.Delay,
                        RandomDelay = respawn.RandomDelay,
                        Direction = respawn.Direction,
                        RoutePath = respawn.RoutePath ?? string.Empty,
                        SaveRespawnTime = respawn.SaveRespawnTime ? 1 : 0,
                        RespawnTicks = respawn.RespawnTicks,
                        UpdatedUtcMs = nowMs,
                    });
                }

                for (var j = 0; j < map.Movements.Count; j++)
                {
                    var movement = map.Movements[j];
                    if (movement == null) continue;
                    snapshot.MapMovements.Add(new WorldMapMovementRow
                    {
                        MapId = map.Index,
                        SlotIndex = j,
                        DestinationMapId = movement.MapIndex,
                        SrcX = movement.Source.X,
                        SrcY = movement.Source.Y,
                        DstX = movement.Destination.X,
                        DstY = movement.Destination.Y,
                        NeedHole = movement.NeedHole ? 1 : 0,
                        NeedMove = movement.NeedMove ? 1 : 0,
                        ConquestIndex = movement.ConquestIndex,
                        ShowOnBigMap = movement.ShowOnBigMap ? 1 : 0,
                        Icon = movement.Icon,
                        UpdatedUtcMs = nowMs,
                    });
                }

                for (var j = 0; j < map.MineZones.Count; j++)
                {
                    var zone = map.MineZones[j];
                    if (zone == null) continue;
                    snapshot.MapMineZones.Add(new WorldMapMineZoneRow
                    {
                        MapId = map.Index,
                        SlotIndex = j,
                        X = zone.Location.X,
                        Y = zone.Location.Y,
                        ZoneSize = zone.Size,
                        Mine = zone.Mine,
                        UpdatedUtcMs = nowMs,
                    });
                }
            }

            for (var i = 0; i < envir.ItemInfoList.Count; i++)
            {
                var item = envir.ItemInfoList[i];
                if (item == null) continue;

                snapshot.ItemInfos.Add(new WorldItemInfoRow
                {
                    ItemId = item.Index,
                    Name = item.Name ?? string.Empty,
                    ItemType = (int)item.Type,
                    Grade = (int)item.Grade,
                    RequiredType = (int)item.RequiredType,
                    RequiredClass = (int)item.RequiredClass,
                    RequiredGender = (int)item.RequiredGender,
                    ItemSet = (int)item.Set,
                    Shape = item.Shape,
                    Weight = item.Weight,
                    Light = item.Light,
                    RequiredAmount = item.RequiredAmount,
                    Image = item.Image,
                    Durability = item.Durability,
                    Price = item.Price,
                    StackSize = item.StackSize,
                    StartItem = item.StartItem ? 1 : 0,
                    Effect = item.Effect,
                    NeedIdentify = item.NeedIdentify ? 1 : 0,
                    ShowGroupPickup = item.ShowGroupPickup ? 1 : 0,
                    GlobalDropNotify = item.GlobalDropNotify ? 1 : 0,
                    ClassBased = item.ClassBased ? 1 : 0,
                    LevelBased = item.LevelBased ? 1 : 0,
                    CanMine = item.CanMine ? 1 : 0,
                    Bind = (int)item.Bind,
                    UniqueMode = (int)item.Unique,
                    RandomStatsId = item.RandomStatsId,
                    CanFastRun = item.CanFastRun ? 1 : 0,
                    CanAwakening = item.CanAwakening ? 1 : 0,
                    Slots = item.Slots,
                    ToolTip = item.ToolTip,
                    UpdatedUtcMs = nowMs,
                });

                if (item.Stats != null && item.Stats.Values != null && item.Stats.Values.Count > 0)
                {
                    foreach (var pair in item.Stats.Values)
                    {
                        snapshot.ItemInfoStats.Add(new WorldItemInfoStatRow
                        {
                            ItemId = item.Index,
                            Stat = (int)pair.Key,
                            StatValue = pair.Value,
                            UpdatedUtcMs = nowMs,
                        });
                    }
                }
            }

            for (var i = 0; i < envir.MonsterInfoList.Count; i++)
            {
                var monster = envir.MonsterInfoList[i];
                if (monster == null) continue;

                snapshot.MonsterInfos.Add(new WorldMonsterInfoRow
                {
                    MonsterId = monster.Index,
                    Name = monster.Name ?? string.Empty,
                    Image = (int)monster.Image,
                    AI = monster.AI,
                    Effect = monster.Effect,
                    Level = monster.Level,
                    ViewRange = monster.ViewRange,
                    CoolEye = monster.CoolEye,
                    Light = monster.Light,
                    AttackSpeed = monster.AttackSpeed,
                    MoveSpeed = monster.MoveSpeed,
                    Experience = monster.Experience,
                    CanTame = monster.CanTame ? 1 : 0,
                    CanPush = monster.CanPush ? 1 : 0,
                    AutoRev = monster.AutoRev ? 1 : 0,
                    Undead = monster.Undead ? 1 : 0,
                    DropPath = monster.DropPath ?? string.Empty,
                    UpdatedUtcMs = nowMs,
                });

                if (monster.Stats != null && monster.Stats.Values != null && monster.Stats.Values.Count > 0)
                {
                    foreach (var pair in monster.Stats.Values)
                    {
                        snapshot.MonsterInfoStats.Add(new WorldMonsterInfoStatRow
                        {
                            MonsterId = monster.Index,
                            Stat = (int)pair.Key,
                            StatValue = pair.Value,
                            UpdatedUtcMs = nowMs,
                        });
                    }
                }
            }

            for (var i = 0; i < envir.NPCInfoList.Count; i++)
            {
                var npc = envir.NPCInfoList[i];
                if (npc == null) continue;

                snapshot.NpcInfos.Add(new WorldNpcInfoRow
                {
                    NpcId = npc.Index,
                    MapId = npc.MapIndex,
                    FileName = npc.FileName ?? string.Empty,
                    Name = npc.Name ?? string.Empty,
                    X = npc.Location.X,
                    Y = npc.Location.Y,
                    Rate = npc.Rate,
                    Image = npc.Image,
                    TimeVisible = npc.TimeVisible ? 1 : 0,
                    HourStart = npc.HourStart,
                    MinuteStart = npc.MinuteStart,
                    HourEnd = npc.HourEnd,
                    MinuteEnd = npc.MinuteEnd,
                    MinLev = npc.MinLev,
                    MaxLev = npc.MaxLev,
                    DayOfWeek = npc.DayofWeek ?? string.Empty,
                    ClassRequired = npc.ClassRequired ?? string.Empty,
                    Conquest = npc.Conquest,
                    FlagNeeded = npc.FlagNeeded,
                    ShowOnBigMap = npc.ShowOnBigMap ? 1 : 0,
                    BigMapIcon = npc.BigMapIcon,
                    CanTeleportTo = npc.CanTeleportTo ? 1 : 0,
                    ConquestVisible = npc.ConquestVisible ? 1 : 0,
                    UpdatedUtcMs = nowMs,
                });

                for (var j = 0; j < npc.CollectQuestIndexes.Count; j++)
                {
                    snapshot.NpcCollectQuests.Add(new WorldNpcQuestRow
                    {
                        NpcId = npc.Index,
                        SlotIndex = j,
                        QuestId = npc.CollectQuestIndexes[j],
                        UpdatedUtcMs = nowMs,
                    });
                }

                for (var j = 0; j < npc.FinishQuestIndexes.Count; j++)
                {
                    snapshot.NpcFinishQuests.Add(new WorldNpcQuestRow
                    {
                        NpcId = npc.Index,
                        SlotIndex = j,
                        QuestId = npc.FinishQuestIndexes[j],
                        UpdatedUtcMs = nowMs,
                    });
                }
            }

            for (var i = 0; i < envir.QuestInfoList.Count; i++)
            {
                var quest = envir.QuestInfoList[i];
                if (quest == null) continue;

                snapshot.QuestInfos.Add(new WorldQuestInfoRow
                {
                    QuestId = quest.Index,
                    Name = quest.Name ?? string.Empty,
                    QuestGroup = quest.Group ?? string.Empty,
                    FileName = quest.FileName ?? string.Empty,
                    RequiredMinLevel = quest.RequiredMinLevel,
                    RequiredMaxLevel = quest.RequiredMaxLevel,
                    RequiredQuest = quest.RequiredQuest,
                    RequiredClass = (int)quest.RequiredClass,
                    QuestType = (int)quest.Type,
                    GotoMessage = quest.GotoMessage ?? string.Empty,
                    KillMessage = quest.KillMessage ?? string.Empty,
                    ItemMessage = quest.ItemMessage ?? string.Empty,
                    FlagMessage = quest.FlagMessage ?? string.Empty,
                    TimeLimitSeconds = quest.TimeLimitInSeconds,
                    UpdatedUtcMs = nowMs,
                });
            }

            for (var i = 0; i < envir.MagicInfoList.Count; i++)
            {
                var magic = envir.MagicInfoList[i];
                if (magic == null) continue;

                snapshot.MagicInfos.Add(new WorldMagicInfoRow
                {
                    Spell = (int)magic.Spell,
                    Name = magic.Name ?? string.Empty,
                    BaseCost = magic.BaseCost,
                    LevelCost = magic.LevelCost,
                    Icon = magic.Icon,
                    Level1 = magic.Level1,
                    Level2 = magic.Level2,
                    Level3 = magic.Level3,
                    Need1 = magic.Need1,
                    Need2 = magic.Need2,
                    Need3 = magic.Need3,
                    DelayBase = magic.DelayBase,
                    DelayReduction = magic.DelayReduction,
                    PowerBase = magic.PowerBase,
                    PowerBonus = magic.PowerBonus,
                    MPowerBase = magic.MPowerBase,
                    MPowerBonus = magic.MPowerBonus,
                    MagicRange = magic.Range,
                    MultiplierBase = magic.MultiplierBase,
                    MultiplierBonus = magic.MultiplierBonus,
                    UpdatedUtcMs = nowMs,
                });
            }

            for (var i = 0; i < envir.GameShopList.Count; i++)
            {
                var item = envir.GameShopList[i];
                if (item == null) continue;

                snapshot.GameShopItems.Add(new WorldGameShopItemRow
                {
                    GameshopItemId = item.GIndex,
                    ItemId = item.ItemIndex,
                    GoldPrice = item.GoldPrice,
                    CreditPrice = item.CreditPrice,
                    Count = item.Count,
                    ClassMask = item.Class ?? string.Empty,
                    Category = item.Category ?? string.Empty,
                    Stock = item.Stock,
                    IStock = item.iStock ? 1 : 0,
                    Deal = item.Deal ? 1 : 0,
                    TopItem = item.TopItem ? 1 : 0,
                    DateBinary = item.Date.ToBinary(),
                    CanBuyGold = item.CanBuyGold ? 1 : 0,
                    CanBuyCredit = item.CanBuyCredit ? 1 : 0,
                    UpdatedUtcMs = nowMs,
                });
            }

            if (envir.DragonInfo != null)
            {
                var dragon = envir.DragonInfo;
                snapshot.DragonInfo = new WorldDragonInfoRow
                {
                    DragonId = 1,
                    Enabled = dragon.Enabled ? 1 : 0,
                    MapFileName = dragon.MapFileName ?? string.Empty,
                    MonsterName = dragon.MonsterName ?? string.Empty,
                    BodyName = dragon.BodyName ?? string.Empty,
                    LocationX = dragon.Location.X,
                    LocationY = dragon.Location.Y,
                    DropAreaTopX = dragon.DropAreaTop.X,
                    DropAreaTopY = dragon.DropAreaTop.Y,
                    DropAreaBottomX = dragon.DropAreaBottom.X,
                    DropAreaBottomY = dragon.DropAreaBottom.Y,
                    UpdatedUtcMs = nowMs,
                };

                if (dragon.Exps != null && dragon.Exps.Length > 0)
                {
                    for (var i = 0; i < dragon.Exps.Length; i++)
                    {
                        snapshot.DragonExps.Add(new WorldDragonExpRow
                        {
                            DragonId = 1,
                            Level = i + 1,
                            Exp = dragon.Exps[i],
                            UpdatedUtcMs = nowMs,
                        });
                    }
                }
            }

            for (var i = 0; i < envir.ConquestInfoList.Count; i++)
            {
                var conquest = envir.ConquestInfoList[i];
                if (conquest == null) continue;

                snapshot.Conquests.Add(new WorldConquestRow
                {
                    ConquestId = conquest.Index,
                    FullMap = conquest.FullMap ? 1 : 0,
                    LocationX = conquest.Location.X,
                    LocationY = conquest.Location.Y,
                    Size = conquest.Size,
                    Name = conquest.Name ?? string.Empty,
                    MapId = conquest.MapIndex,
                    PalaceId = conquest.PalaceIndex,
                    GuardIndex = conquest.GuardIndex,
                    GateIndex = conquest.GateIndex,
                    WallIndex = conquest.WallIndex,
                    SiegeIndex = conquest.SiegeIndex,
                    FlagIndex = conquest.FlagIndex,
                    StartHour = conquest.StartHour,
                    WarLength = conquest.WarLength,
                    ConquestType = (int)conquest.Type,
                    ConquestGame = (int)conquest.Game,
                    Monday = conquest.Monday ? 1 : 0,
                    Tuesday = conquest.Tuesday ? 1 : 0,
                    Wednesday = conquest.Wednesday ? 1 : 0,
                    Thursday = conquest.Thursday ? 1 : 0,
                    Friday = conquest.Friday ? 1 : 0,
                    Saturday = conquest.Saturday ? 1 : 0,
                    Sunday = conquest.Sunday ? 1 : 0,
                    KingLocationX = conquest.KingLocation.X,
                    KingLocationY = conquest.KingLocation.Y,
                    KingSize = conquest.KingSize,
                    ControlPointIndex = conquest.ControlPointIndex,
                    UpdatedUtcMs = nowMs,
                });

                for (var j = 0; j < conquest.ExtraMaps.Count; j++)
                {
                    snapshot.ConquestExtraMaps.Add(new WorldConquestExtraMapRow
                    {
                        ConquestId = conquest.Index,
                        SlotIndex = j,
                        MapId = conquest.ExtraMaps[j],
                        UpdatedUtcMs = nowMs,
                    });
                }

                for (var j = 0; j < conquest.ConquestGuards.Count; j++)
                {
                    var guard = conquest.ConquestGuards[j];
                    if (guard == null) continue;
                    snapshot.ConquestGuards.Add(new WorldConquestGuardRow
                    {
                        ConquestId = conquest.Index,
                        GuardId = guard.Index,
                        LocationX = guard.Location.X,
                        LocationY = guard.Location.Y,
                        MobIndex = guard.MobIndex,
                        Name = guard.Name ?? string.Empty,
                        RepairCost = guard.RepairCost,
                        UpdatedUtcMs = nowMs,
                    });
                }

                for (var j = 0; j < conquest.ConquestGates.Count; j++)
                {
                    var gate = conquest.ConquestGates[j];
                    if (gate == null) continue;
                    snapshot.ConquestGates.Add(new WorldConquestGateRow
                    {
                        ConquestId = conquest.Index,
                        GateId = gate.Index,
                        LocationX = gate.Location.X,
                        LocationY = gate.Location.Y,
                        MobIndex = gate.MobIndex,
                        Name = gate.Name ?? string.Empty,
                        RepairCost = gate.RepairCost,
                        UpdatedUtcMs = nowMs,
                    });
                }

                for (var j = 0; j < conquest.ConquestWalls.Count; j++)
                {
                    var wall = conquest.ConquestWalls[j];
                    if (wall == null) continue;
                    snapshot.ConquestWalls.Add(new WorldConquestWallRow
                    {
                        ConquestId = conquest.Index,
                        WallId = wall.Index,
                        LocationX = wall.Location.X,
                        LocationY = wall.Location.Y,
                        MobIndex = wall.MobIndex,
                        Name = wall.Name ?? string.Empty,
                        RepairCost = wall.RepairCost,
                        UpdatedUtcMs = nowMs,
                    });
                }

                for (var j = 0; j < conquest.ConquestSieges.Count; j++)
                {
                    var siege = conquest.ConquestSieges[j];
                    if (siege == null) continue;
                    snapshot.ConquestSieges.Add(new WorldConquestSiegeRow
                    {
                        ConquestId = conquest.Index,
                        SiegeId = siege.Index,
                        LocationX = siege.Location.X,
                        LocationY = siege.Location.Y,
                        MobIndex = siege.MobIndex,
                        Name = siege.Name ?? string.Empty,
                        RepairCost = siege.RepairCost,
                        UpdatedUtcMs = nowMs,
                    });
                }

                for (var j = 0; j < conquest.ConquestFlags.Count; j++)
                {
                    var flag = conquest.ConquestFlags[j];
                    if (flag == null) continue;
                    snapshot.ConquestFlags.Add(new WorldConquestFlagRow
                    {
                        ConquestId = conquest.Index,
                        FlagId = flag.Index,
                        LocationX = flag.Location.X,
                        LocationY = flag.Location.Y,
                        Name = flag.Name ?? string.Empty,
                        FileName = flag.FileName ?? string.Empty,
                        UpdatedUtcMs = nowMs,
                    });
                }

                for (var j = 0; j < conquest.ControlPoints.Count; j++)
                {
                    var point = conquest.ControlPoints[j];
                    if (point == null) continue;
                    snapshot.ConquestControlPoints.Add(new WorldConquestControlPointRow
                    {
                        ConquestId = conquest.Index,
                        ControlPointId = point.Index,
                        LocationX = point.Location.X,
                        LocationY = point.Location.Y,
                        Name = point.Name ?? string.Empty,
                        FileName = point.FileName ?? string.Empty,
                        UpdatedUtcMs = nowMs,
                    });
                }
            }

            if (envir.RespawnTick != null)
            {
                snapshot.RespawnTimerState = new WorldRespawnTimerStateRow
                {
                    TimerId = 1,
                    BaseSpawnRate = envir.RespawnTick.BaseSpawnRate,
                    CurrentTickCounter = unchecked((long)envir.RespawnTick.CurrentTickcounter),
                    UpdatedUtcMs = nowMs,
                };

                if (envir.RespawnTick.Respawn != null)
                {
                    for (var i = 0; i < envir.RespawnTick.Respawn.Count; i++)
                    {
                        var option = envir.RespawnTick.Respawn[i];
                        if (option == null) continue;

                        snapshot.RespawnTickOptions.Add(new WorldRespawnTickOptionRow
                        {
                            TimerId = 1,
                            SlotIndex = i,
                            UserCount = option.UserCount,
                            DelayLoss = option.DelayLoss,
                            UpdatedUtcMs = nowMs,
                        });
                    }
                }
            }

            return snapshot;
        }

        public static void ReplaceAll(SqlSession session, SqlWorldRelationsSnapshot snapshot)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));

            var nowMs = snapshot.EpochUtcMs <= 0 ? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() : snapshot.EpochUtcMs;

            // 清空旧数据（无外键约束时顺序无所谓，这里按“子表→主表”排列便于理解）。
            session.Execute("DELETE FROM map_safe_zones");
            session.Execute("DELETE FROM map_respawns");
            session.Execute("DELETE FROM map_movements");
            session.Execute("DELETE FROM map_mine_zones");
            session.Execute("DELETE FROM map_infos");

            session.Execute("DELETE FROM item_info_stats");
            session.Execute("DELETE FROM item_infos");

            session.Execute("DELETE FROM monster_info_stats");
            session.Execute("DELETE FROM monster_infos");

            session.Execute("DELETE FROM npc_collect_quests");
            session.Execute("DELETE FROM npc_finish_quests");
            session.Execute("DELETE FROM npc_infos");

            session.Execute("DELETE FROM quest_infos");
            session.Execute("DELETE FROM magic_infos");

            session.Execute("DELETE FROM gameshop_items");

            session.Execute("DELETE FROM dragon_exps");
            session.Execute("DELETE FROM dragon_info");

            session.Execute("DELETE FROM conquest_extra_maps");
            session.Execute("DELETE FROM conquest_guards");
            session.Execute("DELETE FROM conquest_gates");
            session.Execute("DELETE FROM conquest_walls");
            session.Execute("DELETE FROM conquest_sieges");
            session.Execute("DELETE FROM conquest_flags");
            session.Execute("DELETE FROM conquest_control_points");
            session.Execute("DELETE FROM conquests");

            session.Execute("DELETE FROM respawn_tick_options");
            session.Execute("DELETE FROM respawn_timer_state");

            if (snapshot.MapInfos.Count > 0)
            {
                session.Execute(
                    "INSERT INTO map_infos (" +
                    "map_id, file_name, title, mini_map, big_map, music, light, map_dark_light, mine_index, " +
                    "no_teleport, no_reconnect, no_reconnect_map, no_random, no_escape, no_recall, no_drug, no_position, " +
                    "no_throw_item, no_drop_player, no_drop_monster, no_names, fight, fire, fire_damage, lightning, lightning_damage, " +
                    "no_mount, need_bridle, no_fight, no_town_teleport, no_reincarnation, weather_particles, updated_utc_ms" +
                    ") VALUES (" +
                    "@MapId, @FileName, @Title, @MiniMap, @BigMap, @Music, @Light, @MapDarkLight, @MineIndex, " +
                    "@NoTeleport, @NoReconnect, @NoReconnectMap, @NoRandom, @NoEscape, @NoRecall, @NoDrug, @NoPosition, " +
                    "@NoThrowItem, @NoDropPlayer, @NoDropMonster, @NoNames, @Fight, @Fire, @FireDamage, @Lightning, @LightningDamage, " +
                    "@NoMount, @NeedBridle, @NoFight, @NoTownTeleport, @NoReincarnation, @WeatherParticles, @UpdatedUtcMs" +
                    ")",
                    snapshot.MapInfos);
            }

            if (snapshot.MapSafeZones.Count > 0)
            {
                session.Execute(
                    "INSERT INTO map_safe_zones (map_id, slot_index, x, y, zone_size, start_point, updated_utc_ms) VALUES " +
                    "(@MapId, @SlotIndex, @X, @Y, @ZoneSize, @StartPoint, @UpdatedUtcMs)",
                    snapshot.MapSafeZones);
            }

            if (snapshot.MapRespawns.Count > 0)
            {
                session.Execute(
                    "INSERT INTO map_respawns (" +
                    "respawn_index, map_id, monster_index, x, y, spawn_count, spread, delay, random_delay, direction, route_path, save_respawn_time, respawn_ticks, updated_utc_ms" +
                    ") VALUES (" +
                    "@RespawnIndex, @MapId, @MonsterIndex, @X, @Y, @SpawnCount, @Spread, @Delay, @RandomDelay, @Direction, @RoutePath, @SaveRespawnTime, @RespawnTicks, @UpdatedUtcMs" +
                    ")",
                    snapshot.MapRespawns);
            }

            if (snapshot.MapMovements.Count > 0)
            {
                session.Execute(
                    "INSERT INTO map_movements (" +
                    "map_id, slot_index, destination_map_id, src_x, src_y, dst_x, dst_y, need_hole, need_move, conquest_index, show_on_big_map, icon, updated_utc_ms" +
                    ") VALUES (" +
                    "@MapId, @SlotIndex, @DestinationMapId, @SrcX, @SrcY, @DstX, @DstY, @NeedHole, @NeedMove, @ConquestIndex, @ShowOnBigMap, @Icon, @UpdatedUtcMs" +
                    ")",
                    snapshot.MapMovements);
            }

            if (snapshot.MapMineZones.Count > 0)
            {
                session.Execute(
                    "INSERT INTO map_mine_zones (map_id, slot_index, x, y, zone_size, mine, updated_utc_ms) VALUES " +
                    "(@MapId, @SlotIndex, @X, @Y, @ZoneSize, @Mine, @UpdatedUtcMs)",
                    snapshot.MapMineZones);
            }

            if (snapshot.ItemInfos.Count > 0)
            {
                session.Execute(
                    "INSERT INTO item_infos (" +
                    "item_id, name, item_type, grade, required_type, required_class, required_gender, item_set, " +
                    "shape, weight, light, required_amount, image, durability, price, stack_size, start_item, effect, " +
                    "need_identify, show_group_pickup, global_drop_notify, class_based, level_based, can_mine, bind, unique_mode, random_stats_id, " +
                    "can_fast_run, can_awakening, slots, tool_tip, updated_utc_ms" +
                    ") VALUES (" +
                    "@ItemId, @Name, @ItemType, @Grade, @RequiredType, @RequiredClass, @RequiredGender, @ItemSet, " +
                    "@Shape, @Weight, @Light, @RequiredAmount, @Image, @Durability, @Price, @StackSize, @StartItem, @Effect, " +
                    "@NeedIdentify, @ShowGroupPickup, @GlobalDropNotify, @ClassBased, @LevelBased, @CanMine, @Bind, @UniqueMode, @RandomStatsId, " +
                    "@CanFastRun, @CanAwakening, @Slots, @ToolTip, @UpdatedUtcMs" +
                    ")",
                    snapshot.ItemInfos);
            }

            if (snapshot.ItemInfoStats.Count > 0)
            {
                session.Execute(
                    "INSERT INTO item_info_stats (item_id, stat, stat_value, updated_utc_ms) VALUES (@ItemId, @Stat, @StatValue, @UpdatedUtcMs)",
                    snapshot.ItemInfoStats);
            }

            if (snapshot.MonsterInfos.Count > 0)
            {
                session.Execute(
                    "INSERT INTO monster_infos (" +
                    "monster_id, name, image, ai, effect, level, view_range, cool_eye, light, attack_speed, move_speed, experience, " +
                    "can_tame, can_push, auto_rev, undead, drop_path, updated_utc_ms" +
                    ") VALUES (" +
                    "@MonsterId, @Name, @Image, @AI, @Effect, @Level, @ViewRange, @CoolEye, @Light, @AttackSpeed, @MoveSpeed, @Experience, " +
                    "@CanTame, @CanPush, @AutoRev, @Undead, @DropPath, @UpdatedUtcMs" +
                    ")",
                    snapshot.MonsterInfos);
            }

            if (snapshot.MonsterInfoStats.Count > 0)
            {
                session.Execute(
                    "INSERT INTO monster_info_stats (monster_id, stat, stat_value, updated_utc_ms) VALUES (@MonsterId, @Stat, @StatValue, @UpdatedUtcMs)",
                    snapshot.MonsterInfoStats);
            }

            if (snapshot.NpcInfos.Count > 0)
            {
                session.Execute(
                    "INSERT INTO npc_infos (" +
                    "npc_id, map_id, file_name, name, x, y, rate, image, time_visible, hour_start, minute_start, hour_end, minute_end, " +
                    "min_lev, max_lev, day_of_week, class_required, conquest, flag_needed, show_on_big_map, big_map_icon, can_teleport_to, conquest_visible, updated_utc_ms" +
                    ") VALUES (" +
                    "@NpcId, @MapId, @FileName, @Name, @X, @Y, @Rate, @Image, @TimeVisible, @HourStart, @MinuteStart, @HourEnd, @MinuteEnd, " +
                    "@MinLev, @MaxLev, @DayOfWeek, @ClassRequired, @Conquest, @FlagNeeded, @ShowOnBigMap, @BigMapIcon, @CanTeleportTo, @ConquestVisible, @UpdatedUtcMs" +
                    ")",
                    snapshot.NpcInfos);
            }

            if (snapshot.NpcCollectQuests.Count > 0)
            {
                session.Execute(
                    "INSERT INTO npc_collect_quests (npc_id, slot_index, quest_id, updated_utc_ms) VALUES (@NpcId, @SlotIndex, @QuestId, @UpdatedUtcMs)",
                    snapshot.NpcCollectQuests);
            }

            if (snapshot.NpcFinishQuests.Count > 0)
            {
                session.Execute(
                    "INSERT INTO npc_finish_quests (npc_id, slot_index, quest_id, updated_utc_ms) VALUES (@NpcId, @SlotIndex, @QuestId, @UpdatedUtcMs)",
                    snapshot.NpcFinishQuests);
            }

            if (snapshot.QuestInfos.Count > 0)
            {
                session.Execute(
                    "INSERT INTO quest_infos (" +
                    "quest_id, name, quest_group, file_name, required_min_level, required_max_level, required_quest, required_class, quest_type, " +
                    "goto_message, kill_message, item_message, flag_message, time_limit_seconds, updated_utc_ms" +
                    ") VALUES (" +
                    "@QuestId, @Name, @QuestGroup, @FileName, @RequiredMinLevel, @RequiredMaxLevel, @RequiredQuest, @RequiredClass, @QuestType, " +
                    "@GotoMessage, @KillMessage, @ItemMessage, @FlagMessage, @TimeLimitSeconds, @UpdatedUtcMs" +
                    ")",
                    snapshot.QuestInfos);
            }

            if (snapshot.MagicInfos.Count > 0)
            {
                session.Execute(
                    "INSERT INTO magic_infos (" +
                    "spell, name, base_cost, level_cost, icon, level1, level2, level3, need1, need2, need3, delay_base, delay_reduction, " +
                    "power_base, power_bonus, mpower_base, mpower_bonus, magic_range, multiplier_base, multiplier_bonus, updated_utc_ms" +
                    ") VALUES (" +
                    "@Spell, @Name, @BaseCost, @LevelCost, @Icon, @Level1, @Level2, @Level3, @Need1, @Need2, @Need3, @DelayBase, @DelayReduction, " +
                    "@PowerBase, @PowerBonus, @MPowerBase, @MPowerBonus, @MagicRange, @MultiplierBase, @MultiplierBonus, @UpdatedUtcMs" +
                    ")",
                    snapshot.MagicInfos);
            }

            if (snapshot.GameShopItems.Count > 0)
            {
                session.Execute(
                    "INSERT INTO gameshop_items (" +
                    "gameshop_item_id, item_id, gold_price, credit_price, count, class_mask, category, stock, i_stock, deal, top_item, date_binary, " +
                    "can_buy_gold, can_buy_credit, updated_utc_ms" +
                    ") VALUES (" +
                    "@GameshopItemId, @ItemId, @GoldPrice, @CreditPrice, @Count, @ClassMask, @Category, @Stock, @IStock, @Deal, @TopItem, @DateBinary, " +
                    "@CanBuyGold, @CanBuyCredit, @UpdatedUtcMs" +
                    ")",
                    snapshot.GameShopItems);
            }

            if (snapshot.DragonInfo != null)
            {
                session.Execute(
                    "INSERT INTO dragon_info (" +
                    "dragon_id, enabled, map_file_name, monster_name, body_name, location_x, location_y, drop_area_top_x, drop_area_top_y, drop_area_bottom_x, drop_area_bottom_y, updated_utc_ms" +
                    ") VALUES (" +
                    "@DragonId, @Enabled, @MapFileName, @MonsterName, @BodyName, @LocationX, @LocationY, @DropAreaTopX, @DropAreaTopY, @DropAreaBottomX, @DropAreaBottomY, @UpdatedUtcMs" +
                    ")",
                    snapshot.DragonInfo);
            }

            if (snapshot.DragonExps.Count > 0)
            {
                session.Execute(
                    "INSERT INTO dragon_exps (dragon_id, level, exp, updated_utc_ms) VALUES (@DragonId, @Level, @Exp, @UpdatedUtcMs)",
                    snapshot.DragonExps);
            }

            if (snapshot.Conquests.Count > 0)
            {
                session.Execute(
                    "INSERT INTO conquests (" +
                    "conquest_id, full_map, location_x, location_y, size, name, map_id, palace_id, guard_index, gate_index, wall_index, siege_index, flag_index, " +
                    "start_hour, war_length, conquest_type, conquest_game, monday, tuesday, wednesday, thursday, friday, saturday, sunday, king_location_x, king_location_y, king_size, control_point_index, updated_utc_ms" +
                    ") VALUES (" +
                    "@ConquestId, @FullMap, @LocationX, @LocationY, @Size, @Name, @MapId, @PalaceId, @GuardIndex, @GateIndex, @WallIndex, @SiegeIndex, @FlagIndex, " +
                    "@StartHour, @WarLength, @ConquestType, @ConquestGame, @Monday, @Tuesday, @Wednesday, @Thursday, @Friday, @Saturday, @Sunday, @KingLocationX, @KingLocationY, @KingSize, @ControlPointIndex, @UpdatedUtcMs" +
                    ")",
                    snapshot.Conquests);
            }

            if (snapshot.ConquestExtraMaps.Count > 0)
            {
                session.Execute(
                    "INSERT INTO conquest_extra_maps (conquest_id, slot_index, map_id, updated_utc_ms) VALUES (@ConquestId, @SlotIndex, @MapId, @UpdatedUtcMs)",
                    snapshot.ConquestExtraMaps);
            }

            if (snapshot.ConquestGuards.Count > 0)
            {
                session.Execute(
                    "INSERT INTO conquest_guards (conquest_id, guard_id, location_x, location_y, mob_index, name, repair_cost, updated_utc_ms) VALUES " +
                    "(@ConquestId, @GuardId, @LocationX, @LocationY, @MobIndex, @Name, @RepairCost, @UpdatedUtcMs)",
                    snapshot.ConquestGuards);
            }

            if (snapshot.ConquestGates.Count > 0)
            {
                session.Execute(
                    "INSERT INTO conquest_gates (conquest_id, gate_id, location_x, location_y, mob_index, name, repair_cost, updated_utc_ms) VALUES " +
                    "(@ConquestId, @GateId, @LocationX, @LocationY, @MobIndex, @Name, @RepairCost, @UpdatedUtcMs)",
                    snapshot.ConquestGates);
            }

            if (snapshot.ConquestWalls.Count > 0)
            {
                session.Execute(
                    "INSERT INTO conquest_walls (conquest_id, wall_id, location_x, location_y, mob_index, name, repair_cost, updated_utc_ms) VALUES " +
                    "(@ConquestId, @WallId, @LocationX, @LocationY, @MobIndex, @Name, @RepairCost, @UpdatedUtcMs)",
                    snapshot.ConquestWalls);
            }

            if (snapshot.ConquestSieges.Count > 0)
            {
                session.Execute(
                    "INSERT INTO conquest_sieges (conquest_id, siege_id, location_x, location_y, mob_index, name, repair_cost, updated_utc_ms) VALUES " +
                    "(@ConquestId, @SiegeId, @LocationX, @LocationY, @MobIndex, @Name, @RepairCost, @UpdatedUtcMs)",
                    snapshot.ConquestSieges);
            }

            if (snapshot.ConquestFlags.Count > 0)
            {
                session.Execute(
                    "INSERT INTO conquest_flags (conquest_id, flag_id, location_x, location_y, name, file_name, updated_utc_ms) VALUES " +
                    "(@ConquestId, @FlagId, @LocationX, @LocationY, @Name, @FileName, @UpdatedUtcMs)",
                    snapshot.ConquestFlags);
            }

            if (snapshot.ConquestControlPoints.Count > 0)
            {
                session.Execute(
                    "INSERT INTO conquest_control_points (conquest_id, control_point_id, location_x, location_y, name, file_name, updated_utc_ms) VALUES " +
                    "(@ConquestId, @ControlPointId, @LocationX, @LocationY, @Name, @FileName, @UpdatedUtcMs)",
                    snapshot.ConquestControlPoints);
            }

            if (snapshot.RespawnTimerState != null)
            {
                session.Execute(
                    "INSERT INTO respawn_timer_state (timer_id, base_spawn_rate, current_tick_counter, updated_utc_ms) VALUES " +
                    "(@TimerId, @BaseSpawnRate, @CurrentTickCounter, @UpdatedUtcMs)",
                    snapshot.RespawnTimerState);
            }

            if (snapshot.RespawnTickOptions.Count > 0)
            {
                session.Execute(
                    "INSERT INTO respawn_tick_options (timer_id, slot_index, user_count, delay_loss, updated_utc_ms) VALUES " +
                    "(@TimerId, @SlotIndex, @UserCount, @DelayLoss, @UpdatedUtcMs)",
                    snapshot.RespawnTickOptions);
            }

            UpsertNextIds(session, snapshot.NextIds, nowMs);
            UpsertServerMeta(session, MetaKeyWorldRelationsEpochUtcMs, nowMs.ToString(), nowMs);
        }
    }
}
