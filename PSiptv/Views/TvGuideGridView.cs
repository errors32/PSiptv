using System.ComponentModel;
using System.Runtime.CompilerServices;
using PSiptv.Core;
using PSiptv.Services;

namespace PSiptv.Views;

public sealed class TvGuideGridView : ContentView
{
    private const double ChannelWidth = 116;
    private const double PixelsPerMinute = 3.5;
    private readonly CollectionView rows;
    private readonly ScrollView timelineScroll;
    private readonly HorizontalStackLayout timeline = new() { Spacing = 0 };
    private readonly Grid root;
    private readonly BoxView nowLine;
    private readonly Label nowLabel;
    private readonly List<WeakReference<ScrollView>> synchronizedScrolls = [];
    private IReadOnlyList<MediaItem> channels = [];
    private CancellationTokenSource? loading;
    private DateTimeOffset windowStart;
    private DateTimeOffset windowEnd;
    private double scrollX;
    private bool synchronizing;
    private bool active;
    private Timer? clock;

    public Func<MediaItem, Task>? PlayRequested { get; set; }
    public Func<MediaItem, TvProgramme, Task>? ReminderRequested { get; set; }

    public TvGuideGridView()
    {
        timelineScroll = NewHorizontalScroll(timeline);
        var corner = Ui.Text("Canal", 12, true);
        corner.WidthRequest = ChannelWidth;
        corner.Padding = new Thickness(8, 0);
        var header = new Grid
        {
            HeightRequest = 32,
            ColumnSpacing = 0,
            ColumnDefinitions = [new(ChannelWidth), new(GridLength.Star)]
        };
        header.Add(corner);
        header.Add(timelineScroll, 1);

        rows = new CollectionView
        {
            SelectionMode = SelectionMode.None,
            EmptyView = Ui.Text("Nenhum canal neste grupo.", 15, true),
            ItemsLayout = new LinearItemsLayout(ItemsLayoutOrientation.Vertical) { ItemSpacing = 6 },
            ItemTemplate = new DataTemplate(CreateChannelRow)
        };
        root = new Grid
        {
            RowSpacing = 4,
            IsClippedToBounds = true,
            RowDefinitions = [new(GridLength.Auto), new(GridLength.Star)]
        };
        root.Add(header);
        root.Add(rows, 0, 1);
        nowLine = new BoxView
        {
            WidthRequest = 2,
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Fill,
            Margin = new Thickness(0, 25, 0, 0),
            InputTransparent = true
        };
        nowLine.SetDynamicResource(BackgroundColorProperty, "Accent");
        Grid.SetRowSpan(nowLine, 2);
        root.Add(nowLine);
        nowLabel = Ui.Text("", 10);
        nowLabel.WidthRequest = 54;
        nowLabel.HeightRequest = 24;
        nowLabel.Padding = new Thickness(4, 2);
        nowLabel.HorizontalTextAlignment = TextAlignment.Center;
        nowLabel.VerticalTextAlignment = TextAlignment.Center;
        nowLabel.HorizontalOptions = LayoutOptions.Start;
        nowLabel.VerticalOptions = LayoutOptions.Start;
        nowLabel.InputTransparent = true;
        nowLabel.TextColor = Color.FromArgb("#071520");
        nowLabel.SetDynamicResource(BackgroundColorProperty, "Accent");
        root.Add(nowLabel);
        Content = root;
        Loaded += (_, _) =>
        {
            clock?.Dispose();
            clock = new Timer(_ => Dispatcher.Dispatch(UpdateNowIndicator), null, TimeSpan.Zero, TimeSpan.FromSeconds(30));
        };
        Unloaded += (_, _) => { clock?.Dispose(); clock = null; };
    }

    public void SetActive(bool value)
    {
        active = value;
        IsVisible = value;
        nowLine.IsVisible = value;
        nowLabel.IsVisible = value;
        if (value) StartLoading();
        else loading?.Cancel();
        UpdateNowIndicator();
    }

    public void SetChannels(IReadOnlyList<MediaItem> items)
    {
        channels = items;
        if (active) StartLoading();
    }

    private View CreateChannelRow()
    {
        var logo = new LogoImage { WidthRequest = 28, HeightRequest = 28, Aspect = Aspect.AspectFit };
        logo.SetBinding(BindingContextProperty, nameof(GuideChannelRow.Channel));
        var name = Ui.Text("", 11);
        name.MaxLines = 2;
        name.LineBreakMode = LineBreakMode.TailTruncation;
        name.SetBinding(Label.TextProperty, nameof(GuideChannelRow.Name));
        var channel = new Grid
        {
            Padding = new Thickness(5, 4),
            ColumnSpacing = 5,
            ColumnDefinitions = [new(32), new(GridLength.Star)]
        };
        channel.SetDynamicResource(BackgroundColorProperty, "Surface");
        channel.Add(logo);
        channel.Add(name, 1);
        var channelTap = new TapGestureRecognizer();
        channelTap.Tapped += async (sender, _) =>
        {
            if (sender is BindableObject { BindingContext: GuideChannelRow row } && PlayRequested is not null)
                await PlayRequested(row.Channel);
        };
        channel.GestureRecognizers.Add(channelTap);

        var programmes = new HorizontalStackLayout { Spacing = 0, HeightRequest = 70 };
        BindableLayout.SetItemsSource(programmes, null);
        BindableLayout.SetItemTemplate(programmes, new DataTemplate(CreateProgrammeCell));
        programmes.SetBinding(BindableLayout.ItemsSourceProperty, nameof(GuideChannelRow.Cells));
        var programmeScroll = NewHorizontalScroll(programmes);
        programmeScroll.Loaded += async (_, _) => await programmeScroll.ScrollToAsync(scrollX, 0, false);

        var rowGrid = new Grid
        {
            HeightRequest = 70,
            ColumnSpacing = 4,
            ColumnDefinitions = [new(ChannelWidth), new(GridLength.Star)]
        };
        rowGrid.Add(channel);
        rowGrid.Add(programmeScroll, 1);
        return rowGrid;
    }

    private View CreateProgrammeCell()
    {
        var title = Ui.Text("", 11);
        title.MaxLines = 2;
        title.LineBreakMode = LineBreakMode.TailTruncation;
        title.SetBinding(Label.TextProperty, nameof(GuideProgrammeCell.Title));
        var time = Ui.Text("", 10, true);
        time.SetBinding(Label.TextProperty, nameof(GuideProgrammeCell.Time));
        var content = Ui.Stack(title, time);
        content.Spacing = 2;
        var card = Ui.Card(content);
        card.Padding = new Thickness(7, 5);
        card.SetBinding(WidthRequestProperty, nameof(GuideProgrammeCell.Width));
        card.SetBinding(OpacityProperty, nameof(GuideProgrammeCell.Opacity));
        var tap = new TapGestureRecognizer();
        tap.Tapped += async (sender, _) =>
        {
            if (sender is not BindableObject { BindingContext: GuideProgrammeCell cell }) return;
            if (cell.PlayItem is { } item && PlayRequested is not null)
                await PlayRequested(item);
            else if (cell.Channel is { } channel && cell.Programme is { } programme && ReminderRequested is not null)
                await ReminderRequested(channel, programme);
        };
        card.GestureRecognizers.Add(tap);
        return card;
    }

    private ScrollView NewHorizontalScroll(View content)
    {
        var scroll = new ScrollView
        {
            Orientation = ScrollOrientation.Horizontal,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Never,
            Content = content
        };
        synchronizedScrolls.Add(new(scroll));
        scroll.Scrolled += (_, e) => SynchronizeScroll(scroll, e.ScrollX);
        return scroll;
    }

    private void SynchronizeScroll(ScrollView source, double x)
    {
        if (synchronizing || Math.Abs(x - scrollX) < .5) return;
        scrollX = x;
        synchronizing = true;
        synchronizedScrolls.RemoveAll(reference => !reference.TryGetTarget(out _));
        foreach (var reference in synchronizedScrolls)
            if (reference.TryGetTarget(out var target) && target != source && Math.Abs(target.ScrollX - x) >= .5)
                _ = target.ScrollToAsync(x, 0, false);
        synchronizing = false;
        UpdateNowIndicator();
    }

    private void StartLoading()
    {
        loading?.Cancel();
        loading?.Dispose();
        loading = new CancellationTokenSource();
        var now = DateTimeOffset.Now;
        var rounded = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, now.Minute < 30 ? 0 : 30, 0, now.Offset);
        windowStart = rounded.AddMinutes(-90);
        windowEnd = rounded.AddHours(8);
        BuildTimeline();
        // Keep the current-time marker inside the programme area instead of
        // exactly against the fixed channel column.
        scrollX = 60 * PixelsPerMinute;
        _ = timelineScroll.ScrollToAsync(scrollX, 0, false);
        var models = channels.Select(channel => new GuideChannelRow(channel, PlaceholderCells())).ToArray();
        rows.ItemsSource = models;
        UpdateNowIndicator();
        _ = LoadRowsAsync(models, loading.Token);
    }

    private void UpdateNowIndicator()
    {
        if (!active || windowStart == default || Width <= 0)
        {
            nowLine.IsVisible = false;
            nowLabel.IsVisible = false;
            return;
        }
        var now = DateTimeOffset.Now;
        var x = ChannelWidth + 4 + (now - windowStart).TotalMinutes * PixelsPerMinute - scrollX;
        var visible = now >= windowStart && now <= windowEnd && x >= ChannelWidth && x <= Width;
        nowLine.IsVisible = visible;
        nowLabel.IsVisible = visible;
        if (!visible) return;
        nowLine.TranslationX = x;
        nowLabel.TranslationX = x - nowLabel.WidthRequest / 2;
        nowLabel.Text = now.ToString(AppOptions.Clock12 ? "h:mm tt" : "HH:mm");
    }

    private void BuildTimeline()
    {
        timeline.Clear();
        for (var time = windowStart; time < windowEnd; time = time.AddMinutes(30))
        {
            var label = Ui.Text(time.ToString(AppOptions.Clock12 ? "h:mm tt" : "HH:mm"), 10, true);
            label.WidthRequest = 30 * PixelsPerMinute;
            label.Padding = new Thickness(5, 0);
            timeline.Add(label);
        }
    }

    private async Task LoadRowsAsync(GuideChannelRow[] models, CancellationToken token)
    {
        if (AppServices.ActiveAccount is not { } account) return;
        var index = -1;
        async Task Worker()
        {
            while (true)
            {
                var current = Interlocked.Increment(ref index);
                if (current >= models.Length) return;
                token.ThrowIfCancellationRequested();
                var model = models[current];
                try
                {
                    var programmes = await EpgService.GetAsync(account, model.Channel, token);
                    token.ThrowIfCancellationRequested();
                    var cells = BuildCells(account, model.Channel, programmes);
                    Dispatcher.Dispatch(() => { if (!token.IsCancellationRequested) model.Cells = cells; });
                }
                catch (OperationCanceledException) { return; }
                catch
                {
                    Dispatcher.Dispatch(() => { if (!token.IsCancellationRequested) model.Cells = NoInformationCells(); });
                }
            }
        }
        await Task.WhenAll(Enumerable.Range(0, Math.Min(4, models.Length)).Select(_ => Worker()));
    }

    private IReadOnlyList<GuideProgrammeCell> BuildCells(PlaylistAccount account, MediaItem channel, IReadOnlyList<TvProgramme> programmes)
    {
        var result = new List<GuideProgrammeCell>();
        var cursor = windowStart;
        foreach (var programme in programmes.Where(p => p.End > windowStart && p.Start < windowEnd).OrderBy(p => p.Start))
        {
            var start = programme.Start < windowStart ? windowStart : programme.Start;
            var end = programme.End > windowEnd ? windowEnd : programme.End;
            if (start > cursor) result.Add(Gap(start - cursor));
            start = start < cursor ? cursor : start;
            if (end <= start) continue;
            var archive = CatchupStream.Create(account, channel, programme, DateTimeOffset.Now);
            var isCurrent = programme.Start <= DateTimeOffset.Now && programme.End > DateTimeOffset.Now;
            var time = programme.Start.ToLocalTime().ToString(AppOptions.Clock12 ? "h:mm tt" : "HH:mm");
            if (isCurrent) time = LanguageService.Text("AGORA") + " · " + time;
            else if (archive is not null) time = LanguageService.Text("REVER") + " · " + time;
            else if (programme.Start > DateTimeOffset.Now) time = "🔔 " + time;
            result.Add(new(programme.Title, time, Math.Max(1, (end - start).TotalMinutes * PixelsPerMinute),
                1, archive ?? (isCurrent ? channel : null), channel, programme.Start > DateTimeOffset.Now ? programme : null));
            cursor = end;
        }
        if (cursor < windowEnd) result.Add(Gap(windowEnd - cursor));
        return result;
    }

    private IReadOnlyList<GuideProgrammeCell> PlaceholderCells() =>
        [new(LanguageService.Text("A carregar…"), "", (windowEnd - windowStart).TotalMinutes * PixelsPerMinute, 1, null)];
    private IReadOnlyList<GuideProgrammeCell> NoInformationCells() =>
        [new(LanguageService.Text("Sem informação"), "", (windowEnd - windowStart).TotalMinutes * PixelsPerMinute, 1, null)];
    private static GuideProgrammeCell Gap(TimeSpan duration) => new("", "", Math.Max(1, duration.TotalMinutes * PixelsPerMinute), 0, null);

    private sealed class GuideChannelRow(MediaItem channel, IReadOnlyList<GuideProgrammeCell> cells) : INotifyPropertyChanged
    {
        private IReadOnlyList<GuideProgrammeCell> cells = cells;
        public MediaItem Channel { get; } = channel;
        public string Name => Channel.Name;
        public IReadOnlyList<GuideProgrammeCell> Cells
        {
            get => cells;
            set { cells = value; OnPropertyChanged(); }
        }
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? propertyName = null) => PropertyChanged?.Invoke(this, new(propertyName));
    }

    private sealed record GuideProgrammeCell(string Title, string Time, double Width, double Opacity,
        MediaItem? PlayItem, MediaItem? Channel = null, TvProgramme? Programme = null);
}
