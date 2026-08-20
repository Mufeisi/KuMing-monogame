namespace Server.Scripting
{
    /// <summary>
    /// C# 版变量/数值表定义（Key = Values/&lt;FileName&gt;，用于替代 Envir/Values/*.txt）。
    /// 当前目标：先做到 1:1 对齐现有 InIReader(LoadValue/SaveValue) 的表达能力。
    /// </summary>
    public sealed class ValueTableDefinition
    {
        public string Key { get; }

        private readonly Dictionary<string, Dictionary<string, string>> _sections =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);

        public ValueTableDefinition(string key)
        {
            Key = LogicKey.NormalizeOrThrow(key);
        }

        public ValueTableDefinition Set(string section, string key, string value)
        {
            section ??= string.Empty;

            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("key 不能为空。", nameof(key));

            if (!_sections.TryGetValue(section, out var dict))
            {
                dict = new Dictionary<string, string>(StringComparer.Ordinal);
                _sections.Add(section, dict);
            }

            dict[key] = value ?? string.Empty;
            return this;
        }

        public bool TryGet(string section, string key, out string value)
        {
            value = string.Empty;

            if (string.IsNullOrWhiteSpace(key))
                return false;

            section ??= string.Empty;

            if (!_sections.TryGetValue(section, out var dict))
                return false;

            return dict.TryGetValue(key, out value);
        }
    }
}

