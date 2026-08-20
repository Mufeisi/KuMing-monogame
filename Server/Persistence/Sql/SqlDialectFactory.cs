namespace Server.Persistence.Sql
{
    public static class SqlDialectFactory
    {
        public static ISqlDialect Create(DatabaseProviderKind provider)
        {
            return provider switch
            {
                DatabaseProviderKind.Sqlite => new SqliteDialect(),
                DatabaseProviderKind.MySql => new MySqlDialect(),
                _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, "不支持的数据库 Provider。"),
            };
        }
    }
}

