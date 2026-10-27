using System.Globalization;
using PSiptv.Core;
using PSiptv.Services;

namespace PSiptv.Views;

public sealed partial class SettingsPage
{
    private CancellationTokenSource? expirationRefresh;
    private bool listsVisible;

    protected override void OnDisappearing()
    {
        listsVisible = false;
        CancelExpirationRefresh();
        base.OnDisappearing();
    }

    private void CancelExpirationRefresh()
    {
        expirationRefresh?.Cancel();
        expirationRefresh?.Dispose();
        expirationRefresh = null;
    }

    private static async Task RefreshExpirationAsync(PlaylistAccount account, Label label, SemaphoreSlim slots, CancellationToken token)
    {
        var entered = false;
        try
        {
            await slots.WaitAsync(token);
            entered = true;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromSeconds(8));
            var expiration = await AppServices.Client.GetPlaylistExpirationAsync(account, deadline.Token);
            if (!token.IsCancellationRequested) label.Text = ExpirationText(expiration);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            if (!token.IsCancellationRequested)
                label.Text = LanguageService.Text("Expiração indisponível. Volte a abrir esta página para tentar novamente.");
        }
        finally { if (entered) slots.Release(); }
    }

    private static string ExpirationText(PlaylistExpiration expiration)
    {
        if (expiration.IsUnlimited) return LanguageService.Text("Sem expiração");
        if (expiration.ExpiresAt is not { } expiresAt) return LanguageService.Text("Expiração não informada pela fonte.");
        var date = expiresAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
        var remaining = expiresAt - DateTimeOffset.Now;
        if (remaining <= TimeSpan.Zero) return LanguageService.Format("Expirada em {0}", date);
        if (remaining.Days > 0) return LanguageService.Format("Expira em {0} · faltam {1} d {2} h", date, remaining.Days, remaining.Hours);
        if (remaining.Hours > 0) return LanguageService.Format("Expira em {0} · faltam {1} h {2} min", date, remaining.Hours, remaining.Minutes);
        if (remaining.Minutes > 0) return LanguageService.Format("Expira em {0} · faltam {1} min", date, remaining.Minutes);
        return LanguageService.Format("Expira em {0} · falta menos de 1 min", date);
    }
}
