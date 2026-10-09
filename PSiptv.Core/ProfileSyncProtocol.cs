using System.Buffers.Binary;
using System.Text.Json;

namespace PSiptv.Core;

public sealed record ProfileSyncMessage(string Magic, int Version, string ProfileKey, FavoriteSnapshot? Snapshot = null);

/// <summary>Bounded frames and a two-way exchange, independent of the underlying transport.</summary>
public static class ProfileSyncProtocol
{
    public const string Magic = "PSiptv-PROFILE-SYNC";
    public const int Version = 1;
    public const int MaximumFrameSize = 8 * 1024 * 1024;

    public static async Task WriteAsync(Stream stream, ProfileSyncMessage message, CancellationToken token)
    {
        var data = JsonSerializer.SerializeToUtf8Bytes(message);
        if (data.Length > MaximumFrameSize) throw new InvalidOperationException("Os dados de sincronização excedem o limite de 8 MB.");
        var header = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(header, data.Length);
        await stream.WriteAsync(header, token);
        await stream.WriteAsync(data, token);
        await stream.FlushAsync(token);
    }

    public static async Task<ProfileSyncMessage> ReadAsync(Stream stream, string profileKey, CancellationToken token)
    {
        var header = new byte[4];
        await stream.ReadExactlyAsync(header, token);
        var length = BinaryPrimitives.ReadInt32BigEndian(header);
        if (length <= 0 || length > MaximumFrameSize) throw new InvalidOperationException("Mensagem de sincronização inválida.");
        var data = new byte[length];
        await stream.ReadExactlyAsync(data, token);
        var message = JsonSerializer.Deserialize<ProfileSyncMessage>(data);
        if (string.IsNullOrWhiteSpace(profileKey) || message is null || message.Magic != Magic ||
            message.Version != Version || message.ProfileKey != profileKey)
            throw new InvalidOperationException("Os equipamentos devem utilizar a mesma lista e o mesmo nome de perfil.");
        if (message.Snapshot is { } snapshot) Validate(snapshot);
        return message;
    }

    public static void Validate(FavoriteSnapshot snapshot)
    {
        if (snapshot.Entries is null || snapshot.HeardEpisodes is null ||
            snapshot.Entries.Any(e => e is null || e.Item is null || string.IsNullOrEmpty(e.Key)) ||
            snapshot.Entries.Select(e => e.Key).Distinct().Count() != snapshot.Entries.Count ||
            snapshot.HeardEpisodes.Any(e => e.Value is null) ||
            snapshot.PodcastProgress?.Any(e => !ValidProgress(e.Value)) == true ||
            snapshot.Sync is { } sync && (sync.Favorites is null || sync.Heard is null || sync.Progress is null ||
                sync.Favorites.Any(e => e.Value is null || e.Value.Entry is { Item: null } ||
                    e.Value.Entry is { } entry && entry.Key != e.Key) ||
                sync.Heard.Any(e => e.Value is null) ||
                sync.Progress.Any(e => e.Value is null || e.Value.Entry is { } entry && !ValidProgress(entry))))
            throw new InvalidOperationException("Os dados de sincronização são inválidos.");
    }

    private static bool ValidProgress(PodcastPlaybackEntry? entry) => entry is not null && entry.Item is not null &&
        double.IsFinite(entry.Position) && double.IsFinite(entry.Duration) && entry.Position >= 0 && entry.Duration >= 0;

    public static async Task ExchangeAsync(Stream input, Stream output, string profileKey, bool server,
        Func<Task<FavoriteSnapshot>> readLocal, Func<FavoriteSnapshot, Task> merge, CancellationToken token)
    {
        if (server)
        {
            var hello = await ReadAsync(input, profileKey, token);
            if (hello.Snapshot is not null) throw new InvalidOperationException("Pedido de sincronização inválido.");
            await WriteAsync(output, new(Magic, Version, profileKey, await readLocal()), token);
            var incoming = await ReadAsync(input, profileKey, token);
            await merge(incoming.Snapshot ?? throw new InvalidOperationException("Faltam dados de sincronização."));
            await WriteAsync(output, new(Magic, Version, profileKey, await readLocal()), token);
        }
        else
        {
            await WriteAsync(output, new(Magic, Version, profileKey), token);
            var incoming = await ReadAsync(input, profileKey, token);
            await merge(incoming.Snapshot ?? throw new InvalidOperationException("Faltam dados de sincronização."));
            await WriteAsync(output, new(Magic, Version, profileKey, await readLocal()), token);
            var confirmed = await ReadAsync(input, profileKey, token);
            await merge(confirmed.Snapshot ?? throw new InvalidOperationException("Faltam dados de sincronização."));
        }
    }
}
