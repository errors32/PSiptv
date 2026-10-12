using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using PSiptv.Core;

internal static class BluetoothRemoteTests
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        var hello = new RemoteDiscoveryRequest(RemoteControlProtocol.Magic, RemoteControlProtocol.Version, "profile", "phone");
        var peer = new RemoteDiscoveryResponse(RemoteControlProtocol.Magic, RemoteControlProtocol.Version,
            "Tablet", 0, "secret", "tablet", "profile");
        check(BluetoothRemoteProtocol.Matches(hello, peer), "Bluetooth aceita a mesma lista e perfil");
        check(!BluetoothRemoteProtocol.Matches(hello with { ProfileKey = "other" }, peer), "Bluetooth rejeita perfis diferentes");
        check(!BluetoothRemoteProtocol.Matches(hello with { ProfileKey = "" }, peer with { ProfileKey = "" }), "Bluetooth rejeita listas fechadas");
        check(!BluetoothRemoteProtocol.Matches(hello with { Version = 1 }, peer), "Bluetooth rejeita versões incompatíveis");

        // Exercise the full duplex stream contract. TCP is only an in-process test carrier;
        // production passes the Android RFCOMM input/output streams directly.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var client = new TcpClient();
        await client.ConnectAsync((IPEndPoint)listener.LocalEndpoint, timeout.Token);
        using var server = await listener.AcceptTcpClientAsync(timeout.Token);
        using var serverStream = server.GetStream();
        using var clientStream = client.GetStream();
        RemoteControlRequest? received = null;
        string? receivedProfile = null;
        var serving = BluetoothRemoteProtocol.ServeAsync(serverStream, serverStream, () => peer,
            (request, profile) =>
            {
                received = request; receivedProfile = profile;
                return Task.FromResult(new RemoteControlResponse(true, "",
                    new("Tablet", "Favoritos", "", 35, request.Value, [new(request.Value, "RTP 1")])));
            }, timeout.Token);
        var response = await BluetoothRemoteProtocol.SendAsync(clientStream, clientStream, hello,
            "channel", "rtp1", timeout.Token);
        await serving;
        check(received is { Token: "secret", Command: "channel", Value: "rtp1" } && receivedProfile == "profile" &&
            response.State?.CurrentChannelId == "rtp1", "Bluetooth autentica e transmite troca de canal e estado");

        bool executed = false;
        using var wrongProfile = Input(hello with { ProfileKey = "other" });
        using var rejected = new MemoryStream();
        await BluetoothRemoteProtocol.ServeAsync(wrongProfile, rejected, () => peer,
            (_, _) => { executed = true; throw new Exception("Must not execute"); }, timeout.Token);
        rejected.Position = 0;
        using var rejectionReader = new StreamReader(rejected);
        var rejection = JsonSerializer.Deserialize<RemoteDiscoveryResponse>(await rejectionReader.ReadToEndAsync(), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        check(!executed && rejection?.Token == "", "Bluetooth não divulga token nem executa comandos de outro perfil");

        using var invalidToken = new MemoryStream(Input(hello).ToArray().Concat(Input(new RemoteControlRequest("wrong", "channel", "rtp1")).ToArray()).ToArray());
        using var invalidOutput = new MemoryStream();
        await BluetoothRemoteProtocol.ServeAsync(invalidToken, invalidOutput, () => peer,
            (_, _) => { executed = true; throw new Exception(); }, timeout.Token);
        check(!executed && Encoding.UTF8.GetString(invalidOutput.ToArray()).Contains("\"success\":false"),
            "Bluetooth rejeita tokens inválidos antes de executar o comando");

        using var deniedCommand = new MemoryStream(Input(hello).ToArray().Concat(Input(new RemoteControlRequest("secret", "channel", "blocked")).ToArray()).ToArray());
        using var deniedOutput = new MemoryStream();
        await BluetoothRemoteProtocol.ServeAsync(deniedCommand, deniedOutput, () => peer,
            (_, _) => throw new InvalidOperationException("Reprodução bloqueada"), timeout.Token);
        check(Encoding.UTF8.GetString(deniedOutput.ToArray()).Contains("\"success\":false"),
            "Bluetooth devolve falhas do leitor ao comando remoto");

        using var truncated = new MemoryStream(Encoding.UTF8.GetBytes("{\"magic\":"));
        using var output = new MemoryStream();
        var interrupted = false;
        try { await BluetoothRemoteProtocol.ServeAsync(truncated, output, () => peer, (_, _) => throw new Exception(), timeout.Token); }
        catch (IOException) { interrupted = true; }
        check(interrupted, "Bluetooth deteta ligação interrompida durante a mensagem");

        using var oversized = new MemoryStream(Encoding.UTF8.GetBytes(new string('x', 64 * 1024 + 1) + "\n"));
        var bounded = false;
        try { await BluetoothRemoteProtocol.ServeAsync(oversized, output, () => peer, (_, _) => throw new Exception(), timeout.Token); }
        catch (IOException) { bounded = true; }
        check(bounded, "Bluetooth limita o tamanho das mensagens recebidas");

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        using var input = Input(hello);
        var stopped = false;
        try { await BluetoothRemoteProtocol.ServeAsync(input, output, () => peer, (_, _) => throw new Exception(), cancelled.Token); }
        catch (OperationCanceledException) { stopped = true; }
        check(stopped, "Bluetooth respeita o cancelamento ao fechar o comando remoto");
    }

    private static MemoryStream Input<T>(T value) => new(Encoding.UTF8.GetBytes(
        JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web)) + "\n"));
}
