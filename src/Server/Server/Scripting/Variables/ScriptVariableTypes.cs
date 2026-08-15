using System.Globalization;

namespace Server.Scripting.Variables
{
    public enum ScriptVariableKind
    {
        Integer,
        Decimal,
        String,
        List,
        Dictionary
    }

    public enum ScriptVariableScope
    {
        P,
        D,
        M,
        N,
        S,
        I,
        G,
        A,
        U,
        T,
        J,
        Z,
        Human,
        Guild,
        Global,
        Call
    }

    public enum ScriptVariableErrorCode
    {
        None,
        UnknownReference,
        DeclarationConflict,
        TypeMismatch,
        Overflow,
        ScaleExceeded,
        InvalidExpression,
        ContextUnavailable,
        TargetOffline,
        QuotaExceeded,
        WrongThread,
        PersistenceUnavailable
    }

    public enum ScriptVariableOperation
    {
        Set,
        Add,
        Subtract,
        Multiply,
        Divide,
        Modulo
    }

    public enum ScriptVariableRounding
    {
        Round,
        Floor,
        Ceiling,
        Truncate
    }

    public readonly struct ScriptVariableValue : IEquatable<ScriptVariableValue>
    {
        public const int MaximumDecimalScale = 8;

        private readonly long _integer;
        private readonly decimal _decimal;
        private readonly string _text;

        private ScriptVariableValue(ScriptVariableKind kind, long integer, decimal decimalValue, string text)
        {
            Kind = kind;
            _integer = integer;
            _decimal = decimalValue;
            _text = text ?? string.Empty;
        }

        public ScriptVariableKind Kind { get; }
        public long Integer => Kind == ScriptVariableKind.Integer
            ? _integer
            : throw new InvalidOperationException("变量不是整数。");
        public decimal Decimal => Kind == ScriptVariableKind.Decimal
            ? _decimal
            : throw new InvalidOperationException("变量不是小数。");
        public string Text => Kind == ScriptVariableKind.String
            ? _text ?? string.Empty
            : throw new InvalidOperationException("变量不是字符串。");

        public static ScriptVariableValue FromInteger(long value) =>
            new ScriptVariableValue(ScriptVariableKind.Integer, value, default, string.Empty);

        public static ScriptVariableValue FromDecimal(decimal value)
        {
            if (GetDecimalScale(value) > MaximumDecimalScale)
                throw new ArgumentOutOfRangeException(nameof(value), $"小数位不能超过 {MaximumDecimalScale} 位。");

            return new ScriptVariableValue(ScriptVariableKind.Decimal, default, value, string.Empty);
        }

        public static ScriptVariableValue FromString(string value) =>
            new ScriptVariableValue(ScriptVariableKind.String, default, default, value ?? string.Empty);

        public static bool TryParseDecimal(string text, out ScriptVariableValue value)
        {
            value = default;
            if (string.IsNullOrWhiteSpace(text)) return false;

            const NumberStyles styles = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;
            if (!decimal.TryParse(text.Trim(), styles, CultureInfo.InvariantCulture, out var parsed))
                return false;
            if (GetDecimalScale(parsed) > MaximumDecimalScale)
                return false;

            value = FromDecimal(parsed);
            return true;
        }

        public string Format(int? decimalDigits = null)
        {
            switch (Kind)
            {
                case ScriptVariableKind.Integer:
                    return _integer.ToString(CultureInfo.InvariantCulture);
                case ScriptVariableKind.Decimal:
                    if (!decimalDigits.HasValue)
                        return _decimal.ToString("G29", CultureInfo.InvariantCulture);
                    if (decimalDigits.Value < 0 || decimalDigits.Value > MaximumDecimalScale)
                        throw new ArgumentOutOfRangeException(nameof(decimalDigits));
                    return _decimal.ToString("F" + decimalDigits.Value, CultureInfo.InvariantCulture);
                case ScriptVariableKind.String:
                    return _text ?? string.Empty;
                default:
                    throw new InvalidOperationException("复合变量尚不支持直接格式化。");
            }
        }

        internal static int GetDecimalScale(decimal value) =>
            (decimal.GetBits(value)[3] >> 16) & 0x7F;

        public bool Equals(ScriptVariableValue other) =>
            Kind == other.Kind && Kind switch
            {
                ScriptVariableKind.Integer => _integer == other._integer,
                ScriptVariableKind.Decimal => _decimal == other._decimal,
                ScriptVariableKind.String => string.Equals(_text, other._text, StringComparison.Ordinal),
                _ => false
            };

        public override bool Equals(object obj) => obj is ScriptVariableValue other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Kind, _integer, _decimal, _text);
        public override string ToString() => Format();
    }

    public readonly struct ScriptVariableResult
    {
        private ScriptVariableResult(bool success, ScriptVariableErrorCode errorCode, ScriptVariableValue value, string diagnostic)
        {
            Success = success;
            ErrorCode = errorCode;
            Value = value;
            Diagnostic = diagnostic ?? string.Empty;
        }

        public bool Success { get; }
        public ScriptVariableErrorCode ErrorCode { get; }
        public ScriptVariableValue Value { get; }
        public string Diagnostic { get; }

        public static ScriptVariableResult Ok(ScriptVariableValue value) =>
            new ScriptVariableResult(true, ScriptVariableErrorCode.None, value, string.Empty);

        public static ScriptVariableResult Fail(ScriptVariableErrorCode errorCode, string diagnostic) =>
            new ScriptVariableResult(false, errorCode, default, diagnostic);
    }
}
