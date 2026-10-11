using System.Text.Json;
using PSiptv.Core;
using PSiptv.Services;

namespace PSiptv.Views;

public sealed class UnifiedCatalogPage : LocalizedPage
{
    private sealed record Entry(PlaylistAccount Account, MediaItem Item, CatalogPreferences Options)
    { public string Title => Item.Name; public string Detail => Account.Name + " · " + Item.Category + " · " + Item.Kind; }
    private readonly List<Entry> entries = [];
    private readonly HashSet<string> included = [];
    private readonly SearchBar search = new() { Placeholder = "Pesquisar em todas as listas adicionadas" };
    private readonly Label status = Ui.Text("Adicione as listas que pretende consultar.", 13, true);
    private readonly Picker kind = new() { ItemsSource = new[] { "Todos", "Canais", "Filmes", "Séries" }, SelectedIndex = 0 };
    private readonly CollectionView results = new() { SelectionMode = SelectionMode.Single };
    private readonly Func<PlaylistAccount, MediaItem, Task> open;
    private readonly CancellationTokenSource lifetime = new();
    private readonly int session = AppServices.SessionVersion;
    private CancellationTokenSource? searchRequest;
    private int searchGeneration;
    public UnifiedCatalogPage(Func<PlaylistAccount, MediaItem, Task> open)
    {
        this.open = open; Ui.Page(this, "Catálogo unificado");
        search.SetDynamicResource(SearchBar.TextColorProperty, "Ink"); kind.SetDynamicResource(Picker.TextColorProperty, "Ink");
        results.ItemTemplate = new DataTemplate(() =>
        {
            var title = Ui.Text("", 17); title.SetBinding(Label.TextProperty, nameof(Entry.Title));
            var detail = Ui.Text("", 12, true); detail.SetBinding(Label.TextProperty, nameof(Entry.Detail));
            return Ui.FocusableCard(Ui.Stack(title, detail), value => results.SelectedItem = value);
        });
        results.SelectionChanged += async (_, e) =>
        {
            if (e.CurrentSelection.FirstOrDefault() is not Entry entry) return;
            results.SelectedItem = null;
            if (session != AppServices.SessionVersion) return;
            try { await open(entry.Account, entry.Item); } catch (Exception ex) { await Ui.ErrorAsync(this, ex); }
        };
        search.TextChanged += (_, _) => Apply(); kind.SelectedIndexChanged += (_, _) => Apply();
        var layout = new Grid { Padding = 16, RowSpacing = 12, RowDefinitions = [new(GridLength.Auto), new(GridLength.Star)] };
        layout.Add(Ui.Stack(Ui.Button("+ Adicionar lista ao catálogo", AddAsync), search, kind, status)); layout.Add(results, 0, 1);
        Content = layout; AppServices.Locked += Clear;
    }
    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        if (!Navigation.NavigationStack.Contains(this)) { lifetime.Cancel(); AppServices.Locked -= Clear; entries.Clear(); }
    }
    private void Clear() { lifetime.Cancel(); searchRequest?.Cancel(); entries.Clear(); results.ItemsSource = null; }
    private async Task AddAsync()
    {
        var accounts = (await AppServices.Accounts.LoadAsync()).Where(a => !included.Contains(a.Id)).ToArray();
        var labels = accounts.Select((a, i) => $"{i + 1}. {a.Name}").ToArray();
        if (labels.Length == 0) { status.Text = "Todas as listas foram adicionadas."; return; }
        var chosen = await DisplayActionSheetAsync("Adicionar lista", "Cancelar", null, labels);
        var index = Array.IndexOf(labels, chosen); if (index < 0) return;
        var account = accounts[index];
        if (session != AppServices.SessionVersion || !await PinPage.AuthorizeAsync(this, account)) return;
        if (session != AppServices.SessionVersion) return;
        status.Text = "A carregar " + account.Name + "…";
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); deadline.CancelAfter(TimeSpan.FromMinutes(2));
        var options = JsonSerializer.Deserialize<CatalogPreferences>(await SecureStorage.Default.GetAsync($"catalog-options.{account.Id}") ?? "{}") ?? new();
        var catalogs = await CatalogCacheService.LoadAsync(account.Id);
        foreach (var type in new[] { MediaKind.Channel, MediaKind.Movie, MediaKind.Series })
        {
            if (!catalogs.ContainsKey(type)) catalogs[type] = await AppServices.Client.GetCatalogAsync(account, type, deadline.Token);
            if (session != AppServices.SessionVersion) return;
        }
        entries.AddRange(catalogs.Values.SelectMany(c => c).Where(c => !options.IsHidden(c)).Select(c => new Entry(account, c, options)));
        included.Add(account.Id); Apply();
    }
    private async void Apply()
    {
        if (session != AppServices.SessionVersion) { Clear(); return; }
        searchRequest?.Cancel();
        using var request = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        searchRequest = request;
        var generation = ++searchGeneration;
        var query = search.Text?.Trim() ?? "";
        var selectedKind = kind.SelectedIndex;
        var snapshot = entries.ToArray();
        try
        {
            await Task.Delay(150, request.Token);
            var found = await Task.Run(() =>
            {
                var filtered = snapshot.Where(e => selectedKind == 0 || (int)e.Item.Kind == selectedKind - 1);
                if (query.Length == 0) return filtered.Take(300).ToArray();
                var index = new TextSearchIndex<Entry>(filtered.Select(e => new SearchDocument<Entry>(e.Title, [e.Title, e.Account.Name, e.Item.Category], e)));
                return index.Search(query, 300, request.Token).ToArray();
            }, request.Token);
            if (generation != searchGeneration || request.IsCancellationRequested || session != AppServices.SessionVersion) return;
            results.ItemsSource = found;
            status.Text = $"{included.Count} listas · {entries.Count} conteúdos · até 300 resultados por pesquisa";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { await Ui.ErrorAsync(this, ex); }
        finally { if (ReferenceEquals(searchRequest, request)) searchRequest = null; }
    }
}
