using PSiptv.Core;
using PSiptv.Services;

namespace PSiptv.Views;

public sealed class PodcastsPage : LocalizedPage
{
    private readonly MediaItem? podcast;
    private readonly bool favoritesOnly;
    private readonly SearchBar search = new() { Placeholder = "Pesquisar podcasts" };
    private readonly Picker filter = new() { ItemsSource = new[] { "Todos", "Por ouvir", "Ouvidos" }, SelectedIndex = 0 };
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
        filter.SetDynamicResource(Picker.TextColorProperty, "Ink");
        filter.IsVisible = podcast is not null;
        list.EmptyView = Ui.Text("Sem podcasts para mostrar.", 16, true);
        list.ItemTemplate = new DataTemplate(() =>
        {
            var name = Ui.Text("", 16); name.MaxLines = 3;
            name.SetBinding(Label.TextProperty, nameof(MediaItem.Name));
            var state = Ui.Text("", 12, true);
            var favorite = new FavoriteButton();
            var toggle = Ui.Button("", async () =>
            {
                if (name.BindingContext is not MediaItem episode) return;
                await FavoritesService.SetHeardAsync(episode, !heard.ContainsKey(episode.Id));
                await RefreshAsync();
            });
            toggle.MinimumHeightRequest = 44;
            var row = new Grid { Padding = 8, ColumnSpacing = 8,
                ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto), new(GridLength.Auto)] };
            row.Add(Ui.Stack(name, state)); row.Add(toggle, 1); row.Add(favorite, 2);
            row.BindingContextChanged += (_, _) =>
            {
                if (row.BindingContext is not MediaItem item) return;
                var tracked = !item.HasEpisodes && (FavoritesService.Contains(item) ||
                    FavoritesService.Items.Any(e => e.Id == item.ParentSeriesId));
                toggle.IsVisible = tracked;
                toggle.Text = heard.ContainsKey(item.Id) ? "Por ouvir" : "Marcar ouvido";
                state.Text = item.HasEpisodes ? item.Category : tracked ?
                    heard.ContainsKey(item.Id) ? "✓ Ouvido" : "Por ouvir" : item.Category;
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
        filter.SelectedIndexChanged += (_, _) => Show();
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
        grid.Add(search, 0, 1); grid.Add(Ui.Stack(actions, filter), 0, 2); grid.Add(list, 0, 3);
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
        var query = search.Text?.Trim() ?? "";
        list.ItemsSource = items.Where(item => (podcast is null && !favoritesOnly ||
            item.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)) &&
            (filter.SelectedIndex switch { 1 => !heard.ContainsKey(item.Id), 2 => heard.ContainsKey(item.Id), _ => true })).ToArray();
    }
}
