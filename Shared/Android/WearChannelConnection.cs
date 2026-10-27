using Android.Gms.Wearable;
using Android.Runtime;

namespace PSiptv.WearTransport;

// Uses the paired Wear OS transport (Bluetooth when available), without LAN/RFCOMM sockets.
internal sealed class WearChannelConnection : IAsyncDisposable
{
    internal const string Path = "/psiptv/remote/v1";
    internal const string PhoneCapability = "psiptv_remote_phone_v1";
    private readonly ChannelClient client;
    private readonly ChannelClient.IChannel channel;
    internal Stream Input { get; }
    internal Stream Output { get; }
    private readonly object sync = new();
    private Task? closing;

    private WearChannelConnection(ChannelClient client, ChannelClient.IChannel channel, Stream input, Stream output)
    { this.client = client; this.channel = channel; Input = input; Output = output; }

    internal static async Task<WearChannelConnection> OpenAsync(ChannelClient client, string nodeId, CancellationToken token)
    {
        var opening = client.OpenChannelAsync(nodeId, Path);
        ChannelClient.IChannel channel;
        try { channel = await opening.WaitAsync(token); }
        catch { _ = CloseLateAsync(); throw; }
        return await AcceptAsync(client, channel, token);
        async Task CloseLateAsync()
        { try { await CloseAsync(client, await opening); } catch { } }
    }

    internal static async Task<WearChannelConnection> AcceptAsync(ChannelClient client, ChannelClient.IChannel channel, CancellationToken token)
    {
        Stream? input = null;
        Stream? output = null;
        try
        {
            var nativeInput = await client.GetInputStreamAsync(channel).WaitAsync(token);
            input = new InputStreamInvoker(nativeInput);
            var nativeOutput = await client.GetOutputStreamAsync(channel).WaitAsync(token);
            output = new OutputStreamInvoker(nativeOutput);
            token.ThrowIfCancellationRequested();
            return new(client, channel, input, output);
        }
        catch
        {
            await CloseAsync(client, channel);
            input?.Dispose(); output?.Dispose();
            throw;
        }
    }

    internal static async Task CloseAsync(ChannelClient client, ChannelClient.IChannel channel)
    {
        try { await client.CloseAsync(channel).WaitAsync(TimeSpan.FromSeconds(3)); }
        catch { /* Disconnection may close the channel before local cleanup. */ }
    }

    internal async Task AbortAsync()
    {
        await CloseAsync(client, channel);
        await DisposeAsync();
    }

    public ValueTask DisposeAsync()
    { lock (sync) return new(closing ??= DisposeCoreAsync()); }

    private async Task DisposeCoreAsync()
    {
        // A graceful close must flush/close both streams before closing the
        // channel. Cancellation uses AbortAsync first to unblock native I/O.
        await Task.Run(() =>
        {
            try { Output.Dispose(); } catch (Exception ex) when (ex is not OutOfMemoryException) { }
            try { Input.Dispose(); } catch (Exception ex) when (ex is not OutOfMemoryException) { }
        });
        await CloseAsync(client, channel);
    }
}
