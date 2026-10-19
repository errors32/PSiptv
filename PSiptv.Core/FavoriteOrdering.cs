namespace PSiptv.Core;

public static class FavoriteOrdering
{
    public static IReadOnlyList<MediaItem> ChannelsByRecency(IEnumerable<MediaItem> items,
        IEnumerable<WatchEntry> history, PlaylistAccount account)
    {
        var result = items.ToArray();
        var watched = history.Where(entry => entry.Item.Kind == MediaKind.Channel)
            .GroupBy(entry => FavoriteStore.ItemKey(account, entry.Item))
            .ToDictionary(group => group.Key, group => group.Max(entry => entry.WatchedAt));
        var channels = result.Where(item => item.Kind == MediaKind.Channel)
            .OrderByDescending(item => watched.GetValueOrDefault(FavoriteStore.ItemKey(account, item)))
            .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
        // Keep films, series and podcasts in their existing positions.
        var next = 0;
        for (var index = 0; index < result.Length; index++)
            if (result[index].Kind == MediaKind.Channel) result[index] = channels[next++];
        return result;
    }
}
