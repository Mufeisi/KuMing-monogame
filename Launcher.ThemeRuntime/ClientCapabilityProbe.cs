namespace Launcher.ThemeRuntime;

public enum ClientLaunchCapability { Unsupported, Current15Arguments }

/// <summary>只读识别既有客户端启动协议；不加载或执行玩家目录中的程序集。</summary>
public static class ClientCapabilityProbe
{
    public static ClientLaunchCapability Detect(string directory)
    {
        try
        {
            string root = Path.GetFullPath(directory);
            RejectReparseChain(root);
            string executable = Path.Combine(root, "Client.exe");
            if (!IsPlainFile(executable)) return ClientLaunchCapability.Unsupported;
            string marker = Path.Combine(root, "launcher-capabilities.json");
            if (IsPlainFile(marker)) return ValidateCurrentMarker(marker) ? ClientLaunchCapability.Current15Arguments : ClientLaunchCapability.Unsupported;
            return ClientLaunchCapability.Unsupported;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or BadImageFormatException) { return ClientLaunchCapability.Unsupported; }
    }

    private static bool IsPlainFile(string path)
    {
        RejectReparseChain(path);
        return File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0;
    }

    private static void RejectReparseChain(string path)
    {
        string full = Path.GetFullPath(path);
        string current = Path.GetPathRoot(full) ?? string.Empty;
        foreach (string part in full[current.Length..].Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (part.Length == 0) continue;
            current = Path.Combine(current, part);
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("客户端入口路径不得经过重解析点");
        }
    }

    private static bool ValidateCurrentMarker(string marker)
    {
        if (new FileInfo(marker).Length > 4096) return false;
        try
        {
            using System.Text.Json.JsonDocument json = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(marker));
            return json.RootElement.TryGetProperty("product", out var product) && product.GetString() == "LyoCrystal"
                && json.RootElement.TryGetProperty("launchArgumentsVersion", out var version) && version.TryGetInt32(out int value) && value == 1;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException) { return false; }
    }
}
