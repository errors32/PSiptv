namespace PSiptv.Core;

/// <summary>
/// Calculates a stable transfer rate from a monotonically increasing byte
/// counter. Short periods without reads are expected while a media buffer is
/// full, so the last measured value is retained briefly instead of flashing 0.
/// </summary>
public sealed class TransferRateEstimator
{
    private const long MinimumSampleMilliseconds = 500;
    private const long StalledAfterMilliseconds = 3000;
    private long previousBytes;
    private long previousTimestamp;
    private long lastActivityTimestamp;
    private bool initialized;

    public double MegabitsPerSecond { get; private set; }

    public double Sample(long totalBytes, long timestampMilliseconds)
    {
        if (!initialized || totalBytes < previousBytes || timestampMilliseconds < previousTimestamp)
        {
            initialized = true;
            previousBytes = Math.Max(0, totalBytes);
            previousTimestamp = timestampMilliseconds;
            lastActivityTimestamp = timestampMilliseconds;
            MegabitsPerSecond = 0;
            return 0;
        }

        var elapsedMilliseconds = timestampMilliseconds - previousTimestamp;
        if (elapsedMilliseconds < MinimumSampleMilliseconds) return MegabitsPerSecond;

        var bytes = totalBytes - previousBytes;
        previousBytes = totalBytes;
        previousTimestamp = timestampMilliseconds;
        if (bytes > 0)
        {
            var measured = bytes * 8d / (elapsedMilliseconds * 1000d);
            MegabitsPerSecond = MegabitsPerSecond <= 0
                ? measured
                : MegabitsPerSecond * 0.55 + measured * 0.45;
            lastActivityTimestamp = timestampMilliseconds;
        }
        else if (timestampMilliseconds - lastActivityTimestamp >= StalledAfterMilliseconds)
        {
            MegabitsPerSecond = 0;
        }

        return MegabitsPerSecond;
    }

    public void Reset()
    {
        initialized = false;
        previousBytes = previousTimestamp = lastActivityTimestamp = 0;
        MegabitsPerSecond = 0;
    }
}
