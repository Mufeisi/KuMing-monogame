namespace Launcher.ThemeRuntime;

public sealed record LauncherProgressState(
    string Stage,
    string CurrentFile,
    long CurrentReceived,
    long CurrentTotal,
    long OverallReceived,
    long OverallTotal,
    double BytesPerSecond,
    int PendingFiles = 0)
{
    public double CurrentFraction => CurrentTotal <= 0 ? 0 : Math.Clamp((double)CurrentReceived / CurrentTotal, 0, 1);
    public double OverallFraction => OverallTotal <= 0 ? 0 : Math.Clamp((double)OverallReceived / OverallTotal, 0, 1);
    public long RemainingBytes => Math.Max(0, OverallTotal - OverallReceived);
}
