using PSiptv.Core;
using PSiptv.Services;

namespace PSiptv.Views;

public enum SettingsSection
{
    General,
    Browser,
    Epg,
    Parental,
    Categories,
    Player,
    About,
    Theme,
    Lists
}

public sealed partial class SettingsPage : LocalizedPage
{
    private readonly VerticalStackLayout accounts = new() { Spacing = 12 };
    private readonly SettingsSection section;

    public SettingsPage(SettingsSection section)
    {
        this.section = section;
        var sectionTitle = SectionTitle(section);
        Ui.Page(this, sectionTitle);

        var modes = new[] { ThemeMode.Dark, ThemeMode.Light, ThemeMode.System };
        var theme = new Picker
        {
            Title = "Perfil de cores",
            ItemsSource = new[] { "Escuro", "Claro", "Do sistema" },
            SelectedIndex = Array.IndexOf(modes, ThemeService.Mode),
            MinimumHeightRequest = 48
        };
        theme.SetDynamicResource(Picker.TextColorProperty, "Ink");
        theme.SelectedIndexChanged += (_, _) =>
        {
            if (theme.SelectedIndex >= 0) ThemeService.Apply(mode: modes[theme.SelectedIndex]);
        };
        var openLast = new Switch { IsToggled = Preferences.Default.Get("openLastPlaylist", true) };
        openLast.Toggled += (_, e) => Preferences.Default.Set("openLastPlaylist", e.Value);
        var externalEnabled = Preferences.Default.Get("externalPlayer", false) &&
                              ExternalPlayerService.IsConfigured;
        if (!externalEnabled) Preferences.Default.Set("externalPlayer", false);
        var external = new Switch { IsToggled = externalEnabled };
        var selectedPlayer = Ui.Text("", 12, true);
        void RefreshExternalPlayer() => selectedPlayer.Text = !ExternalPlayerService.IsConfigured
            ? LanguageService.Text("Nenhum leitor selecionado")
            : $"{LanguageService.Text("Leitor selecionado")}: {ExternalPlayerService.SelectedName}";
        var changingExternal = false;
        async Task ChooseExternalPlayerAsync(bool disableOnCancel)
        {
            var selected = await ExternalPlayerService.ChooseAsync(this);
            if (!selected && !disableOnCancel)
            {
                RefreshExternalPlayer();
                return;
            }
            changingExternal = true;
            external.IsToggled = selected;
            changingExternal = false;
            Preferences.Default.Set("externalPlayer", selected);
            RefreshExternalPlayer();
        }
        external.Toggled += async (_, e) =>
        {
            if (changingExternal) return;
            if (!e.Value)
            {
                Preferences.Default.Set("externalPlayer", false);
                return;
            }
            try { await ChooseExternalPlayerAsync(disableOnCancel: true); }
            catch (Exception ex)
            {
                changingExternal = true;
                external.IsToggled = false;
                changingExternal = false;
                Preferences.Default.Set("externalPlayer", false);
                await Ui.ErrorAsync(this, ex);
            }
        };
        RefreshExternalPlayer();
        var colors = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap, Direction = Microsoft.Maui.Layouts.FlexDirection.Row };
        foreach (var (label, hex) in new[] { ("Menta", "#36D6B0"), ("Azul", "#69B4FF"), ("Violeta", "#B7A1FF"), ("Coral", "#FF9F9F"), ("Âmbar", "#F7C96A") })
        {
            var button = Ui.Button(label, () => { ThemeService.Apply(hex); return Task.CompletedTask; });
            button.BackgroundColor = Color.FromArgb(hex); button.TextColor = Color.FromArgb("#071520"); button.Margin = new Thickness(0, 0, 8, 8);
            colors.Add(button);
        }
        var stack = Ui.Stack();
        switch (section)
        {
            case SettingsSection.Theme:
                stack.Add(Ui.Card(Ui.Stack(Ui.Text("Aparência", 20), Ui.Text("Perfil de cores"), theme,
                    Ui.Text("Do sistema acompanha automaticamente a aparência do dispositivo.", 12, true),
                    Ui.Text("Cor de destaque"), colors)));
                break;
            case SettingsSection.Lists:
                stack.Add(Ui.Button("+ Adicionar lista", () => Navigation.PushAsync(new AccountPage()), true));
                stack.Add(accounts);
                stack.Add(Ui.Text("As credenciais são guardadas no armazenamento seguro do dispositivo. O PIN volta a ser pedido depois de bloquear ou sair da aplicação.", 12, true));
                break;
            default:
                stack.Add(BuildFeatures(section));
                if (section == SettingsSection.General)
                    stack.Add(Ui.Card(Ui.Stack(Ui.Text("Arranque e segurança", 20),
                        Ui.Text("Abrir a última lista ao iniciar"), openLast,
                        Ui.Text("As listas protegidas continuam a pedir PIN ou biometria.", 12, true))));
                if (section == SettingsSection.Player)
                    stack.Add(Ui.Card(Ui.Stack(Ui.Text("Leitor externo", 20),
                        Ui.Text("Preferir leitor externo"), external,
                        selectedPlayer,
                        Ui.Button("Escolher leitor", () => ChooseExternalPlayerAsync(disableOnCancel: false)),
                        Ui.Text(ExternalPlayerService.ExperienceDescription, 12, true))));
                break;
        }
        stack.Padding = Ui.IsTelevision ? 14 : 24;
        stack.MaximumWidthRequest = Ui.IsTelevision ? 1100 : 800;
        Content = new ScrollView { Content = stack };
    }

    private static string SectionTitle(SettingsSection section) => section switch
    {
        SettingsSection.General => "Configurações Gerais",
        SettingsSection.Browser => "Browser",
        SettingsSection.Epg => "EPG",
        SettingsSection.Parental => "Controlo Parental",
        SettingsSection.Categories => "Personalizar Categorias",
        SettingsSection.Player => "Configurações do Player",
        SettingsSection.About => "Sobre",
        SettingsSection.Theme => "Tema",
        SettingsSection.Lists => "As Suas Listas",
        _ => "Configurações"
    };

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try { await ReloadAsync(); } catch (Exception ex) { await Ui.ErrorAsync(this, ex); }
    }

    private async Task ReloadAsync()
    {
        if (AppServices.ActiveAccount is { } active) await HistoryService.LoadAsync(active.Id);
        foreach (var refresh in refreshRows) refresh();
        if (section != SettingsSection.Lists) return;
        accounts.Clear();
        var saved = await AppServices.Accounts.LoadAsync();
        if (saved.Count == 0) accounts.Add(Ui.Text("Ainda não adicionou nenhuma lista.", 14, true));
        foreach (var account in saved)
        {
            accounts.Add(Ui.Card(Ui.Stack(Ui.Text(account.Name, 18), Ui.Text(account.Description, 12, true),
                Ui.Button("Editar ligação / PIN", async () =>
                {
                    if (await PinPage.AuthorizeAsync(this, account)) await Navigation.PushAsync(new AccountPage(account));
                }), Ui.Button("Diagnosticar fonte", async () =>
                {
                    if (await PinPage.AuthorizeAsync(this, account)) await Navigation.PushAsync(new SourceDiagnosticsPage(account));
                }), Ui.Button("Eliminar lista", async () =>
                {
                    if (!await PinPage.AuthorizeAsync(this, account)) return;
                    if (!await DisplayAlertAsync("Eliminar lista", $"Eliminar «{account.Name}» deste dispositivo?", "Eliminar", "Cancelar")) return;
                    await AppServices.Accounts.DeleteAsync(account.Id);
                    if (AppServices.ActiveAccount?.Id == account.Id) AppServices.Lock();
                    await ReloadAsync();
                }))));
        }
    }
}



