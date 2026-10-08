namespace PSiptv.Core;

public static class AudioPlaybackPolicy
{
    public static bool PersistsAcrossNavigation(MediaItem item) =>
        item.Id.StartsWith("radio-browser:", StringComparison.Ordinal)
        || (PodcastFeed.IsPodcast(item) && !item.HasEpisodes);

    public static double SeekTarget(double position, double duration, int seconds) =>
        Math.Clamp(position + seconds, 0, duration > 0 ? duration : double.MaxValue);
}
