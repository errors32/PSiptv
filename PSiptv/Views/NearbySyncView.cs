using PSiptv.Services;

namespace PSiptv.Views;

internal sealed class NearbySyncView : ContentView
{
    private readonly Label status = Ui.Text("", 13, true);
    private readonly Label error = Ui.Text("", 13, true);
    private readonly Label remembered = Ui.Text("", 13, true);
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
            Ui.Text("Emparelhe os equipamentos no Android, ative esta opção em ambos e abra a mesma lista com o mesmo nome de perfil. Na primeira utilização, escolha o equipamento e sincronize. Depois, a sincronização ocorre em segundo plano ao abrir a aplicação e nas marcações de ouvido/visto. Os ficheiros de áudio não são transferidos.", 13, true),
            Ui.Text("Os equipamentos sincronizados ficam memorizados por lista e perfil. Ao abrir a aplicação, tenta sincronizar em segundo plano com eles, até cinco tentativas no total. Se nenhum responder, para até à próxima abertura. Não faz consultas periódicas. As marcações de ouvido/visto continuam a sincronizar depois de uma ligação bem-sucedida.", 13, true),
            remembered, status, error, Ui.Button("Escolher dispositivo e sincronizar", SynchronizeAsync)));
        Loaded += (_, _) => { NearbySyncService.Changed += Refresh; Refresh(); };
        Unloaded += (_, _) => NearbySyncService.Changed -= Refresh;
        Refresh();
    }

    private void Refresh()
    {
        status.Text = LanguageService.Text(NearbySyncService.Status);
        error.Text = LanguageService.Text(NearbySyncService.LastError);
        error.IsVisible = error.Text.Length > 0;
        var peers = NearbySyncService.RememberedDevices;
        remembered.Text = peers.Count == 0 ? LanguageService.Text("Ainda não há equipamentos sincronizados memorizados.") :
            LanguageService.Format("Equipamentos memorizados: {0}", string.Join(", ", peers.Select(p => string.IsNullOrWhiteSpace(p.Name) ? p.Address : p.Name)));
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
