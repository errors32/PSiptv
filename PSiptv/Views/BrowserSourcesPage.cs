using PSiptv.Services;

namespace PSiptv.Views;

public sealed class BrowserSourcesPage : LocalizedPage
{
    private readonly VerticalStackLayout list = new() { Spacing = 12 };

    public BrowserSourcesPage()
    {
        Ui.Page(this, "Fontes do browser");
        var add = Ui.Button("+ Adicionar fonte", () => EditAsync(null), true);
        var body = Ui.Stack(
            Ui.Text("Fontes do browser", 28),
            Ui.Text("A fonte predefinida abre automaticamente ao entrar na tab Browser.", 13, true),
            add,
            list);
        body.Padding = 24;
        body.MaximumWidthRequest = 800;
        Content = new ScrollView { Content = body };
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        BrowserSourcesService.Changed += Refresh;
        Refresh();
    }

    protected override void OnDisappearing()
    {
        BrowserSourcesService.Changed -= Refresh;
        base.OnDisappearing();
    }

    private void Refresh()
    {
        Dispatcher.Dispatch(() =>
        {
            list.Clear();
            var selected = BrowserSourcesService.Default;
            foreach (var source in BrowserSourcesService.Sources)
            {
                var isDefault = source.Id == selected?.Id;
                var actions = new Grid
                {
                    ColumnSpacing = 8,
                    ColumnDefinitions = [new(GridLength.Star), new(GridLength.Star)]
                };
                actions.Add(Ui.Button("Editar", () => EditAsync(source)));
                actions.Add(Ui.Button("Remover", () => DeleteAsync(source)), 1);
                var content = Ui.Stack(
                    Ui.Text(source.Name + (isDefault ? " · " + LanguageService.Text("Predefinida") : ""), 18),
                    Ui.Text(source.Url, 12, true),
                    isDefault ? new Grid() : Ui.Button("Definir como predefinida", () =>
                    {
                        BrowserSourcesService.SetDefault(source.Id);
                        return Task.CompletedTask;
                    }),
                    actions);
                list.Add(Ui.Card(content));
            }
            if (BrowserSourcesService.Sources.Count == 0)
                list.Add(Ui.Text("Ainda não existem fontes. Adicione um endereço para utilizar o browser.", 14, true));
        });
    }

    private async Task EditAsync(BrowserSource? existing)
    {
        var name = await DisplayPromptAsync(existing is null ? "Adicionar fonte" : "Editar fonte",
            "Nome apresentado na lista", "Continuar", "Cancelar", initialValue: existing?.Name ?? "", maxLength: 80);
        if (name is null) return;
        var url = await DisplayPromptAsync(existing is null ? "Adicionar fonte" : "Editar fonte",
            "Endereço HTTP ou HTTPS", "Guardar", "Cancelar", keyboard: Keyboard.Url, initialValue: existing?.Url ?? "", maxLength: 2048);
        if (url is null) return;
        try
        {
            var id = existing?.Id ?? Guid.NewGuid().ToString("N");
            BrowserSourcesService.Save(new BrowserSource(id, name, url), existing?.Id);
        }
        catch (Exception ex) { await Ui.ErrorAsync(this, ex); }
    }

    private async Task DeleteAsync(BrowserSource source)
    {
        if (!await DisplayAlertAsync("Remover fonte", $"Remover «{source.Name}»?", "Remover", "Cancelar")) return;
        BrowserSourcesService.Delete(source.Id);
    }
}
