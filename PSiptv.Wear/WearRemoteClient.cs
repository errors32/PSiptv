using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using PSiptv.Core;

namespace PSiptv.Wear;

public sealed record RemoteDevice(string Name, IPAddress Address, int Port, string Token, bool IsActive = true,
    string BluetoothAddress = "")
{
    public string Key => BluetoothAddress.Length > 0 ? BluetoothAddress : Address.ToString();
}

public sealed class WearRemoteClient
{
    private readonly TimeSpan commandTimeout;

    public WearRemoteClient() : this(TimeSpan.FromSeconds(5)) { }

    internal WearRemoteClient(TimeSpan commandTimeout) => this.commandTimeout = commandTimeout;

    public async Task<IReadOnlyList<RemoteDevice>> DiscoverAsync(CancellationToken cancellationToken)
    {
        using var udp = new UdpClient(AddressFamily.InterNetwork) { EnableBroadcast = true };
        udp.Client.Bind(new IPEndPoint(IPAddress.Any, 0));
        var request = JsonSerializer.SerializeToUtf8Bytes(
            new RemoteDiscoveryRequest(RemoteControlProtocol.Magic, RemoteControlProtocol.Version),
            WearJsonContext.Default.RemoteDiscoveryRequest);

        foreach (var address in BroadcastAddresses().Distinct())
        {
            try { await udp.SendAsync(request, new IPEndPoint(address, RemoteControlProtocol.DiscoveryPort), cancellationToken); }
            catch (SocketException) { }
        }

        var found = new Dictionary<string, RemoteDevice>();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            while (true)
            {
                var packet = await udp.ReceiveAsync(timeout.Token);
                RemoteDiscoveryResponse? response;
                try { response = JsonSerializer.Deserialize(packet.Buffer, WearJsonContext.Default.RemoteDiscoveryResponse); }
                catch (JsonException) { continue; }
                if (response?.Magic != RemoteControlProtocol.Magic ||
                    response.Version != RemoteControlProtocol.Version || string.IsNullOrEmpty(response.Token) ||
                    response.Port is < 1 or > 65535) continue;
                found[packet.RemoteEndPoint.Address.ToString()] = new RemoteDevice(
                    response.DeviceName, packet.RemoteEndPoint.Address, response.Port, response.Token, response.IsActive);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
        return found.Values.OrderByDescending(device => device.IsActive).ThenBy(device => device.Name).ToArray();
    }

    public async Task<RemoteControlState> SendAsync(RemoteDevice device, string command,
        string value, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(commandTimeout);
        try
        {
            using var tcp = new TcpClient(AddressFamily.InterNetwork);
            await tcp.ConnectAsync(device.Address, device.Port, timeout.Token);
            await using var stream = tcp.GetStream();
            using var writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, true) { AutoFlush = true };
            using var reader = new StreamReader(stream, Encoding.UTF8, false, 4096, true);
            var request = new RemoteControlRequest(device.Token, command, value);
            await writer.WriteLineAsync(JsonSerializer.Serialize(request, WearJsonContext.Default.RemoteControlRequest)
                .AsMemory(), timeout.Token);
            var line = await reader.ReadLineAsync(timeout.Token);
            var response = line is null ? null : JsonSerializer.Deserialize(line, WearJsonContext.Default.RemoteControlResponse);
            if (response is null) throw new IOException("O dispositivo não respondeu.");
            if (!response.Success || response.State is null) throw new IOException(response.Error);
            return response.State;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new IOException("O dispositivo não respondeu. Verifique o Wi-Fi e mantenha o PSiptv aberto.");
        }
    }

    private static IEnumerable<IPAddress> BroadcastAddresses()
    {
        yield return IPAddress.Broadcast;
        foreach (var network in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (network.OperationalStatus != OperationalStatus.Up) continue;
            foreach (var info in network.GetIPProperties().UnicastAddresses)
            {
                if (info.Address.AddressFamily != AddressFamily.InterNetwork || info.IPv4Mask is null) continue;
                var address = info.Address.GetAddressBytes();
                var mask = info.IPv4Mask.GetAddressBytes();
                yield return new IPAddress(address.Zip(mask, (a, m) => (byte)(a | ~m)).ToArray());
            }
        }
    }
}
