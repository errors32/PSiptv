using PSiptv.Core;

namespace PSiptv.Services;

public static class PodcastListingService
{
    public static async Task LoadAsync(IEnumerable<MediaItem> items,
        Action<MediaItem, IReadOnlyList<MediaItem>> loaded, Action updated, CancellationToken token)
    {
        // Keep browsing responsive and limit concurrent requests to podcast publishers.
        foreach (var batch in items.Where(i => PodcastFeed.IsPodcast(i) && i.HasEpisodes).DistinctBy(i => i.Id).Chunk(4))
        {
            var results = await Task.WhenAll(batch.Select(async feed =>
            {
                IReadOnlyList<MediaItem>? episodes;
                try { episodes = await PodcastService.EpisodesAsync(feed, token); }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(ex);
                    episodes = (await PodcastCacheService.ReadAsync(feed.Url, token))?.Items;
                }
                return (Feed: feed, Episodes: episodes);
            }));
            token.ThrowIfCancellationRequested();
            foreach (var result in results)
                if (result.Episodes is not null) loaded(result.Feed, result.Episodes);
            updated();
        }
    }
}
