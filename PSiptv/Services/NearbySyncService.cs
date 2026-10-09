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
        await PSiptv.AndroidBluetoothSync.SynchronizeAsync(address);
#else
        await Task.CompletedTask;
#endif
    }

    internal static void SetStatus(string value)
    {
        MainThread.BeginInvokeOnMainThread(() => { Status = value; Changed?.Invoke(); });
    }
}
