using PSiptv.Core;
using PSiptv.Services;

namespace PSiptv.Views;

public sealed class SmartHomeView : ContentView
{
    private readonly VerticalStackLayout content = new() { Spacing = 22, Padding = new Thickness(0, 4, 0, 18) };
    private CancellationTokenSource? loading;
    private int generation;

    public SmartHomeView()
    {
        var scroll = new ScrollView { Content = content };
        Content = scroll;
        Clear();
    }

    public void Clear()
    {
        loading?.Cancel();
        generation++;
        content.Clear();
        content.Add(Ui.Text("Abra uma lista para preparar a sua página inicial.", 16, true));
    }

    public async Task LoadAsync(PlaylistAccount account, IReadOnlyList<MediaItem> channels, bool showLoading = true)
    {
        loading?.Cancel();
        var cancellation = new CancellationTokenSource();
        loading = cancellation;
        var request = ++generation;
        if (showLoading) RenderLoading();
        try
        {
            var favoritesTask = FavoritesService.LoadAsync();
            var historyTask = HistoryService.LoadAsync(account.Id);
            var recordingsTask = DvrService.LoadAsync(account.Id);
            await Task.WhenAll(favoritesTask, historyTask, recordingsTask);
            cancellation.Token.ThrowIfCancellationRequested();
            if (!IsCurrent(account, request)) return;

            var history = historyTask.Result;
            var recordings = recordingsTask.Result;
            var favorites = FavoritesService.Items;
            Render(account, history, favorites, recordings, []);

            var candidates = favorites.Where(item => item.Kind == MediaKind.Channel)
                .Concat(SmartHomePolicy.RecentChannels(history).Select(entry => entry.Item))
                .Concat(channels.Where(item => item.Kind == MediaKind.Channel))
                .Where(item => item.EpgId.Length > 0 || account.Provider == ProviderType.Xtream)
                .GroupBy(item => CatalogPreferences.ItemKey(item), StringComparer.Ordinal)
                .Select(group => group.First()).Take(12).ToArray();
            var upcoming = await LoadUpcomingAsync(account, candidates, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (IsCurrent(account, request)) Render(account, history, favorites, recordings, upcoming);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (IsCurrent(account, request))
            {
                RenderLoading(LanguageService.Text("Não foi possível preparar a página inicial.") + "\n" + ex.Message);
            }
        }
        finally
        {
            if (ReferenceEquals(loading, cancellation)) loading = null;
            cancellation.Dispose();
        }
    }

    private async Task<IReadOnlyList<UpcomingProgramme>> LoadUpcomingAsync(PlaylistAccount account,
        IReadOnlyList<MediaItem> channels, CancellationToken token)
    {
        using var gate = new SemaphoreSlim(4);
        var requests = channels.Select(async channel =>
        {
            await gate.WaitAsync(token);
            try
            {
                var guide = await EpgService.GetAsync(account, channel, token);
                return guide.Select(programme => new UpcomingProgramme(channel, programme)).ToArray();
            }
            catch (OperationCanceledException) { throw; }
            catch { return []; }
            finally { gate.Release(); }
        });
        var supplied = (await Task.WhenAll(requests)).SelectMany(items => items);
        return SmartHomePolicy.StartingSoon(supplied, DateTimeOffset.Now, TimeSpan.FromMinutes(90), 10);
    }

    private void Render(PlaylistAccount account, IReadOnlyList<WatchEntry> history,
        IReadOnlyList<MediaItem> favorites, IReadOnlyList<DvrRecording> recordings,
        IReadOnlyList<UpcomingProgramme> upcoming)
    {
        content.Clear();
        var refresh = Ui.Button("Atualizar início", () => LoadAsync(account,
            CatalogOptionsService.Catalogs.GetValueOrDefault(MediaKind.Channel) ?? []));
        refresh.MinimumWidthRequest = 130;
        var heading = new Grid
        {
            ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)],
            ColumnSpacing = 10
        };
        var greeting = Ui.Text(LanguageService.Format("Olá, {0}", UserProfileService.Active.Name), 25);
        greeting.FontAttributes = FontAttributes.Bold;
        heading.Add(Ui.Stack(greeting, Ui.Text(account.Name + " · " + LanguageService.Text("Escolhas para si"), 13, true)));
        heading.Add(refresh, 1);
        content.Add(heading);

        var sectionCount = 0;
        sectionCount += AddShelf("Continuar a ver", SmartHomePolicy.ContinueWatching(history).Select(entry =>
            new HomeCard(entry.Item, entry.Item.Name,
                LanguageService.Format("Retomar em {0}", FormatPosition(entry.PositionSeconds)),
                () => PlaybackService.PlayAsync(FindPage(), entry.Item, resume: entry.PositionSeconds))));
        sectionCount += AddShelf("Canais recentes", SmartHomePolicy.RecentChannels(history).Select(entry =>
            new HomeCard(entry.Item, entry.Item.Name,
                LanguageService.Format("Visto em {0}", AppOptions.FormatTime(entry.WatchedAt)),
                () => PlaybackService.PlayAsync(FindPage(), entry.Item))));
        sectionCount += AddShelf("Favoritos", favorites.Take(10).Select(item =>
            new HomeCard(item, item.Name, item.Category, () => OpenMediaAsync(account, item))));
        sectionCount += AddShelf("A começar", upcoming.Select(item =>
            new HomeCard(item.Channel, item.Programme.Title,
                $"{item.Channel.Name} · {LanguageService.Format("Começa às {0}", item.Programme.Start.ToLocalTime().ToString(AppOptions.Clock12 ? "hh:mm tt" : "HH:mm"))}",
                () => FindPage().Navigation.PushAsync(new GuidePage(account, item.Channel)))));
        sectionCount += AddShelf("Gravações recentes", recordings
            .Where(item => item.State is DvrRecordingState.Completed or DvrRecordingState.Recording or DvrRecordingState.Scheduled)
            .OrderByDescending(item => item.IsRecording).ThenByDescending(item => item.Start).Take(10)
            .Select(item => new HomeCard(item.Channel, item.ProgrammeTitle, RecordingDetails(item),
                () => OpenRecordingAsync(item))));

        if (sectionCount == 0)
            content.Add(Ui.Card(Ui.Stack(Ui.Text("A sua página inicial ainda está vazia.", 17),
                Ui.Text("Veja um canal, adicione favoritos ou agende uma gravação para começar.", 13, true))));
    }

    private int AddShelf(string title, IEnumerable<HomeCard> supplied)
    {
        var cards = supplied.ToArray();
        if (cards.Length == 0) return 0;
        var row = new HorizontalStackLayout { Spacing = 12, Padding = new Thickness(2, 2, 8, 6) };
        foreach (var card in cards) row.Add(CreateCard(card));
        content.Add(Ui.Stack(Ui.Text(title, 20), new ScrollView
        {
            Orientation = ScrollOrientation.Horizontal,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Never,
            Content = row
        }));
        return 1;
    }

    private View CreateCard(HomeCard card)
    {
        var image = new LogoImage
        {
            BindingContext = card.Item, WidthRequest = 58, HeightRequest = 58,
            Aspect = Aspect.AspectFit, VerticalOptions = LayoutOptions.Center
        };
        var title = Ui.Text(card.Title, 16); title.FontAttributes = FontAttributes.Bold;
        title.MaxLines = 2; title.LineBreakMode = LineBreakMode.TailTruncation;
        var subtitle = Ui.Text(card.Subtitle, 12, true);
        subtitle.MaxLines = 2; subtitle.LineBreakMode = LineBreakMode.TailTruncation;
        var layout = new Grid
        {
            ColumnDefinitions = [new(new GridLength(64)), new(GridLength.Star)],
            ColumnSpacing = 10
        };
        layout.Add(image); layout.Add(Ui.Stack(title, subtitle), 1);
        var border = Ui.FocusableCard(layout, _ => _ = ActivateAsync(card.Action));
        border.FavoriteItem = card.Item;
        border.WidthRequest = Ui.IsTelevision ? 320 : 270;
        border.MinimumHeightRequest = Ui.IsTelevision ? 112 : 104;
        border.BindingContext = card;
        if (!Ui.UsesLargeControls)
        {
            var tap = new TapGestureRecognizer();
            tap.Tapped += (_, _) => _ = ActivateAsync(card.Action);
            border.GestureRecognizers.Add(tap);
        }
        return border;
    }

    private async Task ActivateAsync(Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex) { await Ui.ErrorAsync(this, ex); }
    }

    private async Task OpenMediaAsync(PlaylistAccount account, MediaItem item)
    {
        if (AppServices.ActiveAccount?.Id != account.Id) return;
        if (item.Kind == MediaKind.Movie || item.HasEpisodes)
            await FindPage().Navigation.PushAsync(new MediaDetailsPage(account, item));
        else await PlaybackService.PlayAsync(FindPage(), item);
    }

    private async Task OpenRecordingAsync(DvrRecording recording)
    {
        if (!recording.IsCompleted || recording.FileName.Length == 0)
        {
            await FindPage().Navigation.PushAsync(new RecordingsPage());
            return;
        }
        var path = DvrService.RecordingLocation(recording.FileName);
        if (path.Length == 0) throw new InvalidOperationException("O ficheiro desta gravação já não existe.");
        var item = recording.Channel with
        {
            Name = recording.ProgrammeTitle, Url = new Uri(path, UriKind.Absolute).AbsoluteUri,
            Kind = MediaKind.Movie, IsCatchup = true
        };
        await FindPage().Navigation.PushAsync(new PlayerPage(item));
    }

    private static string RecordingDetails(DvrRecording item)
    {
        var state = LanguageService.Text(item.State switch
        {
            DvrRecordingState.Scheduled => "Agendada",
            DvrRecordingState.Recording => "A gravar",
            _ => "Concluída"
        });
        return $"{item.Channel.Name} · {AppOptions.FormatTime(item.Start)} · {state}";
    }

    private void RenderLoading(string text = "A preparar a sua página inicial…")
    {
        content.Clear();
        content.Add(Ui.Text(text, 16, true));
    }

    private bool IsCurrent(PlaylistAccount account, int request) =>
        request == generation && AppServices.ActiveAccount?.Id == account.Id;

    private Page FindPage()
    {
        Element? current = this;
        while (current is not null)
        {
            if (current is Page page) return page;
            current = current.Parent;
        }
        throw new InvalidOperationException("A página principal já não está disponível.");
    }

    private static string FormatPosition(double seconds) =>
        TimeSpan.FromSeconds(Math.Max(0, seconds)).ToString(seconds >= 3600 ? @"h\:mm\:ss" : @"m\:ss");

    private sealed record HomeCard(MediaItem Item, string Title, string Subtitle, Func<Task> Action);
}
