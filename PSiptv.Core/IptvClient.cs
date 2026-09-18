using System.Net;
using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace PSiptv.Core;

public sealed class IptvClient(HttpClient http)
{
    public string UserAgent { get; set; } = "PSiptv/1.0";
    public async Task<string> DownloadAsync(string url, CancellationToken ct, int limit = 40_000_000)
    {
        WebAddress.Require(url);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > limit) throw new InvalidOperationException($"A resposta excede o limite de {limit / 1_000_000} MB.");
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            if (output.Length + read > limit) throw new InvalidOperationException($"A resposta excede o limite de {limit / 1_000_000} MB.");
            output.Write(buffer, 0, read);
        }
        return DecodeText(output.ToArray(), url, limit);
    }

    private static string DecodeText(byte[] data, string source, int limit)
    {
        var gzip = source.EndsWith(".gz", StringComparison.OrdinalIgnoreCase) ||
                   data is [0x1f, 0x8b, ..];
        if (!gzip) return Encoding.UTF8.GetString(data);
        using var input = new MemoryStream(data);
        using var compressed = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = compressed.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (output.Length + read > limit)
                throw new InvalidOperationException($"A resposta descomprimida excede o limite de {limit / 1_000_000} MB.");
            output.Write(buffer, 0, read);
        }
        return Encoding.UTF8.GetString(output.ToArray());
    }

    public async Task ValidateAsync(PlaylistAccount account, CancellationToken ct)
    {
        if (account.Provider is ProviderType.M3U or ProviderType.LocalM3U)
        {
            await LoadM3uAsync(account, ct);
            return;
        }
        if (account.Provider != ProviderType.Xtream)
        {
            await AdditionalSourceClient.ValidateAsync(http, account, UserAgent, ct).ConfigureAwait(false);
            return;
        }
        using var doc = await ApiAsync(account, "", ct);
        if (!doc.RootElement.TryGetProperty("user_info", out var user) || Text(user, "auth") != "1")
            throw new InvalidOperationException("A conta Xtream foi recusada. Verifique o utilizador e a palavra-passe.");
        var status = Text(user, "status");
        if (status.Length > 0 && !status.Equals("Active", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("A conta Xtream não está ativa. Contacte o fornecedor.");
    }

    public async Task<M3uPlaylist> LoadM3uAsync(PlaylistAccount account, CancellationToken ct)
    {
        Uri source;
        string text;
        if (account.Provider == ProviderType.LocalM3U)
        {
            var path = Path.GetFullPath(account.Url);
            if (!File.Exists(path)) throw new InvalidOperationException("O ficheiro M3U local não foi encontrado.");
            source = new Uri(path);
            text = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        }
        else
        {
            source = WebAddress.Require(account.Url);
            text = await DownloadAsync(account.Url, ct, 120_000_000).ConfigureAwait(false);
        }
        return await Task.Run(() => M3uParser.Parse(text, source), ct).ConfigureAwait(false);
    }

    public async Task<SourceDiagnosticReport> DiagnoseAsync(PlaylistAccount account, CancellationToken ct)
    {
        var started = DateTimeOffset.Now;
        var checks = new List<SourceDiagnosticCheck>();
        IReadOnlyList<MediaItem> channels = [];

        var connected = await DiagnosticStepAsync("Ligação e autenticação", async token =>
        {
            await ValidateAsync(account, token).ConfigureAwait(false);
            return "A fonte aceitou a ligação e as credenciais.";
        }, SourceDiagnosticState.Failed, account, ct).ConfigureAwait(false);
        checks.Add(connected);

        if (connected.State == SourceDiagnosticState.Passed)
        {
            var catalog = await DiagnosticStepAsync("Catálogo de canais", async token =>
            {
                channels = await GetCatalogAsync(account, MediaKind.Channel, token).ConfigureAwait(false);
                if (channels.Count == 0) throw new InvalidOperationException("A fonte não devolveu canais reproduzíveis.");
                return $"Foram encontrados {channels.Count} canais.";
            }, SourceDiagnosticState.Failed, account, ct).ConfigureAwait(false);
            checks.Add(catalog);
        }
        else
        {
            checks.Add(new("Catálogo de canais", SourceDiagnosticState.Skipped, "Ignorado porque a ligação falhou."));
        }

        if (connected.State == SourceDiagnosticState.Passed && SupportsGuide(account))
        {
            checks.Add(await DiagnosticStepAsync("Guia EPG", async token =>
            {
                var programmes = await GetFullGuideAsync(account, token).ConfigureAwait(false);
                return programmes.Count == 0
                    ? throw new InvalidOperationException("O guia respondeu, mas não contém programas válidos.")
                    : $"Foram encontrados {programmes.Count} programas.";
            }, SourceDiagnosticState.Warning, account, ct).ConfigureAwait(false));
        }
        else
        {
            checks.Add(new("Guia EPG", SourceDiagnosticState.Skipped,
                connected.State == SourceDiagnosticState.Passed ? "Não existe uma fonte EPG configurada para este fornecedor." : "Ignorado porque a ligação falhou."));
        }

        var sample = channels.FirstOrDefault(item => item.Url.Length > 0 || item.SourceCommand.Length > 0);
        if (sample is not null)
        {
            checks.Add(await DiagnosticStepAsync("Stream de amostra", async token =>
            {
                var resolved = await ResolveStreamAsync(account, sample, token).ConfigureAwait(false);
                var response = await ProbeStreamAsync(resolved, token).ConfigureAwait(false);
                return $"«{sample.Name}» respondeu HTTP {(int)response.StatusCode} ({response.StatusCode})" +
                       (response.ContentType.Length > 0 ? $" · {response.ContentType}." : ".");
            }, SourceDiagnosticState.Warning, account, ct).ConfigureAwait(false));
        }
        else
        {
            checks.Add(new("Stream de amostra", SourceDiagnosticState.Skipped,
                channels.Count == 0 ? "Não existe um canal disponível para testar." : "Os canais não contêm um endereço de reprodução testável."));
        }

        return new(account.Name, account.ProviderName, SourceDiagnosticPolicy.PublicLocation(account),
            started, DateTimeOffset.Now, checks);
    }

    private async Task<(HttpStatusCode StatusCode, string ContentType)> ProbeStreamAsync(MediaItem item, CancellationToken ct)
    {
        var uri = WebAddress.Require(item.Url);
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.TryAddWithoutValidation("User-Agent", item.HttpUserAgent.Length > 0 ? item.HttpUserAgent : UserAgent);
        if (item.HttpReferer.Length > 0) request.Headers.TryAddWithoutValidation("Referer", item.HttpReferer);
        if (item.HttpCookie.Length > 0) request.Headers.TryAddWithoutValidation("Cookie", item.HttpCookie);
        request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 0);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
        if (contentType.Equals("text/html", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("O endereço da stream devolveu uma página HTML em vez de vídeo.");
        return (response.StatusCode, contentType);
    }

    private static bool SupportsGuide(PlaylistAccount account) => account.EpgUrl.Length > 0 ||
        account.Provider is ProviderType.Xtream or ProviderType.M3U or ProviderType.LocalM3U or ProviderType.Tvheadend;

    private static async Task<SourceDiagnosticCheck> DiagnosticStepAsync(string name,
        Func<CancellationToken, Task<string>> action, SourceDiagnosticState failureState,
        PlaylistAccount account, CancellationToken ct)
    {
        var timer = Stopwatch.StartNew();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            var detail = await action(timeout.Token).ConfigureAwait(false);
            return new(name, SourceDiagnosticState.Passed, detail, timer.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (OperationCanceledException)
        {
            return new(name, failureState, "O teste excedeu o limite de 30 segundos.", timer.ElapsedMilliseconds);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return new(name, failureState, SourceDiagnosticPolicy.SafeError(ex, account), timer.ElapsedMilliseconds);
        }
    }

    public async Task<IReadOnlyList<MediaItem>> GetCatalogAsync(PlaylistAccount account, MediaKind kind, CancellationToken ct)
    {
        if (account.Provider is ProviderType.M3U or ProviderType.LocalM3U)
            return (await LoadM3uAsync(account, ct).ConfigureAwait(false)).Items.Where(i => i.Kind == kind).ToArray();
        if (account.Provider != ProviderType.Xtream)
            return await AdditionalSourceClient.GetCatalogAsync(http, account, kind, UserAgent, ct).ConfigureAwait(false);

        var categories = await GetCategoriesAsync(account, kind, ct).ConfigureAwait(false);
        return await GetXtreamCatalogAsync(account, kind, categories, "", ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<CatalogCategory>> GetCategoriesAsync(
        PlaylistAccount account, MediaKind kind, CancellationToken ct)
    {
        if (account.Provider != ProviderType.Xtream)
            throw new InvalidOperationException("O carregamento separado de categorias só está disponível para fontes Xtream.");
        var action = kind switch
        {
            MediaKind.Channel => "get_live_categories",
            MediaKind.Movie => "get_vod_categories",
            _ => "get_series_categories"
        };
        using var document = await ApiAsync(account, action, ct).ConfigureAwait(false);
        return Rows(document.RootElement)
            .Select(row => new CatalogCategory(Text(row, "category_id"), Text(row, "category_name").Trim()))
            .Where(category => category.Id.Length > 0 && category.Name.Length > 0)
            .DistinctBy(category => category.Id)
            .ToArray();
    }

    public Task<IReadOnlyList<MediaItem>> GetCatalogAsync(PlaylistAccount account, MediaKind kind,
        CatalogCategory category, CancellationToken ct)
    {
        if (account.Provider != ProviderType.Xtream)
            throw new InvalidOperationException("O carregamento por categoria só está disponível para fontes Xtream.");
        return GetXtreamCatalogAsync(account, kind, [category], category.Id, ct);
    }

    public Task<IReadOnlyList<MediaItem>> GetCatalogAsync(PlaylistAccount account, MediaKind kind,
        IReadOnlyList<CatalogCategory> categories, CancellationToken ct)
    {
        if (account.Provider != ProviderType.Xtream)
            throw new InvalidOperationException("As categorias previamente carregadas só podem ser usadas com fontes Xtream.");
        return GetXtreamCatalogAsync(account, kind, categories, "", ct);
    }

    private async Task<IReadOnlyList<MediaItem>> GetXtreamCatalogAsync(PlaylistAccount account, MediaKind kind,
        IReadOnlyList<CatalogCategory> categories, string categoryId, CancellationToken ct)
    {
        var itemAction = kind switch { MediaKind.Channel => "get_live_streams", MediaKind.Movie => "get_vod_streams", _ => "get_series" };
        var names = categories.ToDictionary(category => category.Id, category => category.Name);
        using var items = await ApiAsync(account, itemAction, ct,
            categoryId.Length > 0 ? "category_id" : "", categoryId).ConfigureAwait(false);
        var result = new List<MediaItem>();
        foreach (var row in Rows(items.RootElement))
        {
            var id = Text(row, kind == MediaKind.Series ? "series_id" : "stream_id");
            if (string.IsNullOrEmpty(id)) continue;
            var ext = Text(row, "container_extension");
            if (string.IsNullOrEmpty(ext)) ext = kind == MediaKind.Channel ? "m3u8" : "mp4";
            result.Add(new(id, Text(row, "name"), names.GetValueOrDefault(Text(row, "category_id"), "Sem categoria"), kind,
                kind == MediaKind.Series ? "" : StreamUrl(account, kind == MediaKind.Channel ? "live" : "movie", id, ext),
                SafeImage(Text(row, kind == MediaKind.Series ? "cover" : "stream_icon")), Text(row, "epg_channel_id"), kind == MediaKind.Series,
                HasCatchup: kind == MediaKind.Channel && Text(row, "tv_archive") == "1" && PositiveInt(Text(row, "tv_archive_duration")) > 0,
                CatchupDays: kind == MediaKind.Channel ? PositiveInt(Text(row, "tv_archive_duration")) : 0,
                CatchupMode: kind == MediaKind.Channel && Text(row, "tv_archive") == "1" ? "xc" : ""));
        }
        return result;
    }

    public async Task<IReadOnlyList<MediaItem>> GetEpisodesAsync(PlaylistAccount account, MediaItem series, CancellationToken ct)
    {
        return (await GetDetailsAsync(account, series with { HasEpisodes = true }, ct)).EpisodeItems;
    }

    public async Task<MediaItem> ResolveStreamAsync(PlaylistAccount account, MediaItem item, CancellationToken ct)
    {
        if (account.Provider != ProviderType.Stalker || item.SourceCommand.Length == 0) return item;
        var url = await AdditionalSourceClient.ResolveStalkerStreamAsync(http, account, item.SourceCommand, UserAgent, ct)
            .ConfigureAwait(false);
        return item with { Url = url };
    }

    public async Task<MediaDetails> GetDetailsAsync(PlaylistAccount account, MediaItem item, CancellationToken ct)
    {
        if (account.Provider != ProviderType.Xtream) return new(item, Episodes: []);
        var action = item.HasEpisodes ? "get_series_info" : "get_vod_info";
        var key = item.HasEpisodes ? "series_id" : "vod_id";
        using var doc = await ApiAsync(account, action, ct, key, item.Id);
        var root = doc.RootElement;
        var info = root.TryGetProperty("info", out var suppliedInfo) && suppliedInfo.ValueKind == JsonValueKind.Object
            ? suppliedInfo : root;
        var data = root.TryGetProperty("movie_data", out var movieData) && movieData.ValueKind == JsonValueKind.Object
            ? movieData : info;
        var title = TextAny(data, "name", "title");
        var logo = SafeImage(TextAny(info, "cover_big", "movie_image", "cover"));
        var enriched = item with
        {
            Name = title.Length > 0 ? title : item.Name,
            Logo = logo.Length > 0 ? logo : item.Logo
        };
        var backdrop = FirstImage(info, "backdrop_path");
        var trailer = Trailer(TextAny(info, "youtube_trailer", "trailer"));
        var episodes = item.HasEpisodes ? ParseEpisodes(account, enriched, root) : [];
        return new(enriched,
            TextAny(info, "plot", "description"),
            TextAny(info, "releaseDate", "releasedate", "release_date", "year"),
            TextAny(info, "duration", "episode_run_time"),
            TextAny(info, "genre"), TextAny(info, "cast"), TextAny(info, "director"),
            TextAny(info, "rating", "rating_5based"), trailer, backdrop, episodes);
    }

    private static IReadOnlyList<MediaItem> ParseEpisodes(PlaylistAccount account, MediaItem series, JsonElement root)
    {
        if (!root.TryGetProperty("episodes", out var episodes) || episodes.ValueKind != JsonValueKind.Object) return [];
        var result = new List<MediaItem>();
        foreach (var season in episodes.EnumerateObject().OrderBy(s => int.TryParse(s.Name, out var n) ? n : int.MaxValue))
        foreach (var row in Rows(season.Value).OrderBy(r => int.TryParse(Text(r, "episode_num"), out var n) ? n : int.MaxValue))
        {
            var id = Text(row, "id");
            if (id.Length == 0) continue;
            var ext = Text(row, "container_extension");
            var title = Text(row, "title");
            result.Add(new(id, title.Length > 0 ? title : $"Episódio {Text(row, "episode_num")}", $"Temporada {season.Name}", MediaKind.Series,
                StreamUrl(account, "series", id, ext.Length > 0 ? ext : "mp4"), series.Logo));
        }
        return result;
    }

    public async Task<IReadOnlyList<TvProgramme>> GetShortGuideAsync(PlaylistAccount account, MediaItem channel, CancellationToken ct)
    {
        using var doc = await ApiAsync(account, channel.HasCatchup ? "get_simple_data_table" : "get_short_epg", ct, "stream_id", channel.Id);
        if (!doc.RootElement.TryGetProperty("epg_listings", out var listings)) return [];
        var result = new List<TvProgramme>();
        foreach (var row in Rows(listings))
        {
            if (long.TryParse(Text(row, "start_timestamp"), out var start) && long.TryParse(Text(row, "stop_timestamp"), out var end) && end > start)
            {
                try
                {
                    var archiveValue = Text(row, "has_archive");
                    result.Add(new(channel.EpgId, Decode(Text(row, "title")), Decode(Text(row, "description")),
                        DateTimeOffset.FromUnixTimeSeconds(start), DateTimeOffset.FromUnixTimeSeconds(end), Text(row, "start"),
                        archiveValue.Length == 0 ? null : archiveValue == "1"));
                }
                catch (ArgumentOutOfRangeException) { /* Invalid programme supplied by provider. */ }
            }
        }
        return result.OrderBy(p => p.Start).ToArray();
    }

    public async Task<IReadOnlyList<TvProgramme>> GetFullGuideAsync(PlaylistAccount account, CancellationToken ct)
    {
        string url;
        if (account.EpgUrl.Length > 0) url = account.EpgUrl;
        else if (account.Provider is ProviderType.M3U or ProviderType.LocalM3U)
        {
            url = (await LoadM3uAsync(account, ct)).EpgUrl;
            if (url.Length == 0) throw new InvalidOperationException("Esta lista não tem fonte EPG. Adicione um endereço XMLTV nas configurações.");
        }
        else if (account.Provider == ProviderType.Tvheadend)
            url = account.Url.TrimEnd('/') + "/xmltv/channels";
        else
            url = $"{account.Url.TrimEnd('/')}/xmltv.php?username={Uri.EscapeDataString(account.Username)}&password={Uri.EscapeDataString(account.Password)}";
        var xml = await DownloadGuideAsync(account, url, ct);
        return await Task.Run(() => XmlTvParser.Parse(xml), ct);
    }

    public Task<string> DownloadGuideAsync(PlaylistAccount account, string url, CancellationToken ct) =>
        account.Provider == ProviderType.Tvheadend
            ? AdditionalSourceClient.DownloadTvheadendGuideAsync(http, account, url, UserAgent, ct)
            : DownloadAsync(url, ct, 120_000_000);

    private async Task<JsonDocument> ApiAsync(PlaylistAccount account, string action, CancellationToken ct, string key = "", string value = "")
    {
        var url = $"{account.Url.TrimEnd('/')}/player_api.php?username={Uri.EscapeDataString(account.Username)}&password={Uri.EscapeDataString(account.Password)}";
        if (action.Length > 0) url += $"&action={action}";
        if (key.Length > 0) url += $"&{key}={Uri.EscapeDataString(value)}";
        return JsonDocument.Parse(await DownloadAsync(url, ct, 120_000_000).ConfigureAwait(false));
    }

    private static string StreamUrl(PlaylistAccount a, string type, string id, string ext) =>
        $"{a.Url.TrimEnd('/')}/{type}/{Uri.EscapeDataString(a.Username)}/{Uri.EscapeDataString(a.Password)}/{Uri.EscapeDataString(id)}.{Uri.EscapeDataString(ext)}";
    private static string Text(JsonElement row, string key) => row.ValueKind == JsonValueKind.Object && row.TryGetProperty(key, out var v) && v.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined) ? v.ToString() : "";
    private static IEnumerable<JsonElement> Rows(JsonElement element) => element.ValueKind == JsonValueKind.Array ? element.EnumerateArray() : [];
    private static string SafeImage(string url) => Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme is "http" or "https" ? url : "";
    private static string TextAny(JsonElement row, params string[] keys)
    {
        foreach (var key in keys)
        {
            var value = Text(row, key).Trim();
            if (value.Length > 0 && !value.Equals("null", StringComparison.OrdinalIgnoreCase)) return value;
        }
        return "";
    }
    private static string FirstImage(JsonElement row, string key)
    {
        if (!row.TryGetProperty(key, out var value)) return "";
        if (value.ValueKind == JsonValueKind.Array)
            return value.EnumerateArray().Select(item => SafeImage(item.ToString())).FirstOrDefault(url => url.Length > 0) ?? "";
        return SafeImage(value.ToString());
    }
    private static string Trailer(string value)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https") return uri.AbsoluteUri;
        return value.Length is >= 6 and <= 32 && value.All(c => char.IsLetterOrDigit(c) || c is '-' or '_')
            ? $"https://www.youtube.com/watch?v={Uri.EscapeDataString(value)}" : "";
    }
    private static int PositiveInt(string value) => int.TryParse(value, out var number) && number > 0 ? Math.Min(number, 365) : 0;
    private static string Decode(string value)
    {
        try { return WebUtility.HtmlDecode(Encoding.UTF8.GetString(Convert.FromBase64String(value))); }
        catch (FormatException) { return WebUtility.HtmlDecode(value); }
    }
}
