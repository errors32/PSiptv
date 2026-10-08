using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PSiptv.Core;

public sealed record FavoriteEntry(string Key, MediaItem Item);
public sealed record PodcastHeardEntry(DateTimeOffset HeardAt, string PodcastId);
public sealed record FavoriteSnapshot(DateTimeOffset ModifiedAt, IReadOnlyList<FavoriteEntry> Entries,
    IReadOnlyDictionary<string, PodcastHeardEntry> HeardEpisodes);

public sealed class FavoriteStore(
    Func<string, Task<string?>> read,
    Func<string, string, Task> write,
    Func<string, Task> remove)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private static string StorageKey(string accountId) => $"psiptv.favorites.v1.{accountId}";
    private static string SnapshotKey(string scope) => $"psiptv.favorites.v2.{scope}";

    public static string ItemKey(PlaylistAccount account, MediaItem item)
    {
        // M3U IDs are positional, so use the stream address to survive reordering.
        var identity = item.Id.StartsWith("radio-browser:", StringComparison.Ordinal) || PodcastFeed.IsPodcast(item)
            ? item.Id
            : account.Provider is ProviderType.M3U or ProviderType.LocalM3U ? item.Url : item.Id;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        return $"{item.Kind}:{item.HasEpisodes}:{hash}";
    }

    public async Task<IReadOnlyList<FavoriteEntry>> LoadAsync(string accountId)
    {
        await gate.WaitAsync();
        try { return await ReadAsync(accountId); }
        finally { gate.Release(); }
    }

    public async Task<IReadOnlyList<FavoriteEntry>> SetAsync(PlaylistAccount account, MediaItem item, bool favorite,
        string? storageScope = null)
    {
        await gate.WaitAsync();
        try
        {
            var scope = storageScope ?? account.Id;
            var previous = await ReadSnapshotAsync(scope);
            var entries = previous.Entries.ToList();
            var key = ItemKey(account, item);
            entries.RemoveAll(e => e.Key == key);
            if (favorite) entries.Add(new(key, item));
            await SaveAsync(scope, new(NextDate(previous.ModifiedAt), entries, Prune(previous.HeardEpisodes, entries)));
            return entries;
        }
        finally { gate.Release(); }
    }

    public async Task DeleteAsync(string accountId)
    {
        await gate.WaitAsync();
        try { await remove(StorageKey(accountId)); await remove(SnapshotKey(accountId)); }
        finally { gate.Release(); }
    }

    public async Task ReplaceAsync(string accountId, IReadOnlyList<FavoriteEntry> entries,
        IReadOnlyDictionary<string, PodcastHeardEntry>? heard = null)
    {
        await gate.WaitAsync();
        try
        {
            var previous = await ReadSnapshotAsync(accountId);
            var merged = previous.HeardEpisodes.Concat(heard ?? new Dictionary<string, PodcastHeardEntry>())
                .GroupBy(e => e.Key).ToDictionary(g => g.Key, g => g.OrderByDescending(e => e.Value.HeardAt).First().Value);
            await SaveAsync(accountId, new(NextDate(previous.ModifiedAt), entries, Prune(merged, entries)));
        }
        finally { gate.Release(); }
    }

    private async Task<List<FavoriteEntry>> ReadAsync(string accountId)
        => (await ReadSnapshotAsync(accountId)).Entries.ToList();

    public async Task<FavoriteSnapshot> SnapshotAsync(string scope)
    {
        await gate.WaitAsync();
        try { return await ReadSnapshotAsync(scope); }
        finally { gate.Release(); }
    }

    public async Task<bool> ImportNewerAsync(string scope, PlaylistAccount account, FavoriteSnapshot snapshot)
    {
        await gate.WaitAsync();
        try
        {
            var local = await ReadSnapshotAsync(scope);
            if (snapshot.ModifiedAt <= local.ModifiedAt) return false;
            var entries = snapshot.Entries.Select(e => new FavoriteEntry(ItemKey(account, e.Item), e.Item))
                .DistinctBy(e => e.Key).ToArray();
            await SaveAsync(scope, snapshot with { Entries = entries, HeardEpisodes = Prune(snapshot.HeardEpisodes, entries) });
            return true;
        }
        finally { gate.Release(); }
    }

    public Task SetHeardAsync(string scope, MediaItem episode, bool heard) => SetHeardAsync(scope, [episode], heard);

    public async Task SetHeardAsync(string scope, IReadOnlyList<MediaItem> episodes, bool heard)
    {
        await gate.WaitAsync();
        try
        {
            var snapshot = await ReadSnapshotAsync(scope);
            var favorites = snapshot.Entries.Where(e => PodcastFeed.IsPodcast(e.Item)).Select(e => e.Item.Id).ToHashSet();
            var values = snapshot.HeardEpisodes.ToDictionary(e => e.Key, e => e.Value);
            var date = NextDate(snapshot.ModifiedAt);
            var changed = false;
            foreach (var episode in episodes)
            {
                if (!PodcastFeed.IsPodcast(episode) || episode.HasEpisodes ||
                    !favorites.Contains(episode.Id) && !favorites.Contains(episode.ParentSeriesId) ||
                    heard == values.ContainsKey(episode.Id)) continue;
                if (heard) values[episode.Id] = new(date, episode.ParentSeriesId); else values.Remove(episode.Id);
                changed = true;
            }
            if (!changed) return;
            await SaveAsync(scope, snapshot with { ModifiedAt = date, HeardEpisodes = values });
        }
        finally { gate.Release(); }
    }

    private static IReadOnlyDictionary<string, PodcastHeardEntry> Prune(IReadOnlyDictionary<string, PodcastHeardEntry> heard,
        IReadOnlyList<FavoriteEntry> entries)
    {
        var ids = entries.Where(e => PodcastFeed.IsPodcast(e.Item)).Select(e => e.Item.Id).ToHashSet();
        return heard.Where(e => ids.Contains(e.Key) || ids.Contains(e.Value.PodcastId)).ToDictionary(e => e.Key, e => e.Value);
    }

    private static DateTimeOffset NextDate(DateTimeOffset previous)
    {
        var now = DateTimeOffset.UtcNow;
        return now > previous ? now : previous.AddTicks(1);
    }

    private Task SaveAsync(string scope, FavoriteSnapshot snapshot) =>
        write(SnapshotKey(scope), JsonSerializer.Serialize(snapshot));

    private async Task<FavoriteSnapshot> ReadSnapshotAsync(string accountId)
    {
        var snapshot = await read(SnapshotKey(accountId));
        if (snapshot is not null) return JsonSerializer.Deserialize<FavoriteSnapshot>(snapshot)
            ?? throw new InvalidOperationException("Não foi possível ler os favoritos guardados.");
        var json = await read(StorageKey(accountId));
        var entries = json is null ? [] : JsonSerializer.Deserialize<List<FavoriteEntry>>(json)
            ?? throw new InvalidOperationException("Não foi possível ler os favoritos guardados.");
        return new(DateTimeOffset.UnixEpoch, entries, new Dictionary<string, PodcastHeardEntry>());
    }
}
