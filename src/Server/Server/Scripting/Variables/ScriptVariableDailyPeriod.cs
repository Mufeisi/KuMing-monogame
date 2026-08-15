namespace Server.Scripting.Variables
{
    public static class ScriptVariableDailyPeriod
    {
        public static long FromServerTime(DateTime serverTime, int resetHour = 0)
        {
            if (resetHour < 0 || resetHour > 23)
                throw new ArgumentOutOfRangeException(nameof(resetHour), "每日重置小时必须是 0-23。");
            DateTime periodDate = serverTime.AddHours(-resetHour).Date;
            return periodDate.Year * 10000L + periodDate.Month * 100L + periodDate.Day;
        }
    }
}
