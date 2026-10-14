namespace PSiptv.Core;

/// <summary>Bounds a single transfer, extending only its inactivity deadline on real progress.</summary>
public sealed class SyncTransferDeadline : IDisposable
{
    public static readonly TimeSpan DefaultIdleTimeout = TimeSpan.FromSeconds(90);
    public static readonly TimeSpan DefaultTotalTimeout = TimeSpan.FromMinutes(5);
    private readonly object gate = new();
    private readonly CancellationToken parent;
    private readonly CancellationTokenSource total;
    private readonly CancellationTokenSource idle;
    private readonly TimeSpan idleTimeout;
    private bool disposed;
    public CancellationToken Token { get; }
    public bool TimedOut => !parent.IsCancellationRequested && Token.IsCancellationRequested;
    public bool TotalExpired => total.IsCancellationRequested && !parent.IsCancellationRequested;

    public SyncTransferDeadline(CancellationToken parent, TimeSpan? idleTimeout = null, TimeSpan? totalTimeout = null)
    {
        this.parent = parent;
        this.idleTimeout = idleTimeout ?? DefaultIdleTimeout;
        total = CancellationTokenSource.CreateLinkedTokenSource(parent);
        total.CancelAfter(totalTimeout ?? DefaultTotalTimeout);
        idle = CancellationTokenSource.CreateLinkedTokenSource(total.Token);
        Token = idle.Token;
        Touch();
    }

    public void Touch()
    {
        lock (gate)
            if (!disposed && !Token.IsCancellationRequested) idle.CancelAfter(idleTimeout);
    }

    public void Dispose()
    {
        lock (gate)
        {
            disposed = true;
            idle.Dispose(); total.Dispose();
        }
    }
}
