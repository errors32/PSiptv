namespace PSiptv.Services;

public sealed record StorageSnapshot(
    long RecordingsBytes, int RecordingFiles,
    long DownloadsBytes, int DownloadFiles,
    long CacheBytes, int CacheFiles,
    long TemporaryBytes, int TemporaryFiles,
    long DeviceTotalBytes, long DeviceFreeBytes)
{
    public long AppManagedBytes => RecordingsBytes + DownloadsBytes + CacheBytes + TemporaryBytes;
}

public static class StorageManagerService
{
    public static Task<StorageSnapshot> MeasureAsync() => Task.Run(() =>
    {
        var recordings = DvrService.MeasureRecordingStorage();
        var downloads = OfflineDownloadService.MeasureDownloadStorage();
        var cache = Sum(
            MeasureDirectory(Path.Combine(FileSystem.AppDataDirectory, "catalog-cache")),
            MeasureDirectory(Path.Combine(FileSystem.AppDataDirectory, "epg-cache")),
            MeasureDirectory(Path.Combine(FileSystem.CacheDirectory, "logos")));
        var temporary = Sum(
            MeasureDirectory(Path.Combine(FileSystem.CacheDirectory, "timeshift")),
            MeasureDirectory(Path.Combine(FileSystem.CacheDirectory, "external-playback")));

        var (total, free) = DeviceSpace();
        return new StorageSnapshot(recordings.Bytes, recordings.Files, downloads.Bytes, downloads.Files,
            cache.Bytes, cache.Files, temporary.Bytes, temporary.Files, total, free);
    });

    public static async Task ClearCacheAsync()
    {
        CacheService.Clear();
        await CatalogCacheService.ClearAllAsync();
        TimeshiftService.Cleanup();
        PlaybackService.CleanOldPlaylists();
    }

    private static (long Bytes, int Files) MeasureDirectory(string path)
    {
        long bytes = 0;
        var files = 0;
        try
        {
            if (!Directory.Exists(path)) return (0, 0);
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                try { bytes += new FileInfo(file).Length; files++; }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return (bytes, files);
    }

    private static (long Bytes, int Files) Sum(params (long Bytes, int Files)[] values) =>
        (values.Sum(value => value.Bytes), values.Sum(value => value.Files));

    private static (long Total, long Free) DeviceSpace()
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(FileSystem.AppDataDirectory));
            if (string.IsNullOrWhiteSpace(root)) return (0, 0);
            var drive = new DriveInfo(root);
            return (drive.TotalSize, drive.AvailableFreeSpace);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return (0, 0); }
    }
}
