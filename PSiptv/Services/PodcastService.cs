using System.Text.Json;
using PSiptv.Core;
using PSiptv.Views;

namespace PSiptv.Services;

public static class PodcastService
{
    private static readonly HttpClient client = new() { Timeout = TimeSpan.FromSeconds(20), MaxResponseContentBufferSize = 8 * 1024 * 1024 };
    private static readonly Dictionary<string, (DateTimeOffset Date, IReadOnlyList<MediaItem> Items)> cache = [];

    public static async Task<IReadOnlyList<MediaItem>> SearchAsync(string query, CancellationToken token)
    {
        query = string.IsNullOrWhiteSpace(query) ? "Portugal" : query.Trim();
        if (cache.TryGetValue(query, out var saved) && DateTimeOffset.UtcNow - saved.Date < TimeSpan.FromHours(12)) return saved.Items;
        var json = await client.GetStringAsync("https://itunes.apple.com/search?media=podcast&entity=podcast&country=PT&limit=100&term=" +
            Uri.EscapeDataString(query), token);
        using var document = JsonDocument.Parse(json);
        var result = new List<MediaItem>();
        foreach (var row in document.RootElement.GetProperty("results").EnumerateArray())
        {
            string Text(string property) => row.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? "" : "";
            var feed = Text("feedUrl");
            if (!Uri.TryCreate(feed, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) continue;
            var name = Text("collectionName");
            if (name.Length == 0) continue;
            result.Add(new(PodcastFeed.Id(feed), name, Text("artistName"), MediaKind.Podcast,
                feed, Text("artworkUrl600"), HasEpisodes: true));
        }
        var items = result.DistinctBy(e => e.Id).ToArray();
        cache[query] = (DateTimeOffset.UtcNow, items);
        return items;
    }

    public static async Task<IReadOnlyList<MediaItem>> EpisodesAsync(MediaItem podcast, CancellationToken token)
        => PodcastFeed.Parse(await client.GetStringAsync(WebAddress.Require(podcast.Url), token), podcast);

    public static Task OpenAsync(Page owner, MediaItem item) => item.HasEpisodes
        ? owner.Navigation.PushAsync(new PodcastsPage(item)) : PlaybackService.PlayAsync(owner, item);
}
