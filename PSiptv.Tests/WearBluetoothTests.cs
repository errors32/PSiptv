using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using PSiptv.Core;
using PSiptv.Wear;

internal static class WearBluetoothTests
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var peer = new RemoteDiscoveryResponse(RemoteControlProtocol.Magic, RemoteControlProtocol.Version,
            "Telefone", 0, "session-token", ProfileKey: "current-profile", KeepConnection: true);
        var executed = new List<RemoteControlRequest>();
        var profiles = new List<string>();
        Task<RemoteControlResponse> Execute(RemoteControlRequest command, string profile)
        {
            executed.Add(command);
            profiles.Add(profile);
            return Task.FromResult(new RemoteControlResponse(command.Value != "blocked", "Reprodução bloqueada",
                new("Telefone", "Favoritos", "", 30, command.Value, [new("rtp1", "RTP 1")], IsActive: command.Command != "state")));
        }
        async Task ServeAsync()
        {
            // The bootstrap receives the same rejection response as any unmatched
            // profile; no server changes or relaxed authentication are needed.
            using (var server = await listener.AcceptTcpClientAsync(timeout.Token))
            using (var stream = server.GetStream())
                await BluetoothRemoteProtocol.ServeAsync(stream, stream, () => peer, Execute, timeout.Token);
            using (var server = await listener.AcceptTcpClientAsync(timeout.Token))
            using (var stream = server.GetStream())
                for (var i = 0; i < 5; i++)
                    await BluetoothRemoteProtocol.ServeAsync(stream, stream, () => peer, Execute, timeout.Token);
            using (var server = await listener.AcceptTcpClientAsync(timeout.Token))
            using (var stream = server.GetStream())
                await BluetoothRemoteProtocol.ServeAsync(stream, stream, () => peer with { KeepConnection = false }, Execute, timeout.Token);
            using (var server = await listener.AcceptTcpClientAsync(timeout.Token))
            using (var stream = server.GetStream())
                await BluetoothRemoteProtocol.ServeAsync(stream, stream, () => peer with { ProfileKey = "changed-profile" }, Execute, timeout.Token);
        }
        var serving = ServeAsync();
        string profile;
        using (var bootstrap = new TcpClient())
        {
            await bootstrap.ConnectAsync((IPEndPoint)listener.LocalEndpoint, timeout.Token);
            using var stream = bootstrap.GetStream();
            profile = await WearBluetoothProtocol.DiscoverProfileAsync(stream, stream, timeout.Token);
            check(profile == "current-profile" && executed.Count == 0,
                "Wear Bluetooth obtém o perfil aberto sem credenciais IPTV nem executar comandos no bootstrap");
        }
        using (var client = new TcpClient())
        {
            await client.ConnectAsync((IPEndPoint)listener.LocalEndpoint, timeout.Token);
            using var stream = client.GetStream();
            var keep = false;
            var state = await WearBluetoothProtocol.SendAsync(stream, stream, profile, "state", "", timeout.Token, value => keep = value);
            var active = await WearBluetoothProtocol.SendAsync(stream, stream, profile, "activate", "", timeout.Token, _ => { });
            var volume = await WearBluetoothProtocol.SendAsync(stream, stream, profile, "volume", "30", timeout.Token, _ => { });
            var rejected = await WearBluetoothProtocol.SendAsync(stream, stream, profile, "channel", "blocked", timeout.Token, _ => { });
            var channel = await WearBluetoothProtocol.SendAsync(stream, stream, profile, "channel", "rtp1", timeout.Token, _ => { });
            check(keep && state.State is { IsActive: false } && active.State is { IsActive: true } && volume.State?.Volume == 30 &&
                channel.State?.CurrentChannelId == "rtp1", "Wear Bluetooth consulta estado, ativa, altera volume e canal numa ligação persistente");
            check(!rejected.Success && channel.Success, "Wear Bluetooth conserva a sessão após um comando rejeitado sem o repetir");
        }
        using (var legacy = new TcpClient())
        {
            await legacy.ConnectAsync((IPEndPoint)listener.LocalEndpoint, timeout.Token);
            using var stream = legacy.GetStream();
            var keep = true;
            await WearBluetoothProtocol.SendAsync(stream, stream, profile, "state", "", timeout.Token, value => keep = value);
            check(!keep, "Wear Bluetooth reconhece recetores antigos que fecham a ligação após cada comando");
        }
        using (var changed = new TcpClient())
        {
            await changed.ConnectAsync((IPEndPoint)listener.LocalEndpoint, timeout.Token);
            using var stream = changed.GetStream();
            var rejected = false;
            try { await WearBluetoothProtocol.SendAsync(stream, stream, profile, "channel", "other", timeout.Token, _ => { }); }
            catch (IOException ex) { rejected = ex.Message.Contains("perfil mudou"); }
            check(rejected, "Wear Bluetooth rejeita um perfil alterado antes de transmitir o comando");
        }
        await serving;
        check(executed.Count == 6 && executed.All(request => request.Token == "session-token") &&
            profiles.All(value => value == "current-profile") &&
            executed.Select(request => request.Command).SequenceEqual(["state", "activate", "volume", "channel", "channel", "state"]),
            "Wear Bluetooth autentica com o token atual e executa cada comando uma única vez no perfil validado");

        using var output = new MemoryStream();
        using var noProfile = Input(peer with { ProfileKey = "" });
        var locked = false;
        try { await WearBluetoothProtocol.DiscoverProfileAsync(noProfile, output, timeout.Token); }
        catch (IOException ex) { locked = ex.Message.Contains("Abra uma lista"); }
        check(locked, "Wear Bluetooth explica que é necessário abrir uma lista no recetor");

        using var incompatible = Input(peer with { Version = 999 });
        var wrongVersion = false;
        try { await WearBluetoothProtocol.DiscoverProfileAsync(incompatible, output, timeout.Token); }
        catch (IOException ex) { wrongVersion = ex.Message.Contains("Atualize"); }
        check(wrongVersion, "Wear Bluetooth rejeita versões de protocolo incompatíveis");

        using var oversized = new MemoryStream(Encoding.UTF8.GetBytes(new string('x', 64 * 1024 + 1) + "\n"));
        var bounded = false;
        try { await WearBluetoothProtocol.DiscoverProfileAsync(oversized, output, timeout.Token); }
        catch (IOException ex) { bounded = ex.Message.Contains("demasiado grande"); }
        check(bounded, "Wear Bluetooth limita o tamanho das mensagens recebidas");

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        using var input = Input(peer);
        var stopped = false;
        try { await WearBluetoothProtocol.DiscoverProfileAsync(input, output, cancelled.Token); }
        catch (OperationCanceledException) { stopped = true; }
        check(stopped, "Wear Bluetooth cancela a negociação ao sair ou trocar de ligação");
    }

    private static MemoryStream Input(RemoteDiscoveryResponse response) => new(Encoding.UTF8.GetBytes(
        JsonSerializer.Serialize(response, WearJsonContext.Default.RemoteDiscoveryResponse) + "\n"));
}
