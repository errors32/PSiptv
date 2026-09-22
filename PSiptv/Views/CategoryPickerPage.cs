using PSiptv.Services;

namespace PSiptv.Views;

public sealed class CategoryPickerPage : LocalizedPage
{
    private readonly TaskCompletionSource<string?> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly SearchBar search = new() { Placeholder = LanguageService.Text("Pesquisar categorias") };
    private readonly CollectionView categories = new()
    {
        // WinUI can terminate inside Microsoft.UI.Xaml when a selected item is
        // changed and its modal visual tree is removed by the same pointer event.
        // Cards already have an explicit tap gesture on Windows, so native
        // CollectionView selection is unnecessary there.
        SelectionMode = DeviceInfo.Platform == DevicePlatform.WinUI
            ? SelectionMode.None
            : SelectionMode.Single
    };
    private readonly IReadOnlyList<string> values;
    private bool closing;

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
            return Ui.FocusableCard(row, value =>
            {
                if (DeviceInfo.Platform == DevicePlatform.WinUI)
                {
                    if (value is string category) _ = CloseAsync(category);
                    return;
                }

                categories.SelectedItem = value;
            });
        });
        categories.SelectionChanged += (_, e) =>
        {
            if (e.CurrentSelection.FirstOrDefault() is string value) _ = CloseAsync(value);
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
        if (closing) return;
        var query = search.Text?.Trim() ?? "";
        if (categories.SelectionMode != SelectionMode.None) categories.SelectedItem = null;
        categories.ItemsSource = values.Where(value => value.Contains(query, StringComparison.CurrentCultureIgnoreCase)).ToArray();
    }

    private async Task CloseAsync(string? value)
    {
        if (closing) return;
        closing = true;

        try
        {
            // Let the WinUI pointer event finish before removing the visual tree
            // that raised it. Popping synchronously from that event causes a
            // native Microsoft.UI.Xaml fail-fast (0xc000027b).
            if (DeviceInfo.Platform == DevicePlatform.WinUI) await Task.Delay(50);
            if (Navigation.ModalStack.Contains(this)) await Navigation.PopModalAsync();
            completion.TrySetResult(value);
        }
        catch (Exception exception)
        {
            completion.TrySetException(exception);
        }
    }

    protected override bool OnBackButtonPressed()
    {
        _ = CloseAsync(null);
        return true;
    }

    protected override void OnDisappearing()
    {
        if (!closing) completion.TrySetResult(null);
        base.OnDisappearing();
    }
}
