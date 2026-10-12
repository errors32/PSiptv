using System.Collections.Concurrent;

namespace PSiptv.Core;

public static class BluetoothRemoteDiscovery
{
    /// <summary>Only peers answering the authenticated state exchange are presented to the user.</summary>
    public static async Task<IReadOnlyList<(string Address, RemoteControlState State)>> DiscoverAsync(
        IEnumerable<string> addresses, Func<string, CancellationToken, Task<RemoteControlState>> probe,
        CancellationToken token)
    {
        var found = new ConcurrentBag<(string Address, RemoteControlState State)>();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            await Parallel.ForEachAsync(addresses.Distinct(), new ParallelOptions
            { MaxDegreeOfParallelism = 3, CancellationToken = deadline.Token }, async (address, cancellation) =>
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
                timeout.CancelAfter(TimeSpan.FromSeconds(5));
                try
                {
                    var state = await probe(address, timeout.Token);
                    timeout.Token.ThrowIfCancellationRequested();
                    if (!string.IsNullOrWhiteSpace(state.DeviceId)) found.Add((address, state));
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                { cancellation.ThrowIfCancellationRequested(); }
            });
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { }
        token.ThrowIfCancellationRequested();
        return found.OrderByDescending(peer => peer.State.IsActive).ThenBy(peer => peer.State.DeviceName)
            .ThenBy(peer => peer.Address).DistinctBy(peer => peer.State.DeviceId).ToArray();
    }
}
