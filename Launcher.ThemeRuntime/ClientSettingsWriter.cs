using System.Runtime.InteropServices;

namespace Launcher.ThemeRuntime;

public static class ClientSettingsWriter
{
    public static LauncherPlayerSettings Read(string clientDirectory, LauncherPlayerSettings defaults)
    {
        ArgumentNullException.ThrowIfNull(defaults);
        string ini = Path.Combine(Path.GetFullPath(clientDirectory), "Mir2Config.ini");
        if (!File.Exists(ini)) return defaults;
        return new LauncherPlayerSettings
        {
            Resolution = ReadInt(ini, "Graphics", "Resolution", defaults.Resolution, 1024, 1920),
            FullScreen = ReadBool(ini, "Graphics", "FullScreen", defaults.FullScreen),
            Borderless = ReadBool(ini, "Graphics", "Borderless", defaults.Borderless),
            FpsCap = ReadBool(ini, "Graphics", "FPSCap", defaults.FpsCap),
            MaxFps = ReadInt(ini, "Graphics", "MaxFPS", defaults.MaxFps, 30, 240),
            TopMost = ReadBool(ini, "Graphics", "AlwaysOnTop", defaults.TopMost),
            Volume = ReadInt(ini, "Sound", "Volume", defaults.Volume, 0, 100),
            MusicVolume = ReadInt(ini, "Sound", "Music", defaults.MusicVolume, 0, 100),
            AutoStart = ReadBool(ini, "Launcher", "AutoStart", defaults.AutoStart),
            AdvancedLogs = ReadBool(ini, "Logs", "TracePackets", defaults.AdvancedLogs),
            MicroCacheLimitMb = ReadInt(ini, "Micro", "CacheLimitMb", defaults.MicroCacheLimitMb, 256, 16384),
        };
    }

    public static void Write(string clientDirectory, LauncherPlayerSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        string directory = Path.GetFullPath(clientDirectory);
        Directory.CreateDirectory(directory);
        string ini = Path.Combine(directory, "Mir2Config.ini");
        Write(ini, "Graphics", "Resolution", settings.Resolution.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Write(ini, "Graphics", "FullScreen", settings.FullScreen.ToString());
        Write(ini, "Graphics", "Borderless", settings.Borderless.ToString());
        Write(ini, "Graphics", "FPSCap", settings.FpsCap.ToString());
        Write(ini, "Graphics", "MaxFPS", settings.MaxFps.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Write(ini, "Graphics", "AlwaysOnTop", settings.TopMost.ToString());
        Write(ini, "Sound", "Volume", settings.Volume.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Write(ini, "Sound", "Music", settings.MusicVolume.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Write(ini, "Launcher", "AutoStart", settings.AutoStart.ToString());
        Write(ini, "Logs", "TracePackets", settings.AdvancedLogs.ToString());
        Write(ini, "Micro", "CacheLimitMb", settings.MicroCacheLimitMb.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    public static void WriteMicroIdentity(string clientDirectory, string projectId, string? user)
    {
        string ini = Path.Combine(Path.GetFullPath(clientDirectory), "Mir2Config.ini");
        Write(ini, "Micro", "CredentialKey", projectId);
        Write(ini, "Micro", "User", user?.Trim() ?? string.Empty);
        Write(ini, "Micro", "Code", string.Empty);
    }

    private static void Write(string path, string section, string key, string value)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("玩家设置仅支持 Windows");
        if (!WritePrivateProfileStringW(section, key, value, path)) throw new IOException("无法写入玩家设置：" + key);
    }

    private static bool ReadBool(string path, string section, string key, bool fallback) => bool.TryParse(Read(path, section, key), out bool value) ? value : fallback;
    private static int ReadInt(string path, string section, string key, int fallback, int minimum, int maximum) => int.TryParse(Read(path, section, key), out int value) ? Math.Clamp(value, minimum, maximum) : fallback;
    private static string Read(string path, string section, string key)
    {
        var buffer = new System.Text.StringBuilder(1024);
        GetPrivateProfileStringW(section, key, string.Empty, buffer, buffer.Capacity, path);
        return buffer.ToString().Trim();
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool WritePrivateProfileStringW(string section, string key, string value, string fileName);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern uint GetPrivateProfileStringW(string section, string key, string defaultValue, System.Text.StringBuilder returnedString, int size, string fileName);
}
