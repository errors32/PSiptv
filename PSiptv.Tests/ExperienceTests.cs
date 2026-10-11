using System.Text.Json;
using PSiptv.Core;

internal static class ExperienceTests
{
    public static void Run(Action<bool, string> check)
    {
        var hd = new MediaItem("hd", "RTP 1 HD", "TV", MediaKind.Channel, "https://example.test/hd");
        var sd = hd with { Id = "sd", Name = "RTP 1 SD", Url = "https://example.test/sd" };
        var fhd = hd with { Id = "fhd", Name = "RTP 1 FHD", Url = "https://example.test/fhd" };
        var fourK = hd with { Id = "4k", Name = "RTP 1 [4K]", Url = "https://example.test/4k" };
        check(ChannelGroupPolicy.Key(hd) == ChannelGroupPolicy.Key(sd) && ChannelGroupPolicy.Key(hd) == ChannelGroupPolicy.Key(fourK), "Alternativas agrupam qualidades do mesmo canal");
        check(ChannelGroupPolicy.Key(hd) != ChannelGroupPolicy.Key(hd with { Name = "RTP 2 HD" }), "Agrupamento preserva a identidade de canais diferentes");
        check(ChannelGroupPolicy.Key(hd with { Name = "HDTV Portugal" }) == "HDTV PORTUGAL", "Agrupamento não remove HD do nome de uma estação");
        check(ChannelGroupPolicy.Alternatives(fhd, [fhd, sd, hd], quality: "HD").First() == hd, "Qualidade HD não seleciona FHD por correspondência parcial");
        check(ChannelGroupPolicy.Alternatives(hd, [hd, sd], CatalogPreferences.ItemKey(sd)).First() == sd, "Versão explícita tem precedência");
        check(ChannelGroupPolicy.Alternatives(hd, [hd, hd, sd with { IsCatchup = true }, fhd with { Kind = MediaKind.Movie }]).Count == 1, "Alternativas excluem duplicados, VOD e catch-up");
        var start = new DateTimeOffset(2026, 10, 11, 18, 0, 0, TimeSpan.Zero);
        DvrRecording Recording(string id, int from, int to, string account = "a") => new(id, account, "p", hd, id, start.AddMinutes(from), start.AddMinutes(to));
        var proposed = Recording("new", 0, 60);
        check(!DvrConflictPolicy.ExceedsLimit(proposed, [Recording("first", 0, 30), Recording("second", 30, 60)], 2), "Gravações sequenciais não contam como simultâneas");
        check(DvrConflictPolicy.ExceedsLimit(proposed, [Recording("first", 0, 40), Recording("second", 30, 60)], 2), "Conflito deteta picos reais de concorrência");
        check(!DvrConflictPolicy.ExceedsLimit(proposed, [Recording("adjacent", 60, 90)], 1), "Fim e início no mesmo instante não criam conflito");
        check(!DvrConflictPolicy.ExceedsLimit(proposed, [Recording("other", 0, 60, "b")], 1), "Limite de gravações mantém isolamento entre listas");
        check(!DvrConflictPolicy.ExceedsLimit(proposed, [Recording("failed", 0, 60) with { State = DvrRecordingState.Failed }, proposed], 1), "Conflitos ignoram falhas e o próprio agendamento");
        var margins = DvrConflictPolicy.WithMargins(proposed, 5, 10);
        check(margins.Start == start.AddMinutes(-5) && margins.End == start.AddMinutes(70) && margins.Id == proposed.Id, "Margens mantêm identidade e expandem horários");
        check(DvrConflictPolicy.ExceedsLimit(margins, [Recording("previous", -10, -1)], 1), "Conflitos incluem a margem anterior");
        check(!DvrConflictPolicy.CanClean(proposed, start.AddDays(10), 7) && DvrConflictPolicy.CanClean(proposed with { State = DvrRecordingState.Completed }, start.AddDays(10), 7), "Limpeza conserva agendamentos e só elimina concluídos antigos");
        check(!DvrConflictPolicy.CanClean(proposed with { State = DvrRecordingState.Completed }, start.AddDays(100), 0), "Limpeza manual nunca apaga automaticamente");
        var sourceA = new TvProgramme("a", "Notícias", "", start, start.AddHours(1));
        var sourceB = sourceA with { ChannelId = "b", Title = "Cultura" };
        var guide = new EpgConfiguration { Sources = [new("https://example.test/a", 60), new("https://example.test/b", -30)] };
        guide.Mappings[CatalogPreferences.ItemKey(hd)] = new("b", 1);
        var mapped = guide.Select(hd, [[sourceA], [sourceB]]);
        check(mapped.Count == 1 && mapped[0].Title == "Cultura" && mapped[0].Start == start.AddMinutes(-30), "Associação EPG seleciona a fonte e corrige o horário");
        guide.Mappings.Clear(); guide.Sources = [new("https://example.test/a"), new("https://example.test/b")];
        var sameId = hd with { EpgId = "a" };
        check(guide.Select(sameId, [[sourceA], [sourceA]]).Count == 1, "Múltiplas fontes não duplicam o mesmo programa");
        check(guide.Select(sameId, [[], [sourceA]]).Count == 1, "Fonte vazia não impede o guia de outra fonte");
        var library = new PersonalLibrary();
        check(library.State(hd) == ViewingState.Unwatched, "Bibliotecas antigas começam por ver");
        library.SetState(hd, ViewingState.Completed);
        var restored = JsonSerializer.Deserialize<PersonalLibrary>(JsonSerializer.Serialize(library))!;
        check(restored.State(hd) == ViewingState.Completed, "Estado concluído sobrevive à serialização");
        var first = new PersonalLibrary { Pinned = [hd], Collections = [new() { Id = "shared", Name = "Antes", Items = [hd] }] };
        var second = new PersonalLibrary { Pinned = [sd, hd], Collections = [new() { Id = "shared", Name = "Depois", Items = [sd] }] };
        var merged = PersonalLibrary.Merge(first, second);
        check(merged.Pinned.Count == 2 && merged.Collections.Single().Items.Count == 2 && merged.Collections.Single().Name == "Depois", "Reposição combina coleções sem perder itens locais");
        var legacy = JsonSerializer.Deserialize<CatalogPreferences>("{\"Hidden\":[]}")!;
        check(legacy.Epg.Sources.Count == 0 && JsonSerializer.Deserialize<PersonalLibrary>("{}")!.HomeOrder.Count == 6, "Dados anteriores permanecem compatíveis sem migração");
    }
}
