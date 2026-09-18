using PSiptv.Core;
using PSiptv.Services;

namespace PSiptv.Views;

public sealed class MediaDetailsPage : LocalizedPage
{
    private readonly PlaylistAccount account;
    private MediaItem item;
    private readonly CancellationTokenSource lifetime = new();
    private readonly LogoImage artwork = new() { Aspect = Aspect.AspectFill, HeightRequest = 220 };
    private readonly Label title = Ui.Text("", 27);
    private readonly Label metadata = Ui.Text("", 13, true);
    private readonly Label plot = Ui.Text("", 15);
    private readonly Label cast = Ui.Text("", 13, true);
    private readonly Label director = Ui.Text("", 13, true);
    private readonly Label status = Ui.Text("A carregar detalhes…", 13, true);
    private readonly FavoriteButton favorite = new();
    private readonly Button primary;
    private readonly Button trailer;
    private readonly Button download;
    private MediaDetails? details;
    private bool loaded;

    public MediaDetailsPage(PlaylistAccount account, MediaItem item)
    {
        this.account = account;
        this.item = item;
        Ui.Page(this, item.Name);
        title.Text = item.Name;
        title.FontAttributes = FontAttributes.Bold;
        favorite.BindingContext = item;
        artwork.BindingContext = item;
        var heading = new Grid
        {
            ColumnSpacing = 8,
            ColumnDefinitions = [new(GridLength.Star), new(52)]
        };
        heading.Add(title);
        heading.Add(favorite, 1);
        primary = Ui.Button(item.HasEpisodes ? "Ver episódios" : "Reproduzir", PrimaryAsync, true);
        trailer = Ui.Button("Ver trailer", OpenTrailerAsync);
        trailer.IsVisible = false;
        download = Ui.Button("Descarregar", DownloadAsync);
        download.IsVisible = !item.HasEpisodes;
        var actions = new Grid
        {
            ColumnSpacing = 8,
            RowSpacing = 8,
            ColumnDefinitions = [new(GridLength.Star), new(GridLength.Star)]
        };
        actions.Add(primary);
        actions.Add(trailer, 1);
        actions.Add(download, 0, 1);
        Grid.SetColumnSpan(download, 2);
        var information = Ui.Stack(heading, metadata, status, actions,
            Section("Sinopse", plot), Section("Elenco", cast), Section("Realização", director));
        var body = Ui.Stack(artwork, information);
        body.Spacing = 16;
        body.Padding = new Thickness(16, 12, 16, 24);
        body.MaximumWidthRequest = 900;
        Content = new ScrollView { Content = body };
        AppServices.Locked += Clear;
        SizeChanged += (_, _) =>
        {
            var viewport = Ui.Viewport(this);
            artwork.HeightRequest = Ui.IsTelevision
                ? Math.Clamp(viewport.Height * 0.34, 150, 250)
                : Math.Clamp(viewport.Width * 9 / 16, 180, 420);
        };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await RefreshDownloadAsync();
        if (loaded) return;
        loaded = true;
        try
        {
            details = await AppServices.Client.GetDetailsAsync(account, item, lifetime.Token);
            if (AppServices.ActiveAccount?.Id != account.Id || lifetime.IsCancellationRequested) return;
            item = details.Item;
            Title = item.Name;
            title.Text = item.Name;
            favorite.BindingContext = item;
            artwork.BindingContext = item with { Logo = details.Backdrop.Length > 0 ? details.Backdrop : item.Logo };
            metadata.Text = BuildMetadata(details);
            plot.Text = details.Plot.Length > 0 ? details.Plot : LanguageService.Text("Sem sinopse disponível.");
            cast.Text = details.Cast.Length > 0 ? details.Cast : LanguageService.Text("Sem informação disponível.");
            director.Text = details.Director.Length > 0 ? details.Director : LanguageService.Text("Sem informação disponível.");
            trailer.IsVisible = details.TrailerUrl.Length > 0;
            primary.Text = LanguageService.Text(item.HasEpisodes ? "Ver episódios" : "Reproduzir");
            download.IsVisible = !item.HasEpisodes;
            await RefreshDownloadAsync();
            status.Text = item.HasEpisodes && details.EpisodeItems.Count > 0
                ? LanguageService.Format("{0} episódios disponíveis", details.EpisodeItems.Count) : "";
        }
        catch (OperationCanceledException) { }
        catch
        {
            status.Text = "Não foi possível obter informação adicional. A reprodução continua disponível.";
            plot.Text = LanguageService.Text("Sem sinopse disponível.");
        }
    }

    private async Task DownloadAsync()
    {
        if (!await CatalogOptionsService.AuthorizePlaybackAsync(this, item)) return;
        var existing = await OfflineDownloadService.FindAsync(account.Id, UserProfileService.Active.Id, item);
        if (existing?.IsComplete == true)
        {
            await Navigation.PushAsync(new PlayerPage(OfflineDownloadService.PlaybackItem(existing)));
            return;
        }
        if (existing?.State is OfflineDownloadState.Paused or OfflineDownloadState.Failed)
            await OfflineDownloadService.ResumeAsync(existing);
        else if (existing is null)
            await OfflineDownloadService.QueueAsync(account, item);
        else
            await Navigation.PushAsync(new OfflineDownloadsPage());
        await RefreshDownloadAsync();
    }

    private async Task RefreshDownloadAsync()
    {
        if (item.HasEpisodes) return;
        var existing = await OfflineDownloadService.FindAsync(account.Id, UserProfileService.Active.Id, item);
        download.Text = LanguageService.Text(existing?.State switch
        {
            OfflineDownloadState.Completed => "Reproduzir offline",
            OfflineDownloadState.Downloading or OfflineDownloadState.Queued => "Ver download",
            OfflineDownloadState.Paused or OfflineDownloadState.Failed => "Retomar download",
            _ => "Descarregar"
        });
    }

    private async Task PrimaryAsync()
    {
        if (AppServices.ActiveAccount?.Id != account.Id) return;
        if (item.HasEpisodes)
            await Navigation.PushAsync(new EpisodesPage(account, item, details?.EpisodeItems));
        else await PlaybackService.PlayAsync(this, item);
    }

    private async Task OpenTrailerAsync()
    {
        if (details?.TrailerUrl is not { Length: > 0 } url) return;
        if (!await Launcher.Default.TryOpenAsync(new Uri(url)))
            await LanguageService.AlertAsync(this, "Trailer", "Não foi possível abrir o trailer.");
    }

    private static View Section(string heading, Label content)
    {
        var label = Ui.Text(heading, 18);
        label.FontAttributes = FontAttributes.Bold;
        return Ui.Stack(label, content);
    }

    private static string BuildMetadata(MediaDetails details)
    {
        var values = new[]
        {
            details.ReleaseDate, details.Genre, details.Duration,
            details.Rating.Length > 0 ? "★ " + details.Rating : ""
        }.Where(value => !string.IsNullOrWhiteSpace(value));
        return string.Join("  ·  ", values);
    }

    private void Clear() { lifetime.Cancel(); }
    protected override void OnNavigatedFrom(NavigatedFromEventArgs args)
    {
        base.OnNavigatedFrom(args);
        if (!Navigation.NavigationStack.Contains(this))
        {
            Clear();
            AppServices.Locked -= Clear;
            lifetime.Dispose();
        }
    }
}
