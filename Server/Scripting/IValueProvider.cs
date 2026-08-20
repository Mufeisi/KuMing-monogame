namespace Server.Scripting
{
    public interface IValueProvider
    {
        /// <summary>
        /// 获取所有数值表定义（用于诊断/导出/迁移检查）。
        /// </summary>
        IReadOnlyCollection<ValueTableDefinition> GetAll();

        /// <summary>
        /// 按 Key/section/key 读取值（Key 约定：Values/&lt;FileName&gt;）。
        /// tableKey 会按 <see cref="LogicKey"/> 归一化；找不到则返回 false。
        /// </summary>
        bool TryGet(string tableKey, string section, string key, out string value);
    }
}

