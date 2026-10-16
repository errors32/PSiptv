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
    private static CancellationTokenSource? consumptionLifetime;
    private static Task? listenerTask;
    private static CancellationToken listenerToken;
    private static BluetoothStateReceiver? receiver;
    private static bool foreground;
    static AndroidBluetoothSync() => AppServices.Locked += () => Stop();

    internal static void OnProfileChanged()
    {
        StopConsumptionSync();
        if (AppServices.ActiveAccount is null) Stop();
        else Start();
    }
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
    internal static void Pause() { foreground = false; }

    internal static void Start()
    {
        lock (sync)
        {
            if (!NearbySyncService.Enabled || !HasPermission || AppServices.ActiveAccount is null) return;
            if (foreground) AndroidBluetoothSyncForegroundService.EnsureStarted();
            if (!foreground && !AndroidBluetoothSyncForegroundService.Running) return;
            if (lifetime is { } existing) { StartListener(existing.Token); return; }
            lifetime = new();
            var token = lifetime.Token;
            try { RegisterReceiver(); }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            { Stop(); NearbySyncService.ReportError(ex); return; }
            StartListener(token);
        }
    }

    internal static void Stop(bool stopService = true)
    {
        StopConsumptionSync();
        lock (sync)
        {
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
        }
        if (stopService) AndroidBluetoothSyncForegroundService.Stop();
    }

    internal static void RequestSync(string scope)
    {
        lock (sync)
        {
            if (!NearbySyncService.Enabled || !HasPermission || Adapter?.IsEnabled != true) return;
            if (AppServices.ActiveAccount is not { } account || UserProfileService.Scope(account.Id) != scope) return;
            // Only a local edit renews the bounded attempt budget.
            if (pendingChanges is null)
            {
                consumptionLifetime = new CancellationTokenSource();
                var token = consumptionLifetime.Token;
                var changes = new ConsumptionSyncWorker();
                pendingChanges = changes;
                _ = Task.Run(() => changes.RunAsync(() => SyncPendingAsync(token), token), token);
            }
            pendingChanges?.Request();
        }
    }

    private static void StopConsumptionSync()
    {
        lock (sync)
        {
            pendingChanges = null;
            consumptionLifetime?.Cancel();
            consumptionLifetime?.Dispose();
            consumptionLifetime = null;
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
        }
    }

    private static void StartListener(CancellationToken token)
    {
        if (token.IsCancellationRequested || listenerTask is { IsCompleted: false } && listenerToken == token) return;
        var previous = listenerTask;
        listenerToken = token;
        listenerTask = Task.Run(async () =>
        {
            if (previous is not null) await previous.ConfigureAwait(false);
            if (!token.IsCancellationRequested) await ListenAsync(token).ConfigureAwait(false);
        });
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
            NearbySyncService.SetStatus("Sincronização Bluetooth pronta em segundo plano. Aguarda alterações nos favoritos ou conteúdos vistos/ouvidos.");
            while (!token.IsCancellationRequested)
            {
                using var socket = server.Accept();
                if (socket is null) continue;
                // Secure RFCOMM requires Android pairing. Only bonded peers can exchange profile data.
                if (socket.RemoteDevice?.BondState != Bond.Bonded) continue;
                try
                {
                    using var awake = AndroidBluetoothSyncForegroundService.KeepTransferAwake();
                    await ExchangeAsync(socket, true, token);
                }
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

    internal static async Task SynchronizeAsync(string address, string? expectedScope = null,
        CancellationToken requestToken = default, bool consumptionAttempt = false)
    {
        if (!NearbySyncService.Enabled || !HasPermission || Adapter?.IsEnabled != true)
            throw new InvalidOperationException("Ative o Bluetooth e a sincronização na aplicação.");
        if (!consumptionAttempt) Start();
        CancellationToken receiverToken = requestToken;
        if (!consumptionAttempt)
            lock (sync) receiverToken = lifetime?.Token ?? throw new InvalidOperationException("Mantenha a aplicação aberta para sincronizar.");
        using var request = CancellationTokenSource.CreateLinkedTokenSource(receiverToken, requestToken);
        var token = request.Token;
        var selection = await MainThread.InvokeOnMainThreadAsync(() =>
        {
            var account = AppServices.ActiveAccount ?? throw new InvalidOperationException("Abra uma lista antes de sincronizar.");
            var key = RemoteControlService.CurrentProfileKey;
            var scope = UserProfileService.Scope(account.Id);
            if (expectedScope is not null && scope != expectedScope) throw new OperationCanceledException(token);
            if (!consumptionAttempt) Preferences.Default.Set("profile-sync.bluetooth.peer." + key, address);
            return (Key: key, Scope: scope, Version: AppServices.SessionVersion);
        });
        await clientGate.WaitAsync(token);
        try
        {
            using var awake = AndroidBluetoothSyncForegroundService.KeepTransferAwake();
            var profileKey = selection.Key;
            if (selection.Version != AppServices.SessionVersion || selection.Key != RemoteControlService.CurrentProfileKey)
                throw new InvalidOperationException("O perfil mudou durante a sincronização.");
            if (profileKey.Length == 0) throw new InvalidOperationException("Abra uma lista e selecione o perfil antes de sincronizar.");
            using var device = Adapter!.GetRemoteDevice(address);
            if (device?.BondState != Bond.Bonded) throw new InvalidOperationException("Emparelhe os equipamentos nas definições Bluetooth do Android.");
            using var socket = device.CreateRfcommSocketToServiceRecord(ServiceId)
                ?? throw new InvalidOperationException("O dispositivo não suporta esta ligação Bluetooth.");
            NearbySyncService.SetStatus("A ligar ao equipamento por Bluetooth…");
            // This deadline applies only to Connect, not to the subsequent snapshot exchange.
            using (var connection = CancellationTokenSource.CreateLinkedTokenSource(token))
            using (var cancellation = connection.Token.Register(() => Close(socket)))
            {
                var seconds = consumptionAttempt ? 10 : 30;
                connection.CancelAfter(TimeSpan.FromSeconds(seconds));
                try { await Task.Run(socket.Connect, connection.Token); }
                catch (Exception ex) when (ex is not OutOfMemoryException && connection.IsCancellationRequested && !token.IsCancellationRequested)
                { throw new TimeoutException(LanguageService.Format("A ligação Bluetooth não foi estabelecida em {0} segundos.", seconds), ex); }
            }
            if (profileKey != RemoteControlService.CurrentProfileKey) return;
            // Android stream implementations can block: keep transport I/O off the UI thread.
            await Task.Run(() => ExchangeAsync(socket, false, token), token);
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
        using var timeout = new SyncTransferDeadline(token);
        using var cancellation = timeout.Token.Register(() => Close(socket));
        var lastProgress = new ProfileSyncProgress(ProfileSyncPhase.Preparing);
        var lastReport = 0L;
        void Progress(ProfileSyncProgress progress)
        {
            timeout.Touch();
            var changedPhase = lastProgress.Phase != progress.Phase;
            lastProgress = progress;
            var now = Environment.TickCount64;
            if (!changedPhase && now - lastReport < 500 && progress.Bytes != progress.TotalBytes) return;
            lastReport = now;
            NearbySyncService.SetStatus(ProgressText(progress));
        }
        void CheckContext()
        {
            timeout.Token.ThrowIfCancellationRequested();
            if (context.Version != AppServices.SessionVersion || context.Key != RemoteControlService.CurrentProfileKey)
                throw new InvalidOperationException("O perfil mudou durante a sincronização.");
        }
        try
        {
            await ProfileSyncProtocol.ExchangeAsync(socket.InputStream!, socket.OutputStream!, context.Key, server,
                async () =>
                {
                    CheckContext();
                    var value = await FavoritesService.ExportSnapshotAsync(context.Account.Id, context.Profile);
                    var library = await ExperienceService.LoadAsync(context.Account.Id, context.Profile);
                    CheckContext();
                    return value! with { ViewingStates = ViewingStateSync.Snapshot(library) };
                },
                async snapshot =>
                {
                    CheckContext();
                    await FavoritesService.ImportNewerAsync(context.Account, context.Profile, snapshot with { ViewingStates = null });
                    CheckContext();
                    if (snapshot.ViewingStates is { } states)
                        await ExperienceService.ImportViewingStatesAsync(context.Account.Id, context.Profile, states);
                    CheckContext();
                }, timeout.Token, Progress);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException && timeout.TimedOut)
        {
            throw new TimeoutException((timeout.TotalExpired
                ? "A sincronização Bluetooth excedeu o limite total de 5 minutos. "
                : "A sincronização Bluetooth ficou 90 segundos sem progresso. ") + ProgressText(lastProgress), ex);
        }
        catch (Exception ex) when (ex is IOException || ex.GetType().FullName?.StartsWith("Java.IO.", StringComparison.Ordinal) == true)
        {
            if (token.IsCancellationRequested) throw new OperationCanceledException(token);
            throw new IOException("Ligação interrompida. " + ProgressText(lastProgress) + " Detalhe: " + ex.Message, ex);
        }
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            CheckContext();
            // The receiving device can also initiate subsequent exchanges after local edits.
            if (socket.RemoteDevice?.Address is { Length: > 0 } address &&
                Preferences.Default.Get("profile-sync.bluetooth.peer." + context.Key, "").Length == 0)
                Preferences.Default.Set("profile-sync.bluetooth.peer." + context.Key, address);
            if (socket.RemoteDevice?.Address is { Length: > 0 } peer)
            {
                NearbySyncService.RememberPeer(context.Key, peer, socket.RemoteDevice.Name ?? "Dispositivo Bluetooth");
            }
            NearbySyncService.Confirm(context.Scope, pending);
        });
        await Task.Delay(TimeSpan.FromMilliseconds(150), token);
        NearbySyncService.SetStatus(LanguageService.Format("Sincronização Bluetooth concluída · {0}", DateTimeOffset.Now.ToString("HH:mm")));
    }

    private static string ProgressText(ProfileSyncProgress progress)
    {
        var phase = progress.Phase switch
        {
            ProfileSyncPhase.Preparing => "A preparar os dados para sincronizar…",
            ProfileSyncPhase.Sending => "A enviar dados por Bluetooth…",
            ProfileSyncPhase.Receiving => "A receber dados por Bluetooth…",
            ProfileSyncPhase.Saving => "A guardar os dados recebidos…",
            _ => "A confirmar a sincronização Bluetooth…"
        };
        return LanguageService.Text(phase) + (progress.TotalBytes > 4
            ? $" · {(progress.Bytes + 1023) / 1024}/{(progress.TotalBytes + 1023) / 1024} KB" : "");
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
                    var address = key.Length == 0 ? "" : Preferences.Default.Get("profile-sync.bluetooth.peer." + key, "");
                    if (address.Length == 0) address = NearbySyncService.RememberedPeers(key).FirstOrDefault()?.Address ?? "";
                    return (Scope: scope, Change: NearbySyncService.Pending(scope), Address: address);
                });
            if (pending.Change.Length == 0) return;
            if (pending.Address.Length == 0)
            { NearbySyncService.SetStatus("Sem equipamento Bluetooth conhecido. Aguarda uma nova alteração nos favoritos ou conteúdos vistos/ouvidos."); return; }
            using var awake = AndroidBluetoothSyncForegroundService.KeepTransferAwake();
            var succeeded = await BoundedSyncAttempt.RunAsync(async (attempt, attemptToken) =>
            {
                // A manual or received exchange can have sent this change already.
                if (NearbySyncService.Pending(pending.Scope) != pending.Change) return true;
                NearbySyncService.SetStatus($"Sincronização em segundo plano · tentativa {attempt}/2");
                await SynchronizeAsync(pending.Address, pending.Scope, attemptToken, consumptionAttempt: true);
                return true;
            }, token);
            if (!succeeded)
                NearbySyncService.SetStatus("Equipamento indisponível após duas tentativas. Alterações guardadas; aguarda uma nova alteração local.");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            if (!token.IsCancellationRequested)
            {
                System.Diagnostics.Debug.WriteLine(ex);
                NearbySyncService.SetStatus("Não foi possível sincronizar. Alterações guardadas; aguarda uma nova alteração local.");
            }
        }
    }

    private static void Close(BluetoothSocket socket)
    { try { socket.Close(); } catch (Java.IO.IOException) { } catch (ObjectDisposedException) { } }
}
