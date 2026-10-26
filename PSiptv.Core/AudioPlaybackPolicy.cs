namespace PSiptv.Core;

public static class AudioPlaybackPolicy
{
    public static bool IsInFinalMinute(double position, double duration) =>
        double.IsFinite(position) && double.IsFinite(duration) &&
        position > 0 && duration > 0 && position <= duration && duration - position <= 60;

    public static bool PersistsAcrossNavigation(MediaItem item) =>
        item.Id.StartsWith("radio-browser:", StringComparison.Ordinal)
        || (PodcastFeed.IsPodcast(item) && !item.HasEpisodes);

    public static double SeekTarget(double position, double duration, int seconds) =>
        Math.Clamp(position + seconds, 0, duration > 0 ? duration : double.MaxValue);
}
