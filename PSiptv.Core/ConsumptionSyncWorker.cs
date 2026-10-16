using System.Threading.Channels;

namespace PSiptv.Core;

/// <summary>One attempt per consumption event (coalescing batches); failures never schedule retries.</summary>
public sealed class ConsumptionSyncWorker(TimeSpan? debounce = null)
{
    private readonly Channel<bool> requests = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
        { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });

    public void Request() => requests.Writer.TryWrite(true);

    public async Task RunAsync(Func<Task> synchronize, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                await requests.Reader.ReadAsync(token);
                await Task.Delay(debounce ?? TimeSpan.FromMilliseconds(500), token);
                while (requests.Reader.TryRead(out _)) { }
                try { await synchronize(); }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    // Discard this attempt. Keep listening so the next consumed
                    // programme can initiate a fresh attempt after an offline peer.
                    System.Diagnostics.Debug.WriteLine(ex);
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }
}
