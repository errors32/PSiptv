using PSiptv.Core;
using PSiptv.Services;
using System.Collections.ObjectModel;

namespace PSiptv.Views;

public sealed class PodcastsPage : LocalizedPage
{
    private readonly MediaItem? podcast;
    private readonly bool favoritesOnly;
    private readonly bool continuing;
    private readonly ObservableCollection<MediaItem> displayed = [];
    private IReadOnlyList<MediaItem> filteredItems = [];
    private FavoriteSnapshot? snapshot;
    private HashSet<string> favoriteIds = [];
    private DateTimeOffset? lastVisit;
    private DateTimeOffset openedAt;
    private string? scrollAnchor;
    private int firstVisibleIndex;
    private readonly Dictionary<string, (int New, int Unplayed)> feedCounts = [];
    private readonly Dictionary<string, IReadOnlyList<MediaItem>> feedEpisodes = [];
    private DateTimeOffset? renderedVersion;
    private int countsRevision;
    private int renderedCountsRevision;
    private readonly SearchBar search = new() { Placeholder = "Pesquisar podcasts" };
    private bool showAll;
    private readonly Button showAllButton;
    private readonly Button markAllButton;
    private readonly Picker dateOrder = new()
    {
        Title = "Ordenar por data", ItemsSource = new[] { "Mais recentes", "Mais antigos", "Ordem do feed" }, SelectedIndex = 0
    };
    private readonly Label status = Ui.Text("", 14, true);
    private readonly CollectionView list = new() { SelectionMode = SelectionMode.None };
    private IReadOnlyList<MediaItem> items = [];
    private IReadOnlyDictionary<string, PodcastHeardEntry> heard = new Dictionary<string, PodcastHeardEntry>();
    private CancellationTokenSource? loading;
    private readonly Grid layout = new() { Padding = 16, RowSpacing = 10, ColumnSpacing = 16,
        RowDefinitions = [new(GridLength.Auto), new(GridLength.Star)],
        ColumnDefinitions = [new(GridLength.Star)] };
    private readonly Grid sidebar = new() { RowSpacing = 10,
        RowDefinitions = [new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto)] };
    private readonly ScrollView sidebarScroll;
    private readonly View information;
    private readonly View artwork;
    private bool landscape;

    public PodcastsPage(MediaItem? podcast = null, bool favoritesOnly = false, bool continuing = false)
    {
        this.podcast = podcast;
        this.favoritesOnly = favoritesOnly;
        this.continuing = continuing;
        Ui.Page(this, continuing ? "Continuar a ouvir" : podcast?.Name ?? (favoritesOnly ? "Podcasts favoritos" : "Podcasts"));
        list.ItemsSource = displayed;
        list.RemainingItemsThreshold = 15;
        list.RemainingItemsThresholdReached += (_, _) => AppendPage();
        list.Scrolled += (_, e) =>
        {
            firstVisibleIndex = e.FirstVisibleItemIndex;
            if (e.FirstVisibleItemIndex >= 0 && e.FirstVisibleItemIndex < displayed.Count)
                scrollAnchor = displayed[e.FirstVisibleItemIndex].Id;
        };
        search.SetDynamicResource(SearchBar.TextColorProperty, "Ink");
        search.SetDynamicResource(SearchBar.PlaceholderColorProperty, "Muted");
        showAllButton = IconButton(FaIcons.Eye, "Mostrar todos os episódios", () =>
        {
            showAll = !showAll;
            SetAction(showAllButton!, showAll ? FaIcons.EyeSlash : FaIcons.Eye,
                showAll ? "Ocultar episódios ouvidos" : "Mostrar todos os episódios");
            Show();
            return Task.CompletedTask;
        });
        markAllButton = IconButton(FaIcons.CheckDouble, "Marcar todos os episódios da lista como ouvidos", async () =>
        {
            await FavoritesService.SetAllHeardAsync(VisibleItems().Where(IsTrackedEpisode).ToArray());
            await RefreshAsync();
        });
        dateOrder.Title = LanguageService.Text("Ordenar por data");
        dateOrder.ItemsSource = new[] { "Mais recentes", "Mais antigos", "Ordem do feed" }.Select(LanguageService.Text).ToArray();
        dateOrder.SetDynamicResource(Picker.TextColorProperty, "Ink");
        dateOrder.IsVisible = podcast is not null || favoritesOnly;
        list.EmptyView = Ui.Text("Sem podcasts para mostrar.", 16, true);
        list.ItemTemplate = new DataTemplate(() =>
        {
            var cover = new LogoImage { WidthRequest = 64, HeightRequest = 64, Aspect = Aspect.AspectFit, VerticalOptions = LayoutOptions.Center };
            var name = Ui.Text("", 16); name.MaxLines = 3;
            name.SetBinding(Label.TextProperty, nameof(MediaItem.Name));
            var state = Ui.Text("", 12, true);
            var date = Ui.Text("", 12, true);
            var favorite = new FavoriteButton();
            var toggle = IconButton(FaIcons.Check, "Marcar como ouvido", async () =>
            {
                if (name.BindingContext is not MediaItem episode) return;
                await FavoritesService.SetHeardAsync(episode, !heard.ContainsKey(episode.Id));
                await RefreshAsync();
            });
            var download = IconButton(FaIcons.Download, "Descarregar episódio em MP3", async () =>
            {
                if (name.BindingContext is not MediaItem episode || AppServices.ActiveAccount is not { } account) return;
                await OfflineDownloadService.QueueAsync(account, episode, episode.Category);
                await Navigation.PushAsync(new OfflineDownloadsPage());
            });
            var row = new Grid { Padding = 8, ColumnSpacing = 8,
                ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto)] };
            var details = new Grid { ColumnSpacing = 10, ColumnDefinitions = [new(new GridLength(64)), new(GridLength.Star)] };
            details.Add(cover); details.Add(Ui.Stack(name, date, state), 1);
            row.Add(details); row.Add(toggle, 1); row.Add(download, 2); row.Add(favorite, 3);
            row.BindingContextChanged += (_, _) =>
            {
                if (row.BindingContext is not MediaItem item) return;
                var tracked = IsTrackedEpisode(item);
                toggle.IsVisible = tracked;
                SetAction(toggle, heard.ContainsKey(item.Id) ? FaIcons.EarListen : FaIcons.Check,
                    heard.ContainsKey(item.Id) ? "Marcar como por ouvir" : "Marcar como ouvido");
                download.IsVisible = !item.HasEpisodes && OfflineDownloadPolicy.CanDownload(item);
                state.Text = item.HasEpisodes ? item.Category : tracked ?
                    heard.ContainsKey(item.Id) ? "✓ Ouvido" : "Por ouvir" : item.Category;
                if (item.HasEpisodes && feedCounts.TryGetValue(item.Id, out var counts))
                    state.Text = LanguageService.Format("{0} novos · {1} por ouvir", counts.New, counts.Unplayed);
                else if (!item.HasEpisodes && PodcastPolicy.IsNew(item, lastVisit, DateTimeOffset.UtcNow))
                    state.Text = LanguageService.Text("Novo episódio") + " · " + state.Text;
                if (!item.HasEpisodes && snapshot?.PodcastProgress?.GetValueOrDefault(item.Id) is { Position: > 0 } progress &&
                    !heard.ContainsKey(item.Id))
                    state.Text += " · " + LanguageService.Format("Continuar aos {0}", TimeSpan.FromSeconds(progress.Position).ToString(@"hh\:mm\:ss"));
                date.IsVisible = !item.HasEpisodes;
                date.Text = item.PublishedAt is { } published
                    ? LanguageService.Format("Publicado em {0}", published.ToLocalTime().ToString("dd/MM/yyyy"))
                    : LanguageService.Text("Data indisponível");
            };
            return Ui.FocusableCard(row, async value =>
            {
                if (value is not MediaItem item) return;
                try { await PodcastService.OpenAsync(this, item); }
                catch (Exception ex) { await Ui.ErrorAsync(this, ex); }
            }, tapTarget: details);
        });
        search.SearchButtonPressed += async (_, _) => await LoadAsync();
        search.TextChanged += (_, _) => { if (podcast is not null || favoritesOnly || continuing) Show(); };
        dateOrder.SelectedIndexChanged += (_, _) => Show();
        var actions = new Grid { ColumnSpacing = 8, ColumnDefinitions = [new(GridLength.Star), new(GridLength.Star)] };
        actions.Add(Ui.Button("Pesquisar", LoadAsync));
        actions.Add(Ui.Button("Favoritos", () => Navigation.PushAsync(new PodcastsPage(favoritesOnly: true))), 1);
        actions.IsVisible = podcast is null && !favoritesOnly && !continuing;
        var shortcuts = new Grid { ColumnSpacing = 8, ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto), new(GridLength.Auto)] };
        shortcuts.Add(Ui.Button("Continuar a ouvir", () => Navigation.PushAsync(new PodcastsPage(continuing: true))));
        shortcuts.Add(IconButton(FaIcons.Gear, "Definições dos podcasts", () => Navigation.PushAsync(new PodcastSettingsPage())), 1);
        shortcuts.Add(IconButton(FaIcons.ArrowsRotate, "Atualizar podcasts", () => LoadAsync(force: true)), 2);
        shortcuts.IsVisible = !continuing;
        var subscription = podcast is null ? new FavoriteButton() { IsVisible = false } : new FavoriteButton { BindingContext = podcast };
        var heading = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)] };
        heading.Add(Ui.Text(Title ?? "Podcasts", 24)); heading.Add(subscription, 1);
        artwork = new LogoImage { BindingContext = podcast, WidthRequest = 88, HeightRequest = 88,
            Aspect = Aspect.AspectFit, HorizontalOptions = LayoutOptions.Start, IsVisible = false };
        var category = Ui.Text(podcast?.Category ?? "", 14, true);
        category.IsVisible = !string.IsNullOrWhiteSpace(podcast?.Category);
        information = Ui.Stack(artwork, heading, category, status);
        var episodeFilters = new Grid { ColumnSpacing = 8,
            ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto), new(GridLength.Auto)] };
        episodeFilters.Add(dateOrder); episodeFilters.Add(showAllButton, 1); episodeFilters.Add(markAllButton, 2);
        episodeFilters.IsVisible = podcast is not null || favoritesOnly;
        sidebar.Add(information);
        sidebar.Add(search, 0, 1);
        sidebar.Add(Ui.Stack(shortcuts, actions, episodeFilters), 0, 2);
        sidebarScroll = new ScrollView { Content = sidebar, Orientation = ScrollOrientation.Neither };
        layout.Add(sidebarScroll);
        layout.Add(list, 0, 1);
        Content = layout;
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        if (width <= 0 || height <= 0) return;
        var horizontal = width > height;
        if (horizontal != landscape)
        {
            landscape = horizontal;
            layout.RowDefinitions = horizontal ? [new(GridLength.Star)] :
                [new(GridLength.Auto), new(GridLength.Star)];
            layout.ColumnDefinitions = horizontal ? [new(new GridLength(1, GridUnitType.Star)), new(GridLength.Star)] :
                [new(GridLength.Star)];
            Grid.SetRow(list, horizontal ? 0 : 1);
            Grid.SetColumn(list, horizontal ? 1 : 0);
            Grid.SetRow(search, horizontal ? 0 : 1);
            Grid.SetRow(information, horizontal ? 1 : 0);
            sidebarScroll.Orientation = horizontal ? ScrollOrientation.Vertical : ScrollOrientation.Neither;
            artwork.IsVisible = horizontal && !string.IsNullOrWhiteSpace(podcast?.Logo);
        }
        // Keep the sidebar compact, leaving most of the width and all of the height for the list.
        if (horizontal)
        {
            var sidebarWidth = Math.Clamp((width - 48) * 0.32, 220, 320);
            if (layout.ColumnDefinitions[0].Width.Value != sidebarWidth)
                layout.ColumnDefinitions[0].Width = new GridLength(sidebarWidth);
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        FavoritesService.Changed -= OnFavoritesChanged;
        FavoritesService.Changed += OnFavoritesChanged;
        await LoadAsync();
    }
    protected override void OnDisappearing()
    {
        FavoritesService.Changed -= OnFavoritesChanged;
        loading?.Cancel();
        base.OnDisappearing();
    }
    private void OnFavoritesChanged() => Dispatcher.Dispatch(async () =>
    {
        try { await RefreshAsync(); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
    });

    private Task LoadAsync() => LoadAsync(force: false);

    private async Task LoadAsync(bool force)
    {
        loading?.Cancel(); loading?.Dispose(); loading = new();
        var token = loading.Token;
        var session = AppServices.SessionVersion;
        status.Text = "A carregar podcasts…";
        try
        {
            await FavoritesService.LoadAsync();
            snapshot = await FavoritesService.SnapshotAsync();
            if (podcast is not null && openedAt == default)
            {
                openedAt = DateTimeOffset.UtcNow;
                lastVisit = VisitFor(podcast.Id);
            }
            var cached = !favoritesOnly && !continuing
                ? await PodcastCacheService.ReadAsync(podcast?.Url ?? PodcastService.SearchCacheKey(search.Text ?? ""), token) : null;
            if (token.IsCancellationRequested || session != AppServices.SessionVersion) return;
            if (cached is not null)
            {
                items = cached.Items;
                await RefreshAsync();
                status.Text = LanguageService.Text("A atualizar os episódios…");
            }
            var loaded = podcast is not null ? await PodcastService.EpisodesAsync(podcast, token, force) :
                continuing ? ContinuingItems() : favoritesOnly ? FavoritesService.Items.Where(PodcastFeed.IsPodcast).ToArray() :
                await PodcastService.SearchAsync(search.Text ?? "", token, force);
            if (token.IsCancellationRequested || session != AppServices.SessionVersion) return;
            items = loaded;
            await RefreshAsync();
            status.Text = LanguageService.Format("{0} episódios apresentados · {1} no total", displayed.Count, filteredItems.Count) +
                " · " + UserProfileService.Active.Name;
            if (podcast is not null)
            {
                var shown = await PodcastCacheService.ReadAsync(podcast.Url, token);
                await FavoritesService.VisitPodcastAsync(podcast.Id, shown?.FetchedAt ?? openedAt);
            }
            if (podcast is null && !continuing) await LoadFeedCountsAsync(token, session);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) { status.Text = items.Count > 0 ? LanguageService.Text("A mostrar episódios guardados. Sem atualização de rede.") :
            "Não foi possível carregar os podcasts. Tente novamente."; System.Diagnostics.Debug.WriteLine(ex); }
    }
    private async Task RefreshAsync()
    {
        snapshot = await FavoritesService.SnapshotAsync();
        heard = snapshot?.HeardEpisodes ?? new Dictionary<string, PodcastHeardEntry>();
        favoriteIds = snapshot?.Entries.Select(e => e.Item.Id).ToHashSet() ?? [];
        if (favoritesOnly) items = FavoritesService.Items.Where(PodcastFeed.IsPodcast).ToArray();
        if (continuing) items = ContinuingItems();
        UpdateFeedCounts();
        Show();
    }
    private void Show()
    {
        var visible = VisibleItems();
        filteredItems = visible;
        var loadedCount = Math.Max(60, displayed.Count);
        var page = visible.Take(loadedCount).ToArray();
        markAllButton.IsEnabled = visible.Any(item => IsTrackedEpisode(item) && !heard.ContainsKey(item.Id));
        if (displayed.SequenceEqual(page) && renderedVersion == snapshot?.ModifiedAt && renderedCountsRevision == countsRevision) return;
        renderedVersion = snapshot?.ModifiedAt;
        renderedCountsRevision = countsRevision;
        var anchor = scrollAnchor;
        displayed.Clear();
        foreach (var item in page) displayed.Add(item);
        if ((podcast is not null || continuing || firstVisibleIndex > 0) && anchor is not null && displayed.FirstOrDefault(e => e.Id == anchor) is { } target)
            Dispatcher.Dispatch(() => list.ScrollTo(target, position: ScrollToPosition.Start, animate: false));
        status.Text = LanguageService.Format("{0} episódios apresentados · {1} no total", displayed.Count, visible.Count);
    }

    private void AppendPage()
    {
        foreach (var item in filteredItems.Skip(displayed.Count).Take(60).ToArray()) displayed.Add(item);
        status.Text = LanguageService.Format("{0} episódios apresentados · {1} no total", displayed.Count, filteredItems.Count);
    }

    private IReadOnlyList<MediaItem> ContinuingItems() => (snapshot?.PodcastProgress ?? new Dictionary<string, PodcastPlaybackEntry>())
        .Values.Where(p => PodcastPolicy.ResumePosition(p, snapshot?.HeardEpisodes.ContainsKey(p.Item.Id) == true) > 0)
        .OrderByDescending(p => p.UpdatedAt).Select(p => p.Item).ToArray();

    private Task LoadFeedCountsAsync(CancellationToken token, int session) => PodcastListingService.LoadAsync(items,
        (feed, episodes) =>
        {
            if (!token.IsCancellationRequested && session == AppServices.SessionVersion) feedEpisodes[feed.Id] = episodes;
        },
        () =>
        {
            if (token.IsCancellationRequested || session != AppServices.SessionVersion) return;
            UpdateFeedCounts();
            Show();
        }, token);

    private void UpdateFeedCounts()
    {
        foreach (var feed in items.Where(i => i.HasEpisodes))
        {
            if (!feedEpisodes.TryGetValue(feed.Id, out var episodes)) continue;
            var counts = (episodes.Count(e => PodcastPolicy.IsNew(e, VisitFor(feed.Id), DateTimeOffset.UtcNow)),
                episodes.Count(e => !heard.ContainsKey(e.Id)));
            if (feedCounts.TryGetValue(feed.Id, out var previous) && previous == counts) continue;
            feedCounts[feed.Id] = counts;
            countsRevision++;
        }
    }

    private DateTimeOffset? VisitFor(string id) => snapshot?.PodcastVisits is { } visits && visits.TryGetValue(id, out var date) ? date : null;

    private IReadOnlyList<MediaItem> VisibleItems()
    {
        var query = search.Text?.Trim() ?? "";
        var filtered = items.Where(item => (podcast is null && !favoritesOnly && !continuing ||
            item.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)) &&
            (showAll || item.HasEpisodes || !heard.ContainsKey(item.Id)));
        var dated = dateOrder.IsVisible && dateOrder.SelectedIndex is 0 or 1
            ? PodcastFeed.OrderByDate(filtered, dateOrder.SelectedIndex == 0) : filtered.ToArray();
        return PodcastFeed.OrderByUnplayed(dated, feedEpisodes, heard);
    }

    private bool IsTrackedEpisode(MediaItem item) => !item.HasEpisodes &&
        (favoriteIds.Contains(item.Id) || favoriteIds.Contains(item.ParentSeriesId));

    private static Button IconButton(string glyph, string action, Func<Task> clicked)
    {
        var button = Ui.Button("", clicked);
        button.WidthRequest = 44;
        button.MinimumHeightRequest = 44;
        button.Padding = 8;
        SetAction(button, glyph, action);
        return button;
    }

    private static void SetAction(Button button, string glyph, string action)
    {
        button.ImageSource = Ui.FontIconSource(glyph, 19);
        SemanticProperties.SetDescription(button, LanguageService.Text(action));
        ToolTipProperties.SetText(button, LanguageService.Text(action));
    }
}
