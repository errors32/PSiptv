// The real cache and storage services are linked into this test executable.
// These adapters replace only MAUI filesystem roots and unrelated media services.
namespace PSiptv.Services;

internal static class FileSystem
{
    internal static string Root { get; set; } = "";
    internal static string AppDataDirectory => Path.Combine(Root, "data");
    internal static string CacheDirectory => Path.Combine(Root, "cache");
}
internal static class DvrService
{
    internal static (long Bytes, int Files) MeasureRecordingStorage() => (0, 0);
}
internal static class OfflineDownloadService
{
    internal static (long Bytes, int Files) MeasureDownloadStorage() => (0, 0);
}
internal static class CacheService { internal static void Clear() { } }
internal static class CatalogCacheService { internal static Task ClearAllAsync() => Task.CompletedTask; }
internal static class TimeshiftService { internal static void Cleanup() { } }
internal static class PlaybackService { internal static void CleanOldPlaylists() { } }
