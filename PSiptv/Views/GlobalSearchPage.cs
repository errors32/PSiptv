using PSiptv.Core;
using PSiptv.Services;

namespace PSiptv.Views;

public sealed class GlobalSearchPage : LocalizedPage
{
    private readonly PlaylistAccount account;
    private readonly SearchBar search = new() { Placeholder = "Pesquisar em canais, programas, filmes e séries" };
    private readonly Label status = Ui.Text("A preparar pesquisa global…", 13, true);
    private readonly ActivityIndicator catalogLoading = new() { IsVisible = true, IsRunning = true, WidthRequest = 22, HeightRequest = 22 };
    private readonly CollectionView results = new() { SelectionMode = SelectionMode.Single };
    private readonly CancellationTokenSource lifetime = new();
    private readonly Dictionary<MediaKind, IReadOnlyList<MediaItem>> catalogs = [];
    private IReadOnlyList<TvProgramme> programmes = [];
    private IReadOnlyList<WatchEntry> history = [];
    private CancellationTokenSource? typing;
    private bool loaded;

    public GlobalSearchPage(PlaylistAccount account)
    {
        this.account = account;
        Ui.Page(this, "Pesquisa global");
        search.SetDynamicResource(SearchBar.TextColorProperty, "Ink");
        search.SetDynamicResource(SearchBar.PlaceholderColorProperty, "Muted");
        catalogLoading.SetDynamicResource(ActivityIndicator.ColorProperty, "Accent");
        status.VerticalOptions = LayoutOptions.Center;
        results.EmptyView = Ui.Text("Escreva pelo menos dois caracteres para pesquisar.", 15, true);
        results.ItemTemplate = new DataTemplate(() =>
        {
            var title = Ui.Text("", 16); title.FontAttributes = FontAttributes.Bold;
            title.SetBinding(Label.TextProperty, nameof(SearchResult.Title));
            var detail = Ui.Text("", 12, true); detail.SetBinding(Label.TextProperty, nameof(SearchResult.Detail));
            var icon = Ui.Text("", 18); icon.SetBinding(Label.TextProperty, nameof(SearchResult.Icon));
            var grid = new Grid
            {
                ColumnSpacing = 10,
                ColumnDefinitions = [new(34), new(GridLength.Star), new(22)]
            };
            grid.Add(icon);
            grid.Add(Ui.Stack(title, detail), 1);
            grid.Add(Ui.Text("›", 24, true), 2);
            var card = Ui.FocusableCard(grid, value => results.SelectedItem = value);
            card.Margin = new Thickness(0, 0, 0, 7);
            card.Padding = 12;
            return card;
        });
        results.SelectionChanged += OnSelected;
        search.TextChanged += (_, _) => DebounceSearch();
        var progress = new Grid
        {
            ColumnDefinitions = [new(28), new(GridLength.Star)],
            ColumnSpacing = 6
        };
        progress.Add(catalogLoading);
        progress.Add(status, 1);
        var header = Ui.Stack(Ui.Text("Pesquisa global", 25), search, progress);
        var grid = new Grid
        {
            Padding = 16, RowSpacing = 10,
            RowDefinitions = [new(GridLength.Auto), new(GridLength.Star)]
        };
        grid.Add(header);
        grid.Add(results, 0, 1);
        Content = grid;
        AppServices.Locked += Clear;
        // On a television, opening this page must not immediately cover the
        // results with the on-screen keyboard. The user can focus the search
        // box explicitly when they want to type.
        Loaded += (_, _) => { if (!Ui.IsTelevision) search.Focus(); };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (!loaded) await LoadAsync();
    }

    private async Task LoadAsync()
    {
        var failures = new List<string>();
        SetCatalogLoading(true);
        try
        {
            status.Text = "A carregar dados disponíveis…";
            foreach (var (kind, items) in CatalogOptionsService.Catalogs)
                catalogs[kind] = items;
            // Secure-storage failures must not make cached catalogues unusable.
            try { await FavoritesService.LoadAsync(); }
            catch (Exception ex) { failures.Add("favoritos"); System.Diagnostics.Debug.WriteLine(ex); }
            try { history = await HistoryService.LoadAsync(account.Id); }
            catch (Exception ex) { failures.Add("histórico"); System.Diagnostics.Debug.WriteLine(ex); }
            lifetime.Token.ThrowIfCancellationRequested();
            loaded = true;
            ApplySearch();
            var guideAccount = account;
            if (account.Provider is ProviderType.M3U or ProviderType.LocalM3U)
            {
                if (Enum.GetValues<MediaKind>().Any(kind => !catalogs.ContainsKey(kind)))
                {
                    try
                    {
                        using var request = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                        request.CancelAfter(TimeSpan.FromMinutes(2));
                        var playlist = await AppServices.Client.LoadM3uAsync(account, request.Token);
                        if (account.EpgUrl.Length == 0 && playlist.EpgUrl.Length > 0)
                            guideAccount = account with { EpgUrl = playlist.EpgUrl };
                        foreach (var kind in Enum.GetValues<MediaKind>())
                        {
                            var items = playlist.Items.Where(item => item.Kind == kind).ToArray();
                            catalogs[kind] = items;
                            CatalogOptionsService.Catalogs[kind] = items;
                            await TrySaveCatalogAsync(kind, items);
                        }
                    }
                    catch (OperationCanceledException) when (!lifetime.IsCancellationRequested) { failures.Add("lista M3U"); }
                    catch (Exception ex) { failures.Add("lista M3U"); System.Diagnostics.Debug.WriteLine(ex); }
                }
            }
            else
            {
                // Some panels reject concurrent player_api requests. Load only
                // missing catalogues and do so sequentially, preserving every
                // successful result if another endpoint is slow or unavailable.
                foreach (var kind in Enum.GetValues<MediaKind>())
                {
                    if (catalogs.ContainsKey(kind)) continue;
                    try
                    {
                        status.Text = LanguageService.Format("A carregar {0}…", TypeName(kind));
                        using var request = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                        request.CancelAfter(TimeSpan.FromMinutes(2));
                        var items = await AppServices.Client.GetCatalogAsync(account, kind, request.Token);
                        catalogs[kind] = items;
                        CatalogOptionsService.Catalogs[kind] = items;
                        await TrySaveCatalogAsync(kind, items);
                        ApplySearch();
                    }
                    catch (OperationCanceledException) when (!lifetime.IsCancellationRequested) { failures.Add(TypeName(kind)); }
                    catch (Exception ex) { failures.Add(TypeName(kind)); System.Diagnostics.Debug.WriteLine(ex); }
                }
            }
            var contentCount = catalogs.Values.Sum(x => x.Count);
            SetCatalogLoading(false);
            status.Text = failures.Count == 0
                ? "Catálogos prontos · A carregar programação EPG…"
                : LanguageService.Format("Pesquisa disponível · {0} conteúdos · Algumas fontes não responderam", contentCount);
            ApplySearch();
            try
            {
                using var epgRequest = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                epgRequest.CancelAfter(TimeSpan.FromSeconds(45));
                programmes = await AppServices.Client.GetFullGuideAsync(guideAccount, epgRequest.Token);
                if (AppServices.ActiveAccount?.Id != account.Id) return;
                status.Text = failures.Count == 0
                    ? LanguageService.Format("Pesquisa pronta · {0} conteúdos e {1} programas", contentCount, programmes.Count)
                    : LanguageService.Format("Pesquisa pronta · {0} conteúdos e {1} programas · Resultado parcial", contentCount, programmes.Count);
            }
            catch (OperationCanceledException) { throw; }
            catch
            {
                status.Text = LanguageService.Format("Pesquisa pronta · {0} conteúdos · EPG indisponível", contentCount);
            }
            ApplySearch();
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (OperationCanceledException)
        {
            loaded = true;
            status.Text = LanguageService.Format("Pesquisa disponível · {0} conteúdos · O fornecedor demorou demasiado tempo", catalogs.Values.Sum(x => x.Count));
            ApplySearch();
        }
        catch (Exception ex)
        {
            // Searching cached data is still useful and avoids a blocking error
            // dialog when optional provider endpoints behave differently.
            loaded = true;
            status.Text = catalogs.Count > 0
                ? LanguageService.Format("Pesquisa disponível · {0} conteúdos · Resultado parcial", catalogs.Values.Sum(x => x.Count))
                : "Não foi possível carregar conteúdos. Feche e tente novamente.";
            System.Diagnostics.Debug.WriteLine(ex);
            ApplySearch();
        }
        finally { SetCatalogLoading(false); }
    }

    private void SetCatalogLoading(bool active)
    {
        catalogLoading.IsRunning = active;
        catalogLoading.IsVisible = active;
    }

    private async Task TrySaveCatalogAsync(MediaKind kind, IReadOnlyList<MediaItem> items)
    {
        try { await CatalogCacheService.SaveAsync(account.Id, kind, items); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        { System.Diagnostics.Debug.WriteLine(ex); }
    }

    private static string TypeName(MediaKind kind) => LanguageService.Text(kind switch
    {
        MediaKind.Channel => "canais", MediaKind.Movie => "filmes", _ => "séries"
    });

    private void DebounceSearch()
    {
        typing?.Cancel();
        typing?.Dispose();
        typing = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        _ = DebounceSearchAsync(typing.Token);
    }

    private async Task DebounceSearchAsync(CancellationToken token)
    {
        try { await Task.Delay(220, token); ApplySearch(); }
        catch (OperationCanceledException) { }
    }

    private void ApplySearch()
    {
        var query = search.Text?.Trim() ?? "";
        if (query.Length < 2)
        {
            results.ItemsSource = null;
            results.EmptyView = Ui.Text("Escreva pelo menos dois caracteres para pesquisar.", 15, true);
            return;
        }

        var found = new List<SearchResult>();
        var allMedia = catalogs.Values.SelectMany(value => value).Concat(FavoritesService.Items).Concat(history.Select(entry => entry.Item))
            .Where(item => !CatalogOptionsService.Current.IsHidden(item))
            .DistinctBy(CatalogPreferences.ItemKey);
        foreach (var item in allMedia.Where(item => Matches(item.Name, query) || Matches(item.Category, query)))
        {
            var type = item.Kind switch { MediaKind.Channel => "Canal", MediaKind.Movie => "Filme", _ => item.HasEpisodes ? "Série" : "Episódio" };
            var favorite = FavoritesService.Contains(item) ? " ★" : "";
            found.Add(new(item.Name, $"{LanguageService.Text(type)}{favorite} · {item.Category}",
                item.Kind switch { MediaKind.Channel => "▣", MediaKind.Movie => "▶", _ => "▤" }, item, null, null));
        }

        var channelByEpg = catalogs.GetValueOrDefault(MediaKind.Channel, [])
            .Where(channel => channel.EpgId.Length > 0)
            .GroupBy(channel => channel.EpgId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var now = DateTimeOffset.Now;
        foreach (var programme in programmes.Where(programme => programme.End > now.AddDays(-30) &&
                     (Matches(programme.Title, query) || Matches(programme.Description, query))))
        {
            if (!channelByEpg.TryGetValue(programme.ChannelId, out var channel)) continue;
            var playable = programme.End > now || CatchupStream.IsAvailable(channel, programme, now);
            if (!playable || CatalogOptionsService.Current.IsHidden(channel)) continue;
            found.Add(new(programme.Title,
                $"{LanguageService.Text("Programa")} · {channel.Name} · {AppOptions.FormatTime(programme.Start)}",
                "◷", channel, programme, channel));
        }

        var ordered = found.OrderBy(row => ExactRank(row.Title, query)).ThenBy(row => row.Title, StringComparer.CurrentCultureIgnoreCase)
            .Take(300).ToArray();
        results.ItemsSource = ordered;
        results.EmptyView = Ui.Text("Nenhum resultado encontrado.", 15, true);
    }

    private async void OnSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not SearchResult result || AppServices.ActiveAccount?.Id != account.Id) return;
        results.SelectedItem = null;
        try
        {
            if (result.Programme is { } programme && result.Channel is { } channel)
            {
                var now = DateTimeOffset.Now;
                var archive = CatchupStream.Create(account, channel, programme, now);
                if (archive is not null) await PlaybackService.PlayAsync(this, archive);
                else if (programme.Start <= now && programme.End > now) await PlaybackService.PlayAsync(this, channel);
                else await Navigation.PushAsync(new GuidePage(account, channel));
            }
            else if (result.Item is { } item && (item.Kind == MediaKind.Movie || item.HasEpisodes))
                await Navigation.PushAsync(new MediaDetailsPage(account, item));
            else if (result.Item is { } playable) await PlaybackService.PlayAsync(this, playable);
        }
        catch (Exception ex) { await Ui.ErrorAsync(this, ex); }
    }

    private static bool Matches(string value, string query) => value.Contains(query, StringComparison.CurrentCultureIgnoreCase);
    private static int ExactRank(string value, string query) => value.Equals(query, StringComparison.CurrentCultureIgnoreCase) ? 0
        : value.StartsWith(query, StringComparison.CurrentCultureIgnoreCase) ? 1 : 2;
    private void Clear() { lifetime.Cancel(); typing?.Cancel(); results.ItemsSource = null; }

    protected override void OnNavigatedFrom(NavigatedFromEventArgs args)
    {
        base.OnNavigatedFrom(args);
        if (!Navigation.NavigationStack.Contains(this))
        {
            Clear();
            AppServices.Locked -= Clear;
            typing?.Dispose();
            lifetime.Dispose();
        }
    }

    private sealed record SearchResult(string Title, string Detail, string Icon, MediaItem? Item,
        TvProgramme? Programme, MediaItem? Channel);
}
