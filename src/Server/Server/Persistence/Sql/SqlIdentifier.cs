using System;

namespace Server.Persistence.Sql
{
    public readonly struct SqlIdentifier : IEquatable<SqlIdentifier>
    {
        public string Value { get; }

        private SqlIdentifier(string value)
        {
            Value = value;
        }

        public static SqlIdentifier Create(string value)
        {
            if (!TryCreate(value, out var identifier))
                throw new ArgumentException("无效的 SQL 标识符。仅允许 lower_snake_case（a-z0-9_），且必须以字母开头。", nameof(value));

            return identifier;
        }

        public static bool TryCreate(string value, out SqlIdentifier identifier)
        {
            identifier = default;

            if (string.IsNullOrWhiteSpace(value))
                return false;

            value = value.Trim();

            if (!IsValid(value))
                return false;

            identifier = new SqlIdentifier(value);
            return true;
        }

        public override string ToString() => Value;

        public bool Equals(SqlIdentifier other) => string.Equals(Value, other.Value, StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is SqlIdentifier other && Equals(other);

        public override int GetHashCode() => Value != null ? StringComparer.Ordinal.GetHashCode(Value) : 0;

        public static bool operator ==(SqlIdentifier left, SqlIdentifier right) => left.Equals(right);

        public static bool operator !=(SqlIdentifier left, SqlIdentifier right) => !left.Equals(right);

        public static bool IsValid(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            value = value.Trim();

            for (var i = 0; i < value.Length; i++)
            {
                var ch = value[i];

                if (i == 0)
                {
                    if (ch < 'a' || ch > 'z')
                        return false;
                    continue;
                }

                var isLower = ch >= 'a' && ch <= 'z';
                var isDigit = ch >= '0' && ch <= '9';
                var isUnderscore = ch == '_';

                if (!isLower && !isDigit && !isUnderscore)
                    return false;
            }

            return true;
        }
    }
}

