using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PSiptv.Core;

public sealed record FavoriteEntry(string Key, MediaItem Item);

public sealed class FavoriteStore(
    Func<string, Task<string?>> read,
    Func<string, string, Task> write,
    Func<string, Task> remove)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private static string StorageKey(string accountId) => $"psiptv.favorites.v1.{accountId}";

    public static string ItemKey(PlaylistAccount account, MediaItem item)
    {
        // M3U IDs are positional, so use the stream address to survive reordering.
        var identity = account.Provider is ProviderType.M3U or ProviderType.LocalM3U ? item.Url : item.Id;
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
            var entries = await ReadAsync(scope);
            var key = ItemKey(account, item);
            entries.RemoveAll(e => e.Key == key);
            if (favorite) entries.Add(new(key, item));
            await write(StorageKey(scope), JsonSerializer.Serialize(entries));
            return entries;
        }
        finally { gate.Release(); }
    }

    public async Task DeleteAsync(string accountId)
    {
        await gate.WaitAsync();
        try { await remove(StorageKey(accountId)); }
        finally { gate.Release(); }
    }

    public async Task ReplaceAsync(string accountId, IReadOnlyList<FavoriteEntry> entries)
    {
        await gate.WaitAsync();
        try { await write(StorageKey(accountId), JsonSerializer.Serialize(entries)); }
        finally { gate.Release(); }
    }

    private async Task<List<FavoriteEntry>> ReadAsync(string accountId)
    {
        var json = await read(StorageKey(accountId));
        return json is null ? [] : JsonSerializer.Deserialize<List<FavoriteEntry>>(json)
            ?? throw new InvalidOperationException("Não foi possível ler os favoritos guardados.");
    }
}
