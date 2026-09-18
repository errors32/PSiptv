using PSiptv.Core;
using PSiptv.Services;

namespace PSiptv.Views;

public sealed class AutomotivePage : LocalizedPage
{
    private readonly Label accountName = Ui.Text("Nenhuma lista aberta", 24, true);
    private readonly Label status = Ui.Text("Escolha uma categoria para ver os canais.", 20, true);
    private readonly Button categoryButton;
    private readonly CollectionView channels;
    private IReadOnlyList<MediaItem> catalog = [];
    private string? selectedCategory;
    private bool opening;
    private CancellationTokenSource? loading;

    public AutomotivePage()
    {
        Ui.Page(this, "PSiptv no automóvel");
        NavigationPage.SetHasNavigationBar(this, false);
        categoryButton = Ui.Button("Escolher categoria", ChooseCategoryAsync, true);
        channels = CreateChannelList();
        channels.EmptyView = Ui.Text("Escolha uma categoria ou atualize a lista de canais.", 22, true);
        channels.SelectionChanged += async (_, e) =>
        {
            if (e.CurrentSelection.FirstOrDefault() is not MediaItem channel) return;
            channels.SelectedItem = null;
            try { await PlaybackService.PlayAsync(this, channel); }
            catch (Exception ex) { await Ui.ErrorAsync(this, ex); }
        };

        var actions = new FlexLayout
        {
            Direction = Microsoft.Maui.Layouts.FlexDirection.Row,
            Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap,
            JustifyContent = Microsoft.Maui.Layouts.FlexJustify.Start,
            AlignItems = Microsoft.Maui.Layouts.FlexAlignItems.Center
        };
        actions.Add(Ui.Button("Escolher lista", ChooseAccountAsync));
        actions.Add(categoryButton);
        actions.Add(Ui.Button("Atualizar canais", RefreshAsync));
        foreach (var child in actions.Children.OfType<View>()) child.Margin = new Thickness(0, 0, 24, 12);

        var header = Ui.Stack(Ui.Text("PSiptv", 36), accountName, actions, status);
        var grid = new Grid
        {
            Padding = new Thickness(48, 32), RowSpacing = 24,
            SafeAreaEdges = new SafeAreaEdges(SafeAreaRegions.Container),
            RowDefinitions = [new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star)]
        };
        grid.Add(header); grid.Add(channels, 0, 1); Content = grid;
        SizeChanged += (_, _) => Adapt();
        Loaded += (_, _) => Adapt();
        AppServices.Locked += Clear;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (AppServices.ActiveAccount is not null) { ShowCatalog(); return; }
        try { await OpenLastAccountAsync(); }
        catch (Exception ex) { await Ui.ErrorAsync(this, ex); }
    }

    private async Task OpenLastAccountAsync()
    {
        if (opening) return;
        opening = true;
        try
        {
            var accounts = await AppServices.Accounts.LoadAsync();
            if (accounts.Count == 0)
            {
                accountName.Text = "Adicione primeiro uma lista quando o automóvel estiver estacionado.";
                status.Text = "Não existem listas guardadas.";
                await Navigation.PushAsync(new AccountPage());
                return;
            }
            var lastId = Preferences.Default.Get("lastAccountId", "");
            await OpenAccountAsync(accounts.FirstOrDefault(a => a.Id == lastId) ?? accounts[0]);
        }
        finally { opening = false; }
    }

    private async Task ChooseAccountAsync()
    {
        if (opening) return;
        opening = true;
        try
        {
            var accounts = await AppServices.Accounts.LoadAsync();
            if (accounts.Count == 0) { await Navigation.PushAsync(new AccountPage()); return; }
            var labels = accounts.Select((account, index) => $"{index + 1}. {account.Name}").ToArray();
            var selected = await LanguageService.ActionSheetAsync(this, "Escolher lista", "Cancelar", null, labels);
            var index = Array.IndexOf(labels, selected);
            if (index >= 0) await OpenAccountAsync(accounts[index]);
        }
        finally { opening = false; }
    }

    private async Task OpenAccountAsync(PlaylistAccount account)
    {
        var version = AppServices.SessionVersion;
        if (!await PinPage.AuthorizeAsync(this, account) || version != AppServices.SessionVersion) return;
        loading?.Cancel();
        AppServices.Activate(account);
        await CatalogOptionsService.LoadAsync(account.Id);
        catalog = CatalogOptionsService.Catalogs.GetValueOrDefault(MediaKind.Channel) ?? [];
        EpgService.RefreshInBackground(account, catalog);
        var savedCategory = Preferences.Default.Get($"automotive.category.{account.Id}", "");
        selectedCategory = CatalogOptionsService.Current.Categories(catalog, MediaKind.Channel).Contains(savedCategory) ? savedCategory : null;
        accountName.Text = account.Name;
        ShowCatalog();
    }

    private async Task ChooseCategoryAsync()
    {
        if (AppServices.ActiveAccount is null) { await ChooseAccountAsync(); return; }
        var choices = CatalogOptionsService.Current.Categories(catalog, MediaKind.Channel).ToArray();
        if (choices.Length == 0) { status.Text = "Atualize a lista de canais antes de escolher uma categoria."; return; }
        var selected = await CategoryPickerPage.ChooseAsync(this, "Escolher categoria", choices, selectedCategory);
        if (selected is null) return;
        selectedCategory = selected;
        Preferences.Default.Set($"automotive.category.{AppServices.ActiveAccount.Id}", selected);
        ShowCatalog();
    }

    private async Task RefreshAsync()
    {
        if (AppServices.ActiveAccount is null) { await ChooseAccountAsync(); if (AppServices.ActiveAccount is null) return; }
        var account = AppServices.ActiveAccount;
        loading?.Cancel();
        var source = new CancellationTokenSource(); loading = source;
        status.Text = "A carregar canais…";
        try
        {
            var loaded = await AppServices.Client.GetCatalogAsync(account, MediaKind.Channel, source.Token);
            source.Token.ThrowIfCancellationRequested();
            if (AppServices.ActiveAccount?.Id != account.Id) return;
            await CatalogCacheService.SaveAsync(account.Id, MediaKind.Channel, loaded);
            catalog = loaded;
            CatalogOptionsService.Catalogs[MediaKind.Channel] = loaded;
            EpgService.RefreshInBackground(account, loaded);
            if (selectedCategory is not null && !CatalogOptionsService.Current.Categories(catalog, MediaKind.Channel).Contains(selectedCategory))
                selectedCategory = null;
            ShowCatalog();
        }
        catch (OperationCanceledException) { if (!source.IsCancellationRequested) status.Text = "O fornecedor demorou demasiado tempo. Tente novamente."; }
        catch (Exception ex) { if (!source.IsCancellationRequested) { status.Text = "Não foi possível carregar os canais."; await Ui.ErrorAsync(this, ex); } }
        finally { if (ReferenceEquals(loading, source)) loading = null; source.Dispose(); }
    }

    private void ShowCatalog()
    {
        categoryButton.Text = selectedCategory ?? LanguageService.Text("Escolher categoria");
        if (selectedCategory is null)
        {
            channels.ItemsSource = Array.Empty<MediaItem>();
            status.Text = catalog.Count == 0 ? "Atualize a lista de canais e escolha uma categoria." : "Escolha uma categoria para ver os canais.";
            return;
        }
        var visible = CatalogOptionsService.Current.Filter(catalog, MediaKind.Channel, selectedCategory, "");
        channels.ItemsSource = visible;
        status.Text = LanguageService.Format("{0} canais · Selecione um canal para reproduzir.", visible.Count);
    }

    private CollectionView CreateChannelList()
    {
        CollectionView list = null!;
        list = new CollectionView
        {
            SelectionMode = SelectionMode.Single,
            ItemsLayout = new GridItemsLayout(1, ItemsLayoutOrientation.Vertical) { HorizontalItemSpacing = 18, VerticalItemSpacing = 18 },
            ItemTemplate = new DataTemplate(() =>
            {
                var logo = new LogoImage { WidthRequest = 96, HeightRequest = 72, Aspect = Aspect.AspectFit };
                var name = Ui.Text("", 24); name.FontAttributes = FontAttributes.Bold; name.SetBinding(Label.TextProperty, nameof(MediaItem.Name));
                var row = new Grid { ColumnDefinitions = [new ColumnDefinition(new GridLength(112)), new ColumnDefinition(GridLength.Star)], ColumnSpacing = 16 };
                row.Add(logo); row.Add(name, 1);
                return Ui.FocusableCard(row, value => list.SelectedItem = value);
            })
        };
        return list;
    }

    private void Adapt()
    {
        var viewport = Ui.Viewport(this);
        Ui.UpdateColumns(channels, viewport.Width - 96, 360, 5);
    }

    private void Clear()
    {
        loading?.Cancel(); catalog = []; selectedCategory = null; channels.ItemsSource = null;
        accountName.Text = "Nenhuma lista aberta"; status.Text = "Escolha uma lista.";
    }

    protected override void OnNavigatedFrom(NavigatedFromEventArgs args)
    {
        base.OnNavigatedFrom(args);
        if (!Navigation.NavigationStack.Contains(this)) { loading?.Cancel(); AppServices.Locked -= Clear; }
    }
}
