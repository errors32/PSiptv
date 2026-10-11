using PSiptv.Core;
using PSiptv.Services;

namespace PSiptv.Views;

public sealed class HomeCustomizationPage : LocalizedPage
{
    private readonly VerticalStackLayout rows = Ui.Stack();
    public HomeCustomizationPage()
    {
        Ui.Page(this, "Personalizar início");
        Content = new ScrollView { Content = rows, Padding = 18 };
    }
    protected override async void OnAppearing() { base.OnAppearing(); try { await RefreshAsync(); } catch (Exception ex) { await Ui.ErrorAsync(this, ex); } }
    private async Task RefreshAsync()
    {
        rows.Clear();
        if (AppServices.ActiveAccount is not { } account) { rows.Add(Ui.Text("Abra uma lista.")); return; }
        var data = await ExperienceService.LoadAsync(account.Id);
        rows.Add(Ui.Text("Escolha as secções e a ordem da sua página inicial. As escolhas pertencem a este perfil.", 14, true));
        var order = data.HomeOrder.Concat(ExperienceService.HomeSections).Where(ExperienceService.HomeSections.Contains).Distinct().ToArray();
        foreach (var name in order)
        {
            var visible = new Switch { IsToggled = !data.HiddenHome.Contains(name) };
            visible.Toggled += async (_, e) => { try { await ExperienceService.ChangeAsync(d => { if (e.Value) d.HiddenHome.Remove(name); else d.HiddenHome.Add(name); }); } catch (Exception ex) { await Ui.ErrorAsync(this, ex); } };
            async Task Move(int direction)
            {
                await ExperienceService.ChangeAsync(d =>
                {
                    var items = d.HomeOrder.Concat(ExperienceService.HomeSections).Distinct().ToList();
                    var index = items.IndexOf(name); var next = index + direction;
                    if (next >= 0 && next < items.Count) (items[index], items[next]) = (items[next], items[index]);
                    d.HomeOrder = items;
                });
                await RefreshAsync();
            }
            var line = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto)], ColumnSpacing = 5 };
            line.Add(Ui.Text(name, 16)); line.Add(visible, 1);
            line.Add(Ui.Button("↑", () => Move(-1)), 2); line.Add(Ui.Button("↓", () => Move(1)), 3);
            rows.Add(Ui.Card(line));
        }
        rows.Add(Ui.Button("Gerir conteúdos fixados e coleções", () => Navigation.PushAsync(new CollectionsPage())));
    }
}

public sealed class CollectionsPage : LocalizedPage
{
    private readonly Picker filter = new() { Title = "Coleção ou estado" };
    private readonly SearchBar search = new() { Placeholder = "Pesquisar na biblioteca e no catálogo" };
    private readonly CollectionView list = Ui.MediaList();
    private PersonalLibrary data = new();
    private IReadOnlyList<MediaItem> catalog = [];
    public CollectionsPage()
    {
        Ui.Page(this, "Coleções e estados");
        filter.SetDynamicResource(Picker.TextColorProperty, "Ink");
        search.SetDynamicResource(SearchBar.TextColorProperty, "Ink");
        filter.SelectedIndexChanged += (_, _) => Apply(); search.TextChanged += (_, _) => Apply();
        list.SelectionChanged += async (_, e) =>
        {
            if (e.CurrentSelection.FirstOrDefault() is not MediaItem item) return;
            list.SelectedItem = null;
            await Navigation.PushAsync(new ItemLibraryPage(item));
        };
        var layout = new Grid { Padding = 16, RowSpacing = 10, RowDefinitions = [new(GridLength.Auto), new(GridLength.Star)] };
        layout.Add(Ui.Stack(Ui.Button("+ Nova coleção", CreateAsync), Ui.Button("Gerir coleção selecionada", ManageAsync), filter, search)); layout.Add(list, 0, 1);
        Content = layout;
        SizeChanged += (_, _) => Ui.UpdateColumns(list, Width - 32, 240);
    }
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            if (AppServices.ActiveAccount is not { } account) return;
            data = await ExperienceService.LoadAsync(account.Id);
            catalog = CatalogOptionsService.Catalogs.Values.SelectMany(c => c).Concat(FavoritesService.Items)
                .Concat(data.Pinned).Concat(data.Collections.SelectMany(c => c.Items)).DistinctBy(CatalogPreferences.ItemKey).ToArray();
            var selected = filter.SelectedIndex;
            filter.ItemsSource = new[] { "Catálogo disponível", "Fixados", "Por ver", "A ver", "Concluído" }.Concat(data.Collections.Select(c => c.Name)).ToArray();
            filter.SelectedIndex = Math.Clamp(selected, 0, filter.Items.Count - 1);
            Apply();
        }
        catch (Exception ex) { await Ui.ErrorAsync(this, ex); }
    }
    private async Task CreateAsync()
    {
        var name = await DisplayPromptAsync("Nova coleção", "Nome", "Criar", "Cancelar", maxLength: 60);
        if (string.IsNullOrWhiteSpace(name)) return;
        if (data.Collections.Any(c => c.Name.Equals(name.Trim(), StringComparison.CurrentCultureIgnoreCase))) throw new InvalidOperationException("Já existe uma coleção com esse nome.");
        await ExperienceService.ChangeAsync(d => d.Collections.Add(new() { Name = name.Trim() }));
        OnAppearing();
    }
    private async Task ManageAsync()
    {
        var collection = data.Collections.ElementAtOrDefault(filter.SelectedIndex - 5);
        if (collection is null) { await DisplayAlertAsync("Coleções", "Selecione uma coleção para a gerir.", "OK"); return; }
        var choice = await DisplayActionSheetAsync(collection.Name, "Cancelar", "Eliminar", "Alterar nome");
        if (choice == "Eliminar")
        {
            if (!await DisplayAlertAsync("Eliminar coleção", $"Eliminar a coleção «{collection.Name}»? Os conteúdos continuam no catálogo.", "Eliminar", "Cancelar")) return;
            await ExperienceService.ChangeAsync(d => d.Collections.RemoveAll(c => c.Id == collection.Id));
        }
        else if (choice == "Alterar nome")
        {
            var name = await DisplayPromptAsync("Alterar nome", "Nome", "Guardar", "Cancelar", maxLength: 60, initialValue: collection.Name);
            if (string.IsNullOrWhiteSpace(name)) return;
            await ExperienceService.ChangeAsync(d =>
            {
                if (d.Collections.Any(c => c.Id != collection.Id && c.Name.Equals(name.Trim(), StringComparison.CurrentCultureIgnoreCase))) throw new InvalidOperationException("Já existe uma coleção com esse nome.");
                var target = d.Collections.FirstOrDefault(c => c.Id == collection.Id);
                if (target is not null) target.Name = name.Trim();
            });
        }
        else return;
        OnAppearing();
    }
    private void Apply()
    {
        IEnumerable<MediaItem> items = filter.SelectedIndex switch
        {
            1 => data.Pinned, 2 => catalog.Where(i => i.Kind != MediaKind.Channel && data.State(i) == ViewingState.Unwatched),
            3 => catalog.Where(i => data.State(i) == ViewingState.Watching), 4 => catalog.Where(i => data.State(i) == ViewingState.Completed),
            >= 5 => data.Collections.ElementAtOrDefault(filter.SelectedIndex - 5)?.Items ?? [], _ => catalog
        };
        list.ItemsSource = items.Where(i => !CatalogOptionsService.Current.IsHidden(i) && i.Name.Contains(search.Text ?? "", StringComparison.CurrentCultureIgnoreCase)).ToArray();
    }
}

public sealed class ItemLibraryPage(MediaItem item) : LocalizedPage
{
    private readonly VerticalStackLayout rows = Ui.Stack();
    private bool initialized;
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (!initialized) { initialized = true; Ui.Page(this, item.Name); Content = new ScrollView { Content = rows, Padding = 18 }; }
        try { await RefreshAsync(); } catch (Exception ex) { await Ui.ErrorAsync(this, ex); }
    }
    private async Task RefreshAsync()
    {
        rows.Clear();
        if (AppServices.ActiveAccount is not { } account) return;
        var data = await ExperienceService.LoadAsync(account.Id);
        var key = CatalogPreferences.ItemKey(item);
        rows.Add(Ui.Text(item.Name, 24));
        rows.Add(Ui.Button("Reproduzir / abrir", async () =>
        {
            if (!await CatalogOptionsService.AuthorizePlaybackAsync(this, item)) return;
            if (item.HasEpisodes) await Navigation.PushAsync(new MediaDetailsPage(account, item));
            else await PlaybackService.PlayAsync(this, item);
        }, true));
        var pinned = data.Pinned.Any(i => CatalogPreferences.ItemKey(i) == key);
        rows.Add(Ui.Button(pinned ? "Desafixar do início" : "Fixar no início", async () =>
        {
            await ExperienceService.ChangeAsync(d => { d.Pinned.RemoveAll(i => CatalogPreferences.ItemKey(i) == key); if (!pinned) d.Pinned.Add(item); });
            await RefreshAsync();
        }));
        if (item.Kind != MediaKind.Channel)
        {
            rows.Add(Ui.Text("Estado: " + new[] { "Por ver", "A ver", "Concluído" }[(int)data.State(item)]));
            foreach (var state in Enum.GetValues<ViewingState>()) rows.Add(Ui.Button(new[] { "Marcar por ver", "Marcar a ver", "Marcar concluído" }[(int)state], async () =>
            { await ExperienceService.ChangeAsync(d => d.SetState(item, state)); await RefreshAsync(); }));
        }
        foreach (var collection in data.Collections)
        {
            var contains = collection.Items.Any(i => CatalogPreferences.ItemKey(i) == key);
            rows.Add(Ui.Button((contains ? "Remover de " : "Adicionar a ") + collection.Name, async () =>
            {
                await ExperienceService.ChangeAsync(d =>
                {
                    var target = d.Collections.FirstOrDefault(c => c.Id == collection.Id);
                    if (target is null) return;
                    target.Items.RemoveAll(i => CatalogPreferences.ItemKey(i) == key);
                    if (!contains) target.Items.Add(item);
                });
                await RefreshAsync();
            }));
        }
    }
}

public sealed class ChannelAlternativesPage : LocalizedPage
{
    private readonly VerticalStackLayout rows = Ui.Stack();
    public ChannelAlternativesPage() { Ui.Page(this, "Canais e alternativas"); Content = new ScrollView { Content = rows, Padding = 16 }; }
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            rows.Clear();
            if (AppServices.ActiveAccount is not { } account) return;
            var data = await ExperienceService.LoadAsync(account.Id);
            rows.Add(Ui.Text("Versões do mesmo canal são agrupadas pelo nome. A substituição automática só usa canais da lista ativa.", 14, true));
            foreach (var group in (CatalogOptionsService.Catalogs.GetValueOrDefault(MediaKind.Channel) ?? []).GroupBy(ChannelGroupPolicy.Key).Where(g => g.Count() > 1))
            {
                var choices = group.ToArray();
                rows.Add(Ui.Button(group.Key + " · " + choices.Length + " versões", async () =>
                {
                    var labels = choices.Select((c, i) => $"{i + 1}. {c.Name}").ToArray();
                    var selected = await DisplayActionSheetAsync("Versão preferida", "Cancelar", null, labels);
                    var index = Array.IndexOf(labels, selected);
                    if (index >= 0) { await ExperienceService.ChangeAsync(d => d.PreferredChannels[group.Key] = CatalogPreferences.ItemKey(choices[index])); OnAppearing(); }
                }));
                if (data.PreferredChannels.TryGetValue(group.Key, out var preferred)) rows.Add(Ui.Text("Preferida: " + (choices.FirstOrDefault(c => CatalogPreferences.ItemKey(c) == preferred)?.Name ?? "indisponível"), 12, true));
            }
        }
        catch (Exception ex) { await Ui.ErrorAsync(this, ex); }
    }
}

public sealed class PlaybackDiagnosticsPage : LocalizedPage
{
    private readonly VerticalStackLayout rows = Ui.Stack();
    public PlaybackDiagnosticsPage() { Ui.Page(this, "Diagnóstico de reprodução"); Content = new ScrollView { Content = rows, Padding = 16 }; }
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            rows.Clear();
            if (AppServices.ActiveAccount is not { } account) return;
            var diagnostics = await PlaybackDiagnosticsService.LoadAsync(account.Id, UserProfileService.Active.Id);
            rows.Add(Ui.Text("Últimas sessões neste perfil. Dados indisponíveis no leitor aparecem como n/d. Não são guardados endereços nem credenciais.", 13, true));
            foreach (var entry in diagnostics) rows.Add(Ui.Card(Ui.Stack(Ui.Text(entry.Title, 17),
                Ui.Text($"{AppOptions.FormatTime(entry.Started)} · Arranque {entry.StartupSeconds:F1} s · {entry.Interruptions} interrupções", 13, true),
                Ui.Text($"{entry.Video} · {entry.Audio} · {(entry.BitrateKbps > 0 ? $"{entry.BitrateKbps:F0} kbps" : "bitrate n/d")}", 13), Ui.Text(entry.Outcome, 12, true))));
            if (diagnostics.Count == 0) rows.Add(Ui.Text("Ainda não existem sessões registadas."));
        }
        catch (Exception ex) { await Ui.ErrorAsync(this, ex); }
    }
}
