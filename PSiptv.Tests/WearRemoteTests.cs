using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using PSiptv.Core;
using PSiptv.Wear;

internal static class WearRemoteTests
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        var hello = new RemoteDiscoveryRequest(RemoteControlProtocol.Magic, RemoteControlProtocol.Version);
        var helloJson = JsonSerializer.Serialize(hello, WearJsonContext.Default.RemoteDiscoveryRequest);
        check(helloJson.Contains("\"profileKey\":\"\"") && helloJson.Contains("\"magic\":\"PSiptv-REMOTE\""),
            "Wear anuncia descoberta compatível com o protocolo LAN atual sem reflexão");
        var legacy = JsonSerializer.Deserialize(
            "{\"magic\":\"PSiptv-REMOTE\",\"version\":2,\"deviceName\":\"TV\",\"port\":45873,\"token\":\"secret\"}",
            WearJsonContext.Default.RemoteDiscoveryResponse);
        check(legacy is { IsActive: true, ProfileKey: "" }, "Wear preserva os valores predefinidos dos recetores antigos");

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var device = new RemoteDevice("TV", IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port, "secret");
        var client = new WearRemoteClient();
        var commands = new List<RemoteControlRequest>();
        async Task ServeAsync()
        {
            foreach (var command in new[] { "state", "activate", "volume", "channel", "rejected" })
            {
                using var peer = await listener.AcceptTcpClientAsync(timeout.Token);
                await using var stream = peer.GetStream();
                using var reader = new StreamReader(stream, Encoding.UTF8, false, 4096, true);
                using var writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, true) { AutoFlush = true };
                var request = JsonSerializer.Deserialize((await reader.ReadLineAsync(timeout.Token))!,
                    WearJsonContext.Default.RemoteControlRequest)!;
                commands.Add(request);
                var state = new RemoteControlState("TV", "Favoritos", "Notícias", 25, "rtp1", [new("rtp1", "RTP 1")],
                    IsActive: command != "state");
                var response = new RemoteControlResponse(command != "rejected", command == "rejected" ? "Ligação expirada." : "", state);
                await writer.WriteLineAsync(JsonSerializer.Serialize(response, WearJsonContext.Default.RemoteControlResponse));
            }
        }
        var serving = ServeAsync();
        var initial = await client.SendAsync(device, "state", "", timeout.Token);
        var active = await client.SendAsync(device, "activate", "", timeout.Token);
        var volume = await client.SendAsync(device, "volume", "25", timeout.Token);
        var channel = await client.SendAsync(device, "channel", "rtp1", timeout.Token);
        var rejected = false;
        try { await client.SendAsync(device, "rejected", "", timeout.Token); }
        catch (IOException ex) { rejected = ex.Message == "Ligação expirada."; }
        await serving;
        check(!initial.IsActive && active.IsActive && channel.Channels.Single().Name == "RTP 1" && volume.Volume == 25,
            "Wear consulta estado, ativa recetor e recebe canais e volume por TCP");
        check(commands.Select(request => request.Command).SequenceEqual(["state", "activate", "volume", "channel", "rejected"]) &&
            commands.All(request => request.Token == "secret") && commands[2].Value == "25" && commands[3].Value == "rtp1",
            "Wear envia token e valores corretos em cada comando");
        check(rejected, "Wear apresenta erros do recetor sem aceitar um estado rejeitado");

        var shortTimeoutClient = new WearRemoteClient(TimeSpan.FromMilliseconds(150));
        var stalled = shortTimeoutClient.SendAsync(device, "state", "", timeout.Token);
        using var silentPeer = await listener.AcceptTcpClientAsync(timeout.Token);
        var timedOut = false;
        try { await stalled; }
        catch (IOException ex) { timedOut = ex.Message.Contains("Wi-Fi"); }
        check(timedOut, "Wear limita o tempo de espera quando um recetor aceita mas não responde");

        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        var cancelled = client.SendAsync(device, "state", "", cancel.Token);
        using var cancellingPeer = await listener.AcceptTcpClientAsync(timeout.Token);
        cancel.Cancel();
        var stopped = false;
        try { await cancelled; }
        catch (OperationCanceledException) { stopped = true; }
        check(stopped, "Wear cancela pedidos pendentes ao sair do ecrã sem os confundir com falhas de Wi-Fi");
    }
}
