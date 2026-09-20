namespace PSiptv.Core;

public static class BrowserStreamDetection
{
    private static readonly string[] MediaExtensions =
    [
        ".m3u8", ".mpd", ".mp4", ".m4v", ".webm", ".mov", ".mkv", ".avi", ".flv"
    ];

    public static bool IsLikelyStream(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") || string.IsNullOrWhiteSpace(uri.Host)) return false;

        var path = uri.AbsolutePath;
        if (MediaExtensions.Any(extension => path.EndsWith(extension, StringComparison.OrdinalIgnoreCase)))
            return true;

        var query = uri.Query;
        return query.Contains("m3u8", StringComparison.OrdinalIgnoreCase) ||
               query.Contains("application/vnd.apple.mpegurl", StringComparison.OrdinalIgnoreCase) ||
               query.Contains("application/dash+xml", StringComparison.OrdinalIgnoreCase) ||
               query.Contains("format=mpd", StringComparison.OrdinalIgnoreCase) ||
               query.Contains("type=mpd", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsMediaContentType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var contentType = value.Split(';', 2)[0].Trim();
        return contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase) ||
               contentType.Equals("application/vnd.apple.mpegurl", StringComparison.OrdinalIgnoreCase) ||
               contentType.Equals("application/x-mpegurl", StringComparison.OrdinalIgnoreCase) ||
               contentType.Equals("application/dash+xml", StringComparison.OrdinalIgnoreCase) ||
               contentType.Equals("application/vnd.ms-sstr+xml", StringComparison.OrdinalIgnoreCase);
    }

    public static int Priority(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return 0;
        var candidate = uri.AbsolutePath + uri.Query;
        if (candidate.Contains(".m3u8", StringComparison.OrdinalIgnoreCase)) return 4;
        if (candidate.Contains(".mpd", StringComparison.OrdinalIgnoreCase) ||
            candidate.Contains("application/dash+xml", StringComparison.OrdinalIgnoreCase)) return 3;
        if (candidate.Contains(".mp4", StringComparison.OrdinalIgnoreCase) ||
            candidate.Contains(".webm", StringComparison.OrdinalIgnoreCase)) return 2;
        return IsLikelyStream(value) ? 1 : 0;
    }
}
