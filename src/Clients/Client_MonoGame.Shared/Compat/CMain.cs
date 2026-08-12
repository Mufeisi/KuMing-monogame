using System;
using System.IO;
using System.Text;

namespace MonoShare;

internal static class CMain
{
    private static readonly object LogGate = new();
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public static bool SoftKeyboardVisible { get; set; }

    public static void SaveLog(string message)
    {
        Write("MobileRuntime.log", message);
    }

    public static void SaveError(string message)
    {
        Write("MobileErrors.log", message);
    }

    private static void Write(string fileName, string message)
    {
        if (string.IsNullOrWhiteSpace(fileName) || string.IsNullOrWhiteSpace(message))
            return;

        try
        {
            lock (LogGate)
            {
                string runtimeRoot = ClientResourceLayout.RuntimeRoot;
                Directory.CreateDirectory(runtimeRoot);
                string path = Path.Combine(runtimeRoot, fileName);
                File.AppendAllText(
                    path,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}",
                    Utf8NoBom);
            }
        }
        catch
        {
        }
    }
}
