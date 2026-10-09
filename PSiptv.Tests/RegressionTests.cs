using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using PSiptv.Core;
using PSiptv.Services;

internal static class RegressionTests
{
    internal static async Task RunAsync()
    {
        var passed = 0;
        void Check(bool value, string label)
        {
            if (!value) throw new Exception("FAIL: " + label);
            passed++;
            Console.WriteLine("PASS: " + label);
        }
        async Task Reject(Func<Task> action, string label)
        {
            try { await action(); }
            catch (Exception ex) when (ex is InvalidOperationException or OperationCanceledException or EndOfStreamException)
            { Check(true, label); return; }
            throw new Exception("FAIL: " + label);
        }

        var plain = Encoding.UTF8.GetBytes("{\"Accounts\":[{\"Password\":\"private-test-password\"}]}");
        var encrypted = BackupProtection.Protect(plain, "passphrase-test");
        Check(BackupProtection.IsProtected(encrypted) && !Encoding.UTF8.GetString(encrypted).Contains("private-test-password"),
            "Backup protege todo o documento, incluindo credenciais");
        Check(BackupProtection.Unprotect(encrypted, "passphrase-test").SequenceEqual(plain), "Backup protegido recupera sem alterações");
        Check(!BackupProtection.Protect(plain, "passphrase-test").SequenceEqual(encrypted), "Backups usam sal e nonce aleatórios");
        await Reject(() => Task.Run(() => BackupProtection.Unprotect(encrypted, "wrong")), "Backup rejeita palavra-passe incorreta");
        var tampered = encrypted.ToArray(); tampered[^1] ^= 1;
        await Reject(() => Task.Run(() => BackupProtection.Unprotect(tampered, "passphrase-test")), "Backup rejeita conteúdo adulterado");
        await Reject(() => Task.Run(() => BackupProtection.Unprotect(encrypted[..20], "passphrase-test")), "Backup rejeita ficheiro truncado");
        Check(BackupProtection.Unprotect(plain, null).SequenceEqual(plain), "Backups JSON antigos continuam legíveis");

        var operations = new LatestOperation();
        using var first = operations.Begin();
        var delayed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stops = new List<string>();
        async Task FinishOldAsync()
        {
            await delayed.Task; // A provider may finish even after cancellation.
            if (first.IsCurrent) stops.Add("new-channel");
        }
        var old = FinishOldAsync();
        using var second = operations.Begin();
        delayed.SetResult();
        await old;
        Check(first.Token.IsCancellationRequested && !first.IsCurrent && second.IsCurrent && stops.Count == 0,
            "Resposta antiga não interrompe o canal mais recente");
        operations.Cancel();
        Check(second.Token.IsCancellationRequested && !second.IsCurrent, "Sair do leitor cancela a resolução pendente");

        var temp = Path.Combine(Path.GetTempPath(), "psiptv-regression-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            var path = Path.Combine(temp, "image.img");
            await AtomicFile.WriteAsync(path, plain);
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            await Reject(() => AtomicFile.WriteAsync(path, new byte[2048], cancelled.Token), "Transferência cancelada não substitui imagem válida");
            Check((await File.ReadAllBytesAsync(path)).SequenceEqual(plain) && Directory.GetFiles(temp).Length == 1,
                "Escrita atómica conserva o original e remove temporários");
            await Task.WhenAll(AtomicFile.WriteAsync(path, new byte[10000]), AtomicFile.WriteAsync(path, new byte[12000]));
            Check(new FileInfo(path).Length is 10000 or 12000 && Directory.GetFiles(temp).Length == 1,
                "Escritas concorrentes nunca deixam ficheiros parciais");
        }
        finally
        {
            foreach (var file in Directory.EnumerateFiles(temp)) File.Delete(file);
            Directory.Delete(temp);
        }

        var shared = new SharedWork<string, int>();
        var released = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        Task<int> Transfer() { Interlocked.Increment(ref calls); return released.Task; }
        using var waiter = new CancellationTokenSource();
        var abandoned = shared.RunAsync("same-logo", Transfer, waiter.Token);
        var retained = shared.RunAsync("same-logo", Transfer);
        waiter.Cancel();
        await Reject(async () => await abandoned, "Cancelar um cartão não cancela o download partilhado");
        released.SetResult(42);
        Check(await retained == 42 && calls == 1, "Cartões com o mesmo logótipo partilham uma transferência");

        var cacheRoot = Path.Combine(Path.GetTempPath(), "psiptv-cache-regression-" + Guid.NewGuid().ToString("N"));
        PSiptv.Services.FileSystem.Root = cacheRoot;
        Directory.CreateDirectory(cacheRoot);
        try
        {
            var cachedEpisode = new MediaItem("podcast:cache-test", "Cached", "", MediaKind.Movie);
            await PodcastCacheService.SaveAsync("feed-test", [cachedEpisode], default);
            Check((await PodcastCacheService.ReadAsync("feed-test"))?.Items.Single() == cachedEpisode,
                "Cache real de podcasts guarda e recupera episódios");
            var size = await StorageManagerService.MeasureAsync();
            Check(size.CacheBytes > 0 && size.CacheFiles == 1, "Gestor real de armazenamento inclui a cache dos podcasts");
            await StorageManagerService.ClearCacheAsync();
            Check(await PodcastCacheService.ReadAsync("feed-test") is null && (await StorageManagerService.MeasureAsync()).CacheBytes == 0,
                "Limpeza geral remove a cache real dos podcasts");
            using var cancelledCache = new CancellationTokenSource(); cancelledCache.Cancel();
            await Reject(() => PodcastCacheService.SaveAsync("cancelled", [cachedEpisode], cancelledCache.Token),
                "Cache não escreve pedidos cancelados");
        }
        finally
        {
            foreach (var file in Directory.EnumerateFiles(cacheRoot, "*", SearchOption.AllDirectories)) File.Delete(file);
            foreach (var directory in Directory.EnumerateDirectories(cacheRoot, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length))
                Directory.Delete(directory);
            Directory.Delete(cacheRoot);
        }

        var culture = CultureInfo.GetCultureInfo("pt-PT");
        var documents = Enumerable.Range(0, 20000).Select(i =>
            new SearchDocument<int>($"Canal {i:D5}", [$"Canal {i:D5}", i % 2 == 0 ? "Desporto" : "Notícias"], i)).ToArray();
        var index = new TextSearchIndex<int>(documents, culture);
        var watch = Stopwatch.StartNew();
        var found = index.Search("canal", 300);
        watch.Stop();
        Check(found.Count == 300 && found.First() == 0 && found.Last() == 299, "Pesquisa limita e ordena resultados em catálogo de 20 mil conteúdos");
        Check(index.Search("CANAL 00123", 300).First() == 123, "Pesquisa ignora maiúsculas e dá prioridade à correspondência exata");
        Check(index.Search("Notícias", 3).SequenceEqual(new[] { 1, 3, 5 }), "Índice pesquisa categorias e respeita o limite");
        using var queryCancelled = new CancellationTokenSource(); queryCancelled.Cancel();
        await Reject(() => Task.Run(() => index.Search("canal", 300, queryCancelled.Token)), "Pesquisa substituída é cancelável");
        Console.WriteLine($"BENCHMARK: pesquisa 20 000 conteúdos: {watch.Elapsed.TotalMilliseconds:F1} ms (sem renderização UI)");

        var dataA = new Dictionary<string, string>();
        var dataB = new Dictionary<string, string>();
        FavoriteStore Store(Dictionary<string, string> data) => new(
            key => Task.FromResult(data.GetValueOrDefault(key)),
            (key, value) => { data[key] = value; return Task.CompletedTask; },
            key => { data.Remove(key); return Task.CompletedTask; });
        var a = Store(dataA); var b = Store(dataB);
        var account = new PlaylistAccount();
        var feed = new MediaItem("podcast:feed:merge", "Feed", "", MediaKind.Series, "https://example.test/feed", HasEpisodes: true);
        var episode = new MediaItem("podcast:episode:merge", "Episode", "", MediaKind.Movie, "https://example.test/episode.mp3", ParentSeriesId: feed.Id);
        var channelA = new MediaItem("channel-a", "A", "", MediaKind.Channel);
        var channelB = new MediaItem("channel-b", "B", "", MediaKind.Channel);
        await a.SetAsync(account, feed, true);
        await b.ImportNewerAsync(account.Id, account, await a.SnapshotAsync(account.Id));
        await a.SetAsync(account, channelA, true);
        await b.SetAsync(account, channelB, true);
        var snapshotA = await a.SnapshotAsync(account.Id); var snapshotB = await b.SnapshotAsync(account.Id);
        await a.ImportNewerAsync(account.Id, account, snapshotB);
        await b.ImportNewerAsync(account.Id, account, snapshotA);
        Check((await a.LoadAsync(account.Id)).Count == 3 && (await b.LoadAsync(account.Id)).Count == 3,
            "Alterações independentes em dois dispositivos são preservadas");
        var stale = await b.SnapshotAsync(account.Id);
        await a.SetAsync(account, channelA, false);
        await a.ImportNewerAsync(account.Id, account, stale);
        Check(!(await a.LoadAsync(account.Id)).Any(e => e.Item.Id == channelA.Id), "Cópia antiga não recupera favorito removido");
        await b.ImportNewerAsync(account.Id, account, await a.SnapshotAsync(account.Id));
        Check(!(await b.LoadAsync(account.Id)).Any(e => e.Item.Id == channelA.Id), "Remoções propagam-se ao outro dispositivo");
        await a.RecordPodcastAsync(account.Id, episode, 90, 600);
        await b.RecordPodcastAsync(account.Id, episode, 40, 600);
        await a.ImportNewerAsync(account.Id, account, await b.SnapshotAsync(account.Id));
        Check((await a.SnapshotAsync(account.Id)).PodcastProgress![episode.Id].Position == 40,
            "Progresso usa a alteração mais recente, mesmo quando a posição diminui");
        await a.SetHeardAsync(account.Id, episode, true);
        await b.ImportNewerAsync(account.Id, account, await a.SnapshotAsync(account.Id));
        var heardCopy = await b.SnapshotAsync(account.Id);
        await a.SetHeardAsync(account.Id, episode, false);
        await a.ImportNewerAsync(account.Id, account, heardCopy);
        Check(!(await a.SnapshotAsync(account.Id)).HeardEpisodes.ContainsKey(episode.Id), "Marcar por ouvir não é revertido por cópia antiga");
        var complete = await a.SnapshotAsync(account.Id);
        Check(!await a.ImportNewerAsync(account.Id, account, complete), "Repetir sincronização não altera o estado");
        await b.ImportNewerAsync(account.Id, account, complete);
        await b.RecordPodcastAsync(account.Id, episode, 180, 600);
        var preRemoval = await b.SnapshotAsync(account.Id);
        await a.SetAsync(account, feed, false);
        await a.ImportNewerAsync(account.Id, account, preRemoval);
        await a.SetAsync(account, feed, true);
        await b.SetAsync(account, new MediaItem("force-merge", "Force merge", "", MediaKind.Channel), true);
        await a.ImportNewerAsync(account.Id, account, await b.SnapshotAsync(account.Id));
        Check((await a.SnapshotAsync(account.Id)).PodcastProgress!.Count == 0,
            "Voltar a adicionar uma subscrição não recupera progresso anterior à remoção");
        complete = await a.SnapshotAsync(account.Id);
        Check((await a.LoadAsync("other-profile")).Count == 0, "Sincronização mantém isolamento entre perfis");

        using var frame = new MemoryStream();
        await ProfileSyncProtocol.WriteAsync(frame, new(ProfileSyncProtocol.Magic, ProfileSyncProtocol.Version, "profile", complete), default);
        frame.Position = 0;
        Check((await ProfileSyncProtocol.ReadAsync(frame, "profile", default)).Snapshot!.Entries.Count == complete.Entries.Count,
            "Protocolo Bluetooth transporta estado e remoções");
        frame.Position = 0;
        await Reject(async () => await ProfileSyncProtocol.ReadAsync(frame, "different-profile", default), "Bluetooth rejeita outro perfil antes de importar");
        var oversizedHeader = new byte[4]; BinaryPrimitives.WriteInt32BigEndian(oversizedHeader, ProfileSyncProtocol.MaximumFrameSize + 1);
        await Reject(async () => await ProfileSyncProtocol.ReadAsync(new MemoryStream(oversizedHeader), "profile", default),
            "Protocolo rejeita mensagens excessivas antes de alocar o conteúdo");
        await Reject(async () => await ProfileSyncProtocol.ReadAsync(new MemoryStream(new byte[] { 0, 0 }), "profile", default),
            "Protocolo rejeita ligação interrompida a meio da mensagem");

        // Exercise the real protocol in both directions over streams, without Bluetooth hardware.
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var client = new TcpClient();
            var connection = client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port, deadline.Token);
            using var server = await listener.AcceptTcpClientAsync(deadline.Token);
            await connection;
            await a.SetAsync(account, channelA, true);
            await b.SetAsync(account, new MediaItem("channel-c", "C", "", MediaKind.Channel), true);
            await Task.WhenAll(
                ProfileSyncProtocol.ExchangeAsync(client.GetStream(), client.GetStream(), "profile", false,
                    () => a.SnapshotAsync(account.Id), async s => { await a.ImportNewerAsync(account.Id, account, s); }, deadline.Token),
                ProfileSyncProtocol.ExchangeAsync(server.GetStream(), server.GetStream(), "profile", true,
                    () => b.SnapshotAsync(account.Id), async s => { await b.ImportNewerAsync(account.Id, account, s); }, deadline.Token));
            var idsA = (await a.LoadAsync(account.Id)).Select(e => e.Item.Id).Order().ToArray();
            var idsB = (await b.LoadAsync(account.Id)).Select(e => e.Item.Id).Order().ToArray();
            Check(idsA.SequenceEqual(idsB) && idsA.Contains(channelA.Id) && idsA.Contains("channel-c"),
                "Troca bidirecional completa converge sem perder alterações locais");
        }
        finally { listener.Stop(); }
        Console.WriteLine($"\n{passed} regressões e verificações adicionais concluídas.");
    }
}
