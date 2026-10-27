using Android.Gms.Wearable;
using PSiptv.Core;
using PSiptv.Services;
using PSiptv.WearTransport;

namespace PSiptv;

// Profile/podcast Bluetooth synchronization keeps its existing service and protocol.
internal static class WearPhoneRemote
{
    private static readonly SemaphoreSlim slots = new(4, 4);
    private static readonly ChannelClient client = WearableClass.GetChannelClient(Android.App.Application.Context);
    private static readonly Listener listener = new();
    private static CancellationTokenSource? lifetime;
    private static readonly SemaphoreSlim registration = new(1, 1);

    internal static void Resume() => _ = ChangeAsync(true);
    internal static void Pause() => _ = ChangeAsync(false);

    private static async Task ChangeAsync(bool enabled)
    {
        await registration.WaitAsync();
        try
        {
            if (enabled && lifetime is null)
            {
                lifetime = new();
                try { await client.RegisterChannelCallbackAsync(listener); }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                { lifetime.Dispose(); lifetime = null; Android.Util.Log.Warn("PSiptvWear", "Wear OS receiver unavailable: " + ex.Message); }
            }
            else if (!enabled && lifetime is { } previous)
            {
                lifetime = null;
                previous.Cancel();
                previous.Dispose();
                try { await client.UnregisterChannelCallbackAsync(listener); }
                catch (Exception ex) when (ex is not OutOfMemoryException) { }
            }
        }
        finally { registration.Release(); }
    }

    private sealed class Listener : ChannelClient.ChannelCallback
    {
        public override void OnChannelOpened(ChannelClient.IChannel channel)
        {
            if (channel.Path != WearChannelConnection.Path) return;
            var token = lifetime?.Token;
            _ = Task.Run(() => ReceiveAsync(channel, token));
        }
    }

    private static async Task ReceiveAsync(ChannelClient.IChannel channel, CancellationToken? session)
    {
        if (session is null || session.Value.IsCancellationRequested || !BluetoothRemoteService.Enabled)
        { await WearChannelConnection.CloseAsync(client, channel); return; }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(session.Value);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        var entered = false;
        try
        {
            entered = await slots.WaitAsync(0, timeout.Token);
            if (!entered) { await WearChannelConnection.CloseAsync(client, channel); return; }
            await using var connection = await WearChannelConnection.AcceptAsync(client, channel, timeout.Token);
            using var cancel = timeout.Token.Register(() => _ = Task.Run(connection.AbortAsync));
            // Bootstrap obtains the current profile; the next exchange carries the command.
            for (var exchange = 0; exchange < 2; exchange++)
            {
                var peer = await MainThread.InvokeOnMainThreadAsync(RemoteControlService.BluetoothDiscoveryResponse);
                await BluetoothRemoteProtocol.ServeAsync(connection.Input, connection.Output, () => peer,
                    (request, profile) => BluetoothRemoteService.Enabled
                        ? RemoteControlService.ExecuteRequestAsync(request, profile)
                        : Task.FromResult(new RemoteControlResponse(false, "Ative o controlo Bluetooth no telemóvel.", null)), timeout.Token);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        { Android.Util.Log.Debug("PSiptvWear", "Wear OS channel ended: " + ex.GetType().Name); }
        finally
        {
            await WearChannelConnection.CloseAsync(client, channel);
            if (entered) slots.Release();
        }
    }
}
