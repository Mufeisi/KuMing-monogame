using System.Collections.Generic;

namespace Server.Persistence.Sql
{
    public interface ISqlDialect
    {
        DatabaseProviderKind Provider { get; }

        string QuoteIdentifier(string identifier);

        string LimitOffsetClause { get; }

        string UtcNowMsExpression { get; }

        string BuildAutoIncrementPrimaryKeyColumn(string columnName, bool useBigInt = true);

        string BuildUpsert(
            string tableName,
            IReadOnlyList<string> insertColumns,
            IReadOnlyList<string> keyColumns,
            IReadOnlyList<string> updateColumns);
    }
}
