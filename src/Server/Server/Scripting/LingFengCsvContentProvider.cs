namespace Server.Scripting
{
    internal sealed class LingFengCsvContentProvider
    {
        private readonly IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyList<string>>> _tables;

        internal LingFengCsvContentProvider(
            IReadOnlyDictionary<string, TextFileDefinition> definitions)
        {
            var tables = new Dictionary<string, IReadOnlyList<IReadOnlyList<string>>>(
                StringComparer.OrdinalIgnoreCase);
            foreach ((string key, TextFileDefinition definition) in definitions)
            {
                var rows = new List<IReadOnlyList<string>>();
                for (int index = 0; index < definition.Lines.Count; index++)
                {
                    string line = definition.Lines[index] ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(line) ||
                        line.TrimStart().StartsWith(";", StringComparison.Ordinal))
                        continue;
                    if (!TryParseRow(line, out IReadOnlyList<string> row, out string diagnostic))
                        throw new InvalidDataException(
                            $"LFENV16-CSV-001：CSV 行格式无效：{definition.GetSourceLocation(index)}；{diagnostic}");
                    rows.Add(row);
                }
                tables.Add(key, rows.AsReadOnly());
            }
            _tables = tables;
        }

        internal bool Contains(string path) =>
            TryNormalizeReference(path, out string key) && _tables.ContainsKey(key);

        internal bool TryFindTextRow(
            string path,
            string text,
            int startRow,
            int endRow,
            int column,
            bool findLast,
            out int row)
        {
            row = -1;
            if (!TryNormalizeReference(path, out string key) ||
                !_tables.TryGetValue(key, out IReadOnlyList<IReadOnlyList<string>> rows) ||
                startRow < 0 || endRow < startRow || column < 0)
                return false;

            int upper = Math.Min(endRow, rows.Count - 1);
            if (startRow > upper) return true;
            if (findLast)
            {
                for (int index = upper; index >= startRow; index--)
                {
                    if (column >= rows[index].Count ||
                        !string.Equals(rows[index][column], text, StringComparison.Ordinal))
                        continue;
                    row = index;
                    break;
                }
                return true;
            }

            for (int index = startRow; index <= upper; index++)
            {
                if (column >= rows[index].Count ||
                    !string.Equals(rows[index][column], text, StringComparison.Ordinal))
                    continue;
                row = index;
                break;
            }
            return true;
        }

        internal static bool TryNormalizeReference(string path, out string key)
        {
            key = string.Empty;
            string source = (path ?? string.Empty).Trim().Trim('"').Replace('\\', '/');
            if (source.Length == 0 || Path.IsPathRooted(source)) return false;

            string combined = source.StartsWith("../", StringComparison.Ordinal)
                ? "Market_Def/" + source
                : source;
            var segments = new List<string>();
            foreach (string raw in combined.Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                string segment = raw.Trim();
                if (segment.Length == 0 || segment == ".") continue;
                if (segment == "..")
                {
                    if (segments.Count == 0) return false;
                    segments.RemoveAt(segments.Count - 1);
                    continue;
                }
                if (segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return false;
                segments.Add(segment);
            }
            if (segments.Count == 0 ||
                !segments[^1].EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                return false;
            key = string.Join('/', segments).ToLowerInvariant();
            return true;
        }

        private static bool TryParseRow(
            string line,
            out IReadOnlyList<string> row,
            out string diagnostic)
        {
            var fields = new List<string>();
            var current = new System.Text.StringBuilder();
            bool quoted = false;
            bool closedQuote = false;
            for (int index = 0; index < line.Length; index++)
            {
                char value = line[index];
                if (quoted)
                {
                    if (value != '"')
                    {
                        current.Append(value);
                        continue;
                    }
                    if (index + 1 < line.Length && line[index + 1] == '"')
                    {
                        current.Append('"');
                        index++;
                        continue;
                    }
                    quoted = false;
                    closedQuote = true;
                    continue;
                }
                if (value == '"' && current.Length == 0 && !closedQuote)
                {
                    quoted = true;
                    continue;
                }
                if (value == ',')
                {
                    fields.Add(current.ToString());
                    current.Clear();
                    closedQuote = false;
                    continue;
                }
                if (closedQuote)
                {
                    row = Array.Empty<string>();
                    diagnostic = "闭合引号后只能出现分隔逗号。";
                    return false;
                }
                current.Append(value);
            }
            if (quoted)
            {
                row = Array.Empty<string>();
                diagnostic = "字段引号未闭合。";
                return false;
            }
            fields.Add(current.ToString());
            row = fields.AsReadOnly();
            diagnostic = string.Empty;
            return true;
        }
    }
}
