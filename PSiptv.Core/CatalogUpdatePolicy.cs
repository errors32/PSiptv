namespace PSiptv.Core;

public enum CatalogUpdateSchedule
{
    Manual,
    Startup,
    Daily,
    Weekly
}

public sealed record CatalogChannelChanges(int Added, int Removed);

public static class CatalogUpdatePolicy
{
    public static bool IsDue(CatalogUpdateSchedule schedule, DateTimeOffset? lastUpdate,
        DateTimeOffset now, bool isStartup) => schedule switch
    {
        CatalogUpdateSchedule.Manual => false,
        CatalogUpdateSchedule.Startup => isStartup,
        CatalogUpdateSchedule.Daily => lastUpdate is null || now - lastUpdate >= TimeSpan.FromDays(1),
        CatalogUpdateSchedule.Weekly => lastUpdate is null || now - lastUpdate >= TimeSpan.FromDays(7),
        _ => false
    };

    public static CatalogChannelChanges CompareChannels(IEnumerable<MediaItem> previous,
        IEnumerable<MediaItem> current, ProviderType provider)
    {
        var oldKeys = ChannelKeys(previous, provider);
        var newKeys = ChannelKeys(current, provider);
        return new(newKeys.Except(oldKeys).Count(), oldKeys.Except(newKeys).Count());
    }

    private static HashSet<string> ChannelKeys(IEnumerable<MediaItem> items, ProviderType provider) =>
        items.Where(item => item.Kind == MediaKind.Channel)
            .Select(item => provider is ProviderType.M3U or ProviderType.LocalM3U && item.Url.Length > 0 ? item.Url : item.Id)
            .Where(key => key.Length > 0)
            .ToHashSet(StringComparer.Ordinal);
}
