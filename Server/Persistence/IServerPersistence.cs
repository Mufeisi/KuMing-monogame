using Server.MirDatabase;
using Server.MirEnvir;

namespace Server.Persistence
{
    /// <summary>
    /// 服务端持久化入口：负责按数据域加载/保存整套服务器数据。
    /// 说明：
    /// - 目标是把 Envir.cs 中散落的文件读写收拢到“持久化层”，使后端可切换（Legacy/SQLite/MySQL）。
    /// - 本接口只定义边界，不强制具体线程模型（同步/异步由实现与调用方协商）。
    /// </summary>
    public interface IServerPersistence
    {
        DatabaseProviderKind Provider { get; }

        bool LoadWorld(Envir envir);

        void SaveWorld(Envir envir);

        void LoadAccounts(Envir envir);

        void BeginSaveAccounts(Envir envir);

        void SaveAccounts(Envir envir);

        void LoadGuilds(Envir envir);

        void SaveGuilds(Envir envir, bool forced);

        void SaveGoods(Envir envir, bool forced);

        void LoadConquests(Envir envir);

        void SaveConquests(Envir envir, bool forced);

        void SaveArchivedCharacter(Envir envir, CharacterInfo info);

        CharacterInfo GetArchivedCharacter(Envir envir, string name);
    }
}
