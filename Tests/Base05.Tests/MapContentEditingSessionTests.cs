using System.Drawing;
using Server.Authoring;
using Server.MirDatabase;
using Xunit;

namespace Base05.Tests;

public sealed class MapContentEditingSessionTests
{
    [Fact]
    public void 草稿审查和取消不会修改原地图()
    {
        MapInfo map = CreateMap();
        var session = new MapContentEditingSession(map, new[] { 7 }, 100, 100);
        MapContentDraft changed = Draft(count: 2, x: 20);

        MapContentReview review = session.Review(changed);

        Assert.True(review.HasChanges);
        Assert.False(review.HasErrors);
        Assert.Equal(1, map.Respawns[0].Count);
        Assert.Equal(10, map.Respawns[0].Location.X);
    }

    [Fact]
    public void 非法刷怪在持久化前被阻断()
    {
        MapInfo map = CreateMap();
        var session = new MapContentEditingSession(map, new[] { 7 }, 100, 100);
        MapContentDraft invalid = Draft(count: 0, x: 120);
        bool persisted = false;

        MapContentCommitResult result = session.TryCommit(invalid, () => persisted = true);

        Assert.False(result.Completed);
        Assert.False(persisted);
        Assert.Contains(result.Review.Diagnostics, item => item.Code == "LEG02-SPAWN-002");
        Assert.Contains(result.Review.Diagnostics, item => item.Code == "LEG02-SPAWN-005");
        Assert.Equal(1, map.Respawns[0].Count);
    }

    [Fact]
    public void 持久化失败恢复原地图内容()
    {
        MapInfo map = CreateMap();
        List<RespawnInfo> originalRespawns = map.Respawns;
        List<MineZone> originalMineZones = map.MineZones;
        var session = new MapContentEditingSession(map, new[] { 7 }, 100, 100);

        MapContentCommitResult result = session.TryCommit(Draft(count: 3, x: 30), () => throw new IOException("磁盘只读"));

        Assert.False(result.Completed);
        Assert.Contains("已恢复", result.Error);
        Assert.Equal(1, map.Respawns[0].Count);
        Assert.Equal(10, map.Respawns[0].Location.X);
        Assert.Same(originalRespawns, map.Respawns);
        Assert.Same(originalMineZones, map.MineZones);
    }

    [Fact]
    public void 撤销重做和成功提交通过同一会话接口完成()
    {
        MapInfo map = CreateMap();
        var session = new MapContentEditingSession(map, new[] { 7 }, 100, 100);
        MapContentDraft first = Draft(count: 2, x: 20);
        MapContentDraft second = Draft(count: 3, x: 30);
        session.Observe(first);
        session.Observe(second);

        Assert.Equal((ushort)2, session.Undo(second).Respawns[0].Count);
        Assert.Equal((ushort)3, session.Redo().Respawns[0].Count);

        bool persisted = false;
        MapContentCommitResult result = session.TryCommit(second, () => persisted = true);
        Assert.True(result.Completed, result.Error);
        Assert.True(persisted);
        Assert.Equal(3, map.Respawns[0].Count);
        Assert.Equal(30, map.Respawns[0].Location.X);
        Assert.Equal(42, map.Respawns[0].RespawnIndex);
    }

    private static MapInfo CreateMap()
    {
        var map = new MapInfo { Index = 1, FileName = "0", Title = "测试地图" };
        map.Respawns.Add(new RespawnInfo
        {
            MonsterIndex = 7,
            Location = new Point(10, 10),
            Count = 1,
            Spread = 5,
            Delay = 10,
            RespawnIndex = 42,
        });
        map.MineZones.Add(new MineZone { Mine = 1, Location = new Point(15, 15), Size = 4 });
        return map;
    }

    private static MapContentDraft Draft(ushort count, int x) => new(
        new[] { new MapRespawnDraft(7, new Point(x, 10), count, 5, 10, 0, string.Empty, 0, 42, false, 0) },
        new[] { new MapMineZoneDraft(1, new Point(15, 15), 4) });
}
