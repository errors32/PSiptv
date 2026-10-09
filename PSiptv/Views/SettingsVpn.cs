using PSiptv.Services;

namespace PSiptv.Views;

public sealed partial class SettingsPage
{
    private readonly VerticalStackLayout vpnProfiles = new() { Spacing = 10 };

    private async Task ReloadVpnProfilesAsync()
    {
        vpnProfiles.Clear();
        if (!VpnService.SupportsDirectConnection) return;
        var profiles = await VpnService.LoadProfilesAsync();
        if (profiles.Count == 0)
        {
            vpnProfiles.Add(Ui.Text("Ainda não existem perfis VPN. Adicione um servidor ou túnel para ligar automaticamente.", 14, true));
            return;
        }
        foreach (var profile in profiles)
        {
            var provider = VpnService.Providers.FirstOrDefault(p => p.Id == profile.ProviderId);
            var connectionStatus = Ui.Text("", 12, true);
            var connect = Ui.Button("Ligar", async () =>
            {
                connectionStatus.Text = LanguageService.Text("A pedir a ligação VPN…");
                try
                {
                    await VpnService.ConnectAsync(profile);
                    Preferences.Default.Remove("vpn.lastStartupError");
                    connectionStatus.Text = profile.Method == "wireguard"
                        ? LanguageService.Text("Nova rede VPN detetada. Confirme o túnel no WireGuard.")
                        : LanguageService.Text("Ligação VPN estabelecida.");
                    await LanguageService.AlertAsync(this, "Ligação VPN",
                        profile.Method == "wireguard"
                            ? $"O pedido para ativar «{profile.TunnelName}» foi enviado ao WireGuard e o Android detetou uma nova rede VPN. Confirme no WireGuard que este túnel está ativo."
                            : $"Ligado a {profile.Name}.");
                    foreach (var refresh in refreshRows) refresh();
                }
                catch (OperationCanceledException) { connectionStatus.Text = LanguageService.Text("Ligação VPN cancelada."); }
                catch (Exception ex)
                {
                    connectionStatus.Text = ex.Message;
                    await Ui.ErrorAsync(this, ex);
                }
            });
            var connectionDetails = profile.Method == "wireguard"
                ? VpnService.IsValidWireGuardTunnelName(profile.TunnelName)
                    ? profile.TunnelName + " (WireGuard)"
                    : LanguageService.Text("Nome de túnel inválido; edite o perfil")
                : profile.Server + " (IKEv2)";
            vpnProfiles.Add(Ui.Card(Ui.Stack(
                Ui.Text(profile.Name, 18),
                Ui.Text($"{provider?.Name ?? profile.ProviderId} · {connectionDetails}", 12, true),
                connect,
                connectionStatus,
                profile.Method == "wireguard"
                    ? Ui.Button("Abrir WireGuard e verificar túnel", async () =>
                    {
                        var wireGuard = VpnService.Providers.First(p => p.Id == "wireguard");
                        if (!VpnService.OpenProvider(wireGuard))
                            await LanguageService.AlertAsync(this, "Ligação VPN", "Instale a aplicação WireGuard e importe nela o ficheiro .conf antes de ligar este perfil.");
                    })
                    : new ContentView { IsVisible = false },
                Ui.Button("Editar", () => Navigation.PushAsync(new VpnProfilePage(profile))),
                Ui.Button("Eliminar", async () =>
                {
                    if (!await DisplayAlertAsync("Eliminar perfil VPN", $"Eliminar «{profile.Name}»?", "Eliminar", "Cancelar")) return;
                    await VpnService.DeleteProfileAsync(profile.Id);
                    await ReloadVpnProfilesAsync();
                }))));
        }
    }

    private View BuildVpnSettings(bool android)
    {
        if (!android)
            return Ui.Card(Ui.Stack(Ui.Text("Ligação VPN", 20),
                Ui.Text("A gestão dos serviços VPN nesta aplicação está disponível no Android. Configure a VPN nas definições do sistema neste dispositivo.", 14, true)));

        var status = Ui.Text("", 14, true);
        void RefreshStatus()
        {
            var provider = VpnService.DefaultProvider;
            status.Text = VpnService.IsVpnActive()
                ? LanguageService.Text("O Android detetou uma rede VPN para esta aplicação, mas não confirma o serviço nem o túnel. Verifique o estado na aplicação VPN.")
                : LanguageService.Text("Não foi detetada uma rede VPN para esta aplicação.");
            if (provider is null) status.Text += "\n" + LanguageService.Text("Escolha um serviço VPN predefinido.");
            var startupError = Preferences.Default.Get("vpn.lastStartupError", "");
            if (startupError.Length > 0) status.Text += "\n" + LanguageService.Text("Última tentativa automática: ") + startupError;
        }
        refreshRows.Add(RefreshStatus);
        RefreshStatus();

        var auto = new Switch { IsToggled = VpnService.OpenOnStartup };
        auto.SetDynamicResource(Switch.OnColorProperty, "Accent");
        auto.Toggled += (_, e) => VpnService.OpenOnStartup = e.Value;
        var connectOnStartup = new Switch
        {
            IsToggled = VpnService.ConnectOnStartup,
            IsEnabled = VpnService.SupportsDirectConnection
        };
        connectOnStartup.SetDynamicResource(Switch.OnColorProperty, "Accent");
        connectOnStartup.Toggled += async (_, e) =>
        {
            try
            {
                if (e.Value && await VpnService.StartupProfileAsync() is null)
                {
                    connectOnStartup.IsToggled = false;
                    await LanguageService.AlertAsync(this, "Ligação VPN", "Escolha um serviço predefinido e adicione um perfil VPN antes de ativar a ligação automática.");
                    return;
                }
                VpnService.ConnectOnStartup = e.Value;
            }
            catch (Exception ex)
            {
                connectOnStartup.IsToggled = false;
                await Ui.ErrorAsync(this, ex);
            }
        };
        var addProfile = Ui.Button("Adicionar perfil VPN", () => Navigation.PushAsync(new VpnProfilePage()));
        addProfile.IsEnabled = VpnService.SupportsDirectConnection;

        return Ui.Stack(
            ActionRow(FaIcons.Shield, "Serviço VPN predefinido", async () =>
            {
                var providers = VpnService.Providers;
                var choices = providers.Select(p => p.Name)
                    .Append(LanguageService.Text("Nenhum"))
                    .ToArray();
                var answer = await DisplayActionSheetAsync(LanguageService.Text("Serviço VPN predefinido"),
                    LanguageService.Text("Cancelar"), null, choices);
                var index = Array.IndexOf(choices, answer);
                if (index < 0) return;
                VpnService.SetDefaultProvider(index == providers.Count ? null : providers[index]);
                RefreshStatus();
            }, () => VpnService.DefaultProvider?.Name ?? LanguageService.Text("Nenhum")),
            Ui.Card(Ui.Stack(
                Ui.Text("Ligação VPN automática", 20),
                Ui.Text("Ligar automaticamente ao abrir a aplicação"),
                connectOnStartup,
                Ui.Text("Ao abrir, tenta ligar o último perfil do serviço predefinido. Para WireGuard, importe primeiro o ficheiro .conf na aplicação WireGuard, indique aqui exatamente o nome do túnel e permita o controlo por aplicações externas nas definições do WireGuard. Uma rede VPN detetada não identifica o túnel ativo.", 12, true),
                addProfile,
                vpnProfiles)),
            Ui.Card(Ui.Stack(
                Ui.Text("Ligação ao arrancar", 20),
                Ui.Text("Abrir o serviço VPN automaticamente ao iniciar a aplicação"),
                auto,
                Ui.Text("Esta opção apenas abre a aplicação do serviço VPN. Quando a ligação automática integrada está ativa, é esta que tem prioridade.", 12, true))),
            Ui.Card(Ui.Stack(
                Ui.Text("Estado", 20), status,
                Ui.Button("Abrir serviço VPN", async () =>
                {
                    if (VpnService.DefaultProvider is not { } provider)
                    {
                        await LanguageService.AlertAsync(this, "Ligação VPN", "Escolha primeiro um serviço VPN predefinido.");
                        return;
                    }
                    if (!VpnService.OpenProvider(provider))
                        await LanguageService.AlertAsync(this, "Ligação VPN", $"{provider.Name} não está instalado neste dispositivo.");
                }),
                Ui.Button("Definições de VPN do Android", async () =>
                {
                    if (!VpnService.OpenAndroidVpnSettings())
                        await LanguageService.AlertAsync(this, "Ligação VPN", "As definições de VPN não estão disponíveis neste dispositivo.");
                }),
                Ui.Text("Proton VPN e PureVPN fornecem configurações WireGuard para importação. IKEv2 usa as credenciais VPN manuais, que podem ser diferentes da password da conta. O Android só permite uma VPN ativa de cada vez.", 12, true))));
    }
}
