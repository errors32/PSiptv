using System.Threading.Channels;

namespace PSiptv.Core;

/// <summary>Waits for an explicit consumption/connection event; never polls or retries on a timer.</summary>
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
                await synchronize();
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }
}
