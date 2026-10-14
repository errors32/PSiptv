using PSiptv.Core;
using PSiptv.Views;

namespace PSiptv.Services;

// Keep the native player alive independently of the page that started it.
public static class AudioPlaybackService
{
    public static PlaybackView? Player { get; private set; }
    public static MediaItem? Item { get; private set; }
    public static event Action? Changed;
    private static bool opening;

    static AudioPlaybackService()
    {
        AppServices.Locked += Stop;
        AppServices.PlaybackSuspended += Stop;
    }

    public static bool IsAudio(MediaItem item) => AudioPlaybackPolicy.PersistsAcrossNavigation(item);

    public static async Task<bool> OpenCurrentAsync(Page owner, MediaItem? requested = null)
    {
        if (Player?.OwnerPage is not { } page || Item is not { } current ||
            requested is not null && (requested.Id != current.Id || requested.Kind != current.Kind)) return false;
        if (opening) return true;
        opening = true;
        try
        {
            var navigation = owner.Navigation;
            if (navigation.NavigationStack.Contains(page))
            {
                while (navigation.NavigationStack.LastOrDefault() != page && navigation.NavigationStack.Count > 1)
                    await navigation.PopAsync();
            }
            else await navigation.PushAsync(page);
            return true;
        }
        finally { opening = false; }
    }

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
