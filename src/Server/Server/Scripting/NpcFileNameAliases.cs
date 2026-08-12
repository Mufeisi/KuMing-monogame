using System.Collections.Generic;
using System.Text;

namespace Server.Scripting
{
    /// <summary>
    /// NPC 文件名别名枚举器。
    /// 背景：部分数据库中的 npc_infos.file_name 与脚本侧约定的文件名在“中文与英文/数字后缀之间是否带 '-'”存在差异，
    /// 例如：南蛮/杂货商人NAMMAN  vs  南蛮/杂货商人-NAMMAN。
    /// 为避免整包 NPC 对话“完全无响应”，这里提供一个保守的别名策略：仅在未命中时尝试。
    /// </summary>
    internal static class NpcFileNameAliases
    {
        public static IEnumerable<string> Enumerate(string npcFileName)
        {
            if (string.IsNullOrWhiteSpace(npcFileName))
                yield break;

            var original = npcFileName.Trim();
            yield return original;

            // 仅对最后一个路径段（文件名）做处理，避免目录层级产生组合爆炸。
            var normalizedPath = original.Replace('\\', '/');
            var lastSlash = normalizedPath.LastIndexOf('/');

            var prefix = lastSlash >= 0 ? normalizedPath.Substring(0, lastSlash + 1) : string.Empty;
            var lastSegment = lastSlash >= 0 ? normalizedPath.Substring(lastSlash + 1) : normalizedPath;

            if (lastSegment.Length == 0)
                yield break;

            var removed = RemoveDashBetweenCjkAndAscii(lastSegment);
            if (!string.Equals(removed, lastSegment, System.StringComparison.Ordinal))
                yield return prefix + removed;

            var inserted = InsertDashBetweenCjkAndAscii(lastSegment);
            if (!string.Equals(inserted, lastSegment, System.StringComparison.Ordinal))
                yield return prefix + inserted;
        }

        private static string RemoveDashBetweenCjkAndAscii(string s)
        {
            if (string.IsNullOrEmpty(s))
                return s;

            var sb = new StringBuilder(s.Length);

            for (var i = 0; i < s.Length; i++)
            {
                var ch = s[i];

                if (ch == '-' && i > 0 && i < s.Length - 1)
                {
                    var prev = s[i - 1];
                    var next = s[i + 1];

                    if (IsCjk(prev) && IsAsciiAlphaNum(next))
                        continue;
                }

                sb.Append(ch);
            }

            return sb.ToString();
        }

        private static string InsertDashBetweenCjkAndAscii(string s)
        {
            if (string.IsNullOrEmpty(s))
                return s;

            var sb = new StringBuilder(s.Length + 2);

            for (var i = 0; i < s.Length; i++)
            {
                var ch = s[i];
                sb.Append(ch);

                if (i >= s.Length - 1)
                    continue;

                var next = s[i + 1];

                if (ch != '-' && IsCjk(ch) && IsAsciiAlphaNum(next) && next != '-')
                    sb.Append('-');
            }

            return sb.ToString();
        }

        private static bool IsAsciiAlphaNum(char c) => c <= 0x7F && char.IsLetterOrDigit(c);

        private static bool IsCjk(char c)
        {
            // CJK Unified Ideographs + Extension A
            return (c >= 0x4E00 && c <= 0x9FFF) || (c >= 0x3400 && c <= 0x4DBF);
        }
    }
}

