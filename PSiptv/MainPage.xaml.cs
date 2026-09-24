using PSiptv.Core;
using PSiptv.Services;
using PSiptv.Views;

namespace PSiptv;

internal enum MainSection { Home, LiveTv, MoviesAndSeries, Favorites, Browser }
internal enum ContentFilterMode { All, Movies, Series }

public partial class MainPage : ContentPage
{
    private readonly Label accountLabel = Ui.Text("Nenhuma lista aberta", 13, true);
    private readonly Label status = Ui.Text("Adicione uma conta Xtream ou uma lista M3U para começar.", 14, true);
    private readonly Button categories;
    private readonly CollectionView items = Ui.MediaList();
    private readonly LiveTvView liveTv = new();
    private readonly SmartHomeView home = new();
    private readonly BrowserView browser = new() { IsVisible = false };
    private readonly ActivityIndicator spinner = new() { IsVisible = false };
    private readonly Dictionary<MainSection, Button> tabs = [];
    private readonly Dictionary<ContentFilterMode, Button> contentFilterButtons = [];
    private readonly Grid contentFilters;
    private IReadOnlyList<MediaItem> catalog = [];
    private CancellationTokenSource? loading;
    private MainSection section = InitialMainSection();
    private ContentFilterMode contentFilter;
    private bool guide;
    private bool choosing;
    private bool startupAttempted;
    private bool catalogLoaded;
    private bool contentCatalogPrepared;
    private Action? adaptLayout;
    private string? selectedCategory;
    private IReadOnlyList<string> categoryChoices = [];
    private IReadOnlyList<CatalogCategory> liveProviderCategories = [];
    private readonly Dictionary<string, IReadOnlyList<MediaItem>> liveCategoryCatalogs = [];
    private bool liveCatalogComplete;
    private bool liveAllRequested;
    private IReadOnlyList<MediaItem> visibleRemoteChannels = [];

    public MainPage()
    {
        InitializeComponent();
        Ui.Page(this, "PSiptv");
        NavigationPage.SetHasNavigationBar(this, false);
        categories = Ui.Button("Todas as categorias", ChooseCategoryAsync);
        categories.HorizontalOptions = LayoutOptions.Fill;
        spinner.SetDynamicResource(ActivityIndicator.ColorProperty, "Accent");
        var topBar = new Grid
        {
            ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto)],
            ColumnSpacing = 4
        };
        var brand = Ui.Text("PSiptv", 20);
        brand.LineBreakMode = LineBreakMode.TailTruncation; brand.MaxLines = 1;
        brand.FontAttributes = FontAttributes.Bold;
        brand.VerticalOptions = LayoutOptions.Center;
        var identity = new Grid
        {
            ColumnDefinitions = [new ColumnDefinition(new GridLength(32)), new ColumnDefinition(GridLength.Star)],
            ColumnSpacing = 6
        };
        identity.Add(new Image { Source = "psiptv_mark.svg", HeightRequest = 32, WidthRequest = 32 });
        identity.Add(brand, 1);
        topBar.Add(identity);
        topBar.Add(IconButton(Ui.FontIconSource(FaIcons.Chromecast, 22, "FontAwesomeFreeBrands"), "Transmitir", () => ScreenSharingService.ChooseAsync(this, liveTv.CurrentItem, liveTv.Pause)), 1);
        topBar.Add(IconButton(Ui.FontIconSource(FaIcons.Wifi, 20), "Comando remoto", OpenLanRemoteAsync), 2);
        topBar.Add(IconButton("nav_search.png", "Pesquisa global", OpenGlobalSearchAsync), 3);
        topBar.Add(IconButton("nav_settings.png", "Configurações",
            () => Navigation.PushAsync(new ProfilePage(ChooseAccountAsync, SwitchUserProfileAsync))), 4);
        var nav = new Grid { Padding = new Thickness(8, 6, 8, 8) };
        nav.SetDynamicResource(BackgroundColorProperty, "Surface");
        var tabColumn = 0;
        if (!Ui.IsTelevision)
        {
            nav.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            AddTab(nav, tabColumn++, MainSection.Home, "Início", "nav_home.png");
        }
        foreach (var tab in new[]
        {
            (MainSection.LiveTv, "TV ao Vivo", "nav_live.png"),
            (MainSection.MoviesAndSeries, "Filmes/Séries", "nav_movies.png"),
            (MainSection.Favorites, "Favoritos", "nav_favorites.png"),
            (MainSection.Browser, "Browser", "nav_browser.png")
        })
        {
            nav.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            AddTab(nav, tabColumn++, tab.Item1, tab.Item2, tab.Item3);
        }
        UpdateTabs();
        var header = Ui.Stack(topBar, accountLabel, status);
        contentFilters = new Grid
        {
            ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star)],
            ColumnSpacing = 6,
            IsVisible = false
        };
        AddContentFilter(contentFilters, 0, ContentFilterMode.All, "Todos");
        AddContentFilter(contentFilters, 1, ContentFilterMode.Movies, "Filmes");
        AddContentFilter(contentFilters, 2, ContentFilterMode.Series, "Séries");
        UpdateContentFilters();
        var filters = new Grid { ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(new GridLength(54))] };
        filters.Add(categories); filters.Add(Ui.Button("↻", LoadAsync), 1);
        var grid = new Grid { Padding = 12, RowSpacing = 8,
            SafeAreaEdges = new SafeAreaEdges(SafeAreaRegions.None),
            RowDefinitions = [new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star)] };
        grid.Add(header); grid.Add(contentFilters, 0, 1); grid.Add(filters, 0, 2); grid.Add(items, 0, 3);
        grid.Add(home, 0, 3);
        grid.Add(liveTv, 0, 3);
        home.IsVisible = IsHomeTab;
        liveTv.IsVisible = IsLiveTvTab || IsFavoritesTab;
        items.IsVisible = IsContentTab;
        grid.Add(browser, 0, 3);
        browser.IsVisible = IsBrowserTab;
        var root = new Grid
        {
            RowDefinitions = [new RowDefinition(GridLength.Star), new RowDefinition(GridLength.Auto)],
            SafeAreaEdges = new SafeAreaEdges(SafeAreaRegions.Container)
        };
        root.Add(grid);
        root.Add(nav, 0, 1);
        var loadingOverlay = new Grid
        {
            IsVisible = false,
            BackgroundColor = Color.FromArgb("#990F172C"),
            InputTransparent = false,
            ZIndex = 100
        };
        loadingOverlay.Add(spinner);
        root.Add(loadingOverlay);
        Grid.SetRowSpan(loadingOverlay, 2);
        spinner.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(ActivityIndicator.IsVisible)) return;
            loadingOverlay.IsVisible = spinner.IsVisible;
            grid.IsEnabled = !spinner.IsVisible;
            nav.IsEnabled = !spinner.IsVisible;
        };
        Content = root;
        void AdaptLayout()
        {
            var fullscreen = liveTv.IsVideoOnly;
            var viewport = Ui.Viewport(this);
            var landscape = viewport.Width > viewport.Height;
            var compactLandscape = !Ui.IsTelevision && landscape && viewport.Height < 600;
            header.IsVisible = !fullscreen && !IsBrowserTab;
            filters.IsVisible = !fullscreen && !IsBrowserTab && !IsFavoritesTab && !IsHomeTab;
            contentFilters.IsVisible = !fullscreen && IsContentTab;
            nav.IsVisible = !fullscreen;
            root.SafeAreaEdges = new SafeAreaEdges(fullscreen ? SafeAreaRegions.None : SafeAreaRegions.Container);
            accountLabel.IsVisible = !compactLandscape && !Ui.IsTelevision;
            status.IsVisible = !compactLandscape;
            grid.Padding = fullscreen ? 0 : IsBrowserTab ? new Thickness(6) : Ui.IsTelevision ? new Thickness(28, 12) : compactLandscape ? new Thickness(12, 4) : new Thickness(16, 12);
            nav.Padding = Ui.IsTelevision ? new Thickness(28, 4, 28, 8) : new Thickness(8, 6, 8, 8);
            grid.RowSpacing = fullscreen ? 0 : 8;
            Ui.UpdateColumns(items, viewport.Width - grid.Padding.HorizontalThickness);
            foreach (var button in tabs.Values)
            {
                button.HeightRequest = Ui.IsTelevision ? 50 : compactLandscape ? 44 : 68;
                button.Padding = new Thickness(4, Ui.IsTelevision || compactLandscape ? 2 : 8);
                button.ContentLayout = new Button.ButtonContentLayout(
                    compactLandscape || Ui.IsTelevision ? Button.ButtonContentLayout.ImagePosition.Left : Button.ButtonContentLayout.ImagePosition.Top, 4);
            }
        }
        adaptLayout = AdaptLayout;
        SizeChanged += (_, _) => AdaptLayout();
        Loaded += (_, _) =>
        {
            AdaptLayout();
            if (Ui.IsTelevision) Dispatcher.Dispatch(() => tabs[section].Focus());
        };
        liveTv.FullscreenChanged += _ => AdaptLayout();
        items.SelectionChanged += OnSelected;
        AppServices.Locked += OnLocked;
        Loaded += OnFirstLoaded;
        CatalogOptionsService.Changed += () => { UpdateCategories(); Filter(); };
        FavoritesService.Changed += OnFavoritesChanged;
        DvrService.Changed += OnDvrChanged;
        CatalogUpdateService.Completed += OnAutomaticCatalogUpdated;
    }

    private static Button IconButton(ImageSource icon, string description, Func<Task> action)
    {
        var button = Ui.Button("", action);
        button.ImageSource = icon;
        button.BackgroundColor = Colors.Transparent;
        button.WidthRequest = 44;
        button.Padding = 10;
        SemanticProperties.SetDescription(button, LanguageService.Text(description));
        ToolTipProperties.SetText(button, LanguageService.Text(description));
        return button;
    }

    private async Task OpenGlobalSearchAsync()
    {
        if (AppServices.ActiveAccount is null) await ChooseAccountAsync();
        if (AppServices.ActiveAccount is { } account) await Navigation.PushAsync(new GlobalSearchPage(account));
    }

    private async Task OpenLanRemoteAsync()
    {
        if (AppServices.ActiveAccount is null) await ChooseAccountAsync();
        if (AppServices.ActiveAccount is not null)
        {
            try { await FavoritesService.LoadAsync(); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
            await Navigation.PushAsync(new LanRemotePage());
        }
    }

    private void AddTab(Grid bar, int column, MainSection target, string title, string icon)
    {
        var button = Ui.Button(title, () => SwitchAsync(target, false));
        if (target == MainSection.MoviesAndSeries)
            button.Text = button.Text.Replace(" e ", "\ne ").Replace(" & ", "\n& ");
        button.ImageSource = icon;
        button.ContentLayout = new Button.ButtonContentLayout(Button.ButtonContentLayout.ImagePosition.Top, 4);
        button.BackgroundColor = Colors.Transparent;
        button.CornerRadius = 0;
        button.Padding = new Thickness(4, 8);
        button.FontSize = Ui.IsTelevision ? 15 : 11;
        button.HeightRequest = Ui.IsTelevision ? 50 : 68;
        button.HorizontalOptions = LayoutOptions.Fill;
        button.VerticalOptions = LayoutOptions.Fill;
        button.LineBreakMode = LineBreakMode.NoWrap;
        button.TextTransform = TextTransform.None;
        tabs.Add(target, button);
        bar.Add(button, column);
    }

    private void AddContentFilter(Grid bar, int column, ContentFilterMode filter, string title)
    {
        var button = Ui.Button(title, () => SetContentFilterAsync(filter));
        button.MinimumHeightRequest = 42;
        button.Padding = new Thickness(6, 4);
        contentFilterButtons.Add(filter, button);
        bar.Add(button, column);
    }

    private void UpdateTabs()
    {
        foreach (var (target, button) in tabs)
        {
            var selected = target == section;
            button.SetDynamicResource(Button.TextColorProperty, selected ? "Ink" : "Muted");
            button.FontAttributes = selected ? FontAttributes.Bold : FontAttributes.None;
            button.Opacity = selected ? 1 : 0.55;
            SemanticProperties.SetDescription(button, button.Text + (selected ? ", " + LanguageService.Text("selecionado") : ""));
        }
    }

    private async Task ChooseAccountAsync()
    {
        if (choosing) return;
        choosing = true;
        try
        {
            var accounts = await AppServices.Accounts.LoadAsync();
            if (accounts.Count == 0) { await Navigation.PushAsync(new AccountPage()); return; }
            var labels = accounts.Select((a, i) => $"{i + 1}. {a.Name}{(a.IsProtected ? " · PIN" : "")}").ToArray();
            var selected = await LanguageService.ActionSheetAsync(this, "Abrir lista", "Cancelar", null, labels);
            var index = Array.IndexOf(labels, selected);
            if (index < 0) return;
            var account = accounts[index];
            await OpenAccountAsync(account);
        }
        finally { choosing = false; }
    }

    private async void OnFirstLoaded(object? sender, EventArgs e)
    {
        if (startupAttempted) return;
        startupAttempted = true;
        var nonBlockingStartup = AppOptions.CatalogUpdateInBackground;
        spinner.IsRunning = !nonBlockingStartup;
        spinner.IsVisible = !nonBlockingStartup;
        status.Text = LanguageService.Text("A carregar canais memorizados…");
        try
        {
            await UserProfileService.LoadAsync();
            _ = ReminderService.StartAsync();
            await DvrService.EnsureRecordingsDirectoryAccessAsync();
            _ = DvrService.StartAsync();
            await ResumeAsync();
            await OpenPendingNotificationAsync();
            _ = CatalogUpdateService.TryRunDueAsync(true);
        }
        finally
        {
            if (loading is null)
            {
                spinner.IsRunning = false;
                spinner.IsVisible = false;
            }

            if (AppServices.ActiveAccount is null)
                status.Text = LanguageService.Text("Adicione uma conta Xtream ou uma lista M3U para começar.");
        }
#if ANDROID
        _ = AndroidAppUpdateService.CheckOnStartupAsync(this);
#endif
    }

    private void OnAutomaticCatalogUpdated(CatalogUpdateResult result)
    {
        Dispatcher.Dispatch(() =>
        {
            if (AppServices.ActiveAccount?.Id != result.Account.Id || loading is not null) return;
            foreach (var (kind, updated) in result.Catalogs)
                CatalogOptionsService.Catalogs[kind] = updated;
            contentCatalogPrepared = result.Catalogs.ContainsKey(MediaKind.Movie) ||
                                     result.Catalogs.ContainsKey(MediaKind.Series);
            if (result.Catalogs.TryGetValue(MediaKind.Channel, out var channels))
                EpgService.RefreshInBackground(result.Account, channels);
            if (IsHomeTab) { _ = LoadHomeAsync(); return; }
            if (!IsBrowserTab) ShowCachedCatalog();
            status.Text = LanguageService.Format("Lista atualizada · +{0} / −{1} canais",
                result.ChannelChanges.Added, result.ChannelChanges.Removed);
        });
    }

    internal async Task ResumeAsync()
    {
        RemoteControlService.Start(ReadRemoteState, ExecuteRemoteCommandAsync);
        if (choosing || AppServices.ActiveAccount is not null) return;
        if (!Preferences.Default.Get("openLastPlaylist", true)) return;
        var lastId = Preferences.Default.Get("lastAccountId", "");
        if (lastId.Length == 0) return;
        choosing = true;
        var version = AppServices.SessionVersion;
        try
        {
            var accounts = await AppServices.Accounts.LoadAsync();
            if (version != AppServices.SessionVersion || AppServices.ActiveAccount is not null) return;
            var account = accounts.FirstOrDefault(a => a.Id == lastId);
            if (account is null) { Preferences.Default.Remove("lastAccountId"); return; }
            await OpenAccountAsync(account);
        }
        catch (Exception ex) { await Ui.ErrorAsync(this, ex); }
        finally { choosing = false; }
    }

    private async Task OpenAccountAsync(PlaylistAccount account)
    {
        var sessionVersion = AppServices.SessionVersion;
        if (!await PinPage.AuthorizeAsync(this, account)) return;
        if (sessionVersion != AppServices.SessionVersion) return;
        CancelLoading(); catalog = []; items.ItemsSource = null;
        liveProviderCategories = [];
        liveCategoryCatalogs.Clear();
        liveCatalogComplete = false;
        liveAllRequested = false;
        AppServices.Activate(account);
        UpdateAccountLabel();
        await CatalogOptionsService.LoadAsync(account.Id);
        contentFilter = ReadContentFilter(account.Id);
        UpdateContentFilters();
        contentCatalogPrepared = CatalogOptionsService.Catalogs.ContainsKey(MediaKind.Movie) ||
            CatalogOptionsService.Catalogs.ContainsKey(MediaKind.Series);
        if (CatalogOptionsService.Catalogs.TryGetValue(MediaKind.Channel, out var cachedChannels))
        {
            liveCatalogComplete = true;
            EpgService.RefreshInBackground(account, cachedChannels);
        }
        if (IsHomeTab)
        {
            if (AppOptions.CatalogUpdateInBackground) _ = LoadHomeAsync(ensureChannels: true);
            else await LoadHomeAsync(ensureChannels: true);
            return;
        }
        if (IsBrowserTab)
        {
            visibleRemoteChannels = [];
            try { await FavoritesService.LoadAsync(); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
            browser.Activate();
            return;
        }
        if (IsFavoritesTab)
        {
            ShowCachedCatalog();
            if (AppOptions.CatalogUpdateInBackground) _ = LoadFavoritesTabAsync();
            else await LoadFavoritesTabAsync();
            return;
        }
        var hasCachedCatalog = IsContentTab ? contentCatalogPrepared : HasCurrentCatalogCache;
        ShowCachedCatalog();
        if (!hasCachedCatalog && AppServices.ActiveAccount?.Id == account.Id)
        {
            if (IsContentTab && AppOptions.CatalogUpdateInBackground) _ = LoadContentInBackgroundAsync(account);
            else if (IsLiveTvTab && AppOptions.CatalogUpdateInBackground) _ = LoadLiveTvInBackgroundAsync(account);
            else await LoadAsync();
        }
    }
    private async Task SwitchAsync(MainSection selected, bool showGuide)
    {
        liveTv.Stop();
        section = selected; guide = showGuide;
        Preferences.Default.Set("mainSection", section.ToString());
        home.IsVisible = IsHomeTab;
        browser.IsVisible = IsBrowserTab;
        liveTv.IsVisible = (IsLiveTvTab || IsFavoritesTab) && !guide;
        items.IsVisible = !liveTv.IsVisible && !IsBrowserTab && !IsHomeTab;
        adaptLayout?.Invoke();
        UpdateTabs();
        if (IsHomeTab)
        {
            visibleRemoteChannels = [];
            if (AppOptions.CatalogUpdateInBackground) _ = LoadHomeAsync(ensureChannels: true);
            else await LoadHomeAsync(ensureChannels: true);
            return;
        }
        if (IsBrowserTab)
        {
            visibleRemoteChannels = [];
            browser.Activate();
            return;
        }
        if (IsFavoritesTab)
        {
            ShowCachedCatalog();
            if (AppOptions.CatalogUpdateInBackground) _ = LoadFavoritesTabAsync();
            else await LoadFavoritesTabAsync();
            return;
        }
        var hasCachedCatalog = IsContentTab ? contentCatalogPrepared : HasCurrentCatalogCache;
        ShowCachedCatalog();
        if (!hasCachedCatalog && AppServices.ActiveAccount is { } account)
        {
            if (IsContentTab && AppOptions.CatalogUpdateInBackground) _ = LoadContentInBackgroundAsync(account);
            else if (IsLiveTvTab && AppOptions.CatalogUpdateInBackground) _ = LoadLiveTvInBackgroundAsync(account);
            else await LoadAsync();
        }
    }

    private async Task SwitchUserProfileAsync(UserProfile profile)
    {
        if (profile.Id == UserProfileService.Active.Id) return;
        liveTv.Stop();
        AppServices.ActivateProfile(profile.Id);
        UpdateAccountLabel();
        if (AppServices.ActiveAccount is null) return;
        if (IsHomeTab)
        {
            if (AppOptions.CatalogUpdateInBackground) _ = LoadHomeAsync();
            else await LoadHomeAsync();
        }
        else if (IsFavoritesTab)
        {
            ShowCachedCatalog();
            if (AppOptions.CatalogUpdateInBackground) _ = LoadFavoritesTabAsync();
            else await LoadFavoritesTabAsync();
        }
        else await FavoritesService.LoadAsync();
    }

    private void UpdateAccountLabel()
    {
        accountLabel.Text = AppServices.ActiveAccount is { } account
            ? $"{account.Name} · {account.ProviderName} · {UserProfileService.Active.Avatar} {UserProfileService.Active.Name}"
            : LanguageService.Text("Nenhuma lista aberta");
    }

    private async Task LoadAsync()
    {
        CancelLoading();
        liveTv.Stop();
        if (IsHomeTab)
        {
            await LoadHomeAsync(ensureChannels: true);
            spinner.IsRunning = false;
            spinner.IsVisible = false;
            return;
        }
        if (IsBrowserTab)
        {
            browser.Activate();
            spinner.IsRunning = false;
            spinner.IsVisible = false;
            return;
        }
        var account = AppServices.ActiveAccount;
        if (account is null) { status.Text = "Abra uma lista ou adicione uma conta para começar."; spinner.IsRunning = false; spinner.IsVisible = false; return; }
        if (IsFavoritesTab)
        {
            await LoadFavoritesTabAsync();
            return;
        }
        var source = new CancellationTokenSource(); loading = source;
        spinner.IsRunning = true; spinner.IsVisible = true; status.Text = "A carregar conteúdos…";
        try
        {
            await FavoritesService.LoadAsync();
            source.Token.ThrowIfCancellationRequested();
            if (IsContentTab)
                await LoadMoviesAndSeriesAsync(account, source.Token);
            else
            {
                var previous = CatalogOptionsService.Catalogs.GetValueOrDefault(MediaKind.Channel) ?? [];
                var (loaded, complete) = await LoadLiveCatalogAsync(account, source.Token);
                source.Token.ThrowIfCancellationRequested();
                if (AppServices.ActiveAccount?.Id != account.Id) return;
                liveCatalogComplete = complete;
                if (complete)
                {
                    CatalogOptionsService.Catalogs[MediaKind.Channel] = loaded;
                    await TrySaveCatalogAsync(account.Id, MediaKind.Channel, loaded);
                }
                source.Token.ThrowIfCancellationRequested();
                if (AppServices.ActiveAccount?.Id != account.Id) return;
                EpgService.RefreshInBackground(account, loaded);
                ShowCatalog(loaded, true);
                if (complete)
                {
                    var changes = CatalogUpdatePolicy.CompareChannels(previous, loaded, account.Provider);
                    status.Text = LanguageService.Format("Lista atualizada · +{0} / −{1} canais",
                        changes.Added, changes.Removed);
                }
                else status.Text = LanguageService.Format("{0} canais · Selecione para reproduzir.", loaded.Count);
            }
        }
        catch (OperationCanceledException)
        {
            if (!source.IsCancellationRequested) status.Text = "O fornecedor demorou demasiado tempo. Use ↻ para tentar novamente.";
        }
        catch (Exception ex)
        {
            if (!source.IsCancellationRequested && IsContentTab)
            {
                contentCatalogPrepared = true;
                var available = CombinedContentCatalog();
                ShowCatalog(available, available.Count > 0);
                status.Text = available.Count > 0
                    ? LanguageService.Format("{0} conteúdos disponíveis · Um dos catálogos não foi carregado.", available.Count)
                    : LanguageService.Text("Não foi possível carregar filmes e séries. Use ↻ para tentar novamente.");
                System.Diagnostics.Debug.WriteLine(ex);
            }
            else if (!source.IsCancellationRequested)
            {
                status.Text = "Não foi possível carregar. Use ↻ para tentar novamente.";
                await Ui.ErrorAsync(this, ex);
            }
        }
        finally
        {
            if (ReferenceEquals(loading, source)) { spinner.IsRunning = false; spinner.IsVisible = false; loading = null; }
            source.Dispose();
        }
    }

    private async Task LoadMoviesAndSeriesAsync(PlaylistAccount account, CancellationToken ct)
    {
        if (account.Provider is ProviderType.M3U or ProviderType.LocalM3U)
        {
            if (IsContentTab) status.Text = LanguageService.Text("A carregar conteúdos…");
            var playlist = await AppServices.Client.LoadM3uAsync(account, ct);
            foreach (var mediaKind in new[] { MediaKind.Movie, MediaKind.Series })
            {
                var loaded = await Task.Run(
                    () => playlist.Items.Where(item => item.Kind == mediaKind).ToArray(), ct);
                CatalogOptionsService.Catalogs[mediaKind] = loaded;
                await TrySaveCatalogAsync(account.Id, mediaKind, loaded);
            }
            ct.ThrowIfCancellationRequested();
            if (AppServices.ActiveAccount?.Id == account.Id && IsContentTab)
                ShowCatalog(CombinedContentCatalog(), true);
            contentCatalogPrepared = true;
            return;
        }

        Exception? failure = null;
        foreach (var mediaKind in new[] { MediaKind.Movie, MediaKind.Series })
        {
            try
            {
                if (IsContentTab)
                    status.Text = LanguageService.Format("A carregar {0}…",
                        LanguageService.Text(mediaKind == MediaKind.Movie ? "filmes" : "séries"));
                using var request = CancellationTokenSource.CreateLinkedTokenSource(ct);
                request.CancelAfter(TimeSpan.FromMinutes(2));
                var loaded = await AppServices.Client.GetCatalogAsync(account, mediaKind, request.Token);
                ct.ThrowIfCancellationRequested();
                if (AppServices.ActiveAccount?.Id != account.Id) return;
                CatalogOptionsService.Catalogs[mediaKind] = loaded;
                await TrySaveCatalogAsync(account.Id, mediaKind, loaded);
                ct.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                failure = new TimeoutException($"O catálogo de {mediaKind} demorou demasiado tempo.");
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { failure = ex; }
        }

        if (AppServices.ActiveAccount?.Id != account.Id) return;
        var combined = CombinedContentCatalog();
        if (combined.Count == 0 && failure is not null) throw failure;
        contentCatalogPrepared = true;
        if (IsContentTab)
        {
            ShowCatalog(combined, true);
            if (failure is not null)
                status.Text = LanguageService.Format("{0} conteúdos disponíveis · Um dos catálogos não foi carregado.", combined.Count);
        }
    }

    private static async Task TrySaveCatalogAsync(string accountId, MediaKind mediaKind, IReadOnlyList<MediaItem> loaded)
    {
        try { await CatalogCacheService.SaveAsync(accountId, mediaKind, loaded); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        { System.Diagnostics.Debug.WriteLine(ex); }
    }

    private void UpdateContentFilters()
    {
        foreach (var (filter, button) in contentFilterButtons)
        {
            var selected = filter == contentFilter;
            button.TextColor = selected ? Color.FromArgb("#071520") : Colors.White;
            button.SetDynamicResource(Button.BackgroundColorProperty, selected ? "Accent" : "Surface");
            button.FontAttributes = selected ? FontAttributes.Bold : FontAttributes.None;
        }
    }

    private async Task SetContentFilterAsync(ContentFilterMode filter)
    {
        if (contentFilter == filter) return;
        contentFilter = filter;
        if (AppServices.ActiveAccount is { } account)
            Preferences.Default.Set(ContentFilterPreferenceKey(account.Id), contentFilter.ToString());
        selectedCategory = LoadSelectedCategory();
        UpdateContentFilters();
        UpdateCategories();
        Filter();
        if (IsContentTab && !contentCatalogPrepared && catalog.Count == 0 && loading is null && AppServices.ActiveAccount is not null)
        {
            if (AppOptions.CatalogUpdateInBackground) _ = LoadContentInBackgroundAsync(AppServices.ActiveAccount);
            else await LoadAsync();
        }
    }

    private async Task LoadContentInBackgroundAsync(PlaylistAccount account)
    {
        if (loading is not null || AppServices.ActiveAccount?.Id != account.Id) return;
        var source = new CancellationTokenSource();
        loading = source;
        status.Text = LanguageService.Text("A atualizar filmes e séries em segundo plano…");
        try
        {
            try { await FavoritesService.LoadAsync(); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
            source.Token.ThrowIfCancellationRequested();
            await LoadMoviesAndSeriesAsync(account, source.Token);
            if (AppServices.ActiveAccount?.Id == account.Id && IsContentTab)
                status.Text = LanguageService.Format("{0} conteúdos disponíveis.", CombinedContentCatalog().Count);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
            if (AppServices.ActiveAccount?.Id == account.Id && IsContentTab)
            {
                contentCatalogPrepared = true;
                var available = CombinedContentCatalog();
                ShowCatalog(available, available.Count > 0);
                status.Text = available.Count > 0
                    ? LanguageService.Format("{0} conteúdos disponíveis · Um dos catálogos não foi carregado.", available.Count)
                    : LanguageService.Text("Não foi possível carregar filmes e séries. Use ↻ para tentar novamente.");
            }
        }
        finally
        {
            if (ReferenceEquals(loading, source)) loading = null;
            source.Dispose();
        }
    }

    private async Task LoadLiveTvInBackgroundAsync(PlaylistAccount account)
    {
        if (loading is not null || AppServices.ActiveAccount?.Id != account.Id) return;
        var source = new CancellationTokenSource();
        loading = source;
        status.Text = LanguageService.Text("A atualizar canais em segundo plano…");
        try
        {
            try { await FavoritesService.LoadAsync(); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
            source.Token.ThrowIfCancellationRequested();

            var previous = CatalogOptionsService.Catalogs.GetValueOrDefault(MediaKind.Channel) ?? [];
            var (loaded, complete) = await LoadLiveCatalogAsync(account, source.Token);
            source.Token.ThrowIfCancellationRequested();
            if (AppServices.ActiveAccount?.Id != account.Id) return;

            liveCatalogComplete = complete;
            if (complete)
            {
                CatalogOptionsService.Catalogs[MediaKind.Channel] = loaded;
                await TrySaveCatalogAsync(account.Id, MediaKind.Channel, loaded);
            }
            source.Token.ThrowIfCancellationRequested();
            if (AppServices.ActiveAccount?.Id != account.Id) return;

            EpgService.RefreshInBackground(account, loaded);
            if (!IsLiveTvTab) return;
            ShowCatalog(loaded, true);
            if (complete)
            {
                var changes = CatalogUpdatePolicy.CompareChannels(previous, loaded, account.Provider);
                status.Text = LanguageService.Format("Lista atualizada · +{0} / −{1} canais",
                    changes.Added, changes.Removed);
            }
            else status.Text = LanguageService.Format("{0} canais · Selecione para reproduzir.", loaded.Count);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
            if (!source.IsCancellationRequested && AppServices.ActiveAccount?.Id == account.Id && IsLiveTvTab)
                status.Text = ex is TimeoutException ? ex.Message : LanguageService.Text("Não foi possível carregar. Use ↻ para tentar novamente.");
        }
        finally
        {
            if (ReferenceEquals(loading, source)) loading = null;
            source.Dispose();
        }
    }

    private async Task<(IReadOnlyList<MediaItem> Items, bool Complete)> LoadLiveCatalogAsync(
        PlaylistAccount account, CancellationToken token)
    {
        if (account.Provider != ProviderType.Xtream)
            return (await AppServices.Client.GetCatalogAsync(account, MediaKind.Channel, token), true);

        IReadOnlyList<CatalogCategory> providerCategories;
        using (var categoryRequest = CancellationTokenSource.CreateLinkedTokenSource(token))
        {
            categoryRequest.CancelAfter(TimeSpan.FromSeconds(30));
            try
            {
                status.Text = LanguageService.Text("A carregar categorias…");
                providerCategories = await AppServices.Client.GetCategoriesAsync(
                    account, MediaKind.Channel, categoryRequest.Token);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                throw new TimeoutException("O fornecedor não devolveu as categorias em 30 segundos.");
            }
        }

        token.ThrowIfCancellationRequested();
        if (AppServices.ActiveAccount?.Id != account.Id) throw new OperationCanceledException(token);
        liveProviderCategories = providerCategories;
        UpdateCategories();

        var requestedName = LoadSelectedCategory();
        var category = liveAllRequested ? null :
            providerCategories.FirstOrDefault(item => item.Name == requestedName) ?? providerCategories.FirstOrDefault();
        if (category is not null)
        {
            selectedCategory = category.Name;
            SaveSelectedCategory(selectedCategory);
            categories.Text = selectedCategory;
            UpdateCategories();
            if (liveCategoryCatalogs.TryGetValue(category.Name, out var saved)) return (saved, false);

            using var channelRequest = CancellationTokenSource.CreateLinkedTokenSource(token);
            channelRequest.CancelAfter(TimeSpan.FromSeconds(60));
            try
            {
                status.Text = LanguageService.Format("A carregar {0}…", category.Name);
                var loaded = await AppServices.Client.GetCatalogAsync(
                    account, MediaKind.Channel, category, channelRequest.Token);
                liveCategoryCatalogs[category.Name] = loaded;
                return (loaded, false);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                throw new TimeoutException($"O fornecedor não devolveu os canais de «{category.Name}» em 60 segundos.");
            }
        }

        using var allChannelsRequest = CancellationTokenSource.CreateLinkedTokenSource(token);
        allChannelsRequest.CancelAfter(TimeSpan.FromMinutes(2));
        try
        {
            status.Text = LanguageService.Text("A carregar todos os canais…");
            var loaded = await AppServices.Client.GetCatalogAsync(
                account, MediaKind.Channel, providerCategories, allChannelsRequest.Token);
            return (loaded, true);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new TimeoutException("O fornecedor não devolveu todos os canais em 2 minutos. Escolha uma categoria para carregar apenas os respetivos canais.");
        }
    }

    private async Task LoadXtreamCategoryAsync(PlaylistAccount account, CatalogCategory category)
    {
        CancelLoading();
        var source = new CancellationTokenSource();
        loading = source;
        spinner.IsRunning = true;
        spinner.IsVisible = true;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(source.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        try
        {
            status.Text = LanguageService.Format("A carregar {0}…", category.Name);
            var loaded = await AppServices.Client.GetCatalogAsync(account, MediaKind.Channel, category, timeout.Token);
            source.Token.ThrowIfCancellationRequested();
            if (AppServices.ActiveAccount?.Id != account.Id || !IsLiveTvTab) return;
            liveAllRequested = false;
            liveCatalogComplete = false;
            liveCategoryCatalogs[category.Name] = loaded;
            ShowCatalog(loaded, true);
        }
        catch (OperationCanceledException) when (!source.IsCancellationRequested)
        {
            status.Text = $"O fornecedor não devolveu os canais de «{category.Name}» em 60 segundos.";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            status.Text = LanguageService.Text("Não foi possível carregar. Use ↻ para tentar novamente.");
            System.Diagnostics.Debug.WriteLine(ex);
        }
        finally
        {
            if (ReferenceEquals(loading, source))
            {
                loading = null;
                spinner.IsRunning = false;
                spinner.IsVisible = false;
            }
            source.Dispose();
        }
    }

    private void ShowCachedCatalog()
    {
        CancelLoading();
        spinner.IsRunning = false; spinner.IsVisible = false;
        if (IsFavoritesTab)
        {
            ShowCatalog(FavoritesService.Items.Where(item => item.Kind == MediaKind.Channel).ToArray(), true);
            return;
        }
        if (IsContentTab)
        {
            ShowCatalog(CombinedContentCatalog(), contentCatalogPrepared || HasCurrentCatalogCache);
            return;
        }
        var loaded = CatalogOptionsService.Catalogs.TryGetValue(MediaKind.Channel, out var saved);
        liveCatalogComplete = loaded;
        ShowCatalog(saved ?? [], loaded);
    }

    private void CancelLoading()
    {
        var pending = loading;
        loading = null;
        pending?.Cancel();
    }

    private void ShowCatalog(IReadOnlyList<MediaItem> content, bool loaded)
    {
        catalog = content;
        catalogLoaded = loaded;
        selectedCategory = IsFavoritesTab ? null : LoadSelectedCategory();
        UpdateCategories();
        if (AppServices.ActiveAccount is null)
            status.Text = "Abra uma lista ou adicione uma conta para começar.";
        else if (!loaded)
            status.Text = "Use ↻ para carregar os conteúdos.";
        else
            status.Text = IsFavoritesTab
                ? LanguageService.Format("{0} canais favoritos · Selecione para reproduzir.", catalog.Count)
                : guide
                    ? LanguageService.Format("{0} canais · Escolha um canal para consultar a programação.", catalog.Count)
                    : LanguageService.Format("{0} conteúdos · Selecione para reproduzir.", catalog.Count);
        Filter();
    }

    private void Filter()
    {
        const string query = "";
        IReadOnlyList<MediaItem> filtered;
        if (IsContentTab)
        {
            filtered = SelectedContentKind is { } selectedKind
                ? CatalogOptionsService.Current.Filter(catalog, selectedKind, selectedCategory, query)
                : CatalogOptionsService.Current.Filter(catalog, MediaKind.Movie, selectedCategory, query)
                    .Concat(CatalogOptionsService.Current.Filter(catalog, MediaKind.Series, selectedCategory, query)).ToArray();
        }
        else
            filtered = CatalogOptionsService.Current.Filter(catalog, MediaKind.Channel, selectedCategory, query);
        visibleRemoteChannels = (IsLiveTvTab || IsFavoritesTab) && !guide
            ? filtered.Where(item => item.Kind == MediaKind.Channel).ToArray()
            : [];
        items.ItemsSource = filtered;
        if ((IsLiveTvTab || IsFavoritesTab) && !guide)
            liveTv.SetChannels(filtered, selectedCategory ?? LanguageService.Text("Todas as categorias"));
    }

    private void UpdateCategories()
    {
        IEnumerable<string> availableCategories = IsContentTab
            ? SelectedContentKind is { } selectedKind
                ? CatalogOptionsService.Current.Categories(catalog, selectedKind)
                : CatalogOptionsService.Current.Categories(catalog, MediaKind.Movie)
                    .Concat(CatalogOptionsService.Current.Categories(catalog, MediaKind.Series))
                    .Distinct(StringComparer.CurrentCultureIgnoreCase)
                    .OrderBy(value => value, StringComparer.CurrentCultureIgnoreCase)
            : CatalogOptionsService.Current.Categories(catalog, MediaKind.Channel)
                .Concat(IsLiveTvTab ? liveProviderCategories.Select(category => category.Name) : [])
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .OrderBy(value => value, StringComparer.CurrentCultureIgnoreCase);
        categoryChoices = new[] { LanguageService.Text("Todas as categorias") }.Concat(availableCategories).ToArray();
        if (catalogLoaded && selectedCategory is not null && !categoryChoices.Contains(selectedCategory))
        {
            selectedCategory = null;
            SaveSelectedCategory(null);
        }
        categories.Text = selectedCategory ?? LanguageService.Text("Todas as categorias");
    }

    private async Task ChooseCategoryAsync()
    {
        var selected = await CategoryPickerPage.ChooseAsync(this, "Escolher categoria", categoryChoices,
            selectedCategory ?? LanguageService.Text("Todas as categorias"));
        if (selected is null) return;
        await SelectCategoryAsync(selected == LanguageService.Text("Todas as categorias") ? null : selected);
    }

    private async Task SelectCategoryAsync(string? value)
    {
        selectedCategory = value;
        liveAllRequested = IsLiveTvTab && selectedCategory is null;
        SaveSelectedCategory(selectedCategory);
        categories.Text = selectedCategory ?? LanguageService.Text("Todas as categorias");
        Filter();
        if (!IsLiveTvTab || AppServices.ActiveAccount is not { Provider: ProviderType.Xtream } account) return;
        if (selectedCategory is null)
        {
            if (!liveCatalogComplete) await LoadAsync();
            return;
        }
        liveAllRequested = false;
        if (liveCatalogComplete) return;
        if (liveCategoryCatalogs.TryGetValue(selectedCategory, out var saved))
        {
            ShowCatalog(saved, true);
            return;
        }
        var providerCategory = liveProviderCategories.FirstOrDefault(category => category.Name == selectedCategory);
        if (providerCategory is not null) await LoadXtreamCategoryAsync(account, providerCategory);
    }

    private string? LoadSelectedCategory()
    {
        if (AppServices.ActiveAccount is not { } account) return null;
        var value = Preferences.Default.Get(CategoryPreferenceKey(account.Id), "").Trim();
        return value.Length == 0 ? null : value;
    }

    private void SaveSelectedCategory(string? value)
    {
        if (AppServices.ActiveAccount is not { } account) return;
        var key = CategoryPreferenceKey(account.Id);
        if (string.IsNullOrWhiteSpace(value)) Preferences.Default.Remove(key);
        else Preferences.Default.Set(key, value);
    }

    private string CategoryPreferenceKey(string accountId) =>
        IsContentTab
            ? $"catalog.category.{accountId}.content.{contentFilter.ToString().ToLowerInvariant()}"
            : $"catalog.category.{accountId}.{(int)MediaKind.Channel}";

    private static string ContentFilterPreferenceKey(string accountId) => $"catalog.content-filter.{accountId}";
    private static MainSection ReadMainSection() =>
        Enum.TryParse<MainSection>(Preferences.Default.Get("mainSection", MainSection.Home.ToString()), out var saved)
            && Enum.IsDefined(saved) ? saved : MainSection.Home;
    private static MainSection InitialMainSection()
    {
        var saved = ReadMainSection();
        return Ui.IsTelevision && saved == MainSection.Home ? MainSection.LiveTv : saved;
    }
    private static ContentFilterMode ReadContentFilter(string accountId) =>
        Enum.TryParse<ContentFilterMode>(Preferences.Default.Get(ContentFilterPreferenceKey(accountId), "All"), out var saved)
            ? saved : ContentFilterMode.All;

    private async void OnSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not MediaItem item || AppServices.ActiveAccount is not { } account) return;
        items.SelectedItem = null;
        try
        {
            if (guide) await Navigation.PushAsync(new GuidePage(account, item));
            else if (item.Kind == MediaKind.Movie || item.HasEpisodes) await Navigation.PushAsync(new MediaDetailsPage(account, item));
            else await PlaybackService.PlayAsync(this, item);
        }
        catch (Exception ex) { await Ui.ErrorAsync(this, ex); }
    }

    private async Task OpenFavoritesAsync()
    {
        if (AppServices.ActiveAccount is null) await ChooseAccountAsync();
        if (AppServices.ActiveAccount is null) return;
        await FavoritesService.LoadAsync();
        if (AppServices.ActiveAccount is not null) await Navigation.PushAsync(new FavoritesPage());
    }

    private bool IsHomeTab => section == MainSection.Home;
    private bool IsLiveTvTab => section == MainSection.LiveTv;
    private bool IsContentTab => section == MainSection.MoviesAndSeries;
    private bool IsFavoritesTab => section == MainSection.Favorites;
    private bool IsBrowserTab => section == MainSection.Browser;
    private MediaKind? SelectedContentKind => contentFilter switch
    {
        ContentFilterMode.Movies => MediaKind.Movie,
        ContentFilterMode.Series => MediaKind.Series,
        _ => null
    };
    private bool HasCurrentCatalogCache => IsContentTab
        ? CatalogOptionsService.Catalogs.ContainsKey(MediaKind.Movie) && CatalogOptionsService.Catalogs.ContainsKey(MediaKind.Series)
        : CatalogOptionsService.Catalogs.ContainsKey(MediaKind.Channel);
    private static IReadOnlyList<MediaItem> CombinedContentCatalog() =>
        CatalogOptionsService.Catalogs.GetValueOrDefault(MediaKind.Movie, [])
            .Concat(CatalogOptionsService.Catalogs.GetValueOrDefault(MediaKind.Series, [])).ToArray();

    private async Task LoadHomeAsync(bool ensureChannels = false)
    {
        if (AppServices.ActiveAccount is not { } account)
        {
            home.Clear();
            status.Text = LanguageService.Text("Abra uma lista ou adicione uma conta para começar.");
            return;
        }
        var channelsLoaded = CatalogOptionsService.Catalogs.TryGetValue(MediaKind.Channel, out var channels);
        status.Text = LanguageService.Text("A preparar a página inicial…");
        await home.LoadAsync(account, channels ?? [], showLoading: !AppOptions.CatalogUpdateInBackground);
        if (AppServices.ActiveAccount?.Id != account.Id || !IsHomeTab) return;
        status.Text = LanguageService.Text("Sugestões personalizadas a partir da sua atividade.");
        if (ensureChannels && !channelsLoaded) _ = LoadHomeChannelsAsync(account);
    }

    private async Task LoadHomeChannelsAsync(PlaylistAccount account)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var channels = await AppServices.Client.GetCatalogAsync(account, MediaKind.Channel, timeout.Token);
            if (AppServices.ActiveAccount?.Id != account.Id) return;
            CatalogOptionsService.Catalogs[MediaKind.Channel] = channels;
            await TrySaveCatalogAsync(account.Id, MediaKind.Channel, channels);
            EpgService.RefreshInBackground(account, channels);
            if (IsHomeTab) await LoadHomeAsync();
        }
        catch (Exception ex) when (ex is OperationCanceledException or HttpRequestException or InvalidOperationException)
        {
            System.Diagnostics.Debug.WriteLine(ex);
            if (AppServices.ActiveAccount?.Id == account.Id && IsHomeTab)
                status.Text = LanguageService.Text("A página inicial foi aberta com os dados disponíveis.");
        }
    }

    private async Task LoadFavoritesTabAsync()
    {
        var accountId = AppServices.ActiveAccount?.Id;
        if (accountId is null) return;
        var nonBlocking = AppOptions.CatalogUpdateInBackground;
        if (!nonBlocking)
        {
            spinner.IsRunning = true;
            spinner.IsVisible = true;
        }
        status.Text = LanguageService.Text(nonBlocking
            ? "A atualizar favoritos em segundo plano…" : "A carregar favoritos…");
        try
        {
            try { await FavoritesService.LoadAsync(); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
            if (AppServices.ActiveAccount?.Id != accountId || !IsFavoritesTab) return;
            ShowCatalog(FavoritesService.Items.Where(item => item.Kind == MediaKind.Channel).ToArray(), true);
        }
        finally
        {
            if (!nonBlocking)
            {
                spinner.IsRunning = false;
                spinner.IsVisible = false;
            }
        }
    }

    private void OnFavoritesChanged()
    {
        if (!IsFavoritesTab) return;
        Dispatcher.Dispatch(() =>
            ShowCatalog(FavoritesService.Items.Where(item => item.Kind == MediaKind.Channel).ToArray(), true));
    }

    private void OnDvrChanged()
    {
        if (IsHomeTab) Dispatcher.Dispatch(() => _ = LoadHomeAsync());
    }

    protected override bool OnBackButtonPressed()
    {
        if (!liveTv.IsFullscreen) return base.OnBackButtonPressed();
        liveTv.SetFullscreen(false);
        return true;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (startupAttempted && IsHomeTab && AppServices.ActiveAccount is not null)
            Dispatcher.Dispatch(() => _ = LoadHomeAsync());
    }

    internal async Task OpenPendingNotificationAsync()
    {
#if ANDROID
        var destination = AndroidNotificationNavigation.Consume();
        if (destination is null) return;
        Page page = destination == "downloads" ? new OfflineDownloadsPage() : new RecordingsPage();
        if (Navigation.NavigationStack.LastOrDefault()?.GetType() != page.GetType())
            await Navigation.PushAsync(page);
#else
        await Task.CompletedTask;
#endif
    }

    protected override void OnDisappearing()
    {
        if (!PictureInPictureService.IsActive) liveTv.Stop();
        base.OnDisappearing();
    }

    private void OnLocked()
    {
        liveTv.Stop(); liveTv.SetChannels([], "Todas as categorias");
        home.Clear();
        visibleRemoteChannels = [];
        CancelLoading(); catalog = []; catalogLoaded = false; contentCatalogPrepared = false; items.ItemsSource = null; selectedCategory = null; categoryChoices = [];
        liveProviderCategories = [];
        liveCategoryCatalogs.Clear();
        liveCatalogComplete = false;
        liveAllRequested = false;
        spinner.IsRunning = false; spinner.IsVisible = false;
        accountLabel.Text = "Nenhuma lista aberta";
        categories.Text = LanguageService.Text("Todas as categorias");
        status.Text = "Lista bloqueada. Escolha «Abrir lista» para voltar a entrar.";
    }

    private IReadOnlyList<MediaItem> RemoteChannelItems()
    {
        return FavoritesService.Items.Where(item => item.Kind == MediaKind.Channel).ToArray();
    }

    private RemoteControlState ReadRemoteState()
    {
        var remoteChannels = RemoteChannelItems();
        return new(DeviceInfo.Name,
            IsHomeTab ? "Início" : IsFavoritesTab ? "Favoritos" : IsLiveTvTab ? "TV ao Vivo" : IsContentTab ? "Filmes e Séries" : "Browser",
            LanguageService.Text("Favoritos"),
            DeviceVolumeService.GetMediaVolume(liveTv.Volume),
            liveTv.CurrentItem?.Id ?? "",
            remoteChannels.Select(item => new RemoteChannel(item.Id, item.Name)).ToArray(),
            Categories: []);
    }

    private async Task<RemoteControlState> ExecuteRemoteCommandAsync(RemoteControlRequest request)
    {
        switch (request.Command)
        {
            case "volume" when int.TryParse(request.Value, out var requestedVolume):
                requestedVolume = Math.Clamp(requestedVolume, 0, 100);
                if (DeviceVolumeService.TrySetMediaVolume(requestedVolume))
                    liveTv.Volume = 100;
                else
                    liveTv.Volume = requestedVolume;
                break;
            case "channel":
                var item = RemoteChannelItems().FirstOrDefault(candidate => candidate.Id == request.Value)
                    ?? throw new InvalidOperationException("O canal já não está disponível.");
                if (!await CatalogOptionsService.AuthorizePlaybackAsync(this, item))
                    throw new InvalidOperationException("A reprodução precisa de autorização no dispositivo.");
                if (!IsFavoritesTab || guide) await SwitchAsync(MainSection.Favorites, false);
                await liveTv.PlayRemoteAsync(item);
                break;
            default:
                throw new InvalidOperationException("Comando remoto desconhecido.");
        }
        return ReadRemoteState();
    }
}









