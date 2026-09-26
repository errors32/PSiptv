using System.Globalization;
using PSiptv.Services;

namespace PSiptv.Views;

public sealed class AndroidAppUpdatePage : LocalizedPage
{
    private readonly Label tokenState = Ui.Text("", 12, true);
    private readonly Label status = Ui.Text("Pronto para verificar.", 14, true);
    private readonly Label releaseDetails = Ui.Text("", 14);
    private readonly ProgressBar progress = new() { IsVisible = false };
    private readonly Label progressText = Ui.Text("", 12, true);
    private readonly Entry token = Ui.Entry("Token GitHub de leitura", true);
    private readonly Button downloadButton;
    private AndroidAppRelease? availableRelease;

    public AndroidAppUpdatePage(AndroidAppRelease? release = null)
    {
        Ui.Page(this, "Atualizações da aplicação");
        progress.SetDynamicResource(ProgressBar.ProgressColorProperty, "Accent");
        token.AutomationId = "GitHubReleaseToken";
        var automatic = new Switch { IsToggled = AndroidAppUpdateService.AutomaticChecksEnabled };
        automatic.SetDynamicResource(Switch.OnColorProperty, "Accent");
        automatic.Toggled += (_, args) => AndroidAppUpdateService.AutomaticChecksEnabled = args.Value;

        downloadButton = Ui.Button("Descarregar e instalar", DownloadAndInstallAsync, true);
        downloadButton.IsEnabled = false;
        releaseDetails.IsVisible = false;
        progressText.IsVisible = false;

        var tokenActions = new Grid
        {
            ColumnSpacing = 10,
            ColumnDefinitions = [new(GridLength.Star), new(GridLength.Star)]
        };
        tokenActions.Add(Ui.Button("Guardar token", SaveTokenAsync, true));
        tokenActions.Add(Ui.Button("Remover token", RemoveTokenAsync), 1);

        var automaticRow = new Grid
        {
            ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)],
            ColumnSpacing = 12
        };
        automaticRow.Add(Ui.Text("Verificar automaticamente uma vez por dia", 15));
        automaticRow.Add(automatic, 1);

        var body = Ui.Stack(
            Ui.Card(Ui.Stack(
                Ui.Text("Origem das atualizações", 20),
                Ui.Text(AndroidAppUpdateService.Repository, 16),
                Ui.Text($"Versão instalada: {AppInfo.Current.VersionString} · build {AppInfo.Current.BuildString}", 13, true),
                automaticRow)),
            Ui.Card(Ui.Stack(
                Ui.Text("Acesso ao repositório privado", 20),
                Ui.Text("O token fica no armazenamento seguro deste dispositivo e é incluído em cópias de segurança.", 13, true),
                token,
                tokenActions,
                tokenState)),
            Ui.Card(Ui.Stack(
                Ui.Text("GitHub Release", 20),
                status,
                releaseDetails,
                progress,
                progressText,
                Ui.Button("Verificar agora", CheckAsync),
                downloadButton)));
        body.Padding = Ui.IsTelevision ? 14 : 24;
        body.MaximumWidthRequest = Ui.IsTelevision ? 1100 : 800;
        Content = new ScrollView { Content = body };
        if (release is not null) ShowRelease(release);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        AppUpdateBackgroundDownload.Changed += OnDownloadChanged;
        if (availableRelease is null && AppUpdateBackgroundDownload.ActiveRelease is { } active)
            ShowRelease(active);
        RefreshDownloadState();
        try { await RefreshTokenStateAsync(); }
        catch (Exception ex) { await Ui.ErrorAsync(this, ex); }
    }

    protected override void OnDisappearing()
    {
        AppUpdateBackgroundDownload.Changed -= OnDownloadChanged;
        base.OnDisappearing();
    }

    private async Task SaveTokenAsync()
    {
        await AndroidAppUpdateService.SaveTokenAsync(token.Text ?? "");
        token.Text = "";
        await RefreshTokenStateAsync();
        await CheckAsync();
    }

    private async Task RemoveTokenAsync()
    {
        AndroidAppUpdateService.RemoveToken();
        availableRelease = null;
        downloadButton.IsEnabled = false;
        releaseDetails.IsVisible = false;
        status.Text = "Token removido.";
        await RefreshTokenStateAsync();
    }

    private async Task RefreshTokenStateAsync()
    {
        tokenState.Text = await AndroidAppUpdateService.HasTokenAsync()
            ? "Token configurado no armazenamento seguro."
            : "Ainda não existe um token configurado.";
    }

    private async Task CheckAsync()
    {
        status.Text = "A verificar a última GitHub Release…";
        availableRelease = null;
        downloadButton.IsEnabled = false;
        releaseDetails.IsVisible = false;
        var release = await AndroidAppUpdateService.CheckLatestAsync();
        if (release is null)
        {
            status.Text = $"A versão {AppInfo.Current.VersionString} já está atualizada.";
            return;
        }
        ShowRelease(release);
    }

    private void ShowRelease(AndroidAppRelease release)
    {
        availableRelease = release;
        status.Text = $"Nova versão disponível: {release.Tag}";
        var published = release.PublishedAt is { } date
            ? " · " + date.ToLocalTime().ToString("d", CultureInfo.CurrentCulture) : "";
        var notes = string.IsNullOrWhiteSpace(release.Notes) ? "Sem notas de lançamento."
            : release.Notes.Length > 1600 ? release.Notes[..1600] + "…" : release.Notes;
        releaseDetails.Text = $"{release.Title}{published}\n{FormatBytes(release.AssetSize)} · {release.AssetName}\n\n{notes}";
        releaseDetails.IsVisible = true;
        RefreshDownloadState();
    }

    private async Task DownloadAndInstallAsync()
    {
        var ready = AppUpdateBackgroundDownload.ReadyPath;
        if (ready is not null && (availableRelease is null ||
            AppUpdateBackgroundDownload.State(availableRelease.Tag).ReadyPath is not null))
        {
            status.Text = "APK validado. A abrir o instalador Android…";
            var result = await AndroidAppUpdateService.RequestInstallAsync(ready);
            if (result == InstallRequestResult.PermissionRequired)
                status.Text = "Autorize a instalação desta origem. Ao regressar, o instalador será aberto automaticamente.";
            return;
        }
        if (availableRelease is not { } release) return;
        await PSiptv.AndroidNotificationPermission.RequestAsync();
        AppUpdateBackgroundDownload.Start(release);
        RefreshDownloadState();
    }

    private void OnDownloadChanged() => MainThread.BeginInvokeOnMainThread(RefreshDownloadState);

    private void RefreshDownloadState()
    {
        if (availableRelease is null && AppUpdateBackgroundDownload.ActiveRelease is { } active)
        {
            ShowRelease(active);
            return;
        }
        if (availableRelease is not { } release)
        {
            if (AppUpdateBackgroundDownload.ReadyPath is null) return;
            status.Text = "Atualização descarregada e validada. Pronta para instalar.";
            downloadButton.Text = "Instalar atualização";
            downloadButton.IsEnabled = true;
            return;
        }
        var state = AppUpdateBackgroundDownload.State(release.Tag);
        if (state.Active)
        {
            progress.IsVisible = true;
            progressText.IsVisible = true;
            progress.Progress = state.Progress.Fraction;
            progressText.Text = $"{FormatBytes(state.Progress.BytesReceived)} / {FormatBytes(state.Progress.TotalBytes)} · {state.Progress.Fraction:P0}";
            status.Text = $"A descarregar {release.AssetName} em segundo plano…";
            downloadButton.IsEnabled = false;
        }
        else if (state.ReadyPath is not null)
        {
            progress.IsVisible = true;
            progress.Progress = 1;
            progressText.IsVisible = false;
            status.Text = "Atualização descarregada e validada. Pronta para instalar.";
            downloadButton.Text = "Instalar atualização";
            downloadButton.IsEnabled = true;
        }
        else
        {
            progress.IsVisible = false;
            progressText.IsVisible = false;
            if (state.Error is not null) status.Text = "Falha no download: " + state.Error;
            downloadButton.Text = "Descarregar e instalar";
            downloadButton.IsEnabled = true;
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        var value = (double)Math.Max(0, bytes);
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return $"{value:0.#} {units[unit]}";
    }
}
