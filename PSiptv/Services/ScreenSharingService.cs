using PSiptv.Core;

namespace PSiptv.Services;

/// <summary>Opens the device's screen mirroring controls.</summary>
public static class ScreenSharingService
{
#if ANDROID
    private static IReadOnlyList<MediaItem> castQueue = [];
    private static int castIndex;
    private static string castTitle = "";
#endif

    public static async Task ChooseAsync(Page page, MediaItem? item = null, Action? chromecastStarted = null,
        IReadOnlyList<MediaItem>? queue = null, double startPositionSeconds = 0)
    {
        var choice = await LanguageService.ActionSheetAsync(page, "Transmitir", "Cancelar", null,
            "Chromecast", "Transmitir ecrã (nativo)");
        if (choice == "Chromecast") await OpenChromecastAsync(page, item, chromecastStarted, queue, startPositionSeconds);
        else if (choice == "Transmitir ecrã (nativo)") await OpenAsync(page);
    }

    public static async Task OpenAsync(Page page)
    {
#if ANDROID
        var activity = Platform.CurrentActivity;
        if (activity is not null)
        {
            try
            {
                activity.StartActivity(new Android.Content.Intent(Android.Provider.Settings.ActionCastSettings));
                return;
            }
            catch (Android.Content.ActivityNotFoundException) { }
        }
        await LanguageService.AlertAsync(page, "Partilhar ecrã",
            "Abra as definições rápidas do dispositivo e escolha Transmitir, Smart View ou Partilha de ecrã.");
#elif WINDOWS
        if (!await Launcher.Default.TryOpenAsync(new Uri("ms-settings-connectabledevices:devicediscovery")))
            await LanguageService.AlertAsync(page, "Partilhar ecrã", "Prima Windows + K e escolha o ecrã sem fios.");
#else
        await LanguageService.AlertAsync(page, "Partilhar ecrã",
            "Abra a Central de controlo e escolha Duplicar ecrã para ligar a um dispositivo AirPlay.");
#endif
    }

    private static async Task OpenChromecastAsync(Page page, MediaItem? item, Action? chromecastStarted,
        IReadOnlyList<MediaItem>? queue, double startPositionSeconds)
    {
#if ANDROID
        var activity = Platform.CurrentActivity;
        if (activity is null) return;
        try
        {
            var context = Android.Gms.Cast.Framework.CastContext.GetSharedInstance(activity);
            if (context.SessionManager.CurrentCastSession is { IsConnected: true } session)
            {
                if (item is not null)
                {
                    await LoadOnChromecastAsync(session, item, queue, startPositionSeconds);
                    chromecastStarted?.Invoke();
                }
                await page.Navigation.PushAsync(new PSiptv.Views.ChromecastControllerPage());
                return;
            }

            var routeButton = new AndroidX.MediaRouter.App.MediaRouteButton(activity);
            Android.Gms.Cast.Framework.CastButtonFactory.SetUpMediaRouteButton(activity, routeButton);
            activity.AddContentView(routeButton, new Android.Views.ViewGroup.LayoutParams(1, 1));
            if (!await WaitForRouteAndOpenAsync(routeButton))
            {
                if (routeButton.Parent is Android.Views.ViewGroup parent) parent.RemoveView(routeButton);
                routeButton.Dispose();
                await LanguageService.AlertAsync(page, "Chromecast", "Não foi encontrado nenhum Chromecast. Confirme que está ligado e na mesma rede Wi-Fi.");
                return;
            }
            _ = WaitForChromecastAsync(context, routeButton, page, item, chromecastStarted, queue, startPositionSeconds);
        }
        catch (Exception ex)
        {
            await LanguageService.AlertAsync(page, "Chromecast",
                LanguageService.Text("Não foi possível iniciar o Chromecast. Confirme que o Google Play Services está atualizado e que ambos os dispositivos estão na mesma rede Wi-Fi.") + "\n\n" + ex.Message);
        }
#else
        await LanguageService.AlertAsync(page, "Chromecast", "A ligação direta ao Chromecast está disponível na versão Android.");
#endif
    }

#if ANDROID
    private static async Task<bool> WaitForRouteAndOpenAsync(AndroidX.MediaRouter.App.MediaRouteButton routeButton)
    {
        // Cast discovery starts when the SDK configures the native route button;
        // allow it a short window before opening the device chooser.
        for (var attempt = 0; attempt < 20; attempt++)
        {
            if (routeButton.Enabled && routeButton.PerformClick()) return true;
            await Task.Delay(500);
        }
        return false;
    }

    private static async Task WaitForChromecastAsync(Android.Gms.Cast.Framework.CastContext context,
        AndroidX.MediaRouter.App.MediaRouteButton routeButton, Page page, MediaItem? item, Action? chromecastStarted,
        IReadOnlyList<MediaItem>? queue, double startPositionSeconds)
    {
        try
        {
            for (var attempt = 0; attempt < 120; attempt++)
            {
                await Task.Delay(500);
                if (context.SessionManager.CurrentCastSession is not { IsConnected: true } session) continue;
                if (item is not null)
                {
                    await LoadOnChromecastAsync(session, item, queue, startPositionSeconds);
                    await MainThread.InvokeOnMainThreadAsync(() => chromecastStarted?.Invoke());
                }
                await MainThread.InvokeOnMainThreadAsync(() => page.Navigation.PushAsync(
                    new PSiptv.Views.ChromecastControllerPage()));
                return;
            }
        }
        finally
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                if (routeButton.Parent is Android.Views.ViewGroup parent) parent.RemoveView(routeButton);
                routeButton.Dispose();
            });
        }
    }

    private static async Task LoadOnChromecastAsync(Android.Gms.Cast.Framework.CastSession session, MediaItem item,
        IReadOnlyList<MediaItem>? queue, double startPositionSeconds)
    {
        var account = AppServices.ActiveAccount;
        if (account is null) return;
        var candidates = item.Kind != MediaKind.Channel && queue is { Count: > 1 }
            ? queue.Where(candidate => candidate.Kind != MediaKind.Channel).Take(100).ToArray()
            : [item];
        var selectedIndex = Array.FindIndex(candidates, candidate =>
            CatalogPreferences.ItemKey(candidate) == CatalogPreferences.ItemKey(item));
        if (selectedIndex < 0) { candidates = [item]; selectedIndex = 0; }
        var mediaItems = new List<Android.Gms.Cast.MediaQueueItem>();
        foreach (var candidate in candidates)
        {
            var media = await BuildMediaInfoAsync(account, candidate);
            mediaItems.Add(new Android.Gms.Cast.MediaQueueItem.Builder(media)
                .SetAutoplay(true).SetPreloadTime(10).Build());
        }
        castQueue = candidates;
        castIndex = selectedIndex;
        castTitle = item.Name;
        var startPosition = item.Kind == MediaKind.Channel ? 0 : (long)Math.Max(0, startPositionSeconds * 1000);
        if (mediaItems.Count > 1)
        {
            session.RemoteMediaClient?.QueueLoad(mediaItems.ToArray(), selectedIndex, 0, startPosition, null);
            return;
        }
        var single = mediaItems[0].Media;
        var request = new Android.Gms.Cast.MediaLoadRequestData.Builder()
            .SetMediaInfo(single)
            .SetCurrentTime(startPosition)
            .SetAutoplay(Java.Lang.Boolean.True)
            .Build();
        session.RemoteMediaClient?.Load(request);
    }

    private static async Task<Android.Gms.Cast.MediaInfo> BuildMediaInfoAsync(PlaylistAccount account, MediaItem item)
    {
        var source = StreamPreferences.ApplyFormat(account, item, AppOptions.StreamFormat);
        source = await AppServices.Client.ResolveStreamAsync(account, source, CancellationToken.None);
        WebAddress.Require(source.Url);
        var metadata = new Android.Gms.Cast.MediaMetadata(Android.Gms.Cast.MediaMetadata.MediaTypeGeneric);
        metadata.PutString(Android.Gms.Cast.MediaMetadata.KeyTitle, item.Name);
        return new Android.Gms.Cast.MediaInfo.Builder(source.Url)
            .SetContentType(ChromecastPolicy.ContentType(source))
            .SetStreamType(item.Kind == MediaKind.Channel
                ? Android.Gms.Cast.MediaInfo.StreamTypeLive
                : Android.Gms.Cast.MediaInfo.StreamTypeBuffered)
            .SetMetadata(metadata)
            .Build();
    }
#endif

    public static Task<ChromecastPlaybackState> GetChromecastStateAsync()
    {
#if ANDROID
        var session = Android.Gms.Cast.Framework.CastContext.GetSharedInstance(
            Platform.CurrentActivity ?? Android.App.Application.Context).SessionManager.CurrentCastSession;
        var client = session?.RemoteMediaClient;
        if (session is not { IsConnected: true } || client is null)
            return Task.FromResult(new ChromecastPlaybackState(true, false));
        var remoteTitle = client.MediaInfo?.Metadata?.GetString(Android.Gms.Cast.MediaMetadata.KeyTitle) ?? castTitle;
        return Task.FromResult(new ChromecastPlaybackState(true, true,
            session.CastDevice?.FriendlyName ?? "Chromecast", remoteTitle, client.IsPlaying,
            Math.Max(0, client.ApproximateStreamPosition), Math.Max(0, client.StreamDuration),
            ChromecastPolicy.ClampVolume(session.Volume), session.Mute, castQueue.Count > 1));
#else
        return Task.FromResult(new ChromecastPlaybackState(false, false));
#endif
    }

    public static Task ToggleChromecastPlaybackAsync()
    {
#if ANDROID
        var client = CurrentClient();
        if (client?.IsPlaying == true) client.Pause(); else client?.Play();
#endif
        return Task.CompletedTask;
    }

    public static async Task SeekChromecastAsync(long deltaMilliseconds)
    {
#if ANDROID
        var client = CurrentClient();
        if (client is not null)
        {
            var options = new Android.Gms.Cast.MediaSeekOptions.Builder()
                .SetPosition(ChromecastPolicy.SeekTarget(client.ApproximateStreamPosition, deltaMilliseconds, client.StreamDuration))
                .Build();
            client.Seek(options);
        }
#endif
        await Task.CompletedTask;
    }

    public static Task SetChromecastVolumeAsync(double volume)
    {
#if ANDROID
        var session = CurrentSession();
        if (session is not null)
        {
            session.Volume = ChromecastPolicy.ClampVolume(volume);
            if (session.Mute) session.Mute = false;
        }
#endif
        return Task.CompletedTask;
    }

    public static Task ToggleChromecastMuteAsync()
    {
#if ANDROID
        var session = CurrentSession();
        if (session is not null) session.Mute = !session.Mute;
#endif
        return Task.CompletedTask;
    }

    public static Task NextChromecastAsync()
    {
#if ANDROID
        CurrentClient()?.QueueNext(null);
        if (castQueue.Count > 0) { castIndex = Math.Min(castQueue.Count - 1, castIndex + 1); castTitle = castQueue[castIndex].Name; }
#endif
        return Task.CompletedTask;
    }

    public static Task PreviousChromecastAsync()
    {
#if ANDROID
        CurrentClient()?.QueuePrev(null);
        if (castQueue.Count > 0) { castIndex = Math.Max(0, castIndex - 1); castTitle = castQueue[castIndex].Name; }
#endif
        return Task.CompletedTask;
    }

    public static Task StopChromecastAsync()
    {
#if ANDROID
        CurrentClient()?.Stop();
#endif
        return Task.CompletedTask;
    }

    public static Task DisconnectChromecastAsync()
    {
#if ANDROID
        var activity = Platform.CurrentActivity;
        if (activity is not null)
            Android.Gms.Cast.Framework.CastContext.GetSharedInstance(activity).SessionManager.EndCurrentSession(true);
        castQueue = [];
        castIndex = 0;
        castTitle = "";
#endif
        return Task.CompletedTask;
    }

#if ANDROID
    private static Android.Gms.Cast.Framework.CastSession? CurrentSession()
    {
        var activity = Platform.CurrentActivity;
        return activity is null ? null : Android.Gms.Cast.Framework.CastContext.GetSharedInstance(activity)
            .SessionManager.CurrentCastSession;
    }

    private static Android.Gms.Cast.Framework.Media.RemoteMediaClient? CurrentClient() => CurrentSession()?.RemoteMediaClient;
#endif
}
