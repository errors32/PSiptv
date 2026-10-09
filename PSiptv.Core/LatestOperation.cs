namespace PSiptv.Core;

/// <summary>Cancels superseded work and prevents it from committing side effects.</summary>
public sealed class LatestOperation
{
    private readonly object sync = new();
    private Lease? current;

    public Lease Begin()
    {
        lock (sync)
        {
            current?.Cancel();
            return current = new Lease(this);
        }
    }

    public void Cancel()
    {
        lock (sync) { current?.Cancel(); current = null; }
    }

    public sealed class Lease : IDisposable
    {
        private readonly LatestOperation owner;
        private readonly CancellationTokenSource cancellation = new();
        internal Lease(LatestOperation owner) { this.owner = owner; Token = cancellation.Token; }
        public CancellationToken Token { get; }
        public bool IsCurrent { get { lock (owner.sync) return ReferenceEquals(owner.current, this); } }
        internal void Cancel() => cancellation.Cancel();
        public void Dispose()
        {
            lock (owner.sync)
            {
                if (ReferenceEquals(owner.current, this)) owner.current = null;
                cancellation.Dispose();
            }
        }
    }
}
