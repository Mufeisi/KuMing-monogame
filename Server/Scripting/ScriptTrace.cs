using System.Runtime.CompilerServices;
using System.Text;
using Server.MirObjects;

namespace Server.Scripting
{
    public static class ScriptTrace
    {
        private sealed class Session
        {
            public bool Enabled;
            public int MaxEntries = 500;
            public DateTime StartedAt = DateTime.UtcNow;
            public readonly List<string> Entries = new List<string>(256);

            public void Clear()
            {
                Entries.Clear();
                StartedAt = DateTime.UtcNow;
            }
        }

        private static readonly ConditionalWeakTable<PlayerObject, Session> Sessions = new ConditionalWeakTable<PlayerObject, Session>();

        public static bool IsEnabled(PlayerObject player)
        {
            if (player == null) return false;
            return Sessions.TryGetValue(player, out var session) && session.Enabled;
        }

        public static void Start(PlayerObject player, int maxEntries = 500)
        {
            if (player == null) return;

            if (maxEntries < 50) maxEntries = 50;
            if (maxEntries > 10000) maxEntries = 10000;

            var session = Sessions.GetValue(player, _ => new Session());
            session.Enabled = true;
            session.MaxEntries = maxEntries;
            session.Clear();

            Record(player, $"[Trace] START max={maxEntries}");
        }

        public static bool Stop(PlayerObject player, out IReadOnlyList<string> entries)
        {
            entries = Array.Empty<string>();

            if (player == null) return false;
            if (!Sessions.TryGetValue(player, out var session)) return false;

            session.Enabled = false;
            entries = session.Entries.ToArray();
            return true;
        }

        public static bool StopAndDump(PlayerObject player, out string filePath, out int entryCount, out string error)
        {
            filePath = string.Empty;
            entryCount = 0;
            error = string.Empty;

            if (!Stop(player, out var entries))
            {
                error = "未开启脚本追踪（请先执行 @ScriptTraceStart）。";
                return false;
            }

            entryCount = entries.Count;

            try
            {
                var dir = Path.Combine(".", "Logs", "ScriptTrace");
                Directory.CreateDirectory(dir);

                var safeName = SanitizeFileName(player.Name ?? "Player");
                var ts = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                filePath = Path.Combine(dir, $"{safeName}-{ts}.log");

                var sb = new StringBuilder(entries.Count * 32);
                sb.AppendLine($"# ScriptTrace 玩家={player.Name} 时间={DateTime.Now:yyyy-MM-dd HH:mm:ss} 条数={entries.Count}");
                for (var i = 0; i < entries.Count; i++)
                {
                    sb.AppendLine(entries[i]);
                }

                File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
                return true;
            }
            catch (Exception ex)
            {
                error = "导出失败：" + ex.Message;
                return false;
            }
        }

        public static void Record(PlayerObject player, string message)
        {
            if (player == null) return;
            if (string.IsNullOrWhiteSpace(message)) return;

            if (!Sessions.TryGetValue(player, out var session) || !session.Enabled) return;

            if (session.Entries.Count >= session.MaxEntries + 1) return;

            if (session.Entries.Count == session.MaxEntries)
            {
                session.Entries.Add($"{DateTime.Now:HH:mm:ss.fff} [Trace] 已达到最大记录 {session.MaxEntries} 条，后续将被忽略");
                return;
            }

            session.Entries.Add($"{DateTime.Now:HH:mm:ss.fff} {message}");
        }

        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Player";

            var invalid = Path.GetInvalidFileNameChars();
            var result = new StringBuilder(name.Length);
            for (var i = 0; i < name.Length; i++)
            {
                var ch = name[i];
                if (Array.IndexOf(invalid, ch) >= 0)
                {
                    result.Append('_');
                    continue;
                }

                result.Append(ch);
            }

            return result.ToString();
        }
    }
}
