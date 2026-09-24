using System.Net;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using PSiptv.Core;

var passed = 0;
void Check(bool condition, string label)
{
    if (!condition) throw new Exception($"FAIL: {label}");
    Console.WriteLine($"PASS: {label}"); passed++;
}
void Reject(Action action, string label)
{
    try { action(); } catch (InvalidOperationException) { Check(true, label); return; }
    throw new Exception($"FAIL: {label}");
}

var initialLeader = RemoteLeadership.Resolve("tablet", 0, "phone", 0);
Check(initialLeader.DeviceId == "phone" && initialLeader.Epoch == 0,
    "Eleição remota converge em caso de arranque simultâneo");
var chosenLeader = RemoteLeadership.Resolve("phone", 10, "tablet", 11);
Check(chosenLeader.DeviceId == "tablet" && chosenLeader.Epoch == 11,
    "Escolha mais recente muda o dispositivo ativo");
var staleLeader = RemoteLeadership.Resolve("tablet", 11, "phone", 10);
Check(staleLeader.DeviceId == "tablet" && staleLeader.Epoch == 11,
    "Anúncio antigo não substitui o dispositivo ativo");

Check(ReleaseVersion.TryParse("v1.3.0", out var release130) &&
      ReleaseVersion.TryParse("1.2", out var release120) &&
      release130.CompareTo(release120) > 0,
    "Versões de Release aceitam prefixo v e componentes em falta");
Check(ReleaseVersion.TryParse("1.3.0", out var stable130) &&
      ReleaseVersion.TryParse("1.3.0-rc.2", out var candidate130) &&
      stable130.CompareTo(candidate130) > 0,
    "Versões estáveis têm precedência sobre pré-lançamentos");
Check(ReleaseVersion.TryParse("1.3.0-rc.10+build.7", out var candidate10) &&
      ReleaseVersion.TryParse("1.3.0-rc.2", out var candidate2) &&
      candidate10.CompareTo(candidate2) > 0 &&
      !ReleaseVersion.TryParse("release-final", out _),
    "Comparação de Release respeita identificadores numéricos e rejeita tags inválidas");

var source = new Uri("https://example.test/lists/main.m3u");
var playlist = M3uParser.Parse("""
    #EXTM3U x-tvg-url="../guide.xml"
    #EXTINF:-1 tvg-id="rtp1" tvg-logo="/logo.png" group-title="Portugal, HD" catchup="append" catchup-days="7" catchup-source="?utc={utc}&duration={duration}",RTP 1, HD
    ../live/1.m3u8
    #EXTINF:-1 group-title="Filmes",Cinema
    https://example.test/movie/2.mp4
    #EXTINF:-1 group-title='Séries',Episódio 1
    https://example.test/series/3.mkv
    #EXTINF:-1 group-title="Inválidos",Bad
    ftp://example.test/invalid
    #EXTINF:-1,Sem grupo
    #EXTGRP:Desporto
    https://example.test/live/4
    """, source);
Check(playlist.Items.Count == 4, "M3U ignora esquemas não suportados");
Check(playlist.Items[0].Name == "RTP 1, HD" && playlist.Items[0].Category == "Portugal, HD", "M3U preserva vírgulas em nomes e atributos");
Check(playlist.Items[0].Url == "https://example.test/live/1.m3u8" && playlist.Items[0].Logo == "https://example.test/logo.png", "M3U resolve URLs relativos");
Check(playlist.EpgUrl == "https://example.test/guide.xml" && playlist.Items[0].EpgId == "rtp1", "M3U associa XMLTV e tvg-id");
Check(playlist.Items[0].HasCatchup && playlist.Items[0].CatchupDays == 7 && playlist.Items[0].CatchupMode == "append", "M3U reconhece metadados de catch-up");
Check(playlist.Items[1].Kind == MediaKind.Movie && playlist.Items[2].Kind == MediaKind.Series && playlist.Items[3].Category == "Desporto", "M3U classifica VOD e EXTGRP");
Reject(() => M3uParser.Parse("<html>Login</html>", source), "M3U rejeita resposta HTML");
Reject(() => M3uParser.Parse("#EXTM3U", source), "M3U rejeita lista vazia");
Reject(() => WebAddress.Require("file:///etc/passwd"), "Endereços limitados a HTTP/HTTPS");
var localPlaylist = M3uParser.Parse("#EXTM3U\n#EXTINF:-1,Local\nmedia.ts",
    new Uri("file:///C:/playlists/list.m3u"));
Check(localPlaylist.Items.Single().Url == "file:///C:/playlists/media.ts", "M3U local resolve ficheiros relativos");

var xmlBytes = Encoding.UTF8.GetBytes("<tv><programme channel=\"um\" start=\"20260905130000 +0000\" stop=\"20260905140000 +0000\"><title>GZip</title></programme></tv>");
using var gzipOutput = new MemoryStream();
using (var gzip = new GZipStream(gzipOutput, CompressionMode.Compress, true)) gzip.Write(xmlBytes);
var gzipClient = new IptvClient(new HttpClient(new FakeHandler(_ => new(HttpStatusCode.OK)
    { Content = new ByteArrayContent(gzipOutput.ToArray()) })));
Check((await gzipClient.DownloadAsync("https://guide.test/xmltv.xml.gz", default)).Contains("<title>GZip</title>"),
    "XMLTV comprimido em GZip é descomprimido");

var hdhrClient = new IptvClient(new HttpClient(new FakeHandler(_ => new(HttpStatusCode.OK)
    { Content = new StringContent("[{\"GuideNumber\":\"1\",\"GuideName\":\"Canal HD\",\"URL\":\"http://tuner.test/auto/v1\"}]", Encoding.UTF8, "application/json") })));
var hdhrItems = await hdhrClient.GetCatalogAsync(new PlaylistAccount { Provider = ProviderType.HDHomeRun, Url = "http://tuner.test" }, MediaKind.Channel, default);
Check(hdhrItems.Count == 1 && hdhrItems[0].Name == "Canal HD", "HDHomeRun importa lineup de canais");

var tvhAuthorized = false;
var tvhClient = new IptvClient(new HttpClient(new FakeHandler(request =>
{
    tvhAuthorized = request.Headers.Authorization?.Scheme == "Basic";
    var content = request.RequestUri!.AbsolutePath == "/xmltv/channels"
        ? "<tv><programme channel=\"abc\" start=\"20260905130000 +0000\" stop=\"20260905140000 +0000\"><title>TVH</title></programme></tv>"
        : "#EXTM3U\n#EXTINF:-1 tvg-id=\"abc\" group-title=\"TV\",Canal TVH\n/stream/channelid/abc";
    return new(HttpStatusCode.OK) { Content = new StringContent(content) };
})));
var tvhAccount = new PlaylistAccount { Provider = ProviderType.Tvheadend, Url = "http://tvh.test:9981", Username = "u", Password = "p" };
var tvhItems = await tvhClient.GetCatalogAsync(tvhAccount, MediaKind.Channel, default);
Check(tvhAuthorized && tvhItems.Single().Url == "http://tvh.test:9981/stream/channelid/abc", "Tvheadend usa playlist e autenticação Basic");
var tvhGuide = await tvhClient.GetFullGuideAsync(tvhAccount, default);
Check(tvhAuthorized && tvhGuide.Single().Title == "TVH", "Tvheadend importa XMLTV com autenticação Basic");

var jellyHeaders = false;
var jellyClient = new IptvClient(new HttpClient(new FakeHandler(request =>
{
    jellyHeaders = request.Headers.Contains("X-Emby-Token");
    return new(HttpStatusCode.OK) { Content = new StringContent("{\"Items\":[{\"Id\":\"jf1\",\"Name\":\"Jelly TV\",\"ChannelNumber\":\"7\",\"Genres\":[\"Notícias\"]}]}") };
})));
var jellyItems = await jellyClient.GetCatalogAsync(new PlaylistAccount { Provider = ProviderType.Jellyfin, Url = "https://jelly.test", Password = "token" }, MediaKind.Channel, default);
Check(jellyHeaders && jellyItems.Single().Url.Contains("api_key=token"), "Jellyfin importa canais com token de acesso");

var plexClient = new IptvClient(new HttpClient(new FakeHandler(request =>
{
    var body = request.RequestUri!.AbsolutePath == "/library/sections"
        ? "<MediaContainer><Directory key=\"2\" type=\"movie\" title=\"Cinema\"/></MediaContainer>"
        : "<MediaContainer><Video ratingKey=\"9\" title=\"Filme Plex\"><Media><Part key=\"/library/parts/9/file.mp4\"/></Media></Video></MediaContainer>";
    return new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/xml") };
})));
var plexItems = await plexClient.GetCatalogAsync(new PlaylistAccount { Provider = ProviderType.Plex, Url = "https://plex.test", Password = "plex-token" }, MediaKind.Movie, default);
Check(plexItems.Single().Url.Contains("X-Plex-Token=plex-token"), "Plex importa filmes e preserva o token");

var stalkerClient = new IptvClient(new HttpClient(new FakeHandler(request =>
{
    var query = request.RequestUri!.Query;
    var body = query.Contains("handshake") ? "{\"js\":{\"token\":\"bearer\"}}"
        : query.Contains("create_link") ? "{\"js\":{\"cmd\":\"ffmpeg http://stream.test/live.m3u8\"}}"
        : "{\"js\":{\"data\":[{\"id\":\"3\",\"name\":\"Portal TV\",\"cmd\":\"ffmpeg http://origin.test/cmd\"}]}}";
    return new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
})));
var stalkerAccount = new PlaylistAccount { Provider = ProviderType.Stalker, Url = "http://portal.test", Username = "00:11:22:33:44:55" };
var stalkerItems = await stalkerClient.GetCatalogAsync(stalkerAccount, MediaKind.Channel, default);
var stalkerResolved = await stalkerClient.ResolveStreamAsync(stalkerAccount, stalkerItems.Single(), default);
Check(stalkerResolved.Url == "http://stream.test/live.m3u8", "Stalker resolve o comando apenas ao iniciar a reprodução");

var homeNow = new DateTimeOffset(2026, 9, 12, 20, 0, 0, TimeSpan.Zero);
var homeMovie = new MediaItem("m1", "Filme", "Cinema", MediaKind.Movie, "https://stream.test/movie.mp4");
var homeChannel = new MediaItem("c1", "Canal", "TV", MediaKind.Channel, "https://stream.test/live.m3u8", EpgId: "canal");
var homeHistory = new[]
{
    new WatchEntry(homeMovie, homeNow.AddMinutes(-2), 420),
    new WatchEntry(homeMovie, homeNow.AddMinutes(-5), 120),
    new WatchEntry(homeChannel, homeNow.AddMinutes(-1), 0)
};
Check(SmartHomePolicy.ContinueWatching(homeHistory).Single().PositionSeconds == 420,
    "Início inteligente mantém a retoma mais recente sem duplicados");
Check(SmartHomePolicy.RecentChannels(homeHistory).Single().Item == homeChannel,
    "Início inteligente separa canais recentes");
var starting = SmartHomePolicy.StartingSoon(new[]
{
    new UpcomingProgramme(homeChannel, new TvProgramme("canal", "Mais tarde", "", homeNow.AddHours(2), homeNow.AddHours(3))),
    new UpcomingProgramme(homeChannel, new TvProgramme("canal", "A seguir", "", homeNow.AddMinutes(15), homeNow.AddMinutes(45)))
}, homeNow, TimeSpan.FromMinutes(90));
Check(starting.Single().Programme.Title == "A seguir", "Início inteligente limita programas a começar");

var recurringProgramme = new TvProgramme("canal", "As Águias — Episódio 2", "O segundo episódio",
    homeNow.AddMinutes(20), homeNow.AddMinutes(70));
var recurringLocalStart = recurringProgramme.Start.ToLocalTime();
var recurringRule = new DvrSeriesRule("rule", "account", "profile", "Série", "Águias",
    ChannelId: homeChannel.Id, StartMinutes: recurringLocalStart.Hour * 60 + recurringLocalStart.Minute,
    TimeToleranceMinutes: 35);
Check(DvrSeriesRulePolicy.Matches(recurringRule, homeChannel, recurringProgramme, homeNow, TimeSpan.FromDays(8)),
    "DVR recorrente combina título, canal e tolerância horária");
Check(!DvrSeriesRulePolicy.Matches(recurringRule with { ChannelId = "outro" }, homeChannel,
    recurringProgramme, homeNow, TimeSpan.FromDays(8)), "DVR recorrente respeita o canal escolhido");
Check(DvrSeriesRulePolicy.EpisodeKey(recurringProgramme) == DvrSeriesRulePolicy.EpisodeKey(
    recurringProgramme with { Start = recurringProgramme.Start.AddDays(2), End = recurringProgramme.End.AddDays(2) }),
    "DVR recorrente identifica repetições do mesmo episódio");

var pin = PinProtection.Hash("1234");
Check(PinProtection.Verify("1234", pin) && !PinProtection.Verify("0000", pin), "PIN correto e incorreto");
Check(pin != PinProtection.Hash("1234"), "PIN usa sal aleatório");
Check(!PinProtection.Verify("1234", "invalid") && !PinProtection.IsValid("12a4"), "PIN rejeita formatos inválidos");

Check(BrowserStreamDetection.IsLikelyStream("https://media.test/live/master.m3u8?token=abc") &&
      BrowserStreamDetection.IsLikelyStream("https://media.test/manifest?format=mpd") &&
      BrowserStreamDetection.IsLikelyStream("https://media.test/video.mp4"),
    "Browser reconhece streams HLS, DASH e vídeo direto");
Check(!BrowserStreamDetection.IsLikelyStream("https://site.test/pagina") &&
      !BrowserStreamDetection.IsLikelyStream("blob:https://site.test/id") &&
      BrowserStreamDetection.Priority("https://media.test/master.m3u8") >
      BrowserStreamDetection.Priority("https://media.test/video.mp4"),
    "Browser ignora páginas e prefere manifestos de streaming");
Check(BrowserStreamDetection.IsMediaContentType("application/vnd.apple.mpegurl; charset=utf-8") &&
      BrowserStreamDetection.IsMediaContentType("application/dash+xml") &&
      BrowserStreamDetection.IsMediaContentType("video/mp4") &&
      !BrowserStreamDetection.IsMediaContentType("text/html"),
    "Browser reconhece streams sem extensão através do tipo de conteúdo");

var encryptionKey = AccountDataProtection.CreateKey();
var sensitiveAccount = new PlaylistAccount
{
    Id = "sensitive-list", Provider = ProviderType.Xtream, Name = "Casa",
    Url = "https://provider.test:8443/path?token=secret", Username = "utilizador",
    Password = "palavra-passe", EpgUrl = "https://provider.test/epg?key=secret"
};
var protectedAccount = AccountDataProtection.Protect(sensitiveAccount, encryptionKey);
Check(protectedAccount.Url != sensitiveAccount.Url && protectedAccount.Username != sensitiveAccount.Username &&
    protectedAccount.Password != sensitiveAccount.Password && protectedAccount.EpgUrl != sensitiveAccount.EpgUrl,
    "Dados de ligação são encriptados antes de guardar");
Check(AccountDataProtection.Unprotect(protectedAccount, encryptionKey) == sensitiveAccount,
    "Dados de ligação encriptados recuperam sem alterações");
var tamperedPassword = protectedAccount.Password[..^1] + (protectedAccount.Password[^1] == 'A' ? 'B' : 'A');
Reject(() => AccountDataProtection.Unprotect(protectedAccount with { Password = tamperedPassword }, encryptionKey),
    "Encriptação deteta alterações nos dados guardados");
Reject(() => AccountDataProtection.Unprotect(protectedAccount, AccountDataProtection.CreateKey()),
    "Dados de ligação não abrem com outra chave");
var cacheJson = Encoding.UTF8.GetBytes("[{\"name\":\"Canal\",\"url\":\"https://example.test/live/user/password/1\"}]");
var protectedCache = AccountDataProtection.ProtectData(cacheJson, encryptionKey, "catalog:test:0");
Check(!Encoding.UTF8.GetString(protectedCache).Contains("password", StringComparison.Ordinal) &&
    AccountDataProtection.UnprotectData(protectedCache, encryptionKey, "catalog:test:0").SequenceEqual(cacheJson),
    "Cache persistente protege endereços e recupera o catálogo");
Reject(() => AccountDataProtection.UnprotectData(protectedCache, encryptionKey, "catalog:test:1"),
    "Cache encriptado fica associado à lista e ao tipo de conteúdo");
var githubToken = "github_pat_private-update-token";
var protectedGitHubToken = GitHubTokenBackupProtection.Protect(githubToken);
Check(protectedGitHubToken.StartsWith("enc:v1:", StringComparison.Ordinal) &&
      !protectedGitHubToken.Contains(githubToken, StringComparison.Ordinal) &&
      GitHubTokenBackupProtection.Unprotect(protectedGitHubToken) == githubToken,
    "Cópia de segurança encripta e recupera o token GitHub");
var tamperedGitHubToken = protectedGitHubToken[..10] +
    (protectedGitHubToken[10] == 'A' ? 'B' : 'A') + protectedGitHubToken[11..];
Reject(() => GitHubTokenBackupProtection.Unprotect(tamperedGitHubToken),
    "Token GitHub protegido deteta alterações no ficheiro");
Check(GitHubTokenBackupProtection.Protect("") == "" &&
      GitHubTokenBackupProtection.Unprotect("") == "",
    "Cópia de segurança mantém compatibilidade quando não existe token GitHub");
var largeMovieCatalog = Enumerable.Range(0, 20_000)
    .Select(i => new MediaItem(i.ToString(), $"Filme {i}", "Cinema", MediaKind.Movie,
        $"https://example.test/movie/user/password/{i}.mp4", "https://example.test/poster.jpg"))
    .ToArray();
var rawMovieCatalog = JsonSerializer.SerializeToUtf8Bytes(largeMovieCatalog);
var encodedMovieCatalog = CatalogCacheCodec.Encode(largeMovieCatalog, encryptionKey, "catalog:test:movies");
var restoredMovieCatalog = CatalogCacheCodec.Decode(encodedMovieCatalog, encryptionKey, "catalog:test:movies");
Check(restoredMovieCatalog.Count == largeMovieCatalog.Length && restoredMovieCatalog[12_345] == largeMovieCatalog[12_345],
    "Cache comprimida recupera catálogos grandes de filmes");
Check(encodedMovieCatalog.Length < rawMovieCatalog.Length / 2,
    "Cache comprimida reduz a memória e o espaço necessários para filmes");
var legacyMovieCache = AccountDataProtection.ProtectData(rawMovieCatalog, encryptionKey, "catalog:test:legacy");
Check(CatalogCacheCodec.Decode(legacyMovieCache, encryptionKey, "catalog:test:legacy").Count == largeMovieCatalog.Length,
    "Cache comprimida mantém compatibilidade com catálogos antigos");
Check(StreamRecoveryPolicy.MaxAttempts == 3 &&
    StreamRecoveryPolicy.BufferingTimeout == TimeSpan.FromSeconds(10) &&
    Enumerable.Range(1, 3).Select(StreamRecoveryPolicy.DelayForAttempt).SequenceEqual(
        new[] { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4) }),
    "Reconexão de streams deteta falhas após 10 segundos e usa três tentativas progressivas");
var transferRate = new TransferRateEstimator();
Check(transferRate.Sample(1_000, 0) == 0 &&
      Math.Abs(transferRate.Sample(1_001_000, 1_000) - 8) < 0.001,
    "Velocidade de transferência usa os bytes efetivamente recebidos");
var bufferedRate = transferRate.Sample(1_001_000, 2_000);
var stoppedRate = transferRate.Sample(1_001_000, 4_000);
Check(bufferedRate > 0 && stoppedRate == 0,
    "Velocidade mantém pausas curtas do buffer e deteta transmissão parada");
var updateNow = new DateTimeOffset(2026, 9, 11, 12, 0, 0, TimeSpan.Zero);
Check(!CatalogUpdatePolicy.IsDue(CatalogUpdateSchedule.Manual, null, updateNow, true) &&
      CatalogUpdatePolicy.IsDue(CatalogUpdateSchedule.Startup, updateNow, updateNow, true) &&
      !CatalogUpdatePolicy.IsDue(CatalogUpdateSchedule.Startup, updateNow, updateNow, false),
    "Atualização manual é a predefinição passiva e Ao arrancar só executa no arranque");
Check(CatalogUpdatePolicy.IsDue(CatalogUpdateSchedule.Daily, updateNow.AddDays(-1), updateNow, false) &&
      !CatalogUpdatePolicy.IsDue(CatalogUpdateSchedule.Weekly, updateNow.AddDays(-6), updateNow, false) &&
      CatalogUpdatePolicy.IsDue(CatalogUpdateSchedule.Weekly, updateNow.AddDays(-7), updateNow, false),
    "Atualizações diária e semanal respeitam o último instante concluído");
var previousChannels = new[]
{
    new MediaItem("1", "Um", "TV", MediaKind.Channel, "https://example.test/live/1"),
    new MediaItem("2", "Dois", "TV", MediaKind.Channel, "https://example.test/live/2")
};
var currentChannels = new[]
{
    new MediaItem("2", "Dois renomeado", "TV", MediaKind.Channel, "https://example.test/live/2"),
    new MediaItem("3", "Três", "TV", MediaKind.Channel, "https://example.test/live/3")
};
Check(CatalogUpdatePolicy.CompareChannels(previousChannels, currentChannels, ProviderType.Xtream) == new CatalogChannelChanges(1, 1) &&
      CatalogUpdatePolicy.CompareChannels(previousChannels, currentChannels, ProviderType.M3U) == new CatalogChannelChanges(1, 1),
    "Atualização indica canais adicionados e removidos sem contar mudanças de nome");
var mediaTracks = new[]
{
    new MediaTrackOption(1, "Track 1 - English"),
    new MediaTrackOption(2, "Faixa 2 - Português (Portugal)")
};
Check(TrackPreferencePolicy.Select(mediaTracks, "pt")?.Id == 2 &&
      TrackPreferencePolicy.Select(mediaTracks, "en")?.Id == 1,
    "Preferências encontram faixas de áudio e legendas pelo idioma");
Check(TrackPreferencePolicy.Select(mediaTracks, "en", mediaTracks[1].Name)?.Id == 2 &&
      TrackPreferencePolicy.ResolveLanguage("system", new System.Globalization.CultureInfo("pt-PT")) == "pt",
    "Faixa memorizada tem prioridade e idioma do sistema é resolvido");
var srtCues = ExternalSubtitleParser.Parse("""
    1
    00:00:01,000 --> 00:00:03,500
    <i>Olá &amp; bem-vindo</i>

    2
    00:00:04,000 --> 00:00:06,000
    Segunda legenda
    """);
Check(srtCues.Count == 2 && srtCues[0].Text == "Olá & bem-vindo" &&
      srtCues[1].Start == TimeSpan.FromSeconds(4),
    "Legendas externas interpretam SRT, HTML e entidades");
var vttCues = ExternalSubtitleParser.Parse("""
    WEBVTT

    intro
    00:01.250 --> 00:03.000 align:middle
    Texto WebVTT
    """);
Check(vttCues.Single().Start == TimeSpan.FromMilliseconds(1250) &&
      ExternalSubtitleParser.SupportsFileName("legendas.VTT") &&
      !ExternalSubtitleParser.SupportsFileName("legendas.exe"),
    "Legendas externas interpretam WebVTT e limitam formatos");
Check(ExternalSubtitleParser.TextAt(srtCues, TimeSpan.FromSeconds(2)) == "Olá & bem-vindo" &&
      ExternalSubtitleParser.TextAt(srtCues, TimeSpan.FromSeconds(3.75)) == "",
    "Legendas externas selecionam a fala pela posição do vídeo");
var futureProgramme = new TvProgramme("rtp1", "Telejornal", "", updateNow.AddHours(2), updateNow.AddHours(3));
var reminder = new ProgrammeReminder(
    ProgrammeReminderPolicy.IdFor("casa", "perfil", "rtp1", futureProgramme.Title, futureProgramme.Start),
    "casa", "perfil", "rtp1", "RTP 1", futureProgramme.Title, futureProgramme.Start, futureProgramme.End);
Check(ProgrammeReminderPolicy.CanCreate(futureProgramme, updateNow) &&
      ProgrammeReminderPolicy.NotificationTime(reminder, 10, updateNow) == futureProgramme.Start.AddMinutes(-10) &&
      !ProgrammeReminderPolicy.IsExpired(reminder, updateNow),
    "Lembretes aceitam programas futuros e respeitam a antecedência");
Check(ProgrammeReminderPolicy.IdFor("casa", "perfil", "rtp1", futureProgramme.Title, futureProgramme.Start) == reminder.Id &&
      ProgrammeReminderPolicy.NotificationTime(reminder, 60, futureProgramme.Start) > futureProgramme.Start &&
      ProgrammeReminderPolicy.IsDue(reminder, 10, futureProgramme.Start.AddMinutes(-5)),
    "Lembretes têm identidade estável e nunca são agendados no passado");
Check(TimeshiftPolicy.ClampOffset(20, 30, 30) == 50 &&
      TimeshiftPolicy.ClampOffset(20, -30, 30) == 0 &&
      TimeshiftPolicy.ClampOffset(1790, 30, 30) == 1800,
    "Timeshift limita recuo, avanço e duração da janela");
Check(TimeshiftPolicy.FormatOffset(65) == "−01:05" && TimeshiftPolicy.FormatOffset(3661) == "−1:01:01",
    "Timeshift apresenta corretamente a distância ao direto");
var dvr = new DvrRecording(
    DvrRecordingPolicy.IdFor("casa", "perfil", "rtp1", futureProgramme.Title, futureProgramme.Start),
    "casa", "perfil", new MediaItem("rtp1", "RTP 1", "TV", MediaKind.Channel, "https://example.test/live/1.ts"),
    ProgrammeTitle: futureProgramme.Title,
    Start: futureProgramme.Start, End: futureProgramme.End);
Check(DvrRecordingPolicy.CanSchedule(dvr.Start, dvr.End, updateNow) &&
      !DvrRecordingPolicy.IsDue(dvr, updateNow) && DvrRecordingPolicy.IsDue(dvr, dvr.Start),
    "DVR agenda programas válidos e inicia-os à hora definida");
Check(DvrRecordingPolicy.Remaining(dvr, dvr.Start.AddMinutes(15)) == TimeSpan.FromMinutes(45) &&
      DvrRecordingPolicy.IdFor("casa", "perfil", "rtp1", futureProgramme.Title, futureProgramme.Start) == dvr.Id,
    "DVR calcula a duração restante e mantém identidade estável");
Check(!DvrRecordingPolicy.SafeFileStem("Jornal: edição", futureProgramme.Start).Any(Path.GetInvalidFileNameChars().Contains),
    "DVR cria nomes de ficheiro seguros");
var vodEpisode = new MediaItem("episode-1", "Piloto: início", "Drama", MediaKind.Series,
    "https://example.test/series/episode-1.mkv?token=private", ParentSeriesId: "series-1");
Check(OfflineDownloadPolicy.CanDownload(vodEpisode) &&
      !OfflineDownloadPolicy.CanDownload(vodEpisode with { Kind = MediaKind.Channel }) &&
      !OfflineDownloadPolicy.CanDownload(vodEpisode with { Url = "file:///video.mkv" }),
    "Downloads offline aceitam apenas VOD remoto");
Check(OfflineDownloadPolicy.IdFor("casa", "perfil", vodEpisode) ==
      OfflineDownloadPolicy.IdFor("casa", "perfil", vodEpisode with { Url = "https://renovado.test/video.mkv" }) &&
      OfflineDownloadPolicy.IdFor("casa", "outro", vodEpisode) != OfflineDownloadPolicy.IdFor("casa", "perfil", vodEpisode),
    "Downloads offline mantêm identidade estável e isolamento por perfil");
Check(OfflineDownloadPolicy.ExtensionFor(vodEpisode) == ".mkv" &&
      !OfflineDownloadPolicy.SafeFileStem(vodEpisode.Name).Any(Path.GetInvalidFileNameChars().Contains) &&
      new OfflineDownload("id", "a", "p", vodEpisode, BytesDownloaded: 150, TotalBytes: 100).Progress == 1,
    "Downloads offline escolhem extensão segura e limitam o progresso");
Check(ChromecastPolicy.ContentType(vodEpisode) == "video/x-matroska" &&
      ChromecastPolicy.ContentType(vodEpisode with { Url = "https://example.test/live/master.m3u8?token=x" }) == "application/x-mpegURL" &&
      ChromecastPolicy.ContentType(vodEpisode with { Url = "https://example.test/manifest.mpd" }) == "application/dash+xml",
    "Chromecast anuncia corretamente os formatos de transmissão");
Check(ChromecastPolicy.ClampVolume(-0.5) == 0 && ChromecastPolicy.ClampVolume(1.5) == 1,
    "Chromecast limita o volume ao intervalo do recetor");
Check(ChromecastPolicy.SeekTarget(20_000, -30_000, 100_000) == 0 &&
      ChromecastPolicy.SeekTarget(90_000, 30_000, 100_000) == 100_000,
    "Chromecast limita os saltos à duração disponível");
Check(ProfileStorageScope.ForAccount("casa", ProfileStorageScope.DefaultProfileId) == "casa" &&
    ProfileStorageScope.ForAccount("casa", "perfil-2") != ProfileStorageScope.ForAccount("casa", "perfil-3"),
    "Perfis isolam dados pessoais e preservam os dados existentes no perfil Principal");
var guide = XmlTvParser.Parse("""
    <?xml version="1.0"?>
    <!DOCTYPE tv SYSTEM "https://example.test/not-fetched.dtd">
    <tv>
      <programme channel="rtp1" start="20260905130000 +0100" stop="20260905140000 +0100"><title>Jornal &amp; notícias</title><desc>Informação</desc></programme>
      <programme channel="rtp1" start="20260905140000 +0100" stop="20260905150000 +0100"><title>Seguinte</title></programme>
      <programme channel="rtp1" start="bad" stop="bad"><title>Inválido</title></programme>
    </tv>
    """);
Check(guide.Count == 2 && guide[1].Title == "Seguinte", "XMLTV não salta programas consecutivos");
Check(guide[0].Start.UtcDateTime.Hour == 12 && guide[0].Title == "Jornal & notícias", "XMLTV interpreta fuso horário e entidades");
Reject(() => XmlTvParser.Parse("<html/>"), "XMLTV rejeita documento incorreto");

var requests = new List<string>();
var handler = new FakeHandler(request =>
{
    requests.Add(request.RequestUri!.AbsoluteUri);
    var query = request.RequestUri.Query;
    var data = query.Contains("get_live_categories") ? """[{"category_id":7,"category_name":"Portugal"}]"""
        : query.Contains("get_live_streams") ? """[{"stream_id":42,"name":"Canal","category_id":"7","epg_channel_id":"rtp1","tv_archive":1,"tv_archive_duration":"7"}]"""
        : query.Contains("get_vod_categories") ? """[{"category_id":"8","category_name":"Cinema"}]"""
        : query.Contains("get_vod_info") ? """{"info":{"plot":"Uma história","genre":"Drama","cast":"Pessoa A, Pessoa B","director":"Realizador","rating":"8.2","duration":"01:45","releaseDate":"2026","youtube_trailer":"abc123","backdrop_path":["https://images.test/backdrop.jpg"]},"movie_data":{"name":"Filme detalhado"}}"""
        : query.Contains("get_vod_streams") ? """[{"stream_id":"43","name":"Filme","category_id":8,"container_extension":"mkv","stream_icon":"https://images.test/movie.jpg"}]"""
        : query.Contains("get_series_categories") ? """[{"category_id":9,"category_name":"Drama"}]"""
        : query.Contains("get_series_info") ? """{"info":{"name":"Série detalhada","plot":"Sinopse da série","genre":"Drama","rating":"9.0","cover":"https://images.test/series-detail.jpg"},"episodes":{"2":[{"id":"55","episode_num":1,"title":"Episódio","container_extension":"mkv"}],"1":[{"id":54,"episode_num":"1","title":"Piloto","container_extension":"mp4"}]}}"""
        : query.Contains("action=get_series") ? """[{"series_id":44,"name":"Série","category_id":"9","cover":"https://images.test/series.jpg"}]"""
        : query.Contains("get_short_epg") || query.Contains("get_simple_data_table") ? """{"epg_listings":[{"title":"Sm9ybmFs","description":"Tm90w61jaWFz","start_timestamp":"1788609600","stop_timestamp":"1788613200"}]}"""
        : """{"user_info":{"auth":1,"status":"Active"}}""";
    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(data, Encoding.UTF8, "application/json") };
});
var client = new IptvClient(new HttpClient(handler));
var account = new PlaylistAccount { Provider = ProviderType.Xtream, Url = "https://example.test", Username = "a+b", Password = "p&/?" };
await client.ValidateAsync(account, default);
var channels = await client.GetCatalogAsync(account, MediaKind.Channel, default);
Check(channels.Count == 1 && channels[0].Category == "Portugal", "Xtream aceita IDs numéricos e texto");
var liveCategories = await client.GetCategoriesAsync(account, MediaKind.Channel, default);
var categoryChannels = await client.GetCatalogAsync(account, MediaKind.Channel, liveCategories.Single(), default);
Check(categoryChannels.Count == 1 && categoryChannels[0].Category == "Portugal" &&
      requests.Any(request => request.Contains("action=get_live_streams") && request.Contains("category_id=7")),
    "Xtream carrega canais apenas da categoria escolhida");
Check(channels[0].HasCatchup && channels[0].CatchupDays == 7, "Xtream reconhece canais com arquivo TV");
Check(channels[0].Url == "https://example.test/live/a%2Bb/p%26%2F%3F/42.m3u8", "Xtream codifica credenciais no URL de reprodução");
var movies = await client.GetCatalogAsync(account, MediaKind.Movie, default);
Check(movies.Count == 1 && movies[0].Name == "Filme" && movies[0].Category == "Cinema" && movies[0].Url.EndsWith("/43.mkv"),
    "Xtream carrega o catálogo de filmes e preserva a extensão");
var movieDetails = await client.GetDetailsAsync(account, movies[0], default);
Check(movieDetails.Item.Name == "Filme detalhado" && movieDetails.Plot == "Uma história" && movieDetails.Genre == "Drama" &&
      movieDetails.TrailerUrl == "https://www.youtube.com/watch?v=abc123" && movieDetails.Backdrop == "https://images.test/backdrop.jpg",
    "Xtream carrega detalhes, imagem e trailer do filme");
var series = await client.GetCatalogAsync(account, MediaKind.Series, default);
Check(series.Count == 1 && series[0].Name == "Série" && series[0].Category == "Drama" && series[0].HasEpisodes && series[0].Url.Length == 0,
    "Xtream carrega o catálogo de séries para abrir os episódios");
Check(requests.All(r => r.Contains("username=a%2Bb&password=p%26%2F%3F")), "Xtream codifica credenciais nos pedidos API");
var episodes = await client.GetEpisodesAsync(account, new("9", "Série", "Drama", MediaKind.Series), default);
Check(episodes.Count == 2 && episodes[0].Name == "Piloto" && episodes[1].Url.EndsWith("55.mkv"), "Xtream ordena temporadas e preserva extensões");
var seriesDetails = await client.GetDetailsAsync(account, series[0], default);
Check(seriesDetails.Item.Name == "Série detalhada" && seriesDetails.Plot == "Sinopse da série" && seriesDetails.EpisodeItems.Count == 2,
    "Xtream carrega detalhes e episódios da série num único pedido");
var shortGuide = await client.GetShortGuideAsync(account, channels[0], default);
Check(shortGuide.Count == 1 && shortGuide[0].Title == "Jornal" && shortGuide[0].Description == "Notícias", "Xtream descodifica EPG Base64");
Check(requests.Any(r => r.Contains("action=get_simple_data_table")), "Xtream pede o guia completo para canais com catch-up");
var fullGuideUrl = "";
var fullGuideClient = new IptvClient(new HttpClient(new FakeHandler(request =>
{
    fullGuideUrl = request.RequestUri!.AbsoluteUri;
    return new(HttpStatusCode.OK) { Content = new StringContent("""
        <tv><programme channel="rtp1" start="20260905130000 +0100" stop="20260905140000 +0100"><title>Programa global</title></programme></tv>
        """, Encoding.UTF8, "application/xml") };
})));
var fullGuide = await fullGuideClient.GetFullGuideAsync(account, default);
Check(fullGuide.Single().Title == "Programa global" && fullGuideUrl.Contains("/xmltv.php?username=a%2Bb&password=p%26%2F%3F"),
    "Pesquisa global carrega XMLTV Xtream com credenciais codificadas");
var diagnosticClient = new IptvClient(new HttpClient(new FakeHandler(request =>
{
    var path = request.RequestUri!.AbsolutePath;
    var query = request.RequestUri.Query;
    if (path.Contains("xmltv.php"))
        return new(HttpStatusCode.OK) { Content = new StringContent("<tv><programme channel=\"rtp1\" start=\"20260905130000 +0100\" stop=\"20260905140000 +0100\"><title>Jornal</title></programme></tv>", Encoding.UTF8, "application/xml") };
    if (path.Contains("/live/"))
        return new(HttpStatusCode.PartialContent) { Content = new ByteArrayContent([0x47]) { Headers = { ContentType = new("video/mp2t") } } };
    var data = query.Contains("get_live_categories") ? "[{\"category_id\":\"7\",\"category_name\":\"Portugal\"}]"
        : query.Contains("get_live_streams") ? "[{\"stream_id\":\"42\",\"name\":\"RTP 1\",\"category_id\":\"7\",\"epg_channel_id\":\"rtp1\"}]"
        : "{\"user_info\":{\"auth\":1,\"status\":\"Active\"}}";
    return new(HttpStatusCode.OK) { Content = new StringContent(data, Encoding.UTF8, "application/json") };
}))) { UserAgent = "Diagnostic/1.0" };
var diagnosticReport = await diagnosticClient.DiagnoseAsync(account, default);
Check(diagnosticReport.State == SourceDiagnosticState.Passed && diagnosticReport.Checks.Count == 4 &&
      diagnosticReport.Checks.All(item => item.State == SourceDiagnosticState.Passed),
    "Diagnóstico valida ligação, catálogo, EPG e stream");
Check(diagnosticReport.Location == "https://example.test" &&
      !diagnosticReport.Checks.Any(item => item.Detail.Contains(account.Username) || item.Detail.Contains(account.Password)),
    "Diagnóstico não expõe credenciais nem parâmetros privados");
var warningDiagnostic = new IptvClient(new HttpClient(new FakeHandler(request =>
    request.RequestUri!.AbsolutePath.Contains("/live/")
        ? new(HttpStatusCode.OK) { Content = new StringContent("<html>blocked</html>", Encoding.UTF8, "text/html") }
        : request.RequestUri.Query.Contains("get_live_categories")
            ? new(HttpStatusCode.OK) { Content = new StringContent("[]") }
            : request.RequestUri.Query.Contains("get_live_streams")
                ? new(HttpStatusCode.OK) { Content = new StringContent("[{\"stream_id\":1,\"name\":\"Canal\"}]") }
                : request.RequestUri.AbsolutePath.Contains("xmltv.php")
                    ? new(HttpStatusCode.OK) { Content = new StringContent("<tv></tv>") }
                    : new(HttpStatusCode.OK) { Content = new StringContent("{\"user_info\":{\"auth\":1,\"status\":\"Active\"}}") })));
var warningReport = await warningDiagnostic.DiagnoseAsync(account, default);
Check(warningReport.State == SourceDiagnosticState.Warning && warningReport.Checks.Count(item => item.State == SourceDiagnosticState.Warning) == 2,
    "Diagnóstico distingue avisos opcionais de falhas da fonte");
var archiveProgramme = new TvProgramme("rtp1", "Jornal", "", new DateTimeOffset(2026, 9, 5, 12, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 9, 5, 13, 0, 0, TimeSpan.Zero), "2026-09-05 13:00:00");
var archive = CatchupStream.Create(account, channels[0], archiveProgramme, new DateTimeOffset(2026, 9, 6, 12, 0, 0, TimeSpan.Zero));
Check(archive is { IsCatchup: true } && archive.Url == "https://example.test/timeshift/a%2Bb/p%26%2F%3F/60/2026-09-05:13-00/42.ts", "Xtream cria URL de catch-up com início e duração");
var m3uArchive = CatchupStream.Create(new PlaylistAccount { Provider = ProviderType.M3U }, playlist.Items[0], archiveProgramme, new DateTimeOffset(2026, 9, 6, 12, 0, 0, TimeSpan.Zero));
Check(m3uArchive is not null && m3uArchive.Url.Contains("?utc=1788609600&duration=3600"), "M3U expande modelo de URL de catch-up");
var denied = new IptvClient(new HttpClient(new FakeHandler(_ => new(HttpStatusCode.OK) { Content = new StringContent("{\"user_info\":{\"auth\":0}}") })));
try { await denied.ValidateAsync(account, default); throw new Exception("FAIL: Conta inválida aceite"); }
catch (InvalidOperationException) { Check(true, "Xtream recusa autenticação inválida"); }
using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
try { await client.ValidateAsync(account, cancelled.Token); throw new Exception("FAIL: Cancelamento ignorado"); }
catch (OperationCanceledException) { Check(true, "Pedidos respeitam cancelamento"); }
var externalFile = ExternalPlaylist.Create(new("42", "Canal\n#EXTINF:extra\rTeste", "", MediaKind.Channel, "https://example.test/live?a=1&b=2"));
Check(externalFile.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length == 3, "Leitor externo impede injeção de entradas por nomes");
Check(externalFile.Contains("https://example.test/live?a=1&b=2"), "Leitor externo preserva parâmetros da transmissão");
Reject(() => ExternalPlaylist.Create(new("x", "Inválido", "", MediaKind.Channel, "file:///secret")), "Leitor externo rejeita endereços não HTTP");
var favoriteStorage = new Dictionary<string, string>();
FavoriteStore NewFavorites() => new(
    key => Task.FromResult(favoriteStorage.GetValueOrDefault(key)),
    (key, value) => { favoriteStorage[key] = value; return Task.CompletedTask; },
    key => { favoriteStorage.Remove(key); return Task.CompletedTask; });
var favorites = NewFavorites();
var favoriteChannel = channels[0];
await favorites.SetAsync(account, favoriteChannel, true);
Check((await NewFavorites().LoadAsync(account.Id)).Single().Item == favoriteChannel, "Favoritos persistem entre instâncias");
await favorites.SetAsync(account, favoriteChannel with { Name = "Nome atualizado" }, true);
Check((await favorites.LoadAsync(account.Id)).Count == 1 && (await favorites.LoadAsync(account.Id))[0].Item.Name == "Nome atualizado", "Favoritos atualizam sem duplicar");
var secondAccount = account with { Id = "outra-lista" };
Check((await favorites.LoadAsync(secondAccount.Id)).Count == 0, "Favoritos isolados por lista");
await favorites.SetAsync(secondAccount, favoriteChannel, true);
await favorites.SetAsync(account, favoriteChannel, false);
Check((await favorites.LoadAsync(account.Id)).Count == 0 && (await favorites.LoadAsync(secondAccount.Id)).Count == 1, "Remover favorito não altera outra lista");
var m3uAccount = account with { Provider = ProviderType.M3U };
Check(FavoriteStore.ItemKey(m3uAccount, favoriteChannel) == FavoriteStore.ItemKey(m3uAccount, favoriteChannel with { Id = "999", Name = "Renomeado" }), "Favoritos M3U sobrevivem a reordenação");
Check(FavoriteStore.ItemKey(account, favoriteChannel) != FavoriteStore.ItemKey(account, favoriteChannel with { Kind = MediaKind.Movie }), "Favoritos distinguem IDs de canais e filmes");
Check(FavoriteStore.ItemKey(account, favoriteChannel with { Kind = MediaKind.Series, HasEpisodes = true }) != FavoriteStore.ItemKey(account, favoriteChannel with { Kind = MediaKind.Series }), "Favoritos distinguem séries e episódios");
await Task.WhenAll(Enumerable.Range(1, 12).Select(i => favorites.SetAsync(account, favoriteChannel with { Id = i.ToString() }, true)));
Check((await favorites.LoadAsync(account.Id)).Count == 12, "Gravações concorrentes não perdem favoritos");
await favorites.DeleteAsync(account.Id);
Check((await favorites.LoadAsync(account.Id)).Count == 0 && (await favorites.LoadAsync(secondAccount.Id)).Count == 1, "Eliminar lista limpa apenas os seus favoritos");
var options = new CatalogPreferences();
var sample = new[] { new MediaItem("1", "Zulu", "Sports", MediaKind.Channel, "https://example.test/one"), new MediaItem("2", "Alpha", "News", MediaKind.Channel, "https://example.test/two"), new MediaItem("3", "Movie", "Drama", MediaKind.Movie, "https://example.test/movie") };
options.Sorting[MediaKind.Channel] = CatalogSort.NameAscending;
Check(options.Filter(sample, MediaKind.Channel, null, "")[0].Name == "Alpha", "Ordenação por nome respeita o tipo de conteúdo");
options.Hidden.Add(CatalogPreferences.CategoryKey(MediaKind.Channel, "News"));
Check(options.Filter(sample, MediaKind.Channel, null, "").Count == 1, "Categorias ocultas não aparecem na pesquisa global");
options.Custom.Add(new() { Name = "My sports", Kind = MediaKind.Channel, Items = [CatalogPreferences.ItemKey(sample[0])] });
Check(options.Filter(sample, MediaKind.Channel, "My sports", "z").Single() == sample[0], "Categoria personalizada filtra membros e pesquisa");
options.ParentalEnabled = true; options.Locked.Add(CatalogPreferences.CategoryKey(MediaKind.Channel, "My sports"));
Check(options.IsLocked(sample[0]), "Bloqueio de categoria personalizada protege o conteúdo fora dela");
options.Locked.Add(CatalogPreferences.CategoryKey(MediaKind.Movie, "Drama"));
Check(options.IsLocked(sample[2]), "Bloqueio protege categoria original");
options.ParentalEnabled = false;
Check(!options.IsLocked(sample[0]), "Desativar controlo parental permite reprodução");
options.Order.Add(CatalogPreferences.CategoryKey(MediaKind.Channel, "My sports"));
Check(options.Categories(sample, MediaKind.Channel).First() == "My sports", "Ordem personalizada precede ordem alfabética");
options.Hidden.Add(CatalogPreferences.CategoryKey(MediaKind.Channel, "My sports"));
Check(!options.Categories(sample, MediaKind.Channel).Contains("My sports"), "Ocultar categoria personalizada remove-a do menu");
var ts = StreamPreferences.ApplyFormat(account, channels[0], "ts");
Check(ts.Url.EndsWith(".ts") && ts.Name == channels[0].Name, "Formato MPEGTS modifica apenas a extensão Xtream");
Check(StreamPreferences.ApplyFormat(m3uAccount, channels[0], "ts") == channels[0], "Formato preserva endereços M3U");
Check(StreamPreferences.ApplyFormat(account, sample[2], "ts") == sample[2], "Formato preserva filmes");
var uaReceived = "";
var uaClient = new IptvClient(new HttpClient(new FakeHandler(r => { uaReceived = r.Headers.GetValues("User-Agent").Single(); return new(HttpStatusCode.OK) { Content = new StringContent("ok") }; }))) { UserAgent = "Custom/2.0" };
await uaClient.DownloadAsync("https://example.test", default);
Check(uaReceived == "Custom/2.0", "Agente personalizado é enviado ao fornecedor");
options.ParentalEnabled = true;
options.Custom.Add(new() { Name = "Protected shows", Kind = MediaKind.Series, Items = ["2:show1"] });
options.Locked.Add(CatalogPreferences.CategoryKey(MediaKind.Series, "Protected shows"));
Check(options.IsLocked(new("episode1", "Episode", "Drama", MediaKind.Series, "https://example.test/ep", ParentSeriesId: "show1")), "Episódios herdam bloqueio de séries em categorias personalizadas");
Console.WriteLine($"\n{passed} verificações concluídas.");

sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(respond(request));
    }
}
