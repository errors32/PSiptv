using PSiptv.Services;

namespace PSiptv.Views;

public sealed class ProfilePage : LocalizedPage
{
    private readonly Label accountName = Ui.Text("", 22);
    private readonly Grid tiles = new() { ColumnSpacing = 12, RowSpacing = 12 };
    private readonly Button[] actions;

    public ProfilePage(Func<Task> changePlaylist, Func<UserProfile, Task> changeProfile)
    {
        Ui.Page(this, "Configurações");
        NavigationPage.SetHasNavigationBar(this, false);

        var back = Ui.Button("‹", () => Navigation.PopAsync());
        back.FontSize = 32;
        back.BackgroundColor = Colors.Transparent;
        SemanticProperties.SetDescription(back, LanguageService.Text("Voltar"));
        var title = Ui.Text("Configurações", 24);
        title.FontAttributes = FontAttributes.Bold;
        title.HorizontalOptions = LayoutOptions.Center;
        title.VerticalOptions = LayoutOptions.Center;
        var header = new Grid
        {
            ColumnDefinitions = [new ColumnDefinition(new GridLength(48)), new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(new GridLength(48))]
        };
        header.Add(back);
        header.Add(title, 1);

        accountName.FontAttributes = FontAttributes.Bold;
        accountName.VerticalOptions = LayoutOptions.Center;
        accountName.MaxLines = 2;
        accountName.LineBreakMode = LineBreakMode.TailTruncation;
        var account = new Grid
        {
            ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(new GridLength(40)),
                new ColumnDefinition(new GridLength(48))],
            ColumnSpacing = 8
        };
        account.Add(accountName);
        account.Add(new Image { Source = "psiptv_mark.svg", HeightRequest = 32, WidthRequest = 32 }, 1);
        var switchPlaylist = Ui.Button("", () => RunOnMainAsync(changePlaylist));
        switchPlaylist.ImageSource = Ui.FontIconSource(FaIcons.ArrowsRotate, 24);
        switchPlaylist.WidthRequest = 48;
        switchPlaylist.Padding = 10;
        switchPlaylist.BackgroundColor = Colors.Transparent;
        SemanticProperties.SetDescription(switchPlaylist, LanguageService.Text("Trocar playlist"));
        ToolTipProperties.SetText(switchPlaylist, "Trocar playlist");
        account.Add(switchPlaylist, 2);
        var accountCard = Ui.Card(account);
        accountCard.MinimumHeightRequest = Ui.IsTelevision ? 64 : 88;
        accountCard.SetDynamicResource(BackgroundColorProperty, "ProfileTile");
        actions =
        [
            Tile("Perfis de utilizador", FaIcons.Users, () => Navigation.PushAsync(new UserProfilesPage(changeProfile))),
            Tile("Sincronização e cópia de segurança", FaIcons.CloudArrowUp, () => Navigation.PushAsync(new BackupPage())),
            Tile("Lembretes de programas", FaIcons.Bell, () => Navigation.PushAsync(new RemindersPage())),
            Tile("Gravações DVR", FaIcons.RecordVinyl, () => Navigation.PushAsync(new RecordingsPage())),
            Tile("Gravações recorrentes", FaIcons.ArrowsRotate, () => Navigation.PushAsync(new SeriesRecordingRulesPage())),
            Tile("Diagnóstico de fontes", FaIcons.Stethoscope, () => Navigation.PushAsync(new SourceDiagnosticsPage())),
            Tile("Downloads offline", FaIcons.Download, () => Navigation.PushAsync(new OfflineDownloadsPage())),
            Tile("Gestor de armazenamento", FaIcons.HardDrive, () => Navigation.PushAsync(new StorageManagerPage())),
            SettingsTile(SettingsSection.General, "Configurações Gerais", FaIcons.Gear),
            SettingsTile(SettingsSection.Browser, "Browser", FaIcons.Globe),
            SettingsTile(SettingsSection.Epg, "EPG", FaIcons.Calendar),
            SettingsTile(SettingsSection.Parental, "Controlo Parental", FaIcons.Shield),
            SettingsTile(SettingsSection.Categories, "Personalizar Categorias", FaIcons.LayerGroup),
            SettingsTile(SettingsSection.Player, "Configurações do Player", FaIcons.CirclePlay),
            SettingsTile(SettingsSection.About, "Sobre", FaIcons.CircleInfo),
            SettingsTile(SettingsSection.Theme, "Tema", FaIcons.Palette),
            SettingsTile(SettingsSection.Lists, "As Suas Listas", FaIcons.List)
        ];
        foreach (var action in actions) tiles.Add(action);

        var body = Ui.Stack(accountCard, tiles);
        body.Spacing = Ui.IsTelevision ? 10 : 16;
        var version = Ui.Text(LanguageService.Format("Versão: {0}", AppInfo.Current.VersionString), 12, true);
        version.HorizontalOptions = LayoutOptions.Center;
        var root = new Grid
        {
            Padding = Ui.IsTelevision ? new Thickness(24, 8, 24, 10) : new Thickness(20, 12, 20, 20),
            RowSpacing = Ui.IsTelevision ? 10 : 24,
            SafeAreaEdges = new SafeAreaEdges(SafeAreaRegions.Container),
            RowDefinitions = [new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star), new RowDefinition(GridLength.Auto)]
        };
        root.Add(header);
        root.Add(new ScrollView { Content = body }, 0, 1);
        root.Add(version, 0, 2);
        Content = root;
        SizeChanged += (_, _) => ArrangeTiles();
        Loaded += (_, _) => ArrangeTiles();
        ArrangeTiles();
    }

    private Button SettingsTile(SettingsSection section, string title, string icon) =>
        Tile(title, icon, () => Navigation.PushAsync(new SettingsPage(section)));

    private async Task RunOnMainAsync(Func<Task> action)
    {
        var owner = Navigation.NavigationStack.FirstOrDefault();
        await Navigation.PopAsync();
        try { await action(); }
        catch (Exception ex)
        {
            if (owner is not null) await Ui.ErrorAsync(owner, ex);
        }
    }

    private static Button Tile(string title, string icon, Func<Task> action)
    {
        var button = Ui.Button(title, action);
        button.ImageSource = Ui.FontIconSource(icon, Ui.IsTelevision ? 24 : 32);
        button.ContentLayout = new Button.ButtonContentLayout(
            Ui.IsTelevision ? Button.ButtonContentLayout.ImagePosition.Left : Button.ButtonContentLayout.ImagePosition.Top,
            Ui.IsTelevision ? 8 : 12);
        button.MinimumHeightRequest = Ui.IsTelevision ? 76 : 124;
        button.FontSize = Ui.IsTelevision ? 14 : 15;
        button.Padding = Ui.IsTelevision ? new Thickness(10, 8) : new Thickness(12, 18);
        button.SetDynamicResource(BackgroundColorProperty, "ProfileTile");
        return button;
    }

    private void ArrangeTiles()
    {
        var width = Ui.Viewport(this).Width;
        var columns = width >= 650 ? 3 : 2;
        tiles.ColumnDefinitions.Clear();
        tiles.RowDefinitions.Clear();
        for (var i = 0; i < columns; i++) tiles.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        for (var i = 0; i < (actions.Length + columns - 1) / columns; i++)
            tiles.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        for (var i = 0; i < actions.Length; i++)
        {
            Grid.SetColumn(actions[i], i % columns);
            Grid.SetRow(actions[i], i / columns);
        }
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        accountName.Text = $"{UserProfileService.Active.Avatar} {UserProfileService.Active.Name} · " +
            (AppServices.ActiveAccount?.Name ?? LanguageService.Text("Nenhuma lista aberta"));
    }
}


