using PSiptv.Core;
using PSiptv.Services;

namespace PSiptv.Views;

public sealed class SeriesRecordingRulesPage : LocalizedPage
{
    private readonly CollectionView list = new();
    private readonly Label status = Ui.Text("A carregar regras…", 14, true);
    private PlaylistAccount? account;

    public SeriesRecordingRulesPage()
    {
        Ui.Page(this, "Gravações recorrentes");
        list.EmptyView = Ui.Text("Não existem regras de gravação neste perfil.", 16, true);
        list.ItemsLayout = Ui.IsTelevision
            ? new GridItemsLayout(2, ItemsLayoutOrientation.Vertical) { HorizontalItemSpacing = 8, VerticalItemSpacing = 8 }
            : new LinearItemsLayout(ItemsLayoutOrientation.Vertical) { ItemSpacing = 10 };
        list.ItemTemplate = new DataTemplate(() =>
        {
            var title = Ui.Text("", 18); title.FontAttributes = FontAttributes.Bold;
            title.SetBinding(Label.TextProperty, nameof(RuleRow.Title));
            var details = Ui.Text("", 13, true); details.SetBinding(Label.TextProperty, nameof(RuleRow.Details));
            var enabledLabel = Ui.Text("Regra ativa");
            var enabled = new Switch();
            enabled.Toggled += async (_, args) =>
            {
                if (enabled.BindingContext is not RuleRow row || row.Rule.Enabled == args.Value) return;
                row.Rule = row.Rule with { Enabled = args.Value };
                await DvrService.SetSeriesRuleEnabledAsync(row.Rule, args.Value);
            };
            enabled.SetBinding(Switch.IsToggledProperty, "Rule.Enabled");
            var toggle = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)] };
            toggle.Add(enabledLabel); toggle.Add(enabled, 1);
            Button edit = null!;
            edit = Ui.Button("Editar regra", async () =>
            {
                if (edit.BindingContext is RuleRow row && account is not null)
                    await Navigation.PushAsync(new SeriesRecordingRuleEditorPage(account, row.Rule));
            });
            Button delete = null!;
            delete = Ui.Button("Eliminar regra", async () =>
            {
                if (delete.BindingContext is not RuleRow row) return;
                if (!await LanguageService.ConfirmAsync(this, "Eliminar regra",
                    "Os agendamentos já criados serão preservados. Eliminar esta regra?", "Eliminar", "Cancelar")) return;
                await DvrService.DeleteSeriesRuleAsync(row.Rule);
                await LoadAsync();
            });
            return Ui.Card(Ui.Stack(title, details, toggle, edit, delete));
        });
        var actions = new Grid
        {
            ColumnDefinitions = [new(GridLength.Star), new(GridLength.Star)],
            ColumnSpacing = 10
        };
        actions.Add(Ui.Button("+ Nova regra", AddAsync, true));
        actions.Add(Ui.Button("Procurar episódios", ScanAsync), 1);
        var root = new Grid
        {
            Padding = 20, RowSpacing = 12,
            RowDefinitions = [new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Star)]
        };
        root.Add(status); root.Add(actions, 0, 1); root.Add(list, 0, 2); Content = root;
        DvrService.Changed += Refresh;
    }

    protected override async void OnAppearing() { base.OnAppearing(); await LoadAsync(); }
    protected override void OnNavigatedFrom(NavigatedFromEventArgs args)
    {
        base.OnNavigatedFrom(args);
        if (!Navigation.NavigationStack.Contains(this)) DvrService.Changed -= Refresh;
    }

    private async Task LoadAsync()
    {
        account = AppServices.ActiveAccount;
        if (account is null)
        {
            list.ItemsSource = null;
            status.Text = LanguageService.Text("Abra uma lista para gerir gravações recorrentes.");
            return;
        }
        var rules = await DvrService.LoadSeriesRulesAsync(account.Id);
        var channels = CatalogOptionsService.Catalogs.GetValueOrDefault(MediaKind.Channel) ?? [];
        list.ItemsSource = rules.Select(rule => new RuleRow(rule, rule.Name, Details(rule, channels))).ToArray();
        status.Text = LanguageService.Format("{0} regras · Perfil {1}", rules.Count, UserProfileService.Active.Name);
    }

    private async Task AddAsync()
    {
        if (account is null) throw new InvalidOperationException("Abra uma lista primeiro.");
        await Navigation.PushAsync(new SeriesRecordingRuleEditorPage(account));
    }

    private async Task ScanAsync()
    {
        if (account is null) throw new InvalidOperationException("Abra uma lista primeiro.");
        status.Text = LanguageService.Text("A procurar episódios no guia…");
        var channels = CatalogOptionsService.Catalogs.GetValueOrDefault(MediaKind.Channel) ?? [];
        if (channels.Count == 0)
            channels = (await CatalogCacheService.LoadAsync(account.Id)).GetValueOrDefault(MediaKind.Channel) ?? [];
        if (channels.Count == 0)
            channels = await AppServices.Client.GetCatalogAsync(account, MediaKind.Channel, CancellationToken.None);
        var added = await DvrService.RefreshRecurringAsync(account, channels);
        status.Text = LanguageService.Format("{0} novos agendamentos criados.", added);
    }

    private void Refresh() => Dispatcher.Dispatch(() => _ = LoadAsync());

    private static string Details(DvrSeriesRule rule, IReadOnlyList<MediaItem> channels)
    {
        var channel = rule.ChannelId.Length == 0 ? LanguageService.Text("Todos os canais") :
            channels.FirstOrDefault(item => item.Id == rule.ChannelId)?.Name ?? LanguageService.Text("Canal selecionado");
        var time = rule.StartMinutes < 0 ? LanguageService.Text("qualquer horário") :
            LanguageService.Format("por volta das {0}", TimeSpan.FromMinutes(rule.StartMinutes).ToString(@"hh\:mm"));
        return $"{LanguageService.Text("Título contém")}: {rule.TitlePattern} · {channel} · {time}";
    }

    private sealed class RuleRow(DvrSeriesRule rule, string title, string details)
    {
        public DvrSeriesRule Rule { get; set; } = rule;
        public string Title { get; } = title;
        public string Details { get; } = details;
    }
}

public sealed class SeriesRecordingRuleEditorPage : LocalizedPage
{
    private static readonly int[] Tolerances = [15, 30, 60, 120];
    private readonly PlaylistAccount account;
    private readonly DvrSeriesRule? original;
    private readonly Entry name = Ui.Entry("Nome da regra");
    private readonly Entry title = Ui.Entry("Texto presente no título do programa");
    private readonly Picker channel = new() { Title = "Canal" };
    private readonly Switch restrictTime = new();
    private readonly TimePicker startTime = new();
    private readonly Picker tolerance = new() { Title = "Tolerância do horário" };
    private readonly Label status = Ui.Text("", 13, true);
    private readonly IReadOnlyList<MediaItem> channels;

    public SeriesRecordingRuleEditorPage(PlaylistAccount account, DvrSeriesRule? original = null,
        MediaItem? suggestedChannel = null, TvProgramme? suggestedProgramme = null)
    {
        this.account = account; this.original = original;
        channels = CatalogOptionsService.Catalogs.GetValueOrDefault(MediaKind.Channel) ?? [];
        Ui.Page(this, original is null ? "Nova gravação recorrente" : "Editar gravação recorrente");
        channel.ItemsSource = new[] { LanguageService.Text("Todos os canais") }.Concat(channels.Select(item => item.Name)).ToArray();
        tolerance.ItemsSource = Tolerances.Select(value => LanguageService.Format("± {0} minutos", value)).ToArray();
        var selectedChannel = original?.ChannelId ?? suggestedChannel?.Id ?? "";
        channel.SelectedIndex = selectedChannel.Length == 0 ? 0 : Math.Max(0, channels.ToList().FindIndex(item => item.Id == selectedChannel) + 1);
        name.Text = original?.Name ?? suggestedProgramme?.Title ?? "";
        title.Text = original?.TitlePattern ?? suggestedProgramme?.Title ?? "";
        restrictTime.IsToggled = original?.StartMinutes >= 0;
        var minutes = original?.StartMinutes >= 0 ? original.StartMinutes :
            suggestedProgramme is null ? 20 * 60 : suggestedProgramme.Start.ToLocalTime().Hour * 60 + suggestedProgramme.Start.ToLocalTime().Minute;
        startTime.Time = TimeSpan.FromMinutes(minutes);
        tolerance.SelectedIndex = Math.Max(0, Array.IndexOf(Tolerances, original?.TimeToleranceMinutes ?? 30));
        void UpdateTime() { startTime.IsEnabled = tolerance.IsEnabled = restrictTime.IsToggled; }
        restrictTime.Toggled += (_, _) => UpdateTime(); UpdateTime();
        var timeToggle = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)] };
        timeToggle.Add(Ui.Text("Limitar por horário")); timeToggle.Add(restrictTime, 1);
        var stack = Ui.Stack(Ui.Text("Gravação recorrente", 28),
            Ui.Text("A aplicação procura este título no guia e agenda automaticamente novos episódios sem repetir os já identificados.", 14, true),
            Ui.Text("Nome"), name, Ui.Text("Título contém"), title, Ui.Text("Canal"), channel,
            timeToggle, startTime, tolerance, status, Ui.Button("Guardar regra e procurar episódios", SaveAsync, true));
        stack.Padding = 24; stack.MaximumWidthRequest = 680;
        Content = new ScrollView { Content = stack };
    }

    private async Task SaveAsync()
    {
        if (AppServices.ActiveAccount?.Id != account.Id) return;
        var pattern = title.Text?.Trim() ?? "";
        if (DvrSeriesRulePolicy.Normalize(pattern).Length == 0)
            throw new InvalidOperationException("Indique o título a procurar no guia.");
        var selectedChannel = channel.SelectedIndex > 0 && channel.SelectedIndex - 1 < channels.Count
            ? channels[channel.SelectedIndex - 1].Id : "";
        var rule = new DvrSeriesRule(original?.Id ?? Guid.NewGuid().ToString("N"), account.Id,
            UserProfileService.Active.Id, string.IsNullOrWhiteSpace(name.Text) ? pattern : name.Text.Trim(),
            pattern, selectedChannel,
            restrictTime.IsToggled ? (int)(startTime.Time ?? TimeSpan.Zero).TotalMinutes : -1,
            tolerance.SelectedIndex >= 0 ? Tolerances[tolerance.SelectedIndex] : 30,
            original?.Enabled ?? true, original?.CreatedAt ?? DateTimeOffset.UtcNow);
        await DvrService.SaveSeriesRuleAsync(rule);
        status.Text = LanguageService.Text("A procurar episódios no guia…");
        try
        {
            var available = channels;
            if (available.Count == 0)
                available = await AppServices.Client.GetCatalogAsync(account, MediaKind.Channel, CancellationToken.None);
            var added = await DvrService.RefreshRecurringAsync(account, available);
            status.Text = LanguageService.Format("{0} novos agendamentos criados.", added);
        }
        catch (Exception ex) { status.Text = LanguageService.Text("A regra foi guardada, mas o guia ainda não pôde ser consultado."); System.Diagnostics.Debug.WriteLine(ex); }
        await Navigation.PopAsync();
    }
}
