using Android.Bluetooth;
using Android.Content;
using Android.Content.PM;
using AndroidX.Core.Content;
using PSiptv.Core;
using PSiptv.Services;

namespace PSiptv;

internal static class AndroidBluetoothRemote
{
    private static readonly Java.Util.UUID ServiceId = Java.Util.UUID.FromString(BluetoothRemoteProtocol.ServiceUuid)!;
    private static readonly object Sync = new();
    private static readonly SemaphoreSlim ClientGate = new(1, 1);
    private static CancellationTokenSource? lifetime;
    private static BluetoothServerSocket? listener;
    private static BluetoothSocket? clientSocket;
    private static string clientAddress = "";
    private static string clientProfile = "";
    private static int clientSession;
    private static bool foreground;
    private static BluetoothAdapter? Adapter => (Android.App.Application.Context
        .GetSystemService(Context.BluetoothService) as BluetoothManager)?.Adapter;
    private static bool HasPermission => !OperatingSystem.IsAndroidVersionAtLeast(31) ||
        ContextCompat.CheckSelfPermission(Android.App.Application.Context, Android.Manifest.Permission.BluetoothConnect) == Permission.Granted;

    internal static void Resume() { foreground = true; Start(); }
    internal static void Pause() { foreground = false; Stop(); }

    internal static void Start()
    {
        lock (Sync)
        {
            if (!foreground || !BluetoothRemoteService.Enabled || lifetime is not null) return;
            if (!HasPermission)
            { BluetoothRemoteService.SetStatus("Autorize o acesso a dispositivos próximos para controlar por Bluetooth."); return; }
            if (Adapter?.IsEnabled != true)
            { BluetoothRemoteService.SetStatus("Ligue o Bluetooth nas definições do Android."); return; }
            lifetime = new();
            var token = lifetime.Token;
            _ = Task.Run(() => ListenAsync(token));
        }
    }

    internal static void Stop()
    {
        lock (Sync)
        {
            lifetime?.Cancel();
            lifetime?.Dispose();
            lifetime = null;
            try { listener?.Close(); } catch (Java.IO.IOException) { }
            listener = null;
            Disconnect();
        }
    }

    private static async Task ListenAsync(CancellationToken token)
    {
        BluetoothServerSocket? server = null;
        try
        {
            server = Adapter?.ListenUsingRfcommWithServiceRecord("PSiptv Remote Control", ServiceId);
            if (server is null) return;
            lock (Sync)
            {
                if (token.IsCancellationRequested) return;
                listener = server;
            }
            BluetoothRemoteService.SetStatus("Controlo Bluetooth ativo. Mantenha a aplicação aberta nos dois equipamentos.");
            while (!token.IsCancellationRequested)
            {
                var socket = server.Accept();
                if (socket?.RemoteDevice?.BondState != Bond.Bonded) { socket?.Dispose(); continue; }
                _ = Task.Run(() => HandleSocketAsync(socket, token));
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            if (!token.IsCancellationRequested)
                BluetoothRemoteService.SetStatus("Bluetooth indisponível. Desative e volte a ativar o controlo Bluetooth.");
        }
        finally
        {
            try { server?.Close(); } catch (Java.IO.IOException) { }
            server?.Dispose();
            lock (Sync)
            {
                if (lifetime?.Token == token)
                {
                    listener = null;
                    lifetime?.Dispose();
                    lifetime = null;
                }
            }
        }
    }

    private static async Task HandleSocketAsync(BluetoothSocket socket, CancellationToken token)
    {
        using (socket)
        using (var cancellation = token.Register(() => Close(socket)))
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    // Renew the idle deadline after each exchange; polling keeps the connection alive.
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                    timeout.CancelAfter(TimeSpan.FromSeconds(90));
                    using var idleCancellation = timeout.Token.Register(() => Close(socket));
                    var peer = await MainThread.InvokeOnMainThreadAsync(RemoteControlService.BluetoothDiscoveryResponse);
                    peer = peer with { KeepConnection = true };
                    await BluetoothRemoteProtocol.ServeAsync(socket.InputStream!, socket.OutputStream!, () => peer,
                        (request, profile) => RemoteControlService.ExecuteRequestAsync(request, profile), timeout.Token);
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            { System.Diagnostics.Debug.WriteLine(ex); }
        }
    }

    internal static void Disconnect()
    {
        lock (Sync)
        {
            if (clientSocket is { } socket) { Close(socket); socket.Dispose(); }
            clientSocket = null;
            clientAddress = "";
            clientProfile = "";
        }
    }

    internal static async Task<RemoteControlState> SendAsync(string address, string command, string value,
        CancellationToken token)
    {
        await ClientGate.WaitAsync(token);
        try
        {
            try { return await SendCoreAsync(address, command, value, token); }
            catch (Exception ex) when (command == "state" && !token.IsCancellationRequested && ex is not OutOfMemoryException)
            {
                // A state read is safe to repeat after the remote app closes a stale connection.
                Disconnect();
                return await SendCoreAsync(address, command, value, token);
            }
        }
        finally { ClientGate.Release(); }
    }

    private static async Task<RemoteControlState> SendCoreAsync(string address, string command, string value,
        CancellationToken token)
    {
        Start();
        if (!BluetoothRemoteService.Enabled || !HasPermission || Adapter?.IsEnabled != true)
            throw new InvalidOperationException("Ative o controlo Bluetooth na aplicação e o Bluetooth no Android.");
        CancellationToken receiverToken;
        lock (Sync) receiverToken = lifetime?.Token ?? throw new InvalidOperationException("Mantenha a aplicação aberta para controlar por Bluetooth.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token, receiverToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(command == "channel" ? 45 : 15));
        var key = RemoteControlService.CurrentProfileKey;
        var session = AppServices.SessionVersion;
        if (key.Length == 0) throw new InvalidOperationException("Abra uma lista antes de usar o comando remoto.");
        BluetoothSocket socket;
        bool connect;
        lock (Sync)
        {
            if (clientAddress != address || clientProfile != key || clientSession != session)
                Disconnect();
            connect = clientSocket is null;
            if (connect)
            {
                using var device = Adapter!.GetRemoteDevice(address);
                if (device?.BondState != Bond.Bonded)
                    throw new InvalidOperationException("Emparelhe os equipamentos nas definições Bluetooth do Android.");
                clientSocket = device.CreateRfcommSocketToServiceRecord(ServiceId)
                    ?? throw new IOException("O dispositivo não suporta esta ligação Bluetooth.");
                clientAddress = address; clientProfile = key; clientSession = session;
            }
            socket = clientSocket!;
        }
        using var cancellation = timeout.Token.Register(() => Close(socket));
        RemoteControlResponse reply;
        var keepConnection = false;
        try
        {
            if (connect) await Task.Run(socket.Connect, timeout.Token);
            if (key != RemoteControlService.CurrentProfileKey || session != AppServices.SessionVersion)
                throw new IOException("O perfil mudou.");
            reply = await BluetoothRemoteProtocol.SendAsync(socket.InputStream!, socket.OutputStream!,
                new(RemoteControlProtocol.Magic, RemoteControlProtocol.Version, key, RemoteControlService.DeviceId),
                command, value, timeout.Token, peer => keepConnection = peer.KeepConnection);
            if (key != RemoteControlService.CurrentProfileKey || session != AppServices.SessionVersion)
                throw new IOException("O perfil mudou.");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Disconnect();
            if (timeout.IsCancellationRequested && !token.IsCancellationRequested)
                throw new IOException("A ligação Bluetooth expirou. Confirme que a aplicação está aberta e o controlo Bluetooth está ativo no outro equipamento.", ex);
            throw;
        }
        // Playback errors do not tear down a healthy Bluetooth connection or replay the command.
        if (!keepConnection) Disconnect();
        if (!reply.Success || reply.State is null) throw new IOException(reply.Error);
        return reply.State;
    }

    private static void Close(BluetoothSocket socket)
    { try { socket.Close(); } catch (Java.IO.IOException) { } catch (ObjectDisposedException) { } }
}
