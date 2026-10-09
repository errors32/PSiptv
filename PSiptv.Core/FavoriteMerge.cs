namespace PSiptv.Core;

public sealed record FavoriteMutation(FavoriteEntry? Entry, DateTimeOffset UpdatedAt, string ChangeId);
public sealed record HeardMutation(PodcastHeardEntry? Entry, DateTimeOffset UpdatedAt, string ChangeId);
public sealed record ProgressMutation(PodcastPlaybackEntry? Entry, DateTimeOffset UpdatedAt, string ChangeId);
public sealed record FavoriteSyncState(
    IReadOnlyDictionary<string, FavoriteMutation> Favorites,
    IReadOnlyDictionary<string, HeardMutation> Heard,
    IReadOnlyDictionary<string, ProgressMutation> Progress,
    IReadOnlyDictionary<string, DateTimeOffset>? PodcastResets = null);

/// <summary>Per-item last-writer merge. Null entries are retained deletion markers.</summary>
public static class FavoriteMerge
{
    private static FavoriteSyncState Seed(FavoriteSnapshot value) => value.Sync ?? new(
        value.Entries.ToDictionary(e => e.Key, e => new FavoriteMutation(e, value.ModifiedAt, "legacy")),
        value.HeardEpisodes.ToDictionary(e => e.Key, e => new HeardMutation(e.Value, e.Value.HeardAt, "legacy")),
        (value.PodcastProgress ?? new Dictionary<string, PodcastPlaybackEntry>())
            .ToDictionary(e => e.Key, e => new ProgressMutation(e.Value, e.Value.UpdatedAt, "legacy")));

    public static FavoriteSnapshot Track(FavoriteSnapshot previous, FavoriteSnapshot next)
    {
        var seed = Seed(previous);
        var favorites = seed.Favorites.ToDictionary();
        var heard = seed.Heard.ToDictionary();
        var progress = seed.Progress.ToDictionary();
        var resets = (seed.PodcastResets ?? new Dictionary<string, DateTimeOffset>()).ToDictionary();
        var remaining = next.Entries.Select(e => e.Key).ToHashSet();
        foreach (var removed in previous.Entries.Where(e => !remaining.Contains(e.Key) && PodcastFeed.IsPodcast(e.Item)))
            resets[removed.Item.Id] = next.ModifiedAt;
        Track(previous.Entries.ToDictionary(e => e.Key), next.Entries.ToDictionary(e => e.Key),
            (key, entry) => favorites[key] = new(entry, next.ModifiedAt, Guid.NewGuid().ToString("N")));
        Track(previous.HeardEpisodes, next.HeardEpisodes,
            (key, entry) => heard[key] = new(entry, next.ModifiedAt, Guid.NewGuid().ToString("N")));
        Track(previous.PodcastProgress ?? new Dictionary<string, PodcastPlaybackEntry>(),
            next.PodcastProgress ?? new Dictionary<string, PodcastPlaybackEntry>(),
            (key, entry) => progress[key] = new(entry, next.ModifiedAt, Guid.NewGuid().ToString("N")));
        return next with { Sync = new(favorites, heard, progress, resets) };
    }

    private static void Track<T>(IReadOnlyDictionary<string, T> previous, IReadOnlyDictionary<string, T> next,
        Action<string, T?> set) where T : class
    {
        foreach (var key in previous.Keys.Union(next.Keys))
        {
            previous.TryGetValue(key, out var before);
            next.TryGetValue(key, out var after);
            if (!EqualityComparer<T?>.Default.Equals(before, after)) set(key, after);
        }
    }

    public static (FavoriteSnapshot Snapshot, bool Changed) Merge(FavoriteSnapshot local, FavoriteSnapshot remote)
    {
        var left = Seed(local);
        var right = Seed(remote);
        var changed = false;
        var favorites = Merge(left.Favorites, right.Favorites, e => (e.UpdatedAt, e.ChangeId), ref changed);
        var heard = Merge(left.Heard, right.Heard, e => (e.UpdatedAt, e.ChangeId), ref changed);
        var progress = Merge(left.Progress, right.Progress, e => (e.UpdatedAt, e.ChangeId), ref changed);
        var resets = (left.PodcastResets ?? new Dictionary<string, DateTimeOffset>()).ToDictionary();
        foreach (var (key, date) in right.PodcastResets ?? new Dictionary<string, DateTimeOffset>())
            if (!resets.TryGetValue(key, out var previous) || date > previous) { resets[key] = date; changed = true; }
        var visits = (local.PodcastVisits ?? new Dictionary<string, DateTimeOffset>()).ToDictionary();
        foreach (var (key, date) in remote.PodcastVisits ?? new Dictionary<string, DateTimeOffset>())
            if (!visits.TryGetValue(key, out var previous) || date > previous) { visits[key] = date; changed = true; }
        if (!changed) return (local, false);
        var entries = favorites.Where(e => e.Value.Entry is not null).OrderBy(e => e.Key, StringComparer.Ordinal)
            .Select(e => e.Value.Entry!).ToArray();
        var ids = entries.Where(e => PodcastFeed.IsPodcast(e.Item)).Select(e => e.Item.Id).ToHashSet();
        bool AfterReset(string episodeId, string podcastId, DateTimeOffset updatedAt)
        {
            var reset = resets.GetValueOrDefault(episodeId);
            if (!ids.Contains(episodeId) && resets.GetValueOrDefault(podcastId) > reset) reset = resets[podcastId];
            return updatedAt > reset;
        }
        var heardEntries = heard.Where(e => e.Value.Entry is { } entry && (ids.Contains(e.Key) || ids.Contains(entry.PodcastId)) &&
            AfterReset(e.Key, entry.PodcastId, e.Value.UpdatedAt))
            .ToDictionary(e => e.Key, e => e.Value.Entry!);
        var positions = progress.Where(e => e.Value.Entry is { } entry && !heardEntries.ContainsKey(e.Key) &&
            (ids.Contains(e.Key) || ids.Contains(entry.Item.ParentSeriesId)))
            .Where(e => AfterReset(e.Key, e.Value.Entry!.Item.ParentSeriesId, e.Value.UpdatedAt))
            .ToDictionary(e => e.Key, e => e.Value.Entry!);
        return (new(local.ModifiedAt > remote.ModifiedAt ? local.ModifiedAt : remote.ModifiedAt,
            entries, heardEntries, positions, visits, new(favorites, heard, progress, resets)), true);
    }

    private static Dictionary<string, T> Merge<T>(IReadOnlyDictionary<string, T> local, IReadOnlyDictionary<string, T> remote,
        Func<T, (DateTimeOffset Date, string Id)> version, ref bool changed)
    {
        var result = local.ToDictionary();
        foreach (var (key, value) in remote)
        {
            var candidate = version(value);
            if (!result.TryGetValue(key, out var previous) || candidate.Date > version(previous).Date ||
                candidate.Date == version(previous).Date && string.CompareOrdinal(candidate.Id, version(previous).Id) > 0)
            { result[key] = value; changed = true; }
        }
        return result;
    }
}
