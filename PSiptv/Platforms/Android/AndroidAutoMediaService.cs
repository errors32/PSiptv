using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Media;
using Android.Media.Browse;
using Android.Media.Session;
using Android.OS;
using Android.Runtime;
using Android.Service.Media;
using PSiptv.Core;
using PSiptv.Services;

namespace PSiptv;

// Android's fluent Builder APIs are annotated as nullable by the generated
// bindings even though they return the same non-null builder instance.
#pragma warning disable CS8602, CS8603, CS8604

/// <summary>
/// Media entry point used by Android Auto. Android Auto renders the safe driving UI;
/// this service only supplies the favourite channels and playback session.
/// </summary>
[Service(Name = "com.PS.PSiptv.AndroidAutoMediaService", Exported = true,
    ForegroundServiceType = ForegroundService.TypeMediaPlayback, Label = "PSiptv",
    Icon = "@mipmap/appicon")]
[IntentFilter(["android.media.browse.MediaBrowserService"])]
public sealed class AndroidAutoMediaService : MediaBrowserService
{
    private const string RootId = "psiptv:root";
    private const string NotificationChannelId = "psiptv.android-auto.playback";
    private const int NotificationId = 0x5054;
    private readonly SemaphoreSlim catalogGate = new(1, 1);
    private CatalogSnapshot catalog = new(null, new Dictionary<string, PSiptv.Core.MediaItem>());
    private MediaSession? session;
    private MediaPlayer? player;
    private PSiptv.Core.MediaItem? current;
    private CancellationTokenSource? playbackCancellation;
    private int playbackGeneration;

    public override void OnCreate()
    {
        base.OnCreate();
        session = new MediaSession(this, "PSiptv.AndroidAuto");
        session.SetFlags(MediaSessionFlags.HandlesMediaButtons | MediaSessionFlags.HandlesTransportControls);
        session.SetCallback(new SessionCallback(this));
        session.SetPlaybackState(State(PlaybackStateCode.Stopped));
        session.Active = true;
        SessionToken = session.SessionToken;
        CreateNotificationChannel();
    }

    public override BrowserRoot OnGetRoot(string clientPackageName, int clientUid, Bundle? rootHints) =>
        new(RootId, null);

    public override void OnLoadChildren(string parentId, Result result)
    {
        result.Detach();
        _ = LoadChildrenAsync(parentId, result);
    }

    private async Task LoadChildrenAsync(string parentId, Result result)
    {
        try
        {
            var items = parentId == RootId
                ? await LoadFavoritesAsync().ConfigureAwait(false)
                : [];
            result.SendResult(new JavaList<MediaBrowser.MediaItem>(items));
        }
        catch
        {
            result.SendResult(new JavaList<MediaBrowser.MediaItem>());
        }
    }

    private async Task<List<MediaBrowser.MediaItem>> LoadFavoritesAsync()
    {
        await catalogGate.WaitAsync().ConfigureAwait(false);
        try
        {
            var activeAccount = AppServices.ActiveAccount;
            var accounts = await AppServices.Accounts.LoadAsync().ConfigureAwait(false);
            var lastId = Preferences.Default.Get("lastAccountId", "");
            var account = activeAccount ?? accounts.FirstOrDefault(item => item.Id == lastId) ?? accounts.FirstOrDefault();
            // A PIN-protected list must first be unlocked in the phone or parked-car UI.
            if (account is null || account.IsProtected && activeAccount?.Id != account.Id)
            {
                catalog = new(null, new Dictionary<string, PSiptv.Core.MediaItem>());
                return [];
            }

            await UserProfileService.LoadAsync().ConfigureAwait(false);
            var favorites = await FavoritesService.ExportAsync(account.Id, UserProfileService.Active.Id)
                .ConfigureAwait(false);
            var items = favorites.Select(entry => entry.Item)
                .Where(item => item.Kind == MediaKind.Channel &&
                    (!string.IsNullOrWhiteSpace(item.Url) || !string.IsNullOrWhiteSpace(item.SourceCommand)))
                .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();

            var result = new List<MediaBrowser.MediaItem>(items.Length);
            var channels = new Dictionary<string, PSiptv.Core.MediaItem>(items.Length);
            for (var index = 0; index < items.Length; index++)
            {
                var id = $"psiptv:favorite:{index}";
                channels[id] = items[index];
                result.Add(PlayableItem(id, items[index]));
            }
            catalog = new(account, channels);
            return result;
        }
        finally { catalogGate.Release(); }
    }

    private static MediaBrowser.MediaItem PlayableItem(string id, PSiptv.Core.MediaItem item)
    {
        using var builder = new MediaDescription.Builder().SetMediaId(id).SetTitle(item.Name).SetSubtitle(item.Category);
        using var description = builder.Build();
        return new MediaBrowser.MediaItem(description, MediaItemFlags.Playable);
    }

    private void Play(string mediaId)
    {
        var snapshot = catalog;
        if (!snapshot.Channels.TryGetValue(mediaId, out var item) || snapshot.Account is null) return;
        StopPlayer(false);
        var generation = ++playbackGeneration;
        playbackCancellation = new CancellationTokenSource();
        current = item;
        session?.SetMetadata(new MediaMetadata.Builder()
            .PutString(MediaMetadata.MetadataKeyTitle, item.Name)
            .PutString(MediaMetadata.MetadataKeyArtist, item.Category)
            .Build());
        session?.SetPlaybackState(State(PlaybackStateCode.Connecting));
        StartForeground(NotificationId, BuildNotification());
        _ = OpenStreamAsync(snapshot.Account, item, generation, playbackCancellation.Token);
    }

    private async Task OpenStreamAsync(PlaylistAccount selectedAccount, PSiptv.Core.MediaItem item,
        int generation, CancellationToken cancellationToken)
    {
        try
        {
            var source = StreamPreferences.ApplyFormat(selectedAccount, item, AppOptions.StreamFormat);
            source = await AppServices.Client.ResolveStreamAsync(selectedAccount, source, cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (generation != playbackGeneration) return;

            player = new MediaPlayer();
            player.SetAudioAttributes(new AudioAttributes.Builder()
                .SetContentType(AudioContentType.Music)
                .SetUsage(AudioUsageKind.Media)
                .Build());
            var headers = new Dictionary<string, string>();
            var userAgent = string.IsNullOrWhiteSpace(source.HttpUserAgent) ? AppOptions.UserAgent : source.HttpUserAgent;
            if (!string.IsNullOrWhiteSpace(userAgent)) headers["User-Agent"] = userAgent;
            if (!string.IsNullOrWhiteSpace(source.HttpReferer)) headers["Referer"] = source.HttpReferer;
            if (!string.IsNullOrWhiteSpace(source.HttpCookie)) headers["Cookie"] = source.HttpCookie;
            player.SetDataSource(this, Android.Net.Uri.Parse(source.Url), headers);
            player.Prepared += OnPrepared;
            player.Completion += OnCompletion;
            player.Error += OnError;
            player.PrepareAsync();
        }
        catch (System.OperationCanceledException) { }
        catch
        {
            if (generation == playbackGeneration)
            {
                session?.SetPlaybackState(State(PlaybackStateCode.Error));
                StopPlayer(false);
            }
        }
    }

    private void OnPrepared(object? sender, EventArgs e)
    {
        player?.Start();
        session?.SetPlaybackState(State(PlaybackStateCode.Playing));
        StartForeground(NotificationId, BuildNotification());
    }

    private void OnCompletion(object? sender, EventArgs e) => StopPlayer(true);

    private void OnError(object? sender, MediaPlayer.ErrorEventArgs e)
    {
        e.Handled = true;
        session?.SetPlaybackState(State(PlaybackStateCode.Error));
        StopPlayer(false);
    }

    private void Pause()
    {
        if (player?.IsPlaying != true) return;
        player.Pause();
        session?.SetPlaybackState(State(PlaybackStateCode.Paused));
    }

    private void Resume()
    {
        if (player is null) return;
        player.Start();
        session?.SetPlaybackState(State(PlaybackStateCode.Playing));
    }

    private void StopPlayer(bool updateState)
    {
        playbackCancellation?.Cancel();
        playbackCancellation?.Dispose();
        playbackCancellation = null;
        playbackGeneration++;
        if (player is not null)
        {
            player.Prepared -= OnPrepared;
            player.Completion -= OnCompletion;
            player.Error -= OnError;
            try { player.Stop(); } catch (Java.Lang.IllegalStateException) { }
            player.Release();
            player.Dispose();
            player = null;
        }
        if (updateState) session?.SetPlaybackState(State(PlaybackStateCode.Stopped));
        StopForeground(StopForegroundFlags.Remove);
    }

    private static PlaybackState State(PlaybackStateCode code) => new PlaybackState.Builder()
        .SetActions(PlaybackState.ActionPlay | PlaybackState.ActionPause |
                    PlaybackState.ActionStop | PlaybackState.ActionPlayFromMediaId)
        .SetState(code, 0, 1)
        .Build();

    private Notification BuildNotification()
    {
        var launch = PackageManager?.GetLaunchIntentForPackage(PackageName!);
        var pending = launch is null ? null : PendingIntent.GetActivity(this, 0, launch,
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
        var builder = new Notification.Builder(this, NotificationChannelId)
            .SetSmallIcon(Resource.Drawable.ic_auto_attribution)
            .SetContentTitle(current?.Name ?? "PSiptv")
            .SetContentText(current?.Category ?? "Android Auto")
            .SetOngoing(true)
            .SetOnlyAlertOnce(true)
            .SetContentIntent(pending)
            .SetCategory(Notification.CategoryTransport)
            .SetVisibility(NotificationVisibility.Public)
            .SetStyle(new Notification.MediaStyle().SetMediaSession(session?.SessionToken));
        return builder.Build();
    }

    private void CreateNotificationChannel()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26)) return;
        var manager = (NotificationManager?)GetSystemService(NotificationService);
        manager?.CreateNotificationChannel(new NotificationChannel(NotificationChannelId,
            "Reprodução no Android Auto", NotificationImportance.Low));
    }

    public override void OnDestroy()
    {
        StopPlayer(false);
        catalogGate.Dispose();
        session?.Release();
        session?.Dispose();
        session = null;
        base.OnDestroy();
    }

    private sealed class SessionCallback(AndroidAutoMediaService owner) : MediaSession.Callback
    {
        public override void OnPlayFromMediaId(string? mediaId, Bundle? extras)
        {
            if (!string.IsNullOrWhiteSpace(mediaId)) owner.Play(mediaId);
        }

        public override void OnPlay() => owner.Resume();
        public override void OnPause() => owner.Pause();
        public override void OnStop() => owner.StopPlayer(true);
    }

    private sealed record CatalogSnapshot(PlaylistAccount? Account,
        IReadOnlyDictionary<string, PSiptv.Core.MediaItem> Channels);
}

#pragma warning restore CS8602, CS8603, CS8604
