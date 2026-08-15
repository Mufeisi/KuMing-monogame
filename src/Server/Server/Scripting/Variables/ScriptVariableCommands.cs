using System.Globalization;

namespace Server.Scripting.Variables
{
    public static class ScriptVariableReferenceParser
    {
        public static bool TryParse(string text, out ScriptVariableReference reference)
        {
            reference = default;
            if (string.IsNullOrWhiteSpace(text)) return false;

            string value = text.Trim();
            if (value.Length > 2 && value[1] == '$' &&
                (value[0] == 'N' || value[0] == 'n' || value[0] == 'S' || value[0] == 's'))
            {
                ScriptVariableScope extendedScope = value[0] == 'N' || value[0] == 'n'
                    ? ScriptVariableScope.N
                    : ScriptVariableScope.S;
                try
                {
                    reference = ScriptVariableReference.Named(extendedScope, value.Substring(2));
                    return true;
                }
                catch (ArgumentException)
                {
                    return false;
                }
            }

            int separator = value.IndexOf('.');
            if (separator >= 0)
            {
                if (separator == 0 || separator == value.Length - 1)
                    return false;

                if (!TryParseScope(value.Substring(0, separator), out var namedScope)) return false;
                try
                {
                    reference = ScriptVariableReference.Named(namedScope, value.Substring(separator + 1));
                    return true;
                }
                catch (ArgumentException)
                {
                    return false;
                }
            }

            var digitStart = 0;
            while (digitStart < value.Length && char.IsLetter(value[digitStart])) digitStart++;
            if (digitStart == 0 || digitStart == value.Length) return false;
            if (!TryParseScope(value.Substring(0, digitStart), out var legacyScope)) return false;
            if (!int.TryParse(value.Substring(digitStart), NumberStyles.None,
                    CultureInfo.InvariantCulture, out var index) || index < 0 || index > 999)
                return false;

            reference = ScriptVariableReference.Legacy(legacyScope, index);
            return true;
        }

        private static bool TryParseScope(string text, out ScriptVariableScope scope) =>
            Enum.TryParse(text, ignoreCase: true, out scope) &&
            Enum.IsDefined(typeof(ScriptVariableScope), scope);
    }

    public readonly struct ScriptVariableTextResult
    {
        internal ScriptVariableTextResult(bool success, ScriptVariableErrorCode errorCode, string text, string diagnostic)
        {
            Success = success;
            ErrorCode = errorCode;
            Text = text ?? string.Empty;
            Diagnostic = diagnostic ?? string.Empty;
        }

        public bool Success { get; }
        public ScriptVariableErrorCode ErrorCode { get; }
        public string Text { get; }
        public string Diagnostic { get; }
    }

    public readonly struct ScriptVariableCheckResult
    {
        internal ScriptVariableCheckResult(bool success, bool matched, ScriptVariableErrorCode errorCode, string diagnostic)
        {
            Success = success;
            Matched = matched;
            ErrorCode = errorCode;
            Diagnostic = diagnostic ?? string.Empty;
        }

        public bool Success { get; }
        public bool Matched { get; }
        public ScriptVariableErrorCode ErrorCode { get; }
        public string Diagnostic { get; }
    }

    /// <summary>
    /// TXT 与 C# 脚本共同使用的命令适配层；状态只保存在 IScriptVariableModule 中。
    /// </summary>
    public sealed class ScriptVariableCommands
    {
        private readonly IScriptVariableModule _module;

        public ScriptVariableCommands(IScriptVariableModule module)
        {
            _module = module ?? throw new ArgumentNullException(nameof(module));
        }

        public ScriptVariableMutationResult Mutate(
            in ScriptVariableContext context,
            string referenceText,
            string command,
            string operandText)
        {
            if (!ScriptVariableReferenceParser.TryParse(referenceText, out var reference))
                return MutationFailure(ScriptVariableErrorCode.UnknownReference, "变量引用无效。", default);
            if (!TryMapOperation(command, out var operation))
                return MutationFailure(ScriptVariableErrorCode.InvalidExpression, "变量命令无效。", default);

            ScriptVariableReadResult current = _module.Read(context, reference);
            if (!current.Success)
                return MutationFailure(current.ErrorCode, current.Diagnostic, current.Value);
            if (!TryResolveOperand(context, current.Value.Kind, operandText, out var operand))
                return MutationFailure(ScriptVariableErrorCode.TypeMismatch, "操作数格式与变量类型不匹配。", current.Value);

            return _module.Mutate(context, operation == ScriptVariableOperation.Set
                ? ScriptVariableMutation.Set(reference, operand)
                : ScriptVariableMutation.Apply(reference, operation, operand));
        }

        public ScriptVariableMutationResult Convert(
            in ScriptVariableContext context,
            string destinationText,
            string conversion,
            string sourceText)
        {
            if (!ScriptVariableReferenceParser.TryParse(destinationText, out var destination) ||
                !ScriptVariableReferenceParser.TryParse(sourceText, out var source))
                return MutationFailure(ScriptVariableErrorCode.UnknownReference, "转换变量引用无效。", default);
            if (!TryMapRounding(conversion, out var rounding))
                return MutationFailure(ScriptVariableErrorCode.InvalidExpression, "取整方式无效。", default);

            ScriptVariableReadResult destinationValue = _module.Read(context, destination);
            if (!destinationValue.Success)
                return MutationFailure(destinationValue.ErrorCode, destinationValue.Diagnostic, destinationValue.Value);
            if (destinationValue.Value.Kind != ScriptVariableKind.Integer)
                return MutationFailure(
                    ScriptVariableErrorCode.TypeMismatch,
                    "ROUND/FLOOR/CEIL/TRUNC 的目标必须是 Integer 变量。",
                    destinationValue.Value);

            ScriptVariableReadResult sourceValue = _module.Read(context, source);
            if (!sourceValue.Success)
                return MutationFailure(sourceValue.ErrorCode, sourceValue.Diagnostic, destinationValue.Value);
            ScriptVariableResult converted = ScriptVariableArithmetic.ConvertToInteger(sourceValue.Value, rounding);
            if (!converted.Success)
                return MutationFailure(converted.ErrorCode, converted.Diagnostic, destinationValue.Value);

            return _module.Mutate(
                context, ScriptVariableMutation.Set(destination, converted.Value));
        }

        public ScriptVariableTextResult Format(
            in ScriptVariableContext context,
            string referenceText,
            int? decimalDigits = null)
        {
            if (!ScriptVariableReferenceParser.TryParse(referenceText, out var reference))
                return new ScriptVariableTextResult(false, ScriptVariableErrorCode.UnknownReference, string.Empty, "变量引用无效。");

            ScriptVariableReadResult result = _module.Read(context, reference);
            if (!result.Success)
                return new ScriptVariableTextResult(false, result.ErrorCode, string.Empty, result.Diagnostic);
            try
            {
                return new ScriptVariableTextResult(true, ScriptVariableErrorCode.None,
                    result.Value.Format(decimalDigits), string.Empty);
            }
            catch (ArgumentOutOfRangeException error)
            {
                return new ScriptVariableTextResult(false, ScriptVariableErrorCode.InvalidExpression,
                    string.Empty, error.Message);
            }
        }

        public ScriptVariableCheckResult Check(
            in ScriptVariableContext context,
            string referenceText,
            string comparison,
            string operandText)
        {
            if (!ScriptVariableReferenceParser.TryParse(referenceText, out var reference))
                return CheckFailure(ScriptVariableErrorCode.UnknownReference, "变量引用无效。");

            ScriptVariableReadResult current = _module.Read(context, reference);
            if (!current.Success) return CheckFailure(current.ErrorCode, current.Diagnostic);
            if (!TryResolveOperand(context, current.Value.Kind, operandText, out var operand))
                return CheckFailure(ScriptVariableErrorCode.TypeMismatch, "比较值格式与变量类型不匹配。");

            if (!TryCompare(current.Value, comparison, operand, out var matched))
                return CheckFailure(ScriptVariableErrorCode.InvalidExpression, "比较操作符或类型无效。");
            return new ScriptVariableCheckResult(true, matched, ScriptVariableErrorCode.None, string.Empty);
        }

        private static bool TryMapOperation(string command, out ScriptVariableOperation operation)
        {
            operation = default;
            switch ((command ?? string.Empty).Trim().ToUpperInvariant())
            {
                case "MOV": operation = ScriptVariableOperation.Set; return true;
                case "INC": operation = ScriptVariableOperation.Add; return true;
                case "DEC": operation = ScriptVariableOperation.Subtract; return true;
                case "MUL": operation = ScriptVariableOperation.Multiply; return true;
                case "DIV": operation = ScriptVariableOperation.Divide; return true;
                case "MOD": operation = ScriptVariableOperation.Modulo; return true;
                default: return false;
            }
        }

        private static bool TryMapRounding(string command, out ScriptVariableRounding rounding)
        {
            rounding = default;
            switch ((command ?? string.Empty).Trim().ToUpperInvariant())
            {
                case "ROUND": rounding = ScriptVariableRounding.Round; return true;
                case "FLOOR": rounding = ScriptVariableRounding.Floor; return true;
                case "CEIL": rounding = ScriptVariableRounding.Ceiling; return true;
                case "TRUNC": rounding = ScriptVariableRounding.Truncate; return true;
                default: return false;
            }
        }

        private bool TryResolveOperand(
            in ScriptVariableContext context,
            ScriptVariableKind targetKind,
            string text,
            out ScriptVariableValue value)
        {
            value = default;
            if (ScriptVariableReferenceParser.TryParse(text, out var reference))
            {
                ScriptVariableReadResult read = _module.Read(context, reference);
                if (!read.Success) return false;
                if (read.Value.Kind == targetKind)
                {
                    value = read.Value;
                    return true;
                }
                if (targetKind == ScriptVariableKind.Decimal && read.Value.Kind == ScriptVariableKind.Integer)
                {
                    value = ScriptVariableValue.FromDecimal(read.Value.Integer);
                    return true;
                }
                return false;
            }

            return TryParseOperand(targetKind, text, out value);
        }

        private static bool TryParseOperand(ScriptVariableKind kind, string text, out ScriptVariableValue value)
        {
            value = default;
            string source = text ?? string.Empty;
            switch (kind)
            {
                case ScriptVariableKind.Integer:
                    if (!long.TryParse(source.Trim(), NumberStyles.AllowLeadingSign,
                            CultureInfo.InvariantCulture, out var integer)) return false;
                    value = ScriptVariableValue.FromInteger(integer);
                    return true;
                case ScriptVariableKind.Decimal:
                    return ScriptVariableValue.TryParseDecimal(source, out value);
                case ScriptVariableKind.String:
                    value = ScriptVariableValue.FromString(source);
                    return true;
                default:
                    return false;
            }
        }

        private static bool TryCompare(
            ScriptVariableValue left,
            string comparison,
            ScriptVariableValue right,
            out bool matched)
        {
            matched = false;
            int order;
            if ((left.Kind == ScriptVariableKind.Integer || left.Kind == ScriptVariableKind.Decimal) &&
                (right.Kind == ScriptVariableKind.Integer || right.Kind == ScriptVariableKind.Decimal))
            {
                decimal leftNumber = left.Kind == ScriptVariableKind.Decimal ? left.Decimal : left.Integer;
                decimal rightNumber = right.Kind == ScriptVariableKind.Decimal ? right.Decimal : right.Integer;
                order = leftNumber.CompareTo(rightNumber);
            }
            else if (left.Kind == ScriptVariableKind.String && right.Kind == ScriptVariableKind.String)
            {
                order = string.Compare(left.Text, right.Text, StringComparison.Ordinal);
            }
            else
            {
                return false;
            }

            switch ((comparison ?? string.Empty).Trim())
            {
                case "=":
                case "==": matched = order == 0; return true;
                case "!=":
                case "<>": matched = order != 0; return true;
                case ">": matched = order > 0; return true;
                case ">=": matched = order >= 0; return true;
                case "<": matched = order < 0; return true;
                case "<=": matched = order <= 0; return true;
                default: return false;
            }
        }

        private static ScriptVariableMutationResult MutationFailure(
            ScriptVariableErrorCode code,
            string diagnostic,
            ScriptVariableValue oldValue) =>
            new ScriptVariableMutationResult(false, code, oldValue, oldValue, diagnostic);

        private static ScriptVariableCheckResult CheckFailure(ScriptVariableErrorCode code, string diagnostic) =>
            new ScriptVariableCheckResult(false, false, code, diagnostic);
    }
}
