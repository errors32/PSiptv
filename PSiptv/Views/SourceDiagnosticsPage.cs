using System.Text;
using PSiptv.Core;
using PSiptv.Services;

namespace PSiptv.Views;

public sealed class SourceDiagnosticsPage : LocalizedPage
{
    private readonly PlaylistAccount? account;
    private readonly Label summary = Ui.Text("Pronto para executar o diagnóstico.", 14, true);
    private readonly VerticalStackLayout results = new() { Spacing = 10 };
    private readonly Button run;
    private readonly Button copy;
    private readonly CancellationTokenSource lifetime = new();
    private SourceDiagnosticReport? report;

    public SourceDiagnosticsPage(PlaylistAccount? selectedAccount = null)
    {
        account = selectedAccount ?? AppServices.ActiveAccount;
        Ui.Page(this, "Diagnóstico de fontes");
        var source = account is null
            ? Ui.Text("Abra uma lista para testar a respetiva fonte.", 14, true)
            : Ui.Text($"{account.Name} · {account.ProviderName} · {SourceDiagnosticPolicy.PublicLocation(account)}", 14, true);
        run = Ui.Button("Executar diagnóstico", RunAsync, true);
        run.IsEnabled = account is not null;
        copy = Ui.Button("Copiar relatório", CopyAsync);
        copy.IsVisible = false;
        var stack = Ui.Stack(
            Ui.Text("Estado da fonte", 28),
            Ui.Text("Verifica a ligação, o catálogo, o guia EPG e uma stream de amostra sem iniciar a reprodução.", 14, true),
            Ui.Card(Ui.Stack(Ui.Text("Fonte selecionada", 16), source)),
            run, copy, summary, results);
        stack.Padding = Ui.IsTelevision ? 14 : 24;
        stack.MaximumWidthRequest = 800;
        Content = new ScrollView { Content = stack };
    }

    private async Task RunAsync()
    {
        if (account is null) return;
        run.IsEnabled = false;
        copy.IsVisible = false;
        results.Clear();
        summary.Text = LanguageService.Text("A executar os testes…");
        try
        {
            report = await AppServices.Client.DiagnoseAsync(account, lifetime.Token);
            foreach (var check in report.Checks) results.Add(CheckCard(check));
            summary.Text = LanguageService.Text(report.State switch
            {
                SourceDiagnosticState.Passed => "Fonte operacional. Todos os testes passaram.",
                SourceDiagnosticState.Warning => "Fonte disponível, mas existem avisos a rever.",
                _ => "Foram detetados problemas na fonte."
            });
            copy.IsVisible = true;
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        finally { if (!lifetime.IsCancellationRequested) run.IsEnabled = true; }
    }

    private static View CheckCard(SourceDiagnosticCheck check)
    {
        var (symbol, color) = check.State switch
        {
            SourceDiagnosticState.Passed => ("✓", "#36D6B0"),
            SourceDiagnosticState.Warning => ("!", "#F7C96A"),
            SourceDiagnosticState.Failed => ("×", "#FF7777"),
            _ => ("–", "#A9B8CD")
        };
        var badge = Ui.Text(symbol, 22);
        badge.TextColor = Color.FromArgb(color);
        badge.HorizontalTextAlignment = TextAlignment.Center;
        var title = Ui.Text(LanguageService.Text(check.Name), 17);
        title.FontAttributes = FontAttributes.Bold;
        var elapsed = check.ElapsedMilliseconds > 0 ? $" · {check.ElapsedMilliseconds} ms" : "";
        var detail = Ui.Text(LanguageService.Text(check.Detail) + elapsed, 13, true);
        var grid = new Grid
        {
            ColumnDefinitions = [new(new GridLength(34)), new(GridLength.Star)],
            ColumnSpacing = 10
        };
        grid.Add(badge);
        grid.Add(Ui.Stack(title, detail), 1);
        return Ui.Card(grid);
    }

    private async Task CopyAsync()
    {
        if (report is null) return;
        var text = new StringBuilder()
            .AppendLine("PSiptv · Diagnóstico de fontes")
            .AppendLine($"Fonte: {report.SourceName}")
            .AppendLine($"Tipo: {report.ProviderName}")
            .AppendLine($"Servidor: {report.Location}")
            .AppendLine($"Data: {report.CompletedAt:yyyy-MM-dd HH:mm:ss zzz}")
            .AppendLine();
        foreach (var check in report.Checks)
            text.AppendLine($"[{check.State}] {check.Name}: {check.Detail} ({check.ElapsedMilliseconds} ms)");
        await Clipboard.Default.SetTextAsync(text.ToString());
        await LanguageService.AlertAsync(this, "Diagnóstico de fontes", "Relatório copiado sem credenciais nem tokens.");
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        if (!Navigation.NavigationStack.Contains(this)) lifetime.Cancel();
    }
}
