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
        L,
        Dict,
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
        private readonly string[] _list;
        private readonly KeyValuePair<string, string>[] _dictionary;

        private ScriptVariableValue(
            ScriptVariableKind kind,
            long integer,
            decimal decimalValue,
            string text,
            string[] list = null,
            KeyValuePair<string, string>[] dictionary = null)
        {
            Kind = kind;
            _integer = integer;
            _decimal = decimalValue;
            _text = text ?? string.Empty;
            _list = list;
            _dictionary = dictionary;
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
        public IReadOnlyList<string> List => Kind == ScriptVariableKind.List
            ? _list ?? Array.Empty<string>()
            : throw new InvalidOperationException("变量不是列表。");
        public IReadOnlyList<KeyValuePair<string, string>> Dictionary => Kind == ScriptVariableKind.Dictionary
            ? _dictionary ?? Array.Empty<KeyValuePair<string, string>>()
            : throw new InvalidOperationException("变量不是字典。");

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

        public static ScriptVariableValue FromList(IEnumerable<string> values) =>
            new ScriptVariableValue(
                ScriptVariableKind.List, default, default, string.Empty,
                (values ?? Array.Empty<string>()).Select(value => value ?? string.Empty).ToArray());

        public static ScriptVariableValue FromDictionary(IEnumerable<KeyValuePair<string, string>> values) =>
            new ScriptVariableValue(
                ScriptVariableKind.Dictionary, default, default, string.Empty, null,
                (values ?? Array.Empty<KeyValuePair<string, string>>())
                .Select(pair => new KeyValuePair<string, string>(pair.Key ?? string.Empty, pair.Value ?? string.Empty))
                .ToArray());

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
                case ScriptVariableKind.List:
                    return "[" + string.Join(",", _list ?? Array.Empty<string>()) + "]";
                case ScriptVariableKind.Dictionary:
                    return "{" + string.Join(",", (_dictionary ?? Array.Empty<KeyValuePair<string, string>>())
                        .Select(pair => pair.Key + ":" + pair.Value)) + "}";
                default:
                    throw new InvalidOperationException("未知变量类型。");
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
                ScriptVariableKind.List => (_list ?? Array.Empty<string>()).SequenceEqual(
                    other._list ?? Array.Empty<string>(), StringComparer.Ordinal),
                ScriptVariableKind.Dictionary => (_dictionary ?? Array.Empty<KeyValuePair<string, string>>()).SequenceEqual(
                    other._dictionary ?? Array.Empty<KeyValuePair<string, string>>()),
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
