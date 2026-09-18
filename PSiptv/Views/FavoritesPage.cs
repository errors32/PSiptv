using PSiptv.Core;
using PSiptv.Services;

namespace PSiptv.Views;

public sealed class FavoritesPage : LocalizedPage
{
    private readonly CollectionView items = Ui.MediaList();
    private readonly SearchBar search = new() { Placeholder = "Pesquisar favoritos" };
    private readonly Picker kind = new() { ItemsSource = new[] { "Todos", "Canais", "Filmes", "Séries e episódios" }, SelectedIndex = 0 };
    private readonly Label count = Ui.Text("", 13, true);

    public FavoritesPage()
    {
        Ui.Page(this, "Favoritos");
        search.SetDynamicResource(SearchBar.TextColorProperty, "Ink");
        search.SetDynamicResource(SearchBar.PlaceholderColorProperty, "Muted");
        kind.SetDynamicResource(Picker.TextColorProperty, "Ink");
        items.EmptyView = Ui.Text("Sem favoritos para mostrar. Use ☆ junto a um conteúdo para o guardar.", 16, true);
        var grid = new Grid { Padding = 20, RowSpacing = 12,
            RowDefinitions = [new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Star)] };
        grid.Add(Ui.Stack(Ui.Text("★ Favoritos", 28), count));
        grid.Add(search, 0, 1); grid.Add(kind, 0, 2); grid.Add(items, 0, 3); Content = grid;
        search.TextChanged += (_, _) => Refresh();
        kind.SelectedIndexChanged += (_, _) => Refresh();
        items.SelectionChanged += async (_, e) =>
        {
            if (e.CurrentSelection.FirstOrDefault() is not MediaItem item || AppServices.ActiveAccount is not { } account) return;
            items.SelectedItem = null;
            try
            {
                if (item.Kind == MediaKind.Movie || item.HasEpisodes) await Navigation.PushAsync(new MediaDetailsPage(account, item));
                else await PlaybackService.PlayAsync(this, item);
            }
            catch (Exception ex) { await Ui.ErrorAsync(this, ex); }
        };
        void Adapt()
        {
            var viewport = Ui.Viewport(this);
            Ui.UpdateColumns(items, viewport.Width - 40);
        }
        SizeChanged += (_, _) => Adapt();
        Loaded += (_, _) => Adapt();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing(); FavoritesService.Changed += Refresh; Refresh();
    }
    protected override void OnDisappearing()
    {
        FavoritesService.Changed -= Refresh; base.OnDisappearing();
    }
    private void Refresh()
    {
        var query = search.Text?.Trim() ?? "";
        var result = FavoritesService.Items.Where(i => (kind.SelectedIndex == 0 || (int)i.Kind == kind.SelectedIndex - 1)
            && i.Name.Contains(query, StringComparison.OrdinalIgnoreCase)).OrderBy(i => i.Name).ToList();
        items.ItemsSource = result;
        count.Text = LanguageService.Format("{0} favoritos · {1}", result.Count,
            AppServices.ActiveAccount?.Name ?? LanguageService.Text("Abra uma lista"));
    }
}
