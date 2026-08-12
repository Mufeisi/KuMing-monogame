namespace Server.Scripting
{
    public interface IRouteProvider
    {
        /// <summary>
        /// 获取所有路线定义（用于诊断/导出/迁移检查）。
        /// </summary>
        IReadOnlyCollection<RouteDefinition> GetAll();

        /// <summary>
        /// 按 Key 获取路线定义（Key 约定：Routes/&lt;FileName&gt;）。
        /// key 会按 <see cref="LogicKey"/> 归一化；找不到则返回 null。
        /// </summary>
        RouteDefinition GetByKey(string key);
    }
}

