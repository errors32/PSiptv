namespace PSiptv.Core;

public sealed record PodcastOptions(bool AutomaticDownloads = false, bool WifiOnly = true,
    int MaximumMegabytes = 1024, bool DeletePlayedDownloads = false, string[]? SelectedFeeds = null);

public static class PodcastPolicy
{
    public static MediaItem? AdjacentEpisode(MediaItem current, IEnumerable<MediaItem> episodes, int direction)
    {
        if (string.IsNullOrEmpty(current.ParentSeriesId) || direction is not (-1 or 1)) return null;
        var ordered = PodcastFeed.OrderByDate(episodes.Where(e => !e.HasEpisodes &&
            e.ParentSeriesId == current.ParentSeriesId).DistinctBy(e => e.Id), newestFirst: false).ToArray();
        var index = Array.FindIndex(ordered, e => e.Id == current.Id);
        var target = index + direction;
        return index >= 0 && target >= 0 && target < ordered.Length ? ordered[target] : null;
    }

    public static bool RestartBeforePrevious(double position, bool hasPrevious) =>
        !hasPrevious || double.IsFinite(position) && position > 3;

    public static MediaItem? NextEpisode(MediaItem current, IEnumerable<MediaItem> episodes)
    {
        if (current.PublishedAt is not { } date || string.IsNullOrEmpty(current.ParentSeriesId)) return null;
        return episodes.Where(e => e.Id != current.Id && !e.HasEpisodes &&
                e.ParentSeriesId == current.ParentSeriesId && e.PublishedAt is { } published && published.Date > date.Date)
            .OrderBy(e => e.PublishedAt).FirstOrDefault();
    }

    public static bool CanDownloadAutomatically(PodcastOptions options, bool internet, bool wifi) =>
        options.AutomaticDownloads && internet && (!options.WifiOnly || wifi);

    public static bool IsNew(MediaItem episode, DateTimeOffset? lastVisit, DateTimeOffset now) =>
        !episode.HasEpisodes && episode.PublishedAt is { } date && lastVisit is { } visit && date > visit && date <= now;

    public static IReadOnlyList<MediaItem> AutomaticCandidates(IEnumerable<MediaItem> episodes,
        IReadOnlyDictionary<string, PodcastHeardEntry> heard, DateTimeOffset? previousCheck) => episodes
        .Where(e => !e.HasEpisodes && !heard.ContainsKey(e.Id) &&
            (previousCheck is null || e.PublishedAt is { } date && date > previousCheck))
        .OrderByDescending(e => e.PublishedAt).Take(previousCheck is null ? 1 : 20).ToArray();

    public static double ResumePosition(PodcastPlaybackEntry? entry, bool heard) =>
        heard || entry is null || !double.IsFinite(entry.Position) || entry.Position < 0 ||
        entry.Duration > 0 && entry.Position >= entry.Duration - 2 ? 0 : entry.Position;
}
