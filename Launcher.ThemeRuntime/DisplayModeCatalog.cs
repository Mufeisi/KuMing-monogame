using System.Runtime.InteropServices;

namespace Launcher.ThemeRuntime;

public readonly record struct LauncherDisplayMode(int Width, int Height)
{
    public override string ToString() => $"{Width}×{Height}";
}

public static class DisplayModeCatalog
{
    private static readonly LauncherDisplayMode[] EngineModes =
    {
        new(1024, 768), new(1280, 720), new(1366, 768), new(1920, 1080),
    };

    public static IReadOnlyList<LauncherDisplayMode> GetSupportedModes()
    {
        if (!OperatingSystem.IsWindows()) return EngineModes;
        var available = new HashSet<LauncherDisplayMode>();
        var mode = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
        for (int index = 0; EnumDisplaySettingsW(null, index, ref mode); index++) available.Add(new LauncherDisplayMode(mode.dmPelsWidth, mode.dmPelsHeight));
        LauncherDisplayMode[] intersection = EngineModes.Where(available.Contains).ToArray();
        return intersection.Length == 0 ? new[] { new LauncherDisplayMode(1024, 768) } : intersection;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public int dmFields, dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency, dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool EnumDisplaySettingsW(string? deviceName, int modeNum, ref DEVMODE devMode);
}
