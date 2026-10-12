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
                using var socket = server.Accept();
                if (socket?.RemoteDevice?.BondState != Bond.Bonded) continue;
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(TimeSpan.FromSeconds(15));
                using var cancellation = timeout.Token.Register(() => Close(socket));
                try
                {
                    var peer = await MainThread.InvokeOnMainThreadAsync(RemoteControlService.BluetoothDiscoveryResponse);
                    await BluetoothRemoteProtocol.ServeAsync(socket.InputStream!, socket.OutputStream!, () => peer,
                        (request, profile) => RemoteControlService.ExecuteRequestAsync(request, profile), timeout.Token);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                { System.Diagnostics.Debug.WriteLine(ex); }
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

    internal static async Task<RemoteControlState> SendAsync(string address, string command, string value,
        CancellationToken token)
    {
        await ClientGate.WaitAsync(token);
        try { return await SendCoreAsync(address, command, value, token); }
        finally { ClientGate.Release(); }
    }

    // Discovery probes distinct peers concurrently, without delaying playback commands behind accessories.
    internal static Task<RemoteControlState> ProbeAsync(string address, CancellationToken token) =>
        SendCoreAsync(address, "state", "", token);

    private static async Task<RemoteControlState> SendCoreAsync(string address, string command, string value,
        CancellationToken token)
    {
        Start();
        if (!BluetoothRemoteService.Enabled || !HasPermission || Adapter?.IsEnabled != true)
            throw new InvalidOperationException("Ative o controlo Bluetooth na aplicação e o Bluetooth no Android.");
        CancellationToken receiverToken;
        lock (Sync) receiverToken = lifetime?.Token ?? throw new InvalidOperationException("Mantenha a aplicação aberta para controlar por Bluetooth.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token, receiverToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        var key = RemoteControlService.CurrentProfileKey;
        var session = AppServices.SessionVersion;
        if (key.Length == 0) throw new InvalidOperationException("Abra uma lista antes de usar o comando remoto.");
        using var device = Adapter!.GetRemoteDevice(address);
        if (device?.BondState != Bond.Bonded)
            throw new InvalidOperationException("Emparelhe os equipamentos nas definições Bluetooth do Android.");
        using var socket = device.CreateRfcommSocketToServiceRecord(ServiceId)
            ?? throw new IOException("O dispositivo não suporta esta ligação Bluetooth.");
        using var cancellation = timeout.Token.Register(() => Close(socket));
        try
        {
            await Task.Run(socket.Connect, timeout.Token);
            if (key != RemoteControlService.CurrentProfileKey || session != AppServices.SessionVersion)
                throw new IOException("O perfil mudou.");
            var reply = await BluetoothRemoteProtocol.SendAsync(socket.InputStream!, socket.OutputStream!,
                new(RemoteControlProtocol.Magic, RemoteControlProtocol.Version, key, RemoteControlService.DeviceId),
                command, value, timeout.Token);
            if (key != RemoteControlService.CurrentProfileKey || session != AppServices.SessionVersion)
                throw new IOException("O perfil mudou.");
            if (!reply.Success || reply.State is null) throw new IOException(reply.Error);
            return reply.State;
        }
        catch (Exception ex) when (timeout.IsCancellationRequested && !token.IsCancellationRequested)
        { throw new IOException("A ligação Bluetooth expirou. Confirme que a aplicação está aberta e o controlo Bluetooth está ativo no outro equipamento.", ex); }
    }

    private static void Close(BluetoothSocket socket)
    { try { socket.Close(); } catch (Java.IO.IOException) { } }
}
