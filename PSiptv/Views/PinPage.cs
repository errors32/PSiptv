using PSiptv.Core;
using PSiptv.Services;

namespace PSiptv.Views;

public sealed class PinPage : LocalizedPage
{
    private readonly TaskCompletionSource<bool> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Entry pin = Ui.Entry("PIN de 4 a 8 algarismos", true);
    private readonly Label status = Ui.Text("", 14, true);
    private readonly CancellationTokenSource lifetime = new();
    private readonly PlaylistAccount account;
    private readonly Switch enableBiometrics = new();
    private readonly Button biometric;
    private readonly bool allowBiometrics;
    private bool finishing;
    private bool authenticating;
    private static readonly Dictionary<string, (int Failures, DateTimeOffset Until)> Attempts = new();
    private static string BiometricKey(PlaylistAccount account) => $"psiptv.biometric.{account.Id}";

    private PinPage(PlaylistAccount account, bool allowBiometrics)
    {
        this.account = account;
        this.allowBiometrics = allowBiometrics;
        Ui.Page(this, "Desbloquear lista");
        pin.Keyboard = Keyboard.Numeric;
        pin.MaxLength = 8;
        var unlock = Ui.Button("Desbloquear com PIN", UnlockWithPinAsync, true);
        biometric = Ui.Button("Impressão digital / Face ID", UnlockWithBiometricsAsync, true);
        biometric.IsVisible = false;
        var enrollment = Ui.Stack(Ui.Text("Permitir biometria neste dispositivo"), enableBiometrics,
            Ui.Text("Ative após confirmar o PIN. Qualquer biometria registada neste dispositivo poderá abrir esta lista.", 12, true));
        enrollment.IsVisible = allowBiometrics && BiometricService.IsAvailable;
        var stack = Ui.Stack(Ui.Text("Lista protegida", 28), Ui.Text(account.Name, 18, true),
            biometric, pin, status, unlock, enrollment, Ui.Button("Cancelar", () => FinishAsync(false)));
        stack.Padding = 28; stack.MaximumWidthRequest = 460; stack.VerticalOptions = LayoutOptions.Center;
        Content = new ScrollView { Content = stack };
        AppServices.Locked += OnLocked;
    }

    private async Task UnlockWithPinAsync()
    {
        if (authenticating || finishing) return;
        var state = Attempts.GetValueOrDefault(account.Id);
        if (state.Until > DateTimeOffset.UtcNow)
        {
            status.Text = LanguageService.Format("Aguarde {0} segundos antes de tentar novamente.",
                Math.Ceiling((state.Until - DateTimeOffset.UtcNow).TotalSeconds));
            return;
        }
        authenticating = true;
        try
        {
            if (!await Task.Run(() => PinProtection.Verify(pin.Text ?? "", account.PinHash)))
            {
                var failures = state.Failures + 1;
                Attempts[account.Id] = (failures, failures >= 5 ? DateTimeOffset.UtcNow.AddSeconds(30) : default);
                status.Text = failures >= 5 ? "Demasiadas tentativas. Aguarde 30 segundos." : "PIN incorreto. Tente novamente.";
                pin.Text = "";
                return;
            }
            if (lifetime.IsCancellationRequested) return;
            Attempts.Remove(account.Id);
            if (enableBiometrics.IsToggled && BiometricService.IsAvailable)
            {
                if (await BiometricService.AuthenticateAsync(lifetime.Token))
                    await SecureStorage.Default.SetAsync(BiometricKey(account), account.PinHash);
            }
            else SecureStorage.Default.Remove(BiometricKey(account));
            if (!lifetime.IsCancellationRequested) await FinishAsync(true);
        }
        finally { authenticating = false; }
    }

    private async Task UnlockWithBiometricsAsync()
    {
        if (authenticating || finishing) return;
        authenticating = true;
        try
        {
            // Enrollment is tied to the PIN hash, so changing the PIN revokes it.
            if (await SecureStorage.Default.GetAsync(BiometricKey(account)) != account.PinHash) return;
            if (await BiometricService.AuthenticateAsync(lifetime.Token) && !lifetime.IsCancellationRequested)
                await FinishAsync(true);
            else status.Text = LanguageService.Text("Pode tentar novamente ou introduzir o PIN.");
        }
        finally { authenticating = false; }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            var enrolled = allowBiometrics && await SecureStorage.Default.GetAsync(BiometricKey(account)) == account.PinHash;
            if (lifetime.IsCancellationRequested) return;
            biometric.IsVisible = enrolled && BiometricService.IsAvailable;
            enableBiometrics.IsToggled = enrolled;
        }
        catch (Exception ex) { await Ui.ErrorAsync(this, ex); }
    }

    private async Task FinishAsync(bool allowed)
    {
        if (finishing || lifetime.IsCancellationRequested) return;
        finishing = true;
        await Navigation.PopModalAsync();
        completion.TrySetResult(allowed);
    }

    private void OnLocked()
    {
        lifetime.Cancel();
        completion.TrySetResult(false);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        lifetime.Cancel();
        AppServices.Locked -= OnLocked;
        if (!finishing) completion.TrySetResult(false);
    }

    public static async Task<bool> AuthorizeAsync(Page owner, PlaylistAccount account, bool allowBiometrics = true)
    {
        if (!account.IsProtected) return true;
        var page = new PinPage(account, allowBiometrics);
        await owner.Navigation.PushModalAsync(page);
        return await page.completion.Task;
    }
}
