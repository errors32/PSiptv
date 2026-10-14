using PSiptv.Core;

internal static class OpeningSyncTests
{
    internal static async Task RunAsync()
    {
        var passed = 0;
        void Check(bool value, string label)
        { if (!value) throw new Exception("FAIL: " + label); passed++; Console.WriteLine("PASS: " + label); }
        var session = new OpeningSyncSession(TimeSpan.Zero);
        var calls = new List<string>();
        Task<bool> Offline(string address, CancellationToken token) { calls.Add(address); return Task.FromResult(false); }
        var result = await session.RunAsync(["phone", "tablet", "PHONE"], Offline, default);
        Check(result is { Attempts: 5, Succeeded: 0 } && calls.Count == 5 && session.Blocked,
            "Abertura sem equipamentos disponíveis termina exatamente após cinco tentativas no total");
        Check(calls.Distinct().Count() == 2, "Tentativas alternam entre equipamentos memorizados sem duplicar endereços");
        await session.RunAsync(["phone", "tablet"], Offline, default);
        Check(calls.Count == 5, "Navegação, novos pedidos e retoma simples não renovam o orçamento da mesma abertura");
        var reopened = new OpeningSyncSession(TimeSpan.Zero);
        await reopened.RunAsync(["phone"], Offline, default);
        Check(calls.Count == 10 && reopened.Attempts == 5, "Nova abertura recebe um novo orçamento limitado a cinco tentativas");
        var online = new OpeningSyncSession(TimeSpan.Zero);
        var successCalls = new List<string>();
        var successes = await online.RunAsync(["phone", "tablet"], (a, t) => { successCalls.Add(a); return Task.FromResult(true); }, default);
        Check(successes is { Attempts: 2, Succeeded: 2 } && !online.Blocked,
            "Equipamentos disponíveis sincronizam uma vez cada e terminam antes do limite");
        await online.RunAsync(["phone", "tablet"], Offline, default);
        Check(successCalls.Count == 2 && online.Attempts == 2, "Equipamentos sincronizados não voltam a ser sondados na mesma abertura");
        var mixed = new OpeningSyncSession(TimeSpan.Zero);
        var mixedCalls = new List<string>();
        var mixedResult = await mixed.RunAsync(["offline", "online"], (a, t) =>
        { mixedCalls.Add(a); return Task.FromResult(a == "online"); }, default);
        Check(mixedResult is { Attempts: 5, Succeeded: 1 } && mixedCalls.Count(a => a == "online") == 1,
            "Sucesso parcial não repete o equipamento online nem excede cinco tentativas");
        var empty = new OpeningSyncSession(TimeSpan.Zero);
        await empty.RunAsync([], Offline, default);
        Check(!empty.Started && empty.Attempts == 0, "Sem equipamentos conhecidos não se efetuam ligações nem se gasta o orçamento");
        var incoming = new OpeningSyncSession(TimeSpan.Zero);
        incoming.ConfirmPeer("phone");
        await incoming.RunAsync(["phone"], Offline, default);
        Check(incoming.Started && incoming.Attempts == 0 && !incoming.Blocked,
            "Sincronização recebida antes da campanha evita uma ligação redundante ao remetente");
        var concurrent = new OpeningSyncSession(TimeSpan.Zero);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = concurrent.RunAsync(["phone"], (a, t) => release.Task, default);
        var second = await concurrent.RunAsync(["phone"], Offline, default);
        release.SetResult(true); await first;
        Check(second is null && concurrent.Attempts == 1, "Pedidos concorrentes não criam duas campanhas de abertura");
        using var cancel = new CancellationTokenSource();
        var paused = new OpeningSyncSession(TimeSpan.Zero);
        var cancelled = false;
        try { await paused.RunAsync(["phone"], (a, t) => { cancel.Cancel(); t.ThrowIfCancellationRequested(); return Task.FromResult(false); }, cancel.Token); }
        catch (OperationCanceledException) { cancelled = true; }
        Check(cancelled && paused.Attempts == 1 && !paused.Running && paused.Blocked,
            "Sair da aplicação cancela a campanha e não reinicia tentativas numa simples retoma");
        session.ConfirmPeer("phone");
        Check(!session.Blocked, "Uma sincronização manual ou recebida comprova disponibilidade e permite novas marcações");
        var date = DateTimeOffset.UtcNow;
        var peers = OpeningSyncSession.Remember([], "phone", "Telefone", date);
        peers = OpeningSyncSession.Remember(peers, "tablet", "Tablet", date.AddSeconds(1));
        peers = OpeningSyncSession.Remember(peers, "PHONE", "Novo nome", date.AddSeconds(2));
        Check(peers.Count == 2 && peers[0].Name == "Novo nome" && peers[1].Address == "tablet",
            "Memória conserva vários equipamentos, atualiza nomes e ordena pela última sincronização");
        Console.WriteLine($"\n{passed} verificações de sincronização ao abrir concluídas.");
    }
}
