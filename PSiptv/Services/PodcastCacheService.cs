using System.Collections.Concurrent;
using System.Text.Json;
using PSiptv.Core;

namespace PSiptv.Services;

public sealed record PodcastCacheEntry(DateTimeOffset FetchedAt, IReadOnlyList<MediaItem> Items);

public static class PodcastCacheService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> gates = new();
    private static string PathFor(string key) => Path.Combine(FileSystem.CacheDirectory, "podcasts",
        PodcastFeed.Id(key)[8..] + ".json");

    public static async Task<PodcastCacheEntry?> ReadAsync(string key, CancellationToken token = default)
    {
        var path = PathFor(key);
        var gate = gates.GetOrAdd(key, _ => new(1));
        await gate.WaitAsync(token);
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > 16 * 1024 * 1024) return null;
            await using var input = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<PodcastCacheEntry>(input, cancellationToken: token);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return null; }
        finally { gate.Release(); }
    }

    public static async Task SaveAsync(string key, IReadOnlyList<MediaItem> items, CancellationToken token)
    {
        var path = PathFor(key);
        var gate = gates.GetOrAdd(key, _ => new(1));
        await gate.WaitAsync(token);
        try
        {
            var folder = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(folder);
            await using (var output = File.Create(path + ".tmp"))
                await JsonSerializer.SerializeAsync(output, new PodcastCacheEntry(DateTimeOffset.UtcNow, items), cancellationToken: token);
            File.Move(path + ".tmp", path, true);
            long retainedBytes = 0;
            var retainedCount = 0;
            foreach (var old in new DirectoryInfo(folder).GetFiles("*.json").OrderByDescending(f => f.LastWriteTimeUtc))
            {
                retainedBytes += old.Length;
                if (++retainedCount > 100 || retainedBytes > 64 * 1024 * 1024)
                    try { old.Delete(); } catch (IOException) { }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { System.Diagnostics.Debug.WriteLine(ex); }
        finally { gate.Release(); }
    }
}
