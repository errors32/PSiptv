using PSiptv.Core;
using PSiptv.Services;

namespace PSiptv.Views;

internal static class CategoryEditor
{
    public static async Task<MediaKind?> ChooseKindAsync(Page owner)
    {
        var labels = new[] { "TV ao Vivo", "Filmes", "Séries" };
        var answer = await LanguageService.ActionSheetAsync(owner, "Tipo de conteúdo", "Cancelar", null, labels);
        var index = Array.IndexOf(labels, answer); return index < 0 ? null : (MediaKind)index;
    }
    public static async Task<IReadOnlyList<MediaItem>> LoadAsync(MediaKind kind)
    {
        var account = AppServices.ActiveAccount ?? throw new InvalidOperationException("Abra uma lista primeiro.");
        var version = AppServices.SessionVersion;
        if (CatalogOptionsService.Catalogs.TryGetValue(kind, out var saved)) return saved;
        var result = await AppServices.Client.GetCatalogAsync(account, kind, CancellationToken.None);
        if (version != AppServices.SessionVersion) throw new OperationCanceledException();
        CatalogOptionsService.Catalogs[kind] = result; return result;
    }
}

public sealed class CategoriesPage : LocalizedPage
{
    private readonly CollectionView list = new();
    private readonly bool parental;
    private readonly int version = AppServices.SessionVersion;
    private MediaKind kind;
    private List<string> names = [];
    public CategoriesPage(bool parental = false)
    {
        this.parental = parental;
        Ui.Page(this, parental ? "Categorias protegidas" : "Organizar categorias");
        var picker = new Picker { ItemsSource = new[] { "TV ao Vivo", "Filmes", "Séries" }, SelectedIndex = 0 };
        picker.SetDynamicResource(Picker.TextColorProperty, "Ink");
        picker.SelectedIndexChanged += async (_, _) => { if (picker.SelectedIndex < 0) return; kind = (MediaKind)picker.SelectedIndex; await LoadAsync(); };
        list.ItemTemplate = new DataTemplate(() =>
        {
            var name = Ui.Text("", 16); name.SetBinding(Label.TextProperty, ".");
            var toggle = Ui.Button("", async () =>
            {
                if (version != AppServices.SessionVersion || name.BindingContext is not string value) return;
                var set = parental ? CatalogOptionsService.Current.Locked : CatalogOptionsService.Current.Hidden;
                var key = CatalogPreferences.CategoryKey(kind, value);
                if (!set.Add(key)) set.Remove(key);
                await CatalogOptionsService.SaveAsync(); Refresh();
            });
            toggle.BindingContextChanged += (_, _) =>
            {
                if (toggle.BindingContext is not string value) return;
                var key = CatalogPreferences.CategoryKey(kind, value);
                toggle.Text = LanguageService.Text(parental ? (CatalogOptionsService.Current.Locked.Contains(key) ? "Protegida 🔒" : "Livre") : (CatalogOptionsService.Current.Hidden.Contains(key) ? "Oculta" : "Visível"));
            };
            var up = Ui.Button("↑", () => MoveAsync(name.BindingContext as string, -1));
            var down = Ui.Button("↓", () => MoveAsync(name.BindingContext as string, 1));
            var row = new Grid { ColumnSpacing = 6, Padding = 8, ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto)] };
            row.Add(name); row.Add(toggle, 1); row.Add(up, 2); row.Add(down, 3); up.IsVisible = down.IsVisible = !parental;
            return row;
        });
        var grid = new Grid { Padding = 16, RowDefinitions = [new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Star)] };
        grid.Add(picker); grid.Add(Ui.Text(parental ? "Toque em Livre para exigir o PIN ao reproduzir conteúdos dessa categoria." : "As alterações são guardadas nesta lista. Use as setas para ordenar.", 13, true), 0, 1); grid.Add(list, 0, 2); Content = grid;
    }
    protected override async void OnAppearing() { base.OnAppearing(); await LoadAsync(); }
    private async Task LoadAsync()
    {
        try
        {
            var selected = kind; var items = await CategoryEditor.LoadAsync(selected);
            if (selected != kind || version != AppServices.SessionVersion) return;
            names = items.Select(i => i.Category).Concat(CatalogOptionsService.Current.Custom.Where(c => c.Kind == kind).Select(c => c.Name)).Distinct()
                .OrderBy(n => { var index = CatalogOptionsService.Current.Order.IndexOf(CatalogPreferences.CategoryKey(kind, n)); return index < 0 ? int.MaxValue : index; }).ThenBy(n => n).ToList(); Refresh();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { await Ui.ErrorAsync(this, ex); }
    }
    private void Refresh() { list.ItemsSource = null; list.ItemsSource = names.ToArray(); }
    private async Task MoveAsync(string? value, int offset)
    {
        if (value is null || version != AppServices.SessionVersion) return;
        var i = names.IndexOf(value); var target = i + offset;
        if (i < 0 || target < 0 || target >= names.Count) return;
        (names[i], names[target]) = (names[target], names[i]);
        var order = CatalogOptionsService.Current.Order;
        order.RemoveAll(k => k.StartsWith($"{(int)kind}:", StringComparison.Ordinal)); order.AddRange(names.Select(n => CatalogPreferences.CategoryKey(kind, n)));
        await CatalogOptionsService.SaveAsync(); Refresh();
    }
}

public sealed class CustomCategoriesPage : LocalizedPage
{
    private readonly VerticalStackLayout rows = Ui.Stack();
    public CustomCategoriesPage()
    {
        Ui.Page(this, "Categorias personalizadas");
        var body = Ui.Stack(Ui.Button("+ Adicionar categoria", async () =>
        {
            var kind = await CategoryEditor.ChooseKindAsync(this); if (kind is null) return;
            await Navigation.PushAsync(new CustomCategoryPage(new CustomCategory { Kind = kind.Value }, true));
        }, true), rows); body.Padding = 20; Content = new ScrollView { Content = body };
    }
    protected override void OnAppearing()
    {
        base.OnAppearing(); rows.Clear();
        foreach (var category in CatalogOptionsService.Current.Custom)
            rows.Add(Ui.Button(LanguageService.Format("{0} · {1} conteúdos", category.Name, category.Items.Count), () => Navigation.PushAsync(new CustomCategoryPage(category, false))));
    }
}

public sealed class CustomCategoryPage : LocalizedPage
{
    private readonly CustomCategory original;
    private readonly bool creating;
    private readonly HashSet<string> selected;
    private readonly Entry name;
    private readonly CollectionView list = new();
    private readonly SearchBar search = new() { Placeholder = "Pesquisar conteúdo" };
    private IReadOnlyList<MediaItem> items = [];
    private readonly int version = AppServices.SessionVersion;
    public CustomCategoryPage(CustomCategory category, bool creating)
    {
        original = category; this.creating = creating; selected = [.. category.Items];
        Ui.Page(this, "Editar categoria"); name = Ui.Entry("Nome da categoria"); name.Text = category.Name;
        list.ItemTemplate = new DataTemplate(() =>
        {
            var title = Ui.Text("", 15); title.SetBinding(Label.TextProperty, nameof(MediaItem.Name));
            var check = new CheckBox(); bool binding = false;
            check.BindingContextChanged += (_, _) => { binding = true; check.IsChecked = check.BindingContext is MediaItem i && selected.Contains(CatalogPreferences.ItemKey(i)); binding = false; };
            check.CheckedChanged += (_, e) => { if (binding || check.BindingContext is not MediaItem i) return; if (e.Value) selected.Add(CatalogPreferences.ItemKey(i)); else selected.Remove(CatalogPreferences.ItemKey(i)); };
            var row = new Grid { ColumnDefinitions = [new(GridLength.Auto), new(GridLength.Star)], Padding = 8 }; row.Add(check); row.Add(title, 1); return row;
        });
        search.TextChanged += (_, _) => list.ItemsSource = items.Where(i => i.Name.Contains(search.Text ?? "", StringComparison.CurrentCultureIgnoreCase)).ToArray();
        var save = Ui.Button("Guardar", SaveAsync, true);
        var delete = Ui.Button("Eliminar categoria", async () =>
        {
            if (version != AppServices.SessionVersion) return;
            if (!await DisplayAlertAsync("Eliminar categoria", "Eliminar esta categoria personalizada? Os conteúdos do fornecedor são preservados.", "Eliminar", "Cancelar")) return;
            CatalogOptionsService.Current.Custom.Remove(original); await CatalogOptionsService.SaveAsync(); await Navigation.PopAsync();
        }); delete.IsVisible = !creating;
        var grid = new Grid { Padding = 16, RowDefinitions = [new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto)] };
        grid.Add(name); grid.Add(search, 0, 1); grid.Add(list, 0, 2); grid.Add(Ui.Stack(save, delete), 0, 3); Content = grid;
    }
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try { items = await CategoryEditor.LoadAsync(original.Kind); if (version == AppServices.SessionVersion) list.ItemsSource = items; }
        catch (Exception ex) { await Ui.ErrorAsync(this, ex); }
    }
    private async Task SaveAsync()
    {
        if (version != AppServices.SessionVersion) return;
        var value = name.Text?.Trim() ?? "";
        if (value.Length is 0 or > 80 || items.Any(i => i.Category.Equals(value, StringComparison.OrdinalIgnoreCase)) || CatalogOptionsService.Current.Custom.Any(c => c != original && c.Kind == original.Kind && c.Name.Equals(value, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Escolha um nome único com 1 a 80 caracteres.");
        var options = CatalogOptionsService.Current;
        var oldKey = CatalogPreferences.CategoryKey(original.Kind, original.Name); var newKey = CatalogPreferences.CategoryKey(original.Kind, value);
        if (options.Hidden.Remove(oldKey)) options.Hidden.Add(newKey);
        if (options.Locked.Remove(oldKey)) options.Locked.Add(newKey);
        var index = options.Order.IndexOf(oldKey); if (index >= 0) options.Order[index] = newKey;
        original.Name = value; original.Items = selected;
        if (creating) options.Custom.Add(original);
        await CatalogOptionsService.SaveAsync(); await Navigation.PopAsync();
    }
}

public sealed class HistoryPage : LocalizedPage
{
    private readonly CollectionView list = new() { SelectionMode = SelectionMode.Single };
    private readonly string id = AppServices.ActiveAccount?.Id ?? "";
    public HistoryPage()
    {
        Ui.Page(this, "Histórico de Visualização");
        list.EmptyView = Ui.Text("Ainda não viu nenhum conteúdo.", 16, true);
        list.ItemTemplate = new DataTemplate(() =>
        {
            var title = Ui.Text("", 17); title.SetBinding(Label.TextProperty, "Item.Name");
            var info = Ui.Text("", 13, true);
            info.BindingContextChanged += (_, _) => { if (info.BindingContext is WatchEntry e) info.Text = AppOptions.FormatTime(e.WatchedAt) + (e.Item.Kind == MediaKind.Channel ? "" : $" · {TimeSpan.FromSeconds(e.PositionSeconds):hh\\:mm\\:ss}"); };
            return Ui.Card(Ui.Stack(title, info));
        });
        list.SelectionChanged += async (_, e) =>
        {
            if (e.CurrentSelection.FirstOrDefault() is not WatchEntry entry || AppServices.ActiveAccount?.Id != id) return;
            list.SelectedItem = null;
            try { await PlaybackService.PlayAsync(this, entry.Item, resume: entry.PositionSeconds); } catch (Exception ex) { await Ui.ErrorAsync(this, ex); }
        };
        var grid = new Grid { Padding = 16, RowDefinitions = [new(GridLength.Auto), new(GridLength.Star)] };
        grid.Add(Ui.Button("Limpar histórico", async () => { if (AppServices.ActiveAccount?.Id != id) return; await HistoryService.ClearAsync(id); await LoadAsync(); })); grid.Add(list, 0, 1); Content = grid;
    }
    protected override async void OnAppearing() { base.OnAppearing(); try { await LoadAsync(); } catch (Exception ex) { await Ui.ErrorAsync(this, ex); } }
    private async Task LoadAsync() { var entries = await HistoryService.LoadAsync(id); if (AppServices.ActiveAccount?.Id == id) list.ItemsSource = entries; }
}
