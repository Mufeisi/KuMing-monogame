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
                (value[0] == 'N' || value[0] == 'n' || value[0] == 'S' || value[0] == 's' ||
                 value[0] == 'L' || value[0] == 'l' || value[0] == 'D' || value[0] == 'd'))
            {
                ScriptVariableScope extendedScope = char.ToUpperInvariant(value[0]) switch
                {
                    'N' => ScriptVariableScope.N,
                    'S' => ScriptVariableScope.S,
                    'L' => ScriptVariableScope.L,
                    _ => ScriptVariableScope.Dict
                };
                try
                {
                    reference = ScriptVariableReference.LingFengNamed(
                        extendedScope, value.Substring(2));
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
        private readonly ScriptCompositeVariableCommands _composites;

        public ScriptVariableCommands(IScriptVariableModule module)
        {
            _module = module ?? throw new ArgumentNullException(nameof(module));
            _composites = new ScriptCompositeVariableCommands(_module);
        }

        public ScriptCompositeVariableCommands Composites => _composites;

        public ScriptVariableMutationResult Initialize(
            in ScriptVariableContext context,
            string referenceText)
        {
            if (!ScriptVariableReferenceParser.TryParse(referenceText, out var reference))
                return MutationFailure(ScriptVariableErrorCode.UnknownReference, "初始化变量引用无效。", default);

            ScriptVariableReadResult current = _module.Read(context, reference);
            if (!current.Success)
                return MutationFailure(current.ErrorCode, current.Diagnostic, current.Value);
            if (current.Value.Kind == ScriptVariableKind.List || current.Value.Kind == ScriptVariableKind.Dictionary)
                return MutationFailure(
                    ScriptVariableErrorCode.TypeMismatch,
                    "INITVAR 只支持整数、小数和字符串变量。",
                    current.Value);
            if (current.Found)
                return new ScriptVariableMutationResult(
                    true, ScriptVariableErrorCode.None, current.Value, current.Value, string.Empty);

            return _module.Mutate(context, ScriptVariableMutation.Set(reference, current.Value));
        }

        public ScriptVariableMutationResult Mutate(
            in ScriptVariableContext context,
            string referenceText,
            string command,
            string operandText)
        {
            if (_composites.IsCompositeReference(referenceText))
                return _composites.Mutate(context, referenceText, command, operandText);
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
            bool parseDecimal = string.Equals(conversion, "PARSEDECIMAL", StringComparison.OrdinalIgnoreCase);
            ScriptVariableRounding rounding = default;
            if (!parseDecimal && !TryMapRounding(conversion, out rounding))
                return MutationFailure(ScriptVariableErrorCode.InvalidExpression, "取整方式无效。", default);

            ScriptVariableReadResult destinationValue = _module.Read(context, destination);
            if (!destinationValue.Success)
                return MutationFailure(destinationValue.ErrorCode, destinationValue.Diagnostic, destinationValue.Value);
            if (parseDecimal)
            {
                if (destinationValue.Value.Kind != ScriptVariableKind.Decimal)
                    return MutationFailure(
                        ScriptVariableErrorCode.TypeMismatch,
                        "PARSEDECIMAL 的目标必须是 Decimal 变量。",
                        destinationValue.Value);
                ScriptVariableReadResult parsedSource = _module.Read(context, source);
                if (!parsedSource.Success)
                    return MutationFailure(parsedSource.ErrorCode, parsedSource.Diagnostic, destinationValue.Value);
                if (parsedSource.Value.Kind != ScriptVariableKind.String)
                    return MutationFailure(
                        ScriptVariableErrorCode.TypeMismatch,
                        "PARSEDECIMAL 的来源必须是 String 变量。",
                        destinationValue.Value);
                if (!ScriptVariableValue.TryParseDecimal(parsedSource.Value.Text, out var parsedDecimal))
                    return MutationFailure(
                        ScriptVariableErrorCode.InvalidExpression,
                        $"字符串不是有效的文化无关小数，或超过 {ScriptVariableValue.MaximumDecimalScale} 位小数。",
                        destinationValue.Value);
                return _module.Mutate(
                    context, ScriptVariableMutation.Set(destination, parsedDecimal));
            }
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
            if (_composites.IsCompositeReference(referenceText))
            {
                if (decimalDigits.HasValue)
                    return new ScriptVariableTextResult(false, ScriptVariableErrorCode.TypeMismatch,
                        string.Empty, "复合变量不支持小数位格式化。");
                ScriptCompositeResult composite = _composites.Read(context, referenceText);
                return composite.Success
                    ? new ScriptVariableTextResult(true, ScriptVariableErrorCode.None,
                        composite.Value.Format(), string.Empty)
                    : new ScriptVariableTextResult(false, composite.ErrorCode,
                        string.Empty, composite.Diagnostic);
            }
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

        public ScriptVariableMutationResult Formulate(
            in ScriptVariableContext context,
            string expression,
            string destinationText,
            Func<int, int, int> random = null,
            bool truncateIntegerResult = false)
        {
            if (!ScriptVariableReferenceParser.TryParse(destinationText, out var destination))
                return MutationFailure(ScriptVariableErrorCode.UnknownReference, "公式目标变量引用无效。", default);
            ScriptVariableReadResult target = _module.Read(context, destination);
            if (!target.Success) return MutationFailure(target.ErrorCode, target.Diagnostic, target.Value);
            if (target.Value.Kind != ScriptVariableKind.Integer && target.Value.Kind != ScriptVariableKind.Decimal)
                return MutationFailure(ScriptVariableErrorCode.TypeMismatch, "公式目标必须是整数或小数变量。", target.Value);

            ScriptVariableContext expressionContext = context;
            var parser = new ScriptDecimalExpressionParser(
                expression,
                name =>
                {
                    if (!ScriptVariableReferenceParser.TryParse(name, out var reference))
                        return ScriptVariableResult.Fail(ScriptVariableErrorCode.UnknownReference, $"公式变量引用无效：{name}");
                    ScriptVariableReadResult read = _module.Read(expressionContext, reference);
                    return read.Success
                        ? ScriptVariableResult.Ok(read.Value)
                        : ScriptVariableResult.Fail(read.ErrorCode, read.Diagnostic);
                },
                random ?? Random.Shared.Next);
            ScriptVariableResult evaluated = parser.Parse();
            if (!evaluated.Success)
                return MutationFailure(evaluated.ErrorCode, evaluated.Diagnostic, target.Value);

            ScriptVariableValue result = evaluated.Value;
            if (target.Value.Kind == ScriptVariableKind.Integer)
            {
                decimal integerResult = truncateIntegerResult
                    ? decimal.Truncate(result.Decimal)
                    : result.Decimal;
                if (integerResult != decimal.Truncate(integerResult) ||
                    integerResult < long.MinValue || integerResult > long.MaxValue)
                    return MutationFailure(ScriptVariableErrorCode.TypeMismatch,
                        "公式结果含小数；请使用 Decimal 目标或先显式取整。", target.Value);
                result = ScriptVariableValue.FromInteger(decimal.ToInt64(integerResult));
            }
            return _module.Mutate(context, ScriptVariableMutation.Set(destination, result));
        }

        public ScriptVariableCheckResult Chance(
            in ScriptVariableContext context,
            string referenceText,
            ScriptProbabilityUnit unit = ScriptProbabilityUnit.Percent,
            Func<int, int, int> random = null)
        {
            if (!ScriptVariableReferenceParser.TryParse(referenceText, out var reference))
                return CheckFailure(ScriptVariableErrorCode.UnknownReference, "概率变量引用无效。");
            ScriptVariableReadResult read = _module.Read(context, reference);
            if (!read.Success) return CheckFailure(read.ErrorCode, read.Diagnostic);
            decimal chance = read.Value.Kind switch
            {
                ScriptVariableKind.Integer => read.Value.Integer,
                ScriptVariableKind.Decimal => read.Value.Decimal,
                _ => decimal.MinValue
            };
            if (chance == decimal.MinValue)
                return CheckFailure(ScriptVariableErrorCode.TypeMismatch, "概率变量必须是整数或小数。");
            int roll = (random ?? Random.Shared.Next)(0, ScriptVariableProbability.Resolution);
            return ScriptVariableProbability.Check(chance, unit, roll);
        }

        private static bool TryMapOperation(string command, out ScriptVariableOperation operation)
        {
            operation = default;
            switch ((command ?? string.Empty).Trim().ToUpperInvariant())
            {
                case "MOV": operation = ScriptVariableOperation.Set; return true;
                case "INC":
                case "+": operation = ScriptVariableOperation.Add; return true;
                case "DEC":
                case "-": operation = ScriptVariableOperation.Subtract; return true;
                case "MUL":
                case "*": operation = ScriptVariableOperation.Multiply; return true;
                case "DIV":
                case "/": operation = ScriptVariableOperation.Divide; return true;
                case "MOD":
                case "%": operation = ScriptVariableOperation.Modulo; return true;
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
