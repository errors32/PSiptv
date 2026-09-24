using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PSiptv.Core;

namespace PSiptv.Services;

/// <summary>Advertises this player and keeps one active receiver per profile on the local network.</summary>
public static class RemoteControlService
{
    private const string DeviceIdKey = "lanRemote.deviceId.v1";
    private static readonly object Sync = new();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static CancellationTokenSource? lifetime;
    private static UdpClient? discovery;
    private static TcpListener? control;
    private static Func<RemoteControlState>? readState;
    private static Func<RemoteControlRequest, Task<RemoteControlState>>? execute;
    private static string token = "";
    private static string scope = "";
    private static string leaderDeviceId = "";
    private static long leaderEpoch;
    private static DateTimeOffset leaderLastSeen;

    public static string DeviceId { get; } = GetOrCreateDeviceId();
    public static bool IsActive
    {
        get { EnsureScope(); return scope.Length > 0 && leaderDeviceId == DeviceId; }
    }
    public static string CurrentProfileKey => ProfileKey();
    public static event Action? ActiveDeviceChanged;

    public static void Start(Func<RemoteControlState> stateReader,
        Func<RemoteControlRequest, Task<RemoteControlState>> commandExecutor)
    {
        lock (Sync)
        {
            readState = stateReader;
            execute = commandExecutor;
            EnsureScope();
            if (lifetime is not null) return;
            lifetime = new CancellationTokenSource();
            token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
            _ = RunDiscoveryAsync(lifetime.Token);
            _ = RunControlAsync(lifetime.Token);
            _ = RunCoordinationAsync(lifetime.Token);
        }
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

    public static void ClaimActive()
    {
        EnsureScope();
        if (scope.Length == 0) return;
        var epoch = Math.Max(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), leaderEpoch + 1);
        ApplyLeader(DeviceId, epoch);
    }

    public static void ObserveLeader(string deviceId, long epoch) => ApplyLeader(deviceId, epoch);

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
                EnsureScope();
                if (request.ProfileKey.Length > 0 && request.ProfileKey != scope) continue;
                if (request.ProfileKey == scope && request.DeviceId != DeviceId)
                {
                    ApplyLeader(request.LeaderDeviceId, request.LeaderEpoch);
                    if (request.DeviceId == leaderDeviceId || request.LeaderDeviceId == leaderDeviceId)
                        leaderLastSeen = DateTimeOffset.UtcNow;
                }
                var bytes = JsonSerializer.SerializeToUtf8Bytes(DiscoveryResponse(), Json);
                await socket.SendAsync(bytes, packet.RemoteEndPoint, cancellationToken);
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (SocketException) { }
    }

    private static async Task RunCoordinationAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                EnsureScope();
                if (scope.Length > 0)
                {
                    if (leaderDeviceId != DeviceId && DateTimeOffset.UtcNow - leaderLastSeen > TimeSpan.FromSeconds(15))
                        ClaimActive();
                    await ProbePeersAsync(cancellationToken);
                }
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
        }
    }

    private static async Task ProbePeersAsync(CancellationToken cancellationToken)
    {
        using var udp = new UdpClient(AddressFamily.InterNetwork) { EnableBroadcast = true };
        udp.Client.Bind(new IPEndPoint(IPAddress.Any, 0));
        var request = JsonSerializer.SerializeToUtf8Bytes(new RemoteDiscoveryRequest(RemoteControlProtocol.Magic,
            RemoteControlProtocol.Version, scope, DeviceId, leaderDeviceId, leaderEpoch), Json);
        foreach (var address in BroadcastAddresses().Distinct())
            try { await udp.SendAsync(request, new IPEndPoint(address, RemoteControlProtocol.DiscoveryPort), cancellationToken); }
            catch (SocketException) { }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(900));
        try
        {
            while (true)
            {
                var packet = await udp.ReceiveAsync(timeout.Token);
                RemoteDiscoveryResponse? response;
                try { response = JsonSerializer.Deserialize<RemoteDiscoveryResponse>(packet.Buffer, Json); }
                catch (JsonException) { continue; }
                if (response?.Magic != RemoteControlProtocol.Magic || response.Version != RemoteControlProtocol.Version ||
                    response.ProfileKey != scope || response.DeviceId == DeviceId) continue;
                if (response.LeaderDeviceId == leaderDeviceId) leaderLastSeen = DateTimeOffset.UtcNow;
                ApplyLeader(response.LeaderDeviceId, response.LeaderEpoch);
                if (response.DeviceId == leaderDeviceId || response.LeaderDeviceId == leaderDeviceId)
                    leaderLastSeen = DateTimeOffset.UtcNow;
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
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
                if (request is null || !CryptographicOperations.FixedTimeEquals(
                        Encoding.UTF8.GetBytes(request.Token), Encoding.UTF8.GetBytes(token)))
                    response = new(false, "Ligação expirada. Procure novamente o dispositivo.", null);
                else if (request.Command == "activate")
                {
                    ClaimActive();
                    response = new(true, "", await MainThread.InvokeOnMainThreadAsync(ReadState));
                }
                else if (request.Command == "state")
                    response = new(true, "", await MainThread.InvokeOnMainThreadAsync(ReadState));
                else if (!IsActive)
                    response = new(false, "Este dispositivo não está ativo. Selecione-o primeiro no comando remoto.",
                        await MainThread.InvokeOnMainThreadAsync(ReadState));
                else if (execute is null)
                    response = new(false, "O controlo remoto não está disponível.", null);
                else
                    response = new(true, "", Enrich(await MainThread.InvokeOnMainThreadAsync(() => execute(request))));
            }
            catch (Exception ex) { response = new(false, ex.Message, null); }
            await writer.WriteLineAsync(JsonSerializer.Serialize(response, Json));
        }
    }

    private static RemoteControlState ReadState() => Enrich(readState?.Invoke() ??
        new RemoteControlState(DeviceName(), "", "", 0, "", []));

    private static RemoteControlState Enrich(RemoteControlState state)
    {
        EnsureScope();
        return state with { DeviceName = DeviceName(), IsActive = IsActive, ProfileName = UserProfileService.Active.Name,
            DeviceId = DeviceId, LeaderDeviceId = leaderDeviceId, LeaderEpoch = leaderEpoch };
    }

    private static RemoteDiscoveryResponse DiscoveryResponse() => new(RemoteControlProtocol.Magic,
        RemoteControlProtocol.Version, DeviceName(), RemoteControlProtocol.ControlPort, token, DeviceId, scope,
        UserProfileService.Active.Name, IsActive, leaderDeviceId, leaderEpoch);

    private static void EnsureScope()
    {
        var current = ProfileKey();
        if (current == scope) return;
        scope = current;
        if (scope.Length == 0) { leaderDeviceId = ""; leaderEpoch = 0; return; }
        leaderDeviceId = Preferences.Default.Get($"lanRemote.leader.{scope}", DeviceId);
        leaderEpoch = Preferences.Default.Get($"lanRemote.epoch.{scope}", 0L);
        leaderLastSeen = DateTimeOffset.UtcNow;
        ActiveDeviceChanged?.Invoke();
    }

    private static void ApplyLeader(string candidateId, long candidateEpoch)
    {
        EnsureScope();
        if (scope.Length == 0) return;
        var previous = IsActive;
        var winner = RemoteLeadership.Resolve(leaderDeviceId, leaderEpoch, candidateId, candidateEpoch);
        if (winner.DeviceId == leaderDeviceId && winner.Epoch == leaderEpoch) return;
        leaderDeviceId = winner.DeviceId; leaderEpoch = winner.Epoch;
        if (leaderDeviceId == DeviceId) leaderLastSeen = DateTimeOffset.UtcNow;
        Preferences.Default.Set($"lanRemote.leader.{scope}", leaderDeviceId);
        Preferences.Default.Set($"lanRemote.epoch.{scope}", leaderEpoch);
        if (previous != IsActive)
        {
            if (previous) MainThread.BeginInvokeOnMainThread(AppServices.SuspendPlayback);
            ActiveDeviceChanged?.Invoke();
        }
    }

    private static string ProfileKey()
    {
        if (AppServices.ActiveAccount is not { } account) return "";
        // Use the account connection and profile name rather than local database ids,
        // so the same manually configured subscription can pair across installations.
        // Only the hash is advertised on the LAN; credentials never leave this process.
        var value = $"{account.Provider}\n{account.Url.Trim().ToUpperInvariant()}\n" +
            $"{account.Username}\n{account.Password}\n{UserProfileService.Active.Name.Trim().ToUpperInvariant()}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..24];
    }

    private static string GetOrCreateDeviceId()
    {
        var id = Preferences.Default.Get(DeviceIdKey, "");
        if (id.Length > 0) return id;
        id = Guid.NewGuid().ToString("N");
        Preferences.Default.Set(DeviceIdKey, id);
        return id;
    }

    private static string DeviceName()
    {
        var type = DeviceInfo.Idiom == DeviceIdiom.TV ? "TV" : DeviceInfo.Idiom == DeviceIdiom.Tablet ? "Tablet" :
            DeviceInfo.Idiom == DeviceIdiom.Phone ? "Telemóvel" : "Computador";
        return $"PSiptv · {type} · {DeviceInfo.Name}";
    }

    internal static IEnumerable<IPAddress> BroadcastAddresses()
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
