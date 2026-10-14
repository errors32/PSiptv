using System.Buffers.Binary;
using System.IO.Compression;
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
public enum ProfileSyncPhase { Preparing, Sending, Receiving, Saving, Confirming }
public sealed record ProfileSyncProgress(ProfileSyncPhase Phase, int Bytes = 0, int TotalBytes = 0);

/// <summary>Bounded frames and a two-way exchange, independent of the underlying transport.</summary>
public static class ProfileSyncProtocol
{
    public const string Magic = "PSiptv-PROFILE-SYNC";
    public const int Version = 3;
    public const int MaximumFrameSize = 8 * 1024 * 1024;
    public const int TransferBlockSize = 16 * 1024;

    public static async Task WriteAsync(Stream stream, ProfileSyncMessage message, CancellationToken token,
        Action<ProfileSyncProgress>? progress = null)
    {
        token.ThrowIfCancellationRequested();
        var data = JsonSerializer.SerializeToUtf8Bytes(message);
        if (data.Length > MaximumFrameSize) throw new InvalidOperationException("Os dados de sincronização excedem o limite de 8 MB.");
        var compressed = false;
        // Keep hello/error frames readable by older versions so they receive a useful version-mismatch reply.
        if (message.Version >= 3 && message.Snapshot is not null)
        {
            using var packed = new MemoryStream();
            using (var gzip = new GZipStream(packed, CompressionLevel.Fastest, true))
                await gzip.WriteAsync(data, token).ConfigureAwait(false);
            if (packed.Length < data.Length) { data = packed.ToArray(); compressed = true; }
        }
        var header = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(header, compressed ? -data.Length : data.Length);
        progress?.Invoke(new(ProfileSyncPhase.Sending, 0, data.Length));
        await stream.WriteAsync(header, token).ConfigureAwait(false);
        for (var offset = 0; offset < data.Length;)
        {
            var count = Math.Min(TransferBlockSize, data.Length - offset);
            await stream.WriteAsync(data.AsMemory(offset, count), token).ConfigureAwait(false);
            offset += count;
            progress?.Invoke(new(ProfileSyncPhase.Sending, offset, data.Length));
        }
        await stream.FlushAsync(token).ConfigureAwait(false);
    }

    public static async Task<ProfileSyncMessage> ReadAsync(Stream stream, string profileKey, CancellationToken token,
        Action<ProfileSyncProgress>? progress = null)
    {
        progress?.Invoke(new(ProfileSyncPhase.Receiving));
        var header = new byte[4];
        await ReadBlocksAsync(stream, header, token, progress).ConfigureAwait(false);
        var frameLength = BinaryPrimitives.ReadInt32BigEndian(header);
        if (frameLength == int.MinValue) throw new ProfileSyncException(ProfileSyncError.InvalidData);
        var compressed = frameLength < 0;
        var length = Math.Abs(frameLength);
        if (length == 0 || length > MaximumFrameSize) throw new ProfileSyncException(ProfileSyncError.InvalidData);
        var data = new byte[length];
        await ReadBlocksAsync(stream, data, token, progress).ConfigureAwait(false);
        if (compressed)
        {
            using var packed = new MemoryStream(data, false);
            using var gzip = new GZipStream(packed, CompressionMode.Decompress);
            using var unpacked = new MemoryStream();
            var buffer = new byte[TransferBlockSize];
            try
            {
                while (true)
                {
                    var count = await gzip.ReadAsync(buffer, token).ConfigureAwait(false);
                    if (count == 0) break;
                    if (unpacked.Length + count > MaximumFrameSize) throw new ProfileSyncException(ProfileSyncError.InvalidData);
                    unpacked.Write(buffer, 0, count);
                }
            }
            catch (InvalidDataException) { throw new ProfileSyncException(ProfileSyncError.InvalidData); }
            data = unpacked.ToArray();
        }
        var message = JsonSerializer.Deserialize<ProfileSyncMessage>(data);
        if (message is null || message.Magic != Magic) throw new ProfileSyncException(ProfileSyncError.InvalidData);
        if (message.Error != ProfileSyncError.None) throw new ProfileSyncException(message.Error);
        if (message.Version != Version) throw new ProfileSyncException(ProfileSyncError.VersionMismatch);
        if (string.IsNullOrWhiteSpace(profileKey) || message.ProfileKey != profileKey)
            throw new ProfileSyncException(ProfileSyncError.ProfileMismatch);
        if (message.Snapshot is { } snapshot) Validate(snapshot);
        return message;
    }

    private static async Task ReadBlocksAsync(Stream stream, byte[] data, CancellationToken token,
        Action<ProfileSyncProgress>? progress)
    {
        for (var offset = 0; offset < data.Length;)
        {
            var count = await stream.ReadAsync(data.AsMemory(offset, Math.Min(TransferBlockSize, data.Length - offset)), token).ConfigureAwait(false);
            if (count <= 0) throw new EndOfStreamException("A ligação Bluetooth foi fechada durante a receção.");
            offset += count;
            progress?.Invoke(new(ProfileSyncPhase.Receiving, offset, data.Length));
        }
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
        Func<Task<FavoriteSnapshot>> readLocal, Func<FavoriteSnapshot, Task> merge, CancellationToken token,
        Action<ProfileSyncProgress>? progress = null)
    {
        async Task<FavoriteSnapshot> Local()
        { progress?.Invoke(new(ProfileSyncPhase.Preparing)); return await readLocal().ConfigureAwait(false); }
        async Task Import(FavoriteSnapshot snapshot)
        { progress?.Invoke(new(ProfileSyncPhase.Saving)); await merge(snapshot).ConfigureAwait(false); }
        if (server)
        {
            try
            {
                var hello = await ReadAsync(input, profileKey, token, progress);
                if (hello.Snapshot is not null || hello.Acknowledged) throw new ProfileSyncException(ProfileSyncError.InvalidData);
                await WriteAsync(output, new(Magic, Version, profileKey, await Local()), token, progress);
                var incoming = await ReadAsync(input, profileKey, token, progress);
                await Import(incoming.Snapshot ?? throw new InvalidOperationException("Faltam dados de sincronização."));
                await WriteAsync(output, new(Magic, Version, profileKey, await Local()), token, progress);
                // RFCOMM close can discard buffered output. Keep the socket open until the peer has saved the final frame.
                progress?.Invoke(new(ProfileSyncPhase.Confirming));
                var confirmation = await ReadAsync(input, profileKey, token, progress);
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
            await WriteAsync(output, new(Magic, Version, profileKey), token, progress);
            var incoming = await ReadAsync(input, profileKey, token, progress);
            await Import(incoming.Snapshot ?? throw new InvalidOperationException("Faltam dados de sincronização."));
            await WriteAsync(output, new(Magic, Version, profileKey, await Local()), token, progress);
            var confirmed = await ReadAsync(input, profileKey, token, progress);
            await Import(confirmed.Snapshot ?? throw new InvalidOperationException("Faltam dados de sincronização."));
            progress?.Invoke(new(ProfileSyncPhase.Confirming));
            await WriteAsync(output, new(Magic, Version, profileKey, Acknowledged: true), token, progress);
        }
    }
}
