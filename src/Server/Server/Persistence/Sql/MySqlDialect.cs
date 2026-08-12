using System;
using System.Collections.Generic;
using System.Linq;

namespace Server.Persistence.Sql
{
    internal sealed class MySqlDialect : ISqlDialect
    {
        public DatabaseProviderKind Provider => DatabaseProviderKind.MySql;

        public string QuoteIdentifier(string identifier)
        {
            if (string.IsNullOrWhiteSpace(identifier))
                throw new ArgumentException("标识符不能为空。", nameof(identifier));

            return "`" + identifier.Replace("`", "``") + "`";
        }

        public string LimitOffsetClause => "LIMIT @take OFFSET @skip";

        public string UtcNowMsExpression => "CAST(UNIX_TIMESTAMP(UTC_TIMESTAMP(3)) * 1000 AS SIGNED)";

        public string BuildAutoIncrementPrimaryKeyColumn(string columnName, bool useBigInt = true)
        {
            if (string.IsNullOrWhiteSpace(columnName))
                throw new ArgumentException("列名不能为空。", nameof(columnName));

            var type = useBigInt ? "BIGINT" : "INT";
            return $"{QuoteIdentifier(columnName)} {type} NOT NULL AUTO_INCREMENT PRIMARY KEY";
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

            var qTable = QuoteIdentifier(tableName);
            var qInsertColumns = insertColumns.Select(QuoteIdentifier).ToArray();
            var insertParams = insertColumns.Select(c => "@" + c).ToArray();

            var updateSet = (updateColumns ?? Array.Empty<string>())
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Select(c => $"{QuoteIdentifier(c)} = VALUES({QuoteIdentifier(c)})")
                .ToArray();

            if (updateSet.Length == 0)
            {
                // MySQL 没有 DO NOTHING：用无害的主键自赋值模拟。
                var firstKey = (keyColumns != null && keyColumns.Count > 0) ? keyColumns[0] : insertColumns[0];
                updateSet = new[] { $"{QuoteIdentifier(firstKey)} = {QuoteIdentifier(firstKey)}" };
            }

            return $"INSERT INTO {qTable} ({string.Join(", ", qInsertColumns)}) VALUES ({string.Join(", ", insertParams)}) " +
                   $"ON DUPLICATE KEY UPDATE {string.Join(", ", updateSet)}";
        }
    }
}
