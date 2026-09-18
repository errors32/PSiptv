using Microsoft.Maui.Controls.Shapes;
using PSiptv.Core;
using PSiptv.Services;

namespace PSiptv.Views;

public sealed class MultiviewPage : LocalizedPage
{
    public const int MaximumChannels = 4;

    private readonly IReadOnlyList<MediaItem> availableChannels;
    private readonly MediaItem? initialChannel;
    private readonly MultiviewSlot[] slots = new MultiviewSlot[MaximumChannels];
    private readonly Label status = Ui.Text("Selecione um painel para ouvir o respetivo canal.", 13, true);
    private bool started;
    private bool choosingChannel;
    private bool stopped;
    private int activeSlot = -1;

    public MultiviewPage(IReadOnlyList<MediaItem> channels, MediaItem? initialChannel = null)
    {
        availableChannels = channels
            .Where(item => item.Kind == MediaKind.Channel && !item.IsCatchup)
            .GroupBy(CatalogPreferences.ItemKey)
            .Select(group => group.First())
            .ToArray();
        this.initialChannel = initialChannel is { Kind: MediaKind.Channel, IsCatchup: false }
            ? initialChannel : null;

        Ui.Page(this, "Multiview");
        var add = Ui.Button("Adicionar canal", AddChannelAsync, true);
        add.ImageSource = Ui.FontIconSource(FaIcons.Plus, 18);
        var header = new Grid
        {
            ColumnSpacing = 10,
            ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)]
        };
        header.Add(Ui.Stack(Ui.Text("Multiview", 24), status));
        header.Add(add, 1);

        var mosaic = new Grid
        {
            RowSpacing = 6,
            ColumnSpacing = 6,
            RowDefinitions = [new(GridLength.Star), new(GridLength.Star)],
            ColumnDefinitions = [new(GridLength.Star), new(GridLength.Star)]
        };
        for (var index = 0; index < MaximumChannels; index++)
        {
            var slot = new MultiviewSlot(index, ChooseChannelAsync, RemoveChannelAsync, SetActiveSlot);
            slots[index] = slot;
            mosaic.Add(slot.Frame, index % 2, index / 2);
        }

        var root = new Grid
        {
            Padding = Ui.IsTelevision ? new Thickness(20, 10) : new Thickness(10),
            RowSpacing = 8,
            SafeAreaEdges = new SafeAreaEdges(SafeAreaRegions.Container),
            RowDefinitions = [new(GridLength.Auto), new(GridLength.Star)]
        };
        root.Add(header);
        root.Add(mosaic, 0, 1);
        Content = root;

        AppServices.Locked += StopAll;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (stopped) return;
        ScreenOrientationService.SetFullscreen(true);
        SetKeepScreenOn(true);
        if (started) return;
        started = true;
        if (initialChannel is not null) await AssignChannelAsync(0, initialChannel, authorize: false);
    }

    private Task AddChannelAsync()
    {
        var empty = Array.FindIndex(slots, slot => slot.Item is null);
        if (empty < 0)
            return LanguageService.AlertAsync(this, "Multiview", "O Multiview permite até quatro canais em simultâneo.");
        return ChooseChannelAsync(empty);
    }

    private async Task ChooseChannelAsync(int slotIndex)
    {
        if (choosingChannel || stopped) return;
        var selectedKeys = slots
            .Where((_, index) => index != slotIndex)
            .Select(slot => slot.Item)
            .Where(item => item is not null)
            .Select(item => CatalogPreferences.ItemKey(item!))
            .ToHashSet();
        var choices = availableChannels
            .Where(item => !selectedKeys.Contains(CatalogPreferences.ItemKey(item)))
            .ToArray();
        if (choices.Length == 0)
        {
            await LanguageService.AlertAsync(this, "Multiview", "Não existem outros canais disponíveis nesta categoria.");
            return;
        }

        var labels = choices.Select((item, index) => $"{index + 1}. {item.Name} · {item.Category}").ToArray();
        choosingChannel = true;
        string? selected;
        try { selected = await CategoryPickerPage.ChooseAsync(this, "Escolher canal", labels, null); }
        finally { choosingChannel = false; }
        var selectedIndex = Array.IndexOf(labels, selected);
        if (selectedIndex < 0 || stopped) return;
        await AssignChannelAsync(slotIndex, choices[selectedIndex], authorize: true);
    }

    private async Task AssignChannelAsync(int slotIndex, MediaItem item, bool authorize)
    {
        if (stopped || AppServices.ActiveAccount is null) return;
        if (authorize && !await CatalogOptionsService.AuthorizePlaybackAsync(this, item)) return;

        var slot = slots[slotIndex];
        slot.Player.Stop();
        slot.SetItem(item);
        SetActiveSlot(slotIndex);
        try { await slot.Player.PlayAsync(item); }
        catch (Exception ex)
        {
            slot.SetError(LanguageService.Text("Não foi possível reproduzir este canal."));
            await Ui.ErrorAsync(this, ex);
        }
    }

    private Task RemoveChannelAsync(int slotIndex)
    {
        var slot = slots[slotIndex];
        slot.Player.Stop();
        slot.SetItem(null);
        if (activeSlot == slotIndex)
        {
            activeSlot = Array.FindIndex(slots, candidate => candidate.Item is not null);
            ApplyActiveSlot();
        }
        return Task.CompletedTask;
    }

    private void SetActiveSlot(int slotIndex)
    {
        if (slots[slotIndex].Item is null) return;
        activeSlot = slotIndex;
        ApplyActiveSlot();
    }

    private void ApplyActiveSlot()
    {
        for (var index = 0; index < slots.Length; index++)
            slots[index].SetActive(index == activeSlot);
        status.Text = activeSlot >= 0 && slots[activeSlot].Item is { } item
            ? LanguageService.Format("Áudio ativo · {0}", item.Name)
            : LanguageService.Text("Selecione um painel para ouvir o respetivo canal.");
    }

    private void StopAll()
    {
        if (stopped) return;
        stopped = true;
        AppServices.Locked -= StopAll;
        foreach (var slot in slots) slot.Player.Stop();
        SetKeepScreenOn(false);
        ScreenOrientationService.SetFullscreen(false);
    }

    private static void SetKeepScreenOn(bool value)
    {
        try { DeviceDisplay.Current.KeepScreenOn = value; }
        catch (FeatureNotSupportedException) { }
    }

    protected override void OnDisappearing()
    {
        if (!choosingChannel) StopAll();
        base.OnDisappearing();
    }

    private sealed class MultiviewSlot
    {
        private readonly int index;
        private readonly Label title = Ui.Text("Adicionar canal", 14);
        private readonly Label playbackStatus = Ui.Text("", 12, true);
        private readonly Button audio;
        private readonly Button change;
        private readonly Button remove;
        private readonly Grid overlay;
        public PlaybackView Player { get; } = new(compactMode: true);
        public Border Frame { get; }
        public MediaItem? Item { get; private set; }

        public MultiviewSlot(int index, Func<int, Task> choose, Func<int, Task> remove, Action<int> activate)
        {
            this.index = index;
            Player.UseStretchToViewport();
            Player.InputTransparent = true;
            Player.MediaOpened += (_, _) => playbackStatus.Text = "";
            Player.StatusChanged += text => playbackStatus.Text = text;
            Player.MediaFailed += (_, _) =>
            {
                if (string.IsNullOrWhiteSpace(playbackStatus.Text))
                    playbackStatus.Text = LanguageService.Text("Não foi possível reproduzir este canal.");
            };

            title.TextColor = Colors.White;
            title.FontAttributes = FontAttributes.Bold;
            title.MaxLines = 1;
            title.LineBreakMode = LineBreakMode.TailTruncation;
            playbackStatus.TextColor = Colors.White;

            audio = TileButton(FaIcons.VolumeXmark, () => { activate(index); return Task.CompletedTask; }, "Ativar áudio deste canal");
            change = TileButton(FaIcons.List, () => choose(index), "Trocar canal");
            this.remove = TileButton("×", () => remove(index), "Remover canal");
            var actions = new HorizontalStackLayout
            {
                Spacing = 4,
                HorizontalOptions = LayoutOptions.End,
                Children = { audio, change, this.remove }
            };
            overlay = new Grid
            {
                Padding = new Thickness(8, 5),
                VerticalOptions = LayoutOptions.End,
                BackgroundColor = Color.FromArgb("#B30F172C"),
                ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)]
            };
            overlay.Add(Ui.Stack(title, playbackStatus));
            overlay.Add(actions, 1);

            var content = new Grid { BackgroundColor = Colors.Black };
            content.Add(Player);
            content.Add(overlay);
            Frame = new Border
            {
                Content = content,
                StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = 10 },
                BackgroundColor = Colors.Black
            };
            Frame.SetDynamicResource(Border.StrokeProperty, "Accent");
            var tap = new TapGestureRecognizer();
            tap.Tapped += (_, _) =>
            {
                if (Item is null) _ = choose(index);
                else activate(index);
            };
            Frame.GestureRecognizers.Add(tap);
            SetItem(null);
        }

        public void SetItem(MediaItem? item)
        {
            Item = item;
            title.Text = item?.Name ?? LanguageService.Text("Adicionar canal");
            playbackStatus.Text = item is null ? LanguageService.Text("Toque para escolher") : LanguageService.Text("A ligar à transmissão…");
            audio.IsVisible = change.IsVisible = remove.IsVisible = item is not null;
            Player.IsVisible = item is not null;
            Frame.StrokeThickness = 0;
        }

        public void SetActive(bool active)
        {
            Player.Volume = active ? 100 : 0;
            Frame.StrokeThickness = active ? 4 : 0;
            audio.ImageSource = Ui.FontIconSource(active ? FaIcons.VolumeHigh : FaIcons.VolumeXmark, 16);
            if (Item is not null)
                SemanticProperties.SetDescription(audio, LanguageService.Text(active ? "Áudio ativo" : "Ativar áudio deste canal"));
        }

        public void SetError(string message) => playbackStatus.Text = message;

        private static Button TileButton(string glyph, Func<Task> action, string description)
        {
            var button = Ui.Button("", action);
            if (glyph == "×") button.Text = glyph;
            else button.ImageSource = Ui.FontIconSource(glyph, 16);
            button.WidthRequest = 40;
            button.MinimumHeightRequest = 36;
            button.Padding = 6;
            SemanticProperties.SetDescription(button, LanguageService.Text(description));
            return button;
        }
    }
}
