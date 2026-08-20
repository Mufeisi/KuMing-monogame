using System;
using Server;
using Server.Persistence.Sql;
using Server.MirEnvir;

namespace Server.Persistence
{
    public static class ServerPersistenceFactory
    {
        public static DatabaseProviderKind ParseProvider(string provider)
        {
            if (string.IsNullOrWhiteSpace(provider))
                throw new InvalidOperationException("Database.Provider 不能为空；必须显式配置 Sqlite 或 MySql。");

            provider = provider.Trim();

            if (provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase) || provider.Equals("SQLite", StringComparison.OrdinalIgnoreCase))
                return DatabaseProviderKind.Sqlite;
            if (provider.Equals("MySql", StringComparison.OrdinalIgnoreCase) || provider.Equals("MySQL", StringComparison.OrdinalIgnoreCase))
                return DatabaseProviderKind.MySql;

            throw new InvalidOperationException($"不支持的 Database.Provider：'{provider}'。允许值仅为 Sqlite 或 MySql。");
        }

        public static IGamePersistence CreateFromSettings(Envir envir)
        {
            var provider = ParseProvider(Settings.DatabaseProvider);
            return Create(provider, envir);
        }

        public static IGamePersistence Create(DatabaseProviderKind provider, Envir envir)
        {
            return provider switch
            {
                DatabaseProviderKind.Sqlite => new SqlServerPersistence(DatabaseProviderKind.Sqlite, new EnvirServerStatePort(envir)),
                DatabaseProviderKind.MySql => new SqlServerPersistence(DatabaseProviderKind.MySql, new EnvirServerStatePort(envir)),
                _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, "不支持的数据库 Provider。"),
            };
        }
    }
}
