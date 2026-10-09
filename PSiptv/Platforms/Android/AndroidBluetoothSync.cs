using Android.Bluetooth;
using Android.Content;
using Android.Content.PM;
using AndroidX.Core.Content;
using PSiptv.Core;
using PSiptv.Services;

namespace PSiptv;

internal static class AndroidBluetoothSync
{
    private static readonly Java.Util.UUID ServiceId = Java.Util.UUID.FromString("a9514784-cb90-48f9-92b5-86f4653e5511")!;
    private static readonly object sync = new();
    private static readonly SemaphoreSlim clientGate = new(1);
    private static CancellationTokenSource? lifetime;
    private static BluetoothServerSocket? listener;
    private static bool foreground;
    private static BluetoothAdapter? Adapter => (Android.App.Application.Context
        .GetSystemService(Context.BluetoothService) as BluetoothManager)?.Adapter;
    private static bool HasPermission => !OperatingSystem.IsAndroidVersionAtLeast(31) ||
        ContextCompat.CheckSelfPermission(Android.App.Application.Context, Android.Manifest.Permission.BluetoothConnect) == Permission.Granted;

    private sealed class ConnectPermission : Permissions.BasePlatformPermission
    {
        public ConnectPermission() { }
        public override (string androidPermission, bool isRuntime)[] RequiredPermissions =>
            OperatingSystem.IsAndroidVersionAtLeast(31) ? [(Android.Manifest.Permission.BluetoothConnect, true)] : [];
    }

    internal static async Task<bool> RequestPermissionAsync() =>
        await Permissions.RequestAsync<ConnectPermission>() == PermissionStatus.Granted;

    internal static void Resume() { foreground = true; Start(); }
    internal static void Pause() { foreground = false; Stop(); }

    internal static void Start()
    {
        lock (sync)
        {
            if (!foreground || !NearbySyncService.Enabled || !HasPermission || lifetime is not null) return;
            if (Adapter?.IsEnabled != true)
            { NearbySyncService.SetStatus("Ligue o Bluetooth nas definições do Android."); return; }
            lifetime = new();
            var token = lifetime.Token;
            _ = Task.Run(() => ListenAsync(token));
            _ = AutoSyncAsync(token);
        }
    }

    internal static void Stop()
    {
        lock (sync)
        {
            lifetime?.Cancel();
            lifetime?.Dispose();
            lifetime = null;
            try { listener?.Close(); } catch (Java.IO.IOException) { }
            listener = null;
        }
    }

    internal static IReadOnlyList<NearbySyncDevice> PairedDevices()
    {
        if (!HasPermission) return [];
        return Adapter?.BondedDevices?.Select(device => new NearbySyncDevice(device.Address ?? "",
            device.Name ?? "Dispositivo Bluetooth")).Where(d => d.Address.Length > 0).OrderBy(d => d.Name).ToArray() ?? [];
    }

    private static async Task ListenAsync(CancellationToken token)
    {
        BluetoothServerSocket? server = null;
        try
        {
            server = Adapter?.ListenUsingRfcommWithServiceRecord("PSiptv Profile Sync", ServiceId);
            if (server is null) return;
            lock (sync)
            {
                if (token.IsCancellationRequested) return;
                listener = server;
            }
            while (!token.IsCancellationRequested)
            {
                using var socket = server.Accept();
                if (socket is null) continue;
                // Secure RFCOMM requires Android pairing. Only bonded peers can exchange profile data.
                if (socket.RemoteDevice?.BondState != Bond.Bonded) continue;
                try { await ExchangeAsync(socket, true, token); }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                { if (!token.IsCancellationRequested) NearbySyncService.SetStatus("Não foi possível sincronizar por Bluetooth. Confirme a lista e o perfil."); }
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        { if (!token.IsCancellationRequested) NearbySyncService.SetStatus("Bluetooth indisponível. Desative e volte a ativar a sincronização."); }
        finally
        {
            try { server?.Close(); } catch (Java.IO.IOException) { }
            server?.Dispose();
            lock (sync) { if (ReferenceEquals(listener, server)) listener = null; }
        }
    }

    internal static async Task SynchronizeAsync(string address)
    {
        if (!NearbySyncService.Enabled || !HasPermission || Adapter?.IsEnabled != true)
            throw new InvalidOperationException("Ative o Bluetooth e a sincronização na aplicação.");
        Start();
        CancellationToken token;
        lock (sync) token = lifetime?.Token ?? throw new InvalidOperationException("Mantenha a aplicação aberta para sincronizar.");
        if (!await clientGate.WaitAsync(0, token)) return;
        try
        {
            var profileKey = RemoteControlService.CurrentProfileKey;
            if (profileKey.Length == 0) throw new InvalidOperationException("Abra uma lista e selecione o perfil antes de sincronizar.");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            using var device = Adapter!.GetRemoteDevice(address);
            if (device?.BondState != Bond.Bonded) throw new InvalidOperationException("Emparelhe os equipamentos nas definições Bluetooth do Android.");
            using var socket = device.CreateRfcommSocketToServiceRecord(ServiceId)
                ?? throw new InvalidOperationException("O dispositivo não suporta esta ligação Bluetooth.");
            using var cancellation = timeout.Token.Register(() => Close(socket));
            NearbySyncService.SetStatus("A sincronizar por Bluetooth…");
            await Task.Run(socket.Connect, timeout.Token);
            if (profileKey != RemoteControlService.CurrentProfileKey) return;
            await ExchangeAsync(socket, false, timeout.Token);
            if (profileKey == RemoteControlService.CurrentProfileKey)
                Preferences.Default.Set("profile-sync.bluetooth.peer." + profileKey, address);
        }
        finally { clientGate.Release(); }
    }

    private static async Task ExchangeAsync(BluetoothSocket socket, bool server, CancellationToken token)
    {
        var context = await MainThread.InvokeOnMainThreadAsync(() =>
            (Account: AppServices.ActiveAccount, Profile: UserProfileService.Active.Id,
                Version: AppServices.SessionVersion, Key: RemoteControlService.CurrentProfileKey));
        if (context.Account is null || context.Key.Length == 0) throw new InvalidOperationException("Abra uma lista antes de sincronizar.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var cancellation = timeout.Token.Register(() => Close(socket));
        void CheckContext()
        {
            timeout.Token.ThrowIfCancellationRequested();
            if (context.Version != AppServices.SessionVersion || context.Key != RemoteControlService.CurrentProfileKey)
                throw new InvalidOperationException("O perfil mudou durante a sincronização.");
        }
        await ProfileSyncProtocol.ExchangeAsync(socket.InputStream!, socket.OutputStream!, context.Key, server,
            () => MainThread.InvokeOnMainThreadAsync(async () =>
            { CheckContext(); var value = await FavoritesService.SnapshotAsync(); CheckContext(); return value!; }),
            snapshot => MainThread.InvokeOnMainThreadAsync(async () =>
            { CheckContext(); await FavoritesService.ImportNewerAsync(context.Account, context.Profile, snapshot); CheckContext(); }), timeout.Token);
        NearbySyncService.SetStatus(LanguageService.Format("Sincronização Bluetooth concluída · {0}", DateTimeOffset.Now.ToString("HH:mm")));
    }

    private static async Task AutoSyncAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                var address = await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    var key = RemoteControlService.CurrentProfileKey;
                    return key.Length == 0 ? "" : Preferences.Default.Get("profile-sync.bluetooth.peer." + key, "");
                });
                if (address.Length > 0)
                    try { await SynchronizeAsync(address); }
                    catch (Exception ex) when (ex is not OutOfMemoryException) { }
                await Task.Delay(TimeSpan.FromSeconds(60), token);
            }
        }
        catch (OperationCanceledException) { }
    }

    private static void Close(BluetoothSocket socket)
    { try { socket.Close(); } catch (Java.IO.IOException) { } }
}
