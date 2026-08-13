using System.Drawing;
using System.Text.RegularExpressions;
using Server.Diagnostics;
using Server.MirDatabase;

namespace Server.Authoring;

public enum MapContentLayer
{
    Map,
    Exit,
    Npc,
    Respawn,
    MineZone,
}

public sealed record MapContentTarget(
    MapContentLayer Layer,
    int? ListIndex,
    int? EntityIndex,
    Point Location,
    string Source,
    string Label);

/// <summary>
/// 把地图事实对象投影为只读叠层目标，并解析 LEG-02 稳定 Source 到拥有记录。
/// </summary>
public static partial class MapContentNavigation
{
    public static IReadOnlyList<MapContentTarget> BuildTargets(
        MapInfo map,
        IEnumerable<NPCInfo> npcs,
        MapContentDraft draft = null)
    {
        ArgumentNullException.ThrowIfNull(map);
        npcs ??= Array.Empty<NPCInfo>();

        var result = new List<MapContentTarget>();
        result.Add(new MapContentTarget(
            MapContentLayer.Map, null, map.Index, Point.Empty,
            $"MapInfo[{map.Index}]", $"地图 {map.Title} [{map.FileName}]"));
        for (int index = 0; index < map.Movements.Count; index++)
        {
            MovementInfo item = map.Movements[index];
            result.Add(new MapContentTarget(
                MapContentLayer.Exit, index, null, item.Source,
                $"MapInfo[{map.Index}].Movements[{index}]",
                $"出口 {index + 1} → 地图 {item.MapIndex} ({item.Destination.X},{item.Destination.Y})"));
        }

        foreach (NPCInfo item in npcs.Where(item => item.MapIndex == map.Index).OrderBy(item => item.Index))
        {
            result.Add(new MapContentTarget(
                MapContentLayer.Npc, null, item.Index, item.Location,
                $"NPCInfo[{item.Index}:{item.FileName}]",
                $"NPC {item.Name} [{item.FileName}]"));
        }

        IReadOnlyList<MapRespawnDraft> respawns = draft?.Respawns ?? map.Respawns
            .Select(item => new MapRespawnDraft(
                item.MonsterIndex, item.Location, item.Count, item.Spread, item.Delay,
                item.Direction, item.RoutePath ?? string.Empty, item.RandomDelay,
                item.RespawnIndex, item.SaveRespawnTime, item.RespawnTicks))
            .ToArray();
        for (int index = 0; index < respawns.Count; index++)
        {
            MapRespawnDraft item = respawns[index];
            result.Add(new MapContentTarget(
                MapContentLayer.Respawn, index, null, item.Location,
                $"MapInfo[{map.Index}].Respawns[{index}]",
                $"刷怪 {index + 1} / 怪物 {item.MonsterIndex}"));
        }

        IReadOnlyList<MapMineZoneDraft> mineZones = draft?.MineZones ?? map.MineZones
            .Select(item => new MapMineZoneDraft(item.Mine, item.Location, item.Size))
            .ToArray();
        for (int index = 0; index < mineZones.Count; index++)
        {
            MapMineZoneDraft item = mineZones[index];
            result.Add(new MapContentTarget(
                MapContentLayer.MineZone, index, null, item.Location,
                $"MapInfo[{map.Index}].MineZones[{index}]",
                $"矿区 {index + 1} / 类型 {item.Mine}"));
        }

        return result;
    }

    public static MapContentTarget FindTarget(
        ProjectPreflightDiagnostic diagnostic,
        MapInfo map,
        IEnumerable<NPCInfo> npcs)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        return FindTarget(diagnostic.Source, BuildTargets(map, npcs));
    }

    public static MapContentTarget FindTarget(string source, IEnumerable<MapContentTarget> targets)
    {
        if (string.IsNullOrWhiteSpace(source))
            return null;

        MapContentTarget exact = targets?.FirstOrDefault(item =>
            string.Equals(item.Source, source, StringComparison.Ordinal));
        if (exact is not null)
            return exact;

        Match npc = NpcSource().Match(source);
        if (npc.Success && int.TryParse(npc.Groups[1].Value, out int npcIndex))
            return targets?.FirstOrDefault(item => item.Layer == MapContentLayer.Npc && item.EntityIndex == npcIndex);

        return null;
    }

    public static MapInfo CreatePreflightMap(MapInfo source, MapContentDraft draft)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(draft);
        var result = new MapInfo
        {
            Index = source.Index,
            FileName = source.FileName,
            Title = source.Title,
        };
        result.Movements.AddRange(source.Movements);
        result.Respawns.AddRange(draft.Respawns.Select(item => new RespawnInfo
        {
            MonsterIndex = item.MonsterIndex,
            Location = item.Location,
            Count = item.Count,
            Spread = item.Spread,
            Delay = item.Delay,
            Direction = item.Direction,
            RoutePath = item.RoutePath,
            RandomDelay = item.RandomDelay,
            RespawnIndex = item.RespawnIndex,
            SaveRespawnTime = item.SaveRespawnTime,
            RespawnTicks = item.RespawnTicks,
        }));
        return result;
    }

    [GeneratedRegex(@"^NPCInfo\[(\d+)(?::[^\]]*)?\]", RegexOptions.CultureInvariant)]
    private static partial Regex NpcSource();
}
