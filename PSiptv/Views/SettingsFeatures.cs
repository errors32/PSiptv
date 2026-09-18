using PSiptv.Core;
using PSiptv.Services;

namespace PSiptv.Views;

public sealed partial class SettingsPage
{
    private readonly List<Action> refreshRows = [];
    private View BuildFeatures(SettingsSection section)
    {
        var android = DeviceInfo.Platform == DevicePlatform.Android;
        return section switch
        {
            SettingsSection.General => BuildGeneralSettings(android),
            SettingsSection.Browser => Ui.Stack(
                ActionRow(FaIcons.Link, "Fontes do browser", () => Navigation.PushAsync(new BrowserSourcesPage()),
                    () => BrowserSourcesService.Default?.Name ?? "Sem fonte predefinida")),
            SettingsSection.Epg => Ui.Stack(
                ActionRow(FaIcons.Calendar, "Fonte EPG", EpgAsync, () => AppServices.ActiveAccount is { } a
                    ? LanguageService.Text(a.EpgUrl.Length == 0 ? "Padrão" : "XMLTV personalizado") +
                      (EpgService.LastUpdated(a.Id) is { } t ? " · " + AppOptions.FormatTime(t) : " · " + LanguageService.Text("Ainda não atualizado"))
                    : LanguageService.Text("Abra uma lista")),
                Choice(FaIcons.Bell, "Antecedência dos lembretes", "reminderMinutesBefore", "5",
                    [("À hora de início", "0"), ("5 minutos antes", "5"), ("10 minutos antes", "10"),
                     ("15 minutos antes", "15"), ("30 minutos antes", "30")],
                    value => { _ = ReminderService.RescheduleAllAsync(); })),
            SettingsSection.Parental => Ui.Stack(
                ActionRow(FaIcons.Shield, "Ligar / Desligar", ToggleParentalAsync,
                    () => CatalogOptionsService.Current.ParentalEnabled ? "Ligado" : "Desligado"),
                ActionRow(FaIcons.Key, "Alterar código PIN", ChangeParentalPinAsync),
                ActionRow(FaIcons.Lock, "Categorias protegidas", async () =>
                {
                    if (await AuthorizeParentAsync()) await Navigation.PushAsync(new CategoriesPage(true));
                })),
            SettingsSection.Categories => Ui.Stack(
                ActionRow(FaIcons.ArrowDownWideShort, "Reordenar e mostrar / ocultar categorias",
                    () => OpenAccountPageAsync(() => new CategoriesPage())),
                ActionRow(FaIcons.Plus, "Adicionar / editar categorias personalizadas", async () =>
                {
                    if (!CatalogOptionsService.Current.ParentalEnabled || await AuthorizeParentAsync())
                        await OpenAccountPageAsync(() => new CustomCategoriesPage());
                })),
            SettingsSection.Player => BuildPlayerSettings(android),
            SettingsSection.About => BuildAboutSettings(),
            _ => new ContentView()
        };
    }

    private View BuildAboutSettings()
    {
        var items = new List<View>();
#if ANDROID
        items.Add(ActionRow(FaIcons.Download, "Atualizações da aplicação",
            () => Navigation.PushAsync(new AndroidAppUpdatePage())));
#endif
        items.Add(ActionRow(FaIcons.Shield, "Política de Privacidade",
            () => Navigation.PushAsync(new InformationPage(false))));
        items.Add(ActionRow(FaIcons.FileLines, "Termos de Uso",
            () => Navigation.PushAsync(new InformationPage(true))));
        return Ui.Stack([.. items]);
    }

    private View BuildGeneralSettings(bool android) =>
        Ui.Stack(
            Toggle(FaIcons.WindowRestore, "Imagem em Imagem", "pip", true, PictureInPictureService.Supported),
            Choice(FaIcons.TowerBroadcast, "Formato de Transmissão", "streamFormat", "auto", [("Automático", "auto"), ("MPEGTS (.ts)", "ts"), ("HLS (.m3u8)", "m3u8")]),
            Ui.Text("O formato aplica-se aos canais Xtream. Os endereços M3U são preservados.", 12, true),
            Toggle(FaIcons.Play, "Episódio Auto Play", "episodeAutoPlay", false),
            Number(FaIcons.Clock, "Segundos para Auto Play", "autoPlaySeconds", 30, 0, 60, "s entre episódios"),
            Choice(FaIcons.ArrowsRotate, "Atualização das listas", "catalogUpdateSchedule", "manual",
                [("Manual", "manual"), ("Ao arrancar", "startup"), ("Diariamente", "daily"), ("Semanalmente", "weekly")],
                _ => CatalogUpdateService.ConfigureSchedule()),
            Toggle(FaIcons.Wifi, "Apenas por Wi-Fi", "catalogUpdateWifiOnly", false, true,
                _ => CatalogUpdateService.ConfigureSchedule()),
            Toggle(FaIcons.CloudArrowUp, "Atualizar em segundo plano", "catalogUpdateInBackground", false, true,
                _ => CatalogUpdateService.ConfigureSchedule()),
            ActionRow(FaIcons.CircleInfo, "Última atualização automática", () => Task.CompletedTask,
                () => CatalogUpdateService.LastSummary),
            Ui.Text("Manual é a opção predefinida e mantém o botão ↻. Em segundo plano, o sistema executa a atualização quando houver oportunidade; no Android, a tarefa fica agendada mesmo depois de sair da aplicação.", 12, true),
            Toggle(FaIcons.Broom, "Limpar Cache Automaticamente", "autoClearCache", false, true,
                enabled => { if (enabled) CacheService.Clear(); }),
            Ui.Text("Ao ativar, o cache do guia e das imagens é limpo imediatamente e em cada arranque. Desativar preserva o cache existente.", 12, true),
            ActionRow(FaIcons.RotateRight, "Limpar cache agora", () => { CacheService.Clear(); return DisplayAlertAsync("Cache", "Cache do guia e imagens limpo. As listas e os favoritos foram preservados.", "OK"); }),
            ActionRow(FaIcons.ClockRotateLeft, "Histórico de Visualização", () => OpenAccountPageAsync(() => new HistoryPage()), () => HistoryService.Count.ToString() + " · " + LanguageService.Text("conteúdos")),
            Choice(FaIcons.Clock, "Formato de Hora", "clockFormat", "24", [("24 horas", "24"), ("12 horas", "12")], value => Preferences.Default.Set("clock12", value == "12")),
            ActionRow(FaIcons.ArrowDownWideShort, "Ordenar Por", SortAsync),
            ActionRow(FaIcons.UserSecret, "Agente do Utilizador", UserAgentAsync, () => AppOptions.UserAgent),
            ActionRow(FaIcons.Language, "Idioma", LanguageAsync, () => LanguageService.DisplayName));

    private View BuildPlayerSettings(bool android) =>
        Ui.Stack(
            Choice(FaIcons.Microchip, "Descodificador do Player", "decoder", "auto", [("Automático", "auto"), ("Hardware", "hardware"), ("Software", "software")], enabled: android),
            Number(FaIcons.Database, "Limite de Tamanho de Buffer", "bufferSeconds", 5, 1, 60, "segundos de cache de rede", android),
            Toggle(FaIcons.ClockRotateLeft, "Timeshift nos canais em direto", "timeshiftEnabled", true, android),
            Choice(FaIcons.Clock, "Janela máxima de Timeshift", "timeshiftMinutes", "30",
                [("5 minutos", "5"), ("15 minutos", "15"), ("30 minutos", "30"),
                 ("60 minutos", "60"), ("120 minutos", "120")], enabled: android),
            Toggle(FaIcons.GaugeHigh, "Mostrar Velocidade de Rede", "networkSpeed", false, android),
            Choice(FaIcons.VolumeHigh, "Idioma de áudio preferido", "preferredAudioLanguage", "system", TrackLanguages(), enabled: android),
            Choice(FaIcons.ClosedCaptioning, "Modo das legendas", "subtitleMode", "foreign",
                [("Desligadas", "off"), ("Quando o áudio for diferente", "foreign"), ("Sempre que disponíveis", "always")],
                value => Preferences.Default.Set("subtitles", value != "off"), android),
            Choice(FaIcons.Language, "Idioma de legendas preferido", "preferredSubtitleLanguage", "system", TrackLanguages(), enabled: android),
            Toggle(FaIcons.ClockRotateLeft, "Memorizar faixas escolhidas", "rememberTrackSelection", true, android),
            Number(FaIcons.VolumeHigh, "Atraso do áudio", "audioDelayMs", 0, -5000, 5000, "ms", android),
            Number(FaIcons.ClosedCaptioning, "Atraso das legendas", "subtitleDelayMs", 0, -5000, 5000, "ms", android),
            Toggle(FaIcons.Music, "OpenSL ES (saída de áudio)", "openSl", false, android),
            Toggle(FaIcons.Display, "OpenGL (saída de vídeo)", "openGl", false, android),
            Ui.Text(android
                ? "As opções do leitor aplicam-se ao abrir o próximo vídeo. Hardware depende dos codecs do dispositivo. O Timeshift usa armazenamento temporário e a disponibilidade de pausa ou recuo depende do stream."
                : "Descodificador, buffer, legendas, velocidade, OpenSL ES e OpenGL estão disponíveis no leitor integrado Android.", 12, true));

    private static (string Label, string Value)[] TrackLanguages() =>
        [("Do sistema", "system"), ("Português", "pt"), ("English", "en"), ("Español", "es"),
         ("Français", "fr"), ("Deutsch", "de"), ("Italiano", "it")];

    private View ActionRow(string icon, string title, Func<Task> action, Func<string>? summary = null)
    {
        var detail = Ui.Text(summary?.Invoke() ?? "", 12, true);
        detail.IsVisible = summary is not null;
        if (summary is not null) refreshRows.Add(() => detail.Text = summary());
        var button = Ui.Button(title + "  ›", async () => { await action(); detail.Text = summary?.Invoke() ?? ""; });
        button.HorizontalOptions = LayoutOptions.Fill;
        if (Ui.IsTelevision) { button.MinimumHeightRequest = 40; button.Padding = new Thickness(8, 4); button.FontSize = 14; }
        var grid = new Grid { ColumnSpacing = Ui.IsTelevision ? 8 : 12,
            ColumnDefinitions = [new(new GridLength(Ui.IsTelevision ? 26 : 32)), new(GridLength.Star)],
            Padding = Ui.IsTelevision ? 6 : 10 };
        grid.Add(Ui.FontIcon(icon)); grid.Add(Ui.Stack(button, detail), 1);
        grid.SetDynamicResource(BackgroundColorProperty, "Surface");
        return grid;
    }
    private View Toggle(string icon, string title, string key, bool fallback, bool enabled = true, Action<bool>? changed = null)
    {
        var toggle = new Switch { IsToggled = Preferences.Default.Get(key, fallback), IsEnabled = enabled };
        toggle.SetDynamicResource(Switch.OnColorProperty, "Accent");
        toggle.Toggled += (_, e) => { Preferences.Default.Set(key, e.Value); changed?.Invoke(e.Value); };
        var grid = new Grid { Padding = Ui.IsTelevision ? 10 : 18,
            ColumnSpacing = Ui.IsTelevision ? 8 : 12,
            ColumnDefinitions = [new(new GridLength(Ui.IsTelevision ? 26 : 32)), new(GridLength.Star), new(GridLength.Auto)] };
        grid.Add(Ui.FontIcon(icon)); grid.Add(Ui.Text(LanguageService.Text(title) + (enabled ? "" : " · " + LanguageService.Text("indisponível neste dispositivo")), 16), 1); grid.Add(toggle, 2);
        grid.SetDynamicResource(BackgroundColorProperty, "Surface");
        return grid;
    }
    private View Choice(string icon, string title, string key, string fallback, (string Label, string Value)[] choices, Action<string>? changed = null, bool enabled = true)
    {
        string Summary() => LanguageService.Text(choices.FirstOrDefault(c => c.Value == Preferences.Default.Get(key, fallback)).Label ?? fallback);
        var row = ActionRow(icon, title, async () =>
        {
            var labels = choices.Select(c => LanguageService.Text(c.Label)).ToArray();
            var selected = await DisplayActionSheetAsync(LanguageService.Text(title), LanguageService.Text("Cancelar"), null, labels);
            var index = Array.IndexOf(labels, selected);
            if (index < 0) return;
            Preferences.Default.Set(key, choices[index].Value); changed?.Invoke(choices[index].Value);
        }, Summary);
        row.IsEnabled = enabled; row.Opacity = enabled ? 1 : .5; return row;
    }
    private View Number(string icon, string title, string key, int fallback, int min, int max, string unit, bool enabled = true)
    {
        var row = ActionRow(icon, title, async () =>
        {
            var text = await DisplayPromptAsync(title, $"{min}–{max} {unit}", "Guardar", "Cancelar",
                keyboard: min < 0 ? Keyboard.Default : Keyboard.Numeric,
                initialValue: Preferences.Default.Get(key, fallback).ToString());
            if (text is null) return;
            if (!int.TryParse(text, out var n) || n < min || n > max) throw new InvalidOperationException($"Introduza um valor entre {min} e {max}.");
            Preferences.Default.Set(key, n);
        }, () => $"{Preferences.Default.Get(key, fallback)} {unit}");
        row.IsEnabled = enabled; row.Opacity = enabled ? 1 : .5; return row;
    }
    private async Task OpenAccountPageAsync(Func<Page> create)
    { if (AppServices.ActiveAccount is null) throw new InvalidOperationException("Abra uma lista primeiro."); await Navigation.PushAsync(create()); }
    private async Task SortAsync()
    {
        if (AppServices.ActiveAccount is null) throw new InvalidOperationException("Abra uma lista primeiro.");
        var kind = await CategoryEditor.ChooseKindAsync(this); if (kind is null) return;
        var labels = new[] { "Ordem do fornecedor", "Nome A–Z", "Nome Z–A" };
        var selected = await DisplayActionSheetAsync("Ordenar por", "Cancelar", null, labels);
        var i = Array.IndexOf(labels, selected); if (i < 0) return;
        CatalogOptionsService.Current.Sorting[kind.Value] = (CatalogSort)i; await CatalogOptionsService.SaveAsync();
    }
    private async Task UserAgentAsync()
    {
        var value = await DisplayPromptAsync("Agente do Utilizador", "Cabeçalho enviado ao fornecedor e ao leitor integrado Android.", "Guardar", "Cancelar", initialValue: AppOptions.UserAgent, maxLength: 256);
        if (value is null) return;
        value = value.Trim();
        if (value.Length == 0 || value.Any(char.IsControl)) throw new InvalidOperationException("Introduza um agente válido sem quebras de linha.");
        Preferences.Default.Set("userAgent", value); AppServices.Client.UserAgent = value;
    }
    private async Task LanguageAsync()
    {
        var selected = await DisplayActionSheetAsync("Idioma / Language", "Cancelar", null, "Do sistema", "Português", "English");
        if (selected is not ("Do sistema" or "Português" or "English")) return;
        LanguageService.Apply(selected == "English" ? "en" : selected == "Português" ? "pt" : "system");
        await DisplayAlertAsync("Idioma / Language", "A interface será atualizada ao reabrir a aplicação. / The interface will update when you reopen the app.", "OK");
    }
    private async Task EpgAsync()
    {
        var account = AppServices.ActiveAccount ?? throw new InvalidOperationException("Abra uma lista primeiro.");
        var version = AppServices.SessionVersion;
        var url = await DisplayPromptAsync("Fonte EPG", "Endereço XMLTV. Deixe vazio para usar a fonte do fornecedor.", "Guardar", "Cancelar", keyboard: Keyboard.Url, initialValue: account.EpgUrl);
        if (url is null || version != AppServices.SessionVersion) return;
        url = url.Trim(); if (url.Length > 0) WebAddress.Require(url);
        var updated = account with { EpgUrl = url };
        await AppServices.Accounts.SaveAsync(updated);
        if (version == AppServices.SessionVersion) AppServices.UpdateAccount(updated);
        await EpgService.DeleteAsync(account.Id);
    }
    private async Task<bool> AuthorizeParentAsync()
    {
        var account = AppServices.ActiveAccount ?? throw new InvalidOperationException("Abra uma lista primeiro.");
        var hash = CatalogOptionsService.Current.ParentalPinHash;
        if (hash.Length == 0) { await ChangeParentalPinAsync(); return CatalogOptionsService.Current.ParentalPinHash.Length > 0; }
        var version = AppServices.SessionVersion;
        return await PinPage.AuthorizeAsync(this, account with { Id = account.Id + ".parental", Name = "Controlo parental", PinHash = hash }, false) && version == AppServices.SessionVersion;
    }
    private async Task ChangeParentalPinAsync()
    {
        if (AppServices.ActiveAccount is null) throw new InvalidOperationException("Abra uma lista primeiro.");
        if (CatalogOptionsService.Current.ParentalPinHash.Length > 0 && !await AuthorizeParentAsync()) return;
        var version = AppServices.SessionVersion;
        var hash = await NewPinPage.RequestAsync(this);
        if (hash is null || version != AppServices.SessionVersion) return;
        CatalogOptionsService.Current.ParentalPinHash = hash; await CatalogOptionsService.SaveAsync();
    }
    private async Task ToggleParentalAsync()
    {
        if (!await AuthorizeParentAsync()) return;
        CatalogOptionsService.Current.ParentalEnabled = !CatalogOptionsService.Current.ParentalEnabled;
        await CatalogOptionsService.SaveAsync();
        if (CatalogOptionsService.Current.ParentalEnabled) await Navigation.PushAsync(new CategoriesPage(true));
    }
}

public sealed class NewPinPage : LocalizedPage
{
    private readonly TaskCompletionSource<string?> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool saved;
    private NewPinPage()
    {
        Ui.Page(this, "Novo PIN parental");
        var pin = Ui.Entry("PIN de 4 a 8 algarismos", true); pin.Keyboard = Keyboard.Numeric; pin.MaxLength = 8;
        var confirm = Ui.Entry("Repetir PIN", true); confirm.Keyboard = Keyboard.Numeric; confirm.MaxLength = 8;
        var body = Ui.Stack(Ui.Text("Novo PIN parental", 26), pin, confirm, Ui.Button("Guardar", async () =>
        {
            if (pin.Text != confirm.Text || !PinProtection.IsValid(pin.Text ?? "")) throw new InvalidOperationException("Os PINs devem coincidir e ter 4 a 8 algarismos.");
            var hash = await Task.Run(() => PinProtection.Hash(pin.Text!)); saved = true;
            await Navigation.PopModalAsync(); completion.TrySetResult(hash);
        }, true), Ui.Button("Cancelar", () => Navigation.PopModalAsync()));
        body.Padding = 24; Content = new ScrollView { Content = body };
    }
    protected override void OnDisappearing() { base.OnDisappearing(); if (!saved) completion.TrySetResult(null); }
    public static async Task<string?> RequestAsync(Page owner) { var page = new NewPinPage(); await owner.Navigation.PushModalAsync(page); return await page.completion.Task; }
}

public sealed class InformationPage : LocalizedPage
{
    public InformationPage(bool terms)
    {
        var title = terms ? "Termos de Uso" : "Política de Privacidade"; Ui.Page(this, title);
        var text = terms
            ? LanguageService.Format("PSiptv é um leitor de listas fornecidas pelo utilizador. Não inclui nem vende canais, filmes ou séries.\n\nUtilize apenas conteúdos a que tenha acesso autorizado e respeite as condições do seu fornecedor. A disponibilidade, qualidade e permissões de transmissão dependem desse fornecedor.\n\nAo abrir um leitor externo, o endereço da transmissão é enviado à aplicação escolhida. A partilha de ecrã pode mostrar informação visível no dispositivo.\n\nVersão instalada: {0}", AppInfo.VersionString)
            : LanguageService.Text("As listas, credenciais, preferências por lista e histórico são guardados no armazenamento seguro local do dispositivo. As preferências gerais são guardadas localmente. O guia EPG é guardado localmente em cache para permitir uma abertura rápida e atualizações em segundo plano.\n\nA aplicação contacta os endereços do fornecedor, do guia XMLTV e das imagens da lista para apresentar e reproduzir os conteúdos. Esses serviços recebem o endereço IP e o agente do utilizador configurado.\n\nA biometria é verificada pelo sistema operativo. A aplicação não recebe nem guarda impressões digitais ou imagens faciais. O PIN é guardado como hash com salt.\n\nPode limpar o histórico e eliminar listas nas configurações. Eliminar uma lista remove também o respetivo histórico, favoritos, personalizações e cache EPG. A aplicação não inclui um serviço próprio de recolha de telemetria.");
        var body = Ui.Stack(Ui.Text(title, 26), Ui.Text(text, 16)); body.Padding = 24; body.MaximumWidthRequest = 800; Content = new ScrollView { Content = body };
    }
}
