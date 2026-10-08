using PSiptv.Core;
using PSiptv.Services;

namespace PSiptv.Views;

public sealed class PodcastsPage : LocalizedPage
{
    private readonly MediaItem? podcast;
    private readonly bool favoritesOnly;
    private readonly SearchBar search = new() { Placeholder = "Pesquisar podcasts" };
    private bool showAll;
    private readonly Button showAllButton;
    private readonly Button markAllButton;
    private readonly Picker dateOrder = new()
    {
        Title = "Ordenar por data", ItemsSource = new[] { "Mais recentes", "Mais antigos", "Ordem do feed" }, SelectedIndex = 0
    };
    private readonly Label status = Ui.Text("", 14, true);
    private readonly CollectionView list = new() { SelectionMode = SelectionMode.Single };
    private IReadOnlyList<MediaItem> items = [];
    private IReadOnlyDictionary<string, PodcastHeardEntry> heard = new Dictionary<string, PodcastHeardEntry>();
    private CancellationTokenSource? loading;

    public PodcastsPage(MediaItem? podcast = null, bool favoritesOnly = false)
    {
        this.podcast = podcast;
        this.favoritesOnly = favoritesOnly;
        Ui.Page(this, podcast?.Name ?? (favoritesOnly ? "Podcasts favoritos" : "Podcasts"));
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
            row.Add(Ui.Stack(name, date, state)); row.Add(toggle, 1); row.Add(download, 2); row.Add(favorite, 3);
            row.BindingContextChanged += (_, _) =>
            {
                if (row.BindingContext is not MediaItem item) return;
                var tracked = IsTrackedEpisode(item);
                toggle.IsVisible = tracked;
                SetAction(toggle, heard.ContainsKey(item.Id) ? FaIcons.RotateLeft : FaIcons.Check,
                    heard.ContainsKey(item.Id) ? "Marcar como por ouvir" : "Marcar como ouvido");
                download.IsVisible = !item.HasEpisodes && OfflineDownloadPolicy.CanDownload(item);
                state.Text = item.HasEpisodes ? item.Category : tracked ?
                    heard.ContainsKey(item.Id) ? "✓ Ouvido" : "Por ouvir" : item.Category;
                date.IsVisible = !item.HasEpisodes;
                date.Text = item.PublishedAt is { } published
                    ? LanguageService.Format("Publicado em {0}", published.ToLocalTime().ToString("dd/MM/yyyy"))
                    : LanguageService.Text("Data indisponível");
            };
            return Ui.Card(row);
        });
        list.SelectionChanged += async (_, e) =>
        {
            if (e.CurrentSelection.FirstOrDefault() is not MediaItem item) return;
            list.SelectedItem = null;
            try { await PodcastService.OpenAsync(this, item); }
            catch (Exception ex) { await Ui.ErrorAsync(this, ex); }
        };
        search.SearchButtonPressed += async (_, _) => await LoadAsync();
        search.TextChanged += (_, _) => { if (podcast is not null || favoritesOnly) Show(); };
        dateOrder.SelectedIndexChanged += (_, _) => Show();
        var actions = new Grid { ColumnSpacing = 8, ColumnDefinitions = [new(GridLength.Star), new(GridLength.Star)] };
        actions.Add(Ui.Button("Pesquisar", LoadAsync));
        actions.Add(Ui.Button("Favoritos", () => Navigation.PushAsync(new PodcastsPage(favoritesOnly: true))), 1);
        actions.IsVisible = podcast is null && !favoritesOnly;
        var subscription = podcast is null ? new FavoriteButton() { IsVisible = false } : new FavoriteButton { BindingContext = podcast };
        Content = new Grid { Padding = 16, RowSpacing = 10,
            RowDefinitions = [new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Star)] };
        var grid = (Grid)Content;
        var heading = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)] };
        heading.Add(Ui.Text(Title ?? "Podcasts", 24)); heading.Add(subscription, 1);
        grid.Add(Ui.Stack(heading, status));
        var episodeFilters = new Grid { ColumnSpacing = 8,
            ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto), new(GridLength.Auto)] };
        episodeFilters.Add(dateOrder); episodeFilters.Add(showAllButton, 1); episodeFilters.Add(markAllButton, 2);
        episodeFilters.IsVisible = podcast is not null || favoritesOnly;
        grid.Add(search, 0, 1); grid.Add(Ui.Stack(actions, episodeFilters), 0, 2); grid.Add(list, 0, 3);
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

    private async Task LoadAsync()
    {
        loading?.Cancel(); loading?.Dispose(); loading = new();
        var token = loading.Token;
        var session = AppServices.SessionVersion;
        status.Text = "A carregar podcasts…";
        try
        {
            await FavoritesService.LoadAsync();
            var loaded = podcast is not null ? await PodcastService.EpisodesAsync(podcast, token) :
                favoritesOnly ? FavoritesService.Items.Where(PodcastFeed.IsPodcast).ToArray() :
                await PodcastService.SearchAsync(search.Text ?? "", token);
            if (token.IsCancellationRequested || session != AppServices.SessionVersion) return;
            items = loaded;
            await RefreshAsync();
            status.Text = $"{items.Count} · {UserProfileService.Active.Name}";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) { status.Text = "Não foi possível carregar os podcasts. Tente novamente."; System.Diagnostics.Debug.WriteLine(ex); }
    }
    private async Task RefreshAsync()
    {
        heard = (await FavoritesService.SnapshotAsync())?.HeardEpisodes ?? new Dictionary<string, PodcastHeardEntry>();
        if (favoritesOnly) items = FavoritesService.Items.Where(PodcastFeed.IsPodcast).ToArray();
        Show();
    }
    private void Show()
    {
        var visible = VisibleItems();
        list.ItemsSource = visible;
        markAllButton.IsEnabled = visible.Any(item => IsTrackedEpisode(item) && !heard.ContainsKey(item.Id));
    }

    private IReadOnlyList<MediaItem> VisibleItems()
    {
        var query = search.Text?.Trim() ?? "";
        var filtered = items.Where(item => (podcast is null && !favoritesOnly ||
            item.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)) &&
            (showAll || item.HasEpisodes || !heard.ContainsKey(item.Id)));
        return dateOrder.IsVisible && dateOrder.SelectedIndex is 0 or 1
            ? PodcastFeed.OrderByDate(filtered, dateOrder.SelectedIndex == 0) : filtered.ToArray();
    }

    private static bool IsTrackedEpisode(MediaItem item) => !item.HasEpisodes &&
        (FavoritesService.Contains(item) || FavoritesService.Items.Any(e => e.Id == item.ParentSeriesId));

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
