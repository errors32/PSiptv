using System.Security.Cryptography;
using System.Text.Json;
using PSiptv.Core;

namespace PSiptv.Services;

public static class CatalogCacheService
{
    private const string EncryptionKeyName = "psiptv.catalog-cache.encryption-key.v1";
    private static readonly SemaphoreSlim gate = new(1, 1);

    public static async Task<Dictionary<MediaKind, IReadOnlyList<MediaItem>>> LoadAsync(string accountId,
        Func<MediaKind, IReadOnlyList<MediaItem>, Task>? onLoaded = null, MediaKind? preferredKind = null)
    {
        var result = new Dictionary<MediaKind, IReadOnlyList<MediaItem>>();
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var existing = Enum.GetValues<MediaKind>().Where(kind => File.Exists(PathFor(accountId, kind)))
                .OrderBy(kind => kind == preferredKind ? 0 : 1).ToArray();
            if (existing.Length == 0) return result;

            var key = await GetOrCreateKeyAsync().ConfigureAwait(false);
            try
            {
                foreach (var kind in existing)
                {
                    var path = PathFor(accountId, kind);
                    IReadOnlyList<MediaItem> items;
                    try
                    {
                        var protectedData = await File.ReadAllBytesAsync(path).ConfigureAwait(false);
                        items = await Task.Run(() => CatalogCacheCodec.Decode(
                            protectedData, key, Context(accountId, kind))).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or JsonException)
                    {
                        try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                        continue;
                    }
                    result[kind] = items;
                    if (onLoaded is not null) await onLoaded(kind, items).ConfigureAwait(false);
                }
            }
            finally { CryptographicOperations.ZeroMemory(key); }
        }
        finally { gate.Release(); }
        return result;
    }

    public static async Task SaveAsync(string accountId, MediaKind kind, IReadOnlyList<MediaItem> items)
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(CacheDirectory);
            var key = await GetOrCreateKeyAsync().ConfigureAwait(false);
            try
            {
                var protectedData = await Task.Run(() => CatalogCacheCodec.Encode(
                    items, key, Context(accountId, kind))).ConfigureAwait(false);
                var path = PathFor(accountId, kind);
                var temporaryPath = path + ".new";
                await File.WriteAllBytesAsync(temporaryPath, protectedData).ConfigureAwait(false);
                File.Move(temporaryPath, path, true);
            }
            finally { CryptographicOperations.ZeroMemory(key); }
        }
        finally { gate.Release(); }
    }

    public static async Task DeleteAsync(string accountId)
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            foreach (var kind in Enum.GetValues<MediaKind>())
            {
                var path = PathFor(accountId, kind);
                try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                try { File.Delete(path + ".new"); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
        finally { gate.Release(); }
    }

    public static async Task ClearAllAsync()
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!Directory.Exists(CacheDirectory)) return;
            foreach (var path in Directory.EnumerateFiles(CacheDirectory))
            {
                try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
        finally { gate.Release(); }
        CatalogOptionsService.Catalogs.Clear();
    }

    private static string CacheDirectory => Path.Combine(FileSystem.AppDataDirectory, "catalog-cache");

    private static string PathFor(string accountId, MediaKind kind)
    {
        var id = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(accountId)));
        return Path.Combine(CacheDirectory, $"{id}.{(int)kind}.bin");
    }

    private static string Context(string accountId, MediaKind kind) => $"PSiptv catalog v1\0{accountId}\0{(int)kind}";

    private static async Task<byte[]> GetOrCreateKeyAsync()
    {
        var encoded = await SecureStorage.Default.GetAsync(EncryptionKeyName).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(encoded))
        {
            try
            {
                var existing = Convert.FromBase64String(encoded);
                if (existing.Length == AccountDataProtection.KeySize) return existing;
            }
            catch (FormatException) { }
        }

        var key = AccountDataProtection.CreateKey();
        await SecureStorage.Default.SetAsync(EncryptionKeyName, Convert.ToBase64String(key)).ConfigureAwait(false);
        return key;
    }
}
