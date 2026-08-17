namespace Server.Scripting
{
    internal sealed class LingFengTextDataProvider
    {
        private readonly object _gate = new();
        private readonly string _rootPath;
        private readonly IReadOnlyDictionary<string, TextFileDefinition> _definitions;
        private readonly Dictionary<string, Dictionary<string, string>> _iniValues;
        private readonly Dictionary<string, HashSet<string>> _dirtyIniItems =
            new(StringComparer.OrdinalIgnoreCase);

        internal LingFengTextDataProvider(
            IReadOnlyDictionary<string, TextFileDefinition> definitions,
            string rootPath)
        {
            _rootPath = Path.GetFullPath(rootPath ?? string.Empty);
            _definitions = new Dictionary<string, TextFileDefinition>(
                definitions, StringComparer.OrdinalIgnoreCase);
            var iniValues = new Dictionary<string, Dictionary<string, string>>(
                StringComparer.OrdinalIgnoreCase);
            foreach ((string key, TextFileDefinition definition) in definitions)
            {
                if (!key.EndsWith(".ini", StringComparison.OrdinalIgnoreCase)) continue;
                iniValues.Add(key, ParseIni(definition));
            }
            _iniValues = iniValues;
        }

        internal bool TryGet(string path, out TextFileDefinition definition)
        {
            definition = null;
            return TryNormalizeReference(path, out string key) &&
                   _definitions.TryGetValue(key, out definition);
        }

        internal bool TryReadConfigValue(
            string path, string section, string item, out string value)
        {
            value = string.Empty;
            if (!TryNormalizeReference(path, out string key) ||
                string.IsNullOrWhiteSpace(section) || string.IsNullOrWhiteSpace(item))
                return false;
            lock (_gate)
            {
                return _iniValues.TryGetValue(key, out Dictionary<string, string> values) &&
                       values.TryGetValue($"{section.Trim()}\n{item.Trim()}", out value);
            }
        }

        internal bool TryWriteCachedConfigValue(
            string path, string section, string item, string value)
        {
            if (!TryNormalizeReference(path, out string key) ||
                string.IsNullOrWhiteSpace(section) || string.IsNullOrWhiteSpace(item) ||
                ContainsLineBreak(section) || ContainsLineBreak(item) || ContainsLineBreak(value))
                return false;

            string composite = $"{section.Trim()}\n{item.Trim()}";
            lock (_gate)
            {
                if (!_iniValues.TryGetValue(key, out Dictionary<string, string> values))
                    return false;
                values[composite] = value ?? string.Empty;
                if (!_dirtyIniItems.TryGetValue(key, out HashSet<string> dirtyItems))
                {
                    dirtyItems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    _dirtyIniItems.Add(key, dirtyItems);
                }
                dirtyItems.Add(composite);
                return true;
            }
        }

        internal void ImportCachedWritesFrom(LingFengTextDataProvider previous)
        {
            if (previous == null || ReferenceEquals(previous, this)) return;
            Dictionary<string, Dictionary<string, string>> writes = previous.ExportCachedWrites();
            lock (_gate)
            {
                foreach ((string key, Dictionary<string, string> items) in writes)
                {
                    if (!_iniValues.TryGetValue(key, out Dictionary<string, string> values))
                        continue;
                    if (!_dirtyIniItems.TryGetValue(key, out HashSet<string> dirtyItems))
                    {
                        dirtyItems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        _dirtyIniItems.Add(key, dirtyItems);
                    }
                    foreach ((string composite, string itemValue) in items)
                    {
                        values[composite] = itemValue;
                        dirtyItems.Add(composite);
                    }
                }
            }
        }

        internal bool FlushCachedWrites(out string diagnostic)
        {
            diagnostic = string.Empty;
            lock (_gate)
            {
                foreach ((string key, HashSet<string> dirtyItems) in _dirtyIniItems)
                {
                    if (dirtyItems.Count == 0 ||
                        !_definitions.TryGetValue(key, out TextFileDefinition definition) ||
                        string.IsNullOrWhiteSpace(definition.SourcePath) ||
                        !_iniValues.TryGetValue(key, out Dictionary<string, string> values))
                    {
                        diagnostic = $"缓存配置缺少可写源文件：{key}";
                        return false;
                    }

                    try
                    {
                        IReadOnlyList<string> lines = BuildPersistedIniLines(
                            definition.Lines, values, dirtyItems);
                        string newLine = GetNewLine(definition.SourceNewLine);
                        string text = string.Join(newLine, lines);
                        string sourcePath = Path.GetFullPath(definition.SourcePath);
                        if (!IsSafeSourcePath(sourcePath))
                            throw new UnauthorizedAccessException("缓存配置源路径已越过 TXT 根目录或变为重解析点。");
                        string temporaryPath = sourcePath + ".lfcache.tmp";
                        File.WriteAllText(temporaryPath, text, GetEncoding(definition.SourceEncoding));
                        File.Move(temporaryPath, sourcePath, true);
                    }
                    catch (Exception ex)
                    {
                        diagnostic = $"缓存配置保存失败：{key}；{ex.GetType().Name}";
                        return false;
                    }
                }
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
                !(segments[^1].EndsWith(".txt", StringComparison.OrdinalIgnoreCase) ||
                  segments[^1].EndsWith(".ini", StringComparison.OrdinalIgnoreCase)))
                return false;
            key = string.Join('/', segments).ToLowerInvariant();
            return true;
        }


        private Dictionary<string, Dictionary<string, string>> ExportCachedWrites()
        {
            lock (_gate)
            {
                var result = new Dictionary<string, Dictionary<string, string>>(
                    StringComparer.OrdinalIgnoreCase);
                foreach ((string key, HashSet<string> dirtyItems) in _dirtyIniItems)
                {
                    if (!_iniValues.TryGetValue(key, out Dictionary<string, string> values))
                        continue;
                    var items = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (string composite in dirtyItems)
                    {
                        if (values.TryGetValue(composite, out string value))
                            items[composite] = value;
                    }
                    if (items.Count > 0) result[key] = items;
                }
                return result;
            }
        }

        private static IReadOnlyList<string> BuildPersistedIniLines(
            IReadOnlyList<string> sourceLines,
            IReadOnlyDictionary<string, string> values,
            IReadOnlyCollection<string> dirtyItems)
        {
            var pending = new Dictionary<string, SortedDictionary<string, string>>(
                StringComparer.OrdinalIgnoreCase);
            foreach (string composite in dirtyItems)
            {
                int separator = composite.IndexOf('\n');
                if (separator <= 0 || !values.TryGetValue(composite, out string value)) continue;
                string section = composite[..separator];
                string item = composite[(separator + 1)..];
                if (!pending.TryGetValue(section, out SortedDictionary<string, string> sectionItems))
                {
                    sectionItems = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    pending.Add(section, sectionItems);
                }
                sectionItems[item] = value;
            }

            var result = new List<string>(sourceLines.Count + dirtyItems.Count + pending.Count);
            string currentSection = string.Empty;
            void FlushPendingSection()
            {
                if (currentSection.Length == 0 ||
                    !pending.Remove(currentSection, out SortedDictionary<string, string> items))
                    return;
                foreach ((string item, string value) in items)
                    result.Add($"{item}={value}");
            }

            foreach (string sourceLine in sourceLines)
            {
                string line = sourceLine ?? string.Empty;
                string trimmed = line.Trim();
                if (trimmed.StartsWith('[') && trimmed.EndsWith(']') && trimmed.Length > 2)
                {
                    FlushPendingSection();
                    currentSection = trimmed[1..^1].Trim();
                    result.Add(line);
                    continue;
                }

                int equals = line.IndexOf('=');
                if (currentSection.Length > 0 && equals > 0 &&
                    pending.TryGetValue(currentSection, out SortedDictionary<string, string> sectionItems))
                {
                    string item = line[..equals].Trim();
                    if (sectionItems.Remove(item, out string value))
                    {
                        result.Add($"{line[..equals]}={value}");
                        continue;
                    }
                }
                result.Add(line);
            }
            FlushPendingSection();

            foreach ((string section, SortedDictionary<string, string> items) in
                     pending.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
            {
                if (result.Count > 0 && result[^1].Length > 0) result.Add(string.Empty);
                result.Add($"[{section}]");
                foreach ((string item, string value) in items)
                    result.Add($"{item}={value}");
            }
            return result;
        }

        private static System.Text.Encoding GetEncoding(string sourceEncoding)
        {
            if (string.Equals(sourceEncoding, "CP936", StringComparison.OrdinalIgnoreCase))
            {
                System.Text.Encoding.RegisterProvider(
                    System.Text.CodePagesEncodingProvider.Instance);
                return System.Text.Encoding.GetEncoding(936,
                    System.Text.EncoderFallback.ExceptionFallback,
                    System.Text.DecoderFallback.ExceptionFallback);
            }
            return new System.Text.UTF8Encoding(
                string.Equals(sourceEncoding, "UTF-8 BOM", StringComparison.OrdinalIgnoreCase), true);
        }

        private static string GetNewLine(string sourceNewLine) =>
            sourceNewLine?.ToUpperInvariant() switch
            {
                "LF" => "\n",
                "CR" => "\r",
                _ => "\r\n"
            };

        private static bool ContainsLineBreak(string value) =>
            value?.IndexOfAny(new[] { '\r', '\n' }) >= 0;

        private bool IsSafeSourcePath(string sourcePath)
        {
            string boundary = _rootPath.TrimEnd(
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            if (!sourcePath.StartsWith(boundary, StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(sourcePath) ||
                (File.GetAttributes(sourcePath) & FileAttributes.ReparsePoint) != 0)
                return false;

            string current = Path.GetDirectoryName(sourcePath);
            while (!string.IsNullOrEmpty(current) &&
                   !string.Equals(current, _rootPath, StringComparison.OrdinalIgnoreCase))
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    return false;
                current = Path.GetDirectoryName(current);
            }
            return string.Equals(current, _rootPath, StringComparison.OrdinalIgnoreCase);
        }

        private static Dictionary<string, string> ParseIni(
            TextFileDefinition definition)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var seenSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string section = string.Empty;
            for (int index = 0; index < definition.Lines.Count; index++)
            {
                string line = (definition.Lines[index] ?? string.Empty).Trim();
                if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#') ||
                    line is "{" or "}")
                    continue;
                if (line.StartsWith('[') && line.EndsWith(']') && line.Length > 2)
                {
                    section = line[1..^1].Trim();
                    if (!seenSections.Add(section))
                    {
                        string prefix = section + "\n";
                        foreach (string key in result.Keys
                                     .Where(key => key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                                     .ToArray())
                            result.Remove(key);
                    }
                    continue;
                }
                int separator = line.IndexOf('=');
                if (section.Length == 0 || separator <= 0) continue;
                string item = line[..separator].Trim();
                if (item.Length == 0) continue;
                string composite = $"{section}\n{item}";
                if (!result.TryAdd(composite, line[(separator + 1)..].Trim()))
                    throw new InvalidDataException(
                        $"LFENV16-INI-001：配置项重复：[{section}] {item}；" +
                        definition.GetSourceLocation(index));
            }
            return result;
        }
    }
}
