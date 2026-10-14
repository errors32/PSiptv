using System.Buffers.Binary;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using PSiptv.Core;

internal static class LargeProfileSyncTests
{
    internal static async Task RunAsync()
    {
        var passed = 0;
        void Check(bool condition, string label)
        { if (!condition) throw new Exception("FAIL: " + label); passed++; Console.WriteLine("PASS: " + label); }
        async Task Reject(Stream stream, string label)
        {
            try { await ProfileSyncProtocol.ReadAsync(stream, "profile", default); }
            catch (ProfileSyncException) { Check(true, label); return; }
            throw new Exception("FAIL: " + label);
        }
        var entries = Enumerable.Range(0, 2000).Select(i => new FavoriteEntry("favorite:" + i,
            new MediaItem("podcast:feed:" + i, "Podcast " + i + new string('x', 400), "Podcasts", MediaKind.Series,
                "https://example.test/feed/" + i, HasEpisodes: true))).ToArray();
        var snapshot = new FavoriteSnapshot(DateTimeOffset.UtcNow, entries, new Dictionary<string, PodcastHeardEntry>());
        var message = new ProfileSyncMessage(ProfileSyncProtocol.Magic, ProfileSyncProtocol.Version, "profile", snapshot);
        using var frame = new MemoryStream();
        var writeProgress = new List<ProfileSyncProgress>();
        using (var bounded = new FragmentedStream(frame, int.MaxValue, TimeSpan.Zero))
            await ProfileSyncProtocol.WriteAsync(bounded, message, default, writeProgress.Add);
        var rawLength = JsonSerializer.SerializeToUtf8Bytes(message).Length;
        Check(BinaryPrimitives.ReadInt32BigEndian(frame.ToArray()) < 0 && frame.Length < rawLength / 4,
            "Snapshot grande usa compressão e reduz os bytes Bluetooth em mais de 75%");
        Check(writeProgress.Count > 2 && writeProgress[^1].Bytes == writeProgress[^1].TotalBytes,
            "Envio grande reporta progresso real até ao último bloco");
        Check(writeProgress.Zip(writeProgress.Skip(1)).All(p => p.Second.Bytes - p.First.Bytes <= ProfileSyncProtocol.TransferBlockSize),
            "Dados são enviados em blocos até 16 KB, em vez de uma escrita enorme");
        frame.Position = 0;
        using var transfer = new SyncTransferDeadline(default, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30));
        using var oldFixedLimit = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        using (var fragmented = new FragmentedStream(frame, 256, TimeSpan.FromMilliseconds(2)))
        {
            var incoming = await ProfileSyncProtocol.ReadAsync(fragmented, "profile", transfer.Token, _ => transfer.Touch());
            Check(incoming.Snapshot!.Entries.SequenceEqual(entries), "Leitura lenta e fragmentada recupera todos os podcasts sem perda");
        }
        Check(oldFixedLimit.IsCancellationRequested && !transfer.TimedOut,
            "Transferência com progresso pode ultrapassar o prazo inicial sem fechar a ligação");
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            using var socketDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var client = new TcpClient();
            var connect = client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port, socketDeadline.Token);
            using var server = await listener.AcceptTcpClientAsync(socketDeadline.Token);
            await connect;
            using var clientStream = new FragmentedStream(client.GetStream(), 512, TimeSpan.FromMilliseconds(2));
            using var serverStream = new FragmentedStream(server.GetStream(), 512, TimeSpan.FromMilliseconds(2));
            using var clientTime = new SyncTransferDeadline(socketDeadline.Token, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(25));
            using var serverTime = new SyncTransferDeadline(socketDeadline.Token, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(25));
            var left = snapshot;
            var right = snapshot with { Entries = [.. entries, new("other-device", entries[0].Item with { Id = "podcast:other-device" })] };
            await Task.WhenAll(
                ProfileSyncProtocol.ExchangeAsync(clientStream, clientStream, "profile", false, () => Task.FromResult(left),
                    s => { left = FavoriteMerge.Merge(left, s).Snapshot; return Task.CompletedTask; }, clientTime.Token, _ => clientTime.Touch()),
                ProfileSyncProtocol.ExchangeAsync(serverStream, serverStream, "profile", true, () => Task.FromResult(right),
                    s => { right = FavoriteMerge.Merge(right, s).Snapshot; return Task.CompletedTask; }, serverTime.Token, _ => serverTime.Touch()));
            Check(left.Entries.Count == 2001 && right.Entries.Count == 2001,
                "Troca completa com milhares de podcasts e streams lentos converge nos dois sentidos");
            Check(!clientTime.TimedOut && !serverTime.TimedOut, "Transferência comprimida completa mantém ambos os prazos ativos até à confirmação");
        }
        finally { listener.Stop(); }
        using var hello = new MemoryStream();
        await ProfileSyncProtocol.WriteAsync(hello, new(ProfileSyncProtocol.Magic, ProfileSyncProtocol.Version, "profile"), default);
        Check(BinaryPrimitives.ReadInt32BigEndian(hello.ToArray()) > 0, "Handshake permanece sem compressão para diagnosticar versões antigas");
        using var stalled = new SyncTransferDeadline(default, TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(3));
        await Task.Delay(200);
        Check(stalled.TimedOut && !stalled.TotalExpired, "Ausência de progresso cancela a transferência bloqueada");
        using var total = new SyncTransferDeadline(default, TimeSpan.FromSeconds(3), TimeSpan.FromMilliseconds(100));
        await Task.Delay(200);
        total.Touch();
        Check(total.TimedOut && total.TotalExpired, "Progresso não ultrapassa o limite total nem reanima uma transferência expirada");
        using var parent = new CancellationTokenSource();
        using var paused = new SyncTransferDeadline(parent.Token);
        parent.Cancel();
        Check(paused.Token.IsCancellationRequested && !paused.TimedOut, "Sair da aplicação cancela sem ser confundido com timeout");
        using var packed = new MemoryStream();
        using (var gzip = new GZipStream(packed, CompressionLevel.Fastest, true))
            await gzip.WriteAsync(new byte[ProfileSyncProtocol.MaximumFrameSize + 1]);
        using var bomb = new MemoryStream();
        var header = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(header, -(int)packed.Length);
        bomb.Write(header); bomb.Write(packed.ToArray()); bomb.Position = 0;
        await Reject(bomb, "Conteúdo comprimido não pode ultrapassar o limite descomprimido de 8 MB");
        BinaryPrimitives.WriteInt32BigEndian(header, int.MinValue);
        await Reject(new MemoryStream(header), "Cabeçalho comprimido inválido é rejeitado antes de alocar memória");
        Console.WriteLine($"\n{passed} verificações de transferência Bluetooth grande concluídas.");
    }

    private sealed class FragmentedStream(Stream inner, int fragment, TimeSpan delay) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => inner.CanWrite;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => inner.Flush();
        public override Task FlushAsync(CancellationToken token) => inner.FlushAsync(token);
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, Math.Min(fragment, count));
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        { if (delay > TimeSpan.Zero) await Task.Delay(delay, token); return await inner.ReadAsync(buffer[..Math.Min(fragment, buffer.Length)], token); }
        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken token = default)
        {
            if (buffer.Length > ProfileSyncProtocol.TransferBlockSize) throw new Exception("Unbounded Bluetooth write");
            return inner.WriteAsync(buffer, token);
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
