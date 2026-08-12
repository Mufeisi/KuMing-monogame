using System.Diagnostics;
using Server.MirEnvir;
using Server.Scripting;

namespace Server
{
    public static class TxtScriptsTrace
    {
        private static bool IsDiskTxtReadBlocked(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            // 仅在“脚本运行时已启用 + 全局关闭 txt 回落”时生效。
            // 这样能保证迁移期（FallbackToTxt=true）行为不变，同时在最终阶段暴露任何残留的磁盘 txt 依赖。
            if (!Settings.CSharpScriptsEnabled || Settings.CSharpScriptsFallbackToTxt)
                return false;

            var envir = Envir.Main;
            if (envir == null || !envir.CSharpScripts.Enabled)
                return false;

            try
            {
                var fullPath = Path.GetFullPath(path);

                if (!fullPath.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                    return false;

                var envirRoot = Path.GetFullPath(Settings.EnvirPath);
                var envirRootWithSep = envirRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

                return fullPath.StartsWith(envirRootWithSep, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        public static List<string> ReadAllLinesToList(string path, string source)
        {
            if (IsDiskTxtReadBlocked(path))
            {
                if (Settings.TxtScriptsUsageTraceEnabled)
                    TxtUsageTracker.RecordRead(path, $"BLOCKED:{source}");

                if (Settings.TxtScriptsLogLoads)
                    MessageQueue.Instance.Enqueue($"[TxtScripts] 已阻止读取磁盘 TXT（FallbackToTxt=false）：{path} 来源={source}");

                return new List<string>();
            }

            if (!Settings.TxtScriptsLogLoads && !Settings.TxtScriptsUsageTraceEnabled)
                return File.ReadAllLines(path).ToList();

            Stopwatch? sw = null;
            if (Settings.TxtScriptsLogLoads)
                sw = Stopwatch.StartNew();

            var lines = File.ReadAllLines(path).ToList();

            if (Settings.TxtScriptsUsageTraceEnabled)
                TxtUsageTracker.RecordRead(path, source);

            if (Settings.TxtScriptsLogLoads && sw != null)
            {
                sw.Stop();
                MessageQueue.Instance.Enqueue($"[TxtScripts] 读取 {path}（{sw.ElapsedMilliseconds}ms）来源={source}");
            }

            return lines;
        }

        public static string[] ReadAllLines(string path, string source)
        {
            if (IsDiskTxtReadBlocked(path))
            {
                if (Settings.TxtScriptsUsageTraceEnabled)
                    TxtUsageTracker.RecordRead(path, $"BLOCKED:{source}");

                if (Settings.TxtScriptsLogLoads)
                    MessageQueue.Instance.Enqueue($"[TxtScripts] 已阻止读取磁盘 TXT（FallbackToTxt=false）：{path} 来源={source}");

                return Array.Empty<string>();
            }

            if (!Settings.TxtScriptsLogLoads && !Settings.TxtScriptsUsageTraceEnabled)
                return File.ReadAllLines(path);

            Stopwatch? sw = null;
            if (Settings.TxtScriptsLogLoads)
                sw = Stopwatch.StartNew();

            var lines = File.ReadAllLines(path);

            if (Settings.TxtScriptsUsageTraceEnabled)
                TxtUsageTracker.RecordRead(path, source);

            if (Settings.TxtScriptsLogLoads && sw != null)
            {
                sw.Stop();
                MessageQueue.Instance.Enqueue($"[TxtScripts] 读取 {path}（{sw.ElapsedMilliseconds}ms）来源={source}");
            }

            return lines;
        }
    }
}
