using PSiptv.Core;

namespace PSiptv.Services;

public sealed record NearbySyncDevice(string Address, string Name);

/// <summary>Optional Bluetooth synchronization while both applications are open.</summary>
public static class NearbySyncService
{
    private const string EnabledKey = "profile-sync.bluetooth.enabled.v1";
    public static bool Enabled => Preferences.Default.Get(EnabledKey, false);
    public static bool Supported => DeviceInfo.Platform == DevicePlatform.Android;
    public static string Status { get; private set; } = "Bluetooth desligado na aplicação.";
    public static event Action? Changed;
    private static readonly object pendingLock = new();
    public static string LastError => Preferences.Default.Get("profile-sync.bluetooth.last-error", "");
    private static string PendingKey(string scope) => "profile-sync.bluetooth.pending." + scope;

    internal static void NotifyConsumptionChanged(string scope)
    {
        if (!Enabled) return;
        lock (pendingLock) Preferences.Default.Set(PendingKey(scope), Guid.NewGuid().ToString("N"));
#if ANDROID
        PSiptv.AndroidBluetoothSync.RequestSync();
#endif
    }

    internal static string Pending(string scope)
    {
        lock (pendingLock) return Preferences.Default.Get(PendingKey(scope), "");
    }
    internal static void Confirm(string scope, string change)
    {
        lock (pendingLock)
            if (change.Length > 0 && Pending(scope) == change) Preferences.Default.Remove(PendingKey(scope));
        Preferences.Default.Remove("profile-sync.bluetooth.last-error");
    }

    public static async Task<bool> SetEnabledAsync(bool enabled)
    {
#if ANDROID
        if (enabled && !await PSiptv.AndroidBluetoothSync.RequestPermissionAsync())
        { SetStatus("Autorize o acesso a dispositivos próximos para sincronizar por Bluetooth."); return false; }
        Preferences.Default.Set(EnabledKey, enabled);
        SetStatus(enabled ? "Bluetooth ativo. Emparelhe os equipamentos nas definições do Android e escolha o dispositivo." :
            "Bluetooth desligado na aplicação.");
        if (enabled) PSiptv.AndroidBluetoothSync.Start();
        else PSiptv.AndroidBluetoothSync.Stop();
        return true;
#else
        await Task.CompletedTask;
        return false;
#endif
    }

    public static IReadOnlyList<NearbySyncDevice> PairedDevices()
    {
#if ANDROID
        return PSiptv.AndroidBluetoothSync.PairedDevices();
#else
        return [];
#endif
    }

    public static async Task SynchronizeAsync(string address)
    {
#if ANDROID
        try { await PSiptv.AndroidBluetoothSync.SynchronizeAsync(address); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        { ReportError(ex); throw new InvalidOperationException(ErrorText(ex), ex); }
#else
        await Task.CompletedTask;
#endif
    }

    internal static string ErrorText(Exception ex)
    {
        if (ex is ProfileSyncException || ex is InvalidOperationException) return ex.Message;
        if (ex is TimeoutException) return ex.Message;
        if (ex is OperationCanceledException)
            return "A sincronização Bluetooth excedeu o tempo disponível ou foi interrompida. Confirme que ambas as aplicações estão abertas.";
        if (ex is EndOfStreamException)
            return "O outro equipamento fechou a ligação antes de concluir a sincronização. Confirme a versão da aplicação, lista e perfil nos dois equipamentos.";
        if (ex is IOException || ex.GetType().FullName?.StartsWith("Java.IO.", StringComparison.Ordinal) == true)
        {
            var detail = ex.Message.Replace('\r', ' ').Replace('\n', ' ');
            if (detail.Contains("http", StringComparison.OrdinalIgnoreCase)) detail = ex.GetType().Name;
            if (detail.Length > 160) detail = detail[..160];
            return "Falha na ligação Bluetooth ao equipamento. Confirme o emparelhamento e que a outra aplicação está aberta. Detalhe: " + detail;
        }
        if (ex.GetType().Name == "SecurityException")
            return "O Android recusou a permissão Bluetooth. Autorize o acesso a dispositivos próximos nos dois equipamentos.";
        return "Falha na sincronização Bluetooth. Código: " + ex.GetType().Name + " / " + ex.HResult.ToString("X8");
    }

    internal static void ReportError(Exception ex)
    {
        System.Diagnostics.Debug.WriteLine(ex);
        var error = ErrorText(ex);
        MainThread.BeginInvokeOnMainThread(() =>
        {
            Preferences.Default.Set("profile-sync.bluetooth.last-error", error);
            SetStatus(error + " As alterações pendentes ficam guardadas, sem tentativas periódicas.");
        });
    }

    internal static void SetStatus(string value)
    {
        MainThread.BeginInvokeOnMainThread(() => { Status = value; Changed?.Invoke(); });
    }
}
