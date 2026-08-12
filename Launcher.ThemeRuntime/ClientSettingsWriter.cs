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
        ValidateWritableDirectory(directory);
        string ini = Path.Combine(directory, "Mir2Config.ini");
        ValidateIniPath(directory, ini);
        WriteAtomically(directory, ini, temporary =>
        {
            WriteValue(temporary, "Graphics", "Resolution", settings.Resolution.ToString(System.Globalization.CultureInfo.InvariantCulture));
            WriteValue(temporary, "Graphics", "FullScreen", settings.FullScreen.ToString());
            WriteValue(temporary, "Graphics", "Borderless", settings.Borderless.ToString());
            WriteValue(temporary, "Graphics", "FPSCap", settings.FpsCap.ToString());
            WriteValue(temporary, "Graphics", "MaxFPS", settings.MaxFps.ToString(System.Globalization.CultureInfo.InvariantCulture));
            WriteValue(temporary, "Graphics", "AlwaysOnTop", settings.TopMost.ToString());
            WriteValue(temporary, "Sound", "Volume", settings.Volume.ToString(System.Globalization.CultureInfo.InvariantCulture));
            WriteValue(temporary, "Sound", "Music", settings.MusicVolume.ToString(System.Globalization.CultureInfo.InvariantCulture));
            WriteValue(temporary, "Launcher", "AutoStart", settings.AutoStart.ToString());
            WriteValue(temporary, "Logs", "TracePackets", settings.AdvancedLogs.ToString());
            WriteValue(temporary, "Micro", "CacheLimitMb", settings.MicroCacheLimitMb.ToString(System.Globalization.CultureInfo.InvariantCulture));
        });
    }

    public static void WriteMicroIdentity(string clientDirectory, string projectId, string? user)
    {
        string directory = Path.GetFullPath(clientDirectory);
        ValidateWritableDirectory(directory);
        string ini = Path.Combine(directory, "Mir2Config.ini");
        ValidateIniPath(directory, ini);
        WriteAtomically(directory, ini, temporary =>
        {
            WriteValue(temporary, "Micro", "CredentialKey", projectId);
            WriteValue(temporary, "Micro", "User", user?.Trim() ?? string.Empty);
            WriteValue(temporary, "Micro", "Code", string.Empty);
        });
    }

    public static void ValidateWritableDirectory(string clientDirectory)
    {
        string full = Path.GetFullPath(clientDirectory);
        if (!Directory.Exists(full)) throw new DirectoryNotFoundException("客户端资源目录不存在");
        string current = Path.GetPathRoot(full) ?? string.Empty;
        foreach (string part in full[current.Length..].Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (part.Length == 0) continue;
            current = Path.Combine(current, part);
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("客户端资源目录不得经过重解析点");
        }
    }

    private static void ValidateIniPath(string directory, string ini)
    {
        string full = Path.GetFullPath(ini);
        if (!string.Equals(Path.GetDirectoryName(full), directory, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("玩家设置路径越界");
        if (File.Exists(full) && (File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("玩家设置文件不得为重解析点");
    }

    private static void WriteAtomically(string directory, string ini, Action<string> update)
    {
        string temporary = ini + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            if (File.Exists(ini)) File.Copy(ini, temporary, overwrite: false); else using (File.Create(temporary)) { }
            update(temporary);
            ValidateWritableDirectory(directory);
            ValidateIniPath(directory, ini);
            if ((File.GetAttributes(temporary) & FileAttributes.ReparsePoint) != 0 || !string.Equals(Path.GetDirectoryName(Path.GetFullPath(temporary)), directory, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("玩家设置临时文件路径无效");
            File.Move(temporary, ini, overwrite: true);
        }
        finally { try { File.Delete(temporary); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { } }
    }

    private static void WriteValue(string path, string section, string key, string value)
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
