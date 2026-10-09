using PSiptv.Services;

namespace PSiptv.Views;

public sealed class VpnProfilePage : LocalizedPage
{
    public VpnProfilePage(VpnConnectionProfile? existing = null)
    {
        Ui.Page(this, existing is null ? "Adicionar perfil VPN" : "Editar perfil VPN");
        var providers = VpnService.Providers.Where(item => item.Id != "openvpn").ToArray();
        var provider = new Picker
        {
            Title = LanguageService.Text("Serviço VPN"),
            ItemsSource = providers.Select(item => item.Name).ToArray(),
            SelectedIndex = Math.Max(0, providers.ToList().FindIndex(item => item.Id ==
                (existing?.ProviderId ?? VpnService.DefaultProvider?.Id ?? "proton")))
        };
        provider.SetDynamicResource(Picker.TextColorProperty, "Ink");
        var method = new Picker
        {
            Title = LanguageService.Text("Método de ligação"),
            ItemsSource = new[] { "WireGuard", "IKEv2/IPsec" },
            SelectedIndex = existing?.Method == "ikev2" ? 1 : 0
        };
        method.SetDynamicResource(Picker.TextColorProperty, "Ink");

        var name = Ui.Entry("Nome do perfil / servidor");
        name.Text = existing?.Name ?? "";
        var server = Ui.Entry("Servidor IKEv2 (nome DNS ou IP)");
        server.Text = existing?.Server ?? "";
        var identity = Ui.Entry("Identidade local IKEv2");
        identity.Text = existing?.Identity ?? "";
        var remoteIdentity = Ui.Entry("Identidade remota (opcional, Android 13+)");
        remoteIdentity.Text = existing?.RemoteIdentity ?? "";
        var username = Ui.Entry("Utilizador VPN de configuração manual");
        username.Text = existing?.Username ?? "";
        var password = Ui.Entry("Password VPN de configuração manual", true);
        password.Text = existing?.Password ?? "";
        var tunnel = Ui.Entry("Nome do túnel importado no WireGuard");
        // Older profiles could contain an entire .conf here, including a private key.
        tunnel.Text = VpnService.IsValidWireGuardTunnelName(existing?.TunnelName) ? existing!.TunnelName : "";

        var wireGuardFields = Ui.Stack(
            Ui.Text("Importe o ficheiro .conf na aplicação WireGuard. Depois escreva aqui apenas o nome do túnel que aparece nessa aplicação (até 15 caracteres), nunca o conteúdo do ficheiro. Ative nas definições do WireGuard o controlo por aplicações externas.", 12, true),
            Ui.Text("Nome do túnel"), tunnel);
        var ikev2Fields = Ui.Stack(
            Ui.Text("O Android 11 ou superior é necessário para IKEv2. A identidade local costuma ser o utilizador VPN; o serviço pode fornecer um valor diferente.", 12, true),
            Ui.Text("Servidor"), server,
            Ui.Text("Identidade local IKEv2"), identity,
            Ui.Text("Identidade remota do servidor"), remoteIdentity,
            Ui.Text("Utilizador VPN"), username,
            Ui.Text("Password VPN"), password);
        var serviceHint = Ui.Text("", 12, true);
        void RefreshServiceHint() => serviceHint.Text = providers.ElementAtOrDefault(provider.SelectedIndex)?.Id switch
        {
            "proton" => LanguageService.Text("Proton VPN: use as credenciais OpenVPN/IKEv2 geradas na sua conta para IKEv2; para WireGuard, importe o ficheiro .conf descarregado da Proton."),
            "pure" => LanguageService.Text("PureVPN: use as credenciais VPN manuais. Para IKEv2, indique a identidade remota indicada pela PureVPN (por exemplo, pointtoserver.com) num Android 13 ou superior."),
            _ => ""
        };
        provider.SelectedIndexChanged += (_, _) => RefreshServiceHint();
        RefreshServiceHint();
        void RefreshMethod()
        {
            wireGuardFields.IsVisible = method.SelectedIndex == 0;
            ikev2Fields.IsVisible = method.SelectedIndex == 1;
        }
        method.SelectedIndexChanged += (_, _) => RefreshMethod();
        RefreshMethod();

        var save = Ui.Button("Guardar perfil", async () =>
        {
            try
            {
                var selected = providers.ElementAtOrDefault(provider.SelectedIndex);
                if (selected is null) throw new ArgumentException("Escolha um serviço VPN.");
                var profile = new VpnConnectionProfile(existing?.Id ?? Guid.NewGuid().ToString("N"),
                    selected.Id, name.Text ?? "", method.SelectedIndex == 1 ? "ikev2" : "wireguard",
                    server.Text ?? "", identity.Text ?? "", username.Text ?? "", password.Text ?? "",
                    tunnel.Text ?? "", remoteIdentity.Text ?? "");
                await VpnService.SaveProfileAsync(profile);
                if (VpnService.DefaultProvider is null) VpnService.SetDefaultProvider(selected);
                await Navigation.PopAsync();
            }
            catch (Exception ex) { await Ui.ErrorAsync(this, ex); }
        }, true);

        var stack = Ui.Stack(
            Ui.Text("Ligação VPN automática", 20),
            Ui.Text("Escolha WireGuard para um túnel já importado na aplicação WireGuard, ou IKEv2/IPsec para ligar com as credenciais VPN manuais do serviço.", 12, true),
            Ui.Text("Serviço VPN"), provider, serviceHint,
            Ui.Text("Nome do perfil"), name,
            Ui.Text("Método de ligação"), method,
            wireGuardFields, ikev2Fields,
            Ui.Text("As credenciais e os dados do perfil são guardados no armazenamento seguro do dispositivo. O Android ou WireGuard pedirão autorização antes da primeira ligação.", 12, true),
            save);
        stack.Padding = Ui.IsTelevision ? 14 : 24;
        stack.MaximumWidthRequest = 800;
        Content = new ScrollView { Content = stack };
    }
}
