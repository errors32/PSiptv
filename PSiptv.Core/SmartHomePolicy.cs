namespace PSiptv.Core;

public sealed record UpcomingProgramme(MediaItem Channel, TvProgramme Programme);

public static class SmartHomePolicy
{
    public static IReadOnlyList<WatchEntry> ContinueWatching(IEnumerable<WatchEntry> history, int limit = 10) =>
        DistinctHistory(history.Where(entry => entry.Item.Kind != MediaKind.Channel && entry.PositionSeconds > 0), limit);

    public static IReadOnlyList<WatchEntry> RecentChannels(IEnumerable<WatchEntry> history, int limit = 10) =>
        DistinctHistory(history.Where(entry => entry.Item.Kind == MediaKind.Channel), limit);

    public static IReadOnlyList<UpcomingProgramme> StartingSoon(IEnumerable<UpcomingProgramme> programmes,
        DateTimeOffset now, TimeSpan window, int limit = 10) => programmes
        .Where(item => item.Programme.Start >= now && item.Programme.Start <= now.Add(window))
        .OrderBy(item => item.Programme.Start)
        .ThenBy(item => item.Channel.Name, StringComparer.CurrentCultureIgnoreCase)
        .Take(Math.Max(0, limit)).ToArray();

    private static IReadOnlyList<WatchEntry> DistinctHistory(IEnumerable<WatchEntry> history, int limit) => history
        .OrderByDescending(entry => entry.WatchedAt)
        .GroupBy(entry => CatalogPreferences.ItemKey(entry.Item), StringComparer.Ordinal)
        .Select(group => group.First())
        .Take(Math.Max(0, limit)).ToArray();
}
