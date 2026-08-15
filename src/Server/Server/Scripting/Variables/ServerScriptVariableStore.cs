using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Server.Scripting.Variables
{
    public readonly struct ServerScriptVariableEntry
    {
        public ServerScriptVariableEntry(ScriptVariableScope scope, string key, ScriptVariableValue value)
        {
            Scope = scope;
            Key = key ?? string.Empty;
            Value = value;
        }

        public ScriptVariableScope Scope { get; }
        public string Key { get; }
        public ScriptVariableValue Value { get; }
    }

    public sealed class ServerScriptVariableStore
    {
        private const int MaximumEntries = 8192;
        private readonly Dictionary<string, ScriptVariableValue> _values =
            new Dictionary<string, ScriptVariableValue>(StringComparer.Ordinal);

        public int Count => _values.Count;

        public bool TryGet(ScriptVariableScope scope, string key, out ScriptVariableValue value)
        {
            value = default;
            try { return _values.TryGetValue(CompositeKey(scope, NormalizeKey(key)), out value); }
            catch (InvalidDataException) { return false; }
        }

        public void Set(ScriptVariableScope scope, string key, ScriptVariableValue value)
        {
            key = NormalizeKey(key);
            Validate(scope, key, value);
            string composite = CompositeKey(scope, key);
            if (!_values.ContainsKey(composite) && _values.Count >= MaximumEntries)
                throw new InvalidOperationException($"服务器持久变量不能超过 {MaximumEntries} 项。");
            _values[composite] = value;
        }

        public int Clear(ScriptVariableScope scope)
        {
            string prefix = scope + ":";
            string[] keys = _values.Keys.Where(key => key.StartsWith(prefix, StringComparison.Ordinal)).ToArray();
            foreach (string key in keys) _values.Remove(key);
            return keys.Length;
        }

        public IReadOnlyList<ServerScriptVariableEntry> Snapshot() => _values
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair =>
            {
                int separator = pair.Key.IndexOf(':');
                return new ServerScriptVariableEntry(
                    Enum.Parse<ScriptVariableScope>(pair.Key.Substring(0, separator)),
                    pair.Key.Substring(separator + 1),
                    pair.Value);
            })
            .ToArray();

        public void Replace(IEnumerable<ServerScriptVariableEntry> entries)
        {
            var candidate = new ServerScriptVariableStore();
            foreach (var entry in entries ?? Array.Empty<ServerScriptVariableEntry>())
                candidate.Set(entry.Scope, entry.Key, entry.Value);
            _values.Clear();
            foreach (var pair in candidate._values) _values.Add(pair.Key, pair.Value);
        }

        public void SaveJson(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("保存路径不能为空。", nameof(path));
            string fullPath = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath) ?? ".");
            string temporary = fullPath + ".tmp";
            string backup = fullPath + ".bak";
            var document = new JsonDocumentModel
            {
                SchemaVersion = 1,
                Variables = Snapshot().Select(JsonEntryModel.FromEntry).ToList(),
            };

            try
            {
                using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    JsonSerializer.Serialize(stream, document, JsonOptions);
                    stream.Flush(flushToDisk: true);
                }
                if (File.Exists(fullPath)) File.Copy(fullPath, backup, overwrite: true);
                File.Move(temporary, fullPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        public void LoadJson(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("加载路径不能为空。", nameof(path));
            string fullPath = Path.GetFullPath(path);
            string backup = fullPath + ".bak";
            if (!File.Exists(fullPath) && !File.Exists(backup))
            {
                Replace(Array.Empty<ServerScriptVariableEntry>());
                return;
            }

            Exception primaryError = null;
            if (TryLoadDocument(fullPath, out var entries, out primaryError))
            {
                Replace(entries);
                return;
            }
            if (TryLoadDocument(backup, out entries, out var backupError))
            {
                Replace(entries);
                return;
            }
            throw new InvalidDataException("服务器变量主文件和备份均无法读取。", backupError ?? primaryError);
        }

        private static bool TryLoadDocument(
            string path,
            out IReadOnlyList<ServerScriptVariableEntry> entries,
            out Exception error)
        {
            entries = Array.Empty<ServerScriptVariableEntry>();
            error = null;
            if (!File.Exists(path)) return false;
            try
            {
                using var stream = File.OpenRead(path);
                JsonDocumentModel document = JsonSerializer.Deserialize<JsonDocumentModel>(stream, JsonOptions);
                if (document == null || document.SchemaVersion != 1)
                    throw new InvalidDataException("服务器变量 JSON 版本无效。");
                var parsedEntries = (document.Variables ?? new List<JsonEntryModel>())
                    .Select(model => model.ToEntry())
                    .ToArray();
                // 在接受主文件前完整校验作用域、类型、键名与容量；否则应继续尝试 .bak。
                var validated = new ServerScriptVariableStore();
                validated.Replace(parsedEntries);
                entries = validated.Snapshot();
                return true;
            }
            catch (Exception ex)
            {
                error = ex;
                return false;
            }
        }

        private static void Validate(ScriptVariableScope scope, string key, ScriptVariableValue value)
        {
            if (scope != ScriptVariableScope.G && scope != ScriptVariableScope.A)
                throw new InvalidDataException($"服务器持久作用域无效：{scope}。");
            if (scope == ScriptVariableScope.G && value.Kind == ScriptVariableKind.String)
                throw new InvalidDataException("G 变量只能保存整数或小数。");
            if (scope == ScriptVariableScope.A && value.Kind != ScriptVariableKind.String)
                throw new InvalidDataException("A 变量只能保存字符串。");
        }

        private static string NormalizeKey(string key)
        {
            if (key != null && key.StartsWith("#", StringComparison.Ordinal) &&
                int.TryParse(key.Substring(1), NumberStyles.None, CultureInfo.InvariantCulture, out int index) &&
                index >= 0 && index <= 999)
                return "#" + index.ToString(CultureInfo.InvariantCulture);
            if (ScriptVariableName.TryNormalize(key, out string normalized)) return normalized;
            throw new InvalidDataException("服务器持久变量键无效。");
        }

        private static string CompositeKey(ScriptVariableScope scope, string key) => scope + ":" + key;

        private sealed class JsonDocumentModel
        {
            public int SchemaVersion { get; set; }
            public List<JsonEntryModel> Variables { get; set; } = new List<JsonEntryModel>();
        }

        private sealed class JsonEntryModel
        {
            public string Namespace { get; set; }
            public string Key { get; set; }
            public string Kind { get; set; }
            public long IntegerValue { get; set; }
            public string DecimalValue { get; set; }
            public string TextValue { get; set; }

            public static JsonEntryModel FromEntry(ServerScriptVariableEntry entry) => new JsonEntryModel
            {
                Namespace = entry.Scope.ToString(),
                Key = entry.Key,
                Kind = entry.Value.Kind.ToString(),
                IntegerValue = entry.Value.Kind == ScriptVariableKind.Integer ? entry.Value.Integer : 0,
                DecimalValue = entry.Value.Kind == ScriptVariableKind.Decimal ? entry.Value.Format() : string.Empty,
                TextValue = entry.Value.Kind == ScriptVariableKind.String ? entry.Value.Text : string.Empty,
            };

            public ServerScriptVariableEntry ToEntry()
            {
                if (!Enum.TryParse(Namespace, true, out ScriptVariableScope scope) ||
                    !Enum.TryParse(Kind, true, out ScriptVariableKind kind))
                    throw new InvalidDataException("服务器变量 JSON 的作用域或类型无效。");
                ScriptVariableValue value = kind switch
                {
                    ScriptVariableKind.Integer => ScriptVariableValue.FromInteger(IntegerValue),
                    ScriptVariableKind.Decimal when ScriptVariableValue.TryParseDecimal(DecimalValue, out var parsed) => parsed,
                    ScriptVariableKind.String => ScriptVariableValue.FromString(TextValue),
                    _ => throw new InvalidDataException("服务器变量 JSON 的值无效。")
                };
                return new ServerScriptVariableEntry(scope, Key, value);
            }
        }

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
    }
}
