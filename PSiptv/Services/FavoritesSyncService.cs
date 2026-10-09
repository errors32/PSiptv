using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using PSiptv.Core;

namespace PSiptv.Services;

/// <summary>One pull per application launch, after the first playlist has been unlocked.</summary>
public static class FavoritesSyncService
{
    private static int attempted;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly SemaphoreSlim gate = new(1);
    public static string Status => Preferences.Default.Get("podcast-sync-status." +
        (AppServices.ActiveAccount is { } account ? UserProfileService.Scope(account.Id) : ""),
        LanguageService.Text("Ainda não foi efetuada uma sincronização."));

    public static async Task OnStartupAsync()
    {
        if (AppServices.ActiveAccount is not { } account || Interlocked.Exchange(ref attempted, 1) != 0) return;
        await SynchronizeAsync();
    }

    public static async Task SynchronizeAsync()
    {
        if (AppServices.ActiveAccount is not { } account || !await gate.WaitAsync(0)) return;
        var session = AppServices.SessionVersion;
        var profileId = UserProfileService.Active.Id;
        var profileKey = RemoteControlService.CurrentProfileKey;
        var scope = UserProfileService.Scope(account.Id, profileId);
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(7));
            var devices = await new LanRemoteClient().DiscoverAsync(profileKey, timeout.Token);
            var snapshots = await Task.WhenAll(devices.Take(16).Select(async device =>
            {
                try { return await ReadAsync(device, profileKey, timeout.Token); }
                catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or JsonException)
                { System.Diagnostics.Debug.WriteLine(ex); return null; }
            }));
            var available = snapshots.OfType<FavoriteSnapshot>().OrderBy(s => s.ModifiedAt).ToArray();
            if (session != AppServices.SessionVersion || profileKey != RemoteControlService.CurrentProfileKey) return;
            if (available.Length == 0)
                Preferences.Default.Set("podcast-sync-status." + scope, LanguageService.Text("Nenhuma aplicação compatível encontrada na rede."));
            else
            {
                var updated = false;
                foreach (var snapshot in available)
                {
                    if (session != AppServices.SessionVersion || profileKey != RemoteControlService.CurrentProfileKey) return;
                    updated |= await FavoritesService.ImportNewerAsync(account, profileId, snapshot);
                }
                Preferences.Default.Set("podcast-sync-status." + scope,
                    DateTimeOffset.Now.ToString("dd/MM/yyyy HH:mm") + " · " +
                    LanguageService.Text(updated ? "Favoritos e progresso atualizados." : "Verificado: os dados locais estão atualizados."));
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
            Preferences.Default.Set("podcast-sync-status." + scope, LanguageService.Text("Não foi possível sincronizar. Tente novamente."));
        }
        finally { gate.Release(); }
    }

    private static async Task<FavoriteSnapshot?> ReadAsync(LanRemoteDevice device, string profileKey, CancellationToken token)
    {
        using var tcp = new TcpClient(AddressFamily.InterNetwork);
        await tcp.ConnectAsync(device.Address, device.Port, token);
        await using var stream = tcp.GetStream();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, true) { AutoFlush = true };
        using var reader = new StreamReader(stream, Encoding.UTF8, false, 4096, true);
        await writer.WriteLineAsync(JsonSerializer.Serialize(new RemoteControlRequest(device.Token, "favorites-snapshot", profileKey), Json).AsMemory(), token);
        // Bound the reply rather than allowing a LAN peer to allocate an unlimited line.
        var reply = new StringBuilder();
        var buffer = new char[4096];
        while (reply.Length <= 8 * 1024 * 1024)
        {
            var count = await reader.ReadAsync(buffer.AsMemory(), token);
            if (count == 0) break;
            reply.Append(buffer, 0, count);
            if (Array.IndexOf(buffer, '\n', 0, count) >= 0) break;
        }
        if (reply.Length > 8 * 1024 * 1024) throw new IOException("Resposta de favoritos demasiado grande.");
        var response = JsonSerializer.Deserialize<RemoteControlResponse>(reply.ToString(), Json);
        var snapshot = response?.Favorites;
        return response is { Success: true } && response.ProfileKey == profileKey &&
            snapshot?.Entries is not null && snapshot.HeardEpisodes is not null &&
            snapshot.Entries.All(e => e is not null && e.Item is not null) &&
            snapshot.HeardEpisodes.All(e => e.Value is not null) ? snapshot : null;
    }
}
