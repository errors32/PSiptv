namespace PSiptv.Core;

public static class TimeshiftPolicy
{
    public const int SkipSeconds = 30;
    public static int ClampOffset(int currentSecondsBehindLive, int changeSeconds, int maximumMinutes) =>
        Math.Clamp(currentSecondsBehindLive + changeSeconds, 0, Math.Clamp(maximumMinutes, 5, 120) * 60);

    public static string FormatOffset(int secondsBehindLive)
    {
        var value = TimeSpan.FromSeconds(Math.Max(0, secondsBehindLive));
        return value.TotalHours >= 1 ? $"−{(int)value.TotalHours}:{value.Minutes:00}:{value.Seconds:00}"
            : $"−{(int)value.TotalMinutes:00}:{value.Seconds:00}";
    }
}
