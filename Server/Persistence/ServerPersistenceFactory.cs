using System;
using Server;
using Server.Persistence.Legacy;
using Server.Persistence.Sql;

namespace Server.Persistence
{
    public static class ServerPersistenceFactory
    {
        public static DatabaseProviderKind ParseProvider(string provider)
        {
            if (string.IsNullOrWhiteSpace(provider))
                return DatabaseProviderKind.Legacy;

            provider = provider.Trim();

            if (provider.Equals("Legacy", StringComparison.OrdinalIgnoreCase))
                return DatabaseProviderKind.Legacy;
            if (provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase) || provider.Equals("SQLite", StringComparison.OrdinalIgnoreCase))
                return DatabaseProviderKind.Sqlite;
            if (provider.Equals("MySql", StringComparison.OrdinalIgnoreCase) || provider.Equals("MySQL", StringComparison.OrdinalIgnoreCase))
                return DatabaseProviderKind.MySql;

            return DatabaseProviderKind.Legacy;
        }

        public static IServerPersistence CreateFromSettings()
        {
            var provider = ParseProvider(Settings.DatabaseProvider);
            return Create(provider);
        }

        public static IServerPersistence Create(DatabaseProviderKind provider)
        {
            return provider switch
            {
                DatabaseProviderKind.Legacy => new LegacyServerPersistence(),
                DatabaseProviderKind.Sqlite => new SqlServerPersistence(DatabaseProviderKind.Sqlite),
                DatabaseProviderKind.MySql => new SqlServerPersistence(DatabaseProviderKind.MySql),
                _ => new LegacyServerPersistence(),
            };
        }
    }
}

