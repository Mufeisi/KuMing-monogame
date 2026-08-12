using System;
using Server.MirDatabase;
using Server.MirEnvir;

namespace Server.Persistence.Legacy
{
    /// <summary>
    /// Legacy 文件存档的默认实现（基于 Envir.Legacy_*）。
    /// 说明：
    /// - 迁移工具应优先通过 <see cref="ILegacyFileStore"/> 读取 legacy 数据，而不是直接调用 Envir 内部方法。
    /// - 本类位于 Server.Library 程序集内，可访问 Envir 的 internal Legacy_* 方法。
    /// </summary>
    public sealed class LegacyFileStore : ILegacyFileStore
    {
        public bool LoadWorld(Envir envir)
        {
            if (envir == null) throw new ArgumentNullException(nameof(envir));
            return envir.Legacy_LoadDB();
        }

        public void SaveWorld(Envir envir)
        {
            if (envir == null) throw new ArgumentNullException(nameof(envir));
            envir.Legacy_SaveDB();
        }

        public void LoadAccounts(Envir envir)
        {
            if (envir == null) throw new ArgumentNullException(nameof(envir));
            envir.Legacy_LoadAccounts();
        }

        public void SaveAccounts(Envir envir)
        {
            if (envir == null) throw new ArgumentNullException(nameof(envir));
            envir.Legacy_SaveAccounts();
        }

        public void LoadGuilds(Envir envir)
        {
            if (envir == null) throw new ArgumentNullException(nameof(envir));
            envir.Legacy_LoadGuilds();
        }

        public void SaveGuilds(Envir envir, bool forced)
        {
            if (envir == null) throw new ArgumentNullException(nameof(envir));
            envir.Legacy_SaveGuilds(forced);
        }

        public void LoadConquests(Envir envir)
        {
            if (envir == null) throw new ArgumentNullException(nameof(envir));
            envir.Legacy_LoadConquests();
        }

        public void SaveConquests(Envir envir, bool forced)
        {
            if (envir == null) throw new ArgumentNullException(nameof(envir));
            envir.Legacy_SaveConquests(forced);
        }

        public void SaveArchivedCharacter(Envir envir, CharacterInfo info)
        {
            if (envir == null) throw new ArgumentNullException(nameof(envir));
            if (info == null) return;
            envir.Legacy_SaveArchivedCharacter(info);
        }

        public CharacterInfo GetArchivedCharacter(Envir envir, string name)
        {
            if (envir == null) throw new ArgumentNullException(nameof(envir));
            return envir.Legacy_GetArchivedCharacter(name);
        }
    }
}

