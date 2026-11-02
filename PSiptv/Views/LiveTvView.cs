using CommunityToolkit.Maui.Views;
using PSiptv.Core;
using PSiptv.Services;

namespace PSiptv.Views;

public sealed class LiveTvView : ContentView
{
    private readonly PlaybackView player = new();
    private readonly Label message = Ui.Text("Escolha um canal para reproduzir", 15, true);
    private readonly Label channelName = Ui.Text("TV ao Vivo", 16);
    private readonly Label groupName = Ui.Text("Todas as categorias", 18);
    private readonly SearchBar radioSearch = new() { Placeholder = "Pesquisar rádios", IsVisible = false };
    private readonly Grid layout = new()
    {
        RowSpacing = 10,
        ColumnSpacing = 14,
        SafeAreaEdges = new SafeAreaEdges(SafeAreaRegions.None)
    };
    private readonly Grid video = new()
    {
        BackgroundColor = Colors.Black,
        IsClippedToBounds = true,
        SafeAreaEdges = new SafeAreaEdges(SafeAreaRegions.None)
    };
    private readonly Grid channelPanel;
    private readonly TvGuideGridView guideGrid = new() { IsVisible = false };
    private readonly GridItemsLayout cardsLayout = new(2, ItemsLayoutOrientation.Vertical)
    {
        HorizontalItemSpacing = 10, VerticalItemSpacing = 10
    };
    private readonly CollectionView channels;
    private readonly DataTemplate currentCardsTemplate;
    private readonly DataTemplate cleanCardsTemplate;
    private readonly CollectionView fullscreenChannels;
    private IReadOnlyList<MediaItem> displayedChannels = [];
    private bool refreshChannelsAfterLayout;
    private readonly Label fullscreenGroupName = Ui.Text("", 16);
    private readonly Grid channelMenu;
    private readonly Button channelMenuButton;
    private readonly Grid fullscreenToolbar;
    private readonly Button fullscreenFocusTarget;
    private readonly Button maximize;
    private readonly Button previewPlayback;
    private bool previewPlaybackActive;
    private bool changingPreviewPlayback;
    private int previewPlaybackVersion;
    private string? focusedChannelKey;
    private string? pendingChannelFocus;
    private int channelFocusRequest;
    private readonly Grid controls;
    private readonly Button viewModeButton;
    private readonly Button multiviewButton;
    private readonly Button radioButton;
    private readonly Button recentButton;
    private bool recentOnly;
    private IReadOnlyList<MediaItem> recentChannels = [];
    private readonly Dictionary<string, string> listAnchors = [];
    private string displayedScope = "";
    private string recentHistoryScope = "";
    private readonly Dictionary<string, WeakReference<TvFocusableBorder>> channelCards = [];
    private readonly Dictionary<string, int> channelIndices = [];
    private bool guideMode;
    private InterfaceDesign? appliedDesign;
    private bool radioMode;
    private bool favoritesSection;
    private int radioRequest;
    private IReadOnlyList<MediaItem> sectionChannels = [];
    private string sectionGroup = "";
    private IReadOnlyList<MediaItem> radioChannels = [];
    private string radioGroup = "";
    private bool changingChannelFromRemote;
#if ANDROID
    private readonly Func<Android.Views.Keycode, bool> fullscreenKeyHandler;
#endif
    public bool IsFullscreen { get; private set; }
    private bool IsPictureInPicture =>
        PictureInPictureService.IsActive && PictureInPictureService.Current == player;
    public bool IsVideoOnly => IsFullscreen || IsPictureInPicture;
    public MediaItem? CurrentItem { get; private set; }
    public int Volume { get => player.Volume; set => player.Volume = value; }
    public event Action<bool>? FullscreenChanged;

    public LiveTvView()
    {
#if ANDROID
        fullscreenKeyHandler = HandleFullscreenTvKey;
#endif
        player.UseStretchToViewport();
        player.SetPreviewMode(true);
        channels = new CollectionView
        {
            SelectionMode = SelectionMode.Single,
            ItemsLayout = cardsLayout,
            EmptyView = Ui.Text("Nenhum canal neste grupo.", 15, true),
            ItemTemplate = new DataTemplate(() =>
            {
                var logo = new LogoImage { HeightRequest = Ui.IsTelevision ? 56 : 80,
                    Aspect = Aspect.AspectFit, Margin = new Thickness(8, 4) };

                var name = Ui.Text("", 14);
                name.FontAttributes = FontAttributes.Bold;
                name.MaxLines = 2;
                name.LineBreakMode = LineBreakMode.TailTruncation;
                name.SetBinding(Label.TextProperty, nameof(MediaItem.Name));
                var top = new Grid { ColumnDefinitions = [new ColumnDefinition(GridLength.Star)] };
                var radioIcon = Ui.FontIcon(FaIcons.Radio, 34);
                radioIcon.IsVisible = false;
                logo.BindingContextChanged += (_, _) =>
                {
                    var showIcon = logo.BindingContext is MediaItem item &&
                        PortugueseRadioService.IsRadio(item) && item.Logo.Length == 0;
                    radioIcon.IsVisible = showIcon;
                    logo.IsVisible = !showIcon;
                };
                top.Add(radioIcon);
                top.Add(logo);
                if (!Ui.IsTelevision)
                {
                    top.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(40)));
                    var favorite = new FavoriteButton { WidthRequest = 40, VerticalOptions = LayoutOptions.Start };
                    top.Add(favorite, 1);
                }
                var card = Ui.FocusableCard(Ui.Stack(top, name), value => channels!.SelectedItem = value);
                TrackChannelCard(card);
                card.Padding = 10;
                card.MinimumHeightRequest = Ui.IsTelevision ? 108 : 142;
                return card;
            })
        };
        currentCardsTemplate = channels.ItemTemplate;
        cleanCardsTemplate = new DataTemplate(() =>
        {
            var logo = new LogoImage { WidthRequest = 52, HeightRequest = 46, Aspect = Aspect.AspectFit };
            var radioIcon = Ui.FontIcon(FaIcons.Radio, 24);
            radioIcon.IsVisible = false;
            logo.BindingContextChanged += (_, _) =>
            {
                var showIcon = logo.BindingContext is MediaItem item &&
                    PortugueseRadioService.IsRadio(item) && item.Logo.Length == 0;
                radioIcon.IsVisible = showIcon;
                logo.IsVisible = !showIcon;
            };
            var title = Ui.Text("", 14);
            title.FontAttributes = FontAttributes.Bold;
            title.MaxLines = 1;
            title.LineBreakMode = LineBreakMode.TailTruncation;
            title.SetBinding(Label.TextProperty, nameof(MediaItem.Name));
            var subtitle = Ui.Text("", 11, true);
            subtitle.MaxLines = 1;
            subtitle.SetBinding(Label.TextProperty, nameof(MediaItem.Category));
            var artwork = new Grid { WidthRequest = 52, HeightRequest = 46 };
            artwork.Add(radioIcon);
            artwork.Add(logo);
            var row = new Grid
            {
                ColumnDefinitions = [new(new GridLength(52)), new(GridLength.Star), new(new GridLength(Ui.IsTelevision ? 0 : 44))],
                ColumnSpacing = 10
            };
            row.Add(artwork);
            row.Add(Ui.Stack(title, subtitle), 1);
            if (!Ui.IsTelevision) row.Add(new FavoriteButton { WidthRequest = 44 }, 2);
            var card = Ui.FocusableCard(row, value => channels.SelectedItem = value);
            TrackChannelCard(card);
            card.Padding = new Thickness(9, 6);
            card.MinimumHeightRequest = 62;
            return card;
        });
        channels.SelectionChanged += async (_, e) =>
        {
            if (e.CurrentSelection.FirstOrDefault() is not MediaItem item) return;
            channels.SelectedItem = null;
            await SelectChannelAsync(item, false);
        };
        channels.Scrolled += (_, e) =>
        {
            if (channels.ItemsSource is not IReadOnlyList<MediaItem> source || e.FirstVisibleItemIndex < 0 || e.FirstVisibleItemIndex >= source.Count) return;
            listAnchors[displayedScope] = CatalogPreferences.ItemKey(source[e.FirstVisibleItemIndex]);
        };
        channels.Loaded += (_, _) => RefreshChannelsAfterLayout();
        channels.SizeChanged += (_, _) => RefreshChannelsAfterLayout();
        fullscreenChannels = new CollectionView
        {
            SelectionMode = SelectionMode.Single,
            ItemsLayout = new LinearItemsLayout(ItemsLayoutOrientation.Horizontal) { ItemSpacing = 10 },
            HorizontalScrollBarVisibility = ScrollBarVisibility.Never,
            EmptyView = Ui.Text("Nenhum canal neste grupo.", 14, true),
            ItemTemplate = new DataTemplate(() =>
            {
                var logo = new LogoImage
                {
                    HeightRequest = 62,
                    Aspect = Aspect.AspectFit,
                    Margin = new Thickness(6, 2)
                };
                var name = Ui.Text("", 13);
                name.FontAttributes = FontAttributes.Bold;
                name.MaxLines = 2;
                name.LineBreakMode = LineBreakMode.TailTruncation;
                name.SetBinding(Label.TextProperty, nameof(MediaItem.Name));
                var card = Ui.FocusableCard(Ui.Stack(logo, name), value => fullscreenChannels!.SelectedItem = value);
                card.WidthRequest = 180;
                card.MinimumHeightRequest = 132;
                card.Padding = 8;
                return card;
            })
        };
        fullscreenChannels.SelectionChanged += async (_, e) =>
        {
            if (e.CurrentSelection.FirstOrDefault() is not MediaItem item) return;
            fullscreenChannels.SelectedItem = null;
            await SelectChannelAsync(item, true);
        };
        player.MediaOpened += (_, _) =>
        {
            if (player.ActiveMedia is { } actual) { CurrentItem = actual; channelName.Text = actual.Name; }
            previewPlaybackActive = true;
            UpdatePreviewPlaybackButton();
            message.Text = "";
            player.UseStretchToViewport();
        };
        player.StatusChanged += text => message.Text = text;
        player.MediaFailed += (_, _) =>
        {
            previewPlaybackActive = false;
            UpdatePreviewPlaybackButton();
            if (string.IsNullOrWhiteSpace(message.Text))
                message.Text = "Não foi possível reproduzir. Toque novamente no canal para tentar.";
        };
        player.MediaEnded += (_, _) => { previewPlaybackActive = false; UpdatePreviewPlaybackButton(); };
        player.PlaybackStopping += () =>
        {
            previewPlaybackVersion++;
            previewPlaybackActive = false;
            UpdatePreviewPlaybackButton();
        };
        video.Add(player);
        controls = new Grid { Padding = 12, ColumnSpacing = 10, VerticalOptions = LayoutOptions.End };
        channelName.TextColor = Colors.White;
        channelName.MaxLines = 1;
        channelName.LineBreakMode = LineBreakMode.TailTruncation;
        maximize = Ui.Button("", () => { SetFullscreen(!IsFullscreen); return Task.CompletedTask; });
        maximize.ImageSource = Ui.FontIconSource(FaIcons.Expand, Ui.IsTelevision ? 32 : 28);
        previewPlayback = Ui.Button("", TogglePreviewPlaybackAsync);
        foreach (var button in new[] { previewPlayback, maximize })
        {
            button.WidthRequest = button.HeightRequest = Ui.IsTelevision ? 76 : 64;
            button.MinimumHeightRequest = Ui.IsTelevision ? 76 : 64;
            button.Padding = 14;
            button.Focused += (_, _) => CancelPendingChannelFocus();
            ConfigurePreviewNavigation(button);
        }
        SemanticProperties.SetDescription(maximize, LanguageService.Text("Maximizar ecrã"));
        ToolTipProperties.SetText(maximize, LanguageService.Text("Maximizar ecrã"));
        controls.ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto), new(GridLength.Auto)];
        channelName.IsVisible = false;
        controls.Add(channelName);
        controls.Add(previewPlayback, 1);
        controls.Add(maximize, 2);
        UpdatePreviewPlaybackButton();
        video.Add(controls);

        var closeMenu = Ui.Button("", () => { CloseChannelMenu(); return Task.CompletedTask; });
        closeMenu.ImageSource = Ui.FontIconSource(FaIcons.Compress, 18);
        closeMenu.Padding = 8;
        closeMenu.WidthRequest = 44;
        SemanticProperties.SetDescription(closeMenu, LanguageService.Text("Fechar canais"));
        var menuHeader = new Grid
        {
            ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)]
        };
        fullscreenGroupName.TextColor = Colors.White;
        fullscreenGroupName.FontAttributes = FontAttributes.Bold;
        menuHeader.Add(fullscreenGroupName);
        menuHeader.Add(closeMenu, 1);
        channelMenu = new Grid
        {
            IsVisible = false,
            HeightRequest = 220,
            Padding = new Thickness(12, 8),
            RowSpacing = 6,
            VerticalOptions = LayoutOptions.Start,
            RowDefinitions = [new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star)],
            BackgroundColor = Color.FromArgb("#EB0F172C")
        };
        channelMenu.Add(menuHeader);
        channelMenu.Add(fullscreenChannels, 0, 1);
        video.Add(channelMenu);

        channelMenuButton = Ui.Button("", () =>
        {
            channelMenu.IsVisible = true;
            UpdatePlaybackControlsVisibility(player.AreControlsVisible);
            return Task.CompletedTask;
        });
        channelMenuButton.ImageSource = Ui.FontIconSource(FaIcons.List, 20);
        channelMenuButton.Padding = 10;
        channelMenuButton.WidthRequest = 48;
        channelMenuButton.IsVisible = false;
        SemanticProperties.SetDescription(channelMenuButton, LanguageService.Text("Abrir canais da categoria"));
        fullscreenFocusTarget = FullscreenButton(FaIcons.Compress, "Sair do ecrã inteiro", () =>
        {
            SetFullscreen(false);
            return Task.CompletedTask;
        });
        var castFullscreen = FullscreenButton(FaIcons.Chromecast, "Transmitir", async () =>
        {
            var page = FindPage();
            if (page is not null) await ScreenSharingService.ChooseAsync(page, CurrentItem, player.Pause);
        }, "FontAwesomeFreeBrands");
        fullscreenToolbar = new Grid
        {
            IsVisible = false,
            Margin = 12,
            ColumnSpacing = 8,
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Start,
            ColumnDefinitions = [new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto)]
        };
        fullscreenToolbar.Add(fullscreenFocusTarget);
        fullscreenToolbar.Add(castFullscreen, 1);
        fullscreenToolbar.Add(channelMenuButton, 2);
        video.Add(fullscreenToolbar);
        guideGrid.PlayRequested = item => SelectChannelAsync(item, false);
        guideGrid.ReminderRequested = async (channel, programme) =>
        {
            if (AppServices.ActiveAccount is not { } account) return;
            var page = FindPage();
            if (page is null) return;
            var choices = new[] { "Alternar lembrete", "Alternar gravação DVR" };
            var selected = await LanguageService.ActionSheetAsync(page, programme.Title, "Cancelar", null, choices);
            if (selected == choices[0])
            {
                var added = await ReminderService.ToggleAsync(account, channel, programme);
                message.Text = LanguageService.Text(added ? "Lembrete criado" : "Lembrete removido");
            }
            else if (selected == choices[1])
            {
                var added = await DvrService.ToggleProgrammeAsync(account, channel, programme);
                message.Text = LanguageService.Text(added ? "Gravação agendada" : "Gravação cancelada");
            }
        };
        viewModeButton = Ui.Button("", ToggleViewModeAsync);
        viewModeButton.WidthRequest = 48;
        viewModeButton.MinimumHeightRequest = 42;
        viewModeButton.Padding = 8;
        multiviewButton = Ui.Button("", OpenMultiviewAsync);
        multiviewButton.ImageSource = Ui.FontIconSource(FaIcons.WindowRestore, 19);
        multiviewButton.WidthRequest = 48;
        multiviewButton.MinimumHeightRequest = 42;
        multiviewButton.Padding = 8;
        multiviewButton.IsVisible = !DeviceProfile.IsAutomotive;
        SemanticProperties.SetDescription(multiviewButton, LanguageService.Text("Abrir Multiview"));
        ToolTipProperties.SetText(multiviewButton, LanguageService.Text("Abrir Multiview"));
        radioButton = Ui.Button("", ToggleRadioModeAsync);
        radioButton.WidthRequest = 48;
        radioButton.MinimumHeightRequest = 42;
        radioButton.Padding = 8;
        radioButton.ContentLayout = new Button.ButtonContentLayout(Button.ButtonContentLayout.ImagePosition.Left, 0);
        UpdateRadioButton();
        recentButton = FullscreenButton(FaIcons.ClockRotateLeft, "Últimos canais", async () =>
        {
            if (AppServices.ActiveAccount is not { } account) return;
            var session = AppServices.SessionVersion;
            var history = await HistoryService.LoadAsync(account.Id);
            if (session != AppServices.SessionVersion) return;
            recentHistoryScope = UserProfileService.Scope(account.Id);
            recentChannels = history.Select(e => e.Item).Where(e => e.Kind == MediaKind.Channel && !e.IsCatchup)
                .DistinctBy(CatalogPreferences.ItemKey).Take(20).ToArray();
            recentOnly = !recentOnly;
            recentButton!.ImageSource = Ui.FontIconSource(recentOnly ? FaIcons.List : FaIcons.ClockRotateLeft, 19);
            SemanticProperties.SetDescription(recentButton, LanguageService.Text(recentOnly ? "Todos os canais" : "Últimos canais"));
            SetChannels(sectionChannels, sectionGroup, favoritesSection);
        });
        var groupHeader = new Grid
        {
            ColumnSpacing = 8,
            ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto), new(GridLength.Auto),
                new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto)]
        };
        groupHeader.Add(groupName);
        groupHeader.Add(viewModeButton, 1);
        groupHeader.Add(multiviewButton, 2);
        groupHeader.Add(radioButton, 3);
        var podcasts = Ui.Button("", () => Navigation.PushAsync(new PodcastsPage(favoritesOnly: favoritesSection)));
        podcasts.ImageSource = Ui.FontIconSource(FaIcons.Podcast, 19);
        podcasts.WidthRequest = 48;
        podcasts.MinimumHeightRequest = 42;
        podcasts.Padding = 8;
        podcasts.ContentLayout = new Button.ButtonContentLayout(Button.ButtonContentLayout.ImagePosition.Left, 0);
        SemanticProperties.SetDescription(podcasts, LanguageService.Text("Mostrar podcasts"));
        ToolTipProperties.SetText(podcasts, LanguageService.Text("Mostrar podcasts"));
        groupHeader.Add(podcasts, 4);
        groupHeader.Add(recentButton, 5);
        radioSearch.Placeholder = LanguageService.Text("Pesquisar rádios");
        radioSearch.SetDynamicResource(SearchBar.TextColorProperty, "Ink");
        radioSearch.SetDynamicResource(SearchBar.PlaceholderColorProperty, "Muted");
        radioSearch.TextChanged += (_, _) => ShowRadioChannels();
        channelPanel = new Grid
        {
            RowSpacing = Ui.IsTelevision ? 5 : 10,
            RowDefinitions = [new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star)]
        };
        channelPanel.Add(groupHeader);
        channelPanel.Add(radioSearch, 0, 1);
        channelPanel.Add(channels, 0, 2);
        channelPanel.Add(guideGrid, 0, 2);
        SetGuideMode(Preferences.Default.Get("liveTvGuideMode", false));
        layout.Add(video);
        layout.Add(channelPanel);
        Content = layout;
        ApplyDesign();
        player.ToggleFullscreen = () => SetFullscreen(!IsFullscreen);
        player.ControlsVisibilityChanged += UpdatePlaybackControlsVisibility;
        SizeChanged += (_, _) => Arrange();
        Loaded += (_, _) => Arrange();
        Unloaded += (_, _) => { ReleaseFullscreenKeyHandler(); CancelPendingChannelFocus(); };
        PictureInPictureService.Changed += () =>
        {
            // PiP needs the same video-only layout as fullscreen, but it must
            // not use SetFullscreen: that method also requests a landscape
            // orientation. Rotating the activity while Android is shrinking it
            // into PiP destroys LibVLC's SurfaceView and leaves a black window.
            controls.IsVisible = !IsPictureInPicture && player.AreControlsVisible;
            if (IsPictureInPicture) CloseChannelMenu();
            Arrange();
            FullscreenChanged?.Invoke(IsVideoOnly);
        };
    }

    private Page? FindPage()
    {
        Element? parent = this;
        while (parent is not null && parent is not Page) parent = parent.Parent;
        return parent as Page;
    }

    public void SetChannels(IReadOnlyList<MediaItem> items, string group, bool favorites = false)
    {
        if (AppServices.ActiveAccount is { } account && recentHistoryScope != UserProfileService.Scope(account.Id))
        {
            recentOnly = false;
            recentChannels = [];
            recentButton.ImageSource = Ui.FontIconSource(FaIcons.ClockRotateLeft, 19);
        }
        sectionChannels = items;
        sectionGroup = group;
        favoritesSection = favorites;
        if (recentOnly) { DisplayChannels(items, group); return; }
        var request = ++radioRequest;
        if (radioMode)
        {
            if (favorites)
                SetRadioChannels(items.Where(PortugueseRadioService.IsRadio).ToArray(), "Rádios favoritas");
            else
            {
                SetRadioChannels([], "A carregar rádios portuguesas…");
                _ = LoadRadiosAsync(request);
            }
            return;
        }
        DisplayChannels(items.Where(item => !PortugueseRadioService.IsRadio(item)).ToArray(), group);
    }

    private void SetRadioChannels(IReadOnlyList<MediaItem> items, string group)
    {
        radioChannels = items;
        radioGroup = group;
        ShowRadioChannels();
    }

    private void ShowRadioChannels()
    {
        if (!radioMode) return;
        var query = radioSearch.Text?.Trim() ?? "";
        DisplayChannels(query.Length == 0 ? radioChannels : radioChannels.Where(item =>
            item.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)).ToArray(), radioGroup);
    }

    private void DisplayChannels(IReadOnlyList<MediaItem> items, string group)
    {
        if (recentOnly)
        {
            items = recentChannels.Where(e => PortugueseRadioService.IsRadio(e) == radioMode).ToArray();
            group = LanguageService.Text("Últimos canais");
        }
        displayedChannels = items;
        channelIndices.Clear();
        for (var index = 0; index < items.Count; index++)
            channelIndices.TryAdd(CatalogPreferences.ItemKey(items[index]), index);
        CancelPendingChannelFocus();
        refreshChannelsAfterLayout = !channels.IsLoaded || channels.Width <= 0 || channels.Height <= 0;
        displayedScope = (AppServices.ActiveAccount is { } account ? UserProfileService.Scope(account.Id) : "") +
            ":" + radioMode + ":" + favoritesSection + ":" + group;
        if (channels.ItemsSource is not IEnumerable<MediaItem> previous || !previous.SequenceEqual(items))
        {
            channels.ItemsSource = items;
            if (listAnchors.TryGetValue(displayedScope, out var anchor) &&
                items.FirstOrDefault(e => CatalogPreferences.ItemKey(e) == anchor) is { } target)
                Dispatcher.Dispatch(() => channels.ScrollTo(target, position: ScrollToPosition.Start, animate: false));
        }
        fullscreenChannels.ItemsSource = items;
        guideGrid.SetChannels(items);
        groupName.Text = $"{group} ({items.Count})";
        fullscreenGroupName.Text = $"{group} ({items.Count})";
    }

    private void TrackChannelCard(TvFocusableBorder card)
    {
        card.Navigate = (direction, repeat) => NavigateChannelCard(card, direction, repeat);
        card.Focused += (_, _) =>
        {
            if (card.BindingContext is MediaItem item) focusedChannelKey = CatalogPreferences.ItemKey(item);
        };
        card.BindingContextChanged += (_, _) =>
        {
            if (card.BindingContext is MediaItem item)
                channelCards[CatalogPreferences.ItemKey(item)] = new(card);
        };
    }

    private bool NavigateChannelCard(TvFocusableBorder card, TvNavigationDirection direction, int repeat)
    {
        if (!Ui.IsTelevision || IsVideoOnly || !channels.IsVisible || card.BindingContext is not MediaItem item) return false;
        var key = pendingChannelFocus ?? CatalogPreferences.ItemKey(item);
        if (!channelIndices.TryGetValue(key, out var index)) return false;
        focusedChannelKey = key;
        if (direction == TvNavigationDirection.Left && index % cardsLayout.Span == 0)
        {
            CancelPendingChannelFocus();
            return maximize.Focus();
        }
        if (repeat >= 8)
            direction = direction switch
            {
                TvNavigationDirection.Up => TvNavigationDirection.PageUp,
                TvNavigationDirection.Down => TvNavigationDirection.PageDown,
                _ => direction
            };
        var rows = Math.Max(1, (int)(channels.Height / Math.Max(1, card.Height + cardsLayout.VerticalItemSpacing)));
        var target = TvChannelNavigation.Target(index, displayedChannels.Count, cardsLayout.Span, direction, rows);
        if (target >= 0) return FocusChannel(target);
        // Do not wrap horizontally into the next row; Up/Down leave the list at its ends.
        return direction == TvNavigationDirection.Right;
    }

    private void ConfigurePreviewNavigation(Button button)
    {
#if ANDROID
        button.HandlerChanged += (_, _) =>
        {
            if (Ui.IsTelevision && button.Handler?.PlatformView is Android.Views.View native)
                AndroidTvFocus.AttachNavigation(native, (direction, _) =>
                {
                    if (IsVideoOnly) return false;
                    if (button == maximize && direction == TvNavigationDirection.Left) return previewPlayback.Focus();
                    if (button == previewPlayback && direction == TvNavigationDirection.Right) return maximize.Focus();
                    if (direction is TvNavigationDirection.Right or TvNavigationDirection.Down) return FocusLastChannel();
                    return false;
                });
        };
#endif
    }

    private bool FocusLastChannel()
    {
        if (!channels.IsVisible || displayedChannels.Count == 0) return false;
        var key = focusedChannelKey ?? (CurrentItem is { } item ? CatalogPreferences.ItemKey(item) : null);
        return FocusChannel(key is not null && channelIndices.TryGetValue(key, out var index) ? index : 0);
    }

    private bool FocusChannel(int index)
    {
        if (index < 0 || index >= displayedChannels.Count) return false;
        var item = displayedChannels[index];
        var key = CatalogPreferences.ItemKey(item);
        pendingChannelFocus = focusedChannelKey = key;
        var request = ++channelFocusRequest;
        channels.ScrollTo(item, position: ScrollToPosition.MakeVisible, animate: false);
        RestoreChannelFocus(key, request, 0);
        return true;
    }

    private void RestoreChannelFocus(string key, int request, int attempt)
    {
        if (request != channelFocusRequest || !IsLoaded || !IsVisible || IsVideoOnly || !channels.IsVisible) return;
        if (FindPage() is not { } page || page.Navigation.ModalStack.Count > 0 || page.Navigation.NavigationStack.LastOrDefault() != page) return;
        if (channelCards.TryGetValue(key, out var weak) && weak.TryGetTarget(out var card) && card.IsLoaded &&
            card.BindingContext is MediaItem bound && CatalogPreferences.ItemKey(bound) == key && card.Focus())
        {
            pendingChannelFocus = null;
            return;
        }
        // RecyclerView may need another layout pass to realize an off-screen target.
        if (attempt < 10) Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(40), () => RestoreChannelFocus(key, request, attempt + 1));
        else pendingChannelFocus = null;
    }

    private void CancelPendingChannelFocus()
    {
        channelFocusRequest++;
        pendingChannelFocus = null;
    }

    private async Task LoadRadiosAsync(int request)
    {
        try
        {
            var items = await PortugueseRadioService.GetAsync();
            if (request == radioRequest && radioMode && !favoritesSection)
                SetRadioChannels(items, "Rádios de Portugal");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
            if (request == radioRequest && radioMode && !favoritesSection)
                SetRadioChannels([], "Não foi possível carregar as rádios. Toque em TV e volte a Rádios para tentar.");
        }
    }

    private Task ToggleRadioModeAsync()
    {
        radioMode = !radioMode;
        UpdateRadioButton();
        radioSearch.IsVisible = radioMode;
        viewModeButton.IsVisible = !radioMode;
        multiviewButton.IsVisible = !radioMode && !DeviceProfile.IsAutomotive;
        channels.IsVisible = radioMode || !guideMode;
        guideGrid.SetActive(!radioMode && guideMode);
        SetChannels(sectionChannels, sectionGroup, favoritesSection);
        return Task.CompletedTask;
    }

    public Task ShowRadioAsync() => radioMode ? Task.CompletedTask : ToggleRadioModeAsync();
    public Task ShowTelevisionAsync() => radioMode ? ToggleRadioModeAsync() : Task.CompletedTask;
    public Task OpenMultiviewFromMenuAsync() => OpenMultiviewAsync();
    public Task TogglePlaybackFromShortcutAsync() => IsVideoOnly ? player.TogglePlayPauseAsync() : TogglePreviewPlaybackAsync();

    private void UpdatePreviewPlaybackButton()
    {
        var description = LanguageService.Text(previewPlaybackActive ? "Parar canal" : "Reproduzir canal");
        previewPlayback.ImageSource = Ui.FontIconSource(previewPlaybackActive ? FaIcons.Stop : FaIcons.Play, Ui.IsTelevision ? 32 : 28);
        previewPlayback.IsEnabled = CurrentItem is not null && (!changingPreviewPlayback || Ui.IsTelevision);
        maximize.IsEnabled = CurrentItem is not null;
        SemanticProperties.SetDescription(previewPlayback, description);
        ToolTipProperties.SetText(previewPlayback, description);
    }

    private async Task TogglePreviewPlaybackAsync()
    {
        if (CurrentItem is not { } item || changingPreviewPlayback) return;
        changingPreviewPlayback = true;
        UpdatePreviewPlaybackButton();
        try
        {
            if (previewPlaybackActive)
            {
                await player.StopForExternalPlaybackAsync();
                if (player.ActiveMedia is null) message.Text = LanguageService.Text("Reprodução parada");
            }
            else await SelectChannelAsync(item, false);
        }
        finally { changingPreviewPlayback = false; UpdatePreviewPlaybackButton(); }
    }

    public void ApplyDesign()
    {
        var clean = DesignService.IsClean;
        if (appliedDesign != DesignService.Mode)
        {
            if (DesignService.Mode == InterfaceDesign.TvGuide) SetGuideMode(true);
            else if (appliedDesign == InterfaceDesign.TvGuide) SetGuideMode(false);
            appliedDesign = DesignService.Mode;
        }
        var template = clean ? cleanCardsTemplate : currentCardsTemplate;
        channels.ItemTemplate = new DataTemplate(() => template.CreateContent());
        layout.RowSpacing = clean ? 6 : 10;
        channelPanel.RowSpacing = clean ? 5 : Ui.IsTelevision ? 5 : 10;
        channelMenu.HeightRequest = clean ? -1 : 220;
        channelMenu.WidthRequest = clean ? Ui.IsTelevision ? 380 : 320 : -1;
        channelMenu.VerticalOptions = clean ? LayoutOptions.Fill : LayoutOptions.Start;
        channelMenu.HorizontalOptions = clean ? LayoutOptions.End : LayoutOptions.Fill;
        fullscreenChannels.ItemsLayout = new LinearItemsLayout(
            clean ? ItemsLayoutOrientation.Vertical : ItemsLayoutOrientation.Horizontal) { ItemSpacing = 8 };
        Arrange();
    }

    private void UpdateRadioButton()
    {
        radioButton.Text = "";
        radioButton.ImageSource = Ui.FontIconSource(radioMode ? FaIcons.Display : FaIcons.Radio, 19);
        var description = LanguageService.Text(radioMode ? "Mostrar canais de TV" : "Mostrar rádios portuguesas");
        SemanticProperties.SetDescription(radioButton, description);
        ToolTipProperties.SetText(radioButton, description);
    }

    private void RefreshChannelsAfterLayout()
    {
        if (!refreshChannelsAfterLayout || !channels.IsLoaded || channels.Width <= 0 || channels.Height <= 0)
            return;
        refreshChannelsAfterLayout = false;
        // Android can miss the first CollectionView binding made before layout.
        channels.ItemsSource = null;
        channels.ItemsSource = displayedChannels;
    }

    private Task ToggleViewModeAsync()
    {
        SetGuideMode(!guideMode);
        return Task.CompletedTask;
    }

    private async Task OpenMultiviewAsync()
    {
        if (FindPage() is not { } page || AppServices.ActiveAccount is null) return;
        var available = (channels.ItemsSource as IEnumerable<MediaItem>)?.ToArray() ?? [];
        if (available.Length == 0)
        {
            await LanguageService.AlertAsync(page, "Multiview", "Não existem canais disponíveis nesta categoria.");
            return;
        }
        var initial = CurrentItem is { Kind: MediaKind.Channel, IsCatchup: false } current &&
                      available.Any(item => CatalogPreferences.ItemKey(item) == CatalogPreferences.ItemKey(current))
            ? current : null;
        player.Stop();
        CurrentItem = null;
        UpdatePreviewPlaybackButton();
        channelName.Text = "TV ao Vivo";
        channelName.IsVisible = false;
        message.Text = "Escolha um canal para reproduzir";
        await page.Navigation.PushAsync(new MultiviewPage(available, initial));
    }

    private void SetGuideMode(bool value)
    {
        guideMode = value;
        Preferences.Default.Set("liveTvGuideMode", value);
        channels.IsVisible = !value;
        guideGrid.SetActive(value);
        viewModeButton.ImageSource = Ui.FontIconSource(value ? FaIcons.List : FaIcons.Calendar, 19);
        SemanticProperties.SetDescription(viewModeButton, LanguageService.Text(value ? "Vista simples" : "Guia TV em grelha"));
        ToolTipProperties.SetText(viewModeButton, LanguageService.Text(value ? "Vista simples" : "Guia TV em grelha"));
    }

    private async Task SelectChannelAsync(MediaItem item, bool fromFullscreenMenu)
    {
        try
        {
            if (FindPage() is not { } page || !await CatalogOptionsService.AuthorizePlaybackAsync(page, item)) return;
            if (fromFullscreenMenu) CloseChannelMenu();
            await PlayAsync(item);
        }
        catch (Exception ex) { await Ui.ErrorAsync(this, ex); }
    }

    private void CloseChannelMenu()
    {
        channelMenu.IsVisible = false;
        UpdatePlaybackControlsVisibility(player.AreControlsVisible);
    }

    private void UpdatePlaybackControlsVisibility(bool visible)
    {
        controls.IsVisible = !IsPictureInPicture && !IsFullscreen;
        channelMenuButton.IsVisible = fullscreenChannels.ItemsSource is not null;
        var showFullscreenToolbar = visible && IsFullscreen && !IsPictureInPicture && !channelMenu.IsVisible;
        var focusToolbar = Ui.IsTelevision && showFullscreenToolbar && !fullscreenToolbar.IsVisible;
        fullscreenToolbar.IsVisible = showFullscreenToolbar;
        if (focusToolbar)
            Dispatcher.Dispatch(() =>
            {
                if (fullscreenToolbar.IsVisible) fullscreenFocusTarget.Focus();
            });
    }

    private static Button FullscreenButton(string icon, string description, Func<Task> action,
        string fontFamily = "FontAwesomeFreeSolid")
    {
        var button = Ui.Button("", action);
        button.ImageSource = Ui.FontIconSource(icon, 20, fontFamily);
        button.WidthRequest = 48;
        button.Padding = 10;
        SemanticProperties.SetDescription(button, LanguageService.Text(description));
        return button;
    }

    private async Task PlayAsync(MediaItem item)
    {
        if (AppServices.ActiveAccount is null) return;
        CurrentItem = item;
        channelName.Text = item.Name;
        channelName.IsVisible = true;
        message.Text = "A ligar à transmissão…";
        var request = ++previewPlaybackVersion;
        previewPlaybackActive = true;
        UpdatePreviewPlaybackButton();
        try { await player.PlayAsync(item); }
        catch
        {
            if (request == previewPlaybackVersion) { previewPlaybackActive = false; UpdatePreviewPlaybackButton(); }
            throw;
        }
    }

#if ANDROID
    private bool HandleFullscreenTvKey(Android.Views.Keycode keyCode)
    {
        if (!Ui.IsTelevision || !IsFullscreen || IsPictureInPicture || player.AreControlsVisible || channelMenu.IsVisible ||
            CurrentItem is null || changingChannelFromRemote) return false;
        var offset = keyCode == Android.Views.Keycode.DpadUp ? -1 : 1;
        changingChannelFromRemote = true;
        Dispatcher.Dispatch(async () =>
        {
            try { await SwitchAdjacentChannelAsync(offset); }
            finally { changingChannelFromRemote = false; }
        });
        return true;
    }

    private async Task SwitchAdjacentChannelAsync(int offset)
    {
        var available = (fullscreenChannels.ItemsSource as IEnumerable<MediaItem>)?.ToArray() ?? [];
        if (available.Length < 2 || CurrentItem is null) return;
        var currentKey = CatalogPreferences.ItemKey(CurrentItem);
        var index = Array.FindIndex(available, item => CatalogPreferences.ItemKey(item) == currentKey);
        if (index < 0) return;
        var next = (index + offset + available.Length) % available.Length;
        await SelectChannelAsync(available[next], false);
        player.HideControls();
    }

    private void ReleaseFullscreenKeyHandler()
    {
        if (ReferenceEquals(MainActivity.TelevisionKeyHandler, fullscreenKeyHandler))
            MainActivity.TelevisionKeyHandler = null;
    }
#else
    private void ReleaseFullscreenKeyHandler() { }
#endif

    public Task PlayRemoteAsync(MediaItem item) => PlayAsync(item);

    public void Stop(bool preserveAudio = false)
    {
        if (preserveAudio && player.RetainAcrossNavigation)
        {
            SetFullscreen(false);
            return;
        }
        player.Stop();
        CurrentItem = null;
        UpdatePreviewPlaybackButton();

        channelName.Text = "TV ao Vivo";
        channelName.IsVisible = false;
        message.Text = "Escolha um canal para reproduzir";
        SetFullscreen(false);
    }

    public void Pause() => player.Pause();

    public void SetFullscreen(bool value)
    {
        if (value == IsFullscreen) return;
        IsFullscreen = value;
        CancelPendingChannelFocus();
#if ANDROID
        if (Ui.IsTelevision)
        {
            if (value) MainActivity.TelevisionKeyHandler = fullscreenKeyHandler;
            else ReleaseFullscreenKeyHandler();
        }
#endif
        player.SetPreviewMode(!value);
        if (!value) channelMenu.IsVisible = false;
        maximize.ImageSource = Ui.FontIconSource(value ? FaIcons.Compress : FaIcons.Expand, Ui.IsTelevision ? 32 : 28);
        SemanticProperties.SetDescription(maximize, LanguageService.Text(value ? "Sair do ecrã inteiro" : "Maximizar ecrã"));
        player.SetFullscreen(value);
        ScreenOrientationService.SetFullscreen(value);
        FullscreenChanged?.Invoke(value);
        Arrange();
        if (!value && Ui.IsTelevision) Dispatcher.Dispatch(() => FocusLastChannel());
    }

    private void Arrange()
    {
        var page = FindPage();
        var viewport = Ui.Viewport((VisualElement?)page ?? this);
        var landscape = viewport.Width > viewport.Height;
        var videoOnly = IsVideoOnly;
        UpdatePlaybackControlsVisibility(player.AreControlsVisible);
        if (!IsFullscreen || IsPictureInPicture) channelMenu.IsVisible = false;
        channelPanel.IsVisible = !videoOnly;
        layout.RowDefinitions.Clear();
        layout.ColumnDefinitions.Clear();
        Grid.SetRow(video, 0);
        Grid.SetColumn(video, 0);
        if (videoOnly)
        {
            layout.RowDefinitions.Add(new RowDefinition(GridLength.Star));
            layout.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            Grid.SetRow(channelPanel, 0);
            Grid.SetColumn(channelPanel, 0);
        }
        else if (landscape)
        {
            layout.RowDefinitions.Add(new RowDefinition(GridLength.Star));
            layout.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(0.8, GridUnitType.Star)));
            layout.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            Grid.SetRow(channelPanel, 0);
            Grid.SetColumn(channelPanel, 1);
        }
        else
        {
            layout.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            layout.RowDefinitions.Add(new RowDefinition(new GridLength(Math.Min(viewport.Width * 9 / 16, viewport.Height * 0.36))));
            layout.RowDefinitions.Add(new RowDefinition(GridLength.Star));
            Grid.SetRow(channelPanel, 1);
            Grid.SetColumn(channelPanel, 0);
        }
        var available = landscape ? viewport.Width / 2.2 : viewport.Width;
        cardsLayout.Span = DesignService.IsClean
            ? Math.Clamp((int)(available / (Ui.IsTelevision ? 230 : 320)), 1, 4)
            : Math.Clamp((int)(available / 155), 2, 5);
    }
}


