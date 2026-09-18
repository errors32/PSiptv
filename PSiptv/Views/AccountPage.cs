using PSiptv.Core;
using PSiptv.Services;

namespace PSiptv.Views;

public sealed class AccountPage : LocalizedPage
{
    private readonly PlaylistAccount? original;
    private readonly Entry name = Ui.Entry("Ex.: TV de casa");
    private readonly Entry url = Ui.Entry("https://fornecedor.pt:porta");
    private readonly Entry username = Ui.Entry("Nome de utilizador");
    private readonly Entry password = Ui.Entry("Palavra-passe", true);
    private readonly Entry epg = Ui.Entry("https://fornecedor.pt/guia.xml (opcional)");
    private readonly Entry pin = Ui.Entry("4 a 8 algarismos (opcional)", true);
    private readonly Entry confirm = Ui.Entry("Confirmar novo PIN", true);
    private static readonly (ProviderType Type, string Name)[] ProviderOptions =
    [
        (ProviderType.Xtream, "Xtream Codes"), (ProviderType.M3U, "Link M3U"),
        (ProviderType.LocalM3U, "Ficheiro M3U local"), (ProviderType.Stalker, "Stalker Portal"),
        (ProviderType.Jellyfin, "Jellyfin"), (ProviderType.Plex, "Plex"),
        (ProviderType.Tvheadend, "Tvheadend"), (ProviderType.HDHomeRun, "HDHomeRun")
    ];
    private readonly Picker provider = new() { Title = LanguageService.Text("Tipo de conta"), ItemsSource = ProviderOptions.Select(item => LanguageService.Text(item.Name)).ToArray() };
    private readonly Switch removePin = new();
    private readonly Label status = Ui.Text("", 14, true);
    private readonly CancellationTokenSource lifetime = new();
    private FileResult? selectedLocalFile;

    public AccountPage(PlaylistAccount? account = null)
    {
        original = account;
        Ui.Page(this, account is null ? "Adicionar lista" : "Editar lista");
        provider.SetDynamicResource(Picker.TextColorProperty, "Ink");
        pin.Keyboard = confirm.Keyboard = Keyboard.Numeric;
        pin.MaxLength = confirm.MaxLength = 8;
        url.Keyboard = epg.Keyboard = Keyboard.Url;
        username.IsTextPredictionEnabled = false;
        name.Text = account?.Name;
        url.Text = account?.Url;
        username.Text = account?.Username;
        password.Text = account?.Password;
        epg.Text = account?.EpgUrl;
        var usernameLabel = Ui.Text("Utilizador");
        var passwordLabel = Ui.Text("Palavra-passe");
        var credentials = Ui.Stack(usernameLabel, username, passwordLabel, password);
        var chooseFile = Ui.Button("Escolher ficheiro M3U", ChooseLocalFileAsync);
        provider.SelectedIndexChanged += (_, _) =>
        {
            var type = SelectedProvider;
            credentials.IsVisible = type is ProviderType.Xtream or ProviderType.Stalker or ProviderType.Jellyfin or ProviderType.Plex or ProviderType.Tvheadend;
            username.IsVisible = usernameLabel.IsVisible = type is ProviderType.Xtream or ProviderType.Stalker or ProviderType.Tvheadend;
            password.IsVisible = passwordLabel.IsVisible = type is ProviderType.Xtream or ProviderType.Jellyfin or ProviderType.Plex or ProviderType.Tvheadend;
            usernameLabel.Text = LanguageService.Text(type == ProviderType.Stalker ? "Endereço MAC" : "Utilizador");
            passwordLabel.Text = LanguageService.Text(type is ProviderType.Jellyfin or ProviderType.Plex ? "Token de acesso" : "Palavra-passe");
            chooseFile.IsVisible = type == ProviderType.LocalM3U;
            url.IsReadOnly = type == ProviderType.LocalM3U;
            url.Placeholder = type switch
            {
                ProviderType.M3U => LanguageService.Text("https://fornecedor.pt/lista.m3u"),
                ProviderType.LocalM3U => LanguageService.Text("Escolha um ficheiro .m3u ou .m3u8"),
                ProviderType.HDHomeRun => "http://endereço-do-hdhomerun",
                _ => LanguageService.Text("https://servidor:porta")
            };
        };
        provider.SelectedIndex = Math.Max(0, Array.FindIndex(ProviderOptions, option => option.Type == (account?.Provider ?? ProviderType.Xtream)));
        var remove = Ui.Stack(Ui.Text("Remover PIN atual"), removePin);
        remove.IsVisible = account?.IsProtected == true;
        var heading = Ui.Stack(
            Ui.Text(account is null ? "A sua televisão começa aqui." : "Gerir esta lista", 28),
            Ui.Text("Ligue a sua conta e organize os conteúdos num só lugar.", 14, true));
        var connection = Ui.Stack(
            Ui.Text("Nome da lista"), name, Ui.Text("Ligação"), provider,
            Ui.Text("Endereço do servidor ou da lista"), url, chooseFile, credentials);
        var access = Ui.Stack(
            Ui.Text("Guia TV · XMLTV"), epg,
            Ui.Text("PIN de acesso"),
            Ui.Text(account?.IsProtected == true ? "Deixe vazio para manter o PIN atual." : "Opcional. Será pedido ao abrir a lista.", 12, true),
            pin, confirm, remove, status, Ui.Button("Testar ligação e guardar", SaveAsync, true));

        if (Ui.IsTelevision)
        {
            var television = new Grid
            {
                Padding = 14, ColumnSpacing = 12, RowSpacing = 10,
                MaximumWidthRequest = 1100,
                ColumnDefinitions = [new(GridLength.Star), new(GridLength.Star)],
                RowDefinitions = [new(GridLength.Auto), new(GridLength.Auto)]
            };
            television.Add(heading);
            Grid.SetColumnSpan(heading, 2);
            television.Add(Ui.Card(connection), 0, 1);
            television.Add(Ui.Card(access), 1, 1);
            Content = new ScrollView { Content = television };
        }
        else
        {
            var stack = Ui.Stack(heading, connection, access);
            stack.Padding = 24; stack.MaximumWidthRequest = 680; stack.HorizontalOptions = LayoutOptions.Fill;
            Content = new ScrollView { Content = stack };
        }
    }

    private async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(name.Text)) throw new InvalidOperationException("Dê um nome à lista.");
        var providerType = SelectedProvider;
        var accountId = original?.Id ?? Guid.NewGuid().ToString("N");
        var savedUrl = providerType == ProviderType.LocalM3U
            ? await SaveLocalFileAsync(accountId)
            : WebAddress.Require(url.Text ?? "").AbsoluteUri;
        if (!string.IsNullOrWhiteSpace(epg.Text)) WebAddress.Require(epg.Text);
        if (providerType == ProviderType.Xtream && (string.IsNullOrWhiteSpace(username.Text) || string.IsNullOrEmpty(password.Text)))
            throw new InvalidOperationException("Preencha o utilizador e a palavra-passe Xtream.");
        if (providerType == ProviderType.Stalker && string.IsNullOrWhiteSpace(username.Text))
            throw new InvalidOperationException("Introduza o endereço MAC do dispositivo Stalker.");
        if (providerType is ProviderType.Jellyfin or ProviderType.Plex && string.IsNullOrWhiteSpace(password.Text))
            throw new InvalidOperationException("Introduza o token de acesso do servidor.");
        if (providerType == ProviderType.Xtream && (new Uri(savedUrl).Query.Length > 0 || new Uri(savedUrl).Fragment.Length > 0 || new Uri(savedUrl).AbsolutePath.EndsWith(".php", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("No Xtream, indique apenas o endereço base do servidor, sem player_api.php nem parâmetros.");
        var newPin = pin.Text ?? "";
        if (newPin != (confirm.Text ?? "")) throw new InvalidOperationException("Os PINs não coincidem.");
        if (removePin.IsToggled && newPin.Length > 0) throw new InvalidOperationException("Escolha entre remover o PIN atual ou definir um novo.");
        var hash = newPin.Length > 0 ? await Task.Run(() => PinProtection.Hash(newPin)) : removePin.IsToggled ? "" : original?.PinHash ?? "";
        var account = new PlaylistAccount
        {
            Id = accountId, Name = name.Text.Trim(),
            Provider = providerType,
            Url = providerType is ProviderType.M3U or ProviderType.LocalM3U ? savedUrl : savedUrl.TrimEnd('/'),
            Username = providerType is ProviderType.Xtream or ProviderType.Stalker or ProviderType.Tvheadend ? username.Text?.Trim() ?? "" : "",
            Password = providerType is ProviderType.Xtream or ProviderType.Jellyfin or ProviderType.Plex or ProviderType.Tvheadend ? password.Text ?? "" : "",
            EpgUrl = epg.Text?.Trim() ?? "", PinHash = hash
        };
        status.Text = "A verificar a ligação…";
        try
        {
            await AppServices.Client.ValidateAsync(account, lifetime.Token);
            lifetime.Token.ThrowIfCancellationRequested();
            await AppServices.Accounts.SaveAsync(account);
            if (original is not null && (original.Provider != account.Provider || original.Url != account.Url || original.EpgUrl != account.EpgUrl))
                await EpgService.DeleteAsync(account.Id);
            if (AppServices.ActiveAccount?.Id == account.Id) AppServices.Lock();
            await Navigation.PopAsync();
        }
        finally { status.Text = ""; }
    }

    private ProviderType SelectedProvider => provider.SelectedIndex >= 0 && provider.SelectedIndex < ProviderOptions.Length
        ? ProviderOptions[provider.SelectedIndex].Type : ProviderType.Xtream;

    private async Task ChooseLocalFileAsync()
    {
        var picked = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = LanguageService.Text("Escolher ficheiro M3U") });
        if (picked is null) return;
        var extension = Path.GetExtension(picked.FileName);
        if (!extension.Equals(".m3u", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".m3u8", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Escolha um ficheiro .m3u ou .m3u8.");
        selectedLocalFile = picked;
        url.Text = picked.FileName;
    }

    private async Task<string> SaveLocalFileAsync(string accountId)
    {
        if (selectedLocalFile is null)
        {
            var existing = original?.Provider == ProviderType.LocalM3U ? original.Url : "";
            if (existing.Length > 0 && File.Exists(existing)) return existing;
            throw new InvalidOperationException("Escolha um ficheiro M3U local.");
        }
        var folder = Path.Combine(FileSystem.AppDataDirectory, "local-playlists");
        Directory.CreateDirectory(folder);
        var extension = Path.GetExtension(selectedLocalFile.FileName).ToLowerInvariant();
        var path = Path.Combine(folder, accountId + extension);
        await using var input = await selectedLocalFile.OpenReadAsync();
        await using var output = File.Create(path);
        await input.CopyToAsync(output, lifetime.Token);
        return path;
    }

    protected override void OnDisappearing() { base.OnDisappearing(); lifetime.Cancel(); }
}
