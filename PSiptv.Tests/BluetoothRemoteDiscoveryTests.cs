using PSiptv.Core;

internal static class BluetoothRemoteDiscoveryTests
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        RemoteControlState State(string id, bool active = false) => new(id, "Favoritos", "", 50, "", [],
            IsActive: active, DeviceId: id);
        var probes = new Dictionary<string, int>();
        var found = await BluetoothRemoteDiscovery.DiscoverAsync(["tablet", "headphones", "closed", "invalid", "tablet", "tv"],
            (address, _) =>
            {
                lock (probes) probes[address] = probes.GetValueOrDefault(address) + 1;
                return address switch
                {
                    "tablet" => Task.FromResult(State("tablet")),
                    "tv" => Task.FromResult(State("tv", true)),
                    "invalid" => Task.FromResult(State("")),
                    _ => Task.FromException<RemoteControlState>(new IOException("No PSiptv response"))
                };
            }, CancellationToken.None);
        check(found.Select(peer => peer.Address).SequenceEqual(["tv", "tablet"]),
            "Descoberta Bluetooth exclui acessórios e aplicações fechadas e prioriza o equipamento ativo");
        check(probes["tablet"] == 1, "Descoberta Bluetooth consulta cada endereço apenas uma vez");

        var activeProbes = 0;
        var exceededLimit = false;
        await BluetoothRemoteDiscovery.DiscoverAsync(Enumerable.Range(0, 8).Select(i => i.ToString()),
            async (address, token) =>
            {
                if (Interlocked.Increment(ref activeProbes) > 3) exceededLimit = true;
                try { await Task.Delay(10, token); return State(address); }
                finally { Interlocked.Decrement(ref activeProbes); }
            }, CancellationToken.None);
        check(!exceededLimit && activeProbes == 0, "Descoberta Bluetooth limita e termina as consultas paralelas");

        using var cancellation = new CancellationTokenSource();
        var cancelled = false;
        try
        {
            await BluetoothRemoteDiscovery.DiscoverAsync(["tablet"], async (_, token) =>
            {
                cancellation.Cancel();
                await Task.Delay(Timeout.Infinite, token);
                return State("tablet");
            }, cancellation.Token);
        }
        catch (OperationCanceledException) { cancelled = true; }
        check(cancelled, "Fechar a página cancela a descoberta Bluetooth em curso");
    }
}
