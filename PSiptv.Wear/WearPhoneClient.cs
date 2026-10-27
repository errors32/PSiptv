using Android.Gms.Wearable;
using PSiptv.Core;
using PSiptv.WearTransport;

namespace PSiptv.Wear;

internal sealed class WearPhoneClient
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly object sync = new();
    private CancellationTokenSource? active;

    internal static async Task<IReadOnlyList<RemoteDevice>> PairedDevicesAsync(CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        using var client = WearableClass.GetCapabilityClient(Android.App.Application.Context);
        try
        {
            using var capability = await client.GetCapabilityAsync(WearChannelConnection.PhoneCapability,
                CapabilityClient.FilterReachable).WaitAsync(timeout.Token);
            return capability.Nodes.Where(node => node.IsNearby).Select(node => new RemoteDevice(
                node.DisplayName ?? "Telemóvel emparelhado", System.Net.IPAddress.None, 0, "", WearNodeId: node.Id ?? ""))
                .Where(device => device.WearNodeId.Length > 0).OrderBy(device => device.Name).ToArray();
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { throw new IOException("O telemóvel não respondeu. Confirme a ligação na aplicação Galaxy Wearable."); }
        catch (Exception ex) when (ex is not OutOfMemoryException and not OperationCanceledException)
        { throw new IOException("Não foi possível encontrar o telemóvel. Confirme a ligação na aplicação Galaxy Wearable e atualize o PSiptv nos dois equipamentos.", ex); }
    }

    internal async Task<RemoteControlState> SendAsync(string nodeId, string command, string value, CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(command == "channel" ? 45 : 20));
            lock (sync) active = timeout;
            try
            {
                // Never replay commands: a request may have arrived before a disconnection.
                return await Task.Run(async () =>
                {
                    using var client = WearableClass.GetChannelClient(Android.App.Application.Context);
                    await using var connection = await WearChannelConnection.OpenAsync(client, nodeId, timeout.Token);
                    using var cancel = timeout.Token.Register(() => _ = Task.Run(connection.AbortAsync));
                    var profile = await WearBluetoothProtocol.DiscoverProfileAsync(connection.Input, connection.Output, timeout.Token);
                    var response = await WearBluetoothProtocol.SendAsync(connection.Input, connection.Output, profile,
                        command, value, timeout.Token, _ => { });
                    timeout.Token.ThrowIfCancellationRequested();
                    if (!response.Success || response.State is null)
                        throw new IOException(string.IsNullOrWhiteSpace(response.Error) ? "O telemóvel não enviou o estado." : response.Error);
                    return response.State;
                }, timeout.Token);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                token.ThrowIfCancellationRequested();
                if (timeout.IsCancellationRequested || ex is not IOException)
                    throw new IOException("Sem resposta do telemóvel. Confirme o Bluetooth e a ligação Galaxy Wearable; abra uma lista no PSiptv e ative o controlo Bluetooth.", ex);
                throw;
            }
            finally { lock (sync) active = null; }
        }
        finally { gate.Release(); }
    }

    internal void Disconnect()
    { lock (sync) active?.Cancel(); }
}
