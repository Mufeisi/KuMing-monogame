using System.Collections.ObjectModel;
using System.Globalization;

namespace Server.Scripting.ServerSymbols
{
    public interface IServerSymbolResolver
    {
        ServerSymbolResult Resolve(ServerSymbolContext context, ServerSymbolReference reference);
    }

    public enum ServerSymbolStatus
    {
        Faulted,
        Resolved,
        ContextUnavailable,
        DependencyMissing,
        SensitiveDenied,
        Unsupported,
        InvalidReference
    }

    public enum ServerSymbolValueType
    {
        String,
        Integer,
        Decimal,
        DateTime,
        ObjectSummary
    }

    [Flags]
    public enum ServerSymbolContextKind
    {
        None = 0,
        Player = 1 << 0,
        Hero = 1 << 1,
        Npc = 1 << 2,
        Map = 1 << 3,
        Item = 1 << 4,
        Monster = 1 << 5,
        Pet = 1 << 6,
        Attacker = 1 << 7,
        Target = 1 << 8,
        Guild = 1 << 9,
        Conquest = 1 << 10,
        TriggerResult = 1 << 11,
        Server = 1 << 12,
        Client = 1 << 13,
        Variable = 1 << 14
    }

    [Flags]
    internal enum ServerSymbolSecurityClassification
    {
        Public = 0,
        Privacy = 1 << 0,
        ServerPath = 1 << 1,
        MachineIdentifier = 1 << 2,
        AccountInformation = 1 << 3,
        Credential = 1 << 4
    }

    internal enum ServerSymbolAccessPolicy
    {
        Allowed,
        Denied
    }

    internal enum ServerSymbolNoContextBehavior
    {
        StructuredFailure,
        EmptyString,
        Zero
    }

    public readonly struct ServerSymbolValue
    {
        private readonly string _text;
        private readonly long _integer;
        private readonly decimal _decimal;
        private readonly DateTime _dateTime;

        private ServerSymbolValue(
            ServerSymbolValueType type,
            string text,
            long integer,
            decimal decimalValue,
            DateTime dateTime)
        {
            Type = type;
            _text = text ?? string.Empty;
            _integer = integer;
            _decimal = decimalValue;
            _dateTime = dateTime;
        }

        public ServerSymbolValueType Type { get; }

        internal static ServerSymbolValue FromString(string value) =>
            new ServerSymbolValue(ServerSymbolValueType.String, value, default, default, default);

        internal static ServerSymbolValue FromInteger(long value) =>
            new ServerSymbolValue(ServerSymbolValueType.Integer, string.Empty, value, default, default);

        internal static ServerSymbolValue FromDecimal(decimal value) =>
            new ServerSymbolValue(ServerSymbolValueType.Decimal, string.Empty, default, value, default);

        internal static ServerSymbolValue FromDateTime(DateTime value) =>
            new ServerSymbolValue(ServerSymbolValueType.DateTime, string.Empty, default, default, value);

        internal static ServerSymbolValue FromObjectSummary(string value) =>
            new ServerSymbolValue(ServerSymbolValueType.ObjectSummary, value, default, default, default);

        public string Format()
        {
            switch (Type)
            {
                case ServerSymbolValueType.String:
                case ServerSymbolValueType.ObjectSummary:
                    return _text ?? string.Empty;
                case ServerSymbolValueType.Integer:
                    return _integer.ToString(CultureInfo.InvariantCulture);
                case ServerSymbolValueType.Decimal:
                    return _decimal.ToString("G29", CultureInfo.InvariantCulture);
                case ServerSymbolValueType.DateTime:
                    return _dateTime.ToString("O", CultureInfo.InvariantCulture);
                default:
                    throw new InvalidOperationException("未知服务器常量值类型。");
            }
        }
    }

    public readonly struct ServerSymbolResult
    {
        private ServerSymbolResult(
            ServerSymbolStatus status,
            string canonicalName,
            ServerSymbolValue value,
            string diagnostic)
        {
            Status = status;
            CanonicalName = canonicalName ?? string.Empty;
            Value = value;
            Diagnostic = diagnostic ?? string.Empty;
        }

        public ServerSymbolStatus Status { get; }
        public string CanonicalName { get; }
        public ServerSymbolValue Value { get; }
        public string Diagnostic { get; }
        public bool Success => Status == ServerSymbolStatus.Resolved;

        public static ServerSymbolResult Resolved(string canonicalName, ServerSymbolValue value) =>
            new ServerSymbolResult(ServerSymbolStatus.Resolved, canonicalName, value, string.Empty);

        public static ServerSymbolResult Fail(
            ServerSymbolStatus status,
            string canonicalName,
            string diagnostic)
        {
            if (status == ServerSymbolStatus.Resolved)
                throw new ArgumentOutOfRangeException(nameof(status), "失败结果不能使用 Resolved 状态。");

            return new ServerSymbolResult(status, canonicalName, default, diagnostic);
        }
    }

    public sealed class ServerSymbolReference
    {
        private ServerSymbolReference(
            bool isValid,
            string normalizedName,
            IReadOnlyList<string> arguments,
            string diagnostic)
        {
            IsValid = isValid;
            NormalizedName = normalizedName ?? string.Empty;
            Arguments = arguments ?? Array.Empty<string>();
            Diagnostic = diagnostic ?? string.Empty;
        }

        public bool IsValid { get; }
        public string NormalizedName { get; }
        public IReadOnlyList<string> Arguments { get; }
        public string Diagnostic { get; }

        internal ServerSymbolReference WithCanonicalName(string canonicalName) =>
            new ServerSymbolReference(true, canonicalName, Arguments, string.Empty);

        public static ServerSymbolReference Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return Invalid();

            string body = text.Trim();
            if (body.StartsWith("<$", StringComparison.Ordinal) && body.EndsWith(">", StringComparison.Ordinal))
                body = body.Substring(2, body.Length - 3).Trim();
            else if (body.StartsWith("<$", StringComparison.Ordinal) || body.EndsWith(">", StringComparison.Ordinal))
                return Invalid();

            int open = body.IndexOf('(');
            string name = open < 0 ? body : body.Substring(0, open).Trim();
            if (!TryNormalizeName(name, out string normalizedName)) return Invalid();

            if (open < 0)
                return new ServerSymbolReference(true, normalizedName, Array.Empty<string>(), string.Empty);
            if (!body.EndsWith(")", StringComparison.Ordinal) || open == body.Length - 1)
                return Invalid();

            string argumentText = body.Substring(open + 1, body.Length - open - 2);
            if (!TrySplitArguments(argumentText, out IReadOnlyList<string> arguments)) return Invalid();

            return new ServerSymbolReference(true, normalizedName, arguments, string.Empty);
        }

        internal static bool TryNormalizeName(string name, out string normalized)
        {
            normalized = string.Empty;
            if (string.IsNullOrWhiteSpace(name)) return false;

            string candidate = name.Trim();
            if (candidate.Length > 96 || !char.IsLetter(candidate[0])) return false;
            for (int i = 1; i < candidate.Length; i++)
            {
                char current = candidate[i];
                if (!(char.IsLetterOrDigit(current) || current == '_' || current == '.')) return false;
                if (current == '.' && (candidate[i - 1] == '.' || i == candidate.Length - 1)) return false;
            }

            normalized = candidate.ToUpperInvariant();
            return true;
        }

        private static bool TrySplitArguments(string text, out IReadOnlyList<string> arguments)
        {
            var values = new List<string>();
            int depth = 0;
            int start = 0;
            bool quoted = false;
            bool escaped = false;
            for (int i = 0; i < text.Length; i++)
            {
                if (quoted)
                {
                    if (escaped)
                    {
                        escaped = false;
                        continue;
                    }
                    if (text[i] == '\\')
                    {
                        escaped = true;
                        continue;
                    }
                    if (text[i] == '"') quoted = false;
                    continue;
                }

                if (text[i] == '"')
                {
                    quoted = true;
                    continue;
                }

                switch (text[i])
                {
                    case '(':
                        depth++;
                        break;
                    case ')':
                        if (depth == 0)
                        {
                            arguments = Array.Empty<string>();
                            return false;
                        }
                        depth--;
                        break;
                    case ',' when depth == 0:
                        if (!TryAddArgument(text.Substring(start, i - start), values))
                        {
                            arguments = Array.Empty<string>();
                            return false;
                        }
                        start = i + 1;
                        break;
                }
            }

            if (depth != 0 || quoted || escaped || !TryAddArgument(text.Substring(start), values))
            {
                arguments = Array.Empty<string>();
                return false;
            }

            arguments = new ReadOnlyCollection<string>(values);
            return true;
        }

        private static bool TryAddArgument(string text, ICollection<string> values)
        {
            string value = text.Trim();
            if (value.Length == 0 || value.Length > 256) return false;
            values.Add(value);
            return true;
        }

        private static ServerSymbolReference Invalid() =>
            new ServerSymbolReference(false, string.Empty, Array.Empty<string>(), "服务器常量引用语法无效。");
    }
}
