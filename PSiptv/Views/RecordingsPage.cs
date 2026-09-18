using PSiptv.Core;
using PSiptv.Services;
using CommunityToolkit.Maui.Storage;

namespace PSiptv.Views;

public sealed class RecordingsPage : LocalizedPage
{
    private readonly CollectionView list = new();
    private readonly Label status = Ui.Text("A carregar gravações…", 14, true);
    private readonly Label folderPath = Ui.Text("", 12, true);

    public RecordingsPage()
    {
        Ui.Page(this, "Gravações DVR");
        list.EmptyView = Ui.Text("Não existem gravações neste perfil.", 16, true);
        list.ItemsLayout = Ui.IsTelevision
            ? new GridItemsLayout(2, ItemsLayoutOrientation.Vertical) { HorizontalItemSpacing = 8, VerticalItemSpacing = 8 }
            : new LinearItemsLayout(ItemsLayoutOrientation.Vertical) { ItemSpacing = 10 };
        list.ItemTemplate = new DataTemplate(() =>
        {
            var title = Ui.Text("", 18); title.FontAttributes = FontAttributes.Bold;
            title.SetBinding(Label.TextProperty, nameof(RecordingRow.Title));
            var details = Ui.Text("", 13, true);
            details.SetBinding(Label.TextProperty, nameof(RecordingRow.Details));
            var error = Ui.Text("", 12, true);
            error.SetBinding(Label.TextProperty, nameof(RecordingRow.Error));
            error.SetBinding(IsVisibleProperty, nameof(RecordingRow.HasError));
            Button play = null!;
            play = Ui.Button("Reproduzir gravação", async () =>
            {
                if (play.BindingContext is not RecordingRow row || !row.CanPlay) return;
                var path = DvrService.RecordingLocation(row.Recording.FileName);
                if (path.Length == 0) throw new InvalidOperationException("O ficheiro desta gravação já não existe.");
                var item = row.Recording.Channel with
                {
                    Name = row.Recording.ProgrammeTitle, Url = new Uri(path, UriKind.Absolute).AbsoluteUri,
                    Kind = MediaKind.Movie, IsCatchup = true
                };
                await Navigation.PushAsync(new PlayerPage(item));
            }, true);
            play.SetBinding(IsVisibleProperty, nameof(RecordingRow.CanPlay));
            Button stop = null!;
            stop = Ui.Button("Parar gravação", async () =>
            {
                if (stop.BindingContext is RecordingRow row) await DvrService.StopAsync(row.Recording);
                await LoadAsync();
            });
            stop.SetBinding(IsVisibleProperty, nameof(RecordingRow.CanStop));
            Button remove = null!;
            remove = Ui.Button("Eliminar", async () =>
            {
                if (remove.BindingContext is not RecordingRow row) return;
                var confirmed = await LanguageService.ConfirmAsync(this, "Eliminar gravação",
                    "Eliminar este agendamento e o respetivo ficheiro?", "Eliminar", "Cancelar");
                if (!confirmed) return;
                await DvrService.RemoveAsync(row.Recording);
                await LoadAsync();
            });
            return Ui.Card(Ui.Stack(title, details, error, play, stop, remove));
        });
        var root = new Grid { Padding = 20, RowSpacing = 12,
            RowDefinitions = [new(GridLength.Auto), new(GridLength.Star)] };
        folderPath.LineBreakMode = LineBreakMode.MiddleTruncation;
        var folderActions = new Grid
        {
            ColumnDefinitions = [new(GridLength.Star), new(GridLength.Star)],
            ColumnSpacing = 8
        };
        folderActions.Add(Ui.Button("Abrir pasta das gravações", OpenRecordingsFolderAsync));
        folderActions.Add(Ui.Button("Escolher outra pasta", ChooseRecordingsFolderAsync), 1);
        root.Add(Ui.Stack(status, folderPath, folderActions, Ui.Button("Gerir gravações recorrentes", () =>
            Navigation.PushAsync(new SeriesRecordingRulesPage()))));
        root.Add(list, 0, 1); Content = root;
        DvrService.Changed += Refresh;
    }

    protected override async void OnAppearing() { base.OnAppearing(); await LoadAsync(); }
    protected override void OnNavigatedFrom(NavigatedFromEventArgs args)
    {
        base.OnNavigatedFrom(args);
        if (!Navigation.NavigationStack.Contains(this)) DvrService.Changed -= Refresh;
    }

    private void Refresh() => Dispatcher.Dispatch(async () => await LoadAsync());

    private async Task LoadAsync()
    {
        var recordings = await DvrService.LoadActiveProfileAsync();
        list.ItemsSource = recordings.Select(item => new RecordingRow(item,
            item.ProgrammeTitle, Details(item), item.Error)).ToArray();
        status.Text = LanguageService.Format("{0} gravações · Perfil {1}", recordings.Count, UserProfileService.Active.Name);
        folderPath.Text = DvrService.RecordingsDirectory;
    }

    private async Task OpenRecordingsFolderAsync()
    {
        var path = DvrService.RecordingsFolderLocation;
#if WINDOWS
        Directory.CreateDirectory(path);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "explorer.exe",
            ArgumentList = { path },
            UseShellExecute = true
        });
#elif ANDROID
        if (!await PSiptv.AndroidFolderGrant.OpenAsync(path))
            await LanguageService.AlertAsync(this, "Pasta das gravações", DvrService.RecordingsDirectory);
#else
        if (!await Launcher.Default.TryOpenAsync(new Uri(path)))
            await LanguageService.AlertAsync(this, "Pasta das gravações", path);
#endif
    }

    private async Task ChooseRecordingsFolderAsync()
    {
#if ANDROID
        var selectedPath = await PSiptv.AndroidFolderGrant.PickAsync(CancellationToken.None);
        if (selectedPath is null) return;
        await DvrService.ChangeRecordingsDirectoryAsync(selectedPath);
#else
        if (!await DvrService.EnsureRecordingsDirectoryAccessAsync(force: true))
        {
            await LanguageService.AlertAsync(this, "Permissão necessária",
                "Autorize o acesso ao armazenamento para escolher e usar outra pasta de gravações.");
            return;
        }
        var result = await FolderPicker.Default.PickAsync(DvrService.RecordingsDirectory, CancellationToken.None);
        if (!result.IsSuccessful) return;
        await DvrService.ChangeRecordingsDirectoryAsync(result.Folder.Path);
#endif
        folderPath.Text = DvrService.RecordingsDirectory;
    }

    private static string Details(DvrRecording item)
    {
        var state = LanguageService.Text(item.State switch
        {
            DvrRecordingState.Scheduled => "Agendada",
            DvrRecordingState.Recording => "A gravar",
            DvrRecordingState.Completed => "Concluída",
            _ => "Falhou"
        });
        var size = item.Bytes > 0 ? $" · {item.Bytes / 1024d / 1024d:0.0} MB" : "";
        return $"{item.Channel.Name} · {AppOptions.FormatTime(item.Start)}–{AppOptions.FormatTime(item.End)} · {state}{size}";
    }

    private sealed record RecordingRow(DvrRecording Recording, string Title, string Details, string Error)
    {
        public bool CanPlay => Recording.IsCompleted && Recording.FileName.Length > 0;
        public bool CanStop => Recording.IsRecording;
        public bool HasError => Error.Length > 0;
    }
}
