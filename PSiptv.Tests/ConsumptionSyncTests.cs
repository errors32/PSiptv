using System.Net;
using System.Net.Sockets;
using PSiptv.Core;

internal static class ConsumptionSyncTests
{
    internal static async Task RunAsync()
    {
        var passed = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new Exception("FAIL: " + message);
            passed++;
            Console.WriteLine("PASS: " + message);
        }

        var item = new MediaItem("movie:sync", "Movie", "", MediaKind.Movie, "https://example.test/movie");
        var episode = item with { Id = "episode:sync", Url = "https://example.test/episode" };
        var a = new PersonalLibrary();
        var b = new PersonalLibrary();
        bool Set(PersonalLibrary library, MediaItem media, ViewingState state)
        {
            var previous = library.States.ToDictionary();
            library.SetState(media, state);
            return ViewingStateSync.Track(library, previous);
        }
        Check(!Set(a, item, ViewingState.Watching), "Iniciar reprodução não desencadeia sincronização Bluetooth");
        Check(Set(a, item, ViewingState.Completed), "Concluir filme desencadeia sincronização Bluetooth");
        var old = ViewingStateSync.Snapshot(a);
        Check(!Set(a, item, ViewingState.Completed), "Repetir visto não desencadeia outra sincronização");
        Check(ViewingStateSync.Merge(b, old) && b.State(item) == ViewingState.Completed,
            "Estado visto sincroniza sem exigir que o filme esteja nos favoritos");
        Check(Set(b, item, ViewingState.Unwatched), "Marcar por ver desencadeia sincronização");
        ViewingStateSync.Merge(b, old);
        ViewingStateSync.Merge(a, ViewingStateSync.Snapshot(b));
        Check(a.State(item) == ViewingState.Unwatched && b.State(item) == ViewingState.Unwatched,
            "Cópia antiga não recupera marcação visto removida");
        Check(!ViewingStateSync.Merge(a, ViewingStateSync.Snapshot(b)), "Importação repetida é idempotente");
        var legacyA = new PersonalLibrary(); var legacyB = new PersonalLibrary();
        legacyA.SetState(item, ViewingState.Completed); legacyB.SetState(item, ViewingState.Watching);
        var legacyCopy = ViewingStateSync.Snapshot(legacyA);
        ViewingStateSync.Merge(legacyA, ViewingStateSync.Snapshot(legacyB));
        ViewingStateSync.Merge(legacyB, legacyCopy);
        Check(legacyA.State(item) == legacyB.State(item), "Estados anteriores à atualização convergem sem datas de alteração");
        Set(a, item, ViewingState.Completed);
        Set(b, episode, ViewingState.Completed);
        var copyA = ViewingStateSync.Snapshot(a);
        var copyB = ViewingStateSync.Snapshot(b);
        ViewingStateSync.Merge(a, copyB); ViewingStateSync.Merge(b, copyA);
        Check(a.State(item) == ViewingState.Completed && a.State(episode) == ViewingState.Completed &&
            b.State(item) == ViewingState.Completed && b.State(episode) == ViewingState.Completed,
            "Marcações independentes de filme e episódio convergem nos dois sentidos");

        var memory = new Dictionary<string, string>();
        var store = new FavoriteStore(k => Task.FromResult(memory.GetValueOrDefault(k)),
            (k, v) => { memory[k] = v; return Task.CompletedTask; }, k => { memory.Remove(k); return Task.CompletedTask; });
        var account = new PlaylistAccount();
        var feed = new MediaItem("podcast:feed:events", "Feed", "", MediaKind.Series, HasEpisodes: true);
        var podcast = item with { Id = "podcast:episode:events", ParentSeriesId = feed.Id };
        await store.SetAsync(account, feed, true);
        Check(await store.SetHeardAsync(account.Id, podcast, true), "Primeira marcação ouvido devolve alteração real");
        Check(!await store.SetHeardAsync(account.Id, podcast, true), "Marcações ouvido repetidas não causam envios");
        Check(!await store.SetHeardAsync(account.Id, item, true), "Conteúdo não registado como podcast não provoca envio ouvido");
        Check(await store.SetHeardAsync(account.Id, podcast, false), "Marcação por ouvir devolve alteração real");

        var worker = new ConsumptionSyncWorker(TimeSpan.FromMilliseconds(20));
        using var cancelled = new CancellationTokenSource();
        using var attempts = new SemaphoreSlim(0);
        var count = 0;
        var run = worker.RunAsync(() => { Interlocked.Increment(ref count); attempts.Release(); return Task.CompletedTask; }, cancelled.Token);
        await Task.Delay(80);
        Check(count == 0, "Worker ocioso não tenta ligações por temporizador");
        for (var i = 0; i < 20; i++) worker.Request();
        Check(await attempts.WaitAsync(TimeSpan.FromSeconds(2)), "Evento de consumo acorda o worker");
        await Task.Delay(80);
        Check(count == 1, "Marcações em lote são agrupadas num envio sem repetições periódicas");
        worker.Request();
        Check(await attempts.WaitAsync(TimeSpan.FromSeconds(2)), "Nova marcação provoca nova troca");
        cancelled.Cancel(); await run;
        worker.Request(); await Task.Delay(40);
        Check(count == 2, "Sair da aplicação cancela o worker e impede envios tardios");

        var once = new ConsumptionSyncWorker(TimeSpan.Zero);
        using var onceLifetime = new CancellationTokenSource();
        using var offlineAttempts = new SemaphoreSlim(0);
        var offlineCount = 0;
        var firstTransfer = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var oneShotRun = once.RunAsync(async () =>
        {
            var attempt = Interlocked.Increment(ref offlineCount);
            offlineAttempts.Release();
            if (attempt == 1)
            {
                await firstTransfer.Task;
                throw new IOException("Peer offline");
            }
        }, onceLifetime.Token);
        once.Request();
        Check(await offlineAttempts.WaitAsync(TimeSpan.FromSeconds(2)), "Conclusão inicia uma única tentativa com o equipamento indisponível");
        firstTransfer.SetResult();
        await Task.Delay(80);
        Check(offlineCount == 1 && !oneShotRun.IsCompleted,
            "Falha não repete a tentativa nem impede a próxima marcação");
        once.Request();
        Check(await offlineAttempts.WaitAsync(TimeSpan.FromSeconds(2)),
            "Outro programa ouvido inicia nova tentativa depois de uma falha");
        await Task.Delay(80);
        Check(offlineCount == 2, "Nova tentativa termina sem qualquer repetição automática");
        onceLifetime.Cancel();
        await oneShotRun;

        var duringTransfer = new ConsumptionSyncWorker(TimeSpan.Zero);
        using var duringLifetime = new CancellationTokenSource();
        using var duringAttempts = new SemaphoreSlim(0);
        var duringCount = 0;
        var finishTransfer = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var duringRun = duringTransfer.RunAsync(async () =>
        {
            var attempt = Interlocked.Increment(ref duringCount);
            duringAttempts.Release();
            if (attempt == 1) await finishTransfer.Task;
        }, duringLifetime.Token);
        duringTransfer.Request();
        Check(await duringAttempts.WaitAsync(TimeSpan.FromSeconds(2)), "Primeira marcação inicia a troca");
        duringTransfer.Request();
        finishTransfer.SetResult();
        Check(await duringAttempts.WaitAsync(TimeSpan.FromSeconds(2)),
            "Marcação durante uma troca mantém uma nova tentativa pendente");
        await Task.Delay(80);
        Check(duringCount == 2, "Terminar a troca não gera uma terceira tentativa");
        duringLifetime.Cancel();
        await duringRun;

        using var frames = new MemoryStream();
        var snapshot = (await store.SnapshotAsync(account.Id)) with { ViewingStates = ViewingStateSync.Snapshot(a) };
        await ProfileSyncProtocol.WriteAsync(frames, new(ProfileSyncProtocol.Magic, ProfileSyncProtocol.Version, "profile", snapshot), default);
        frames.Position = 0;
        var read = await ProfileSyncProtocol.ReadAsync(frames, "profile", default);
        Check(read.Snapshot!.ViewingStates![CatalogPreferences.ItemKey(item)].State == ViewingState.Completed,
            "Protocolo transporta estados visto juntamente com podcasts");

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var client = new TcpClient();
            var connection = client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port, deadline.Token);
            using var server = await listener.AcceptTcpClientAsync(deadline.Token);
            await connection;
            var served = ProfileSyncProtocol.ExchangeAsync(server.GetStream(), server.GetStream(), "profile", true,
                () => Task.FromResult(snapshot), _ => Task.CompletedTask, deadline.Token);
            await ProfileSyncProtocol.WriteAsync(client.GetStream(), new(ProfileSyncProtocol.Magic, ProfileSyncProtocol.Version, "profile"), deadline.Token);
            await ProfileSyncProtocol.ReadAsync(client.GetStream(), "profile", deadline.Token);
            await ProfileSyncProtocol.WriteAsync(client.GetStream(), new(ProfileSyncProtocol.Magic, ProfileSyncProtocol.Version, "profile", snapshot), deadline.Token);
            await ProfileSyncProtocol.ReadAsync(client.GetStream(), "profile", deadline.Token);
            Check(!served.IsCompleted, "Servidor mantém ligação aberta até o cliente confirmar o último snapshot");
            await ProfileSyncProtocol.WriteAsync(client.GetStream(), new(ProfileSyncProtocol.Magic, ProfileSyncProtocol.Version, "profile", Acknowledged: true), deadline.Token);
            await served;
            Check(served.IsCompletedSuccessfully, "Confirmação de receção conclui a troca Bluetooth");
            var rejection = ProfileSyncProtocol.ExchangeAsync(server.GetStream(), server.GetStream(), "profile", true,
                () => Task.FromResult(snapshot), _ => Task.CompletedTask, deadline.Token);
            await ProfileSyncProtocol.WriteAsync(client.GetStream(), new(ProfileSyncProtocol.Magic, ProfileSyncProtocol.Version, "another-profile"), deadline.Token);
            var identified = false;
            try { await ProfileSyncProtocol.ReadAsync(client.GetStream(), "another-profile", deadline.Token); }
            catch (ProfileSyncException ex) { identified = ex.Error == ProfileSyncError.ProfileMismatch; }
            try { await rejection; } catch (ProfileSyncException) { }
            Check(identified, "Cliente recebe causa concreta quando o perfil/lista difere no recetor");
            rejection = ProfileSyncProtocol.ExchangeAsync(server.GetStream(), server.GetStream(), "profile", true,
                () => Task.FromResult(snapshot), _ => Task.CompletedTask, deadline.Token);
            await ProfileSyncProtocol.WriteAsync(client.GetStream(), new(ProfileSyncProtocol.Magic, 1, "profile"), deadline.Token);
            identified = false;
            try { await ProfileSyncProtocol.ReadAsync(client.GetStream(), "profile", deadline.Token); }
            catch (ProfileSyncException ex) { identified = ex.Error == ProfileSyncError.VersionMismatch; }
            try { await rejection; } catch (ProfileSyncException) { }
            Check(identified, "Versão incompatível explica que ambos os equipamentos precisam da atualização");
            rejection = ProfileSyncProtocol.ExchangeAsync(server.GetStream(), server.GetStream(), "profile", true,
                () => throw new IOException("Storage unavailable"), _ => Task.CompletedTask, deadline.Token);
            await ProfileSyncProtocol.WriteAsync(client.GetStream(), new(ProfileSyncProtocol.Magic, ProfileSyncProtocol.Version, "profile"), deadline.Token);
            identified = false;
            try { await ProfileSyncProtocol.ReadAsync(client.GetStream(), "profile", deadline.Token); }
            catch (ProfileSyncException ex) { identified = ex.Error == ProfileSyncError.StorageFailure; }
            try { await rejection; } catch (IOException) { }
            Check(identified, "Falha de armazenamento no recetor é comunicada ao remetente");
        }
        finally { listener.Stop(); }
        Console.WriteLine($"\n{passed} verificações de sincronização por consumo concluídas.");
    }
}
