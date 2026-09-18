using PSiptv.Services;

namespace PSiptv.Views;

public sealed class BackupPage : LocalizedPage
{
    private readonly ActivityIndicator spinner = new() { IsVisible = false };
    private readonly Label status = Ui.Text("", 13, true);
    private readonly VerticalStackLayout content;
    private bool busy;

    public BackupPage()
    {
        Ui.Page(this, "Sincronização e cópia de segurança");
        spinner.SetDynamicResource(ActivityIndicator.ColorProperty, "Accent");
        var export = Ui.Card(Ui.Stack(
            Ui.Text("Criar cópia", 20),
            Ui.Text("Inclui listas, perfis, favoritos, histórico e configurações. Guarde o ficheiro no serviço cloud ou dispositivo que preferir.", 13, true),
            Ui.Text("O ficheiro não é encriptado. Guarde-o num local privado.", 13, true),
            Ui.Button("Criar e partilhar cópia", ExportAsync, true)));
        var import = Ui.Card(Ui.Stack(
            Ui.Text("Restaurar e sincronizar", 20),
            Ui.Text("Os dados da cópia são unidos aos deste dispositivo. As listas e perfis que já existem são atualizados.", 13, true),
            Ui.Button("Escolher cópia para restaurar", ImportAsync)));
        View operations;
        if (Ui.IsTelevision)
        {
            var grid = new Grid
            {
                ColumnSpacing = 10,
                ColumnDefinitions = [new(GridLength.Star), new(GridLength.Star)]
            };
            grid.Add(export);
            grid.Add(import, 1);
            operations = grid;
        }
        else operations = Ui.Stack(export, import);
        content = Ui.Stack(Ui.Text("Exporte e importe os seus dados", 28), operations, spinner, status);
        content.Padding = 24;
        content.MaximumWidthRequest = 800;
        Content = new ScrollView { Content = content };
    }

    private async Task ExportAsync()
    {
        if (busy) return;
        await RunAsync("A criar cópia…", async () =>
        {
            var payload = await BackupService.CreateAsync();
            var path = Path.Combine(FileSystem.CacheDirectory,
                $"PSiptv-backup-{DateTime.Now:yyyyMMdd-HHmmss}.psiptvbackup");
            await File.WriteAllBytesAsync(path, payload);
            await Share.Default.RequestAsync(new ShareFileRequest(
                LanguageService.Text("Guardar ou partilhar cópia PSiptv"), new ShareFile(path)));
            status.Text = LanguageService.Text("Cópia criada. Confirme que guardou o ficheiro num local seguro.");
        });
    }

    private async Task ImportAsync()
    {
        if (busy) return;
        var selected = await FilePicker.Default.PickAsync(new PickOptions
        {
            PickerTitle = LanguageService.Text("Escolher cópia PSiptv")
        });
        if (selected is null) return;
        if (!await LanguageService.ConfirmAsync(this, "Restaurar cópia",
                "Unir os dados desta cópia aos dados atuais?", "Restaurar", "Cancelar")) return;
        await RunAsync("A restaurar cópia…", async () =>
        {
            await using var input = await selected.OpenReadAsync();
            using var output = new MemoryStream();
            var buffer = new byte[81920];
            int read;
            while ((read = await input.ReadAsync(buffer)) > 0)
            {
                if (output.Length + read > BackupService.MaximumFileSize)
                    throw new InvalidOperationException("A cópia de segurança excede o limite de 50 MB.");
                output.Write(buffer, 0, read);
            }
            await BackupService.RestoreAsync(output.ToArray());
            await LanguageService.AlertAsync(this, "Cópia restaurada",
                "Os dados foram sincronizados. Abra uma lista para continuar.");
            await Navigation.PopToRootAsync(false);
        });
    }

    private async Task RunAsync(string message, Func<Task> action)
    {
        busy = true;
        content.IsEnabled = false;
        spinner.IsVisible = spinner.IsRunning = true;
        status.Text = LanguageService.Text(message);
        try { await action(); }
        catch (Exception ex) { status.Text = ""; await Ui.ErrorAsync(this, ex); }
        finally
        {
            spinner.IsRunning = spinner.IsVisible = false;
            content.IsEnabled = true;
            busy = false;
        }
    }

}
