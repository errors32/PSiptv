namespace PSiptv.Core;

public static class PictureInPicturePolicy
{
    public static bool Allows(MediaItem? item, bool hasVideo) =>
        hasVideo && item is not null
        && item.Kind is MediaKind.Channel or MediaKind.Movie or MediaKind.Series
        && !AudioPlaybackPolicy.PersistsAcrossNavigation(item)
        && !PodcastFeed.IsPodcast(item);
}
