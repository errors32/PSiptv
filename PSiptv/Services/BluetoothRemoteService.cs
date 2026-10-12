using PSiptv.Core;

namespace PSiptv.Services;

public static class BluetoothRemoteService
{
    private const string EnabledKey = "remote.bluetooth.enabled.v1";
    public static bool Supported => DeviceInfo.Platform == DevicePlatform.Android;
    public static bool Enabled => Preferences.Default.Get(EnabledKey, false);
    public static string Status { get; private set; } = "";
    public static event Action? Changed;

    public static async Task<bool> SetEnabledAsync(bool enabled)
    {
#if ANDROID
        if (enabled && !await PSiptv.AndroidBluetoothSync.RequestPermissionAsync())
        { SetStatus("Autorize o acesso a dispositivos próximos para controlar por Bluetooth."); return false; }
        Preferences.Default.Set(EnabledKey, enabled);
        if (enabled) PSiptv.AndroidBluetoothRemote.Start();
        else { PSiptv.AndroidBluetoothRemote.Stop(); SetStatus("Controlo Bluetooth desligado."); }
        return true;
#else
        await Task.CompletedTask;
        return false;
#endif
    }

    public static async Task<IReadOnlyList<LanRemoteDevice>> DiscoverAsync(CancellationToken token)
    {
#if ANDROID
        if (!Enabled) return [];
        var profile = RemoteControlService.CurrentProfileKey;
        var session = AppServices.SessionVersion;
        if (profile.Length == 0) return [];
        PSiptv.AndroidBluetoothRemote.Start();
        var found = await BluetoothRemoteDiscovery.DiscoverAsync(
            PSiptv.AndroidBluetoothSync.PairedDevices().Select(device => device.Address),
            PSiptv.AndroidBluetoothRemote.ProbeAsync, token);
        if (profile != RemoteControlService.CurrentProfileKey || session != AppServices.SessionVersion || !Enabled) return [];
        return found.Select(peer => new LanRemoteDevice(peer.State.DeviceName, System.Net.IPAddress.None, 0, "",
            peer.State.DeviceId, peer.State.ProfileName, peer.State.IsActive, peer.State.LeaderDeviceId,
            peer.State.LeaderEpoch, peer.Address)).ToArray();
#else
        await Task.CompletedTask;
        return [];
#endif
    }

    internal static async Task<RemoteControlState> SendAsync(string address, string command, string value,
        CancellationToken token)
    {
#if ANDROID
        return await PSiptv.AndroidBluetoothRemote.SendAsync(address, command, value, token);
#else
        await Task.CompletedTask;
        throw new PlatformNotSupportedException("O controlo Bluetooth está disponível no Android.");
#endif
    }

    internal static void SetStatus(string value) => MainThread.BeginInvokeOnMainThread(() =>
    { Status = value; Changed?.Invoke(); });
}
