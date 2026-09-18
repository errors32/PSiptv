using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace PSiptv.Core;

internal static class AdditionalSourceClient
{
    public static async Task ValidateAsync(HttpClient http, PlaylistAccount account, string userAgent, CancellationToken ct)
    {
        var channels = await GetCatalogAsync(http, account, MediaKind.Channel, userAgent, ct).ConfigureAwait(false);
        if (channels.Count == 0 && account.Provider is ProviderType.HDHomeRun or ProviderType.Tvheadend or ProviderType.Stalker)
            throw new InvalidOperationException("A fonte não devolveu canais reproduzíveis.");
        if (account.Provider is ProviderType.Jellyfin or ProviderType.Plex)
        {
            var movies = await GetCatalogAsync(http, account, MediaKind.Movie, userAgent, ct).ConfigureAwait(false);
            var series = await GetCatalogAsync(http, account, MediaKind.Series, userAgent, ct).ConfigureAwait(false);
            if (channels.Count == 0 && movies.Count == 0 && series.Count == 0)
                throw new InvalidOperationException("A fonte não devolveu conteúdos reproduzíveis.");
        }
    }

    public static Task<IReadOnlyList<MediaItem>> GetCatalogAsync(HttpClient http, PlaylistAccount account,
        MediaKind kind, string userAgent, CancellationToken ct) => account.Provider switch
    {
        ProviderType.HDHomeRun => kind == MediaKind.Channel ? HdHomeRunAsync(http, account, userAgent, ct) : Empty(),
        ProviderType.Tvheadend => kind == MediaKind.Channel ? TvheadendAsync(http, account, userAgent, ct) : Empty(),
        ProviderType.Jellyfin => JellyfinAsync(http, account, kind, userAgent, ct),
        ProviderType.Plex => PlexAsync(http, account, kind, userAgent, ct),
        ProviderType.Stalker => kind == MediaKind.Channel ? StalkerAsync(http, account, userAgent, ct) : Empty(),
        _ => Empty()
    };

    private static Task<IReadOnlyList<MediaItem>> Empty() => Task.FromResult<IReadOnlyList<MediaItem>>([]);

    private static async Task<IReadOnlyList<MediaItem>> HdHomeRunAsync(HttpClient http, PlaylistAccount account,
        string userAgent, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(await SendAsync(http, account.Url.TrimEnd('/') + "/lineup.json", userAgent, ct));
        var result = new List<MediaItem>();
        foreach (var row in Rows(doc.RootElement))
        {
            var url = Text(row, "URL");
            var name = Text(row, "GuideName");
            var number = Text(row, "GuideNumber");
            if (!Http(url)) continue;
            result.Add(new MediaItem(number.Length > 0 ? number : result.Count.ToString(),
                name.Length > 0 ? name : $"Canal {number}", "HDHomeRun", MediaKind.Channel, url,
                EpgId: number));
        }
        return result;
    }

    private static async Task<IReadOnlyList<MediaItem>> TvheadendAsync(HttpClient http, PlaylistAccount account,
        string userAgent, CancellationToken ct)
    {
        var baseUrl = account.Url.TrimEnd('/');
        var text = await SendAsync(http, baseUrl + "/playlist/channels.m3u", userAgent, ct,
            Basic(account.Username, account.Password)).ConfigureAwait(false);
        return M3uParser.Parse(text, new Uri(baseUrl + "/")).Items
            .Where(item => item.Kind == MediaKind.Channel)
            .Select(item => item with { Category = item.Category == "Sem categoria" ? "Tvheadend" : item.Category })
            .ToArray();
    }

    private static async Task<IReadOnlyList<MediaItem>> JellyfinAsync(HttpClient http, PlaylistAccount account,
        MediaKind kind, string userAgent, CancellationToken ct)
    {
        var root = account.Url.TrimEnd('/');
        var token = account.Password;
        var type = kind switch { MediaKind.Channel => "LiveTvChannel", MediaKind.Movie => "Movie", _ => "Episode" };
        var endpoint = kind == MediaKind.Channel
            ? $"{root}/LiveTv/Channels?EnableImages=true&Fields=Genres"
            : $"{root}/Items?Recursive=true&IncludeItemTypes={type}&Fields=Genres,MediaSources,SeriesName&EnableImages=true";
        using var doc = JsonDocument.Parse(await SendAsync(http, endpoint, userAgent, ct, ("X-Emby-Token", token)));
        var items = doc.RootElement.TryGetProperty("Items", out var supplied) ? supplied : doc.RootElement;
        var result = new List<MediaItem>();
        foreach (var row in Rows(items))
        {
            var id = Text(row, "Id");
            if (id.Length == 0) continue;
            var name = Text(row, "Name");
            var series = Text(row, "SeriesName");
            var category = FirstArrayText(row, "Genres");
            var stream = $"{root}/Videos/{Uri.EscapeDataString(id)}/stream?static=true&api_key={Uri.EscapeDataString(token)}";
            var image = $"{root}/Items/{Uri.EscapeDataString(id)}/Images/Primary?api_key={Uri.EscapeDataString(token)}";
            result.Add(new MediaItem(id, series.Length > 0 ? $"{series} · {name}" : name,
                category.Length > 0 ? category : kind == MediaKind.Channel ? "Jellyfin TV" : "Jellyfin",
                kind, stream, image, Text(row, "ChannelNumber")));
        }
        return result;
    }

    private static async Task<IReadOnlyList<MediaItem>> PlexAsync(HttpClient http, PlaylistAccount account,
        MediaKind kind, string userAgent, CancellationToken ct)
    {
        if (kind == MediaKind.Channel) return [];
        var root = account.Url.TrimEnd('/');
        var token = Uri.EscapeDataString(account.Password);
        var sections = XDocument.Parse(await SendAsync(http, $"{root}/library/sections?X-Plex-Token={token}", userAgent, ct));
        var wanted = kind == MediaKind.Movie ? "movie" : "show";
        var result = new List<MediaItem>();
        foreach (var section in sections.Descendants("Directory").Where(node => (string?)node.Attribute("type") == wanted))
        {
            var key = (string?)section.Attribute("key");
            if (string.IsNullOrWhiteSpace(key)) continue;
            var suffix = kind == MediaKind.Series ? "&type=4" : "";
            var xml = await SendAsync(http, $"{root}/library/sections/{Uri.EscapeDataString(key)}/all?X-Plex-Token={token}{suffix}", userAgent, ct);
            foreach (var video in XDocument.Parse(xml).Descendants("Video"))
            {
                var ratingKey = (string?)video.Attribute("ratingKey") ?? "";
                var part = video.Descendants("Part").Select(node => (string?)node.Attribute("key")).FirstOrDefault();
                if (ratingKey.Length == 0 || string.IsNullOrWhiteSpace(part)) continue;
                var title = (string?)video.Attribute("title") ?? "Conteúdo Plex";
                var show = (string?)video.Attribute("grandparentTitle") ?? "";
                var thumb = (string?)video.Attribute("thumb") ?? "";
                result.Add(new MediaItem(ratingKey, show.Length > 0 ? $"{show} · {title}" : title,
                    (string?)section.Attribute("title") ?? "Plex", kind,
                    Absolute(root, part) + (part.Contains('?') ? "&" : "?") + "X-Plex-Token=" + token,
                    thumb.Length > 0 ? Absolute(root, thumb) + "?X-Plex-Token=" + token : ""));
            }
        }
        return result;
    }

    private static async Task<IReadOnlyList<MediaItem>> StalkerAsync(HttpClient http, PlaylistAccount account,
        string userAgent, CancellationToken ct)
    {
        var portal = account.Url.TrimEnd('/') + "/portal.php";
        var mac = account.Username.Trim();
        var cookie = $"mac={Uri.EscapeDataString(mac)}; stb_lang=pt; timezone=Europe%2FLisbon";
        using var handshake = JsonDocument.Parse(await SendAsync(http,
            portal + "?type=stb&action=handshake&JsHttpRequest=1-xml", userAgent, ct,
            ("Cookie", cookie), ("X-User-Agent", "Model: MAG254; Link: Ethernet")));
        var payload = handshake.RootElement.TryGetProperty("js", out var js) ? js : handshake.RootElement;
        var token = Text(payload, "token");
        if (token.Length == 0) throw new InvalidOperationException("O portal Stalker não devolveu um token válido.");
        using var channels = JsonDocument.Parse(await SendAsync(http,
            portal + "?type=itv&action=get_all_channels&JsHttpRequest=1-xml", userAgent, ct,
            ("Cookie", cookie), ("Authorization", "Bearer " + token),
            ("X-User-Agent", "Model: MAG254; Link: Ethernet")));
        var channelPayload = channels.RootElement.TryGetProperty("js", out var list) ? list : channels.RootElement;
        if (channelPayload.ValueKind == JsonValueKind.Object && channelPayload.TryGetProperty("data", out var data)) channelPayload = data;
        var result = new List<MediaItem>();
        foreach (var row in Rows(channelPayload))
        {
            var cmd = Text(row, "cmd").Trim();
            if (cmd.Length == 0) continue;
            var id = Text(row, "id");
            var name = Text(row, "name");
            result.Add(new MediaItem(id.Length > 0 ? id : result.Count.ToString(), name.Length > 0 ? name : "Canal Stalker",
                "Stalker", MediaKind.Channel, account.Url, SafeHttp(Text(row, "logo")), SourceCommand: cmd));
        }
        return result;
    }

    public static async Task<string> ResolveStalkerStreamAsync(HttpClient http, PlaylistAccount account,
        string command, string userAgent, CancellationToken ct)
    {
        var portal = account.Url.TrimEnd('/') + "/portal.php";
        var mac = account.Username.Trim();
        var cookie = $"mac={Uri.EscapeDataString(mac)}; stb_lang=pt; timezone=Europe%2FLisbon";
        using var handshake = JsonDocument.Parse(await SendAsync(http,
            portal + "?type=stb&action=handshake&JsHttpRequest=1-xml", userAgent, ct,
            ("Cookie", cookie), ("X-User-Agent", "Model: MAG254; Link: Ethernet")));
        var payload = handshake.RootElement.TryGetProperty("js", out var js) ? js : handshake.RootElement;
        var token = Text(payload, "token");
        if (token.Length == 0) throw new InvalidOperationException("O portal Stalker não devolveu um token válido.");
        var requestUrl = portal + "?type=itv&action=create_link&cmd=" + Uri.EscapeDataString(command) +
                         "&series=0&forced_storage=undefined&disable_ad=0&download=0&JsHttpRequest=1-xml";
        using var link = JsonDocument.Parse(await SendAsync(http, requestUrl, userAgent, ct,
            ("Cookie", cookie), ("Authorization", "Bearer " + token),
            ("X-User-Agent", "Model: MAG254; Link: Ethernet")));
        var linkPayload = link.RootElement.TryGetProperty("js", out var linkJs) ? linkJs : link.RootElement;
        var url = Text(linkPayload, "cmd").Trim();
        if (url.StartsWith("ffmpeg ", StringComparison.OrdinalIgnoreCase)) url = url[7..].Trim();
        if (!Http(url)) throw new InvalidOperationException("O portal Stalker não devolveu uma transmissão HTTP válida.");
        return url;
    }

    public static Task<string> DownloadTvheadendGuideAsync(HttpClient http, PlaylistAccount account,
        string url, string userAgent, CancellationToken ct) =>
        SendAsync(http, url, userAgent, ct, Basic(account.Username, account.Password));

    private static async Task<string> SendAsync(HttpClient http, string url, string userAgent,
        CancellationToken ct, params (string Name, string Value)[] headers)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("User-Agent", userAgent);
        foreach (var (name, value) in headers)
            if (!string.IsNullOrWhiteSpace(value)) request.Headers.TryAddWithoutValidation(name, value);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        const int limit = 120_000_000;
        if (response.Content.Headers.ContentLength > limit)
            throw new InvalidOperationException("A resposta da fonte excede o limite de 120 MB.");
        var content = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (content.Length > limit) throw new InvalidOperationException("A resposta da fonte excede o limite de 120 MB.");
        return content;
    }

    private static (string, string) Basic(string username, string password) =>
        ("Authorization", "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(username + ":" + password)));
    private static IEnumerable<JsonElement> Rows(JsonElement element) => element.ValueKind == JsonValueKind.Array ? element.EnumerateArray() : [];
    private static string Text(JsonElement row, string key) => row.ValueKind == JsonValueKind.Object && row.TryGetProperty(key, out var value) && value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined) ? value.ToString() : "";
    private static string FirstArrayText(JsonElement row, string key) => row.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().Select(item => item.ToString()).FirstOrDefault() ?? "" : "";
    private static bool Http(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";
    private static string SafeHttp(string value) => Http(value) ? value : "";
    private static string Absolute(string root, string path) => Http(path) ? path : root + "/" + path.TrimStart('/');
}
