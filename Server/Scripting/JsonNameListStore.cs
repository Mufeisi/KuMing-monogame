using System.Collections.Concurrent;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Server.Scripting
{
    /// <summary>
    /// NameLists 持久化存储（选择：写回 JSON）。
    /// - 主存储：Envir/NameLists/*.json（与旧文本文件同目录同名，仅扩展名不同）
    /// - 当前口径：仅使用 JSON + C# seed，不再从 legacy 文本自动导入
    /// </summary>
    internal sealed class JsonNameListStore
    {
        private sealed class CacheEntry
        {
            public HashSet<string> Values = new HashSet<string>(StringComparer.Ordinal);

            public DateTime JsonLastWriteTimeUtc;
            public long SeedVersion;
            public bool SeedDefined;
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

        public bool Contains(
            string normalizedListKey,
            string jsonPath,
            string legacyTxtPath,
            bool allowLegacyImport,
            IReadOnlyCollection<string> seed,
            long seedVersion,
            string value)
        {
            value ??= string.Empty;

            var entry = GetOrReload(normalizedListKey, jsonPath, legacyTxtPath, allowLegacyImport, seed, seedVersion);
            if (entry == null) return false;

            return entry.Values.Contains(value);
        }

        public bool Add(
            string normalizedListKey,
            string jsonPath,
            string legacyTxtPath,
            bool allowLegacyImport,
            IReadOnlyCollection<string> seed,
            long seedVersion,
            string value)
        {
            value ??= string.Empty;

            if (string.IsNullOrWhiteSpace(normalizedListKey))
                return false;

            var listLock = _locks.GetOrAdd(normalizedListKey, _ => new object());

            lock (listLock)
            {
                var entry = GetOrReloadUnderLock(normalizedListKey, jsonPath, legacyTxtPath, allowLegacyImport, seed, seedVersion);
                if (entry == null) return false;

                entry.Values ??= new HashSet<string>(StringComparer.Ordinal);

                var changed = entry.Values.Add(value);

                WriteJsonSafe(jsonPath, entry.Values, normalizedListKey);

                entry.JsonLastWriteTimeUtc = TryGetLastWriteTimeUtc(jsonPath);
                entry.SeedVersion = seedVersion;
                entry.SeedDefined = seed != null;

                lock (_gate)
                {
                    _cache[normalizedListKey] = entry;
                }

                // legacy 行为：即便已存在也返回 true。
                return true;
            }
        }

        public bool Remove(
            string normalizedListKey,
            string jsonPath,
            string legacyTxtPath,
            bool allowLegacyImport,
            IReadOnlyCollection<string> seed,
            long seedVersion,
            string value)
        {
            value ??= string.Empty;

            if (string.IsNullOrWhiteSpace(normalizedListKey))
                return false;

            var listLock = _locks.GetOrAdd(normalizedListKey, _ => new object());

            lock (listLock)
            {
                var entry = GetOrReloadUnderLock(normalizedListKey, jsonPath, legacyTxtPath, allowLegacyImport, seed, seedVersion);
                if (entry == null) return false;

                var exists = seed != null ||
                             entry.JsonLastWriteTimeUtc != DateTime.MinValue;
                if (!exists) return false;

                entry.Values ??= new HashSet<string>(StringComparer.Ordinal);

                entry.Values.Remove(value);

                WriteJsonSafe(jsonPath, entry.Values, normalizedListKey);

                entry.JsonLastWriteTimeUtc = TryGetLastWriteTimeUtc(jsonPath);
                entry.SeedVersion = seedVersion;
                entry.SeedDefined = seed != null;

                lock (_gate)
                {
                    _cache[normalizedListKey] = entry;
                }

                // legacy 行为：即便未命中也返回 true（只要名单存在/可定义）。
                return true;
            }
        }

        public bool Clear(
            string normalizedListKey,
            string jsonPath,
            string legacyTxtPath,
            bool allowLegacyImport,
            IReadOnlyCollection<string> seed,
            long seedVersion)
        {
            if (string.IsNullOrWhiteSpace(normalizedListKey))
                return false;

            var listLock = _locks.GetOrAdd(normalizedListKey, _ => new object());

            lock (listLock)
            {
                var entry = GetOrReloadUnderLock(normalizedListKey, jsonPath, legacyTxtPath, allowLegacyImport, seed, seedVersion);
                if (entry == null) return false;

                var exists = seed != null ||
                             entry.JsonLastWriteTimeUtc != DateTime.MinValue;
                if (!exists) return false;

                entry.Values = new HashSet<string>(StringComparer.Ordinal);

                WriteJsonSafe(jsonPath, entry.Values, normalizedListKey);

                entry.JsonLastWriteTimeUtc = TryGetLastWriteTimeUtc(jsonPath);
                entry.SeedVersion = seedVersion;
                entry.SeedDefined = seed != null;

                lock (_gate)
                {
                    _cache[normalizedListKey] = entry;
                }

                return true;
            }
        }

        private CacheEntry GetOrReload(
            string normalizedListKey,
            string jsonPath,
            string legacyTxtPath,
            bool allowLegacyImport,
            IReadOnlyCollection<string> seed,
            long seedVersion)
        {
            if (string.IsNullOrWhiteSpace(normalizedListKey))
                return null;

            var listLock = _locks.GetOrAdd(normalizedListKey, _ => new object());

            lock (listLock)
            {
                return GetOrReloadUnderLock(normalizedListKey, jsonPath, legacyTxtPath, allowLegacyImport, seed, seedVersion);
            }
        }

        private CacheEntry GetOrReloadUnderLock(
            string normalizedListKey,
            string jsonPath,
            string legacyTxtPath,
            bool allowLegacyImport,
            IReadOnlyCollection<string> seed,
            long seedVersion)
        {
            CacheEntry entry;

            lock (_gate)
            {
                _cache.TryGetValue(normalizedListKey, out entry);
            }

            var jsonWriteTime = TryGetLastWriteTimeUtc(jsonPath);
            var seedDefined = seed != null;

            if (entry != null &&
                entry.JsonLastWriteTimeUtc == jsonWriteTime &&
                entry.SeedVersion == seedVersion &&
                entry.SeedDefined == seedDefined)
            {
                return entry;
            }

            var values = new HashSet<string>(StringComparer.Ordinal);

            if (jsonWriteTime != DateTime.MinValue)
            {
                if (!TryReadJson(jsonPath, out values, out var error) && !string.IsNullOrWhiteSpace(error))
                {
                    LogOnce(normalizedListKey, $"[Scripts] NameLists JSON 读取失败：key={normalizedListKey} err={error}");
                }
            }
            else if (seedDefined)
            {
                foreach (var v in seed)
                {
                    if (string.IsNullOrWhiteSpace(v)) continue;
                    values.Add(v);
                }
            }

            entry = new CacheEntry
            {
                Values = values,
                JsonLastWriteTimeUtc = jsonWriteTime,
                SeedVersion = seedVersion,
                SeedDefined = seedDefined,
            };

            lock (_gate)
            {
                _cache[normalizedListKey] = entry;
            }

            return entry;
        }

        private static DateTime TryGetLastWriteTimeUtc(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return DateTime.MinValue;

            try
            {
                return File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
            }
            catch
            {
                return DateTime.MinValue;
            }
        }

        private static bool TryReadJson(string jsonPath, out HashSet<string> values, out string error)
        {
            values = new HashSet<string>(StringComparer.Ordinal);
            error = string.Empty;

            try
            {
                if (string.IsNullOrWhiteSpace(jsonPath) || !File.Exists(jsonPath))
                    return true;

                var json = File.ReadAllText(jsonPath, Encoding.UTF8);

                var list = JsonSerializer.Deserialize<List<string>>(json, JsonOptions) ?? new List<string>();

                for (var i = 0; i < list.Count; i++)
                {
                    var v = list[i];
                    if (string.IsNullOrWhiteSpace(v)) continue;
                    values.Add(v);
                }

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static void WriteJsonSafe(string jsonPath, HashSet<string> values, string normalizedListKey)
        {
            if (string.IsNullOrWhiteSpace(jsonPath))
                return;

            try
            {
                var dir = Path.GetDirectoryName(jsonPath);
                if (!string.IsNullOrWhiteSpace(dir))
                    Directory.CreateDirectory(dir);

                var list = (values ?? new HashSet<string>(StringComparer.Ordinal))
                    .Where(v => !string.IsNullOrWhiteSpace(v))
                    .ToList();

                list.Sort(StringComparer.Ordinal);

                var json = JsonSerializer.Serialize(list, JsonOptions);

                var temp = jsonPath + ".tmp";
                File.WriteAllText(temp, json, Utf8NoBom);
                File.Move(temp, jsonPath, overwrite: true);
            }
            catch (Exception ex)
            {
                if (!string.IsNullOrWhiteSpace(normalizedListKey))
                {
                    MessageQueue.Instance.Enqueue($"[Scripts] NameLists JSON 写入失败：key={normalizedListKey} err={ex.Message}");
                }
            }
        }

        private void LogOnce(string normalizedListKey, string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;

            lock (_gate)
            {
                if (!_loggedErrors.Add(normalizedListKey))
                    return;
            }

            MessageQueue.Instance.Enqueue(message);
        }
    }
}
