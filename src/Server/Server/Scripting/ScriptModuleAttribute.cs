namespace Server.Scripting
{
    /// <summary>
    /// 可选：为脚本模块声明 “Key + 是否自动注册” 元信息。
    /// 用途：
    /// - AutoRegister=false：模块不会被 <see cref="ScriptManager"/> 自动执行 Register，需由其它模块通过 <see cref="ScriptRegistry.Import(string)"/> 或 <see cref="ScriptRegistry.Import{TModule}"/> 组合导入。
    /// - Key：用于 <see cref="ScriptRegistry.Import(string)"/> 按 Key 导入（Key 会按 <see cref="LogicKey"/> 归一化）。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class ScriptModuleAttribute : Attribute
    {
        public string Key { get; }
        public bool AutoRegister { get; }

        public ScriptModuleAttribute()
        {
            Key = string.Empty;
            AutoRegister = true;
        }

        public ScriptModuleAttribute(string key)
        {
            Key = key ?? string.Empty;
            AutoRegister = true;
        }

        public ScriptModuleAttribute(bool autoRegister)
        {
            Key = string.Empty;
            AutoRegister = autoRegister;
        }

        public ScriptModuleAttribute(string key, bool autoRegister)
        {
            Key = key ?? string.Empty;
            AutoRegister = autoRegister;
        }
    }
}

