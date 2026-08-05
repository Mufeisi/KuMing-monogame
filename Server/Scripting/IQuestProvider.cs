namespace Server.Scripting
{
    public interface IQuestProvider
    {
        /// <summary>
        /// 获取所有任务定义（用于诊断/导出/迁移检查）。
        /// </summary>
        IReadOnlyCollection<QuestDefinition> GetAll();

        /// <summary>
        /// 按 Key 获取任务定义（Key 约定：Quests/&lt;FileName&gt;）。
        /// key 会按 <see cref="LogicKey"/> 归一化；找不到则返回 null。
        /// </summary>
        QuestDefinition GetByKey(string key);
    }
}

