using System.Security.Cryptography;
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
            var url = row.Elements().FirstOrDefault(e => e.Name.LocalName == "enclosure")?.Attribute("url")?.Value ?? "";
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) continue;
            var title = Value("title");
            if (title.Length == 0) continue;
            var guid = Value("guid");
            result.Add(new MediaItem(Id(podcast.Id + "\n" + (guid.Length > 0 ? guid : url)), title,
                podcast.Name, MediaKind.Podcast, url, podcast.Logo, ParentSeriesId: podcast.Id));
        }
        return result.DistinctBy(e => e.Id).ToArray();
    }
}
