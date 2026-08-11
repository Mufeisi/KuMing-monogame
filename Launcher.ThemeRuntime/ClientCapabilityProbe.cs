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
            if (!File.Exists(Path.Combine(root, "Client.exe"))) return ClientLaunchCapability.Unsupported;
            string marker = Path.Combine(root, "launcher-capabilities.json");
            if (File.Exists(marker)) return ValidateCurrentMarker(marker) ? ClientLaunchCapability.Current15Arguments : ClientLaunchCapability.Unsupported;
            return ClientLaunchCapability.Unsupported;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or BadImageFormatException) { return ClientLaunchCapability.Unsupported; }
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
