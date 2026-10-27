using PSiptv.Core;
using PSiptv.Services;

namespace PSiptv.Views;

public sealed class WearInstallPage : LocalizedPage
{
    private readonly Entry host = Ui.Entry("IP do relógio · ex.: 192.168.1.50");
    private readonly Entry connectionPort = Ui.Entry("Porta de ligação");
    private readonly Entry pairingPort = Ui.Entry("Porta de emparelhamento");
    private readonly Entry pairingCode = Ui.Entry("Código de seis algarismos", true);
    private readonly Label status = Ui.Text("Configure a ligação e verifique a versão mais recente.", 14, true);
    private readonly Label details = Ui.Text("", 14);
    private readonly ProgressBar progress = new() { IsVisible = false };
    private readonly Label progressText = Ui.Text("", 12, true);
    private readonly Button install;
    private readonly List<Button> actions = [];
    private AndroidAppRelease? release;
    private CancellationTokenSource? operation;
    private bool busy;
    private bool visible;

    public WearInstallPage()
    {
        Ui.Page(this, "Instalar / atualizar relógio");
        host.AutomationId = "WearHost";
        connectionPort.AutomationId = "WearConnectionPort";
        pairingPort.AutomationId = "WearPairingPort";
        pairingCode.AutomationId = "WearPairingCode";
        host.Text = Preferences.Default.Get("wear-installer.host", "");
        connectionPort.Text = Preferences.Default.Get("wear-installer.port", "");
        pairingPort.Keyboard = connectionPort.Keyboard = pairingCode.Keyboard = Keyboard.Numeric;
        pairingCode.MaxLength = 6;
        progress.SetDynamicResource(ProgressBar.ProgressColorProperty, "Accent");
        install = Action("Descarregar e instalar no relógio", InstallAsync, true);
        install.AutomationId = "WearInstall";
        var cancel = Ui.Button("Cancelar operação", () => { operation?.Cancel(); return Task.CompletedTask; });
        var body = Ui.Stack(
            Ui.Card(Ui.Stack(Ui.Text("PSiptv Remote no Watch 5", 20),
                Ui.Text("O telemóvel descarrega o APK Wear OS da última release, valida-o e instala-o diretamente no relógio. Serve para a primeira instalação e para atualizações.", 14),
                Ui.Text("Ligue ambos ao mesmo Wi-Fi. No relógio, abra Definições → Opções de programador e ative Depuração ADB e Depuração sem fios. Autorize a rede quando solicitado.", 13, true),
                Ui.Text("Para mostrar as opções de programador no Watch 5: Sobre o relógio → Informações de software → toque repetidamente em Versão do software até aparecer a confirmação.", 13, true))),
            Ui.Card(Ui.Stack(Ui.Text("Ligação ao relógio", 20), host, connectionPort,
                Ui.Text("Use a porta do ecrã principal de Depuração sem fios. A porta pode mudar quando o relógio volta a ligar ao Wi-Fi.", 13, true),
                Action("Verificar ligação e versão instalada", InspectAsync))),
            Ui.Card(Ui.Stack(Ui.Text("Emparelhar na primeira utilização", 20),
                Ui.Text("No relógio, escolha Emparelhar novo dispositivo. Introduza abaixo a porta e o código desse ecrã. A porta de emparelhamento é diferente da porta de ligação acima.", 13, true),
                pairingPort, pairingCode, Action("Emparelhar telemóvel e relógio", PairAsync))),
            Ui.Card(Ui.Stack(Ui.Text("Versão mais recente", 20),
                Ui.Text("Usa o token GitHub já configurado nas atualizações da aplicação.", 13, true),
                Action("Configurar acesso ao GitHub", () => Navigation.PushAsync(new AndroidAppUpdatePage())),
                Action("Verificar última versão do relógio", CheckAsync), details, progress, progressText, install, cancel, status,
                Ui.Text("Mantenha este painel aberto durante o envio. Ao terminar, pode desligar a depuração sem fios no relógio. O comando remoto continua a funcionar por Bluetooth ou Wi-Fi.", 13, true))));
        body.Padding = 24;
        body.MaximumWidthRequest = 800;
        Content = new ScrollView { Content = body };
        RefreshActions();
    }

    protected override void OnAppearing() { base.OnAppearing(); visible = true; }
    protected override void OnDisappearing()
    {
        visible = false;
        operation?.Cancel();
        pairingCode.Text = "";
        base.OnDisappearing();
    }

    private Button Action(string title, Func<Task> action, bool primary = false)
    {
        var button = Ui.Button(title, () => RunAsync(action), primary);
        actions.Add(button);
        return button;
    }

    private async Task RunAsync(Func<Task> action)
    {
        if (busy) return;
        busy = true;
        operation = new CancellationTokenSource(TimeSpan.FromMinutes(15));
        RefreshActions();
        try { await action(); }
        catch (OperationCanceledException)
        { if (visible) status.Text = "Operação cancelada. Verifique a versão no relógio antes de tentar novamente."; }
        catch (Exception ex) { if (visible) status.Text = ex.Message; }
        finally
        {
            operation.Dispose();
            operation = null;
            busy = false;
            progress.IsVisible = false;
            RefreshActions();
        }
    }

    private CancellationToken Token => operation?.Token ?? CancellationToken.None;
    private (string Host, int Port) Connection() => (WearInstallPolicy.Host(host.Text ?? ""), WearInstallPolicy.Port(connectionPort.Text ?? ""));
    private void SaveConnection(string address, int port)
    {
        Preferences.Default.Set("wear-installer.host", address);
        Preferences.Default.Set("wear-installer.port", port.ToString());
    }

    private async Task PairAsync()
    {
        var address = WearInstallPolicy.Host(host.Text ?? "");
        var port = WearInstallPolicy.Port(pairingPort.Text ?? "");
        var code = WearInstallPolicy.PairingCode(pairingCode.Text ?? "");
        pairingCode.Text = "";
        status.Text = "A emparelhar com o relógio…";
        if (!await WearInstallService.PairAsync(address, port, code, Token)) throw new IOException("O relógio não confirmou o emparelhamento.");
        Preferences.Default.Set("wear-installer.host", address);
        status.Text = "Emparelhamento concluído. Volte ao ecrã principal de Depuração sem fios no relógio e confirme a porta de ligação.";
    }

    private async Task InspectAsync()
    {
        var target = Connection();
        status.Text = "A verificar o relógio…";
        var installed = await WearInstallService.InspectAsync(target.Host, target.Port, Token);
        SaveConnection(target.Host, target.Port);
        status.Text = installed.VersionCode == 0 ? "Relógio ligado. O PSiptv Remote ainda não está instalado." : $"Relógio ligado · PSiptv Remote {installed.VersionName} · build {installed.VersionCode}.";
    }

    private async Task CheckAsync()
    {
        status.Text = "A procurar o APK Wear OS na última release…";
        release = null;
        release = await AndroidAppUpdateService.CheckLatestWearAsync(Token) ?? throw new IOException("A release não contém uma versão do relógio.");
        details.Text = $"PSiptv Remote {release.Tag}\n{release.AssetSize / 1024d / 1024d:F1} MB · {release.AssetName}";
        status.Text = "Versão disponível para instalar no relógio.";
    }

    private async Task InstallAsync()
    {
        var token = Token;
        var target = Connection();
        await CheckAsync();
        var available = release!;
        status.Text = "A confirmar a ligação ao relógio…";
        var installed = await WearInstallService.InspectAsync(target.Host, target.Port, Token);
        SaveConnection(target.Host, target.Port);
        progress.IsVisible = true;
        progress.Progress = 0;
        status.Text = "A descarregar e validar o APK do relógio…";
        var download = new Progress<AppUpdateDownloadProgress>(value =>
        {
            if (!visible || token.IsCancellationRequested) return;
            progress.Progress = value.Fraction;
            progressText.Text = $"Download: {value.BytesReceived / 1024d / 1024d:F1} / {value.TotalBytes / 1024d / 1024d:F1} MB";
        });
        var path = await AndroidAppUpdateService.DownloadWearAsync(available, download, Token);
        var apk = AndroidAppUpdateService.ValidateWearApk(path);
        if (installed.VersionCode >= apk.Code)
        { status.Text = $"O relógio já tem a versão {installed.VersionName} ou uma versão mais recente."; return; }
        status.Text = "A enviar e instalar no relógio…";
        progress.Progress = 0;
        var sent = new Progress<long>(bytes =>
        {
            if (!visible || token.IsCancellationRequested) return;
            progress.Progress = Math.Clamp((double)bytes / available.AssetSize, 0, 1);
            progressText.Text = bytes < available.AssetSize
                ? $"Envio: {bytes / 1024d / 1024d:F1} / {available.AssetSize / 1024d / 1024d:F1} MB"
                : "APK enviado. A aguardar confirmação da instalação…";
        });
        var result = await WearInstallService.InstallAsync(target.Host, target.Port, path, sent, Token);
        status.Text = $"Instalação confirmada no relógio · PSiptv Remote {result.VersionName}. Pode desligar a depuração sem fios.";
        progressText.Text = "Concluído.";
    }

    private void RefreshActions()
    {
        foreach (var action in actions) action.IsEnabled = !busy;
        host.IsEnabled = connectionPort.IsEnabled = pairingPort.IsEnabled = pairingCode.IsEnabled = !busy;
    }
}
