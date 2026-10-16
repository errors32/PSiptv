using PSiptv.Core;
using PSiptv.Services;

namespace PSiptv.Views;

public sealed class WindowsAppUpdatePage : LocalizedPage
{
    private readonly Label status = Ui.Text("Pronto para verificar.", 14, true);
    private readonly Label notes = Ui.Text("", 14);
    private readonly Entry token = Ui.Entry("Token GitHub de leitura (repositório privado)", true);
    private readonly ProgressBar progress = new();
    private readonly Button check;
    private readonly Button install;
    private readonly Button save;
    private readonly Button remove;
    private readonly Button cancel;
    private WindowsRelease? release;
    private CancellationTokenSource? operation;

    public WindowsAppUpdatePage(WindowsRelease? available = null)
    {
        Ui.Page(this, "Atualizações da aplicação");
        release = available;
        var automatic = new Switch { IsToggled = WindowsAppUpdateService.AutomaticChecksEnabled };
        automatic.Toggled += (_, e) => WindowsAppUpdateService.AutomaticChecksEnabled = e.Value;
        check = Ui.Button("Verificar agora", CheckAsync);
        install = Ui.Button("Descarregar e instalar", InstallAsync, true);
        save = Ui.Button("Guardar token", async () =>
        {
            await GitHubUpdateTokenStore.SaveAsync(token.Text ?? "");
            token.Text = "";
            await CheckAsync();
        });
        remove = Ui.Button("Remover token", () =>
        {
            GitHubUpdateTokenStore.Remove();
            status.Text = "Token removido.";
            return Task.CompletedTask;
        });
        cancel = Ui.Button("Cancelar transferência", () => { operation?.Cancel(); return Task.CompletedTask; });
        cancel.IsVisible = false;
        install.IsEnabled = release is not null;
        notes.Text = release?.Notes ?? "";
        Content = new ScrollView { Content = Ui.Stack(
            Ui.Card(Ui.Stack(Ui.Text($"Windows · {AppInfo.Current.VersionString} · {WindowsAppUpdateService.Architecture}", 20),
                Ui.Text(WindowsAppUpdateService.Repository, 14),
                Ui.Text("Verificar automaticamente uma vez por dia", 14), automatic)),
            Ui.Card(Ui.Stack(Ui.Text("Acesso ao GitHub", 20),
                Ui.Text("Em repositórios públicos, o token é opcional. Em privados, use um token com permissão Contents: Read. É guardado no armazenamento seguro e incluído nas cópias de segurança.", 13, true),
                token, save, remove)),
            Ui.Card(Ui.Stack(status, notes, progress, check, install, cancel))) };
    }
    private async Task CheckAsync()
    {
        if (operation is not null) return;
        operation = new();
        Busy(true);
        try
        {
            status.Text = "A verificar atualizações Windows…";
            release = await WindowsAppUpdateService.CheckLatestAsync(operation.Token);
            notes.Text = release?.Notes ?? "";
            status.Text = release is null ? "Tem a versão Windows mais recente disponível." : $"Versão {release.Tag} disponível.";
        }
        finally { operation.Dispose(); operation = null; Busy(false); }
    }
    private async Task InstallAsync()
    {
        if (operation is not null || release is null) return;
        if (!await DisplayAlertAsync("Instalar atualização", "A aplicação vai fechar para substituir os ficheiros e voltar a abrir. As suas definições e listas serão preservadas.", "Continuar", "Cancelar")) return;
        operation = new();
        Busy(true);
        try
        {
            status.Text = "A descarregar e validar o pacote…";
            var path = await WindowsAppUpdateService.DownloadAsync(release, new Progress<double>(v => progress.Progress = v), operation.Token);
            status.Text = "A instalar…";
            await WindowsAppUpdateService.InstallAsync(path);
        }
        catch (OperationCanceledException) { status.Text = "Transferência cancelada."; }
        finally { operation.Dispose(); operation = null; Busy(false); }
    }
    private void Busy(bool busy)
    {
        check.IsEnabled = save.IsEnabled = remove.IsEnabled = !busy;
        install.IsEnabled = !busy && release is not null;
        cancel.IsVisible = busy;
    }
    protected override void OnDisappearing() { operation?.Cancel(); base.OnDisappearing(); }
}
