using System.Reflection;
using Shared.Diagnostics;

namespace Server.Diagnostics;

public static class ServerCrashDiagnostics
{
    public static void Capture(Exception exception)
    {
        try
        {
            Logger.GetLogger(LogType.Server).Fatal("服务器发生未处理异常", exception);
            Logger.Flush(TimeSpan.FromSeconds(1));
        }
        catch { }

        try
        {
            CrashDiagnosticBundle.TryWriteOnce(new CrashDiagnosticRequest
            {
                OutputRoot = Path.Combine(AppContext.BaseDirectory, "CrashDiagnostics"),
                Component = "server",
                ProductVersion = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? Globals.ProductVersion,
                ResourceVersionPath = Path.Combine(AppContext.BaseDirectory, "resources.manifest.json"),
                ResourceVersionFallbackPath = Path.Combine(Environment.CurrentDirectory, "resources.manifest.json"),
                Exception = exception,
                LogPaths = FindRecentLogs(),
                Configuration = new Dictionary<string, string>
                {
                    ["DatabaseProvider"] = Settings.DatabaseProvider ?? string.Empty,
                    ["TestServer"] = Settings.TestServer.ToString(),
                    ["TlsEnabled"] = Settings.TlsEnabled.ToString(),
                    ["StartHTTPService"] = Settings.StartHTTPService.ToString(),
                    ["Multithreaded"] = Settings.Multithreaded.ToString(),
                    ["SqliteBackupEnabled"] = Settings.SqliteBackupEnabled.ToString(),
                    ["SaveDelayMinutes"] = Settings.SaveDelay.ToString(),
                },
            }, out _, out _);
        }
        catch
        {
        }
    }

    private static IReadOnlyList<string> FindRecentLogs()
    {
        try
        {
            string directory = Path.GetFullPath(Settings.LogDirectory ?? @".\Logs");
            if (!Directory.Exists(directory)) return Array.Empty<string>();
            return Directory.GetFiles(directory, "*.log", SearchOption.TopDirectoryOnly)
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Take(5)
                .Select(file => file.FullName)
                .ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }
}
