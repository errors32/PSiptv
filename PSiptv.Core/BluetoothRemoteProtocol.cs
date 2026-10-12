using System.Text;
using System.Text.Json;
using System.Security.Cryptography;

namespace PSiptv.Core;

/// <summary>Authenticated command exchanges that can share a secure RFCOMM connection.</summary>
public static class BluetoothRemoteProtocol
{
    public const string ServiceUuid = "d321875d-72bc-4374-bf59-6b8a84e88c21";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static bool Matches(RemoteDiscoveryRequest request, RemoteDiscoveryResponse response) =>
        request.Magic == RemoteControlProtocol.Magic && request.Version == RemoteControlProtocol.Version &&
        response.Magic == request.Magic && response.Version == request.Version &&
        !string.IsNullOrWhiteSpace(request.ProfileKey) && request.ProfileKey == response.ProfileKey &&
        response.Token.Length > 0;

    public static async Task<RemoteControlResponse> SendAsync(Stream input, Stream output,
        RemoteDiscoveryRequest hello, string command, string value, CancellationToken token,
        Action<RemoteDiscoveryResponse>? observePeer = null)
    {
        using var reader = new StreamReader(input, Encoding.UTF8, false, 4096, true);
        using var writer = new StreamWriter(output, new UTF8Encoding(false), 4096, true) { AutoFlush = true };
        await WriteAsync(writer, hello, token);
        var peer = await ReadAsync<RemoteDiscoveryResponse>(reader, 64 * 1024, token);
        if (!Matches(hello, peer))
            throw new IOException("Abra a mesma lista e o mesmo perfil nos dois equipamentos.");
        observePeer?.Invoke(peer);
        await WriteAsync(writer, new RemoteControlRequest(peer.Token, command, value), token);
        return await ReadAsync<RemoteControlResponse>(reader, 2 * 1024 * 1024, token);
    }

    public static async Task ServeAsync(Stream input, Stream output,
        Func<RemoteDiscoveryResponse> discover,
        Func<RemoteControlRequest, string, Task<RemoteControlResponse>> execute, CancellationToken token)
    {
        using var reader = new StreamReader(input, Encoding.UTF8, false, 4096, true);
        using var writer = new StreamWriter(output, new UTF8Encoding(false), 4096, true) { AutoFlush = true };
        var hello = await ReadAsync<RemoteDiscoveryRequest>(reader, 64 * 1024, token);
        var peer = discover();
        if (!Matches(hello, peer))
        {
            await WriteAsync(writer, peer with { Token = "" }, token);
            return;
        }
        await WriteAsync(writer, peer, token);
        var request = await ReadAsync<RemoteControlRequest>(reader, 64 * 1024, token);
        // The executor checks the current profile again before dispatching to the player.
        RemoteControlResponse response;
        if (request.Token is null || !CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(request.Token), Encoding.UTF8.GetBytes(peer.Token)))
            response = new(false, "Ligação expirada. Procure novamente o dispositivo.", null);
        else
        {
            try { response = await execute(request, hello.ProfileKey); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            { response = new(false, ex.Message, null); }
        }
        await WriteAsync(writer, response, token);
    }

    private static async Task WriteAsync<T>(StreamWriter writer, T value, CancellationToken token) =>
        await writer.WriteLineAsync(JsonSerializer.Serialize(value, Json).AsMemory(), token);

    private static async Task<T> ReadAsync<T>(StreamReader reader, int limit, CancellationToken token)
    {
        var text = new StringBuilder();
        var buffer = new char[1];
        while (await reader.ReadAsync(buffer.AsMemory(), token) > 0)
        {
            if (buffer[0] == '\n')
                return JsonSerializer.Deserialize<T>(text.ToString(), Json) ?? throw new IOException("Resposta Bluetooth inválida.");
            if (text.Length >= limit) throw new IOException("Mensagem Bluetooth demasiado grande.");
            text.Append(buffer[0]);
        }
        throw new IOException("A ligação Bluetooth foi interrompida.");
    }
}
