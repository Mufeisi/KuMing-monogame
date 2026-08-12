using System.Collections.Generic;
using System.Text;

namespace Client.Utils
{
    internal static class ResolutionTrace
    {
        private static readonly object Gate = new object();
        private static readonly Dictionary<string, long> ThrottleMap = new Dictionary<string, long>();

        private static string LogPath => Path.Combine(AppContext.BaseDirectory, "ResolutionTrace.log");

        internal static bool Enabled
        {
            get
            {
                try
                {
                    return Client.Settings.ResolutionTraceEnabled;
                }
                catch
                {
                    return false;
                }
            }
        }

        internal static void StartSession(string title)
        {
            if (!Enabled) return;
            Log("Session", $"===== {title} =====");
        }

        internal static void Log(string stage, string message)
        {
            if (!Enabled) return;
            try
            {
                lock (Gate)
                {
                    File.AppendAllText(
                        LogPath,
                        $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{stage}] {message}{Environment.NewLine}",
                        new UTF8Encoding(false));
                }
            }
            catch
            {
            }
        }

        internal static void LogThrottled(string stage, string key, int intervalMs, string message)
        {
            if (!Enabled) return;

            try
            {
                lock (Gate)
                {
                    long now = Environment.TickCount64;
                    string throttleKey = $"{stage}:{key}";

                    if (ThrottleMap.TryGetValue(throttleKey, out long lastTick) && now - lastTick < intervalMs)
                        return;

                    ThrottleMap[throttleKey] = now;

                    File.AppendAllText(
                        LogPath,
                        $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{stage}] {message}{Environment.NewLine}",
                        new UTF8Encoding(false));
                }
            }
            catch
            {
            }
        }

        internal static void LogClientState(string stage, string extra = "")
        {
            try
            {
                var form = Program.Form;
                var scene = Client.MirControls.MirScene.ActiveScene;
                var formText = form == null || form.IsDisposed
                    ? "Form=null"
                    : $"Form.Client={form.ClientSize.Width}x{form.ClientSize.Height}, Form.Bounds={form.Bounds.Width}x{form.Bounds.Height}, Form.WindowState={form.WindowState}, Form.BorderStyle={form.FormBorderStyle}";
                var sceneText = scene == null || scene.IsDisposed
                    ? "Scene=null"
                    : $"Scene={scene.GetType().Name}, Scene.Size={scene.Size.Width}x{scene.Size.Height}, Scene.Visible={scene.Visible}";

                Log(stage,
                    $"Resolution={Settings.Resolution}, Screen={Settings.ScreenWidth}x{Settings.ScreenHeight}, FullScreen={Settings.FullScreen}, Borderless={Settings.Borderless}, TopMost={Settings.TopMost}, {formText}, {sceneText}"
                    + (string.IsNullOrWhiteSpace(extra) ? string.Empty : $", {extra}"));
            }
            catch (Exception ex)
            {
                Log(stage, $"LogClientState failed: {ex.Message}");
            }
        }
    }
}
