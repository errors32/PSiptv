using System.Text.RegularExpressions;

namespace PSiptv.Core;

public sealed record M3uPlaylist(IReadOnlyList<MediaItem> Items, string EpgUrl);

public static partial class M3uParser
{
    [GeneratedRegex("([\\w-]+)\\s*=\\s*(?:\"([^\"]*)\"|'([^']*)')", RegexOptions.CultureInvariant)]
    private static partial Regex Attributes();

    public static M3uPlaylist Parse(string text, Uri source)
    {
        var items = new List<MediaItem>();
        string name = "", group = "", logo = "", epgId = "", epgUrl = "";
        string catchup = "", catchupSource = "";
        var catchupDays = 0;
        string defaultCatchup = "", defaultCatchupSource = "";
        var defaultCatchupDays = 0;
        var hasHeader = false;
        foreach (var raw in text.TrimStart('\uFEFF').Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("#EXTM3U", StringComparison.OrdinalIgnoreCase))
            {
                hasHeader = true;
                var attrs = ReadAttributes(line);
                epgUrl = attrs.GetValueOrDefault("url-tvg", attrs.GetValueOrDefault("x-tvg-url", ""));
                if (epgUrl.Length > 0 && Uri.TryCreate(source, epgUrl, out var epg)) epgUrl = epg.AbsoluteUri;
                defaultCatchup = CatchupMode(attrs);
                defaultCatchupSource = attrs.GetValueOrDefault("catchup-source", "");
                defaultCatchupDays = PositiveInt(attrs.GetValueOrDefault("catchup-days", ""));
            }
            else if (line.StartsWith("#EXTINF:", StringComparison.OrdinalIgnoreCase))
            {
                var attrs = ReadAttributes(line);
                // The separator is the first comma outside quoted attributes.
                var quote = '\0';
                var comma = -1;
                for (var i = 0; i < line.Length; i++)
                {
                    if (line[i] == quote) quote = '\0';
                    else if (quote == '\0' && line[i] is '\'' or '"') quote = line[i];
                    else if (quote == '\0' && line[i] == ',') { comma = i; break; }
                }
                name = comma >= 0 ? line[(comma + 1)..].Trim() : attrs.GetValueOrDefault("tvg-name", "");
                group = attrs.GetValueOrDefault("group-title", "");
                logo = attrs.GetValueOrDefault("tvg-logo", "");
                epgId = attrs.GetValueOrDefault("tvg-id", "");
                catchup = CatchupMode(attrs);
                catchupSource = attrs.GetValueOrDefault("catchup-source", "");
                catchupDays = PositiveInt(attrs.GetValueOrDefault("catchup-days", ""));
                if (catchup.Length == 0) catchup = defaultCatchup;
                if (catchupSource.Length == 0) catchupSource = defaultCatchupSource;
                if (catchupDays == 0) catchupDays = defaultCatchupDays;
            }
            else if (line.StartsWith("#EXTGRP:", StringComparison.OrdinalIgnoreCase)) group = line[8..].Trim();
            else if (line.Length > 0 && !line.StartsWith('#'))
            {
                if (Uri.TryCreate(source, line, out var uri) && uri.Scheme is "http" or "https" or "file")
                {
                    var kind = Classify(group, uri.AbsolutePath);
                    items.Add(new MediaItem(items.Count.ToString(), string.IsNullOrWhiteSpace(name) ? $"Canal {items.Count + 1}" : name,
                        string.IsNullOrWhiteSpace(group) ? "Sem categoria" : group, kind, uri.AbsoluteUri,
                        Uri.TryCreate(source, logo, out var image) && logo.Length > 0 && image.Scheme is "http" or "https" or "file" ? image.AbsoluteUri : "", epgId,
                        HasCatchup: kind == MediaKind.Channel && CatchupEnabled(catchup, catchupSource, catchupDays),
                        CatchupDays: catchupDays, CatchupMode: catchup, CatchupSource: catchupSource));
                }
                name = group = logo = epgId = catchup = catchupSource = "";
                catchupDays = 0;
            }
        }
        if (!hasHeader) throw new InvalidOperationException("O endereço não devolveu uma lista M3U válida (#EXTM3U).");
        if (items.Count == 0) throw new InvalidOperationException("A lista não contém conteúdos reproduzíveis.");
        return new(items, epgUrl);
    }

    private static Dictionary<string, string> ReadAttributes(string line) => Attributes().Matches(line)
        .GroupBy(m => m.Groups[1].Value, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(g => g.Key, g => g.Last().Groups[2].Success ? g.Last().Groups[2].Value : g.Last().Groups[3].Value, StringComparer.OrdinalIgnoreCase);

    private static string CatchupMode(IReadOnlyDictionary<string, string> attrs) =>
        attrs.GetValueOrDefault("catchup", attrs.GetValueOrDefault("catchup-type", "")).Trim().ToLowerInvariant();
    private static int PositiveInt(string value) => int.TryParse(value, out var days) && days > 0 ? Math.Min(days, 365) : 0;
    private static bool CatchupEnabled(string mode, string template, int days) => days > 0 &&
        mode is not ("" or "none" or "off" or "0") && (template.Length > 0 || mode is "xc" or "xtream" or "xstream" or "xtream-codes");

    private static MediaKind Classify(string group, string path)
    {
        var tag = group.ToLowerInvariant();
        if (tag.Contains("series") || tag.Contains("séries") || path.Contains("/series/", StringComparison.OrdinalIgnoreCase)) return MediaKind.Series;
        if (tag.Contains("filme") || tag.Contains("movie") || tag.Contains("vod") ||
            path.Contains("/movie/", StringComparison.OrdinalIgnoreCase) ||
            new[] { ".mp4", ".mkv", ".avi", ".mov" }.Contains(Path.GetExtension(path).ToLowerInvariant())) return MediaKind.Movie;
        return MediaKind.Channel;
    }
}
