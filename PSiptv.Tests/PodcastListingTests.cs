using PSiptv.Core;

internal static class PodcastListingTests
{
    public static void Run(Action<bool, string> check)
    {
        var day = new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
        MediaItem Dated(string id, int days, string parent = "podcast:feed") =>
            new("podcast:" + id, id, "", MediaKind.Podcast, ParentSeriesId: parent, PublishedAt: day.AddDays(days));
        var current = Dated("current", 0);
        var tomorrow = Dated("tomorrow", 1);
        var later = Dated("later", 3);
        check(PodcastPolicy.NextEpisode(current, [later, tomorrow, current, Dated("old", -1)]) == tomorrow,
            "Próximo podcast avança por data mesmo com a lista do mais recente para o mais antigo");
        check(PodcastPolicy.NextEpisode(current, [Dated("other", 1, "podcast:other"), later, current]) == later,
            "Próximo podcast ignora outros programas e usa a data seguinte disponível quando há dias sem episódios");
        check(PodcastPolicy.NextEpisode(current, [current, Dated("same-day", 0)]) is null &&
            PodcastPolicy.NextEpisode(current with { PublishedAt = null }, [tomorrow]) is null,
            "Próximo podcast não repete o mesmo dia nem adivinha a sequência sem data");
        var older = Dated("older", -1);
        var sameDay = Dated("same-day-manual", 0) with { PublishedAt = day.AddHours(1) };
        MediaItem[] navigationQueue = [later, tomorrow, sameDay, current, older, older,
            Dated("other-feed", 0, "podcast:other"), current with { Id = "podcast:subscription", HasEpisodes = true }];
        check(PodcastPolicy.AdjacentEpisode(current, navigationQueue, 1) == sameDay &&
            PodcastPolicy.AdjacentEpisode(sameDay, navigationQueue, 1) == tomorrow,
            "Seguinte manual avança por ordem cronológica e inclui episódios publicados no mesmo dia");
        check(PodcastPolicy.AdjacentEpisode(current, navigationQueue, -1) == older &&
            PodcastPolicy.AdjacentEpisode(tomorrow, navigationQueue, -1) == sameDay,
            "Anterior manual recua por data e ignora duplicados, subscrições e outros podcasts");
        check(PodcastPolicy.AdjacentEpisode(older, navigationQueue, -1) is null &&
            PodcastPolicy.AdjacentEpisode(later, navigationQueue, 1) is null,
            "Navegação manual termina no primeiro e último episódios sem dar a volta à fila");
        check(PodcastPolicy.AdjacentEpisode(Dated("missing", 0), navigationQueue, 1) is null &&
            PodcastPolicy.AdjacentEpisode(current with { ParentSeriesId = "" }, navigationQueue, 1) is null &&
            PodcastPolicy.AdjacentEpisode(current, navigationQueue, 0) is null,
            "Fila sem episódio atual, podcast identificado ou direção válida não muda de episódio");
        var undatedFirst = older with { PublishedAt = null };
        var undatedSecond = current with { PublishedAt = null };
        check(PodcastPolicy.AdjacentEpisode(undatedFirst, [undatedFirst, undatedSecond], 1) == undatedSecond &&
            PodcastPolicy.AdjacentEpisode(undatedSecond, [undatedFirst, undatedSecond], -1) == undatedFirst,
            "Episódios sem data preservam a ordem da fila para navegação manual");
        check(PodcastPolicy.RestartBeforePrevious(3.1, true) &&
            !PodcastPolicy.RestartBeforePrevious(3, true) && !PodcastPolicy.RestartBeforePrevious(0, true),
            "Anterior reinicia após três segundos e passa ao episódio anterior quando está no início");
        check(PodcastPolicy.RestartBeforePrevious(0, false) && PodcastPolicy.RestartBeforePrevious(60, false),
            "Sem episódio anterior, o comando anterior volta sempre ao início do atual");
        check(AudioPlaybackPolicy.IsInFinalMinute(540, 600) && AudioPlaybackPolicy.IsInFinalMinute(599, 600) &&
            !AudioPlaybackPolicy.IsInFinalMinute(539, 600) && !AudioPlaybackPolicy.IsInFinalMinute(0, 30) &&
            !AudioPlaybackPolicy.IsInFinalMinute(20, 0) && !AudioPlaybackPolicy.IsInFinalMinute(double.NaN, 600),
            "Marca como visto a partir do último minuto com duração conhecida e reprodução iniciada");
        MediaItem Feed(string id) => new("podcast:" + id, id, "", MediaKind.Podcast, HasEpisodes: true);
        MediaItem Episode(string id) => new("podcast:" + id, id, "", MediaKind.Podcast);
        var small = Feed("small"); var large = Feed("large"); var unknown = Feed("unknown"); var zero = Feed("zero");
        var first = Episode("1"); var second = Episode("2"); var third = Episode("3");
        var episodes = new Dictionary<string, IReadOnlyList<MediaItem>>
        {
            [small.Id] = [first], [large.Id] = [first, second, third, third], [zero.Id] = []
        };
        var heard = new Dictionary<string, PodcastHeardEntry>();
        check(PodcastFeed.OrderByUnplayed([unknown, small, zero, large], episodes, heard).SequenceEqual([large, small, zero, unknown]),
            "Podcasts com mais episódios por ouvir aparecem primeiro; contagens desconhecidas ficam no fim");
        heard[second.Id] = new(DateTimeOffset.UtcNow, large.Id);
        heard[third.Id] = new(DateTimeOffset.UtcNow, large.Id);
        check(PodcastFeed.OrderByUnplayed([small, large], episodes, heard).SequenceEqual([small, large]),
            "Ordenação desconta episódios ouvidos, ignora duplicados e mantém a ordem nos empates");
        heard[first.Id] = new(DateTimeOffset.UtcNow, small.Id);
        check(PodcastFeed.OrderByUnplayed([large, small], episodes, heard).SequenceEqual([large, small]),
            "Podcasts totalmente ouvidos mantêm a ordem nos empates");
        episodes[large.Id] = [first, first];
        check(PodcastFeed.OrderByUnplayed([small, large], episodes, new Dictionary<string, PodcastHeardEntry>()).SequenceEqual([small, large]),
            "Entradas duplicadas do feed não aumentam a contagem por ouvir");
        episodes[large.Id] = [first, second, third];
        var movie = new MediaItem("movie", "Filme", "", MediaKind.Movie);
        check(PodcastFeed.OrderByUnplayed([small, movie, first, large], episodes,
            new Dictionary<string, PodcastHeardEntry>()).SequenceEqual([large, movie, first, small]),
            "Ordenação de podcasts preserva posições de filmes e episódios nos favoritos mistos");
        check(PodcastFeed.OrderByUnplayed([small, large], episodes, new Dictionary<string, PodcastHeardEntry>())[0] == large,
            "Contagem é calculada com os episódios ouvidos do perfil ativo");
    }
}
