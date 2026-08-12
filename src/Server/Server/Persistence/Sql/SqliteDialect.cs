using System;
using System.Collections.Generic;
using System.Linq;

namespace Server.Persistence.Sql
{
    internal sealed class SqliteDialect : ISqlDialect
    {
        public DatabaseProviderKind Provider => DatabaseProviderKind.Sqlite;

        public string QuoteIdentifier(string identifier)
        {
            if (string.IsNullOrWhiteSpace(identifier))
                throw new ArgumentException("标识符不能为空。", nameof(identifier));

            return "\"" + identifier.Replace("\"", "\"\"") + "\"";
        }

        public string LimitOffsetClause => "LIMIT @take OFFSET @skip";

        public string UtcNowMsExpression => "CAST((julianday('now') - 2440587.5) * 86400000 AS INTEGER)";

        public string BuildAutoIncrementPrimaryKeyColumn(string columnName, bool useBigInt = true)
        {
            if (string.IsNullOrWhiteSpace(columnName))
                throw new ArgumentException("列名不能为空。", nameof(columnName));

            return $"{QuoteIdentifier(columnName)} INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT";
        }

        public string BuildUpsert(
            string tableName,
            IReadOnlyList<string> insertColumns,
            IReadOnlyList<string> keyColumns,
            IReadOnlyList<string> updateColumns)
        {
            if (string.IsNullOrWhiteSpace(tableName))
                throw new ArgumentException("表名不能为空。", nameof(tableName));
            if (insertColumns == null || insertColumns.Count == 0)
                throw new ArgumentException("insertColumns 不能为空。", nameof(insertColumns));
            if (keyColumns == null || keyColumns.Count == 0)
                throw new ArgumentException("keyColumns 不能为空。", nameof(keyColumns));

            var qTable = QuoteIdentifier(tableName);
            var qInsertColumns = insertColumns.Select(QuoteIdentifier).ToArray();
            var insertParams = insertColumns.Select(c => "@" + c).ToArray();
            var qKeyColumns = keyColumns.Select(QuoteIdentifier).ToArray();

            var updateSet = (updateColumns ?? Array.Empty<string>())
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Select(c => $"{QuoteIdentifier(c)} = excluded.{QuoteIdentifier(c)}")
                .ToArray();

            var sql = $"INSERT INTO {qTable} ({string.Join(", ", qInsertColumns)}) VALUES ({string.Join(", ", insertParams)}) " +
                      $"ON CONFLICT({string.Join(", ", qKeyColumns)}) ";

            if (updateSet.Length == 0)
                return sql + "DO NOTHING";

            return sql + "DO UPDATE SET " + string.Join(", ", updateSet);
        }
    }
}
