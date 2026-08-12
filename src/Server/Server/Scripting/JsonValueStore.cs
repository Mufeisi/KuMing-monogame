using System.Collections.Concurrent;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Server.Scripting
{
    /// <summary>
    /// Values 持久化存储（选择：写回 JSON）。
    /// - 主存储：Envir/Values/*.json（与旧 *.txt 同目录同名，仅扩展名不同）
    /// - 当前口径：仅使用 JSON + C# 默认值，不再从 legacy *.txt(INI) 自动导入
    /// </summary>
    internal sealed class JsonValueStore
    {
        private sealed class CacheEntry
        {
            public Dictionary<string, Dictionary<string, string>> Data =
                new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);

            public DateTime JsonLastWriteTimeUtc;
        }

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        private readonly object _gate = new object();
        private readonly Dictionary<string, CacheEntry> _cache = new Dictionary<string, CacheEntry>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, object> _locks = new ConcurrentDictionary<string, object>(StringComparer.Ordinal);
        private readonly HashSet<string> _loggedErrors = new HashSet<string>(StringComparer.Ordinal);

        public bool TryGet(string normalizedTableKey, string jsonPath, string legacyTxtPath, bool allowLegacyImport, string section, string key, out string value)
        {
            value = string.Empty;

            if (string.IsNullOrWhiteSpace(normalizedTableKey))
                return false;

            if (string.IsNullOrWhiteSpace(key))
                return false;

            section ??= string.Empty;

            var entry = GetOrReload(normalizedTableKey, jsonPath, legacyTxtPath, allowLegacyImport);

            if (!entry.Data.TryGetValue(section, out var dict))
                return false;

            if (!dict.TryGetValue(key, out var v))
                return false;

            // 对齐 legacy InIReader.ReadString：空字符串视为“未命中”，会回落到 default 并可写回。
            if (string.IsNullOrEmpty(v))
                return false;

            value = v;
            return true;
        }

        public void Set(string normalizedTableKey, string jsonPath, string legacyTxtPath, bool allowLegacyImport, string section, string key, string value)
        {
            if (string.IsNullOrWhiteSpace(normalizedTableKey))
                throw new ArgumentException("normalizedTableKey 不能为空。", nameof(normalizedTableKey));

            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("key 不能为空。", nameof(key));

            section ??= string.Empty;
            value ??= string.Empty;

            var entry = GetOrReload(normalizedTableKey, jsonPath, legacyTxtPath, allowLegacyImport);

            if (!entry.Data.TryGetValue(section, out var dict))
            {
                dict = new Dictionary<string, string>(StringComparer.Ordinal);
                entry.Data.Add(section, dict);
            }

            dict[key] = value;

            WriteJsonSafe(jsonPath, entry.Data, normalizedTableKey);

            entry.JsonLastWriteTimeUtc = TryGetLastWriteTimeUtc(jsonPath);
            lock (_gate)
            {
                _cache[normalizedTableKey] = entry;
            }
        }

        private CacheEntry GetOrReload(string normalizedTableKey, string jsonPath, string legacyTxtPath, bool allowLegacyImport)
        {
            var tableLock = _locks.GetOrAdd(normalizedTableKey, _ => new object());

            lock (tableLock)
            {
                CacheEntry entry;

                lock (_gate)
                {
                    _cache.TryGetValue(normalizedTableKey, out entry);
                }

                var jsonWriteTime = TryGetLastWriteTimeUtc(jsonPath);

                if (entry != null &&
                    entry.JsonLastWriteTimeUtc == jsonWriteTime)
                {
                    return entry;
                }

                var data = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);

                if (!string.IsNullOrWhiteSpace(jsonPath) && File.Exists(jsonPath))
                {
                    if (TryReadJson(jsonPath, out var jsonData, out var error))
                    {
                        MergeInto(data, jsonData, overwriteWhenTargetEmpty: true);
                    }
                    else
                    {
                        LogOnce(normalizedTableKey, $"[Scripts] Values JSON 解析失败：{jsonPath} {error}");
                    }
                }

                if (!string.IsNullOrWhiteSpace(jsonPath) && !File.Exists(jsonPath))
                {
                    try
                    {
                        WriteJsonSafe(jsonPath, data, normalizedTableKey);
                        jsonWriteTime = TryGetLastWriteTimeUtc(jsonPath);
                    }
                    catch (Exception ex)
                    {
                        LogOnce(normalizedTableKey, $"[Scripts] Values JSON 写入失败：{jsonPath} {ex}");
                    }
                }

                var newEntry = new CacheEntry
                {
                    Data = data,
                    JsonLastWriteTimeUtc = jsonWriteTime,
                };

                lock (_gate)
                {
                    _cache[normalizedTableKey] = newEntry;
                }

                return newEntry;
            }
        }

        private static DateTime TryGetLastWriteTimeUtc(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path)) return DateTime.MinValue;
                if (!File.Exists(path)) return DateTime.MinValue;
                return File.GetLastWriteTimeUtc(path);
            }
            catch
            {
                return DateTime.MinValue;
            }
        }

        private static bool TryReadJson(string jsonPath, out Dictionary<string, Dictionary<string, string>> data, out string error)
        {
            data = null;
            error = string.Empty;

            try
            {
                var json = File.ReadAllText(jsonPath, Utf8NoBom);

                data = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(json, JsonOptions) ??
                       new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);

                // 清理 null value（避免后续 NRE）
                foreach (var section in data.Values)
                {
                    if (section == null) continue;

                    var keys = section.Keys.ToArray();
                    for (var i = 0; i < keys.Length; i++)
                    {
                        var k = keys[i];
                        section[k] ??= string.Empty;
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static bool MergeInto(Dictionary<string, Dictionary<string, string>> target, Dictionary<string, Dictionary<string, string>> source, bool overwriteWhenTargetEmpty)
        {
            if (source == null || source.Count == 0) return false;

            var changed = false;

            foreach (var sectionPair in source)
            {
                var section = sectionPair.Key ?? string.Empty;
                var dict = sectionPair.Value;
                if (dict == null) continue;

                if (!target.TryGetValue(section, out var targetDict))
                {
                    targetDict = new Dictionary<string, string>(StringComparer.Ordinal);
                    target.Add(section, targetDict);
                    changed = true;
                }

                foreach (var kv in dict)
                {
                    var key = kv.Key;
                    if (string.IsNullOrWhiteSpace(key)) continue;

                    var value = kv.Value ?? string.Empty;

                    if (!targetDict.TryGetValue(key, out var existing))
                    {
                        targetDict[key] = value;
                        changed = true;
                        continue;
                    }

                    if (overwriteWhenTargetEmpty && string.IsNullOrEmpty(existing) && !string.IsNullOrEmpty(value))
                    {
                        targetDict[key] = value;
                        changed = true;
                    }
                }
            }

            return changed;
        }

        private static void WriteJsonSafe(string jsonPath, Dictionary<string, Dictionary<string, string>> data, string normalizedTableKey)
        {
            if (string.IsNullOrWhiteSpace(jsonPath))
                return;

            var dir = Path.GetDirectoryName(jsonPath);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(data ?? new Dictionary<string, Dictionary<string, string>>(), JsonOptions);

            var temp = jsonPath + ".tmp";
            File.WriteAllText(temp, json, Utf8NoBom);
            File.Move(temp, jsonPath, overwrite: true);
        }

        private void LogOnce(string normalizedTableKey, string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;

            lock (_gate)
            {
                if (!_loggedErrors.Add(normalizedTableKey))
                    return;
            }

            MessageQueue.Instance.Enqueue(message);
        }
    }
}
