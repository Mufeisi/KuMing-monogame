using System;
using System.Collections.Generic;
using System.Globalization;

namespace Server.Scripting
{
    public static class LingFengNumericCommandExecutor
    {
        public static uint PlanGoldGain(uint current, uint requested) =>
            requested > uint.MaxValue - current ? uint.MaxValue - current : requested;

        public static uint PlanGoldTake(uint current, uint requested) =>
            requested > current ? current : requested;

        public static bool TryCheck(
            long current,
            IReadOnlyList<string> comparisons,
            out bool matched,
            out string diagnostic)
        {
            matched = false;
            diagnostic = string.Empty;
            if (comparisons == null || comparisons.Count is not (2 or 4))
            {
                diagnostic = "数值检测必须包含一组或两组“操作符 数值”。";
                return false;
            }

            for (int index = 0; index < comparisons.Count; index += 2)
            {
                if (!long.TryParse(comparisons[index + 1], NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out long expected))
                {
                    diagnostic = $"数值参数无效：{comparisons[index + 1]}。";
                    return false;
                }

                bool currentMatch = comparisons[index] switch
                {
                    "=" or "==" => current == expected,
                    "!=" or "<>" => current != expected,
                    ">" => current > expected,
                    ">=" => current >= expected,
                    "<" => current < expected,
                    "<=" => current <= expected,
                    _ => false
                };
                if (comparisons[index] is not ("=" or "==" or "!=" or "<>" or ">" or ">=" or "<" or "<="))
                {
                    diagnostic = $"比较操作符无效：{comparisons[index]}。";
                    return false;
                }
                if (!currentMatch)
                {
                    matched = false;
                    return true;
                }
            }

            matched = true;
            return true;
        }

        public static bool TryAdjust(
            long current,
            string operation,
            string operandText,
            long minimum,
            long maximum,
            bool clampBelowMinimum,
            out long result,
            out string diagnostic)
        {
            result = current;
            diagnostic = string.Empty;
            if (!long.TryParse(operandText, NumberStyles.Integer, CultureInfo.InvariantCulture, out long operand) || operand < 0)
            {
                diagnostic = $"调整值无效：{operandText}。";
                return false;
            }

            try
            {
                long candidate = operation switch
                {
                    "=" => operand,
                    "+" => checked(current + operand),
                    "-" => checked(current - operand),
                    _ => throw new ArgumentException($"调整操作符无效：{operation}。")
                };
                if (candidate < minimum && clampBelowMinimum) candidate = minimum;
                if (candidate < minimum || candidate > maximum)
                {
                    diagnostic = $"调整结果 {candidate} 超出允许范围 {minimum}..{maximum}。";
                    return false;
                }
                result = candidate;
                return true;
            }
            catch (OverflowException)
            {
                diagnostic = "调整结果发生整数溢出。";
                return false;
            }
            catch (ArgumentException exception)
            {
                diagnostic = exception.Message;
                return false;
            }
        }
    }
}
