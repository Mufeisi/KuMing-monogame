using Server.MirDatabase;
using Server.MirEnvir;

namespace Server.Persistence
{
    /// <summary>
    /// Legacy 文件存档封装：用于迁移、回滚与 Legacy Provider 运行模式。
    /// 说明：
    /// - 封装现有二进制存档：`Server.MirDB` / `Server.MirADB` / `Guilds/*.mgd` / `Envir/Goods/*.msd` / `Conquests/*.mcd` / `Archive/*.MirCA`。
    /// - 迁移工具应优先通过该接口读取 legacy 数据（复用现有 BinaryReader 构造/Load 逻辑）。
    /// </summary>
    public interface ILegacyFileStore
    {
        bool LoadWorld(Envir envir);

        void SaveWorld(Envir envir);

        void LoadAccounts(Envir envir);

        void SaveAccounts(Envir envir);

        void LoadGuilds(Envir envir);

        void SaveGuilds(Envir envir, bool forced);

        void LoadConquests(Envir envir);

        void SaveConquests(Envir envir, bool forced);

        void SaveArchivedCharacter(Envir envir, CharacterInfo info);

        CharacterInfo GetArchivedCharacter(Envir envir, string name);
    }
}

