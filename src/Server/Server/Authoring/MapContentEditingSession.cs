using System.Drawing;
using Server.Diagnostics;
using Server.MirDatabase;

namespace Server.Authoring;

public sealed record MapRespawnDraft(
    int MonsterIndex,
    Point Location,
    ushort Count,
    ushort Spread,
    ushort Delay,
    byte Direction,
    string RoutePath,
    ushort RandomDelay,
    int RespawnIndex,
    bool SaveRespawnTime,
    ushort RespawnTicks);

public sealed record MapMineZoneDraft(byte Mine, Point Location, ushort Size);

public sealed record MapContentDraft(
    IReadOnlyList<MapRespawnDraft> Respawns,
    IReadOnlyList<MapMineZoneDraft> MineZones);

public sealed record MapContentDifference(string Kind, string Source, string Summary);

public sealed class MapContentReview
{
    internal MapContentReview(
        IReadOnlyList<ProjectPreflightDiagnostic> diagnostics,
        IReadOnlyList<MapContentDifference> differences)
    {
        Diagnostics = diagnostics;
        Differences = differences;
    }

    public IReadOnlyList<ProjectPreflightDiagnostic> Diagnostics { get; }
    public IReadOnlyList<MapContentDifference> Differences { get; }
    public bool HasErrors => Diagnostics.Any(item => item.Severity == ProjectPreflightSeverity.Error);
    public bool HasChanges => Differences.Count > 0;
}

public sealed class MapContentCommitResult
{
    internal MapContentCommitResult(bool completed, MapContentReview review, string error)
    {
        Completed = completed;
        Review = review;
        Error = error ?? string.Empty;
    }

    public bool Completed { get; }
    public MapContentReview Review { get; }
    public string Error { get; }
}

/// <summary>
/// 隔离地图可视化编辑器的草稿、历史、校验、差异与原子提交。
/// 原始 MapInfo 只会在 TryCommit 成功时改变。
/// </summary>
public sealed class MapContentEditingSession
{
    private readonly MapInfo _source;
    private readonly HashSet<int> _monsterIndexes;
    private readonly int _mapWidth;
    private readonly int _mapHeight;
    private readonly List<MapContentDraft> _history = new();
    private int _historyIndex;
    private MapContentDraft _baseline;

    public MapContentEditingSession(MapInfo source, IEnumerable<int> monsterIndexes, int mapWidth, int mapHeight)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _monsterIndexes = (monsterIndexes ?? Array.Empty<int>()).ToHashSet();
        _mapWidth = mapWidth;
        _mapHeight = mapHeight;
        _baseline = Capture(source);
        _history.Add(Clone(_baseline));
    }

    public bool CanUndo => _historyIndex > 0;
    public bool CanRedo => _historyIndex + 1 < _history.Count;

    public static MapContentDraft Capture(MapInfo source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new MapContentDraft(
            source.Respawns.Select(item => new MapRespawnDraft(
                item.MonsterIndex, item.Location, item.Count, item.Spread, item.Delay,
                item.Direction, item.RoutePath ?? string.Empty, item.RandomDelay,
                item.RespawnIndex, item.SaveRespawnTime, item.RespawnTicks)).ToArray(),
            source.MineZones.Select(item => new MapMineZoneDraft(item.Mine, item.Location, item.Size)).ToArray());
    }

    public bool Observe(MapContentDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        MapContentDraft normalized = Clone(draft);
        if (EqualsDraft(_history[_historyIndex], normalized))
            return false;

        if (_historyIndex + 1 < _history.Count)
            _history.RemoveRange(_historyIndex + 1, _history.Count - _historyIndex - 1);
        _history.Add(normalized);
        _historyIndex++;
        return true;
    }

    public MapContentDraft Undo(MapContentDraft current)
    {
        Observe(current);
        if (CanUndo)
            _historyIndex--;
        return Clone(_history[_historyIndex]);
    }

    public MapContentDraft Redo()
    {
        if (CanRedo)
            _historyIndex++;
        return Clone(_history[_historyIndex]);
    }

    public MapContentReview Review(MapContentDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var diagnostics = new List<ProjectPreflightDiagnostic>();
        for (int index = 0; index < draft.Respawns.Count; index++)
        {
            MapRespawnDraft item = draft.Respawns[index];
            string source = $"MapInfo[{_source.Index}].Respawns[{index}]";
            if (!_monsterIndexes.Contains(item.MonsterIndex))
                diagnostics.Add(Error("LEG02-SPAWN-001", source, $"怪物 {item.MonsterIndex} 不存在"));
            if (item.Count == 0)
                diagnostics.Add(Error("LEG02-SPAWN-002", source, "刷新数量必须大于 0"));
            if (item.Spread > 1000)
                diagnostics.Add(Warning("LEG02-SPAWN-003", source, "刷新范围异常大于 1000，请确认不是录入错误"));
            if (item.Delay == 0)
                diagnostics.Add(Warning("LEG02-SPAWN-004", source, "刷新时间为 0，请确认是否需要立即刷新"));
            if (!IsWithinMap(item.Location))
                diagnostics.Add(Error("LEG02-SPAWN-005", source, $"刷新坐标超出地图边界 {_mapWidth}x{_mapHeight}"));
        }

        for (int index = 0; index < draft.MineZones.Count; index++)
        {
            MapMineZoneDraft item = draft.MineZones[index];
            string source = $"MapInfo[{_source.Index}].MineZones[{index}]";
            if (item.Size == 0)
                diagnostics.Add(Error("LEG06-MINE-001", source, "矿区范围必须大于 0"));
            if (!IsWithinMap(item.Location))
                diagnostics.Add(Error("LEG06-MINE-002", source, $"矿区坐标超出地图边界 {_mapWidth}x{_mapHeight}"));
        }

        return new MapContentReview(
            diagnostics.OrderBy(item => item.Code, StringComparer.Ordinal).ThenBy(item => item.Source, StringComparer.Ordinal).ToArray(),
            BuildDifferences(_baseline, draft));
    }

    public MapContentCommitResult TryCommit(MapContentDraft draft, Action persist)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(persist);
        MapContentReview review = Review(draft);
        if (review.HasErrors)
            return new MapContentCommitResult(false, review, "保存前校验未通过。");

        MapContentDraft beforeCommit = Capture(_source);
        try
        {
            Apply(_source, draft);
            persist();
            _baseline = Clone(draft);
            _history.Clear();
            _history.Add(Clone(_baseline));
            _historyIndex = 0;
            return new MapContentCommitResult(true, review, string.Empty);
        }
        catch (Exception ex)
        {
            Apply(_source, beforeCommit);
            return new MapContentCommitResult(false, review, $"持久化失败，已恢复保存前内容：{ex.Message}");
        }
    }

    private bool IsWithinMap(Point location) =>
        _mapWidth > 0 && _mapHeight > 0 &&
        location.X >= 0 && location.Y >= 0 &&
        location.X < _mapWidth && location.Y < _mapHeight;

    private static ProjectPreflightDiagnostic Error(string code, string source, string message) =>
        new(code, ProjectPreflightSeverity.Error, source, message);

    private static ProjectPreflightDiagnostic Warning(string code, string source, string message) =>
        new(code, ProjectPreflightSeverity.Warning, source, message);

    private static IReadOnlyList<MapContentDifference> BuildDifferences(MapContentDraft baseline, MapContentDraft draft)
    {
        var result = new List<MapContentDifference>();
        AppendDifferences(result, "刷怪", baseline.Respawns, draft.Respawns, FormatRespawn);
        AppendDifferences(result, "矿区", baseline.MineZones, draft.MineZones, FormatMineZone);
        return result;
    }

    private static void AppendDifferences<T>(
        ICollection<MapContentDifference> output,
        string label,
        IReadOnlyList<T> before,
        IReadOnlyList<T> after,
        Func<T, string> format)
    {
        int common = Math.Min(before.Count, after.Count);
        for (int index = 0; index < common; index++)
        {
            if (!EqualityComparer<T>.Default.Equals(before[index], after[index]))
                output.Add(new MapContentDifference("修改", $"{label}[{index}]", $"{format(before[index])} → {format(after[index])}"));
        }
        for (int index = common; index < before.Count; index++)
            output.Add(new MapContentDifference("删除", $"{label}[{index}]", format(before[index])));
        for (int index = common; index < after.Count; index++)
            output.Add(new MapContentDifference("新增", $"{label}[{index}]", format(after[index])));
    }

    private static string FormatRespawn(MapRespawnDraft item) =>
        $"怪物={item.MonsterIndex} 坐标={item.Location.X},{item.Location.Y} 数量={item.Count} 范围={item.Spread} 刷新={item.Delay}";

    private static string FormatMineZone(MapMineZoneDraft item) =>
        $"矿区={item.Mine} 坐标={item.Location.X},{item.Location.Y} 范围={item.Size}";

    private static void Apply(MapInfo target, MapContentDraft draft)
    {
        List<RespawnInfo> respawns = draft.Respawns.Select(item => new RespawnInfo
        {
            MonsterIndex = item.MonsterIndex,
            Location = item.Location,
            Count = item.Count,
            Spread = item.Spread,
            Delay = item.Delay,
            Direction = item.Direction,
            RoutePath = item.RoutePath ?? string.Empty,
            RandomDelay = item.RandomDelay,
            RespawnIndex = item.RespawnIndex,
            SaveRespawnTime = item.SaveRespawnTime,
            RespawnTicks = item.RespawnTicks,
        }).ToList();
        List<MineZone> mineZones = draft.MineZones.Select(item => new MineZone
        {
            Mine = item.Mine,
            Location = item.Location,
            Size = item.Size,
        }).ToList();
        target.Respawns.Clear();
        target.Respawns.AddRange(respawns);
        target.MineZones.Clear();
        target.MineZones.AddRange(mineZones);
    }

    private static MapContentDraft Clone(MapContentDraft draft) =>
        new(draft.Respawns.ToArray(), draft.MineZones.ToArray());

    private static bool EqualsDraft(MapContentDraft left, MapContentDraft right) =>
        left.Respawns.SequenceEqual(right.Respawns) && left.MineZones.SequenceEqual(right.MineZones);
}
