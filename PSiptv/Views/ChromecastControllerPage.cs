using PSiptv.Core;
using PSiptv.Services;

namespace PSiptv.Views;

public sealed class ChromecastControllerPage : LocalizedPage
{
    private readonly Label device = Ui.Text("A obter estado do Chromecast…", 15, true);
    private readonly Label title = Ui.Text("", 24);
    private readonly Label time = Ui.Text("", 13, true);
    private readonly Label volumeText = Ui.Text("", 13, true);
    private readonly ProgressBar progress = new();
    private readonly Slider volume = new() { Minimum = 0, Maximum = 1 };
    private readonly Button toggle;
    private readonly Button mute;
    private readonly Button previous;
    private readonly Button next;
    private readonly CancellationTokenSource lifetime = new();
    private bool updatingVolume;
    private bool started;

    public ChromecastControllerPage()
    {
        Ui.Page(this, "Controlo Chromecast");
        title.FontAttributes = FontAttributes.Bold;
        progress.SetDynamicResource(ProgressBar.ProgressColorProperty, "Accent");
        toggle = Ui.Button("Reproduzir", ScreenSharingService.ToggleChromecastPlaybackAsync, true);
        mute = Ui.Button("Silenciar", ScreenSharingService.ToggleChromecastMuteAsync);
        previous = Ui.Button("Anterior", ScreenSharingService.PreviousChromecastAsync);
        next = Ui.Button("Seguinte", ScreenSharingService.NextChromecastAsync);
        volume.DragCompleted += async (_, _) =>
        {
            if (!updatingVolume) await ScreenSharingService.SetChromecastVolumeAsync(volume.Value);
        };
        var transport = new Grid
        {
            ColumnDefinitions = [new(GridLength.Star), new(GridLength.Star), new(GridLength.Star)],
            ColumnSpacing = 8
        };
        transport.Add(Ui.Button("−30 s", () => ScreenSharingService.SeekChromecastAsync(-30_000)));
        transport.Add(toggle, 1);
        transport.Add(Ui.Button("+30 s", () => ScreenSharingService.SeekChromecastAsync(30_000)), 2);
        var queue = new Grid
        {
            ColumnDefinitions = [new(GridLength.Star), new(GridLength.Star)],
            ColumnSpacing = 8
        };
        queue.Add(previous);
        queue.Add(next, 1);
        var stack = Ui.Stack(
            Ui.Text("A reproduzir no recetor", 28), device,
            Ui.Card(Ui.Stack(title, progress, time)),
            transport, queue,
            Ui.Text("Volume do Chromecast"), volume, volumeText, mute,
            Ui.Button("Parar transmissão", ScreenSharingService.StopChromecastAsync),
            Ui.Button("Desligar Chromecast", DisconnectAsync));
        stack.Padding = Ui.IsTelevision ? 14 : 24;
        stack.MaximumWidthRequest = 760;
        Content = new ScrollView { Content = stack };
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (started) return;
        started = true;
        _ = RefreshLoopAsync();
    }

    private async Task RefreshLoopAsync()
    {
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                var state = await ScreenSharingService.GetChromecastStateAsync();
                if (!state.Supported)
                {
                    device.Text = LanguageService.Text("O controlo Chromecast avançado está disponível no Android.");
                    SetControls(false);
                    return;
                }
                if (!state.Connected)
                {
                    device.Text = LanguageService.Text("O Chromecast já não está ligado.");
                    title.Text = "";
                    SetControls(false);
                    return;
                }
                SetControls(true);
                device.Text = LanguageService.Format("Ligado a {0}", state.DeviceName);
                title.Text = state.Title.Length > 0 ? state.Title : LanguageService.Text("Conteúdo no Chromecast");
                toggle.Text = LanguageService.Text(state.IsPlaying ? "Pausar" : "Reproduzir");
                progress.Progress = state.DurationMilliseconds > 0
                    ? Math.Clamp((double)state.PositionMilliseconds / state.DurationMilliseconds, 0, 1) : 0;
                time.Text = state.DurationMilliseconds > 0
                    ? $"{FormatTime(state.PositionMilliseconds)} / {FormatTime(state.DurationMilliseconds)}"
                    : LanguageService.Text("Transmissão em direto ou duração indisponível");
                updatingVolume = true;
                volume.Value = state.Volume;
                updatingVolume = false;
                volumeText.Text = LanguageService.Format("Volume {0}%", Math.Round(state.Volume * 100));
                mute.Text = LanguageService.Text(state.Muted ? "Ativar som" : "Silenciar");
                previous.IsVisible = next.IsVisible = state.HasQueue;
                await Task.Delay(1000, lifetime.Token);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { await Ui.ErrorAsync(this, ex); }
    }

    private void SetControls(bool enabled)
    {
        toggle.IsEnabled = mute.IsEnabled = volume.IsEnabled = enabled;
        previous.IsEnabled = next.IsEnabled = enabled;
    }

    private async Task DisconnectAsync()
    {
        await ScreenSharingService.DisconnectChromecastAsync();
        if (Navigation.NavigationStack.Contains(this)) await Navigation.PopAsync();
    }

    private static string FormatTime(long milliseconds)
    {
        var value = TimeSpan.FromMilliseconds(Math.Max(0, milliseconds));
        return value.TotalHours >= 1 ? value.ToString(@"h\:mm\:ss") : value.ToString(@"m\:ss");
    }

    protected override void OnNavigatedFrom(NavigatedFromEventArgs args)
    {
        base.OnNavigatedFrom(args);
        if (!Navigation.NavigationStack.Contains(this)) lifetime.Cancel();
    }
}
