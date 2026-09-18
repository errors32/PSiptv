namespace PSiptv.Core;

public enum CatalogSort { Provider, NameAscending, NameDescending }

public sealed class CustomCategory
{
    public string Name { get; set; } = "";
    public MediaKind Kind { get; set; }
    public HashSet<string> Items { get; set; } = [];
}

public sealed class CatalogPreferences
{
    public HashSet<string> Hidden { get; set; } = [];
    public List<string> Order { get; set; } = [];
    public List<CustomCategory> Custom { get; set; } = [];
    public HashSet<string> Locked { get; set; } = [];
    public bool ParentalEnabled { get; set; }
    public string ParentalPinHash { get; set; } = "";
    public Dictionary<MediaKind, CatalogSort> Sorting { get; set; } = [];
    public static string CategoryKey(MediaKind kind, string category) => $"{(int)kind}:{category}";
    public static string ItemKey(MediaItem item) => $"{(int)item.Kind}:{(item.Url.Length > 0 ? item.Url : item.Id)}";
    public bool IsHidden(MediaItem item) => Hidden.Contains(CategoryKey(item.Kind, item.Category));
    public bool IsLocked(MediaItem item) => ParentalEnabled && (Locked.Contains(CategoryKey(item.Kind, item.Category)) || Custom.Any(c => c.Kind == item.Kind && Locked.Contains(CategoryKey(c.Kind, c.Name)) && (c.Items.Contains(ItemKey(item)) || (item.ParentSeriesId.Length > 0 && c.Items.Contains($"{(int)MediaKind.Series}:{item.ParentSeriesId}")))));
    public bool Matches(MediaItem item, string? category) => category is null || item.Category == category ||
        Custom.Any(c => c.Kind == item.Kind && c.Name == category && c.Items.Contains(ItemKey(item)));
    public IReadOnlyList<MediaItem> Filter(IEnumerable<MediaItem> items, MediaKind kind, string? category, string query)
    {
        var result = items.Where(i => i.Kind == kind && !IsHidden(i) && Matches(i, category) && i.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase));
        return Sorting.GetValueOrDefault(kind) switch
        {
            CatalogSort.NameAscending => result.OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase).ToArray(),
            CatalogSort.NameDescending => result.OrderByDescending(i => i.Name, StringComparer.CurrentCultureIgnoreCase).ToArray(),
            _ => result.ToArray()
        };
    }
    public IReadOnlyList<string> Categories(IEnumerable<MediaItem> items, MediaKind kind) => items
        .Where(i => i.Kind == kind && !IsHidden(i)).Select(i => i.Category)
        .Concat(Custom.Where(c => c.Kind == kind && !Hidden.Contains(CategoryKey(kind, c.Name))).Select(c => c.Name)).Distinct()
        .OrderBy(c => { var index = Order.IndexOf(CategoryKey(kind, c)); return index < 0 ? int.MaxValue : index; })
        .ThenBy(c => c, StringComparer.CurrentCultureIgnoreCase).ToArray();
}

public static class StreamPreferences
{
    public static MediaItem ApplyFormat(PlaylistAccount account, MediaItem item, string format)
    {
        if (account.Provider != ProviderType.Xtream || item.Kind != MediaKind.Channel || format is not ("ts" or "m3u8")) return item;
        // Only rewrite provider-generated live URLs; never alter arbitrary M3U signed URLs.
        var prefix = account.Url.TrimEnd('/') + "/live/";
        if (!item.Url.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return item;
        var uri = new UriBuilder(item.Url);
        var dot = uri.Path.LastIndexOf('.');
        if (dot > uri.Path.LastIndexOf('/')) uri.Path = uri.Path[..dot] + "." + format;
        return item with { Url = uri.Uri.AbsoluteUri };
    }
}

public sealed record WatchEntry(MediaItem Item, DateTimeOffset WatchedAt, double PositionSeconds);
