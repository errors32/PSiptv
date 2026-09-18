using System.Security.Cryptography;
using System.Text;

namespace PSiptv.Core;

public enum OfflineDownloadState { Queued, Downloading, Paused, Completed, Failed }

public sealed record OfflineDownload(
    string Id,
    string AccountId,
    string ProfileId,
    MediaItem Item,
    string SeriesName = "",
    OfflineDownloadState State = OfflineDownloadState.Queued,
    long BytesDownloaded = 0,
    long? TotalBytes = null,
    string FileName = "",
    string Error = "",
    DateTimeOffset? CreatedAt = null,
    DateTimeOffset? UpdatedAt = null)
{
    public double Progress => TotalBytes is > 0 ? Math.Clamp((double)BytesDownloaded / TotalBytes.Value, 0, 1) : 0;
    public bool IsComplete => State == OfflineDownloadState.Completed && FileName.Length > 0;
}

public static class OfflineDownloadPolicy
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
        { ".mp4", ".mkv", ".webm", ".mov", ".avi", ".m4v", ".ts" };

    public static string IdFor(string accountId, string profileId, MediaItem item)
    {
        var identity = item.ParentSeriesId.Length > 0
            ? $"{item.ParentSeriesId}\0{item.Id}"
            : item.Id.Length > 0 ? item.Id : item.Url;
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{accountId}\0{profileId}\0{(int)item.Kind}\0{identity}"))).ToLowerInvariant();
    }

    public static bool CanDownload(MediaItem item) => item.Kind != MediaKind.Channel &&
        Uri.TryCreate(item.Url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";

    public static string ExtensionFor(MediaItem item, string? mediaType = null)
    {
        if (Uri.TryCreate(item.Url, UriKind.Absolute, out var uri))
        {
            var extension = Path.GetExtension(uri.AbsolutePath);
            if (Extensions.Contains(extension)) return extension.ToLowerInvariant();
        }
        return mediaType?.ToLowerInvariant() switch
        {
            "video/x-matroska" => ".mkv",
            "video/webm" => ".webm",
            "video/mp2t" => ".ts",
            _ => ".mp4"
        };
    }

    public static string SafeFileStem(string name)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var clean = new string(name.Trim().Select(character => invalid.Contains(character) || char.IsControl(character) ? '_' : character).ToArray());
        return string.IsNullOrWhiteSpace(clean) ? "video" : clean[..Math.Min(clean.Length, 80)].TrimEnd('.', ' ');
    }
}
