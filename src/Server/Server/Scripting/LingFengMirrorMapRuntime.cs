using System.Drawing;
using Server.MirDatabase;
using Server.MirObjects;

namespace Server.MirEnvir;

public readonly record struct LingFengMirrorMapStatus(
    string RuntimeName,
    int TotalSeconds,
    int RemainingSeconds,
    Map Map);

public partial class Envir
{
    private sealed class LingFengMirrorMapEntry
    {
        public required Map Map { get; init; }
        public required Map ReturnMap { get; init; }
        public Point ReturnLocation { get; init; }
        public long StartedAt { get; set; }
        public long ExpiresAt { get; set; }
        public int TotalSeconds { get; set; }
    }

    private readonly Dictionary<string, LingFengMirrorMapEntry> _lingFengMirrorMaps =
        new(StringComparer.OrdinalIgnoreCase);

    public bool TryCreateLingFengMirrorMap(
        string sourceMapName,
        string runtimeName,
        string title,
        int durationSeconds,
        string returnMapName,
        ushort miniMap,
        Point returnLocation,
        out Map mirror)
    {
        mirror = null;
        if (!IsMainThread && Volatile.Read(ref _mainThreadId) != 0)
        {
            (bool Success, Map Created) result = InvokeOnMainThread(() =>
            {
                bool success = TryCreateLingFengMirrorMap(
                    sourceMapName, runtimeName, title, durationSeconds,
                    returnMapName, miniMap, returnLocation, out Map created);
                return (success, created);
            });
            mirror = result.Created;
            return result.Success;
        }

        if (string.IsNullOrWhiteSpace(sourceMapName) ||
            string.IsNullOrWhiteSpace(runtimeName) ||
            string.IsNullOrWhiteSpace(title) ||
            durationSeconds <= 0 ||
            string.IsNullOrWhiteSpace(returnMapName) ||
            _lingFengMirrorMaps.ContainsKey(runtimeName) ||
            MapList.Any(map => string.Equals(map.Info.FileName, runtimeName,
                StringComparison.OrdinalIgnoreCase)))
            return false;

        Map source = GetMapByNameAndInstance(sourceMapName);
        Map returnMap = GetMapByNameAndInstance(returnMapName);
        if (source == null || returnMap == null || source.Info.LingFengIsMirror ||
            returnMap.Info.LingFengIsMirror ||
            source.Cells == null || returnMap.Cells == null)
            return false;

        if (returnLocation != Point.Empty && !returnMap.ValidPoint(returnLocation))
            return false;
        if (returnLocation == Point.Empty &&
            (returnMap.WalkableCells == null || returnMap.WalkableCells.Count == 0))
            return false;

        MapInfo mirrorInfo = source.Info.CreateLingFengMirrorClone(
            runtimeName, title, miniMap);
        Map candidate;
        try
        {
            candidate = Map.CreateLingFengMirror(source, mirrorInfo);
        }
        catch (Exception ex)
        {
            MessageQueue.Enqueue(ex);
            return false;
        }

        long duration = (long)durationSeconds * Settings.Second;
        long expiresAt = Time + Math.Min(long.MaxValue - Time, duration);
        var entry = new LingFengMirrorMapEntry
        {
            Map = candidate,
            ReturnMap = returnMap,
            ReturnLocation = returnLocation,
            StartedAt = Time,
            ExpiresAt = expiresAt,
            TotalSeconds = durationSeconds
        };
        _lingFengMirrorMaps.Add(runtimeName, entry);
        MapList.Add(candidate);
        mirror = candidate;
        return true;
    }

    public bool IsLingFengMirrorMap(string runtimeName)
    {
        if (!IsMainThread && Volatile.Read(ref _mainThreadId) != 0)
            return InvokeOnMainThread(() => IsLingFengMirrorMap(runtimeName));
        return !string.IsNullOrWhiteSpace(runtimeName) &&
               _lingFengMirrorMaps.ContainsKey(runtimeName);
    }

    public bool TryGetLingFengMirrorMapStatus(
        string runtimeName, out LingFengMirrorMapStatus status)
    {
        status = default;
        if (!IsMainThread && Volatile.Read(ref _mainThreadId) != 0)
        {
            (bool Success, LingFengMirrorMapStatus Status) result = InvokeOnMainThread(() =>
            {
                bool success = TryGetLingFengMirrorMapStatus(runtimeName, out var value);
                return (success, value);
            });
            status = result.Status;
            return result.Success;
        }

        if (string.IsNullOrWhiteSpace(runtimeName) ||
            !_lingFengMirrorMaps.TryGetValue(runtimeName, out LingFengMirrorMapEntry entry))
            return false;

        long remainingMilliseconds = Math.Max(0, entry.ExpiresAt - Time);
        int remainingSeconds = (int)Math.Min(int.MaxValue,
            (remainingMilliseconds + Settings.Second - 1) / Settings.Second);
        status = new LingFengMirrorMapStatus(
            runtimeName, entry.TotalSeconds, remainingSeconds, entry.Map);
        return true;
    }

    public bool TrySetLingFengMirrorMapTime(
        string runtimeName, int durationSeconds, bool restart)
    {
        if (!IsMainThread && Volatile.Read(ref _mainThreadId) != 0)
            return InvokeOnMainThread(() => TrySetLingFengMirrorMapTime(
                runtimeName, durationSeconds, restart));
        if (durationSeconds < 0 || string.IsNullOrWhiteSpace(runtimeName) ||
            !_lingFengMirrorMaps.TryGetValue(runtimeName, out LingFengMirrorMapEntry entry))
            return false;

        long duration = (long)durationSeconds * Settings.Second;
        if (restart) entry.StartedAt = Time;
        entry.TotalSeconds = durationSeconds;
        entry.ExpiresAt = entry.StartedAt + Math.Min(
            long.MaxValue - entry.StartedAt, duration);
        return true;
    }

    public bool TryDeleteLingFengMirrorMap(string runtimeName)
    {
        if (!IsMainThread && Volatile.Read(ref _mainThreadId) != 0)
            return InvokeOnMainThread(() => TryDeleteLingFengMirrorMap(runtimeName));
        if (string.IsNullOrWhiteSpace(runtimeName) ||
            !_lingFengMirrorMaps.TryGetValue(runtimeName, out LingFengMirrorMapEntry entry))
            return false;
        if (!TryReturnLingFengMirrorPlayers(entry)) return false;

        foreach (MapObject mapObject in Objects
                     .Where(value => value.CurrentMap == entry.Map)
                     .ToArray())
        {
            if (mapObject is PlayerObject) return false;
            entry.Map.RemoveObject(mapObject);
            if (mapObject.Node != null) mapObject.Despawn();
        }

        SavedSpawns.RemoveAll(respawn => respawn.Map == entry.Map);
        entry.Map.Respawns.Clear();
        entry.Map.ActionList.Clear();
        MapList.Remove(entry.Map);
        _lingFengMirrorMaps.Remove(runtimeName);
        RemoveLingFengEctypeRuntime(runtimeName);
        return true;
    }

    private bool TryReturnLingFengMirrorPlayers(LingFengMirrorMapEntry entry)
    {
        foreach (PlayerObject player in entry.Map.Players.ToArray())
        {
            Point destination = entry.ReturnLocation;
            if (destination == Point.Empty)
            {
                if (entry.ReturnMap.WalkableCells == null ||
                    entry.ReturnMap.WalkableCells.Count == 0)
                    return false;
                destination = entry.ReturnMap.WalkableCells[
                    Random.Next(entry.ReturnMap.WalkableCells.Count)];
            }

            if (!player.Teleport(entry.ReturnMap, destination)) return false;
        }
        return true;
    }

    private void ProcessLingFengMirrorMaps()
    {
        ProcessLingFengEctypes();
        if (_lingFengMirrorMaps.Count == 0) return;
        foreach (string runtimeName in _lingFengMirrorMaps
                     .Where(pair => pair.Value.ExpiresAt <= Time)
                     .Select(pair => pair.Key)
                     .ToArray())
        {
            if (TryDeleteLingFengMirrorMap(runtimeName)) continue;
            if (_lingFengMirrorMaps.TryGetValue(runtimeName, out LingFengMirrorMapEntry entry))
                entry.ExpiresAt = Time + Settings.Second;
            MessageQueue.Enqueue(
                $"[TxtScripts] 镜像地图 {runtimeName} 到期回送失败，保留地图并于下一秒重试。");
        }
    }

    private void ClearLingFengMirrorMapRuntime()
    {
        _lingFengMirrorMaps.Clear();
        ClearLingFengEctypeRuntime();
    }
}
