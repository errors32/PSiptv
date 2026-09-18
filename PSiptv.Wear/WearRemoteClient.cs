using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using PSiptv.Core;

namespace PSiptv.Wear;

public sealed record RemoteDevice(string Name, IPAddress Address, int Port, string Token);

public sealed class WearRemoteClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<RemoteDevice>> DiscoverAsync(CancellationToken cancellationToken)
    {
        using var udp = new UdpClient(AddressFamily.InterNetwork) { EnableBroadcast = true };
        udp.Client.Bind(new IPEndPoint(IPAddress.Any, 0));
        var request = JsonSerializer.SerializeToUtf8Bytes(
            new RemoteDiscoveryRequest(RemoteControlProtocol.Magic, RemoteControlProtocol.Version), Json);

        foreach (var address in BroadcastAddresses())
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
                try { response = JsonSerializer.Deserialize<RemoteDiscoveryResponse>(packet.Buffer, Json); }
                catch (JsonException) { continue; }
                if (response?.Magic != RemoteControlProtocol.Magic ||
                    response.Version != RemoteControlProtocol.Version || response.Token.Length == 0) continue;
                found[packet.RemoteEndPoint.Address.ToString()] = new RemoteDevice(
                    response.DeviceName, packet.RemoteEndPoint.Address, response.Port, response.Token);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
        return found.Values.OrderBy(device => device.Name).ToArray();
    }

    public async Task<RemoteControlState> SendAsync(RemoteDevice device, string command,
        string value, CancellationToken cancellationToken)
    {
        using var tcp = new TcpClient(AddressFamily.InterNetwork);
        await tcp.ConnectAsync(device.Address, device.Port, cancellationToken);
        await using var stream = tcp.GetStream();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, true) { AutoFlush = true };
        using var reader = new StreamReader(stream, Encoding.UTF8, false, 4096, true);
        var request = new RemoteControlRequest(device.Token, command, value);
        await writer.WriteLineAsync(JsonSerializer.Serialize(request, Json));
        var line = await reader.ReadLineAsync(cancellationToken);
        var response = line is null ? null : JsonSerializer.Deserialize<RemoteControlResponse>(line, Json);
        if (response is null) throw new IOException("O dispositivo não respondeu.");
        if (!response.Success || response.State is null) throw new IOException(response.Error);
        return response.State;
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
