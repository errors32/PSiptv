using System.Text.Json;
using PSiptv.Core;

namespace PSiptv.Services;

public static class PortugueseRadioService
{
    private const string Prefix = "radio-browser:";
    private static readonly HttpClient client = new() { Timeout = TimeSpan.FromSeconds(20) };
    private static readonly SemaphoreSlim gate = new(1, 1);
    private static IReadOnlyList<MediaItem> cached = [];
    private static DateTimeOffset fetchedAt;

    public static bool IsRadio(MediaItem item) => item.Id.StartsWith(Prefix, StringComparison.Ordinal);

    public static async Task<IReadOnlyList<MediaItem>> GetAsync(CancellationToken cancellationToken = default)
    {
        if (cached.Count > 0 && DateTimeOffset.UtcNow - fetchedAt < TimeSpan.FromHours(12)) return cached;
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (cached.Count > 0 && DateTimeOffset.UtcNow - fetchedAt < TimeSpan.FromHours(12)) return cached;
            Exception? lastError = null;
            foreach (var server in new[] { "de1", "nl1", "at1" })
            {
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get,
                        $"https://{server}.api.radio-browser.info/json/stations/bycountrycodeexact/PT?hidebroken=true&order=name");
                    request.Headers.TryAddWithoutValidation("User-Agent", "PSiptv/1.4 (Portuguese radio directory)");
                    using var response = await client.SendAsync(request, cancellationToken);
                    response.EnsureSuccessStatusCode();
                    await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                    using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
                    if (document.RootElement.ValueKind != JsonValueKind.Array)
                        throw new JsonException("A resposta do diretório de rádios é inválida.");

                    var stations = new List<MediaItem>();
                    foreach (var row in document.RootElement.EnumerateArray())
                    {
                        var id = Text(row, "stationuuid");
                        var name = Text(row, "name").Trim();
                        var url = Text(row, "url_resolved");
                        if (!ValidHttp(url)) url = Text(row, "url");
                        if (id.Length == 0 || name.Length == 0 || !ValidHttp(url)) continue;
                        var logo = Text(row, "favicon");
                        stations.Add(new MediaItem(Prefix + id, name, "Rádios de Portugal", MediaKind.Channel,
                            url, ValidHttp(logo) ? logo : ""));
                    }
                    cached = stations.DistinctBy(item => item.Id).OrderBy(item => item.Name,
                        StringComparer.CurrentCultureIgnoreCase).ToArray();
                    fetchedAt = DateTimeOffset.UtcNow;
                    return cached;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
                {
                    lastError = ex;
                }
            }
            if (cached.Count > 0) return cached;
            throw new InvalidOperationException("Não foi possível carregar as rádios portuguesas.", lastError);
        }
        finally { gate.Release(); }
    }

    private static string Text(JsonElement row, string property) =>
        row.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? "" : "";

    private static bool ValidHttp(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https" && uri.Host.Length > 0;
}
