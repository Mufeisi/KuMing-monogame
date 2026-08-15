using System.Globalization;

namespace Server.Scripting.Variables
{
    public enum ScriptProbabilityUnit
    {
        Percent,
        BasisPoints,
        Fraction
    }

    public static class ScriptVariableProbability
    {
        public const int Resolution = 1_000_000;

        public static ScriptVariableCheckResult Check(
            decimal chance,
            ScriptProbabilityUnit unit,
            int roll)
        {
            decimal maximum = unit switch
            {
                ScriptProbabilityUnit.Percent => 100m,
                ScriptProbabilityUnit.BasisPoints => 10_000m,
                ScriptProbabilityUnit.Fraction => 1m,
                _ => -1m
            };
            if (maximum < 0m || chance < 0m || chance > maximum)
                return new ScriptVariableCheckResult(false, false, ScriptVariableErrorCode.InvalidExpression,
                    $"{unit} 概率必须在 0 到 {maximum.ToString(CultureInfo.InvariantCulture)} 之间。");
            if (roll < 0 || roll >= Resolution)
                return new ScriptVariableCheckResult(false, false, ScriptVariableErrorCode.InvalidExpression,
                    $"概率随机值必须在 0 到 {Resolution - 1} 之间。");

            int threshold = decimal.ToInt32(decimal.Truncate(chance / maximum * Resolution));
            return new ScriptVariableCheckResult(
                true, roll < threshold, ScriptVariableErrorCode.None, string.Empty);
        }
    }

    /// <summary>只识别十进制数、变量引用、括号、四则运算、幂和 Random(min,max)。</summary>
    internal sealed class ScriptDecimalExpressionParser
    {
        internal const int MaximumLength = 1024;
        internal const int MaximumTokens = 256;
        internal const int MaximumDepth = 32;

        private readonly string _source;
        private readonly Func<string, ScriptVariableResult> _resolveVariable;
        private readonly Func<int, int, int> _random;
        private int _position;
        private int _tokens;
        private int _depth;

        internal ScriptDecimalExpressionParser(
            string source,
            Func<string, ScriptVariableResult> resolveVariable,
            Func<int, int, int> random)
        {
            _source = source ?? string.Empty;
            _resolveVariable = resolveVariable;
            _random = random;
        }

        internal ScriptVariableResult Parse()
        {
            if (_source.Length == 0 || _source.Length > MaximumLength)
                return Fail("公式为空或超过 1024 个字符。");
            try
            {
                decimal value = ParseAdditive();
                SkipWhiteSpace();
                if (_position != _source.Length) return Fail("公式包含无法识别的内容。");
                return ScriptVariableResult.Ok(ScriptVariableValue.FromDecimal(Normalize(value)));
            }
            catch (ExpressionException error)
            {
                return Fail(error.Message);
            }
            catch (OverflowException)
            {
                return ScriptVariableResult.Fail(ScriptVariableErrorCode.Overflow, "公式计算溢出。");
            }
        }

        private decimal ParseAdditive()
        {
            decimal left = ParseMultiplicative();
            while (true)
            {
                SkipWhiteSpace();
                if (Take('+')) left = Normalize(checked(left + ParseMultiplicative()));
                else if (Take('-')) left = Normalize(checked(left - ParseMultiplicative()));
                else return left;
            }
        }

        private decimal ParseMultiplicative()
        {
            decimal left = ParsePower();
            while (true)
            {
                SkipWhiteSpace();
                if (Take('*')) left = Normalize(checked(left * ParsePower()));
                else if (Take('/'))
                {
                    decimal right = ParsePower();
                    if (right == 0m) throw new ExpressionException("公式不能除以零。");
                    left = Normalize(checked(left / right));
                }
                else return left;
            }
        }

        private decimal ParsePower()
        {
            decimal value = ParseUnary();
            SkipWhiteSpace();
            if (!Take('^')) return value;
            decimal exponentValue = ParseUnary();
            if (exponentValue != decimal.Truncate(exponentValue) || exponentValue < 0m || exponentValue > 28m)
                throw new ExpressionException("幂指数必须是 0 到 28 的整数。");
            decimal result = 1m;
            for (int i = 0; i < (int)exponentValue; i++) result = Normalize(checked(result * value));
            return result;
        }

        private decimal ParseUnary()
        {
            SkipWhiteSpace();
            if (Take('+')) return ParseUnary();
            if (Take('-')) return checked(-ParseUnary());
            return ParsePrimary();
        }

        private decimal ParsePrimary()
        {
            CountToken();
            SkipWhiteSpace();
            if (Take('('))
            {
                if (++_depth > MaximumDepth) throw new ExpressionException("公式括号嵌套超过 32 层。");
                decimal value = ParseAdditive();
                SkipWhiteSpace();
                if (!Take(')')) throw new ExpressionException("公式缺少右括号。");
                _depth--;
                return value;
            }

            if (_position < _source.Length && (char.IsLetter(_source[_position]) ||
                                                _source[_position] == '_' ||
                                                _source[_position] == '$'))
            {
                string identifier = ReadIdentifier();
                SkipWhiteSpace();
                if (string.Equals(identifier, "Random", StringComparison.OrdinalIgnoreCase) && Take('('))
                    return ParseRandom();
                ScriptVariableResult resolved = _resolveVariable(identifier);
                if (!resolved.Success) throw new ExpressionException(resolved.Diagnostic);
                return resolved.Value.Kind switch
                {
                    ScriptVariableKind.Integer => resolved.Value.Integer,
                    ScriptVariableKind.Decimal => resolved.Value.Decimal,
                    _ => throw new ExpressionException("公式变量必须是整数或小数。")
                };
            }

            int start = _position;
            while (_position < _source.Length &&
                   (char.IsDigit(_source[_position]) || _source[_position] == '.')) _position++;
            string number = _source.Substring(start, _position - start);
            if (!decimal.TryParse(number, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal parsed))
                throw new ExpressionException("公式数字格式无效。");
            return parsed;
        }

        private decimal ParseRandom()
        {
            decimal minimumValue = ParseAdditive();
            SkipWhiteSpace();
            if (!Take(',')) throw new ExpressionException("Random 缺少上下界分隔符。");
            decimal maximumValue = ParseAdditive();
            SkipWhiteSpace();
            if (!Take(')')) throw new ExpressionException("Random 缺少右括号。");
            if (minimumValue != decimal.Truncate(minimumValue) || maximumValue != decimal.Truncate(maximumValue) ||
                minimumValue < int.MinValue || maximumValue > int.MaxValue || minimumValue > maximumValue)
                throw new ExpressionException("Random 上下界必须是有效整数且最小值不能大于最大值。");
            int minimum = (int)minimumValue;
            int maximum = (int)maximumValue;
            if (maximum == int.MaxValue) throw new ExpressionException("Random 最大值不能等于 Int32.MaxValue。");
            return _random(minimum, maximum + 1);
        }

        private string ReadIdentifier()
        {
            int start = _position;
            while (_position < _source.Length)
            {
                char value = _source[_position];
                if (!char.IsLetterOrDigit(value) && value != '_' && value != '.' && value != '$') break;
                _position++;
            }
            return _source.Substring(start, _position - start);
        }

        private void CountToken()
        {
            if (++_tokens > MaximumTokens) throw new ExpressionException("公式词元超过 256 个。");
        }

        private void SkipWhiteSpace()
        {
            while (_position < _source.Length && char.IsWhiteSpace(_source[_position])) _position++;
        }

        private bool Take(char expected)
        {
            if (_position >= _source.Length || _source[_position] != expected) return false;
            _position++;
            CountToken();
            return true;
        }

        private static decimal Normalize(decimal value) =>
            decimal.Round(value, ScriptVariableValue.MaximumDecimalScale, MidpointRounding.AwayFromZero);

        private static ScriptVariableResult Fail(string diagnostic) =>
            ScriptVariableResult.Fail(ScriptVariableErrorCode.InvalidExpression, diagnostic);

        private sealed class ExpressionException : Exception
        {
            internal ExpressionException(string message) : base(message) { }
        }
    }
}
