namespace PSiptv.Core;

public sealed record RememberedSyncPeer(string Address, string Name, DateTimeOffset LastSyncedAt);
public sealed record OpeningSyncResult(int Attempts, int Succeeded);

/// <summary>One campaign per genuine app opening, with a shared five-attempt budget across peers.</summary>
public sealed class OpeningSyncSession(TimeSpan? retryDelay = null)
{
    public const int MaximumAttempts = 5;
    private readonly object gate = new();
    private readonly HashSet<string> succeeded = new(StringComparer.OrdinalIgnoreCase);
    private bool started;
    private bool finished;
    private int attempts;
    public int Attempts { get { lock (gate) return attempts; } }
    public bool Started { get { lock (gate) return started; } }
    public bool Running { get { lock (gate) return started && !finished; } }
    public bool Blocked { get { lock (gate) return started && finished && succeeded.Count == 0; } }

    public void ConfirmPeer(string address) { lock (gate) succeeded.Add(address); }

    public async Task<OpeningSyncResult?> RunAsync(IReadOnlyList<string> addresses,
        Func<string, CancellationToken, Task<bool>> synchronize, CancellationToken token)
    {
        var candidates = addresses.Where(a => !string.IsNullOrWhiteSpace(a)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (candidates.Count == 0) return null;
        lock (gate)
        {
            if (started || token.IsCancellationRequested) return null;
            candidates.RemoveAll(succeeded.Contains);
            started = true;
            if (candidates.Count == 0) { finished = true; return null; }
        }
        var index = 0;
        try
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();
                string address;
                lock (gate)
                {
                    candidates.RemoveAll(succeeded.Contains);
                    if (attempts >= MaximumAttempts || candidates.Count == 0) break;
                    address = candidates[index++ % candidates.Count];
                    attempts++;
                }
                if (await synchronize(address, token).ConfigureAwait(false)) ConfirmPeer(address);
                lock (gate)
                    if (attempts >= MaximumAttempts || candidates.All(succeeded.Contains)) break;
                await Task.Delay(retryDelay ?? TimeSpan.FromSeconds(3), token).ConfigureAwait(false);
            }
            lock (gate) return new(attempts, succeeded.Count);
        }
        finally { lock (gate) finished = true; }
    }

    public static IReadOnlyList<RememberedSyncPeer> Remember(IReadOnlyList<RememberedSyncPeer> previous,
        string address, string name, DateTimeOffset date) => previous
        .Where(p => !p.Address.Equals(address, StringComparison.OrdinalIgnoreCase))
        .Append(new(address, name, date)).OrderByDescending(p => p.LastSyncedAt).ToArray();
}
