namespace Server.Scripting
{
    /// <summary>
    /// C# 版本名单定义（Key = NameLists/&lt;FileName&gt;），用于替代 Envir/NameLists/* 的只读内容。
    /// 注意：引擎侧仍可能允许运行期写入（ADD/DEL/CLEAR），此类用于提供默认名单/热更替换。
    /// </summary>
    public sealed class NameListDefinition
    {
        public string Key { get; }

        private readonly HashSet<string> _values = new HashSet<string>(StringComparer.Ordinal);

        public IReadOnlyCollection<string> Values => _values;

        public NameListDefinition(string key)
        {
            Key = LogicKey.NormalizeOrThrow(key);
        }

        public NameListDefinition Add(string value)
        {
            _values.Add(value ?? string.Empty);
            return this;
        }

        public bool Contains(string value)
        {
            return _values.Contains(value ?? string.Empty);
        }
    }
}

