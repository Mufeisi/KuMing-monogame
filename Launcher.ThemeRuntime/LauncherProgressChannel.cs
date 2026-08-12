using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;

namespace Launcher.ThemeRuntime;

public sealed record LauncherProgressSnapshot(DateTimeOffset UpdatedUtc, LauncherProgressState State);

/// <summary>游戏进程与仍在显示的玩家入口之间的本机原子进度通道。</summary>
public static class LauncherProgressChannel
{
    private static readonly object WriteLock = new();

    public static void Publish(string projectId, LauncherProgressState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        string path = GetPath(projectId);
        string temporary = path + ".tmp-" + Environment.ProcessId + "-" + Guid.NewGuid().ToString("N");
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(
            new LauncherProgressSnapshot(DateTimeOffset.UtcNow, state),
            LauncherSnapshotJsonContext.Default.LauncherProgressSnapshot);
        lock (WriteLock)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            try
            {
                File.WriteAllBytes(temporary, json);
                File.Move(temporary, path, overwrite: true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }

    public static bool TryRead(string projectId, out LauncherProgressSnapshot? snapshot)
    {
        snapshot = null;
        string path = GetPath(projectId);
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > 64 * 1024) return false;
            snapshot = JsonSerializer.Deserialize(File.ReadAllBytes(path), LauncherSnapshotJsonContext.Default.LauncherProgressSnapshot);
            return snapshot?.State is not null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return false; }
    }

    public static void Clear(string projectId)
    {
        string path = GetPath(projectId);
        try { File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static string GetPath(string projectId)
    {
        LauncherSnapshotValidator.ValidateProjectId(projectId);
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LyoCrystal", "Launcher", "Progress", projectId + ".json");
    }
}

/// <summary>在一个游戏会话内合并并发微端请求，生成当前文件与总体队列两级进度。</summary>
public sealed class LauncherDownloadProgressPublisher
{
    private sealed class Transfer { public string File = string.Empty; public long Received; public long Total; public bool Started; }
    private readonly string _projectId;
    private readonly object _sync = new();
    private readonly Dictionary<string, Transfer> _transfers = new(StringComparer.Ordinal);
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private long _completedReceived;
    private long _completedTotal;
    private long _lastBytes;
    private TimeSpan _lastSample;
    private double _speed;

    public LauncherDownloadProgressPublisher(string projectId)
    {
        LauncherSnapshotValidator.ValidateProjectId(projectId);
        _projectId = projectId;
    }

    public void Queue(string key, string file)
    {
        lock (_sync)
        {
            if (!_transfers.ContainsKey(key)) _transfers[key] = new Transfer { File = file };
            Publish(file, 0, 0);
        }
    }

    public void Report(string key, string file, long received, long total)
    {
        lock (_sync)
        {
            _transfers[key] = new Transfer { File = file, Received = Math.Max(0, received), Total = Math.Max(0, total), Started = true };
            Publish(file, received, total);
        }
    }

    public void Complete(string key, bool succeeded)
    {
        lock (_sync)
        {
            if (_transfers.TryGetValue(key, out Transfer? transfer))
            {
                if (succeeded)
                {
                    transfer.Started = true;
                    transfer.Total = Math.Max(transfer.Total, transfer.Received);
                    transfer.Received = transfer.Total;
                    _completedReceived += transfer.Received;
                    _completedTotal += transfer.Total;
                    _transfers.Remove(key);
                }
                else
                {
                    _transfers.Remove(key);
                    _lastBytes = _completedReceived + _transfers.Values.Sum(value => value.Received);
                    _lastSample = _clock.Elapsed;
                    _speed = 0;
                }
            }
            Publish(transfer?.File ?? string.Empty, transfer?.Received ?? 0, transfer?.Total ?? 0);
        }
    }

    private void Publish(string file, long received, long total)
    {
            long overallReceived = _completedReceived + _transfers.Values.Sum(value => value.Received);
            long overallTotal = _completedTotal + _transfers.Values.Sum(value => value.Total);
            int pending = _transfers.Values.Count(value => !value.Started);
            TimeSpan now = _clock.Elapsed;
            double seconds = (now - _lastSample).TotalSeconds;
            if (seconds >= .25)
            {
                _speed = Math.Max(0, overallReceived - _lastBytes) / seconds;
                _lastBytes = overallReceived;
                _lastSample = now;
            }
            LauncherProgressChannel.Publish(_projectId, new LauncherProgressState(
                pending > 0 ? $"微端资源按需下载（队列 {pending}）" : "微端资源按需下载",
                file, received, total, overallReceived, overallTotal, _speed, pending));
    }
}
