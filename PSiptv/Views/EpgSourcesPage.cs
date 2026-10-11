using PSiptv.Core;
using PSiptv.Services;

namespace PSiptv.Views;

public sealed class EpgSourcesPage : LocalizedPage
{
    private readonly VerticalStackLayout rows = Ui.Stack();
    public EpgSourcesPage() { Ui.Page(this, "Fontes e associações EPG"); Content = new ScrollView { Content = rows, Padding = 16 }; }
    protected override async void OnAppearing() { base.OnAppearing(); try { await RefreshAsync(); } catch (Exception ex) { await Ui.ErrorAsync(this, ex); } }
    private async Task SaveAsync()
    {
        if (AppServices.ActiveAccount is not { } account) return;
        await CatalogOptionsService.SaveAsync(); await EpgService.DeleteAsync(account.Id); await RefreshAsync();
    }
    private async Task RefreshAsync()
    {
        rows.Clear();
        if (AppServices.ActiveAccount is null) { rows.Add(Ui.Text("Abra uma lista.")); return; }
        var config = CatalogOptionsService.Current.Epg;
        rows.Add(Ui.Text("Até oito fontes XMLTV. Os desvios horários aplicam-se por fonte. Sem fontes adicionais, mantém-se o guia existente.", 14, true));
        rows.Add(Ui.Button("+ Adicionar fonte XMLTV", async () =>
        {
            if (config.Sources.Count >= 8) throw new InvalidOperationException("O limite é de oito fontes.");
            var url = await DisplayPromptAsync("Fonte XMLTV", "Endereço HTTP/HTTPS", "Adicionar", "Cancelar");
            if (string.IsNullOrWhiteSpace(url)) return;
            config.Sources.Add(new(WebAddress.Require(url).AbsoluteUri)); await SaveAsync();
        }));
        for (var i = 0; i < config.Sources.Count; i++)
        {
            var index = i; var source = config.Sources[i];
            rows.Add(Ui.Card(Ui.Stack(Ui.Text($"Fonte {i + 1} · {new Uri(source.Url).Host}", 17),
                Ui.Text($"Correção: {source.OffsetMinutes:+0;-0;0} minutos", 13, true),
                Ui.Button("Corrigir horário", async () =>
                {
                    var text = await DisplayPromptAsync("Desvio horário", "Minutos entre -1440 e 1440", "Guardar", "Cancelar", initialValue: source.OffsetMinutes.ToString(), keyboard: Keyboard.Numeric);
                    if (text is null) return;
                    if (!int.TryParse(text, out var offset) || offset is < -1440 or > 1440) throw new InvalidOperationException("Introduza um desvio válido em minutos.");
                    config.Sources[index] = source with { OffsetMinutes = offset }; await SaveAsync();
                }), Ui.Button("Remover fonte", async () =>
                {
                    config.Sources.RemoveAt(index);
                    foreach (var key in config.Mappings.Keys.ToArray())
                    {
                        var mapping = config.Mappings[key];
                        if (mapping.SourceIndex == index) config.Mappings.Remove(key);
                        else if (mapping.SourceIndex > index) config.Mappings[key] = mapping with { SourceIndex = mapping.SourceIndex - 1 };
                    }
                    await SaveAsync();
                }))));
        }
        var channel = new Picker { Title = "Canal a associar", ItemsSource = (CatalogOptionsService.Catalogs.GetValueOrDefault(MediaKind.Channel) ?? []).Where(c => c.Kind == MediaKind.Channel).ToArray() };
        channel.SetDynamicResource(Picker.TextColorProperty, "Ink");
        // MediaItem is a record, so specify a human-readable display binding.
        channel.ItemDisplayBinding = new Binding(nameof(MediaItem.Name));
        var sourcePicker = new Picker { Title = "Fonte", ItemsSource = config.Sources.Select((s, i) => $"{i + 1}. {new Uri(s.Url).Host}").ToArray(), SelectedIndex = config.Sources.Count > 0 ? 0 : -1 };
        sourcePicker.SetDynamicResource(Picker.TextColorProperty, "Ink");
        var id = Ui.Entry("Identificador XMLTV do canal (tvg-id)");
        channel.SelectedIndexChanged += (_, _) =>
        {
            if (channel.SelectedItem is not MediaItem selected) return;
            var mapping = config.Mappings.GetValueOrDefault(CatalogPreferences.ItemKey(selected));
            id.Text = mapping?.ChannelId ?? selected.EpgId;
            if (mapping is not null) sourcePicker.SelectedIndex = mapping.SourceIndex;
        };
        rows.Add(Ui.Card(Ui.Stack(Ui.Text("Associação manual", 20), channel, sourcePicker, id,
            Ui.Button("Guardar associação", async () =>
            {
                if (channel.SelectedItem is not MediaItem selected || sourcePicker.SelectedIndex < 0 || string.IsNullOrWhiteSpace(id.Text)) return;
                config.Mappings[CatalogPreferences.ItemKey(selected)] = new(id.Text.Trim(), sourcePicker.SelectedIndex); await SaveAsync();
            }), Ui.Button("Usar associação original", async () =>
            { if (channel.SelectedItem is MediaItem selected) { config.Mappings.Remove(CatalogPreferences.ItemKey(selected)); await SaveAsync(); } }))));
    }
}
