using PSiptv.Core;
using PSiptv.Views;

namespace PSiptv.Services;

// Keep the native player alive independently of the page that started it.
public static class AudioPlaybackService
{
    public static PlaybackView? Player { get; private set; }
    public static MediaItem? Item { get; private set; }
    public static event Action? Changed;

    static AudioPlaybackService()
    {
        AppServices.Locked += Stop;
        AppServices.PlaybackSuspended += Stop;
    }

    public static bool IsAudio(MediaItem item) => AudioPlaybackPolicy.PersistsAcrossNavigation(item);

    public static async Task ActivateAsync(PlaybackView player, MediaItem item)
    {
        if (Player is { } previous && previous != player) await StopAsync();
        Player = player;
        Item = item;
        player.RetainAcrossNavigation = true;
        Changed?.Invoke();
    }

    public static void Detach(PlaybackView player)
    {
        if (Player != player) return;
        Player = null;
        Item = null;
        player.RetainAcrossNavigation = false;
        Changed?.Invoke();
    }

    private static async void Stop() => await StopAsync();

    public static async Task StopAsync()
    {
        if (Player is not { } player) return;
        Detach(player);
        await player.StopForExternalPlaybackAsync();
    }
}
