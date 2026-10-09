using PSiptv.Services;

namespace PSiptv.Views;

internal sealed class NearbySyncView : ContentView
{
    private readonly Label status = Ui.Text("", 13, true);
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
            Ui.Text("Funciona sem a mesma rede Wi-Fi. Emparelhe os equipamentos no Android, ative esta opção em ambos e abra a mesma lista com o mesmo nome de perfil. Sincroniza favoritos e progresso dos podcasts enquanto as aplicações estão abertas.", 13, true),
            status, Ui.Button("Escolher dispositivo e sincronizar", SynchronizeAsync)));
        Loaded += (_, _) => { NearbySyncService.Changed += Refresh; Refresh(); };
        Unloaded += (_, _) => NearbySyncService.Changed -= Refresh;
        Refresh();
    }

    private void Refresh() => status.Text = LanguageService.Text(NearbySyncService.Status);
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
        catch (Exception ex) { await Ui.ErrorAsync(page, new InvalidOperationException(
            "Não foi possível sincronizar por Bluetooth. Confirme que ambas as aplicações estão abertas com a mesma lista e perfil.", ex)); }
        finally { busy = false; Refresh(); }
    }
}
