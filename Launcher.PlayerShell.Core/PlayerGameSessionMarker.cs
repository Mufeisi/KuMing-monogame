using System.Diagnostics;
using System.Globalization;

namespace Launcher.PlayerShell;

public static class PlayerGameSessionMarker
{
    private const string MarkerDirectoryName = "player-game-sessions";

    public static void Record(string playerExecutable, Process gameProcess)
    {
        long started = gameProcess.StartTime.ToUniversalTime().Ticks;
        string directory = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(playerExecutable))!, MarkerDirectoryName);
        if (Directory.Exists(directory) && (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("游戏会话标记目录不得为重解析点");
        Directory.CreateDirectory(directory);
        string target = Path.Combine(directory, gameProcess.Id.ToString(CultureInfo.InvariantCulture) + "-" + started.ToString(CultureInfo.InvariantCulture) + ".session");
        string temporary = target + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temporary, gameProcess.Id.ToString(CultureInfo.InvariantCulture) + "|" + started.ToString(CultureInfo.InvariantCulture));
            File.Move(temporary, target, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static bool IsGameRunning(string playerExecutable)
    {
        string directory = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(playerExecutable))!, MarkerDirectoryName);
        if (!Directory.Exists(directory) || (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) return false;
        string[] markers = Directory.EnumerateFiles(directory, "*.session").Take(129).ToArray();
        if (markers.Length > 128) return true;
        foreach (string marker in markers)
        {
            try
            {
                if ((File.GetAttributes(marker) & FileAttributes.ReparsePoint) != 0 || new FileInfo(marker).Length > 128) continue;
                string[] parts = File.ReadAllText(marker).Trim().Split('|');
                if (parts.Length != 2 || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int pid) || !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out long ticks)) continue;
                using Process process = Process.GetProcessById(pid);
                if (!process.HasExited && process.StartTime.ToUniversalTime().Ticks == ticks) return true;
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException) { }
            try { File.Delete(marker); } catch { }
        }
        return false;
    }
}
