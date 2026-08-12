using Server.Persistence.Sql;

namespace Server.Persistence
{
    /// <summary>
    /// SQL 存储抽象：统一 SQLite / MySQL 的连接与会话入口（内部基于 Dapper + 方言执行）。
    /// 注意：该接口只负责“打开会话/执行 SQL”的边界，具体业务表的读写由上层 Repository 实现。
    /// </summary>
    public interface ISqlStore
    {
        DatabaseProviderKind Provider { get; }

        SqlDatabaseOptions Options { get; }

        ISqlDialect Dialect { get; }

        SqlSession OpenSession(SqlSessionOptions sessionOptions = null);
    }
}

