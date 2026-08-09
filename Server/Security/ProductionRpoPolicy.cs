namespace Server.Security;

public static class ProductionRpoPolicy
{
    internal const int MinimumSaveDelayMinutes = 1;
    internal const int MaximumSaveDelayMinutes = 5;

    public static void ValidateSaveDelay(int minutes, bool enforceProductionMaximum)
    {
        if (minutes < MinimumSaveDelayMinutes)
            throw new InvalidOperationException("自动保存间隔必须至少为 1 分钟");
        if (enforceProductionMaximum && minutes > MaximumSaveDelayMinutes)
            throw new InvalidOperationException("正式服自动保存间隔必须在 1～5 分钟之间");
    }

    internal static long GetNextAutoSaveDeadline(long currentMilliseconds, int minutes)
    {
        ValidateSaveDelay(minutes, enforceProductionMaximum: false);
        return checked(currentMilliseconds + (long)minutes * Settings.Minute);
    }
}
