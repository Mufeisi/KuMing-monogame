using System.Text;
using Server.MirEnvir;

namespace Server.Scripting
{
    internal static class TxtInsertIncludeScanner
    {
        private static bool ShouldIgnoreRelativePath(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath)) return true;

            var p = relativePath.Replace('\\', '/').TrimStart('/');
            if (p.Length == 0) return true;

            var slash = p.IndexOf('/');
            var firstSegment = slash >= 0 ? p.Substring(0, slash) : p;

            if (firstSegment.StartsWith("_", StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }

        internal sealed class Entry
        {
            public string RelativePath { get; set; } = string.Empty;
            public int InsertCount { get; set; }
            public int IncludeCount { get; set; }
        }

        public static bool TryScanEnvir(out List<Entry> entries, out string error)
        {
            entries = new List<Entry>();
            error = string.Empty;

            try
            {
                var provider = Envir.Main?.TextFileProvider;
                if (provider == null)
                {
                    error = "TextFileProvider 未就绪。";
                    return false;
                }

                var all = provider.GetAll();
                if (all == null || all.Count == 0) return true;

                foreach (var definition in all)
                {
                    if (definition == null) continue;

                    var key = definition.Key ?? string.Empty;
                    if (key.Length == 0) continue;

                    // 统一输出为“相对路径”的样式（方便与旧 Envir 结构对照）
                    var relative = key.Replace('\\', '/').TrimStart('/') + ".txt";

                    if (ShouldIgnoreRelativePath(relative))
                        continue;

                    var insertCount = 0;
                    var includeCount = 0;

                    var lines = definition.Lines;
                    if (lines != null)
                    {
                        for (var i = 0; i < lines.Count; i++)
                        {
                            var trimmed = (lines[i] ?? string.Empty).TrimStart();

                            if (trimmed.StartsWith("#INSERT", StringComparison.OrdinalIgnoreCase))
                            {
                                insertCount++;
                                continue;
                            }

                            if (trimmed.StartsWith("#INCLUDE", StringComparison.OrdinalIgnoreCase))
                            {
                                includeCount++;
                            }
                        }
                    }

                    if (insertCount <= 0 && includeCount <= 0)
                        continue;

                    entries.Add(new Entry { RelativePath = relative, InsertCount = insertCount, IncludeCount = includeCount });
                }

                entries.Sort((a, b) => string.Compare(a.RelativePath, b.RelativePath, StringComparison.OrdinalIgnoreCase));
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public static bool DumpLatest(IReadOnlyList<Entry> entries, out string filePath, out string error)
        {
            filePath = string.Empty;
            error = string.Empty;

            try
            {
                var dir = Path.Combine(".", "Logs", "TxtScripts");
                Directory.CreateDirectory(dir);

                filePath = Path.Combine(dir, "insert-include-latest.txt");
                var tempFilePath = filePath + ".tmp";

                var lines = new List<string>(entries?.Count ?? 0);

                if (entries != null)
                {
                    for (var i = 0; i < entries.Count; i++)
                    {
                        var e = entries[i];
                        if (e == null) continue;

                        var insert = e.InsertCount > 0 ? e.InsertCount : 0;
                        var include = e.IncludeCount > 0 ? e.IncludeCount : 0;
                        lines.Add($"{e.RelativePath}\tinsert={insert}\tinclude={include}");
                    }
                }

                File.WriteAllLines(tempFilePath, lines, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                File.Move(tempFilePath, filePath, overwrite: true);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }
}
