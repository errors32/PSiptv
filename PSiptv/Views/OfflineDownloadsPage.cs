using PSiptv.Core;
using PSiptv.Services;
using CommunityToolkit.Maui.Storage;

namespace PSiptv.Views;

public sealed class OfflineDownloadsPage : LocalizedPage
{
    private readonly CollectionView list = new();
    private readonly Label status = Ui.Text("A carregar downloads…", 14, true);
    private readonly Label folderPath = Ui.Text("", 12, true);

    public OfflineDownloadsPage()
    {
        Ui.Page(this, "Downloads offline");
        list.EmptyView = Ui.Text("Não existem downloads neste perfil.", 16, true);
        list.ItemsLayout = Ui.IsTelevision
            ? new GridItemsLayout(2, ItemsLayoutOrientation.Vertical) { HorizontalItemSpacing = 8, VerticalItemSpacing = 8 }
            : new LinearItemsLayout(ItemsLayoutOrientation.Vertical) { ItemSpacing = 10 };
        list.ItemTemplate = new DataTemplate(() =>
        {
            var title = Ui.Text("", 18); title.FontAttributes = FontAttributes.Bold;
            title.SetBinding(Label.TextProperty, nameof(DownloadRow.Title));
            var details = Ui.Text("", 13, true); details.SetBinding(Label.TextProperty, nameof(DownloadRow.Details));
            var error = Ui.Text("", 12, true); error.SetBinding(Label.TextProperty, nameof(DownloadRow.Error));
            error.SetBinding(IsVisibleProperty, nameof(DownloadRow.HasError));
            var progress = new ProgressBar(); progress.SetDynamicResource(ProgressBar.ProgressColorProperty, "Accent");
            progress.SetBinding(ProgressBar.ProgressProperty, nameof(DownloadRow.Progress));
            progress.SetBinding(IsVisibleProperty, nameof(DownloadRow.ShowProgress));
            Button play = null!, pause = null!, resume = null!, remove = null!;
            play = Ui.Button("Reproduzir offline", async () =>
            {
                if (play.BindingContext is DownloadRow row &&
                    await CatalogOptionsService.AuthorizePlaybackAsync(this, row.Download.Item))
                    await Navigation.PushAsync(new PlayerPage(OfflineDownloadService.PlaybackItem(row.Download)));
            }, true);
            play.SetBinding(IsVisibleProperty, nameof(DownloadRow.CanPlay));
            pause = Ui.Button("Pausar download", async () =>
            {
                if (pause.BindingContext is DownloadRow row) await OfflineDownloadService.PauseAsync(row.Download);
            });
            pause.SetBinding(IsVisibleProperty, nameof(DownloadRow.CanPause));
            resume = Ui.Button("Retomar download", async () =>
            {
                if (resume.BindingContext is DownloadRow row) await OfflineDownloadService.ResumeAsync(row.Download);
            });
            resume.SetBinding(IsVisibleProperty, nameof(DownloadRow.CanResume));
            remove = Ui.Button("Eliminar", async () =>
            {
                if (remove.BindingContext is not DownloadRow row) return;
                if (!await LanguageService.ConfirmAsync(this, "Eliminar download",
                    "Eliminar o download e o respetivo ficheiro?", "Eliminar", "Cancelar")) return;
                await OfflineDownloadService.RemoveAsync(row.Download);
            });
            return Ui.Card(Ui.Stack(title, details, error, progress, play, pause, resume, remove));
        });
        var root = new Grid { Padding = 20, RowSpacing = 12,
            RowDefinitions = [new(GridLength.Auto), new(GridLength.Star)] };
        folderPath.LineBreakMode = LineBreakMode.MiddleTruncation;
        var folderActions = new Grid
        {
            ColumnDefinitions = [new(GridLength.Star), new(GridLength.Star)], ColumnSpacing = 8
        };
        folderActions.Add(Ui.Button("Abrir pasta dos downloads", OpenDownloadsFolderAsync));
        folderActions.Add(Ui.Button("Escolher outra pasta", ChooseDownloadsFolderAsync), 1);
        root.Add(Ui.Stack(status, folderPath, folderActions));
        root.Add(list, 0, 1);
        Content = root;
        OfflineDownloadService.Changed += Refresh;
    }

    protected override async void OnAppearing() { base.OnAppearing(); await LoadAsync(); }

    private void Refresh() => Dispatcher.Dispatch(async () => await LoadAsync());

    private async Task LoadAsync()
    {
        var downloads = await OfflineDownloadService.LoadActiveProfileAsync();
        list.ItemsSource = downloads.Select(item => new DownloadRow(item, item.Item.Name, Details(item), item.Error)).ToArray();
        var bytes = downloads.Where(item => item.IsComplete).Sum(item => item.BytesDownloaded);
        status.Text = LanguageService.Format("{0} downloads · {1} offline", downloads.Count, FormatBytes(bytes));
        folderPath.Text = OfflineDownloadService.DownloadsDirectory;
    }

    private async Task OpenDownloadsFolderAsync()
    {
        var path = OfflineDownloadService.DownloadsFolderLocation;
#if WINDOWS
        Directory.CreateDirectory(path);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "explorer.exe", ArgumentList = { path }, UseShellExecute = true
        });
#elif ANDROID
        if (!await PSiptv.AndroidFolderGrant.OpenAsync(path))
            await LanguageService.AlertAsync(this, "Pasta dos downloads", OfflineDownloadService.DownloadsDirectory);
#else
        if (!await Launcher.Default.TryOpenAsync(new Uri(path)))
            await LanguageService.AlertAsync(this, "Pasta dos downloads", path);
#endif
    }

    private async Task ChooseDownloadsFolderAsync()
    {
#if ANDROID
        var selectedPath = await PSiptv.AndroidFolderGrant.PickAsync(CancellationToken.None);
        if (selectedPath is null) return;
        await OfflineDownloadService.ChangeDownloadsDirectoryAsync(selectedPath);
#else
        var result = await FolderPicker.Default.PickAsync(OfflineDownloadService.DownloadsDirectory, CancellationToken.None);
        if (!result.IsSuccessful) return;
        await OfflineDownloadService.ChangeDownloadsDirectoryAsync(result.Folder.Path);
#endif
        folderPath.Text = OfflineDownloadService.DownloadsDirectory;
    }

    private static string Details(OfflineDownload item)
    {
        var state = LanguageService.Text(item.State switch
        {
            OfflineDownloadState.Queued => "Em fila",
            OfflineDownloadState.Downloading => "A descarregar",
            OfflineDownloadState.Paused => "Em pausa",
            OfflineDownloadState.Completed => "Disponível offline",
            _ => "Falhou"
        });
        var size = item.TotalBytes is > 0
            ? $"{FormatBytes(item.BytesDownloaded)} / {FormatBytes(item.TotalBytes.Value)}"
            : FormatBytes(item.BytesDownloaded);
        var series = item.SeriesName.Length > 0 ? item.SeriesName + " · " : "";
        return $"{series}{state} · {size}";
    }

    private static string FormatBytes(long bytes) => bytes >= 1024L * 1024 * 1024
        ? $"{bytes / 1024d / 1024d / 1024d:0.00} GB"
        : $"{bytes / 1024d / 1024d:0.0} MB";

    protected override void OnNavigatedFrom(NavigatedFromEventArgs args)
    {
        base.OnNavigatedFrom(args);
        if (!Navigation.NavigationStack.Contains(this)) OfflineDownloadService.Changed -= Refresh;
    }

    private sealed record DownloadRow(OfflineDownload Download, string Title, string Details, string Error)
    {
        public double Progress => Download.Progress;
        public bool ShowProgress => Download.State is OfflineDownloadState.Queued or OfflineDownloadState.Downloading or OfflineDownloadState.Paused;
        public bool CanPlay => Download.IsComplete;
        public bool CanPause => Download.State is OfflineDownloadState.Queued or OfflineDownloadState.Downloading;
        public bool CanResume => Download.State is OfflineDownloadState.Paused or OfflineDownloadState.Failed;
        public bool HasError => Error.Length > 0;
    }
}
