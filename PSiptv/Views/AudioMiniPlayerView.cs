using PSiptv.Services;
using Microsoft.Maui.Controls.Shapes;

namespace PSiptv.Views;

public sealed class AudioMiniPlayerView : ContentView
{
    private readonly Label title = Ui.Text("", 14);
    private readonly Label position = Ui.Text("", 12, true);
    private readonly Button toggle;
    private readonly Button backward;
    private readonly Button forward;
    private readonly IDispatcherTimer timer;
    private readonly Slider timeline = new() { Minimum = 0, Maximum = 1 };
    private bool dragging;
    private PlaybackView? seekingPlayer;

    public AudioMiniPlayerView()
    {
        IsVisible = false;
        SemanticProperties.SetDescription(timeline, LanguageService.Text("Posição de reprodução"));
        timeline.DragStarted += (_, _) => { dragging = true; seekingPlayer = AudioPlaybackService.Player; };
        timeline.ValueChanged += (_, _) =>
        {
            if (dragging && seekingPlayer is { } player) UpdatePosition(timeline.Value, player.Duration);
        };
        timeline.DragCompleted += async (_, _) =>
        {
            var player = seekingPlayer;
            var target = timeline.Value;
            try
            {
                if (player is not null && player == AudioPlaybackService.Player) await player.SeekToAsync(target);
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
            finally { dragging = false; seekingPlayer = null; Refresh(); }
        };
        title.LineBreakMode = LineBreakMode.TailTruncation;
        toggle = Icon(FaIcons.Play, "Reproduzir ou pausar", async () =>
        {
            if (AudioPlaybackService.Player is { } player) await player.TogglePlayPauseAsync();
            Refresh();
        });
        backward = Ui.Button("−15 s", () => AudioPlaybackService.Player?.SeekByAsync(-15) ?? Task.CompletedTask);
        forward = Ui.Button("+30 s", () => AudioPlaybackService.Player?.SeekByAsync(30) ?? Task.CompletedTask);
        var stop = Icon("\uf04d", "Parar e fechar o leitor", AudioPlaybackService.StopAsync);
        var details = new VerticalStackLayout { Spacing = 4, Children = { title, position } };
        var open = new TapGestureRecognizer();
        open.Tapped += async (_, _) => await OpenCurrentAsync();
        details.GestureRecognizers.Add(open);
        var heading = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)] };
        heading.Add(details);
        heading.Add(Icon(FaIcons.ArrowUpRightFromSquare, "Voltar ao episódio em reprodução", OpenCurrentAsync), 1);
        var buttons = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new(GridLength.Star), new(GridLength.Star), new(GridLength.Star), new(GridLength.Star)
            },
            ColumnSpacing = 6
        };
        buttons.Add(backward, 0); buttons.Add(toggle, 1); buttons.Add(forward, 2); buttons.Add(stop, 3);
        Content = new Border
        {
            Margin = new Thickness(8, 4, 8, 8), Padding = 10,
            StrokeShape = new RoundRectangle { CornerRadius = 16 },
            Content = new VerticalStackLayout { Spacing = 4, Children = { heading, timeline, buttons } }
        };
        Content.SetDynamicResource(BackgroundColorProperty, "Surface");
        timer = Dispatcher.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(500);
        timer.Tick += (_, _) => Refresh();
        Loaded += (_, _) =>
        {
            AudioPlaybackService.Changed += Refresh;
            PictureInPictureService.Changed += OnPictureInPictureChanged;
            timer.Start(); Refresh();
        };
        Unloaded += (_, _) =>
        {
            AudioPlaybackService.Changed -= Refresh;
            PictureInPictureService.Changed -= OnPictureInPictureChanged;
            timer.Stop();
        };
    }

    private void OnPictureInPictureChanged() => Refresh();

    private async Task OpenCurrentAsync()
    {
        Element? ancestor = this;
        while (ancestor is not null && ancestor is not Page) ancestor = ancestor.Parent;
        if (ancestor is not Page page) return;
        try { await AudioPlaybackService.OpenCurrentAsync(page); }
        catch (Exception ex) { await Ui.ErrorAsync(page, ex); }
    }

    private void Refresh()
    {
        var player = AudioPlaybackService.Player;
        var item = AudioPlaybackService.Item;
        IsVisible = player is not null && item is not null && !PictureInPictureService.IsActive;
        if (player is null || item is null) return;
        title.Text = item.Name;
        if (!dragging)
        {
            var maximum = Math.Max(1, player.Duration);
            if (timeline.Value > maximum) timeline.Value = maximum;
            timeline.Maximum = maximum;
            timeline.Value = Math.Clamp(player.Position, 0, timeline.Maximum);
            if (player.Duration > 0) UpdatePosition(player.Position, player.Duration);
            else position.Text = LanguageService.Text(PortugueseRadioService.IsRadio(item) ? "Rádio em direto" : "Podcast");
        }
        timeline.IsEnabled = player.CanSeek && player.Duration > 0;
        toggle.Text = player.IsPlaying ? FaIcons.Pause : FaIcons.Play;
        SemanticProperties.SetDescription(toggle, LanguageService.Text(player.IsPlaying ? "Pausar" : "Reproduzir"));
        backward.IsEnabled = forward.IsEnabled = player.CanSeek;
    }

    private void UpdatePosition(double seconds, double duration) => position.Text =
        $"{TimeSpan.FromSeconds(Math.Max(0, seconds)):hh\\:mm\\:ss} / {TimeSpan.FromSeconds(Math.Max(0, duration)):hh\\:mm\\:ss}";

    private static Button Icon(string glyph, string description, Func<Task> action)
    {
        var button = Ui.Button(glyph, action);
        button.FontFamily = "FontAwesomeFreeSolid";
        SemanticProperties.SetDescription(button, LanguageService.Text(description));
        ToolTipProperties.SetText(button, LanguageService.Text(description));
        return button;
    }
}
