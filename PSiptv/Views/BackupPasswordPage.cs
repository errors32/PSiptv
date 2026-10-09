using PSiptv.Services;

namespace PSiptv.Views;

internal sealed class BackupPasswordPage : LocalizedPage
{
    private readonly TaskCompletionSource<string?> result = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Entry password = Ui.Entry("Palavra-passe", true);
    private readonly Entry confirmation = Ui.Entry("Repetir palavra-passe", true);
    private readonly Label error = Ui.Text("", 13, true);

    private BackupPasswordPage(bool confirm)
    {
        Ui.Page(this, confirm ? "Proteger cópia de segurança" : "Abrir cópia protegida");
        confirmation.IsVisible = confirm;
        var body = Ui.Stack(Ui.Text(Title ?? "", 24),
            Ui.Text(confirm ? "Guarde a palavra-passe: será necessária para restaurar esta cópia." :
                "Introduza a palavra-passe utilizada ao criar esta cópia.", 14, true),
            password, confirmation, error,
            Ui.Button("Continuar", async () =>
            {
                if (string.IsNullOrWhiteSpace(password.Text))
                { error.Text = LanguageService.Text("Introduza uma palavra-passe."); return; }
                if (confirm && password.Text != confirmation.Text)
                { error.Text = LanguageService.Text("As palavras-passe não coincidem."); return; }
                result.TrySetResult(password.Text);
                await Navigation.PopModalAsync();
            }, true), Ui.Button("Cancelar", async () =>
            { result.TrySetResult(null); await Navigation.PopModalAsync(); }));
        body.Padding = 24;
        body.MaximumWidthRequest = 700;
        Content = new ScrollView { Content = body };
    }

    protected override void OnDisappearing()
    {
        result.TrySetResult(null);
        password.Text = confirmation.Text = "";
        base.OnDisappearing();
    }

    internal static async Task<string?> AskAsync(Page owner, bool confirm)
    {
        var page = new BackupPasswordPage(confirm);
        await owner.Navigation.PushModalAsync(page);
        return await page.result.Task;
    }
}
