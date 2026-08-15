namespace Server.Scripting.Variables
{
    public static class ScriptVariableArithmetic
    {
        public static ScriptVariableResult Apply(
            ScriptVariableValue left,
            ScriptVariableOperation operation,
            ScriptVariableValue right)
        {
            if (operation == ScriptVariableOperation.Set)
                return ScriptVariableResult.Ok(right);

            if (!IsNumeric(left.Kind) || !IsNumeric(right.Kind))
                return ScriptVariableResult.Fail(ScriptVariableErrorCode.TypeMismatch, "数值运算只接受 Integer 或 Decimal。");

            try
            {
                if (left.Kind == ScriptVariableKind.Decimal || right.Kind == ScriptVariableKind.Decimal)
                {
                    decimal leftValue = left.Kind == ScriptVariableKind.Decimal ? left.Decimal : left.Integer;
                    decimal rightValue = right.Kind == ScriptVariableKind.Decimal ? right.Decimal : right.Integer;
                    decimal result;

                    checked
                    {
                        result = operation switch
                        {
                            ScriptVariableOperation.Add => leftValue + rightValue,
                            ScriptVariableOperation.Subtract => leftValue - rightValue,
                            ScriptVariableOperation.Multiply => leftValue * rightValue,
                            ScriptVariableOperation.Divide when rightValue != 0 => leftValue / rightValue,
                            ScriptVariableOperation.Modulo when rightValue != 0 => leftValue % rightValue,
                            ScriptVariableOperation.Divide or ScriptVariableOperation.Modulo =>
                                throw new DivideByZeroException(),
                            _ => throw new InvalidOperationException("不支持的变量运算。")
                        };
                    }

                    if (ScriptVariableValue.GetDecimalScale(result) > ScriptVariableValue.MaximumDecimalScale)
                        return ScriptVariableResult.Fail(
                            ScriptVariableErrorCode.ScaleExceeded,
                            $"运算结果超过 {ScriptVariableValue.MaximumDecimalScale} 位小数。");

                    return ScriptVariableResult.Ok(ScriptVariableValue.FromDecimal(result));
                }

                long integerResult;
                checked
                {
                    integerResult = operation switch
                    {
                        ScriptVariableOperation.Add => left.Integer + right.Integer,
                        ScriptVariableOperation.Subtract => left.Integer - right.Integer,
                        ScriptVariableOperation.Multiply => left.Integer * right.Integer,
                        ScriptVariableOperation.Divide when right.Integer != 0 => left.Integer / right.Integer,
                        ScriptVariableOperation.Modulo when right.Integer != 0 => left.Integer % right.Integer,
                        ScriptVariableOperation.Divide or ScriptVariableOperation.Modulo =>
                            throw new DivideByZeroException(),
                        _ => throw new InvalidOperationException("不支持的变量运算。")
                    };
                }

                return ScriptVariableResult.Ok(ScriptVariableValue.FromInteger(integerResult));
            }
            catch (DivideByZeroException)
            {
                return ScriptVariableResult.Fail(ScriptVariableErrorCode.InvalidExpression, "除数不能为零。");
            }
            catch (OverflowException)
            {
                return ScriptVariableResult.Fail(ScriptVariableErrorCode.Overflow, "数值运算溢出。");
            }
            catch (InvalidOperationException error)
            {
                return ScriptVariableResult.Fail(ScriptVariableErrorCode.InvalidExpression, error.Message);
            }
        }

        public static ScriptVariableResult ConvertToInteger(
            ScriptVariableValue value,
            ScriptVariableRounding rounding)
        {
            if (value.Kind == ScriptVariableKind.Integer)
                return ScriptVariableResult.Ok(value);
            if (value.Kind != ScriptVariableKind.Decimal)
                return ScriptVariableResult.Fail(ScriptVariableErrorCode.TypeMismatch, "只有数值可以转换为整数。");

            try
            {
                decimal rounded = rounding switch
                {
                    ScriptVariableRounding.Round => decimal.Round(value.Decimal, 0, MidpointRounding.AwayFromZero),
                    ScriptVariableRounding.Floor => decimal.Floor(value.Decimal),
                    ScriptVariableRounding.Ceiling => decimal.Ceiling(value.Decimal),
                    ScriptVariableRounding.Truncate => decimal.Truncate(value.Decimal),
                    _ => throw new ArgumentOutOfRangeException(nameof(rounding))
                };
                return ScriptVariableResult.Ok(ScriptVariableValue.FromInteger(checked((long)rounded)));
            }
            catch (OverflowException)
            {
                return ScriptVariableResult.Fail(ScriptVariableErrorCode.Overflow, "取整结果超出 Int64 范围。");
            }
        }

        private static bool IsNumeric(ScriptVariableKind kind) =>
            kind == ScriptVariableKind.Integer || kind == ScriptVariableKind.Decimal;
    }
}
