using System.Diagnostics;

namespace Launcher.ThemeRuntime;

internal static class ClientMaintenance
{
    public static void ClearMicroCache(IWin32Window owner, string clientDirectory)
    {
        string cache = Path.GetFullPath(Path.Combine(clientDirectory, "Cache", "MicroResponses"));
        string root = Path.GetFullPath(clientDirectory);
        if (!cache.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("缓存目录越界");
        for (string? current = cache; current is not null && current.StartsWith(root, StringComparison.OrdinalIgnoreCase); current = Path.GetDirectoryName(current))
            if (Directory.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("微端缓存目录不允许使用重解析点");
        if (!Directory.Exists(cache))
        {
            MessageBox.Show(owner, "当前没有可清理的微端缓存。", "微端缓存", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (MessageBox.Show(owner, "只清理可自动重建的 Cache/MicroResponses，不会删除其他缓存或 Data、Map、Sound 资源。是否继续？", "微端缓存", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        string quarantine = cache + ".clearing-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
        Directory.Move(cache, quarantine);
        try { Directory.Delete(quarantine, recursive: true); }
        catch (IOException) { MessageBox.Show(owner, "部分缓存正被游戏使用，将在游戏退出后再清理。", "微端缓存", MessageBoxButtons.OK, MessageBoxIcon.Information); }
    }

    public static void StartRepair(IWin32Window owner, string clientDirectory)
    {
        string client = Path.Combine(Path.GetFullPath(clientDirectory), "Client.exe");
        if (!File.Exists(client)) { MessageBox.Show(owner, "找不到 Client.exe。", "客户端修复", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        var start = new ProcessStartInfo(client) { WorkingDirectory = clientDirectory, UseShellExecute = false };
        start.ArgumentList.Add("--prelogin-update-cli");
        start.ArgumentList.Add("--clientRoot");
        start.ArgumentList.Add(clientDirectory);
        Process.Start(start)?.Dispose();
    }

    public static void OpenLogs(string clientDirectory)
    {
        string logs = Path.Combine(Path.GetFullPath(clientDirectory), "Logs");
        Directory.CreateDirectory(logs);
        Process.Start(new ProcessStartInfo("explorer.exe", logs) { UseShellExecute = true })?.Dispose();
    }
}
