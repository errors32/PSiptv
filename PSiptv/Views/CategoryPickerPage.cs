using PSiptv.Services;

namespace PSiptv.Views;

public sealed class CategoryPickerPage : LocalizedPage
{
    private readonly TaskCompletionSource<string?> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly SearchBar search = new() { Placeholder = LanguageService.Text("Pesquisar categorias") };
    private readonly CollectionView categories = new() { SelectionMode = SelectionMode.Single };
    private readonly IReadOnlyList<string> values;

    private CategoryPickerPage(string title, IReadOnlyList<string> values, string? selected)
    {
        this.values = values;
        Ui.Page(this, title);
        NavigationPage.SetHasNavigationBar(this, false);
        search.SetDynamicResource(SearchBar.TextColorProperty, "Ink");
        search.SetDynamicResource(SearchBar.PlaceholderColorProperty, "Muted");
        search.TextChanged += (_, _) => Filter();

        categories.EmptyView = Ui.Text("Nenhuma categoria encontrada.", 15, true);
        categories.ItemTemplate = new DataTemplate(() =>
        {
            var marker = Ui.Text("○", 24);
            var name = Ui.Text("", 16);
            name.SetBinding(Label.TextProperty, ".");
            marker.BindingContextChanged += (_, _) => marker.Text = Equals(marker.BindingContext, selected) ? "●" : "○";
            var row = new Grid
            {
                Padding = new Thickness(8, 12), ColumnSpacing = 12,
                ColumnDefinitions = [new ColumnDefinition(new GridLength(32)), new ColumnDefinition(GridLength.Star)]
            };
            row.Add(marker); row.Add(name, 1);
            return Ui.FocusableCard(row, value => categories.SelectedItem = value);
        });
        categories.SelectionChanged += async (_, e) =>
        {
            if (e.CurrentSelection.FirstOrDefault() is string value) await CloseAsync(value);
        };

        var cancel = Ui.Button("Cancelar", () => CloseAsync(null));
        var header = new Grid
        {
            ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)]
        };
        header.Add(Ui.Text(title, 24)); header.Add(cancel, 1);
        var layout = new Grid
        {
            Padding = Ui.IsTelevision ? new Thickness(28, 14) : 16,
            RowSpacing = Ui.IsTelevision ? 8 : 12,
            SafeAreaEdges = new SafeAreaEdges(SafeAreaRegions.Container),
            RowDefinitions = [new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star)]
        };
        layout.Add(header); layout.Add(search, 0, 1); layout.Add(categories, 0, 2);
        Content = layout;
        Filter();
        Loaded += (_, _) => { if (Ui.IsTelevision) Dispatcher.Dispatch(() => categories.Focus()); };
    }

    public static async Task<string?> ChooseAsync(Page owner, string title, IReadOnlyList<string> values, string? selected)
    {
        var page = new CategoryPickerPage(title, values, selected);
        await owner.Navigation.PushModalAsync(page);
        return await page.completion.Task;
    }

    private void Filter()
    {
        var query = search.Text?.Trim() ?? "";
        categories.ItemsSource = values.Where(value => value.Contains(query, StringComparison.CurrentCultureIgnoreCase)).ToArray();
    }

    private async Task CloseAsync(string? value)
    {
        if (!completion.TrySetResult(value)) return;
        await Navigation.PopModalAsync();
    }

    protected override bool OnBackButtonPressed()
    {
        completion.TrySetResult(null);
        return base.OnBackButtonPressed();
    }

    protected override void OnDisappearing()
    {
        completion.TrySetResult(null);
        base.OnDisappearing();
    }
}
