using Android.Bluetooth;
using Android.Content;
using Android.Content.PM;
using PSiptv.Core;

namespace PSiptv.Wear;

internal sealed class WearBluetoothClient
{
    private static readonly Java.Util.UUID ServiceId = Java.Util.UUID.FromString(BluetoothRemoteProtocol.ServiceUuid)!;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly object sync = new();
    private BluetoothSocket? socket;
    private string address = "";
    private string profile = "";

    private static BluetoothAdapter? Adapter => (Android.App.Application.Context
        .GetSystemService(Context.BluetoothService) as BluetoothManager)?.Adapter;

    internal static bool HasPermission => !OperatingSystem.IsAndroidVersionAtLeast(31) ||
        Android.App.Application.Context.CheckSelfPermission(Android.Manifest.Permission.BluetoothConnect) == Permission.Granted;

    internal static IReadOnlyList<RemoteDevice> PairedDevices()
    {
        if (!HasPermission) throw new InvalidOperationException("Autorize o acesso a dispositivos próximos para usar Bluetooth.");
        var adapter = Adapter;
        if (adapter is null) throw new InvalidOperationException("Este relógio não disponibiliza Bluetooth.");
        if (!adapter.IsEnabled) throw new InvalidOperationException("Ligue o Bluetooth nas definições do relógio.");
        return (adapter.BondedDevices ?? []).Select(device => new RemoteDevice(
            device.Name ?? device.Address ?? "Dispositivo Bluetooth", System.Net.IPAddress.None, 0, "",
            BluetoothAddress: device.Address ?? "")).Where(device => device.BluetoothAddress.Length > 0)
            .OrderBy(device => device.Name).ToArray();
    }

    internal async Task<RemoteControlState> SendAsync(string deviceAddress, string command, string value, CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            try { return await SendCoreAsync(deviceAddress, command, value, token); }
            catch (Exception ex) when (command == "state" && !token.IsCancellationRequested && ex is not OutOfMemoryException)
            {
                // Only reads are safe to repeat after a stale connection or profile change.
                Disconnect();
                return await SendCoreAsync(deviceAddress, command, value, token);
            }
        }
        finally { gate.Release(); }
    }

    private async Task<RemoteControlState> SendCoreAsync(string deviceAddress, string command, string value, CancellationToken token)
    {
        if (!HasPermission || Adapter?.IsEnabled != true)
            throw new InvalidOperationException("Autorize os dispositivos próximos e ligue o Bluetooth no relógio.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(command == "channel" ? 45 : 15));
        using var cancellation = timeout.Token.Register(Disconnect);
        var keepConnection = false;
        RemoteControlResponse response;
        try
        {
            BluetoothSocket? current;
            lock (sync) current = address == deviceAddress ? socket : null;
            if (current is null)
            {
                Disconnect();
                current = await ConnectAsync(deviceAddress, timeout.Token);
                var discoveredProfile = await WearBluetoothProtocol.DiscoverProfileAsync(current.InputStream!, current.OutputStream!, timeout.Token);
                // Old receivers close after the unmatched bootstrap hello. Reconnect
                // explicitly so both single-exchange and persistent receivers work.
                Disconnect();
                current = await ConnectAsync(deviceAddress, timeout.Token);
                profile = discoveredProfile;
            }
            response = await WearBluetoothProtocol.SendAsync(current.InputStream!, current.OutputStream!, profile,
                command, value, timeout.Token, keep => keepConnection = keep);
            timeout.Token.ThrowIfCancellationRequested();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Disconnect();
            token.ThrowIfCancellationRequested();
            if (timeout.IsCancellationRequested)
                throw new IOException("Bluetooth sem resposta. Abra o PSiptv e ative Permitir controlo por Bluetooth no outro equipamento.", ex);
            throw;
        }
        if (!keepConnection) Disconnect();
        if (!response.Success || response.State is null)
            throw new IOException(string.IsNullOrWhiteSpace(response.Error) ? "O dispositivo não enviou o estado." : response.Error);
        return response.State;
    }

    private async Task<BluetoothSocket> ConnectAsync(string deviceAddress, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var device = Adapter!.GetRemoteDevice(deviceAddress);
        if (device?.BondState != Bond.Bonded)
            throw new IOException("Emparelhe o relógio e o dispositivo nas definições Bluetooth.");
        var created = device.CreateRfcommSocketToServiceRecord(ServiceId)
            ?? throw new IOException("O dispositivo não suporta a ligação Bluetooth do PSiptv.");
        lock (sync) { socket = created; address = deviceAddress; }
        // Closing the socket cancels Android's blocking connect/read, including
        // cancellation that happened just before the socket was assigned.
        using var cancellation = token.Register(() => Close(created));
        await Task.Run(created.Connect, token);
        token.ThrowIfCancellationRequested();
        return created;
    }

    internal void Disconnect()
    {
        lock (sync)
        {
            if (socket is { } current) { Close(current); current.Dispose(); }
            socket = null;
            address = "";
            profile = "";
        }
    }

    private static void Close(BluetoothSocket value)
    { try { value.Close(); } catch (Java.IO.IOException) { } catch (ObjectDisposedException) { } }
}
