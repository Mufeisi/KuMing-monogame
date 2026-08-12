using Server.MirDatabase;

namespace Server.Scripting
{
    public interface IDropTableProvider
    {
        /// <summary>
        /// 按 Key 获取掉落表（Key 约定：Drops/&lt;FileName&gt;）。
        /// key 会按 <see cref="LogicKey"/> 归一化；找不到则返回 null。
        /// </summary>
        IReadOnlyList<DropInfo> Get(string key);
    }
}

