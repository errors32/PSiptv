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
    private readonly CollectionView fullscreenChannels;
    private readonly Label fullscreenGroupName = Ui.Text("", 16);
    private readonly Grid channelMenu;
    private readonly Button channelMenuButton;
    private readonly Grid fullscreenToolbar;
    private readonly Button fullscreenFocusTarget;
    private readonly Button maximize;
    private readonly Grid controls;
    private readonly Button viewModeButton;
    private readonly Button multiviewButton;
    private bool guideMode;
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
                top.Add(logo);
                if (!Ui.IsTelevision)
                {
                    top.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(40)));
                    var favorite = new FavoriteButton { WidthRequest = 40, VerticalOptions = LayoutOptions.Start };
                    top.Add(favorite, 1);
                }
                var card = Ui.FocusableCard(Ui.Stack(top, name), value => channels!.SelectedItem = value);
                card.Padding = 10;
                card.MinimumHeightRequest = Ui.IsTelevision ? 108 : 142;
                return card;
            })
        };
        channels.SelectionChanged += async (_, e) =>
        {
            if (e.CurrentSelection.FirstOrDefault() is not MediaItem item) return;
            channels.SelectedItem = null;
            await SelectChannelAsync(item, false);
        };
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
            message.Text = "";
            player.UseStretchToViewport();
        };
        player.StatusChanged += text => message.Text = text;
        player.MediaFailed += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(message.Text))
                message.Text = "Não foi possível reproduzir. Toque novamente no canal para tentar.";
        };
        video.Add(player);
        controls = new Grid { Padding = 8, ColumnSpacing = 6, VerticalOptions = LayoutOptions.End };
        channelName.TextColor = Colors.White;
        channelName.MaxLines = 1;
        channelName.LineBreakMode = LineBreakMode.TailTruncation;
        maximize = Ui.Button("", () => { SetFullscreen(!IsFullscreen); return Task.CompletedTask; });
        maximize.ImageSource = Ui.FontIconSource(FaIcons.Expand, 22);
        maximize.Padding = 10;
        SemanticProperties.SetDescription(maximize, LanguageService.Text("Maximizar ecrã"));
        controls.ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)];
        channelName.IsVisible = false;
        controls.Add(channelName);
        controls.Add(maximize, 1);
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
        var groupHeader = new Grid
        {
            ColumnSpacing = 8,
            ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto), new(GridLength.Auto)]
        };
        groupHeader.Add(groupName);
        groupHeader.Add(viewModeButton, 1);
        groupHeader.Add(multiviewButton, 2);
        channelPanel = new Grid
        {
            RowSpacing = Ui.IsTelevision ? 5 : 10,
            RowDefinitions = [new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star)]
        };
        channelPanel.Add(groupHeader);
        channelPanel.Add(channels, 0, 1);
        channelPanel.Add(guideGrid, 0, 1);
        SetGuideMode(Preferences.Default.Get("liveTvGuideMode", false));
        layout.Add(video);
        layout.Add(channelPanel);
        Content = layout;
        player.ToggleFullscreen = () => SetFullscreen(!IsFullscreen);
        player.ControlsVisibilityChanged += UpdatePlaybackControlsVisibility;
        SizeChanged += (_, _) => Arrange();
        Loaded += (_, _) => Arrange();
        Unloaded += (_, _) => ReleaseFullscreenKeyHandler();
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

    public void SetChannels(IReadOnlyList<MediaItem> items, string group)
    {
        channels.ItemsSource = items;
        fullscreenChannels.ItemsSource = items;
        guideGrid.SetChannels(items);
        groupName.Text = $"{group} ({items.Count})";
        fullscreenGroupName.Text = $"{group} ({items.Count})";
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
        await player.PlayAsync(item);
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

    public void Stop()
    {
        player.Stop();
        CurrentItem = null;

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
#if ANDROID
        if (Ui.IsTelevision)
        {
            if (value) MainActivity.TelevisionKeyHandler = fullscreenKeyHandler;
            else ReleaseFullscreenKeyHandler();
        }
#endif
        player.SetPreviewMode(!value);
        if (!value) channelMenu.IsVisible = false;
        maximize.ImageSource = Ui.FontIconSource(value ? FaIcons.Compress : FaIcons.Expand, 22);
        SemanticProperties.SetDescription(maximize, LanguageService.Text(value ? "Sair do ecrã inteiro" : "Maximizar ecrã"));
        player.SetFullscreen(value);
        ScreenOrientationService.SetFullscreen(value);
        FullscreenChanged?.Invoke(value);
        Arrange();
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
        cardsLayout.Span = Math.Clamp((int)(available / 155), 2, 5);
    }
}


