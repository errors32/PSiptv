using System.Text.Json;
using PSiptv.Core;
using PSiptv.Views;

namespace PSiptv.Services;

public static class PodcastService
{
    private static readonly HttpClient client = new() { Timeout = TimeSpan.FromSeconds(20), MaxResponseContentBufferSize = 8 * 1024 * 1024 };
    public static string SearchCacheKey(string query) => "search:" + (string.IsNullOrWhiteSpace(query) ? "Portugal" : query.Trim());

    public static async Task<IReadOnlyList<MediaItem>> SearchAsync(string query, CancellationToken token, bool refresh = false)
    {
        query = string.IsNullOrWhiteSpace(query) ? "Portugal" : query.Trim();
        var saved = await PodcastCacheService.ReadAsync(SearchCacheKey(query), token);
        if (!refresh && saved is not null && DateTimeOffset.UtcNow - saved.FetchedAt < TimeSpan.FromHours(12)) return saved.Items;
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
        await PodcastCacheService.SaveAsync(SearchCacheKey(query), items, token);
        return items;
    }

    public static async Task<IReadOnlyList<MediaItem>> EpisodesAsync(MediaItem podcast, CancellationToken token, bool refresh = false)
    {
        var saved = await PodcastCacheService.ReadAsync(podcast.Url, token);
        if (!refresh && saved is not null && DateTimeOffset.UtcNow - saved.FetchedAt < TimeSpan.FromMinutes(10)) return saved.Items;
        var episodes = PodcastFeed.Parse(await client.GetStringAsync(WebAddress.Require(podcast.Url), token), podcast);
        await PodcastCacheService.SaveAsync(podcast.Url, episodes, token);
        return episodes;
    }

    public static async Task OpenAsync(Page owner, MediaItem item)
    {
        if (item.HasEpisodes) { await owner.Navigation.PushAsync(new PodcastsPage(item)); return; }
        var snapshot = await FavoritesService.SnapshotAsync();
        var resume = PodcastPolicy.ResumePosition(snapshot?.PodcastProgress?.GetValueOrDefault(item.Id),
            snapshot?.HeardEpisodes.ContainsKey(item.Id) == true);
        if (AppServices.ActiveAccount is { } account &&
            await OfflineDownloadService.FindAsync(account.Id, UserProfileService.Active.Id, item) is { IsComplete: true } download)
            item = OfflineDownloadService.PlaybackItem(download);
        await PlaybackService.PlayAsync(owner, item, resume: resume);
    }
}
