using PSiptv.Services;

namespace PSiptv.Views;

internal sealed class NearbySyncView : ContentView
{
    private readonly Label status = Ui.Text("", 13, true);
    private readonly Label error = Ui.Text("", 13, true);
    private readonly Switch enabled = new() { IsToggled = NearbySyncService.Enabled };
    private bool updating;
    private bool busy;

    internal NearbySyncView()
    {
        IsVisible = NearbySyncService.Supported;
        enabled.Toggled += async (_, e) =>
        {
            if (updating) return;
            try
            {
                var accepted = await NearbySyncService.SetEnabledAsync(e.Value);
                if (!accepted) { updating = true; enabled.IsToggled = false; updating = false; }
            }
            catch (Exception ex)
            {
                updating = true; enabled.IsToggled = NearbySyncService.Enabled; updating = false;
                if (FindPage() is { } page) await Ui.ErrorAsync(page, ex);
            }
            Refresh();
        };
        Content = Ui.Card(Ui.Stack(Ui.Text("Sincronização por Bluetooth", 20),
            Ui.Text("Ativar neste equipamento"), enabled,
            Ui.Text("Instale a mesma versão nos dois equipamentos, emparelhe-os no Android, ative esta opção em ambos e abra a mesma lista com o mesmo nome de perfil. Escolha o equipamento uma vez. A sincronização ocorre em segundo plano quando um podcast é marcado como ouvido/por ouvir ou um filme/episódio como visto/por ver. Sem consultas periódicas nem envios durante a reprodução. Se a ligação falhar, as alterações ficam pendentes até à próxima marcação, reabertura da aplicação ou restabelecimento do Bluetooth. Os ficheiros de áudio não são transferidos.", 13, true),
            status, error, Ui.Button("Escolher dispositivo e sincronizar", SynchronizeAsync)));
        Loaded += (_, _) => { NearbySyncService.Changed += Refresh; Refresh(); };
        Unloaded += (_, _) => NearbySyncService.Changed -= Refresh;
        Refresh();
    }

    private void Refresh()
    {
        status.Text = LanguageService.Text(NearbySyncService.Status);
        error.Text = LanguageService.Text(NearbySyncService.LastError);
        error.IsVisible = error.Text.Length > 0;
    }
    private Page? FindPage()
    {
        for (Element? current = this; current is not null; current = current.Parent)
            if (current is Page page) return page;
        return null;
    }

    private async Task SynchronizeAsync()
    {
        if (busy || FindPage() is not { } page) return;
        busy = true;
        try
        {
            if (!NearbySyncService.Enabled) throw new InvalidOperationException("Ative a sincronização por Bluetooth primeiro.");
            var devices = NearbySyncService.PairedDevices();
            if (devices.Count == 0) throw new InvalidOperationException("Emparelhe os equipamentos nas definições Bluetooth do Android.");
            var labels = devices.Select(d => d.Name + " · " + d.Address[^5..]).ToArray();
            var choice = await LanguageService.ActionSheetAsync(page, "Escolher dispositivo", "Cancelar", null, labels);
            var index = Array.IndexOf(labels, choice);
            if (index >= 0) await NearbySyncService.SynchronizeAsync(devices[index].Address);
        }
        catch (InvalidOperationException ex) { await Ui.ErrorAsync(page, ex); }
        catch (Exception ex) { NearbySyncService.ReportError(ex); await Ui.ErrorAsync(page, new InvalidOperationException(NearbySyncService.ErrorText(ex), ex)); }
        finally { busy = false; Refresh(); }
    }
}
