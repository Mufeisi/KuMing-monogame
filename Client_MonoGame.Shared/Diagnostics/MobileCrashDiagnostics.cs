using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Microsoft.Xna.Framework;
using Shared.Diagnostics;

namespace MonoShare;

public static class MobileCrashDiagnostics
{
    public static void Capture(Exception exception, string fallbackRuntimeRoot = "")
    {
        try
        {
#if ANDROID || IOS
            CMain.FlushCrashDiagnosticLogs();
#endif
            string runtimeRoot = ResolveRuntimeRoot(fallbackRuntimeRoot);
            CrashDiagnosticBundle.TryWriteOnce(new CrashDiagnosticRequest
            {
                OutputRoot = Path.Combine(runtimeRoot, "CrashDiagnostics"),
                Component = "mobile-client",
                ProductVersion = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown",
                ResourceVersionPath = ResolveManifestStatePath(runtimeRoot),
                ResourceVersionFallbackPath = Path.Combine(
                    string.IsNullOrWhiteSpace(fallbackRuntimeRoot) ? ClientResourceLayout.ClientRoot : fallbackRuntimeRoot,
                    "BootstrapAssets", "bootstrap-package-index.json"),
                ResourceVersionFallbackContent = ReadBundledBaselineIndex(),
                Exception = exception,
                LogPaths = new[]
                {
                    Path.Combine(runtimeRoot, "MobileErrors.log"),
                    Path.Combine(runtimeRoot, "MobileRuntime.log"),
                    Path.Combine(runtimeRoot, "BootstrapDownloader.log"),
                    Path.Combine(runtimeRoot, "BootstrapBundleInbox.log"),
                    Path.Combine(runtimeRoot, "BootstrapMissingPackages.log"),
                },
                Configuration = new Dictionary<string, string>
                {
                    ["Platform"] = Environment.OSVersion.Platform.ToString(),
                    ["UseTlsV2"] = Settings.UseTlsV2.ToString(),
                    ["Resolution"] = Settings.Resolution.ToString(),
                    ["UIProfileId"] = Settings.UIProfileId ?? string.Empty,
                    ["MobileBackBufferScale"] = Settings.MobileBackBufferScale.ToString("0.###"),
                    ["BootstrapAutoDownload"] = Settings.BootstrapAutoDownloadPackages.ToString(),
                },
            }, out _, out _);
        }
        catch
        {
        }
    }

    private static string ResolveRuntimeRoot(string fallbackRuntimeRoot)
    {
        if (!string.IsNullOrWhiteSpace(fallbackRuntimeRoot))
            return Path.Combine(Path.GetFullPath(fallbackRuntimeRoot), "Cache", "Mobile", "Runtime");

        try
        {
            if (!string.IsNullOrWhiteSpace(ClientResourceLayout.RuntimeRoot))
                return ClientResourceLayout.RuntimeRoot;
        }
        catch
        {
        }

        return AppContext.BaseDirectory;
    }

    private static string ResolveManifestStatePath(string runtimeRoot)
    {
        try
        {
            return ClientResourceLayout.ManifestSecurityStatePath;
        }
        catch
        {
            return Path.Combine(runtimeRoot, "BootstrapManifestSecurityState.json");
        }
    }

    private static string ReadBundledBaselineIndex()
    {
        try
        {
            using Stream stream = TitleContainer.OpenStream("BootstrapAssets/bootstrap-package-index.json");
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return reader.ReadToEnd();
        }
        catch
        {
            return string.Empty;
        }
    }
}
