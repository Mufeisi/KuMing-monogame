using System;
using Server.MirDatabase;
using Server.MirEnvir;

namespace Server.Persistence.Legacy
{
    public sealed class LegacyServerPersistence : IServerPersistence
    {
        public DatabaseProviderKind Provider => DatabaseProviderKind.Legacy;

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

        public void BeginSaveAccounts(Envir envir)
        {
            if (envir == null) throw new ArgumentNullException(nameof(envir));
            envir.Legacy_BeginSaveAccounts();
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

        public void SaveGoods(Envir envir, bool forced)
        {
            if (envir == null) throw new ArgumentNullException(nameof(envir));
            envir.Legacy_SaveGoods(forced);
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

