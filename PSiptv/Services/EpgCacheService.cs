using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PSiptv.Core;

namespace PSiptv.Services;

internal sealed record EpgCacheEntry(DateTimeOffset Updated, IReadOnlyList<TvProgramme> Items);

internal static class EpgCacheService
{
    private static readonly SemaphoreSlim gate = new(1, 1);
    private static string CacheDirectory => Path.Combine(FileSystem.AppDataDirectory, "epg-cache");

    public static async Task<EpgCacheEntry?> LoadAsync(string accountId, string sourceKey)
    {
        await gate.WaitAsync();
        try
        {
            var path = PathFor(accountId, sourceKey);
            if (!File.Exists(path)) return null;
            try
            {
                await using var input = File.OpenRead(path);
                await using var gzip = new GZipStream(input, CompressionMode.Decompress);
                var stored = await JsonSerializer.DeserializeAsync<StoredEpgCache>(gzip);
                return stored is null ? null : new(stored.Updated, stored.Items);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
            {
                TryDelete(path);
                return null;
            }
        }
        finally { gate.Release(); }
    }

    public static async Task SaveAsync(string accountId, string sourceKey, EpgCacheEntry entry)
    {
        await gate.WaitAsync();
        try
        {
            Directory.CreateDirectory(CacheDirectory);
            var path = PathFor(accountId, sourceKey);
            var temporary = path + ".new";
            try
            {
                await using (var output = File.Create(temporary))
                await using (var gzip = new GZipStream(output, CompressionLevel.Optimal))
                    await JsonSerializer.SerializeAsync(gzip, new StoredEpgCache { Updated = entry.Updated, Items = entry.Items.ToList() });
                File.Move(temporary, path, true);
            }
            finally { TryDelete(temporary); }
        }
        finally { gate.Release(); }
    }

    public static DateTimeOffset? LastUpdated(string accountId)
    {
        try
        {
            if (!Directory.Exists(CacheDirectory)) return null;
            var prefix = AccountHash(accountId) + ".";
            return Directory.EnumerateFiles(CacheDirectory, prefix + "*.json.gz")
                .Select(File.GetLastWriteTimeUtc)
                .Where(value => value > DateTime.MinValue)
                .Select(value => (DateTimeOffset?)new DateTimeOffset(value, TimeSpan.Zero))
                .Max();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    public static async Task DeleteAsync(string accountId)
    {
        await gate.WaitAsync();
        try
        {
            if (!Directory.Exists(CacheDirectory)) return;
            var prefix = AccountHash(accountId) + ".";
            foreach (var path in Directory.EnumerateFiles(CacheDirectory, prefix + "*")) TryDelete(path);
        }
        finally { gate.Release(); }
    }

    public static void Clear()
    {
        try
        {
            if (!Directory.Exists(CacheDirectory)) return;
            foreach (var path in Directory.EnumerateFiles(CacheDirectory, "*.json.gz")) TryDelete(path);
            foreach (var path in Directory.EnumerateFiles(CacheDirectory, "*.json.gz.new")) TryDelete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static string PathFor(string accountId, string sourceKey) => Path.Combine(CacheDirectory,
        $"{AccountHash(accountId)}.{Hash(sourceKey)}.json.gz");
    private static string AccountHash(string accountId) => Hash(accountId);
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private sealed class StoredEpgCache
    {
        public DateTimeOffset Updated { get; init; }
        public List<TvProgramme> Items { get; init; } = [];
    }
}
