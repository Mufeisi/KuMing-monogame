using System;
using System.Globalization;

namespace Server.Scripting
{
    public static class LingFengSocialCommandExecutor
    {
        public static bool TryParseQuestIndex(string text, out int index, out string diagnostic)
        {
            diagnostic = string.Empty;
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out index) || index <= 0)
            {
                diagnostic = $"任务编号无效：{text}。";
                return false;
            }

            return true;
        }

        public static bool TryPlanGuildExperience(
            bool hasGuild,
            string amountText,
            out uint amount,
            out string diagnostic)
        {
            amount = 0;
            diagnostic = string.Empty;
            if (!hasGuild)
            {
                diagnostic = "当前玩家不属于任何行会。";
                return false;
            }

            if (!uint.TryParse(amountText, NumberStyles.Integer, CultureInfo.InvariantCulture, out amount) || amount == 0)
            {
                diagnostic = $"行会经验无效：{amountText}。";
                return false;
            }

            return true;
        }
    }
}
