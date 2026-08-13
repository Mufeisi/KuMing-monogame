using System.Drawing;
using Server.Authoring;
using Server.Diagnostics;
using Server.MirDatabase;
using Xunit;

namespace Base05.Tests;

public sealed class MapContentNavigationTests
{
    [Fact]
    public void 四类事实记录投影为稳定叠层目标()
    {
        var map = CreateMap();
        NPCInfo[] npcs =
        [
            new NPCInfo { Index = 7, MapIndex = 10, FileName = "merchant", Name = "商人", Location = new Point(8, 9) },
            new NPCInfo { Index = 8, MapIndex = 11, FileName = "other", Location = new Point(1, 1) },
        ];

        IReadOnlyList<MapContentTarget> targets = MapContentNavigation.BuildTargets(map, npcs);

        Assert.Collection(targets,
            item => Assert.Equal(MapContentLayer.Map, item.Layer),
            item => AssertTarget(item, MapContentLayer.Exit, 0, null, new Point(2, 3), "MapInfo[10].Movements[0]"),
            item => AssertTarget(item, MapContentLayer.Npc, null, 7, new Point(8, 9), "NPCInfo[7:merchant]"),
            item => AssertTarget(item, MapContentLayer.Respawn, 0, null, new Point(4, 5), "MapInfo[10].Respawns[0]"),
            item => AssertTarget(item, MapContentLayer.MineZone, 0, null, new Point(6, 7), "MapInfo[10].MineZones[0]"));
    }

    [Fact]
    public void LEG02稳定来源可定位到拥有记录()
    {
        var map = CreateMap();
        NPCInfo npc = new() { Index = 7, MapIndex = 10, FileName = "merchant", Location = new Point(8, 9) };

        MapContentTarget respawn = MapContentNavigation.FindTarget(
            new ProjectPreflightDiagnostic("LEG02-SPAWN-001", ProjectPreflightSeverity.Error,
                "MapInfo[10].Respawns[0]", "怪物不存在"), map, [npc]);
        MapContentTarget npcTarget = MapContentNavigation.FindTarget(
            new ProjectPreflightDiagnostic("LEG02-NPC-003", ProjectPreflightSeverity.Warning,
                "NPCInfo[7:merchant]", "脚本不存在"), map, [npc]);

        Assert.Equal(MapContentLayer.Respawn, respawn.Layer);
        Assert.Equal(new Point(4, 5), respawn.Location);
        Assert.Equal(MapContentLayer.Npc, npcTarget.Layer);
        Assert.Equal(7, npcTarget.EntityIndex);
        Assert.Null(MapContentNavigation.FindTarget("recipe/item", MapContentNavigation.BuildTargets(map, [npc])));
    }

    [Fact]
    public void LEG02真实预检报告可反向定位地图出口NPC与刷怪()
    {
        var map = CreateMap();
        map.Movements[0].MapIndex = 999;
        NPCInfo npc = new() { Index = 7, MapIndex = 10, FileName = "merchant", Location = new Point(8, 29) };

        ProjectPreflightReport report = ProjectSemanticPreflight.ValidateMapContent(new ProjectPreflightRequest
        {
            Maps = [map, new MapInfo { Index = 11 }],
            Monsters = [],
            Npcs = [npc],
            MapBounds = [new ProjectMapBounds(10, 20, 20)],
        }, 10);
        ProjectPreflightDiagnostic[] diagnostics = report.Diagnostics
            .Where(item => MapContentNavigation.FindTarget(item, map, [npc]) != null)
            .ToArray();

        Assert.Contains(diagnostics, item => item.Code == "LEG02-MAP-001");
        Assert.Contains(diagnostics, item => item.Code == "LEG02-MAP-002");
        Assert.Contains(diagnostics, item => item.Code == "LEG02-NPC-002");
        Assert.Contains(diagnostics, item => item.Code == "LEG02-NPC-003");
        Assert.Contains(diagnostics, item => item.Code == "LEG02-SPAWN-001");
        Assert.DoesNotContain(diagnostics, item => item.Source == "MapInfo[11]");
        Assert.All(diagnostics, item => Assert.NotNull(MapContentNavigation.FindTarget(item, map, [npc])));
    }

    [Fact]
    public void 未保存草稿的刷怪矿区坐标用于叠层定位()
    {
        var map = CreateMap();
        var draft = new MapContentDraft(
            [new MapRespawnDraft(5, new Point(14, 15), 1, 2, 3, 0, string.Empty, 0, 0, false, 0)],
            [new MapMineZoneDraft(1, new Point(16, 17), 2)]);

        IReadOnlyList<MapContentTarget> targets = MapContentNavigation.BuildTargets(map, [], draft);

        Assert.Equal(new Point(14, 15), Assert.Single(targets, item => item.Layer == MapContentLayer.Respawn).Location);
        Assert.Equal(new Point(16, 17), Assert.Single(targets, item => item.Layer == MapContentLayer.MineZone).Location);
        Assert.Equal(new Point(2, 3), Assert.Single(targets, item => item.Layer == MapContentLayer.Exit).Location);
    }

    private static MapInfo CreateMap()
    {
        var map = new MapInfo { Index = 10 };
        map.Movements.Add(new MovementInfo { Source = new Point(2, 3), MapIndex = 11, Destination = new Point(12, 13) });
        map.Respawns.Add(new RespawnInfo { MonsterIndex = 5, Location = new Point(4, 5) });
        map.MineZones.Add(new MineZone { Mine = 1, Location = new Point(6, 7), Size = 2 });
        return map;
    }

    private static void AssertTarget(
        MapContentTarget actual,
        MapContentLayer layer,
        int? listIndex,
        int? entityIndex,
        Point location,
        string source)
    {
        Assert.Equal(layer, actual.Layer);
        Assert.Equal(listIndex, actual.ListIndex);
        Assert.Equal(entityIndex, actual.EntityIndex);
        Assert.Equal(location, actual.Location);
        Assert.Equal(source, actual.Source);
    }
}
