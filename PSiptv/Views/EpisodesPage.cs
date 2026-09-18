using PSiptv.Core;
using PSiptv.Services;

namespace PSiptv.Views;

public sealed class EpisodesPage : LocalizedPage
{
    private readonly PlaylistAccount account;
    private readonly MediaItem series;
    private readonly CollectionView episodes = Ui.MediaList();
    private readonly Label status = Ui.Text("Use «Atualizar episódios» para carregar os episódios.", 14, true);
    private readonly CancellationTokenSource lifetime = new();
    private readonly IReadOnlyList<MediaItem>? initialEpisodes;

    public EpisodesPage(PlaylistAccount account, MediaItem series, IReadOnlyList<MediaItem>? initialEpisodes = null)
    {
        this.account = account; this.series = series; this.initialEpisodes = initialEpisodes;
        Ui.Page(this, series.Name);
        var grid = new Grid { Padding = 20, RowSpacing = 12, RowDefinitions = [new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star)] };
        grid.Add(Ui.Stack(Ui.Text(series.Name, 26), status, Ui.Button("Atualizar episódios", LoadAsync)));
        grid.Add(episodes, 0, 1); Content = grid;
        episodes.SelectionChanged += async (_, e) =>
        {
            if (e.CurrentSelection.FirstOrDefault() is not MediaItem item || AppServices.ActiveAccount?.Id != account.Id) return;
            episodes.SelectedItem = null;
            try
            {
                var action = await LanguageService.ActionSheetAsync(this, item.Name, "Cancelar", null, "Reproduzir", "Descarregar");
                if (action == "Reproduzir") await PlayEpisodeAsync(item);
                else if (action == "Descarregar")
                {
                    var prepared = item with { Category = series.Category, ParentSeriesId = series.Id };
                    if (!await CatalogOptionsService.AuthorizePlaybackAsync(this, prepared)) return;
                    var existing = await OfflineDownloadService.FindAsync(account.Id, UserProfileService.Active.Id, prepared);
                    if (existing?.IsComplete == true)
                        await Navigation.PushAsync(new PlayerPage(OfflineDownloadService.PlaybackItem(existing)));
                    else
                    {
                        if (existing?.State is OfflineDownloadState.Paused or OfflineDownloadState.Failed)
                            await OfflineDownloadService.ResumeAsync(existing);
                        else if (existing is null)
                            await OfflineDownloadService.QueueAsync(account, prepared, series.Name);
                        await LanguageService.AlertAsync(this, "Downloads offline", "O episódio foi adicionado aos downloads.");
                    }
                }
            }
            catch (Exception ex) { await Ui.ErrorAsync(this, ex); }
        };
        AppServices.Locked += Clear;
        void Adapt()
        {
            var viewport = Ui.Viewport(this);
            Ui.UpdateColumns(episodes, viewport.Width - 40);
        }
        SizeChanged += (_, _) => Adapt();
        Loaded += (_, _) => Adapt();
        if (initialEpisodes is { Count: > 0 })
        {
            episodes.ItemsSource = initialEpisodes;
            status.Text = LanguageService.Format("{0} episódios · Organizados por temporada", initialEpisodes.Count);
        }
    }

    private async Task PlayEpisodeAsync(MediaItem item)
    {
        var queue = ((IEnumerable<MediaItem>?)episodes.ItemsSource ?? []).Select(e => e with { Category = series.Category, ParentSeriesId = series.Id }).ToArray();
        await PlaybackService.PlayAsync(this, item with { Category = series.Category, ParentSeriesId = series.Id }, queue);
    }
    private void Clear() { lifetime.Cancel(); episodes.ItemsSource = null; }
    private async Task LoadAsync()
    {
        try
        {
            var result = await AppServices.Client.GetEpisodesAsync(account, series, lifetime.Token);
            if (AppServices.ActiveAccount?.Id != account.Id || lifetime.IsCancellationRequested) return;
            episodes.ItemsSource = result;
            status.Text = LanguageService.Format("{0} episódios · Organizados por temporada", result.Count);
        }
        catch (OperationCanceledException) { status.Text = "Pedido cancelado ou sem resposta. Tente atualizar."; }
        catch (Exception ex) { status.Text = "Não foi possível obter os episódios."; await Ui.ErrorAsync(this, ex); }
    }
    protected override void OnNavigatedFrom(NavigatedFromEventArgs args)
    {
        base.OnNavigatedFrom(args);
        if (!Navigation.NavigationStack.Contains(this)) { lifetime.Cancel(); AppServices.Locked -= Clear; }
    }
}

