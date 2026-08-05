using System;
using System.IO;
using Microsoft.Data.Sqlite;
using MySqlConnector;

namespace Server.Persistence.Sql
{
    internal static class SqlTransientDetector
    {
        public static bool IsTransient(DatabaseProviderKind provider, Exception ex)
        {
            if (ex == null) return false;

            if (provider == DatabaseProviderKind.MySql)
            {
                if (ex is MySqlException mySqlEx)
                    return mySqlEx.IsTransient;
            }

            if (provider == DatabaseProviderKind.Sqlite)
            {
                if (ex is SqliteException sqliteEx)
                {
                    // SQLITE_BUSY(5)/SQLITE_LOCKED(6) 通常可通过重试恢复。
                    if (sqliteEx.SqliteErrorCode == 5 || sqliteEx.SqliteErrorCode == 6)
                        return true;
                }
            }

            if (ex is TimeoutException) return true;
            if (ex is IOException) return true;

            return ex.InnerException != null && IsTransient(provider, ex.InnerException);
        }
    }
}

