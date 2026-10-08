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

    public AudioMiniPlayerView()
    {
        IsVisible = false;
        title.LineBreakMode = LineBreakMode.TailTruncation;
        toggle = Icon(FaIcons.Play, "Reproduzir ou pausar", async () =>
        {
            if (AudioPlaybackService.Player is { } player) await player.TogglePlayPauseAsync();
            Refresh();
        });
        backward = Ui.Button("−15 s", () => AudioPlaybackService.Player?.SeekByAsync(-15) ?? Task.CompletedTask);
        forward = Ui.Button("+30 s", () => AudioPlaybackService.Player?.SeekByAsync(30) ?? Task.CompletedTask);
        var stop = Icon("\uf04d", "Parar e fechar o leitor", AudioPlaybackService.StopAsync);
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
            Content = new VerticalStackLayout { Spacing = 4, Children = { title, position, buttons } }
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

    private void Refresh()
    {
        var player = AudioPlaybackService.Player;
        var item = AudioPlaybackService.Item;
        IsVisible = player is not null && item is not null && !PictureInPictureService.IsActive;
        if (player is null || item is null) return;
        title.Text = item.Name;
        position.Text = player.Duration > 0
            ? $"{TimeSpan.FromSeconds(Math.Max(0, player.Position)):hh\\:mm\\:ss} / {TimeSpan.FromSeconds(player.Duration):hh\\:mm\\:ss}"
            : LanguageService.Text(PortugueseRadioService.IsRadio(item) ? "Rádio em direto" : "Podcast");
        toggle.Text = player.IsPlaying ? FaIcons.Pause : FaIcons.Play;
        SemanticProperties.SetDescription(toggle, LanguageService.Text(player.IsPlaying ? "Pausar" : "Reproduzir"));
        backward.IsEnabled = forward.IsEnabled = player.CanSeek;
    }

    private static Button Icon(string glyph, string description, Func<Task> action)
    {
        var button = Ui.Button(glyph, action);
        button.FontFamily = "FontAwesomeFreeSolid";
        SemanticProperties.SetDescription(button, LanguageService.Text(description));
        ToolTipProperties.SetText(button, LanguageService.Text(description));
        return button;
    }
}
