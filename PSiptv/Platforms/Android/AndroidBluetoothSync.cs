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
    private static ConsumptionSyncWorker? pendingChanges;
    private static Task? listenerTask;
    private static BluetoothStateReceiver? receiver;
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
            lifetime = new();
            var token = lifetime.Token;
            var changes = new ConsumptionSyncWorker();
            pendingChanges = changes;
            try { RegisterReceiver(); }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            { Stop(); NearbySyncService.ReportError(ex); return; }
            StartListener(token);
            _ = changes.RunAsync(() => SyncPendingAsync(token), token);
            changes.Request(); // Resume a saved pending change once; idle profiles do not connect.
        }
    }

    internal static void Stop()
    {
        lock (sync)
        {
            pendingChanges = null;
            if (receiver is { } registered)
            {
                try { Android.App.Application.Context.UnregisterReceiver(registered); }
                catch (Java.Lang.IllegalArgumentException) { }
                receiver = null;
                registered.Dispose();
            }
            lifetime?.Cancel();
            lifetime?.Dispose();
            lifetime = null;
            try { listener?.Close(); } catch (Java.IO.IOException) { }
            listener = null;
            listenerTask = null;
        }
    }

    internal static void RequestSync()
    {
        Start();
        lock (sync)
        {
            if (lifetime is { } current) StartListener(current.Token);
            pendingChanges?.Request();
        }
    }

    private static void RegisterReceiver()
    {
        var filter = new IntentFilter(BluetoothAdapter.ActionStateChanged);
        receiver = new BluetoothStateReceiver();
        var permission = OperatingSystem.IsAndroidVersionAtLeast(31)
            ? Android.Manifest.Permission.BluetoothConnect : Android.Manifest.Permission.Bluetooth;
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
            Android.App.Application.Context.RegisterReceiver(receiver, filter, permission, null, ReceiverFlags.Exported);
        else Android.App.Application.Context.RegisterReceiver(receiver, filter, permission, null);
    }

    private sealed class BluetoothStateReceiver : BroadcastReceiver
    {
        public override void OnReceive(Context? context, Intent? intent)
        {
            if (intent?.Action != BluetoothAdapter.ActionStateChanged) return;
            var state = (State)intent.GetIntExtra(BluetoothAdapter.ExtraState, -1);
            lock (sync)
            {
                if (state is State.Off or State.TurningOff)
                {
                    try { listener?.Close(); } catch (Java.IO.IOException) { }
                    listener = null;
                }
                else if (state == State.On && lifetime is { } current)
                    _ = RestartAfterRadioChangeAsync(listenerTask, current.Token);
            }
        }
    }

    private static async Task RestartAfterRadioChangeAsync(Task? previous, CancellationToken token)
    {
        // A radio-off event closes Accept. Wait for that listener to exit before registering another one.
        if (previous is not null) await previous.ConfigureAwait(false);
        lock (sync)
        {
            if (token.IsCancellationRequested) return;
            StartListener(token);
            pendingChanges?.Request();
        }
    }

    private static void StartListener(CancellationToken token)
    {
        if (token.IsCancellationRequested || listenerTask is { IsCompleted: false }) return;
        listenerTask = Task.Run(() => ListenAsync(token));
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
            if (Adapter?.IsEnabled != true)
            { NearbySyncService.SetStatus("Ligue o Bluetooth nas definições do Android."); return; }
            server = Adapter?.ListenUsingRfcommWithServiceRecord("PSiptv Profile Sync", ServiceId);
            if (server is null) return;
            lock (sync)
            {
                if (token.IsCancellationRequested) return;
                listener = server;
            }
            NearbySyncService.SetStatus("Sincronização Bluetooth pronta. Aguarda alterações de episódios ouvidos ou conteúdos vistos.");
            while (!token.IsCancellationRequested)
            {
                using var socket = server.Accept();
                if (socket is null) continue;
                // Secure RFCOMM requires Android pairing. Only bonded peers can exchange profile data.
                if (socket.RemoteDevice?.BondState != Bond.Bonded) continue;
                try { await ExchangeAsync(socket, true, token); }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                { if (!token.IsCancellationRequested) NearbySyncService.ReportError(ex); }
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        { if (!token.IsCancellationRequested) NearbySyncService.ReportError(ex); }
        finally
        {
            try { server?.Close(); } catch (Java.IO.IOException) { }
            server?.Dispose();
            lock (sync) { if (ReferenceEquals(listener, server)) listener = null; }
        }
    }

    internal static async Task SynchronizeAsync(string address, string? expectedScope = null)
    {
        if (!NearbySyncService.Enabled || !HasPermission || Adapter?.IsEnabled != true)
            throw new InvalidOperationException("Ative o Bluetooth e a sincronização na aplicação.");
        Start();
        CancellationToken token;
        lock (sync) token = lifetime?.Token ?? throw new InvalidOperationException("Mantenha a aplicação aberta para sincronizar.");
        var selection = await MainThread.InvokeOnMainThreadAsync(() =>
        {
            var account = AppServices.ActiveAccount ?? throw new InvalidOperationException("Abra uma lista antes de sincronizar.");
            var key = RemoteControlService.CurrentProfileKey;
            var scope = UserProfileService.Scope(account.Id);
            if (expectedScope is not null && scope != expectedScope) throw new OperationCanceledException(token);
            Preferences.Default.Set("profile-sync.bluetooth.peer." + key, address);
            return (Key: key, Scope: scope, Version: AppServices.SessionVersion);
        });
        await clientGate.WaitAsync(token);
        try
        {
            var profileKey = selection.Key;
            if (selection.Version != AppServices.SessionVersion || selection.Key != RemoteControlService.CurrentProfileKey)
                throw new InvalidOperationException("O perfil mudou durante a sincronização.");
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
            // Android stream implementations can block: keep transport I/O off the UI thread.
            await Task.Run(() => ExchangeAsync(socket, false, timeout.Token), timeout.Token);
        }
        finally { clientGate.Release(); }
    }

    private static async Task ExchangeAsync(BluetoothSocket socket, bool server, CancellationToken token)
    {
        var context = await MainThread.InvokeOnMainThreadAsync(() =>
            (Account: AppServices.ActiveAccount, Profile: UserProfileService.Active.Id,
                Version: AppServices.SessionVersion, Key: RemoteControlService.CurrentProfileKey,
                Scope: UserProfileService.Scope(AppServices.ActiveAccount?.Id ?? "")));
        if (context.Account is null || context.Key.Length == 0) throw new InvalidOperationException("Abra uma lista antes de sincronizar.");
        var pending = await MainThread.InvokeOnMainThreadAsync(() => NearbySyncService.Pending(context.Scope));
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
            {
                CheckContext();
                var value = await FavoritesService.SnapshotAsync();
                var library = await ExperienceService.LoadAsync(context.Account.Id, context.Profile);
                CheckContext();
                return value! with { ViewingStates = ViewingStateSync.Snapshot(library) };
            }),
            snapshot => MainThread.InvokeOnMainThreadAsync(async () =>
            {
                CheckContext();
                await FavoritesService.ImportNewerAsync(context.Account, context.Profile, snapshot with { ViewingStates = null });
                CheckContext();
                if (snapshot.ViewingStates is { } states)
                    await ExperienceService.ImportViewingStatesAsync(context.Account.Id, context.Profile, states);
                CheckContext();
            }), timeout.Token);
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            CheckContext();
            // The receiving device can also initiate subsequent exchanges after local edits.
            if (socket.RemoteDevice?.Address is { Length: > 0 } address &&
                Preferences.Default.Get("profile-sync.bluetooth.peer." + context.Key, "").Length == 0)
                Preferences.Default.Set("profile-sync.bluetooth.peer." + context.Key, address);
            NearbySyncService.Confirm(context.Scope, pending);
        });
        await Task.Delay(TimeSpan.FromMilliseconds(150), timeout.Token);
        NearbySyncService.SetStatus(LanguageService.Format("Sincronização Bluetooth concluída · {0}", DateTimeOffset.Now.ToString("HH:mm")));
    }

    private static async Task SyncPendingAsync(CancellationToken token)
    {
        try
        {
            token.ThrowIfCancellationRequested();
            var pending = await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    var key = RemoteControlService.CurrentProfileKey;
                    var scope = AppServices.ActiveAccount is { } account ? UserProfileService.Scope(account.Id) : "";
                    return (Scope: scope, Change: NearbySyncService.Pending(scope),
                        Address: key.Length == 0 ? "" : Preferences.Default.Get("profile-sync.bluetooth.peer." + key, ""));
                });
            if (pending.Change.Length == 0) return;
            if (pending.Address.Length == 0)
            { NearbySyncService.SetStatus("Escolha o equipamento Bluetooth uma vez para sincronizar os conteúdos vistos e ouvidos."); return; }
            await SynchronizeAsync(pending.Address, pending.Scope);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        { if (!token.IsCancellationRequested) NearbySyncService.ReportError(ex); }
    }

    private static void Close(BluetoothSocket socket)
    { try { socket.Close(); } catch (Java.IO.IOException) { } }
}
