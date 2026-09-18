namespace PSiptv.Core;

public static class StreamRecoveryPolicy
{
    public const int MaxAttempts = 3;
    public static readonly TimeSpan BufferingTimeout = TimeSpan.FromSeconds(10);

    public static TimeSpan DelayForAttempt(int attempt) =>
        TimeSpan.FromSeconds(attempt switch
        {
            <= 1 => 1,
            2 => 2,
            _ => 4
        });
}
