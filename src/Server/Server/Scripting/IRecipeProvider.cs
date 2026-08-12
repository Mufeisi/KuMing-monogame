namespace Server.Scripting
{
    public interface IRecipeProvider
    {
        /// <summary>
        /// 获取所有配方定义（用于诊断/导出/迁移检查）。
        /// </summary>
        IReadOnlyCollection<RecipeDefinition> GetAll();

        /// <summary>
        /// 按 Key 获取配方定义（Key 约定：Recipe/&lt;FileName&gt;）。
        /// key 会按 <see cref="LogicKey"/> 归一化；找不到则返回 null。
        /// </summary>
        RecipeDefinition GetByKey(string key);
    }
}

