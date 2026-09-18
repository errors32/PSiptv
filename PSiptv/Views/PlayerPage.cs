using PSiptv.Core;
using PSiptv.Services;

namespace PSiptv.Views;

public sealed class PlayerPage : LocalizedPage
{
    private readonly PlaybackView player;
    private readonly Label mediaTitle = Ui.Text("", 24);
    private readonly FavoriteButton favorite = new() { HorizontalOptions = LayoutOptions.Start };
    private readonly Button cancelNext;
    private readonly Label message = Ui.Text("A ligar à transmissão…", 14, true);
    private readonly Label nextMessage = Ui.Text("", 14);
    private readonly Label guideStatus = Ui.Text("A carregar programação…", 13, true);
    private readonly CollectionView programmes = new();
    private readonly Grid guideSection;
    private readonly Grid fullscreenToolbar;
    private readonly Grid channelMenu;
    private readonly CollectionView fullscreenChannels;
    private readonly IReadOnlyList<MediaItem> queue;
    private MediaItem current;
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? countdown;
    private bool started;
    private bool fullscreen;
    private int guideRequest;
    private Action? adaptLayout;
    private readonly double resume;
    private readonly bool liveChannel;
    public PlayerPage(MediaItem item, IReadOnlyList<MediaItem>? episodes = null, double resume = 0,
        bool transient = false)
    {
        player = new PlaybackView(recordHistory: !transient, requireActiveAccount: !transient);
        current = item; queue = episodes ?? []; this.resume = resume;
        liveChannel = !transient && item.Kind == MediaKind.Channel && !item.IsCatchup;
        Ui.Page(this, item.Name);
        player.MediaOpened += (_, _) => { message.Text = ""; message.IsVisible = false; };
        player.StatusChanged += text => { message.Text = text; message.IsVisible = text.Length > 0; };
        player.MediaFailed += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(message.Text))
                message.Text = "Não foi possível reproduzir. Verifique a ligação ou tente outro descodificador nas configurações.";
            message.IsVisible = true;
        };
        player.MediaEnded += async (_, _) => await NextAsync();
        mediaTitle.Text = item.Name; favorite.BindingContext = item;
        var header = Ui.Stack(mediaTitle, favorite);
        favorite.IsVisible = !transient && !DeviceProfile.IsAutomotive && !item.IsCatchup;
        cancelNext = Ui.Button("Cancelar próximo episódio", () => { countdown?.Cancel(); nextMessage.Text = ""; return Task.CompletedTask; });
        cancelNext.IsVisible = false;
        nextMessage.IsVisible = false;
        var fullscreenButton = IconButton(FaIcons.Expand, "Ecrã inteiro", () => { SetFullscreen(!fullscreen); return Task.CompletedTask; });
        var retryButton = IconButton(FaIcons.ArrowsRotate, "Voltar a tentar", player.RetryAsync);
        var recordButton = IconButton(FaIcons.RecordVinyl, "Gravar canal", async () =>
        {
            var account = AppServices.ActiveAccount;
            if (account is null || current.Kind != MediaKind.Channel || current.IsCatchup) return;
            var labels = new[] { "30 minutos", "60 minutos", "120 minutos" };
            var selected = await LanguageService.ActionSheetAsync(this, "Duração da gravação", "Cancelar", null, labels);
            var index = Array.IndexOf(labels, selected);
            if (index < 0) return;
            var minutes = new[] { 30, 60, 120 }[index];
            await DvrService.StartManualAsync(account, current, TimeSpan.FromMinutes(minutes));
            await LanguageService.AlertAsync(this, "Gravação iniciada",
                "A gravação continua em segundo plano. Pode geri-la em Gravações DVR.");
        });
        recordButton.IsVisible = item.Kind == MediaKind.Channel && !item.IsCatchup;

        programmes.SelectionMode = SelectionMode.None;
        programmes.ItemsLayout = new LinearItemsLayout(ItemsLayoutOrientation.Vertical) { ItemSpacing = 8 };
        programmes.ItemTemplate = new DataTemplate(() =>
        {
            var schedule = Ui.Text("", 12, true);
            schedule.SetBinding(Label.TextProperty, nameof(PlayerProgrammeRow.Schedule));
            var status = Ui.Text("", 12);
            status.SetDynamicResource(Label.TextColorProperty, "Accent");
            status.SetBinding(Label.TextProperty, nameof(PlayerProgrammeRow.Status));
            var title = Ui.Text("", 16);
            title.FontAttributes = FontAttributes.Bold;
            title.MaxLines = 2;
            title.LineBreakMode = LineBreakMode.TailTruncation;
            title.SetBinding(Label.TextProperty, nameof(PlayerProgrammeRow.Title));
            var description = Ui.Text("", 13, true);
            description.MaxLines = 2;
            description.LineBreakMode = LineBreakMode.TailTruncation;
            description.SetBinding(Label.TextProperty, nameof(PlayerProgrammeRow.Description));
            var heading = new Grid
            {
                ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)]
            };
            heading.Add(schedule);
            heading.Add(status, 1);
            var card = Ui.Card(Ui.Stack(heading, title, description));
            card.Padding = 12;
            return card;
        });
        guideSection = new Grid
        {
            IsVisible = liveChannel,
            RowSpacing = 8,
            RowDefinitions = [new(GridLength.Auto), new(GridLength.Star)]
        };
        guideSection.Add(Ui.Stack(Ui.Text("Programação", 20), guideStatus));
        guideSection.Add(programmes, 0, 1);

        fullscreenChannels = new CollectionView
        {
            SelectionMode = SelectionMode.Single,
            ItemsLayout = new LinearItemsLayout(ItemsLayoutOrientation.Horizontal) { ItemSpacing = 10 },
            HorizontalScrollBarVisibility = ScrollBarVisibility.Never,
            EmptyView = Ui.Text("Nenhum canal neste grupo.", 14, true)
        };
        fullscreenChannels.ItemTemplate = new DataTemplate(() =>
        {
            var logo = new LogoImage { HeightRequest = 62, Aspect = Aspect.AspectFit, Margin = new Thickness(6, 2) };
            var name = Ui.Text("", 13);
            name.FontAttributes = FontAttributes.Bold;
            name.MaxLines = 2;
            name.LineBreakMode = LineBreakMode.TailTruncation;
            name.SetBinding(Label.TextProperty, nameof(MediaItem.Name));
            var card = Ui.FocusableCard(Ui.Stack(logo, name), value => fullscreenChannels.SelectedItem = value);
            card.WidthRequest = 180;
            card.MinimumHeightRequest = 132;
            card.Padding = 8;
            return card;
        });
        fullscreenChannels.ItemsSource = AvailableChannels();
        fullscreenChannels.SelectionChanged += async (_, e) =>
        {
            if (e.CurrentSelection.FirstOrDefault() is not MediaItem channel) return;
            fullscreenChannels.SelectedItem = null;
            await SwitchChannelAsync(channel);
        };

        var closeChannels = IconButton(FaIcons.Compress, "Fechar canais", () =>
        {
            CloseChannelMenu();
            return Task.CompletedTask;
        });
        var menuHeader = new Grid
        {
            ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)]
        };
        var menuTitle = Ui.Text(item.Category, 16);
        menuTitle.TextColor = Colors.White;
        menuTitle.FontAttributes = FontAttributes.Bold;
        menuHeader.Add(menuTitle);
        menuHeader.Add(closeChannels, 1);
        channelMenu = new Grid
        {
            IsVisible = false,
            HeightRequest = 220,
            Padding = new Thickness(12, 8),
            RowSpacing = 6,
            VerticalOptions = LayoutOptions.Start,
            RowDefinitions = [new(GridLength.Auto), new(GridLength.Star)],
            BackgroundColor = Color.FromArgb("#EB0F172C")
        };
        channelMenu.Add(menuHeader);
        channelMenu.Add(fullscreenChannels, 0, 1);

        var minimize = IconButton(FaIcons.Compress, "Sair do ecrã inteiro", () =>
        {
            SetFullscreen(false);
            return Task.CompletedTask;
        });
        var castFullscreen = IconButton(FaIcons.Chromecast, "Transmitir",
            () => ScreenSharingService.ChooseAsync(this, current, player.Pause, queue, player.Position),
            "FontAwesomeFreeBrands");
        var changeChannel = IconButton(FaIcons.List, "Abrir canais da categoria", () =>
        {
            channelMenu.IsVisible = true;
            UpdateFullscreenOverlay();
            return Task.CompletedTask;
        });
        changeChannel.IsVisible = liveChannel;
        fullscreenToolbar = new Grid
        {
            IsVisible = false,
            Margin = 12,
            ColumnSpacing = 8,
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Start,
            ColumnDefinitions = [new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto)]
        };
        fullscreenToolbar.Add(minimize);
        fullscreenToolbar.Add(castFullscreen, 1);
        fullscreenToolbar.Add(changeChannel, 2);

        View footer;
        if (DeviceProfile.IsAutomotive)
        {
            var actions = new HorizontalStackLayout { Spacing = 24, Children = { fullscreenButton, retryButton } };
            footer = Ui.Stack(message, actions);
        }
        else
        {
            var actions = new HorizontalStackLayout
            {
                Spacing = 8,
                HorizontalOptions = LayoutOptions.Center,
                Children =
                {
                    fullscreenButton,
                    IconButton(FaIcons.Chromecast, "Transmitir", () => ScreenSharingService.ChooseAsync(this, current, player.Pause, queue, player.Position), "FontAwesomeFreeBrands"),
                    IconButton(FaIcons.ClosedCaptioning, "Legendas externas", () => player.SelectExternalSubtitlesAsync(this)),
                    recordButton,
                    IconButton(FaIcons.ArrowUpRightFromSquare, "Abrir com leitor externo", async () =>
                    {
                        if (AppServices.ActiveAccount is null) return;
                        await player.StopForExternalPlaybackAsync();
                        await PlaybackService.OpenExternalAsync(current);
                    }),
                    retryButton
                }
            };
            var actionScroll = new ScrollView
            {
                Orientation = ScrollOrientation.Horizontal,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Never,
                HorizontalOptions = LayoutOptions.Fill,
                Content = actions
            };
            footer = Ui.Stack(message, nextMessage, cancelNext, actionScroll);
        }
        var videoHost = new Grid
        {
            BackgroundColor = Colors.Black,
            IsClippedToBounds = true,
            SafeAreaEdges = new SafeAreaEdges(SafeAreaRegions.None)
        };
        videoHost.Add(player);
        videoHost.Add(fullscreenToolbar);
        videoHost.Add(channelMenu);
        var grid = new Grid
        {
            Padding = 12,
            RowSpacing = 8,
            SafeAreaEdges = new SafeAreaEdges(SafeAreaRegions.Container),
            RowDefinitions = [new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto), new(GridLength.Star)]
        };
        grid.Add(header);
        grid.Add(videoHost, 0, 1);
        grid.Add(footer, 0, 2);
        grid.Add(guideSection, 0, 3);
        Content = grid;
        void Adapt()
        {
            var pip = PictureInPictureService.IsActive;
            header.IsVisible = !pip && !fullscreen && !Ui.IsLandscape(this);
            footer.IsVisible = !pip && !fullscreen;
            guideSection.IsVisible = liveChannel && !pip && !fullscreen;
            grid.RowDefinitions[0].Height = header.IsVisible ? GridLength.Auto : new GridLength(0);
            var viewport = Ui.Viewport(this);
            grid.RowDefinitions[1].Height = liveChannel && !pip && !fullscreen && viewport.Width <= viewport.Height
                ? new GridLength(Math.Min(Math.Max(1, viewport.Width - 24) * 9 / 16, viewport.Height * 0.38))
                : GridLength.Star;
            grid.RowDefinitions[2].Height = footer.IsVisible ? GridLength.Auto : new GridLength(0);
            grid.RowDefinitions[3].Height = guideSection.IsVisible ? GridLength.Star : new GridLength(0);
            grid.Padding = pip || fullscreen ? 0 : 12;
            grid.RowSpacing = pip || fullscreen ? 0 : 8;
            grid.SafeAreaEdges = new SafeAreaEdges(pip || fullscreen
                ? SafeAreaRegions.None : SafeAreaRegions.Container);
            NavigationPage.SetHasNavigationBar(this, !pip && !fullscreen);
            if (!fullscreen || pip) channelMenu.IsVisible = false;
            UpdateFullscreenOverlay();
            grid.InvalidateMeasure();
        }
        adaptLayout = Adapt;
        player.ToggleFullscreen = () => SetFullscreen(!fullscreen);
        player.ControlsVisibilityChanged += _ => UpdateFullscreenOverlay();
        SizeChanged += (_, _) => Adapt();
        Loaded += (_, _) => Adapt();
        PictureInPictureService.Changed += Adapt;
        AppServices.Locked += Stop;
        Unloaded += (_, _) => { PictureInPictureService.Changed -= Adapt; AppServices.Locked -= Stop; };
    }
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (started) return; started = true;
        try
        {
            await player.PlayAsync(current, resume);
            if (liveChannel) await LoadGuideAsync();
        }
        catch (Exception ex) { await Ui.ErrorAsync(this, ex); }
    }

    private static Button IconButton(string icon, string description, Func<Task> action,
        string fontFamily = "FontAwesomeFreeSolid")
    {
        var button = Ui.Button("", action);
        button.ImageSource = Ui.FontIconSource(icon, 20, fontFamily);
        button.WidthRequest = 48;
        button.Padding = 10;
        SemanticProperties.SetDescription(button, LanguageService.Text(description));
        ToolTipProperties.SetText(button, LanguageService.Text(description));
        return button;
    }

    private IReadOnlyList<MediaItem> AvailableChannels()
    {
        var all = CatalogOptionsService.Catalogs.GetValueOrDefault(MediaKind.Channel) ?? [];
        var visible = all.Where(channel => !CatalogOptionsService.Current.IsHidden(channel)).ToArray();
        var category = visible.Where(channel => channel.Category == current.Category).ToArray();
        if (category.Length > 0) return category;
        if (visible.Length > 0) return visible;
        return liveChannel ? [current] : [];
    }

    private async Task SwitchChannelAsync(MediaItem channel)
    {
        channelMenu.IsVisible = false;
        UpdateFullscreenOverlay();
        if (CatalogPreferences.ItemKey(channel) == CatalogPreferences.ItemKey(current)) return;
        if (!await CatalogOptionsService.AuthorizePlaybackAsync(this, channel)) return;
        current = channel;
        Title = channel.Name;
        mediaTitle.Text = channel.Name;
        favorite.BindingContext = channel;
        message.Text = "A ligar à transmissão…";
        await player.PlayAsync(channel);
        await LoadGuideAsync();
    }

    private async Task LoadGuideAsync()
    {
        if (!liveChannel || AppServices.ActiveAccount is not { } account) return;
        var request = ++guideRequest;
        var channel = current;
        guideStatus.Text = "A carregar programação…";
        try
        {
            var guide = await EpgService.GetAsync(account, channel, lifetime.Token);
            if (request != guideRequest || lifetime.IsCancellationRequested ||
                CatalogPreferences.ItemKey(channel) != CatalogPreferences.ItemKey(current)) return;
            var now = DateTimeOffset.Now;
            var rows = guide.Where(programme => programme.End > now).Take(60)
                .Select(programme => new PlayerProgrammeRow(
                    programme.Title,
                    programme.Description,
                    programme.Schedule,
                    LanguageService.Text(programme.Status)))
                .ToArray();
            programmes.ItemsSource = rows;
            guideStatus.Text = rows.Length == 0
                ? "Sem programação disponível para este canal."
                : LanguageService.Format("{0} programas · Horário local do dispositivo", rows.Length);
        }
        catch (OperationCanceledException) { }
        catch
        {
            if (request != guideRequest) return;
            programmes.ItemsSource = null;
            guideStatus.Text = "Não foi possível obter o guia TV.";
        }
    }

    private void UpdateFullscreenOverlay()
    {
        var pip = PictureInPictureService.IsActive;
        fullscreenToolbar.IsVisible = fullscreen && !pip && player.AreControlsVisible && !channelMenu.IsVisible;
    }

    private void CloseChannelMenu()
    {
        channelMenu.IsVisible = false;
        UpdateFullscreenOverlay();
    }

    private async Task NextAsync()
    {
        if (!AppOptions.AutoPlay || countdown is not null || lifetime.IsCancellationRequested) return;
        var index = queue.ToList().FindIndex(i => CatalogPreferences.ItemKey(i) == CatalogPreferences.ItemKey(current));
        if (index < 0 || index + 1 >= queue.Count) return;
        countdown = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        cancelNext.IsVisible = true;
        nextMessage.IsVisible = true;
        try
        {
            for (var seconds = AppOptions.AutoPlaySeconds; seconds > 0; seconds--)
            { nextMessage.Text = LanguageService.Format("Próximo episódio em {0} s · {1}", seconds, queue[index + 1].Name); await Task.Delay(1000, countdown.Token); }
            countdown.Token.ThrowIfCancellationRequested();
            if (AppServices.ActiveAccount is null) return;
            // All episodes belong to the series authorized before opening this page.
            current = queue[index + 1]; Title = current.Name; mediaTitle.Text = current.Name; favorite.BindingContext = current; nextMessage.Text = "";
            await player.PlayAsync(current);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { await Ui.ErrorAsync(this, ex); }
        finally { cancelNext.IsVisible = false; nextMessage.IsVisible = false; countdown?.Dispose(); countdown = null; }
    }
    private void SetFullscreen(bool value)
    {
        fullscreen = value;
        if (!value) channelMenu.IsVisible = false;
        player.SetFullscreen(value);
        ScreenOrientationService.SetFullscreen(value);
        adaptLayout?.Invoke();
    }
    protected override bool OnBackButtonPressed() { if (!fullscreen) return base.OnBackButtonPressed(); SetFullscreen(false); return true; }
    private void Stop() { lifetime.Cancel(); countdown?.Cancel(); player.Stop(); }
    protected override void OnDisappearing()
    {
        if (!PictureInPictureService.IsActive) { Stop(); ScreenOrientationService.SetFullscreen(false); }
        base.OnDisappearing();
    }

    private sealed record PlayerProgrammeRow(string Title, string Description, string Schedule, string Status);
}
