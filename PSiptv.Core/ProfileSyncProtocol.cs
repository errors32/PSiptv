using System.Buffers.Binary;
using System.Text.Json;

namespace PSiptv.Core;

public enum ProfileSyncError { None, ProfileMismatch, VersionMismatch, InvalidData, StorageFailure }
public sealed class ProfileSyncException(ProfileSyncError error) : InvalidOperationException(error switch
{
    ProfileSyncError.ProfileMismatch => "Os equipamentos devem utilizar a mesma lista, credenciais e nome de perfil.",
    ProfileSyncError.VersionMismatch => "Atualize a aplicação nos dois equipamentos para a mesma versão antes de sincronizar.",
    ProfileSyncError.StorageFailure => "O outro equipamento não conseguiu guardar os dados de sincronização.",
    _ => "Os dados de sincronização são inválidos."
}) { public ProfileSyncError Error { get; } = error; }
public sealed record ProfileSyncMessage(string Magic, int Version, string ProfileKey, FavoriteSnapshot? Snapshot = null,
    bool Acknowledged = false, ProfileSyncError Error = ProfileSyncError.None);

/// <summary>Bounded frames and a two-way exchange, independent of the underlying transport.</summary>
public static class ProfileSyncProtocol
{
    public const string Magic = "PSiptv-PROFILE-SYNC";
    public const int Version = 2;
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
        if (message is null || message.Magic != Magic) throw new ProfileSyncException(ProfileSyncError.InvalidData);
        if (message.Error != ProfileSyncError.None) throw new ProfileSyncException(message.Error);
        if (message.Version != Version) throw new ProfileSyncException(ProfileSyncError.VersionMismatch);
        if (string.IsNullOrWhiteSpace(profileKey) || message.ProfileKey != profileKey)
            throw new ProfileSyncException(ProfileSyncError.ProfileMismatch);
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
            snapshot.ViewingStates?.Any(e => string.IsNullOrWhiteSpace(e.Key) || e.Value is null ||
                !Enum.IsDefined(e.Value.State) || string.IsNullOrWhiteSpace(e.Value.ChangeId)) == true ||
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
            try
            {
                var hello = await ReadAsync(input, profileKey, token);
                if (hello.Snapshot is not null || hello.Acknowledged) throw new ProfileSyncException(ProfileSyncError.InvalidData);
                await WriteAsync(output, new(Magic, Version, profileKey, await readLocal()), token);
                var incoming = await ReadAsync(input, profileKey, token);
                await merge(incoming.Snapshot ?? throw new InvalidOperationException("Faltam dados de sincronização."));
                await WriteAsync(output, new(Magic, Version, profileKey, await readLocal()), token);
                // RFCOMM close can discard buffered output. Keep the socket open until the peer has saved the final frame.
                var confirmation = await ReadAsync(input, profileKey, token);
                if (!confirmation.Acknowledged || confirmation.Snapshot is not null)
                    throw new ProfileSyncException(ProfileSyncError.InvalidData);
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException)
            {
                var error = ex is ProfileSyncException protocol ? protocol.Error :
                    ex is JsonException or InvalidOperationException ? ProfileSyncError.InvalidData : ProfileSyncError.StorageFailure;
                try { await WriteAsync(output, new(Magic, Version, profileKey, Error: error), token); }
                catch (Exception writeError) when (writeError is not OutOfMemoryException) { }
                throw;
            }
        }
        else
        {
            await WriteAsync(output, new(Magic, Version, profileKey), token);
            var incoming = await ReadAsync(input, profileKey, token);
            await merge(incoming.Snapshot ?? throw new InvalidOperationException("Faltam dados de sincronização."));
            await WriteAsync(output, new(Magic, Version, profileKey, await readLocal()), token);
            var confirmed = await ReadAsync(input, profileKey, token);
            await merge(confirmed.Snapshot ?? throw new InvalidOperationException("Faltam dados de sincronização."));
            await WriteAsync(output, new(Magic, Version, profileKey, Acknowledged: true), token);
        }
    }
}
