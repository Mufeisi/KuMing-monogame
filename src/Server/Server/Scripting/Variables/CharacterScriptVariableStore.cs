using System.Globalization;

namespace Server.Scripting.Variables
{
    public readonly struct CharacterScriptVariableEntry
    {
        public CharacterScriptVariableEntry(ScriptVariableScope scope, string key, ScriptVariableValue value)
        {
            if (scope != ScriptVariableScope.U && scope != ScriptVariableScope.T)
                throw new ArgumentOutOfRangeException(nameof(scope), "当前角色持久变量仅支持 U/T。");
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("持久变量键不能为空。", nameof(key));

            Scope = scope;
            Key = key;
            Value = value;
        }

        public ScriptVariableScope Scope { get; }
        public string Key { get; }
        public ScriptVariableValue Value { get; }
    }

    public sealed class CharacterScriptVariableStore
    {
        private const int MaximumEntries = 4096;
        private const int MaximumKeyLength = 96;
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
            string compositeKey = CompositeKey(scope, key);
            if (!_values.ContainsKey(compositeKey) && _values.Count >= MaximumEntries)
                throw new InvalidOperationException($"单角色持久变量不能超过 {MaximumEntries} 项。");
            _values[compositeKey] = value;
        }

        public int Clear(ScriptVariableScope scope)
        {
            string prefix = scope + ":";
            string[] keys = _values.Keys.Where(key => key.StartsWith(prefix, StringComparison.Ordinal)).ToArray();
            foreach (string key in keys) _values.Remove(key);
            return keys.Length;
        }

        public IReadOnlyList<CharacterScriptVariableEntry> Snapshot()
        {
            var result = new List<CharacterScriptVariableEntry>(_values.Count);
            foreach (var pair in _values.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                int separator = pair.Key.IndexOf(':');
                result.Add(new CharacterScriptVariableEntry(
                    Enum.Parse<ScriptVariableScope>(pair.Key.Substring(0, separator)),
                    pair.Key.Substring(separator + 1),
                    pair.Value));
            }
            return result;
        }

        public void Save(BinaryWriter writer)
        {
            if (writer == null) throw new ArgumentNullException(nameof(writer));
            IReadOnlyList<CharacterScriptVariableEntry> entries = Snapshot();
            writer.Write(entries.Count);
            foreach (var entry in entries)
            {
                writer.Write((byte)entry.Scope);
                writer.Write(entry.Key);
                writer.Write((byte)entry.Value.Kind);
                switch (entry.Value.Kind)
                {
                    case ScriptVariableKind.Integer:
                        writer.Write(entry.Value.Integer);
                        break;
                    case ScriptVariableKind.Decimal:
                        writer.Write(entry.Value.Format());
                        break;
                    case ScriptVariableKind.String:
                        writer.Write(entry.Value.Text);
                        break;
                    default:
                        throw new InvalidDataException($"不能持久化变量类型：{entry.Value.Kind}。");
                }
            }
        }

        public void Load(BinaryReader reader)
        {
            if (reader == null) throw new ArgumentNullException(nameof(reader));
            int count = reader.ReadInt32();
            if (count < 0 || count > MaximumEntries)
                throw new InvalidDataException($"角色持久变量数量无效：{count}。");

            var candidate = new Dictionary<string, ScriptVariableValue>(StringComparer.Ordinal);
            for (int i = 0; i < count; i++)
            {
                var scope = (ScriptVariableScope)reader.ReadByte();
                string key = NormalizeKey(reader.ReadString());
                var kind = (ScriptVariableKind)reader.ReadByte();
                ScriptVariableValue value = kind switch
                {
                    ScriptVariableKind.Integer => ScriptVariableValue.FromInteger(reader.ReadInt64()),
                    ScriptVariableKind.Decimal => ReadDecimal(reader.ReadString()),
                    ScriptVariableKind.String => ScriptVariableValue.FromString(reader.ReadString()),
                    _ => throw new InvalidDataException($"角色持久变量类型无效：{kind}。")
                };
                Validate(scope, key, value);
                if (!candidate.TryAdd(CompositeKey(scope, key), value))
                    throw new InvalidDataException($"角色持久变量重复：{scope}.{key}。");
            }

            _values.Clear();
            foreach (var pair in candidate) _values.Add(pair.Key, pair.Value);
        }

        private static ScriptVariableValue ReadDecimal(string text)
        {
            if (ScriptVariableValue.TryParseDecimal(text, out var value)) return value;
            throw new InvalidDataException($"角色持久小数无效：{text}。");
        }

        private static void Validate(ScriptVariableScope scope, string key, ScriptVariableValue value)
        {
            if (scope != ScriptVariableScope.U && scope != ScriptVariableScope.T)
                throw new InvalidDataException($"角色持久作用域无效：{scope}。");
            if (string.IsNullOrWhiteSpace(key) || key.Length > MaximumKeyLength || key.Contains(':'))
                throw new InvalidDataException("角色持久变量键无效。");
            if (value.Kind != ScriptVariableKind.Integer && value.Kind != ScriptVariableKind.Decimal &&
                value.Kind != ScriptVariableKind.String)
                throw new InvalidDataException($"角色持久变量类型无效：{value.Kind}。");
            if (scope == ScriptVariableScope.U && value.Kind == ScriptVariableKind.String)
                throw new InvalidDataException("U 变量只能保存整数或小数。");
            if (scope == ScriptVariableScope.T && value.Kind != ScriptVariableKind.String)
                throw new InvalidDataException("T 变量只能保存字符串。");
        }

        private static string NormalizeKey(string key)
        {
            if (key != null && key.StartsWith("#", StringComparison.Ordinal) &&
                int.TryParse(key.Substring(1), NumberStyles.None, CultureInfo.InvariantCulture, out int index) &&
                index >= 0 && index <= 499)
                return "#" + index.ToString(CultureInfo.InvariantCulture);
            if (ScriptVariableName.TryNormalize(key, out string normalized)) return normalized;
            throw new InvalidDataException("角色持久变量键无效。");
        }

        private static string CompositeKey(ScriptVariableScope scope, string key) => scope + ":" + key;
    }
}
