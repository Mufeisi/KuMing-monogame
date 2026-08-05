namespace Server.Persistence.Sql
{
    public sealed class SqlDatabaseOptions
    {
        public string SqlitePath { get; set; } = ".\\Data\\server.db";

        public string MySqlConnectionString { get; set; } = string.Empty;

        /// <summary>
        /// -1=不覆盖（使用连接串/驱动默认），0=禁用，1=启用
        /// </summary>
        public int MySqlPooling { get; set; } = -1;

        public int MySqlMinPoolSize { get; set; } = 0;

        public int MySqlMaxPoolSize { get; set; } = 0;

        public int MySqlConnectionTimeoutSeconds { get; set; } = 0;

        public int MySqlKeepAliveSeconds { get; set; } = 0;

        public int MySqlConnectionIdleTimeoutSeconds { get; set; } = 0;

        public int MySqlConnectionLifeTimeSeconds { get; set; } = 0;

        public int CommandTimeoutSeconds { get; set; } = 30;
    }
}
