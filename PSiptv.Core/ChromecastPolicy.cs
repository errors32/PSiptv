namespace PSiptv.Core;

public sealed record ChromecastPlaybackState(
    bool Supported,
    bool Connected,
    string DeviceName = "",
    string Title = "",
    bool IsPlaying = false,
    long PositionMilliseconds = 0,
    long DurationMilliseconds = 0,
    double Volume = 1,
    bool Muted = false,
    bool HasQueue = false);

public static class ChromecastPolicy
{
    public static string ContentType(MediaItem item)
    {
        if (!Uri.TryCreate(item.Url, UriKind.Absolute, out var uri)) return "video/mp4";
        return Path.GetExtension(uri.AbsolutePath).ToLowerInvariant() switch
        {
            ".m3u8" => "application/x-mpegURL",
            ".mpd" => "application/dash+xml",
            ".ts" => "video/mp2t",
            ".mkv" => "video/x-matroska",
            ".webm" => "video/webm",
            _ => "video/mp4"
        };
    }

    public static double ClampVolume(double volume) => Math.Clamp(volume, 0, 1);

    public static long SeekTarget(long positionMilliseconds, long deltaMilliseconds, long durationMilliseconds)
    {
        var maximum = durationMilliseconds > 0 ? durationMilliseconds : long.MaxValue;
        if (deltaMilliseconds > 0 && positionMilliseconds > long.MaxValue - deltaMilliseconds) return maximum;
        return Math.Clamp(positionMilliseconds + deltaMilliseconds, 0, maximum);
    }
}
