using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using PSiptv.Core;

namespace PSiptv.Wear;

internal static class WearBluetoothProtocol
{
    // The existing receiver advertises its current profile hash (without a token)
    // when a hello has no matching profile. Only use this on a secure bonded socket.
    // The watch can then authenticate a new connection without storing IPTV credentials.
    internal static async Task<string> DiscoverProfileAsync(Stream input, Stream output, CancellationToken token)
    {
        using var reader = new StreamReader(input, Encoding.UTF8, false, 4096, true);
        using var writer = new StreamWriter(output, new UTF8Encoding(false), 4096, true) { AutoFlush = true };
        await WriteAsync(writer, Hello(""), WearJsonContext.Default.RemoteDiscoveryRequest, token);
        var peer = await ReadAsync(reader, WearJsonContext.Default.RemoteDiscoveryResponse, 64 * 1024, token);
        if (peer.Magic != RemoteControlProtocol.Magic || peer.Version != RemoteControlProtocol.Version)
            throw new IOException("Atualize o PSiptv no dispositivo selecionado.");
        if (string.IsNullOrWhiteSpace(peer.ProfileKey))
            throw new IOException("Abra uma lista e um perfil no PSiptv do dispositivo selecionado.");
        return peer.ProfileKey;
    }

    internal static async Task<RemoteControlResponse> SendAsync(Stream input, Stream output, string profile,
        string command, string value, CancellationToken token, Action<bool> keepConnection)
    {
        using var reader = new StreamReader(input, Encoding.UTF8, false, 4096, true);
        using var writer = new StreamWriter(output, new UTF8Encoding(false), 4096, true) { AutoFlush = true };
        var hello = Hello(profile);
        await WriteAsync(writer, hello, WearJsonContext.Default.RemoteDiscoveryRequest, token);
        var peer = await ReadAsync(reader, WearJsonContext.Default.RemoteDiscoveryResponse, 64 * 1024, token);
        if (!BluetoothRemoteProtocol.Matches(hello, peer))
            throw new IOException("O perfil mudou. Volte a ligar ao dispositivo.");
        keepConnection(peer.KeepConnection);
        await WriteAsync(writer, new RemoteControlRequest(peer.Token, command, value),
            WearJsonContext.Default.RemoteControlRequest, token);
        return await ReadAsync(reader, WearJsonContext.Default.RemoteControlResponse, 2 * 1024 * 1024, token);
    }

    private static RemoteDiscoveryRequest Hello(string profile) =>
        new(RemoteControlProtocol.Magic, RemoteControlProtocol.Version, profile, "wear-remote");

    private static async Task WriteAsync<T>(StreamWriter writer, T value, JsonTypeInfo<T> type, CancellationToken token) =>
        await writer.WriteLineAsync(JsonSerializer.Serialize(value, type).AsMemory(), token);

    private static async Task<T> ReadAsync<T>(StreamReader reader, JsonTypeInfo<T> type, int limit, CancellationToken token)
    {
        var text = new StringBuilder();
        var buffer = new char[1];
        while (await reader.ReadAsync(buffer.AsMemory(), token) > 0)
        {
            if (buffer[0] == '\n')
                return JsonSerializer.Deserialize(text.ToString(), type) ?? throw new IOException("Resposta Bluetooth inválida.");
            if (text.Length >= limit) throw new IOException("Mensagem Bluetooth demasiado grande.");
            text.Append(buffer[0]);
        }
        throw new IOException("A ligação Bluetooth foi interrompida.");
    }
}
