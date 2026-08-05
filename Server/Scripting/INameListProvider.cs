namespace Server.Scripting
{
    public interface INameListProvider
    {
        /// <summary>
        /// 获取所有名单定义（用于诊断/导出/迁移检查）。
        /// </summary>
        IReadOnlyCollection<NameListDefinition> GetAll();

        /// <summary>
        /// 按 Key 获取名单（Key 约定：NameLists/&lt;FileName&gt;）。
        /// key 会按 <see cref="LogicKey"/> 归一化；找不到则返回 null。
        /// </summary>
        NameListDefinition GetByKey(string key);
    }
}

