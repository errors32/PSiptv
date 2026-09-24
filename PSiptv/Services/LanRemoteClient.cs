using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using PSiptv.Core;

namespace PSiptv.Services;

public sealed record LanRemoteDevice(string Name, IPAddress Address, int Port, string Token,
    string DeviceId, string ProfileName, bool IsActive, string LeaderDeviceId, long LeaderEpoch);

public sealed class LanRemoteClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<LanRemoteDevice>> DiscoverAsync(string profileKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(profileKey)) return [];
        using var udp = new UdpClient(AddressFamily.InterNetwork) { EnableBroadcast = true };
        udp.Client.Bind(new IPEndPoint(IPAddress.Any, 0));
        var request = JsonSerializer.SerializeToUtf8Bytes(new RemoteDiscoveryRequest(RemoteControlProtocol.Magic,
            RemoteControlProtocol.Version, profileKey, RemoteControlService.DeviceId), Json);
        foreach (var address in RemoteControlService.BroadcastAddresses().Distinct())
            try { await udp.SendAsync(request, new IPEndPoint(address, RemoteControlProtocol.DiscoveryPort), cancellationToken); }
            catch (SocketException) { }

        var found = new Dictionary<string, LanRemoteDevice>();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            while (true)
            {
                var packet = await udp.ReceiveAsync(timeout.Token);
                RemoteDiscoveryResponse? response;
                try { response = JsonSerializer.Deserialize<RemoteDiscoveryResponse>(packet.Buffer, Json); }
                catch (JsonException) { continue; }
                if (response?.Magic != RemoteControlProtocol.Magic || response.Version != RemoteControlProtocol.Version ||
                    response.ProfileKey != profileKey || response.Token.Length == 0 ||
                    response.DeviceId == RemoteControlService.DeviceId) continue;
                found[response.DeviceId] = new(response.DeviceName, packet.RemoteEndPoint.Address, response.Port,
                    response.Token, response.DeviceId, response.ProfileName, response.IsActive,
                    response.LeaderDeviceId, response.LeaderEpoch);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
        return found.Values.OrderByDescending(device => device.IsActive).ThenBy(device => device.Name).ToArray();
    }

    public async Task<RemoteControlState> SendAsync(LanRemoteDevice device, string command, string value,
        CancellationToken cancellationToken)
    {
        using var tcp = new TcpClient(AddressFamily.InterNetwork);
        await tcp.ConnectAsync(device.Address, device.Port, cancellationToken);
        await using var stream = tcp.GetStream();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, true) { AutoFlush = true };
        using var reader = new StreamReader(stream, Encoding.UTF8, false, 4096, true);
        await writer.WriteLineAsync(JsonSerializer.Serialize(new RemoteControlRequest(device.Token, command, value), Json));
        var line = await reader.ReadLineAsync(cancellationToken);
        var response = line is null ? null : JsonSerializer.Deserialize<RemoteControlResponse>(line, Json);
        if (response is null) throw new IOException("O dispositivo não respondeu.");
        if (!response.Success || response.State is null) throw new IOException(response.Error);
        return response.State;
    }
}
