using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using PSiptv.Core;
using PSiptv.Wear;

internal static class WearDataLayerTests
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        var phone = new RemoteDevice("Telefone", IPAddress.None, 0, "", WearNodeId: "paired-phone");
        var lan = new RemoteDevice("TV", IPAddress.Loopback, 1234, "token");
        check(phone.Key == "wear:paired-phone" && lan.Key == "127.0.0.1",
            "Wear distingue o telemóvel Data Layer dos equipamentos LAN sem usar endereços Bluetooth como IP");
        await ExchangeAsync(false, check);
        await ExchangeAsync(true, check);
    }

    private static async Task ExchangeAsync(bool changeProfile, Action<bool, string> check)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var channels = Enumerable.Range(0, 1800).Select(i => new RemoteChannel(i.ToString(),
            $"Canal {i} · {new string('x', 50)}")).ToArray();
        var state = new RemoteControlState("Telefone", "TV", "Todos", 30, "1", channels);
        var response = new RemoteControlResponse(true, "", state);
        var peer = new RemoteDiscoveryResponse(RemoteControlProtocol.Magic, RemoteControlProtocol.Version,
            "Telefone", 0, "current-token", ProfileKey: "initial-profile");
        var commands = new List<RemoteControlRequest>();
        Task<RemoteControlResponse> Execute(RemoteControlRequest request, string profile)
        { commands.Add(request); return Task.FromResult(response); }
        async Task ServeAsync()
        {
            using var server = await listener.AcceptTcpClientAsync(timeout.Token);
            using var stream = server.GetStream();
            await BluetoothRemoteProtocol.ServeAsync(stream, stream, () => peer, Execute, timeout.Token);
            if (changeProfile) peer = peer with { ProfileKey = "new-profile" };
            await BluetoothRemoteProtocol.ServeAsync(stream, stream, () => peer, Execute, timeout.Token);
        }
        var serving = ServeAsync();
        using var client = new TcpClient();
        await client.ConnectAsync((IPEndPoint)listener.LocalEndpoint, timeout.Token);
        using var duplex = client.GetStream();
        var profile = await WearBluetoothProtocol.DiscoverProfileAsync(duplex, duplex, timeout.Token);
        RemoteControlResponse? received = null;
        var rejected = false;
        try { received = await WearBluetoothProtocol.SendAsync(duplex, duplex, profile, "state", "", timeout.Token, _ => { }); }
        catch (IOException ex) when (changeProfile) { rejected = ex.Message.Contains("perfil mudou"); }
        await serving;
        if (changeProfile)
            check(rejected && commands.Count == 0,
                "Wear Data Layer recusa executar comandos se o perfil muda entre bootstrap e pedido no mesmo canal");
        else
        {
            check(received?.State?.Channels.Count == channels.Length &&
                JsonSerializer.SerializeToUtf8Bytes(response, WearJsonContext.Default.RemoteControlResponse).Length > 100 * 1024,
                "Wear Data Layer transporta listas acima de 100 KB num canal duplex, sem o limite de MessageClient");
            check(commands.Count == 1 && commands[0].Token == "current-token" && commands[0].Command == "state",
                "Wear Data Layer negocia o perfil e autentica um único comando no mesmo canal");
        }
    }
}
