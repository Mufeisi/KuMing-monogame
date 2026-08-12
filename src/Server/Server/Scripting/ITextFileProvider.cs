namespace Server.Scripting
{
    public interface ITextFileProvider
    {
        /// <summary>
        /// 获取所有文本文件定义（用于诊断/导出/迁移检查）。
        /// </summary>
        IReadOnlyCollection<TextFileDefinition> GetAll();

        /// <summary>
        /// 按 Key 获取文本定义（Key 约定：与 Envir 下相对路径一致；根目录文件如 Notice.txt 的 Key 为 notice）。
        /// key 会按 <see cref="LogicKey"/> 归一化；找不到则返回 null。
        /// </summary>
        TextFileDefinition GetByKey(string key);
    }
}

