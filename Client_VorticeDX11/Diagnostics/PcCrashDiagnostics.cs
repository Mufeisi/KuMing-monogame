using System.Reflection;
using Client.Bootstrap;
using Shared.Diagnostics;

namespace Client.Diagnostics;

internal static class PcCrashDiagnostics
{
    internal static void Capture(Exception exception)
    {
        try
        {
            CrashDiagnosticBundle.TryWriteOnce(new CrashDiagnosticRequest
            {
                OutputRoot = Path.Combine(PcBootstrapLayout.RuntimeRoot, "CrashDiagnostics"),
                Component = "pc-client",
                ProductVersion = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? Globals.ProductVersion,
                ResourceVersionPath = PcBootstrapLayout.ManifestSecurityStatePath,
                ResourceVersionFallbackPath = PcBootstrapLayout.BaselinePackageIndexPath,
                Exception = exception,
                LogPaths = new[]
                {
                    Path.Combine(Environment.CurrentDirectory, "Error.txt"),
                    Path.Combine(AppContext.BaseDirectory, "ResolutionTrace.log"),
                    PcBootstrapLayout.PreLoginUpdateLogPath,
                },
                Configuration = new Dictionary<string, string>
                {
                    ["UseTestConfig"] = Settings.UseTestConfig.ToString(),
                    ["UseTlsV2"] = Settings.UseTlsV2.ToString(),
                    ["FullScreen"] = Settings.FullScreen.ToString(),
                    ["Borderless"] = Settings.Borderless.ToString(),
                    ["Resolution"] = Settings.Resolution.ToString(),
                    ["BootstrapAutoDownload"] = Settings.BootstrapAutoDownload.ToString(),
                    ["UIProfileId"] = Settings.UIProfileId ?? string.Empty,
                },
            }, out _, out _);
        }
        catch
        {
        }
    }
}
