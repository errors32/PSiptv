using PSiptv.Services;

namespace PSiptv.Views;

public sealed class StorageManagerPage : LocalizedPage
{
    private readonly Label summary = Ui.Text("A calcular utilização…", 14, true);
    private readonly Label device = Ui.Text("", 13, true);
    private readonly Label recordings = Ui.Text("", 14, true);
    private readonly Label downloads = Ui.Text("", 14, true);
    private readonly Label cache = Ui.Text("", 14, true);
    private readonly Label temporary = Ui.Text("", 14, true);
    private readonly ProgressBar deviceUsage = new();
    private readonly ActivityIndicator loading = new();

    public StorageManagerPage()
    {
        Ui.Page(this, "Gestor de armazenamento");
        deviceUsage.SetDynamicResource(ProgressBar.ProgressColorProperty, "Accent");
        loading.SetDynamicResource(ActivityIndicator.ColorProperty, "Accent");

        var recordingsCard = UsageCard("Gravações DVR", recordings,
            Ui.Button("Gerir gravações", () => Navigation.PushAsync(new RecordingsPage())),
            Ui.Button("Eliminar todas", DeleteRecordingsAsync));
        var downloadsCard = UsageCard("Downloads offline", downloads,
            Ui.Button("Gerir downloads", () => Navigation.PushAsync(new OfflineDownloadsPage())),
            Ui.Button("Eliminar todos", DeleteDownloadsAsync));
        var cacheCard = UsageCard("Cache da aplicação", cache,
            Ui.Button("Limpar cache", ClearCacheAsync));
        var temporaryCard = UsageCard("Ficheiros temporários", temporary);

        var deviceCard = Ui.Card(Ui.Stack(Ui.Text("Armazenamento do dispositivo", 18), device, deviceUsage));
        var note = Ui.Text("Os valores incluem todas as listas e perfis. Ficheiros temporários em utilização são preservados.", 12, true);
        View managedStorage;
        if (Ui.IsTelevision)
        {
            var grid = new Grid
            {
                ColumnSpacing = 8, RowSpacing = 8,
                ColumnDefinitions = [new(GridLength.Star), new(GridLength.Star)],
                RowDefinitions = [new(GridLength.Auto), new(GridLength.Auto)]
            };
            grid.Add(recordingsCard);
            grid.Add(downloadsCard, 1);
            grid.Add(cacheCard, 0, 1);
            grid.Add(temporaryCard, 1, 1);
            managedStorage = grid;
        }
        else managedStorage = Ui.Stack(recordingsCard, downloadsCard, cacheCard, temporaryCard);
        var content = Ui.Stack(summary, loading, deviceCard, managedStorage, note);
        content.Padding = 20;
        content.MaximumWidthRequest = 800;
        content.HorizontalOptions = LayoutOptions.Center;
        Content = new ScrollView { Content = content };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await RefreshAsync();
    }

    private static Border UsageCard(string title, Label value, params Button[] actions)
    {
        var heading = Ui.Text(title, 18);
        heading.FontAttributes = FontAttributes.Bold;
        var stack = Ui.Stack(heading, value);
        if (actions.Length > 0)
        {
            var buttons = new Grid { ColumnSpacing = 8 };
            for (var i = 0; i < actions.Length; i++)
            {
                buttons.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
                buttons.Add(actions[i], i);
            }
            stack.Add(buttons);
        }
        return Ui.Card(stack);
    }

    private async Task RefreshAsync()
    {
        loading.IsRunning = loading.IsVisible = true;
        try
        {
            var value = await StorageManagerService.MeasureAsync();
            summary.Text = LanguageService.Format("{0} utilizados pela aplicação", FormatBytes(value.AppManagedBytes));
            recordings.Text = Description(value.RecordingsBytes, value.RecordingFiles);
            downloads.Text = Description(value.DownloadsBytes, value.DownloadFiles);
            cache.Text = Description(value.CacheBytes, value.CacheFiles);
            temporary.Text = Description(value.TemporaryBytes, value.TemporaryFiles);
            if (value.DeviceTotalBytes > 0)
            {
                var used = value.DeviceTotalBytes - value.DeviceFreeBytes;
                device.Text = LanguageService.Format("{0} livres de {1}", FormatBytes(value.DeviceFreeBytes), FormatBytes(value.DeviceTotalBytes));
                deviceUsage.Progress = Math.Clamp(used / (double)value.DeviceTotalBytes, 0, 1);
                deviceUsage.IsVisible = true;
            }
            else
            {
                device.Text = LanguageService.Text("Informação de espaço livre indisponível.");
                deviceUsage.IsVisible = false;
            }
        }
        finally { loading.IsRunning = loading.IsVisible = false; }
    }

    private async Task ClearCacheAsync()
    {
        if (!await LanguageService.ConfirmAsync(this, "Limpar cache",
            "Eliminar o guia, catálogos e imagens guardados em cache? Serão descarregados novamente quando necessário.",
            "Limpar", "Cancelar")) return;
        await StorageManagerService.ClearCacheAsync();
        await RefreshAsync();
    }

    private async Task DeleteRecordingsAsync()
    {
        if (!await LanguageService.ConfirmAsync(this, "Eliminar todas as gravações",
            "Eliminar todas as gravações e agendamentos de todas as listas e perfis? As regras recorrentes serão preservadas.",
            "Eliminar", "Cancelar")) return;
        await DvrService.DeleteAllAsync();
        await RefreshAsync();
    }

    private async Task DeleteDownloadsAsync()
    {
        if (!await LanguageService.ConfirmAsync(this, "Eliminar todos os downloads",
            "Eliminar todos os downloads offline de todas as listas e perfis?", "Eliminar", "Cancelar")) return;
        await OfflineDownloadService.DeleteAllAsync();
        await RefreshAsync();
    }

    private static string Description(long bytes, int files) =>
        LanguageService.Format("{0} · {1} ficheiros", FormatBytes(bytes), files);

    private static string FormatBytes(long bytes) => bytes switch
    {
        >= 1024L * 1024 * 1024 => $"{bytes / 1024d / 1024d / 1024d:0.00} GB",
        >= 1024L * 1024 => $"{bytes / 1024d / 1024d:0.0} MB",
        >= 1024L => $"{bytes / 1024d:0.0} KB",
        _ => $"{bytes} B"
    };
}
