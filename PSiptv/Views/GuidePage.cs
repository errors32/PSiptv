using PSiptv.Core;
using PSiptv.Services;

namespace PSiptv.Views;

public sealed class GuidePage : LocalizedPage
{
    private readonly PlaylistAccount account;
    private readonly MediaItem channel;
    private readonly Label status = Ui.Text("Use «Atualizar guia» para carregar a programação.", 14, true);
    private readonly CollectionView programmes = new()
    {
        ItemsLayout = new GridItemsLayout(1, ItemsLayoutOrientation.Vertical)
        {
            HorizontalItemSpacing = 10, VerticalItemSpacing = 10
        }
    };
    private readonly CancellationTokenSource lifetime = new();
    private bool loaded;

    public GuidePage(PlaylistAccount account, MediaItem channel)
    {
        this.account = account; this.channel = channel;
        Ui.Page(this, "Guia TV");
        programmes.EmptyView = Ui.Text("Sem programação disponível para este canal.", 16, true);
        programmes.ItemTemplate = new DataTemplate(() =>
        {
            var title = Ui.Text("", 18); title.SetBinding(Label.TextProperty, nameof(GuideProgrammeRow.Title));
            var time = Ui.Text("", 13, true); time.SetBinding(Label.TextProperty, nameof(GuideProgrammeRow.Schedule));
            var live = Ui.Text("", 12); live.SetDynamicResource(Label.TextColorProperty, "Accent"); live.SetBinding(Label.TextProperty, nameof(GuideProgrammeRow.Status));
            var description = Ui.Text("", 14, true); description.SetBinding(Label.TextProperty, nameof(GuideProgrammeRow.Description));
            Button watch = null!;
            watch = Ui.Button("Ver programa", async () =>
            {
                if (watch.BindingContext is GuideProgrammeRow { Archive: { } archive } && AppServices.ActiveAccount?.Id == account.Id)
                    await PlaybackService.PlayAsync(this, archive);
            }, true);
            watch.SetBinding(IsVisibleProperty, nameof(GuideProgrammeRow.CanWatch));
            Button reminder = null!;
            reminder = Ui.Button("Criar lembrete", async () =>
            {
                if (reminder.BindingContext is not GuideProgrammeRow row ||
                    AppServices.ActiveAccount?.Id != account.Id) return;
                row.IsReminder = await ReminderService.ToggleAsync(account, channel, row.Programme);
            });
            reminder.SetBinding(Button.TextProperty, nameof(GuideProgrammeRow.ReminderLabel));
            reminder.SetBinding(IsVisibleProperty, nameof(GuideProgrammeRow.CanRemind));
            Button record = null!;
            record = Ui.Button("Agendar gravação", async () =>
            {
                if (record.BindingContext is not GuideProgrammeRow row ||
                    AppServices.ActiveAccount?.Id != account.Id) return;
                row.IsRecording = await DvrService.ToggleProgrammeAsync(account, channel, row.Programme);
            });
            record.SetBinding(Button.TextProperty, nameof(GuideProgrammeRow.RecordingLabel));
            record.SetBinding(IsVisibleProperty, nameof(GuideProgrammeRow.CanRecord));
            Button recurring = null!;
            recurring = Ui.Button("Gravar série", async () =>
            {
                if (recurring.BindingContext is not GuideProgrammeRow row ||
                    AppServices.ActiveAccount?.Id != account.Id) return;
                await Navigation.PushAsync(new SeriesRecordingRuleEditorPage(account, suggestedChannel: channel,
                    suggestedProgramme: row.Programme));
            });
            recurring.SetBinding(IsVisibleProperty, nameof(GuideProgrammeRow.CanRecord));
            var card = Ui.Card(Ui.Stack(time, title, live, description, watch, reminder, record, recurring)); card.Margin = new Thickness(0, 0, 0, 10); return card;
        });
        var grid = new Grid { Padding = 20, RowSpacing = 12, RowDefinitions = [new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star)] };
        grid.Add(Ui.Stack(Ui.Text(channel.Name, 26), status, Ui.Button("Ver canal em direto", async () =>
        {
            if (AppServices.ActiveAccount?.Id == account.Id) await PlaybackService.PlayAsync(this, channel);
        }, true), Ui.Button("Atualizar guia", () => LoadAsync(true))));
        grid.Add(programmes, 0, 1); Content = grid;
        AppServices.Locked += Clear;
        void Adapt()
        {
            var viewport = Ui.Viewport(this);
            Ui.UpdateColumns(programmes, viewport.Width - 40, 420, 3);
        }
        SizeChanged += (_, _) => Adapt();
        Loaded += (_, _) => Adapt();
    }
    private void Clear() { lifetime.Cancel(); programmes.ItemsSource = null; }
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (!loaded) await LoadAsync(false);
    }
    private async Task LoadAsync(bool refresh)
    {
        try
        {
            status.Text = "A carregar programação…";
            var result = await EpgService.GetAsync(account, channel, lifetime.Token, refresh);
            if (AppServices.ActiveAccount?.Id != account.Id || lifetime.IsCancellationRequested) return;
            var now = DateTimeOffset.Now;
            var reminderIds = (await ReminderService.LoadAsync(account.Id)).Select(item => item.Id).ToHashSet();
            var recordingIds = (await DvrService.LoadAsync(account.Id))
                .Where(item => item.IsScheduled || item.IsRecording).Select(item => item.Id).ToHashSet();
            var rows = result.Select(p => new GuideProgrammeRow(p.Title, p.Description,
                LanguageService.Text(p.Status), AppOptions.FormatTime(p.Start) + " – " + AppOptions.FormatTime(p.End),
                CatchupStream.Create(account, channel, p, now), p, p.Start > now,
                reminderIds.Contains(ProgrammeReminderPolicy.IdFor(account.Id, UserProfileService.Active.Id,
                    channel.Id, p.Title, p.Start)), p.End > now,
                recordingIds.Contains(DvrRecordingPolicy.IdFor(account.Id, UserProfileService.Active.Id,
                    channel.Id, p.Title, p.Start)))).ToArray();
            programmes.ItemsSource = rows;
            var archived = rows.Count(row => row.CanWatch);
            status.Text = archived > 0
                ? LanguageService.Format("{0} programas · {1} disponíveis em catch-up · Horário local do dispositivo", result.Count, archived)
                : LanguageService.Format("{0} programas · Horário local do dispositivo", result.Count);
            loaded = true;
        }
        catch (OperationCanceledException) { status.Text = "Pedido cancelado ou sem resposta. Tente atualizar."; }
        catch (Exception ex) { status.Text = "Não foi possível obter o guia TV."; await Ui.ErrorAsync(this, ex); }
    }
    protected override void OnNavigatedFrom(NavigatedFromEventArgs args)
    {
        base.OnNavigatedFrom(args);
        if (!Navigation.NavigationStack.Contains(this)) { lifetime.Cancel(); AppServices.Locked -= Clear; }
    }

    private sealed class GuideProgrammeRow(string title, string description, string status, string schedule,
        MediaItem? archive, TvProgramme programme, bool canRemind, bool isReminder,
        bool canRecord, bool isRecording) : System.ComponentModel.INotifyPropertyChanged
    {
        public string Title { get; } = title;
        public string Description { get; } = description;
        public string Status { get; } = status;
        public string Schedule { get; } = schedule;
        public MediaItem? Archive { get; } = archive;
        public TvProgramme Programme { get; } = programme;
        public bool CanRemind { get; } = canRemind;
        public bool CanRecord { get; } = canRecord;
        private bool isReminder = isReminder;
        public bool IsReminder
        {
            get => isReminder;
            set
            {
                if (isReminder == value) return;
                isReminder = value;
                PropertyChanged?.Invoke(this, new(nameof(IsReminder)));
                PropertyChanged?.Invoke(this, new(nameof(ReminderLabel)));
            }
        }
        public string ReminderLabel => LanguageService.Text(IsReminder ? "Remover lembrete" : "Criar lembrete");
        private bool isRecording = isRecording;
        public bool IsRecording
        {
            get => isRecording;
            set
            {
                if (isRecording == value) return;
                isRecording = value;
                PropertyChanged?.Invoke(this, new(nameof(IsRecording)));
                PropertyChanged?.Invoke(this, new(nameof(RecordingLabel)));
            }
        }
        public string RecordingLabel => LanguageService.Text(IsRecording ? "Cancelar gravação" :
            Programme.Start <= DateTimeOffset.Now ? "Gravar agora" : "Agendar gravação");
        public bool CanWatch => Archive is not null;
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    }
}

