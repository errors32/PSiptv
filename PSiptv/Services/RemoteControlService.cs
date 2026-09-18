using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using PSiptv.Core;

namespace PSiptv.Services;

/// <summary>Exposes the foreground player to a PSiptv Wear remote on the local network.</summary>
public static class RemoteControlService
{
    private static readonly object Sync = new();
    private static CancellationTokenSource? lifetime;
    private static UdpClient? discovery;
    private static TcpListener? control;
    private static Func<RemoteControlState>? readState;
    private static Func<RemoteControlRequest, Task<RemoteControlState>>? execute;
    private static string token = "";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static void Start(Func<RemoteControlState> stateReader,
        Func<RemoteControlRequest, Task<RemoteControlState>> commandExecutor)
    {
#if ANDROID
        lock (Sync)
        {
            readState = stateReader;
            execute = commandExecutor;
            if (lifetime is not null) return;
            lifetime = new CancellationTokenSource();
            token = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16));
            _ = RunDiscoveryAsync(lifetime.Token);
            _ = RunControlAsync(lifetime.Token);
        }
#endif
    }

    public static void Stop()
    {
        lock (Sync)
        {
            lifetime?.Cancel();
            lifetime?.Dispose();
            lifetime = null;
            discovery?.Dispose(); discovery = null;
            control?.Stop(); control = null;
            token = "";
        }
    }

#if ANDROID
    private static async Task RunDiscoveryAsync(CancellationToken cancellationToken)
    {
        try
        {
            var socket = new UdpClient(AddressFamily.InterNetwork);
            socket.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            socket.Client.Bind(new IPEndPoint(IPAddress.Any, RemoteControlProtocol.DiscoveryPort));
            discovery = socket;
            while (!cancellationToken.IsCancellationRequested)
            {
                var packet = await socket.ReceiveAsync(cancellationToken);
                RemoteDiscoveryRequest? request;
                try { request = JsonSerializer.Deserialize<RemoteDiscoveryRequest>(packet.Buffer, Json); }
                catch (JsonException) { continue; }
                if (request?.Magic != RemoteControlProtocol.Magic || request.Version != RemoteControlProtocol.Version) continue;
                var response = new RemoteDiscoveryResponse(RemoteControlProtocol.Magic,
                    RemoteControlProtocol.Version, DeviceName(), RemoteControlProtocol.ControlPort, token);
                var bytes = JsonSerializer.SerializeToUtf8Bytes(response, Json);
                await socket.SendAsync(bytes, packet.RemoteEndPoint, cancellationToken);
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (SocketException) { }
    }

    private static async Task RunControlAsync(CancellationToken cancellationToken)
    {
        try
        {
            var listener = new TcpListener(IPAddress.Any, RemoteControlProtocol.ControlPort);
            listener.Start(); control = listener;
            while (!cancellationToken.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(cancellationToken);
                _ = HandleClientAsync(client, cancellationToken);
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (SocketException) { }
    }

    private static async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using (client)
        using (var stream = client.GetStream())
        using (var reader = new StreamReader(stream, Encoding.UTF8, false, 4096, true))
        using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, true) { AutoFlush = true })
        {
            RemoteControlResponse response;
            try
            {
                var line = await reader.ReadLineAsync(cancellationToken);
                var request = line is null ? null : JsonSerializer.Deserialize<RemoteControlRequest>(line, Json);
                if (request is null || !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                        Encoding.UTF8.GetBytes(request.Token), Encoding.UTF8.GetBytes(token)))
                    response = new(false, "Ligação expirada. Procure novamente o dispositivo.", null);
                else if (request.Command == "state")
                    response = new(true, "", await MainThread.InvokeOnMainThreadAsync(() => readState?.Invoke()));
                else if (execute is null)
                    response = new(false, "O controlo remoto não está disponível.", null);
                else
                    response = new(true, "", await MainThread.InvokeOnMainThreadAsync(() => execute(request)));
            }
            catch (Exception ex) { response = new(false, ex.Message, null); }
            await writer.WriteLineAsync(JsonSerializer.Serialize(response, Json));
        }
    }

    private static string DeviceName()
    {
        var type = DeviceInfo.Idiom == DeviceIdiom.TV ? "TV" : DeviceInfo.Idiom == DeviceIdiom.Tablet ? "Tablet" : "Telemóvel";
        return $"PSiptv · {type} · {DeviceInfo.Name}";
    }
#endif
}
