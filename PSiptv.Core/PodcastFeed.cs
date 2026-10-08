using System.Security.Cryptography;
using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace PSiptv.Core;

public static class PodcastFeed
{
    public static bool IsPodcast(MediaItem item) => item.Id.StartsWith("podcast:", StringComparison.Ordinal);
    public static string Id(string identity) => "podcast:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));

    public static IReadOnlyList<MediaItem> Parse(string xml, MediaItem podcast)
    {
        using var text = new StringReader(xml);
        using var reader = XmlReader.Create(text, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 8 * 1024 * 1024
        });
        var document = XDocument.Load(reader);
        var result = new List<MediaItem>();
        foreach (var row in document.Descendants().Where(e => e.Name.LocalName == "item"))
        {
            string Value(string name) => row.Elements().FirstOrDefault(e => e.Name.LocalName == name)?.Value.Trim() ?? "";
            var sources = row.Descendants().Where(e => e.Name.LocalName is "enclosure" or "content")
                .Select(e => (Url: e.Attribute("url")?.Value ?? "", Type: e.Attribute("type")?.Value ?? ""))
                .Where(e => Uri.TryCreate(e.Url, UriKind.Absolute, out var address) && address.Scheme is "http" or "https")
                .ToArray();
            var audio = sources.FirstOrDefault(e => e.Type.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) ||
                Path.GetExtension(new Uri(e.Url).AbsolutePath).ToLowerInvariant() is ".mp3" or ".m4a" or ".ogg");
            var video = sources.FirstOrDefault(e => e.Type.StartsWith("video/", StringComparison.OrdinalIgnoreCase) ||
                Path.GetExtension(new Uri(e.Url).AbsolutePath).ToLowerInvariant() is ".mp4" or ".m4v" or ".webm");
            var source = audio.Url is not null ? audio : video.Url is not null ? video : sources.FirstOrDefault();
            var url = source.Url ?? "";
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) continue;
            var title = Value("title");
            if (title.Length == 0) continue;
            var guid = Value("guid");
            result.Add(new MediaItem(Id(podcast.Id + "\n" + (guid.Length > 0 ? guid : url)), title,
                podcast.Name, MediaKind.Podcast, url, podcast.Logo, ParentSeriesId: podcast.Id,
                PublishedAt: PublicationDate(Value("pubDate")), PodcastVideoUrl: video.Url ?? "", MediaType: source.Type ?? "",
                PodcastAudioUrl: url));
        }
        return result.DistinctBy(e => e.Id).ToArray();
    }

    private static DateTimeOffset? PublicationDate(string value)
    {
        // RSS commonly uses an RFC 822 numeric offset without a colon.
        var normalized = System.Text.RegularExpressions.Regex.Replace(value, @"([+-]\d{2})(\d{2})$", "$1:$2");
        return DateTimeOffset.TryParse(normalized, CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal, out var date) ? date : null;
    }

    public static IReadOnlyList<MediaItem> OrderByDate(IEnumerable<MediaItem> episodes, bool newestFirst)
    {
        // Undated episodes always follow dated ones; ties keep the original feed order.
        var datedFirst = episodes.OrderByDescending(item => item.PublishedAt.HasValue);
        return (newestFirst ? datedFirst.ThenByDescending(item => item.PublishedAt) :
            datedFirst.ThenBy(item => item.PublishedAt)).ToArray();
    }
}
