using System.Net;
using System.Text.Json;
using PSiptv.Core;

internal static class PlaylistExpirationTests
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        const long timestamp = 1893456000;
        var expected = DateTimeOffset.FromUnixTimeSeconds(timestamp);
        var dated = Parse("{\"user_info\":{\"auth\":1,\"exp_date\":\"1893456000\"}}");
        check(dated.ExpiresAt == expected && !dated.IsUnlimited, "Expiração Xtream lê timestamps em texto sem depender da cultura");
        check(Parse("{\"user_info\":{\"auth\":\"1\",\"exp_date\":1893456000}}").ExpiresAt == expected,
            "Expiração Xtream aceita timestamps numéricos e auth em texto");
        check(Parse("{\"user_info\":{\"auth\":1,\"exp_date\":null}}").IsUnlimited,
            "Expiração Xtream distingue validade ilimitada explicitamente indicada por null");
        check(Parse("{\"user_info\":{\"auth\":1}}") == PlaylistExpiration.Unknown,
            "Ausência de exp_date não é apresentada como validade ilimitada");
        var invalid = new[] { "0", "-1", "999999999999999999", "\"invalid\"", "\"\"", "true", "{}", "[]" };
        check(invalid.All(value => Parse("{\"user_info\":{\"auth\":1,\"exp_date\":" + value + "}}") == PlaylistExpiration.Unknown),
            "Datas inválidas, negativas, zero e fora do intervalo não originam expirações inventadas");
        var expired = Parse("{\"user_info\":{\"auth\":0,\"status\":\"Expired\",\"exp_date\":1609459200}}");
        check(expired.ExpiresAt == DateTimeOffset.FromUnixTimeSeconds(1609459200) && !expired.IsUnlimited,
            "Contas expiradas continuam a mostrar a data quando o fornecedor devolve auth=0 e Expired");
        var refused = false;
        try { Parse("{\"user_info\":{\"auth\":0,\"exp_date\":null}}"); }
        catch (InvalidDataException) { refused = true; }
        check(refused, "Credenciais recusadas não são interpretadas como uma conta sem expiração");
        var malformed = false;
        try { Parse("{\"user_info\":[]}"); }
        catch (InvalidDataException) { malformed = true; }
        check(malformed, "Resposta sem metadados válidos é recusada sem expor o corpo do servidor");

        var account = new PlaylistAccount { Provider = ProviderType.Xtream, Url = "https://example.test:8443/panel", Username = "user & name", Password = "p+&word" };
        check(PlaylistExpiration.MetadataAccount(account) == account, "Consulta de validade conserva o servidor e as credenciais Xtream configurados");
        var m3u = account with { Provider = ProviderType.M3U, Url = "https://example.test:8443/panel/get.php?username=user+name&password=p%2B%26word&type=m3u_plus", Username = "", Password = "" };
        var resolved = PlaylistExpiration.MetadataAccount(m3u);
        check(resolved is { Provider: ProviderType.Xtream, Url: "https://example.test:8443/panel", Username: "user name", Password: "p+&word" } && m3u.Provider == ProviderType.M3U,
            "Link M3U Xtream consulta a validade no mesmo servidor e caminho sem alterar a lista guardada");
        check(PlaylistExpiration.MetadataAccount(m3u with { Url = m3u.Url + "&username=other" }) is null,
            "Links M3U com credenciais ambíguas não são usados para consultar validade");
        check(PlaylistExpiration.MetadataAccount(m3u with { Url = "https://example.test/list.m3u?username=user&password=pass" }) is null &&
            PlaylistExpiration.MetadataAccount(account with { Provider = ProviderType.Stalker }) is null,
            "Fontes sem API de expiração reconhecida não recebem pedidos Xtream presumidos");

        var requests = 0;
        Uri? requested = null;
        using var http = new HttpClient(new FakeHandler(request =>
        {
            requests++;
            requested = request.RequestUri;
            return new(HttpStatusCode.OK) { Content = new StringContent("{\"user_info\":{\"auth\":1,\"exp_date\":1893456000}}") };
        }));
        var client = new IptvClient(http);
        var result = await client.GetPlaylistExpirationAsync(account, CancellationToken.None);
        check(result.ExpiresAt == expected && requests == 1 && requested?.AbsolutePath == "/panel/player_api.php" &&
            requested.Query.Contains("username=user%20%26%20name") && requested.Query.Contains("password=p%2B%26word") && !requested.Query.Contains("action="),
            "Consulta de expiração pede apenas user_info e codifica corretamente as credenciais");
        await client.GetPlaylistExpirationAsync(account with { Provider = ProviderType.LocalM3U }, CancellationToken.None);
        check(requests == 1, "Consultar validade de ficheiros locais não descarrega catálogos nem faz pedidos de rede");
        using var largeHttp = new HttpClient(new FakeHandler(_ => new(HttpStatusCode.OK) { Content = new StringContent(new string('x', 1_000_001)) }));
        var limited = false;
        try { await new IptvClient(largeHttp).GetPlaylistExpirationAsync(account, CancellationToken.None); }
        catch (InvalidOperationException) { limited = true; }
        check(limited, "Metadados de expiração têm um limite de resposta de 1 MB");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cancelled = false;
        try { await client.GetPlaylistExpirationAsync(account, cancellation.Token); }
        catch (OperationCanceledException) { cancelled = true; }
        check(cancelled && requests == 1, "Sair da página pode cancelar a consulta antes de contactar o fornecedor");
    }

    private static PlaylistExpiration Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return PlaylistExpiration.ParseXtream(document.RootElement);
    }
}
